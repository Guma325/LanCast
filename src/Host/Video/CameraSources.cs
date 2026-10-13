using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LanCast.Video;

/// <summary>Lista as câmeras (DirectShow) que o ffmpeg embutido consegue abrir, inclusive câmeras virtuais (OBS, DroidCam...).</summary>
public static class CameraSources
{
    private static readonly Regex VideoDevice = new("\"([^\"]+)\"\\s+\\(video\\)", RegexOptions.Compiled);

    public static async Task<List<string>> ListAsync()
    {
        var names = new List<string>();
        try
        {
            var ffmpeg = Paths.EnsureFfmpeg();
            using var p = Process.Start(new ProcessStartInfo(ffmpeg, "-hide_banner -list_devices true -f dshow -i dummy")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            })!;
            ProcessJob.Assign(p);
            var read = p.StandardError.ReadToEndAsync();
            if (await Task.WhenAny(read, Task.Delay(8000)) != read) { try { p.Kill(true); } catch { } return names; }
            foreach (Match m in VideoDevice.Matches(await read))
                if (!names.Contains(m.Groups[1].Value)) names.Add(m.Groups[1].Value);
        }
        catch (Exception ex) { AppLog.Info("[camera] listar câmeras: " + ex.Message); }
        return names;
    }

    /// <summary>Resolução e FPS pedidos à câmera. Automático = 720p a 30 FPS (o encoder recua para o modo padrão se a câmera não aceitar).</summary>
    public static (int Height, int Fps) Resolve(StreamConfig cfg) =>
        (cfg.CameraHeight > 0 ? cfg.CameraHeight : 720, cfg.CameraFps > 0 ? cfg.CameraFps : 30);

    public static int WidthFor(int height) => height switch { 480 => 640, 720 => 1280, 1080 => 1920, _ => height * 16 / 9 / 2 * 2 };
}
