using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Companion.Api.Endpoints;

namespace Companion.Api.Services;

/// <summary>How the text of the add dialog's search box is read: an IGDB number, an igdb.com address or a name.</summary>
public enum IgdbSearchKind
{
    /// <summary>All digits: the IGDB game with that id.</summary>
    Id,

    /// <summary>An <c>igdb.com/games/&lt;slug&gt;</c> address: the IGDB game with that slug.</summary>
    Slug,

    /// <summary>Anything else: a name search.</summary>
    Text,
}

/// <summary>A parsed search box entry; Value is already safe to put into an Apicalypse query (digits, a slug, or text without quotes).</summary>
public sealed record IgdbSearchQuery(IgdbSearchKind Kind, string Value);

/// <summary>
/// Search for the "add a game" dialog (<c>GET /api/igdb/search</c>) and the mapping of IGDB platforms onto the platforms of PlatinVault.
/// Part of <see cref="IgdbService"/> (same pacing, token and error rules; a failed call is never cached).
/// </summary>
public sealed partial class IgdbService
{
    public const int MaxSearchLength = 120;
    public const int MaxSearchHits = 20;

    private static readonly TimeSpan SearchLifetime = TimeSpan.FromMinutes(10);
    private const int MaxCachedSearches = 200;

    // In memory only (not igdb.json): searches are throw-away, the facts of a chosen game are cached by GetByIgdbIdAsync
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IgdbSearchResponseDto Result)> _searches = new();

    // Game types that can be added as a game of its own (documented names of the game_types table), compared letters only, lower case
    private static readonly HashSet<string> SearchGameTypes = ["maingame", "remake", "remaster", "standaloneexpansion", "port", "expandedgame"];

    private const string SearchFields =
        "fields name,slug,first_release_date,cover.image_id,platforms.name,platforms.abbreviation,platforms.slug,game_type,version_parent;";

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?igdb\.com/games/([a-z0-9][a-z0-9_\-]{0,119})(?:[/?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex IgdbGameUrlPattern();

    // ASCII digits only: \d also matches the digits of other scripts, which IGDB does not read as an id
    [GeneratedRegex(@"^[0-9]{1,9}\z")]
    private static partial Regex DigitsPattern();

    /// <summary>
    /// Reads the search box: 1-120 characters; all digits = an IGDB id, <c>igdb.com/games/&lt;slug&gt;</c> = that game, else a name. The text is
    /// cleaned of what could break out of the query string ("  \  ; and control characters). Null = nothing usable (empty, too long, only
    /// forbidden characters): the endpoint answers 400.
    /// </summary>
    public static IgdbSearchQuery? ParseSearch(string? input)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxSearchLength)
        {
            return null;
        }

        if (DigitsPattern().IsMatch(text))
        {
            return new IgdbSearchQuery(IgdbSearchKind.Id, text.TrimStart('0') is { Length: > 0 } digits ? digits : "0");
        }
        if (IgdbGameUrlPattern().Match(text) is { Success: true } url)
        {
            return new IgdbSearchQuery(IgdbSearchKind.Slug, url.Groups[1].Value.ToLowerInvariant());
        }

        var clean = new string(text.Where(c => c is not ('"' or '\\' or ';') && !char.IsControl(c)).ToArray());
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        return clean.Length == 0 ? null : new IgdbSearchQuery(IgdbSearchKind.Text, clean);
    }

    /// <summary>
    /// Up to 20 IGDB games for the add dialog. Null = no credentials. By name only main games, remakes, remasters, standalone expansions, ports
    /// and expanded games are kept (no editions, DLC, bundles); an id or an address finds exactly that game. <c>Existing</c> of every hit is
    /// empty here (the endpoint fills it from the vault). Cached for ten minutes per query; throws <see cref="TwitchApiException"/> when IGDB
    /// cannot be reached (nothing is cached then).
    /// </summary>
    public async Task<IgdbSearchResponseDto?> SearchAsync(IgdbSearchQuery query, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var key = $"{query.Kind}:{query.Value.ToLowerInvariant()}";
        if (_searches.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < SearchLifetime)
        {
            return cached.Result;
        }

        var result = await SharedAsync($"search:{key}", token => RunSearchAsync(query, token), ct);
        _searches[key] = (DateTimeOffset.UtcNow, result);
        PruneSearches();
        return result;
    }

    private async Task<IgdbSearchResponseDto> RunSearchAsync(IgdbSearchQuery query, CancellationToken ct)
    {
        var body = query.Kind switch
        {
            IgdbSearchKind.Id => $"{SearchFields} where id = {query.Value}; limit 1;",
            IgdbSearchKind.Slug => $"{SearchFields} where slug = \"{query.Value}\"; limit 1;",
            // version_parent = null leaves out the editions of a game (Deluxe, GOTY, ...)
            _ => $"search \"{query.Value}\"; {SearchFields} where version_parent = null; limit {MaxSearchHits};",
        };
        using var doc = await QueryAsync("games", body, ct);

        // An id or an address names its game exactly: no type filter. By name, a game type outside the list is skipped; if the
        // table cannot be matched to the documented names everything stays (like the series filter)
        HashSet<int>? wantedTypes = null;
        if (query.Kind == IgdbSearchKind.Text)
        {
            var types = await GetGameTypesAsync(ct);
            var wanted = types.Where(p => SearchGameTypes.Contains(LettersOnly(p.Value))).Select(p => p.Key).ToHashSet();
            wantedTypes = wanted.Count > 0 ? wanted : null;
        }

        var hits = new List<IgdbSearchHitDto>();
        foreach (var item in Items(doc.RootElement))
        {
            var id = Int(item, "id");
            var name = Clip(Str(item, "name"), 200);
            if (id is null || name is null)
            {
                continue;
            }
            if (wantedTypes is not null && Int(item, "game_type") is { } type && !wantedTypes.Contains(type))
            {
                continue;
            }

            var release = UnixTime(item, "first_release_date");
            hits.Add(new IgdbSearchHitDto(
                IgdbId: id.Value,
                Name: name,
                ReleaseYear: release?.Year,
                FirstReleaseAt: release,
                CoverUrl: item.TryGetProperty("cover", out var cover) ? ImageUrl(ImageId(cover), "cover_big") : null,
                Platforms: MapPlatforms(item),
                Existing: []));
            if (hits.Count == MaxSearchHits)
            {
                break;
            }
        }
        return new IgdbSearchResponseDto(hits);
    }

    private void PruneSearches()
    {
        if (_searches.Count <= MaxCachedSearches)
        {
            return;
        }
        foreach (var stale in _searches.OrderBy(p => p.Value.At).Take(_searches.Count - MaxCachedSearches / 2).Select(p => p.Key).ToList())
        {
            _searches.TryRemove(stale, out _);
        }
    }

    // ---- Platforms ---------------------------------------------------------------------------------------------------

    /// <summary>The platforms IGDB lists for a game, each with the PlatinVault platform it maps to (null = none of ours).</summary>
    private static List<IgdbPlatformDto> MapPlatforms(JsonElement game)
    {
        var result = new List<IgdbPlatformDto>();
        foreach (var item in Items(game, "platforms").Where(x => x.ValueKind == JsonValueKind.Object))
        {
            var name = Clip(Str(item, "name"), 80);
            if (name is null)
            {
                continue;
            }
            var abbreviation = Clip(Str(item, "abbreviation"), 40);
            if (result.Any(p => p.Name == name))
            {
                continue;
            }
            result.Add(new IgdbPlatformDto(name, abbreviation, IgdbPlatformMap.Map(name, abbreviation, Str(item, "slug"))));
        }
        return result;
    }
}

