using System.Collections.Concurrent;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>
/// Keeps the card facts of games added by hand current: a game that has an IGDB id gets its ratings, hours to 100 %, genres, release date,
/// developer, publisher and summary refreshed when they are older than a week (the facts of a Steam game are refreshed from Steam every half hour,
/// see <c>SteamLiveTrackerService.RefreshTrophySnapshots</c>). Started by <c>GET /api/games</c> without waiting for it: the next list shows the result.
/// A game without an IGDB id (facts from the AI, or none) is never touched. A failed lookup is retried at the earliest after 15 minutes.
/// Singleton; every run uses its own scope (database context).
/// </summary>
public sealed class ManualFactsRefresh(IServiceScopeFactory scopes, IgdbService igdb, ILogger<ManualFactsRefresh> logger)
{
    /// <summary>Facts older than this (or never fetched) are refreshed.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _retryNotBefore = new();
    private readonly ConcurrentDictionary<Guid, byte> _running = new();

    /// <summary>Whether the facts of this game are due for a refresh (a manual game with an IGDB id whose facts are missing or a week old).</summary>
    public static bool IsDue(GameEntity game, DateTimeOffset now) =>
        GamePlatforms.IsManual(game) && game.IgdbId is > 0 && (game.FactsUpdatedAt is null || now - game.FactsUpdatedAt > MaxAge);

    /// <summary>Queues a refresh of these games in the background (language = the one of the request: it decides the readable release date). Never throws, never waits.</summary>
    public void Trigger(IEnumerable<Guid> gameIds, string language)
    {
        if (!igdb.IsConfigured)
        {
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var due = gameIds.Where(id => !_retryNotBefore.TryGetValue(id, out var notBefore) || notBefore <= now).Distinct().ToList();
        foreach (var id in due)
        {
            if (!_running.TryAdd(id, 0))
            {
                continue;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    using (Lang.Use(language))
                    {
                        await RefreshAsync(id, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Spielfakten von IGDB konnten für das Spiel {GameId} nicht aufgefrischt werden.", id);
                }
                finally
                {
                    _running.TryRemove(id, out _);
                }
            });
        }
    }

    /// <summary>
    /// Fetches IGDB's facts for one game and writes them (<see cref="ManualGameFacts.Apply"/>). Returns false when nothing was written (game
    /// gone, not a manual game with an IGDB id, no credentials, IGDB unreachable - then the facts keep their old date and are tried again later).
    /// </summary>
    public async Task<bool> RefreshAsync(Guid gameId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var game = await db.Games.FirstOrDefaultAsync(g => g.Id == gameId, ct);
        if (game is null || !GamePlatforms.IsManual(game) || game.IgdbId is not > 0 || !igdb.IsConfigured)
        {
            return false;
        }

        IgdbGameDto? facts;
        try
        {
            facts = await igdb.GetByIgdbIdAsync(game.IgdbId.Value, ct);
        }
        catch (TwitchApiException ex)
        {
            _retryNotBefore[gameId] = DateTimeOffset.UtcNow + RetryAfterFailure;
            logger.LogDebug("IGDB-Fakten für das Spiel {GameId} (IGDB {IgdbId}) nicht verfügbar: {Message}", gameId, game.IgdbId, ex.Message);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (facts is null)
        {
            // IGDB no longer knows the game: remember the attempt, so the list does not ask again on every request
            game.FactsUpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return false;
        }

        ManualGameFacts.Apply(game, facts, Lang.Current, now);
        await db.SaveChangesAsync(ct);
        _retryNotBefore.TryRemove(gameId, out _);
        return true;
    }
}
