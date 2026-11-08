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

    /// <summary>Nothing newer that the selected channel receives.</summary>
    UpToDate,

    /// <summary>A newer version of the selected channel is offered (<see cref="UpdateStatus.Available"/>).</summary>
    Available,

    /// <summary>The latest attempt failed (<see cref="UpdateStatus.Error"/>).</summary>
    Error,

    /// <summary>
    /// This is a development run on the developer's own machine (<see cref="VersionKind.Dev"/>): it never looks for nightlies, betas or releases
    /// and never offers one. Dev has no published tag, image or package, so there is nothing it could be updated to.
    /// </summary>
    DevBuild,
}

/// <summary>
/// What a container should do about its image tag (only reported when PlatinVault does not run as the Windows program: a container
/// cannot exchange its own image, the host does that - Unraid's Update button, a NAS container manager, <c>docker compose pull</c>).
/// </summary>
public enum DockerAdvice
{
    /// <summary>Nothing to change: the running kind matches the selected channel.</summary>
    None,

    /// <summary>Beta channel selected but a release runs: the container most likely uses <c>:latest</c> and should use <c>:beta</c>.</summary>
    UseBeta,

    /// <summary>No channel selected and a beta runs, no release is at least as new yet: stay, switch to <c>:latest</c> once a release is out.</summary>
    BetaStays,

    /// <summary>No channel selected, a pre-release runs and a release at least as new exists: switching to <c>:latest</c> is safe now.</summary>
    SwitchToLatest,

    /// <summary>Nightly channel selected but a release or beta runs and the move is not a downgrade: the container should use <c>:nightly</c>.</summary>
    UseNightly,

    /// <summary>A nightly runs on a channel that does not receive nightlies (none or Beta), nothing the channel receives is at least as new yet: stay until one catches up.</summary>
    NightlyStays,

    /// <summary>Beta channel selected, a nightly runs and a beta or release at least as new exists: switching to <c>:beta</c> is safe now.</summary>
    SwitchToBeta,

    /// <summary>
    /// Nightly channel selected and a beta runs, but no nightly or release is at least as new yet: moving to <c>:nightly</c> now would be a
    /// downgrade, so stay on <c>:beta</c> until a nightly or release catches up.
    /// </summary>
    BetaStaysForNightly,
}

/// <summary>Everything the settings (and the Windows program's tray) need to know about updates. Built by <see cref="UpdateStatusBuilder"/>.</summary>
public sealed record UpdateStatus(
    RunningVersion Running,
    UpdateChannel Channel,
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
    PublishedRelease? LatestNightly = null);

/// <summary>
/// Turns the release list into the update status - pure apart from <see cref="GetAsync"/>, which only collects its inputs. The rule: ONE
/// channel is selected - none (full releases only), Beta (betas and releases) or Nightly (nightlies and releases, no betas); the newest
/// version that channel receives and that is newer than the running one is offered; nothing older is ever offered, so changing the channel
/// keeps a running nightly or beta until a newer version of the chosen channel exists.
/// </summary>
public static class UpdateStatusBuilder
{
    /// <summary>Reads the channel, asks the update service (with <paramref name="mode"/>) and builds the status.</summary>
    public static async Task<UpdateStatus> GetAsync(IServiceProvider services, UpdateCheckMode mode, CancellationToken ct)
    {
        UpdateChannel channel;
        UpdateCheckInterval interval;
        using (var scope = services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<SettingsStore>();
            channel = await store.GetUpdateChannelAsync(ct);
            interval = await store.GetUpdateCheckIntervalAsync(ct);
        }
        var running = services.GetRequiredService<AppVersion>().Running;
        // A dev build does not even ask GitHub: there is nothing it could be offered
        var list = running.Kind == VersionKind.Dev
            ? new ReleaseListResult(false, null, null, null, null, false, null)
            : await services.GetRequiredService<UpdateService>().GetAsync(mode, interval, ct);
        var desktop = services.GetService<IDesktopHost>();
        var linuxPackage = desktop is null && services.GetRequiredService<IOptions<CompanionOptions>>().Value.IsLinuxPackage;
        return Build(running, list, channel, desktop is not null, desktop?.CanInstallUpdates == true, interval, linuxPackage);
    }

    public static UpdateStatus Build(RunningVersion running, ReleaseListResult list, UpdateChannel channel, bool desktopHost, bool desktopCanInstall,
        UpdateCheckInterval interval = UpdateCheckIntervals.Default, bool linuxPackage = false)
    {
        var releases = list.Releases ?? [];
        var latestRelease = ReleaseFeed.LatestRelease(releases);
        var latestBeta = ReleaseFeed.LatestBeta(releases);
        var latestNightly = ReleaseFeed.LatestNightly(releases);
        // A dev build is never offered anything (see UpdateState.DevBuild)
        var isDev = running.Kind == VersionKind.Dev;
        var available = running.Version is { } current && !isDev
            ? ReleaseFeed.PickUpdate(releases, current, channel, requireInstaller: desktopHost, requireLinuxPackage: linuxPackage)
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
            channel,
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
            desktopHost || linuxPackage ? DockerAdvice.None : AdviceFor(running, channel, latestRelease, latestBeta, latestNightly),
            interval,
            linuxPackage,
            latestNightly);
    }

    /// <summary>
    /// The image-tag advice for a container. The kind of the running build says which tag the container most likely uses (a nightly image
    /// comes from <c>:nightly</c>, a beta from <c>:beta</c>, a release from <c>:latest</c>); the selected channel says which tag it needs
    /// (<see cref="UpdateChannels.ImageTag"/>). A move to another tag is only advised when the new tag already carries something at least as
    /// new as what runs - a full release reaches all three tags, so one at least as new as the running build always makes the move safe.
    /// Never recommends a downgrade.
    /// </summary>
    public static DockerAdvice AdviceFor(
        RunningVersion running,
        UpdateChannel channel,
        PublishedRelease? latestRelease,
        PublishedRelease? latestBeta = null,
        PublishedRelease? latestNightly = null)
    {
        if (running.Kind == VersionKind.Dev || running.Version is not { } current) return DockerAdvice.None;
        var releaseCaughtUp = latestRelease is not null && latestRelease.Version >= current;
        var betaCaughtUp = releaseCaughtUp || (latestBeta is not null && latestBeta.Version >= current);
        var nightlyCaughtUp = releaseCaughtUp || (latestNightly is not null && latestNightly.Version >= current);

        switch (channel)
        {
            case UpdateChannel.Nightly:
                return running.Kind switch
                {
                    VersionKind.Nightly => DockerAdvice.None,
                    VersionKind.Beta => nightlyCaughtUp ? DockerAdvice.UseNightly : DockerAdvice.BetaStaysForNightly,
                    // :nightly also carries every full release, so it is never older than a running release
                    _ => DockerAdvice.UseNightly,
                };
            case UpdateChannel.Beta:
                return running.Kind switch
                {
                    VersionKind.Release => DockerAdvice.UseBeta,
                    VersionKind.Nightly => betaCaughtUp ? DockerAdvice.SwitchToBeta : DockerAdvice.NightlyStays,
                    _ => DockerAdvice.None,
                };
            default:
                return running.Kind switch
                {
                    VersionKind.Beta => releaseCaughtUp ? DockerAdvice.SwitchToLatest : DockerAdvice.BetaStays,
                    VersionKind.Nightly => releaseCaughtUp ? DockerAdvice.SwitchToLatest : DockerAdvice.NightlyStays,
                    _ => DockerAdvice.None,
                };
        }
    }
}
