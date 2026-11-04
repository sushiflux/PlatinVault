using System.Text.Json;
using System.Text.RegularExpressions;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Settings;
using Microsoft.Extensions.Caching.Memory;

namespace Companion.Api.Services;

/// <summary>
/// Game and player statistics from Steam's own public endpoints (SteamDB offers no API and forbids scraping):
/// store details (release, genres, developers, Metacritic), review summary (IUserReviewsService/GetAppReviews),
/// current players (ISteamUserStats/GetNumberOfCurrentPlayers), announcements/patch notes (ISteamNews/GetNewsForApp)
/// and - with the Steam Web API key - the player's own playtime (IPlayerService/GetOwnedGames).
/// Every source is optional: a failing call leaves its fields empty instead of failing the whole page.
/// </summary>
public sealed partial class SteamStatsService(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    SecretStore secretStore,
    SteamLocalStatsService localStats,
    ILogger<SteamStatsService> logger)
{
    [GeneratedRegex(@"\b(patch|update|hotfix|version)\b[^\n]*\d", RegexOptions.IgnoreCase)]
    private static partial Regex PatchTitlePattern();

    public sealed record StoreFacts(
        string Name,
        string ShortDescription,
        List<string> Genres,
        List<string> Categories,
        string ReleaseDate,
        bool ComingSoon,
        List<string> Developers,
        List<string> Publishers,
        int? MetacriticScore,
        int? AchievementsTotal,
        // The languages the store lists (in the language of the request, e.g. "Deutsch" with l=german) and those with full audio
        List<string>? Languages = null,
        List<string>? AudioLanguages = null);

    /// <summary>
    /// Reads appdetails' "supported_languages": an HTML string in the language of the request, e.g. (app 359870, l=german, read 2026-11-03)
    /// <c>Englisch&lt;strong&gt;*&lt;/strong&gt;, Französisch, …, Deutsch, …&lt;br&gt;&lt;strong&gt;*&lt;/strong&gt;Sprachen mit voller Audiounterstützung</c>.
    /// The footnote after &lt;br&gt; is dropped, tags are removed, a "*" marks a language with full audio.
    /// </summary>
    internal static (List<string> Languages, List<string> Audio) ParseSupportedLanguages(string? html)
    {
        var languages = new List<string>();
        var audio = new List<string>();
        if (string.IsNullOrWhiteSpace(html))
        {
            return (languages, audio);
        }
        var list = Regex.Split(html, @"<br\s*/?>", RegexOptions.IgnoreCase)[0];
        foreach (var raw in list.Split(','))
        {
            var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(raw, "<[^>]*>", string.Empty)).Trim();
            var withAudio = text.Contains('*');
            text = text.Replace("*", string.Empty).Trim();
            if (text.Length == 0 || text.Length > 60)
            {
                continue;
            }
            languages.Add(text);
            if (withAudio)
            {
                audio.Add(text);
            }
        }
        return (languages, audio);
    }

    /// <summary>Store facts in the language of the current request ("de" when there is none).</summary>
    public Task<StoreFacts?> GetStoreFactsAsync(int appId, CancellationToken ct) => GetStoreFactsAsync(appId, Lang.Current, ct);

    /// <summary>
    /// Store facts (genres, categories, release date, descriptions) in one language. Cached per language: Steam delivers them
    /// translated, and a language switch must not show the other language's texts for hours.
    /// </summary>
    public Task<StoreFacts?> GetStoreFactsAsync(int appId, string language, CancellationToken ct) =>
        CachedAsync($"store:{appId}:{language}", TimeSpan.FromHours(6), async () =>
        {
            using var doc = await GetJsonAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l={Lang.SteamName(language)}", ct);
            if (doc is null ||
                !doc.RootElement.TryGetProperty(appId.ToString(), out var app) ||
                !app.TryGetProperty("success", out var ok) || !ok.GetBoolean() ||
                !app.TryGetProperty("data", out var data))
            {
                return null;
            }

            var release = data.TryGetProperty("release_date", out var rd) ? rd : default;
            var languages = ParseSupportedLanguages(Str(data, "supported_languages"));
            return new StoreFacts(
                Name: Str(data, "name"),
                ShortDescription: Str(data, "short_description"),
                Genres: Descriptions(data, "genres"),
                Categories: Descriptions(data, "categories"),
                ReleaseDate: release.ValueKind == JsonValueKind.Object ? Str(release, "date") : string.Empty,
                ComingSoon: release.ValueKind == JsonValueKind.Object && release.TryGetProperty("coming_soon", out var cs) && cs.ValueKind == JsonValueKind.True,
                Developers: Strings(data, "developers"),
                Publishers: Strings(data, "publishers"),
                MetacriticScore: data.TryGetProperty("metacritic", out var mc) && mc.TryGetProperty("score", out var score) && score.TryGetInt32(out var s) ? s : null,
                AchievementsTotal: data.TryGetProperty("achievements", out var ach) && ach.TryGetProperty("total", out var total) && total.TryGetInt32(out var t) ? t : null,
                Languages: languages.Languages,
                AudioLanguages: languages.Audio);
        });

