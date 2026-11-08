using System.Globalization;
using System.Text.RegularExpressions;

namespace PlatinVault.Versioning;

/// <summary>
/// A PlatinVault version as it appears in tags and file names: vYear.Month.CommitOfYear for a release, vYear.Month.Commit.Prompts.Lines-beta
/// for a beta (since 2026-11-02), and the former "-rc&lt;N&gt;" tags (rc4-rc6 stay in the public repository as pre-releases; the
/// counters are optional there). Order: year, then commit of the year (the month follows from those), then a release before its own
/// pre-releases, a beta before the older release candidates. The build counters do not take part in the order.
/// A tag ending in <c>-dev</c> (<c>v2026.11.12.13.557-dev</c>) deliberately does NOT parse: <see cref="Parse"/> returns null, so
/// <see cref="ReleaseFeed"/> skips it and no installation is ever offered one. Dev is local only and never published; the skip only
/// guards against a stray tag of that shape (earlier dev builds that may still exist as drafts).
/// Shared by the server's update check (all hosts) and the Windows program's installer download - one implementation.
/// </summary>
public readonly partial record struct ReleaseVersion(int Year, int Month, int Commit, bool IsBeta, int? Rc, string Text, bool IsNightly = false) : IComparable<ReleaseVersion>
{
    // [0-9] on purpose: \d also matches the digits of other scripts, which are no version numbers
    [GeneratedRegex(@"^v?([0-9]{4})\.([0-9]{1,2})\.([0-9]+)(?:\.[0-9]+\.[0-9]+)?(?:-(?:(beta)|(nightly)|rc([0-9]+)))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    /// <summary>The version of a tag or file name; null for anything else, including numbers too large for a version (never throws: the text comes from the release feed).</summary>
    public static ReleaseVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = Pattern().Match(text.Trim());
        if (!match.Success) return null;
        int? Num(int g) => int.TryParse(match.Groups[g].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
        if (Num(1) is not { } year || Num(2) is not { } month || Num(3) is not { } commit) return null;
        var rc = match.Groups[6].Success ? Num(6) : null;
        if (match.Groups[6].Success && rc is null) return null;
        return new ReleaseVersion(
            year,
            month,
            commit,
            match.Groups[4].Success,
            rc,
            text.Trim(),
            match.Groups[5].Success);
    }

    /// <summary>A beta, nightly or former release candidate: offered only while pre-releases are switched on.</summary>
    public bool IsPreRelease => IsBeta || IsNightly || Rc is not null;

    /// <summary>The readable form for the user (<c>v2026.11.12.13.557 (β)</c> / <c>v2026.11.12.13.557 (α)</c>), see <see cref="VersionFormat.DisplayFromTag"/>.</summary>
    public string Display => VersionFormat.DisplayFromTag(Text);

    public int CompareTo(ReleaseVersion other)
    {
        var c = Year.CompareTo(other.Year);
        if (c != 0) return c;
        c = Commit.CompareTo(other.Commit);
        if (c != 0) return c;
        // Same commit: the release is newer than its pre-releases, a beta newer than a nightly, a nightly newer than a legacy release candidate
        return Rank.CompareTo(other.Rank);
    }

    private int Rank => IsBeta ? int.MaxValue - 1 : IsNightly ? int.MaxValue - 2 : Rc ?? int.MaxValue;

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;
}
