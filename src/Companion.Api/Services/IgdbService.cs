using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Companion.Api.Configuration;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Microsoft.Extensions.Options;

namespace Companion.Api.Services;

/// <summary>
/// Game facts from IGDB (https://api-docs.igdb.com/), looked up by Steam AppID: ratings, genres/themes/modes/perspectives,
/// franchises, similar games, artwork, languages and the average time to beat. IGDB signs in with the Twitch developer app
/// (<see cref="TwitchAuthService"/>); it is free for non-commercial use, allows 4 requests per second and 8 open requests, and
/// must not be called from a browser (CORS) - which is why the server asks and the UI only reads the result.
///
/// Lookup: every game with a Steam page has an entry in IGDB's <c>external_games</c> whose <c>uid</c> is the Steam AppID and
/// whose <c>external_game_source</c> is "Steam"; its <c>game</c> is the IGDB game. Results are cached for a week (IGDB keeps
/// replaced images for 30 days) and persisted in DataDir/cache/igdb.json, so a restart does not ask again. A failed call is
/// never cached; a game IGDB does not know is remembered for a day. Without credentials every method returns null.
/// </summary>
public sealed partial class IgdbService
{
    // Documented legacy value of the (deprecated) external_games "category" enum for Steam; only a fallback when the
    // external_game_sources table cannot be asked
    private const int LegacySteamSourceId = 1;

    private static readonly TimeSpan GameLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan MissingLifetime = TimeSpan.FromDays(1);
    private static readonly TimeSpan ReferenceLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan PruneAfter = TimeSpan.FromDays(60);

    // Limit is 4 requests per second and 8 open requests; stay clearly below both
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(270);
    private const int MaxOpenRequests = 4;
    private const int MaxSimilarGames = 12;
    private const int MaxImages = 12;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public sealed record GameCacheEntry(DateTimeOffset FetchedAt, IgdbGameDto? Game);
    public sealed record IdCacheEntry(DateTimeOffset FetchedAt, int? IgdbId);
    public sealed record SteamSourceEntry(DateTimeOffset FetchedAt, int Id);
    public sealed record LanguageRef(string Name, string? NativeName);
    public sealed record LanguageTable(DateTimeOffset FetchedAt, Dictionary<int, LanguageRef> Items);
    public sealed record SupportTypeTable(DateTimeOffset FetchedAt, Dictionary<int, string> Items);
    public sealed record GameTypeTable(DateTimeOffset FetchedAt, Dictionary<int, string> Items);

    /// <summary>One game of a series as IGDB knows it (no user data: owned/vault/trophies are added per request).</summary>
    public sealed record SeriesGame(int IgdbId, string Name, string? CoverUrl, int? ReleaseYear, int? SteamAppId);

    /// <summary>The series a game belongs to: Source is "collection" (IGDB's series) or "franchise" (fallback); Games sorted by release.</summary>
    public sealed record SeriesInfo(string Name, string Source, List<SeriesGame> Games);

    /// <summary>Series of one IGDB game (keyed by its IGDB id); Series null = no series (or fewer than two games).</summary>
    public sealed record SeriesCacheEntry(DateTimeOffset FetchedAt, SeriesInfo? Series);

    public sealed class PersistedState
    {
        public Dictionary<int, GameCacheEntry> Games { get; set; } = [];
        public Dictionary<int, IdCacheEntry> Ids { get; set; } = [];
        public SteamSourceEntry? SteamSource { get; set; }
        public LanguageTable? Languages { get; set; }
        public SupportTypeTable? SupportTypes { get; set; }
        public GameTypeTable? GameTypes { get; set; }
        public Dictionary<int, SeriesCacheEntry> Series { get; set; } = [];
    }

    private readonly TwitchAuthService _auth;
    private readonly ILogger<IgdbService> _logger;
    private readonly string _cacheFile;
    private readonly ConcurrentDictionary<int, GameCacheEntry> _games = new();
    private readonly ConcurrentDictionary<int, IdCacheEntry> _ids = new();
    private readonly ConcurrentDictionary<int, SeriesCacheEntry> _series = new();
    private readonly ConcurrentDictionary<string, object> _inFlight = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly SemaphoreSlim _paceLock = new(1, 1);
    private readonly SemaphoreSlim _openRequests = new(MaxOpenRequests, MaxOpenRequests);
    private DateTime _nextSlotUtc = DateTime.MinValue;
    // Replaced as a whole (never modified in place), read without a lock
    private volatile SteamSourceEntry? _steamSource;
    private volatile LanguageTable? _languages;
    private volatile SupportTypeTable? _supportTypes;
    private volatile GameTypeTable? _gameTypes;

