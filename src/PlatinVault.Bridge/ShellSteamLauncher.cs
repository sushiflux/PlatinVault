using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace PlatinVault.Bridge;

/// <summary>Opens <c>steam://run/&lt;id&gt;</c> through the Windows shell, exactly as a link in a browser would.</summary>
internal sealed class ShellSteamLauncher : ISteamLauncher
{
    // Win32 errors of ShellExecute for "nothing is registered for this address": file not found, no association, SE_ERR_NOASSOC
    private const int ErrorFileNotFound = 2;
    private const int ErrorNoAssociation = 1155;
    private const int SeErrNoAssoc = 31;

    public LaunchResult Launch(uint appId)
    {
        // Steam registers the steam:// handler when it is installed/started. Without it ShellExecute would pop up a system "which app?"
        // box on this PC, so check first and report it instead.
        if (!HasProtocolHandler())
            return new LaunchResult(LaunchOutcome.SteamNotFound, "no steam:// handler is registered in Windows");

        try
        {
            Process.Start(new ProcessStartInfo(SteamUrl.Run(appId)) { UseShellExecute = true })?.Dispose();
            return new LaunchResult(LaunchOutcome.Started);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is ErrorFileNotFound or ErrorNoAssociation or SeErrNoAssoc)
        {
            return new LaunchResult(LaunchOutcome.SteamNotFound, ex.Message);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new LaunchResult(LaunchOutcome.Failed, ex.Message);
        }
    }

    private static bool HasProtocolHandler()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(@"steam\shell\open\command");
            return key?.GetValue(null) is string { Length: > 0 };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Cannot tell: let the shell decide
            return true;
        }
    }
}
