using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace PlatinVault.Bridge;

/// <summary>How a start attempt ended. Mirrors the failure reasons the server knows (<c>steam-not-found</c>, <c>start-failed</c>).</summary>
internal enum LaunchOutcome
{
    /// <summary>The steam:// address was handed to Windows (Steam takes over from there).</summary>
    Started,

    /// <summary>Windows has no handler for steam:// - Steam is not installed on this PC (or was never started once).</summary>
    SteamNotFound,

    /// <summary>Any other error while handing the address over.</summary>
    Failed,
}

internal readonly record struct LaunchResult(LaunchOutcome Outcome, string? Detail = null);

/// <summary>
/// The seam between "a launch command arrived" and "Windows opens steam://...". The real implementation starts Steam; a test run
/// (<c>--test-root</c>) gets <see cref="RecordingLauncher"/>, which only writes down what it would have opened - so nothing can
/// ever start a game on a developer's PC while testing.
/// </summary>
internal interface ISteamLauncher
{
    LaunchResult Launch(uint appId);
}

/// <summary>
/// The only Steam address the bridge ever opens: <c>steam://run/&lt;number&gt;</c>, built from an integer. Nothing the server sends is
/// ever put into a URL as text, so a manipulated command cannot open anything else.
/// Source: https://developer.valvesoftware.com/wiki/Steam_browser_protocol ("steam://run/&lt;id&gt;: Runs an application. It will be
/// installed if necessary." - and it opens Steam first if Steam is not running).
/// </summary>
internal static class SteamUrl
{
    public static string Run(uint appId) => string.Create(CultureInfo.InvariantCulture, $"steam://run/{appId}");
}

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

/// <summary>Test launcher: appends the address it would have opened to a file and answers with a fixed outcome. Starts nothing.</summary>
internal sealed class RecordingLauncher(string recordPath, LaunchOutcome outcome) : ISteamLauncher
{
    public LaunchResult Launch(uint appId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
        File.AppendAllText(recordPath, SteamUrl.Run(appId) + Environment.NewLine);
        return outcome == LaunchOutcome.Started ? new LaunchResult(outcome) : new LaunchResult(outcome, "test launcher");
    }
}
