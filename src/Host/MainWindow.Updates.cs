using System.Windows;
using System.Windows.Threading;
using LanCast.Updates;

namespace LanCast;

public partial class MainWindow
{
    private readonly UpdateService _updates = new();
    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    private UpdateRelease? _availableUpdate;
    private bool _checkingUpdate, _applyingUpdate;
    private CancellationTokenSource? _downloadCancellation;

    private async Task InitializeUpdatesAsync()
    {
        SCheckUpdates.IsChecked = _cfg.CheckUpdatesOnLaunch;
        UpdateStatus.Text = $"Versão instalada: {UpdateService.CurrentVersion.ToString(3)}.";
        _updateTimer.Tick += async (_, _) => { if (_cfg.CheckUpdatesOnLaunch) await CheckUpdatesAsync(false); };
        if (!UpdateService.CanApply)
        {
            UpdateStatus.Text += " Atualizações são aplicadas nas versões distribuídas pelo publicar.bat.";
            return;
        }
        _updateTimer.Start();
        if (_cfg.CheckUpdatesOnLaunch) await CheckUpdatesAsync(false);
    }

    private void CheckUpdatesPreference_Click(object sender, RoutedEventArgs e)
    {
        _cfg.CheckUpdatesOnLaunch = SCheckUpdates.IsChecked == true;
        _cfg.Save();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(true);

    private void CancelUpdate_Click(object sender, RoutedEventArgs e) => _downloadCancellation?.Cancel();

    private async Task CheckUpdatesAsync(bool manual)
    {
        if (_checkingUpdate || _applyingUpdate || _exiting) return;
        _checkingUpdate = true;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Verificando atualizações…";
        try
        {
            UpdateWorker.CleanOldDownloads();
            _availableUpdate = await _updates.CheckAsync(UpdateService.IsInstalled(Environment.ProcessPath!), _updateCancellation.Token);
            if (_exiting) return;
            UpdateStatus.Text = _availableUpdate == null ? "Nenhuma atualização disponível para esta versão." : $"Versão {_availableUpdate.Version} disponível. Atualize com a transmissão parada.";
            if (_availableUpdate != null)
            {
                Notify($"LanCast {_availableUpdate.Version} disponível em Configurações > Atualizações.");
                _tray.ShowBalloonTip(5000, "Atualização do LanCast", $"Versão {_availableUpdate.Version} disponível. Abra Configurações para atualizar.", System.Windows.Forms.ToolTipIcon.Info);
            }
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            UpdateStatus.Text = "Não foi possível verificar atualizações. Tente novamente mais tarde.";
            AppLog.Info("Verificação de atualização: " + ex.Message);
            if (manual) UpdateStatus.Text += " " + ex.Message;
        }
        finally { _checkingUpdate = false; CheckUpdateButton.IsEnabled = true; RefreshUpdateButton(); }
    }

    private void RefreshUpdateButton()
    {
        ApplyUpdateButton.IsEnabled = _availableUpdate != null && UpdateService.CanApply && !_checkingUpdate && !_applyingUpdate && !_svc.Running && !_busy;
    }

    private async void ApplyUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not { } release || _applyingUpdate || _svc.Running || _busy || _exiting || !UpdateService.CanApply) return;
        _applyingUpdate = true;
        RefreshUpdateButton();
        CheckUpdateButton.IsEnabled = false;
        try
        {
            if (!await ConfirmAsync("Atualizar o LanCast?", $"A versão {release.Version} será baixada. O app fechará e abrirá novamente após a atualização. Suas configurações serão preservadas." +
                (release.Installed ? " O Windows solicitará permissão de administrador." : ""), "Atualizar", information: false)) return;
            // Block all transmission entry points while download/install is in progress.
            if (_svc.Running || _busy || _exiting) return;
            StartStop.IsEnabled = false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_updateCancellation.Token);
            _downloadCancellation = timeout;
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            var directory = Path.Combine(UpdateWorker.Root, Guid.NewGuid().ToString("N"));
            UpdateProgress.Visibility = Visibility.Visible;
            CancelUpdateButton.Visibility = Visibility.Visible;
            var progress = new Progress<int>(percent => { UpdateProgress.Value = percent; UpdateStatus.Text = $"Baixando versão {release.Version}: {percent}%"; });
            await _updates.DownloadAsync(release, directory, progress, timeout.Token);
            CancelUpdateButton.Visibility = Visibility.Collapsed;
            _downloadCancellation = null;
            UpdateStatus.Text = "Preparando atualização…";
            await UpdateWorker.LaunchAsync(directory, release, timeout.Token);
            await ExitAsync();
        }
        catch (OperationCanceledException) when (_exiting) { }
        catch (OperationCanceledException)
        {
            UpdateStatus.Text = "Download cancelado ou tempo limite atingido. Você pode tentar novamente.";
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = "A atualização não foi aplicada. " + ex.Message;
            AppLog.Info("Download/atualização: " + ex.Message);
        }
        finally
        {
            _applyingUpdate = false;
            _downloadCancellation = null;
            CancelUpdateButton.Visibility = Visibility.Collapsed;
            UpdateProgress.Visibility = Visibility.Collapsed;
            CheckUpdateButton.IsEnabled = true;
            StartStop.IsEnabled = !_busy;
            RefreshUpdateButton();
        }
    }
}
