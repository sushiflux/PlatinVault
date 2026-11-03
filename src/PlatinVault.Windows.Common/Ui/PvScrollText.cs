namespace PlatinVault.Windows.Ui;

/// <summary>
/// A text area of a fixed size that always holds its text COMPLETELY: it wraps, and a text taller than the area scrolls inside it
/// (thin scroll bar, mouse wheel, arrow keys / Page up / Page down / Home / End once it has the focus; Ctrl+C copies everything).
/// Nothing around it ever moves (rigid layout); a text is never cut with "..." (an error message is always shown in full).
/// Understands a little Markdown for the licence text: "#"/"##" headings, "- " bullets, **bold** markers are removed.
/// </summary>
public sealed class PvScrollText : PvControl
{
    private readonly List<(string Text, Font Font, Color Color, int Indent, int Top, int Height)> _blocks = [];
    private int _contentHeight;
    private int _offset;
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartOffset;
    private bool _markdown;
    private Func<Palette, Color> _color = p => p.TextSecondary;

    public PvScrollText()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = false;
        AccessibleRole = AccessibleRole.StaticText;
    }

    public float Points { get; set; } = 9.5f;

    /// <summary>Draws a framed card (licence text) instead of plain text on the page (messages).</summary>
    public bool Framed { get; set; }

    public void SetText(string text, Func<Palette, Color>? color = null, bool markdown = false)
    {
        _markdown = markdown;
        _color = color ?? (p => p.TextSecondary);
        Text = text;
        _offset = 0;
        Relayout();
    }

    public bool Overflows => _contentHeight > Inner.Height;

    public int Offset => _offset;

    private Rectangle Inner
    {
        get
        {
            var pad = Framed ? (int)U(14) : 0;
            var bar = (int)U(10);
            return new Rectangle(pad, pad, Math.Max(10, Width - 2 * pad - bar), Math.Max(10, Height - 2 * pad));
        }
    }

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Relayout(); }

    /// <summary>Recomputes wrapping (after a text, size, scale or theme change).</summary>
    public void Relayout()
    {
        _blocks.Clear();
        var width = Inner.Width;
        var y = 0;
        var body = Fonts.Get(Points, S);
        var lines = Text.Replace("\r\n", "\n").Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            Font font = body;
            var color = _color(P);
            var indent = 0;
            var text = line;
            var spaceBefore = 0;
            if (_markdown)
            {
                text = text.Replace("**", string.Empty).Replace("`", string.Empty);
                if (text.StartsWith("## ", StringComparison.Ordinal) || text.StartsWith("### ", StringComparison.Ordinal))
                {
                    text = text.TrimStart('#', ' ');
                    font = Fonts.Get(Points + 1f, S, FontStyle.Bold);
                    color = P.Text;
                    spaceBefore = (int)U(8);
                }
                else if (text.StartsWith("# ", StringComparison.Ordinal))
                {
                    text = text[2..];
                    font = Fonts.Get(Points + 3f, S, FontStyle.Bold, display: true);
                    color = P.Text;
                    spaceBefore = (int)U(4);
                }
                else if (text.StartsWith("- ", StringComparison.Ordinal) || text.StartsWith("* ", StringComparison.Ordinal))
                {
                    text = "•  " + text[2..];
                    indent = (int)U(8);
                }
                else if (text.StartsWith("---", StringComparison.Ordinal))
                {
                    text = string.Empty;
                }
            }

            if (text.Length == 0)
            {
                y += (int)U(7);
                continue;
            }
            y += spaceBefore;
            var height = Draw.Measure(text, font, width - indent).Height;
            _blocks.Add((text, font, color, indent, y, height));
            y += height + (int)U(2);
        }
        _contentHeight = y;
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _contentHeight - Inner.Height));
        // A text that scrolls must be reachable from the keyboard; a short one stays out of the tab order
        TabStop = Overflows;
        Invalidate();
    }

    public void ScrollBy(int pixels)
    {
        var max = Math.Max(0, _contentHeight - Inner.Height);
        var next = Math.Clamp(_offset + pixels, 0, max);
        if (next == _offset) return;
        _offset = next;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollBy(-e.Delta / 120 * (int)U(48));
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var page = Inner.Height - (int)U(24);
        switch (e.KeyCode)
        {
            case Keys.Up: ScrollBy(-(int)U(24)); break;
            case Keys.Down: ScrollBy((int)U(24)); break;
            case Keys.PageUp: ScrollBy(-page); break;
            case Keys.PageDown: ScrollBy(page); break;
            case Keys.Home: ScrollBy(-_contentHeight); break;
            case Keys.End: ScrollBy(_contentHeight); break;
            case Keys.C when e.Control:
                try { Clipboard.SetText(Text); } catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy: nothing to do */ }
                break;
        }
        base.OnKeyDown(e);
    }

    private RectangleF Thumb()
    {
        var inner = Inner;
        var trackTop = inner.Top;
        var trackHeight = inner.Height;
        var ratio = trackHeight / (float)Math.Max(1, _contentHeight);
        var thumbHeight = Math.Max(U(28), trackHeight * ratio);
        var max = Math.Max(1, _contentHeight - inner.Height);
        var top = trackTop + (trackHeight - thumbHeight) * (_offset / (float)max);
        return new RectangleF(Width - (Framed ? U(14) : 0) - U(6), top, U(5), thumbHeight);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (Overflows && Thumb() is var thumb && e.X >= thumb.Left - U(6))
        {
            _dragging = true;
            _dragStartY = e.Y;
            _dragStartOffset = _offset;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            var inner = Inner;
            var thumb = Thumb();
            var travel = Math.Max(1f, inner.Height - thumb.Height);
            var max = Math.Max(0, _contentHeight - inner.Height);
            _offset = Math.Clamp(_dragStartOffset + (int)((e.Y - _dragStartY) / travel * max), 0, max);
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Draw.Smooth(g);
        if (Framed)
        {
            var card = new RectangleF(U(0.5f), U(0.5f), Width - U(1), Height - U(1));
            Draw.FillRounded(g, card, U(8), P.Surface);
            Draw.StrokeRounded(g, card, U(8), Focused && ShowFocusCues ? P.Focus : P.Border, Math.Max(1f, U(Focused ? 1.5f : 1f)));
        }

        var inner = Inner;
        var state = g.Save();
        g.SetClip(inner);
        foreach (var block in _blocks)
        {
            var top = inner.Top + block.Top - _offset;
            if (top + block.Height < inner.Top || top > inner.Bottom) continue;
            Draw.Text(g, block.Text, block.Font, block.Color, new Rectangle(inner.Left + block.Indent, top, inner.Width - block.Indent, block.Height + 2));
        }
        g.Restore(state);

        if (Overflows)
        {
            var thumb = Thumb();
            Draw.FillRounded(g, thumb, U(2.5f), Draw.Mix(P.BorderStrong, Surface, 0.15f));
        }
    }
}
