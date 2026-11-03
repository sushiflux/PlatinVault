using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Endpoints;

/// <summary>
/// The guide via the user's own AI subscription ("copy the prompt, paste the answer"). Everything here is local: prompts are built, pasted answers
/// are read and saved - nothing is ever sent to an AI provider. The browser keeps the plan between the steps, the server stores nothing of it.
/// </summary>
public static class ManualGuideEndpoints
{
    private const int MaxTitle = 240;
    private const int MaxFocus = 4000;

    public static void MapManualGuide(this RouteGroupBuilder api)
    {
        // Step 1: the prompt for the chapter list
        api.MapPost("/games/{slug}/manual/blueprint-prompt", async (string slug, CompanionDbContext db, AiGuideGeneratorService ai, CancellationToken ct) =>
        {
            var game = await LoadAsync(db, slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            await AdoptLanguageAsync(db, game, ct);
            return Results.Ok(await ai.BuildManualBlueprintPromptAsync(game, ct));
        });

        // The pasted answer to it: errors/warnings in the UI language, and - when it is readable - the plan the builder shows
        api.MapPost("/games/{slug}/manual/blueprint-answer", async (string slug, ManualAnswerRequest request, CompanionDbContext db, AiGuideGeneratorService ai, CancellationToken ct) =>
        {
            var game = await LoadAsync(db, slug, ct);
            return game is null ? Problems.GameNotFound(slug) : Results.Ok(await ai.CheckManualBlueprintAsync(game, request.Text, ct));
        });

        // Step 2: the prompt for one chapter
        api.MapPost("/games/{slug}/manual/chapter-prompt", async (string slug, ManualChapterPromptRequest request, CompanionDbContext db, AiGuideGeneratorService ai, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Focus))
            {
                return Problems.BadRequest(ServerTexts.Games.InputIncompleteTitle, ServerTexts.Manual.PromptTitleMissing);
            }
            if (request.Title.Length > MaxTitle || request.Focus.Length > MaxFocus)
            {
                return Problems.BadRequest(ServerTexts.Games.InputIncompleteTitle, ServerTexts.Import.FieldTooLong("title/focus", Math.Max(request.Title.Length, request.Focus.Length), MaxFocus));
            }
            var game = await LoadAsync(db, slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            await AdoptLanguageAsync(db, game, ct);
            return Results.Ok(await ai.BuildManualChapterPromptAsync(game, request, ct));
        });

        // The pasted answer to a chapter prompt, checked live (nothing is saved)
        api.MapPost("/games/{slug}/manual/chapter-check", async (string slug, ManualChapterAnswerRequest request, CompanionDbContext db, AiGuideGeneratorService ai, CancellationToken ct) =>
        {
            var game = await LoadAsync(db, slug, ct);
            return game is null ? Problems.GameNotFound(slug) : Results.Ok(ai.CheckManualChapter(game, request.Text, request.Title ?? string.Empty, request.TopicId, request.TabId is not null).Dto);
        });

        // Saves the answer: a new chapter at the end of the guide (before the lore tab), or in place of a failed chapter's placeholder (TabId)
        api.MapPost("/games/{slug}/manual/chapter-answer", async (string slug, ManualChapterAnswerRequest request, CompanionDbContext db, AiGuideGeneratorService ai, GuideTranslationService translations, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > MaxTitle)
            {
                return Problems.BadRequest(ServerTexts.Games.InputIncompleteTitle, ServerTexts.Manual.PromptTitleMissing);
            }
            var game = await db.Games.Include(g => g.Tabs).Include(g => g.ProgressChecks).FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            await AdoptLanguageAsync(db, game, ct);

            var checkedAnswer = ai.CheckManualChapter(game, request.Text, request.Title, request.TopicId, request.TabId is not null);
            if (checkedAnswer.CanonicalJson is null)
            {
                return Problems.BadRequest(ServerTexts.Manual.NotSavedTitle, ServerTexts.Manual.NotSaved(string.Join(" ", checkedAnswer.Dto.Errors.Select(e => e.Message))));
            }

            // "This topic does not exist in this game": no chapter is created; the failed chapter it was meant for is removed (never one with content)
            if (checkedAnswer.Dto.NotApplicable is not null)
            {
                Guid? removed = null;
                if (request.TabId is { } droppedTabId)
                {
                    var placeholder = game.Tabs.FirstOrDefault(t => t.Id == droppedTabId);
                    if (placeholder is null)
                    {
                        return Problems.NotFound(ServerTexts.Manual.NotSavedTitle, ServerTexts.Manual.ChapterNotFound);
                    }
                    if (placeholder.Slug == LoreTab.Slug || FailedChapters.Read(placeholder.SectionsJson) is null)
                    {
                        return Problems.BadRequest(ServerTexts.Manual.NotSavedTitle, ServerTexts.Manual.ChapterNotAPlaceholder);
                    }
                    db.GuideTabs.Remove(placeholder);
                    game.Tabs.Remove(placeholder);
                    game.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                    removed = droppedTabId;
                }

                db.ChangeTracker.Clear();
                var afterDrop = await db.Games
                    .AsNoTracking()
                    .Include(g => g.Tabs.OrderBy(t => t.SortOrder))
                    .Include(g => g.ProgressChecks)
                    .FirstAsync(g => g.Slug == slug, ct);
                return Results.Ok(new ManualSavedDto(await ApiEndpoints.BuildDetailAsync(db, translations, afterDrop, null, ct), removed, Replaced: false, Dropped: true));
            }

            GuideTabEntity saved;
            var replaced = false;
            if (request.TabId is { } tabId)
            {
                var placeholder = game.Tabs.FirstOrDefault(t => t.Id == tabId);
                if (placeholder is null)
                {
                    return Problems.NotFound(ServerTexts.Manual.NotSavedTitle, ServerTexts.Manual.ChapterNotFound);
                }
                if (FailedChapters.Read(placeholder.SectionsJson) is null)
                {
                    return Problems.BadRequest(ServerTexts.Manual.NotSavedTitle, ServerTexts.Manual.ChapterNotAPlaceholder);
                }
                var fresh = ai.BuildManualChapterTab(game, placeholder.SortOrder, request, checkedAnswer.CanonicalJson);
                ApiEndpoints.MakeItemIdsUnique([fresh], ApiEndpoints.CollectItemIds(game.Tabs.Where(t => t.Id != placeholder.Id)));
                // Same tab, same place, same address; a group that was chosen before stays (the same rule as a repeated AI research)
                placeholder.Title = fresh.Title;
                placeholder.Icon = fresh.Icon;
                placeholder.SectionsJson = fresh.SectionsJson;
                placeholder.GroupKey ??= fresh.GroupKey;
                saved = placeholder;
                replaced = true;
            }
            else
            {
                // New chapters go before the optional lore tab, which always stays last
                var regular = game.Tabs.Where(t => t.Slug != LoreTab.Slug).ToList();
                var next = (regular.Count > 0 ? regular.Max(t => t.SortOrder) : 0) + 1;
                saved = ai.BuildManualChapterTab(game, next, request, checkedAnswer.CanonicalJson);
                // The walkthrough exists once per game (fixed address): a new answer replaces the one that is there
                var oldWalkthrough = saved.Slug == WalkthroughTab.Slug ? game.Tabs.FirstOrDefault(WalkthroughTab.Is) : null;
                if (oldWalkthrough is not null)
                {
                    ApiEndpoints.MakeItemIdsUnique([saved], ApiEndpoints.CollectItemIds(game.Tabs.Where(t => t.Id != oldWalkthrough.Id)));
                    oldWalkthrough.Title = saved.Title;
                    oldWalkthrough.Icon = saved.Icon;
                    oldWalkthrough.SectionsJson = saved.SectionsJson;
                    oldWalkthrough.GroupKey ??= saved.GroupKey;
                    saved = oldWalkthrough;
                    replaced = true;
                }
                else
                {
                    ApiEndpoints.MakeItemIdsUnique([saved], ApiEndpoints.CollectItemIds(game.Tabs));
                    db.GuideTabs.Add(saved);
                }
            }

            game.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            db.ChangeTracker.Clear();
            var reloaded = await db.Games
                .AsNoTracking()
                .Include(g => g.Tabs.OrderBy(t => t.SortOrder))
                .Include(g => g.ProgressChecks)
                .FirstAsync(g => g.Slug == slug, ct);
            return Results.Ok(new ManualSavedDto(await ApiEndpoints.BuildDetailAsync(db, translations, reloaded, null, ct), saved.Id, replaced));
        });
    }

    private static Task<GameEntity?> LoadAsync(CompanionDbContext db, string slug, CancellationToken ct) =>
        db.Games.Include(g => g.Tabs).FirstOrDefaultAsync(g => g.Slug == slug, ct);

    /// <summary>A tile without any researched chapter takes the language of the UI before its first prompt, so prompt and answer agree (see AdoptRequestLanguageIfEmpty).</summary>
    private static async Task AdoptLanguageAsync(CompanionDbContext db, GameEntity game, CancellationToken ct)
    {
        if (ApiEndpoints.AdoptRequestLanguageIfEmpty(game))
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
