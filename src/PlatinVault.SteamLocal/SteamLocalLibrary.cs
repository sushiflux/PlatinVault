using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace PlatinVault.SteamLocal;

/// <summary>
/// What the local Steam client knows about one app and the dashboard sorts by. Every field is null when unknown - never a stand-in 0.
/// </summary>
/// <param name="ReleaseUnix">Release date as Unix seconds (appinfo.vdf).</param>
/// <param name="Metacritic">Metacritic score 0-100 (appinfo.vdf).</param>
/// <param name="ReviewPercent">Share of positive Steam reviews 0-100 (appinfo.vdf).</param>
/// <param name="ReviewScore">Steam's rating scale 1-9 (appinfo.vdf).</param>
/// <param name="SizeBytes">Size on disk of the installed game (appmanifest_*.acf, <c>SizeOnDisk</c>); null while it is not installed.</param>
/// <param name="InstallSizeBytes">Estimated install size from the app's depots in appinfo.vdf (see <see cref="SteamAppInfoReader"/>), known for installed and not installed games; null without depot data.</param>
public sealed record LocalLibraryEntry(long? ReleaseUnix, int? Metacritic, int? ReviewPercent, int? ReviewScore, long? SizeBytes, long? InstallSizeBytes = null);

/// <summary>
/// Reads the Steam client's own files on this PC, read only, to answer what the Web API cannot: release date, Metacritic score and
/// review rating of every app the client knows (<c>appcache/appinfo.vdf</c>, see <see cref="SteamAppInfoReader"/>) and the size on
/// disk of the installed games (<c>steamapps/appmanifest_*.acf</c> in every library folder named by <c>libraryfolders.vdf</c>).
/// Used directly by the Windows program and, through the bridge, for a server on Unraid.
/// </summary>
public sealed partial class SteamLocalLibraryService(SteamLocalStatsService local, ILogger<SteamLocalLibraryService> logger)
{
    // Rescanning the manifest folders is cheap, but not needed more than about once a minute
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private Dictionary<int, LocalLibraryEntry>? _cached;
    private string _cachedKey = string.Empty;
    private long _checkedTick = long.MinValue;

