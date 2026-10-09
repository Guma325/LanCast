using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace LanCast.Video;

public sealed record RtpVideoPacket(byte[] Payload, uint Timestamp, bool Marker, bool StartsKeyframe);

/// <summary>
/// Captura a fonte escolhida (tela ou janela) com ffmpeg, codifica em H.264 e entrega os pacotes RTP prontos para serem
/// repassados aos espectadores sem recodificar. Tenta, em ordem, vários métodos (NVENC, AMF, Quick Sync, software;
/// captura DXGI, Windows Graphics Capture e GDI) e fica no primeiro que realmente produzir vídeo.
/// </summary>
public sealed class ScreenEncoder : IDisposable
{
    private sealed record Attempt(string Label, string Encoder, CapKind Kind, bool Cpu);

    private enum Result { Worked, Failed, Missing }

    private readonly StreamConfig _cfg;
    private readonly CancellationTokenSource _cts = new();
    private Process? _ffmpeg;
    private int _good;                                      // índice da última tentativa que funcionou
    private volatile bool _restart, _resetLadder;

    // continuidade do timestamp RTP entre reinícios do ffmpeg (troca de fonte), para os navegadores não se perderem
    private uint _lastTs, _tsOffset;
    private bool _newRun = true;

    public event Action<RtpVideoPacket>? OnPacket;

    /// <summary>Descrição do método em uso, ex.: "NVIDIA NVENC · captura DXGI (GPU)".</summary>
    public string Active { get; private set; } = "iniciando...";
    /// <summary>Método de captura que está funcionando (usado pela pré-visualização).</summary>
    public CapKind? ActiveKind { get; private set; }
    /// <summary>true quando pacotes de vídeo estão realmente chegando.</summary>
    public bool Working { get; private set; }
    /// <summary>Último erro relevante (vazio quando está tudo bem).</summary>
    public string? LastError { get; private set; }

    public ScreenEncoder(StreamConfig cfg) => _cfg = cfg;

    public void Start() => new Thread(Run) { IsBackground = true, Name = "video-encoder" }.Start();

    /// <summary>Reinicia a captura (troca de fonte). resetLadder: volta a testar os métodos desde o início.</summary>
    public void Restart(bool resetLadder)
    {
        _resetLadder = resetLadder;
        _restart = true;
        try { if (_ffmpeg is { HasExited: false }) _ffmpeg.Kill(true); } catch { }
    }

    private static string EncName(string e) => e switch
    {
        "nvenc" => "NVIDIA NVENC",
        "amf" => "AMD AMF",
        "qsv" => "Intel Quick Sync",
        _ => "Software (x264)",
    };

    private List<Attempt> Ladder()
    {
        var pref = (_cfg.Encoder ?? "auto").ToLowerInvariant();
        var encs = pref == "auto" ? new[] { "nvenc", "amf", "qsv", "x264" }
                 : pref == "x264" ? new[] { "x264" } : new[] { pref, "x264" };
        var l = new List<Attempt>();
        foreach (var kind in CaptureArgs.Kinds(_cfg))
            foreach (var enc in encs)
            {
                string name = $"{EncName(enc)} · {CaptureArgs.KindName(kind)}";
                if (kind != CapKind.Gdi && enc is "nvenc" or "amf") l.Add(new($"{name} (GPU)", enc, kind, false));
                l.Add(new(name, enc, kind, true));
            }
        return l;
    }

    private void Run()
    {
        string ffmpegPath;
        try { ffmpegPath = Paths.EnsureFfmpeg(); }
        catch (Exception ex)
        {
            LastError = "ffmpeg não pôde ser extraído/iniciado: " + ex.Message + " (antivírus?)";
            AppLog.Info("[video] " + LastError);
            return;
        }
        AppLog.Info($"[video] ffmpeg: {ffmpegPath}");
        AppLog.Info("[video] GPUs: " + string.Join(" | ", SystemInfo.Gpus()));

        var ladder = Ladder();
        int idx = Math.Min(_good, ladder.Count - 1);
        while (!_cts.IsCancellationRequested)
        {
            var at = ladder[idx];
            Active = at.Label;
            ActiveKind = at.Kind;
            var res = RunOnce(ffmpegPath, at);
            if (_cts.IsCancellationRequested) break;
            Working = false;

            if (_restart)                                          // troca de fonte pedida pela interface
            {
                _restart = false;
                if (_resetLadder) { ladder = Ladder(); idx = 0; _good = 0; }
                continue;
            }
            if (res == Result.Missing)                             // ex.: janela fechada/minimizada: espera sem trocar de método
            {
                AppLog.Info($"[video] aguardando fonte: {LastError}");
                Thread.Sleep(1500);
                continue;
            }
            if (res == Result.Worked)
            {
                _good = idx;                                       // caiu depois de funcionar: tenta o mesmo de novo
                Thread.Sleep(1000);
                continue;
            }
            AppLog.Info($"[video] '{at.Label}' não funcionou: {LastError}");
            idx++;
            if (idx >= ladder.Count)
            {
                idx = 0;
                AppLog.Info("[video] nenhum método de captura funcionou; tentando de novo em 5 s");
                Thread.Sleep(5000);
            }
        }
    }

    private Result RunOnce(string ffmpegPath, Attempt at)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        udp.Client.ReceiveBufferSize = 4 * 1024 * 1024;
        int port = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;

