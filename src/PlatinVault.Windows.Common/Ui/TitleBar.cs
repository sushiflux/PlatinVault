using System.Drawing.Drawing2D;
using PlatinVault.Windows.Localization;

namespace PlatinVault.Windows.Ui;

/// <summary>A part of the title bar: the empty strip (drag area) or one of the three window commands.</summary>
public enum CaptionPart
{
    None,
    Minimize,
    Maximize,
    Close,
}

public enum CaptionState
{
    Normal,
    Hover,
    Pressed,
}

/// <summary>
/// Colours of the window chrome, shared by the program's title bar and the installer's title strip. The bar has the app's page colour
/// (the web app's header sits right below it in its surface colour, so the step in tone separates them without a line). Hover and
/// pressed are tinted squares; close turns Windows' own close red (#c42b1c) with a white cross, the colour every Windows user knows.
/// Contrast (checked in CaptionColorTests): glyphs and title reach at least 4.5:1 on every background they can lie on, light and dark.
/// </summary>
public static class CaptionColors
{
    public static readonly Color CloseHover = Palette.Hex("#c42b1c");
    public static readonly Color ClosePressed = Palette.Hex("#a32317");
    public static readonly Color CloseGlyph = Color.White;

    /// <summary>The night-blue palette of the start screen: the bar continues the brand surface while the program starts.</summary>
    public static readonly Palette BrandPalette = Palette.Dark with { Page = Ui.Brand.NightTop, Elevated = Palette.Hex("#1c2a4d"), Border = Palette.Hex("#2c3d63") };

    public static Color Bar(Palette p) => p.Page;

    public static Color ButtonBack(Palette p, Color bar, bool close, CaptionState state) => (close, state) switch
    {
        (_, CaptionState.Normal) => bar,
        (true, CaptionState.Hover) => CloseHover,
        (true, CaptionState.Pressed) => ClosePressed,
        (false, CaptionState.Hover) => p.IsDark ? p.Elevated : Draw.Mix(bar, p.Border, 0.55f),
        _ => p.IsDark ? Draw.Mix(p.Elevated, p.Border, 0.6f) : p.Border,
    };

    public static Color Glyph(Palette p, bool close, CaptionState state, bool active) =>
        close && state != CaptionState.Normal ? CloseGlyph : active || state != CaptionState.Normal ? p.TextSecondary : p.Muted;

    public static Color Title(Palette p, bool active) => active ? p.Text : p.Muted;

    public static (Color Back, Color Fore) Badge(Palette p) => (p.GoldSoft, p.Gold);
}

/// <summary>
/// Geometry of the title bar in device pixels for a width and a scale: 40 design units high, three command cells of 46 units at the
/// right edge (full height, so the top-right corner of a maximised window is the close button), the visible hover square inset in each
/// cell with the radius of the app's buttons. Pure, so hit testing is unit-tested without a window.
/// </summary>
public sealed record CaptionLayout(int Width, int Height, float Scale, Rectangle Minimize, Rectangle Maximize, Rectangle Close)
{
    public const int BarHeight = 40;
    public const int CellWidth = 46;
    public const int PillInsetX = 4;
    public const int PillInsetY = 5;
    public const int PillRadius = 5;
    public const int LogoLeft = 14;
    public const int LogoSize = 18;
    public const int TitleGap = 10;

    public static CaptionLayout Compute(int width, float scale)
    {
        var height = Px(BarHeight, scale);
        var cell = Px(CellWidth, scale);
        var close = new Rectangle(width - cell, 0, cell, height);
        var maximize = new Rectangle(close.Left - cell, 0, cell, height);
        var minimize = new Rectangle(maximize.Left - cell, 0, cell, height);
        return new CaptionLayout(width, height, scale, minimize, maximize, close);
    }

    /// <summary>The visible (hover) square inside a command cell.</summary>
    public Rectangle Pill(Rectangle cell) =>
        Rectangle.FromLTRB(cell.Left + Px(PillInsetX, Scale), cell.Top + Px(PillInsetY, Scale), cell.Right - Px(PillInsetX, Scale), cell.Bottom - Px(PillInsetY, Scale));

    public int PillTop => Px(PillInsetY, Scale);

    public Rectangle Logo => new(Px(LogoLeft, Scale), (Height - Px(LogoSize, Scale)) / 2, Px(LogoSize, Scale), Px(LogoSize, Scale));

    public int TitleLeft => Logo.Right + Px(TitleGap, Scale);

    public Rectangle CellOf(CaptionPart part) => part switch
    {
        CaptionPart.Minimize => Minimize,
        CaptionPart.Maximize => Maximize,
        CaptionPart.Close => Close,
        _ => Rectangle.Empty,
    };

    public CaptionPart PartAt(Point point) =>
        point.Y < 0 || point.Y >= Height ? CaptionPart.None
        : Close.Contains(point) ? CaptionPart.Close
        : Maximize.Contains(point) ? CaptionPart.Maximize
        : Minimize.Contains(point) ? CaptionPart.Minimize
        : CaptionPart.None;

