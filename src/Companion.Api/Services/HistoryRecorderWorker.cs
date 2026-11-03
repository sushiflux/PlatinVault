using Companion.Api.Data;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>
/// Records what Steam only reports as "right now", so the statistics tab can draw charts over time:
/// <list type="bullet">
/// <item>Player counts: every 30 minutes the number of players online (ISteamUserStats/GetNumberOfCurrentPlayers - no key needed,
/// see the Steamworks docs) of every game in the vault that has a Steam AppID. Raw samples are kept for 30 days, then condensed
/// into one row per day (min / average / max) that is kept for two years.</item>
/// <item>Playtime: the total playtime of every such game (Steam GetOwnedGames with the Web API key, else this PC's localconfig.vdf).
/// The value of a day is overwritten with every run, so the last reading of the day stays; the playtime per day is the difference
/// between consecutive days. Tiny, kept forever.</item>
/// </list>
/// A run happens at start-up (a restart shortly after a sample does not add another one) and then every 30 minutes. About
/// 48 requests per game and day against Steam's 100,000 calls per day; a failing call is logged and skipped, never fatal.
/// All times are UTC, also the "day" of a snapshot.
/// </summary>
public sealed class HistoryRecorderWorker(IServiceScopeFactory scopeFactory, ILogger<HistoryRecorderWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    public const int RawRetentionDays = 30;
    public const int DailyRetentionDays = 730;

    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    // Restarts (dotnet watch, Docker) must not pile up extra samples between two 30-minute readings
    private static readonly TimeSpan MinSampleGap = TimeSpan.FromMinutes(20);

    // Pause between two Steam requests of one run
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(120);

    private DateOnly _maintainedOn;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
    }

    /// <summary>One recording run: player counts, playtime, then (once per day) condensing and cleaning up old data. Never throws.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();

            // Two servers can share one database file (development: browser stack + Windows program). Only one records at a time, and
            // the "last sample is younger than 20 minutes" check below runs inside the lock - so a run of the other server
            // that just finished is seen and nothing is recorded twice.
            // The lease runs as long as the interval: a server that died while recording blocks the other one for at most one run
            await using var gate = await DatabaseLease.TryAcquireAsync(db, DatabaseLease.HistoryRecorder, Interval, ct);
            if (gate is null)
            {
                logger.LogDebug("Aufzeichnung: ein anderer Server derselben Datenbank zeichnet gerade auf, dieser Lauf entfällt.");
                return;
            }

            var stats = scope.ServiceProvider.GetRequiredService<SteamStatsService>();
            var settings = await scope.ServiceProvider.GetRequiredService<SettingsStore>().GetAsync(ct);

            var appIds = await db.Games.AsNoTracking()
                .Where(g => g.SteamAppId > 0)
                .Select(g => g.SteamAppId!.Value)
                .Distinct()
                .ToListAsync(ct);
            if (appIds.Count == 0)
            {
                return;
            }

            await SafeAsync("Spielerzahlen", () => SamplePlayerCountsAsync(db, stats, appIds, ct));
            await SafeAsync("Spielzeit", () => SnapshotPlaytimeAsync(db, stats, settings.SteamUserId ?? string.Empty, appIds, ct));
            await SafeAsync("Aufräumen", () => MaintainAsync(db, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Aufzeichnung von Spielerzahlen und Spielzeit fehlgeschlagen – nächster Versuch in {Minutes} Minuten.", Interval.TotalMinutes);
        }
    }

    private async Task SafeAsync(string what, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Aufzeichnung ({What}) fehlgeschlagen.", what);
        }
    }

    private async Task SamplePlayerCountsAsync(CompanionDbContext db, SteamStatsService stats, List<int> appIds, CancellationToken ct)
    {
        var last = await db.PlayerCountSamples.AsNoTracking().MaxAsync(s => (DateTimeOffset?)s.SampledAt, ct);
        if (last is { } lastAt && DateTimeOffset.UtcNow - lastAt < MinSampleGap)
        {
            logger.LogDebug("Spielerzahlen: letzte Messung vor {Minutes:0} Minuten, keine neue.", (DateTimeOffset.UtcNow - lastAt).TotalMinutes);
            return;
        }

        // All readings of one run share one time stamp (UTC; the database stores UTC only, see UtcDateTimeOffsetConverter)
        var now = DateTimeOffset.UtcNow;
        var saved = 0;
        foreach (var appId in appIds)
        {
            ct.ThrowIfCancellationRequested();
            var count = await stats.GetCurrentPlayersAsync(appId, ct);
            if (count is { } players)
            {
                db.PlayerCountSamples.Add(new PlayerCountSampleEntity { AppId = appId, SampledAt = now, Count = players });
                saved++;
            }
            await Task.Delay(RequestSpacing, ct);
        }

        if (saved > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        if (saved < appIds.Count)
        {
            logger.LogWarning("Spielerzahlen: für {Missing} von {Total} Spielen lieferte Steam keine Zahl.", appIds.Count - saved, appIds.Count);
        }
        else
        {
            logger.LogInformation("Spielerzahlen von {Count} Spielen aufgezeichnet.", saved);
        }
    }

    private async Task SnapshotPlaytimeAsync(CompanionDbContext db, SteamStatsService stats, string steamId64, List<int> appIds, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var existing = await db.PlaytimeSnapshots
            .Where(s => s.Date == today && appIds.Contains(s.AppId))
            .ToDictionaryAsync(s => s.AppId, ct);

        var changed = 0;
        foreach (var appId in appIds)
        {
            ct.ThrowIfCancellationRequested();
            var minutes = await stats.GetPlaytimeMinutesAsync(appId, steamId64, ct);
            if (minutes is not { } total)
            {
                continue;
            }

            if (existing.TryGetValue(appId, out var row))
            {
                if (row.PlaytimeMinutes != total)
                {
                    row.PlaytimeMinutes = total;
                    changed++;
                }
            }
            else
            {
                db.PlaytimeSnapshots.Add(new PlaytimeSnapshotEntity { AppId = appId, Date = today, PlaytimeMinutes = total });
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogDebug("Spielzeit-Stand vom {Date}: {Count} Spiele geschrieben.", today, changed);
        }
    }

    /// <summary>
    /// Once per day: raw samples older than 30 days become one row per game and day (min/avg/max), then they are deleted; daily rows
    /// older than two years are deleted. One transaction, so a crash never loses samples that were not condensed yet.
    /// </summary>
    /// <summary>
    /// Condenses raw samples before <paramref name="rawCutoff"/> into one row per game and UTC day (an existing day row keeps its lower
    /// minimum / higher maximum, the average is the one of the condensed samples), deletes those samples and the day rows before
    /// <paramref name="dailyCutoff"/> - in one transaction. The grouping by UTC day happens here, not in SQL: SQLite has no portable date
    /// type, and the rows of one day of samples are few (at most 48 per game).
    /// </summary>
    internal static async Task<(int Condensed, int Samples, int Daily)> CondenseAsync(CompanionDbContext db, DateTimeOffset rawCutoff, DateOnly dailyCutoff, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var old = await db.PlayerCountSamples.AsNoTracking().Where(s => s.SampledAt < rawCutoff).ToListAsync(ct);
        var days = old
            .GroupBy(s => (s.AppId, Day: DateOnly.FromDateTime(s.SampledAt.UtcDateTime)))
            .Select(g => (g.Key.AppId, g.Key.Day, Min: g.Min(s => s.Count), Avg: g.Average(s => (double)s.Count), Max: g.Max(s => s.Count)))
            .ToList();
        foreach (var day in days)
        {
            var row = await db.PlayerCountDaily.FindAsync([day.AppId, day.Day], ct);
            if (row is null)
            {
                db.PlayerCountDaily.Add(new PlayerCountDailyEntity { AppId = day.AppId, Date = day.Day, MinCount = day.Min, AvgCount = day.Avg, MaxCount = day.Max });
            }
            else
            {
                row.MinCount = Math.Min(row.MinCount, day.Min);
                row.MaxCount = Math.Max(row.MaxCount, day.Max);
                row.AvgCount = day.Avg;
            }
        }
        await db.SaveChangesAsync(ct);
        var removedSamples = await db.PlayerCountSamples.Where(s => s.SampledAt < rawCutoff).ExecuteDeleteAsync(ct);
        var removedDaily = await db.PlayerCountDaily.Where(d => d.Date < dailyCutoff).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return (days.Count, removedSamples, removedDaily);
    }

    private async Task MaintainAsync(CompanionDbContext db, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_maintainedOn == today)
        {
            return;
        }

        // Whole UTC days only: everything before this instant belongs to days that are complete
        var rawCutoff = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-RawRetentionDays), DateTimeKind.Utc));
        var dailyCutoff = today.AddDays(-DailyRetentionDays);

        var (condensed, removedSamples, removedDaily) = await CondenseAsync(db, rawCutoff, dailyCutoff, ct);

        _maintainedOn = today;
        if (removedSamples > 0 || removedDaily > 0)
        {
            logger.LogInformation("Verlauf aufgeräumt: {Condensed} Tageswerte verdichtet, {Samples} alte Messungen und {Daily} sehr alte Tageswerte entfernt.",
                condensed, removedSamples, removedDaily);
        }
    }
}
