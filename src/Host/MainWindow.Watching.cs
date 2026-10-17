using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LanCast.Viewer;

namespace LanCast;

// Aba "Assistindo": transmissões de amigos abertas dentro do LanCast, lado a lado.
public partial class MainWindow
{
    private readonly Multiview _watch = new();
    private readonly FullScreen _watchFullScreen = new();
    private ViewerWindow? _viewerWindow;
    private Rect _watchBounds;

    private void InitWatching()
    {
        _watch.ViewerName = _cfg.ViewerName;
        SViewerName.Text = _cfg.ViewerName;
        _watch.Changed += OnWatchChanged;
        _watch.PopoutRequested += popout => { if (popout) PopOutWatch(); else DockWatch(); };
        _watch.FullScreenChanged += SetWatchFullScreen;
        // O WebView2 é uma janela nativa: fica fora do TabControl (que descarrega abas ocultas) e é
        // posicionado por cima da área da aba. Assim as conexões continuam ao trocar de aba.
        WatchHost.Child = _watch.View;
        WatchPlaceholder.LayoutUpdated += (_, _) => PositionWatchHost();
    }

    private async void SavedWatch_Click(object sender, RoutedEventArgs e)
    {
        if (SavedFrom(sender) is not { } s) return;
        if (!await EnsureViewerNameAsync()) return;
        _watch.Add(s);
        ShowWatching();
        if (await _watch.EnsureAsync()) return;
        _watch.Forget(s.Id);
        if (await ConfirmAsync("Não foi possível abrir aqui", "O LanCast usa o Microsoft Edge WebView2 para exibir transmissões, e ele não foi encontrado neste computador. Instale o WebView2 Runtime ou abra a transmissão no navegador.", "Abrir no navegador"))
            SavedOpen_Click(sender, e);
    }

    private void ShowWatching()
    {
        if (_viewerWindow != null) { _viewerWindow.Activate(); return; }
        WatchTab.IsSelected = true;
    }

    private void OnWatchChanged()
    {
        int n = _watch.Count;
        WatchNavItem.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        WatchBadgeText.Text = n.ToString();
        if (n == 0)
        {
            _viewerWindow?.Close();
            if (WatchTab.IsSelected) SavedTab.IsSelected = true;
        }
        UpdateWatchVisibility();
    }

    /// <summary>O WebView2 sempre desenha por cima do WPF, então é escondido enquanto há um diálogo aberto.</summary>
    private void UpdateWatchVisibility()
    {
        bool popped = _viewerWindow != null;
        bool show = !popped && _dialog == null && _watch.Count > 0 && (WatchTab.IsSelected || _watchFullScreen.On);
        WatchHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        WatchPlaceholderTitle.Text = popped ? "Transmissões abertas em outra janela" : "Carregando o player…";
        WatchPlaceholderText.Text = popped ? "Feche aquela janela ou use o botão abaixo para trazê-las de volta." : "";
        WatchDockButton.Visibility = popped ? Visibility.Visible : Visibility.Collapsed;
        if (show) PositionWatchHost();
    }

    private void PositionWatchHost()
    {
        Rect bounds;
        if (_watchFullScreen.On) bounds = new Rect(0, 0, RootGrid.ActualWidth, RootGrid.ActualHeight);
        else if (WatchTab.IsSelected && WatchPlaceholder.IsVisible)
            bounds = new Rect(WatchPlaceholder.TranslatePoint(new Point(0, 0), RootGrid), new Size(WatchPlaceholder.ActualWidth, WatchPlaceholder.ActualHeight));
        else return;
        if (bounds == _watchBounds) return;
        _watchBounds = bounds;
        WatchHost.Margin = new Thickness(bounds.X, bounds.Y, 0, 0);
        WatchHost.Width = bounds.Width; WatchHost.Height = bounds.Height;
    }

    private void SetWatchFullScreen(bool on)
    {
        if (_viewerWindow != null) { _viewerWindow.SetFullScreen(on); return; }
        _watchFullScreen.Set(this, on);
        _watchBounds = Rect.Empty;
        UpdateWatchVisibility();
    }

    private void PopOutWatch()
    {
        if (_viewerWindow != null) { _viewerWindow.Activate(); return; }
        _watch.ExitFullScreen();
        WatchHost.Child = null;
        var window = new ViewerWindow(_cfg, _watch.View);
        window.Closed += (_, _) =>
        {
            _viewerWindow = null;
            WatchHost.Child = _watch.View;
            _watch.SetPopped(false);
            UpdateWatchVisibility();
        };
        _viewerWindow = window;
        window.Show();
        _watch.SetPopped(true);
        UpdateWatchVisibility();
    }

    private void DockWatch()
    {
        _viewerWindow?.Close();
        ShowFromTray();
        if (_watch.Count > 0) WatchTab.IsSelected = true;
    }

    private void WatchDock_Click(object sender, RoutedEventArgs e) => DockWatch();

    // ---------- nome ao assistir ----------

    private async Task<bool> EnsureViewerNameAsync()
    {
        if (_cfg.ViewerName.Length > 0) return true;
        DialogInput.Text = Environment.UserName.Length > 24 ? Environment.UserName[..24] : Environment.UserName;
        var result = ConfirmAsync("Como você quer aparecer?", "O anfitrião vê esse nome na lista de espectadores. Você pode mudar depois em Configurações.", "Continuar");
        DialogConfirm.Style = (Style)FindResource("PrimaryButton");
        DialogInput.Visibility = Visibility.Visible;
        DialogInput.Focus(); DialogInput.SelectAll();
        if (!await result) return false;
        var name = DialogInput.Text.Trim();
        if (name.Length == 0) return false;
        SViewerName.Text = name;   // ViewerName_Changed salva
        return true;
    }

    private void DialogInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DialogInput.Text.Trim().Length > 0) { CloseDialog(true); e.Handled = true; }
    }

    private void ViewerName_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        var name = SViewerName.Text.Trim();
        if (name == _cfg.ViewerName) return;
        _cfg.ViewerName = _watch.ViewerName = name;
        _cfg.Save();
    }
}
