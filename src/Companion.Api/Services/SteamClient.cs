using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Settings;

namespace Companion.Api.Services;

/// <summary>
/// Queries the Steam Store Search &amp; Browse APIs for official high-resolution game cover art, hero banners, and AppIDs,
/// reads the user's personal Steam library via IPlayerService/GetOwnedGames,
/// and fetches official achievement schemas and player progress when a Steam Web API key is configured.
/// </summary>
public sealed class SteamClient(
    IHttpClientFactory httpClientFactory,
    SecretStore secretStore,
    SettingsStore settingsStore,
    ILogger<SteamClient> logger)
{
    public const string HttpClientName = "steam";

    // Keyed by language too: Steam may deliver localized capsule/header art per language
    private static readonly ConcurrentDictionary<(int AppId, string Language), (string CoverUrl, string HeroUrl, string HeaderUrl)> AssetCache = new();

    public async Task<(string CoverUrl, string HeroUrl, string HeaderUrl)> ResolveSingleAppAssetsAsync(int appId, CancellationToken ct = default)
    {
        if (appId <= 0)
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        if (AssetCache.TryGetValue((appId, Lang.Current), out var cached))
        {
            return cached;
        }

        var map = await ResolveStoreAssetsAsync([appId], ct);
        if (map.TryGetValue(appId, out var resolved))
        {
            return resolved;
        }

        return GetFallbackUrls(appId);
    }

    public async Task<Dictionary<int, (string CoverUrl, string HeroUrl, string HeaderUrl)>> ResolveStoreAssetsAsync(
        IReadOnlyCollection<int> appIds,
        CancellationToken ct = default)
    {
        var result = new Dictionary<int, (string CoverUrl, string HeroUrl, string HeaderUrl)>();
        var missingIds = new List<int>();

        foreach (var id in appIds.Distinct())
        {
            if (id <= 0) continue;
            if (AssetCache.TryGetValue((id, Lang.Current), out var cached))
            {
                result[id] = cached;
            }
            else
            {
                missingIds.Add(id);
            }
        }

        if (missingIds.Count == 0)
        {
            return result;
        }

        var client = httpClientFactory.CreateClient(HttpClientName);

        // Query in chunks of up to 50 appIds via official Steam IStoreBrowseService/GetItems/v1
        foreach (var chunk in missingIds.Chunk(50))
        {
            try
            {
                var idsJson = string.Join(",", chunk.Select(id => $"{{\"appid\":{id}}}"));
                var inputJson = $"{{\"ids\":[{idsJson}],\"context\":{{\"language\":\"{Lang.SteamName(Lang.Current)}\",\"country_code\":\"DE\"}},\"data_request\":{{\"include_assets\":true}}}}";
                var url = $"https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json={Uri.EscapeDataString(inputJson)}";

                using var response = await client.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                    if (doc.RootElement.TryGetProperty("response", out var respEl) &&
                        respEl.TryGetProperty("store_items", out var itemsArr) &&
                        itemsArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in itemsArr.EnumerateArray())
                        {
                            if (!item.TryGetProperty("appid", out var idProp) || !idProp.TryGetInt32(out var appId))
                            {
                                continue;
                            }

                            var fallback = GetFallbackUrls(appId);
                            var coverUrl = fallback.CoverUrl;
                            var heroUrl = fallback.HeroUrl;
                            var headerUrl = fallback.HeaderUrl;

                            if (item.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Object)
                            {
                                var basePrefix = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/";

                                var libCapsule2x = GetAssetString(assets, "library_capsule_2x");
                                var libCapsule = GetAssetString(assets, "library_capsule");
                                var mainCapsule = GetAssetString(assets, "main_capsule");
                                var header2x = GetAssetString(assets, "header_2x");
                                var header = GetAssetString(assets, "header");
                                var libHero2x = GetAssetString(assets, "library_hero_2x");
                                var libHero = GetAssetString(assets, "library_hero");

                                if (!string.IsNullOrWhiteSpace(header))
                                {
                                    headerUrl = basePrefix + header;
                                }
                                else if (!string.IsNullOrWhiteSpace(header2x))
                                {
                                    headerUrl = basePrefix + header2x;
                                }

                                if (!string.IsNullOrWhiteSpace(libCapsule2x))
                                {
                                    coverUrl = basePrefix + libCapsule2x;
                                }
                                else if (!string.IsNullOrWhiteSpace(libCapsule))
                                {
                                    coverUrl = basePrefix + libCapsule;
                                }
                                else if (!string.IsNullOrWhiteSpace(mainCapsule))
                                {
                                    coverUrl = basePrefix + mainCapsule;
                                }
                                else
                                {
                                    coverUrl = headerUrl;
                                }

                                if (!string.IsNullOrWhiteSpace(libHero2x))
                                {
                                    heroUrl = basePrefix + libHero2x;
                                }
                                else if (!string.IsNullOrWhiteSpace(libHero))
                                {
                                    heroUrl = basePrefix + libHero;
                                }
                                else
                                {
                                    heroUrl = headerUrl;
                                }
                            }

                            var tuple = (coverUrl, heroUrl, headerUrl);
                            AssetCache[(appId, Lang.Current)] = tuple;
                            result[appId] = tuple;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Steam Store Asset-Auflösung fehlgeschlagen.");
            }

            foreach (var id in chunk)
            {
                if (!result.ContainsKey(id))
                {
                    result[id] = GetFallbackUrls(id);
                }
            }
        }

        return result;
    }

    private static string? GetAssetString(JsonElement assets, string propName)
    {
        return assets.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static (string CoverUrl, string HeroUrl, string HeaderUrl) GetFallbackUrls(int appId) =>
    (
        $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900_2x.jpg",
        $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_hero.jpg",
        $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg"
    );

    public async Task<List<SteamSearchItemDto>> SearchGamesAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(query.Trim())}&l={Lang.SteamName(Lang.Current)}&cc=DE";
        try
        {
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var rawItems = new List<(int AppId, string Name)>();
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idProp) || !idProp.TryGetInt32(out var appId))
                {
                    continue;
                }

                var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    rawItems.Add((appId, name));
                }
            }

            var assetsMap = await ResolveStoreAssetsAsync(rawItems.Select(x => x.AppId).ToList(), ct);
            var results = new List<SteamSearchItemDto>();
            foreach (var (appId, name) in rawItems)
            {
                var urls = assetsMap.TryGetValue(appId, out var resolved) ? resolved : GetFallbackUrls(appId);
                results.Add(new SteamSearchItemDto(appId, name, urls.CoverUrl, urls.HeroUrl, urls.HeaderUrl));
            }

            return results;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Steam-Suche fuer '{Query}' fehlgeschlagen.", query);
            return [];
        }
    }

    /// <summary>
    /// Name and avatar from the public community profile XML (no key needed); the number of owned games only with the
    /// Steam Web API key (IPlayerService/GetOwnedGames). Cached for 30 minutes.
    /// </summary>
    public async Task<SteamProfileDto> GetProfileAsync(Microsoft.Extensions.Caching.Memory.IMemoryCache cache, CancellationToken ct = default)
    {
        var settings = await settingsStore.GetAsync(ct);
        var steamId = settings.SteamUserId.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(steamId, @"^\d{17}$"))
        {
            return new SteamProfileDto(null, null, null, null, null);
        }

        var hasKey = !string.IsNullOrWhiteSpace(secretStore.GetSecret(SettingsStore.SecretSteam));
        return await Microsoft.Extensions.Caching.Memory.CacheExtensions.GetOrCreateAsync(cache, $"steam-profile:{steamId}:{hasKey}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            var client = httpClientFactory.CreateClient(HttpClientName);
            string? name = null, avatar = null;
            try
            {
                var xml = await client.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}?xml=1", ct);
                var doc = System.Xml.Linq.XDocument.Parse(xml);
                name = doc.Root?.Element("steamID")?.Value.Trim();
                avatar = doc.Root?.Element("avatarMedium")?.Value.Trim();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Steam-Profil {SteamId} nicht abrufbar.", steamId);
            }

            int? libraryCount = null;
            if (hasKey)
            {
                try
                {
                    var library = await FetchOwnedGamesAsync(steamId, [], ct);
                    libraryCount = library.TotalGames;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "Steam-Bibliothek für {SteamId} nicht abrufbar.", steamId);
                }
            }

            return new SteamProfileDto(
                steamId,
                string.IsNullOrWhiteSpace(name) ? null : name,
                avatar is { Length: > 0 } && avatar.StartsWith("https://", StringComparison.Ordinal) ? avatar : null,
                $"https://steamcommunity.com/profiles/{steamId}",
                libraryCount);
        }) ?? new SteamProfileDto(steamId, null, null, null, null);
    }

    public async Task<SteamLibraryResponseDto> FetchOwnedGamesAsync(
        string? steamIdOverride,
        Dictionary<int, string> existingAppIdToSlug,
        CancellationToken ct = default,
        IReadOnlySet<int>? favoriteAppIds = null)
    {
        var apiKey = secretStore.GetSecret(SettingsStore.SecretSteam);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new LocalizedException(ServerTexts.Steam.NoApiKey);
        }

        var settings = await settingsStore.GetAsync(ct);
        var rawSteamInput = !string.IsNullOrWhiteSpace(steamIdOverride)
            ? steamIdOverride.Trim()
            : settings.SteamUserId.Trim();

        if (string.IsNullOrWhiteSpace(rawSteamInput))
        {
            throw new LocalizedException(ServerTexts.Steam.NoSteamId);
        }

        var resolvedSteamId = await ResolveSteamIdAsync(apiKey, rawSteamInput, ct);
        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(resolvedSteamId)}&include_appinfo=1&include_played_free_games=1&format=json";

        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new LocalizedException(ServerTexts.Steam.ApiStatus((int)response.StatusCode));
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("response", out var respEl) ||
            !respEl.TryGetProperty("games", out var gamesArr) ||
            gamesArr.ValueKind != JsonValueKind.Array)
        {
            return new SteamLibraryResponseDto(resolvedSteamId, 0, []);
        }

        var rawGames = new List<(int AppId, string Name, double PlaytimeHours, DateTimeOffset? LastPlayed)>();
        foreach (var g in gamesArr.EnumerateArray())
        {
            if (!g.TryGetProperty("appid", out var idProp) || !idProp.TryGetInt32(out var appId))
            {
                continue;
            }

            var name = g.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? $"App {appId}" : $"App {appId}";
            var playtimeMinutes = g.TryGetProperty("playtime_forever", out var ptProp) && ptProp.TryGetInt32(out var mins) ? mins : 0;
            var playtimeHours = Math.Round(playtimeMinutes / 60.0, 1);
            DateTimeOffset? lastPlayed = g.TryGetProperty("rtime_last_played", out var lpProp) && lpProp.TryGetInt64(out var lastUnix) && lastUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(lastUnix)
                : null;
            rawGames.Add((appId, name, playtimeHours, lastPlayed));
        }

        var sortedRaw = rawGames
            .OrderByDescending(x => x.PlaytimeHours)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Resolve the exact (hashed) asset URLs for the whole library: guessed URLs often do not exist for newer games.
        // ResolveStoreAssetsAsync asks in batches of 50 and caches, so this costs a few calls once per app start.
        var allIds = sortedRaw.Select(x => x.AppId).ToList();
        var assetsMap = await ResolveStoreAssetsAsync(allIds, ct);

        var list = new List<SteamOwnedGameDto>();
        foreach (var (appId, name, playtimeHours, lastPlayed) in sortedRaw)
        {
            var urls = assetsMap.TryGetValue(appId, out var resolved) ? resolved : GetFallbackUrls(appId);
            var alreadyInVault = existingAppIdToSlug.TryGetValue(appId, out var existingSlug);

            list.Add(new SteamOwnedGameDto(
                AppId: appId,
                Name: name,
                PlaytimeHours: playtimeHours,
                CoverUrl: urls.CoverUrl,
                HeroBannerUrl: urls.HeroUrl,
                HeaderImageUrl: urls.HeaderUrl,
                AlreadyInVault: alreadyInVault,
                ExistingSlug: existingSlug,
                IsFavorite: favoriteAppIds?.Contains(appId) == true,
                LastPlayedAt: lastPlayed
            ));
        }

        return new SteamLibraryResponseDto(resolvedSteamId, list.Count, list);
    }

    /// <summary>The official achievement list of a game in <paramref name="language"/> ("de" / "en"; null = the current request's language; English = Steam's original texts).</summary>
    public async Task<List<(string ApiName, string DisplayName, string Description)>> FetchSteamAchievementsSchemaAsync(int appId, string? language = null, CancellationToken ct = default)
    {
        var apiKey = secretStore.GetSecret(SettingsStore.SecretSteam);
        if (string.IsNullOrWhiteSpace(apiKey) || appId <= 0)
        {
            return [];
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={Uri.EscapeDataString(apiKey)}&appid={appId}&l={Lang.SteamName(Lang.NormalizeOr(language, Lang.Current))}";
        try
        {
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("game", out var gameEl) ||
                !gameEl.TryGetProperty("availableGameStats", out var statsEl) ||
                !statsEl.TryGetProperty("achievements", out var achArray) ||
                achArray.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<(string ApiName, string DisplayName, string Description)>();
            foreach (var ach in achArray.EnumerateArray())
            {
                var apiName = ach.TryGetProperty("name", out var an) ? an.GetString() ?? string.Empty : string.Empty;
                var displayName = ach.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? string.Empty : string.Empty;
                var desc = ach.TryGetProperty("description", out var ds) ? ds.GetString() ?? (Lang.NormalizeOr(language, Lang.Current) == Lang.English ? "Secret achievement (unlocked while playing)" : "Geheimer Erfolg (im Spielverlauf freischaltbar)") : string.Empty;
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    list.Add((apiName, displayName, desc));
                }
            }
            return list;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Abruf der Steam-Achievements fuer AppId {AppId} fehlgeschlagen.", appId);
            return [];
        }
    }

    private async Task<string> ResolveSteamIdAsync(string apiKey, string input, CancellationToken ct)
    {
        var cleaned = input.Trim().TrimEnd('/');

        var profileMatch = Regex.Match(cleaned, @"steamcommunity\.com/profiles/(\d{17})", RegexOptions.IgnoreCase);
        if (profileMatch.Success)
        {
            return profileMatch.Groups[1].Value;
        }

        var vanityMatch = Regex.Match(cleaned, @"steamcommunity\.com/id/([^/?#]+)", RegexOptions.IgnoreCase);
        if (vanityMatch.Success)
        {
            cleaned = vanityMatch.Groups[1].Value;
        }

        if (Regex.IsMatch(cleaned, @"^\d{17}$"))
        {
            return cleaned;
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"https://api.steampowered.com/ISteamUser/ResolveVanityURL/v1/?key={Uri.EscapeDataString(apiKey)}&vanityurl={Uri.EscapeDataString(cleaned)}";
        using var response = await client.GetAsync(url, ct);
        if (response.IsSuccessStatusCode)
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("response", out var resp) &&
                resp.TryGetProperty("success", out var succ) && succ.GetInt32() == 1 &&
                resp.TryGetProperty("steamid", out var sid) && !string.IsNullOrWhiteSpace(sid.GetString()))
            {
                return sid.GetString()!;
            }
        }

        throw new LocalizedException(ServerTexts.Steam.ProfileUnresolved(input));
    }
}
