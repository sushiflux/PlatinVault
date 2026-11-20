using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// The review of ONE guide chapter by a second AI (brief <c>ki-pruefung.md</c>): answer A is the chapter as it is stored, answer B a fresh research of the same
/// chapter by another AI (built exactly like "redo", so entries both answers have come back with the same item id) - or an answer pasted from the player's own
/// chat - and the reviewer (a provider WITH web search) gets both in full and returns the merged chapter plus findings. The code guards the result
/// (<see cref="ChapterReview.Merge"/>). Both runs use a fixed provider (<see cref="AiRouter.BeginFixedAsync"/>): nothing switches by itself, because the point is
/// who answers. Nothing here saves: the callers (the job, the paste endpoints) store the chapter and the review in one transaction.
/// Built so "Alle Kapitel prüfen" / the "gründlich" choice at guide creation can call it chapter by chapter.
/// </summary>
public sealed partial class AiGuideGeneratorService
{
    /// <summary>What a review produced: the merged chapter (markers included), what the guard found, A and B as they were, and who did what.</summary>
    internal sealed record ChapterReviewOutcome(
        GuideTabEntity Merged,
        ReviewMerge Merge,
        string ASectionsJson,
        string BSectionsJson,
        string ResearcherProvider,
        string ReviewerProvider,
        AiUsage? ResearcherUsage,
        AiUsage? ReviewerUsage,
        List<ManualIssueDto> Warnings);

    /// <summary>The label stored for an answer B (or a review) that was pasted from the player's own chat.</summary>
    internal const string ReviewManualProvider = "manual";

    /// <summary>
    /// Answer B through an API: the chapter researched again like "redo", by exactly this provider (no fallback to another one). A failure of the provider
    /// is reported as the failure of the second research, with the provider's complete message kept for the technical details.
    /// </summary>
    internal async Task<(GuideTabEntity Tab, AiUsage Usage)> ResearchSecondAnswerAsync(GameEntity game, GuideTabEntity tab, string researcher, AiJobProgress? progress, CancellationToken ct)
    {
        var run = await router.BeginFixedAsync(researcher, research: true, ct);
        try
        {
            return await RedoChapterWithRunAsync(game, tab, run, progress, ct);
        }
        catch (AiProviderException ex)
        {
            throw NamedFailure(ex, secondResearch: true, researcher);
        }
    }

    /// <summary>"The second research with Claude failed: ..." - keeps the provider, the failure kind and the provider's own text of the original failure.</summary>
    private static AiProviderException NamedFailure(AiProviderException ex, bool secondResearch, string provider) =>
        new(Msg.Join(" ", ServerTexts.Review.StepFailed(secondResearch, ex.Provider ?? provider), ex.Text),
            ex.ProviderMessage, ex.Status, ex, ex.Provider ?? provider, ex.RetryAfter, ex.Kind, ex.IsTruncated);

    /// <summary>
    /// B first (API), then the review: the whole per-chapter review of the API way. <paramref name="progress"/> reports <c>facts</c> and <c>chapter</c> (B), then
    /// <c>review</c>. Throws with a complete localized reason when something fails; changes nothing.
    /// </summary>
    internal async Task<ChapterReviewOutcome> ReviewChapterWithSecondResearchAsync(GameEntity game, GuideTabEntity tab, string researcher, string reviewer, AiJobProgress? progress, CancellationToken ct)
    {
        var (second, researcherUsage) = await ResearchSecondAnswerAsync(game, tab, researcher, progress, ct);
        return await ReviewChapterAsync(game, tab, second, AiProvider.Normalize(researcher) ?? researcher, reviewer, researcherUsage, progress, ct);
    }