/// <summary>
    /// The game's top Steam community tags in English, ordered by votes (IStoreBrowseService/GetItems with
    /// include_tag_count + IStoreService/GetTagList). Much finer than Steam's genres; used to pick genre profiles.
    /// </summary>
    public async Task<List<string>> GetSteamTagsAsync(int appId, CancellationToken ct)
    {
        var tagNames = await CachedAsync("taglist:english", TimeSpan.FromHours(24), async () =>
        {
            using var doc = await GetJsonAsync("https://api.steampowered.com/IStoreService/GetTagList/v1/?language=english", ct);
            if (doc is null ||
                !doc.RootElement.TryGetProperty("response", out var resp) ||
                !resp.TryGetProperty("tags", out var tags) ||
                tags.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var map = new Dictionary<int, string>();
            foreach (var tag in tags.EnumerateArray())
            {
                if (tag.TryGetProperty("tagid", out var id) && id.TryGetInt32(out var tagId))
                {
                    map[tagId] = Str(tag, "name");
                }
            }
            return map;
        });

        var tagIds = await CachedAsync($"tags:{appId}", TimeSpan.FromHours(24), async () =>
        {
            var input = Uri.EscapeDataString($"{{\"ids\":[{{\"appid\":{appId}}}],\"context\":{{\"language\":\"english\",\"country_code\":\"DE\"}},\"data_request\":{{\"include_tag_count\":15}}}}");
            using var doc = await GetJsonAsync($"https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json={input}", ct);
            if (doc is null ||
                !doc.RootElement.TryGetProperty("response", out var resp) ||
                !resp.TryGetProperty("store_items", out var items) ||
                items.ValueKind != JsonValueKind.Array ||
                items.GetArrayLength() == 0 ||
                !items[0].TryGetProperty("tags", out var tags) ||
                tags.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return tags.EnumerateArray()
                .Select(t => t.TryGetProperty("tagid", out var id) && id.TryGetInt32(out var tagId) ? tagId : 0)
                .Where(id => id > 0)
                .ToList();
        });

        if (tagIds is null || tagNames is null)
        {
            return [];
        }
        return tagIds.Select(id => tagNames.GetValueOrDefault(id)).OfType<string>().Where(n => n.Length > 0).ToList();
    }

    /// <summary>What a dashboard card shows: store facts, review summary and own playtime - without news and player count.</summary>
    public async Task<CardFacts> GetCardFactsAsync(int appId, string steamId64, CancellationToken ct)
    {
        var factsTask = GetStoreFactsAsync(appId, ct);
        var reviewsTask = GetReviewSummaryAsync(appId, ct);
        var playtimeTask = GetPlaytimeAsync(appId, steamId64, ct);
        await Task.WhenAll(factsTask, reviewsTask, playtimeTask);

        var facts = await factsTask;
        var reviews = await reviewsTask;
        var playtime = await playtimeTask;
        return new CardFacts(
            facts?.MetacriticScore,
            reviews?.Desc,
            reviews is { Total: > 0 } r ? (int)Math.Round(r.Positive * 100.0 / r.Total) : null,
            facts?.ReleaseDate,
            playtime?.TotalHours,
            playtime?.LastPlayed,
            facts?.Genres ?? []);
    }

    public sealed record CardFacts(int? MetacriticScore, string? ReviewScoreDesc, int? ReviewPercent, string? ReleaseDate, double? PlaytimeHours, DateTimeOffset? LastPlayed, List<string> Genres);

    public async Task<GameStatsDto> GetGameStatsAsync(int appId, string steamId64, LiveGameAchievementsDto? achievements, CancellationToken ct)
    {
        var factsTask = GetStoreFactsAsync(appId, ct);
        var reviewsTask = GetReviewSummaryAsync(appId, ct);
        var playersTask = GetCurrentPlayersAsync(appId, ct);
        var newsTask = GetNewsAsync(appId, ct);
        var playtimeTask = GetPlaytimeAsync(appId, steamId64, ct);
        await Task.WhenAll(factsTask, reviewsTask, playersTask, newsTask, playtimeTask);

        var facts = await factsTask;
        var reviews = await reviewsTask;
        var news = await newsTask ?? [];
        var playtime = await playtimeTask;

        PlayerGameStatsDto? player = null;
        if (playtime is not null || achievements is { TotalCount: > 0 })
        {
            var unlocked = achievements?.Achievements.Where(a => a.Achieved).ToList() ?? [];
            // Only achievements with a real worldwide share count; a placeholder would show up as a made-up "rarest"
            var rarest = unlocked.Where(a => a.HasGlobalPercent).OrderBy(a => a.GlobalPercent).FirstOrDefault();
            var unlockTimes = unlocked.Where(a => a.UnlockTime.HasValue).Select(a => a.UnlockTime!.Value).ToList();
            player = new PlayerGameStatsDto(
                PlaytimeHours: playtime?.TotalHours,
                Playtime2WeeksHours: playtime?.RecentHours,
                LastPlayed: playtime?.LastPlayed,
                AchievementsUnlocked: achievements?.AchievedCount ?? 0,
                AchievementsTotal: achievements?.TotalCount ?? 0,
                ProgressPercent: achievements?.ProgressPercentage ?? 0,
                RarestUnlockedName: rarest?.DisplayName,
                RarestUnlockedPercent: rarest?.GlobalPercent,
                FirstUnlock: unlockTimes.Count > 0 ? unlockTimes.Min() : null,
                LastUnlock: unlockTimes.Count > 0 ? unlockTimes.Max() : null);
        }

        return new GameStatsDto(
            AppId: appId,
            ReleaseDate: facts?.ReleaseDate,
            ComingSoon: facts?.ComingSoon ?? false,
            Developers: facts?.Developers ?? [],
            Publishers: facts?.Publishers ?? [],
            Genres: facts?.Genres ?? [],
            MetacriticScore: facts?.MetacriticScore,
            AchievementsTotal: facts?.AchievementsTotal,
            ReviewScoreDesc: reviews?.Desc,
            ReviewTotal: reviews?.Total,
            ReviewPositive: reviews?.Positive,
            ReviewPercentPositive: reviews is { Total: > 0 } r ? (int)Math.Round(r.Positive * 100.0 / r.Total) : null,
            CurrentPlayers: await playersTask,
            News: news.Take(5).ToList(),
            LatestUpdate: news.FirstOrDefault(n => n.IsUpdate),
            Player: player);
    }

    private sealed record ReviewSummary(string Desc, int Total, int Positive);

    // Steam's review scale 1-9 with the labels of the German Steam store (GetAppReviews only returns English text).
    // Checked on store.steampowered.com/?l=german: 9, 8 and 5; the others follow the same store wording.
    private static string? GermanReviewLabel(int score) => score switch
    {
        9 => "Äußerst positiv",
        8 => "Sehr positiv",
        7 => "Positiv",
        6 => "Größtenteils positiv",
        5 => "Ausgeglichen",
        4 => "Größtenteils negativ",
        3 => "Negativ",
        2 => "Sehr negativ",
        1 => "Äußerst negativ",
        _ => null
    };

    private Task<ReviewSummary?> GetReviewSummaryAsync(int appId, CancellationToken ct) => GetReviewSummaryAsync(appId, Lang.Current, ct);

    private Task<ReviewSummary?> GetReviewSummaryAsync(int appId, string language, CancellationToken ct) =>
        CachedAsync($"reviews:{appId}:{language}", TimeSpan.FromHours(1), async () =>
        {
            var input = Uri.EscapeDataString($"{{\"appid\":{appId},\"num_per_page\":1}}");
            using var doc = await GetJsonAsync($"https://api.steampowered.com/IUserReviewsService/GetAppReviews/v1/?input_json={input}", ct);
            if (doc is null ||
                !doc.RootElement.TryGetProperty("response", out var resp) ||
                !resp.TryGetProperty("query_summary", out var summary))
            {
                return null;
            }

            var score = summary.TryGetProperty("review_score", out var rs) && rs.TryGetInt32(out var value) ? value : 0;
            return new ReviewSummary(
                // German: the wording of the German Steam store; English: Steam's own English text from the same answer
                (language == Lang.English ? null : GermanReviewLabel(score)) ?? Str(summary, "review_score_desc"),
                summary.TryGetProperty("total_reviews", out var tr) && tr.TryGetInt32(out var total) ? total : 0,
                summary.TryGetProperty("total_positive", out var tp) && tp.TryGetInt32(out var positive) ? positive : 0);
        });

    /// <summary>
    /// Players online right now (GetNumberOfCurrentPlayers needs no key, see the Steamworks docs); null when Steam does not answer.
    /// Cached for two minutes, so the 30-minute recording always reads a fresh value.
    /// </summary>
    public Task<int?> GetCurrentPlayersAsync(int appId, CancellationToken ct) =>
        CachedAsync<int?>($"players:{appId}", TimeSpan.FromMinutes(2), async () =>
        {
            using var doc = await GetJsonAsync($"https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid={appId}", ct);
            return doc is not null &&
                doc.RootElement.TryGetProperty("response", out var resp) &&
                resp.TryGetProperty("player_count", out var count) && count.TryGetInt32(out var players)
                ? players
                : null;
        });

    private Task<List<GameNewsItemDto>?> GetNewsAsync(int appId, CancellationToken ct) =>
        CachedAsync($"news:{appId}", TimeSpan.FromMinutes(30), async () =>
        {
            using var doc = await GetJsonAsync(
                $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={appId}&count=15&maxlength=1&feeds=steam_community_announcements", ct);
            if (doc is null ||
                !doc.RootElement.TryGetProperty("appnews", out var appNews) ||
                !appNews.TryGetProperty("newsitems", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var list = new List<GameNewsItemDto>();
            foreach (var item in items.EnumerateArray())
            {
                var title = Str(item, "title");
                var tags = Strings(item, "tags");
                var date = item.TryGetProperty("date", out var d) && d.TryGetInt64(out var unix)
                    ? DateTimeOffset.FromUnixTimeSeconds(unix)
                    : DateTimeOffset.MinValue;
                list.Add(new GameNewsItemDto(
                    Title: title,
                    Url: Str(item, "url"),
                    Date: date,
                    IsUpdate: tags.Contains("patchnotes") || PatchTitlePattern().IsMatch(title)));
            }
            return list;
        });

    private sealed record Playtime(double TotalHours, double RecentHours, DateTimeOffset? LastPlayed, int TotalMinutes);

    /// <summary>
    /// Total playtime of a game in whole minutes: Steam GetOwnedGames (with key, cached five minutes, one call serves every game),
    /// otherwise the local localconfig.vdf of this PC. Null when neither knows the game. Used by the daily playtime recording.
    /// </summary>
    public async Task<int?> GetPlaytimeMinutesAsync(int appId, string steamId64, CancellationToken ct) =>
        (await GetPlaytimeAsync(appId, steamId64, ct))?.TotalMinutes;

    private async Task<Playtime?> GetPlaytimeAsync(int appId, string steamId64, CancellationToken ct)
    {
        var apiKey = secretStore.GetSecret(SettingsStore.SecretSteam);
        if (string.IsNullOrWhiteSpace(apiKey) || !Regex.IsMatch(steamId64, @"^\d{17}$"))
        {
            return LocalPlaytime(appId);
        }

        var owned = await CachedAsync($"owned:{steamId64}", TimeSpan.FromMinutes(5), async () =>
        {
            using var doc = await GetJsonAsync(
                $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={steamId64}&include_played_free_games=1&format=json", ct);
            if (doc is null ||
                !doc.RootElement.TryGetProperty("response", out var resp) ||
                !resp.TryGetProperty("games", out var games) ||
                games.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var map = new Dictionary<int, Playtime>();
            foreach (var g in games.EnumerateArray())
            {
                if (!g.TryGetProperty("appid", out var idProp) || !idProp.TryGetInt32(out var id))
                {
                    continue;
                }

                var total = g.TryGetProperty("playtime_forever", out var pf) && pf.TryGetInt32(out var minutes) ? minutes : 0;
                var recent = g.TryGetProperty("playtime_2weeks", out var p2) && p2.TryGetInt32(out var recentMinutes) ? recentMinutes : 0;
                DateTimeOffset? lastPlayed = g.TryGetProperty("rtime_last_played", out var lp) && lp.TryGetInt64(out var lastUnix) && lastUnix > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(lastUnix)
                    : null;
                map[id] = new Playtime(Math.Round(total / 60.0, 1), Math.Round(recent / 60.0, 1), lastPlayed, total);
            }
            return map;
        });

        return owned is not null && owned.TryGetValue(appId, out var playtime) ? playtime : LocalPlaytime(appId);
    }

    // Without a Steam key (or for a game the Web API does not list): the local Steam client of this PC
    private Playtime? LocalPlaytime(int appId) =>
        appId > 0 && localStats.ReadLocalPlaytime((uint)appId) is { } local
            ? new Playtime(Math.Round(local.PlaytimeMinutes / 60.0, 1), Math.Round(local.Playtime2WeeksMinutes / 60.0, 1), local.LastPlayed, local.PlaytimeMinutes)
            : null;

    private async Task<T?> CachedAsync<T>(string key, TimeSpan duration, Func<Task<T?>> load)
    {
        if (cache.TryGetValue(key, out T? cached))
        {
            return cached;
        }

        var value = await load();
        if (value is not null)
        {
            cache.Set(key, value, duration);
        }
        return value;
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient(SteamClient.HttpClientName);
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Steam-Statistik {Url} antwortete mit {Status}.", RedactKey(url), (int)response.StatusCode);
                return null;
            }
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Steam-Statistik {Url} nicht abrufbar.", RedactKey(url));
            return null;
        }
    }

    private static string RedactKey(string url) => Regex.Replace(url, @"key=[^&]+", "key=***");

    private static string Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? string.Empty : string.Empty;

    private static List<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];

    private static List<string> Descriptions(JsonElement element, string name) =>
        element.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(x => Str(x, "description")).Where(x => x.Length > 0).ToList()
            : [];
}
