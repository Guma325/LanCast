using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LanCast;

/// <summary>
/// Indicador de "transmitindo": pílula vermelha "AO VIVO" no topo da tela (como o ponto vermelho do Android),
/// que não rouba o foco, deixa o mouse passar e fica fora da captura (espectadores não a veem).
/// </summary>
public sealed class LiveIndicator : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr h, uint a);

    private readonly TextBlock _text = new() { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    public LiveIndicator()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight; Focusable = false;

        var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = Brushes.White, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(800)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot); row.Children.Add(_text);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xD9, 0x2D, 0x2D)), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(11, 4, 12, 4), Margin = new Thickness(6), Child = row,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.5 },
        };
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            SetWindowDisplayAffinity(h, WDA_EXCLUDEFROMCAPTURE); // Win10 2004+; falha silenciosa em versões antigas
        };
        SizeChanged += (_, _) => Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - ActualWidth) / 2;
        Left = SystemParameters.WorkArea.Left; Top = SystemParameters.WorkArea.Top;
    }

    public void Set(bool on, int viewers)
    {
        _text.Text = viewers > 0 ? $"AO VIVO · {viewers}" : "AO VIVO";
        if (on && !IsVisible) Show();
        else if (!on && IsVisible) Hide();
    }
}
