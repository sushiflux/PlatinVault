using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>
/// Combines local Steam binary stats (&lt; 400ms), Steam Community XML, and Steam Web API
/// to provide console-style trophy tracking AND automatic synchronization with the 100% guides in the database.
/// </summary>
public sealed partial class SteamLiveTrackerService(
    IHttpClientFactory httpClientFactory,
    SteamLocalStatsService localStatsService,
    TranslationService translationService,
    IServiceScopeFactory scopeFactory,
    SecretStore secretStore,
    IgdbService igdbService,
    ILogger<SteamLiveTrackerService> logger)
{
    // Remote achievement lists (Community XML / Web API) are cached briefly: the realtime part comes from the local
    // Steam stats merge, so the network is only asked again after this interval or when a refresh is forced.
    private static readonly TimeSpan RemoteCacheDuration = TimeSpan.FromSeconds(60);

    // The bridge sends a heartbeat every 60 s; a state older than this no longer counts as "a game is running".
    private static readonly TimeSpan BridgeFreshness = TimeSpan.FromSeconds(150);

    // Achievement texts in the chosen language from the Steam Web API (only needed without a local Steam client)
    private static readonly TimeSpan LocalizedSchemaCacheDuration = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<(uint AppId, string Lang), (Dictionary<string, (string Name, string Description)> Texts, DateTime CachedAt)> _localizedSchemas = new();

    // One background AI translation per game at a time; a failure falls back to the free translator for a while
    private static readonly TimeSpan AiTranslationRetryDelay = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<uint, Task> _aiTranslationJobs = new();
    private readonly ConcurrentDictionary<uint, DateTime> _aiTranslationFailedAt = new();

    private uint _lastKnownAppId;
    private BridgeSnapshot? _bridge;
    private BridgeSnapshot? _lastBridgeWithAchievements;
    private readonly UnlockLedgers _ledgers = new();
    private uint _announcedAppId;
    private readonly Dictionary<(uint AppId, string Language), (LiveGameInfoDto Info, DateTime CachedAt)> _gameInfoCache = [];
    private readonly Dictionary<uint, (Dictionary<string, double> Map, DateTime CachedAt)> _globalPercentCache = [];
    // Keyed by language too: the cached items carry the achievement texts and the rarity label of that language
    private readonly Dictionary<(uint AppId, string SteamId, string Language), (List<LiveAchievementItemDto> Items, DateTime CachedAt)> _remoteCache = [];
    private readonly Lock _stateLock = new();

    /// <summary>Stores the latest push of the realtime bridge and returns the previous one (null on the first push since start).</summary>
    public BridgeSnapshot? RecordBridgeState(BridgeSnapshot snapshot)
    {
        lock (_stateLock)
        {
            var previous = _bridge;
            _bridge = snapshot;
            if (snapshot.RunningAppId > 0)
            {
                _lastKnownAppId = snapshot.RunningAppId;
            }
            if (snapshot.Achievements.Count > 0)
            {
                _lastBridgeWithAchievements = snapshot;
            }

            // New unlocks on the gaming PC make the cached remote list (Steam's servers lag behind) outdated
            if (snapshot.RunningAppId > 0 && previous?.RunningAppId == snapshot.RunningAppId &&
                previous.Achievements.Count(a => a.Achieved) != snapshot.Achievements.Count(a => a.Achieved))
            {
                foreach (var key in _remoteCache.Keys.Where(k => k.AppId == snapshot.RunningAppId).ToList())
                {
                    _remoteCache.Remove(key);
                }
            }
            return previous;
        }
    }

    public DateTimeOffset? GetLastBridgeEventAt()
    {
        lock (_stateLock)
        {
            return _bridge?.ReceivedAt;
        }
    }

    /// <summary>Forgets what the bridge reported (its token was replaced or deleted).</summary>
    public void ClearBridgeState()
    {
        lock (_stateLock)
        {
            _bridge = null;
            _lastBridgeWithAchievements = null;
        }
    }

    /// <summary>
    /// Returns the achievements that are unlocked now but were not at the previous call for this game (see <see cref="UnlockLedgers"/>:
    /// the first call only takes the baseline, plus what happened after <paramref name="catchUpAfter"/>). The local worker and the
    /// bridge keep separate ledgers because they may see the same achievements under different names (the bridge reports Steam API
    /// names, a guide-based fallback list uses item ids); the worker stays silent while the bridge is active
    /// (<see cref="IsBridgeFreshFor"/>), so nothing is announced twice.
    /// </summary>
    public UnlockDiff DiffUnlocks(UnlockSource source, uint appId, IReadOnlyCollection<UnlockState> achieved, int totalCount, DateTimeOffset? catchUpAfter = null) =>
        _ledgers.Diff(source, appId, achieved, totalCount, catchUpAfter);

    /// <summary>True when the next <see cref="DiffUnlocks"/> of this source for the game only takes the baseline.</summary>
    public bool NeedsUnlockBaseline(UnlockSource source, uint appId) => _ledgers.NeedsBaseline(source, appId);

    /// <summary>The next <see cref="DiffUnlocks"/> of this source takes a new baseline (call when the game was closed or the source went silent).</summary>
    public void ResetUnlockLedger(UnlockSource source) => _ledgers.Reset(source);

    /// <summary>True while the bridge has reported the given game recently, i.e. it announces that game's unlocks.</summary>
    public bool IsBridgeFreshFor(uint appId)
    {
        lock (_stateLock)
        {
            return _bridge is { } bridge && bridge.RunningAppId == appId && DateTimeOffset.UtcNow - bridge.ReceivedAt < BridgeFreshness;
        }
    }

    /// <summary>True the first time a game start is announced (the worker and the bridge both notice it; only one sends "GameChanged").</summary>
    public bool TryAnnounceGame(uint appId)
    {
        lock (_stateLock)
        {
            if (_announcedAppId == appId)
            {
                return false;
            }
            _announcedAppId = appId;
            return true;
        }
    }

    public void ClearAnnouncedGame()
    {
        lock (_stateLock)
        {
            _announcedAppId = 0;
        }
    }

    /// <summary>Local change marker for the game's achievement file (see <see cref="SteamLocalStatsService.GetLocalStatsStamp"/>).</summary>
    public long GetLocalStatsStamp(uint appId) => localStatsService.GetLocalStatsStamp(appId);

    /// <summary>The game being played: from the local Steam client if this server runs on the gaming PC, else from a fresh bridge push.</summary>
    public uint GetRunningAppId()
    {
        var localAppId = localStatsService.GetLocalRunningAppId();
        if (localAppId > 0)
        {
            lock (_stateLock)
            {
                _lastKnownAppId = localAppId;
            }
            return localAppId;
        }

        lock (_stateLock)
        {
            if (_bridge is { RunningAppId: > 0 } && DateTimeOffset.UtcNow - _bridge.ReceivedAt < BridgeFreshness)
            {
                return _bridge.RunningAppId;
            }
        }

        return 0;
    }

    /// <summary>The bridge's achievement list for a game: the live push if it is for this game, else the last push that carried a list for it.</summary>
    private BridgeSnapshot? GetBridgeAchievementsFor(uint appId)
    {
        lock (_stateLock)
        {
            if (_bridge is { } live && live.RunningAppId == appId && live.Achievements.Count > 0)
            {
                return live;
            }
            return _lastBridgeWithAchievements is { } last && last.RunningAppId == appId ? last : null;
        }
    }

    /// <summary>
    /// Applies the bridge's unlocked flags to the base list (names, icons and rarity from Steam / the guide). Achievements
    /// the base list does not know are only added when the base list is empty, so a partial list never injects junk.
    /// </summary>
    private static List<LiveAchievementItemDto> MergeBridgeAchievements(
        List<LiveAchievementItemDto> baseList,
        BridgeSnapshot bridge,
        Dictionary<string, double> globalPercentages,
        Dictionary<string, string> notes,
        string uiLanguage)
    {
        var reported = new Dictionary<string, BridgeAchievement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in bridge.Achievements)
        {
            reported[item.ApiName] = item;
        }

        if (baseList.Count == 0)
        {
            return bridge.Achievements.Select(a =>
            {
                var hasPct = globalPercentages.TryGetValue(a.ApiName, out var p);
                var pct = hasPct ? p : 25.0;
                return new LiveAchievementItemDto(
                    ApiName: a.ApiName,
                    DisplayName: a.ApiName,
                    Description: string.Empty,
                    OriginalDescription: null,
                    IsTranslated: false,
                    IconUrl: string.Empty,
                    IconGrayUrl: string.Empty,
                    Achieved: a.Achieved,
                    UnlockTime: a.UnlockTime,
                    GlobalPercent: Math.Round(pct, 1),
                    RarityTier: CalculateRarityTier(pct),
                    RarityLabel: CalculateRarityLabel(pct, hasPct, uiLanguage),
                    IsHidden: false,
                    UserNote: notes.GetValueOrDefault(a.ApiName, string.Empty))
                {
                    HasGlobalPercent = hasPct,
                    RarityBand = CalculateRarityBand(pct, hasPct),
                };
            }).ToList();
        }

        for (var i = 0; i < baseList.Count; i++)
        {
            var item = baseList[i];
            if (!reported.TryGetValue(item.ApiName, out var live) || !live.Achieved)
            {
                continue;
            }

            if (!item.Achieved)
            {
                baseList[i] = item with { Achieved = true, UnlockTime = live.UnlockTime ?? item.UnlockTime ?? DateTime.Now };
            }
            else if (item.UnlockTime is null && live.UnlockTime is not null)
            {
                baseList[i] = item with { UnlockTime = live.UnlockTime };
            }
        }
        return baseList;
    }

    public async Task<LiveStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var settingsStore = scope.ServiceProvider.GetRequiredService<SettingsStore>();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var settings = await settingsStore.GetAsync(ct);

        var runningAppId = GetRunningAppId();
        var runningGame = runningAppId > 0
            ? await db.Games
                .AsNoTracking()
                .Where(g => g.SteamAppId == (int)runningAppId)
                .Select(g => new { g.Slug, g.Title })
                .FirstOrDefaultAsync(ct)
            : null;

        var lastBridgeEventAt = GetLastBridgeEventAt();
        var bridgeFresh = lastBridgeEventAt.HasValue && DateTimeOffset.UtcNow - lastBridgeEventAt.Value < BridgeFreshness;

        return new LiveStatusDto(
            RunningAppId: runningAppId,
            SteamId64: settings.SteamUserId,
            RunningGameSlug: runningGame?.Slug,
            RunningGameTracked: runningGame is not null,
            LocalSteamAvailable: localStatsService.IsLocalSteamAvailable() || bridgeFresh,
            LastBridgeEventAt: lastBridgeEventAt,
            RunningGameName: runningGame?.Title
        );
    }

    /// <summary>
    /// The language for texts of this call: the one of the current request (Accept-Language header, which the UI sends with every
    /// call, so a language switch is effective at once), else - background work without a request - the stored app language.
    /// </summary>
    private static string ResolveUiLanguage(string? storedUiLanguage) =>
        Lang.IsSet ? Lang.Current : Lang.NormalizeOr(storedUiLanguage);

    /// <summary>An explicit language, else the request's, else (no request) the stored app language, else German.</summary>
    private async Task<string> ResolveLanguageAsync(string? explicitLanguage, CancellationToken ct)
    {
        if (Lang.Normalize(explicitLanguage) is { } given)
        {
            return given;
        }
        if (Lang.IsSet)
        {
            return Lang.Current;
        }
        await using var scope = scopeFactory.CreateAsyncScope();
        return Lang.NormalizeOr(await RequestLanguage.GetStoredAsync(scope.ServiceProvider, ct));
    }

    /// <summary>"english" -> "en", everything else -> "de" (the fetchers know the Steam name of the language, the labels need the app language).</summary>
    private static string UiOfSteam(string steamLanguage) => steamLanguage == "english" ? Lang.English : Lang.German;

    private static string LastPlayedText(bool isRunning, string language) =>
        (isRunning ? ServerTexts.Live.PlayingNow : ServerTexts.Live.Ready).Resolve(language);

    public async Task<LiveGameAchievementsDto> GetAchievementsAsync(uint? appIdOverride = null, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var settingsStore = scope.ServiceProvider.GetRequiredService<SettingsStore>();
        var settings = await settingsStore.GetAsync(ct);
        var uiLanguage = ResolveUiLanguage(settings.UiLanguage);

        var targetAppId = appIdOverride ?? GetRunningAppId();
        if (targetAppId == 0)
        {
            lock (_stateLock)
            {
                if (_lastKnownAppId > 0)
                {
                    targetAppId = _lastKnownAppId;
                }
            }
        }

        // If no game is currently running, fall back to the most recently played game on the Steam profile.
        if (targetAppId == 0 && !string.IsNullOrWhiteSpace(settings.SteamUserId))
        {
            var recentAppId = await GetMostRecentGameAppIdAsync(settings.SteamUserId, ct);
            if (recentAppId > 0)
            {
                targetAppId = recentAppId;
                lock (_stateLock)
                {
                    _lastKnownAppId = targetAppId;
                }
            }
        }

        if (targetAppId == 0)
        {
            return EmptyAchievements(uiLanguage);
        }

        var linkedGame = await db.Games
            .AsNoTracking()
            .Where(g => g.SteamAppId == (int)targetAppId)
            .Select(g => new { g.Slug })
            .FirstOrDefaultAsync(ct);
        var linkedGuideSlug = linkedGame?.Slug;

        var gameInfo = await GetGameInfoAsync(targetAppId, settings.SteamUserId, linkedGuideSlug, ct, uiLanguage);
        var globalPercentages = await GetGlobalPercentagesAsync(targetAppId, ct);
        var notes = await settingsStore.GetNotesForAppAsync(targetAppId, ct);
        // Steam delivers the texts in this language; English = Steam original texts, no translation step
        var preferredLang = Lang.SteamName(uiLanguage);

        var localData = localStatsService.ReadLocalAppData(targetAppId, preferredLang);
        List<LiveAchievementItemDto>? achievements = null;

        if (!string.IsNullOrWhiteSpace(settings.SteamUserId))
        {
            achievements = await GetRemoteAchievementsAsync(targetAppId, settings.SteamUserId, globalPercentages, notes, preferredLang, ct);
        }

        // Merge with < 400ms local binary Steam stats
        if (localData is not null && localData.Achievements.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(localData.GameName) &&
                (string.IsNullOrWhiteSpace(gameInfo.Name) || gameInfo.Name.StartsWith("Steam App", StringComparison.OrdinalIgnoreCase)))
            {
                gameInfo = gameInfo with { Name = localData.GameName };
            }

            achievements = MergeLocalStats(achievements, localData.Achievements, globalPercentages, notes, uiLanguage);
        }

        // Fallback to public schema if neither local stats nor user XML returned achievements
        if (achievements is null || achievements.Count == 0)
        {
            achievements = await FetchFromPublicGuideOrStoreAsync(db, targetAppId, globalPercentages, notes, uiLanguage, ct);
        }

        // The realtime bridge knows what is unlocked right now - Steam's servers (XML / Web API) lag behind by a minute or more.
        // Merged the same way as the server-local stats above: unlocked on either side counts as unlocked.
        if (GetBridgeAchievementsFor(targetAppId) is { } bridge)
        {
            if (!string.IsNullOrWhiteSpace(bridge.GameName) &&
                (string.IsNullOrWhiteSpace(gameInfo.Name) || gameInfo.Name.StartsWith("Steam App", StringComparison.OrdinalIgnoreCase)))
            {
                gameInfo = gameInfo with { Name = bridge.GameName };
            }
            achievements = MergeBridgeAchievements(achievements, bridge, globalPercentages, notes, uiLanguage);
        }

        // Steam's progress counters ("50 / 400") come only from the local stats files of this PC
        if (localData is { Achievements.Count: > 0 })
        {
            var progressByName = localData.Achievements
                .Where(a => a.ProgressMax is > 0)
                .GroupBy(a => a.ApiName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < achievements.Count; i++)
            {
                if (progressByName.TryGetValue(achievements[i].ApiName, out var withProgress))
                {
                    achievements[i] = achievements[i] with { ProgressCurrent = withProgress.ProgressCurrent, ProgressMax = withProgress.ProgressMax };
                }
            }
        }

        // Names before localisation (usually English) - guides written with the original titles must still match
        var originalNames = achievements
            .Where(a => a.Achieved)
            .Select(a => a.DisplayName)
            .Concat(localData?.Achievements.Where(a => a.Achieved).Select(a => a.EnglishName) ?? [])
            .ToList();

        // AI translations only for games in the vault - a lookup of any other app must not use up the AI quota
        achievements = await LocalizeAchievementsAsync(targetAppId, gameInfo.Name, preferredLang, settings.AutoTranslate, linkedGame is not null, localData, achievements, ct);

        // Check off unlocked achievements in the game's 100% guide
        var syncedGuideCount = linkedGame is not null
            ? await SyncUnlockedAchievementsToGuideAsync(db, targetAppId, achievements, originalNames, ct)
            : 0;

        var total = achievements.Count;
        var achievedCount = achievements.Count(a => a.Achieved);
        if (linkedGame is not null)
        {
            await UpdateTrophySnapshotAsync(db, targetAppId, achievements, ct);
        }
        var remainingCount = Math.Max(0, total - achievedCount);
        var progressPct = total > 0 ? Math.Round((double)achievedCount / total * 100.0, 1) : 0.0;
        var is100 = total > 0 && achievedCount == total;

        var goldList = achievements.Where(a => a.RarityTier == "Gold").ToList();
        var silverList = achievements.Where(a => a.RarityTier == "Silver").ToList();
        var bronzeList = achievements.Where(a => a.RarityTier == "Bronze").ToList();

        var latest = achievements
            .Where(a => a.Achieved && a.UnlockTime.HasValue)
            .OrderByDescending(a => a.UnlockTime)
            .FirstOrDefault()
            ?? achievements.FirstOrDefault(a => a.Achieved);

        return new LiveGameAchievementsDto(
            Game: gameInfo,
            Achievements: achievements,
            TotalCount: total,
            AchievedCount: achievedCount,
            RemainingCount: remainingCount,
            ProgressPercentage: progressPct,
            Is100Percent: is100,
            Platinum: new LiveTrophyTierSummaryDto(is100 ? 1 : 0, 1),
            Gold: new LiveTrophyTierSummaryDto(goldList.Count(a => a.Achieved), goldList.Count),
            Silver: new LiveTrophyTierSummaryDto(silverList.Count(a => a.Achieved), silverList.Count),
            Bronze: new LiveTrophyTierSummaryDto(bronzeList.Count(a => a.Achieved), bronzeList.Count),
            LatestAchievement: latest,
            SyncedGuideItemsCount: syncedGuideCount
        );
    }

    /// <summary>
    /// Merges the local stats file of this PC into the remote list (Community XML / Web API, cached up to
    /// <see cref="RemoteCacheDuration"/> and lagging behind Steam's servers anyway). Unlocked on either side counts as unlocked, so a
    /// stale remote list never hides a fresh local unlock. When the remote list shares no name with the local file (e.g. an XML
    /// without API names) the local list is used instead - otherwise local unlocks could never reach the list.
    /// </summary>
    internal static List<LiveAchievementItemDto> MergeLocalStats(
        List<LiveAchievementItemDto>? remote,
        IReadOnlyList<SteamLocalStatsService.LocalAchievementStat> local,
        Dictionary<string, double> globalPercentages,
        Dictionary<string, string> notes,
        string uiLanguage)
    {
        var localMap = local
            .GroupBy(a => a.ApiName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        if (remote is null || remote.Count == 0 || !remote.Any(item => localMap.ContainsKey(item.ApiName)))
        {
            return local.Select(loc =>
            {
                var hasPct = globalPercentages.TryGetValue(loc.ApiName, out var p);
                var pct = hasPct ? p : 25.0;
                return new LiveAchievementItemDto(
                    ApiName: loc.ApiName,
                    DisplayName: loc.DisplayName,
                    Description: loc.Description,
                    OriginalDescription: null,
                    IsTranslated: false,
                    IconUrl: loc.Icon,
                    IconGrayUrl: loc.IconGray,
                    Achieved: loc.Achieved,
                    UnlockTime: loc.UnlockTime,
                    GlobalPercent: Math.Round(pct, 1),
                    RarityTier: CalculateRarityTier(pct),
                    RarityLabel: CalculateRarityLabel(pct, hasPct, uiLanguage),
                    IsHidden: loc.IsHidden,
                    UserNote: notes.GetValueOrDefault(loc.ApiName, string.Empty)
                )
                {
                    HasGlobalPercent = hasPct,
                    RarityBand = CalculateRarityBand(pct, hasPct),
                };
            }).ToList();
        }

        var merged = new List<LiveAchievementItemDto>(remote.Count);
        foreach (var item in remote)
        {
            if (!localMap.TryGetValue(item.ApiName, out var loc))
            {
                merged.Add(item);
                continue;
            }
            var achieved = item.Achieved || loc.Achieved;
            merged.Add(item with
            {
                Achieved = achieved,
                UnlockTime = loc.UnlockTime ?? item.UnlockTime,
                // The remote list chose the grey icon for a locked achievement; an unlock it does not know yet needs the colour one
                IconUrl = achieved && !item.Achieved && !string.IsNullOrEmpty(loc.Icon) ? loc.Icon : item.IconUrl,
                Description = string.IsNullOrWhiteSpace(item.Description) && !string.IsNullOrWhiteSpace(loc.Description)
                    ? loc.Description
                    : item.Description,
                IsHidden = item.IsHidden || loc.IsHidden
            });
        }
        return merged;
    }

    /// <summary>
    /// Achievement list from Steam Community XML (public profile) or, as fallback, the Web API (needs the key).
    /// Cached for <see cref="RemoteCacheDuration"/>; always returns a copy so callers can merge local data into it.
    /// </summary>
    private async Task<List<LiveAchievementItemDto>?> GetRemoteAchievementsAsync(
        uint appId,
        string steamId64,
        Dictionary<string, double> globalPercentages,
        Dictionary<string, string> notes,
        string lang,
        CancellationToken ct)
    {
        var cacheKey = (appId, steamId64, lang);
        lock (_stateLock)
        {
            if (_remoteCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CachedAt < RemoteCacheDuration)
            {
                return cached.Items
                    .Select(a => a with { UserNote = notes.GetValueOrDefault(a.ApiName, string.Empty) })
                    .ToList();
            }
        }

        var achievements = await FetchFromCommunityXmlAsync(appId, steamId64, globalPercentages, notes, lang, ct);
        var apiKey = secretStore.GetSecret(SettingsStore.SecretSteam);
        if ((achievements is null || achievements.Count == 0) && !string.IsNullOrWhiteSpace(apiKey))
        {
            achievements = await FetchFromWebApiAsync(appId, steamId64, apiKey, globalPercentages, notes, lang, ct);
        }

        if (achievements is { Count: > 0 })
        {
            lock (_stateLock)
            {
                _remoteCache[cacheKey] = (achievements, DateTime.UtcNow);
            }
            return [.. achievements];
        }

        return achievements;
    }

    private static LiveGameAchievementsDto EmptyAchievements(string uiLanguage)
    {
        var empty = new LiveTrophyTierSummaryDto(0, 0);
        return new LiveGameAchievementsDto(
            Game: new LiveGameInfoDto(0, ServerTexts.Live.NoGameDetected.Resolve(uiLanguage), string.Empty, string.Empty, false, 0, 0, string.Empty, string.Empty, ServerTexts.Live.Ready.Resolve(uiLanguage), null),
            Achievements: [],
            TotalCount: 0,
            AchievedCount: 0,
            RemainingCount: 0,
            ProgressPercentage: 0,
            Is100Percent: false,
            Platinum: empty,
            Gold: empty,
            Silver: empty,
            Bronze: empty,
            LatestAchievement: null,
            SyncedGuideItemsCount: 0);
    }

    /// <summary>
    /// Automatically checks off unlocked Steam achievements inside the matching game's 100% guide in the database.
    /// </summary>
    public async Task<int> SyncUnlockedAchievementsToGuideAsync(
        CompanionDbContext db,
        uint appId,
        List<LiveAchievementItemDto> achievements,
        IReadOnlyCollection<string> extraUnlockedNames,
        CancellationToken ct = default)
    {
        var unlocked = achievements.Where(a => a.Achieved).ToList();
        if (unlocked.Count == 0) return 0;

        var game = await db.Games
            .Include(g => g.Tabs)
            .Include(g => g.ProgressChecks)
            .FirstOrDefaultAsync(g => g.SteamAppId == (int)appId, ct);

        if (game is null) return 0;

        var existingKeys = game.ProgressChecks
            .Select(p => p.ItemKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unlockedNames = unlocked
            .Select(a => a.DisplayName)
            .Concat(extraUnlockedNames)
            .Select(NormalizeTitle)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unlockedApiNames = unlocked
            .Select(a => a.ApiName.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newlyChecked = 0;

        foreach (var tab in game.Tabs)
        {
            if (string.IsNullOrWhiteSpace(tab.SectionsJson)) continue;
            try
            {
                using var doc = JsonDocument.Parse(tab.SectionsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                foreach (var sec in doc.RootElement.EnumerateArray())
                {
                    if (!sec.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "checklist_table")
                        continue;
                    if (!sec.TryGetProperty("table", out var tableEl))
                        continue;
                    if (!tableEl.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var rowEl in rowsEl.EnumerateArray())
                    {
                        var itemId = rowEl.TryGetProperty("itemId", out var iid) ? iid.GetString() ?? string.Empty : string.Empty;
                        if (string.IsNullOrEmpty(itemId)) continue;

                        if (existingKeys.Contains(itemId)) continue;

                        var matched = unlockedApiNames.Contains(itemId);
                        if (!matched && rowEl.TryGetProperty("cells", out var cellsEl) && cellsEl.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var cell in cellsEl.EnumerateObject())
                            {
                                if (cell.Value.ValueKind != JsonValueKind.String) continue;
                                var cellVal = cell.Value.GetString();
                                if (string.IsNullOrWhiteSpace(cellVal)) continue;

                                if (TitleCandidates(cellVal).Any(unlockedNames.Contains))
                                {
                                    matched = true;
                                    break;
                                }
                            }
                        }

                        if (matched)
                        {
                            db.ProgressChecks.Add(new ProgressCheckEntity
                            {
                                Id = Guid.NewGuid(),
                                GameId = game.Id,
                                ItemKey = itemId,
                                CheckedAt = DateTimeOffset.UtcNow
                            });
                            existingKeys.Add(itemId);
                            newlyChecked++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Konnte Tab-JSON beim Achievement-Sync nicht prüfen.");
            }
        }

        if (newlyChecked > 0)
        {
            game.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("{Count} freigeschaltete Steam-Achievements automatisch im 100%-Guide ({Game}) abgehakt.", newlyChecked, game.Title);
        }

        return existingKeys.Count;
    }

    /// <summary>
    /// The ways a guide cell can name an achievement: "Title", "Title (Original)", "2. Deutscher Titel / English Title".
    /// Every variant is compared as a whole after normalising, so a short title never matches inside a longer text.
    /// </summary>
    private static IEnumerable<string> TitleCandidates(string cellValue)
    {
        var variants = new List<string> { cellValue };
        var parenIdx = cellValue.IndexOf('(');
        if (parenIdx > 0)
        {
            variants.Add(cellValue[..parenIdx]);
            variants.Add(cellValue[(parenIdx + 1)..].TrimEnd(')', ' ', '.'));
        }

        foreach (var variant in variants.ToList())
        {
            variants.AddRange(variant.Split([" / ", " | ", " – "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        foreach (var variant in variants)
        {
            // "12. Title" / "12) Title": the numbering belongs to the guide, not to the achievement
            var withoutNumber = LeadingNumbering().Replace(variant, string.Empty);
            var normalized = NormalizeTitle(withoutNumber);
            if (normalized.Length > 0) yield return normalized;
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*\d{1,3}[.)]\s+")]
    private static partial System.Text.RegularExpressions.Regex LeadingNumbering();

    /// <summary>
    /// Steam's own texts in the chosen language come first: the local schema of this PC (every language the game ships),
    /// then the Steam Web API schema (a server without a local Steam client, e.g. Unraid). Only achievements without an
    /// official translation are translated - by the AI when a key is set (game-aware, in the background, cached on
    /// disk), otherwise by the free translator.
    /// </summary>
    private async Task<List<LiveAchievementItemDto>> LocalizeAchievementsAsync(
        uint appId,
        string gameName,
        string lang,
        bool autoTranslate,
        bool allowAi,
        SteamLocalStatsService.LocalSchemaResult? localData,
        List<LiveAchievementItemDto> achievements,
        CancellationToken ct)
    {
        if (achievements.Count == 0 || lang == "english") return achievements;

        var official = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var local = localData?.Achievements
            .Where(a => a.IsLocalized)
            .GroupBy(a => a.ApiName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase) ?? [];
        var remote = local.Count == 0 ? await GetLocalizedSchemaAsync(appId, lang, ct) : null;

        for (var i = 0; i < achievements.Count; i++)
        {
            var a = achievements[i];
            if (local.TryGetValue(a.ApiName, out var loc))
            {
                achievements[i] = a with
                {
                    DisplayName = loc.DisplayName,
                    Description = string.IsNullOrWhiteSpace(loc.Description) ? a.Description : loc.Description,
                    OriginalDescription = null,
                    IsTranslated = false
                };
                official.Add(a.ApiName);
            }
            else if (remote is not null && remote.TryGetValue(a.ApiName, out var texts) &&
                     (!string.Equals(texts.Name, a.DisplayName, StringComparison.Ordinal) ||
                      (texts.Description.Length > 0 && !string.Equals(texts.Description, a.Description, StringComparison.Ordinal))))
            {
                // The Web API falls back to English for games without this language, so only a different text counts
                achievements[i] = a with
                {
                    DisplayName = string.IsNullOrWhiteSpace(texts.Name) ? a.DisplayName : texts.Name,
                    Description = string.IsNullOrWhiteSpace(texts.Description) ? a.Description : texts.Description,
                    OriginalDescription = null,
                    IsTranslated = false
                };
                official.Add(a.ApiName);
            }
        }

        if (!autoTranslate || lang != "german") return achievements;

        var pending = Enumerable.Range(0, achievements.Count)
            .Where(i => !official.Contains(achievements[i].ApiName) &&
                        !string.IsNullOrWhiteSpace(achievements[i].Description) &&
                        !TranslationService.IsLikelyGerman(achievements[i].Description))
            .ToList();
        if (pending.Count == 0) return achievements;

        var aiAvailable = allowAi && HasAiKey() &&
            !(_aiTranslationFailedAt.TryGetValue(appId, out var failedAt) && DateTime.UtcNow - failedAt < AiTranslationRetryDelay);
        if (aiAvailable)
        {
            var missing = pending
                .Select(i => achievements[i].Description)
                .Where(d => !translationService.TryGetAiTranslation(d, "de", out _))
                .ToList();
            if (missing.Count > 0) StartAiTranslation(appId, gameName, missing);
        }

        foreach (var i in pending)
        {
            var a = achievements[i];
            string? translated = null;
            if (translationService.TryGetAiTranslation(a.Description, "de", out var aiText))
            {
                translated = aiText;
            }
            else if (!aiAvailable)
            {
                var (text, wasTranslated) = await translationService.TranslateIfNeededAsync(a.Description, "de", ct);
                if (wasTranslated) translated = text;
            }
            // else: the AI translation is on its way - the original text is better than a word-by-word guess meanwhile

            if (translated is not null)
            {
                achievements[i] = a with { OriginalDescription = a.Description, Description = translated, IsTranslated = true };
            }
        }

        return achievements;
    }

    private bool HasAiKey() =>
        !string.IsNullOrWhiteSpace(secretStore.GetSecret(SettingsStore.SecretGemini)) ||
        !string.IsNullOrWhiteSpace(secretStore.GetSecret(SettingsStore.SecretClaude)) ||
        !string.IsNullOrWhiteSpace(secretStore.GetSecret(SettingsStore.SecretOpenAi));

    private void StartAiTranslation(uint appId, string gameName, List<string> texts)
    {
        var started = new TaskCompletionSource();
        if (!_aiTranslationJobs.TryAdd(appId, started.Task)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                await using var scope = scopeFactory.CreateAsyncScope();
                var ai = scope.ServiceProvider.GetRequiredService<AiGuideGeneratorService>();
                var translations = await ai.TranslateAchievementTextsAsync(gameName, texts, timeout.Token);
                if (translations.Count == 0) throw new InvalidOperationException("Die KI hat keine Übersetzung geliefert.");
                translationService.StoreAiTranslations(translations, "de");
                lock (_stateLock)
                {
                    // The next request rebuilds the list with the new texts
                    _remoteCache.Clear();
                }
                logger.LogInformation("{Count} Erfolgsbeschreibungen für App {AppId} per KI übersetzt.", translations.Count, appId);
            }
            catch (Exception ex)
            {
                _aiTranslationFailedAt[appId] = DateTime.UtcNow;
                logger.LogWarning("KI-Übersetzung der Erfolge für App {AppId} fehlgeschlagen, nutze die einfache Übersetzung: {Message}", appId, ex.Message);
            }
            finally
            {
                _aiTranslationJobs.TryRemove(appId, out _);
                started.TrySetResult();
            }
        });
    }

    /// <summary>Achievement names/descriptions in one language from the Steam Web API schema (needs the Steam key), cached a day.</summary>
    private async Task<Dictionary<string, (string Name, string Description)>?> GetLocalizedSchemaAsync(uint appId, string lang, CancellationToken ct)
    {
        if (_localizedSchemas.TryGetValue((appId, lang), out var cached) && DateTime.UtcNow - cached.CachedAt < LocalizedSchemaCacheDuration)
        {
            return cached.Texts;
        }

        var apiKey = secretStore.GetSecret(SettingsStore.SecretSteam);
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        try
        {
            var client = httpClientFactory.CreateClient("steam");
            var url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={Uri.EscapeDataString(apiKey)}&appid={appId}&l={Uri.EscapeDataString(lang)}";
            using var doc = JsonDocument.Parse(await client.GetStringAsync(url, ct));
            var texts = new Dictionary<string, (string Name, string Description)>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("game", out var game) &&
                game.TryGetProperty("availableGameStats", out var stats) &&
                stats.TryGetProperty("achievements", out var list) &&
                list.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in list.EnumerateArray())
                {
                    var apiName = s.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    if (apiName.Length == 0) continue;
                    var name = s.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? string.Empty : string.Empty;
                    var desc = s.TryGetProperty("description", out var ds) ? ds.GetString() ?? string.Empty : string.Empty;
                    texts[apiName] = (name, desc);
                }
            }

            _localizedSchemas[(appId, lang)] = (texts, DateTime.UtcNow);
            return texts;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Lokalisiertes Erfolgs-Schema für App {AppId} ({Lang}) nicht abrufbar.", appId, lang);
            return null;
        }
    }

    /// <summary>
    /// Stores the trophy state of a vault game (dashboard cards). Written only when it changed, otherwise at most every
    /// ten minutes for the timestamp - the game page polls every few seconds.
    /// </summary>
    private async Task UpdateTrophySnapshotAsync(CompanionDbContext db, uint appId, List<LiveAchievementItemDto> achievements, CancellationToken ct)
    {
        try
        {
            var total = achievements.Count;
            if (total == 0) return;
            var unlocked = achievements.Count(a => a.Achieved);
            var game = await db.Games.FirstOrDefaultAsync(g => g.SteamAppId == (int)appId, ct);
            if (game is null) return;

            var now = DateTimeOffset.UtcNow;
            var changed = game.TrophiesUnlocked != unlocked || game.TrophiesTotal != total;
            if (!changed && game.TrophiesUpdatedAt is { } updatedAt && now - updatedAt < TimeSpan.FromMinutes(10)) return;

            game.TrophiesUnlocked = unlocked;
            game.TrophiesTotal = total;
            game.TrophiesUpdatedAt = now;
            if (unlocked >= total)
            {
                // Platinum date = the last unlock, if Steam reports times
                var lastUnlock = achievements.Where(a => a.UnlockTime.HasValue).Select(a => a.UnlockTime!.Value).DefaultIfEmpty().Max();
                // Stored as UTC (UtcDateTimeOffsetConverter): convert here so the value read back equals the one kept in memory
                game.PlatinumAt ??= lastUnlock == default ? now : new DateTimeOffset(lastUnlock).ToUniversalTime();
            }
            else
            {
                game.PlatinumAt = null;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Trophäen-Stand für App {AppId} nicht gespeichert.", appId);
        }
    }

    /// <summary>Playtime, last session, Metacritic and reviews of a vault game for its dashboard card.</summary>
    private async Task UpdateCardFactsAsync(uint appId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var stats = scope.ServiceProvider.GetRequiredService<SteamStatsService>();
        var settings = await scope.ServiceProvider.GetRequiredService<SettingsStore>().GetAsync(ct);
        // The review text and release date are stored on the card: always in the stored app language, not in whatever a request
        // that happened to trigger this refresh asked for. Changing the app language resets FactsUpdatedAt, so cards follow it.
        using var languageScope = Lang.Use(Lang.NormalizeOr(settings.UiLanguage));

        var game = await db.Games.FirstOrDefaultAsync(g => g.SteamAppId == (int)appId, ct);
        if (game is null) return;

        // Only what the card shows (no news, no current player count)
        var facts = await stats.GetCardFactsAsync((int)appId, settings.SteamUserId ?? string.Empty, ct);
        game.MetacriticScore = facts.MetacriticScore ?? game.MetacriticScore;
        if (!string.IsNullOrWhiteSpace(facts.ReviewScoreDesc))
        {
            game.ReviewScoreDesc = facts.ReviewScoreDesc.Length > 60 ? facts.ReviewScoreDesc[..60] : facts.ReviewScoreDesc;
            game.ReviewPercent = facts.ReviewPercent;
        }
        if (!string.IsNullOrWhiteSpace(facts.ReleaseDate))
        {
            game.ReleaseDate = facts.ReleaseDate.Length > 60 ? facts.ReleaseDate[..60] : facts.ReleaseDate;
        }
        if (facts.PlaytimeHours is { } hours)
        {
            game.PlaytimeMinutes = (int)Math.Round(hours * 60);
        }
        if (facts.LastPlayed is { } lastPlayed)
        {
            game.LastPlayedAt = lastPlayed.ToUniversalTime();
        }

        // Genres from the Steam store (in the language of this refresh). Steam always wins; what is stored stays when Steam answers nothing
        var steamGenres = CleanGenres(facts.Genres);
        if (steamGenres.Count > 0)
        {
            game.SteamGenres = steamGenres;
            game.SteamGenresLanguage = Lang.NormalizeOr(settings.UiLanguage);
        }

        // IGDB (hours to 100 %, critics' rating) is optional: without Twitch credentials, or when IGDB is down, the card keeps
        // what it had and the Steam facts above are saved all the same
        if (igdbService.IsConfigured)
        {
            try
            {
                using var igdbTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                igdbTimeout.CancelAfter(TimeSpan.FromSeconds(25));
                if (await igdbService.GetBySteamAppIdAsync((int)appId, igdbTimeout.Token) is { } igdb)
                {
                    game.CompletionHours = igdb.TimeToBeat?.CompletelyHours ?? game.CompletionHours;
                    var rating = igdb.AggregatedRating ?? igdb.TotalRating;
                    game.IgdbRating = rating is { } r ? (int)Math.Round(r) : game.IgdbRating;
                    // Only a game without any genre takes IGDB's (English names); Steam's are better and replace them as soon as there are some
                    if (game.SteamGenres.Count == 0 && CleanGenres(igdb.Genres) is { Count: > 0 } igdbGenres)
                    {
                        game.SteamGenres = igdbGenres;
                        game.SteamGenresLanguage = Lang.English;
                    }
                }
            }
            catch (TwitchApiException ex)
            {
                logger.LogDebug("IGDB-Fakten für App {AppId} nicht verfügbar: {Message}", appId, ex.Message);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogDebug("IGDB-Fakten für App {AppId}: Zeitlimit erreicht.", appId);
            }
        }
        game.FactsUpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Trimmed, distinct, at most eight genre names (a card has room for a few).</summary>
    private static List<string> CleanGenres(IEnumerable<string> genres) =>
        genres.Select(g => g.Trim()).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();

    // Background refresh of trophy states and card facts for the dashboard. Games are queued (a new favourite never has
    // to wait for a whole run to finish), up to three are refreshed at a time, one worker drains the queue.
    private readonly ConcurrentDictionary<uint, byte> _refreshQueue = new();
    private int _trophyRefreshRunning;

    public void RefreshTrophySnapshots(IEnumerable<uint> appIds)
    {
        foreach (var appId in appIds) _refreshQueue.TryAdd(appId, 0);
        if (_refreshQueue.IsEmpty || Interlocked.CompareExchange(ref _trophyRefreshRunning, 1, 0) != 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_refreshQueue.IsEmpty)
                {
                    var batch = _refreshQueue.Keys.ToList();
                    foreach (var appId in batch) _refreshQueue.TryRemove(appId, out _);
                    await Parallel.ForEachAsync(batch, new ParallelOptions { MaxDegreeOfParallelism = 3 }, async (appId, _) =>
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                        try
                        {
                            // Trophies and card facts are independent - fetched at the same time
                            await Task.WhenAll(GetAchievementsAsync(appId, timeout.Token), UpdateCardFactsAsync(appId, timeout.Token));
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Trophäen-Stand für App {AppId} konnte nicht aktualisiert werden.", appId);
                        }
                    });
                }
            }
            finally
            {
                Interlocked.Exchange(ref _trophyRefreshRunning, 0);
                // A game queued right after the loop finished starts the next run
                if (!_refreshQueue.IsEmpty) RefreshTrophySnapshots([]);
            }
        });
    }

    private static string NormalizeTitle(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Name, artwork and playtime of a game. <paramref name="uiLanguage"/> = language of the texts; null = the current request's,
    /// else (background work: monitor worker, bridge) the stored app language.
    /// </summary>
    public async Task<LiveGameInfoDto> GetGameInfoAsync(uint appId, string steamId64, string? linkedGuideSlug, CancellationToken ct, string? uiLanguage = null)
    {
        var runningId = GetRunningAppId();
        var isRunning = runningId == appId && appId > 0;
        var language = await ResolveLanguageAsync(uiLanguage, ct);

        lock (_stateLock)
        {
            if (_gameInfoCache.TryGetValue((appId, language), out var cached) && (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 2)
            {
                return cached.Info with { IsRunning = isRunning, LinkedGuideSlug = linkedGuideSlug, FormattedLastPlayed = LastPlayedText(isRunning, language) };
            }
        }

        var name = $"Steam App {appId}";
        var headerImage = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";
        var backgroundImage = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/page_bg_generated_v6b.jpg";
        var playtimeForever = 0;
        var playtime2Weeks = 0;

        var client = httpClientFactory.CreateClient("steam");
        try
        {
            var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l={Lang.SteamName(language)}";
            using var resp = await client.GetAsync(url, ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty(appId.ToString(), out var appEl) &&
                    appEl.TryGetProperty("success", out var succ) && succ.GetBoolean() &&
                    appEl.TryGetProperty("data", out var dataEl))
                {
                    if (dataEl.TryGetProperty("name", out var n) && !string.IsNullOrWhiteSpace(n.GetString()))
                        name = n.GetString()!;
                    if (dataEl.TryGetProperty("header_image", out var h) && !string.IsNullOrWhiteSpace(h.GetString()))
                        headerImage = h.GetString()!;
                    if (dataEl.TryGetProperty("background", out var bg) && !string.IsNullOrWhiteSpace(bg.GetString()))
                        backgroundImage = bg.GetString()!;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Store-Details für App {AppId} konnten nicht geladen werden.", appId);
        }

        if (!string.IsNullOrWhiteSpace(steamId64))
        {
            try
            {
                var xmlUrl = $"https://steamcommunity.com/profiles/{steamId64}/games?tab=recent&xml=1";
                var xmlStr = await client.GetStringAsync(xmlUrl, ct);
                var xdoc = XDocument.Parse(xmlStr);
                foreach (var gNode in xdoc.Descendants("game"))
                {
                    if (uint.TryParse(gNode.Element("appID")?.Value, out var id) && id == appId)
                    {
                        var hoursForeverStr = gNode.Element("hoursOnRecord")?.Value?.Replace(",", "");
                        var hoursRecentStr = gNode.Element("hoursLast2Weeks")?.Value?.Replace(",", "");

                        if (double.TryParse(hoursForeverStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var hf))
                            playtimeForever = (int)Math.Round(hf * 60);
                        if (double.TryParse(hoursRecentStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var hr))
                            playtime2Weeks = (int)Math.Round(hr * 60);
                        break;
                    }
                }
            }
            catch
            {
                // Ignore XML playtime lookup failures
            }
        }

        var info = new LiveGameInfoDto(
            AppId: appId,
            Name: name,
            HeaderImage: headerImage,
            BackgroundImage: backgroundImage,
            IsRunning: isRunning,
            PlaytimeForeverMinutes: playtimeForever,
            Playtime2WeeksMinutes: playtime2Weeks,
            FormattedPlaytimeTotal: FormatPlaytime(playtimeForever, language),
            FormattedPlaytimeRecent: FormatPlaytime(playtime2Weeks, language),
            FormattedLastPlayed: LastPlayedText(isRunning, language),
            LinkedGuideSlug: linkedGuideSlug
        );

        lock (_stateLock)
        {
            _gameInfoCache[(appId, language)] = (info, DateTime.UtcNow);
        }

        return info;
    }

    private async Task<uint> GetMostRecentGameAppIdAsync(string steamId64, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("steam");
            var xmlUrl = $"https://steamcommunity.com/profiles/{steamId64}/games?tab=recent&xml=1";
            var xmlStr = await client.GetStringAsync(xmlUrl, ct);
            var doc = XDocument.Parse(xmlStr);
            var firstGame = doc.Descendants("game").FirstOrDefault();
            if (firstGame is not null && uint.TryParse(firstGame.Element("appID")?.Value, out var parsedId))
            {
                return parsedId;
            }
        }
        catch
        {
            // Ignore errors
        }
        return 0;
    }

    private async Task<List<LiveAchievementItemDto>?> FetchFromCommunityXmlAsync(
        uint appId,
        string steamId64,
        Dictionary<string, double> globalPercentages,
        Dictionary<string, string> notes,
        string lang,
        CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("steam");
            var url = $"https://steamcommunity.com/profiles/{steamId64}/stats/{appId}/achievements/?xml=1&l={lang}";
            using var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var xmlContent = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(xmlContent) || !xmlContent.TrimStart().StartsWith('<')) return null;

            var doc = XDocument.Parse(xmlContent);
            var achNodes = doc.Descendants("achievement").ToList();
            if (achNodes.Count == 0) return null;

            var list = new List<LiveAchievementItemDto>(achNodes.Count);
            foreach (var node in achNodes)
            {
                var closedAttr = node.Attribute("closed")?.Value;
                var achieved = closedAttr == "1";

                var apiName = node.Element("apiname")?.Value ?? Guid.NewGuid().ToString();
                var name = node.Element("name")?.Value ?? apiName;
                var desc = node.Element("description")?.Value ?? string.Empty;
                var iconClosed = node.Element("iconClosed")?.Value ?? string.Empty;
                var iconOpen = node.Element("iconOpen")?.Value ?? string.Empty;
                var unlockTimestampStr = node.Element("unlockTimestamp")?.Value;

                DateTime? unlockTime = null;
                if (achieved && long.TryParse(unlockTimestampStr, out var ts) && ts > 0)
                {
                    unlockTime = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
                }

                var hasPct = globalPercentages.TryGetValue(apiName, out var pct);
                var globalPct = hasPct ? pct : 25.0;
                var isHidden = string.IsNullOrWhiteSpace(desc) && !achieved;

                list.Add(new LiveAchievementItemDto(
                    ApiName: apiName,
                    DisplayName: name,
                    Description: desc,
                    OriginalDescription: null,
                    IsTranslated: false,
                    IconUrl: achieved ? iconClosed : iconOpen,
                    IconGrayUrl: iconOpen,
                    Achieved: achieved,
                    UnlockTime: unlockTime,
                    GlobalPercent: Math.Round(globalPct, 1),
                    RarityTier: CalculateRarityTier(globalPct),
                    RarityLabel: CalculateRarityLabel(globalPct, hasPct, UiOfSteam(lang)),
                    IsHidden: isHidden,
                    UserNote: notes.GetValueOrDefault(apiName, string.Empty)
                )
                {
                    HasGlobalPercent = hasPct,
                    RarityBand = CalculateRarityBand(globalPct, hasPct),
                });
            }
            return list;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Community-XML für App {AppId} nicht abrufbar.", appId);
            return null;
        }
    }

    private async Task<List<LiveAchievementItemDto>?> FetchFromWebApiAsync(
        uint appId,
        string steamId64,
        string apiKey,
        Dictionary<string, double> globalPercentages,
        Dictionary<string, string> notes,
        string lang,
        CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("steam");
            var schemaUrl = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={Uri.EscapeDataString(apiKey)}&appid={appId}&l={lang}";
            var playerUrl = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId64)}&appid={appId}&l={lang}";

            var schemaTask = client.GetStringAsync(schemaUrl, ct);
            var playerTask = client.GetStringAsync(playerUrl, ct);
            await Task.WhenAll(schemaTask, playerTask);

            using var schemaDoc = JsonDocument.Parse(await schemaTask);
            using var playerDoc = JsonDocument.Parse(await playerTask);

            var unlockedMap = new Dictionary<string, (bool Achieved, DateTime? UnlockTime)>(StringComparer.OrdinalIgnoreCase);
            if (playerDoc.RootElement.TryGetProperty("playerstats", out var ps) &&
                ps.TryGetProperty("achievements", out var playerAchArr) &&
                playerAchArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in playerAchArr.EnumerateArray())
                {
                    var apiname = p.GetProperty("apiname").GetString() ?? string.Empty;
                    var achieved = p.TryGetProperty("achieved", out var achVal) && achVal.GetInt32() == 1;
                    DateTime? unlockTime = null;
                    if (achieved && p.TryGetProperty("unlocktime", out var ut) && ut.GetInt64() > 0)
                    {
                        unlockTime = DateTimeOffset.FromUnixTimeSeconds(ut.GetInt64()).LocalDateTime;
                    }
                    unlockedMap[apiname] = (achieved, unlockTime);
                }
            }

            var list = new List<LiveAchievementItemDto>();
            if (schemaDoc.RootElement.TryGetProperty("game", out var g) &&
                g.TryGetProperty("availableGameStats", out var stats) &&
                stats.TryGetProperty("achievements", out var schemaAchArr) &&
                schemaAchArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in schemaAchArr.EnumerateArray())
                {
                    var apiName = s.GetProperty("name").GetString() ?? string.Empty;
                    var displayName = s.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? apiName : apiName;
                    var desc = s.TryGetProperty("description", out var ds) ? ds.GetString() ?? string.Empty : string.Empty;
                    var hidden = s.TryGetProperty("hidden", out var hd) && hd.GetInt32() == 1;
                    var icon = s.TryGetProperty("icon", out var ic) ? ic.GetString() ?? string.Empty : string.Empty;
                    var iconGray = s.TryGetProperty("icongray", out var icg) ? icg.GetString() ?? string.Empty : string.Empty;

                    unlockedMap.TryGetValue(apiName, out var pState);
                    var hasPct = globalPercentages.TryGetValue(apiName, out var pct);
                    var globalPct = hasPct ? pct : 25.0;

                    list.Add(new LiveAchievementItemDto(
                        ApiName: apiName,
                        DisplayName: displayName,
                        Description: desc,
                        OriginalDescription: null,
                        IsTranslated: false,
                        IconUrl: pState.Achieved ? icon : iconGray,
                        IconGrayUrl: iconGray,
                        Achieved: pState.Achieved,
                        UnlockTime: pState.UnlockTime,
                        GlobalPercent: Math.Round(globalPct, 1),
                        RarityTier: CalculateRarityTier(globalPct),
                        RarityLabel: CalculateRarityLabel(globalPct, hasPct, UiOfSteam(lang)),
                        IsHidden: hidden,
                        UserNote: notes.GetValueOrDefault(apiName, string.Empty)
                    )
                    {
                        HasGlobalPercent = hasPct,
                        RarityBand = CalculateRarityBand(globalPct, hasPct),
                    });
                }
            }

            return list;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Steam Web API Abruf für App {AppId} fehlgeschlagen.", appId);
            return null;
        }
    }

    private static async Task<List<LiveAchievementItemDto>> FetchFromPublicGuideOrStoreAsync(
        CompanionDbContext db,
        uint appId,
        Dictionary<string, double> globalPercentages,
        Dictionary<string, string> notes,
        string uiLanguage,
        CancellationToken ct)
    {
        var list = new List<LiveAchievementItemDto>();
        var game = await db.Games
            .AsNoTracking()
            .Include(g => g.Tabs)
            .Include(g => g.ProgressChecks)
            .FirstOrDefaultAsync(g => g.SteamAppId == (int)appId, ct);

        if (game is not null)
        {
            var checkedSet = game.ProgressChecks
                .Select(p => p.ItemKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Only the guide's achievement list (the Steam table, also the German "Steam-Erfolge", or the "achievements" tab of older guides) - never a
            // content chapter, whose rows are no achievements
            var achTab = game.Tabs
                .Where(t => ApiEndpoints.IsAchievementList(t, ApiEndpoints.DeserializeSections(t.SectionsJson)))
                .OrderByDescending(t => t.Slug != "achievements")
                .FirstOrDefault();

            if (achTab is not null && !string.IsNullOrWhiteSpace(achTab.SectionsJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(achTab.SectionsJson);
                    foreach (var sec in doc.RootElement.EnumerateArray())
                    {
                        if (!sec.TryGetProperty("type", out var tp) || tp.GetString() != "checklist_table") continue;
                        if (!sec.TryGetProperty("table", out var tbl)) continue;
                        if (!tbl.TryGetProperty("rows", out var rows)) continue;

                        var idx = 1;
                        foreach (var row in rows.EnumerateArray())
                        {
                            var itemId = row.TryGetProperty("itemId", out var iid) ? iid.GetString() ?? $"ach-{idx}" : $"ach-{idx}";
                            var cells = row.TryGetProperty("cells", out var cEl) ? cEl : default;
                            var title = cells.ValueKind == JsonValueKind.Object && cells.TryGetProperty("name", out var nEl)
                                ? nEl.GetString() ?? itemId
                                : itemId;
                            var desc = cells.ValueKind == JsonValueKind.Object && cells.TryGetProperty("tip", out var tEl)
                                ? tEl.GetString() ?? string.Empty
                                : (cells.ValueKind == JsonValueKind.Object && cells.TryGetProperty("cond", out var cdEl) ? cdEl.GetString() ?? string.Empty : string.Empty);

                            var isChecked = checkedSet.Contains(itemId);
                            var hasPct = globalPercentages.TryGetValue(itemId, out var gp);
                            var pct = hasPct ? gp : Math.Max(1.8, 65.0 - idx * 1.1);
                            var headerImg = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg";

                            list.Add(new LiveAchievementItemDto(
                                ApiName: itemId,
                                DisplayName: title,
                                Description: desc,
                                OriginalDescription: null,
                                IsTranslated: false,
                                IconUrl: headerImg,
                                IconGrayUrl: headerImg,
                                Achieved: isChecked,
                                UnlockTime: isChecked ? DateTime.Now : null,
                                GlobalPercent: Math.Round(pct, 1),
                                RarityTier: CalculateRarityTier(pct),
                                RarityLabel: CalculateRarityLabel(pct, hasPct, uiLanguage),
                                IsHidden: false,
                                UserNote: notes.GetValueOrDefault(itemId, string.Empty)
                            )
                            {
                                // The made-up descending fallback share is no real data: only a value Steam delivered counts
                                HasGlobalPercent = hasPct,
                                RarityBand = CalculateRarityBand(pct, hasPct),
                            });
                            idx++;
                        }
                    }
                }
                catch
                {
                    // Ignore parse fallback errors
                }
            }
        }

        return list;
    }

    private async Task<Dictionary<string, double>> GetGlobalPercentagesAsync(uint appId, CancellationToken ct)
    {
        lock (_stateLock)
        {
            if (_globalPercentCache.TryGetValue(appId, out var cached) && (DateTime.UtcNow - cached.CachedAt).TotalMinutes < 30)
            {
                return cached.Map;
            }
        }

        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var client = httpClientFactory.CreateClient("steam");
            var url = $"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v0002/?gameid={appId}&format=json";
            using var resp = await client.GetAsync(url, ct);
            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.TryGetProperty("achievementpercentages", out var ap) &&
                    ap.TryGetProperty("achievements", out var arr) &&
                    arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        var name = item.GetProperty("name").GetString();
                        double pct = 0;
                        if (item.TryGetProperty("percent", out var pVal))
                        {
                            if (pVal.ValueKind == JsonValueKind.Number)
                                pct = pVal.GetDouble();
                            else if (pVal.ValueKind == JsonValueKind.String)
                                _ = double.TryParse(pVal.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out pct);
                        }

                        if (!string.IsNullOrEmpty(name))
                        {
                            map[name] = pct;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Globale Seltenheitswerte für App {AppId} nicht verfügbar.", appId);
        }

        lock (_stateLock)
        {
            _globalPercentCache[appId] = (map, DateTime.UtcNow);
        }
        return map;
    }

    // Trophy tier from Steam's worldwide share (user decision 2026-11-04: gold up to and including 10 %, silver up to and including 50 %; was 5 % / 25 %).
    // The texts in stats.ts (tiers.ruleGold/ruleSilver/ruleBronze) and the handbook name the same limits.
    public static string CalculateRarityTier(double globalPercent)
    {
        if (globalPercent <= 10.0) return "Gold";
        if (globalPercent <= 50.0) return "Silver";
        return "Bronze";
    }

    // The one rarity scale of the whole app (live trophies, popups, statistics bands use the same five steps).
    // Lower bound inclusive: exactly 5 % is already "very rare". The tier above (Gold/Silver/Bronze) keeps its own
    // thresholds on purpose. Without a real worldwide share the band is empty, so the UI simply shows none.
    // The band is the language-neutral key the UI translates ("ultraRare", "veryRare", "rare", "uncommon", "common").
    public static string CalculateRarityBand(double globalPercent, bool hasGlobalPercent = true)
    {
        if (!hasGlobalPercent) return string.Empty;
        if (globalPercent < 5.0) return "ultraRare";
        if (globalPercent < 10.0) return "veryRare";
        if (globalPercent < 20.0) return "rare";
        if (globalPercent < 50.0) return "uncommon";
        return "common";
    }

    /// <summary>The same scale as a ready-made text in <paramref name="language"/> (null = the language of the current request). Kept for older clients; the UI translates the band.</summary>
    public static string CalculateRarityLabel(double globalPercent, bool hasGlobalPercent = true, string? language = null)
    {
        var band = CalculateRarityBand(globalPercent, hasGlobalPercent);
        return band.Length == 0 ? string.Empty : ServerTexts.Live.RarityLabel(band, language ?? Lang.Current);
    }

    private static string FormatPlaytime(int minutes, string language) =>
        minutes <= 0 ? string.Empty : ServerTexts.Live.Playtime(minutes, language);
}