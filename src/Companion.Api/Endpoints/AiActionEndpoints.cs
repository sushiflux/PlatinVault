using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Endpoints;

/// <summary>
/// The AI actions that take minutes: a new guide (<c>ai-generate</c>), the analysis of the guide builder (<c>ai-plan</c>), "complete guide"
/// (<c>ai-complete</c>), one more chapter (<c>ai-expand-tab</c>) and Story &amp; Lore (<c>ai-lore</c>). Each POST checks its input, starts the
/// work as a background job (<see cref="AiJobService"/>) and answers at once with the job (HTTP 202); <c>GET /api/ai/jobs/{id}</c> tells step,
/// elapsed time and finally the result - the same answer the request used to deliver - or the failure with its reason. The work itself is in
/// the <c>Run...Async</c> methods below (the former endpoint bodies), which report their steps.
/// </summary>
public static class AiActionEndpoints
{
    public static void MapAiActions(this RouteGroupBuilder api)
    {
        api.MapGet("/ai/jobs/{id:guid}", (Guid id, AiJobService jobs) =>
            jobs.Get(id) is { } snapshot ? Results.Ok(snapshot.ToDto()) : Problems.NotFound(ServerTexts.Ai.JobGoneTitle, ServerTexts.Ai.JobGone));

        // What runs (or finished a moment ago) for one game: a reloaded game page picks its progress up again
        api.MapGet("/games/{slug}/ai-jobs", (string slug, AiJobService jobs) => Results.Ok(jobs.ForSlug(slug).Select(s => s.ToDto()).ToList()));

        api.MapPost("/games/ai-generate", (AiGenerateGameRequest req, AiJobService jobs) =>
        {
            if (string.IsNullOrWhiteSpace(req.GameTitle))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.GameTitleMissingTitle, ServerTexts.Games.GameTitleMissingDetail);
            }

            var language = Lang.Current;
            var job = jobs.Start("generate", "generate:" + req.GameTitle.Trim().ToLowerInvariant(), null,
                (sp, progress, ct) => RunGenerateAsync(sp, req, language, progress, ct));
            return Accepted(job, "generate");
        });

        api.MapPost("/games/{slug}/ai-plan", async (string slug, AiPlanRequest? req, CompanionDbContext db, AiJobService jobs, CancellationToken ct) =>
        {
            if (!await db.Games.AnyAsync(g => g.Slug == slug, ct))
            {
                return Problems.GameNotFound(slug);
            }
            var job = jobs.Start("plan", "game:" + slug, slug, (sp, progress, token) => RunPlanAsync(sp, slug, req?.ProviderOverride, progress, token));
            return Accepted(job, "plan");
        });

        // "Guide vervollständigen": researches every applicable standard topic the guide does not cover yet (or exactly the chapters ticked in the builder)
        api.MapPost("/games/{slug}/ai-complete", async (string slug, AiCompleteGuideRequest? req, CompanionDbContext db, AiJobService jobs, CancellationToken ct) =>
        {
            if (!await db.Games.AnyAsync(g => g.Slug == slug, ct))
            {
                return Problems.GameNotFound(slug);
            }
            var job = jobs.Start("complete", "game:" + slug, slug, (sp, progress, token) => RunCompleteAsync(sp, slug, req, progress, token));
            return Accepted(job, "complete");
        });

        api.MapPost("/games/{slug}/ai-expand-tab", async (string slug, AiExpandTabRequest req, CompanionDbContext db, AiJobService jobs, CancellationToken ct) =>
        {
            if (!await db.Games.AnyAsync(g => g.Slug == slug, ct))
            {
                return Problems.GameNotFound(slug);
            }
            if (string.IsNullOrWhiteSpace(req.TabTitle) || string.IsNullOrWhiteSpace(req.TopicPrompt))
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Games.InputIncompleteTitle, ServerTexts.Games.InputIncompleteDetail);
            }
            // Another chapter title = another action: it must not attach to the running one (the page would announce the wrong chapter)
            var expandTarget = req.TabTitle.Trim().ToLowerInvariant();
            var job = jobs.Start("expand", "game:" + slug, slug, (sp, progress, token) => RunExpandAsync(sp, slug, req, progress, token), expandTarget, req.TabTitle.Trim());
            return Accepted(job, "expand", expandTarget);
        });

        // "Kapitel erneut recherchieren": chapters whose research failed (placeholder with the remembered topic) are researched again exactly as planned
        api.MapPost("/games/{slug}/ai-retry-chapters", async (string slug, AiRetryChaptersRequest? req, CompanionDbContext db, AiJobService jobs, CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking().Include(g => g.Tabs).FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            if (FailedChapters.Find(game.Tabs, req?.TabIds).Count == 0)
            {
                return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Ai.NoFailedChaptersTitle, ServerTexts.Ai.NoFailedChapters);
            }
            // null = every failed chapter; a list = exactly those (sorted, so the same selection in another order is the same action)
            var retryTarget = req?.TabIds is { Count: > 0 } ids ? string.Join(",", ids.Distinct().Order()) : null;
            var job = jobs.Start("retry", "game:" + slug, slug, (sp, progress, token) => RunRetryAsync(sp, slug, req?.TabIds, req?.ProviderOverride, progress, token), retryTarget);
            return Accepted(job, "retry", retryTarget);
        });

        // "Kapitel neu recherchieren": researches ANY chapter again (content, walkthrough, placeholder) and replaces it in place - only when the research
        // succeeded; the tick marks of item ids that still exist stay. Not the hidden Steam achievement list and not Story & Lore (same rule as removing).
        api.MapPost("/games/{slug}/chapters/{tabId:guid}/redo", async (string slug, Guid tabId, AiRedoChapterRequest? req, CompanionDbContext db, AiJobService jobs, CancellationToken ct) =>
        {
            var game = await db.Games.AsNoTracking().Include(g => g.Tabs).FirstOrDefaultAsync(g => g.Slug == slug, ct);
            if (game is null)
            {
                return Problems.GameNotFound(slug);
            }
            var tab = game.Tabs.FirstOrDefault(t => t.Id == tabId);
            if (tab is null)
            {
                return Problems.NotFound(ServerTexts.Ai.ChapterNotFoundTitle, ServerTexts.Ai.ChapterNotFound);
            }
            switch (ChapterRemoval.Check(tab))
            {
                case ChapterRemovalVerdict.AchievementList:
                    return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Ai.ChapterNotRedoableTitle, ServerTexts.Ai.ChapterRedoAchievementList);
                case ChapterRemovalVerdict.StoryLore:
                    return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Ai.ChapterNotRedoableTitle, ServerTexts.Ai.ChapterIsLore);
            }
            // A redo of ANOTHER chapter must not attach to the running one (the page would say that chapter B was researched again with A's result)
            var job = jobs.Start("redo", "game:" + slug, slug, (sp, progress, token) => RunRedoAsync(sp, slug, tabId, req?.ProviderOverride, progress, token), tabId.ToString(), tab.Title);
            return Accepted(job, "redo", tabId.ToString());
        });

        // "Kapitel entfernen": takes any chapter out of the guide (researched content, walkthrough, failed placeholder). Not the hidden Steam
        // achievement list and not Story & Lore (see ChapterRemoval). The answer is the guide as it is now.
        api.MapDelete("/games/{slug}/chapters/{tabId:guid}", (string slug, Guid tabId, CompanionDbContext db, GuideTranslationService translations, CancellationToken ct) =>
            RemoveChapterAsync(slug, tabId, onlyFailed: false, db, translations, ct));

        // The older address of the same action, for a page that was loaded before "Kapitel entfernen" worked for every chapter: it only removes
        // a chapter that holds nothing but the note "could not be researched" (real content is never removed through it)
        api.MapDelete("/games/{slug}/failed-chapters/{tabId:guid}", (string slug, Guid tabId, CompanionDbContext db, GuideTranslationService translations, CancellationToken ct) =>
            RemoveChapterAsync(slug, tabId, onlyFailed: true, db, translations, ct));

        // Optional "Story & Lore" tab (spoilers): researched on demand. Creates the tab with the fixed slug "story-lore" or replaces its content;
        // the tab always sorts last.
        api.MapPost("/games/{slug}/ai-lore", async (string slug, AiLoreRequest? req, CompanionDbContext db, AiJobService jobs, CancellationToken ct) =>
        {
            if (!await db.Games.AnyAsync(g => g.Slug == slug, ct))
            {
                return Problems.GameNotFound(slug);
            }
            var job = jobs.Start("lore", "game:" + slug, slug, (sp, progress, token) => RunLoreAsync(sp, slug, req?.ProviderOverride, progress, token));
            return Accepted(job, "lore");
        });
    }

    /// <summary>
    /// 202 with the job - or 409 when another kind of AI action of the same game is still running (the job service hands back the running job
    /// for the same key; answering with it would make the page treat e.g. a "complete guide" result as the result of "Story &amp; Lore").
    /// </summary>
    /// <summary>
    /// 202 with the job when it is the one that was asked for (also a double click attaching to it); 409 when the running job of this game is another
    /// action - another kind, or the same kind for another target (another chapter) - so the page never reports the result of a job it did not start.
    /// </summary>
    private static IResult Accepted(AiJob job, string kind, string? target = null)
    {
        if (job.Kind != kind)
        {
            return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Ai.JobBusyTitle, ServerTexts.Ai.JobBusy(job.Kind));
        }
        if (!string.Equals(job.Target, target, StringComparison.Ordinal))
        {
            return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Ai.JobBusyTitle, ServerTexts.Ai.JobBusyOtherTarget(job.Kind, job.TargetLabel));
        }
        return Results.Json(job.Snapshot().ToDto(), statusCode: StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// Removes one chapter with everything that only belonged to it: its translations (rows of <c>guide_tab_translations</c> go with the tab by
    /// the foreign key's cascade) and the tick marks of its own checklist rows and puzzles (<see cref="ChapterRemoval.OrphanedCheckKeys"/>).
    /// Steam achievements are not touched. Everything else of the guide stays.
    /// </summary>
    private static async Task<IResult> RemoveChapterAsync(string slug, Guid tabId, bool onlyFailed, CompanionDbContext db, GuideTranslationService translations, CancellationToken ct)
    {
        var game = await db.Games.Include(g => g.Tabs).Include(g => g.ProgressChecks).FirstOrDefaultAsync(g => g.Slug == slug, ct);
        if (game is null)
        {
            return Problems.GameNotFound(slug);
        }
        var tab = game.Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is null)
        {
            return Problems.NotFound(ServerTexts.Ai.ChapterNotFoundTitle, ServerTexts.Ai.ChapterNotFound);
        }

        switch (ChapterRemoval.Check(tab))
        {
            case ChapterRemovalVerdict.AchievementList:
                return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Ai.ChapterNotRemovableTitle, ServerTexts.Ai.ChapterIsAchievementList);
            case ChapterRemovalVerdict.StoryLore:
                return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Ai.ChapterNotRemovableTitle, ServerTexts.Ai.ChapterIsLore);
        }
        if (onlyFailed && FailedChapters.Read(tab.SectionsJson) is null)
        {
            return Problems.Create(StatusCodes.Status400BadRequest, ServerTexts.Ai.ChapterHasContentTitle, ServerTexts.Ai.ChapterHasContent);
        }

        db.GuideTabs.Remove(tab);
        game.Tabs.Remove(tab);
        // Rows elsewhere that pointed at a row of this chapter become normal rows again and take over its tick - BEFORE the tick marks of this chapter go
        GuideLinkMaintenance.Apply(db, game);
        var orphaned = ChapterRemoval.OrphanedCheckKeys(tab, game.Tabs);
        var orphanedChecks = game.ProgressChecks.Where(p => orphaned.Contains(p.ItemKey)).ToList();
        if (orphanedChecks.Count > 0)
        {
            db.ProgressChecks.RemoveRange(orphanedChecks);
            foreach (var check in orphanedChecks)
            {
                game.ProgressChecks.Remove(check);
            }
        }
        game.UpdatedAt = DateTimeOffset.UtcNow;
        await GuideLinkMaintenance.SaveAsync(db, ct);

        game.Tabs = game.Tabs.OrderBy(t => t.SortOrder).ToList();
        return Results.Ok(await ApiEndpoints.BuildDetailAsync(db, translations, game, null, ct));
    }

    // ---- the work (what the endpoints did before they became jobs) -------------------------------------------------------------------

    private static async Task<object> RunGenerateAsync(IServiceProvider sp, AiGenerateGameRequest req, string language, AiJobProgress progress, CancellationToken ct)
    {
        var db = sp.GetRequiredService<CompanionDbContext>();
        var ai = sp.GetRequiredService<AiGuideGeneratorService>();

        var maxSort = await db.Games.MaxAsync(g => (int?)g.SortOrder, ct) ?? 0;
        // The tile is saved right after the blueprint and every chapter the moment it is done: an interrupted research keeps what was finished
        var sink = new ChapterSink(sp.GetRequiredService<IServiceScopeFactory>(), []);
        var (game, tabs, usage) = await ai.ResearchAndBuildGameAsync(req, maxSort + 1, language, progress, sink, ct);

        game.Tabs = tabs;
        // All chapters are saved now: link the walkthrough rows and side quests to the rows of the other chapters, then answer from what is stored
        if (await GuideLinkMaintenance.RelinkAsync(db, game.Id, ct))
        {
            var stored = await db.Games.AsNoTracking().Include(g => g.Tabs).Include(g => g.ProgressChecks).FirstOrDefaultAsync(g => g.Id == game.Id, ct);
            if (stored is not null)
            {
                game = stored;
            }
        }
        return new AiResultDto<GameSummaryDto>(ApiEndpoints.MapToSummaryDto(game), usage.ToDto());
    }

    private static async Task<object> RunPlanAsync(IServiceProvider sp, string slug, string? providerOverride, AiJobProgress progress, CancellationToken ct)
    {
        var db = sp.GetRequiredService<CompanionDbContext>();
        var ai = sp.GetRequiredService<AiGuideGeneratorService>();

        var game = await db.Games.AsNoTracking().Include(g => g.Tabs).FirstOrDefaultAsync(g => g.Slug == slug, ct)
                   ?? throw new LocalizedException(ServerTexts.GameNotFound(slug));
        // A tile without any researched chapter takes the language of the UI before its first research (see AdoptRequestLanguageIfEmpty)
        ApiEndpoints.AdoptRequestLanguageIfEmpty(game);

        var (plan, usage) = await ai.PlanGuideAsync(game, providerOverride, progress, ct);
        return plan with { Ai = usage.ToDto() };
    }

    private static async Task<object> RunCompleteAsync(IServiceProvider sp, string slug, AiCompleteGuideRequest? req, AiJobProgress progress, CancellationToken ct)
    {
        var db = sp.GetRequiredService<CompanionDbContext>();
        var ai = sp.GetRequiredService<AiGuideGeneratorService>();
        var translations = sp.GetRequiredService<GuideTranslationService>();

        var game = await db.Games
            .Include(g => g.Tabs)
            .Include(g => g.ProgressChecks)
            .FirstOrDefaultAsync(g => g.Slug == slug, ct)
            ?? throw new LocalizedException(ServerTexts.GameNotFound(slug));

        // An empty tile takes the language of the UI - saved now, so the chapters that are saved one by one below agree with the tile
        if (ApiEndpoints.AdoptRequestLanguageIfEmpty(game))
        {
            await db.SaveChangesAsync(ct);
        }

        var regularTabs = game.Tabs.Where(t => t.Slug != LoreTab.Slug).ToList();
        var nextSort = (regularTabs.Count > 0 ? regularTabs.Max(t => t.SortOrder) : 0) + 1;
        // Every chapter is saved the moment it is done (slugs come from the sort order, item ids are kept unique one by one): after an
        // interruption the next "complete guide" finds those chapters as covered and researches only the rest
        var sink = new ChapterSink(sp.GetRequiredService<IServiceScopeFactory>(), ApiEndpoints.CollectTakenItemIds(game.Tabs));
        var (newTabs, droppedTitles, usage) = await ai.CompleteGuideAsync(game, req?.ProviderOverride, nextSort, req?.Chapters, progress, sink, ct);

        if (newTabs.Count > 0)
        {
            progress.Step("saving");
            game.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        // Reload so the answer is built from what is stored (the chapters were saved through other contexts); link the new chapters to the old ones first
        await GuideLinkMaintenance.RelinkAsync(db, game.Id, ct);
        var fresh = await db.Games
            .AsNoTracking()
            .Include(g => g.Tabs.OrderBy(t => t.SortOrder))
            .Include(g => g.ProgressChecks)
            .FirstAsync(g => g.Slug == slug, ct);
        return new AiCompleteGuideResponse(newTabs.Count, newTabs.Select(t => t.Title).ToList(), await ApiEndpoints.BuildDetailAsync(db, translations, fresh, null, ct), usage.ToDto(), droppedTitles);
    }

    private static async Task<object> RunExpandAsync(IServiceProvider sp, string slug, AiExpandTabRequest req, AiJobProgress progress, CancellationToken ct)
    {
        var db = sp.GetRequiredService<CompanionDbContext>();
        var ai = sp.GetRequiredService<AiGuideGeneratorService>();
        var translations = sp.GetRequiredService<GuideTranslationService>();

        var game = await db.Games
            .Include(g => g.Tabs)
            .Include(g => g.ProgressChecks)
            .FirstOrDefaultAsync(g => g.Slug == slug, ct)
            ?? throw new LocalizedException(ServerTexts.GameNotFound(slug));

        // Stored right away: the state loaded here is not used for saving after the research (see below)
        if (ApiEndpoints.AdoptRequestLanguageIfEmpty(game))
        {
            await db.SaveChangesAsync(ct);
        }

        // New tabs go before the optional lore tab, which always stays last
        var regularTabs = game.Tabs.Where(t => t.Slug != LoreTab.Slug).ToList();
        var nextSort = (regularTabs.Count > 0 ? regularTabs.Max(t => t.SortOrder) : 0) + 1;
        var (newTab, usage) = await ai.ResearchAndBuildSingleTabAsync(game, req, nextSort, progress, ct);

        progress.Step("saving");
        // The research took a while: chapters and ticks may have changed meanwhile (the other server, a manual save). Save ONLY the new chapter against
        // the fresh state, then link on what is stored (like "complete" and "retry") - nothing loaded before the research is written back
        db.ChangeTracker.Clear();
        var current = await db.Games.Include(g => g.Tabs).FirstOrDefaultAsync(g => g.Id == game.Id, ct)
            ?? throw new LocalizedException(ServerTexts.GameNotFound(slug));
        ApiEndpoints.MakeItemIdsUnique([newTab], ApiEndpoints.CollectTakenItemIds(current.Tabs));
        current.UpdatedAt = DateTimeOffset.UtcNow;
        db.GuideTabs.Add(newTab);
        await db.SaveChangesAsync(ct);
        await GuideLinkMaintenance.RelinkAsync(db, game.Id, ct);

        var stored = await db.Games
            .AsNoTracking()
            .Include(g => g.Tabs.OrderBy(t => t.SortOrder))
            .Include(g => g.ProgressChecks)
            .FirstAsync(g => g.Id == game.Id, ct);
        return new AiResultDto<GameDetailDto>(await ApiEndpoints.BuildDetailAsync(db, translations, stored, null, ct), usage.ToDto());
    }

    private static async Task<object> RunRetryAsync(IServiceProvider sp, string slug, IReadOnlyCollection<Guid>? tabIds, string? providerOverride, AiJobProgress progress, CancellationToken ct)
    {
        var db = sp.GetRequiredService<CompanionDbContext>();
        var ai = sp.GetRequiredService<AiGuideGeneratorService>();
        var translations = sp.GetRequiredService<GuideTranslationService>();

        var game = await db.Games
            .Include(g => g.Tabs)
            .Include(g => g.ProgressChecks)
            .FirstOrDefaultAsync(g => g.Slug == slug, ct)
            ?? throw new LocalizedException(ServerTexts.GameNotFound(slug));

        var failed = FailedChapters.Find(game.Tabs, tabIds);
        if (failed.Count == 0)
        {
            throw new LocalizedException(ServerTexts.Ai.NoFailedChapters);
        }

        // Every chapter replaces its placeholder the moment it is done: an interruption keeps what was finished, the rest stays a placeholder
        var replacer = new ChapterReplacer(sp.GetRequiredService<IServiceScopeFactory>(), ApiEndpoints.CollectTakenItemIds(game.Tabs));
        var (succeeded, failedTitles, droppedTitles, usage) = await ai.RetryChaptersAsync(game, failed, providerOverride, progress, replacer.ReplaceAsync, ct, replacer.RemoveAsync);

        await GuideLinkMaintenance.RelinkAsync(db, game.Id, ct);
        var fresh = await db.Games
            .AsNoTracking()
            .Include(g => g.Tabs.OrderBy(t => t.SortOrder))
            .Include(g => g.ProgressChecks)
            .FirstAsync(g => g.Slug == slug, ct);
        return new AiRetryChaptersResponse(failed.Count, succeeded, failedTitles, await ApiEndpoints.BuildDetailAsync(db, translations, fresh, null, ct), usage.ToDto(), droppedTitles);
    }

    private static async Task<object> RunRedoAsync(IServiceProvider sp, string slug, Guid tabId, string? providerOverride, AiJobProgress progress, CancellationToken ct)
    {
        var db = sp.GetRequiredService<CompanionDbContext>();
        var ai = sp.GetRequiredService<AiGuideGeneratorService>();
        var translations = sp.GetRequiredService<GuideTranslationService>();

        var game = await db.Games
            .Include(g => g.Tabs)
            .Include(g => g.ProgressChecks)
            .FirstOrDefaultAsync(g => g.Slug == slug, ct)
            ?? throw new LocalizedException(ServerTexts.GameNotFound(slug));
        var tab = game.Tabs.FirstOrDefault(t => t.Id == tabId) ?? throw new LocalizedException(ServerTexts.Ai.ChapterNotFound);
        switch (ChapterRemoval.Check(tab))
        {
            case ChapterRemovalVerdict.AchievementList:
                throw new LocalizedException(ServerTexts.Ai.ChapterRedoAchievementList);
            case ChapterRemovalVerdict.StoryLore:
                throw new LocalizedException(ServerTexts.Ai.ChapterIsLore);
        }

        // Nothing is changed before the research succeeded: a failure throws and the chapter stays as it is
        var (fresh, usage) = await ai.RedoChapterAsync(game, tab, providerOverride, progress, ct);

        progress.Step("saving");
        // The research took minutes: the other server on the same database or a manual save may have changed chapters and ticks meanwhile. Everything
        // loaded before is stale - replace and link on a fresh copy, so nothing old is written back
        db.ChangeTracker.Clear();
        var current = await db.Games
            .Include(g => g.Tabs)
            .Include(g => g.ProgressChecks)
            .FirstOrDefaultAsync(g => g.Id == game.Id, ct)
            ?? throw new LocalizedException(ServerTexts.GameNotFound(slug));
        var target = current.Tabs.FirstOrDefault(t => t.Id == tabId)
            ?? throw new LocalizedException(ServerTexts.Ai.RedoChapterGone(tab.Title));
        if (ChapterRemoval.Check(target) != ChapterRemovalVerdict.Removable)
        {
            throw new LocalizedException(ServerTexts.Ai.RedoChapterGone(tab.Title));
        }
        ReplaceChapterInPlace(db, current, target, fresh);
        current.UpdatedAt = DateTimeOffset.UtcNow;
        await GuideLinkMaintenance.SaveAsync(db, ct);

        db.ChangeTracker.Clear();
        var reloaded = await db.Games
            .AsNoTracking()
            .Include(g => g.Tabs.OrderBy(t => t.SortOrder))
            .Include(g => g.ProgressChecks)
            .FirstAsync(g => g.Slug == slug, ct);
        return new AiResultDto<GameDetailDto>(await ApiEndpoints.BuildDetailAsync(db, translations, reloaded, null, ct), usage.ToDto());
    }

    /// <summary>
    /// Puts a newly researched (or pasted) chapter into an existing tab: same tab, same place, same address; a chosen group stays. Item ids are made unique
    /// against the OTHER tabs (so an id the new version shares with the old one keeps its tick). When the new version gave an entry another id, its tick
    /// and the refs of other chapters pointing at it move to the new row - only on a strict one-to-one name match (<see cref="GuideLinker.PlanIdTransfer"/>,
    /// the linker's rules). Only then the tick marks of item ids that existed only in the old version and nowhere else any more are removed (the same rule
    /// as removing a chapter). Shared by "redo", the manual replace (single and batch) and a replaced walkthrough.
    /// </summary>
    internal static void ReplaceChapterInPlace(CompanionDbContext db, GameEntity game, GuideTabEntity existing, GuideTabEntity fresh, HashSet<string>? taken = null)
    {
        var others = game.Tabs.Where(t => t.Id != existing.Id).ToList();
        // The ids the old version defined; what is not defined by anything any more afterwards loses its tick
        var oldIds = ApiEndpoints.CollectItemIds([existing]);
        // A batch passes its own running set (the ids of everything else on the page, growing with every saved chapter)
        ApiEndpoints.MakeItemIdsUnique([fresh], taken ?? ApiEndpoints.CollectTakenItemIds(others));

        // Same entry, new id - or the entry came back as a row that points at another chapter ("ref"): hand the tick and the incoming refs over to
        // where the item lives now (the new row, or the ref's final target; ticked if either was) before anything is deleted
        var counterparts = GuideLinker.PlanIdTransfer(existing.SectionsJson, fresh.SectionsJson);
        var transfer = new Dictionary<string, string>(StringComparer.Ordinal);
        if (counterparts.Count > 0)
        {
            var after = others.Append(new GuideTabEntity { Id = existing.Id, Slug = existing.Slug, Title = fresh.Title, SectionsJson = fresh.SectionsJson }).ToList();
            foreach (var (oldId, counterpart) in counterparts)
            {
                // A ref that ends nowhere is cleared by the linker: the row then defines the item under its own (new) id
                var destination = counterpart.Ref is { } reference ? GuideLinker.FinalTarget(after, reference, existing.Id) ?? counterpart.NewId : counterpart.NewId;
                if (destination != oldId)
                {
                    transfer[oldId] = destination;
                }
            }
        }
        if (transfer.Count > 0)
        {
            var ticked = game.ProgressChecks.Select(p => p.ItemKey).ToHashSet(StringComparer.Ordinal);
            foreach (var (oldId, newId) in transfer)
            {
                if (ticked.Contains(oldId) && ticked.Add(newId))
                {
                    var check = new ProgressCheckEntity { Id = Guid.NewGuid(), GameId = game.Id, ItemKey = newId, CheckedAt = DateTimeOffset.UtcNow };
                    db.ProgressChecks.Add(check);
                    if (!game.ProgressChecks.Contains(check))
                    {
                        game.ProgressChecks.Add(check);
                    }
                }
            }
            foreach (var other in others)
            {
                if (GuideLinker.RemapRefs(other.SectionsJson, transfer) is { } remapped)
                {
                    other.SectionsJson = remapped;
                }
            }
        }

        existing.Title = fresh.Title;
        existing.Icon = fresh.Icon;
        existing.SectionsJson = fresh.SectionsJson;
        existing.GroupKey ??= fresh.GroupKey;
        existing.TopicId ??= fresh.TopicId;
        // Links first (they may need the ticks of ids that vanished: a row that pointed at one becomes a normal row and keeps the tick), deletions after
        GuideLinkMaintenance.Apply(db, game);
        oldIds.ExceptWith(ApiEndpoints.CollectItemIds(game.Tabs));
        var orphanedChecks = game.ProgressChecks.Where(p => oldIds.Contains(p.ItemKey)).ToList();
        if (orphanedChecks.Count > 0)
        {
            db.ProgressChecks.RemoveRange(orphanedChecks);
            foreach (var check in orphanedChecks)
            {
                game.ProgressChecks.Remove(check);
            }
        }
    }

    private static async Task<object> RunLoreAsync(IServiceProvider sp, string slug, string? providerOverride, AiJobProgress progress, CancellationToken ct)
    {
        var db = sp.GetRequiredService<CompanionDbContext>();
        var ai = sp.GetRequiredService<AiGuideGeneratorService>();
        var translations = sp.GetRequiredService<GuideTranslationService>();
        var logger = sp.GetRequiredService<ILogger<AiGuideGeneratorService>>();

        var game = await db.Games
            .Include(g => g.Tabs)
            .FirstOrDefaultAsync(g => g.Slug == slug, ct)
            ?? throw new LocalizedException(ServerTexts.GameNotFound(slug));

        ApiEndpoints.AdoptRequestLanguageIfEmpty(game);

        var (researched, usage) = await ai.ResearchLoreTabAsync(game, providerOverride, progress, ct);

        progress.Step("saving");
        // Item ids must be unique per game (checklist rows of the other tabs count); the old lore tab's ids are replaced
        ApiEndpoints.MakeItemIdsUnique([researched], ApiEndpoints.CollectTakenItemIds(game.Tabs.Where(t => t.Slug != LoreTab.Slug)));

        var existing = game.Tabs.FirstOrDefault(t => t.Slug == LoreTab.Slug);
        if (existing is null)
        {
            db.GuideTabs.Add(researched);
        }
        else
        {
            existing.Title = researched.Title;
            existing.Icon = researched.Icon;
            existing.SortOrder = researched.SortOrder;
            existing.SectionsJson = researched.SectionsJson;
        }
        game.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Story & Lore für {Title} recherchiert ({Replaced}).", game.Title, existing is null ? "neu" : "ersetzt");

        // Reload so the answer is built from what is stored (not from tracked navigation fix-ups)
        db.ChangeTracker.Clear();
        var fresh = await db.Games
            .AsNoTracking()
            .Include(g => g.Tabs.OrderBy(t => t.SortOrder))
            .Include(g => g.ProgressChecks)
            .FirstAsync(g => g.Slug == slug, ct);
        return new AiResultDto<GameDetailDto>(await ApiEndpoints.BuildDetailAsync(db, translations, fresh, null, ct), usage.ToDto());
    }
}

