using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Microsoft.Extensions.Caching.Memory;

namespace Companion.Api.Services;

/// <summary>
/// Live Twitch streams of a game (Twitch Helix, https://dev.twitch.tv/docs/api/reference/). A game is a "category" on Twitch:
/// its id comes from "Get Games" - by the IGDB id when the game is known to IGDB (exact), else by the exact game name. Then
/// "Get Streams" lists the live streams of that category, most viewers first. Both calls only need the app access token of
/// the Twitch developer app; Twitch allows 800 requests per minute for it, and the answers are cached (streams 60 s,
/// categories a day), so the limit is far away.
/// </summary>
public sealed partial class TwitchStreamsService(TwitchAuthService auth, IgdbService igdb, IMemoryCache cache, ILogger<TwitchStreamsService> logger)
{
    public const int StreamCount = 20;
    private const string ThumbnailSize = "440x248";

    /// <summary>"Get Streams" returns at most 100 streams per page and no total, so the live count is paged: 3 pages = exact up to 300, then "300+".</summary>
    public const int CountPageSize = 100;
    public const int CountMaxPages = 3;

    private static readonly TimeSpan StreamsCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CountCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CategoryCacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan MissingCategoryCacheDuration = TimeSpan.FromHours(1);

    private sealed record Category(string Id, string Name);

    // A cache entry that says "Twitch has no category for this game" (null cannot be stored in the cache)
    private sealed record CategoryLookup(Category? Category);

    [GeneratedRegex(@"^[A-Za-z0-9_]{1,25}$")]
    private static partial Regex LoginPattern();

    [GeneratedRegex(@"^(?:[a-z]{2}|other)$")]
    private static partial Regex LanguagePattern();

    /// <summary>The language filter Twitch understands (ISO 639-1 two letters or "other"); null for anything else = no filter.</summary>
    public static string? NormalizeLanguage(string? language)
    {
        var value = language?.Trim().ToLowerInvariant();
        return value is not null && LanguagePattern().IsMatch(value) ? value : null;
    }

    /// <summary>
    /// The first <see cref="StreamCount"/> live streams of the game, optionally only in one language. Null without credentials.
    /// Throws <see cref="TwitchApiException"/> when Twitch cannot be reached or rejects the credentials.
    /// </summary>
    public async Task<TwitchStreamsDto?> GetStreamsAsync(int? steamAppId, string? gameName, string? language, CancellationToken ct)
    {
        if (!auth.HasCredentials)
        {
            return null;
        }

        var lang = NormalizeLanguage(language);
        var category = await ResolveCategoryAsync(steamAppId, gameName, ct);
        if (category is null)
        {
            return new TwitchStreamsDto(null, false, lang, [], DateTimeOffset.UtcNow);
        }

        var key = $"twitch-streams:{category.Id}:{lang}";
        if (cache.TryGetValue(key, out TwitchStreamsDto? cached) && cached is not null)
        {
            return cached;
        }

        var query = $"game_id={Uri.EscapeDataString(category.Id)}&first={StreamCount}&type=live" +
                    (lang is null ? string.Empty : $"&language={lang}");
        using var doc = await GetAsync($"streams?{query}", ct);
        var streams = new List<TwitchStreamDto>();
        foreach (var item in Items(doc.RootElement, "data"))
        {
            if (MapStream(item) is { } stream)
            {
                streams.Add(stream);
            }
        }

        var result = new TwitchStreamsDto(category.Name, true, lang, streams, DateTimeOffset.UtcNow);
        cache.Set(key, result, StreamsCacheDuration);
        return result;
    }

    /// <summary>
    /// How many streams are live right now in the game's Twitch category - the same category the stream list uses (IGDB id, else the exact
    /// name) and the same optional language filter. Counted by paging ("Get Streams" has no total): up to <see cref="CountMaxPages"/> pages
    /// of <see cref="CountPageSize"/>; a full last page gives <c>AtLeast</c> ("300+"). Cached per category and language for five minutes; a
    /// failure throws <see cref="TwitchApiException"/> and is never cached. Null without credentials; a game without a Twitch category has 0.
    /// </summary>
    public async Task<TwitchLiveCount?> CountLiveAsync(int? steamAppId, string? gameName, string? language, CancellationToken ct)
    {
        if (!auth.HasCredentials)
        {
            return null;
        }

        var lang = NormalizeLanguage(language);
        var category = await ResolveCategoryAsync(steamAppId, gameName, ct);
        if (category is null)
        {
            return new TwitchLiveCount(0, false);
        }

        var key = $"twitch-live-count:{category.Id}:{lang}";
        if (cache.TryGetValue(key, out TwitchLiveCount? cached) && cached is not null)
        {
            return cached;
        }

        var total = 0;
        var atLeast = false;
        string? cursor = null;
        for (var page = 1; page <= CountMaxPages; page++)
        {
            var query = $"game_id={Uri.EscapeDataString(category.Id)}&first={CountPageSize}&type=live" +
                        (lang is null ? string.Empty : $"&language={lang}") +
                        (cursor is null ? string.Empty : $"&after={Uri.EscapeDataString(cursor)}");
            using var doc = await GetAsync($"streams?{query}", ct);
            var onPage = Items(doc.RootElement, "data").Count();
            total += onPage;
            cursor = doc.RootElement.TryGetProperty("pagination", out var pagination) ? Str(pagination, "cursor") : null;
            if (onPage < CountPageSize || string.IsNullOrEmpty(cursor))
            {
                break;
            }
            // A full page with more behind it: on the last page that is where the exact count ends
            atLeast = page == CountMaxPages;
        }

        var result = new TwitchLiveCount(total, atLeast);
        cache.Set(key, result, CountCacheDuration);
        return result;
    }

