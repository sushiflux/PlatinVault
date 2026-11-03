using System.Runtime.InteropServices;

namespace PlatinVault.Windows.Ui;

/// <summary>
/// The custom frame of <see cref="ChromeWindow"/> as pure functions (unit-tested) plus the few Win32 calls behind them.
/// The window keeps the normal Windows frame (WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | min/max box) and only gives the caption's
/// height to the client area in WM_NCCALCSIZE: left, right and bottom keep the system's (invisible) resize borders - they lie outside
/// the client, so WebView2 below the bar never swallows them - and Aero Snap, the snap layouts, the system menu, the minimise/maximise
/// animations, the shadow and the rounded corners stay Windows' own. Same technique as Windows Terminal
/// (https://github.com/microsoft/terminal, NonClientIslandWindow.cpp) and the "Custom Window Frame Using DWM" article
/// (https://learn.microsoft.com/en-us/windows/win32/dwm/customframe).
/// </summary>
public static class WindowFrame
{
    public const int HtTransparent = -1;
    public const int HtClient = 1;
    public const int HtCaption = 2;
    public const int HtMinButton = 8;
    public const int HtMaxButton = 9;
    public const int HtTop = 12;
    public const int HtTopLeft = 13;
    public const int HtTopRight = 14;
    public const int HtClose = 20;

    /// <summary>Sides of a monitor with an auto-hiding taskbar.</summary>
    [Flags]
    public enum Edges
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
    }

    /// <summary>Gap left free at a side with an auto-hiding taskbar, so the mouse can still reveal it over a maximised window.</summary>
    public const int AutohideGap = 2;

    /// <summary>
    /// What a point of the title bar (client coordinates of the window, the bar starts at 0,0) is: the top resize edge (only when not
    /// maximised; above the three commands only up to their visible square), a window command, or the drag area (HTCAPTION).
    /// Points below the bar are client area (the page).
    /// </summary>
    public static int HitTest(Point client, CaptionLayout layout, int resizeBorder, bool maximized)
    {
        if (client.Y < 0 || client.Y >= layout.Height || client.X < 0 || client.X >= layout.Width) return HtClient;
        var part = layout.PartAt(client);
        if (!maximized)
        {
            var band = part == CaptionPart.None ? resizeBorder : Math.Min(resizeBorder, layout.PillTop);
            if (client.Y < band)
            {
                var corner = resizeBorder * 2;
                if (client.X < corner) return HtTopLeft;
                if (client.X >= layout.Width - corner) return HtTopRight;
                return HtTop;
            }
        }
        return part switch
        {
            CaptionPart.Minimize => HtMinButton,
            CaptionPart.Maximize => HtMaxButton,
            CaptionPart.Close => HtClose,
            _ => HtCaption,
        };
    }

    public static CaptionPart PartOf(int hitCode) => hitCode switch
    {
        HtMinButton => CaptionPart.Minimize,
        HtMaxButton => CaptionPart.Maximize,
        HtClose => CaptionPart.Close,
        _ => CaptionPart.None,
    };

    /// <summary>
    /// The client rectangle without the caption: Windows' standard client (<paramref name="standardClient"/>, frame on every side) with
    /// the top moved back to the window's top. A maximised window hangs over the monitor by its resize border on every side; the top is
    /// moved down by that border so nothing is cut off (left/right/bottom are already right), and a side with an auto-hiding taskbar
    /// keeps a small gap.
    /// </summary>
    public static Rectangle ClientArea(Rectangle standardClient, int windowTop, bool maximized, int resizeBorder, Edges autohideTaskbars)
    {
        int left = standardClient.Left, top = windowTop, right = standardClient.Right, bottom = standardClient.Bottom;
        if (maximized)
        {
            top += resizeBorder;
            if (autohideTaskbars.HasFlag(Edges.Top)) top += AutohideGap;
            if (autohideTaskbars.HasFlag(Edges.Bottom)) bottom -= AutohideGap;
            if (autohideTaskbars.HasFlag(Edges.Left)) left += AutohideGap;
            if (autohideTaskbars.HasFlag(Edges.Right)) right -= AutohideGap;
        }
        return Rectangle.FromLTRB(left, top, right, Math.Max(top, bottom));
    }

    /// <summary>Height of the top resize band (and of the overhang of a maximised window) at the window's DPI.</summary>
    public static int ResizeBorder(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            if (dpi == 0) dpi = 96;
            return GetSystemMetricsForDpi(SmCxPaddedBorder, dpi) + GetSystemMetricsForDpi(SmCySizeFrame, dpi);
        }
        catch (EntryPointNotFoundException)
        {
            return SystemInformation.FrameBorderSize.Height + SystemInformation.BorderSize.Height * 2;
        }
    }

    /// <summary>Sides of the window's monitor that carry an auto-hiding taskbar (none when auto-hide is off).</summary>
    public static Edges AutohideTaskbars(IntPtr hwnd)
    {
        var state = new AppBarData { cbSize = Marshal.SizeOf<AppBarData>() };
        if (((long)SHAppBarMessage(AbmGetState, ref state) & AbsAutohide) == 0) return Edges.None;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return Edges.None;
        var edges = Edges.None;
        foreach (var (edge, flag) in new[] { (AbeLeft, Edges.Left), (AbeTop, Edges.Top), (AbeRight, Edges.Right), (AbeBottom, Edges.Bottom) })
        {
            var data = new AppBarData { cbSize = Marshal.SizeOf<AppBarData>(), uEdge = edge, rc = info.rcMonitor };
            if (SHAppBarMessage(AbmGetAutohideBarEx, ref data) != IntPtr.Zero) edges |= flag;
        }
        return edges;
    }

    private const int SmCySizeFrame = 33;
    private const int SmCxPaddedBorder = 92;
    private const uint AbmGetState = 0x4;
    private const uint AbmGetAutohideBarEx = 0xb;
    private const long AbsAutohide = 0x1;
    private const uint AbeLeft = 0, AbeTop = 1, AbeRight = 2, AbeBottom = 3;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left, Top, Right, Bottom;

        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);

        public static NativeRect From(Rectangle r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public NativeRect rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}

/// <summary>
/// DPI of a monitor (or of a point/rectangle on it) for windows that size themselves before their handle exists. In a per-monitor
/// aware process this is the monitor's own scaling; in a system-aware process (the installer) Windows answers with the system DPI, so
/// the same code gives the same result there as before (GetDpiForMonitor, learn.microsoft.com).
/// </summary>
public static class ScreenDpi
{
    public static float ScaleAt(Point screenPoint) => ScaleOfMonitor(MonitorFromPoint(screenPoint, 2));

    public static float ScaleOf(Rectangle screenRect)
    {
        var rect = WindowFrame.NativeRect.From(screenRect);
        return ScaleOfMonitor(MonitorFromRect(ref rect, 2));
    }

    public static float ScaleOfWindow(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi == 0 ? 1f : dpi / 96f;
        }
        catch (EntryPointNotFoundException)
        {
            return 1f;
        }
    }

    private static float ScaleOfMonitor(IntPtr monitor)
    {
        try
        {
            return monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96f : 1f;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 1f;
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref WindowFrame.NativeRect rect, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
