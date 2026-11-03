using System.Drawing.Drawing2D;

namespace PlatinVault.Windows.Ui;

/// <summary>
/// Base of every owner-drawn control: double-buffered, knows the window's scale (design units are 96-dpi pixels) and paints with
/// the window's current <see cref="Palette"/>. Controls never size themselves: the window places them in a fixed layout, so a
/// message appearing or a language switch never moves anything (rigid layout rule).
/// </summary>
public abstract class PvControl : Control
{
    protected PvControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
    }

    /// <summary>Pixels per design unit (1.0 at 100 %, 2.0 at 200 %).</summary>
    public float S { get; set; } = 1f;

    public Palette P { get; set; } = Palette.Light;

    /// <summary>The colour of whatever this control lies on (owner-drawn controls paint their own background).</summary>
    public Color Surface { get; set; } = Palette.Light.Page;

    protected float U(float designUnits) => designUnits * S;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new SolidBrush(Surface);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}

public enum PvButtonKind
{
    Primary,
    Secondary,
    Danger,
    /// <summary>Text-only, for small secondary actions.</summary>
    Ghost,
    /// <summary>A window command (minimise, light/dark): the same hover and pressed look as the program's title bar (<see cref="CaptionColors"/>).</summary>
    WindowButton,
    /// <summary>The window's close button: like <see cref="WindowButton"/>, but red with a white cross on hover (<see cref="CaptionColors"/>).</summary>
    WindowClose,
}

/// <summary>Button in the app's look: tight radius, clear hover/pressed states, visible focus ring, readable disabled state.</summary>
public sealed class PvButton : PvControl, IButtonControl
{
    private bool _hover;
    private bool _pressed;

    public PvButton()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public PvButtonKind Kind { get; set; } = PvButtonKind.Secondary;

    /// <summary>Optional glyph drawn left of the text (or alone when the text is empty).</summary>
    public Action<Graphics, RectangleF, Color>? Glyph { get; set; }

    public DialogResult DialogResult { get; set; }

    public void NotifyDefault(bool value) { }

