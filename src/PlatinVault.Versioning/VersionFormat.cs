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

    /// <summary>A nightly build (rapid interim build channel marked with Greek small letter alpha, α).</summary>
    Nightly,

    /// <summary>
    /// A local development run on the developer machine: never published (no tag, image or package) and never takes part in the update check.
    /// </summary>
    Dev,
}

/// <summary>
/// The single definition of how a version is shown and tagged (user decision 2026-11-02 / 2026-11-05). Input is what the pre-commit hook writes to
/// version.txt: line 1 = <c>v2026.11.12</c> (year.month.commitOfYear), line 2 = <c>v2026.11.12.13.557</c> (plus prompts and changed lines).
/// <list type="bullet">
/// <item>Readable display (UI, installer, GitHub release title, customer changelog headings): all five segments, then the channel symbol
/// in parentheses; a release has no symbol. Release <c>v2026.11.12.13.557</c>, beta <c>v2026.11.12.13.557 (β)</c>,
/// nightly <c>v2026.11.12.13.557 (α)</c>, dev <c>v2026.11.12.13.557 (&lt;/&gt;)</c>. Without the counters (no line 2): the plain line 1 plus the
/// symbol, if there is one - never empty parentheses.</item>
/// <item>Technical form (git tag, Docker tag, image label, file names - none of them may contain spaces, parentheses, β or α):
/// release <c>v2026.11.12</c>, beta <c>v2026.11.12.13.557-beta</c>, nightly <c>v2026.11.12.13.557-nightly</c>, dev <c>v2026.11.12.13.557-dev</c>.</item>
/// <item>Until 2026-11-08 the readable form was <c>v2026.11.12 (13.557)</c> / <c>v2026.11.12 (13.557 β)</c>. Changelog headings that were stamped in that
/// form are never rewritten, so <see cref="LegacyDisplay"/> turns a readable version into the old form for looking up such headings.</item>
/// </list>
/// scripts/version-display.sh implements the same rules for the pre-commit hook and the release workflow; keep both in step.
/// </summary>
public static partial class VersionFormat
{
    /// <summary>The Greek small letter beta (U+03B2) that marks a beta in the readable form.</summary>
    public const string BetaMark = "β";

    /// <summary>The Greek small letter alpha (U+03B1) that marks a nightly build in the readable form.</summary>
    public const string NightlyMark = "α";

    /// <summary>The code symbol that marks a development run in the readable form (a dev build is never tagged or published).</summary>
    public const string DevMark = "</>";

    [GeneratedRegex(@"^\d+\.\d+$")]
    private static partial Regex CountersPattern();

    // The readable form: "v2026.11.12.13.557" optionally followed by " (β)", " (α)" or " (</>)"
    [GeneratedRegex(@"^(v\d{4}\.\d{1,2}\.\d+)\.(\d+\.\d+)(?: \((β|α|</>)\))?$")]
    private static partial Regex ReadablePattern();