    [GeneratedRegex("\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathPattern();

    [GeneratedRegex("\"SizeOnDisk\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SizePattern();

    [GeneratedRegex("\"appid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AppIdPattern();

    /// <summary>The facts of this PC's Steam client per app id; null when no Steam is installed here or its files are not readable.</summary>
    public Dictionary<int, LocalLibraryEntry>? Read()
    {
        var steamPath = local.GetSteamPath();
        if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return null;

        lock (_gate)
        {
            var now = Environment.TickCount64;
            if (_cached is not null && now - _checkedTick < MinInterval.TotalMilliseconds) return _cached;
            _checkedTick = now;

            try
            {
                var appInfoFile = Path.Combine(steamPath, "appcache", "appinfo.vdf");
                var manifests = ManifestFiles(steamPath, local.LocationProvider.Platform).ToList();
                // Rebuilt only when the appinfo file or a manifest changed (name, length, last write time)
                var key = string.Join('|',
                    Stamp(appInfoFile),
                    string.Join(';', manifests.Select(Stamp)));
                if (_cached is not null && key == _cachedKey) return _cached;

                var info = SteamAppInfoReader.ReadFile(appInfoFile);
                if (info is null)
                {
                    logger.LogDebug("appinfo.vdf von Steam nicht lesbar oder in einem unbekannten Format - Veröffentlichungsdatum, Metacritic und Bewertungen kommen dann aus dem Store.");
                }
                var sizes = ReadSizes(manifests);
                _cached = Merge(info, sizes);
                _cachedKey = key;
                return _cached;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Lokale Steam-Dateien der Bibliothek nicht lesbar.");
                return _cached;
            }
        }
    }

    /// <summary>Joins the two sources; an app that only has a size (or only appinfo data) still gets an entry.</summary>
    public static Dictionary<int, LocalLibraryEntry> Merge(IReadOnlyDictionary<int, AppInfoCommon>? info, IReadOnlyDictionary<int, long> sizes)
    {
        var result = new Dictionary<int, LocalLibraryEntry>();
        if (info is not null)
        {
            foreach (var (appId, common) in info)
            {
                result[appId] = new LocalLibraryEntry(common.ReleaseUnix, common.Metacritic, common.ReviewPercent, common.ReviewScore, sizes.TryGetValue(appId, out var s) ? s : null, common.InstallSizeBytes);
            }
        }
        foreach (var (appId, size) in sizes)
        {
            result[appId] = result.TryGetValue(appId, out var known)
                ? known with { SizeBytes = size }
                : new LocalLibraryEntry(null, null, null, null, size);
        }
        return result;
    }

    /// <summary>Every <c>appmanifest_*.acf</c> in the steamapps folder of each library folder (the Steam folder itself and the extra ones in libraryfolders.vdf).</summary>
    /// <param name="steamPath">The Steam folder.</param>
    /// <param name="platform">Whose rules apply (see <see cref="LibraryFolders"/>); null = this computer's.</param>
    public static IEnumerable<string> ManifestFiles(string steamPath, SteamPlatform? platform = null)
    {
        foreach (var library in LibraryFolders(steamPath, platform))
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps)) continue;
            foreach (var file in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf")) yield return file;
        }
    }

    /// <summary>
    /// The folders that hold games: Steam's own folder first, then the <c>path</c> entries of libraryfolders.vdf (no duplicates).
    /// Windows reads <c>steamapps/libraryfolders.vdf</c> as always. Linux reads it and <c>config/libraryfolders.vdf</c>: tools on Linux
    /// (Lutris, game-detector) read the one in <c>config/</c>, steamlocate the one in <c>steamapps/</c>; which one a current Linux Steam
    /// keeps up to date is not verified, so both are used. Paths compare case-sensitively on Linux.
    /// </summary>
    /// <param name="steamPath">The Steam folder.</param>
    /// <param name="platform">Whose rules apply; null = this computer's.</param>
    public static IReadOnlyList<string> LibraryFolders(string steamPath, SteamPlatform? platform = null)
    {
        var rules = platform ?? SteamLocator.CurrentPlatform;
        var comparer = SteamLocator.PathComparer(rules);
        var folders = new List<string> { steamPath };
        string[] lists = rules == SteamPlatform.Linux
            ? [Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"), Path.Combine(steamPath, "config", "libraryfolders.vdf")]
            : [Path.Combine(steamPath, "steamapps", "libraryfolders.vdf")];
        foreach (var vdf in lists)
        {
            try
            {
                if (File.Exists(vdf))
                {
                    foreach (Match match in PathPattern().Matches(File.ReadAllText(vdf)))
                    {
                        // VDF escapes backslashes (and quotes) inside values
                        var path = match.Groups[1].Value.Replace("\\\\", "\\", StringComparison.Ordinal);
                        if (path.Length > 0 && !folders.Contains(path, comparer) && Directory.Exists(path)) folders.Add(path);
                    }
                }
            }
            catch (IOException)
            {
                // Unreadable list: the games in Steam's own folder are still found
            }
        }
        return folders;
    }

    /// <summary>App id -> size on disk from the manifest files; a manifest without a positive size (not installed yet) is left out.</summary>
    public static Dictionary<int, long> ReadSizes(IEnumerable<string> manifestFiles)
    {
        var sizes = new Dictionary<int, long>();
        foreach (var file in manifestFiles)
        {
            try
            {
                // A manifest is a few hundred bytes; the head holds appid and SizeOnDisk
                var text = File.ReadAllText(file);
                var appId = AppIdPattern().Match(text) is { Success: true } a && int.TryParse(a.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                    ? id
                    : IdFromFileName(file);
                if (appId > 0 && SizePattern().Match(text) is { Success: true } s &&
                    long.TryParse(s.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size > 0)
                {
                    sizes[appId] = size;
                }
            }
            catch (IOException)
            {
                // Steam may be rewriting this manifest right now: the next read picks it up
            }
        }
        return sizes;
    }

    private static int IdFromFileName(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var underscore = name.LastIndexOf('_');
        return underscore >= 0 && int.TryParse(name[(underscore + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;
    }

    private static string Stamp(string file)
    {
        try
        {
            var info = new FileInfo(file);
            return info.Exists ? $"{info.Name}:{info.Length}:{info.LastWriteTimeUtc.Ticks}" : $"{Path.GetFileName(file)}:-";
        }
        catch (IOException)
        {
            return $"{Path.GetFileName(file)}:?";
        }
    }
}