    private static int Px(float units, float scale) => (int)Math.Round(units * scale);
}

/// <summary>
/// The program's own title bar: trophy, "PlatinVault" (plus an optional gold badge such as "Entwicklung"), and minimise,
/// maximise/restore and close at the right. It only draws: the window (<see cref="ChromeWindow"/>) answers WM_NCHITTEST for the whole
/// bar - drag area, top resize edge and the three commands as HTMINBUTTON/HTMAXBUTTON/HTCLOSE (that is what makes Windows 11 show the
/// snap layouts on the maximise button) - and tells the bar which command is hovered or pressed. For screen readers the bar is a
/// "title bar" with three push buttons that can be invoked.
/// </summary>
public sealed class PvTitleBar : Control
{
    private CaptionPart _hover;
    private CaptionPart _pressed;
    private bool _active = true;
    private bool _maximized;
    private Bitmap? _logo;

    public PvTitleBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Height = CaptionLayout.BarHeight;
    }

    /// <summary>Pixels per design unit. Changing it changes the bar's height.</summary>
    public float S
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _logo?.Dispose();
            _logo = null;
            Height = Geometry.Height;
            Invalidate();
        }
    } = 1f;

    public Palette P
    {
        get;
        set
        {
            field = value;
            Invalidate();
        }
    } = Palette.Light;

    /// <summary>While true the bar continues the night-blue start screen below it (brand gradient instead of the page colour).</summary>
    public bool BrandMode
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Invalidate();
        }
    }

    /// <summary>The window title shown next to the trophy.</summary>
    public string Title
    {
        get;
        set
        {
            field = value;
            Invalidate();
        }
    } = "PlatinVault";

    /// <summary>Optional small gold badge after the title (e.g. "Entwicklung" in a development run).</summary>
    public Msg? Badge
    {
        get;
        set
        {
            field = value;
            Invalidate();
        }
    }

    /// <summary>Windows 10 draws no line at the top of a window without a caption: the bar draws it (not when maximised).</summary>
    public bool DrawTopBorder { get; set; } = !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            Invalidate();
        }
    }

    public bool Maximized
    {
        get => _maximized;
        set
        {
            if (_maximized == value) return;
            _maximized = value;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.NameChange, 2);
        }
    }

    public CaptionPart HoverPart => _hover;

    public CaptionPart PressedPart => _pressed;

    public CaptionLayout Geometry => CaptionLayout.Compute(Width, S);

    public Palette Colors => BrandMode ? CaptionColors.BrandPalette : P;

    /// <summary>A command was invoked (by the window's mouse handling or by a screen reader).</summary>
    public event Action<CaptionPart>? Command;

    public void Execute(CaptionPart part)
    {
        if (part != CaptionPart.None) Command?.Invoke(part);
    }

    public void SetHover(CaptionPart part)
    {
        if (_hover == part) return;
        _hover = part;
        if (part != _pressed) _pressed = CaptionPart.None;
        Invalidate();
    }

    public void SetPressed(CaptionPart part)
    {
        if (_pressed == part) return;
        _pressed = part;
        Invalidate();
    }

    public void ResetStates()
    {
        if (_hover == CaptionPart.None && _pressed == CaptionPart.None) return;
        _hover = _pressed = CaptionPart.None;
        Invalidate();
    }

    public CaptionState StateOf(CaptionPart part) =>
        _pressed == part && _hover == part ? CaptionState.Pressed : _hover == part ? CaptionState.Hover : CaptionState.Normal;

    public string NameOf(CaptionPart part) => part switch
    {
        CaptionPart.Minimize => CommonTexts.CaptionMinimize,
        CaptionPart.Maximize => _maximized ? CommonTexts.CaptionRestore : CommonTexts.CaptionMaximize,
        CaptionPart.Close => CommonTexts.Close,
        _ => Title,
    };

    /// <summary>When true (the default) every point of the bar belongs to the window's own hit test (see <see cref="ChromeWindow"/>).</summary>
    public bool PassHitTestToWindow { get; set; } = true;

    protected override void WndProc(ref Message m)
    {
        const int WmNcHitTest = 0x0084;
        const int HtTransparent = -1;
        if (m.Msg == WmNcHitTest && PassHitTestToWindow)
        {
            m.Result = HtTransparent;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Painted completely in OnPaint
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Smooth(g);
        var layout = Geometry;
        var p = Colors;
        var bar = CaptionColors.Bar(p);

        if (BrandMode)
        {
            // The same gradient and glows as the start screen below, painted for the whole window so both meet without a seam
            var canvas = Parent is { } parent ? parent.ClientSize.Height : Height;
            Brand.PaintNight(g, new Rectangle(0, 0, Width, Math.Max(Height, canvas)));
        }
        else
        {
            using var back = new SolidBrush(bar);
            g.FillRectangle(back, ClientRectangle);
        }
        if (DrawTopBorder && !_maximized)
        {
            using var pen = new Pen(p.Border, 1f);
            g.DrawLine(pen, 0, 0.5f, Width, 0.5f);
        }

        // Trophy and title (and the badge)
        var logoRect = layout.Logo;
        _logo ??= AppIcons.GetBitmap(logoRect.Width);
        if (_active) g.DrawImage(_logo, logoRect);
        else
        {
            using var faded = new System.Drawing.Imaging.ImageAttributes();
            faded.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.6f });
            g.DrawImage(_logo, logoRect, 0, 0, _logo.Width, _logo.Height, GraphicsUnit.Pixel, faded);
        }
        var (titleRect, badgeRect) = TextAreas();
        TextRenderer.DrawText(g, Title, TitleFont, titleRect, BrandMode ? Ui.Brand.Title : CaptionColors.Title(p, _active), TextFlags);
        if (badgeRect is { } pill)
        {
            var (back, fore) = CaptionColors.Badge(p);
            Draw.FillRounded(g, pill, 4 * S, back);
            TextRenderer.DrawText(g, BadgeText, BadgeFont, pill, fore, TextFlags | TextFormatFlags.HorizontalCenter);
        }

        // The three window commands
        foreach (var part in new[] { CaptionPart.Minimize, CaptionPart.Maximize, CaptionPart.Close })
        {
            var cell = layout.CellOf(part);
            var state = StateOf(part);
            var close = part == CaptionPart.Close;
            var back = CaptionColors.ButtonBack(p, bar, close, state);
            if (state != CaptionState.Normal) Draw.FillRounded(g, layout.Pill(cell), CaptionLayout.PillRadius * S, back);
            var glyphSize = 16 * S;
            var box = new RectangleF(cell.Left + (cell.Width - glyphSize) / 2f, cell.Top + (cell.Height - glyphSize) / 2f, glyphSize, glyphSize);
            var color = CaptionColors.Glyph(p, close, state, _active);
            switch (part)
            {
                case CaptionPart.Minimize: CaptionGlyphs.Minimize(g, box, color, S); break;
                case CaptionPart.Maximize when _maximized: CaptionGlyphs.Restore(g, box, color, S); break;
                case CaptionPart.Maximize: CaptionGlyphs.Maximize(g, box, color, S); break;
                default: CaptionGlyphs.Close(g, box, color, S); break;
            }
        }
    }

    private const TextFormatFlags TextFlags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;

    private Font TitleFont => Fonts.Get(10f, S, FontStyle.Bold, display: true);

    private Font BadgeFont => Fonts.Get(7.5f, S, FontStyle.Bold);

    private string BadgeText => Badge is { } badge ? badge.ToString().ToUpperInvariant() : string.Empty;

    /// <summary>Where the title and the badge are drawn (the layout check makes sure they end before the window commands).</summary>
    public (Rectangle Title, Rectangle? Badge) TextAreas()
    {
        var layout = Geometry;
        var title = new Rectangle(layout.TitleLeft, 0, TextRenderer.MeasureText(Title, TitleFont, Size.Empty, TextFlags).Width + 2, layout.Height);
        if (Badge is null) return (title, null);
        var text = TextRenderer.MeasureText(BadgeText, BadgeFont, Size.Empty, TextFlags);
        var height = (int)Math.Round(18 * S);
        return (title, new Rectangle(title.Right + (int)Math.Round(8 * S), (layout.Height - height) / 2, text.Width + (int)Math.Round(14 * S), height));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _logo?.Dispose();
        base.Dispose(disposing);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new BarAccessible(this);

    private sealed class BarAccessible(PvTitleBar bar) : ControlAccessibleObject(bar)
    {
        private static readonly CaptionPart[] Parts = [CaptionPart.Minimize, CaptionPart.Maximize, CaptionPart.Close];

        public override AccessibleRole Role => AccessibleRole.TitleBar;

        public override string? Name => bar.Title;

        public override int GetChildCount() => Parts.Length;

        public override AccessibleObject? GetChild(int index) => index >= 0 && index < Parts.Length ? new ButtonAccessible(bar, this, Parts[index]) : null;
    }

    private sealed class ButtonAccessible(PvTitleBar bar, AccessibleObject parent, CaptionPart part) : AccessibleObject
    {
        public override string? Name => bar.NameOf(part);

        public override AccessibleRole Role => AccessibleRole.PushButton;

        public override AccessibleObject? Parent => parent;

        public override Rectangle Bounds => bar.IsHandleCreated ? bar.RectangleToScreen(bar.Geometry.CellOf(part)) : Rectangle.Empty;

        public override string? DefaultAction => CommonTexts.Press;

        public override void DoDefaultAction() => bar.Execute(part);

        public override AccessibleStates State => bar.StateOf(part) == CaptionState.Hover ? AccessibleStates.HotTracked : AccessibleStates.None;
    }
}
