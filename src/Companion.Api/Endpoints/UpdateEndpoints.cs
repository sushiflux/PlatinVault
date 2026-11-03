using Companion.Api.Desktop;
using Companion.Api.Localization;
using Companion.Api.Services;
using PlatinVault.Versioning;

namespace Companion.Api.Endpoints;

/// <summary>
/// A published beta or release: technical tag, readable form, the page with its notes (<c>NotesUrl</c>) and the notes themselves as plain
/// text (<c>Notes</c> = the GitHub release body, at most 64 KiB, null when the release has none), so the UI never has to call GitHub itself.
/// <c>LinuxPackageName</c> / <c>LinuxPackageUrl</c> are the Linux package of that release (null when it has none).
/// </summary>
public sealed record UpdateReleaseDto(string Tag, string Display, bool IsBeta, string? NotesUrl, DateTimeOffset? PublishedAt, bool HasInstaller, string? Notes = null, string? LinuxPackageName = null, string? LinuxPackageUrl = null);

/// <summary>
/// Settings > System > Updates. <c>State</c>: "disabled" | "unchecked" | "upToDate" | "available" | "error" | "devBuild" (a dev build is
/// never offered a beta or release and does not ask GitHub); <c>Host</c>: "desktop" (the
/// Windows program, installs itself), "linuxPackage" (the Linux package: download the new package and run its install.sh) or "server" (a container: the image tag decides, see <c>DockerAdvice</c>: "none" | "useBeta" |
/// "betaStays" | "switchToLatest"). <c>Error</c> is complete and in the language of the request. <c>CurrentChannel</c>: "release" |
/// "beta" | "dev". <c>Throttled</c>: a manual check came within a minute of the previous one and was answered from memory.
/// </summary>
public sealed record UpdateStatusDto(
    string CurrentVersion,
    string CurrentTag,
    string CurrentChannel,
    bool BetaUpdatesEnabled,
    string Host,
    string State,
    UpdateReleaseDto? Available,
    UpdateReleaseDto? LatestRelease,
    UpdateReleaseDto? LatestBeta,
    DateTimeOffset? CheckedAt,
    string? Error,
    bool Throttled,
    DateTimeOffset? NextManualCheckAt,
    bool CanInstall,
    string DockerAdvice,
    string ImageRepository,
    // How often the server looks by itself: "startup" | "4h" | "daily" | "weekly" | "monthly" | "never" (setting "updates.check_interval")
    string CheckInterval);

public sealed record InstallUpdateRequest(string? Tag);

/// <summary>Answer of a started update: the installer runs and will quit the program.</summary>
public sealed record InstallUpdateResponse(bool Started, string Tag);

/// <summary>
/// The update check for every host (Windows program, Unraid, NAS, Docker): status (automatic, cached for hours), manual check (at most
/// once a minute) and - only in the Windows program - downloading and starting the installer. Inside the <c>/api</c> group, so POSTs need
/// the X-Requested-With header like every state-changing call.
/// </summary>
public static class UpdateEndpoints
{
    /// <summary>The image the containers run; <c>:latest</c> = releases, <c>:beta</c> = betas and releases (CI moves both on a release).</summary>
    public const string ImageRepository = "ghcr.io/sushiflux/platinvault";

    public static void MapUpdates(this RouteGroupBuilder api)
    {
        api.MapGet("/updates/status", async (IServiceProvider services, CancellationToken ct) =>
            Results.Ok(ToDto(await UpdateStatusBuilder.GetAsync(services, UpdateCheckMode.Automatic, ct))));

        api.MapPost("/updates/check", async (IServiceProvider services, CancellationToken ct) =>
            Results.Ok(ToDto(await UpdateStatusBuilder.GetAsync(services, UpdateCheckMode.Manual, ct))));

        api.MapPost("/updates/install", async (InstallUpdateRequest? req, IServiceProvider services, CancellationToken ct) =>
        {
            if (services.GetService<IDesktopHost>() is not { } desktop)
            {
                return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Updates.InstallTitle, ServerTexts.Updates.InstallOnlyDesktop);
            }
            if (!desktop.CanInstallUpdates)
            {
                return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Updates.InstallTitle, ServerTexts.Updates.InstallNotAllowed);
            }

            // Only what the last check offers can be installed - never a tag the request names on its own
            var tag = req?.Tag?.Trim() ?? string.Empty;
            var status = await UpdateStatusBuilder.GetAsync(services, UpdateCheckMode.CacheOnly, ct);
            if (status.Available is not { Installer: not null } offer || !string.Equals(offer.Version.Text, tag, StringComparison.Ordinal))
            {
                return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Updates.InstallTitle, ServerTexts.Updates.InstallNotOffered(tag));
            }

            var result = await desktop.InstallUpdateAsync(offer, ct);
            return result.Outcome switch
            {
                DesktopUpdateOutcome.Started => Results.Ok(new InstallUpdateResponse(true, offer.Version.Text)),
                DesktopUpdateOutcome.ChecksumMismatch => Problems.Create(StatusCodes.Status502BadGateway, ServerTexts.Updates.InstallTitle, ServerTexts.Updates.InstallChecksumMismatch),
                DesktopUpdateOutcome.SignatureRejected => Problems.Create(StatusCodes.Status502BadGateway, ServerTexts.Updates.InstallTitle, ServerTexts.Updates.InstallSignatureRejected(result.Detail ?? "?")),
                DesktopUpdateOutcome.ProgramSignatureInvalid => Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Updates.InstallTitle, ServerTexts.Updates.InstallProgramSignatureInvalid(result.Detail ?? "?")),
                DesktopUpdateOutcome.NotSupported => Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Updates.InstallTitle, ServerTexts.Updates.InstallNotAllowed),
                _ => Problems.Create(StatusCodes.Status502BadGateway, ServerTexts.Updates.InstallTitle, ServerTexts.Updates.InstallFailed(result.Detail ?? "?")),
            };
        });
    }

    public static UpdateStatusDto ToDto(UpdateStatus status) => new(
        status.Running.Display,
        status.Running.Tag,
        status.Running.Kind switch { VersionKind.Dev => "dev", VersionKind.Beta => "beta", _ => "release" },
        status.BetaUpdatesEnabled,
        status.DesktopHost ? "desktop" : status.LinuxPackageHost ? "linuxPackage" : "server",
        StateId(status.State),
        Release(status.Available),
        Release(status.LatestRelease),
        Release(status.LatestBeta),
        status.CheckedAt,
        status.Error?.ToString(),
        status.Throttled,
        status.NextManualCheckAt,
        status.CanInstall,
        AdviceId(status.Docker),
        ImageRepository,
        UpdateCheckIntervals.ToId(status.CheckInterval));

    private static UpdateReleaseDto? Release(PublishedRelease? release) => release is null
        ? null
        : new UpdateReleaseDto(release.Version.Text, release.Version.Display, release.IsPreRelease, release.NotesUrl, release.PublishedAt, release.Installer is not null, release.Notes, release.LinuxPackage?.Name, release.LinuxPackage?.Url);

    private static string StateId(UpdateState state) => state switch
    {
        UpdateState.Disabled => "disabled",
        UpdateState.Unchecked => "unchecked",
        UpdateState.UpToDate => "upToDate",
        UpdateState.Available => "available",
        UpdateState.DevBuild => "devBuild",
        _ => "error",
    };

    private static string AdviceId(DockerAdvice advice) => advice switch
    {
        DockerAdvice.UseBeta => "useBeta",
        DockerAdvice.BetaStays => "betaStays",
        DockerAdvice.SwitchToLatest => "switchToLatest",
        _ => "none",
    };
}
