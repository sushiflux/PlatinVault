using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Endpoints;

/// <summary>
/// Batch mode of the manual flow: one prompt for many chapters, one field for all collected answers. General for every game and every entry point -
/// the chapters of a new-guide plan, "complete guide" (existing chapters stay, new ones go to the end) and failed chapters (an item with a TabId replaces
/// - or, for a "does not exist" answer, removes - that placeholder), mixed in one list. Nothing is sent to an AI provider.
/// </summary>
public static partial class ManualGuideEndpoints
{
    private static void MapManualBatch(RouteGroupBuilder api)
    {
        // ONE prompt for the whole list
        api.MapPost("/games/{slug}/manual/batch-prompt", async (string slug, ManualBatchPromptRequest request, CompanionDbContext db, AiGuideGeneratorService ai, CancellationToken ct) =>
        {
            if (ManualBatchChapters.Normalize(request.Chapters, forPrompt: true, out var chapters) is { } invalid)
            {
                return Problems.BadRequest(ServerTexts.Games.InputIncompleteTitle, invalid);
            }
            var game = await LoadAsync(db, slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            await AdoptLanguageAsync(db, game, ct);
            return Results.Ok(await ai.BuildManualBatchPromptAsync(game, chapters, ct));
        });

        // Everything the user collected, judged chapter by chapter (nothing is saved)
        api.MapPost("/games/{slug}/manual/batch-check", async (string slug, ManualBatchCheckRequest request, CompanionDbContext db, AiGuideGeneratorService ai, CancellationToken ct) =>
        {
            if (Validate(request.Text, request.Chapters, out var chapters) is { } refusal)
            {
                return refusal;
            }
            var game = await LoadAsync(db, slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            var batch = ai.CheckManualBatch(game, request.Text, AiGuideGeneratorService.ResolveStoredChapters(game, chapters));
            return batch.TooMany ? TooManyObjects() : Results.Ok(batch.Dto);
        });

        // Saves every valid chapter at once, with exactly the single-chapter rules, in ONE transaction
        api.MapPost("/games/{slug}/manual/batch-answer", async (string slug, ManualBatchAnswerRequest request, CompanionDbContext db, AiGuideGeneratorService ai, GuideTranslationService translations, CancellationToken ct) =>
        {
            if (Validate(request.Text, request.Chapters, out var chapters) is { } refusal)
            {
                return refusal;
            }
            var game = await db.Games.Include(g => g.Tabs).Include(g => g.ProgressChecks).FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            await AdoptLanguageAsync(db, game, ct);
            chapters = AiGuideGeneratorService.ResolveStoredChapters(game, chapters);

            var batch = ai.CheckManualBatch(game, request.Text, chapters);
            if (batch.TooMany)
            {
                return TooManyObjects();
            }

            var (plans, skipped) = SelectBatch(game, chapters, batch, request.Keys);
            var saved = new List<ManualBatchSavedItemDto>();

            // Ids that are taken: everything on the page except the tabs this batch overwrites or removes (placeholders, an old walkthrough)
            var overwritten = plans.Where(p => p.Placeholder is not null).Select(p => p.Placeholder!.Id).ToHashSet();
            var oldWalkthrough = game.Tabs.FirstOrDefault(WalkthroughTab.Is);
            var replacesWalkthrough = oldWalkthrough is not null && plans.Any(p => p.Placeholder is null && p.Checked.Dto.NotApplicable is null && p.Chapter.TopicId == WalkthroughTab.TopicId);
            if (replacesWalkthrough)
            {
                overwritten.Add(oldWalkthrough!.Id);
            }
            var taken = ApiEndpoints.CollectTakenItemIds(game.Tabs.Where(t => !overwritten.Contains(t.Id)));

            // New chapters go before the optional lore tab, which always stays last; in the order of the list
            var regular = game.Tabs.Where(t => t.Slug != LoreTab.Slug).ToList();
            var next = (regular.Count > 0 ? regular.Max(t => t.SortOrder) : 0) + 1;
            var walkthroughDone = false;

            foreach (var plan in plans)
            {
                var chapter = plan.Chapter;
                var answer = new ManualChapterAnswerRequest(null, chapter.Title, chapter.Icon, chapter.Focus, chapter.TopicId, chapter.TabId);

                // "This topic does not exist in this game": no chapter; the failed chapter it was meant for is removed
                if (plan.Checked.Dto.NotApplicable is not null)
                {
                    if (plan.Placeholder is { } removed)
                    {
                        db.GuideTabs.Remove(removed);
                        game.Tabs.Remove(removed);
                    }
                    saved.Add(new ManualBatchSavedItemDto(chapter.Key, plan.Placeholder?.Id, Replaced: false, Dropped: true));
                    continue;
                }

                if (plan.Placeholder is { } placeholder)
                {
                    // The walkthrough exists once per game: its placeholder (or the walkthrough itself with "replace") counts as THE walkthrough answer of
                    // this list - a further entry for the walkthrough topic is skipped, whichever of the two comes first
                    if (WalkthroughTab.Is(placeholder))
                    {
                        if (walkthroughDone)
                        {
                            skipped.Add(new ManualBatchSkippedDto(chapter.Key, ServerTexts.Manual.BatchSkipSecondWalkthrough));
                            continue;
                        }
                        walkthroughDone = true;
                    }
                    var fresh = ai.BuildManualChapterTab(game, placeholder.SortOrder, answer, plan.Checked.CanonicalJson!);
                    // Same tab, same place, same address; a chosen group stays; for a replaced chapter with content the ticks of item ids that still
                    // exist stay and the others go (the same rules as "redo" and a repeated AI research)
                    AiActionEndpoints.ReplaceChapterInPlace(db, game, placeholder, fresh, taken);
                    saved.Add(new ManualBatchSavedItemDto(chapter.Key, placeholder.Id, Replaced: true, Dropped: false));
                    continue;
                }

                var tab = ai.BuildManualChapterTab(game, next, answer, plan.Checked.CanonicalJson!);
                if (tab.Slug == WalkthroughTab.Slug)
                {
                    // The walkthrough exists once per game (fixed address): a new answer replaces the one that is there
                    if (walkthroughDone)
                    {
                        skipped.Add(new ManualBatchSkippedDto(chapter.Key, ServerTexts.Manual.BatchSkipSecondWalkthrough));
                        continue;
                    }
                    walkthroughDone = true;
                    if (oldWalkthrough is not null)
                    {
                        // The same release/cleanup as "redo": ticks move along with a one-to-one name match, ticks of entries that are gone go
                        AiActionEndpoints.ReplaceChapterInPlace(db, game, oldWalkthrough, tab, taken);
                        saved.Add(new ManualBatchSavedItemDto(chapter.Key, oldWalkthrough.Id, Replaced: true, Dropped: false));
                        continue;
                    }
                }

                ApiEndpoints.MakeItemIdsUnique([tab], taken);
                db.GuideTabs.Add(tab);
                if (!game.Tabs.Contains(tab))
                {
                    game.Tabs.Add(tab);
                }
                next++;
                saved.Add(new ManualBatchSavedItemDto(chapter.Key, tab.Id, Replaced: false, Dropped: false));
            }

            if (saved.Count > 0)
            {
                // Link every saved chapter to the rows of the others once all of them are in place
                GuideLinkMaintenance.Apply(db, game);
                game.UpdatedAt = DateTimeOffset.UtcNow;
                await GuideLinkMaintenance.SaveAsync(db, ct);
            }

            db.ChangeTracker.Clear();
            var reloaded = await db.Games
                .AsNoTracking()
                .Include(g => g.Tabs.OrderBy(t => t.SortOrder))
                .Include(g => g.ProgressChecks)
                .FirstAsync(g => g.Slug == slug, ct);
            return Results.Ok(new ManualBatchSavedDto(await ApiEndpoints.BuildDetailAsync(db, translations, reloaded, null, ct), saved, skipped));
        });
    }

    /// <summary>One chapter that is going to be saved: its checked answer and, for a failed chapter, the placeholder it replaces or removes.</summary>
    private sealed record BatchSavePlan(BatchChapter Chapter, AiGuideGeneratorService.ManualChapterChecked Checked, GuideTabEntity? Placeholder);

    /// <summary>
    /// Which chapters are saved: the requested keys, or by default every item that is ok, a warning or notApplicable. Everything else is reported as
    /// skipped with the reason - an item with errors is never saved. Chapters come back in the order of the list.
    /// </summary>
    private static (List<BatchSavePlan> Plans, List<ManualBatchSkippedDto> Skipped) SelectBatch(GameEntity game, IReadOnlyList<BatchChapter> chapters, AiGuideGeneratorService.BatchChecked batch, List<string>? keys)
    {
        var plans = new List<BatchSavePlan>();
        var skipped = new List<ManualBatchSkippedDto>();

        HashSet<string>? requested = null;
        if (keys is not null)
        {
            requested = [];
            var known = chapters.ToDictionary(c => c.KeyForm, StringComparer.Ordinal);
            foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)))
            {
                if (known.TryGetValue(ManualBatchChapters.KeyForm(key), out var chapter))
                {
                    requested.Add(chapter.Key);
                }
                else
                {
                    skipped.Add(new ManualBatchSkippedDto(key.Trim(), ServerTexts.Manual.BatchSkipUnknownKey(key.Trim())));
                }
            }
        }

