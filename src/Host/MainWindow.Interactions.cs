using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LanCast.Audio;
using LanCast.Video;
using NAudio.CoreAudioApi;

namespace LanCast;

public partial class MainWindow
{
    private TaskCompletionSource<bool>? _dialog;
    private IInputElement? _previousFocus;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private MicCapture? _localMic;
    private bool _meterFailed;

    private Task<bool> ConfirmAsync(string title, string message, string action, string? name = null, string? ip = null, bool information = false)
    {
        if (_dialog != null) return Task.FromResult(false);
        _previousFocus = Keyboard.FocusedElement;
        _dialog = new TaskCompletionSource<bool>();
        DialogTitle.Text = title; DialogMessage.Text = message; DialogConfirm.Content = action;
        DialogSourcePicker.Visibility = Visibility.Collapsed; DialogConfirm.Visibility = Visibility.Visible;
        DialogConfirm.Style = (Style)FindResource(information ? "PrimaryButton" : "DangerOutline");
        DialogCancel.Visibility = information ? Visibility.Collapsed : Visibility.Visible;
        DialogPerson.Visibility = name == null ? Visibility.Collapsed : Visibility.Visible;
        DialogPersonName.Text = name ?? ""; DialogPersonIp.Text = ip ?? "";
        AppShell.IsEnabled = false; DialogLayer.Visibility = Visibility.Visible;
        (information ? DialogConfirm : DialogCancel).Focus();
        return _dialog.Task;
    }
    private void CloseDialog(bool result)
    {
        var pending = _dialog; _dialog = null;
        DialogLayer.Visibility = Visibility.Collapsed; DialogSourcePicker.Visibility = Visibility.Collapsed; AppShell.IsEnabled = true;
        if (_previousFocus != null) Keyboard.Focus(_previousFocus);
        pending?.TrySetResult(result);
    }
    private void DialogCancel_Click(object sender, RoutedEventArgs e) => CloseDialog(false);
    private void DialogConfirm_Click(object sender, RoutedEventArgs e) => CloseDialog(true);
    private void DialogBackdrop_Click(object sender, MouseButtonEventArgs e) => CloseDialog(false);
    private void DialogPanel_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _dialog != null) { CloseDialog(false); e.Handled = true; }
    }
    private void Notify(string message)
    {
        ToastText.Text = message; Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop(); _toastTimer.Tick -= HideToast; _toastTimer.Tick += HideToast; _toastTimer.Start();
    }
    private void HideToast(object? sender, EventArgs e) { Toast.Visibility = Visibility.Collapsed; _toastTimer.Stop(); }
    private void Navigation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PageTitle == null) return;
        string[] titles = { "Compartilhar", "Espectadores", "Áudio dos aplicativos", "Microfone", "Banidos", "Configurações" };
        string[] descriptions = { "Escolha o que deseja transmitir para sua rede.", "Gerencie quem está assistindo à transmissão.", "Escolha quais aplicativos os espectadores podem ouvir.", "Configure sua voz para a transmissão.", "Gerencie os IPs bloqueados na transmissão.", "Personalize a transmissão e a aparência do LanCast." };
        int index = Math.Clamp(NavList.SelectedIndex, 0, 5);
        PageTitle.Text = titles[index]; PageSubtitle.Text = descriptions[index];
        InvitePanel.Visibility = index is 0 or 1 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreviewState(); UpdateLocalMeter();
    }
    private void SettingsTab_Click(object sender, RoutedEventArgs e)
    {
        SettingsTabs.SelectedIndex = int.Parse((string)((Button)sender).Tag);
        TransmissionSettingsButton.Style = (Style)FindResource(SettingsTabs.SelectedIndex == 0 ? "SettingsTabButton" : "GhostButton");
        AppearanceSettingsButton.Style = (Style)FindResource(SettingsTabs.SelectedIndex == 1 ? "SettingsTabButton" : "GhostButton");
    }
    private void RestoreTheme_Click(object sender, RoutedEventArgs e)
    {
        STheme.SelectedItem = ThemeManager.Themes[0];
        ThemeStatus.Text = "Tema padrão restaurado e salvo.";
    }
    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || ApplySettingsButton == null) return;
        bool dirty = SPort.Text != _cfg.Port.ToString() || SPassword.Password != _cfg.Password || SBitrate.Text != _cfg.VideoBitrateKbps.ToString()
            || !Equals(SFps.SelectedItem, _cfg.Fps) || SHeight.SelectedItem is not KeyValuePair<string, int> height || height.Value != _cfg.Height
            || SEncoder.SelectedItem is not KeyValuePair<string, string> encoder || encoder.Value != _cfg.Encoder
            || (SCursor.IsChecked == true) != _cfg.ShowCursor || (SAuto.IsChecked == true) != _cfg.StartOnLaunch || (SLive.IsChecked == true) != _cfg.ShowLiveIndicator;
        ApplySettingsButton.IsEnabled = dirty && !_busy;
        ApplySettingsButton.Content = dirty ? "Aplicar configurações" : "Configurações salvas";
        SMsg.Text = dirty ? "Você tem alterações não salvas." : "Configurações salvas.";
        if (int.TryParse(SBitrate.Text, out int bitrate)) BitrateHint.Text = $"Aproximadamente {bitrate / 1000.0:0.#} Mbps de upload por espectador.";
    }
    private async void ConnectionHelp_Click(object sender, RoutedEventArgs e) => await ConfirmAsync("Como conectar espectadores", "Compartilhe o link da rede local ou do Radmin VPN. As pessoas precisam estar na mesma rede e abrir o link no navegador. Se o acesso falhar, confira a porta e a regra do Firewall nas Configurações.", "Entendi", information: true);
    private async void RecoverSource_Click(object sender, RoutedEventArgs e)
    {
        RefreshSources();
        DialogMonitorList.ItemsSource = _monRows; DialogWindowList.ItemsSource = _winRows;
        DialogMonitorList.ItemTemplate = DialogWindowList.ItemTemplate = (DataTemplate)MonitorList.FindResource("PICKCARD");
        var result = ConfirmAsync("Escolha outra fonte", "Selecionar uma tela ou janela atualiza a fonte. A transmissão permanece no estado atual.", "Selecionar");
        DialogSourcePicker.Visibility = Visibility.Visible; DialogConfirm.Visibility = Visibility.Collapsed;
        await result;
    }
    private void UpdateLocalMeter()
    {
        bool want = _cfg.MicEnabled && !_svc.Running && IsVisible && WindowState != WindowState.Minimized && NavList.SelectedIndex == 3;
        if (!want) { _localMic?.Dispose(); _localMic = null; _meterFailed = false; return; }
        if (_localMic != null || _meterFailed) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = string.IsNullOrEmpty(_cfg.MicDeviceId) ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications) : enumerator.GetDevice(_cfg.MicDeviceId);
            _localMic = new MicCapture(device, new PcmBuffer()) { Gain = (float)_cfg.MicGain }; _localMic.Start();
        }
        catch (Exception ex) { _localMic?.Dispose(); _localMic = null; _meterFailed = true; MicMsg.Text = "Microfone: " + ex.Message; }
    }
}
