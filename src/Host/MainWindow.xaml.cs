using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using LanCast.Audio;
using LanCast.Video;

namespace LanCast;

public sealed class Row : INotifyPropertyChanged
{
    private readonly Dictionary<string, object?> _v = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public object? this[string k] => _v.TryGetValue(k, out var x) ? x : null;
    public void Set(string k, object? val)
    {
        if (_v.TryGetValue(k, out var old) && Equals(old, val)) return;
        _v[k] = val;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(k));
    }
    // propriedades usadas pelos bindings
    public Guid Id => (Guid)(this["Id"] ?? Guid.Empty);
    public string Name => (string?)this["Name"] ?? "";
    public string Title => (string?)this["Title"] ?? "";
    public string Remote => (string?)this["Remote"] ?? "";
    public string Duration => (string?)this["Duration"] ?? "";
    public string State => (string?)this["State"] ?? "";
    public string Label => (string?)this["Label"] ?? "";
    public string Url => (string?)this["Url"] ?? "";
    public string Pick => (string?)this["Pick"] ?? "";
    public string Icon => (string?)this["Icon"] ?? "";
    public Brush SelBg => (Brush?)this["SelBg"] ?? Brushes.Transparent;
    public Brush SelBorder => (Brush?)this["SelBorder"] ?? Brushes.Transparent;
    public Visibility CheckVis => (Visibility?)this["CheckVis"] ?? Visibility.Collapsed;
    public Brush Dot => (Brush?)this["Dot"] ?? Brushes.Gray;
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public Brush ChipBg => (Brush?)this["ChipBg"] ?? Brushes.Transparent;
    public Brush ChipFg => (Brush?)this["ChipFg"] ?? Brushes.Gray;
    public Brush MuteBg => (Brush?)this["MuteBg"] ?? Brushes.Transparent;
    public Brush MuteFg => (Brush?)this["MuteFg"] ?? Brushes.White;
}

public partial class MainWindow : Window
{
    private readonly StreamConfig _cfg = StreamConfig.Load();
    private readonly StreamService _svc = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly ObservableCollection<Row> _viewers = new(), _apps = new(), _addrs = new(), _bans = new(), _monRows = new(), _winRows = new();
    private PreviewCapture? _preview;
    private byte[]? _pendingJpeg;
    private int _decoding, _tick;
    private List<MonitorInfo> _monitors = new();
    private List<WindowInfo> _wins = new();
    private readonly System.Windows.Forms.NotifyIcon _tray = new();
    private bool _exiting, _loading = true, _busy;
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();
        ViewerList.ItemsSource = _viewers;
        AppList.ItemsSource = _apps;
        Addresses.ItemsSource = _addrs;
        BanList.ItemsSource = _bans;
        MonitorList.ItemsSource = _monRows;
        WindowList.ItemsSource = _winRows;
        _preview = new PreviewCapture(_cfg, () => _svc.Encoder?.ActiveKind);
        _preview.Frame += jpg => { _pendingJpeg = jpg; if (Interlocked.Exchange(ref _decoding, 1) == 0) Dispatcher.BeginInvoke(DispatcherPriority.Background, ShowFrame); };
        PreviewCheck.IsChecked = _cfg.ShowPreview;
        OnlyAudioCheck.IsChecked = _cfg.OnlyAppAudio;
        Footer.Text = "v" + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0") + "  ·  dados em %AppData%\\LanCast";

        SFps.ItemsSource = new[] { 30, 60 };
        SEncoder.ItemsSource = new[] { new KeyValuePair<string, string>("Automático (recomendado)", "auto"), new("NVIDIA (NVENC)", "nvenc"),
            new("AMD (AMF)", "amf"), new("Intel (Quick Sync)", "qsv"), new("Software (CPU)", "x264") };
        SHeight.ItemsSource = new[] { new KeyValuePair<string, int>("Nativa", 0), new("1080p", 1080), new("720p", 720), new("480p", 480) };
        LoadSettingsToUi();
        LoadMicDevices();
        RefreshAddresses();
        UpdateMicButton();
        SetupTray();
        RefreshBans();
        RefreshSources();

        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        _loading = false;

