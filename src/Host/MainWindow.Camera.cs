using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LanCast.Video;

namespace LanCast;

// Aba "Câmera": escolhe a câmera, mostra a prévia, espelha a imagem e usa a câmera como fonte da transmissão.
public partial class MainWindow
{
    private PreviewCapture? _camPreview;
    private readonly StreamConfig _camCfg = new() { SourceType = "camera" };
    private List<string> _cams = new();
    private bool _camsLoaded, _camsLoading, _camUpdating;
    private byte[]? _camJpeg;
    private int _camDecoding;

    /// <summary>Mensagem quando a câmera está ocupada pela própria transmissão (a maioria das câmeras só abre uma vez).</summary>
    private string? CameraBusyMessage(string? name) =>
        _svc.Running && _cfg.SourceType == "camera" && (name == null || name == _cfg.CameraName)
            ? "A câmera está em uso pela transmissão.\nOs espectadores estão vendo esta imagem."
            : null;

    private void InitCamera()
    {
        _camPreview = new PreviewCapture(_camCfg, () => null, () => CameraBusyMessage(_camCfg.CameraName));
        _camPreview.Frame += jpg => { _camJpeg = jpg; if (Interlocked.Exchange(ref _camDecoding, 1) == 0) Dispatcher.BeginInvoke(DispatcherPriority.Background, ShowCameraFrame); };
        SyncCameraConfig();
        UpdateCameraUi();
    }

    private void SyncCameraConfig()
    {
        _camCfg.CameraName = _cfg.CameraName;
        _camCfg.CameraMirror = _cfg.CameraMirror;
    }

    private async Task LoadCamerasAsync()
    {
        if (_camsLoading) return;
        _camsLoading = true;
        CamRefresh.IsEnabled = false;
        CamPreviewMsg.Text = "Procurando câmeras…"; CamPreviewMsg.Visibility = Visibility.Visible;
        try { _cams = await CameraSources.ListAsync(); _camsLoaded = true; }
        finally { _camsLoading = false; CamRefresh.IsEnabled = true; }

        _camUpdating = true;
        CamList.ItemsSource = _cams;
        if (_cams.Count > 0)
        {
            if (!_cams.Contains(_cfg.CameraName)) { _cfg.CameraName = _cams[0]; _cfg.Save(); SyncCameraConfig(); }
            CamList.SelectedItem = _cfg.CameraName;
        }
        _camUpdating = false;
        UpdateCameraUi();
        _camPreview?.Restart();
    }

    private void UpdateCameraUi()
    {
        bool has = _cams.Count > 0;
        bool sharing = _cfg.SourceType == "camera";
        CamList.IsEnabled = has;
        CamNormalButton.IsEnabled = CamMirrorButton.IsEnabled = has;
        CamNormalButton.Style = (Style)FindResource(_cfg.CameraMirror ? "GhostButton" : "SettingsTabButton");
        CamMirrorButton.Style = (Style)FindResource(_cfg.CameraMirror ? "SettingsTabButton" : "GhostButton");
        CamMirrorHint.Text = _cfg.CameraMirror ? "Como num espelho: o que está à sua direita aparece à direita da tela."
                                               : "Como os outros veem você: sem inverter a imagem.";
        CamShareButton.IsEnabled = has || sharing;
        CamShareButton.Content = sharing ? "Parar de compartilhar a câmera" : "Compartilhar esta câmera";
        CamShareButton.Style = (Style)FindResource(sharing ? "DangerOutline" : "PrimaryButton");
        CamBadge.Text = sharing ? (_svc.Running ? "Transmitindo a câmera" : "Fonte: câmera") : "Prévia local";
        CamStatus.Text = !_camsLoaded ? "" :
            !has ? "Nenhuma câmera encontrada. Conecte uma câmera (ou ligue uma câmera virtual, como a do OBS) e clique em Atualizar." :
            sharing ? (_svc.Running ? "A câmera é a fonte da transmissão agora." : "A câmera será a fonte quando você iniciar a transmissão.") :
            "Escolher a câmera não inicia a transmissão. Use o botão para torná-la a fonte.";
    }

