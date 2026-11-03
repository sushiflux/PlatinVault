using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Companion.Api.Endpoints;

/// <summary>
/// Data for the charts of the game page's statistics tab: the series a game belongs to (IGDB, needs the player's own Twitch
/// keys) and the history PlatinVault records itself (player counts and daily playtime, see <see cref="HistoryRecorderWorker"/>).
/// </summary>
public static class StatisticsEndpoints
{
    public static void MapStatistics(this RouteGroupBuilder api)
    {
        // The series of a game: 204 = no IGDB credentials, no Steam AppID, game unknown to IGDB or in no series; 404 = unknown slug;
        // 502 = IGDB cannot be reached (detail in the language of the request)
        api.MapGet("/games/{slug}/series", async (
            string slug,
            CompanionDbContext db,
            IgdbService igdb,
            SteamClient steamClient,
            LibraryStatsService libraryStats,
            SecretStore secrets,
            IMemoryCache cache,
            ILogger<IgdbService> logger,
            CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking()
                .Where(g => g.Slug == slug)
                .Select(g => new { g.SteamAppId })
                .FirstOrDefaultAsync(ct);
            if (game is null)
            {
                return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.GameNotFoundTitle, ServerTexts.GameNotFound(slug));
            }
            if (game.SteamAppId is not > 0 || !igdb.IsConfigured)
            {
                return Results.NoContent();
            }

            IgdbService.SeriesInfo? series;
            int? currentIgdbId;
            try
            {
                series = await igdb.GetSeriesAsync(game.SteamAppId.Value, ct);
                currentIgdbId = await igdb.GetIgdbIdAsync(game.SteamAppId.Value, ct);
            }
            catch (TwitchApiException ex)
            {
                return Problems.Create(StatusCodes.Status502BadGateway, ServerTexts.Twitch.IgdbUnavailableTitle, ex.Message);
            }
            if (series is null)
            {
                return Results.NoContent();
            }

            // The user's side, computed fresh on every request (the IGDB part above is cached for a week)
            var vault = await db.Games.AsNoTracking()
                .Where(g => g.SteamAppId != null)
                .Select(g => new { AppId = g.SteamAppId!.Value, g.Slug, g.TrophiesUnlocked, g.TrophiesTotal })
                .ToListAsync(ct);
            var vaultByApp = vault.GroupBy(g => g.AppId).ToDictionary(g => g.Key, g => g.First());

            // Owned = in the Steam library; needs the Steam key (without it nothing is claimed to be owned)
            var owned = new HashSet<int>();
            if (!string.IsNullOrWhiteSpace(secrets.GetSecret(SettingsStore.SecretSteam)))
            {
                try
                {
                    var library = await cache.GetOrCreateAsync("steam-library-summary-source", async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                        return await steamClient.FetchOwnedGamesAsync(null, [], ct);
                    });
                    owned = library?.Games.Select(g => g.AppId).ToHashSet() ?? [];
                }
                catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
                {
                    logger.LogDebug("Steam-Bibliothek für die Serie nicht verfügbar: {Message}", ex.Message);
                }
            }

            var games = series.Games.Select(g =>
            {
                var appId = g.SteamAppId;
                var inVault = appId is { } a && vaultByApp.ContainsKey(a);
                var vaultGame = inVault ? vaultByApp[appId!.Value] : null;

                int? unlocked = null;
                int? total = null;
                if (vaultGame is { TrophiesTotal: > 0 })
                {
                    (unlocked, total) = (vaultGame.TrophiesUnlocked, vaultGame.TrophiesTotal);
                }
                else if (appId is { } id && libraryStats.Get(id) is { Total: > 0 } stat)
                {
                    (unlocked, total) = (stat.Unlocked, stat.Total);
                }

                return new GameSeriesGameDto(
                    IgdbId: g.IgdbId,
                    Name: g.Name,
                    CoverUrl: g.CoverUrl,
                    ReleaseYear: g.ReleaseYear,
                    SteamAppId: appId,
                    Owned: appId is { } own && owned.Contains(own),
                    InVault: inVault,
                    Slug: vaultGame?.Slug,
                    TrophiesUnlocked: unlocked,
                    TrophiesTotal: total,
                    IsCurrent: g.IgdbId == currentIgdbId || (appId is not null && appId == game.SteamAppId));
            }).ToList();

            return Results.Ok(new GameSeriesDto(series.Name, series.Source, games));
        });

        // What was recorded over time: player counts (raw last 30 days + condensed older days) and daily playtime. 200 with empty
        // lists while nothing is recorded; days is clamped to 7..730 (default 90)
        api.MapGet("/games/{slug}/history", async (string slug, int? days, CompanionDbContext db, CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking()
                .Where(g => g.Slug == slug)
                .Select(g => new { g.SteamAppId })
                .FirstOrDefaultAsync(ct);
            if (game is null)
            {
                return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.GameNotFoundTitle, ServerTexts.GameNotFound(slug));
            }
            if (game.SteamAppId is not > 0)
            {
                return Results.Ok(new GameHistoryDto(null, [], [], []));
            }

            var appId = game.SteamAppId.Value;
            var range = Math.Clamp(days ?? 90, 7, 730);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var fromDate = today.AddDays(-range);
            var fromInstant = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            var playerCounts = await db.PlayerCountSamples.AsNoTracking()
                .Where(s => s.AppId == appId && s.SampledAt >= fromInstant)
                .OrderBy(s => s.SampledAt)
                .Select(s => new PlayerCountPointDto(s.SampledAt, s.Count))
                .ToListAsync(ct);

            var playerCountsDaily = (await db.PlayerCountDaily.AsNoTracking()
                .Where(d => d.AppId == appId && d.Date >= fromDate)
                .OrderBy(d => d.Date)
                .ToListAsync(ct))
                .Select(d => new PlayerCountDayDto(d.Date, d.MinCount, Math.Round(d.AvgCount, 1), d.MaxCount))
                .ToList();

            // Minutes played per day = difference to the previous snapshot (the last one before the range is the starting point);
            // days without a snapshot are left out, a drop (source change) counts as 0
            var baseline = await db.PlaytimeSnapshots.AsNoTracking()
                .Where(s => s.AppId == appId && s.Date < fromDate)
                .OrderByDescending(s => s.Date)
                .FirstOrDefaultAsync(ct);
            var snapshots = await db.PlaytimeSnapshots.AsNoTracking()
                .Where(s => s.AppId == appId && s.Date >= fromDate)
                .OrderBy(s => s.Date)
                .ToListAsync(ct);
            var playtimeDaily = new List<PlaytimeDayDto>();
            var previous = baseline;
            foreach (var snapshot in snapshots)
            {
                if (previous is not null)
                {
                    playtimeDaily.Add(new PlaytimeDayDto(snapshot.Date, Math.Max(0, snapshot.PlaytimeMinutes - previous.PlaytimeMinutes)));
                }
                previous = snapshot;
            }

            // The first thing ever recorded for this game
            var firstSample = await db.PlayerCountSamples.AsNoTracking().Where(s => s.AppId == appId).MinAsync(s => (DateTimeOffset?)s.SampledAt, ct);
            var firstDaily = await db.PlayerCountDaily.AsNoTracking().Where(d => d.AppId == appId).MinAsync(d => (DateOnly?)d.Date, ct);
            var firstSnapshot = await db.PlaytimeSnapshots.AsNoTracking().Where(s => s.AppId == appId).MinAsync(s => (DateOnly?)s.Date, ct);
            var starts = new List<DateTimeOffset>();
            if (firstSample is { } sample) starts.Add(sample);
            if (firstDaily is { } daily) starts.Add(new DateTimeOffset(daily.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
            if (firstSnapshot is { } snap) starts.Add(new DateTimeOffset(snap.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

            return Results.Ok(new GameHistoryDto(starts.Count > 0 ? starts.Min() : null, playerCounts, playerCountsDaily, playtimeDaily));
        });
    }
}
