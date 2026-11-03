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
            var job = jobs.Start("expand", "game:" + slug, slug, (sp, progress, token) => RunExpandAsync(sp, slug, req, progress, token));
            return Accepted(job, "expand");
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
            var job = jobs.Start("retry", "game:" + slug, slug, (sp, progress, token) => RunRetryAsync(sp, slug, req?.TabIds, req?.ProviderOverride, progress, token));
            return Accepted(job, "retry");
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
    private static IResult Accepted(AiJob job, string kind) => job.Kind == kind
        ? Results.Json(job.Snapshot().ToDto(), statusCode: StatusCodes.Status202Accepted)
        : Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Ai.JobBusyTitle, ServerTexts.Ai.JobBusy(job.Kind));

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

        var orphaned = ChapterRemoval.OrphanedCheckKeys(tab, game.Tabs.Where(t => t.Id != tab.Id));
        var orphanedChecks = game.ProgressChecks.Where(p => orphaned.Contains(p.ItemKey)).ToList();
        if (orphanedChecks.Count > 0)
        {
            db.ProgressChecks.RemoveRange(orphanedChecks);
            foreach (var check in orphanedChecks)
            {
                game.ProgressChecks.Remove(check);
            }
        }
        db.GuideTabs.Remove(tab);
        game.Tabs.Remove(tab);
        game.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

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
        var sink = new ChapterSink(sp.GetRequiredService<IServiceScopeFactory>(), ApiEndpoints.CollectItemIds(game.Tabs));
        var (newTabs, droppedTitles, usage) = await ai.CompleteGuideAsync(game, req?.ProviderOverride, nextSort, req?.Chapters, progress, sink, ct);

        if (newTabs.Count > 0)
        {
            progress.Step("saving");
            game.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        // Reload so the answer is built from what is stored (the chapters were saved through other contexts)
        db.ChangeTracker.Clear();
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

        ApiEndpoints.AdoptRequestLanguageIfEmpty(game);

        // New tabs go before the optional lore tab, which always stays last
        var regularTabs = game.Tabs.Where(t => t.Slug != LoreTab.Slug).ToList();
        var nextSort = (regularTabs.Count > 0 ? regularTabs.Max(t => t.SortOrder) : 0) + 1;
        var (newTab, usage) = await ai.ResearchAndBuildSingleTabAsync(game, req, nextSort, progress, ct);

        progress.Step("saving");
        ApiEndpoints.MakeItemIdsUnique([newTab], ApiEndpoints.CollectItemIds(game.Tabs));
        game.UpdatedAt = DateTimeOffset.UtcNow;

        db.GuideTabs.Add(newTab);
        await db.SaveChangesAsync(ct);

        // EF relationship fixup already added the tracked tab to game.Tabs on db.GuideTabs.Add
        if (!game.Tabs.Contains(newTab))
        {
            game.Tabs.Add(newTab);
        }
        game.Tabs = game.Tabs.OrderBy(t => t.SortOrder).ToList();
        return new AiResultDto<GameDetailDto>(await ApiEndpoints.BuildDetailAsync(db, translations, game, null, ct), usage.ToDto());
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
        var replacer = new ChapterReplacer(sp.GetRequiredService<IServiceScopeFactory>(), ApiEndpoints.CollectItemIds(game.Tabs));
        var (succeeded, failedTitles, droppedTitles, usage) = await ai.RetryChaptersAsync(game, failed, providerOverride, progress, replacer.ReplaceAsync, ct, replacer.RemoveAsync);

        db.ChangeTracker.Clear();
        var fresh = await db.Games
            .AsNoTracking()
            .Include(g => g.Tabs.OrderBy(t => t.SortOrder))
            .Include(g => g.ProgressChecks)
            .FirstAsync(g => g.Slug == slug, ct);
        return new AiRetryChaptersResponse(failed.Count, succeeded, failedTitles, await ApiEndpoints.BuildDetailAsync(db, translations, fresh, null, ct), usage.ToDto(), droppedTitles);
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
        ApiEndpoints.MakeItemIdsUnique([researched], ApiEndpoints.CollectItemIds(game.Tabs.Where(t => t.Slug != LoreTab.Slug)));

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
