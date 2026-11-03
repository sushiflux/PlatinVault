using Companion.Api.Data;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Companion.Api.Endpoints;

/// <summary>One game in the trophy cabinet: every Steam achievement unlocked. Missing figures stay null (the UI hides them).</summary>
public sealed record ShowcaseGameDto(
    int AppId,
    string Name,
    string? CoverUrl,
    string? HeroBannerUrl,
    string? ExistingSlug,
    int TrophiesTotal,
    DateTimeOffset? PlatinumAt,
    DateTimeOffset? FirstUnlockAt,
    int? DaysToPlatinum,
    double? PlaytimeHours,
    string? RarestName,
    string? RarestIconUrl,
    double? RarestPercent,
    double? AverageRarityPercent
);

public sealed record ShowcaseDto(List<ShowcaseGameDto> Games, int TotalTrophies, double? TotalPlaytimeHours);

/// <summary>
/// The trophy cabinet: all games of the whole Steam library at 100 % (library evaluation) plus vault games with a
/// platinum date, newest platinum first. Per game the achievement list (localized names, unlock times, global rarity)
/// comes from the same source as the live trophies tab; the result is cached for ten minutes.
/// </summary>
public static class ShowcaseEndpoints
{
    public static void MapShowcase(this RouteGroupBuilder api)
    {
        api.MapGet("/showcase", async (
            CompanionDbContext db,
            LibraryStatsService libraryStats,
            SteamLiveTrackerService liveTracker,
            SteamClient steamClient,
            SecretStore secrets,
            IMemoryCache cache,
            CancellationToken ct) =>
        {
            // A game that just reached 100 % must be in the cabinet right away (it is presented there): the cache key carries the state of the
            // vault's platinum dates, so a new platinum starts a fresh evaluation instead of waiting for the ten minutes to pass
            var platinumGames = db.Games.AsNoTracking().Where(g => g.SteamAppId != null && g.PlatinumAt != null);
            var platinumCount = await platinumGames.CountAsync(ct);
            var latestPlatinum = platinumCount > 0 ? await platinumGames.MaxAsync(g => g.PlatinumAt, ct) : null;
            var result = await cache.GetOrCreateAsync($"showcase:{platinumCount}:{latestPlatinum?.UtcTicks}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                var vault = await db.Games.AsNoTracking()
                    .Where(g => g.SteamAppId != null)
                    .Select(g => new { AppId = g.SteamAppId!.Value, g.Slug, g.Title, g.CoverUrl, g.HeroBannerUrl, g.PlatinumAt, g.TrophiesTotal })
                    .ToListAsync(ct);
                var appIds = libraryStats.PerfectAppIds()
                    .Concat(vault.Where(g => g.PlatinumAt != null).Select(g => g.AppId))
                    .Distinct()
                    .ToList();

                // Names, covers and playtime of the library (only with a Steam key); vault games carry their own
                Dictionary<int, SteamOwnedGameDto> owned = [];
                if (!string.IsNullOrWhiteSpace(secrets.GetSecret(SettingsStore.SecretSteam)))
                {
                    try
                    {
                        var library = await cache.GetOrCreateAsync("steam-library-summary-source", async e =>
                        {
                            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                            return await steamClient.FetchOwnedGamesAsync(null, [], ct);
                        });
                        owned = library?.Games.ToDictionary(g => g.AppId) ?? [];
                    }
                    catch (InvalidOperationException)
                    {
                        // Library not available - the cabinet then shows the vault games only
                    }
                }

                var games = new List<ShowcaseGameDto>();
                foreach (var appId in appIds)
                {
                    var inVault = vault.FirstOrDefault(g => g.AppId == appId);
                    owned.TryGetValue(appId, out var ownedGame);
                    LiveGameAchievementsDto? achievements = null;
                    try
                    {
                        achievements = await liveTracker.GetAchievementsAsync((uint)appId, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Figures stay null for this game
                    }

                    var unlocked = achievements?.Achievements.Where(a => a.Achieved).ToList() ?? [];
                    var times = unlocked.Where(a => a.UnlockTime.HasValue).Select(a => new DateTimeOffset(a.UnlockTime!.Value).ToUniversalTime()).ToList();
                    // Placeholder shares (HasGlobalPercent = false) are no data and must not feed the rarity figures
                    var withShare = unlocked.Where(a => a.HasGlobalPercent && a.GlobalPercent > 0).ToList();
                    var rarest = withShare.OrderBy(a => a.GlobalPercent).FirstOrDefault();
                    DateTimeOffset? first = times.Count > 0 ? times.Min() : null;
                    DateTimeOffset? last = times.Count > 0 ? times.Max() : inVault?.PlatinumAt;

                    games.Add(new ShowcaseGameDto(
                        AppId: appId,
                        Name: inVault?.Title ?? ownedGame?.Name ?? achievements?.Game.Name ?? $"Steam-App {appId}",
                        CoverUrl: !string.IsNullOrWhiteSpace(inVault?.CoverUrl) ? inVault.CoverUrl : ownedGame?.CoverUrl,
                        HeroBannerUrl: !string.IsNullOrWhiteSpace(inVault?.HeroBannerUrl) ? inVault.HeroBannerUrl : ownedGame?.HeroBannerUrl,
                        ExistingSlug: inVault?.Slug,
                        TrophiesTotal: achievements?.TotalCount ?? libraryStats.Get(appId)?.Total ?? inVault?.TrophiesTotal ?? 0,
                        PlatinumAt: last,
                        FirstUnlockAt: first,
                        DaysToPlatinum: first is { } f && last is { } l ? Math.Max(1, (int)Math.Ceiling((l - f).TotalDays)) : null,
                        PlaytimeHours: ownedGame is { PlaytimeHours: > 0 } ? ownedGame.PlaytimeHours : null,
                        RarestName: rarest?.DisplayName,
                        RarestIconUrl: rarest?.IconUrl,
                        RarestPercent: rarest?.GlobalPercent,
                        AverageRarityPercent: withShare.Count > 0
                            ? Math.Round(withShare.Average(a => a.GlobalPercent), 1)
                            : null));
                }

                var ordered = games.OrderByDescending(g => g.PlatinumAt ?? DateTimeOffset.MinValue).ToList();
                var playtimes = ordered.Where(g => g.PlaytimeHours.HasValue).Select(g => g.PlaytimeHours!.Value).ToList();
                return new ShowcaseDto(ordered, ordered.Sum(g => g.TrophiesTotal), playtimes.Count > 0 ? Math.Round(playtimes.Sum(), 1) : null);
            });
            return Results.Ok(result);
        });

        // Which completed games the cabinet has already presented (animated into their slot); see CabinetPresented
        api.MapGet("/showcase/presented", async (CompanionDbContext db, CancellationToken ct) =>
            Results.Ok(await CabinetPresented.GetAsync(db, ct)));

        // Adds games to the presented set (never removes). Without a stored state the same call creates the baseline.
        api.MapPut("/showcase/presented", async (CabinetPresentedRequest request, CompanionDbContext db, CancellationToken ct) =>
            Results.Ok(await CabinetPresented.AddAsync(db, request.AppIds, ct)));
    }
}
