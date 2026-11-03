using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using PlatinVault.Windows.Localization;

namespace PlatinVault.Windows.Ui;

/// <summary>One step of a <see cref="WizardWindow"/>: builds its controls into the content area (design units, origin = top left of the area).</summary>
public abstract class WizardPage
{
    public abstract Msg StepLabel { get; }

    public abstract Msg Kicker { get; }

    public abstract Msg Title { get; }

    public abstract Msg Subtitle { get; }

    /// <summary>Creates the controls of the page. Called again after a language or theme switch (the page keeps its own state).</summary>
    public abstract void Build(PageCanvas canvas);

    /// <summary>Called when the page became the visible one.</summary>
    public virtual void Shown() { }
}

/// <summary>What a page gets to place its controls: the scale, the palette and a helper that converts design units to pixels.</summary>
public sealed class PageCanvas(Control host, float scale, Palette palette)
{
    public const int Width = WizardWindow.ContentWidth;
    public const int Height = WizardWindow.ContentHeight;

    public float S { get; } = scale;

    public Palette P { get; } = palette;

    public Control Host { get; } = host;

    public Rectangle R(int x, int y, int width, int height) =>
        new((int)Math.Round(x * S), (int)Math.Round(y * S), (int)Math.Round(width * S), (int)Math.Round(height * S));

    /// <summary>Adds an owner-drawn control at a fixed place in design units and returns it.</summary>
    public T Add<T>(T control, int x, int y, int width, int height, string? name = null) where T : Control
    {
        control.Bounds = R(x, y, width, height);
        if (name is not null) control.Name = name;
        if (control is PvControl pv)
        {
            pv.S = S;
            pv.P = P;
            pv.Surface = P.Page;
        }
        Host.Controls.Add(control);
        // Later controls lie on top of earlier ones (a card first, then its label and button). WinForms does the opposite: the
        // first child added is the topmost, so a background card covered everything placed on it once it painted - the folder
        // and "Change" of the location step vanished right after the page appeared. DrawToBitmap (the offscreen screenshots)
        // paints children in reverse order and hid the bug, see WizardCanvasTests.
        control.BringToFront();
        return control;
    }

    public PvText Text(string text, int x, int y, int width, int height, float points = 10f, FontStyle style = FontStyle.Regular,
        Func<Palette, Color>? color = null, string? name = null)
    {
        var label = new PvText { Text = text, Points = points, Style = style };
        if (color is not null) label.Color = color;
        return Add(label, x, y, width, height, name);
    }
}

/// <summary>
/// The designed window of the installer and the uninstaller: no grey Windows wizard, but the PlatinVault look - a night-blue brand
/// panel on the left (logo, steps, version, "made with" line), a light or dark content area on the right with kicker, title and the
/// page, and an action bar. Borderless with its own title bar (drag, light/dark switch, minimise, close), rounded corners and the
/// system shadow on Windows 11. Pages change with a short slide-and-fade (off when Windows' animations are off). Every position is
/// a fixed design unit (96-dpi pixel) times the scale, so nothing ever moves when a message appears.
/// </summary>
public class WizardWindow : Form
{
    public const int WindowWidth = 920;
    public const int WindowHeight = 600;
    public const int SideWidth = 300;
    public const int HeaderHeight = 52;
    public const int FooterHeight = 76;
    public const int ContentLeft = SideWidth + 48;
    public const int ContentTop = HeaderHeight + 4;
    public const int ContentWidth = WindowWidth - ContentLeft - 48;
    // Room below the title block for the page itself
    public const int PageTop = 114;
    public const int NextWidth = 290;
    public const int ContentHeight = WindowHeight - ContentTop - FooterHeight - 12;

    private readonly List<WizardPage> _steps;
    private readonly Panel _content;
    private readonly PvText _kicker;
    private readonly PvText _title;
    private readonly PvText _subtitle;
    private readonly Panel _pageHost;
    private readonly TransitionLayer _transition;
    private readonly PvButton _themeButton;
    private readonly PvButton _minimizeButton;
    private readonly PvButton _closeButton;
    private readonly string _sideSubtitle;
    private readonly string _version;
    private int _index = -1;