    /// <summary>
    /// The review call: both chapters go to the reviewer (a fixed provider with web search, author-blind prompt), the answer is read, the chapter built and
    /// guarded. <paramref name="researcherLabel"/> is only recorded ("gemini", ... or <see cref="ReviewManualProvider"/>).
    /// </summary>
    internal async Task<ChapterReviewOutcome> ReviewChapterAsync(
        GameEntity game, GuideTabEntity tab, GuideTabEntity second, string researcherLabel, string reviewer, AiUsage? researcherUsage, AiJobProgress? progress, CancellationToken ct)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var settings = await settingsStore.GetAsync(ct);
        var run = await router.BeginFixedAsync(reviewer, research: true, ct);
        if (!AiProvider.HasWebSearch(run.Primary))
        {
            throw new LocalizedException(ServerTexts.Review.ReviewerNoWebSearch(run.Primary));
        }
        progress?.Step("review", 0, 1, tab.Title);

        // The chapter points at the rows of the OTHER chapters ("ref"); those are the link candidates of the prompt
        var context = WithLinkCandidates(await BuildGameContextAsync(game, language, ct), game.Tabs, new HashSet<Guid> { tab.Id });
        var a = ChapterReview.CleanSections(tab.SectionsJson);
        var b = ChapterReview.ParseSections(second.SectionsJson);
        var prompt = BuildReviewPrompt(context, tab.Title, ChapterReview.ChapterJson(tab.Title, tab.Icon, a), ChapterReview.ChapterJson(tab.Title, tab.Icon, b));

        AiAnswer answer;
        try
        {
            // The same call as every content call: one compact retry when the answer is cut off, then a complete message (AnswerTooLong)
            answer = await CallContentAnswerAsync(run, run.Primary, prompt, settings, language, "review: " + tab.Title, ct);
        }
        catch (AiProviderException ex)
        {
            throw NamedFailure(ex, secondResearch: false, run.Primary);
        }

