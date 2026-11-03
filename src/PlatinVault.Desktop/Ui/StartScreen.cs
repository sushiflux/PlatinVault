using PlatinVault.Windows.Localization;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Desktop.Ui;

/// <summary>
/// What the program window shows until the UI is loaded: the night-blue brand surface with the trophy, the name, what happens now and a
/// progress bar - or, when the start failed, the complete reason (scrolls when long) with "Open logs" and "Quit". Fixed layout around
/// the centre: the message slot has its size from the start, nothing moves when an error appears.
/// </summary>
internal sealed class StartScreen : UserControl
{
    private const int BlockWidth = 560;
    private readonly PvProgress _bar = new() { Running = true };
    private readonly PvScrollText _error = new() { Framed = true, Visible = false, Name = "txtStartError" };
    private readonly PvButton _logs = new() { Kind = PvButtonKind.Secondary, Visible = false, Name = "btnLogs" };
    private readonly PvButton _quit = new() { Kind = PvButtonKind.Primary, Visible = false, Name = "btnQuit" };
    private string _status = DesktopTexts.Window.Starting;
    private string? _title;
    private string? _hint;

    public StartScreen()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Controls.AddRange([_bar, _error, _logs, _quit]);
        _logs.Click += (_, _) => OpenLogsRequested?.Invoke();
        _quit.Click += (_, _) => QuitRequested?.Invoke();
        Resize += (_, _) => Layout2();
        ApplyLook();
    }

    /// <summary>Pixels per design unit; changing it (another monitor DPI) lays the screen out again.</summary>
    public float S
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Layout2();
            Invalidate();
        }
    } = 1f;

    public event Action? QuitRequested;

    public event Action? OpenLogsRequested;

    private int Px(float v) => (int)Math.Round(v * S);

    private void ApplyLook()
    {
        // The start surface is always the dark brand surface: the controls on it use the dark palette
        foreach (var control in new PvControl[] { _bar, _error, _logs, _quit })
        {
            control.P = Palette.Dark;
            control.Surface = Brand.NightBottom;
        }
        _logs.Text = DesktopTexts.Window.OpenLogs;
        _quit.Text = DesktopTexts.Window.Quit;
    }

    public void SetProgress(float value, string text)
    {
        _status = text;
        _bar.Value = value;
        Invalidate();
    }

    public void SetError(Msg message, string? detail, string logFolder)
    {
        _title = DesktopTexts.Window.FailedTitle;
        _hint = DesktopTexts.Window.FailedLogHint(logFolder);
        _bar.Visible = false;
        _bar.Running = false;
        var text = message.ToString();
        if (!string.IsNullOrWhiteSpace(detail)) text += Environment.NewLine + Environment.NewLine + detail;
        _error.SetText(text, p => p.Text);
        _error.Visible = _logs.Visible = _quit.Visible = true;
        Layout2();
        Invalidate();
    }

    private Rectangle Block => new((Width - Px(BlockWidth)) / 2, Math.Max(Px(40), Height / 2 - Px(200)), Px(BlockWidth), Px(400));

    private void Layout2()
    {
        foreach (var control in new PvControl[] { _bar, _error, _logs, _quit }) control.S = S;
        var b = Block;
        _bar.Bounds = new Rectangle(b.Left, b.Top + Px(200), b.Width, Px(8));
        _error.Bounds = new Rectangle(b.Left, b.Top + Px(150), b.Width, Px(150));
        _quit.Bounds = new Rectangle(b.Right - Px(160), b.Top + Px(318), Px(160), Px(42));
        _logs.Bounds = new Rectangle(b.Right - Px(160 + 12 + 190), b.Top + Px(318), Px(190), Px(42));
        _error.Relayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Smooth(g);
        // Painted as if it started at the top of the window: the title bar above paints the same surface (BrandMode), no seam
        Brand.PaintNight(g, new Rectangle(0, -Top, Width, Height + Top));
        var b = Block;
        Brand.PaintGlow(g, new RectangleF(b.Left + b.Width / 2f - Px(110), b.Top - Px(40), Px(220), Px(200)), Brand.Crystal, 70);
        using (var logo = AppIcons.GetBitmap(Px(84))) g.DrawImage(logo, b.Left + (b.Width - Px(84)) / 2, b.Top, Px(84), Px(84));
        var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
        TextRenderer.DrawText(g, _title ?? "PlatinVault", Fonts.Get(_title is null ? 22f : 17f, S, FontStyle.Bold, display: true),
            new Rectangle(b.Left, b.Top + Px(98), b.Width, Px(40)), Brand.Title, center);
        if (_title is null)
        {
            TextRenderer.DrawText(g, _status, Fonts.Get(10.5f, S), new Rectangle(b.Left, b.Top + Px(150), b.Width, Px(40)), Brand.Body, center);
        }
        else if (_hint is not null)
        {
            // Below the buttons on its own line; the folder is data, a long path is shortened in the middle (Open logs opens it anyway)
            TextRenderer.DrawText(g, _hint, Fonts.Get(8.5f, S), new Rectangle(b.Left, b.Top + Px(372), b.Width, Px(20)), Brand.Faint,
                TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.Left);
        }
    }
}
