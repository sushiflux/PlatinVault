namespace Companion.Api.Services;

/// <summary>An unlocked achievement as a source sees it: Steam API name and, when known, the unlock time.</summary>
public sealed record UnlockState(string Name, DateTime? UnlockTime);

/// <summary>Result of one comparison: the achievements to announce and whether this call only took the baseline.</summary>
public sealed record UnlockDiff(IReadOnlyList<string> Fresh, bool IsBaseline, int CaughtUp)
{
    public static readonly UnlockDiff NoData = new([], false, 0);
}

/// <summary>
/// What each source (local stats file, bridge) has already seen per game, so every unlock is announced exactly once.
/// The first comparison for a game only takes the baseline - opening a game never replays old unlocks. Exception: with a
/// catch-up time (the moment up to which this server had already taken every unlock into account, persisted across restarts by
/// <see cref="UnlockWatermarkStore"/>), achievements unlocked after it are announced with the baseline - they happened while the
/// server was restarting and nobody announced them. Known names only ever grow until <see cref="Reset"/>, so a list that briefly
/// lacks an unlock (stale remote cache, half-written file) can never make it count as new a second time.
/// </summary>
public sealed class UnlockLedgers
{
    /// <summary>Unlocks older than this are never replayed with a baseline, even when the server was off for longer.</summary>
    public static readonly TimeSpan MaxCatchUp = TimeSpan.FromMinutes(10);

    private sealed class Ledger
    {
        public uint AppId;
        public HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly Lock _lock = new();
    private readonly Dictionary<UnlockSource, Ledger> _ledgers = new()
    {
        [UnlockSource.Local] = new(),
        [UnlockSource.Bridge] = new()
    };

    /// <summary>True when the next <see cref="Diff"/> of this source for the game only takes the baseline.</summary>
    public bool NeedsBaseline(UnlockSource source, uint appId)
    {
        lock (_lock)
        {
            return _ledgers[source].AppId != appId;
        }
    }

    /// <summary>
    /// Compares the achievements unlocked now with what this source saw before. <paramref name="catchUpAfter"/> only matters for a
    /// baseline: unlocks with a time after it are announced (see the class comment); null = announce nothing with the baseline.
    /// </summary>
    public UnlockDiff Diff(UnlockSource source, uint appId, IReadOnlyCollection<UnlockState> achieved, int totalCount, DateTimeOffset? catchUpAfter = null)
    {
        // An empty list means "no data yet", not "nothing unlocked" - taking it as baseline would replay everything later
        if (totalCount == 0)
        {
            return UnlockDiff.NoData;
        }

        lock (_lock)
        {
            var ledger = _ledgers[source];
            if (ledger.AppId != appId)
            {
                ledger.AppId = appId;
                ledger.Known = new HashSet<string>(achieved.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
                var caughtUp = catchUpAfter is { } after
                    ? achieved.Where(a => a.UnlockTime is { } at && ToUtc(at) > after).Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    : [];
                return new UnlockDiff(caughtUp, true, caughtUp.Count);
            }

            var fresh = new List<string>();
            foreach (var item in achieved)
            {
                if (ledger.Known.Add(item.Name))
                {
                    fresh.Add(item.Name);
                }
            }
            return new UnlockDiff(fresh, false, 0);
        }
    }

    public void Reset(UnlockSource source)
    {
        lock (_lock)
        {
            var ledger = _ledgers[source];
            ledger.AppId = 0;
            ledger.Known.Clear();
        }
    }

    /// <summary>
    /// The catch-up time for a baseline: the stored watermark, but never further back than <see cref="MaxCatchUp"/>. Null when no
    /// watermark was ever stored for the game (first time it is watched: nothing to catch up).
    /// </summary>
    public static DateTimeOffset? CatchUpAfter(DateTimeOffset? storedWatermark, DateTimeOffset now) =>
        storedWatermark is { } stored ? (stored > now - MaxCatchUp ? stored : now - MaxCatchUp) : null;

    /// <summary>
    /// The new watermark after a baseline or an announcement: now, or the latest unlock time if a clock ahead of ours (the bridge PC)
    /// reported a later one - otherwise that unlock would count as "after the watermark" again after the next restart.
    /// </summary>
    public static DateTimeOffset NextWatermark(IEnumerable<UnlockState> achieved, DateTimeOffset now)
    {
        var latest = now;
        foreach (var item in achieved)
        {
            if (item.UnlockTime is { } at && ToUtc(at) > latest)
            {
                latest = ToUtc(at);
            }
        }
        return latest;
    }

    // Unlock times are local DateTimes (Steam unix seconds -> LocalDateTime); a UTC one is taken as it is
    internal static DateTimeOffset ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? new DateTimeOffset(value) : new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero);
}
