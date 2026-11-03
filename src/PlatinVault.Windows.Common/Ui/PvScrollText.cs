namespace PlatinVault.Windows.Ui;

/// <summary>
/// A text area of a fixed size that always holds its text COMPLETELY: it wraps, and a text taller than the area scrolls inside it
/// (thin scroll bar, mouse wheel, arrow keys / Page up / Page down / Home / End once it has the focus). Nothing around it ever moves
/// (rigid layout); a text is never cut with "..." (an error message is always shown in full).
/// <para>
/// The text is selectable like in any text field (rule "error messages are always copyable"): drag with the mouse, double-click selects a
/// word, Ctrl+A selects everything, Ctrl+C / Ctrl+Insert copies the selection - or the whole text when nothing is selected. The control
/// breaks the lines itself and draws every line on its own, so the position of every character is known for the selection.
/// </para>
/// Understands a little Markdown for the licence text: "#"/"##" headings, "- " bullets, **bold** markers are removed.
/// </summary>
public sealed class PvScrollText : PvControl
{
    private const TextFormatFlags MeasureFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
    private const TextFormatFlags DrawFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine;

    private readonly record struct Line(string Text, Font Font, Color Color, int Indent, int Top, int Height, int Start);

    private readonly List<Line> _lines = [];
    // What is on screen, the lines joined by '\n' (blank lines are empty): the selection counts characters in here
    private string _flat = string.Empty;
    private int _contentHeight;
    private int _offset;
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartOffset;
    private bool _selecting;
    private int _anchor;
    private int _caret;
    private bool _markdown;
    private Func<Palette, Color> _color = p => p.TextSecondary;