        var errors = new List<ManualIssueDto>();
        var warnings = new List<ManualIssueDto>();
        if (!TryMergeReviewAnswer(game, tab, a, b, answer.Text, answer.Sources, errors, warnings, out var merged, out var merge) || merged is null || merge is null)
        {
            logger.LogWarning("Prüfung von '{Chapter}': Antwort von {Provider} nicht verwendbar: {Reasons}", tab.Title, run.Primary, string.Join(" | ", errors.Select(e => e.Message)));
            throw new AiProviderException(ServerTexts.Review.AnswerNotUsable(run.Primary, string.Join(" ", errors.Select(e => e.Message))), null, 0, null, run.Primary, kind: AiFailureKind.Other);
        }
        return new ChapterReviewOutcome(merged, merge, ChapterReview.SerializeSections(a), ChapterReview.SerializeSections(b),
            researcherLabel, run.Primary, researcherUsage, run.Usage(), warnings);
    }

    /// <summary>
    /// Reads a review answer (an API answer or one pasted from a chat: the first JSON object, also inside a code fence or after a sentence), builds the
    /// merged chapter through the same normalizer and checks as every AI chapter and guards it against A and B. false with complete sentences in
    /// <paramref name="errors"/> when the answer cannot be used; <paramref name="warnings"/> name what was dropped or put back and findings without a source.
    /// </summary>
    internal bool TryMergeReviewAnswer(
        GameEntity game, GuideTabEntity tab, JsonArray a, JsonArray b, string? answerText, AiSourceMeta? sources,
        List<ManualIssueDto> errors, List<ManualIssueDto> warnings, out GuideTabEntity? merged, out ReviewMerge? merge)
    {
        merged = null;
        merge = null;
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);

        if (!ManualAnswers.TryRead(answerText, errors, out var root) || root is null)
        {
            return false;
        }
        ManualAnswers.CheckGame(root, game.SteamAppId, game.Title, errors, warnings);
        if (errors.Count > 0)
        {
            return false;
        }
        var parts = ChapterReview.ReadAnswer(root, errors);
        if (parts is null)
        {
            return false;
        }

        GuideTabEntity built;
        try
        {
            built = BuildTabFromChapterJson(game.Id, tab.SortOrder, RedoPlan(tab, language), parts.ChapterJson, new NormalizeOptions(AchievementApiNames(game)), sources);
        }
        catch (LocalizedException ex)
        {
            errors.Add(ManualAnswers.Issue("chapterUnusable", ex.Text));
            return false;
        }
        var sections = ChapterReview.ParseSections(built.SectionsJson);
        if (sections.Count == 0)
        {
            errors.Add(ManualAnswers.Issue("nothingReadable", ServerTexts.Review.AnswerNoSections));
            return false;
        }

        var result = ChapterReview.Merge(sections, a, b, parts.Findings, language);
        // The reviewer never renames the chapter
        built.Title = tab.Title;
        built.SectionsJson = result.Sections.ToJsonString();

        if (parts.Findings.Dropped > 0)
        {
            logger.LogInformation("Prüfung von '{Chapter}': {Count} Befund(e) ohne bekannte Art oder ohne Begründung verworfen.", tab.Title, parts.Findings.Dropped);
            warnings.Add(ManualAnswers.Issue("findingsDropped", ServerTexts.Review.WarnFindingsDropped(parts.Findings.Dropped)));
        }
        if (parts.Findings.WithoutSource > 0)
        {
            warnings.Add(ManualAnswers.Issue("findingsWithoutSource", ServerTexts.Review.WarnFindingsWithoutSource(parts.Findings.WithoutSource)));
        }
        if (result.PutBack > 0)
        {
            logger.LogInformation("Prüfung von '{Chapter}': {Count} Eintrag/Einträge ohne Begründung weggelassen und wieder eingefügt.", tab.Title, result.PutBack);
            warnings.Add(ManualAnswers.Issue("entriesPutBack", ServerTexts.Review.WarnEntriesPutBack(result.PutBack)));
        }
        merged = built;
        merge = result;
        return true;
    }

    /// <summary>
    /// Answer B pasted from the player's own chat (the answer to the existing "research chapter again" prompt with the chapter's id and "replace"): checked
    /// like any manual chapter answer and built like an AI chapter. Null tab = not usable, the check says why. An answer "this topic does not exist" is no
    /// second answer.
    /// </summary>
    internal (GuideTabEntity? Tab, ManualChapterCheckDto Dto) PrepareSecondFromChat(GameEntity game, GuideTabEntity tab, string? text)
    {
        var checkedAnswer = CheckManualChapter(game, text, tab.Title, tab.TopicId);
        if (checkedAnswer.CanonicalJson is null || !checkedAnswer.Dto.Ok)
        {
            return (null, checkedAnswer.Dto);
        }
        if (checkedAnswer.Dto.NotApplicable is not null)
        {
            var errors = new List<ManualIssueDto> { ManualAnswers.Issue("secondNotApplicable", ServerTexts.Review.SecondSaysNotApplicable(tab.Title, checkedAnswer.Dto.NotApplicable)) };
            return (null, checkedAnswer.Dto with { Ok = false, Errors = errors });
        }
        var request = new ManualChapterAnswerRequest(null, tab.Title, tab.Icon, tab.Title, tab.TopicId, tab.Id, true);
        return (BuildManualChapterTab(game, tab.SortOrder, request, checkedAnswer.CanonicalJson), checkedAnswer.Dto);
    }

    /// <summary>The prompt to copy into the player's chat: the review prompt with the chapter as it is (A) and the second answer (B) inside, plus the chat's output contract.</summary>
    public async Task<ManualPromptDto> BuildManualReviewPromptAsync(GameEntity game, GuideTabEntity tab, JsonArray second, CancellationToken ct = default)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var context = WithLinkCandidates(await ManualContextAsync(game, language, ct), game.Tabs, new HashSet<Guid> { tab.Id });
        var a = ChapterReview.CleanSections(tab.SectionsJson);
        var prompt = BuildManualReviewPrompt(context, tab.Title, ChapterReview.ChapterJson(tab.Title, tab.Icon, a), ChapterReview.ChapterJson(tab.Title, tab.Icon, second));
        return new ManualPromptDto("review", prompt, ManualFileName(game, (language == Lang.English ? "review-" : "pruefung-") + Slugify(tab.Title)), language, game.SteamAppId, game.Title);
    }
}
