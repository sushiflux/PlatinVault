using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Hubs;
using Companion.Api.Localization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>
/// Takes the state the realtime bridge pushes (running game + unlocked achievements read from the local Steam stats file
/// of the gaming PC), compares it with the previous state and announces new unlocks immediately - without waiting for
/// Steam's servers. Only games in the vault are announced.
/// </summary>
public sealed class BridgeIngestService(
    SteamLiveTrackerService liveTracker,
    IServiceScopeFactory scopeFactory,
    IHubContext<AchievementHub> hubContext,
    LiveAnnouncer announcer,
    UnlockWatermarkStore watermarks,
    ILogger<BridgeIngestService> logger)
{
    // Enriching the unlock (name, icon, rarity) may need a Steam request; never let that delay the popup for long
    private static readonly TimeSpan EnrichmentTimeout = TimeSpan.FromSeconds(5);

    public const int MaxAchievements = 5000;
    public const int MaxNameLength = 200;

    public async Task IngestAsync(uint runningAppId, string gameName, string? steamId64, List<BridgeAchievement> achievements, CancellationToken ct)
    {
        var snapshot = new BridgeSnapshot(runningAppId, gameName, steamId64, achievements, DateTimeOffset.UtcNow);
        var previous = liveTracker.RecordBridgeState(snapshot);
        var appChanged = previous?.RunningAppId != runningAppId;

        if (runningAppId == 0)
        {
            // Game closed: the next start of a game takes a fresh baseline and is announced again
            liveTracker.ResetUnlockLedger(UnlockSource.Bridge);
            liveTracker.ClearAnnouncedGame();
            return;
        }

        if (!await IsTrackedAsync(runningAppId, ct))
        {
            return;
        }

        if (appChanged && liveTracker.TryAnnounceGame(runningAppId))
        {
            var status = await liveTracker.GetStatusAsync(ct);
            var gameInfo = await liveTracker.GetGameInfoAsync(runningAppId, status.SteamId64, status.RunningGameSlug, ct);
            await hubContext.Clients.All.SendAsync("GameChanged", gameInfo, ct);
        }

        if (achievements.Count == 0)
        {
            return;
        }

        // A baseline (first report since the server started, or after the game was closed) also catches up on unlocks after the
        // stored watermark - they happened while the server was restarting
        DateTimeOffset? catchUpAfter = liveTracker.NeedsUnlockBaseline(UnlockSource.Bridge, runningAppId)
            ? UnlockLedgers.CatchUpAfter(await watermarks.GetAsync(runningAppId, ct), DateTimeOffset.UtcNow)
            : null;
        var achieved = achievements
            .Where(a => a.Achieved)
            .Select(a => new UnlockState(a.ApiName, a.UnlockTime))
            .ToList();
        var diff = liveTracker.DiffUnlocks(UnlockSource.Bridge, runningAppId, achieved, achievements.Count, catchUpAfter);
        if (diff.IsBaseline)
        {
            logger.LogInformation(
                "Bridge, App {AppId}: Ausgangsstand übernommen: {Achieved}/{Total} freigeschaltet{CatchUp}.",
                runningAppId, achieved.Count, achievements.Count,
                diff.CaughtUp > 0 ? $"; {diff.CaughtUp} während eines Neustarts freigeschaltete(s) wird nachgeholt" : string.Empty);
        }
        if (diff.Fresh.Count == 0)
        {
            if (diff.IsBaseline)
            {
                await watermarks.SetAsync(runningAppId, UnlockLedgers.NextWatermark(achieved, DateTimeOffset.UtcNow), ct);
            }
            return;
        }

        var enriched = await TryGetEnrichedListAsync(runningAppId, ct);
        var byName = enriched?.Achievements
            .GroupBy(a => a.ApiName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var title = enriched?.Game.Name is { Length: > 0 } fromList && !fromList.StartsWith("Steam App", StringComparison.OrdinalIgnoreCase)
            ? fromList
            : gameName;

        foreach (var name in diff.Fresh)
        {
            var reported = achievements.First(a => string.Equals(a.ApiName, name, StringComparison.OrdinalIgnoreCase));
            var item = byName is not null && byName.TryGetValue(name, out var known)
                ? known with { Achieved = true, UnlockTime = known.UnlockTime ?? reported.UnlockTime ?? DateTime.Now }
                : MinimalItem(name, reported.UnlockTime);

            await announcer.AnnounceUnlockAsync(item, title, (int)runningAppId, "Bridge", ct);
        }

        if (achievements.All(a => a.Achieved))
        {
            await announcer.AnnouncePlatinumAsync(title, achievements.Count, (int)runningAppId, "Bridge", ct);
        }

        // Everything up to now is taken into account - a restart only catches up on what happens after this
        await watermarks.SetAsync(runningAppId, UnlockLedgers.NextWatermark(achieved, DateTimeOffset.UtcNow), ct);
    }

    private async Task<LiveGameAchievementsDto?> TryGetEnrichedListAsync(uint appId, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(EnrichmentTimeout);
        try
        {
            // The popup texts belong to the app and its chosen language, not to the bridge that sent the report with its own
            // Accept-Language (its first report comes before it has learned the app language, and --language can pin it to another one)
            string appLanguage;
            using (var scope = scopeFactory.CreateScope())
            {
                appLanguage = await RequestLanguage.GetStoredAsync(scope.ServiceProvider, timeout.Token) ?? Lang.German;
            }
            using var language = Lang.Use(appLanguage);
            return await liveTracker.GetAchievementsAsync(appId, timeout.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Achievement-Liste für App {AppId} nicht rechtzeitig verfügbar - Popup ohne Namen/Symbol: {Message}", appId, ex.Message);
            return null;
        }
    }

    private static LiveAchievementItemDto MinimalItem(string apiName, DateTime? unlockTime) => new(
        ApiName: apiName,
        DisplayName: apiName,
        Description: string.Empty,
        OriginalDescription: null,
        IsTranslated: false,
        IconUrl: string.Empty,
        IconGrayUrl: string.Empty,
        Achieved: true,
        UnlockTime: unlockTime ?? DateTime.Now,
        GlobalPercent: 25.0,
        RarityTier: SteamLiveTrackerService.CalculateRarityTier(25.0),
        // No worldwide share is known here (HasGlobalPercent stays false), so the label is empty
        RarityLabel: SteamLiveTrackerService.CalculateRarityLabel(25.0, hasGlobalPercent: false),
        IsHidden: false,
        UserNote: string.Empty);

    private async Task<bool> IsTrackedAsync(uint appId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        return await db.Games.AsNoTracking().AnyAsync(
            g => g.SteamAppId == (int)appId, ct);
    }
}
