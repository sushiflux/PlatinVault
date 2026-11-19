using Companion.Api.Data;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Companion.Api.Endpoints;

/// <summary>
/// One game in the trophy cabinet: every achievement unlocked (a Steam game, or a game added by hand at its platinum moment). Missing figures stay
/// null (the UI hides them). Key identifies the entry ("steam:{appId}" | "game:{guid}"); a game added by hand has AppId 0, a GameId and its platform.
/// </summary>
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
    double? AverageRarityPercent,
    string? Key = null,
    Guid? GameId = null,
    string? Platform = null,
    string? PlatformVariant = null
);

public sealed record ShowcaseDto(List<ShowcaseGameDto> Games, int TotalTrophies, double? TotalPlaytimeHours);

/// <summary>
/// The trophy cabinet: all games of the whole Steam library at 100 % (library evaluation) plus vault games with a
/// platinum date (Steam games and games added by hand), newest platinum first. Per game the achievement list (localized names, unlock times, global rarity)
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
            // (every game counts, Steam or added by hand: both have a platinum date)
            var platinumGames = db.Games.AsNoTracking().Where(g => g.PlatinumAt != null);
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
                            : null,
                        Key: $"steam:{appId}",
                        Platform: GamePlatforms.Steam));
                }

                games.AddRange(await ManualPlatinumGamesAsync(db, ct));

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
            Results.Ok(await CabinetPresented.AddAsync(db, request.AppIds, ct, request.GameIds)));
    }

    /// <summary>
    /// The games added by hand that reached their platinum moment (platinum trophy ticked, or 100 % where the platform has no platinum), with the
    /// figures of their own trophy list: ticks give first/last unlock and days to platinum, a stated share gives the rarest and the average;
    /// there is no playtime. Empty when there are none.
    /// </summary>
    internal static async Task<List<ShowcaseGameDto>> ManualPlatinumGamesAsync(CompanionDbContext db, CancellationToken ct)
    {
        var manual = await db.Games.AsNoTracking()
            .Where(g => g.Platform != GamePlatforms.Steam && g.PlatinumAt != null)
            .Select(g => new { g.Id, g.Slug, g.Title, g.CoverUrl, g.HeroBannerUrl, g.Platform, g.PlatformVariant, g.PlatinumAt })
            .ToListAsync(ct);
        if (manual.Count == 0)
        {
            return [];
        }

        var ids = manual.Select(g => g.Id).ToList();
        var trophies = (await db.GameTrophies.AsNoTracking()
                .Where(t => ids.Contains(t.GameId))
                .Select(t => new { t.GameId, t.Name, t.UnlockedAt, t.RarityPercent })
                .ToListAsync(ct))
            .ToLookup(t => t.GameId);

        return manual.Select(g =>
        {
            var list = trophies[g.Id].ToList();
            var ticked = list.Where(t => t.UnlockedAt != null).ToList();
            DateTimeOffset? first = ticked.Count > 0 ? ticked.Min(t => t.UnlockedAt) : null;
            // A share of 0 or none is no data (the AI states one only with a source)
            var withShare = ticked.Where(t => t.RarityPercent is > 0 and <= 100).ToList();
            var rarest = withShare.OrderBy(t => t.RarityPercent).FirstOrDefault();
            return new ShowcaseGameDto(
                AppId: 0,
                Name: g.Title,
                CoverUrl: string.IsNullOrWhiteSpace(g.CoverUrl) ? null : g.CoverUrl,
                HeroBannerUrl: string.IsNullOrWhiteSpace(g.HeroBannerUrl) ? null : g.HeroBannerUrl,
                ExistingSlug: g.Slug,
                TrophiesTotal: list.Count,
                PlatinumAt: g.PlatinumAt,
                FirstUnlockAt: first,
                DaysToPlatinum: first is { } f && g.PlatinumAt is { } p ? Math.Max(1, (int)Math.Ceiling((p - f).TotalDays)) : null,
                PlaytimeHours: null,
                RarestName: rarest?.Name,
                RarestIconUrl: null,
                RarestPercent: rarest?.RarityPercent,
                AverageRarityPercent: withShare.Count > 0 ? Math.Round(withShare.Average(t => t.RarityPercent!.Value), 1) : null,
                Key: $"game:{g.Id:D}",
                GameId: g.Id,
                Platform: g.Platform,
                PlatformVariant: g.PlatformVariant);
        }).ToList();
    }
}
