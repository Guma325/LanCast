using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LanCast.Video;

public sealed record MonitorInfo(int Index, string Device, IntPtr Handle, int Left, int Top, int Width, int Height, bool Primary)
{
    public string Label => $"Tela {Index + 1}{(Primary ? " (principal)" : "")}";
    public string Detail => $"{Width}×{Height}";
}

public sealed record WindowInfo(IntPtr Hwnd, string Title, string Exe, int Pid, int Width, int Height, bool Minimized);

/// <summary>Lista monitores e janelas que podem ser compartilhados.</summary>
public static class ScreenSources
{
    // ---------- monitores ----------

    public static List<MonitorInfo> ListMonitors()
    {
        var raw = new List<(string dev, IntPtr h, RECT r, bool prim)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMon, ref mi))
                raw.Add((mi.szDevice, hMon, mi.rcMonitor, (mi.dwFlags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return raw.OrderBy(m => m.dev, StringComparer.OrdinalIgnoreCase)
                  .Select((m, i) => new MonitorInfo(i, m.dev, m.h, m.r.Left, m.r.Top, m.r.Right - m.r.Left, m.r.Bottom - m.r.Top, m.prim))
                  .ToList();
    }

    /// <summary>Acha o monitor salvo (por nome do dispositivo, senão por índice, senão o principal).</summary>
    public static MonitorInfo? ResolveMonitor(string device, int index)
    {
        var l = ListMonitors();
        return l.FirstOrDefault(m => m.Device.Equals(device, StringComparison.OrdinalIgnoreCase))
            ?? l.FirstOrDefault(m => m.Index == index)
            ?? l.FirstOrDefault(m => m.Primary) ?? l.FirstOrDefault();
    }

    // ---------- janelas ----------

    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    { "Program Manager", "Windows Input Experience", "Experiência de Entrada do Windows", "Configurações do Sistema", "Microsoft Text Input Application" };

    public static List<WindowInfo> ListWindows()
    {
        var list = new List<WindowInfo>();
        int self = Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4 /*GW_OWNER*/) != IntPtr.Zero) return true;
                long ex = GetWindowLongPtr(hwnd, -20).ToInt64();
                if ((ex & 0x80) != 0 && (ex & 0x40000) == 0) return true;          // WS_EX_TOOLWINDOW sem APPWINDOW
                if (DwmGetWindowAttribute(hwnd, 14 /*CLOAKED*/, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                int len = GetWindowTextLength(hwnd);
                if (len == 0) return true;
                var sb = new StringBuilder(len + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                var title = sb.ToString();
                if (Ignored.Contains(title)) return true;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == self) return true;
                bool min = IsIconic(hwnd);
                GetWindowRect(hwnd, out var r);
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                if (!min && (w < 50 || h < 50)) return true;
                string exe;
                try { using var p = Process.GetProcessById((int)pid); exe = p.ProcessName; } catch { return true; }
                list.Add(new WindowInfo(hwnd, title, exe, (int)pid, w, h, min));
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Acha a janela salva: mesmo programa + mesmo título; senão mesmo programa (a maior janela).</summary>
    public static WindowInfo? ResolveWindow(string exe, string title)
    {
        if (string.IsNullOrEmpty(exe)) return null;
        var same = ListWindows().Where(w => w.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase)).ToList();
        return same.FirstOrDefault(w => w.Title == title)
            ?? same.FirstOrDefault(w => title.Length > 0 && (w.Title.Contains(title, StringComparison.OrdinalIgnoreCase) || title.Contains(w.Title, StringComparison.OrdinalIgnoreCase)))
            ?? same.OrderByDescending(w => (long)w.Width * w.Height).FirstOrDefault();
    }

    // ---------- Win32 ----------

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFOEX mi);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int idx);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int val, int size);
}
