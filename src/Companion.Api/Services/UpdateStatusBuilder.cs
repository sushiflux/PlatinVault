using Companion.Api.Configuration;
using Companion.Api.Desktop;
using Companion.Api.Localization;
using Companion.Api.Settings;
using Microsoft.Extensions.Options;
using PlatinVault.Versioning;

namespace Companion.Api.Services;

/// <summary>The state shown in Settings > System > Updates.</summary>
public enum UpdateState
{
    /// <summary>Update checks are switched off for this host (Windows program in a development or test run).</summary>
    Disabled,

    /// <summary>No answer from GitHub yet.</summary>
    Unchecked,

    /// <summary>Nothing newer that the switch allows.</summary>
    UpToDate,

    /// <summary>A newer beta or release is offered (<see cref="UpdateStatus.Available"/>).</summary>
    Available,

    /// <summary>The latest attempt failed (<see cref="UpdateStatus.Error"/>).</summary>
    Error,

    /// <summary>
    /// This is a dev build or a development run (<see cref="VersionKind.Dev"/>): it never looks for betas or releases and never offers one,
    /// so it cannot leave the dev line unnoticed. New dev builds come through the host (Unraid's Update button on the <c>:dev</c> tag, or the
    /// installer from the draft release in the source repository).
    /// </summary>
    DevBuild,
}

/// <summary>
/// What a container should do about its image tag (only reported when PlatinVault does not run as the Windows program: a container
/// cannot exchange its own image, the host does that - Unraid's Update button, a NAS container manager, <c>docker compose pull</c>).
/// </summary>
public enum DockerAdvice
{
    /// <summary>Nothing to change: the running kind matches the switch.</summary>
    None,

    /// <summary>Betas wanted but a release runs: the container most likely uses <c>:latest</c> and should use <c>:beta</c>.</summary>
    UseBeta,

    /// <summary>Betas no longer wanted and a beta runs, no release is newer yet: stay, switch to <c>:latest</c> once a release is out.</summary>
    BetaStays,

    /// <summary>Betas no longer wanted, a beta runs and a release at least as new exists: switching to <c>:latest</c> is safe now.</summary>
    SwitchToLatest,
}

/// <summary>Everything the settings (and the Windows program's tray) need to know about updates. Built by <see cref="UpdateStatusBuilder"/>.</summary>
public sealed record UpdateStatus(
    RunningVersion Running,
    bool BetaUpdatesEnabled,
    bool DesktopHost,
    UpdateState State,
    PublishedRelease? Available,
    PublishedRelease? LatestRelease,
    PublishedRelease? LatestBeta,
    DateTimeOffset? CheckedAt,
    Msg? Error,
    bool Throttled,
    DateTimeOffset? NextManualCheckAt,
    bool CanInstall,
    DockerAdvice Docker,
    UpdateCheckInterval CheckInterval = UpdateCheckIntervals.Default,
    bool LinuxPackageHost = false);

/// <summary>
/// Turns the release list into the update status - pure apart from <see cref="GetAsync"/>, which only collects its inputs. The rules
/// (user decision 2026-11-02): the switch "Beta-Versionen erhalten" (default off) decides whether betas count; the newest allowed version
/// newer than the running one is offered; nothing older is ever offered, so switching the beta channel off keeps a running beta until a
/// release newer than it exists. The Windows program only counts versions with an installer.
/// </summary>
public static class UpdateStatusBuilder
{
    /// <summary>Reads the switch, asks the update service (with <paramref name="mode"/>) and builds the status.</summary>
    public static async Task<UpdateStatus> GetAsync(IServiceProvider services, UpdateCheckMode mode, CancellationToken ct)
    {
        bool betaEnabled;
        UpdateCheckInterval interval;
        using (var scope = services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<SettingsStore>();
            betaEnabled = await store.GetBetaUpdatesEnabledAsync(ct);
            interval = await store.GetUpdateCheckIntervalAsync(ct);
        }
        var running = services.GetRequiredService<AppVersion>().Running;
        // A dev build does not even ask GitHub: there is nothing it could be offered
        var list = running.Kind == VersionKind.Dev
            ? new ReleaseListResult(false, null, null, null, null, false, null)
            : await services.GetRequiredService<UpdateService>().GetAsync(mode, interval, ct);
        var desktop = services.GetService<IDesktopHost>();
        var linuxPackage = desktop is null && services.GetRequiredService<IOptions<CompanionOptions>>().Value.IsLinuxPackage;
        return Build(running, list, betaEnabled, desktop is not null, desktop?.CanInstallUpdates == true, interval, linuxPackage);
    }

    public static UpdateStatus Build(RunningVersion running, ReleaseListResult list, bool betaEnabled, bool desktopHost, bool desktopCanInstall,
        UpdateCheckInterval interval = UpdateCheckIntervals.Default, bool linuxPackage = false)
    {
        var releases = list.Releases ?? [];
        var latestRelease = ReleaseFeed.LatestRelease(releases);
        var latestBeta = ReleaseFeed.LatestBeta(releases);
        // A dev build is never offered anything (see UpdateState.DevBuild)
        var isDev = running.Kind == VersionKind.Dev;
        var available = running.Version is { } current && !isDev
            ? ReleaseFeed.PickUpdate(releases, current, betaEnabled, requireInstaller: desktopHost, requireLinuxPackage: linuxPackage)
            : null;

        var error = list.Error;
        UpdateState state;
        if (isDev) state = UpdateState.DevBuild;
        else if (!list.Enabled) state = UpdateState.Disabled;
        else if (running.Version is null)
        {
            state = UpdateState.Error;
            error = ServerTexts.Updates.VersionUnknown;
        }
        else if (error is not null) state = UpdateState.Error;
        else if (list.Releases is null) state = UpdateState.Unchecked;
        else state = available is null ? UpdateState.UpToDate : UpdateState.Available;

        return new UpdateStatus(
            running,
            betaEnabled,
            desktopHost,
            state,
            state == UpdateState.Available ? available : null,
            latestRelease,
            latestBeta,
            list.CheckedAt,
            state == UpdateState.Error ? error : null,
            list.Throttled,
            list.NextManualCheckAt,
            desktopHost && desktopCanInstall && state == UpdateState.Available && available?.Installer is not null,
            desktopHost || linuxPackage ? DockerAdvice.None : AdviceFor(running, betaEnabled, latestRelease),
            interval,
            linuxPackage);
    }

    /// <summary>
    /// The image-tag advice for a container. The kind of the running build says which tag the container most likely uses (a beta image
    /// comes from <c>:beta</c>, a release from <c>:latest</c> - or from <c>:beta</c> when the release also moved that tag, which is then
    /// right for "betas on"). Never recommends a downgrade: <c>:latest</c> is only suggested once the newest release is at least as new as
    /// the running beta.
    /// </summary>
    public static DockerAdvice AdviceFor(RunningVersion running, bool betaEnabled, PublishedRelease? latestRelease)
    {
        if (running.Kind == VersionKind.Dev || running.Version is not { } current) return DockerAdvice.None;
        if (betaEnabled && running.Kind == VersionKind.Release) return DockerAdvice.UseBeta;
        if (!betaEnabled && running.Kind == VersionKind.Beta)
        {
            return latestRelease is not null && latestRelease.Version >= current ? DockerAdvice.SwitchToLatest : DockerAdvice.BetaStays;
        }
        return DockerAdvice.None;
    }
}
