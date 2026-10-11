using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LanCast;

// Aba "Streams salvas": endereços favoritos com indicador de ativa/offline.
public partial class MainWindow
{
    private enum StreamState { Checking, Active, Offline, Blocked }

    private static readonly HttpClient Probe = new() { Timeout = TimeSpan.FromSeconds(2.5) };
    private readonly ObservableCollection<Row> _saved = new();
    private readonly ConcurrentDictionary<Guid, (StreamState State, int Viewers)> _savedState = new();
    private readonly HashSet<Guid> _editing = new();
    private readonly DispatcherTimer _savedTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _checkingSaved;

    private void InitSavedStreams()
    {
        SavedList.ItemsSource = _saved;
        _savedTimer.Tick += async (_, _) => await CheckSavedAsync();
        _savedTimer.Start();
        RefreshSavedRows();
    }

    private bool SavedTabVisible => IsVisible && WindowState != WindowState.Minimized && SavedTab.IsSelected;

    /// <summary>Aceita "26.51.157.46", "26.51.157.46:8080" ou "http://26.51.157.46:8080/". Sem porta = 8080.</summary>
    private static bool TryParseAddress(string text, out string host, out int port)
    {
        host = ""; port = 8080;
        text = text.Trim();
        int scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) text = text[(scheme + 3)..];
        int slash = text.IndexOfAny(new[] { '/', '?', '#' });
        if (slash >= 0) text = text[..slash];
        int colon = text.LastIndexOf(':');
        if (colon >= 0)
        {
            if (!int.TryParse(text[(colon + 1)..], out port) || port is < 1 or > 65535) return false;
            text = text[..colon];
        }
        if (text.Length == 0 || text.Any(c => char.IsWhiteSpace(c) || c == '@')) return false;
        host = text;
        return true;
    }

    private static string AddressText(SavedStream s) => $"{s.Host}:{s.Port}";

    private async Task CheckSavedAsync(bool force = false)
    {
        if (_checkingSaved || (!force && !SavedTabVisible)) return;
        var items = _cfg.SavedStreams;
        if (items.Count == 0) return;
        _checkingSaved = true;
        try { await Task.WhenAll(items.Select(async s => _savedState[s.Id] = await ProbeAsync(s))); }
        catch { }
        finally { _checkingSaved = false; }
        RefreshSavedRows();
    }

    private static async Task<(StreamState, int)> ProbeAsync(SavedStream s)
    {
        try
        {
            using var response = await Probe.GetAsync($"http://{s.Host}:{s.Port}/api/viewers");
            if (response.StatusCode == HttpStatusCode.Forbidden) return (StreamState.Blocked, 0);
            if (!response.IsSuccessStatusCode) return (StreamState.Offline, 0);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            int viewers = doc.RootElement.TryGetProperty("count", out var c) && c.TryGetInt32(out var n) ? n : 0;
            return (StreamState.Active, viewers);
        }
        catch { return (StreamState.Offline, 0); }
    }

    private void RefreshSavedRows()
    {
        var items = _cfg.SavedStreams;
        Sync(_saved, items, s => s.Id.ToString(), (row, s) =>
        {
            _savedState.TryGetValue(s.Id, out var st);   // padrão = Checking
            bool active = st.State == StreamState.Active;
            row.Set("Id", s.Id); row.Set("Name", s.Name); row.Set("Address", AddressText(s));
            row.Set("StatusText", st.State switch
            {
                StreamState.Active => st.Viewers > 0 ? $"Ativa · {st.Viewers} assistindo" : "Ativa",
                StreamState.Blocked => "Sem acesso (seu IP foi banido)",
                StreamState.Offline => "Offline",
                _ => "Verificando…",
            });
            row.Set("StatusFg", (Brush)FindResource(st.State switch { StreamState.Active => "Ok", StreamState.Blocked => "Warn", _ => "Muted" }));
            row.Set("IconBg", (Brush)FindResource(active ? "OkSoft" : "Surface3"));
            row.Set("IconFg", (Brush)FindResource(active ? "Ok" : "Muted"));
            bool editing = _editing.Contains(s.Id);
            row.Set("NameVis", editing ? Visibility.Collapsed : Visibility.Visible);
            row.Set("EditVis", editing ? Visibility.Visible : Visibility.Collapsed);
        });
        SavedEmpty.Visibility = _saved.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SavedList.Visibility = _saved.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        int activeCount = items.Count(s => _savedState.TryGetValue(s.Id, out var x) && x.State == StreamState.Active);
        SavedBadge.Visibility = activeCount == 0 ? Visibility.Collapsed : Visibility.Visible;
        SavedBadgeText.Text = activeCount.ToString();
    }

    private async void SavedAdd_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseAddress(SavedAddress.Text, out var host, out var port))
        {
            SavedMsg.Text = "Endereço inválido. Use o IP da pessoa, por exemplo 26.51.157.46 ou 26.51.157.46:8080.";
            return;
        }
        if (_cfg.SavedStreams.Any(s => s.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && s.Port == port))
        {
            SavedMsg.Text = "Esse endereço já está salvo.";
            return;
        }
        var name = SavedName.Text.Trim();
        var item = new SavedStream { Id = Guid.NewGuid(), Name = name.Length > 0 ? name : host, Host = host, Port = port };
        _cfg.SavedStreams = new List<SavedStream>(_cfg.SavedStreams) { item };   // cópia: o Save() pode rodar em outra thread
        _cfg.Save();
        SavedName.Text = ""; SavedAddress.Text = ""; SavedMsg.Text = "";
        RefreshSavedRows();
        Notify("Stream salva.");
        await CheckSavedAsync(force: true);
    }

    private void SavedField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SavedAdd_Click(sender, e);
    }

    private SavedStream? SavedFrom(object sender) =>
        ((FrameworkElement)sender).Tag is Guid id ? _cfg.SavedStreams.FirstOrDefault(s => s.Id == id) : null;

    private void SavedOpen_Click(object sender, RoutedEventArgs e)
    {
        if (SavedFrom(sender) is not { } s) return;
        try { Process.Start(new ProcessStartInfo($"http://{s.Host}:{s.Port}/") { UseShellExecute = true }); }
        catch (Exception ex) { Notify("Não foi possível abrir o navegador: " + ex.Message); }
    }

    private void SavedRename_Click(object sender, RoutedEventArgs e)
    {
        if (SavedFrom(sender) is not { } s) return;
        _editing.Add(s.Id);
        RefreshSavedRows();
    }

    private void SavedNameEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox box && box.IsVisible)
            box.Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Input);
    }

    private void SavedNameEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitSavedName(sender, true); e.Handled = true; }
        else if (e.Key == Key.Escape) { CommitSavedName(sender, false); e.Handled = true; }
    }

    private void SavedNameEdit_LostFocus(object sender, RoutedEventArgs e) => CommitSavedName(sender, true);

    private void CommitSavedName(object sender, bool save)
    {
        var box = (System.Windows.Controls.TextBox)sender;
        if (box.Tag is not Guid id || !_editing.Remove(id)) return;
        var name = box.Text.Trim();
        if (save && name.Length > 0 && _cfg.SavedStreams.FirstOrDefault(s => s.Id == id) is { } s && s.Name != name)
        {
            _cfg.SavedStreams = _cfg.SavedStreams.Select(x => x.Id == id ? new SavedStream { Id = x.Id, Name = name, Host = x.Host, Port = x.Port } : x).ToList();
            _cfg.Save();
        }
        RefreshSavedRows();
    }

    private async void SavedRemove_Click(object sender, RoutedEventArgs e)
    {
        if (SavedFrom(sender) is not { } s) return;
        if (!await ConfirmAsync("Remover stream salva?", "Ela some da lista. A transmissão em si não é afetada.", "Remover", s.Name, AddressText(s))) return;
        _cfg.SavedStreams = _cfg.SavedStreams.Where(x => x.Id != s.Id).ToList();
        _savedState.TryRemove(s.Id, out _);
        _cfg.Save();
        RefreshSavedRows();
        Notify("Stream removida.");
    }
}
