using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Endpoints;

/// <summary>
/// The trophy list of a manual game (read, tick / un-tick by hand). Contract: <c>ManualGameDtos.cs</c>; ticks and platinum go through
/// <c>TrophyProgress</c> (one conditional UPDATE per change: exactly one request, even of two servers on one database file, announces the popup),
/// popups through <c>LiveAnnouncer</c> (the same path as a live Steam unlock: hub, event log, catch-up), the list itself is built by
/// <c>ManualTrophyLists</c>. A Steam game has no such list (its achievements come live from Steam): both calls answer 400 for it.
/// </summary>
public static class TrophyEndpoints
{
    public static void MapTrophies(this RouteGroupBuilder api)
    {
        // The list with the ticks. ?lang=de|en asks for that language (default: the language of the request); until a translation exists the
        // list comes in its own language and translation.state says "missing"
        api.MapGet("/games/{slug}/trophies", async (string slug, string? lang, CompanionDbContext db, GuideTranslationService translations, CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking().Where(g => g.Slug == slug).Select(g => new { g.Id, g.Platform }).FirstOrDefaultAsync(ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            if (GamePlatforms.IsSteam(game.Platform))
            {
                return NotManual();
            }

            var list = await ManualTrophyLists.BuildAsync(db, game.Id, lang, translations, ct);
            return list is null ? Problems.GameNotFound(slug) : Results.Ok(list);
        });

        // Tick (popup + sound like a live unlock, exactly once) or un-tick (silent) one trophy. The answer is the whole list, so the page needs no second call.
        api.MapPost("/games/{slug}/trophies/{trophyKey}/unlock", async (
            string slug,
            string trophyKey,
            SetTrophyUnlockedRequest? req,
            CompanionDbContext db,
            LiveAnnouncer announcer,
            GuideTranslationService translations,
            CancellationToken ct) =>
        {
            if (req is null || string.IsNullOrWhiteSpace(trophyKey) || trophyKey.Length > ManualTrophyLists.MaxKeyLength)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.InvalidRequestTitle, ServerTexts.ManualGames.TrophyKeyInvalid);
            }
            var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            if (GamePlatforms.IsSteam(game.Platform))
            {
                return NotManual();
            }

            var result = await TrophyProgress.SetUnlockedAsync(db, game.Id, trophyKey, req.Unlocked, DateTimeOffset.UtcNow, ct);
            if (result.Outcome is TrophyTickOutcome.GameNotFound)
            {
                return Problems.GameNotFound(slug);
            }
            if (result.Outcome is TrophyTickOutcome.TrophyNotFound)
            {
                return Problems.Create(StatusCodes.Status404NotFound, ServerTexts.ManualGames.TrophyNotFoundTitle, ServerTexts.ManualGames.TrophyNotFound(trophyKey));
            }

            var list = (await ManualTrophyLists.BuildAsync(db, game.Id, null, translations, ct))!;

            // Announce AFTER the change is stored, and only what THIS request changed: a second window or server that ticks the same trophy
            // at the same moment gets "unchanged" and stays silent. The platinum trophy is never a normal popup - its tick is the platinum moment.
            if (result.AnnounceTrophy && list.Achievements.FirstOrDefault(a => a.ApiName == result.TrophyKey) is { } ticked)
            {
                await announcer.AnnounceManualUnlockAsync(game.Id, ticked.ToLiveItem(), game.Title, ct);
            }
            if (result.AnnouncePlatinum)
            {
                await announcer.AnnounceManualPlatinumAsync(game.Title, list.TotalCount, ct);
            }

            return Results.Ok(list with { Announced = new TrophyAnnouncedDto(result.AnnounceTrophy, result.AnnouncePlatinum) });
        });
    }

    private static IResult NotManual() =>
        Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.ManualGames.TrophiesOnlyManualTitle, ServerTexts.ManualGames.TrophiesOnlyManualDetail);
}
