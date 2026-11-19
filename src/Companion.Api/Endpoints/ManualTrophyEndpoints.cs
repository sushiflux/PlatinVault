using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Endpoints;

/// <summary>
/// The trophy list of a manual game with the player's own AI subscription (prompt, check, answer; nothing is sent to an AI provider).
/// Contract: <c>ManualGameDtos.cs</c>. The prompt is the one of the AI research plus the chat output contract; the answer goes through the same reader
/// (<see cref="TrophyList"/>) and the same save (<see cref="TrophyListStore"/>) as the AI job <c>POST /api/games/{slug}/ai-trophies</c>.
/// </summary>
public static class ManualTrophyEndpoints
{
    public static void MapManualTrophies(this RouteGroupBuilder api)
    {
        // The prompt to copy into the chat (FileName = the download of the prompt itself; the answer file is named inside the prompt)
        api.MapPost("/games/{slug}/manual/trophies-prompt", async (string slug, ManualTrophiesPromptRequest? request, CompanionDbContext db, AiGuideGeneratorService ai, CancellationToken ct) =>
        {
            var game = await LoadAsync(db, slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            if (AiActionEndpoints.TrophyResearchRefusal(game, request?.Include) is { } refused)
            {
                return refused;
            }
            if (ApiEndpoints.AdoptRequestLanguageIfEmpty(game))
            {
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(await ai.BuildManualTrophyPromptAsync(game, AiGuideGeneratorService.DefaultInclude(game, request?.Include), ct));
        });

        // The pasted answer, checked live (nothing is saved)
        api.MapPost("/games/{slug}/manual/trophies-check", async (string slug, ManualAnswerRequest request, CompanionDbContext db, AiGuideGeneratorService ai, CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            if (!GamePlatforms.IsManual(game))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Trophies.NotManualTitle, ServerTexts.Trophies.NotManual);
            }
            return Results.Ok(await ai.CheckManualTrophiesAsync(game, request.Text, ct));
        });

        // Saves the answer: the list (ticks kept, see TrophyMerge) and the facts of a game without IGDB; 400 with errors[] when it cannot be used
        api.MapPost("/games/{slug}/manual/trophies-answer", async (string slug, ManualAnswerRequest request, CompanionDbContext db, GuideTranslationService translations, CancellationToken ct) =>
        {
            var game = await LoadAsync(db, slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            if (!GamePlatforms.IsManual(game))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Trophies.NotManualTitle, ServerTexts.Trophies.NotManual);
            }
            ApiEndpoints.AdoptRequestLanguageIfEmpty(game);

            var (read, include) = AiGuideGeneratorService.ReadManualTrophyAnswer(game, request.Text);
            if (!read.Ok)
            {
                return Problems.BadRequestWithIssues(ServerTexts.Trophies.NotSavedTitle, ServerTexts.Trophies.NotSaved(string.Join(" ", read.Errors.Select(e => e.Message))), read.Errors);
            }

            var saved = await TrophyListStore.SaveAsync(db, game.Id, read, include, DateTimeOffset.UtcNow, ct);
            db.ChangeTracker.Clear();
            var list = await ManualTrophyLists.BuildAsync(db, game.Id, null, translations, ct);
            return list is null
                ? Problems.GameNotFound(slug)
                : Results.Ok(new ManualTrophyResearchResultDto(list, saved.FactsUpdated, saved.KeptTicks, saved.RetainedOldTicked));
        });
    }

    // Tabs are loaded: a tile without chapters adopts the request language before its first research (AdoptRequestLanguageIfEmpty)
    private static Task<GameEntity?> LoadAsync(CompanionDbContext db, string slug, CancellationToken ct) =>
        db.Games.Include(g => g.Tabs).FirstOrDefaultAsync(g => g.Slug == slug, ct);
}
