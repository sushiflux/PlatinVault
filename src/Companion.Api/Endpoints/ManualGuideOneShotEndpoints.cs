using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Endpoints;

/// <summary>
/// The whole guide from ONE prompt of the user's own AI subscription: the prompt (research + plan + all chapters) and the reading of the plan from the pasted
/// answer. Saving goes through the batch endpoints (batch-check / batch-answer), which skip the plan element. Nothing is sent to an AI provider.
/// </summary>
public static partial class ManualGuideEndpoints
{
    private static void MapManualOneShot(RouteGroupBuilder api)
    {
        // The one prompt for the whole guide (a new guide, or only the missing chapters of an existing one)
        api.MapPost("/games/{slug}/manual/oneshot-prompt", async (string slug, CompanionDbContext db, AiGuideGeneratorService ai, CancellationToken ct) =>
        {
            var game = await LoadAsync(db, slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            await AdoptLanguageAsync(db, game, ct);
            return Results.Ok(await ai.BuildManualOneShotPromptAsync(game, ct));
        });

        // The plan of the pasted answer, as the chapter list the batch endpoints take (nothing is saved)
        api.MapPost("/games/{slug}/manual/oneshot-plan", async (string slug, ManualAnswerRequest request, CompanionDbContext db, AiGuideGeneratorService ai, CancellationToken ct) =>
        {
            if (request.Text is not null && request.Text.Length > ManualBatchReader.MaxKilobytes * 1000)
            {
                return Problems.Create(StatusCodes.Status413PayloadTooLarge, ServerTexts.Manual.NotSavedTitle, ServerTexts.Manual.BatchTooLarge(request.Text.Length / 1000, ManualBatchReader.MaxKilobytes));
            }
            var game = await LoadAsync(db, slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            var checkedPlan = await ai.CheckManualOneShotPlanAsync(game, request.Text, ct);
            return checkedPlan.TooMany ? TooManyObjects() : Results.Ok(checkedPlan.Dto);
        });
    }
}
