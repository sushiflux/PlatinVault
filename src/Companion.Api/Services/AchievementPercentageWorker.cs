using Companion.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>
/// Keeps the worldwide unlock shares of every game in the vault (games.SteamAppId set) fresh in the background: a few seconds after the start
/// and then every hour, so the first request for a game already finds current values and Gold/Silver/Bronze never rests on a guess.
/// Sequential with a short pause between Steam requests; what is not due yet is skipped (<see cref="AchievementPercentageService"/> owns the
/// refresh and back-off rules). When Steam cannot be reached the run gives up after a few failures in a row instead of logging one
/// warning per game. Never throws.
/// </summary>
public sealed class AchievementPercentageWorker(
    AchievementPercentageService percentages,
    IServiceScopeFactory scopeFactory,
    ILogger<AchievementPercentageWorker> logger) : BackgroundService
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    public static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(400);
    public const int MaxFailuresInARow = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                // Counted from the end of the run, so every stored value is older than the refresh age again at the next run
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
    }

    /// <summary>One pass over the collection; returns how many games were asked at Steam. Never throws.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct, TimeSpan? spacing = null)
    {
        var asked = 0;
        try
        {
            List<int> appIds;
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
                appIds = await db.Games.AsNoTracking().Where(g => g.SteamAppId != null).Select(g => g.SteamAppId!.Value).Distinct().ToListAsync(ct);
            }

            // Games that left the collection are no longer written to the cache file
            await percentages.RetainCollectionAsync(appIds.Where(id => id > 0).Select(id => (uint)id).ToHashSet());

            var failuresInARow = 0;
            foreach (var appId in appIds.Where(id => id > 0))
            {
                ct.ThrowIfCancellationRequested();
                var snapshot = await percentages.GetAsync((uint)appId, inCollection: true, ct);
                if (!snapshot.Attempted) continue;

                asked++;
                failuresInARow = snapshot.State == AchievementPercentageService.StateOk ? 0 : failuresInARow + 1;
                if (failuresInARow >= MaxFailuresInARow)
                {
                    logger.LogWarning("Seltenheitswerte: Steam antwortet nicht, der Lauf wird abgebrochen; die übrigen Spiele folgen im nächsten Lauf.");
                    break;
                }
                await Task.Delay(spacing ?? RequestSpacing, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Die Auffrischung der Seltenheitswerte ist fehlgeschlagen.");
        }
        return asked;
    }
}
