using System.Globalization;
using System.Text.Json;
using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Companion.Api.Endpoints;

/// <summary>
/// Answer of <c>POST /api/games/{slug}/launch</c> and <c>POST /api/steam/library/{appId}/launch</c>.
/// <c>Via = "bridge"</c>: the command is queued for the PlatinVault Bridge on the gaming PC (follow it with
/// <c>GET /api/launch/{CommandId}</c>); <c>Via = "browser"</c>: no bridge is connected, the caller opens <c>Url</c> itself;
/// <c>Via = "local"</c>: the Windows program (<see cref="Companion.Api.Desktop.IDesktopHost"/>) already started the game on this PC.
/// <c>Url</c> is always the fixed <c>steam://run/&lt;AppId&gt;</c> address.
/// </summary>
public sealed record LaunchResponseDto(
    string Via,
    string Url,
    int AppId,
    string? CommandId,
    string? Status,
    int? ExpiresInSeconds,
    bool BridgeConnected);

/// <summary>How a queued command is doing (<c>GET /api/launch/{commandId}</c>). Status: pending, delivered, done, failed or expired.</summary>
public sealed record LaunchCommandStatusDto(
    string CommandId,
    int AppId,
    string GameName,
    string Status,
    bool Finished,
    string? Reason,
    string Message);

/// <summary>Whether a bridge that can run commands is connected right now (<c>GET /api/launch/status</c>).</summary>
public sealed record LaunchBridgeStatusDto(bool BridgeConnected, DateTimeOffset? BridgeLastSeenAt);

/// <summary>Body of <c>POST /api/bridge/commands</c>: how the commands ended that the bridge ran since its last request.</summary>
public sealed record BridgePollRequest(List<BridgeCommandResult>? Results);

/// <summary>Status is "done" or "failed"; Reason is a <see cref="LaunchFailureReason"/> code, Detail the system's own error text.</summary>
public sealed record BridgeCommandResult(string? Id, string? Status, string? Reason, string? Detail);

/// <summary>One command for the bridge. Type is always "launch"; the bridge builds steam://run/&lt;AppId&gt; itself.</summary>
public sealed record BridgeCommandDto(string Id, string Type, int AppId, string GameName);

/// <summary>Answer of <c>POST /api/bridge/commands</c>: every command that is still open (the bridge runs each id once).</summary>
public sealed record BridgePollReply(bool Ok, List<BridgeCommandDto> Commands);

/// <summary>
/// "Play" from the app: starts a game in Steam on the gaming PC. Two ways, decided by the server:
/// a connected bridge runs <c>steam://run/&lt;appid&gt;</c> on the gaming PC (the browser may be on a tablet), otherwise the answer
/// tells the browser to open that address itself (it is then on the PC that runs Steam). See
/// https://developer.valvesoftware.com/wiki/Steam_browser_protocol ("steam://run/&lt;id&gt;: Runs an application. It will be installed if necessary.").
/// </summary>
public static class LaunchEndpoints
{
    // A poll carries a handful of results at most (one per command the bridge ran since the last poll)
    private const long MaxPollBodyBytes = 16 * 1024;
    private const int MaxPollResults = 20;

    // The same cached owned-games list the library summary uses, so a launch does not cost an extra Steam request
    private const string LibraryCacheKey = "steam-library-summary-source";

    /// <summary>The only address the server ever hands out or queues: built from a validated integer, never from text.</summary>
    public static string SteamRunUrl(int appId) => string.Create(CultureInfo.InvariantCulture, $"steam://run/{appId}");