        var args = BuildArgs(at, port, out var missing);
        if (args == null)
        {
            LastError = missing;
            return missing != null && (missing.Contains("encontrada") || missing.Contains("minimizada")) ? Result.Missing : Result.Failed;
        }
        AppLog.Info($"[video] tentando '{at.Label}': ffmpeg {args}");
        var tail = new Queue<string>();
        try
        {
            _ffmpeg = Process.Start(new ProcessStartInfo(ffmpegPath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            })!;
        }
        catch (Exception ex)
        {
            LastError = "não foi possível iniciar o ffmpeg: " + ex.Message;
            return Result.Failed;
        }
        _ffmpeg.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            AppLog.Info("[ffmpeg] " + e.Data);
            lock (tail) { tail.Enqueue(e.Data); while (tail.Count > 4) tail.Dequeue(); }
        };
        ProcessJob.Assign(_ffmpeg);
        _ffmpeg.BeginErrorReadLine();
        _newRun = true;

        var ep = new IPEndPoint(IPAddress.Any, 0);
        udp.Client.ReceiveTimeout = 500;
        var started = Stopwatch.StartNew();
        bool got = false;
        while (!_cts.IsCancellationRequested && !_ffmpeg.HasExited)
        {
            byte[] data;
            try { data = udp.Receive(ref ep); }
            catch (SocketException)
            {
                if (!got && started.Elapsed > TimeSpan.FromSeconds(8)) { LastError = "nenhum quadro de vídeo em 8 s"; break; }
                continue;
            }
            if (Parse(data) && !got)
            {
                got = true; Working = true; LastError = null;
                AppLog.Info($"[video] funcionando com '{at.Label}'");
            }
        }

        try { if (!_ffmpeg.HasExited) _ffmpeg.Kill(true); } catch { }
        if (!got)
        {
            string last; lock (tail) last = tail.LastOrDefault() ?? "";
            LastError = string.IsNullOrEmpty(last) ? (LastError ?? "o ffmpeg encerrou sem gerar vídeo") : last;
        }
        return got ? Result.Worked : Result.Failed;
    }

    private string? BuildArgs(Attempt at, int port, out string? error)
    {
        bool soft = at.Encoder == "x264";
        int fps = soft ? Math.Min(_cfg.Fps, 30) : _cfg.Fps;          // software: limita para não travar a CPU
        int height = _cfg.Height > 0 ? _cfg.Height : (soft ? 720 : 0);
        bool cpu = at.Cpu || height > 0;                            // escalar exige quadros em memória
        int br = _cfg.VideoBitrateKbps;
        int gop = Math.Max(1, (int)(fps * _cfg.KeyframeSeconds));
        int buf = Math.Max(200, br / fps * 3);

        var cap = CaptureArgs.Build(_cfg, at.Kind, fps, cpu, out error);
        if (cap == null) return null;

        var a = new List<string> { "-hide_banner -loglevel warning -fflags nobuffer", cap.InputArgs };
        if (cpu)
        {
            string scale = height > 0 ? $"scale=-2:{height}:flags=fast_bilinear," : "";
            a.Add($"-vf \"{scale}format=yuv420p\"");
        }
        // a captura de janela só gera quadros quando o conteúdo muda; repete o último para manter taxa constante
        if (cap.NeedsCfr) a.Add($"-r {fps} -fps_mode cfr");

        a.Add(at.Encoder switch
        {
            "nvenc" => $"-c:v h264_nvenc -preset p1 -tune ull -zerolatency 1 -rc cbr -b:v {br}k -maxrate {br}k -bufsize {buf}k -forced-idr 1 -profile:v high",
            "amf" => $"-c:v h264_amf -usage ultralowlatency -quality speed -rc cbr -b:v {br}k -maxrate {br}k -bufsize {buf}k",
            "qsv" => $"-c:v h264_qsv -preset veryfast -async_depth 1 -b:v {br}k -maxrate {br}k",
            _ => $"-c:v libx264 -preset ultrafast -tune zerolatency -b:v {br}k -maxrate {br}k -bufsize {buf}k -x264-params repeat-headers=1:scenecut=0",
        });
        a.Add($"-g {gop} -bf 0 -flags +low_delay -bsf:v dump_extra=freq=keyframe");     // SPS/PPS em todo keyframe: o navegador só começa a exibir com eles
        a.Add($"-f rtp -payload_type 96 -pkt_size 1200 rtp://127.0.0.1:{port}?localaddr=127.0.0.1");
        return string.Join(' ', a);
    }

    private bool Parse(byte[] d)
    {
        if (d.Length < 14 || (d[0] >> 6) != 2) return false;
        int cc = d[0] & 0x0F;
        int hdr = 12 + cc * 4;
        if ((d[0] & 0x10) != 0)                                // extensão
        {
            if (d.Length < hdr + 4) return false;
            hdr += 4 + ((d[hdr + 2] << 8) | d[hdr + 3]) * 4;
        }
        if (hdr >= d.Length) return false;
        int pt = d[1] & 0x7F;
        if (pt != 96) return false;                            // ignora RTCP e outros
        bool marker = (d[1] & 0x80) != 0;
        uint raw = (uint)(d[4] << 24 | d[5] << 16 | d[6] << 8 | d[7]);
        if (_newRun) { _newRun = false; _tsOffset = unchecked(_lastTs + 3000 - raw); }
        uint ts = unchecked(raw + _tsOffset);
        _lastTs = ts;
        var payload = d.AsSpan(hdr).ToArray();

        int nal = payload[0] & 0x1F;
        bool key = nal == 7 || nal == 24 || nal == 5 ||
                   (nal == 28 && payload.Length > 1 && (payload[1] & 0x80) != 0 && ((payload[1] & 0x1F) is 5 or 7));
        OnPacket?.Invoke(new RtpVideoPacket(payload, ts, marker, key));
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { if (_ffmpeg is { HasExited: false }) _ffmpeg.Kill(true); } catch { }
    }
}
