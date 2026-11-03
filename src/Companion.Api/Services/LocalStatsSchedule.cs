namespace Companion.Api.Services;

/// <summary>Why the worker rebuilds the achievement list in this cycle (None = not due).</summary>
public enum LocalCheckReason
{
    None,
    /// <summary>First check for the running game: takes the baseline.</summary>
    Initial,
    /// <summary>Steam rewrote the game's local stats file - an unlock is usually what changed.</summary>
    FileChanged,
    /// <summary>A second look shortly after a file change that showed nothing new (Steam may write the file in several steps).</summary>
    Settle,
    /// <summary>Safety net every <see cref="LocalStatsSchedule.PeriodicRefresh"/> (remote list, a missed change marker).</summary>
    Periodic,
    /// <summary>The previous attempt failed; tried again after <see cref="LocalStatsSchedule.RetryDelay"/>.</summary>
    Retry
}

/// <summary>
/// Decides in which 350 ms cycle the worker rebuilds the achievement list of the running game, so the cheap loop (registry value +
/// file timestamp) detects an unlock within a cycle while Steam's servers are asked at most once a minute. A change of the stats
/// file is only marked as handled after a successful check (<see cref="Completed"/>); a failed check keeps it pending and is retried
/// after <see cref="RetryDelay"/> instead of waiting for the 60 s safety net.
/// </summary>
public sealed class LocalStatsSchedule(TimeProvider? clock = null)
{
    public static readonly TimeSpan PeriodicRefresh = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private bool _loaded;
    private long _handledStamp;
    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;
    private DateTimeOffset? _settleAt;
    private DateTimeOffset? _retryAt;

    /// <summary>A new game (or the bridge took over): the next check takes a new baseline.</summary>
    public void Reset()
    {
        _loaded = false;
        _handledStamp = 0;
        _lastCheck = DateTimeOffset.MinValue;
        _settleAt = null;
        _retryAt = null;
    }

    public LocalCheckReason Due(long stamp)
    {
        var now = _clock.GetUtcNow();
        if (_retryAt is { } retryAt)
        {
            return now >= retryAt ? LocalCheckReason.Retry : LocalCheckReason.None;
        }
        if (!_loaded) return LocalCheckReason.Initial;
        if (stamp != _handledStamp) return LocalCheckReason.FileChanged;
        if (_settleAt is { } settleAt && now >= settleAt) return LocalCheckReason.Settle;
        if (now - _lastCheck >= PeriodicRefresh) return LocalCheckReason.Periodic;
        return LocalCheckReason.None;
    }

    /// <summary>
    /// The check for <paramref name="stamp"/> (the marker read BEFORE the list was built) succeeded. A file change that showed
    /// nothing new gets one more look after <see cref="SettleDelay"/>.
    /// </summary>
    public void Completed(long stamp, LocalCheckReason reason, bool foundNew)
    {
        var now = _clock.GetUtcNow();
        _loaded = true;
        _handledStamp = stamp;
        _lastCheck = now;
        _retryAt = null;
        _settleAt = reason == LocalCheckReason.FileChanged && !foundNew ? now + SettleDelay : null;
    }

    /// <summary>The check threw: whatever was pending stays pending and is tried again after <see cref="RetryDelay"/>.</summary>
    public void Failed()
    {
        var now = _clock.GetUtcNow();
        _lastCheck = now;
        _retryAt = now + RetryDelay;
    }
}
