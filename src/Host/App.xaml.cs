using System.Windows;

namespace VideoStreaming;

public partial class App : Application
{
    private Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        _single = new Mutex(true, "VideoStreaming.SingleInstance" + (Environment.GetEnvironmentVariable("VIDEOSTREAMING_DATA") is { } d ? "." + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(d)))[..8] : ""), out bool first);
        if (!first)
        {
            MessageBox.Show("O VideoStreaming já está aberto (veja o ícone na bandeja).", "VideoStreaming");
            Shutdown();
            return;
        }
        base.OnStartup(e);
        new MainWindow().Show();
    }
}
