namespace PlatinVault.Bridge.Ui;

/// <summary>Confirmation window of the uninstall (Windows "Apps" and tray menu): says what will be removed.</summary>
internal sealed class UninstallForm : BrandedForm
{
    private readonly CheckBox _deleteLog;

    public UninstallForm(BridgeEnvironment env)
        : base(BridgeTexts.Uninstall.WindowTitle, BridgeEnvironment.ProductName, BridgeTexts.Uninstall.Subtitle, clientWidth: 520, clientHeight: 384)
    {
        const int margin = 24;
        var bold = new Font(SystemFonts.MessageBoxFont!.FontFamily, 10.5f, FontStyle.Bold);

        var question = Theme.CreateLabel(BridgeTexts.Uninstall.Question, R(margin, 114, 472, 26), Theme.Text, bold);
        var details = Theme.CreateLabel(BridgeTexts.Uninstall.Details, R(margin, 148, 472, 112), Theme.Text);
        var folder = Theme.CreateLabel(env.InstallRoot, R(margin + 18, 264, 454, 22), Theme.Muted);
        folder.AutoEllipsis = true;
        folder.Name = "lblFolder";

        _deleteLog = new CheckBox
        {
            Text = BridgeTexts.Uninstall.DeleteLog,
            Bounds = R(margin, 298, 472, 26),
            ForeColor = Theme.Text,
            Name = "chkDeleteLog",
            UseMnemonic = false,
        };

        var confirm = Theme.CreateButton(BridgeTexts.Uninstall.Confirm, R(margin + 472 - 160, 334, 160, 38), ButtonKind.Danger);
        confirm.Name = "btnUninstall";
        var cancel = Theme.CreateButton(BridgeTexts.Dialog.Cancel, R(margin + 472 - 160 - 12 - 120, 334, 120, 38), ButtonKind.Secondary);
        cancel.Name = "btnCancel";
        confirm.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        CancelButton = cancel;

        Controls.AddRange([question, details, folder, _deleteLog, cancel, confirm]);
    }

    internal bool DeleteLog => _deleteLog.Checked;
}
