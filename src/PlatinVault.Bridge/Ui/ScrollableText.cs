using System.Runtime.InteropServices;

namespace PlatinVault.Bridge.Ui;

/// <summary>
/// A message slot of a fixed size that always shows its text COMPLETELY: the text wraps, and when it is longer than the slot it
/// scrolls inside the slot (vertical scroll bar only while needed, mouse wheel, arrow keys once it has the focus). It is read-only
/// but selectable, so a long error text can be copied. Nothing around the slot ever moves or changes size - that is the point of
/// the window's rigid layout; the text is never cut with "..." (project rule: an error message is shown in full).
/// Looks like the plain labels around it: no border, the colours of <see cref="Theme"/>, the surface colour of whatever it lies on.
/// </summary>
internal sealed class ScrollableText : RichTextBox
{
    private const int GwlStyle = -16;
    private const int WsVscroll = 0x00200000;

    // What the slot shows right now: the same text again (every keystroke re-validates the form) must not reset the scroll position
    private string? _shown;
    private Color _shownColor;
    private int _linePitch;

    public ScrollableText(Font font, Color surface)
    {
        ReadOnly = true;
        Multiline = true;
        WordWrap = true;
        BorderStyle = BorderStyle.None;
        ScrollBars = RichTextBoxScrollBars.Vertical; // shown only while the text is taller than the slot
        DetectUrls = false;
        ShortcutsEnabled = true;
        TabStop = false;
        Font = font;
        // After ReadOnly: a read-only edit control otherwise swaps in the grey "disabled" surface
        BackColor = surface;
        ForeColor = Theme.Muted;
        AccessibleRole = AccessibleRole.StaticText;
    }

    /// <summary>The whole text is the slot's content - there is no shortened copy of it anywhere.</summary>
    public void SetText(string text, Color color)
    {
        if (_shown == text && _shownColor == color) return;
        _shown = text;
        _shownColor = color;
        Text = text;
        ForeColor = color;
        // Without a window handle yet (a window that is built but not shown) the rest happens when the handle is created: forcing the handle
        // of one child before its parent has one leaves that first rich edit blank
        if (IsHandleCreated) Apply();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SnapToWholeLines();
        if (_shown is not null) Apply();
    }

    // The slot keeps the size the window design gives it, rounded DOWN to whole lines of its own font: a text that scrolls then never shows a
    // half-cut line at the bottom edge. The line pitch is measured on the control itself (a rich edit has its own metrics).
    private void SnapToWholeLines()
    {
        // A probe text that wraps into several lines of one paragraph: the pitch is the distance of line 2 to line 3, the top margin what is
        // left of line 2's top after one pitch
        var original = Text;
        Text = new string('A', 3000);
        var second = GetPositionFromCharIndex(GetFirstCharIndexFromLine(1)).Y;
        var third = GetPositionFromCharIndex(GetFirstCharIndexFromLine(2)).Y;
        Text = original;
        var pitch = third - second;
        var margin = Math.Max(0, second - pitch);
        if (pitch <= 0 || ClientSize.Height < margin + pitch) return;
        _linePitch = pitch;
        var snapped = margin + (ClientSize.Height - margin) / pitch * pitch;
        Height -= ClientSize.Height - snapped;
    }

    // Colours the text (a rich edit keeps its colour per character), puts the view at the top and decides whether the slot is a stop of the keyboard
    private void Apply()
    {
        SelectAll();
        // Starts where the text of a label starts (a label keeps a small inset of its own, a rich edit has none)
        SelectionIndent = (int)Math.Round(3 * DeviceDpi / 96f);
        SelectionColor = _shownColor;
        Select(0, 0);
        ScrollToCaret();
        // A text that scrolls must be reachable from the keyboard as well (arrow keys, Page up/down); a short one stays out of the tab order
        TabStop = Overflows;
    }

    /// <summary>How many whole lines the slot shows at once (test aid; the slot is snapped to whole lines when its window handle is created).</summary>
    public int VisibleLines
    {
        get
        {
            _ = Handle;
            return _linePitch > 0 ? Math.Max(1, ClientSize.Height / _linePitch) : 1;
        }
    }

    /// <summary>True while the text is taller than the slot, so it scrolls.</summary>
    public bool Overflows
    {
        get
        {
            _ = Handle; // (test aid and Apply: the handle exists by then)
            if (TextLength == 0) return false;
            // Position of the end of the text when the view is at the top: below the slot's bottom edge = not all of it fits
            var end = GetPositionFromCharIndex(TextLength);
            return end.Y + Font.Height > ClientSize.Height + 1;
        }
    }

    /// <summary>Test aid: is the vertical scroll bar really there (the user can reach the rest of a long text with it)?</summary>
    public bool HasVerticalScrollBar
    {
        get
        {
            _ = Handle;
            return (GetWindowLong(Handle, GwlStyle) & WsVscroll) != 0;
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);
}
