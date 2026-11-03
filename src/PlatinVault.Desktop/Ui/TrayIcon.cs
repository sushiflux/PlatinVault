using System.Runtime.InteropServices;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Desktop.Ui;

/// <summary>
/// The icon next to the clock, written directly against Shell_NotifyIcon (WinForms' NotifyIcon cannot set a custom balloon icon or
/// switch the notification sound off). Notifications use the trophy as their icon and are silent (NIIF_NOSOUND) - PlatinVault plays its
/// own trophy sound - and are skipped during Windows' "quiet time" (NIIF_RESPECT_QUIET_TIME). On Windows 10/11 they appear as Windows
/// notifications and land in the notification centre. Reference: NOTIFYICONDATAW (learn.microsoft.com, shellapi.h).
/// </summary>
internal sealed class TrayIcon : NativeWindow, IDisposable
{
    private const int CallbackMessage = 0x8000 + 77; // WM_APP + 77
    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    private const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4, NifInfo = 0x10, NifShowTip = 0x80;
    private const uint NiifUser = 0x4, NiifNoSound = 0x10, NiifLargeIcon = 0x20, NiifRespectQuietTime = 0x80;
    private const int WmLButtonUp = 0x0202, WmRButtonUp = 0x0205, WmContextMenu = 0x007B, NinSelect = 0x0400, NinKeySelect = 0x0401, NinBalloonUserClick = 0x0405;
    private static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    private readonly Icon _icon;
    private readonly Icon _balloonIcon;
    private bool _added;

    public TrayIcon()
    {
        CreateHandle(new CreateParams { Caption = "PlatinVault tray" });
        _icon = AppIcons.GetIcon(SystemInformation.SmallIconSize.Width);
        _balloonIcon = AppIcons.GetIcon(SystemInformation.IconSize.Width);
    }

    public ContextMenuStrip? Menu { get; set; }

    public event Action? Clicked;

    public event Action? BalloonClicked;

    public string Tooltip { get; set; } = "PlatinVault";

    public void Show()
    {
        var data = NewData(NifMessage | NifIcon | NifTip | NifShowTip);
        _added = ShellNotifyIcon(NimAdd, ref data);
        data.uVersion = 4; // NOTIFYICON_VERSION_4
        ShellNotifyIcon(NimSetVersion, ref data);
    }

    public void UpdateTooltip(string text)
    {
        Tooltip = text;
        if (!_added) return;
        var data = NewData(NifTip | NifShowTip);
        ShellNotifyIcon(NimModify, ref data);
    }

    /// <summary>A Windows notification with the trophy icon, without a system sound.</summary>
    public void Notify(string title, string text)
    {
        if (!_added) return;
        var data = NewData(NifInfo);
        data.szInfoTitle = Clip(title, 63);
        data.szInfo = Clip(text, 255);
        data.dwInfoFlags = NiifUser | NiifLargeIcon | NiifNoSound | NiifRespectQuietTime;
        data.hBalloonIcon = _balloonIcon.Handle;
        ShellNotifyIcon(NimModify, ref data);
    }

    // The shell limits these fields; a longer text is shortened there (the overlay and the app show it completely)
    private static string Clip(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";

    private NotifyIconData NewData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon.Handle,
        szTip = Clip(Tooltip, 127),
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == CallbackMessage)
        {
            var evt = (int)((long)m.LParam & 0xFFFF);
            switch (evt)
            {
                case WmLButtonUp:
                case NinSelect:
                case NinKeySelect:
                    Clicked?.Invoke();
                    break;
                case WmRButtonUp:
                case WmContextMenu:
                    ShowMenu();
                    break;
                case NinBalloonUserClick:
                    BalloonClicked?.Invoke();
                    break;
            }
            return;
        }
        if (m.Msg == TaskbarCreated && _added)
        {
            // Explorer restarted: the icon has to be added again
            Show();
        }
        base.WndProc(ref m);
    }

    private void ShowMenu()
    {
        if (Menu is null) return;
        // Without this the menu would not close when clicking elsewhere (documented Shell_NotifyIcon behaviour)
        SetForegroundWindow(Handle);
        Menu.Show(Cursor.Position);
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData(0);
            ShellNotifyIcon(NimDelete, ref data);
            _added = false;
        }
        _icon.Dispose();
        _balloonIcon.Dispose();
        DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}

/// <summary>Tray menu in the PlatinVault colours (dark surface, crystal highlight) instead of the grey system menu.</summary>
internal sealed class TrayMenuRenderer() : ToolStripProfessionalRenderer(new TrayColors())
{
    private static readonly Palette P = Palette.Dark;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? P.Text : P.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // On/off entries: green check = on (the "off" state shows no mark, the entry text says what is switched)
        var g = e.Graphics;
        Draw.Smooth(g);
        var r = new RectangleF(e.ImageRectangle.X, e.ImageRectangle.Y, e.ImageRectangle.Width, e.ImageRectangle.Height);
        Draw.FillRounded(g, r, 3, P.ToggleOn);
        Draw.Check(g, RectangleF.Inflate(r, -3, -3), P.Knob, 2f);
    }

    private sealed class TrayColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => P.Surface;
        public override Color ImageMarginGradientBegin => P.Surface;
        public override Color ImageMarginGradientMiddle => P.Surface;
        public override Color ImageMarginGradientEnd => P.Surface;
        public override Color MenuBorder => P.Border;
        public override Color MenuItemBorder => P.Accent;
        public override Color MenuItemSelected => P.Elevated;
        public override Color MenuItemSelectedGradientBegin => P.Elevated;
        public override Color MenuItemSelectedGradientEnd => P.Elevated;
        public override Color SeparatorDark => P.Border;
        public override Color SeparatorLight => P.Surface;
        public override Color CheckBackground => P.Surface;
        public override Color CheckSelectedBackground => P.Elevated;
        public override Color CheckPressedBackground => P.Elevated;
    }
}
