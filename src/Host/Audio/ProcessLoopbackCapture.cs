using System.Runtime.InteropServices;

namespace LanCast.Audio;

/// <summary>
/// Captura o áudio de UM processo (e seus filhos) usando a Process Loopback API do Windows 10 2004+ / Windows 11.
/// Entrega PCM 16-bit, 48 kHz, estéreo.
/// </summary>
public sealed class ProcessLoopbackCapture : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;

    private readonly uint _pid;
    private readonly PcmBuffer _buffer;
    private Thread? _thread;
    private volatile bool _stop;
    private IAudioClient? _client;

    public int ProcessId => (int)_pid;
    public bool Failed { get; private set; }

    public ProcessLoopbackCapture(int pid, PcmBuffer buffer)
    {
        _pid = (uint)pid;
        _buffer = buffer;
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = $"loopback-{_pid}", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void Run()
    {
        var evt = new AutoResetEvent(false);
        try
        {
            _client = Activate(_pid);

            var fmt = Marshal.AllocHGlobal(18);
            try
            {
                Marshal.WriteInt16(fmt, 0, 1);                       // WAVE_FORMAT_PCM
                Marshal.WriteInt16(fmt, 2, Channels);
                Marshal.WriteInt32(fmt, 4, SampleRate);
                Marshal.WriteInt32(fmt, 8, SampleRate * Channels * 2);
                Marshal.WriteInt16(fmt, 12, Channels * 2);
                Marshal.WriteInt16(fmt, 14, 16);
                Marshal.WriteInt16(fmt, 16, 0);
                const uint LOOPBACK = 0x00020000, EVENTCALLBACK = 0x00040000, AUTOCONVERT = 0x80000000, SRC_QUALITY = 0x08000000;
                Check(_client.Initialize(0, LOOPBACK | EVENTCALLBACK | AUTOCONVERT | SRC_QUALITY, 0, 0, fmt, IntPtr.Zero), "Initialize");
            }
            finally { Marshal.FreeHGlobal(fmt); }

            Check(_client.SetEventHandle(evt.SafeWaitHandle.DangerousGetHandle()), "SetEventHandle");
            var iid = typeof(IAudioCaptureClient).GUID;
            Check(_client.GetService(ref iid, out var obj), "GetService");
            var capture = (IAudioCaptureClient)obj;
            Check(_client.Start(), "Start");

            var tmp = new short[4096];
            while (!_stop)
            {
                if (!evt.WaitOne(100)) continue;
                while (!_stop && capture.GetNextPacketSize(out var n) == 0 && n > 0)
                {
                    if (capture.GetBuffer(out var data, out var frames, out var flags, out _, out _) != 0) break;
                    int samples = (int)frames * Channels;
                    if (tmp.Length < samples) tmp = new short[samples];
                    if ((flags & 2) != 0) Array.Clear(tmp, 0, samples);          // AUDCLNT_BUFFERFLAGS_SILENT
                    else Marshal.Copy(data, tmp, 0, samples);
                    capture.ReleaseBuffer(frames);
                    _buffer.Write(tmp, samples);
                }
            }
            _client.Stop();
        }
        catch (Exception ex)
        {
            Failed = true;
            AppLog.Info($"[audio] captura do PID {_pid} falhou: {ex.Message}");
        }
        finally
        {
            if (_client != null) { try { Marshal.ReleaseComObject(_client); } catch { } _client = null; }
            evt.Dispose();
        }
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(500);
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0) throw new COMException($"{what} falhou", hr);
    }

    // ---------- ativação ----------

    private static IAudioClient Activate(uint pid)
    {
        // AUDIOCLIENT_ACTIVATION_PARAMS { type=PROCESS_LOOPBACK(1); targetPid; mode=INCLUDE_TARGET_PROCESS_TREE(0) }
        var prm = Marshal.AllocHGlobal(12);
        Marshal.WriteInt32(prm, 0, 1);
        Marshal.WriteInt32(prm, 4, (int)pid);
        Marshal.WriteInt32(prm, 8, 0);

        // PROPVARIANT VT_BLOB (x64: 24 bytes)
        var pv = Marshal.AllocHGlobal(24);
        for (int i = 0; i < 24; i += 8) Marshal.WriteInt64(pv, i, 0);
        Marshal.WriteInt16(pv, 0, 0x41);
        Marshal.WriteInt32(pv, 8, 12);
        Marshal.WriteIntPtr(pv, 16, prm);

        try
        {
            var handler = new Handler();
            var iid = typeof(IAudioClient).GUID;
            ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, pv, handler, out var op);
            if (!handler.Done.Wait(5000)) throw new TimeoutException("ActivateAudioInterfaceAsync expirou");
            op.GetActivateResult(out var hr, out var obj);
            Check(hr, "GetActivateResult");
            return (IAudioClient)obj;
        }
        finally
        {
            Marshal.FreeHGlobal(pv);
            Marshal.FreeHGlobal(prm);
        }
    }

    private sealed class Handler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEventSlim Done = new(false);
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op) => Done.Set();
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject { }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity, IntPtr format, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint numBufferFrames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint numPaddingFrames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr deviceFormat);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint numFramesToRead, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint numFramesRead);
        [PreserveSig] int GetNextPacketSize(out uint numFramesInNextPacket);
    }
}
