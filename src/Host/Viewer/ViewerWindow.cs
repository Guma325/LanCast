using System.Windows;
using System.Windows.Controls;

namespace LanCast.Viewer;

/// <summary>Janela separada para as transmissões de amigos (ex.: num segundo monitor). Fechar devolve o player ao LanCast.</summary>
public sealed class ViewerWindow : Window
{
    private readonly Border _frame = new() { Padding = new Thickness(16) };
    private readonly StreamConfig _cfg;
    private readonly FullScreen _fullScreen = new();

    public ViewerWindow(StreamConfig cfg, UIElement content)
    {
        _cfg = cfg;
        Title = "LanCast · Assistindo";
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app.ico"));
        MinWidth = 480; MinHeight = 320;
        SetResourceReference(BackgroundProperty, "Bg");
        _frame.Child = content;
        Content = _frame;

        var b = cfg.ViewerWindow;
        if (b != null && b.Width >= MinWidth && b.Height >= MinHeight && OnScreen(b))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = b.Left; Top = b.Top; Width = b.Width; Height = b.Height;
            if (b.Maximized) Loaded += (_, _) => WindowState = WindowState.Maximized;
        }
        else { Width = 1280; Height = 760; WindowStartupLocation = WindowStartupLocation.CenterScreen; }

        SourceInitialized += (_, _) => MainWindow.ApplyTitleBar(this);
        Closing += (_, e) =>
        {
            if (e.Cancel) return;
            SetFullScreen(false);
            var r = RestoreBounds;
            if (!r.IsEmpty) { _cfg.ViewerWindow = new WindowBounds { Left = r.Left, Top = r.Top, Width = r.Width, Height = r.Height, Maximized = WindowState == WindowState.Maximized }; _cfg.Save(); }
            // solta o player antes de a janela ser destruída (senão a janela nativa do WebView2 iria junto)
            _frame.Child = null;
        };
    }

    public void SetFullScreen(bool on)
    {
        _frame.Padding = new Thickness(on ? 0 : 16);
        _fullScreen.Set(this, on);
    }

    private static bool OnScreen(WindowBounds b) =>
        new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)
            .IntersectsWith(new Rect(b.Left, b.Top, b.Width, b.Height));
}

/// <summary>Tela cheia de verdade (sem bordas, cobrindo a barra de tarefas) e volta ao estado anterior.</summary>
public sealed class FullScreen
{
    private WindowStyle _style;
    private WindowState _state;
    private ResizeMode _resize;
    public bool On { get; private set; }

    public void Set(Window w, bool on)
    {
        if (on == On) return;
        On = on;
        if (on)
        {
            _style = w.WindowStyle; _state = w.WindowState; _resize = w.ResizeMode;
            w.WindowStyle = WindowStyle.None; w.ResizeMode = ResizeMode.NoResize;
            if (w.WindowState == WindowState.Maximized) w.WindowState = WindowState.Normal;   // reaplica para cobrir a barra de tarefas
            w.WindowState = WindowState.Maximized;
        }
        else
        {
            w.WindowStyle = _style; w.ResizeMode = _resize; w.WindowState = _state;
        }
    }
}
