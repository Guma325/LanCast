namespace VideoStreaming.Video;

public enum CapKind { Dda, Wgc, Gdi }

/// <summary>Argumentos de entrada do ffmpeg para a fonte escolhida (tela ou janela).</summary>
public sealed record CaptureInput(string InputArgs, bool HwFrames, bool NeedsCfr, string Description);

public static class CaptureArgs
{
    public static string KindName(CapKind k) => k switch
    {
        CapKind.Dda => "captura DXGI",
        CapKind.Wgc => "captura Windows",
        _ => "captura GDI",
    };

    /// <summary>Métodos de captura em ordem de preferência para o tipo de fonte.</summary>
    public static CapKind[] Kinds(StreamConfig cfg) => cfg.SourceType == "window"
        ? new[] { CapKind.Wgc }                       // GDI sai preta em janelas aceleradas por GPU; DXGI não captura janelas
        : new[] { CapKind.Dda, CapKind.Wgc, CapKind.Gdi };

    /// <summary>
    /// Monta a entrada. Retorna null com 'error' quando a fonte não existe (janela fechada) ou o método não se aplica.
    /// cpuFrames: true quando o consumidor precisa de quadros em memória (acrescenta hwdownload).
    /// </summary>
    public static CaptureInput? Build(StreamConfig cfg, CapKind kind, int fps, bool cpuFrames, out string? error)
    {
        error = null;
        string mouse = cfg.ShowCursor ? "1" : "0";
        string down = cpuFrames ? ",hwdownload,format=bgra" : "";

        if (cfg.SourceType == "window")
        {
            var w = ScreenSources.ResolveWindow(cfg.WindowExe, cfg.WindowTitle);
            if (w == null) { error = $"janela \"{cfg.WindowTitle}\" ({cfg.WindowExe}) não encontrada"; return null; }
            if (w.Minimized) { error = $"a janela \"{w.Title}\" está minimizada"; return null; }
            if (kind != CapKind.Wgc) return null;
            return new CaptureInput(
                $"-f lavfi -i \"gfxcapture=hwnd={w.Hwnd.ToInt64()}:max_framerate={fps}:capture_cursor={mouse}:resize_mode=scale_aspect:width=-2:height=-2{down}\"",
                HwFrames: !cpuFrames, NeedsCfr: true, $"janela {w.Exe}");
        }

        var m = ScreenSources.ResolveMonitor(cfg.MonitorDevice, cfg.Monitor);
        if (m == null) { error = "nenhum monitor encontrado"; return null; }
        switch (kind)
        {
            case CapKind.Dda:
                return new CaptureInput(
                    $"-f lavfi -i \"ddagrab=output_idx={m.Index}:framerate={fps}:draw_mouse={mouse}{down}\"",
                    HwFrames: !cpuFrames, NeedsCfr: false, m.Label);
            case CapKind.Wgc:
                return new CaptureInput(
                    $"-f lavfi -i \"gfxcapture=hmonitor={m.Handle.ToInt64()}:max_framerate={fps}:capture_cursor={mouse}:resize_mode=scale_aspect:width=-2:height=-2{down}\"",
                    HwFrames: !cpuFrames, NeedsCfr: true, m.Label);
            default:
                if (!cpuFrames) return null;          // GDI sempre entrega quadros em memória
                return new CaptureInput(
                    $"-f gdigrab -framerate {fps} -draw_mouse {mouse} -offset_x {m.Left} -offset_y {m.Top} -video_size {m.Width}x{m.Height} -i desktop",
                    HwFrames: false, NeedsCfr: false, m.Label);
        }
    }
}