    [GeneratedRegex(@"^v?(\d{4}\.\d{1,2}\.\d+)(?:\.(\d+\.\d+))?(?:-(beta|nightly|dev|rc\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex TagPattern();

    /// <summary>
    /// Maps a channel name to its kind: <c>beta</c> (and the legacy <c>rc</c>) is a beta, <c>nightly</c> (or <c>alpha</c>) is a nightly,
    /// <c>dev</c> a development run (still accepted for images and packages built before dev builds were abolished), everything else a release.
    /// </summary>
    public static VersionKind KindFromChannel(string? channel)
    {
        var value = channel?.Trim();
        if (string.Equals(value, "dev", StringComparison.OrdinalIgnoreCase)) return VersionKind.Dev;
        if (string.Equals(value, "nightly", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "alpha", StringComparison.OrdinalIgnoreCase))
            return VersionKind.Nightly;
        return string.Equals(value, "beta", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "rc", StringComparison.OrdinalIgnoreCase)
            ? VersionKind.Beta
            : VersionKind.Release;
    }

    /// <summary>The channel name written into release-channel.txt / <c>Companion:ReleaseChannel</c>.</summary>
    public static string ChannelName(VersionKind kind) => kind switch
    {
        VersionKind.Beta => "beta",
        VersionKind.Nightly => "nightly",
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
        return WithMark(counters is null ? version : $"{version}.{counters}", kind);
    }

    /// <summary>Appends the channel symbol in parentheses; a release has none.</summary>
    private static string WithMark(string version, VersionKind kind) => kind switch
    {
        VersionKind.Beta => $"{version} ({BetaMark})",
        VersionKind.Nightly => $"{version} ({NightlyMark})",
        VersionKind.Dev => $"{version} ({DevMark})",
        _ => version,
    };

    /// <summary>
    /// The readable form in the notation used until 2026-11-08 (<c>v2026.11.12 (13.557)</c>, <c>v2026.11.12 (13.557 β)</c>), or null when
    /// <paramref name="display"/> is not a readable version with counters. Changelog headings are stamped once and never rewritten, so entries
    /// written before the change are still found with this form.
    /// </summary>
    public static string? LegacyDisplay(string? display)
    {
        if (string.IsNullOrWhiteSpace(display)) return null;
        var match = ReadablePattern().Match(display.Trim());
        if (!match.Success) return null;
        var version = match.Groups[1].Value;
        var counters = match.Groups[2].Value;
        return match.Groups[3].Success
            ? match.Groups[3].Value switch
            {
                BetaMark => $"{version} ({counters} {BetaMark})",
                NightlyMark => $"{version} ({counters} {NightlyMark})",
                _ => $"{version} ({counters}-dev)",
            }
            : $"{version} ({counters})";
    }

    /// <summary>
    /// The technical form for git tags, Docker tags, image labels and file names. A beta or nightly without counters falls back to
    /// <c>vYear.Month.Commit-beta</c> / <c>-nightly</c>; a development run returns the build id with <c>-dev</c> (only an identifier in the status - Dev is never tagged or published).
    /// </summary>
    public static string Tag(string shortVersion, string? buildId, VersionKind kind)
    {
        var version = shortVersion.Trim();
        var counters = Counters(version, buildId);
        var full = counters is null ? version : $"{version}.{counters}";
        return kind switch
        {
            VersionKind.Beta => full + "-beta",
            VersionKind.Nightly => full + "-nightly",
            VersionKind.Dev => full + "-dev",
            _ => version,
        };
    }

    /// <summary>
    /// The readable form for a version known only as a git tag (the update check): <c>v2026.11.12.13.557-beta</c> becomes
    /// <c>v2026.11.12.13.557 (β)</c>, <c>v2026.11.12.13.557-nightly</c> becomes <c>v2026.11.12.13.557 (α)</c>,
    /// <c>v2026.11.12.13.557-dev</c> becomes <c>v2026.11.12.13.557 (&lt;/&gt;)</c>. A release tag stays as it is (it carries no counters);
    /// unknown or legacy (<c>-rc&lt;N&gt;</c>) tags are returned unchanged.
    /// </summary>
    public static string DisplayFromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return string.Empty;
        var text = tag.Trim();
        var match = TagPattern().Match(text);
        if (!match.Success) return text;
        var suffix = match.Groups[3].Success ? match.Groups[3].Value.ToLower(CultureInfo.InvariantCulture) : null;
        var version = "v" + match.Groups[1].Value + (match.Groups[2].Success ? "." + match.Groups[2].Value : string.Empty);
        return suffix switch
        {
            "beta" => WithMark(version, VersionKind.Beta),
            "nightly" => WithMark(version, VersionKind.Nightly),
            "dev" => WithMark(version, VersionKind.Dev),
            null => version,
            _ => text,
        };
    }
}
