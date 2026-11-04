using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Endpoints;

/// <summary>
/// The guide via the user's own AI subscription ("copy the prompt, paste the answer"). Everything here is local: prompts are built, pasted answers
/// are read and saved - nothing is ever sent to an AI provider. The browser keeps the plan between the steps, the server stores nothing of it.
/// </summary>
public static partial class ManualGuideEndpoints
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
            // The assignment may be left out when an existing chapter is replaced: the server builds it from the stored chapter (like "redo")
            var replacesStored = request.TabId is not null && request.Replace == true;
            if (string.IsNullOrWhiteSpace(request.Title) || (string.IsNullOrWhiteSpace(request.Focus) && !replacesStored))
            {
                return Problems.BadRequest(ServerTexts.Games.InputIncompleteTitle, ServerTexts.Manual.PromptTitleMissing);
            }
            var focusLength = request.Focus?.Length ?? 0;
            if (request.Title.Length > MaxTitle || focusLength > MaxFocus)
            {
                return Problems.BadRequest(ServerTexts.Games.InputIncompleteTitle, ServerTexts.Import.FieldTooLong("title/focus", Math.Max(request.Title.Length, focusLength), MaxFocus));
            }
            var game = await LoadAsync(db, slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            if (request.TabId is { } tabId)
            {
                var existing = game.Tabs.FirstOrDefault(t => t.Id == tabId);
                if (existing is null)
                {
                    return Problems.NotFound(ServerTexts.Games.InputIncompleteTitle, ServerTexts.Manual.ChapterNotFound);
                }
                if (ChapterRemoval.Check(existing) != ChapterRemovalVerdict.Removable)
                {
                    return Problems.BadRequest(ServerTexts.Games.InputIncompleteTitle, ServerTexts.Manual.ChapterNotReplaceable);
                }
            }
            await AdoptLanguageAsync(db, game, ct);
            return Results.Ok(await ai.BuildManualChapterPromptAsync(game, request, ct));
        });

        // The pasted answer to a chapter prompt, checked live (nothing is saved)
        api.MapPost("/games/{slug}/manual/chapter-check", async (string slug, ManualChapterAnswerRequest request, CompanionDbContext db, AiGuideGeneratorService ai, CancellationToken ct) =>
        {
            var game = await LoadAsync(db, slug, ct);
            return game is null ? Problems.GameNotFound(slug) : Results.Ok(ai.CheckManualChapter(game, request.Text, request.Title ?? string.Empty, request.TopicId, request.TabId is not null && request.Replace != true).Dto);
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

            var checkedAnswer = ai.CheckManualChapter(game, request.Text, request.Title, request.TopicId, request.TabId is not null && request.Replace != true);
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
                        // A chapter with content is never deleted by an answer (also not with "replace")
                        return Problems.BadRequest(ServerTexts.Manual.NotSavedTitle,
                            request.Replace == true && placeholder.Slug != LoreTab.Slug ? ServerTexts.Manual.BatchReplaceNotApplicable : ServerTexts.Manual.ChapterNotAPlaceholder);
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
                if (ChapterRemoval.Check(placeholder) != ChapterRemovalVerdict.Removable)
                {
                    return Problems.BadRequest(ServerTexts.Manual.NotSavedTitle, ServerTexts.Manual.ChapterNotReplaceable);
                }
                // A chapter with content is only replaced when the request says so explicitly ("Kapitel neu schreiben")
                if (FailedChapters.Read(placeholder.SectionsJson) is null && request.Replace != true)
                {
                    return Problems.BadRequest(ServerTexts.Manual.NotSavedTitle, ServerTexts.Manual.ChapterNotAPlaceholder);
                }
                var fresh = ai.BuildManualChapterTab(game, placeholder.SortOrder, request, checkedAnswer.CanonicalJson);
                // Same tab, same place, same address; a chosen group stays; ticks of item ids that still exist stay (the same rules as "redo")
                AiActionEndpoints.ReplaceChapterInPlace(db, game, placeholder, fresh);
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
                    // The same replace as "redo": ticks of entries that only got a new id move along, ticks of entries that are gone go
                    AiActionEndpoints.ReplaceChapterInPlace(db, game, oldWalkthrough, saved);
                    saved = oldWalkthrough;
                    replaced = true;
                }
                else
                {
                    ApiEndpoints.MakeItemIdsUnique([saved], ApiEndpoints.CollectTakenItemIds(game.Tabs));
                    db.GuideTabs.Add(saved);
                    if (!game.Tabs.Contains(saved))
                    {
                        game.Tabs.Add(saved);
                    }
                }
            }

            // Link the saved chapter to the rows of the other chapters (and the other way round), then save everything together
            GuideLinkMaintenance.Apply(db, game);
            game.UpdatedAt = DateTimeOffset.UtcNow;
            await GuideLinkMaintenance.SaveAsync(db, ct);

            db.ChangeTracker.Clear();
            var reloaded = await db.Games
                .AsNoTracking()
                .Include(g => g.Tabs.OrderBy(t => t.SortOrder))
                .Include(g => g.ProgressChecks)
                .FirstAsync(g => g.Slug == slug, ct);
            return Results.Ok(new ManualSavedDto(await ApiEndpoints.BuildDetailAsync(db, translations, reloaded, null, ct), saved.Id, replaced));
        });

        // Batch mode: one prompt for many chapters, one field for all collected answers
        MapManualBatch(api);
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
