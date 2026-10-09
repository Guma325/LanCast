using System.Reflection;
using LanCast.Audio;
using LanCast.Video;

namespace LanCast;

public static class NetUtil
{
    public static string Ip(System.Net.IPAddress? a) =>
        a == null ? "?" : a.IsIPv4MappedToIPv6 ? a.MapToIPv4().ToString() : a.ToString();
}

public sealed record OfferRequest(string Sdp, string? Password, string? Name);

/// <summary>Sobe/derruba tudo: servidor web (viewer + sinalização), captura de vídeo e mixer de áudio.</summary>
public sealed class StreamService
{
    private WebApplication? _app;
    private ScreenEncoder? _encoder;

    public AudioMixer? Mixer { get; private set; }
    public StreamHub? Hub { get; private set; }
    public ScreenEncoder? Encoder => _encoder;

    /// <summary>Aplica a fonte escolhida (tela/janela) à transmissão em andamento.</summary>
    public void ApplySource(StreamConfig cfg, bool restartVideo = true)
    {
        // janela de apps UWP roda dentro do ApplicationFrameHost: o áudio vem de outro processo, então não filtra
        bool only = cfg.SourceType == "window" && cfg.OnlyAppAudio && !string.IsNullOrEmpty(cfg.WindowExe)
                    && !cfg.WindowExe.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);
        Mixer?.SetOnlyApp(only ? cfg.WindowExe : null);
        if (restartVideo) _encoder?.Restart(resetLadder: true);
    }
    public bool Running => _app != null;

    public async Task StartAsync(StreamConfig cfg)
    {
        if (Running) return;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://0.0.0.0:{cfg.Port}");

        AppLog.Info($"=== iniciando v{typeof(StreamService).Assembly.GetName().Version} | {Environment.OSVersion} | porta {cfg.Port} | encoder={cfg.Encoder}");
        var encoder = new ScreenEncoder(cfg);
        var mixer = new AudioMixer(cfg.AudioBitrateKbps, cfg.DefaultMutedApps);
        var hub = new StreamHub(encoder, mixer);

        var app = builder.Build();
        var indexHtml = ReadResource("index.html");

        // IPs banidos nem carregam a página
        app.Use(async (ctx, next) =>
        {
            if (cfg.IsBanned(NetUtil.Ip(ctx.Connection.RemoteIpAddress)))
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.ContentType = "text/plain; charset=utf-8";
                await ctx.Response.WriteAsync("Você foi banido desta transmissão.");
                return;
            }
            await next();
        });

        app.MapGet("/", () => Results.Text(indexHtml, "text/html; charset=utf-8"));
        app.MapGet("/favicon.ico", () => Results.NoContent());
        app.MapGet("/assets/{name}", (string name) =>
        {
            if (name.IndexOfAny(new[] { '/', '\\' }) >= 0) return Results.NotFound();
            var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("assets/" + name);
            if (resource == null) return Results.NotFound();
            return Results.Stream(resource, name.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase) ? "font/woff2" : "image/svg+xml");
        });
        app.MapGet("/api/info", () => new { passwordRequired = cfg.Password.Length > 0 });
        app.MapGet("/api/viewers", () => new { count = hub.ViewerCount, names = hub.ConnectedNames });
        app.MapPost("/api/offer", async (OfferRequest req, HttpContext ctx) =>
        {
            if (cfg.Password.Length > 0 && req.Password != cfg.Password) return Results.Unauthorized();
            var name = (req.Name ?? "").Trim();
            if (name.Length > 24) name = name[..24];
            if (name.Length == 0) name = "Visitante";
            var sdp = await hub.AcceptOfferAsync(req.Sdp, NetUtil.Ip(ctx.Connection.RemoteIpAddress), name);
            return Results.Json(new { sdp });
        });

        try { await app.StartAsync(); }
        catch { encoder.Dispose(); mixer.Dispose(); throw; }

        _app = app; _encoder = encoder; Mixer = mixer; Hub = hub;
        encoder.Start();
        mixer.Start();
        mixer.SetMic(cfg.MicEnabled, cfg.MicDeviceId, cfg.MicGain);
        bool only = cfg.SourceType == "window" && cfg.OnlyAppAudio && !string.IsNullOrEmpty(cfg.WindowExe)
                    && !cfg.WindowExe.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);
        mixer.SetOnlyApp(only ? cfg.WindowExe : null);
    }

    public async Task StopAsync()
    {
        var app = _app; if (app == null) return;
        _app = null;
        Hub?.CloseAll();
        _encoder?.Dispose();
        Mixer?.Dispose();
        try { await app.StopAsync(TimeSpan.FromSeconds(2)); await app.DisposeAsync(); } catch { }
        _encoder = null; Mixer = null; Hub = null;
    }

    private static string ReadResource(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