    public WizardWindow(Msg windowTitle, Msg sideSubtitle, string version, IReadOnlyList<WizardPage> steps, float? forcedScale = null)
    {
        _steps = [.. steps];
        _sideSubtitle = sideSubtitle;
        _version = version;
        Headless = forcedScale is not null;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Text = windowTitle;
        Icon = AppIcons.GetIcon(32);
        ShowInTaskbar = !Headless;
        KeyPreview = true;
        DoubleBuffered = true;
        // The scale of the monitor the window opens on (CenterScreen = the monitor under the mouse); DeviceDpi is not known yet
        // before the handle exists. The layout is fixed in design units, so a later move to a monitor with another DPI keeps the size
        // (see OnDpiChanged) instead of shrinking the window around unscaled controls.
        S = forcedScale ?? ScreenDpi.ScaleAt(Cursor.Position);
        ClientSize = new Size(Px(WindowWidth), Px(WindowHeight));
        P = ThemeMode.Current;
        BackColor = P.Page;

        _content = new Panel { Bounds = new Rectangle(Px(ContentLeft), Px(ContentTop), Px(ContentWidth), Px(WindowHeight - ContentTop - FooterHeight)), BackColor = P.Page, Name = "content" };
        _kicker = new PvText { Kicker = true, Points = 8.5f, Style = FontStyle.Bold, Color = p => p.Gold, Name = "kicker" };
        _title = new PvText { Points = 21f, Style = FontStyle.Bold, Display = true, Name = "title" };
        _subtitle = new PvText { Points = 10.5f, Color = p => p.Muted, Name = "subtitle" };
        _pageHost = new Panel { BackColor = P.Page, Name = "page" };
        _transition = new TransitionLayer { Visible = false, Name = "transition" };
        PlaceInContent(_kicker, 0, 14, ContentWidth, 18);
        PlaceInContent(_title, 0, 32, ContentWidth, 42);
        // Two lines for the subtitle in every language and scale
        PlaceInContent(_subtitle, 0, 76, ContentWidth, 46);
        _pageHost.Bounds = new Rectangle(0, Px(PageTop + 14), Px(ContentWidth), _content.Height - Px(PageTop + 14));
        _transition.Bounds = _pageHost.Bounds;
        _content.Controls.AddRange([_transition, _kicker, _title, _subtitle, _pageHost]);

        // Same glyphs and the same hover/close-red as the program's title bar (CaptionGlyphs, CaptionColors)
        _themeButton = ChromeButton("btnTheme", PvButtonKind.WindowButton, (g, r, c) => (P.IsDark ? (Action<Graphics, RectangleF, Color>)CaptionGlyphs.Sun : CaptionGlyphs.Moon)(g, r, c));
        _minimizeButton = ChromeButton("btnMinimize", PvButtonKind.WindowButton, CaptionGlyphs.Minimize);
        _closeButton = ChromeButton("btnClose", PvButtonKind.WindowClose, CaptionGlyphs.Close);
        _themeButton.Bounds = new Rectangle(Px(WindowWidth - 132), Px(10), Px(36), Px(32));
        _minimizeButton.Bounds = new Rectangle(Px(WindowWidth - 92), Px(10), Px(36), Px(32));
        _closeButton.Bounds = new Rectangle(Px(WindowWidth - 52), Px(10), Px(36), Px(32));
        _themeButton.Click += (_, _) => { ThemeMode.Set(!P.IsDark); };
        _minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _closeButton.Click += (_, _) => OnCloseRequested();

        Back = new PvButton { Kind = PvButtonKind.Secondary, Name = "btnBack" };
        Next = new PvButton { Kind = PvButtonKind.Primary, Name = "btnNext" };
        Cancel = new PvButton { Kind = PvButtonKind.Ghost, Name = "btnCancel" };
        // The primary button is wide enough for the longest action label in German and English ("Delete everything and uninstall");
        // it never changes size between states, so nothing in the action bar moves. The layout check compares every label with its button.
        PlaceFooterButton(Cancel, ContentLeft - 12, 110);
        PlaceFooterButton(Back, WindowWidth - 48 - NextWidth - 12 - 110, 110);
        PlaceFooterButton(Next, WindowWidth - 48 - NextWidth, NextWidth);

        Controls.AddRange([_content, _themeButton, _minimizeButton, _closeButton, Back, Next, Cancel]);
        ApplyPalette();

        ThemeMode.Changed += OnThemeChanged;
        UiLang.Changed += OnLanguageChanged;
        FormClosed += (_, _) =>
        {
            ThemeMode.Changed -= OnThemeChanged;
            UiLang.Changed -= OnLanguageChanged;
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeMode.Changed -= OnThemeChanged;
            UiLang.Changed -= OnLanguageChanged;
        }
        base.Dispose(disposing);
    }

