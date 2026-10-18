using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace LanCast.Viewer;

/// <summary>
/// Transmissões de amigos dentro do app: um WebView2 com a página multiview (um RTCPeerConnection por transmissão).
/// A sinalização (/api/offer, /api/viewers) passa por aqui porque o LanCast do anfitrião não libera CORS;
/// assim funciona com qualquer versão do anfitrião. A mídia vai direto do anfitrião para o WebView2.
/// </summary>
public sealed class Multiview
{
    private const string Origin = "https://lancast.internal/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    // cache do navegador fica em LocalAppData (não no perfil móvel); com LANCAST_DATA (testes) fica junto dos dados
    private static string DataFolder => Environment.GetEnvironmentVariable("LANCAST_DATA") != null
        ? Path.Combine(Paths.DataDir, "WebView2")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanCast", "WebView2");

    private readonly Dictionary<Guid, SavedStream> _open = new();
    private readonly List<object> _queue = new();   // mensagens enviadas antes da página carregar
    private Task<bool>? _init;
    private bool _ready;

    public WebView2 View { get; } = new();
    /// <summary>Nome enviado ao anfitrião (aparece em Espectadores).</summary>
    public string ViewerName { get; set; } = "";
    public int Count => _open.Count;

    /// <summary>Transmissão aberta ou fechada.</summary>
    public event Action? Changed;
    /// <summary>true = abrir em janela separada; false = voltar para o LanCast.</summary>
    public event Action<bool>? PopoutRequested;
    public event Action<bool>? FullScreenChanged;
    /// <summary>Botão "+" da página: escolher outra transmissão em Streams salvas.</summary>
    public event Action? PickRequested;

    /// <summary>Cria o WebView2 na primeira vez. false = runtime do WebView2 indisponível.</summary>
    public Task<bool> EnsureAsync()
    {
        if (_init is { IsCompleted: true, Result: false }) _init = null;   // permite tentar de novo
        return _init ??= InitAsync();
    }

