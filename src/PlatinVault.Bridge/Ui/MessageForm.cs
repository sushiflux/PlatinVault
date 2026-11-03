namespace PlatinVault.Bridge.Ui;

/// <summary>
/// Message and question window in the bridge's own look (dark header with the app icon, gold line, themed buttons). Replaces
/// Windows' MessageBox, which ignores the app's design. Only opened through <see cref="Dialogs"/>.
/// </summary>
internal sealed class MessageForm : BrandedForm
{
    private const int FormWidth = 480;
    private const int Gutter = 24;
    private const int TextTop = BrandedForm.HeaderHeight + 3 + 22;
    private const int ButtonHeight = 38;

    public MessageForm(string text, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        : base(BridgeEnvironment.ProductName, BridgeEnvironment.ProductName, SubtitleFor(icon), FormWidth, FormHeight(text))
    {
        var textHeight = MeasureTextHeight(text);
        var marker = new Panel { Bounds = R(Gutter, TextTop + 2, 4, Math.Max(20, textHeight - 4)), BackColor = AccentFor(icon) };
        var message = Theme.CreateLabel(text, R(Gutter + 16, TextTop, FormWidth - 2 * Gutter - 16, textHeight), Theme.Text);
        message.Name = "lblMessage";
        Controls.AddRange([marker, message]);

        // Buttons right-aligned, the positive answer outermost right (same order as the other bridge windows)
        var answers = buttons switch
        {
            MessageBoxButtons.OKCancel => new[] { (DialogResult.Cancel, BridgeTexts.Dialog.Cancel), (DialogResult.OK, BridgeTexts.Dialog.Ok) },
            MessageBoxButtons.YesNo => new[] { (DialogResult.No, BridgeTexts.Dialog.No), (DialogResult.Yes, BridgeTexts.Dialog.Yes) },
            MessageBoxButtons.YesNoCancel => new[] { (DialogResult.Cancel, BridgeTexts.Dialog.Cancel), (DialogResult.No, BridgeTexts.Dialog.No), (DialogResult.Yes, BridgeTexts.Dialog.Yes) },
            _ => new[] { (DialogResult.OK, BridgeTexts.Dialog.Ok) },
        };

        var buttonTop = TextTop + textHeight + 24;
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

    private static int FormHeight(string text) => TextTop + MeasureTextHeight(text) + 24 + ButtonHeight + Gutter;

    // Height of the wrapped text in 96-dpi design units (the form converts design units to real pixels itself)
    private static int MeasureTextHeight(string text)
    {
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        var scale = graphics.DpiY / 96f;
        var widthPx = (int)Math.Round((FormWidth - 2 * Gutter - 16) * scale);
        var size = TextRenderer.MeasureText(text, SystemFonts.MessageBoxFont, new Size(widthPx, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        return (int)Math.Ceiling(size.Height / scale) + 4;
    }
}