    public void PerformClick()
    {
        if (Enabled) OnClick(EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e)
    {
        if (!Enabled) return;
        if (DialogResult != DialogResult.None && FindForm() is { } form) form.DialogResult = DialogResult;
        base.OnClick(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    /// <summary>Width the label (plus glyph) needs with 14 design units of padding on each side - the layout check compares it with the real width.</summary>
    public int NeededWidth
    {
        get
        {
            if (Text.Length == 0) return 0;
            var font = Fonts.Get(10f, S, FontStyle.Bold);
            var text = TextRenderer.MeasureText(Text, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            var glyph = Glyph is null ? 0f : U(16) + U(8);
            return (int)Math.Ceiling(text + glyph + 2 * U(14));
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            PerformClick();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Smooth(g);
        var r = new RectangleF(U(1), U(1), Width - U(2), Height - U(2));
        var radius = U(5);

        Color back, fore, border;
        switch (Kind)
        {
            case PvButtonKind.Primary:
                back = !Enabled ? P.Elevated : _pressed || _hover ? P.AccentHover : P.Accent;
                fore = Enabled ? P.OnAccent : P.Muted;
                border = Enabled ? back : P.Border;
                break;
            case PvButtonKind.Danger:
                back = !Enabled ? P.Elevated : _pressed || _hover ? P.DangerHover : P.Danger;
                fore = Enabled ? (P.IsDark ? Palette.Hex("#0b1220") : Color.White) : P.Muted;
                border = Enabled ? back : P.Border;
                break;
            case PvButtonKind.Ghost:
                back = _pressed ? P.Elevated : _hover ? Draw.Mix(Surface, P.Elevated, 0.8f) : Surface;
                fore = Enabled ? P.TextSecondary : P.Muted;
                border = back;
                break;
            case PvButtonKind.WindowButton:
            case PvButtonKind.WindowClose:
                var state = !Enabled ? CaptionState.Normal : _pressed ? CaptionState.Pressed : _hover ? CaptionState.Hover : CaptionState.Normal;
                var close = Kind == PvButtonKind.WindowClose;
                back = CaptionColors.ButtonBack(P, Surface, close, state);
                fore = Enabled ? CaptionColors.Glyph(P, close, state, active: true) : P.Muted;
                border = back;
                break;
            default:
                back = !Enabled ? P.Elevated : _pressed ? P.Elevated : _hover ? Draw.Mix(P.Surface, P.Elevated, 0.7f) : P.Surface;
                fore = Enabled ? P.Text : P.Muted;
                border = _hover && Enabled ? P.BorderStrong : P.Border;
                break;
        }

        Draw.FillRounded(g, r, radius, back);
        if (Kind is not (PvButtonKind.Ghost or PvButtonKind.WindowButton or PvButtonKind.WindowClose)) Draw.StrokeRounded(g, r, radius, border, Math.Max(1f, U(1)));
        if (Focused && ShowFocusCues)
        {
            var ring = RectangleF.Inflate(r, -U(2.5f), -U(2.5f));
            Draw.StrokeRounded(g, ring, U(3.5f), Kind is PvButtonKind.Primary or PvButtonKind.Danger ? fore : P.Focus, Math.Max(1.5f, U(1.75f)));
        }

        var font = Fonts.Get(10f, S, FontStyle.Bold);
        var textSize = TextRenderer.MeasureText(g, Text, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        var glyphSize = Glyph is null ? 0f : U(16);
        var gap = Glyph is not null && Text.Length > 0 ? U(8) : 0f;
        var total = glyphSize + gap + textSize.Width;
        var x = (Width - total) / 2f;
        if (Glyph is not null)
        {
            Glyph(g, new RectangleF(x, (Height - glyphSize) / 2f, glyphSize, glyphSize), fore);
        }
        if (Text.Length > 0)
        {
            var textRect = new Rectangle((int)Math.Round(x + glyphSize + gap), 0, textSize.Width + 2, Height);
            TextRenderer.DrawText(g, Text, font, textRect, fore, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }
}

/// <summary>Plain text that wraps inside its fixed bounds (no auto size). Role decides font and colour.</summary>
public sealed class PvText : PvControl
{
    public PvText()
    {
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    public float Points { get; set; } = 10f;

    public FontStyle Style { get; set; } = FontStyle.Regular;

    public bool Display { get; set; }

    /// <summary>Colour picker from the palette; default = the body text colour.</summary>
    public Func<Palette, Color> Color { get; set; } = p => p.Text;

    /// <summary>Letter-spaced caps (kicker above a title).</summary>
    public bool Kicker { get; set; }

    public TextFormatFlags Align { get; set; } = TextFormatFlags.Left;

    public Font TextFont => Fonts.Get(Points, S, Style, Display);

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var text = Kicker ? Spaced(Text.ToUpperInvariant()) : Text;
        Draw.Text(e.Graphics, text, TextFont, Color(P), ClientRectangle, Align);
    }

    private static string Spaced(string text) => string.Join(" ", text.ToCharArray());

    /// <summary>Height the text needs at the control's width (layout check).</summary>
    public int NeededHeight => Draw.Measure(Kicker ? Spaced(Text.ToUpperInvariant()) : Text, TextFont, Width).Height;
}

/// <summary>On/off switch row: green = on, red = off (tokens --c-toggle-on/off), check/cross in the knob, the whole row is clickable.</summary>
public sealed class PvToggle : PvControl
{
    private bool _checked;
    private bool _hover;

    public PvToggle()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    public string Description { get; set; } = string.Empty;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;

    protected override void OnClick(EventArgs e)
    {
        if (Enabled) Checked = !Checked;
        base.OnClick(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter && Enabled)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Smooth(g);

        var card = new RectangleF(U(0.5f), U(0.5f), Width - U(1), Height - U(1));
        Draw.FillRounded(g, card, U(6), _hover ? Draw.Mix(P.Surface, P.Elevated, 0.5f) : P.Surface);
        Draw.StrokeRounded(g, card, U(6), P.Border, Math.Max(1f, U(1)));

        // Switch on the right: 44 x 24 design units
        var track = new RectangleF(Width - U(16 + 44), (Height - U(24)) / 2f, U(44), U(24));
        Draw.FillRounded(g, track, U(12), Checked ? P.ToggleOn : P.ToggleOff);
        var knobSize = U(18);
        var knob = new RectangleF(Checked ? track.Right - U(3) - knobSize : track.Left + U(3), track.Top + U(3), knobSize, knobSize);
        using (var brush = new SolidBrush(P.Knob)) g.FillEllipse(brush, knob);
        var glyph = RectangleF.Inflate(knob, -U(3.5f), -U(3.5f));
        if (Checked) Draw.Check(g, glyph, P.ToggleOn, Math.Max(1.5f, U(2)));
        else Draw.Cross(g, glyph, P.ToggleOff, Math.Max(1.5f, U(2)));

        if (Focused && ShowFocusCues)
        {
            Draw.StrokeRounded(g, RectangleF.Inflate(track, U(3), U(3)), U(15), P.Focus, Math.Max(1.5f, U(2)));
        }

        var textLeft = (int)U(16);
        var textWidth = (int)(track.Left - U(16) - textLeft);
        var title = Fonts.Get(10f, S, FontStyle.Bold);
        if (Description.Length == 0)
        {
            Draw.Text(g, Text, title, Enabled ? P.Text : P.Muted, new Rectangle(textLeft, 0, textWidth, Height), TextFormatFlags.VerticalCenter);
            return;
        }
        Draw.Text(g, Text, title, Enabled ? P.Text : P.Muted, new Rectangle(textLeft, (int)U(11), textWidth, (int)U(20)));
        Draw.Text(g, Description, Fonts.Get(9f, S), P.Muted, new Rectangle(textLeft, (int)U(32), textWidth, Height - (int)U(36)));
    }
}

/// <summary>Checkbox row for agreements (not an on/off setting): square box with a check, text beside it, whole row clickable.</summary>
public sealed class PvCheck : PvControl
{
    private bool _checked;

    public PvCheck()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Smooth(g);
        var box = new RectangleF(U(1), (Height - U(20)) / 2f, U(20), U(20));
        if (Checked)
        {
            Draw.FillRounded(g, box, U(4), P.Accent);
            Draw.Check(g, RectangleF.Inflate(box, -U(3), -U(3)), P.OnAccent, Math.Max(1.5f, U(2.2f)));
        }
        else
        {
            Draw.FillRounded(g, box, U(4), P.Surface);
            Draw.StrokeRounded(g, box, U(4), P.BorderStrong, Math.Max(1f, U(1.5f)));
        }
        if (Focused && ShowFocusCues) Draw.StrokeRounded(g, RectangleF.Inflate(box, U(3), U(3)), U(6), P.Focus, Math.Max(1.5f, U(2)));
        Draw.Text(g, Text, Fonts.Get(10f, S, FontStyle.Bold), P.Text,
            new Rectangle((int)U(32), 0, Width - (int)U(32), Height), TextFormatFlags.VerticalCenter);
    }
}

/// <summary>A selectable card (radio semantics): badge, title and a line of description; the chosen one has an accent frame and a check.</summary>
public sealed class PvChoice : PvControl
{
    private bool _selected;
    private bool _hover;

    public PvChoice()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.RadioButton;
    }

    public string Badge { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Badge colour family: accent (default), gold, danger.</summary>
    public Func<Palette, (Color Back, Color Fore)>? BadgeColors { get; set; }

    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); }
    }

    protected override void OnClick(EventArgs e) { Focus(); base.OnClick(e); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Smooth(g);
        var card = new RectangleF(U(1), U(1), Width - U(2), Height - U(2));
        var back = Selected ? Draw.Mix(P.Surface, P.AccentSoft, P.IsDark ? 0.75f : 0.55f) : _hover ? Draw.Mix(P.Surface, P.Elevated, 0.6f) : P.Surface;
        Draw.FillRounded(g, card, U(8), back);
        Draw.StrokeRounded(g, card, U(8), Selected ? P.Accent : _hover ? P.BorderStrong : P.Border, Selected ? Math.Max(1.5f, U(2)) : Math.Max(1f, U(1)));
        if (Focused && ShowFocusCues) Draw.StrokeRounded(g, RectangleF.Inflate(card, -U(4), -U(4)), U(5), P.Focus, Math.Max(1f, U(1.5f)));

        var left = U(18);
        if (Badge.Length > 0)
        {
            var (badgeBack, badgeFore) = BadgeColors?.Invoke(P) ?? (P.AccentSoft, P.Accent);
            var badge = new RectangleF(left, (Height - U(40)) / 2f, U(40), U(40));
            Draw.FillRounded(g, badge, U(8), badgeBack);
            TextRenderer.DrawText(g, Badge, Fonts.Get(10.5f, S, FontStyle.Bold), Rectangle.Round(badge), badgeFore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            left = badge.Right + U(14);
        }

        // Selection mark at the right: filled circle with a check, or an empty ring
        var mark = new RectangleF(Width - U(18 + 22), (Height - U(22)) / 2f, U(22), U(22));
        if (Selected)
        {
            using var brush = new SolidBrush(P.Accent);
            g.FillEllipse(brush, mark);
            Draw.Check(g, RectangleF.Inflate(mark, -U(4.5f), -U(4.5f)), P.OnAccent, Math.Max(1.5f, U(2.2f)));
        }
        else
        {
            using var pen = new Pen(P.BorderStrong, Math.Max(1f, U(1.5f)));
            g.DrawEllipse(pen, RectangleF.Inflate(mark, -U(1), -U(1)));
        }

        var width = (int)(mark.Left - U(12) - left);
        if (Description.Length == 0)
        {
            Draw.Text(g, Text, Fonts.Get(11f, S, FontStyle.Bold), P.Text, new Rectangle((int)left, 0, width, Height), TextFormatFlags.VerticalCenter);
            return;
        }
        var titleFont = Fonts.Get(11f, S, FontStyle.Bold);
        var bodyFont = Fonts.Get(9f, S);
        var titleH = TextRenderer.MeasureText(g, "Ag", titleFont, Size.Empty, TextFormatFlags.NoPadding).Height;
        var bodyH = Draw.Measure(Description, bodyFont, width).Height;
        var top = (int)((Height - titleH - U(4) - bodyH) / 2f);
        Draw.Text(g, Text, titleFont, P.Text, new Rectangle((int)left, top, width, titleH + 2));
        Draw.Text(g, Description, bodyFont, P.Muted, new Rectangle((int)left, top + titleH + (int)U(4), width, bodyH + 2));
    }
}

/// <summary>Progress bar: rounded track, accent fill with a gentle moving sheen while <see cref="Running"/>.</summary>
public sealed class PvProgress : PvControl
{
    private float _value;
    private float _sheen;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };

    public PvProgress()
    {
        TabStop = false;
        _timer.Tick += (_, _) =>
        {
            _sheen = (_sheen + 0.012f) % 1.4f;
            Invalidate();
        };
    }

    /// <summary>0..1.</summary>
    public float Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0f, 1f); Invalidate(); }
    }

    public bool Failed { get; set; }

    public bool Running
    {
        get => _timer.Enabled;
        set
        {
            // No animation while motion is reduced in Windows (accessibility setting "Show animations")
            _timer.Enabled = value && SystemInformation.UIEffectsEnabled;
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Smooth(g);
        var track = new RectangleF(0, 0, Width, Height);
        var radius = Height / 2f;
        Draw.FillRounded(g, track, radius, P.Elevated);
        if (_value <= 0) return;
        var fill = new RectangleF(0, 0, Math.Max(Height, Width * _value), Height);
        var color = Failed ? P.Danger : P.Accent;
        using (var path = Draw.Rounded(fill, radius))
        using (var brush = new LinearGradientBrush(fill, Draw.Mix(color, Brand.Crystal, 0.25f), color, 0f))
        {
            g.FillPath(brush, path);
            if (Running)
            {
                var state = g.Save();
                g.SetClip(path);
                var x = fill.Left + (_sheen - 0.2f) * Width;
                var sheen = new RectangleF(x, 0, Width * 0.18f, Height);
                using var shine = new LinearGradientBrush(new RectangleF(sheen.X - 1, 0, sheen.Width + 2, Height),
                    Color.FromArgb(0, Color.White), Color.FromArgb(0, Color.White), 0f);
                shine.InterpolationColors = new ColorBlend
                {
                    Colors = [Color.FromArgb(0, Color.White), Color.FromArgb(90, Color.White), Color.FromArgb(0, Color.White)],
                    Positions = [0f, 0.5f, 1f],
                };
                g.FillRectangle(shine, sheen);
                g.Restore(state);
            }
        }
    }
}
