using System.Globalization;

namespace PlatinVault.Bridge;

/// <summary>How a start attempt ended. Mirrors the failure reasons the server knows (<c>steam-not-found</c>, <c>start-failed</c>).</summary>
internal enum LaunchOutcome
{
    /// <summary>The steam:// address was handed to the operating system (Steam takes over from there).</summary>
    Started,

    /// <summary>The system has no handler for steam:// - Steam is not installed on this machine (or was never started once).</summary>
    SteamNotFound,

    /// <summary>Any other error while handing the address over.</summary>
    Failed,
}

internal readonly record struct LaunchResult(LaunchOutcome Outcome, string? Detail = null);

/// <summary>
/// The seam between "a launch command arrived" and "the system opens steam://...". The real implementations start Steam (Windows shell,
/// Linux <c>xdg-open</c>/<c>steam</c>); a test run (<c>--test-root</c>) gets <see cref="RecordingLauncher"/>, which only writes down
/// what it would have opened - so nothing can ever start a game on a developer's machine while testing.
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

/// <summary>
/// Replaces the running bridge binary by the one the PlatinVault server offers. The state push tells which version and SHA-256 the server
/// has; each platform implements the actual file swap (Windows renames the running exe, Linux replaces the file and lets the service
/// manager restart the process).
/// </summary>
internal interface ISelfUpdater
{
    /// <summary>Returns true when a newer binary is in place and the caller must restart; false in every other case (all logged).</summary>
    Task<bool> TryUpdateAsync(string? latestText, string? expectedSha256, CancellationToken ct);

    /// <summary>Removes what an earlier update left behind. Returns true when nothing is left (the caller stops asking then).</summary>
    bool CleanupLeftovers();
}
