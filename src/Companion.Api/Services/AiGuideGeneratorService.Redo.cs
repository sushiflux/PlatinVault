using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// "Kapitel neu recherchieren" for ANY chapter (not only a failed placeholder): researches it again with the current rules and AI selection. The result
/// replaces the chapter only when the research succeeded - a failure, a walkthrough with failed parts or an answer "does not exist in this game" leave
/// the chapter as it is and end the job with the reason.
/// </summary>
public sealed partial class AiGuideGeneratorService
{
    /// <summary>The plan a chapter is researched again with: its stored assignment (failed placeholder), else its title and current headings.</summary>
    internal static ChapterPlan RedoPlan(GuideTabEntity tab, string language)
    {
        if (FailedChapters.Read(tab.SectionsJson) is { } failure)
        {
            return new ChapterPlan(tab.Title, tab.Icon, failure.Focus, WalkthroughTab.Is(tab) ? WalkthroughTab.TopicId : failure.TopicId ?? "extra");
        }
        var headings = ApiEndpoints.DeserializeSections(tab.SectionsJson)
            .Where(s => s.Type is "heading" or "checklist_table" or "cards_grid" or "boss" or "puzzle")
            .Select(s => s.Type == "checklist_table" ? s.Table?.Caption : s.Title)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        // The stored topic (chapters researched since the column exists) keeps its own rules, e.g. "never dropped" for the roadmap
        var topic = WalkthroughTab.Is(tab) ? WalkthroughTab.TopicId : tab.TopicId ?? "extra";
        return new ChapterPlan(tab.Title, tab.Icon, RedoFocus(tab.Title, headings, language), topic);
    }

    /// <summary>
    /// Researches one existing chapter again and returns the new tab (same game, same sort order; the caller copies it into the existing tab). Throws with a
    /// localized reason when nothing usable came back - the caller then changes nothing.
    /// </summary>
    public async Task<(GuideTabEntity Tab, AiUsage Usage)> RedoChapterAsync(GameEntity game, GuideTabEntity tab, string? providerOverride, AiJobProgress? progress, CancellationToken ct = default)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var settings = await settingsStore.GetAsync(ct);
        var run = await router.BeginAsync(research: true, providerOverride, background: false, ct);
        progress?.Step("facts");
        // The chapter may point at the rows of the OTHER chapters ("ref"); its own rows are researched anew
        // ... and keeps the item ids of the rows it has today for the same entries (the ticks hang on them)
        var context = WithKeepItems(WithLinkCandidates(await BuildGameContextAsync(game.Title, game.SteamAppId, language, ct), game.Tabs, new HashSet<Guid> { tab.Id }), tab);
        var plan = RedoPlan(tab, language);
        progress?.Step("chapter", 0, 1, plan.Title);

        if (plan.TopicId == WalkthroughTab.TopicId)
        {
            using var gate = new SemaphoreSlim(MaxParallelChapters);
            var others = game.Tabs.Where(t => !WalkthroughTab.Is(t) && t.Slug != LoreTab.Slug).OrderBy(t => t.SortOrder).Select(t => t.Title).ToList();
            // An existing walkthrough is only replaced when EVERY part worked (a failed placeholder takes whatever came back)
            var requireAll = FailedChapters.Read(tab.SectionsJson) is null;
            var walkthrough = await ResearchWalkthroughAsync(game.Id, tab.SortOrder, plan, context, UnknownSource(language), others, run, settings, gate,
                title => progress?.Step("chapter", 0, 1, title), ct, requireAll);
            return (walkthrough, run.Usage());
        }

        var answer = await CallContentAnswerAsync(run, run.Primary, BuildChapterPrompt(context, plan.Title, plan.Focus, UnknownSource(language), plan.TopicId), settings, language, plan.Title, ct);
        try
        {
            return (BuildTabFromChapterJson(game.Id, tab.SortOrder, plan, answer.Text, OptionsFor(context), answer.Sources), run.Usage());
        }
        catch (ChapterNotApplicableException dropped)
        {
            logger.LogInformation("Kapitel '{Chapter}' bleibt unverändert: laut neuer Recherche gibt es das in „{Game}“ nicht – {Reason}", plan.Title, game.Title, dropped.Reason);
            throw new LocalizedException(ServerTexts.Ai.RedoNotApplicable(plan.Title, dropped.Reason), dropped);
        }
    }
}
