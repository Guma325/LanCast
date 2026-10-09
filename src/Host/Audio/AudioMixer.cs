using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Concentus.Enums;
using Concentus.Structs;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace LanCast.Audio;

public sealed record AppAudioInfo(string Name, string Title, bool Muted, bool Playing, int Pids);

/// <summary>
/// Descobre os aplicativos com sessão de áudio, captura cada um separadamente (Process Loopback),
/// mixa os que NÃO estão silenciados e codifica em Opus (frames de 10 ms).
/// </summary>
public sealed class AudioMixer : IDisposable
{
    public const int FrameSamples = 480;                       // 10 ms @ 48 kHz
    private const int FrameValues = FrameSamples * ProcessLoopbackCapture.Channels;

    private sealed class Source
    {
        public string Name = "";
        public int Pid;
        public PcmBuffer Buffer = null!;
        public ProcessLoopbackCapture Capture = null!;
    }

    private readonly object _lock = new();
    private readonly Dictionary<int, Source> _sources = new();                 // por PID
    private readonly Dictionary<string, (string Title, bool Playing, int Pids)> _known = new();
    private readonly HashSet<string> _muted = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _mutesFile;
    private readonly OpusEncoder _opus;
    private readonly CancellationTokenSource _cts = new();
    private readonly int _maxBacklogValues;
    private string? _onlyApp;
    private PcmBuffer _micBuf = new();
    private MicCapture? _mic;
    public string? MicError { get; private set; }
    public float MicLevel => _mic?.Level ?? 0f;

    /// <summary>Disparado a cada 10 ms com um pacote Opus.</summary>
    public event Action<byte[]>? OnOpusFrame;

