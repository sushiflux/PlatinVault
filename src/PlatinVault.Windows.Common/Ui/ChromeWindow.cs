using System.Runtime.InteropServices;
using PlatinVault.Windows.Localization;

namespace PlatinVault.Windows.Ui;

/// <summary>
/// A resizable window with PlatinVault's own title bar (<see cref="PvTitleBar"/>) that still behaves like every Windows window: drag
/// to move, double-click to maximise/restore, Aero Snap, the Windows 11 snap layouts on the maximise button, resizing from every edge and
/// corner, the system menu (right-click on the bar, Alt+Space), Win+arrow keys, the minimise/maximise animations, the shadow and the
/// rounded corners. How (details in <see cref="WindowFrame"/>): the normal frame stays, WM_NCCALCSIZE hands the caption's height to
/// the client, WM_NCHITTEST answers for the bar (HTTOP*, HTCAPTION, HTMINBUTTON/HTMAXBUTTON/HTCLOSE) and the non-client mouse messages
/// of the three commands are handled here, so Windows never draws its own buttons. Content is added with <see cref="AddContent"/>
/// below the bar. Scale (<see cref="S"/>) follows the monitor the window is on (per-monitor DPI), palette follows <see cref="ThemeMode"/>.
/// </summary>
public class ChromeWindow : Form
{
    private bool _trackingNcMouse;

