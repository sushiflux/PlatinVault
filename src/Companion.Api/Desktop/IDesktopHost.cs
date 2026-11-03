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

    /// <summary>The folders the settings can show and open (program, data, backups, logs), each with its full path. Empty = none.</summary>
    IReadOnlyList<DesktopFolder> Folders => [];

    /// <summary>
    /// Opens one of the known folders (<see cref="DesktopFolderKinds"/>) in Windows Explorer. The caller has validated the kind; the path
    /// is always taken from <see cref="Folders"/>, never from a request. Never throws; the result says what happened.
    /// </summary>
    DesktopFolderOpenResult OpenFolder(string kind) => new(false, null);
}

/// <summary>The four folders of the Windows program the user may open from the app. Anything else is refused.</summary>
public static class DesktopFolderKinds
{
    public const string Program = "program";
    public const string Data = "data";
    public const string Backups = "backups";
    public const string Logs = "logs";

    public static readonly IReadOnlyList<string> All = [Program, Data, Backups, Logs];

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind, StringComparer.Ordinal);
}

/// <summary>A folder of the Windows program: <paramref name="Kind"/> is one of <see cref="DesktopFolderKinds"/>.</summary>
public sealed record DesktopFolder(string Kind, string Path);

/// <summary>Outcome of <see cref="IDesktopHost.OpenFolder"/>; <paramref name="Detail"/> is the system's own error text (not translated).</summary>
public sealed record DesktopFolderOpenResult(bool Opened, string? Detail);

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

    /// <summary>
    /// The installer's digital signature is missing, invalid or from another publisher than the running program; it was deleted and not
    /// started. The detail names the technical reason (not translated).
    /// </summary>
    SignatureRejected,

    /// <summary>
    /// The installed program itself has a digital signature that Windows does not accept (changed or damaged files), so no downloaded
    /// installer can be judged against it; the installer was deleted and not started. The fix is a manual reinstall.
    /// </summary>
    ProgramSignatureInvalid,
}

/// <summary>Outcome of <see cref="IDesktopHost.InstallUpdateAsync"/>; <paramref name="Detail"/> is the system's own error text (not translated).</summary>
public sealed record DesktopUpdateStart(DesktopUpdateOutcome Outcome, string? Detail);
