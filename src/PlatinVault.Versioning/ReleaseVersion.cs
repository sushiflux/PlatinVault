using System.Globalization;
using System.Text.RegularExpressions;

namespace PlatinVault.Versioning;

/// <summary>
/// A PlatinVault version as it appears in tags and file names: vYear.Month.CommitOfYear for a release, vYear.Month.Commit.Prompts.Lines-beta
/// for a beta (since 2026-11-02), and the former "-rc&lt;N&gt;" tags (rc4-rc6 stay in the public repository as pre-releases; the
/// counters are optional there). Order: year, then commit of the year (the month follows from those), then a release before its own
/// pre-releases, a beta before the older release candidates. The build counters do not take part in the order.
/// A dev-build tag (<c>v2026.11.12.13.557-dev</c>, the user's own test build) deliberately does NOT parse: <see cref="Parse"/> returns null,
/// so <see cref="ReleaseFeed"/> skips it and no beta or release installation is ever offered one (dev builds are not published in the
/// public repository anyway).
/// Shared by the server's update check (all hosts) and the Windows program's installer download - one implementation.
/// </summary>
public readonly partial record struct ReleaseVersion(int Year, int Month, int Commit, bool IsBeta, int? Rc, string Text, bool IsNightly = false) : IComparable<ReleaseVersion>
{
    [GeneratedRegex(@"^v?(\d{4})\.(\d{1,2})\.(\d+)(?:\.\d+\.\d+)?(?:-(?:(beta)|(nightly)|rc(\d+)))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static ReleaseVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = Pattern().Match(text.Trim());
        if (!match.Success) return null;
        int Num(int g) => int.Parse(match.Groups[g].Value, CultureInfo.InvariantCulture);
        return new ReleaseVersion(
            Num(1),
            Num(2),
            Num(3),
            match.Groups[4].Success,
            match.Groups[6].Success ? Num(6) : null,
            text.Trim(),
            match.Groups[5].Success);
    }

    /// <summary>A beta, nightly or former release candidate: offered only while pre-releases are switched on.</summary>
    public bool IsPreRelease => IsBeta || IsNightly || Rc is not null;

    /// <summary>The readable form for the user (<c>v2026.11.12 (13.557 β)</c> / <c>v2026.11.12 (13.557 α)</c>), see <see cref="VersionFormat.DisplayFromTag"/>.</summary>
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
