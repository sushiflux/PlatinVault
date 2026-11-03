using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Endpoints;

/// <summary>
/// Optional "Twitch &amp; IGDB" integration: IGDB facts per game and live Twitch streams per game. Both need the Client-ID and
/// Client-Secret of a Twitch developer app (Settings); without them every data endpoint answers 204 and the UI hides the
/// section. The browser never talks to IGDB or Twitch itself (IGDB has no CORS, the secret must stay on the server).
/// </summary>
public static class TwitchIgdbEndpoints
{
    public static void MapTwitchIgdb(this RouteGroupBuilder api)
    {
        // IGDB facts of a vault game (found through its Steam AppID). 204 = no credentials, no AppID or IGDB does not know the game.
        api.MapGet("/games/{slug}/igdb", async (string slug, CompanionDbContext db, IgdbService igdb, CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking()
                .Where(g => g.Slug == slug)
                .Select(g => new { g.SteamAppId })
                .FirstOrDefaultAsync(ct);
            if (game is null)
            {
                return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.GameNotFoundTitle, ServerTexts.GameNotFound(slug));
            }
            if (game.SteamAppId is not > 0 || !igdb.IsConfigured)
            {
                return Results.NoContent();
            }

            try
            {
                var facts = await igdb.GetBySteamAppIdAsync(game.SteamAppId.Value, ct);
                return facts is null ? Results.NoContent() : Results.Ok(facts);
            }
            catch (TwitchApiException ex)
            {
                return Problems.Create(StatusCodes.Status502BadGateway, ServerTexts.Twitch.IgdbUnavailableTitle, ex.Message);
            }
        });

        // Live streams of a vault game. 204 = feature off or no credentials.
        api.MapGet("/games/{slug}/streams", async (
            string slug,
            string? lang,
            CompanionDbContext db,
            SettingsStore settings,
            TwitchStreamsService streams,
            CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking()
                .Where(g => g.Slug == slug)
                .Select(g => new { g.SteamAppId, g.Title })
                .FirstOrDefaultAsync(ct);
            if (game is null)
            {
                return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.GameNotFoundTitle, ServerTexts.GameNotFound(slug));
            }
            if (!await settings.IsTwitchStreamsActiveAsync(ct))
            {
                return Results.NoContent();
            }

            return await LoadStreamsAsync(streams, game.SteamAppId, game.Title, lang, ct);
        });

        // Live streams of any library game (dashboard "Alle Spiele"), identified by Steam AppID and/or title.
        api.MapGet("/twitch/streams", async (
            int? appId,
            string? name,
            string? lang,
            SettingsStore settings,
            TwitchStreamsService streams,
            CancellationToken ct) =>
        {
            var title = name?.Trim();
            if (appId is not > 0 && string.IsNullOrWhiteSpace(title))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Twitch.GameMissingTitle, ServerTexts.Twitch.GameMissingDetail);
            }
            if (title is { Length: > 200 })
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Twitch.NameTooLongTitle, ServerTexts.Twitch.NameTooLongDetail);
            }
            if (!await settings.IsTwitchStreamsActiveAsync(ct))
            {
                return Results.NoContent();
            }

            return await LoadStreamsAsync(streams, appId, title, lang, ct);
        });

        // How many streams are live right now for a batch of games (the count badge on the Twitch buttons of the dashboard cards). A batch is
        // answered within a few seconds: games that are not done yet are listed as pending, the client asks again. 204 = feature off or no
        // credentials (the buttons then keep their plain look). Failed lookups simply leave a game out - never an error for a badge.
        api.MapPost("/twitch/live-counts", async (
            LiveCountsRequest? req,
            SettingsStore settings,
            TwitchLiveCountService counts,
            CancellationToken ct) =>
        {
            var games = req?.Games ?? [];
            if (games.Count > TwitchLiveCountService.MaxGames)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Twitch.GameMissingTitle, ServerTexts.Twitch.TooManyGames(TwitchLiveCountService.MaxGames));
            }
            if (games.Any(g => g is null || g.Name is { Length: > 200 }))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Twitch.NameTooLongTitle, ServerTexts.Twitch.NameTooLongDetail);
            }
            if (!await settings.IsTwitchStreamsActiveAsync(ct))
            {
                return Results.NoContent();
            }

            return Results.Ok(await counts.CountAsync(games, req?.Lang, TimeSpan.FromSeconds(8), ct));
        });

        // Credentials check for the settings page (like /ai/models): blank fields test the stored credentials. The answer is
        // always 200 with Connected true/false and a message in the language of the request; the tested values are not stored.
        api.MapPost("/twitch/test", async (TwitchTestRequestDto? req, TwitchAuthService auth, CancellationToken ct) =>
        {
            return Results.Ok(await auth.TestAsync(req?.ClientId, req?.ClientSecret, ct));
        });
    }

    private static async Task<IResult> LoadStreamsAsync(
        TwitchStreamsService streams, int? appId, string? title, string? lang, CancellationToken ct)
    {
        try
        {
            var result = await streams.GetStreamsAsync(appId, title, lang, ct);
            return result is null ? Results.NoContent() : Results.Ok(result);
        }
        catch (TwitchApiException ex)
        {
            return Problems.Create(StatusCodes.Status502BadGateway, ServerTexts.Twitch.TwitchUnavailableTitle, ex.Message);
        }
    }
}

/// <summary>Body of <c>POST /api/twitch/live-counts</c>: up to 120 games (Steam AppID + title) and an optional two-letter language filter.</summary>
public sealed record LiveCountsRequest(List<LiveCountGame>? Games, string? Lang);