    /// <summary>
    /// The channel the bridge uses: token-authenticated like <c>/api/bridge/state</c>, therefore outside the CSRF-header group, and
    /// rate-limited by the same "bridge" policy (20 per second per IP; the bridge polls about every 2 seconds).
    /// </summary>
    public static void MapBridgeCommands(this WebApplication app)
    {
        app.MapPost("/api/bridge/commands", async (
            HttpContext http,
            BridgeTokenStore tokens,
            BridgeCommandQueue queue,
            CancellationToken ct) =>
        {
            if (!await tokens.AuthenticateAsync(http.Request.Headers.Authorization.ToString(), ct))
            {
                http.Response.Headers.WWWAuthenticate = "Bearer";
                return Problems.Create(StatusCodes.Status401Unauthorized, ServerTexts.Bridge.UnauthorizedTitle, ServerTexts.Bridge.UnauthorizedDetail);
            }

            // The caller is a genuine bridge that can run commands, whatever the body says
            queue.RecordBridgeContact();

            // Capped only after the token was accepted, so unauthenticated callers can never make the server buffer a body
            if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            {
                sizeFeature.MaxRequestBodySize = MaxPollBodyBytes;
            }

            BridgePollRequest? request = null;
            try
            {
                if (http.Request.ContentLength != 0)
                {
                    request = await http.Request.ReadFromJsonAsync<BridgePollRequest>(ApiEndpoints.JsonOptions, ct);
                }
            }
            catch (Exception ex) when (ex is JsonException or BadHttpRequestException or InvalidOperationException)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Launch.PollNotJson);
            }

            var results = request?.Results ?? [];
            if (results.Count > MaxPollResults)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.Launch.TooManyResults(MaxPollResults));
            }

            // Results first: a command that just ended is no longer "open" and drops out of the answer below
            foreach (var result in results)
            {
                if (result is null || !Guid.TryParse(result.Id, out var id))
                {
                    continue;
                }

                switch (result.Status?.Trim().ToLowerInvariant())
                {
                    case "done":
                        queue.ApplyResult(id, success: true, reason: null, detail: null);
                        break;
                    case "failed":
                        queue.ApplyResult(id, success: false, result.Reason, result.Detail);
                        break;
                }
            }

            var commands = queue.ClaimOpen()
                .Select(c => new BridgeCommandDto(c.Id.ToString(), "launch", c.AppId, c.GameName))
                .ToList();
            return Results.Ok(new BridgePollReply(true, commands));
        }).RequireRateLimiting("bridge");
    }

    /// <summary>Endpoints the app calls (inside the <c>/api</c> group: they need the X-Requested-With header like every state-changing call).</summary>
    public static void MapLaunch(this RouteGroupBuilder api)
    {
        // From the collection: the AppID comes from the stored game, never from the caller
        api.MapPost("/games/{slug}/launch", async (
            string slug,
            HttpContext http,
            CompanionDbContext db,
            BridgeCommandQueue queue,
            ILogger<BridgeCommandQueue> logger,
            CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking()
                .Where(g => g.Slug == slug)
                .Select(g => new { g.Title, g.SteamAppId })
                .FirstOrDefaultAsync(ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            if (game.SteamAppId is not > 0)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Launch.NoSteamAppTitle, ServerTexts.Launch.NoSteamAppDetail(game.Title));
            }

            return Launch(http.RequestServices, queue, logger, game.SteamAppId.Value, game.Title);
        });

        // From the whole Steam library ("All games" view, games that are not on the dashboard): accepted when the AppID belongs to
        // the collection or to the user's owned-games list - never an arbitrary number
        api.MapPost("/steam/library/{appId:int}/launch", async (
            int appId,
            HttpContext http,
            CompanionDbContext db,
            SteamClient steamClient,
            IMemoryCache cache,
            BridgeCommandQueue queue,
            ILogger<BridgeCommandQueue> logger,
            CancellationToken ct) =>
        {
            if (appId <= 0)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.InvalidAppIdTitle, ServerTexts.Games.InvalidSteamAppIdDetail);
            }

            var title = await db.Games.AsNoTracking()
                .Where(g => g.SteamAppId == appId)
                .Select(g => g.Title)
                .FirstOrDefaultAsync(ct);

            if (title is null)
            {
                SteamLibraryResponseDto? library;
                try
                {
                    library = await cache.GetOrCreateAsync(LibraryCacheKey, async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                        return await steamClient.FetchOwnedGamesAsync(null, [], ct);
                    });
                }
                catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
                {
                    return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Launch.LibraryUnknownTitle, ServerTexts.Launch.LibraryUnknownDetail(appId, ex.Message));
                }

                var owned = library?.Games.FirstOrDefault(g => g.AppId == appId);
                if (owned is null)
                {
                    return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.Launch.NotInLibraryTitle, ServerTexts.Launch.NotInLibraryDetail(appId));
                }
                title = owned.Name;
            }

            return Launch(http.RequestServices, queue, logger, appId, title);
        });

        api.MapGet("/launch/status", (BridgeCommandQueue queue) =>
            Results.Ok(new LaunchBridgeStatusDto(queue.IsBridgeConnected, queue.LastBridgeContactAt)));

        api.MapGet("/launch/{commandId:guid}", (Guid commandId, BridgeCommandQueue queue) =>
        {
            var command = queue.Find(commandId);
            if (command is null)
            {
                return Problems.NotFound(ServerTexts.Launch.CommandNotFoundTitle, ServerTexts.Launch.CommandNotFoundDetail);
            }

            return Results.Ok(DescribeCommand(command));
        });
    }

    private static IResult Launch(IServiceProvider services, BridgeCommandQueue queue, ILogger logger, int appId, string title)
    {
        var url = SteamRunUrl(appId);

        // The Windows program runs on the PC with Steam: it starts the game itself, the browser in its window never asks
        if (services.GetService<Companion.Api.Desktop.IDesktopHost>() is { } desktop)
        {
            var result = desktop.LaunchGame(appId);
            if (result.Started)
            {
                logger.LogInformation("Spielstart {Title} (AppID {AppId}) direkt auf diesem PC.", title, appId);
                return Results.Ok(new LaunchResponseDto("local", url, appId, null, "done", null, BridgeConnected: false));
            }

            logger.LogWarning("Spielstart {Title} (AppID {AppId}) fehlgeschlagen: {Detail}", title, appId, result.Detail);
            return Problems.Create(StatusCodes.Status502BadGateway, ServerTexts.Launch.LocalFailedTitle,
                result.SteamMissing ? ServerTexts.Launch.LocalSteamMissing(title) : ServerTexts.Launch.LocalStartFailed(title, result.Detail));
        }

        if (!queue.IsBridgeConnected)
        {
            // No bridge: the browser that asked opens steam:// itself (it is on the PC that runs Steam, or nothing happens)
            logger.LogInformation("Spielstart {Title} (AppID {AppId}): keine Bridge verbunden, der Browser öffnet {Url} selbst.", title, appId, url);
            return Results.Ok(new LaunchResponseDto("browser", url, appId, null, null, null, BridgeConnected: false));
        }

        var queued = queue.Enqueue(appId, title);
        if (queued.Command is not { } command)
        {
            return Problems.Create(StatusCodes.Status429TooManyRequests, ServerTexts.Launch.TooManyOpenTitle, ServerTexts.Launch.TooManyOpenDetail(BridgeCommandQueue.MaxOpen));
        }

        if (queued.Created)
        {
            logger.LogInformation("Spielstart angefordert: {Title} (AppID {AppId}) – Befehl {Id} liegt für die Bridge bereit.", title, appId, command.Id);
        }

        return Results.Ok(new LaunchResponseDto(
            "bridge", url, appId, command.Id.ToString(), StatusName(command.State), queue.RemainingSeconds(command), BridgeConnected: true));
    }

    private static LaunchCommandStatusDto DescribeCommand(LaunchCommand command)
    {
        var finished = !command.IsOpen;
        var message = command.State switch
        {
            LaunchState.Pending or LaunchState.Delivered => ServerTexts.Launch.Waiting(command.GameName),
            LaunchState.Done => ServerTexts.Launch.Done(command.GameName),
            LaunchState.Failed => FailureMessage(command),
            // Never picked up vs. picked up but never confirmed: different things to check on the PC
            _ => command.DeliveredAt is null ? ServerTexts.Launch.NotPickedUp : ServerTexts.Launch.NotConfirmed,
        };

        return new LaunchCommandStatusDto(
            command.Id.ToString(), command.AppId, command.GameName, StatusName(command.State), finished, command.FailureReason, message);
    }

    // The PC's own error text (Windows language, not translated) is appended: it says what exactly went wrong
    private static string FailureMessage(LaunchCommand command)
    {
        string text = command.FailureReason == LaunchFailureReason.SteamNotFound ? ServerTexts.Launch.SteamNotFound : ServerTexts.Launch.StartFailed;
        return command.FailureDetail is { Length: > 0 } detail ? $"{text.TrimEnd('.')} ({detail})." : text;
    }

    private static string StatusName(LaunchState state) => state switch
    {
        LaunchState.Pending => "pending",
        LaunchState.Delivered => "delivered",
        LaunchState.Done => "done",
        LaunchState.Failed => "failed",
        _ => "expired",
    };
}
