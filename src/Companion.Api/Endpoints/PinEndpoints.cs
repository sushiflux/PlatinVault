using Companion.Api.Data;
using Companion.Api.Hubs;
using Companion.Api.Localization;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Endpoints;

/// <summary>
/// Pinned achievements ("Im Blick"): up to <see cref="SettingsStore.MaxPinnedAchievements"/> per game, in the order they were pinned.
/// Storage and concurrency rules: <see cref="PinnedAchievements"/>. Every answer is a <see cref="PinnedAchievementsDto"/>. The calls sit
/// in the <c>/api</c> group, so POST and DELETE need the X-Requested-With header like every state-changing call. After a real change the
/// hub event "PinsChanged" <c>{ appId, gameId }</c> tells every other open window to reload its list.
/// <para>
/// A game is named by its Steam AppID (<c>appId</c>) or - a game added by hand has no AppID - by its game id (<c>gameId</c>, then the answer has
/// <c>appId</c> 0 and the <c>gameId</c>); giving both, or neither, is a 400.
/// </para>
/// </summary>
public static class PinEndpoints
{
    // "Remove the unlocked ones" can never name more than the achievements of one game
    private const int MaxNamesPerRemove = 5000;

    public static void MapPins(this RouteGroupBuilder api)
    {
        api.MapGet("/live/pins", async (uint? appId, Guid? gameId, CompanionDbContext db, CancellationToken ct) =>
        {
            if (!TryScope(appId, gameId, out var scope))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Pins.InvalidDetail);
            }
            if (await CheckGameAsync(scope, db, ct) is { } missing)
            {
                return missing;
            }
            return Results.Ok(Answer(scope, await GetAsync(scope, db, ct)));
        });

        api.MapPost("/live/pins", async (PinAchievementRequest? req, CompanionDbContext db, IHubContext<AchievementHub> hub, ILogger<PinnedAchievementsLog> logger, CancellationToken ct) =>
        {
            if (!TryScope(req?.AppId, req?.GameId, out var scope) || !IsValidName(req?.ApiName))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Pins.InvalidDetail);
            }
            if (await CheckGameAsync(scope, db, ct) is { } missing)
            {
                return missing;
            }
            return await ApplyAsync(scope, hub, logger, ct, () => scope.GameId is { } id
                ? PinnedAchievements.AddForGameAsync(db, id, req!.ApiName!, ct)
                : PinnedAchievements.AddAsync(db, scope.AppId, req!.ApiName!, ct));
        });

        api.MapDelete("/live/pins", async (uint? appId, Guid? gameId, string? apiName, CompanionDbContext db, IHubContext<AchievementHub> hub, ILogger<PinnedAchievementsLog> logger, CancellationToken ct) =>
        {
            if (!TryScope(appId, gameId, out var scope) || !IsValidName(apiName))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Pins.InvalidDetail);
            }
            if (await CheckGameAsync(scope, db, ct) is { } missing)
            {
                return missing;
            }
            return await ApplyAsync(scope, hub, logger, ct, () => RemoveAsync(scope, db, [apiName!], ct));
        });

        api.MapPost("/live/pins/remove", async (UnpinAchievementsRequest? req, CompanionDbContext db, IHubContext<AchievementHub> hub, ILogger<PinnedAchievementsLog> logger, CancellationToken ct) =>
        {
            if (req is null || !TryScope(req.AppId, req.GameId, out var scope) || req.ApiNames is null || req.ApiNames.Any(n => !IsValidName(n)))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Pins.InvalidListDetail);
            }
            if (req.ApiNames.Count > MaxNamesPerRemove)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Pins.TooManyNames(MaxNamesPerRemove));
            }
            if (await CheckGameAsync(scope, db, ct) is { } missing)
            {
                return missing;
            }
            return await ApplyAsync(scope, hub, logger, ct, () => RemoveAsync(scope, db, req.ApiNames, ct));
        });
    }

    /// <summary>Sends "PinsChanged" <c>{ appId, gameId }</c> to every connected window (a Steam game: gameId null; a game added by hand: appId 0). A failed send never fails the caller.</summary>
    public static async Task BroadcastChangedAsync(IHubContext<AchievementHub> hub, uint appId, ILogger logger, CancellationToken ct, Guid? gameId = null)
    {
        try
        {
            await hub.Clients.All.SendAsync("PinsChanged", new { appId, gameId }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Änderung der angehefteten Erfolge (App {AppId}, Spiel {GameId}) konnte nicht an die Fenster gesendet werden.", appId, gameId);
        }
    }

    /// <summary>Which game a pin call is about: a Steam AppID, or the id of a game added by hand.</summary>
    private readonly record struct PinScope(uint AppId, Guid? GameId);

    // Exactly one of appId / gameId: both at once or none is ambiguous
    private static bool TryScope(uint? appId, Guid? gameId, out PinScope scope)
    {
        var hasGame = gameId is { } id && id != Guid.Empty;
        var hasApp = appId is > 0;
        scope = new PinScope(hasApp ? appId!.Value : 0, hasGame ? gameId : null);
        return hasApp ^ hasGame;
    }

    private static Task<IResult?> CheckGameAsync(PinScope scope, CompanionDbContext db, CancellationToken ct) =>
        scope.GameId is { } id ? CheckManualGameAsync(id, db, ct) : Task.FromResult<IResult?>(null);

    /// <summary>
    /// A game id must name a game added by hand that exists: 404 for a game that is gone, 400 for a game of the Steam library (its pins and notes are
    /// addressed by the Steam AppID, so a second key under the game id would never be shown). Null = fine. Shared with the notes endpoint.
    /// </summary>
    internal static async Task<IResult?> CheckManualGameAsync(Guid id, CompanionDbContext db, CancellationToken ct)
    {
        var game = await db.Games.AsNoTracking().Where(g => g.Id == id).Select(g => new { g.Platform }).FirstOrDefaultAsync(ct);
        if (game is null)
        {
            return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.ManualGames.ScopeGameNotFoundTitle, ServerTexts.ManualGames.ScopeGameNotFound(id));
        }
        return GamePlatforms.IsSteam(game.Platform)
            ? Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.ManualGames.ScopeNotManualTitle, ServerTexts.ManualGames.ScopeNotManual(id))
            : null;
    }

    private static Task<IReadOnlyList<string>> GetAsync(PinScope scope, CompanionDbContext db, CancellationToken ct) =>
        scope.GameId is { } id ? PinnedAchievements.GetForGameAsync(db, id, ct) : PinnedAchievements.GetAsync(db, scope.AppId, ct);

    private static Task<PinChange> RemoveAsync(PinScope scope, CompanionDbContext db, IEnumerable<string> names, CancellationToken ct) =>
        scope.GameId is { } id ? PinnedAchievements.RemoveForGameAsync(db, id, names, ct) : PinnedAchievements.RemoveAsync(db, scope.AppId, names, ct);

    private static async Task<IResult> ApplyAsync(
        PinScope scope,
        IHubContext<AchievementHub> hub,
        ILogger logger,
        CancellationToken ct,
        Func<Task<PinChange>> change)
    {
        PinChange result;
        try
        {
            result = await change();
        }
        catch (PinConflictException)
        {
            return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Pins.ConflictTitle, ServerTexts.Pins.ConflictDetail);
        }

        if (result.LimitReached)
        {
            return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Pins.LimitTitle, ServerTexts.Pins.LimitReached(SettingsStore.MaxPinnedAchievements));
        }
        if (result.Changed)
        {
            await BroadcastChangedAsync(hub, scope.AppId, logger, ct, scope.GameId);
        }
        return Results.Ok(Answer(scope, result.ApiNames));
    }

    private static PinnedAchievementsDto Answer(PinScope scope, IReadOnlyList<string> names) =>
        new(scope.AppId, names, SettingsStore.MaxPinnedAchievements, scope.GameId);

    private static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= PinnedAchievements.MaxNameLength;
}

/// <summary>Category for the log lines of the pinned-achievement endpoints.</summary>
public sealed class PinnedAchievementsLog;
