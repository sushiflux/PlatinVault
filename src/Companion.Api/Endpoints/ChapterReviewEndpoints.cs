using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Endpoints;

/// <summary>
/// The AI review of one guide chapter ("Prüf-KI"). Answer A = the chapter as it is stored; answer B = the same
/// chapter researched again by another AI or pasted from the player's own chat; the reviewer (a provider with web search, or the player's chat) decides and
/// returns the merged chapter plus findings. The merged chapter replaces the chapter in place (ticks, refs and links keep working, see
/// <see cref="AiActionEndpoints.ReplaceChapterInPlace"/>) and the review is stored in the same save.
/// The four combinations: B by API + review by API = <c>POST .../review {researcher, reviewer}</c> (one job: B, review, save); B from a chat + review by API =
/// <c>POST .../review {secondAnswer, reviewer}</c>; B by API + review by chat = <c>POST .../review/second {researcher}</c> (job: B only) then
/// <c>manual-answer</c>; B from a chat + review by chat = <c>manual-prompt</c> then <c>manual-answer</c>. The server keeps nothing between the steps: the
/// client sends the second answer back (<c>secondSections</c>) with the <c>baseHash</c> of the chapter it belongs to, and a changed chapter is a 409.
/// </summary>
public static class ChapterReviewEndpoints
{
    /// <summary>Job kind of every review job; the target tells the full review (<c>{tabId}</c>) from the second-answer-only job (<c>{tabId}:second</c>).</summary>
    private const string JobKind = "review";

