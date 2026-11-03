namespace PlatinVault.Bridge;

/// <summary>Why the loop ended.</summary>
internal enum BridgeRunResult
{
    /// <summary>Tray menu, quit request, Ctrl+C, logoff - plain shutdown.</summary>
    Stopped,

    /// <summary>A newer exe was installed; the caller starts it after releasing the single-instance mutex.</summary>
    RestartAfterUpdate,
}

/// <summary>
/// The polling loop: watches the running game and its local stats file and pushes the state to the server on a game
/// change, on a stats change and as a 60 s heartbeat. Network trouble never stops it; it logs only state changes
/// (online/offline/token invalid), not every failed attempt, and mirrors them into <see cref="BridgeStatus"/> for the
/// tray icon.
/// </summary>
internal sealed class BridgeRunner(
    BridgeSettings settings,
    BridgeClient client,
    StateSampler sampler,
    int intervalMs,
    ISelfUpdater updater,
    BridgeStatus status,
    LanguageFollower language)
{
    private const long HeartbeatMs = 60_000;
    private const long SlowRetryMs = 30_000;
    private static readonly long[] TransientBackoffMs = [2_000, 4_000, 8_000, 15_000];

    private enum ConnectionState { Unknown, Online, Offline, Unauthorized, Rejected }

    // Delivery state.
    private Observation? _lastSent;
    private long _lastSuccessTick;
    private long? _retryAtTick;
    private int _failureCount;
    private ConnectionState _state = ConnectionState.Unknown;
    private int _lastRejectedStatus;
    private string? _lastUnexpectedError;

    // Content of the last successfully delivered state (for log lines and the regression guard).
    private uint _sentAppId;
    private HashSet<string> _sentAchieved = [];

    private bool _restartAfterUpdate;
    private bool _leftoversClean;

    public async Task<BridgeRunResult> RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var observation = sampler.Observe();
                if (IsSendDue(observation, Environment.TickCount64))
                    await DeliverAsync(observation, ct).ConfigureAwait(false);

                if (_restartAfterUpdate) return BridgeRunResult.RestartAfterUpdate;

                // Best effort and rare: never delays the achievement state above (it only runs between two polls)
                await PushLibraryIfDueAsync(Environment.TickCount64, ct).ConfigureAwait(false);

                await Task.Delay(intervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Anything unexpected (file hiccup, ...) must not end the bridge. Log each distinct problem once.
                var text = $"{ex.GetType().Name}: {ex.Message}";
                if (text != _lastUnexpectedError)
                {
                    _lastUnexpectedError = text;
                    BridgeLog.Warn(CoreTexts.Loop.UnexpectedError(text));
                    BridgeLog.Debug(ex.ToString());
                }

                try { await Task.Delay(5_000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        return BridgeRunResult.Stopped;
    }

    // Library facts for the dashboard sort orders (release date, ratings, size on disk): looked at every 5 minutes, sent when the
    // content changed (a game was installed, Steam refreshed its app data), a refused send is retried after 30 minutes.
    private const long LibraryCheckMs = 300_000;
    private const long LibraryRetryMs = 1_800_000;
    private long _libraryDueTick;
    private string? _librarySentHash;

    private async Task PushLibraryIfDueAsync(long now, CancellationToken ct)
    {
        if (_state != ConnectionState.Online || now < _libraryDueTick) return;
        _libraryDueTick = now + LibraryCheckMs;

        var library = sampler.BuildLibrary();
        if (library is null) return;

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(library, BridgeJsonContext.Default.BridgeLibraryDto)));
        if (hash == _librarySentHash) return;

        var result = await client.SendLibraryAsync(library, ct).ConfigureAwait(false);
        if (result.Outcome == SendOutcome.Ok)
        {
            _librarySentHash = hash;
            BridgeLog.Info(CoreTexts.Loop.LibrarySent(library.Games.Count));
        }
        else
        {
            _libraryDueTick = now + LibraryRetryMs;
            BridgeLog.Debug(CoreTexts.Loop.LibraryNotAccepted(result.Reason).ToString());
        }
    }

    private bool IsSendDue(Observation observation, long now)
    {
        // After a failed attempt wait for the back-off, whatever changed meanwhile: the retry reads fresh data anyway.
        if (_retryAtTick is { } retryAt) return now >= retryAt;

        return _lastSent is null || observation != _lastSent.Value || now - _lastSuccessTick >= HeartbeatMs;
    }

    private async Task DeliverAsync(Observation observation, CancellationToken ct)
    {
        var (payload, data) = await sampler.BuildAsync(observation, _sentAppId, _sentAchieved, ct).ConfigureAwait(false);
        var result = await client.SendAsync(payload, ct).ConfigureAwait(false);
        var now = Environment.TickCount64;

        switch (result.Outcome)
        {
            case SendOutcome.Ok:
                OnDelivered(observation, payload, data, now);

                if (result.Reply is { } reply)
                {
                    // The server's language comes first: a freshly switched language applies to everything that follows.
                    language.Observe(reply.UiLanguage);

                    // The answer says which bridge version the server offers. Only checked after the state was acknowledged,
                    // so an update never costs an unreported unlock.
                    _restartAfterUpdate = await updater.TryUpdateAsync(reply.LatestBridgeVersion, reply.BridgeSha256, ct)
                        .ConfigureAwait(false);
                }
                break;

            case SendOutcome.Unauthorized:
                if (_state != ConnectionState.Unauthorized)
                    BridgeLog.Error(CoreTexts.Loop.TokenInvalidLog);
                _state = ConnectionState.Unauthorized;
                _retryAtTick = now + SlowRetryMs;
                status.Update(s => s with { Connection = ConnectionKind.Unauthorized, Detail = CoreTexts.Loop.TokenInvalidShort });
                break;

            case SendOutcome.Rejected:
                if (_state != ConnectionState.Rejected || _lastRejectedStatus != result.StatusCode)
                    BridgeLog.Error(CoreTexts.Loop.ServerRejects(result.Reason));
                _state = ConnectionState.Rejected;
                _lastRejectedStatus = result.StatusCode;
                _retryAtTick = now + SlowRetryMs;
                status.Update(s => s with { Connection = ConnectionKind.Rejected, Detail = result.Reason });
                break;

            default: // Transient
                if (_state != ConnectionState.Offline)
                    BridgeLog.Warn(CoreTexts.Loop.ServerUnreachable(settings.ServerText, result.Reason));
                _state = ConnectionState.Offline;
                _retryAtTick = now + TransientBackoffMs[Math.Min(_failureCount, TransientBackoffMs.Length - 1)];
                _failureCount++;
                status.Update(s => s with { Connection = ConnectionKind.Offline, Detail = result.Reason });
                break;
        }
    }

    private void OnDelivered(Observation observation, BridgeStateDto payload, LocalAppData? data, long now)
    {
        // Until the first delivery it is simply "connected", even if earlier attempts failed.
        switch (_lastSent is null ? ConnectionState.Unknown : _state)
        {
            case ConnectionState.Unknown:
                BridgeLog.Success(CoreTexts.Loop.Connected(settings.ServerText, settings.TokenHint));
                break;
            case ConnectionState.Offline:
                BridgeLog.Success(CoreTexts.Loop.ConnectionRestored);
                break;
            case ConnectionState.Unauthorized:
                BridgeLog.Success(CoreTexts.Loop.TokenAccepted);
                break;
            case ConnectionState.Rejected:
                BridgeLog.Success(CoreTexts.Loop.ServerAcceptsAgain);
                break;
        }

        _state = ConnectionState.Online;
        _failureCount = 0;
        _retryAtTick = null;
        _lastSuccessTick = now;

        // What an update left behind (on Windows the previous exe, still locked right after the update) is removed as soon as it can be.
        if (!_leftoversClean) _leftoversClean = updater.CleanupLeftovers();

        var unlocked = LogChanges(observation, payload, data);

        var achievedCount = data?.Achievements.Count(a => a.Achieved) ?? 0;
        var gameName = observation.AppId == 0
            ? null
            : string.IsNullOrWhiteSpace(data?.GameName) ? $"AppID {observation.AppId}" : data!.GameName;
        var lastUnlock = unlocked.Count > 0 ? unlocked[^1] : null;

        status.Update(s => s with
        {
            Connection = ConnectionKind.Online,
            Detail = null,
            SteamId64 = payload.SteamId64.Length > 0 ? payload.SteamId64 : null,
            AppId = observation.AppId,
            GameName = gameName,
            Achieved = achievedCount,
            Total = data?.Achievements.Count ?? 0,
            LastDeliveredAt = DateTime.Now,
            LastUnlockName = lastUnlock is null ? s.LastUnlockName
                : string.IsNullOrWhiteSpace(lastUnlock.DisplayName) ? lastUnlock.ApiName : lastUnlock.DisplayName,
            LastUnlockAt = lastUnlock is null ? s.LastUnlockAt : DateTime.Now,
        });

        _lastSent = observation;
        _sentAppId = observation.AppId;
        _sentAchieved = data?.Achievements.Where(a => a.Achieved).Select(a => a.ApiName).ToHashSet(StringComparer.Ordinal) ?? [];
    }

    /// <summary>Logs what changed since the last delivery and returns the achievements that are new.</summary>
    private List<LocalAchievement> LogChanges(
        Observation observation, BridgeStateDto payload, LocalAppData? data)
    {
        var first = _lastSent is null;
        List<LocalAchievement> none = [];

        if (first || observation.AccountId != _lastSent!.Value.AccountId)
        {
            if (observation.AccountId == 0)
                BridgeLog.Warn(CoreTexts.Loop.NoSteamAccount);
            else
                BridgeLog.Info(CoreTexts.Loop.ActiveSteamAccount(payload.SteamId64));
        }

        if (first || observation.AppId != _sentAppId)
        {
            if (observation.AppId == 0)
            {
                BridgeLog.Info(first ? CoreTexts.Loop.WaitingForGame : CoreTexts.Loop.GameEnded);
            }
            else if (data is null)
            {
                BridgeLog.Info(CoreTexts.Loop.GameDetectedNoData(observation.AppId));
            }
            else
            {
                var name = string.IsNullOrWhiteSpace(data.GameName) ? $"AppID {observation.AppId}" : data.GameName;
                var done = data.Achievements.Count(a => a.Achieved);
                BridgeLog.Info(CoreTexts.Loop.GameDetected(name, observation.AppId, done, data.Achievements.Count));
            }
            return none;
        }

        if (data is null)
        {
            BridgeLog.Debug(CoreTexts.Loop.HeartbeatSent);
            return none;
        }

        var unlocked = data.Achievements.Where(a => a.Achieved && !_sentAchieved.Contains(a.ApiName)).ToList();
        if (unlocked.Count == 0)
        {
            BridgeLog.Debug(observation == _lastSent!.Value ? CoreTexts.Loop.HeartbeatSent : CoreTexts.Loop.StatsChangedNoNew);
            return none;
        }

        var total = data.Achievements.Count;
        var count = data.Achievements.Count(a => a.Achieved) - unlocked.Count;
        foreach (var achievement in unlocked.Take(5))
        {
            count++;
            var title = string.IsNullOrWhiteSpace(achievement.DisplayName) ? achievement.ApiName : achievement.DisplayName;
            BridgeLog.Success(CoreTexts.Loop.AchievementUnlocked(title, count, total));
        }
        if (unlocked.Count > 5)
            BridgeLog.Info(CoreTexts.Loop.MoreAchievements(unlocked.Count - 5));

        return unlocked;
    }
}
