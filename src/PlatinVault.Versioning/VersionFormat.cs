using System.Globalization;
using System.Text.RegularExpressions;

namespace PlatinVault.Versioning;

/// <summary>Which kind of build a version belongs to.</summary>
public enum VersionKind
{
    /// <summary>A real, public release.</summary>
    Release,

    /// <summary>A beta build (replaces the former release candidate).</summary>
    Beta,

    /// <summary>
    /// A development run, or a built dev package (commit word "Dev-Build", channel "dev"): the user's own test build, never published.
    /// It never takes part in the update check.
    /// </summary>
    Dev,
}

/// <summary>
/// The single definition of how a version is shown and tagged (user decision 2026-11-02). Input is what the pre-commit hook writes to
/// version.txt: line 1 = <c>v2026.11.12</c> (year.month.commitOfYear), line 2 = <c>v2026.11.12.13.557</c> (plus prompts and changed lines).
/// <list type="bullet">
/// <item>Readable display (UI, installer, GitHub release title): release <c>v2026.11.12 (13.557)</c>, beta <c>v2026.11.12 (13.557 β)</c>,
/// dev <c>v2026.11.12 (13.557-dev)</c>. Without the counters (no line 2): the plain line 1, plus <c>-dev</c> for dev.</item>
/// <item>Technical form (git tag, Docker tag, image label, file names - none of them may contain spaces, parentheses or β):
/// release <c>v2026.11.12</c>, beta <c>v2026.11.12.13.557-beta</c>, dev build <c>v2026.11.12.13.557-dev</c>.</item>
/// </list>
/// scripts/version-display.sh implements the same rules for the pre-commit hook and the release workflow; keep both in step.
/// </summary>
public static partial class VersionFormat
{
    /// <summary>The Greek small letter beta (U+03B2) that marks a beta in the readable form.</summary>
    public const string BetaMark = "β";

    [GeneratedRegex(@"^\d+\.\d+$")]
    private static partial Regex CountersPattern();

    [GeneratedRegex(@"^v?(\d{4}\.\d{1,2}\.\d+)(?:\.(\d+\.\d+))?(?:-(beta|dev|rc\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex TagPattern();

    /// <summary>
    /// Maps a channel name to its kind: <c>beta</c> (and the legacy <c>rc</c>) is a beta, <c>dev</c> a dev build, everything else a
    /// release. A dev build (channel <c>dev</c>) is not the ASP.NET Core Development environment - the image keeps running as Production.
    /// </summary>
    public static VersionKind KindFromChannel(string? channel)
    {
        var value = channel?.Trim();
        if (string.Equals(value, "dev", StringComparison.OrdinalIgnoreCase)) return VersionKind.Dev;
        return string.Equals(value, "beta", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "rc", StringComparison.OrdinalIgnoreCase)
            ? VersionKind.Beta
            : VersionKind.Release;
    }

    /// <summary>The channel name written into release-channel.txt / <c>Companion:ReleaseChannel</c>.</summary>
    public static string ChannelName(VersionKind kind) => kind switch
    {
        VersionKind.Beta => "beta",
        VersionKind.Dev => "dev",
        _ => "release",
    };

    /// <summary>
    /// The "prompts.lines" part of the build id, or null when the build id is missing or is not <paramref name="shortVersion"/> followed by
    /// two numbers.
    /// </summary>
    public static string? Counters(string shortVersion, string? buildId)
    {
        if (string.IsNullOrWhiteSpace(buildId)) return null;
        var prefix = shortVersion.Trim() + ".";
        var id = buildId.Trim();
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var rest = id[prefix.Length..];
        return CountersPattern().IsMatch(rest) ? rest : null;
    }

    /// <summary>The readable form shown to people.</summary>
    public static string Display(string shortVersion, string? buildId, VersionKind kind)
    {
        var version = shortVersion.Trim();
        var counters = Counters(version, buildId);
        if (counters is null) return kind == VersionKind.Dev ? version + "-dev" : version;
        return kind switch
        {
            VersionKind.Beta => $"{version} ({counters} {BetaMark})",
            VersionKind.Dev => $"{version} ({counters}-dev)",
            _ => $"{version} ({counters})",
        };
    }

    /// <summary>
    /// The technical form for git tags, Docker tags, image labels and file names. A beta without counters falls back to
    /// <c>vYear.Month.Commit-beta</c>; a dev build (or development run) returns the build id with <c>-dev</c>, which is the tag of a "Dev-Build".
    /// </summary>
    public static string Tag(string shortVersion, string? buildId, VersionKind kind)
    {
        var version = shortVersion.Trim();
        var counters = Counters(version, buildId);
        var full = counters is null ? version : $"{version}.{counters}";
        return kind switch
        {
            VersionKind.Beta => full + "-beta",
            VersionKind.Dev => full + "-dev",
            _ => version,
        };
    }

    /// <summary>
    /// The readable form for a version known only as a git tag (the update check): <c>v2026.11.12.13.557-beta</c> becomes
    /// <c>v2026.11.12 (13.557 β)</c>, <c>v2026.11.12.13.557-dev</c> becomes <c>v2026.11.12 (13.557-dev)</c>. A release tag carries no
    /// counters and stays as it is; unknown or legacy (<c>-rc&lt;N&gt;</c>) tags are returned unchanged.
    /// </summary>
    public static string DisplayFromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return string.Empty;
        var text = tag.Trim();
        var match = TagPattern().Match(text);
        if (!match.Success) return text;
        var suffix = match.Groups[3].Success ? match.Groups[3].Value.ToLower(CultureInfo.InvariantCulture) : null;
        var counters = match.Groups[2].Success ? match.Groups[2].Value : null;
        var version = "v" + match.Groups[1].Value;
        if (suffix == "beta") return counters is null ? $"{version} {BetaMark}" : $"{version} ({counters} {BetaMark})";
        if (suffix == "dev") return counters is null ? $"{version}-dev" : $"{version} ({counters}-dev)";
        if (suffix is not null) return text;
        return counters is null ? version : $"{version} ({counters})";
    }
}
