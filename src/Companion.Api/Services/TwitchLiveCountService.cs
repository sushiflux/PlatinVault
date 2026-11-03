using System.Collections.Concurrent;

namespace Companion.Api.Services;

/// <summary>One game of a batch: the Steam AppID and the title (for the exact-name category lookup when IGDB does not know the game).</summary>
public sealed record LiveCountGame(int AppId, string? Name);

/// <summary>The answer for a batch: the counts that are known, and the games that are still being looked up (ask again shortly).</summary>
public sealed record LiveCountsResult(IReadOnlyList<LiveCountEntry> Counts, IReadOnlyList<int> Pending);

public sealed record LiveCountEntry(int AppId, int Count, bool AtLeast);

/// <summary>
/// The live-stream count for many games at once (the badge on the Twitch buttons of the dashboard cards). Each game costs one to
/// three Twitch calls once and nothing for five minutes afterwards (<see cref="TwitchStreamsService.CountLiveAsync"/>), so a batch
/// works with a small number of parallel calls and a time budget: whatever is done in time is returned, the rest is reported as
/// pending and finishes in the next request (the answers so far are cached). A game whose lookup fails is simply left out - it gets
/// no badge, never an error. A rate-limit answer from Twitch ends the batch (Twitch: every call costs points of the app's bucket and
/// an empty bucket answers 429, https://dev.twitch.tv/docs/api/guide/#twitch-rate-limits).
/// </summary>
public sealed class TwitchLiveCountService(TwitchStreamsService streams, ILogger<TwitchLiveCountService> logger)
{
    public const int MaxGames = 120;
    private const int Concurrency = 4;

    public async Task<LiveCountsResult> CountAsync(IReadOnlyList<LiveCountGame> games, string? language, TimeSpan budget, CancellationToken ct)
    {
        var distinct = games.Where(g => g.AppId > 0).DistinctBy(g => g.AppId).Take(MaxGames).ToList();
        var done = new ConcurrentDictionary<int, LiveCountEntry>();
        var settled = new ConcurrentDictionary<int, bool>();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(budget);
        var stop = false;

        try
        {
            await Parallel.ForEachAsync(distinct, new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = limit.Token }, async (game, token) =>
            {
                if (Volatile.Read(ref stop)) return;
                try
                {
                    if (await streams.CountLiveAsync(game.AppId, game.Name, language, token) is { } count)
                    {
                        done[game.AppId] = new LiveCountEntry(game.AppId, count.Count, count.AtLeast);
                    }
                    settled[game.AppId] = true;
                }
                catch (TwitchApiException ex)
                {
                    // No badge for this game; a rate limit ends the batch instead of hammering Twitch
                    logger.LogDebug("Live-Anzahl für App {AppId} nicht verfügbar: {Message}", game.AppId, ex.Message);
                    settled[game.AppId] = true;
                    if (ex.IsRateLimit)
                    {
                        Volatile.Write(ref stop, true);
                    }
                }
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The time budget is used up: what is done is returned, the rest is pending
        }

        var pending = Volatile.Read(ref stop) ? [] : distinct.Where(g => !settled.ContainsKey(g.AppId)).Select(g => g.AppId).ToList();
        return new LiveCountsResult(done.Values.OrderBy(e => e.AppId).ToList(), pending);
    }
}
