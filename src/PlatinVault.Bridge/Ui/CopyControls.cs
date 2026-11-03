namespace PlatinVault.Bridge.Ui;

/// <summary>
/// The "Copy" button of an error message and the status slot beside it (rule "error messages are always copyable"): a click copies the
/// complete message plus version and time (<see cref="ErrorCopy"/>), the slot then says "Copied" - or "Copy not possible" when the
/// clipboard is busy - and clears itself after a few seconds. Button and slot have their place from the start, so nothing moves.
/// </summary>
internal sealed class CopyControls : IDisposable
{
    private readonly Func<string> _text;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3500 };

    /// <param name="text">The complete message, read when the button is clicked.</param>
    /// <param name="button">Place of the button, in pixels.</param>
    /// <param name="status">Place of the status text, in pixels.</param>
    /// <param name="name">Suffix of the control names ("btnCopy" + name).</param>
    public CopyControls(Func<string> text, Rectangle button, Rectangle status, string name = "")
    {
        _text = text;
        Button = Theme.CreateButton(BridgeTexts.Dialog.Copy.ToString(), button, ButtonKind.Secondary);
        Button.Name = "btnCopy" + name;
        Status = Theme.CreateLabel(string.Empty, status, Theme.Success);
        Status.Name = "lblCopyStatus" + name;
        Status.TextAlign = ContentAlignment.MiddleLeft;
        Button.Click += (_, _) => CopyNow();
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Status.Text = string.Empty;
        };
    }

    public Button Button { get; }

    public Label Status { get; }

    public Control[] Controls => [Button, Status];

    /// <summary>Shows or hides button and status together (a window shows the button only while there is an error to copy).</summary>
    public bool Visible
    {
        get => Button.Visible;
        set => Button.Visible = Status.Visible = value;
    }

    /// <summary>Texts again in the bridge's current language (after a language switch).</summary>
    public void ApplyLanguage() => Button.Text = BridgeTexts.Dialog.Copy.ToString();

    /// <summary>What a click does: copy and report the outcome (also a test aid, a window that was never shown cannot be clicked).</summary>
    public void CopyNow() => ShowOutcome(ErrorCopy.TryCopy(ErrorCopy.Build(_text())));

    public void ShowOutcome(bool ok)
    {
        Status.ForeColor = ok ? Theme.Success : Theme.Danger;
        Status.Text = (ok ? BridgeTexts.Dialog.Copied : BridgeTexts.Dialog.CopyFailed).ToString();
        _timer.Stop();
        _timer.Start();
    }

    public void Dispose() => _timer.Dispose();
}