    public ChromeWindow()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.Sizable;
        SizeGripStyle = SizeGripStyle.Hide;
        DoubleBuffered = true;
        P = ThemeMode.Current;
        BackColor = P.Page;
        TitleBar = new PvTitleBar { Dock = DockStyle.Top, Name = "titleBar", P = P };
        TitleBar.Command += OnCaptionCommand;
        Controls.Add(TitleBar);
        ThemeMode.Changed += OnThemeModeChanged;
        UiLang.Changed += OnLanguageChanged;
    }

    public PvTitleBar TitleBar { get; }

    /// <summary>Pixels per design unit on the monitor the window is on (1.0 = 100 %).</summary>
    public float S { get; private set; } = 1f;

    public Palette P { get; private set; }

    /// <summary>Adds a control that fills the window below the title bar (docking keeps the bar on top, never overlapping).</summary>
    protected void AddContent(Control control)
    {
        control.Dock = DockStyle.Fill;
        Controls.Add(control);
        // Docking runs from the back of the z-order to the front: the bar (further back) takes the top first, the content fills the rest
        control.BringToFront();
    }

    /// <summary>Sets the scale (before the handle exists: the scale of the monitor the window will open on).</summary>
    protected void ApplyScale(float scale)
    {
        if (scale <= 0) return;
        S = scale;
        TitleBar.S = scale;
        OnScaleChanged();
    }

    /// <summary>The scale changed (first handle, or the window moved to a monitor with another DPI): lay out scaled parts again.</summary>
    protected virtual void OnScaleChanged() { }

    /// <summary>The palette changed (light/dark).</summary>
    protected virtual void OnPaletteChanged() { }

    private void OnThemeModeChanged()
    {
        P = ThemeMode.Current;
        BackColor = P.Page;
        TitleBar.P = P;
        if (IsHandleCreated) ApplyDwmAttributes();
        OnPaletteChanged();
        Invalidate();
    }

    // The badge and the names for screen readers are read when painted/asked: repaint in the new language
    private void OnLanguageChanged()
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(new Action(TitleBar.Invalidate));
        else TitleBar.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeMode.Changed -= OnThemeModeChanged;
            UiLang.Changed -= OnLanguageChanged;
        }
        base.Dispose(disposing);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyScale(DeviceDpi / 96f);
        ApplyDwmAttributes();
        // The new client size only applies with the next WM_NCCALCSIZE: ask for it now, before the window is shown
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SwpFrameChanged | SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // The base applies Windows' suggested rectangle for the new monitor; the bar and the content are scaled here
        base.OnDpiChanged(e);
        if (!e.Cancel) ApplyScale(e.DeviceDpiNew / 96f);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        TitleBar.Maximized = IsHandleCreated ? IsZoomed(Handle) : WindowState == FormWindowState.Maximized;
    }

    private void ApplyDwmAttributes()
    {
        try
        {
            // Windows 11: rounded corners (and the system menu/border in the app's light or dark mode)
            var round = DwmwcpRound;
            _ = DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
            var dark = P.IsDark ? 1 : 0;
            _ = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows: square corners
        }
    }

    // ---- window messages ------------------------------------------------------------------------------------------------------------

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmNcCalcSize when m.WParam != IntPtr.Zero:
                NcCalcSize(ref m);
                return;

            case WmNcHitTest:
                base.WndProc(ref m);
                // Left, right and bottom are the system's own resize borders (outside the client); the bar is answered here
                if ((int)m.Result == WindowFrame.HtClient)
                {
                    var point = PointToClient(ScreenPoint(m.LParam));
                    m.Result = WindowFrame.HitTest(point, TitleBar.Geometry, WindowFrame.ResizeBorder(Handle), IsZoomed(Handle));
                }
                return;

            case WmNcMouseMove:
                TitleBar.SetHover(WindowFrame.PartOf((int)m.WParam));
                if (!_trackingNcMouse && WindowFrame.PartOf((int)m.WParam) != CaptionPart.None)
                {
                    // TME_NONCLIENT: otherwise the leave message does not come when the mouse leaves the button quickly
                    var track = new TrackMouseEventData { cbSize = Marshal.SizeOf<TrackMouseEventData>(), dwFlags = TmeLeave | TmeNonClient, hwndTrack = Handle };
                    _trackingNcMouse = TrackMouseEvent(ref track);
                }
                base.WndProc(ref m);
                return;

            case WmNcMouseLeave:
                _trackingNcMouse = false;
                TitleBar.ResetStates();
                base.WndProc(ref m);
                return;

            case WmNcLButtonDown:
            case WmNcLButtonDblClk:
                if (WindowFrame.PartOf((int)m.WParam) is var down and not CaptionPart.None)
                {
                    // Never to DefWindowProc: it would run its own button loop and paint the classic caption buttons
                    TitleBar.SetHover(down);
                    TitleBar.SetPressed(down);
                    m.Result = IntPtr.Zero;
                    return;
                }
                break;

            case WmNcLButtonUp:
                if (WindowFrame.PartOf((int)m.WParam) is var up and not CaptionPart.None)
                {
                    var clicked = TitleBar.PressedPart == up;
                    TitleBar.SetPressed(CaptionPart.None);
                    if (clicked) TitleBar.Execute(up);
                    m.Result = IntPtr.Zero;
                    return;
                }
                break;

            case WmNcRButtonUp when (int)m.WParam is WindowFrame.HtCaption or WindowFrame.HtMinButton or WindowFrame.HtMaxButton or WindowFrame.HtClose:
                // The system menu (Restore, Move, Size, Minimise, Maximise, Close) like on every Windows caption
                OpenSystemMenu(ScreenPoint(m.LParam));
                m.Result = IntPtr.Zero;
                return;

            case WmNcActivate:
                TitleBar.Active = m.WParam != IntPtr.Zero;
                break;
        }
        base.WndProc(ref m);
    }

    private void NcCalcSize(ref Message m)
    {
        var calc = Marshal.PtrToStructure<NcCalcSizeParams>(m.LParam);
        var windowTop = calc.rgrc0.Top;
        base.WndProc(ref m); // the standard frame on every side
        if (m.Result != IntPtr.Zero) return;
        calc = Marshal.PtrToStructure<NcCalcSizeParams>(m.LParam);
        var maximized = IsZoomed(Handle);
        var client = WindowFrame.ClientArea(calc.rgrc0.ToRectangle(), windowTop, maximized, WindowFrame.ResizeBorder(Handle),
            maximized ? WindowFrame.AutohideTaskbars(Handle) : WindowFrame.Edges.None);
        calc.rgrc0 = WindowFrame.NativeRect.From(client);
        Marshal.StructureToPtr(calc, m.LParam, false);
        m.Result = IntPtr.Zero;
    }

    private void OnCaptionCommand(CaptionPart part)
    {
        var command = part switch
        {
            CaptionPart.Minimize => ScMinimize,
            CaptionPart.Maximize => IsHandleCreated && IsZoomed(Handle) ? ScRestore : ScMaximize,
            CaptionPart.Close => ScClose,
            _ => 0,
        };
        // The same path as Windows' own caption buttons (animations; closing raises FormClosing with UserClosing)
        if (command != 0 && IsHandleCreated) SendMessage(Handle, WmSysCommand, (IntPtr)command, IntPtr.Zero);
    }

    /// <summary>Alt+Space opens the system menu even while the page (WebView2) has the keyboard focus.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Alt | Keys.Space) && IsHandleCreated)
        {
            OpenSystemMenu(TitleBar.PointToScreen(new Point(0, TitleBar.Height)));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Shows the window's system menu at a screen point, with the entries that fit the current state.</summary>
    public void OpenSystemMenu(Point screen)
    {
        var menu = GetSystemMenu(Handle, false);
        if (menu == IntPtr.Zero) return;
        var maximized = IsZoomed(Handle);
        void Enable(int item, bool on) => EnableMenuItem(menu, (uint)item, MfByCommand | (on ? MfEnabled : MfGrayed));
        Enable(ScRestore, maximized);
        Enable(ScMove, !maximized);
        Enable(ScSize, !maximized);
        Enable(ScMinimize, true);
        Enable(ScMaximize, !maximized);
        Enable(ScClose, true);
        var command = TrackPopupMenu(menu, TpmReturnCmd | TpmRightButton, screen.X, screen.Y, 0, Handle, IntPtr.Zero);
        if (command != 0) PostMessage(Handle, WmSysCommand, (IntPtr)command, IntPtr.Zero);
    }

    private static Point ScreenPoint(IntPtr lParam)
    {
        var value = (long)lParam;
        return new Point(unchecked((short)(value & 0xFFFF)), unchecked((short)((value >> 16) & 0xFFFF)));
    }

    // ---- native ---------------------------------------------------------------------------------------------------------------------

    private const int WmNcCalcSize = 0x0083;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcActivate = 0x0086;
    private const int WmNcMouseMove = 0x00A0;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmNcLButtonUp = 0x00A2;
    private const int WmNcLButtonDblClk = 0x00A3;
    private const int WmNcRButtonUp = 0x00A5;
    private const int WmNcMouseLeave = 0x02A2;
    private const int WmSysCommand = 0x0112;
    private const int ScSize = 0xF000, ScMove = 0xF010, ScMinimize = 0xF020, ScMaximize = 0xF030, ScClose = 0xF060, ScRestore = 0xF120;
    private const uint MfByCommand = 0x0, MfEnabled = 0x0, MfGrayed = 0x1;
    private const uint TpmRightButton = 0x0002, TpmReturnCmd = 0x0100;
    private const uint TmeLeave = 0x2, TmeNonClient = 0x10;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpFrameChanged = 0x20, SwpNoOwnerZOrder = 0x200;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NcCalcSizeParams
    {
        public WindowFrame.NativeRect rgrc0;
        public WindowFrame.NativeRect rgrc1;
        public WindowFrame.NativeRect rgrc2;
        public IntPtr lppos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventData
    {
        public int cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TrackMouseEventData data);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr hwnd, bool revert);
    [DllImport("user32.dll")] private static extern bool EnableMenuItem(IntPtr menu, uint item, uint flags);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