    /// <summary>True for an offscreen test rendering: never shown, no animation.</summary>
    public bool Headless { get; }

    public float S { get; }

    public Palette P { get; private set; }

    public PvButton Back { get; }

    public PvButton Next { get; }

    public PvButton Cancel { get; }

    public int StepIndex => _index;

    public WizardPage CurrentPage => _steps[_index];

    protected int Px(float designUnits) => (int)Math.Round(designUnits * S);

    private void PlaceInContent(Control control, int x, int y, int width, int height)
    {
        control.Bounds = new Rectangle(Px(x), Px(y), Px(width), Px(height));
        if (control is PvControl pv) pv.S = S;
    }

    private void PlaceFooterButton(PvButton button, int x, int width)
    {
        button.Bounds = new Rectangle(Px(x), Px(WindowHeight - FooterHeight + 18), Px(width), Px(42));
        button.S = S;
    }

    private PvButton ChromeButton(string name, PvButtonKind kind, Action<Graphics, RectangleF, Color> glyph)
    {
        var button = new PvButton { Kind = kind, Name = name, Glyph = glyph, TabStop = false, S = S };
        return button;
    }

    /// <summary>The close button and Esc: the default asks nothing and closes; installers override it.</summary>
    protected virtual void OnCloseRequested() => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape && Cancel.Visible && Cancel.Enabled)
        {
            e.Handled = true;
            Cancel.PerformClick();
        }
        else if (e.KeyCode == Keys.Enter && Next.Visible && Next.Enabled && ActiveControl is not PvButton && ActiveControl is not PvScrollText)
        {
            e.Handled = true;
            Next.PerformClick();
        }
        base.OnKeyDown(e);
    }

    /// <summary>Shows step <paramref name="index"/> (with the slide-and-fade unless headless or animations are off).</summary>
    public void GoTo(int index)
    {
        if (index < 0 || index >= _steps.Count) return;
        var animate = _index >= 0 && !Headless && SystemInformation.UIEffectsEnabled && IsHandleCreated && Visible;
        var direction = index > _index ? 1 : -1;
        Bitmap? before = animate ? Snapshot(_pageHost) : null;

        _index = index;
        RebuildPage();
        Invalidate(); // side panel: steps

        if (before is not null)
        {
            var after = Snapshot(_pageHost);
            _transition.Start(before, after, direction, P.Page);
        }
        CurrentPage.Shown();
        StepChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? StepChanged;

    /// <summary>Builds the current page again (language or theme switch, or a page that changed its own state completely).</summary>
    public void RebuildPage()
    {
        var page = CurrentPage;
        _kicker.Text = page.Kicker;
        _title.Text = page.Title;
        _subtitle.Text = page.Subtitle;

        _pageHost.SuspendLayout();
        foreach (Control old in _pageHost.Controls.Cast<Control>().ToList())
        {
            _pageHost.Controls.Remove(old);
            old.Dispose();
        }
        page.Build(new PageCanvas(_pageHost, S, P));
        _pageHost.ResumeLayout();
        ApplyPalette();
    }

    private static Bitmap Snapshot(Control control)
    {
        // Real stacking order (DrawToBitmap alone paints children in reverse z-order, see ControlSnapshot)
        return ControlSnapshot.Capture(control);
    }

    private void OnThemeChanged()
    {
        P = ThemeMode.Current;
        if (_index >= 0) RebuildPage();
        else ApplyPalette();
        Invalidate(true);
    }

    private void OnLanguageChanged()
    {
        if (_index >= 0) RebuildPage();
        LanguageChanged();
        Invalidate(true);
    }

    /// <summary>For texts the window owns besides the page (buttons): called after a language switch.</summary>
    protected virtual void LanguageChanged() { }

    private void ApplyPalette()
    {
        BackColor = P.Page;
        _content.BackColor = P.Page;
        _pageHost.BackColor = P.Page;
        Apply(this);

        void Apply(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is PvControl pv)
                {
                    pv.P = P;
                    if (pv.Surface == Palette.Light.Page || pv.Surface == Palette.Dark.Page || pv.Surface.IsEmpty) pv.Surface = P.Page;
                    if (pv is PvScrollText scroll) scroll.Relayout();
                    pv.Invalidate();
                }
                Apply(child);
            }
        }
    }

    // ---- painting of the brand panel and the frame ------------------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Smooth(g);
        var side = new Rectangle(0, 0, Px(SideWidth), ClientSize.Height);
        Brand.PaintNight(g, side);

        // Trophy watermark, large and faint, tilted like on the document cover
        using (var watermark = AppIcons.GetBitmap(Px(220)))
        {
            var state = g.Save();
            g.SetClip(side);
            g.TranslateTransform(Px(SideWidth - 70), Px(WindowHeight - 150));
            g.RotateTransform(-8);
            var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = 0.10f });
            g.DrawImage(watermark, new Rectangle(-watermark.Width / 2, -watermark.Height / 2, watermark.Width, watermark.Height),
                0, 0, watermark.Width, watermark.Height, GraphicsUnit.Pixel, attributes);
            g.Restore(state);
        }

        // Logo and wordmark
        using (var logo = AppIcons.GetBitmap(Px(44))) g.DrawImage(logo, Px(32), Px(32), Px(44), Px(44));
        TextRenderer.DrawText(g, "PlatinVault", Fonts.Get(16f, S, FontStyle.Bold, display: true), new Rectangle(Px(88), Px(30), Px(200), Px(28)),
            Brand.Title, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _sideSubtitle, Fonts.Get(9f, S), new Rectangle(Px(89), Px(58), Px(200), Px(20)),
            Brand.Body, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        // Short gradient rule in the two brand accents
        using (var rule = new LinearGradientBrush(new Rectangle(Px(32), Px(96), Px(56), Px(3)), Brand.Crystal, Brand.GoldGlow, 0f))
        {
            Draw.FillRoundedBrush(g, new RectangleF(Px(32), Px(96), Px(56), Px(3)), Px(1.5f), rule);
        }

        PaintSteps(g);

        // Footer: version, "made with" line with the green heart, copyright
        var footerTop = WindowHeight - 92;
        using (var pen = new Pen(Brand.Line, Math.Max(1f, S))) g.DrawLine(pen, Px(32), Px(footerTop), Px(SideWidth - 32), Px(footerTop));
        TextRenderer.DrawText(g, $"PlatinVault {_version}", Fonts.Get(8.5f, S, FontStyle.Bold), new Rectangle(Px(32), Px(footerTop + 14), Px(SideWidth - 64), Px(18)),
            Brand.Body, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        PaintMadeWith(g, new Point(Px(32), Px(footerTop + 36)));
        TextRenderer.DrawText(g, CommonTexts.Copyright, Fonts.Get(8f, S), new Rectangle(Px(32), Px(footerTop + 58), Px(SideWidth - 64), Px(18)),
            Brand.Faint, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

        // Hairline between the action bar and the page
        using (var pen = new Pen(P.Border, Math.Max(1f, S)))
        {
            g.DrawLine(pen, Px(SideWidth), Px(WindowHeight - FooterHeight), ClientSize.Width, Px(WindowHeight - FooterHeight));
        }
    }

    private void PaintMadeWith(Graphics g, Point origin)
    {
        var font = Fonts.Get(8.5f, S);
        var (before, after) = CommonTexts.MadeWithParts;
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var x = origin.X;
        TextRenderer.DrawText(g, before, font, new Point(x, origin.Y), Brand.Body, flags);
        x += TextRenderer.MeasureText(g, before, font, Size.Empty, flags).Width;
        TextRenderer.DrawText(g, "♥", font, new Point(x, origin.Y), Brand.Heart, flags);
        x += TextRenderer.MeasureText(g, "♥", font, Size.Empty, flags).Width;
        TextRenderer.DrawText(g, after, font, new Point(x, origin.Y), Brand.Body, flags);
    }

    private void PaintSteps(Graphics g)
    {
        var top = 132;
        for (var i = 0; i < _steps.Count; i++)
        {
            var y = top + i * 50;
            var circle = new RectangleF(Px(32), Px(y), Px(28), Px(28));
            var done = i < _index;
            var active = i == _index;

            if (i < _steps.Count - 1)
            {
                using var pen = new Pen(done ? Color.FromArgb(150, Brand.Crystal) : Brand.Line, Math.Max(1f, S * 1.5f));
                g.DrawLine(pen, circle.Left + circle.Width / 2, circle.Bottom + Px(4), circle.Left + circle.Width / 2, circle.Bottom + Px(18));
            }

            if (active)
            {
                Brand.PaintGlow(g, RectangleF.Inflate(circle, Px(12), Px(12)), Brand.Crystal, 90);
                using var fill = new SolidBrush(Brand.Crystal);
                g.FillEllipse(fill, circle);
                TextRenderer.DrawText(g, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Fonts.Get(9.5f, S, FontStyle.Bold),
                    Rectangle.Round(circle), Palette.Hex("#0b1220"), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            else if (done)
            {
                using var fill = new SolidBrush(Color.FromArgb(46, Brand.GoldGlow));
                g.FillEllipse(fill, circle);
                using var ring = new Pen(Color.FromArgb(200, Brand.GoldGlow), Math.Max(1f, S * 1.4f));
                g.DrawEllipse(ring, circle);
                Draw.Check(g, RectangleF.Inflate(circle, -Px(7), -Px(7)), Brand.GoldGlow, Math.Max(1.5f, S * 2f));
            }
            else
            {
                using var ring = new Pen(Brand.Line, Math.Max(1f, S * 1.4f));
                g.DrawEllipse(ring, circle);
                TextRenderer.DrawText(g, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Fonts.Get(9.5f, S, FontStyle.Bold),
                    Rectangle.Round(circle), Brand.Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            TextRenderer.DrawText(g, _steps[i].StepLabel, Fonts.Get(10f, S, active ? FontStyle.Bold : FontStyle.Regular),
                new Rectangle(Px(74), Px(y), Px(SideWidth - 74 - 24), Px(28)),
                active ? Brand.Title : done ? Brand.Body : Brand.Faint,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    // ---- borderless frame: drag by the header, rounded corners and shadow (Windows 11) ------------------------------------------

    /// <summary>
    /// Moved to a monitor with another DPI (per-monitor aware program, e.g. the uninstall window of PlatinVault.exe): keep the window's
    /// pixel size. Its layout is fixed in design units at the scale it opened with; Windows' suggested smaller/larger rectangle would cut
    /// off or stretch the fixed controls. A system-aware process (the installer) never gets this message.
    /// </summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        e.Cancel = true;
        base.OnDpiChanged(e);
    }

    private const int WmNcHitTest = 0x84;
    private const int HtCaption = 2;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WmNcHitTest && (int)m.Result == 1)
        {
            var point = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
            // The brand panel and the empty title strip move the window
            if (point.Y < Px(HeaderHeight) || point.X < Px(SideWidth) && GetChildAtPoint(point) is null)
            {
                m.Result = HtCaption;
            }
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // Minimise from the taskbar works without a caption; the drop shadow for Windows 10
            cp.Style |= 0x00020000; // WS_MINIMIZEBOX
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var round = 2; // DWMWCP_ROUND
            _ = DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            var dark = 1; // keep the (invisible) frame dark so no light seam shows at the rounded edge
            _ = DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows: square corners
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Renders the whole window (never shown) into a PNG - offscreen screenshots for the layout check.</summary>
    public void SavePicture(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        CreateHandles(this);
        using var bitmap = ControlSnapshot.Capture(this, ClientSize);
        bitmap.Save(path, ImageFormat.Png);

        static void CreateHandles(Control control)
        {
            _ = control.Handle;
            foreach (Control child in control.Controls) CreateHandles(child);
        }
    }

    /// <summary>The cross-fade layer that lies over the page while it changes (old page slides out, new one slides in).</summary>
    private sealed class TransitionLayer : Control
    {
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
        private Bitmap? _from;
        private Bitmap? _to;
        private int _direction;
        private DateTime _started;
        private const double DurationMs = 260;

        public TransitionLayer()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            _timer.Tick += (_, _) =>
            {
                if ((DateTime.UtcNow - _started).TotalMilliseconds >= DurationMs) Stop();
                else Invalidate();
            };
        }

        public void Start(Bitmap from, Bitmap to, int direction, Color back)
        {
            Stop();
            _from = from;
            _to = to;
            _direction = direction;
            BackColor = back;
            _started = DateTime.UtcNow;
            Visible = true;
            BringToFront();
            _timer.Start();
        }

        private void Stop()
        {
            _timer.Stop();
            Visible = false;
            _from?.Dispose();
            _to?.Dispose();
            _from = _to = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_from is null || _to is null) return;
            var t = Math.Clamp((DateTime.UtcNow - _started).TotalMilliseconds / DurationMs, 0, 1);
            var eased = (float)(1 - Math.Pow(1 - t, 3));
            var shift = Width * 0.06f * _direction;
            Draw(e.Graphics, _from, -shift * eased, 1 - eased);
            Draw(e.Graphics, _to, shift * (1 - eased), eased);
        }

        private static void Draw(Graphics g, Bitmap image, float dx, float alpha)
        {
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
            g.DrawImage(image, new Rectangle((int)dx, 0, image.Width, image.Height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _from?.Dispose();
                _to?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
