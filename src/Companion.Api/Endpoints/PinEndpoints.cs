using Companion.Api.Data;
using Companion.Api.Hubs;
using Companion.Api.Localization;
using Companion.Api.Settings;
using Microsoft.AspNetCore.SignalR;

namespace Companion.Api.Endpoints;

/// <summary>
/// Pinned achievements ("Im Blick"): up to <see cref="SettingsStore.MaxPinnedAchievements"/> per game, in the order they were pinned.
/// Storage and concurrency rules: <see cref="PinnedAchievements"/>. Every answer is a <see cref="PinnedAchievementsDto"/>. The calls sit
/// in the <c>/api</c> group, so POST and DELETE need the X-Requested-With header like every state-changing call. After a real change the
/// hub event "PinsChanged" <c>{ appId }</c> tells every other open window to reload its list.
/// </summary>
public static class PinEndpoints
{
    // "Remove the unlocked ones" can never name more than the achievements of one game
    private const int MaxNamesPerRemove = 5000;

    public static void MapPins(this RouteGroupBuilder api)
    {
        api.MapGet("/live/pins", async (uint? appId, CompanionDbContext db, CancellationToken ct) =>
        {
            if (appId is null or 0)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Pins.InvalidDetail);
            }
            return Results.Ok(Answer(appId.Value, await PinnedAchievements.GetAsync(db, appId.Value, ct)));
        });

        api.MapPost("/live/pins", async (PinAchievementRequest? req, CompanionDbContext db, IHubContext<AchievementHub> hub, ILogger<PinnedAchievementsLog> logger, CancellationToken ct) =>
        {
            if (!IsValid(req?.AppId, req?.ApiName))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Pins.InvalidDetail);
            }
            return await ApplyAsync(req!.AppId, hub, logger, ct, () => PinnedAchievements.AddAsync(db, req.AppId, req.ApiName!, ct));
        });

        api.MapDelete("/live/pins", async (uint? appId, string? apiName, CompanionDbContext db, IHubContext<AchievementHub> hub, ILogger<PinnedAchievementsLog> logger, CancellationToken ct) =>
        {
            if (!IsValid(appId, apiName))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Pins.InvalidDetail);
            }
            return await ApplyAsync(appId!.Value, hub, logger, ct, () => PinnedAchievements.RemoveAsync(db, appId.Value, [apiName!], ct));
        });

        api.MapPost("/live/pins/remove", async (UnpinAchievementsRequest? req, CompanionDbContext db, IHubContext<AchievementHub> hub, ILogger<PinnedAchievementsLog> logger, CancellationToken ct) =>
        {
            if (req is null || req.AppId == 0 || req.ApiNames is null || req.ApiNames.Any(n => !IsValidName(n)))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Pins.InvalidListDetail);
            }
            if (req.ApiNames.Count > MaxNamesPerRemove)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Pins.TooManyNames(MaxNamesPerRemove));
            }
            return await ApplyAsync(req.AppId, hub, logger, ct, () => PinnedAchievements.RemoveAsync(db, req.AppId, req.ApiNames, ct));
        });
    }

    /// <summary>Sends "PinsChanged" <c>{ appId }</c> to every connected window. A failed send never fails the caller.</summary>
    public static async Task BroadcastChangedAsync(IHubContext<AchievementHub> hub, uint appId, ILogger logger, CancellationToken ct)
    {
        try
        {
            await hub.Clients.All.SendAsync("PinsChanged", new { appId }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Änderung der angehefteten Erfolge (App {AppId}) konnte nicht an die Fenster gesendet werden.", appId);
        }
    }

    private static async Task<IResult> ApplyAsync(
        uint appId,
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
            await BroadcastChangedAsync(hub, appId, logger, ct);
        }
        return Results.Ok(Answer(appId, result.ApiNames));
    }

    private static PinnedAchievementsDto Answer(uint appId, IReadOnlyList<string> names) => new(appId, names, SettingsStore.MaxPinnedAchievements);

    private static bool IsValid(uint? appId, string? apiName) => appId is > 0 && IsValidName(apiName);

    private static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= PinnedAchievements.MaxNameLength;
}

/// <summary>Category for the log lines of the pinned-achievement endpoints.</summary>
public sealed class PinnedAchievementsLog;
