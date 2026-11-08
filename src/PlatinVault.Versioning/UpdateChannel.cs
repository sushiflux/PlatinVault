namespace PlatinVault.Versioning;

/// <summary>
/// The one update channel an installation follows. Exactly one is selected at any time (never two independent switches):
/// <see cref="None"/> = full releases only (the default), <see cref="Beta"/> = betas and full releases (no nightlies),
/// <see cref="Nightly"/> = nightlies and full releases (no betas). A full release always reaches every channel.
/// </summary>
public enum UpdateChannel
{
    None,
    Beta,
    Nightly,
}

/// <summary>Stored ids ("none" / "beta" / "nightly"), the container image tag of a channel and which published versions a channel receives.</summary>
public static class UpdateChannels
{
    public const string NoneId = "none";
    public const string BetaId = "beta";
    public const string NightlyId = "nightly";

    /// <summary>The ids the settings accept, in the order the UI lists the channels.</summary>
    public static readonly IReadOnlyList<string> AllIds = [NoneId, BetaId, NightlyId];

    public static string ToId(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Beta => BetaId,
        UpdateChannel.Nightly => NightlyId,
        _ => NoneId,
    };

    /// <summary>The channel for a stored or submitted id (trimmed, case-insensitive); null when it is none of the three.</summary>
    public static UpdateChannel? TryParse(string? id) => id?.Trim().ToLowerInvariant() switch
    {
        NoneId => UpdateChannel.None,
        BetaId => UpdateChannel.Beta,
        NightlyId => UpdateChannel.Nightly,
        _ => null,
    };

    /// <summary>
    /// The tag of the container image a channel needs: <c>latest</c> (releases only), <c>beta</c> (betas and releases) or <c>nightly</c>
    /// (nightlies and releases). CI moves the tags the same way, so a release reaches all three.
    /// </summary>
    public static string ImageTag(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Beta => "beta",
        UpdateChannel.Nightly => "nightly",
        _ => "latest",
    };

    /// <summary>
    /// Whether a channel receives a published version. A full release: every channel. A nightly: only the Nightly channel. Everything else
    /// that is a pre-release (a beta, a former release candidate, a tag GitHub marks as pre-release): only the Beta channel - so the Nightly
    /// channel is never offered a beta.
    /// </summary>
    public static bool Receives(UpdateChannel channel, PublishedRelease release)
    {
        if (!release.IsPreRelease) return true;
        return release.Version.IsNightly ? channel == UpdateChannel.Nightly : channel == UpdateChannel.Beta;
    }
}
