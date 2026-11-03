using PlatinVault.Versioning;

namespace Companion.Api.Desktop;

/// <summary>
/// Registered only by the Windows program (<c>PlatinVault.Desktop</c>), which runs the server in its own process on the PC that
/// also runs Steam. Its presence tells the server (and through <c>/api/branding</c> the UI) that no bridge is needed and that a game
/// can be started directly - without the browser's "open Steam?" question.
/// </summary>
public interface IDesktopHost
{
    /// <summary>Starts a Steam game on this PC (<c>steam://run/&lt;appId&gt;</c>). Never throws; the result says what happened.</summary>
    LocalLaunchResult LaunchGame(int appId);

    /// <summary>
    /// True when this program may download and run an installer (an installed program; never a development or test run). The
    /// settings only offer "Installieren" then.
    /// </summary>
    bool CanInstallUpdates => false;

    /// <summary>
    /// Downloads the installer of <paramref name="release"/>, checks its SHA-256 against the published file and starts it (the installer
    /// then quits this program with a backup and a clean database stop). Completes once the installer was started or failed.
    /// </summary>
    Task<DesktopUpdateStart> InstallUpdateAsync(PublishedRelease release, CancellationToken ct) =>
        Task.FromResult(new DesktopUpdateStart(DesktopUpdateOutcome.NotSupported, null));
}

/// <summary>Outcome of <see cref="IDesktopHost.LaunchGame"/>; <paramref name="Detail"/> is the system's own error text (not translated).</summary>
public sealed record LocalLaunchResult(bool Started, bool SteamMissing, string? Detail)
{
    public static LocalLaunchResult Ok() => new(true, false, null);
}

public enum DesktopUpdateOutcome
{
    /// <summary>The installer runs (the program will quit shortly).</summary>
    Started,

    /// <summary>The download did not match the published SHA-256 and was deleted.</summary>
    ChecksumMismatch,

    /// <summary>Download or start failed; the detail is the system's message.</summary>
    Failed,

    /// <summary>This host does not install updates (development or test run).</summary>
    NotSupported,
}

/// <summary>Outcome of <see cref="IDesktopHost.InstallUpdateAsync"/>; <paramref name="Detail"/> is the system's own error text (not translated).</summary>
public sealed record DesktopUpdateStart(DesktopUpdateOutcome Outcome, string? Detail);
