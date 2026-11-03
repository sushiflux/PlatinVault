using System.Globalization;
using System.Text.Json;

namespace PlatinVault.Versioning;

/// <summary>The Windows installer of a release and its published SHA-256 file (both direct downloads from github.com).</summary>
public sealed record InstallerAsset(string Name, string Url, string ChecksumUrl);

/// <summary>The Linux package "PlatinVault-linux-x64-{tag}.tar.gz" of a release and its published SHA-256 file (both direct downloads from github.com).</summary>
public sealed record LinuxPackageAsset(string Name, string Url, string ChecksumUrl);

/// <summary>
/// One published beta or release of the public repository. <see cref="IsPreRelease"/> is true for a beta tag, a former release
/// candidate tag, or a release GitHub marks as pre-release (a mismatch is treated as the safer "beta", so a release-only installation
/// is never offered it). <see cref="NotesUrl"/> is the release page with its notes; <see cref="Installer"/> is null when the release
/// has no Windows installer (e.g. an image-only build); <see cref="LinuxPackage"/> likewise for the Linux package. <see cref="Notes"/> is the release text (GitHub "body") as plain text, capped at
/// <see cref="ReleaseFeed.MaxNotesLength"/> characters; null when the release has none.
/// </summary>
public sealed record PublishedRelease(ReleaseVersion Version, bool IsPreRelease, string? NotesUrl, DateTimeOffset? PublishedAt, InstallerAsset? Installer, string? Notes = null, LinuxPackageAsset? LinuxPackage = null);

/// <summary>
/// Reads the GitHub releases list of the public repository (<c>GET /repos/{owner}/{repo}/releases</c>) and decides which version to
/// offer. The rule (user decision 2026-11-02, switch "Beta-Versionen erhalten"): off = only releases, on = betas and releases, always
/// the newest of them, and only when it is newer than what runs - so switching the beta channel off never offers a downgrade: a running
/// beta simply stays until a release newer than it exists (a release is always a later commit than the betas before it).
/// Pure functions, used by the server (every host) and the Windows program.
/// </summary>
public static class ReleaseFeed
{
    /// <summary>
    /// The usable entries of a releases list: drafts, tags that are not PlatinVault versions and malformed entries are skipped; links
    /// are only kept when they point to https://github.com (the notes page and the installer downloads).
    /// </summary>
    public static IReadOnlyList<PublishedRelease> Parse(JsonElement releases)
    {
        var result = new List<PublishedRelease>();
        if (releases.ValueKind != JsonValueKind.Array) return result;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object) continue;
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
            if (ReleaseVersion.Parse(Text(release, "tag_name")) is not { } version) continue;

            var markedPreRelease = release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True;
            var notes = Text(release, "html_url") is { } html && IsGitHubUrl(html) ? html : null;
            DateTimeOffset? published = DateTimeOffset.TryParse(Text(release, "published_at"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var at) ? at : null;
            result.Add(new PublishedRelease(version, version.IsPreRelease || markedPreRelease, notes, published, FindInstaller(release), CleanNotes(Text(release, "body")), FindLinuxPackage(release)));
        }
        return result;
    }

    /// <summary>The installer "PlatinVault-Setup-*.exe" together with its ".exe.sha256"; null unless both are github.com downloads.</summary>
    private static InstallerAsset? FindInstaller(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        string? installer = null, installerName = null, checksum = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = Text(asset, "name") ?? string.Empty;
            var url = Text(asset, "browser_download_url");
            if (url is null || !name.StartsWith("PlatinVault-Setup-", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                installer = url;
                installerName = name;
            }
            else if (name.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase))
            {
                checksum = url;
            }
        }
        if (installer is null || checksum is null || installerName is null) return null;
        if (!IsGitHubUrl(installer) || !IsGitHubUrl(checksum)) return null;
        return new InstallerAsset(installerName, installer, checksum);
    }

    /// <summary>The Linux package "PlatinVault-linux-x64-*.tar.gz" together with its ".tar.gz.sha256"; null unless both are github.com downloads.</summary>
    private static LinuxPackageAsset? FindLinuxPackage(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        string? package = null, packageName = null, checksum = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = Text(asset, "name") ?? string.Empty;
            var url = Text(asset, "browser_download_url");
            if (url is null || !name.StartsWith("PlatinVault-linux-x64-", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                package = url;
                packageName = name;
            }
            else if (name.EndsWith(".tar.gz.sha256", StringComparison.OrdinalIgnoreCase))
            {
                checksum = url;
            }
        }
        if (package is null || checksum is null || packageName is null) return null;
        if (!IsGitHubUrl(package) || !IsGitHubUrl(checksum)) return null;
        return new LinuxPackageAsset(packageName, package, checksum);
    }

    /// <summary>
    /// What to offer: the newest release (and, with <paramref name="includeBetas"/>, beta) that is newer than <paramref name="current"/>;
    /// null = up to date. <paramref name="requireInstaller"/>: only releases with a Windows installer count (the Windows program);
    /// <paramref name="requireLinuxPackage"/>: only releases with a Linux package count (the Linux package).
    /// </summary>
    public static PublishedRelease? PickUpdate(IEnumerable<PublishedRelease> releases, ReleaseVersion current, bool includeBetas, bool requireInstaller = false, bool requireLinuxPackage = false) =>
        releases
            .Where(r => includeBetas || !r.IsPreRelease)
            .Where(r => !requireInstaller || r.Installer is not null)
            .Where(r => !requireLinuxPackage || r.LinuxPackage is not null)
            .Where(r => r.Version > current)
            .MaxBy(r => r.Version);

    /// <summary>The newest real release (no beta), or null when none is published yet.</summary>
    public static PublishedRelease? LatestRelease(IEnumerable<PublishedRelease> releases) =>
        releases.Where(r => !r.IsPreRelease).MaxBy(r => r.Version);

    /// <summary>The newest beta that is newer than the newest release (an older beta is no news), or null.</summary>
    public static PublishedRelease? LatestBeta(IEnumerable<PublishedRelease> releases)
    {
        var list = releases as IReadOnlyCollection<PublishedRelease> ?? releases.ToList();
        var release = LatestRelease(list);
        return list.Where(r => r.IsPreRelease && (release is null || r.Version > release.Version)).MaxBy(r => r.Version);
    }

    /// <summary>Longest release text kept (characters): a release body is a few KB; this only protects the status answer and the cache.</summary>
    public const int MaxNotesLength = 64 * 1024;

    /// <summary>
    /// The release body as plain text: control characters (except line breaks and tabs) removed, line endings as "\n", surrounding blank
    /// space trimmed, cut at <see cref="MaxNotesLength"/> (never inside a surrogate pair); null when nothing is left. Nothing is interpreted
    /// here - the UI renders the text safely itself.
    /// </summary>
    internal static string? CleanNotes(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        var text = body.Replace("\r\n", "\n").Replace('\r', '\n');
        var builder = new System.Text.StringBuilder(Math.Min(text.Length, MaxNotesLength + 1));
        foreach (var ch in text)
        {
            if (char.IsControl(ch) && ch is not ('\n' or '\t')) continue;
            builder.Append(ch);
        }
        var clean = builder.ToString().Trim();
        if (clean.Length > MaxNotesLength)
        {
            var length = char.IsHighSurrogate(clean[MaxNotesLength - 1]) ? MaxNotesLength - 1 : MaxNotesLength;
            clean = clean[..length].TrimEnd();
        }
        return clean.Length == 0 ? null : clean;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsGitHubUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com";
}
