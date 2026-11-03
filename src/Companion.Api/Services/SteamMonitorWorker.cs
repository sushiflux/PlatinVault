using Companion.Api.Data;
using Companion.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>
/// Background worker that watches the running Steam game every 350 ms and announces trophy &amp; platinum events (through
/// <see cref="LiveAnnouncer"/>: SignalR, the in-process feed and the catch-up log). Only games in the vault are tracked.
/// The 350 ms loop itself is cheap (registry value + file timestamp); the achievement list is only rebuilt when the
/// local Steam stats file changes, the game changes, or every 60 s as a safety net (<see cref="LocalStatsSchedule"/>) - so
/// Steam's servers are not hit several times per second. Unlocks reported by the realtime bridge are announced right away by
/// <see cref="BridgeIngestService"/>; this worker covers the local stats of the PC it runs on and stays silent for a game
/// while the bridge reports it.
/// </summary>
public sealed class SteamMonitorWorker(
    SteamLiveTrackerService liveTracker,
    IServiceScopeFactory scopeFactory,
    IHubContext<AchievementHub> hubContext,
    LiveAnnouncer announcer,
    UnlockWatermarkStore watermarks,
    ILogger<SteamMonitorWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan TrackedGamesRefresh = TimeSpan.FromSeconds(5);
    // The same failure is logged once and then at most every minute, not every 350 ms
    private static readonly TimeSpan ErrorLogInterval = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly LocalStatsSchedule _schedule = new();
    private uint _lastAppId;
    private bool _bridgeActive;
    private bool _missingStatsFileLogged;
    private HashSet<int> _trackedAppIds = [];
    private DateTime _trackedAppIdsLoadedAt = DateTime.MinValue;
    private string? _lastErrorText;
    private DateTime _lastErrorLoggedAt = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PlatinVault SteamMonitorWorker gestartet (< 400ms Echtzeit-Erkennung).");

        // Give the database migration on startup a moment before the first lookup
        await Task.Delay(1500, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndBroadcastAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogCycleError(ex);
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    /// <summary>Runs one detection cycle.</summary>
    public async Task CheckAndBroadcastAsync(CancellationToken ct = default)
    {
        await _checkLock.WaitAsync(ct);
        try
        {
            await CheckCoreAsync(ct);
        }
        finally
        {
            _checkLock.Release();
        }
    }

    private async Task CheckCoreAsync(CancellationToken ct)
    {
        var runningAppId = liveTracker.GetRunningAppId();
        var tracked = runningAppId > 0 && (await GetTrackedAppIdsAsync(ct)).Contains((int)runningAppId);
        var currentAppId = tracked ? runningAppId : 0;

        if (currentAppId != _lastAppId)
        {
            logger.LogInformation("Steam-Spielwechsel erkannt: AppID {OldAppId} -> {NewAppId}", _lastAppId, currentAppId);
            _lastAppId = currentAppId;
            _schedule.Reset();
            _missingStatsFileLogged = false;

            if (currentAppId == 0)
            {
                // Game closed: the next start of the same game must take a fresh baseline
                liveTracker.ResetUnlockLedger(UnlockSource.Local);
                liveTracker.ClearAnnouncedGame();
            }
            else if (liveTracker.TryAnnounceGame(currentAppId))
            {
                // The bridge announces game starts too; only the first of the two sends the event
                var status = await liveTracker.GetStatusAsync(ct);
                var gameInfo = await liveTracker.GetGameInfoAsync(currentAppId, status.SteamId64, status.RunningGameSlug, ct);
                await hubContext.Clients.All.SendAsync("GameChanged", gameInfo, ct);
            }
        }

        if (currentAppId == 0)
        {
            return;
        }

        // While the bridge reports this game it announces the unlocks itself (see BridgeIngestService). Staying silent here
        // avoids double popups, and the next local check starts from a fresh baseline (plus the catch-up of the watermark, which
        // the bridge keeps current) instead of replaying what the bridge announced.
        if (liveTracker.IsBridgeFreshFor(currentAppId))
        {
            if (!_bridgeActive)
            {
                logger.LogInformation("App {AppId}: Die Bridge meldet dieses Spiel - lokale Erkennung pausiert, damit nichts doppelt erscheint.", currentAppId);
                _bridgeActive = true;
            }
            liveTracker.ResetUnlockLedger(UnlockSource.Local);
            _schedule.Reset();
            return;
        }
        if (_bridgeActive)
        {
            logger.LogInformation("App {AppId}: Die Bridge meldet sich nicht mehr - lokale Erkennung übernimmt wieder.", currentAppId);
            _bridgeActive = false;
        }

        // Read BEFORE the list is built: if Steam rewrites the file while we read it, the next cycle sees a new marker and looks again
        var statsStamp = liveTracker.GetLocalStatsStamp(currentAppId);
        var reason = _schedule.Due(statsStamp);
        if (reason == LocalCheckReason.None)
        {
            return;
        }
        if (statsStamp == 0 && !_missingStatsFileLogged)
        {
            _missingStatsFileLogged = true;
            logger.LogWarning(
                "App {AppId}: keine lokale Steam-Statistikdatei gefunden (appcache\\stats\\UserGameStats_<Konto>_{AppId}.bin) - Freischaltungen werden nur über Steams Server erkannt, also mit bis zu einer Minute Verzögerung.",
                currentAppId, currentAppId);
        }

        try
        {
            await CheckUnlocksAsync(currentAppId, statsStamp, reason, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Keep the file change pending; LocalStatsSchedule tries again shortly
            _schedule.Failed();
            throw;
        }
    }

    private async Task CheckUnlocksAsync(uint appId, long statsStamp, LocalCheckReason reason, CancellationToken ct)
    {
        var needsBaseline = liveTracker.NeedsUnlockBaseline(UnlockSource.Local, appId);
        DateTimeOffset? catchUpAfter = needsBaseline
            ? UnlockLedgers.CatchUpAfter(await watermarks.GetAsync(appId, ct), DateTimeOffset.UtcNow)
            : null;

        var data = await liveTracker.GetAchievementsAsync(appId, ct);
        var achieved = data.Achievements
            .Where(a => a.Achieved)
            .Select(a => new UnlockState(a.ApiName, a.UnlockTime))
            .ToList();
        var diff = liveTracker.DiffUnlocks(UnlockSource.Local, appId, achieved, data.TotalCount, catchUpAfter);
        _schedule.Completed(statsStamp, reason, diff.Fresh.Count > 0);

        var game = data.Game.Name;
        if (data.TotalCount == 0)
        {
            logger.LogInformation("{Game} (App {AppId}): noch keine Erfolgsliste verfügbar ({Reason}) - nichts verglichen.", game, appId, ReasonText(reason));
        }
        else if (diff.IsBaseline)
        {
            logger.LogInformation(
                "{Game} (App {AppId}): Ausgangsstand übernommen ({Reason}): {Achieved}/{Total} freigeschaltet - kein Popup für bereits Freigeschaltetes{CatchUp}.",
                game, appId, ReasonText(reason), achieved.Count, data.TotalCount,
                diff.CaughtUp > 0 ? $"; {diff.CaughtUp} während eines Neustarts freigeschaltete(s) wird nachgeholt" : string.Empty);
        }
        else if (diff.Fresh.Count > 0)
        {
            logger.LogInformation(
                "{Game} (App {AppId}): {Count} neue Freischaltung(en) erkannt ({Reason}): {Names}.",
                game, appId, diff.Fresh.Count, ReasonText(reason), string.Join(", ", diff.Fresh));
        }
        else if (reason is LocalCheckReason.FileChanged or LocalCheckReason.Settle)
        {
            // The file changed, but nothing is new: progress counters/stats, or Steam is not finished writing (one more look follows)
            logger.LogInformation(
                "{Game} (App {AppId}): Statistikdatei geändert ({Reason}), aber nichts neu freigeschaltet: {Achieved}/{Total} wie bisher{Next}.",
                game, appId, ReasonText(reason), achieved.Count, data.TotalCount,
                reason == LocalCheckReason.FileChanged ? $" - erneute Prüfung in {LocalStatsSchedule.SettleDelay.TotalSeconds:0} s" : string.Empty);
        }

        var announced = data.Achievements
            .Where(a => diff.Fresh.Contains(a.ApiName, StringComparer.OrdinalIgnoreCase))
            .ToList();
        foreach (var unlocked in announced)
        {
            await announcer.AnnounceUnlockAsync(unlocked, game, (int)appId, "lokal", ct);
        }

        if (announced.Count > 0 && data.Is100Percent)
        {
            await announcer.AnnouncePlatinumAsync(game, data.TotalCount, (int)appId, "lokal", ct);
        }

        // Everything up to now is taken into account - a restart only catches up on what happens after this
        if (diff.IsBaseline || announced.Count > 0)
        {
            await watermarks.SetAsync(appId, UnlockLedgers.NextWatermark(achieved, DateTimeOffset.UtcNow), ct);
        }
    }

    private static string ReasonText(LocalCheckReason reason) => reason switch
    {
        LocalCheckReason.Initial => "erste Prüfung",
        LocalCheckReason.FileChanged => "Statistikdatei geändert",
        LocalCheckReason.Settle => "zweiter Blick nach Dateiänderung",
        LocalCheckReason.Periodic => "minütliche Prüfung",
        LocalCheckReason.Retry => "neuer Versuch nach Fehler",
        _ => reason.ToString()
    };

    private void LogCycleError(Exception ex)
    {
        var text = $"{ex.GetType().Name}: {ex.Message}";
        var now = DateTime.UtcNow;
        if (text == _lastErrorText && now - _lastErrorLoggedAt < ErrorLogInterval)
        {
            return;
        }
        _lastErrorText = text;
        _lastErrorLoggedAt = now;
        logger.LogWarning(ex, "Fehler im SteamMonitorWorker-Zyklus - Freischaltungen werden in diesem Moment nicht erkannt, neuer Versuch in {Seconds} s.", LocalStatsSchedule.RetryDelay.TotalSeconds);
    }

    /// <summary>Steam AppIDs of vault games with achievements enabled, reloaded from the database every few seconds.</summary>
    private async Task<HashSet<int>> GetTrackedAppIdsAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _trackedAppIdsLoadedAt < TrackedGamesRefresh)
        {
            return _trackedAppIds;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var appIds = await db.Games
            .AsNoTracking()
            .Where(g => g.SteamAppId != null)
            .Select(g => g.SteamAppId!.Value)
            .ToListAsync(ct);

        _trackedAppIds = [.. appIds];
        _trackedAppIdsLoadedAt = DateTime.UtcNow;
        return _trackedAppIds;
    }
}
