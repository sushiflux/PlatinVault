namespace PlatinVault.Bridge.Ui;

/// <summary>The uninstall as the user sees it: confirmation window, stopping the bridge, removing everything, result message.</summary>
internal static class UninstallFlow
{
    /// <param name="runningInstance">True when called from the running bridge itself (tray menu); false for <c>--uninstall</c>, which first has to stop the running one.</param>
    /// <returns>True when the bridge was uninstalled.</returns>
    public static bool Run(BridgeEnvironment env, InstanceGuard guard, bool runningInstance)
    {
        using var form = new UninstallForm(env);
        bool confirmed;
        if (UiEnvironment.Headless)
        {
            // A test run never shows the window: it is rendered to a picture and the uninstall counts as confirmed.
            HeadlessUi.Capture(env, form, "uninstall-dialog");
            confirmed = true;
        }
        else
        {
            confirmed = form.ShowDialog() == DialogResult.OK;
        }
        if (!confirmed) return false;

        if (!runningInstance && !StopRunningBridge(env, guard)) return false;

        IReadOnlyList<Msg> done;
        try
        {
            done = Installer.Uninstall(env, form.DeleteLog);
        }
        catch (InstallException ex)
        {
            BridgeLog.Error(ex.Message);
            Dialogs.Error(ex.Message);
            return false;
        }

        BridgeLog.Info(BridgeTexts.Uninstall.DoneLog(done));
        Dialogs.Info(BridgeTexts.Uninstall.DoneDialog(done));
        return true;
    }

    private static bool StopRunningBridge(BridgeEnvironment env, InstanceGuard guard)
    {
        if (!guard.IsAnotherInstanceRunning()) return true;
        if (guard.RequestQuitAndWait(TimeSpan.FromSeconds(8))) return true;

        // An old build without quit handling. The user already confirmed the uninstall; test runs never force anything.
        if (!env.IsTest)
        {
            InstanceGuard.KillOtherInstances();
            if (guard.RequestQuitAndWait(TimeSpan.FromSeconds(4))) return true;
        }

        Dialogs.Warning(BridgeTexts.Uninstall.CannotQuit);
        return false;
    }
}