/// <summary>
/// Saves what a long research produces while it still runs: the tile of a new guide, then every finished chapter - each through its own database
/// context (chapters finish in parallel), one at a time. Item ids are made unique per game as they arrive.
/// </summary>
internal sealed class ChapterSink(IServiceScopeFactory scopes, HashSet<string> takenItemIds) : IAiChapterSink
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task GameReadyAsync(GameEntity game, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        game.Slug = await ApiEndpoints.EnsureUniqueSlugAsync(db, game.Slug, ct);
        db.Games.Add(game);
        await db.SaveChangesAsync(ct);
    }

    public async Task ChapterDoneAsync(GuideTabEntity tab, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ApiEndpoints.MakeItemIdsUnique([tab], takenItemIds);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            // The walkthrough has a fixed address, one per game: if one exists already (a second research of it, or one that finished earlier) the new one
            // is dropped instead of running into the unique index and failing the whole job
            if (tab.Slug == WalkthroughTab.Slug && await db.GuideTabs.AnyAsync(t => t.GameId == tab.GameId && t.Slug == WalkthroughTab.Slug, ct))
            {
                return;
            }
            db.GuideTabs.Add(tab);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// Puts the result of a repeated chapter research into the placeholder it belongs to (same tab, same place in the guide, same address): new
/// content, or - when it failed again - the placeholder with the new reason. One at a time, each through its own database context.
/// </summary>
internal sealed class ChapterReplacer(IServiceScopeFactory scopes, HashSet<string> takenItemIds)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ReplaceAsync(Guid tabId, GuideTabEntity result)
    {
        await _gate.WaitAsync();
        try
        {
            ApiEndpoints.MakeItemIdsUnique([result], takenItemIds);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var existing = await db.GuideTabs.FirstOrDefaultAsync(t => t.Id == tabId);
            if (existing is null)
            {
                return;
            }
            existing.Title = result.Title;
            existing.Icon = result.Icon;
            existing.SectionsJson = result.SectionsJson;
            // A group the user (or an earlier research) chose stays; a chapter without one gets the group of its topic
            existing.GroupKey ??= result.GroupKey;
            existing.TopicId ??= result.TopicId;
            var game = await db.Games.FirstOrDefaultAsync(g => g.Id == existing.GameId);
            if (game is not null)
            {
                game.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The research now says the game does not have this chapter's topic: the placeholder is removed (same rule as "Kapitel entfernen" - only a tab
    /// that still is a placeholder, never one with real content).
    /// </summary>
    public async Task RemoveAsync(Guid tabId)
    {
        await _gate.WaitAsync();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var existing = await db.GuideTabs.FirstOrDefaultAsync(t => t.Id == tabId);
            if (existing is null || existing.Slug == LoreTab.Slug || FailedChapters.Read(existing.SectionsJson) is null)
            {
                return;
            }
            db.GuideTabs.Remove(existing);
            var game = await db.Games.FirstOrDefaultAsync(g => g.Id == existing.GameId);
            if (game is not null)
            {
                game.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>The status of a job as the UI reads it. Error and the notice are texts in the language of the asking request.</summary>
public static class AiJobMapping
{
    public static AiJobDto ToDto(this AiJobSnapshot s) => new(
        s.Id,
        s.Kind,
        s.Slug,
        s.Running,
        s.ElapsedSeconds,
        s.ElapsedMilliseconds,
        s.StepIdleSeconds,
        new AiJobStepDto(s.Step.Stage, s.Step.Done, s.Step.Total, s.Step.Title),
        // "Gemini quota used up - continuing with Claude.": while the job runs and after it (also when it failed on the next AI as well)
        s.Usage is { Switches.Count: > 0 } usage ? AiUsageMapping.NoticeOf(usage.Switches[^1], usage.Research) : null,
        s.Error?.ToString(),
        s.ProviderMessage,
        s.AiProvider,
        s.AiFailure,
        s.Error is null ? s.Result : null);
}