    private async Task<Category?> ResolveCategoryAsync(int? steamAppId, string? gameName, CancellationToken ct)
    {
        var name = gameName?.Trim();
        if (name is { Length: > 200 })
        {
            name = name[..200];
        }

        var key = steamAppId is > 0 ? $"twitch-category:app:{steamAppId}" : $"twitch-category:name:{name?.ToLowerInvariant()}";
        if (cache.TryGetValue(key, out CategoryLookup? known) && known is not null)
        {
            return known.Category;
        }

        Category? category = null;

        if (steamAppId is > 0)
        {
            int? igdbId = null;
            try
            {
                igdbId = await igdb.GetIgdbIdAsync(steamAppId.Value, ct);
            }
            catch (TwitchApiException ex)
            {
                // The name lookup below still works without IGDB
                logger.LogDebug("IGDB-ID für App {AppId} nicht verfügbar, nutze den Spielnamen: {Message}", steamAppId, ex.Message);
            }

            if (igdbId is not null)
            {
                category = await FindCategoryAsync($"igdb_id={igdbId}", ct);
            }
        }

        if (category is null && !string.IsNullOrWhiteSpace(name))
        {
            // The name must match the Twitch category exactly; try the Steam title with and without trademark signs
            var names = new[] { name, TrademarkSigns().Replace(name, string.Empty).Trim() }
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.Ordinal);
            category = await FindCategoryAsync(string.Join("&", names.Select(n => $"name={Uri.EscapeDataString(n)}")), ct);
        }

        cache.Set(key, new CategoryLookup(category), category is null ? MissingCategoryCacheDuration : CategoryCacheDuration);
        return category;
    }

    [GeneratedRegex(@"[™®©]")]
    private static partial Regex TrademarkSigns();

    /// <summary>"Get Games": the first match of the given query (igdb_id=... or name=...), null when Twitch has none.</summary>
    private async Task<Category?> FindCategoryAsync(string query, CancellationToken ct)
    {
        using var doc = await GetAsync($"games?{query}", ct);
        foreach (var item in Items(doc.RootElement, "data"))
        {
            var id = Str(item, "id");
            var name = Str(item, "name");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name) && Regex.IsMatch(id, @"^\d{1,20}$"))
            {
                return new Category(id, name.Length > 200 ? name[..200] : name);
            }
        }
        return null;
    }

    private async Task<JsonDocument> GetAsync(string pathAndQuery, CancellationToken ct)
    {
        using var response = await auth.SendAsync(
            (credentials, token) =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, TwitchAuthService.HelixBaseUrl + pathAndQuery);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("Client-Id", credentials.ClientId);
                return request;
            },
            null,
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = TwitchAuthService.ReadMessage(body);
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({detail})";
            throw new TwitchApiException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => ServerTexts.Twitch.StreamsUnauthorized(suffix),
                HttpStatusCode.TooManyRequests => ServerTexts.Twitch.StreamsRateLimited,
                _ when (int)response.StatusCode >= 500 => ServerTexts.Twitch.StreamsServerError((int)response.StatusCode, suffix),
                _ => ServerTexts.Twitch.StreamsStatus((int)response.StatusCode, suffix)
            }, isRateLimit: response.StatusCode == HttpStatusCode.TooManyRequests);
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new TwitchApiException(ServerTexts.Twitch.AnswerUnreadable, ex);
        }
    }

    /// <summary>One entry of "Get Streams"; null when it lacks the fields needed for a safe link.</summary>
    private static TwitchStreamDto? MapStream(JsonElement item)
    {
        var login = Str(item, "user_login");
        if (login is null || !LoginPattern().IsMatch(login))
        {
            return null;
        }

        var thumbnail = Str(item, "thumbnail_url")?.Replace("{width}x{height}", ThumbnailSize, StringComparison.Ordinal);
        if (thumbnail is null || !thumbnail.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            thumbnail = null;
        }

        var startedAt = Str(item, "started_at") is { } started && DateTimeOffset.TryParse(started, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : (DateTimeOffset?)null;

        return new TwitchStreamDto(
            UserName: Clip(Str(item, "user_name"), 60) ?? login,
            UserLogin: login,
            Title: Clip(Str(item, "title"), 200) ?? string.Empty,
            ViewerCount: item.TryGetProperty("viewer_count", out var viewers) && viewers.TryGetInt32(out var count) ? count : 0,
            Language: Clip(Str(item, "language"), 10) ?? "other",
            StartedAt: startedAt,
            ThumbnailUrl: thumbnail,
            ChannelUrl: $"https://www.twitch.tv/{Uri.EscapeDataString(login)}");
    }

    private static IEnumerable<JsonElement> Items(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().ToList()
            : Array.Empty<JsonElement>();

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string? Clip(string? value, int max)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        return text.Length > max ? text[..max] : text;
    }
}

/// <summary>Streams live in a game's Twitch category right now; <c>AtLeast</c> = the count stopped at its limit ("300+").</summary>
public sealed record TwitchLiveCount(int Count, bool AtLeast);
