using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LanCast;

public partial class MainWindow
{
    private HotkeyManager? _hotkeys;

    private void InitHotkey()
    {
        SHotkey.Text = HotkeyLabel(_cfg.Hotkey);
        try
        {
            _hotkeys = new HotkeyManager();
            _hotkeys.Pressed += () => Dispatcher.BeginInvoke(new Action(HotkeyPressed));
            if (!_hotkeys.Register(_cfg.Hotkey))
                HotkeyMsg.Text = $"O atalho {_cfg.Hotkey} já está em uso por outro programa. Escolha outro em Configurações.";
        }
        catch (Exception ex) { AppLog.Info("[hotkey] " + ex.Message); }
    }

    private static string HotkeyLabel(string hotkey) => string.IsNullOrWhiteSpace(hotkey) ? "Nenhum" : hotkey;

    private async void HotkeyPressed()
    {
        // sem confirmação: o atalho é um gesto deliberado (e a janela pode estar escondida)
        if (_exiting || _applyingUpdate || _dialog != null) return;
        bool wasRunning = _svc.Running;
        if (wasRunning) await StopAsync(); else await StartAsync();
        if (_svc.Running == wasRunning) return;
        AnnounceHotkey(_svc.Running ? "Transmissão iniciada." : "Transmissão parada.");
    }

    private void AnnounceHotkey(string message)
    {
        Notify(message);
        if (!IsVisible || WindowState == WindowState.Minimized)
            try { _tray.ShowBalloonTip(2000, "LanCast", message, System.Windows.Forms.ToolTipIcon.Info); } catch { }
    }

    private void Hotkey_KeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) return;                                // deixa navegar pelo teclado
        e.Handled = true;
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete) { SetHotkey(""); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None or Key.DeadCharProcessed or Key.ImeProcessed) return;
        if (modifiers == ModifierKeys.None && key is not (>= Key.F1 and <= Key.F24))
        {
            HotkeyMsg.Text = "Use Ctrl, Alt, Shift ou Win junto com a tecla (ou uma tecla F sozinha).";
            return;
        }
        SetHotkey(HotkeyManager.Format(modifiers, key));
    }

    private void SetHotkey(string hotkey)
    {
        var previous = _cfg.Hotkey;
        if (_hotkeys == null || !_hotkeys.Register(hotkey))
        {
            _hotkeys?.Register(previous);
            HotkeyMsg.Text = $"Não foi possível usar {hotkey}: já está em uso por outro programa.";
            return;
        }
        _cfg.Hotkey = hotkey; _cfg.Save();
        SHotkey.Text = HotkeyLabel(hotkey);
        HotkeyMsg.Text = hotkey == "" ? "Atalho removido." : "Atalho salvo.";
    }

    private void HotkeyClear_Click(object sender, RoutedEventArgs e) => SetHotkey("");
    private void HotkeyReset_Click(object sender, RoutedEventArgs e) => SetHotkey(StreamConfig.DefaultHotkey);
}
