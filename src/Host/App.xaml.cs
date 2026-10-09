using System.Windows;

namespace LanCast;

public partial class App : Application
{
    private Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        _single = new Mutex(true, "LanCast.SingleInstance" + (Environment.GetEnvironmentVariable("LANCAST_DATA") is { } d ? "." + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(d)))[..8] : ""), out bool first);
        if (!first)
        {
            MessageBox.Show("O LanCast já está aberto (veja o ícone na bandeja).", "LanCast");
            Shutdown();
            return;
        }
        base.OnStartup(e);
        new MainWindow().Show();
    }
}