        var usedTabs = new HashSet<Guid>();
        foreach (var chapter in chapters)
        {
            if (requested is not null && !requested.Contains(chapter.Key))
            {
                continue;
            }

            var item = batch.Dto.Items[chapter.Index];
            if (item.State == "missing")
            {
                skipped.Add(new ManualBatchSkippedDto(chapter.Key, ServerTexts.Manual.BatchSkipMissing));
                continue;
            }
            if (item.State == "error" || !batch.Checks.TryGetValue(chapter.Key, out var checkedAnswer))
            {
                skipped.Add(new ManualBatchSkippedDto(chapter.Key, ServerTexts.Manual.BatchSkipErrors(string.Join(" ", item.Errors.Select(e => e.Message)))));
                continue;
            }

            GuideTabEntity? placeholder = null;
            if (chapter.TabId is { } tabId)
            {
                placeholder = game.Tabs.FirstOrDefault(t => t.Id == tabId);
                if (placeholder is null)
                {
                    skipped.Add(new ManualBatchSkippedDto(chapter.Key, ServerTexts.Manual.ChapterNotFound));
                    continue;
                }
                if (ChapterRemoval.Check(placeholder) != ChapterRemovalVerdict.Removable)
                {
                    skipped.Add(new ManualBatchSkippedDto(chapter.Key, ServerTexts.Manual.ChapterNotReplaceable));
                    continue;
                }
                var isPlaceholder = FailedChapters.Read(placeholder.SectionsJson) is not null;
                // A chapter with content only with "replace": true - and an answer "does not exist" never deletes it
                if (!isPlaceholder && !chapter.Replace)
                {
                    skipped.Add(new ManualBatchSkippedDto(chapter.Key, ServerTexts.Manual.ChapterNotAPlaceholder));
                    continue;
                }
                if (!isPlaceholder && checkedAnswer.Dto.NotApplicable is not null)
                {
                    skipped.Add(new ManualBatchSkippedDto(chapter.Key, ServerTexts.Manual.BatchReplaceNotApplicable));
                    continue;
                }
                if (!usedTabs.Add(tabId))
                {
                    skipped.Add(new ManualBatchSkippedDto(chapter.Key, ServerTexts.Manual.BatchSkipTabUsedTwice));
                    continue;
                }
            }
            plans.Add(new BatchSavePlan(chapter, checkedAnswer, placeholder));
        }
        return (plans, skipped);
    }

    /// <summary>The checks both batch calls share: the list, the size of the pasted text. Null = fine.</summary>
    private static IResult? Validate(string? text, List<ManualBatchChapter>? list, out List<BatchChapter> chapters)
    {
        if (ManualBatchChapters.Normalize(list, forPrompt: false, out chapters) is { } invalid)
        {
            return Problems.BadRequest(ServerTexts.Manual.NotSavedTitle, invalid);
        }
        if (text is not null && text.Length > ManualBatchReader.MaxKilobytes * 1000)
        {
            return Problems.Create(StatusCodes.Status413PayloadTooLarge, ServerTexts.Manual.NotSavedTitle, ServerTexts.Manual.BatchTooLarge(text.Length / 1000, ManualBatchReader.MaxKilobytes));
        }
        return null;
    }

    private static IResult TooManyObjects() =>
        Problems.BadRequest(ServerTexts.Manual.NotSavedTitle, ServerTexts.Manual.BatchTooManyObjects(ManualBatchReader.MaxObjects));
}
