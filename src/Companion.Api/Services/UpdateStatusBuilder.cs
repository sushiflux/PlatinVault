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

    /// <summary>Betas/nightlies no longer wanted, a pre-release runs and a release at least as new exists: switching to <c>:latest</c> is safe now.</summary>
    SwitchToLatest,

    /// <summary>Nightlies wanted (α + β) but a release or beta runs: the container should use <c>:nightly</c>.</summary>
    UseNightly,

    /// <summary>Nightlies no longer wanted and a nightly runs, no allowed version is at least as new yet: stay until one catches up.</summary>
    NightlyStays,

    /// <summary>Nightlies no longer wanted (betas still wanted), a nightly runs and a beta or release at least as new exists: switching to <c>:beta</c> is safe now.</summary>
    SwitchToBeta,
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
    bool LinuxPackageHost = false,
    bool NightlyUpdatesEnabled = false,
    PublishedRelease? LatestNightly = null);

/// <summary>
/// Turns the release list into the update status - pure apart from <see cref="GetAsync"/>, which only collects its inputs. The rules
/// (user decision 2026-11-02 / 2026-11-05): the switches "Beta-Versionen erhalten" (β) and "Nightly-Versionen erhalten" (α + β) decide
/// whether betas and nightlies count; the newest allowed version newer than the running one is offered; nothing older is ever offered,
/// so switching a pre-release channel off keeps a running nightly or beta until a newer allowed version exists.
/// </summary>
public static class UpdateStatusBuilder
{
    /// <summary>Reads the switches, asks the update service (with <paramref name="mode"/>) and builds the status.</summary>
    public static async Task<UpdateStatus> GetAsync(IServiceProvider services, UpdateCheckMode mode, CancellationToken ct)
    {
        bool betaEnabled;
        bool nightlyEnabled;
        UpdateCheckInterval interval;
        using (var scope = services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<SettingsStore>();
            betaEnabled = await store.GetBetaUpdatesEnabledAsync(ct);
            nightlyEnabled = await store.GetNightlyUpdatesEnabledAsync(ct);
            interval = await store.GetUpdateCheckIntervalAsync(ct);
        }
        var running = services.GetRequiredService<AppVersion>().Running;
        // A dev build does not even ask GitHub: there is nothing it could be offered
        var list = running.Kind == VersionKind.Dev
            ? new ReleaseListResult(false, null, null, null, null, false, null)
            : await services.GetRequiredService<UpdateService>().GetAsync(mode, interval, ct);
        var desktop = services.GetService<IDesktopHost>();
        var linuxPackage = desktop is null && services.GetRequiredService<IOptions<CompanionOptions>>().Value.IsLinuxPackage;
        return Build(running, list, betaEnabled, desktop is not null, desktop?.CanInstallUpdates == true, interval, linuxPackage, nightlyEnabled);
    }

    public static UpdateStatus Build(RunningVersion running, ReleaseListResult list, bool betaEnabled, bool desktopHost, bool desktopCanInstall,
        UpdateCheckInterval interval = UpdateCheckIntervals.Default, bool linuxPackage = false, bool nightlyEnabled = false)
    {
        var releases = list.Releases ?? [];
        var latestRelease = ReleaseFeed.LatestRelease(releases);
        var latestBeta = ReleaseFeed.LatestBeta(releases);
        var latestNightly = ReleaseFeed.LatestNightly(releases);
        // A dev build is never offered anything (see UpdateState.DevBuild)
        var isDev = running.Kind == VersionKind.Dev;
        var available = running.Version is { } current && !isDev
            ? ReleaseFeed.PickUpdate(releases, current, betaEnabled || nightlyEnabled, requireInstaller: desktopHost, requireLinuxPackage: linuxPackage, includeNightly: nightlyEnabled)
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
            betaEnabled || nightlyEnabled,
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
            desktopHost || linuxPackage ? DockerAdvice.None : AdviceFor(running, betaEnabled || nightlyEnabled, latestRelease, nightlyEnabled, latestBeta),
            interval,
            linuxPackage,
            nightlyEnabled,
            latestNightly);
    }

    /// <summary>
    /// The image-tag advice for a container. The kind of the running build says which tag the container most likely uses (a nightly image
    /// comes from <c>:nightly</c>, a beta from <c>:beta</c>, a release from <c>:latest</c>). Never recommends a downgrade.
    /// </summary>
    public static DockerAdvice AdviceFor(
        RunningVersion running,
        bool betaEnabled,
        PublishedRelease? latestRelease,
        bool nightlyEnabled = false,
        PublishedRelease? latestBeta = null)
    {
        if (running.Kind == VersionKind.Dev || running.Version is not { } current) return DockerAdvice.None;
        if (nightlyEnabled)
        {
            return running.Kind == VersionKind.Nightly ? DockerAdvice.None : DockerAdvice.UseNightly;
        }
        if (betaEnabled)
        {
            if (running.Kind == VersionKind.Release) return DockerAdvice.UseBeta;
            if (running.Kind == VersionKind.Nightly)
            {
                var caughtUp = (latestBeta is not null && latestBeta.Version >= current)
                    || (latestRelease is not null && latestRelease.Version >= current);
                return caughtUp ? DockerAdvice.SwitchToBeta : DockerAdvice.NightlyStays;
            }
            return DockerAdvice.None;
        }
        if (running.Kind == VersionKind.Beta)
        {
            return latestRelease is not null && latestRelease.Version >= current ? DockerAdvice.SwitchToLatest : DockerAdvice.BetaStays;
        }
        if (running.Kind == VersionKind.Nightly)
        {
            return latestRelease is not null && latestRelease.Version >= current ? DockerAdvice.SwitchToLatest : DockerAdvice.NightlyStays;
        }
        return DockerAdvice.None;
    }
}