    private async Task<bool> InitAsync()
    {
        if (TryColor("Bg") is { } bg) View.DefaultBackgroundColor = System.Drawing.Color.FromArgb(bg.R, bg.G, bg.B);   // sem clarão branco ao abrir
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, DataFolder,
                new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            await View.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            AppLog.Info("WebView2 indisponível: " + ex.Message);
            return false;
        }
        var core = View.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) => e.Response = Resource(core.Environment, e.Request.Uri);
        core.WebMessageReceived += OnMessage;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.ContainsFullScreenElementChanged += (_, _) => FullScreenChanged?.Invoke(core.ContainsFullScreenElement);
        core.ProcessFailed += (_, e) =>
        {
            AppLog.Info($"WebView2: processo falhou ({e.ProcessFailedKind}); recarregando.");
            _ready = false;
            _queue.Clear();
            foreach (var s in _open.Values) _queue.Add(AddMessage(s));
            try { core.Reload(); } catch { }
        };
        core.Navigate(Origin + "multiview.html");
        return true;
    }

    /// <summary>Serve a página e as fontes a partir dos recursos embutidos.</summary>
    private static CoreWebView2WebResourceResponse Resource(CoreWebView2Environment env, string uri)
    {
        var name = new Uri(uri).AbsolutePath.TrimStart('/');
        bool allowed = name == "multiview.html" || (name.StartsWith("assets/", StringComparison.Ordinal) && name.IndexOf('/', 7) < 0);
        var stream = allowed ? Assembly.GetExecutingAssembly().GetManifestResourceStream(name) : null;
        if (stream == null) return env.CreateWebResourceResponse(null, 404, "Not Found", "");
        var type = name.EndsWith(".html") ? "text/html; charset=utf-8" : name.EndsWith(".woff2") ? "font/woff2" : "image/svg+xml";
        return env.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: " + type);
    }

    public bool IsOpen(Guid id) => _open.ContainsKey(id);

    public void Add(SavedStream stream)
    {
        _open[stream.Id] = stream;
        Send(AddMessage(stream));
        Changed?.Invoke();
    }

    /// <summary>Fecha uma transmissão que ainda não chegou à página (ex.: o WebView2 não pôde ser criado).</summary>
    public void Forget(Guid id)
    {
        if (!_open.Remove(id)) return;
        _queue.RemoveAll(m => m is AddCommand a && a.stream.id == id);
        Changed?.Invoke();
    }

    public void CloseAll() => Send(new { cmd = "closeAll" });
    public void SetPopped(bool popped) => Send(new { cmd = "popped", value = popped });
    public void Toast(string text) { if (_ready) Send(new { cmd = "toast", text }); }
    public void ExitFullScreen() { if (_ready) _ = View.CoreWebView2.ExecuteScriptAsync("document.fullscreenElement&&document.exitFullscreen()"); }

    public void ApplyTheme()
    {
        if (_init == null) return;
        if (TryColor("Bg") is { } bg) View.DefaultBackgroundColor = System.Drawing.Color.FromArgb(bg.R, bg.G, bg.B);
        if (_ready) Send(ThemeMessage());
    }
    /// <summary>Fecha as conexões (o anfitrião vê a saída na hora) e libera o WebView2.</summary>
    public async Task ShutdownAsync()
    {
        if (_ready && _open.Count > 0) { CloseAll(); await Task.Delay(300); }
        try { View.Dispose(); } catch { }
    }

    private sealed record StreamRef(Guid id, string name);
    private sealed record AddCommand(string cmd, StreamRef stream);
    private static AddCommand AddMessage(SavedStream s) => new("add", new StreamRef(s.Id, s.Name));

    private void Send(object message)
    {
        if (!_ready) { _queue.Add(message); return; }
        try { View.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message)); }
        catch (Exception ex) { AppLog.Info("WebView2: falha ao enviar mensagem: " + ex.Message); }
    }

    private static Color? TryColor(string key) => Application.Current.TryFindResource(key) is SolidColorBrush b ? b.Color : null;

    private static object ThemeMessage()
    {
        string C(string key) => TryColor(key) is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "";
        var vars = new Dictionary<string, string>
        {
            ["bg"] = C("Bg"), ["surface"] = C("Surface"), ["raised"] = C("Surface2"), ["line"] = C("Line"), ["text"] = C("Text"),
            ["muted"] = C("Muted"), ["accent"] = C("Accent"), ["onaccent"] = C("OnAccent"), ["ok"] = C("Ok"), ["danger"] = C("Danger"), ["warn"] = C("Warn"),
        };
        return new { cmd = "theme", dark = ThemeManager.IsDark, vars = vars.Where(kv => kv.Value.Length > 0).ToDictionary(kv => kv.Key, kv => kv.Value) };
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement m;
        try { using var doc = JsonDocument.Parse(e.WebMessageAsJson); m = doc.RootElement.Clone(); }
        catch { return; }

        if (m.TryGetProperty("event", out var ev))
        {
            switch (ev.GetString())
            {
                case "ready":
                    _ready = true;
                    ApplyTheme();
                    var pending = _queue.ToList(); _queue.Clear();
                    pending.ForEach(Send);
                    break;
                case "closed":
                    if (Guid.TryParse(Str(m, "id"), out var id) && _open.Remove(id)) Changed?.Invoke();
                    break;
                case "popout": PopoutRequested?.Invoke(true); break;
                case "dock": PopoutRequested?.Invoke(false); break;
                case "pick": PickRequested?.Invoke(); break;
            }
            return;
        }

        if (!m.TryGetProperty("req", out var reqProp) || !reqProp.TryGetInt32(out int req)) return;
        object reply;
        if (!Guid.TryParse(Str(m, "id"), out var streamId) || !_open.TryGetValue(streamId, out var s))
            reply = new { ok = false, status = 0, error = "Transmissão fechada." };
        else reply = Str(m, "method") switch
        {
            "offer" => await RequestAsync(() => Http.PostAsJsonAsync(BaseUrl(s) + "api/offer",
                new { sdp = Str(m, "sdp"), password = Str(m, "password"), name = ViewerName })),
            "viewers" => await RequestAsync(() => Http.GetAsync(BaseUrl(s) + "api/viewers", new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token)),
            _ => new { ok = false, status = 0, error = "Pedido desconhecido." },
        };
        Send(Merge(req, reply));
    }

    private static string BaseUrl(SavedStream s) => $"http://{s.Host}:{s.Port}/";
    private static string Str(JsonElement m, string name) => m.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static Dictionary<string, object?> Merge(int req, object reply)
    {
        var d = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(reply))!;
        d["req"] = req;
        return d;
    }

    private static async Task<object> RequestAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            using var response = await send();
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return new { ok = false, status = (int)response.StatusCode, error = response.StatusCode == HttpStatusCode.NotFound
                    ? "Esse endereço não parece ser um LanCast." : $"O anfitrião recusou a conexão ({(int)response.StatusCode})." };
            return new { ok = true, status = (int)response.StatusCode, body = JsonSerializer.Deserialize<JsonElement>(text) };
        }
        catch (TaskCanceledException) { return new { ok = false, status = 0, error = "O anfitrião demorou a responder." }; }
        catch (Exception) { return new { ok = false, status = 0, error = "Não foi possível alcançar o anfitrião. Ele pode ter parado de transmitir." }; }
    }
}