    public static void MapChapterReviewEndpoints(this RouteGroupBuilder api)
    {
        // Start: B (API research or pasted answer), review by an API provider, save. 202 with the job; the result is ChapterReviewResultDto
        api.MapPost("/games/{slug}/chapters/{tabId:guid}/review", async (string slug, Guid tabId, ChapterReviewRequest? req, CompanionDbContext db, AiRouter router, AiGuideGeneratorService ai, AiJobService jobs, CancellationToken ct) =>
        {
            var loaded = await LoadAsync(db, slug, tabId, ct);
            if (loaded.Error is not null)
            {
                return loaded.Error;
            }
            var (game, tab) = (loaded.Game!, loaded.Tab!);
            if (req?.Reviewer is null)
            {
                return Problems.BadRequest(ServerTexts.Review.ChoiceTitle, ServerTexts.Review.ReviewerMissing);
            }
            var hasChat = !string.IsNullOrWhiteSpace(req.SecondAnswer);
            if (req.Researcher is null && !hasChat)
            {
                return Problems.BadRequest(ServerTexts.Review.ChoiceTitle, ServerTexts.Review.SecondMissing);
            }
            if (req.Researcher is not null && hasChat)
            {
                return Problems.BadRequest(ServerTexts.Review.ChoiceTitle, ServerTexts.Review.SecondBoth);
            }
            if (ValidateProviders(router, req.Researcher, req.Reviewer, out var researcher, out var reviewer) is { } refused)
            {
                return refused;
            }
            // A pasted second answer is judged right away (complete sentences, nothing is started with an unusable answer)
            if (hasChat)
            {
                var (second, check) = ai.PrepareSecondFromChat(game, tab, req.SecondAnswer);
                if (second is null)
                {
                    return Problems.BadRequestWithIssues(ServerTexts.Review.ChoiceTitle, ServerTexts.Review.SecondNotUsable(string.Join(" ", check.Errors.Select(e => e.Message))), check.Errors);
                }
            }

            var secondAnswer = hasChat ? req.SecondAnswer : null;
            var job = jobs.Start(JobKind, "game:" + slug, slug, (sp, progress, token) => RunReviewAsync(sp, slug, tabId, researcher, reviewer, secondAnswer, progress, token), tabId.ToString(), tab.Title);
            return Accepted(job, tabId.ToString());
        });

        // The stored review of the chapter (findings, counts, who, when, stale)
        api.MapGet("/games/{slug}/chapters/{tabId:guid}/review", async (string slug, Guid tabId, CompanionDbContext db, CancellationToken ct) =>
        {
            var gameId = await db.Games.AsNoTracking().Where(g => g.Slug == slug).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct);
            if (gameId is null)
            {
                return Problems.GameNotFound(slug);
            }
            var tab = await db.GuideTabs.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tabId && t.GameId == gameId, ct);
            if (tab is null)
            {
                return Problems.NotFound(ServerTexts.Ai.ChapterNotFoundTitle, ServerTexts.Ai.ChapterNotFound);
            }
            var row = await db.ChapterReviews.AsNoTracking().FirstOrDefaultAsync(r => r.TabId == tabId, ct);
            return row is null
                ? Problems.NotFound(ServerTexts.Review.NoReviewTitle, ServerTexts.Review.NoReview)
                : Results.Ok(ToDto(row, tab.SectionsJson));
        });

        // "Andere Fassung nehmen" (C6): the stored version A or B replaces the entry a finding is about - no AI call. "result" = accept the reviewer's decision.
        api.MapPost("/games/{slug}/chapters/{tabId:guid}/review/findings/{index:int}/choose", async (string slug, Guid tabId, int index, ChapterReviewChooseRequest? req, CompanionDbContext db, GuideTranslationService translations, CancellationToken ct) =>
        {
            var version = req?.Version?.Trim().ToLowerInvariant();
            if (version is not ("a" or "b" or "result"))
            {
                return Problems.BadRequest(ServerTexts.Review.ChooseTitle, ServerTexts.Review.VersionUnknown);
            }
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
            var row = await db.ChapterReviews.FirstOrDefaultAsync(r => r.TabId == tabId, ct);
            if (row is null)
            {
                return Problems.NotFound(ServerTexts.Review.NoReviewTitle, ServerTexts.Review.NoReview);
            }
            var findings = ChapterReview.DeserializeFindings(row.FindingsJson);
            if (index < 0 || index >= findings.Count)
            {
                return Problems.NotFound(ServerTexts.Review.FindingNotFoundTitle, ServerTexts.Review.FindingNotFound(index, findings.Count));
            }
            if (ChapterReview.IsStale(row.ResultHash, tab.SectionsJson))
            {
                return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Review.ChooseTitle, ServerTexts.Review.StaleChoose);
            }

            var finding = findings[index];
            var sections = ChapterReview.ParseSections(tab.SectionsJson);
            if (version == "result")
            {
                // Nothing in the chapter changes; the point counts as decided. After a/b was taken the reviewer's version is gone (it is not stored).
                if (finding.Applied is not null && finding.Applied != "result")
                {
                    return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Review.ChooseTitle, ServerTexts.Review.ResultNotAvailable);
                }
            }
            else
            {
                if (!finding.Target.IsStructured)
                {
                    return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Review.ChooseTitle, ServerTexts.Review.NotStructured);
                }
                var source = ChapterReview.ParseSections(version == "a" ? row.OriginalSectionsJson : row.SecondSectionsJson);
                if (ChapterReview.ApplyVersion(sections, finding, source) != VersionOutcome.Applied)
                {
                    return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Review.ChooseTitle, ServerTexts.Review.VersionNotFound);
                }
            }
            findings[index] = finding with { Applied = version };
            // The markers always follow the findings: a decided point loses its marker, the box at the top lists what is still open
            ChapterReview.ApplyMarkers(sections, findings, row.Language);

            var fresh = new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = tab.Slug, Title = tab.Title, Icon = tab.Icon, SortOrder = tab.SortOrder, GroupKey = tab.GroupKey, TopicId = tab.TopicId, SectionsJson = sections.ToJsonString() };
            AiActionEndpoints.ReplaceChapterInPlace(db, game, tab, fresh);
            game.UpdatedAt = DateTimeOffset.UtcNow;
            row.FindingsJson = ChapterReview.SerializeFindings(findings);
            row.ResultHash = ChapterReview.HashSections(tab.SectionsJson);
            await GuideLinkMaintenance.SaveAsync(db, ct);

            var review = ToDto(row, tab.SectionsJson);
            db.ChangeTracker.Clear();
            var reloaded = await db.Games.AsNoTracking().Include(g => g.Tabs.OrderBy(t => t.SortOrder)).Include(g => g.ProgressChecks).FirstAsync(g => g.Slug == slug, ct);
            return Results.Ok(new ChapterReviewChosenDto(await ApiEndpoints.BuildDetailAsync(db, translations, reloaded, null, ct), review));
        });

        // B by API, review by the player's chat: job that only researches the second answer; the result is ChapterReviewPreparedDto (sections + prompt)
        api.MapPost("/games/{slug}/chapters/{tabId:guid}/review/second", async (string slug, Guid tabId, ChapterReviewSecondRequest? req, CompanionDbContext db, AiRouter router, AiJobService jobs, CancellationToken ct) =>
        {
            var loaded = await LoadAsync(db, slug, tabId, ct);
            if (loaded.Error is not null)
            {
                return loaded.Error;
            }
            if (req?.Researcher is null)
            {
                return Problems.BadRequest(ServerTexts.Review.ChoiceTitle, ServerTexts.Review.SecondMissing);
            }
            var normalized = AiProvider.Normalize(req.Researcher);
            if (normalized is null || !router.HasKey(normalized))
            {
                return Problems.BadRequest(ServerTexts.Review.ChoiceTitle, normalized is null ? ServerTexts.Ai.UnknownProvider : ServerTexts.Ai.NoKey(normalized));
            }
            var job = jobs.Start(JobKind, "game:" + slug, slug, (sp, progress, token) => RunSecondAsync(sp, slug, tabId, normalized, progress, token), tabId + ":second", loaded.Tab!.Title);
            return Accepted(job, tabId + ":second");
        });

        // B pasted from a chat, review by the player's chat: checks B and builds the review prompt (sync, nothing is stored)
        api.MapPost("/games/{slug}/chapters/{tabId:guid}/review/manual-prompt", async (string slug, Guid tabId, ChapterReviewPromptRequest? req, CompanionDbContext db, AiGuideGeneratorService ai, CancellationToken ct) =>
        {
            var loaded = await LoadAsync(db, slug, tabId, ct);
            if (loaded.Error is not null)
            {
                return loaded.Error;
            }
            var (game, tab) = (loaded.Game!, loaded.Tab!);
            var (second, check) = ai.PrepareSecondFromChat(game, tab, req?.SecondAnswer);
            if (second is null)
            {
                return Problems.BadRequestWithIssues(ServerTexts.Review.ChoiceTitle, ServerTexts.Review.SecondNotUsable(string.Join(" ", check.Errors.Select(e => e.Message))), check.Errors);
            }
            return Results.Ok(await PrepareAsync(ai, game, tab, second, null, ct));
        });

        // The pasted review answer, checked live (nothing is saved)
        api.MapPost("/games/{slug}/chapters/{tabId:guid}/review/manual-check", async (string slug, Guid tabId, ChapterReviewAnswerRequest? req, CompanionDbContext db, AiGuideGeneratorService ai, CancellationToken ct) =>
        {
            var loaded = await LoadAsync(db, slug, tabId, ct);
            if (loaded.Error is not null)
            {
                return loaded.Error;
            }
            var evaluated = Evaluate(ai, loaded.Game!, loaded.Tab!, req);
            if (evaluated.Refusal is not null)
            {
                return evaluated.Refusal;
            }
            var preview = evaluated.Merge is null ? null
                : new ChapterReviewPreviewDto(evaluated.Merge.Counts, evaluated.Merge.Findings, evaluated.Merge.Sections.Count, evaluated.Merge.PutBack);
            return Results.Ok(new ChapterReviewCheckDto(evaluated.Errors.Count == 0 && preview is not null, evaluated.Errors, evaluated.Warnings, preview));
        });

        // Saves the pasted review answer: the merged chapter replaces the chapter in place, the review is stored (one save)
        api.MapPost("/games/{slug}/chapters/{tabId:guid}/review/manual-answer", async (string slug, Guid tabId, ChapterReviewAnswerRequest? req, CompanionDbContext db, AiGuideGeneratorService ai, GuideTranslationService translations, CancellationToken ct) =>
        {
            var loaded = await LoadAsync(db, slug, tabId, ct);
            if (loaded.Error is not null)
            {
                return loaded.Error;
            }
            var (game, tab) = (loaded.Game!, loaded.Tab!);
            var evaluated = Evaluate(ai, game, tab, req);
            if (evaluated.Refusal is not null)
            {
                return evaluated.Refusal;
            }
            if (evaluated.Errors.Count > 0 || evaluated.Merged is null || evaluated.Merge is null)
            {
                return Problems.BadRequestWithIssues(ServerTexts.Review.PasteTitle, ServerTexts.Review.NotSaved(string.Join(" ", evaluated.Errors.Select(e => e.Message))), evaluated.Errors);
            }

            var researcher = AiProvider.Normalize(req!.Researcher) ?? AiGuideGeneratorService.ReviewManualProvider;
            var outcome = new AiGuideGeneratorService.ChapterReviewOutcome(evaluated.Merged, evaluated.Merge, ChapterReview.SerializeSections(evaluated.A!), ChapterReview.SerializeSections(evaluated.B!),
                researcher, AiGuideGeneratorService.ReviewManualProvider, null, null, evaluated.Warnings);
            var saved = await SaveReviewAsync(db, translations, slug, game.Id, tab.Id, req.BaseHash!, outcome, AiGuideGeneratorService.GuideLanguage(game), ct);
            return Results.Ok(ResultOf(saved.Game, saved.Review, outcome));
        });
    }

    // ---- shared pieces -------------------------------------------------------------------------------------------------------------------

    private sealed record Loaded(GameEntity? Game, GuideTabEntity? Tab, IResult? Error);

    /// <summary>The game (chapters, ticks, and the trophy keys of a game added by hand) and the chapter; or the refusal: unknown game or chapter (404), a chapter that cannot be reviewed (409).</summary>
    private static async Task<Loaded> LoadAsync(CompanionDbContext db, string slug, Guid tabId, CancellationToken ct)
    {
        var game = await db.Games.Include(g => g.Tabs).Include(g => g.ProgressChecks).FirstOrDefaultAsync(g => g.Slug == slug, ct);
        if (game is null)
        {
            return new Loaded(null, null, Problems.GameNotFound(slug));
        }
        var tab = game.Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is null)
        {
            return new Loaded(null, null, Problems.NotFound(ServerTexts.Ai.ChapterNotFoundTitle, ServerTexts.Ai.ChapterNotFound));
        }
        if (NotReviewable(tab) is { } reason)
        {
            return new Loaded(null, null, Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Review.NotReviewableTitle, reason));
        }
        await ManualGuideEndpoints.LoadTrophiesAsync(db, game, ct);
        return new Loaded(game, tab, null);
    }

    /// <summary>Why a chapter cannot be reviewed (C7): the walkthrough (too large), the hidden Steam achievement list, Story &amp; Lore, a failed placeholder; null = it can.</summary>
    internal static Msg? NotReviewable(GuideTabEntity tab)
    {
        switch (ChapterRemoval.Check(tab))
        {
            case ChapterRemovalVerdict.AchievementList:
                return ServerTexts.Review.NotReviewableAchievementList;
            case ChapterRemovalVerdict.StoryLore:
                return ServerTexts.Review.NotReviewableLore;
        }
        if (WalkthroughTab.Is(tab))
        {
            return ServerTexts.Review.NotReviewableWalkthrough;
        }
        return FailedChapters.Read(tab.SectionsJson) is not null ? ServerTexts.Review.NotReviewableFailed : null;
    }

    /// <summary>
    /// The choice of the two AIs: the reviewer needs a key and web search; a researcher needs a key; they must be two different AIs (with one key only,
    /// the second answer has to come from the player's chat).
    /// </summary>
    internal static IResult? ValidateProviders(AiRouter router, string? researcherRaw, string reviewerRaw, out string? researcher, out string reviewer)
    {
        researcher = null;
        reviewer = string.Empty;
        var reviewerId = AiProvider.Normalize(reviewerRaw);
        if (reviewerId is null)
        {
            return Problems.BadRequest(ServerTexts.Review.ChoiceTitle, ServerTexts.Ai.UnknownProvider);
        }
        if (!router.HasKey(reviewerId))
        {
            return Problems.BadRequest(ServerTexts.Review.ChoiceTitle, ServerTexts.Ai.NoKey(reviewerId));
        }
        if (!AiProvider.HasWebSearch(reviewerId))
        {
            return Problems.BadRequest(ServerTexts.Review.ChoiceTitle, ServerTexts.Review.ReviewerNoWebSearch(reviewerId));
        }
        reviewer = reviewerId;
        if (researcherRaw is null)
        {
            return null;
        }
        var researcherId = AiProvider.Normalize(researcherRaw);
        if (researcherId is null)
        {
            return Problems.BadRequest(ServerTexts.Review.ChoiceTitle, ServerTexts.Ai.UnknownProvider);
        }
        if (!router.HasKey(researcherId))
        {
            return Problems.BadRequest(ServerTexts.Review.ChoiceTitle, ServerTexts.Ai.NoKey(researcherId));
        }
        if (researcherId == reviewerId)
        {
            return Problems.BadRequest(ServerTexts.Review.ChoiceTitle,
                router.ProvidersWithKey().Count < 2 ? ServerTexts.Review.NeedsTwoAis : ServerTexts.Review.SameProvider(researcherId));
        }
        researcher = researcherId;
        return null;
    }

    /// <summary>
    /// 202 with the job when it is the one that was asked for; 409 when the running job of this game is another action or the review of another chapter
    /// (the page must never report the result of a job it did not start).
    /// </summary>
    private static IResult Accepted(AiJob job, string target)
    {
        if (job.Kind != JobKind)
        {
            return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Ai.JobBusyTitle, ServerTexts.Ai.JobBusy(job.Kind));
        }
        if (!string.Equals(job.Target, target, StringComparison.Ordinal))
        {
            return Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Ai.JobBusyTitle, ServerTexts.Ai.JobBusyOtherTarget(job.Kind, job.TargetLabel));
        }
        return Results.Json(job.Snapshot().ToDto(), statusCode: StatusCodes.Status202Accepted);
    }

    internal static ChapterReviewDto ToDto(ChapterReviewEntity row, string? chapterSectionsJson) => new(
        row.TabId, row.CreatedAt, row.Language, row.ResearcherProvider, row.ReviewerProvider,
        ChapterReview.DeserializeCounts(row.CountsJson), ChapterReview.DeserializeFindings(row.FindingsJson), ChapterReview.IsStale(row.ResultHash, chapterSectionsJson));

    private static ChapterReviewResultDto ResultOf(GameDetailDto game, ChapterReviewDto review, AiGuideGeneratorService.ChapterReviewOutcome outcome)
    {
        var providers = new List<string>();
        foreach (var usage in new[] { outcome.ResearcherUsage, outcome.ReviewerUsage })
        {
            providers.AddRange((usage?.Providers ?? []).Where(p => !providers.Contains(p)));
        }
        // A chat is no provider: the manual way names it in the usage lines of the review itself (researcherProvider / reviewerProvider)
        return new ChapterReviewResultDto(game, new AiUsageDto(providers, []), review,
            new ChapterReviewUsageDto(outcome.ResearcherUsage?.ToDto(), (outcome.ReviewerUsage ?? AiUsage.None).ToDto()), outcome.Warnings);
    }

    // ---- the jobs -----------------------------------------------------------------------------------------------------------------------

    /// <summary>B (research or pasted) → review → save. Nothing changes before both succeeded: a failure throws and the chapter stays as it is.</summary>
    private static async Task<object> RunReviewAsync(IServiceProvider sp, string slug, Guid tabId, string? researcher, string reviewer, string? secondAnswer, AiJobProgress progress, CancellationToken ct)
    {
        var db = sp.GetRequiredService<CompanionDbContext>();
        var ai = sp.GetRequiredService<AiGuideGeneratorService>();
        var translations = sp.GetRequiredService<GuideTranslationService>();

        var game = await db.Games.Include(g => g.Tabs).Include(g => g.ProgressChecks).FirstOrDefaultAsync(g => g.Slug == slug, ct)
                   ?? throw new LocalizedException(ServerTexts.GameNotFound(slug));
        var tab = game.Tabs.FirstOrDefault(t => t.Id == tabId) ?? throw new LocalizedException(ServerTexts.Ai.ChapterNotFound);
        if (NotReviewable(tab) is { } reason)
        {
            throw new LocalizedException(reason);
        }
        await ManualGuideEndpoints.LoadTrophiesAsync(db, game, ct);
        // The chapter as it is NOW is answer A; when it is another one after the long work, nothing is saved
        var baseHash = ChapterReview.HashSections(tab.SectionsJson);

        AiGuideGeneratorService.ChapterReviewOutcome outcome;
        if (researcher is not null)
        {
            outcome = await ai.ReviewChapterWithSecondResearchAsync(game, tab, researcher, reviewer, progress, ct);
        }
        else
        {
            var (second, check) = ai.PrepareSecondFromChat(game, tab, secondAnswer);
            if (second is null)
            {
                throw new LocalizedException(ServerTexts.Review.SecondNotUsable(string.Join(" ", check.Errors.Select(e => e.Message))));
            }
            outcome = await ai.ReviewChapterAsync(game, tab, second, AiGuideGeneratorService.ReviewManualProvider, reviewer, null, progress, ct);
        }

        progress.Step("saving");
        var saved = await SaveReviewAsync(db, translations, slug, game.Id, tabId, baseHash, outcome, AiGuideGeneratorService.GuideLanguage(game), ct);
        return ResultOf(saved.Game, saved.Review, outcome);
    }

    /// <summary>Only the second answer (API research); the result carries its sections and the review prompt for the player's chat.</summary>
    private static async Task<object> RunSecondAsync(IServiceProvider sp, string slug, Guid tabId, string researcher, AiJobProgress progress, CancellationToken ct)
    {
        var db = sp.GetRequiredService<CompanionDbContext>();
        var ai = sp.GetRequiredService<AiGuideGeneratorService>();

        var game = await db.Games.Include(g => g.Tabs).Include(g => g.ProgressChecks).FirstOrDefaultAsync(g => g.Slug == slug, ct)
                   ?? throw new LocalizedException(ServerTexts.GameNotFound(slug));
        var tab = game.Tabs.FirstOrDefault(t => t.Id == tabId) ?? throw new LocalizedException(ServerTexts.Ai.ChapterNotFound);
        if (NotReviewable(tab) is { } reason)
        {
            throw new LocalizedException(reason);
        }
        await ManualGuideEndpoints.LoadTrophiesAsync(db, game, ct);
        var (second, usage) = await ai.ResearchSecondAnswerAsync(game, tab, researcher, progress, ct);
        return await PrepareAsync(ai, game, tab, second, usage, ct);
    }

    private static async Task<ChapterReviewPreparedDto> PrepareAsync(AiGuideGeneratorService ai, GameEntity game, GuideTabEntity tab, GuideTabEntity second, AiUsage? usage, CancellationToken ct)
    {
        var sections = ChapterReview.ParseSections(second.SectionsJson);
        var prompt = await ai.BuildManualReviewPromptAsync(game, tab, sections, ct);
        using var doc = JsonDocument.Parse(second.SectionsJson);
        return new ChapterReviewPreparedDto("second", doc.RootElement.Clone(), ChapterReview.HashSections(tab.SectionsJson), prompt, usage?.ToDto());
    }

    // ---- the pasted review answer --------------------------------------------------------------------------------------------------------

    private sealed record Evaluated(
        IResult? Refusal, List<ManualIssueDto> Errors, List<ManualIssueDto> Warnings, GuideTabEntity? Merged, ReviewMerge? Merge, JsonArray? A, JsonArray? B);

    /// <summary>Reads the request of manual-check / manual-answer: the chapter must be unchanged (baseHash), the second sections readable, the answer usable.</summary>
    private static Evaluated Evaluate(AiGuideGeneratorService ai, GameEntity game, GuideTabEntity tab, ChapterReviewAnswerRequest? req)
    {
        var errors = new List<ManualIssueDto>();
        var warnings = new List<ManualIssueDto>();
        Evaluated Refused(IResult refusal) => new(refusal, errors, warnings, null, null, null, null);

        if (string.IsNullOrWhiteSpace(req?.BaseHash))
        {
            return Refused(Problems.BadRequest(ServerTexts.Review.PasteTitle, ServerTexts.Review.BaseHashMissing));
        }
        if (!string.Equals(req.BaseHash, ChapterReview.HashSections(tab.SectionsJson), StringComparison.Ordinal))
        {
            return Refused(Problems.Create(StatusCodes.Status409Conflict, ServerTexts.Review.PasteTitle, ServerTexts.Review.ChapterChangedMeanwhile(tab.Title)));
        }

        JsonArray? second = null;
        if (req.SecondSections is { ValueKind: JsonValueKind.Array } element)
        {
            using (Lang.Use(AiGuideGeneratorService.GuideLanguage(game)))
            {
                // The client hands the sections back: they are normalized like every answer, never trusted as they come
                second = GuideSectionNormalizer.Normalize(element, new NormalizeOptions(AiGuideGeneratorService.AchievementApiNames(game)));
            }
        }
        if (second is null || second.Count == 0)
        {
            return Refused(Problems.BadRequest(ServerTexts.Review.PasteTitle, ServerTexts.Review.SecondSectionsInvalid));
        }
        // Markers have no place in a second answer; they would only mislead the reviewer
        ChapterReview.StripMarkers(second);

        var a = ChapterReview.CleanSections(tab.SectionsJson);
        ai.TryMergeReviewAnswer(game, tab, a, second, req.Answer, null, errors, warnings, out var merged, out var merge);
        return new Evaluated(null, errors, warnings, merged, merge, a, second);
    }

    // ---- saving --------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Stores a finished review: the merged chapter replaces the chapter in place (same tab, ticks of ids that still exist stay, moved ids hand their ticks
    /// over, refs and links are maintained - <see cref="AiActionEndpoints.ReplaceChapterInPlace"/>) and the review row (one per chapter) is written in the
    /// same SaveChanges. Runs on a FRESH copy of the game: the work took minutes. Refuses (nothing saved) when the chapter changed since the review started.
    /// </summary>
    internal static async Task<(GameDetailDto Game, ChapterReviewDto Review)> SaveReviewAsync(
        CompanionDbContext db, GuideTranslationService translations, string slug, Guid gameId, Guid tabId, string baseHash,
        AiGuideGeneratorService.ChapterReviewOutcome outcome, string language, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var current = await db.Games.Include(g => g.Tabs).Include(g => g.ProgressChecks).FirstOrDefaultAsync(g => g.Id == gameId, ct)
                      ?? throw new LocalizedException(ServerTexts.GameNotFound(slug));
        var target = current.Tabs.FirstOrDefault(t => t.Id == tabId)
                     ?? throw new LocalizedException(ServerTexts.Review.ChapterChangedMeanwhile(outcome.Merged.Title));
        if (NotReviewable(target) is not null || !string.Equals(ChapterReview.HashSections(target.SectionsJson), baseHash, StringComparison.Ordinal))
        {
            throw new LocalizedException(ServerTexts.Review.ChapterChangedMeanwhile(target.Title));
        }

        AiActionEndpoints.ReplaceChapterInPlace(db, current, target, outcome.Merged);
        var now = DateTimeOffset.UtcNow;
        current.UpdatedAt = now;

        var row = await db.ChapterReviews.FirstOrDefaultAsync(r => r.TabId == tabId, ct);
        if (row is null)
        {
            row = new ChapterReviewEntity { Id = Guid.NewGuid(), GameId = current.Id, TabId = tabId };
            db.ChapterReviews.Add(row);
        }
        row.CreatedAt = now;
        row.Language = language;
        row.ResearcherProvider = outcome.ResearcherProvider;
        row.ReviewerProvider = outcome.ReviewerProvider;
        row.OriginalSectionsJson = outcome.ASectionsJson;
        row.SecondSectionsJson = outcome.BSectionsJson;
        row.FindingsJson = ChapterReview.SerializeFindings(outcome.Merge.Findings);
        row.CountsJson = ChapterReview.SerializeCounts(outcome.Merge.Counts);
        // After the replace: the saved sections (ids made unique, links applied) are what the review has to match later
        row.ResultHash = ChapterReview.HashSections(target.SectionsJson);
        await GuideLinkMaintenance.SaveAsync(db, ct);

        var review = ToDto(row, target.SectionsJson);
        db.ChangeTracker.Clear();
        var reloaded = await db.Games.AsNoTracking().Include(g => g.Tabs.OrderBy(t => t.SortOrder)).Include(g => g.ProgressChecks).FirstAsync(g => g.Id == gameId, ct);
        return (await ApiEndpoints.BuildDetailAsync(db, translations, reloaded, null, ct), review);
    }
}