    public AudioMixer(int bitrateKbps, IEnumerable<string> defaultMuted)
    {
        _mutesFile = Paths.MutesFile;
        LoadMutes(defaultMuted);

        _opus = new OpusEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY)
        {
            Bitrate = bitrateKbps * 1000,
            Complexity = 5,
            UseVBR = false,
        };
        _maxBacklogValues = FrameValues * 4;                    // ~40 ms de folga máxima por fonte
    }

    public void Start()
    {
        new Thread(WatchSessions) { IsBackground = true, Name = "audio-watch" }.Start();
        new Thread(MixLoop) { IsBackground = true, Name = "audio-mix", Priority = ThreadPriority.Highest }.Start();
    }

    // ---------- API usada pela interface ----------

    public IReadOnlyList<AppAudioInfo> GetApps()
    {
        lock (_lock)
        {
            var names = new HashSet<string>(_known.Keys, StringComparer.OrdinalIgnoreCase);
            names.UnionWith(_muted);
            return names
                .Select(n =>
                {
                    _known.TryGetValue(n, out var k);
                    return new AppAudioInfo(n, k.Title ?? "", _muted.Contains(n), k.Playing, k.Pids);
                })
                .OrderByDescending(a => a.Playing).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private bool Allowed(string name) => _onlyApp == null || name.Equals(_onlyApp, StringComparison.OrdinalIgnoreCase);

    /// <summary>Quando definido, só o áudio desse aplicativo (nome do processo) vai para a transmissão.</summary>
    public void SetOnlyApp(string? name)
    {
        lock (_lock)
        {
            _onlyApp = string.IsNullOrWhiteSpace(name) ? null : name;
            foreach (var s in _sources.Values.Where(s => !Allowed(s.Name)).ToList()) StopSource(s);
        }
    }

    public string? OnlyApp { get { lock (_lock) return _onlyApp; } }

    public void SetMuted(string name, bool muted)
    {
        lock (_lock)
        {
            if (muted) _muted.Add(name); else _muted.Remove(name);
            // aplica na hora: para a captura dos apps silenciados
            foreach (var s in _sources.Values.Where(s => _muted.Contains(s.Name)).ToList())
                StopSource(s);
        }
        SaveMutes();
    }

    // ---------- descoberta de sessões ----------

    public static HashSet<string> ReadMuted(IEnumerable<string> defaults)
    {
        try { if (File.Exists(Paths.MutesFile)) return new HashSet<string>(JsonSerializer.Deserialize<string[]>(File.ReadAllText(Paths.MutesFile)) ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase); } catch { }
        return new HashSet<string>(defaults, StringComparer.OrdinalIgnoreCase);
    }
    public static void SaveMutedPreference(string name, bool muted, IEnumerable<string> defaults)
    {
        var names = ReadMuted(defaults);
        if (muted) names.Add(name); else names.Remove(name);
        File.WriteAllText(Paths.MutesFile, JsonSerializer.Serialize(names));
    }
    /// <summary>Enumera sessões sem iniciar captura nem transmitir áudio.</summary>
    public static IReadOnlyList<AppAudioInfo> DiscoverApps(IEnumerable<string> defaults)
    {
        var muted = ReadMuted(defaults);
        var apps = new Dictionary<string, AppAudioInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioSessionManager.RefreshSessions();
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                int pid = (int)session.GetProcessID;
                if (pid == 0 || pid == Environment.ProcessId || session.IsSystemSoundsSession) continue;
                try
                {
                    using var process = Process.GetProcessById(pid);
                    string name = process.ProcessName;
                    bool active = session.State == AudioSessionState.AudioSessionStateActive && session.AudioMeterInformation.MasterPeakValue > 0.001f;
                    apps.TryGetValue(name, out var previous);
                    apps[name] = new(name, string.IsNullOrEmpty(process.MainWindowTitle) ? previous?.Title ?? "" : process.MainWindowTitle, muted.Contains(name), active || previous?.Playing == true, (previous?.Pids ?? 0) + 1);
                }
                catch { }
            }
        }
        catch { }
        foreach (var name in muted) if (!apps.ContainsKey(name)) apps[name] = new(name, "Sem atividade", true, false, 0);
        return apps.Values.OrderByDescending(a => a.Playing).ThenBy(a => a.Name).ToList();
    }

    private void WatchSessions()
    {
        using var enumerator = new MMDeviceEnumerator();
        while (!_cts.IsCancellationRequested)
        {
            try { Refresh(enumerator); }
            catch (Exception ex) { AppLog.Info($"[audio] varredura de sessões: {ex.Message}"); }
            _cts.Token.WaitHandle.WaitOne(500);
        }
    }

    private void Refresh(MMDeviceEnumerator enumerator)
    {
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var mgr = device.AudioSessionManager;
        mgr.RefreshSessions();
        var sessions = mgr.Sessions;

        var seen = new Dictionary<int, string>();               // pid -> nome
        var info = new Dictionary<string, (string Title, bool Playing, int Pids)>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            int pid = (int)s.GetProcessID;
            if (pid == 0 || pid == Environment.ProcessId || s.IsSystemSoundsSession) continue;
            string name, title = "";
            try
            {
                using var p = Process.GetProcessById(pid);
                name = p.ProcessName;
                title = p.MainWindowTitle;
            }
            catch { continue; }

            bool playing = s.State == AudioSessionState.AudioSessionStateActive && s.AudioMeterInformation.MasterPeakValue > 0.001f;
            seen[pid] = name;
            info.TryGetValue(name, out var cur);
            info[name] = (string.IsNullOrEmpty(cur.Title) ? title : cur.Title, cur.Playing || playing, cur.Pids + 1);
        }

        lock (_lock)
        {
            _known.Clear();
            foreach (var kv in info) _known[kv.Key] = kv.Value;

            // remove capturas de processos que sumiram ou foram silenciados
            foreach (var s in _sources.Values.ToList())
                if (!seen.ContainsKey(s.Pid) || s.Capture.Failed || _muted.Contains(s.Name) || !Allowed(s.Name)) StopSource(s);

            // inicia capturas novas
            foreach (var (pid, name) in seen)
            {
                if (_sources.ContainsKey(pid) || _muted.Contains(name) || !Allowed(name)) continue;
                var buf = new PcmBuffer();
                var cap = new ProcessLoopbackCapture(pid, buf);
                _sources[pid] = new Source { Name = name, Pid = pid, Buffer = buf, Capture = cap };
                cap.Start();
                AppLog.Info($"[audio] capturando {name} (PID {pid})");
            }
        }
    }

    private void StopSource(Source s)
    {
        _sources.Remove(s.Pid);
        s.Capture.Dispose();
        AppLog.Info($"[audio] parou {s.Name} (PID {s.Pid})");
    }

    // ---------- mixagem ----------

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);

    private void MixLoop()
    {
        timeBeginPeriod(1);
        var acc = new int[FrameValues];
        var pcm = new short[FrameValues];
        var packet = new byte[1275];
        var sw = Stopwatch.StartNew();
        long frame = 0;
        const double frameMs = 10.0;

        while (!_cts.IsCancellationRequested)
        {
            double due = (frame + 1) * frameMs;
            double wait = due - sw.Elapsed.TotalMilliseconds;
            if (wait > 1.5) Thread.Sleep((int)(wait - 1));
            while (sw.Elapsed.TotalMilliseconds < due) Thread.SpinWait(50);
            frame++;
            // se atrasou muito (suspensão etc.), ressincroniza em vez de disparar rajada
            if (sw.Elapsed.TotalMilliseconds - due > 100) { frame = (long)(sw.Elapsed.TotalMilliseconds / frameMs); }

            Array.Clear(acc);
            lock (_lock)
            {
                foreach (var s in _sources.Values)
                    s.Buffer.MixInto(acc, FrameValues, _maxBacklogValues);
                if (_mic != null) _micBuf.MixInto(acc, FrameValues, _maxBacklogValues);
            }
            for (int i = 0; i < FrameValues; i++)
                pcm[i] = (short)Math.Clamp(acc[i], short.MinValue, short.MaxValue);

            try
            {
                int len = _opus.Encode(pcm, 0, FrameSamples, packet, 0, packet.Length);
                OnOpusFrame?.Invoke(packet.AsSpan(0, len).ToArray());
            }
            catch (Exception ex) { AppLog.Info($"[audio] opus: {ex.Message}"); }
        }
    }

    // ---------- persistência dos mutes ----------

    private void LoadMutes(IEnumerable<string> defaults)
    {
        try
        {
            if (File.Exists(_mutesFile))
            {
                foreach (var n in JsonSerializer.Deserialize<string[]>(File.ReadAllText(_mutesFile)) ?? Array.Empty<string>())
                    _muted.Add(n);
                return;
            }
        }
        catch { }
        foreach (var n in defaults) _muted.Add(n);
    }

    private void SaveMutes()
    {
        try
        {
            string[] arr; lock (_lock) arr = _muted.ToArray();
            File.WriteAllText(_mutesFile, JsonSerializer.Serialize(arr));
        }
        catch (Exception ex) { AppLog.Info($"[audio] salvar mutes: {ex.Message}"); }
    }

    // ---------- microfone do host ----------

    public static IReadOnlyList<(string Id, string Name)> ListMics()
    {
        using var en = new MMDeviceEnumerator();
        return en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                 .Select(d => (d.ID, d.FriendlyName)).ToList();
    }

    /// <summary>Liga/desliga o microfone e ajusta o ganho. Retorna false (e preenche MicError) se falhar.</summary>
    public bool SetMic(bool enabled, string? deviceId, double gain)
    {
        lock (_lock)
        {
            if (_mic != null && enabled && _mic.Gain != (float)gain) _mic.Gain = (float)gain;
            _mic?.Dispose(); _mic = null;
            MicError = null;
            if (!enabled) return true;
            try
            {
                using var en = new MMDeviceEnumerator();
                var dev = string.IsNullOrEmpty(deviceId)
                    ? en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                    : en.GetDevice(deviceId);
                _micBuf = new PcmBuffer();
                _mic = new MicCapture(dev, _micBuf) { Gain = (float)gain };
                _mic.Start();
                return true;
            }
            catch (Exception ex)
            {
                _mic?.Dispose(); _mic = null;
                MicError = ex.Message;
                return false;
            }
        }
    }

    public void SetMicGain(double gain) { if (_mic != null) _mic.Gain = (float)gain; }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_lock) { _mic?.Dispose(); _mic = null; }
        lock (_lock) foreach (var s in _sources.Values.ToList()) StopSource(s);
    }
}