    public IgdbService(TwitchAuthService auth, IOptions<CompanionOptions> options, ILogger<IgdbService> logger)
    {
        _auth = auth;
        _logger = logger;
        var dir = Path.Combine(options.Value.DataDir, "cache");
        Directory.CreateDirectory(dir);
        _cacheFile = Path.Combine(dir, "igdb.json");
        Load();
    }

    /// <summary>True while the Twitch Client-ID and Client-Secret are stored.</summary>
    public bool IsConfigured => _auth.HasCredentials;

    /// <summary>
    /// IGDB's id for the game with this Steam AppID (null = no credentials, or IGDB does not know the game). Costs one request
    /// the first time, nothing afterwards. Throws <see cref="TwitchApiException"/> when IGDB cannot be reached.
    /// </summary>
    public async Task<int?> GetIgdbIdAsync(int steamAppId, CancellationToken ct)
    {
        if (steamAppId <= 0 || !IsConfigured)
        {
            return null;
        }

        if (_games.TryGetValue(steamAppId, out var game) && IsFresh(game.FetchedAt, game.Game is null))
        {
            return game.Game?.IgdbId;
        }
        if (_ids.TryGetValue(steamAppId, out var known) && IsFresh(known.FetchedAt, known.IgdbId is null))
        {
            return known.IgdbId;
        }

        return await SharedAsync($"id:{steamAppId}", token => ResolveIdAsync(steamAppId, token), ct);
    }

    /// <summary>
    /// All IGDB facts for the game with this Steam AppID (null = no credentials, or IGDB does not know the game). Throws
    /// <see cref="TwitchApiException"/> when IGDB cannot be reached and no earlier result is cached.
    /// </summary>
    public async Task<IgdbGameDto?> GetBySteamAppIdAsync(int steamAppId, CancellationToken ct)
    {
        if (steamAppId <= 0 || !IsConfigured)
        {
            return null;
        }

        _games.TryGetValue(steamAppId, out var cached);
        if (cached is not null && IsFresh(cached.FetchedAt, cached.Game is null))
        {
            return cached.Game;
        }

        try
        {
            return await SharedAsync($"game:{steamAppId}", token => FetchGameAsync(steamAppId, token), ct);
        }
        catch (TwitchApiException ex) when (cached?.Game is not null)
        {
            // An outdated answer is better than none while IGDB is unreachable
            _logger.LogDebug("IGDB-Abruf für App {AppId} fehlgeschlagen, nutze den älteren Stand: {Message}", steamAppId, ex.Message);
            return cached.Game;
        }
    }

    private static bool IsFresh(DateTimeOffset fetchedAt, bool missing) =>
        DateTimeOffset.UtcNow - fetchedAt < (missing ? MissingLifetime : GameLifetime);

    // ---- Lookups -----------------------------------------------------------------------------------------------------

    private async Task<int?> ResolveIdAsync(int steamAppId, CancellationToken ct)
    {
        var sourceId = await GetSteamSourceIdAsync(ct);

        // uid is the other service's id as a string; game.parent_game / game.version_parent mark DLC and editions, so a
        // base game is preferred when several IGDB entries share the AppID
        using var doc = await QueryAsync(
            "external_games",
            $"fields game.id,game.parent_game,game.version_parent; where uid = \"{steamAppId}\" & external_game_source = {sourceId}; limit 10;",
            ct);

        int? chosen = null;
        foreach (var item in Items(doc.RootElement))
        {
            if (!item.TryGetProperty("game", out var game))
            {
                continue;
            }

            int? id;
            var isVersionOrAddon = false;
            if (game.ValueKind == JsonValueKind.Object)
            {
                id = Int(game, "id");
                isVersionOrAddon = game.TryGetProperty("parent_game", out var parent) && parent.ValueKind == JsonValueKind.Number
                    || game.TryGetProperty("version_parent", out var version) && version.ValueKind == JsonValueKind.Number;
            }
            else
            {
                id = game.ValueKind == JsonValueKind.Number && game.TryGetInt32(out var plain) ? plain : null;
            }

            if (id is null)
            {
                continue;
            }
            if (!isVersionOrAddon)
            {
                chosen = id;
                break;
            }
            chosen ??= id;
        }

        _ids[steamAppId] = new IdCacheEntry(DateTimeOffset.UtcNow, chosen);
        await SaveAsync();
        return chosen;
    }

