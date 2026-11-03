using PlatinVault.Windows.Localization;

namespace PlatinVault.Windows.Ui;

/// <summary>
/// The "Copy" button of an error message with its status slot beside it (rule "error messages are always copyable"). The button copies
/// the complete message plus app version and time (<see cref="ErrorReport.Build"/>); afterwards the slot says "Copied" - or, when the
/// clipboard is held by another program, "Copy not possible" - and clears itself after a few seconds. Both live in a fixed place: the
/// button never changes size and the status text only fills a slot reserved for it, so nothing moves (rigid layout).
/// </summary>
public sealed class CopyBar
{
    private readonly Func<string> _text;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3500 };

    /// <param name="text">The complete message, read at the moment of the click (it may have changed since the window was built).</param>
    /// <param name="name">Suffix of the control names ("btnCopy" + name).</param>
    public CopyBar(Func<string> text, float scale, Palette palette, Color surface, string name = "")
    {
        _text = text;
        Button = new PvButton
        {
            Text = CommonTexts.Copy,
            Kind = PvButtonKind.Secondary,
            Glyph = Glyphs.Copy,
            S = scale,
            P = palette,
            Surface = surface,
            Name = "btnCopy" + name,
            AccessibleName = CommonTexts.Copy,
        };
        Status = new PvText
        {
            Points = 9f,
            Style = FontStyle.Bold,
            Align = TextFormatFlags.Left | TextFormatFlags.VerticalCenter,
            S = scale,
            P = palette,
            Surface = surface,
            Name = "lblCopyStatus" + name,
            AccessibleRole = AccessibleRole.Alert,
        };
        Button.Click += (_, _) => Copy();
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Status.Text = string.Empty;
        };
        Button.Disposed += (_, _) => _timer.Dispose();
    }

    public PvButton Button { get; }

    public PvText Status { get; }

    public IEnumerable<Control> Controls => [Button, Status];

    /// <summary>Puts the button at the left of <paramref name="bounds"/> (pixels) and the status slot in the rest of it.</summary>
    public void Place(Rectangle bounds, int gap)
    {
        var width = Math.Min(bounds.Width, Button.NeededWidth + gap / 2);
        Button.Bounds = new Rectangle(bounds.Left, bounds.Top, width, bounds.Height);
        var statusLeft = bounds.Left + width + gap;
        Status.Bounds = new Rectangle(statusLeft, bounds.Top, Math.Max(0, bounds.Right - statusLeft), bounds.Height);
    }

    /// <summary>Copies the message and reports the outcome in the status slot. Returns whether the clipboard accepted it.</summary>
    public bool Copy()
    {
        var ok = ErrorReport.TryCopy(ErrorReport.Build(_text()));
        ShowOutcome(ok);
        return ok;
    }

    /// <summary>Writes "Copied" or "Copy not possible" into the status slot (it clears itself after a few seconds). Also used by the layout checks.</summary>
    public void ShowOutcome(bool ok)
    {
        Status.Color = p => ok ? p.Emerald : p.Danger;
        Status.Text = ok ? CommonTexts.Copied : CommonTexts.CopyFailed;
        _timer.Stop();
        _timer.Start();
    }
}