        Loaded += async (_, _) => { if (_cfg.StartOnLaunch) await StartAsync(); };
        Closing += OnClosing;
    }

    // ---------- iniciar / parar ----------

    private async void StartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_svc.Running) await StopAsync(); else await StartAsync();
    }

    private async Task StartAsync()
    {
        if (_busy || _svc.Running) return;
        _busy = true; StartStop.IsEnabled = false;
        try
        {
            await _svc.StartAsync(_cfg);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível iniciar na porta {_cfg.Port}.\n\n{ex.Message}\n\nTroque a porta em Configurações.",
                "LanCast", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _busy = false; StartStop.IsEnabled = true;
        Refresh();
    }

    private async Task StopAsync()
    {
        if (_busy || !_svc.Running) return;
        _busy = true; StartStop.IsEnabled = false;
        await _svc.StopAsync();
        _busy = false; StartStop.IsEnabled = true;
        Refresh();
    }

    // ---------- atualização periódica da interface ----------

    private void Refresh()
    {
        bool run = _svc.Running;
        var enc = _svc.Encoder;
        bool videoOk = enc?.Working ?? false;
        StatusDot.Fill = (Brush)FindResource(!run ? "Muted" : videoOk ? "Ok" : "Warn");
        StatusText.Text = !run ? "Parado" : videoOk ? "Transmitindo" : "Sem vídeo";
        StartStop.Content = run ? "Parar transmissão" : "Iniciar transmissão";
        StartStop.Style = (Style)FindResource(run ? "DangerButton" : "PrimaryButton");

        var viewers = _svc.Hub?.GetViewers() ?? new List<ViewerInfo>();
        int connected = viewers.Count(v => v.Connected);
        ViewerCount.Text = connected.ToString();
        SubStatus.Text = !run ? "·  clique em Iniciar para começar"
            : videoOk ? $"·  {enc!.Active}  ·  porta {_cfg.Port}"
            : $"·  tentando: {enc?.Active}" + (string.IsNullOrEmpty(enc?.LastError) ? "" : "  ·  " + Shorten(enc!.LastError!, 70));
        Title = run ? $"LanCast — {connected} conectado(s)" : "LanCast";
        _tray.Text = Title.Length > 60 ? Title[..60] : Title;
        SetLive(run, connected);

        Sync(_viewers, viewers, v => v.Id.ToString(), (row, v) =>
        {
            row.Set("Id", v.Id); row.Set("Name", v.Name); row.Set("Remote", v.Remote);
            var d = DateTime.Now - v.Since;
            row.Set("Duration", d.TotalHours >= 1 ? $"{(int)d.TotalHours}h {d.Minutes}min" : $"{d.Minutes}min {d.Seconds}s");
            row.Set("State", v.Connected ? "Conectado" : "Conectando");
            row.Set("ChipBg", (Brush)FindResource(v.Connected ? "OkSoft" : "Surface3"));
            row.Set("ChipFg", (Brush)FindResource(v.Connected ? "Ok" : "Warn"));
        });
        ViewerEmpty.Visibility = _viewers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ViewerList.Visibility = _viewers.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var apps = _svc.Mixer?.GetApps() ?? new List<AppAudioInfo>();
        Sync(_apps, apps, a => a.Name, (row, a) =>
        {
            row.Set("Name", a.Name); row.Set("Title", a.Title);
            row.Set("Label", a.Muted ? "Silenciado" : "Na transmissão");
            row.Set("Dot", (Brush)FindResource(a.Muted ? "Danger" : a.Playing ? "Ok" : "Muted"));
            row.Set("MuteBg", (Brush)FindResource(a.Muted ? "DangerSoft" : "OkSoft"));
            row.Set("MuteFg", (Brush)FindResource(a.Muted ? "Danger" : "Ok"));
        });
        AppEmpty.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdatePreviewState();
        if (++_tick % 4 == 0 && IsVisible && ShareTab.IsSelected) RefreshSources();

        MicLevel.Value = Math.Min(1, _svc.Mixer?.MicLevel ?? 0);
        if (_svc.Mixer?.MicError is { } err) MicMsg.Text = "Microfone: " + err;
        else if (!_cfg.MicEnabled || MicMsg.Text.StartsWith("Microfone:")) MicMsg.Text = "";
    }

    private static void Sync<T>(ObservableCollection<Row> rows, IReadOnlyList<T> items, Func<T, string> key, Action<Row, T> fill)
    {
        var keys = items.Select(key).ToList();
        for (int i = rows.Count - 1; i >= 0; i--)
            if (!keys.Contains(rows[i].KeyOf())) rows.RemoveAt(i);
        for (int i = 0; i < items.Count; i++)
        {
            var row = rows.FirstOrDefault(r => r.KeyOf() == keys[i]);
            if (row == null) { row = new Row(); row.Set("_key", keys[i]); rows.Add(row); }
            fill(row, items[i]);
        }
    }

    // ---------- botões ----------

    private void Kick_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is Guid id) _svc.Hub?.Kick(id);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string name || _svc.Mixer is not { } m) return;
        bool muted = m.GetApps().FirstOrDefault(a => a.Name == name)?.Muted ?? false;
        m.SetMuted(name, !muted);
        Refresh();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string url)
        {
            try { Clipboard.SetText(url); } catch { }
            ((Button)sender).Content = "Copiado!";
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += (_, _) => { ((Button)sender).Content = "Copiar"; t.Stop(); };
            t.Start();
        }
    }

    // ---------- fonte compartilhada + pré-visualização ----------

    private void RefreshSources()
    {
        _monitors = ScreenSources.ListMonitors();
        var selMon = ScreenSources.ResolveMonitor(_cfg.MonitorDevice, _cfg.Monitor);
        bool winMode = _cfg.SourceType == "window";
        Sync(_monRows, _monitors, m => m.Index.ToString(), (row, m) =>
        {
            row.Set("Pick", "m:" + m.Index); row.Set("Icon", "\uE7F4");
            row.Set("Name", m.Label); row.Set("Title", m.Detail);
            SetSelected(row, !winMode && selMon?.Index == m.Index);
        });

        _wins = ScreenSources.ListWindows().OrderBy(w => w.Exe, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Title).ToList();
        var selWin = winMode ? ScreenSources.ResolveWindow(_cfg.WindowExe, _cfg.WindowTitle) : null;
        Sync(_winRows, _wins, w => w.Hwnd.ToString(), (row, w) =>
        {
            row.Set("Pick", "w:" + w.Hwnd.ToInt64()); row.Set("Icon", "\uE737");
            row.Set("Name", w.Title);
            row.Set("Title", $"{w.Exe}  ·  {(w.Minimized ? "minimizada" : $"{w.Width}×{w.Height}")}");
            SetSelected(row, selWin?.Hwnd == w.Hwnd);
        });
        WindowEmpty.Visibility = _winRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // rótulo da fonte atual
        if (winMode)
        {
            SourceLabel.Text = $"Compartilhando a janela: {(selWin?.Title ?? _cfg.WindowTitle)}";
            SourceHint.Text = selWin == null ? $"A janela de {_cfg.WindowExe} não está aberta. A transmissão volta sozinha quando ela aparecer."
                : (_cfg.OnlyAppAudio ? $"Áudio: só de {selWin.Exe}. Os espectadores não escutam os outros apps." : "Áudio: todos os apps (menos os silenciados).");
        }
        else
        {
            SourceLabel.Text = $"Compartilhando: {selMon?.Label ?? "tela"}  ·  {selMon?.Detail}";
            SourceHint.Text = "Áudio: todos os apps (menos os silenciados).";
        }
        OnlyAudioCheck.IsEnabled = winMode;
    }

    private void SetSelected(Row row, bool on)
    {
        row.Set("SelBg", (Brush)FindResource(on ? "Surface3" : "Surface"));
        row.Set("SelBorder", (Brush)FindResource(on ? "Accent" : "Line"));
        row.Set("CheckVis", on ? Visibility.Visible : Visibility.Collapsed);
    }

    private void Source_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string pick) return;
        if (pick.StartsWith("m:") && int.TryParse(pick[2..], out int idx))
        {
            var m = _monitors.FirstOrDefault(x => x.Index == idx); if (m == null) return;
            _cfg.SourceType = "monitor"; _cfg.Monitor = m.Index; _cfg.MonitorDevice = m.Device;
        }
        else if (pick.StartsWith("w:") && long.TryParse(pick[2..], out long h))
        {
            var w = _wins.FirstOrDefault(x => x.Hwnd.ToInt64() == h); if (w == null) return;
            _cfg.SourceType = "window"; _cfg.WindowExe = w.Exe; _cfg.WindowTitle = w.Title;
        }
        else return;
        _cfg.Save();
        _svc.ApplySource(_cfg);
        _preview?.Restart();
        RefreshSources();
    }

    private void RefreshWindows_Click(object sender, RoutedEventArgs e) => RefreshSources();

    private void OnlyAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _cfg.OnlyAppAudio = OnlyAudioCheck.IsChecked == true;
        _cfg.Save();
        _svc.ApplySource(_cfg, restartVideo: false);
        RefreshSources();
    }

    private void PreviewCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _cfg.ShowPreview = PreviewCheck.IsChecked == true;
        _cfg.Save();
        UpdatePreviewState();
    }

    /// <summary>A pré-visualização só roda com a aba visível (não gasta CPU escondida na bandeja).</summary>
    private void UpdatePreviewState()
    {
        if (_preview == null) return;
        bool want = _cfg.ShowPreview && IsVisible && WindowState != WindowState.Minimized && ShareTab.IsSelected;
        if (want && !_preview.Running) _preview.Start();
        else if (!want && _preview.Running) _preview.Stop();

        if (!_cfg.ShowPreview) { PreviewImg.Source = null; PreviewMsg.Text = "Pré-visualização desligada"; PreviewMsg.Visibility = Visibility.Visible; }
        else if (_preview.Running && _preview.Status is { } st) { PreviewMsg.Text = st; PreviewMsg.Visibility = Visibility.Visible; }
        else PreviewMsg.Visibility = Visibility.Collapsed;
    }

    private void ShowFrame()
    {
        try
        {
            var jpg = _pendingJpeg;
            if (jpg == null) return;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(jpg);
            bmp.EndInit();
            bmp.Freeze();
            PreviewImg.Source = bmp;
        }
        catch { }
        finally { Interlocked.Exchange(ref _decoding, 0); }
    }

    // ---------- banimento ----------

    private static bool IsOwnIp(string ip)
    {
        if (System.Net.IPAddress.TryParse(ip, out var a) && System.Net.IPAddress.IsLoopback(a)) return true;
        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Any(u => u.Address.ToString() == ip);
    }

    private bool TryBan(string ip, string name)
    {
        if (IsOwnIp(ip)) { BanMsg.Text = "Esse IP é desta máquina e não pode ser banido."; return false; }
        if (!_cfg.Ban(ip, name)) { BanMsg.Text = $"{ip} já está banido."; return false; }
        _svc.Hub?.KickIp(ip);
        RefreshBans();
        BanMsg.Text = $"{ip} banido.";
        return true;
    }

    private void Ban_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string ip) return;
        var name = _svc.Hub?.GetViewers().FirstOrDefault(v => v.Remote == ip)?.Name ?? "";
        var ok = MessageBox.Show(this, $"Banir {(name.Length > 0 ? name + " " : "")}({ip})?\n\nEssa pessoa será desconectada e não conseguirá mais entrar. Você pode desbanir na aba Banidos.",
            "Banir conexão", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok == MessageBoxResult.Yes) TryBan(ip, name);
    }

    private void BanAdd_Click(object sender, RoutedEventArgs e)
    {
        var text = BanIp.Text.Trim();
        if (!System.Net.IPAddress.TryParse(text, out var a) || a.AddressFamily != AddressFamily.InterNetwork)
        { BanMsg.Text = "IP inválido (use o formato 26.10.20.30)."; return; }
        if (TryBan(a.ToString(), "(adicionado manualmente)")) BanIp.Clear();
    }

    private void BanIp_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) BanAdd_Click(sender, e);
    }

    private void Unban_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string ip) return;
        _cfg.Unban(ip);
        RefreshBans();
        BanMsg.Text = $"{ip} desbanido.";
    }

    private void RefreshBans()
    {
        var bans = _cfg.BanSnapshot();
        Sync(_bans, bans, b => b.Ip, (row, b) =>
        {
            row.Set("Remote", b.Ip);
            row.Set("Title", (string.IsNullOrWhiteSpace(b.Name) ? "" : b.Name + "  ·  ") + "banido em " + b.At.ToString("dd/MM/yyyy HH:mm"));
        });
        BanEmpty.Visibility = _bans.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BanList.Visibility = _bans.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        BanBadge.Visibility = _bans.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        BanBadgeText.Text = _bans.Count.ToString();
    }

    // ---------- aparência ----------

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        // barra de título escura (Windows 10 2004+ / 11)
        int on = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
    }

    // ---------- microfone ----------

    private void LoadMicDevices()
    {
        var list = new List<MicItem> { new("", "Padrão do Windows") };
        try { list.AddRange(AudioMixer.ListMics().Select(m => new MicItem(m.Id, m.Name))); } catch { }
        MicDevice.ItemsSource = list;
        MicDevice.SelectedItem = list.FirstOrDefault(x => x.Id == _cfg.MicDeviceId) ?? list[0];
        MicGain.Value = _cfg.MicGain;
    }

    private sealed record MicItem(string Id, string Name);

    private void UpdateMicButton()
    {
        MicToggle.Content = _cfg.MicEnabled ? "Microfone: LIGADO" : "Microfone: DESLIGADO";
        MicToggle.Style = (Style)FindResource(_cfg.MicEnabled ? "OkButton" : "DangerButton");
    }

    private void ApplyMic()
    {
        _cfg.Save();
        if (_svc.Mixer is { } m)
        {
            if (!m.SetMic(_cfg.MicEnabled, _cfg.MicDeviceId, _cfg.MicGain))
            {
                MicMsg.Text = "Microfone: " + m.MicError;
                _cfg.MicEnabled = false; _cfg.Save();
                UpdateMicButton();
            }
        }
    }

    private void MicToggle_Click(object sender, RoutedEventArgs e)
    {
        _cfg.MicEnabled = !_cfg.MicEnabled;
        UpdateMicButton();
        ApplyMic();
    }

    private void MicDevice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MicDevice.SelectedItem is not MicItem it) return;
        _cfg.MicDeviceId = it.Id;
        if (_cfg.MicEnabled) ApplyMic(); else _cfg.Save();
    }

    private void MicGain_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _cfg.MicGain = e.NewValue;
        _svc.Mixer?.SetMicGain(e.NewValue);
        _cfg.Save();
    }

    // ---------- configurações ----------

    private void LoadSettingsToUi()
    {
        SPort.Text = _cfg.Port.ToString();
        SPassword.Text = _cfg.Password;
        SMonitor.Text = _cfg.Monitor.ToString();
        SFps.SelectedItem = _cfg.Fps == 30 ? 30 : 60;
        SBitrate.Text = _cfg.VideoBitrateKbps.ToString();
        SHeight.SelectedItem = ((IEnumerable<KeyValuePair<string, int>>)SHeight.ItemsSource)
            .FirstOrDefault(k => k.Value == _cfg.Height, new("Nativa", 0));
        SEncoder.SelectedItem = ((IEnumerable<KeyValuePair<string, string>>)SEncoder.ItemsSource)
            .FirstOrDefault(k => k.Value == _cfg.Encoder, new("Automático (recomendado)", "auto"));
        SCursor.IsChecked = _cfg.ShowCursor;
        SAuto.IsChecked = _cfg.StartOnLaunch;
        SLive.IsChecked = _cfg.ShowLiveIndicator;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SPort.Text, out int port) || port is < 1024 or > 65535) { SMsg.Text = "Porta inválida (1024–65535)."; return; }
        if (!int.TryParse(SBitrate.Text, out int br) || br is < 500 or > 100000) { SMsg.Text = "Qualidade inválida (500–100000 kbps)."; return; }

        _cfg.Port = port; _cfg.Password = SPassword.Text.Trim();
        _cfg.Fps = (int)SFps.SelectedItem; _cfg.VideoBitrateKbps = br;
        _cfg.Height = ((KeyValuePair<string, int>)SHeight.SelectedItem).Value;
        _cfg.Encoder = ((KeyValuePair<string, string>)SEncoder.SelectedItem).Value;
        _cfg.ShowCursor = SCursor.IsChecked == true; _cfg.StartOnLaunch = SAuto.IsChecked == true;
        _cfg.ShowLiveIndicator = SLive.IsChecked == true;
        _cfg.Save();
        RefreshAddresses();

        if (_svc.Running)
        {
            SMsg.Text = "Aplicando... (espectadores precisarão reconectar)";
            await StopAsync();
            await StartAsync();
        }
        SMsg.Text = "Configurações salvas.";
    }

    private static string Shorten(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    private void Log_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(AppLog.FilePath)) File.WriteAllText(AppLog.FilePath, "");
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + AppLog.FilePath + "\"") { UseShellExecute = true });
        }
        catch { SMsg.Text = "Log em: " + AppLog.FilePath; }
    }

    private void Firewall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exe = Environment.ProcessPath!;
            Process.Start(new ProcessStartInfo("netsh",
                $"advfirewall firewall add rule name=\"LanCast\" dir=in action=allow program=\"{exe}\" enable=yes profile=any")
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            SMsg.Text = "Regra de firewall enviada (confirme o pedido de administrador).";
        }
        catch { SMsg.Text = "Cancelado."; }
    }

    // ---------- endereços para compartilhar ----------

    private void RefreshAddresses()
    {
        var found = new List<(string ip, string label, int prio)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = ua.Address.ToString();
                if (ip.StartsWith("169.254.")) continue;
                bool radmin = ip.StartsWith("26.") || nic.Name.Contains("Radmin", StringComparison.OrdinalIgnoreCase)
                              || nic.Description.Contains("Radmin", StringComparison.OrdinalIgnoreCase);
                bool virt = nic.Description.Contains("VMware") || nic.Description.Contains("VirtualBox") ||
                            nic.Description.Contains("Hyper-V") || nic.Name.StartsWith("vEthernet");
                if (virt) continue;
                found.Add((ip, radmin ? "Radmin VPN" : "Rede local", radmin ? 0 : 1));
            }
        }
        _addrs.Clear();
        foreach (var f in found.OrderBy(f => f.prio))
        {
            var r = new Row();
            r.Set("Url", $"http://{f.ip}:{_cfg.Port}/"); r.Set("Label", f.label);
            bool rad = f.prio == 0;
            r.Set("ChipBg", rad ? new SolidColorBrush(Color.FromArgb(0x30, 0x3B, 0x82, 0xF6)) : (Brush)FindResource("Surface3"));
            r.Set("ChipFg", rad ? new SolidColorBrush(Color.FromRgb(0x7C, 0xB0, 0xFF)) : (Brush)FindResource("Muted"));
            _addrs.Add(r);
        }
        if (_addrs.Count == 0) { var r = new Row(); r.Set("Url", $"http://localhost:{_cfg.Port}/"); r.Set("Label", "Local"); _addrs.Add(r); }
    }

    // ---------- bandeja / fechar ----------

    // ---------- indicador de transmissão (ponto vermelho na bandeja/barra de tarefas + pílula "AO VIVO") ----------

    private System.Drawing.Icon? _trayIdle, _trayLive;
    private ImageSource? _liveOverlay;
    private LiveIndicator? _liveBadge;
    private bool _live;

    private static System.Drawing.Icon WithRedDot(System.Drawing.Icon src)
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawIcon(src, new System.Drawing.Rectangle(0, 0, 32, 32));
            g.FillEllipse(System.Drawing.Brushes.White, 15, 15, 17, 17);
            g.FillEllipse(System.Drawing.Brushes.Red, 17, 17, 13, 13);
        }
        var h = bmp.GetHicon();
        try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);

    private static ImageSource MakeOverlayDot()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawEllipse(Brushes.White, null, new Point(8, 8), 8, 8);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xE5, 0x1C, 0x1C)), null, new Point(8, 8), 6, 6);
        }
        var rtb = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv); rtb.Freeze();
        return rtb;
    }

    private void SetLive(bool on, int viewers)
    {
        if (on != _live)
        {
            _live = on;
            if (_trayIdle != null) _tray.Icon = on ? _trayLive : _trayIdle;
            TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
            TaskbarItemInfo.Overlay = on ? (_liveOverlay ??= MakeOverlayDot()) : null;
            TaskbarItemInfo.Description = on ? "LanCast — transmitindo a tela" : null;
        }
        try { if (_liveBadge != null || (on && _cfg.ShowLiveIndicator)) (_liveBadge ??= new LiveIndicator()).Set(on && _cfg.ShowLiveIndicator, viewers); } catch { }
    }

    private void SetupTray()
    {
        try { _trayIdle = new System.Drawing.Icon(Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream, 32, 32); }
        catch { _trayIdle = System.Drawing.SystemIcons.Application; }
        try { _trayLive = WithRedDot(_trayIdle); } catch { _trayLive = _trayIdle; }
        _tray.Icon = _trayIdle;
        _tray.Text = "LanCast";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => ShowFromTray();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => ShowFromTray());
        menu.Items.Add("Iniciar/Parar transmissão", null, async (_, _) => { if (_svc.Running) await StopAsync(); else await StartAsync(); });
        menu.Items.Add("Sair", null, async (_, _) => await ExitAsync());
        _tray.ContextMenuStrip = menu;
    }

    private void ShowFromTray() { Show(); WindowState = WindowState.Normal; Activate(); }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;
        // fechar a janela só esconde na bandeja; a transmissão continua
        e.Cancel = true;
        Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray.ShowBalloonTip(3000, "LanCast", "Continua rodando na bandeja. Use o ícone para abrir ou sair.", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private async Task ExitAsync()
    {
        _exiting = true;
        _timer.Stop();
        _preview?.Dispose();
        _cfg.Save();
        await _svc.StopAsync();
        _liveBadge?.Close();
        _tray.Visible = false; _tray.Dispose();
        Application.Current.Shutdown();
    }
}

internal static class RowExt
{
    public static string KeyOf(this Row r) => (string?)r["_key"] ?? "";
}
