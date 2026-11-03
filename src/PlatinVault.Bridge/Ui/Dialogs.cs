namespace PlatinVault.Bridge.Ui;

/// <summary>
/// The only place that opens message boxes - always our own <see cref="MessageForm"/>, never Windows' MessageBox. In a test
/// run (<see cref="UiEnvironment.Headless"/>) nothing may appear on the real desktop, so the question is logged and answered
/// with a default instead.
/// </summary>
internal static class Dialogs
{
    public static DialogResult Show(IWin32Window? owner, string text, MessageBoxButtons buttons, MessageBoxIcon icon,
        DialogResult testAnswer, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        if (UiEnvironment.Headless)
        {
            BridgeLog.Info($"Meldungsfenster (Testlauf, nicht angezeigt, Antwort {testAnswer}): {text.Replace('\n', ' ')}");
            return testAnswer;
        }

        // Our own window instead of Windows' MessageBox, so every message looks like the rest of PlatinVault
        using var form = new MessageForm(text, buttons, icon, defaultButton);
        if (owner is null)
        {
            form.StartPosition = FormStartPosition.CenterScreen;
            form.TopMost = true;
            return form.ShowDialog();
        }
        form.StartPosition = FormStartPosition.CenterParent;
        return form.ShowDialog(owner);
    }

    public static void Info(string text) => Show(null, text, MessageBoxButtons.OK, MessageBoxIcon.Information, DialogResult.OK);

    public static void Warning(string text) => Show(null, text, MessageBoxButtons.OK, MessageBoxIcon.Warning, DialogResult.OK);

    public static void Error(string text) => Show(null, text, MessageBoxButtons.OK, MessageBoxIcon.Error, DialogResult.OK);
}