    private async Task<IgdbGameDto?> FetchGameAsync(int steamAppId, CancellationToken ct)
    {
        var igdbId = await GetIgdbIdAsync(steamAppId, ct);
        if (igdbId is null)
        {
            return await StoreAsync(steamAppId, null);
        }

        using var gameDoc = await QueryAsync(
            "games",
            "fields name,url,summary,storyline,aggregated_rating,aggregated_rating_count,total_rating,total_rating_count," +
            "genres.name,themes.name,game_modes.name,player_perspectives.name,franchise.name,franchises.name,collections.name," +
            "similar_games,cover.image_id,artworks.image_id,screenshots.image_id," +
            $"language_supports.language,language_supports.language_support_type; where id = {igdbId}; limit 1;",
            ct);
        var game = Items(gameDoc.RootElement).FirstOrDefault();
        if (game.ValueKind != JsonValueKind.Object)
        {
            return await StoreAsync(steamAppId, null);
        }

        var similar = await FetchSimilarGamesAsync(game, ct);
        var timeToBeat = await FetchTimeToBeatAsync(igdbId.Value, ct);
        var languages = await MapLanguagesAsync(game, ct);

        var facts = new IgdbGameDto(
            IgdbId: igdbId.Value,
            Name: Clip(Str(game, "name"), 200) ?? $"IGDB {igdbId}",
            Url: SafeIgdbUrl(Str(game, "url")),
            Summary: Clip(Str(game, "summary"), 3000),
            Storyline: Clip(Str(game, "storyline"), 3000),
            AggregatedRating: Rating(game, "aggregated_rating"),
            AggregatedRatingCount: Int(game, "aggregated_rating_count"),
            TotalRating: Rating(game, "total_rating"),
            TotalRatingCount: Int(game, "total_rating_count"),
            Genres: Names(game, "genres"),
            Themes: Names(game, "themes"),
            GameModes: Names(game, "game_modes"),
            PlayerPerspectives: Names(game, "player_perspectives"),
            Franchises: SingleName(game, "franchise").Concat(Names(game, "franchises")).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Collections: Names(game, "collections"),
            SimilarGames: similar,
            CoverUrl: game.TryGetProperty("cover", out var cover) ? ImageUrl(ImageId(cover), "cover_big_2x") : null,
            Artworks: Images(game, "artworks"),
            Screenshots: Images(game, "screenshots"),
            Languages: languages,
            TimeToBeat: timeToBeat,
            FetchedAt: DateTimeOffset.UtcNow);

        return await StoreAsync(steamAppId, facts);
    }

    private async Task<IgdbGameDto?> StoreAsync(int steamAppId, IgdbGameDto? facts)
    {
        _games[steamAppId] = new GameCacheEntry(DateTimeOffset.UtcNow, facts);
        await SaveAsync();
        return facts;
    }

