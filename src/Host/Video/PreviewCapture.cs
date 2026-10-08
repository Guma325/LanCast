using System.Diagnostics;

namespace VideoStreaming.Video;

/// <summary>
/// Pré-visualização da fonte escolhida: um ffmpeg leve e independente do que vai para os espectadores
/// (8 quadros/s, 640 px, JPEG). Só roda enquanto a interface está mostrando a aba.
/// </summary>
public sealed class PreviewCapture : IDisposable
{
    private readonly StreamConfig _cfg;
    private readonly Func<CapKind?> _preferred;
    private CancellationTokenSource? _cts;
    private Process? _ffmpeg;
    private volatile bool _restart;

    /// <summary>Um JPEG completo por evento (thread de fundo).</summary>
    public event Action<byte[]>? Frame;
    /// <summary>Mensagem para mostrar no lugar da imagem quando não há quadros.</summary>
    public string? Status { get; private set; }
    public bool Running => _cts != null;

    public PreviewCapture(StreamConfig cfg, Func<CapKind?> preferred) { _cfg = cfg; _preferred = preferred; }

    public void Start()
    {
        if (Running) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Status = "carregando...";
        new Thread(() => Run(token)) { IsBackground = true, Name = "preview" }.Start();
    }

    public void Stop()
    {
        var c = _cts; if (c == null) return;
        _cts = null;
        c.Cancel();
        try { if (_ffmpeg is { HasExited: false }) _ffmpeg.Kill(true); } catch { }
    }

    /// <summary>Recomeça (troca de fonte).</summary>
    public void Restart()
    {
        _restart = true;
        Status = "carregando...";
        try { if (_ffmpeg is { HasExited: false }) _ffmpeg.Kill(true); } catch { }
    }

    private void Run(CancellationToken ct)
    {
        string ffmpegPath;
        try { ffmpegPath = Paths.EnsureFfmpeg(); }
        catch (Exception ex) { Status = "ffmpeg indisponível: " + ex.Message; return; }

        CapKind? good = null;
        while (!ct.IsCancellationRequested)
        {
            _restart = false;
            var kinds = new List<CapKind>();
            if (good is { } g) kinds.Add(g);
            if (_preferred() is { } p && !kinds.Contains(p)) kinds.Add(p);
            foreach (var k in CaptureArgs.Kinds(_cfg)) if (!kinds.Contains(k)) kinds.Add(k);

            bool anyStarted = false;
            foreach (var kind in kinds)
            {
                if (ct.IsCancellationRequested || _restart) break;
                var cap = CaptureArgs.Build(_cfg, kind, 8, true, out var err);
                if (cap == null)
                {
                    if (err != null) { Status = err; Wait(ct, 1200); anyStarted = true; break; }   // ex.: janela fechada
                    continue;                                                                      // método não se aplica
                }
                anyStarted = true;
                bool ok = RunOnce(ffmpegPath, cap, ct);
                if (ok) { good = kind; break; }
                if (_restart) { good = null; break; }
            }
            if (!anyStarted || (!_restart && good == null)) { Status ??= "pré-visualização indisponível"; Wait(ct, 3000); }
            else Wait(ct, 300);
        }
    }

    private bool RunOnce(string ffmpegPath, CaptureInput cap, CancellationToken ct)
    {
        string cfr = cap.NeedsCfr ? " -r 8 -fps_mode cfr" : "";
        string args = $"-hide_banner -loglevel error -fflags nobuffer {cap.InputArgs} -vf \"scale=640:-2:flags=fast_bilinear,format=yuvj420p\"{cfr} -c:v mjpeg -q:v 6 -f image2pipe pipe:1";
        try
        {
            _ffmpeg = Process.Start(new ProcessStartInfo(ffmpegPath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
        }
        catch (Exception ex) { Status = "preview: " + ex.Message; return false; }
        ProcessJob.Assign(_ffmpeg);
        var proc = _ffmpeg;
        string lastErr = "";
        proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) lastErr = e.Data; };
        proc.BeginErrorReadLine();

        var started = Stopwatch.StartNew();
        bool got = false;
        // watchdog: sem quadros em 6 s -> desiste deste método
        var watchdog = new Timer(_ => { if (!got) try { proc.Kill(true); } catch { } }, null, 6000, Timeout.Infinite);

        var stream = proc.StandardOutput.BaseStream;
        var tmp = new byte[64 * 1024];
        var buf = new byte[1024 * 1024];
        int len = 0;
        try
        {
            while (!ct.IsCancellationRequested && !_restart)
            {
                int n = stream.Read(tmp, 0, tmp.Length);
                if (n <= 0) break;
                if (len + n > buf.Length) len = 0;                  // proteção contra lixo
                Buffer.BlockCopy(tmp, 0, buf, len, n); len += n;

                while (true)
                {
                    int s = Find(buf, len, 0, 0xFF, 0xD8);
                    if (s < 0) { len = len > 0 && buf[len - 1] == 0xFF ? 1 : 0; if (len == 1) buf[0] = 0xFF; break; }
                    int e = Find(buf, len, s + 2, 0xFF, 0xD9);
                    if (e < 0)
                    {
                        if (s > 0) { Buffer.BlockCopy(buf, s, buf, 0, len - s); len -= s; }
                        break;
                    }
                    var jpg = new byte[e + 2 - s];
                    Buffer.BlockCopy(buf, s, jpg, 0, jpg.Length);
                    Buffer.BlockCopy(buf, e + 2, buf, 0, len - (e + 2)); len -= e + 2;
                    if (!got) { got = true; Status = null; }
                    Frame?.Invoke(jpg);
                }
            }
        }
        catch { }
        finally
        {
            watchdog.Dispose();
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
        }
        if (!got) Status = string.IsNullOrEmpty(lastErr) ? "sem imagem" : lastErr;
        return got;
    }

    private static int Find(byte[] b, int len, int from, byte a, byte c)
    {
        for (int i = Math.Max(0, from); i < len - 1; i++)
            if (b[i] == a && b[i + 1] == c) return i;
        return -1;
    }

    private static void Wait(CancellationToken ct, int ms) { try { ct.WaitHandle.WaitOne(ms); } catch { } }

    public void Dispose() => Stop();
}