    /// <summary>A prévia da câmera só roda com a aba visível (não segura a câmera escondida na bandeja).</summary>
    private void UpdateCameraPreviewState()
    {
        if (_camPreview == null) return;
        bool want = CameraTab.IsSelected && IsVisible && WindowState != WindowState.Minimized && _cams.Count > 0;
        if (want && !_camPreview.Running) _camPreview.Start();
        else if (!want && _camPreview.Running) { _camPreview.Stop(); }

        if (_cams.Count == 0) { CamPreviewImg.Source = null; if (_camsLoaded) CamPreviewMsg.Text = "Sem câmera"; CamPreviewMsg.Visibility = Visibility.Visible; }
        else if (_camPreview.Running && _camPreview.Status is { } st) { CamPreviewMsg.Text = st; CamPreviewMsg.Visibility = Visibility.Visible; }
        else CamPreviewMsg.Visibility = CamPreviewImg.Source == null ? Visibility.Visible : Visibility.Collapsed;
        if (!_camsLoading && _camsLoaded && _cams.Count > 0 && CamPreviewImg.Source == null && _camPreview.Status == null) CamPreviewMsg.Text = "Preparando prévia…";
    }

    private void ShowCameraFrame()
    {
        try
        {
            var jpg = _camJpeg; if (jpg == null) return;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(jpg);
            bmp.EndInit();
            bmp.Freeze();
            CamPreviewImg.Source = bmp;
        }
        catch { }
        finally { Interlocked.Exchange(ref _camDecoding, 0); }
    }

    /// <summary>Chamado ao entrar na aba Câmera.</summary>
    private void OnCameraTabShown()
    {
        if (!_camsLoaded) _ = LoadCamerasAsync();
        UpdateCameraUi();
        UpdateCameraPreviewState();
    }

    private async void CamRefresh_Click(object sender, RoutedEventArgs e) { _camPreview?.Stop(); await LoadCamerasAsync(); UpdateCameraPreviewState(); }

    private void CamList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_camUpdating || CamList.SelectedItem is not string name || name == _cfg.CameraName) return;
        _cfg.CameraName = name; _cfg.Save();
        SyncCameraConfig();
        CamPreviewImg.Source = null;
        _camPreview?.Restart();
        if (_cfg.SourceType == "camera") { _svc.ApplySource(_cfg); }
        UpdateCameraUi();
    }

    private void CamMirror_Click(object sender, RoutedEventArgs e)
    {
        bool mirror = ReferenceEquals(sender, CamMirrorButton);
        if (mirror == _cfg.CameraMirror) return;
        _cfg.CameraMirror = mirror; _cfg.Save();
        SyncCameraConfig();
        _camPreview?.Restart();
        if (_cfg.SourceType == "camera") _svc.ApplySource(_cfg);       // reinicia a captura com a nova orientação
        UpdateCameraUi();
        Notify(mirror ? "Imagem espelhada." : "Imagem normal.");
    }

    private void CamShare_Click(object sender, RoutedEventArgs e)
    {
        if (_cfg.SourceType == "camera")
        {
            _cfg.SourceType = "monitor";                                   // volta para a tela escolhida antes
            Notify("Voltou a compartilhar a tela.");
        }
        else
        {
            if (string.IsNullOrEmpty(_cfg.CameraName)) return;
            _cfg.SourceType = "camera";
            Notify(_svc.Running ? "Agora você está compartilhando a câmera." : "A câmera será a fonte da transmissão.");
        }
        _cfg.Save();
        _camPreview?.Stop();                                               // libera a câmera para a captura da transmissão
        _svc.ApplySource(_cfg);
        _preview?.Restart();
        RefreshSources();
        UpdateCameraUi();
        UpdateCameraPreviewState();
    }
}