    public PvScrollText()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = false;
        AccessibleRole = AccessibleRole.StaticText;
        Cursor = Cursors.IBeam;
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
        _anchor = _caret = 0;
        Relayout();
    }

    public bool Overflows => _contentHeight > Inner.Height;

    public int Offset => _offset;

    /// <summary>Height of the wrapped text in pixels at the control's current width (a window sizes its slot with it).</summary>
    public int ContentHeight => _contentHeight;

    /// <summary>The characters of the selection, '' when nothing is selected.</summary>
    public string SelectedText => HasSelection ? _flat[SelectionStart..SelectionEnd] : string.Empty;

    public bool HasSelection => _anchor != _caret;

    private int SelectionStart => Math.Min(_anchor, _caret);

    private int SelectionEnd => Math.Max(_anchor, _caret);

    /// <summary>Selects <paramref name="length"/> characters from <paramref name="start"/> (counted in the displayed text).</summary>
    public void Select(int start, int length)
    {
        _anchor = Math.Clamp(start, 0, _flat.Length);
        _caret = Math.Clamp(start + length, 0, _flat.Length);
        Invalidate();
    }

    public void SelectAll() => Select(0, _flat.Length);

    /// <summary>Copies the selection - or the whole text when nothing is selected. False when the clipboard could not be written.</summary>
    public bool CopyToClipboard()
    {
        var text = HasSelection ? SelectedText : Text;
        return ErrorReport.TryCopy(text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
    }

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

    private static int Advance(string text, Font font) =>
        text.Length == 0 ? 0 : TextRenderer.MeasureText(text, font, Size.Empty, MeasureFlags).Width;

    private static int LineHeight(Font font) => TextRenderer.MeasureText("Ag", font, Size.Empty, MeasureFlags).Height;

    /// <summary>
    /// Breaks one paragraph into lines no wider than <paramref name="maxWidth"/>: at spaces, a word wider than a line is broken by
    /// character. Yields (start, length) pieces of the paragraph; the spaces at a break belong to no line.
    /// </summary>
    internal static IEnumerable<(int Start, int Length)> Wrap(string paragraph, Font font, int maxWidth)
    {
        if (paragraph.Length == 0) yield break;
        var average = Math.Max(3.0, Advance("abcdefghijklmnopqrstuvwxyzABCDEFGHIJ0123456789", font) / 46.0);
        var guess = Math.Max(1, (int)(maxWidth / average));
        var pos = 0;
        while (pos < paragraph.Length)
        {
            var remaining = paragraph.Length - pos;
            int fit;
            if (remaining <= guess * 2 && Advance(paragraph.Substring(pos), font) <= maxWidth)
            {
                fit = remaining;
            }
            else
            {
                // Largest number of characters that still fits: binary search around the estimate
                var hi = Math.Min(remaining, guess * 2 + 2);
                var lo = Math.Max(1, Math.Min(hi, guess / 2));
                if (lo > 1 && Advance(paragraph.Substring(pos, lo), font) > maxWidth) lo = 1;
                while (lo < hi)
                {
                    var mid = (lo + hi + 1) / 2;
                    if (Advance(paragraph.Substring(pos, mid), font) <= maxWidth) lo = mid;
                    else hi = mid - 1;
                }
                fit = lo;
            }

            if (pos + fit >= paragraph.Length)
            {
                yield return (pos, paragraph.Length - pos);
                yield break;
            }

            var space = paragraph.LastIndexOf(' ', pos + fit, fit + 1);
            int end, next;
            if (space > pos)
            {
                end = space;
                next = space;
                while (next < paragraph.Length && paragraph[next] == ' ') next++;
            }
            else
            {
                end = next = pos + fit;
            }
            yield return (pos, end - pos);
            pos = next;
        }
    }

    /// <summary>Recomputes wrapping (after a text, size, scale or theme change).</summary>
    public void Relayout()
    {
        _lines.Clear();
        var flat = new System.Text.StringBuilder();
        var width = Inner.Width;
        var y = 0;
        var body = Fonts.Get(Points, S);
        var rawLines = Text.Replace("\r\n", "\n").Split('\n');
        foreach (var raw in rawLines)
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
                flat.Append('\n');
                continue;
            }
            y += spaceBefore;
            var height = LineHeight(font);
            var paragraphStart = flat.Length;
            foreach (var (start, length) in Wrap(text, font, Math.Max(10, width - indent)))
            {
                _lines.Add(new Line(text.Substring(start, length), font, color, indent, y, height, paragraphStart + start));
                y += height;
            }
            flat.Append(text).Append('\n');
            y += (int)U(2);
        }
        // The last paragraph has no separator after it (select-all copies exactly the text)
        if (flat.Length > 0) flat.Length--;
        _flat = flat.ToString();
        _anchor = Math.Clamp(_anchor, 0, _flat.Length);
        _caret = Math.Clamp(_caret, 0, _flat.Length);
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

    // ---- selection geometry -----------------------------------------------------------------------------------------------------

    /// <summary>Pixels from the start of the line to the character boundary <paramref name="count"/> (the trailing "x" keeps a final space counted).</summary>
    private static int XOf(Line line, int count) =>
        count <= 0 ? 0 : Advance(line.Text[..count] + "x", line.Font) - Advance("x", line.Font);

    private int OffsetAt(Point point)
    {
        if (_lines.Count == 0) return 0;
        var inner = Inner;
        var y = point.Y - inner.Top + _offset;
        var index = _lines.FindLastIndex(l => l.Top <= y);
        if (index < 0) index = 0;
        var line = _lines[index];
        var x = point.X - inner.Left - line.Indent;
        if (x <= 0) return line.Start;
        var length = line.Text.Length;
        if (x >= XOf(line, length)) return line.Start + length;
        int lo = 0, hi = length;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (XOf(line, mid) <= x) lo = mid;
            else hi = mid;
        }
        return line.Start + (x - XOf(line, lo) < XOf(line, hi) - x ? lo : hi);
    }

    private void SelectWordAt(int offset)
    {
        if (_flat.Length == 0) return;
        var at = Math.Min(offset, _flat.Length - 1);
        if (char.IsWhiteSpace(_flat[at]) && at > 0) at--;
        if (char.IsWhiteSpace(_flat[at])) return;
        var start = at;
        var end = at;
        while (start > 0 && !char.IsWhiteSpace(_flat[start - 1])) start--;
        while (end < _flat.Length && !char.IsWhiteSpace(_flat[end])) end++;
        Select(start, end - start);
    }

    // ---- input ------------------------------------------------------------------------------------------------------------------

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
            case Keys.A when e.Control:
                SelectAll();
                e.Handled = true;
                break;
            case Keys.C when e.Control:
            case Keys.Insert when e.Control:
                CopyToClipboard();
                e.Handled = true;
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
        else if (e.Button == MouseButtons.Left)
        {
            _selecting = true;
            _anchor = _caret = OffsetAt(e.Location);
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !(Overflows && e.X >= Thumb().Left - U(6)))
        {
            _selecting = false;
            SelectWordAt(OffsetAt(e.Location));
        }
        base.OnMouseDoubleClick(e);
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
        else if (_selecting && (e.Button & MouseButtons.Left) != 0)
        {
            var inner = Inner;
            // Dragging beyond the top or bottom edge scrolls on
            if (e.Y < inner.Top) ScrollBy(-(int)U(16));
            else if (e.Y > inner.Bottom) ScrollBy((int)U(16));
            _caret = OffsetAt(e.Location);
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        _selecting = false;
        base.OnMouseUp(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    // ---- painting ---------------------------------------------------------------------------------------------------------------

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
        var selStart = SelectionStart;
        var selEnd = SelectionEnd;
        using var selectionBrush = new SolidBrush(Color.FromArgb(P.IsDark ? 120 : 80, P.Accent));
        foreach (var line in _lines)
        {
            var top = inner.Top + line.Top - _offset;
            if (top + line.Height < inner.Top || top > inner.Bottom) continue;
            var left = inner.Left + line.Indent;

            var lineEnd = line.Start + line.Text.Length;
            if (selEnd > selStart && selEnd > line.Start && selStart < lineEnd + 1)
            {
                var from = Math.Clamp(selStart - line.Start, 0, line.Text.Length);
                var to = Math.Clamp(selEnd - line.Start, 0, line.Text.Length);
                var x1 = XOf(line, from);
                var x2 = XOf(line, to);
                // A selection that runs on into the next line also covers the line break: a little tail shows that
                if (selEnd > lineEnd) x2 += (int)U(5);
                if (x2 > x1) g.FillRectangle(selectionBrush, left + x1, top, x2 - x1, line.Height);
            }

            TextRenderer.DrawText(g, line.Text, line.Font, new Rectangle(left, top, inner.Width - line.Indent + (int)U(8), line.Height + 2), line.Color, DrawFlags);
        }
        g.Restore(state);

        if (Overflows)
        {
            var thumb = Thumb();
            Draw.FillRounded(g, thumb, U(2.5f), Draw.Mix(P.BorderStrong, Surface, 0.15f));
        }
    }
}
