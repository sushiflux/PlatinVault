using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Endpoints;

/// <summary>
/// Translation of a guide into the other app language (see <see cref="GuideTranslationService"/>). The page opens a guide in the UI
/// language: GET /games/{slug} answers with the translated version when it is complete, else with the original plus the state; this
/// file holds the two calls around it. POST starts (or confirms) the background job and answers at once, GET asks how it is doing.
/// </summary>
public static class GuideTranslationEndpoints
{
    public static void MapGuideTranslation(this RouteGroupBuilder api)
    {
        // Starts the translation of the guide into ?lang= (default: the language of the request). Nothing to do when the guide is
        // already in that language or fully translated; a running job is not started twice; after a failure (no key, quota) a new
        // attempt only starts with ?force=true ("try again"), so an open page cannot hammer the AI provider.
        api.MapPost("/games/{slug}/translate", async (
            string slug,
            string? lang,
            bool? force,
            CompanionDbContext db,
            GuideTranslationService translations,
            CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking().Include(g => g.Tabs).FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }

            var language = Lang.NormalizeOr(lang, Lang.Current);
            var view = await GuideTranslations.EvaluateAsync(db, game, language, translations.GetJob(game.Id, language), ct);
            if (view.State == "missing")
            {
                translations.TryStart(game.Id, language, force == true);
                view = view with { Job = translations.GetJob(game.Id, language) };
            }
            return Results.Ok(view.ToDto());
        });

        // How the translation of the guide into ?lang= is doing (cheap; the page polls it while a job runs).
        api.MapGet("/games/{slug}/translation", async (
            string slug,
            string? lang,
            CompanionDbContext db,
            GuideTranslationService translations,
            CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking().Include(g => g.Tabs).FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }

            var language = Lang.NormalizeOr(lang, Lang.Current);
            var view = await GuideTranslations.EvaluateAsync(db, game, language, translations.GetJob(game.Id, language), ct);
            return Results.Ok(view.ToDto());
        });
    }
}
