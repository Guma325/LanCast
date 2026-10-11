using System.Windows;

namespace LanCast;

public partial class App : Application
{
    private Mutex? _single;

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] == "--apply-update")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (e.Args.Length == 2) await Updates.UpdateWorker.RunAsync(e.Args[1]);
            Shutdown();
            return;
        }
        _single = new Mutex(true, "LanCast.SingleInstance" + (Environment.GetEnvironmentVariable("LANCAST_DATA") is { } d ? "." + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(d)))[..8] : ""), out bool first);
        if (!first)
        {
            MessageBox.Show("O LanCast já está aberto (veja o ícone na bandeja).", "LanCast");
            Shutdown();
            return;
        }
        base.OnStartup(e);
        ThemeManager.Apply(StreamConfig.Load().Theme);
        new MainWindow().Show();
    }
}