/// <summary>
/// Maps a platform of IGDB onto one of PlatinVault (<see cref="GamePlatforms"/>) by abbreviation, slug or name - never by IGDB's numeric ids,
/// which are not verified (decision D9). IGDB lists consoles, not stores: "PC" maps to nothing (no PC shop is marked), the shops (Epic, GOG,
/// Ubisoft Connect, EA app, Battle.net) are always chosen by the player.
/// </summary>
public static class IgdbPlatformMap
{
    /// <summary>The PlatinVault platform for an IGDB platform; null when none of ours (PC, PS3, Wii U, mobile, ...).</summary>
    public static PlatformChoiceDto? Map(string? name, string? abbreviation, string? slug)
    {
        var n = Compact(name);
        var a = Compact(abbreviation);
        var s = Compact(slug);

        // PlayStation: exactly the 5 and the 4 (PS VR2, PS Vita, PS3 ... are other platforms)
        if (a == "ps5" || n == "playstation5" || s == "ps5") return new PlatformChoiceDto(GamePlatforms.PlayStation, GamePlatforms.Ps5);
        if (a == "ps4" || n == "playstation4" || s == "ps4" || s == "ps41") return new PlatformChoiceDto(GamePlatforms.PlayStation, GamePlatforms.Ps4);

        // Xbox: the original, 360, One and Series X|S all collect achievements in the same Xbox list family
        if (n.StartsWith("xbox", StringComparison.Ordinal) || a is "xone" or "x360" or "xbox" or "seriesxs" || s.StartsWith("xbox", StringComparison.Ordinal) || s is "seriesxs")
        {
            return new PlatformChoiceDto(GamePlatforms.Xbox, null);
        }

        // Nintendo Switch (and Switch 2): the only Nintendo platform of ours
        if (n.StartsWith("nintendoswitch", StringComparison.Ordinal) || a.StartsWith("switch", StringComparison.Ordinal) || s.StartsWith("switch", StringComparison.Ordinal) || s.StartsWith("nintendoswitch", StringComparison.Ordinal))
        {
            return new PlatformChoiceDto(GamePlatforms.Nintendo, null);
        }

        return null;
    }

    // Letters and digits only, lower case: "Series X|S", "series-x-s" and "SeriesXS" compare alike
    private static string Compact(string? value) =>
        value is null ? string.Empty : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
