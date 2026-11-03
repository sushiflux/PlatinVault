namespace PlatinVault.Bridge.Ui;

/// <summary>
/// Message and question window in the bridge's own look (dark header with the app icon, gold line, themed buttons). Replaces
/// Windows' MessageBox, which ignores the app's design. Only opened through <see cref="Dialogs"/>.
/// The text is selectable and scrolls inside its slot when it is very long (an error is always shown in full), and every window with
/// room beside its buttons has a "Copy" button that copies the complete text plus version and time (rule "error messages are always copyable").
/// </summary>
internal sealed class MessageForm : BrandedForm
{
    private const int FormWidth = 480;
    private const int Gutter = 24;
    private const int TextTop = BrandedForm.HeaderHeight + 3 + 22;
    private const int ButtonHeight = 38;
    // A longer text scrolls inside its slot; the window never grows taller than this slot allows
    private const int TextMax = 260;
    private const int CopyWidth = 104;

    private readonly CopyControls? _copy;

    public MessageForm(string text, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        : base(BridgeEnvironment.ProductName, BridgeEnvironment.ProductName, SubtitleFor(icon), FormWidth, FormHeight(text))
    {
        var textHeight = SlotHeight(text);
        var marker = new Panel { Bounds = R(Gutter, TextTop + 2, 4, Math.Max(20, textHeight - 4)), BackColor = AccentFor(icon) };
        var message = new ScrollableText(SystemFonts.MessageBoxFont!, Theme.PageBack)
        {
            Bounds = R(Gutter + 16, TextTop, FormWidth - 2 * Gutter - 16, textHeight),
            Name = "txtMessage",
        };
        message.SetText(text, Theme.Text);
        Controls.AddRange([marker, message]);

        // Buttons right-aligned, the positive answer outermost right (same order as the other bridge windows)
        var answers = buttons switch
        {
            MessageBoxButtons.OKCancel => new[] { (DialogResult.Cancel, BridgeTexts.Dialog.Cancel), (DialogResult.OK, BridgeTexts.Dialog.Ok) },
            MessageBoxButtons.YesNo => new[] { (DialogResult.No, BridgeTexts.Dialog.No), (DialogResult.Yes, BridgeTexts.Dialog.Yes) },
            MessageBoxButtons.YesNoCancel => new[] { (DialogResult.Cancel, BridgeTexts.Dialog.Cancel), (DialogResult.No, BridgeTexts.Dialog.No), (DialogResult.Yes, BridgeTexts.Dialog.Yes) },
            _ => new[] { (DialogResult.OK, BridgeTexts.Dialog.Ok) },
        };

        var buttonTop = TextTop + SlotHeight(text) + 24;
        var right = FormWidth - Gutter;
        var created = new List<Button>();
        for (var i = answers.Length - 1; i >= 0; i--)
        {
            var (result, label) = answers[i];
            var positive = result is DialogResult.OK or DialogResult.Yes;
            var width = positive ? 120 : 110;
            var button = Theme.CreateButton(label.ToString(), R(right - width, buttonTop, width, ButtonHeight), positive ? ButtonKind.Primary : ButtonKind.Secondary);
            button.Name = $"btn{result}";
            button.DialogResult = result;
            right -= width + 12;
            created.Insert(0, button);
        }
        Controls.AddRange(created.ToArray());

        // The "Copy" button at the left of the button row, its status text (Copied / Copy not possible) in the room between the two.
        // Both have their place from the start, so a click moves nothing. A question has nothing to copy; a row without room skips it.
        var statusLeft = Gutter + CopyWidth + 10;
        var statusWidth = right - statusLeft;
        if (icon != MessageBoxIcon.Question && statusWidth >= 110)
        {
            _copy = new CopyControls(() => text, R(Gutter, buttonTop, CopyWidth, ButtonHeight), R(statusLeft, buttonTop, statusWidth, ButtonHeight));
            Controls.AddRange(_copy.Controls);
        }

        // MessageBox semantics: Button1 = first in reading order (left), Esc = Cancel/No/OK
        var defaultIndex = defaultButton switch
        {
            MessageBoxDefaultButton.Button2 => 1,
            MessageBoxDefaultButton.Button3 => 2,
            _ => 0,
        };
        var ordered = buttons switch
        {
            MessageBoxButtons.YesNo => created.OrderByDescending(b => b.DialogResult == DialogResult.Yes).ToList(),
            MessageBoxButtons.OKCancel => created.OrderByDescending(b => b.DialogResult == DialogResult.OK).ToList(),
            MessageBoxButtons.YesNoCancel => [.. created.Where(b => b.DialogResult == DialogResult.Yes), .. created.Where(b => b.DialogResult == DialogResult.No), .. created.Where(b => b.DialogResult == DialogResult.Cancel)],
            _ => created,
        };
        var accept = ordered[Math.Min(defaultIndex, ordered.Count - 1)];
        AcceptButton = accept;
        CancelButton = created.FirstOrDefault(b => b.DialogResult is DialogResult.Cancel or DialogResult.No) ?? created[0];
        ActiveControl = accept;
    }

    /// <summary>The "Copy" button with its status slot (null in a question or when the button row has no room for it).</summary>
    public CopyControls? Copy => _copy;

    /// <summary>What a click on "Copy" does (also a test aid: a window that was never shown cannot be clicked).</summary>
    public void CopyNow() => _copy?.CopyNow();

    public void ShowCopyOutcome(bool ok) => _copy?.ShowOutcome(ok);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _copy?.Dispose();
        base.Dispose(disposing);
    }

    private static string SubtitleFor(MessageBoxIcon icon) => (icon switch
    {
        MessageBoxIcon.Error => BridgeTexts.Dialog.SubtitleError,
        MessageBoxIcon.Warning => BridgeTexts.Dialog.SubtitleWarning,
        MessageBoxIcon.Question => BridgeTexts.Dialog.SubtitleQuestion,
        _ => BridgeTexts.Dialog.SubtitleInfo,
    }).ToString();

    private static Color AccentFor(MessageBoxIcon icon) => icon switch
    {
        MessageBoxIcon.Error => Theme.Danger,
        MessageBoxIcon.Warning => Theme.Gold,
        _ => Theme.Accent,
    };

    private static int FormHeight(string text) => TextTop + SlotHeight(text) + 24 + ButtonHeight + Gutter;

    // Height of the text slot in design units: what the wrapped text needs (a little extra: the rich text box keeps a margin of its own),
    // never more than TextMax - a longer text scrolls
    private static int SlotHeight(string text) => Math.Min(TextMax, MeasureTextHeight(text) + 8);

    // Height of the wrapped text in 96-dpi design units (the form converts design units to real pixels itself)
    private static int MeasureTextHeight(string text)
    {
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        var scale = graphics.DpiY / 96f;
        // The slot indents the text like a label and keeps a few pixels of margin: it wraps as if it were 8 design units narrower. Text widths do
        // not scale exactly with the font size (hinting), so a text at the edge of the line may need another line at another Windows scaling: the
        // measurement is a little narrower than that, a text on the edge gets the extra line at every scaling
        var widthPx = (int)Math.Round((FormWidth - 2 * Gutter - 16 - 8 - 16) * scale);
        var size = TextRenderer.MeasureText(text, SystemFonts.MessageBoxFont, new Size(widthPx, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return (int)Math.Ceiling(size.Height / scale) + 4;
    }
}
