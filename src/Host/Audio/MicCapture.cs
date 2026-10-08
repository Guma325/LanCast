using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VideoStreaming.Audio;

/// <summary>Captura o microfone do host e entrega PCM 16-bit / 48 kHz / estéreo.</summary>
public sealed class MicCapture : IDisposable
{
    private readonly WasapiCapture _cap;
    private readonly BufferedWaveProvider _bwp;
    private readonly ISampleProvider _sp;
    private readonly PcmBuffer _buffer;
    private readonly float[] _f = new float[9600];
    private readonly short[] _s = new short[9600];

    public volatile float Level;
    public volatile float Gain = 1f;

    public MicCapture(MMDevice device, PcmBuffer buffer)
    {
        _buffer = buffer;
        _cap = new WasapiCapture(device, true, 20);
        _bwp = new BufferedWaveProvider(_cap.WaveFormat) { DiscardOnBufferOverflow = true, ReadFully = false };
        ISampleProvider sp = _bwp.ToSampleProvider();
        if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
        else if (sp.WaveFormat.Channels > 2) sp = new MultiplexingSampleProvider(new[] { sp }, 2);
        if (sp.WaveFormat.SampleRate != 48000) sp = new WdlResamplingSampleProvider(sp, 48000);
        _sp = sp;
        _cap.DataAvailable += OnData;
    }

    public void Start() => _cap.StartRecording();

    private void OnData(object? sender, WaveInEventArgs e)
    {
        _bwp.AddSamples(e.Buffer, 0, e.BytesRecorded);
        int n;
        float peak = 0, g = Gain;
        while ((n = _sp.Read(_f, 0, _f.Length)) > 0)
        {
            for (int i = 0; i < n; i++)
            {
                float v = _f[i] * g;
                peak = Math.Max(peak, Math.Abs(v));
                _s[i] = (short)(Math.Clamp(v, -1f, 1f) * 32767);
            }
            _buffer.Write(_s, n);
        }
        Level = peak;
    }

    public void Dispose()
    {
        try { _cap.DataAvailable -= OnData; _cap.StopRecording(); } catch { }
        _cap.Dispose();
    }
}