    /// <summary>"Similar games" arrive as ids; one more request turns them into names and covers (single-level expansion only).</summary>
    private async Task<List<IgdbSimilarGameDto>> FetchSimilarGamesAsync(JsonElement game, CancellationToken ct)
    {
        var ids = Items(game, "similar_games")
            .Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _))
            .Select(x => x.GetInt32())
            .Distinct()
            .Take(MaxSimilarGames)
            .ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        using var doc = await QueryAsync("games", $"fields name,cover.image_id; where id = ({string.Join(",", ids)}); limit {ids.Count};", ct);
        var byId = new Dictionary<int, IgdbSimilarGameDto>();
        foreach (var item in Items(doc.RootElement))
        {
            var id = Int(item, "id");
            var name = Clip(Str(item, "name"), 200);
            if (id is null || name is null)
            {
                continue;
            }
            var cover = item.TryGetProperty("cover", out var c) ? ImageUrl(ImageId(c), "cover_big") : null;
            byId[id.Value] = new IgdbSimilarGameDto(id.Value, name, cover);
        }

        // IGDB lists the most similar first - keep that order
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    /// <summary>Hours to finish the game (game_time_to_beats, seconds on IGDB's side); null when nobody submitted times.</summary>
    private async Task<IgdbTimeToBeatDto?> FetchTimeToBeatAsync(int igdbId, CancellationToken ct)
    {
        using var doc = await QueryAsync("game_time_to_beats", $"fields hastily,normally,completely,count; where game_id = {igdbId}; limit 1;", ct);
        var item = Items(doc.RootElement).FirstOrDefault();
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var result = new IgdbTimeToBeatDto(Hours(item, "hastily"), Hours(item, "normally"), Hours(item, "completely"), Int(item, "count"));
        return result.HastilyHours is null && result.NormallyHours is null && result.CompletelyHours is null ? null : result;
    }

    private async Task<List<IgdbLanguageDto>> MapLanguagesAsync(JsonElement game, CancellationToken ct)
    {
        var supports = Items(game, "language_supports").Where(x => x.ValueKind == JsonValueKind.Object).ToList();
        if (supports.Count == 0)
        {
            return [];
        }

        var languages = await GetLanguagesAsync(ct);
        var types = await GetSupportTypesAsync(ct);
        var result = new Dictionary<int, (bool Audio, bool Subtitles, bool Interface)>();
        foreach (var support in supports)
        {
            var languageId = Int(support, "language");
            if (languageId is null || !languages.ContainsKey(languageId.Value))
            {
                continue;
            }

            var typeName = Int(support, "language_support_type") is { } typeId && types.TryGetValue(typeId, out var name) ? name : string.Empty;
            result.TryGetValue(languageId.Value, out var flags);
            result[languageId.Value] = (
                flags.Audio || typeName.Contains("audio", StringComparison.OrdinalIgnoreCase),
                flags.Subtitles || typeName.Contains("subtitle", StringComparison.OrdinalIgnoreCase),
                flags.Interface || typeName.Contains("interface", StringComparison.OrdinalIgnoreCase));
        }

        return result
            .Select(p => new IgdbLanguageDto(languages[p.Key].Name, languages[p.Key].NativeName, p.Value.Audio, p.Value.Subtitles, p.Value.Interface))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ---- Series (collection / franchise) -----------------------------------------------------------------------------

    private const int MaxSeriesGames = 30;
    private const int MaxSeriesLookup = 500;

    // Game types that count as a game of the series (documented enum names of the game "category": main_game, remake, remaster,
    // standalone_expansion); everything else (DLC, expansion, bundle, mod, episode, season, port, pack, ...) is left out.
    // Compared letters only, lower case, so "Main Game" and "main_game" are the same.
    private static readonly HashSet<string> SeriesGameTypes = ["maingame", "remake", "remaster", "standaloneexpansion"];

    /// <summary>
    /// The series of the game with this Steam AppID: IGDB's collection ("Collection, AKA Series"), else its franchise, with the main
    /// games sorted by release (at most 30, the game itself always included). Null = no credentials, game unknown to IGDB, or it is
    /// in no collection/franchise with at least two games. Cached for a week per IGDB game; errors are never cached.
    /// Throws <see cref="TwitchApiException"/> when IGDB cannot be reached and nothing is cached.
    /// </summary>
    public async Task<SeriesInfo?> GetSeriesAsync(int steamAppId, CancellationToken ct)
    {
        if (steamAppId <= 0 || !IsConfigured)
        {
            return null;
        }

        var igdbId = await GetIgdbIdAsync(steamAppId, ct);
        if (igdbId is null)
        {
            return null;
        }

        _series.TryGetValue(igdbId.Value, out var cached);
        if (cached is not null && IsFresh(cached.FetchedAt, cached.Series is null))
        {
            return cached.Series;
        }

        try
        {
            return await SharedAsync($"series:{igdbId}", token => FetchSeriesAsync(igdbId.Value, token), ct);
        }
        catch (TwitchApiException ex) when (cached?.Series is not null)
        {
            _logger.LogDebug("IGDB-Serie für App {AppId} nicht abrufbar, nutze den älteren Stand: {Message}", steamAppId, ex.Message);
            return cached.Series;
        }
    }

    private async Task<SeriesInfo?> FetchSeriesAsync(int igdbId, CancellationToken ct)
    {
        // 1. Which collection / franchise is the game in, and which games does each contain (single-level expansions only)
        using var groupDoc = await QueryAsync(
            "games",
            "fields collections.name,collections.games,franchise.name,franchise.games,franchises.name,franchises.games; " +
            $"where id = {igdbId}; limit 1;",
            ct);
        var root = Items(groupDoc.RootElement).FirstOrDefault();

        // The series is IGDB's collection; the franchise (main one first) only when there is no usable collection
        var picked = PickGroup(Items(root, "collections"), "collection")
            ?? PickGroup(SingleOrEmpty(root, "franchise").Concat(Items(root, "franchises")), "franchise");
        if (picked is null)
        {
            return await StoreSeriesAsync(igdbId, null);
        }

        // 2. The games: skip editions (version_parent) and DLC/expansions/bundle parts (parent_game) - both documented fields
        var ids = picked.Value.GameIds.Take(MaxSeriesLookup).ToList();
        using var gamesDoc = await QueryAsync(
            "games",
            "fields name,first_release_date,cover.image_id,game_type; " +
            $"where id = ({string.Join(",", ids)}) & version_parent = null & parent_game = null; limit {MaxSeriesLookup};",
            ct);

        // 3. Only main games, remakes, remasters and standalone expansions (by the game_types table); if that table cannot be
        //    matched to the documented names, everything that passed the filters above stays
        var wantedTypes = (await GetGameTypesAsync(ct)).Where(p => SeriesGameTypes.Contains(LettersOnly(p.Value))).Select(p => p.Key).ToHashSet();
        var candidates = new List<(int Id, string Name, string? Cover, long? Release)>();
        foreach (var item in Items(gamesDoc.RootElement))
        {
            var id = Int(item, "id");
            var name = Clip(Str(item, "name"), 200);
            if (id is null || name is null)
            {
                continue;
            }

            var type = Int(item, "game_type");
            var isCurrent = id == igdbId;
            if (!isCurrent && wantedTypes.Count > 0 && type is { } t && !wantedTypes.Contains(t))
            {
                continue;
            }

            var cover = item.TryGetProperty("cover", out var c) ? ImageUrl(ImageId(c), "cover_big") : null;
            long? release = item.TryGetProperty("first_release_date", out var rd) && rd.ValueKind == JsonValueKind.Number && rd.TryGetInt64(out var unix) ? unix : null;
            candidates.Add((id.Value, name, cover, release));
        }

        // Oldest first, games without a release date at the end; a long franchise keeps the first 30 plus the game itself
        var sorted = candidates.OrderBy(g => g.Release ?? long.MaxValue).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (sorted.Count > MaxSeriesGames)
        {
            var kept = sorted.Take(MaxSeriesGames).ToList();
            if (kept.All(g => g.Id != igdbId) && sorted.FirstOrDefault(g => g.Id == igdbId) is { Id: > 0 } current)
            {
                kept[^1] = current;
                kept = [.. kept.OrderBy(g => g.Release ?? long.MaxValue).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)];
            }
            sorted = kept;
        }
        if (sorted.Count < 2)
        {
            return await StoreSeriesAsync(igdbId, null);
        }

        // 4. Steam AppIDs of those games: external_games of the Steam source (same mapping as the AppID -> game lookup)
        var steamIds = new Dictionary<int, int>();
        var sourceId = await GetSteamSourceIdAsync(ct);
        using var externalDoc = await QueryAsync(
            "external_games",
            $"fields game,uid; where game = ({string.Join(",", sorted.Select(g => g.Id))}) & external_game_source = {sourceId}; limit {MaxSeriesLookup};",
            ct);
        foreach (var item in Items(externalDoc.RootElement))
        {
            if (Int(item, "game") is { } gameId && int.TryParse(Str(item, "uid"), out var appId) && appId > 0)
            {
                steamIds.TryAdd(gameId, appId);
            }
        }

        var games = sorted.Select(g => new SeriesGame(
            g.Id,
            g.Name,
            g.Cover,
            g.Release is { } unix ? DateTimeOffset.FromUnixTimeSeconds(unix).Year : null,
            steamIds.TryGetValue(g.Id, out var steamAppId) ? steamAppId : null)).ToList();

        return await StoreSeriesAsync(igdbId, new SeriesInfo(Clip(picked.Value.Name, 200) ?? "Serie", picked.Value.Source, games));
    }

    private async Task<SeriesInfo?> StoreSeriesAsync(int igdbId, SeriesInfo? series)
    {
        _series[igdbId] = new SeriesCacheEntry(DateTimeOffset.UtcNow, series);
        await SaveAsync();
        return series;
    }

    /// <summary>The group (collection or franchise) with the most games, if it has at least two; null otherwise.</summary>
    private static (string Name, string Source, List<int> GameIds)? PickGroup(IEnumerable<JsonElement> groups, string source)
    {
        (string Name, string Source, List<int> GameIds)? best = null;
        foreach (var group in groups.Where(g => g.ValueKind == JsonValueKind.Object))
        {
            var name = Clip(Str(group, "name"), 200);
            var gameIds = Items(group, "games")
                .Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out _))
                .Select(x => x.GetInt32())
                .Distinct()
                .ToList();
            if (name is not null && gameIds.Count >= 2 && (best is null || gameIds.Count > best.Value.GameIds.Count))
            {
                best = (name, source, gameIds);
            }
        }
        return best;
    }

    private static IEnumerable<JsonElement> SingleOrEmpty(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.Object
            ? new[] { p }
            : Array.Empty<JsonElement>();

    private static string LettersOnly(string value) => new(value.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());

    private async Task<Dictionary<int, string>> GetGameTypesAsync(CancellationToken ct)
    {
        if (_gameTypes is { } cached && DateTimeOffset.UtcNow - cached.FetchedAt < ReferenceLifetime)
        {
            return cached.Items;
        }

        return await SharedAsync("ref:game-types", async token =>
        {
            using var doc = await QueryAsync("game_types", "fields type; limit 50;", token);
            var items = new Dictionary<int, string>();
            foreach (var item in Items(doc.RootElement))
            {
                var id = Int(item, "id");
                var name = Clip(Str(item, "type"), 80);
                if (id is not null && name is not null)
                {
                    items[id.Value] = name;
                }
            }

            _gameTypes = new GameTypeTable(DateTimeOffset.UtcNow, items);
            await SaveAsync();
            return items;
        }, ct);
    }

    // ---- Reference data (changes rarely, cached for a month) ----------------------------------------------------------

    private async Task<int> GetSteamSourceIdAsync(CancellationToken ct)
    {
        if (_steamSource is { } cached && DateTimeOffset.UtcNow - cached.FetchedAt < ReferenceLifetime)
        {
            return cached.Id;
        }

        return await SharedAsync("ref:steam-source", async token =>
        {
            using var doc = await QueryAsync("external_game_sources", "fields name; where name = \"Steam\"; limit 1;", token);
            var id = Items(doc.RootElement).Select(x => Int(x, "id")).FirstOrDefault(x => x is not null);
            if (id is null)
            {
                _logger.LogWarning("IGDB kennt keine Quelle \"Steam\" - nutze den dokumentierten Wert {Id}.", LegacySteamSourceId);
                return LegacySteamSourceId;
            }

            _steamSource = new SteamSourceEntry(DateTimeOffset.UtcNow, id.Value);
            await SaveAsync();
            return id.Value;
        }, ct);
    }

    private async Task<Dictionary<int, LanguageRef>> GetLanguagesAsync(CancellationToken ct)
    {
        if (_languages is { } cached && DateTimeOffset.UtcNow - cached.FetchedAt < ReferenceLifetime)
        {
            return cached.Items;
        }

        return await SharedAsync("ref:languages", async token =>
        {
            using var doc = await QueryAsync("languages", "fields name,native_name,locale; limit 500;", token);
            var items = new Dictionary<int, LanguageRef>();
            foreach (var item in Items(doc.RootElement))
            {
                var id = Int(item, "id");
                var name = Clip(Str(item, "name"), 80);
                if (id is not null && name is not null)
                {
                    items[id.Value] = new LanguageRef(name, Clip(Str(item, "native_name"), 80));
                }
            }

            _languages = new LanguageTable(DateTimeOffset.UtcNow, items);
            await SaveAsync();
            return items;
        }, ct);
    }

    private async Task<Dictionary<int, string>> GetSupportTypesAsync(CancellationToken ct)
    {
        if (_supportTypes is { } cached && DateTimeOffset.UtcNow - cached.FetchedAt < ReferenceLifetime)
        {
            return cached.Items;
        }

        return await SharedAsync("ref:support-types", async token =>
        {
            using var doc = await QueryAsync("language_support_types", "fields name; limit 50;", token);
            var items = new Dictionary<int, string>();
            foreach (var item in Items(doc.RootElement))
            {
                var id = Int(item, "id");
                var name = Clip(Str(item, "name"), 80);
                if (id is not null && name is not null)
                {
                    items[id.Value] = name;
                }
            }

            _supportTypes = new SupportTypeTable(DateTimeOffset.UtcNow, items);
            await SaveAsync();
            return items;
        }, ct);
    }

    // ---- HTTP --------------------------------------------------------------------------------------------------------

    /// <summary>POSTs an Apicalypse query to https://api.igdb.com/v4/{endpoint}; paced, retried once or twice on 429.</summary>
    private async Task<JsonDocument> QueryAsync(string endpoint, string body, CancellationToken ct)
    {
        await _openRequests.WaitAsync(ct);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var response = await _auth.SendAsync(
                    (credentials, token) => BuildRequest(endpoint, body, credentials, token),
                    WaitForSlotAsync,
                    ct);

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 2)
                {
                    _logger.LogDebug("IGDB meldet 429 (Anfragelimit) bei {Endpoint}, kurze Pause.", endpoint);
                    await Task.Delay(TimeSpan.FromMilliseconds(1100), ct);
                    continue;
                }

                var text = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    throw new TwitchApiException(DescribeFailure(response.StatusCode, text));
                }

                try
                {
                    return JsonDocument.Parse(text);
                }
                catch (JsonException ex)
                {
                    throw new TwitchApiException(ServerTexts.Twitch.IgdbAnswerUnreadable, ex);
                }
            }
        }
        finally
        {
            _openRequests.Release();
        }
    }

    private static HttpRequestMessage BuildRequest(string endpoint, string body, TwitchCredentials credentials, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, TwitchAuthService.IgdbBaseUrl + endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-ID", credentials.ClientId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>Starts requests at least <see cref="MinRequestInterval"/> apart (IGDB allows 4 per second).</summary>
    private async Task WaitForSlotAsync(CancellationToken ct)
    {
        await _paceLock.WaitAsync(ct);
        try
        {
            var wait = _nextSlotUtc - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }
            _nextSlotUtc = DateTime.UtcNow + MinRequestInterval;
        }
        finally
        {
            _paceLock.Release();
        }
    }

    private static Msg DescribeFailure(HttpStatusCode status, string body)
    {
        var detail = TwitchAuthService.ReadMessage(body);
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({detail})";
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                ServerTexts.Twitch.IgdbUnauthorized((int)status, suffix),
            HttpStatusCode.TooManyRequests =>
                ServerTexts.Twitch.IgdbRateLimited,
            HttpStatusCode.BadRequest =>
                ServerTexts.Twitch.IgdbBadRequest(suffix),
            _ when (int)status >= 500 =>
                ServerTexts.Twitch.IgdbServerError((int)status, suffix),
            _ => ServerTexts.Twitch.IgdbStatus((int)status, suffix)
        };
    }

    /// <summary>
    /// Concurrent callers for the same key share one run (the dashboard refresh and the game page often ask for the same
    /// game at once). The shared run is not tied to one caller's cancellation, only to a 60 s limit.
    /// </summary>
    private async Task<T> SharedAsync<T>(string key, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        var lazy = (Lazy<Task<T>>)_inFlight.GetOrAdd(key, _ => new Lazy<Task<T>>(() => RunSharedAsync(key, work)));
        return await lazy.Value.WaitAsync(ct);
    }

    private async Task<T> RunSharedAsync<T>(string key, Func<CancellationToken, Task<T>> work)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            return await work(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TwitchApiException(ServerTexts.Twitch.IgdbTooSlow);
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    // ---- Persistence -------------------------------------------------------------------------------------------------

    private void Load()
    {
        try
        {
            if (!File.Exists(_cacheFile))
            {
                return;
            }

            var saved = JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(_cacheFile), Json);
            if (saved is null)
            {
                return;
            }

            foreach (var (appId, entry) in saved.Games) _games[appId] = entry;
            foreach (var (appId, entry) in saved.Ids) _ids[appId] = entry;
            _steamSource = saved.SteamSource;
            _languages = saved.Languages;
            _supportTypes = saved.SupportTypes;
            _gameTypes = saved.GameTypes;
            foreach (var (igdbId, entry) in saved.Series) _series[igdbId] = entry;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "IGDB-Cache nicht lesbar, wird neu aufgebaut.");
        }
    }

    private async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var cutoff = DateTimeOffset.UtcNow - PruneAfter;
            var state = new PersistedState
            {
                Games = _games.Where(p => p.Value.FetchedAt > cutoff).ToDictionary(p => p.Key, p => p.Value),
                Ids = _ids.Where(p => p.Value.FetchedAt > cutoff).ToDictionary(p => p.Key, p => p.Value),
                SteamSource = _steamSource,
                Languages = _languages,
                SupportTypes = _supportTypes,
                GameTypes = _gameTypes,
                Series = _series.Where(p => p.Value.FetchedAt > cutoff).ToDictionary(p => p.Key, p => p.Value)
            };

            // Write to a temp file first: a crash in the middle must not leave a half-written cache behind
            var temp = _cacheFile + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, Json));
            File.Move(temp, _cacheFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "IGDB-Cache nicht gespeichert.");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    // ---- JSON helpers ------------------------------------------------------------------------------------------------

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,64}$")]
    private static partial Regex ImageIdPattern();

    /// <summary>The elements of the array itself, or of a named array property; nothing for anything else.</summary>
    private static IEnumerable<JsonElement> Items(JsonElement element, string? property = null)
    {
        var array = element;
        if (property is not null && !(element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out array)))
        {
            return Array.Empty<JsonElement>();
        }
        return array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToList() : Array.Empty<JsonElement>();
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v)
            ? v
            : null;

    private static double? Rating(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var v) && v > 0
            ? Math.Round(v, 1)
            : null;

    /// <summary>IGDB reports seconds; the UI wants hours with one decimal.</summary>
    private static double? Hours(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var seconds) && seconds > 0
            ? Math.Round(seconds / 3600.0, 1)
            : null;

    private static string? Clip(string? value, int max)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        return text.Length > max ? text[..max] : text;
    }

    /// <summary>Names of an expanded reference list (genres.name -> [{id, name}]); ids that were not expanded are skipped.</summary>
    private static List<string> Names(JsonElement element, string property) =>
        Items(element, property)
            .Select(x => Clip(Str(x, "name"), 120))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<string> SingleName(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var p) && Clip(Str(p, "name"), 120) is { } name
            ? new[] { name }
            : Array.Empty<string>();

    private static string? ImageId(JsonElement element)
    {
        var id = Str(element, "image_id");
        return id is not null && ImageIdPattern().IsMatch(id) ? id : null;
    }

    /// <summary>https://images.igdb.com/igdb/image/upload/t_{size}/{image_id}.jpg (sizes: https://api-docs.igdb.com/#images).</summary>
    private static string? ImageUrl(string? imageId, string size) =>
        imageId is null ? null : $"https://images.igdb.com/igdb/image/upload/t_{size}/{imageId}.jpg";

    private static List<IgdbImageDto> Images(JsonElement game, string property) =>
        Items(game, property)
            .Select(x => ImageId(x))
            .OfType<string>()
            .Distinct()
            .Take(MaxImages)
            .Select(id => new IgdbImageDto(ImageUrl(id, "screenshot_med")!, ImageUrl(id, "1080p")!))
            .ToList();

    /// <summary>Only a link to igdb.com itself is passed on to the UI.</summary>
    private static string? SafeIgdbUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("igdb.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".igdb.com", StringComparison.OrdinalIgnoreCase))
            ? uri.AbsoluteUri
            : null;
}
