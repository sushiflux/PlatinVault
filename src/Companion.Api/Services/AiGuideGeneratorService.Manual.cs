using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Settings;

namespace Companion.Api.Services;

/// <summary>
/// The guide via the user's own AI subscription: the prompts are made from the SAME building blocks as the real AI calls (Steam facts incl. the
/// AppID, the research blocks, the guide language, the budget of the scope), only the answer is not fetched but pasted back by the user. Nothing in
/// this file talks to an AI provider.
/// </summary>
public sealed partial class AiGuideGeneratorService
{
    // The Steam facts of a game barely change within minutes; the pasted answers are checked with live feedback, so the context is kept for a short time
    private static readonly ConcurrentDictionary<string, (DateTimeOffset At, GameContext Context)> ManualContexts = new();
    private static readonly TimeSpan ManualContextLifetime = TimeSpan.FromMinutes(3);

    private async Task<GameContext> ManualContextAsync(GameEntity game, string language, CancellationToken ct)
    {
        // The key includes the research blocks: changing them in the settings applies to the very next prompt
        var blocks = await settingsStore.GetResearchBlocksAsync(ct);
        var key = $"{game.SteamAppId}|{game.Title}|{language}|{ResearchBlocks.Serialize(blocks)}";
        if (ManualContexts.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < ManualContextLifetime)
        {
            return cached.Context;
        }
        var context = await BuildGameContextAsync(game.Title, game.SteamAppId, language, ct);
        ManualContexts[key] = (DateTimeOffset.UtcNow, context);
        foreach (var stale in ManualContexts.Where(p => DateTimeOffset.UtcNow - p.Value.At > ManualContextLifetime).Select(p => p.Key).ToList())
        {
            ManualContexts.TryRemove(stale, out _);
        }
        return context;
    }

    private static List<string> ExistingChapterTitles(GameEntity game) =>
        game.Tabs.Where(t => t.Slug != LoreTab.Slug).OrderBy(t => t.SortOrder).Select(t => t.Title).ToList();

    /// <summary>Step 1: the prompt that asks the chat for the chapter list of the guide (answer = the blueprint JSON).</summary>
    public async Task<ManualPromptDto> BuildManualBlueprintPromptAsync(GameEntity game, CancellationToken ct = default)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var context = await ManualContextAsync(game, language, ct);
        var prompt = ComposeManualBlueprintPrompt(context, game, language);
        return new ManualPromptDto("blueprint", prompt, ManualFileName(game, language == Lang.English ? "blueprint" : "bauplan"), language, game.SteamAppId, game.Title);
    }

    /// <summary>Step 2: the prompt for ONE chapter (answer = the chapter JSON). The chapter is the planned one - or the failed one that is researched again.</summary>
    public async Task<ManualPromptDto> BuildManualChapterPromptAsync(GameEntity game, ManualChapterPromptRequest request, CancellationToken ct = default)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var context = await ManualContextAsync(game, language, ct);
        var title = ChapterTitles.Clean(request.Title.Trim(), language);
        var source = string.IsNullOrWhiteSpace(request.SourceSituation) ? UnknownSource(language) : request.SourceSituation.Trim();
        var prompt = ComposeManualChapterPrompt(context, game, language, title, request.Focus.Trim(), source, request.TopicId);
        return new ManualPromptDto("chapter", prompt, ManualFileName(game, (language == Lang.English ? "chapter-" : "kapitel-") + Slugify(title)), language, game.SteamAppId, game.Title);
    }

    /// <summary>The blueprint prompt of the real AI path plus the output contract for a chat.</summary>
    internal static string ComposeManualBlueprintPrompt(GameContext context, GameEntity game, string language) =>
        BuildBlueprintPrompt(context, null, ExistingChapterTitles(game), WalkthroughTab.BudgetCount(game.Tabs)) + "\n\n" + ManualContract(game, language, chapterTitle: null);

    /// <summary>The chapter prompt of the real AI path plus the output contract for a chat.</summary>
    internal static string ComposeManualChapterPrompt(GameContext context, GameEntity game, string language, string title, string focus, string sourceSituation, string? topicId = null) =>
        (topicId == WalkthroughTab.TopicId
            // The walkthrough in a chat is ONE compact answer (the real AI path researches it in parts)
            ? BuildWalkthroughSinglePrompt(context, title, focus, sourceSituation)
            : BuildChapterPrompt(context, title, focus, sourceSituation, topicId ?? "extra"))
        + "\n\n" + ManualContract(game, language, title, ChapterApplicability.CanBeDropped(topicId ?? "extra"));

    private static string ManualFileName(GameEntity game, string part) => $"platinvault-{Slugify(game.Title)}-{part}.md";

    /// <summary>What a chat needs on top of the API prompts: which game exactly, one JSON object in one code block, the two identifying fields, no invention.</summary>
    private static string ManualContract(GameEntity game, string language, string? chapterTitle, bool allowNotApplicable = false)
    {
        var appId = game.SteamAppId is > 0 ? game.SteamAppId.Value.ToString() : "null";
        var title = game.Title.Replace("\"", "'");
        var skeleton = chapterTitle is null
            ? $"{{ \"game\": \"{title}\", \"steamAppId\": {appId}, \"subtitle\": \"...\", \"profiles\": [...], \"topics\": [...], \"extraChapters\": [...] }}"
            : $"{{ \"game\": \"{title}\", \"steamAppId\": {appId}, \"title\": \"{chapterTitle.Replace("\"", "'")}\", \"icon\": \"mdi:...\", \"sections\": [...] }}";
        // The "does not exist in this game" answer, with the same two identifying fields (ChapterApplicability)
        if (allowNotApplicable && chapterTitle is not null)
        {
            skeleton += language == Lang.English
                ? $" – or, if the topic does not exist in this game: {{ \"game\": \"{title}\", \"steamAppId\": {appId}, \"notApplicable\": true, \"reason\": \"...\" }}"
                : $" – oder, wenn es das Thema in diesem Spiel nicht gibt: {{ \"game\": \"{title}\", \"steamAppId\": {appId}, \"notApplicable\": true, \"reason\": \"...\" }}";
        }
        var appIdLine = game.SteamAppId is > 0
            ? language == Lang.English ? $"Steam AppID {game.SteamAppId}" : $"Steam-AppID {game.SteamAppId}"
            : language == Lang.English ? "Steam AppID unknown" : "Steam-AppID unbekannt";

        return language == Lang.English
            ? $"""
              Output in this chat (this applies in addition and wins over the sentence "without a Markdown code block" above):
              - The game is exactly this one: "{game.Title}", {appIdLine} – the Steam version on PC. Before writing, make sure you mean this game – not a game with the same name, a predecessor, a successor, a remake or a demo.
              - Output the result as ONE JSON object in exactly one code block (```json … ```), without any text before or after it.
              - The JSON object starts with the two identifying fields "game" and "steamAppId", like this (shortened, write the real content): {skeleton}
              - Write real content, no placeholders like "..." – the structure description above is only the pattern. What you cannot back up: "Not documented yet (as of <today's date>)" – invent nothing.
              - Use the web search of your chat if it has one; without web search mark the source situation as thin.
              """
            : $"""
              Ausgabe in diesem Chat (das gilt zusätzlich und hat bei der Ausgabe Vorrang vor dem Satz „ohne Markdown-Codeblock“ oben):
              - Gemeint ist genau dieses Spiel: „{game.Title}“, {appIdLine} – die Steam-Version auf dem PC. Prüfe vor dem Schreiben, dass du dieses Spiel meinst – nicht ein gleichnamiges Spiel, einen Vorgänger, Nachfolger, ein Remake oder eine Demo.
              - Gib das Ergebnis als EIN JSON-Objekt aus, in genau einem Codeblock (```json … ```), ohne Text davor oder danach.
              - Das JSON-Objekt beginnt mit den zwei Feldern „game“ und „steamAppId“, so (verkürzt, schreibe die echten Inhalte): {skeleton}
              - Schreibe echte Inhalte, keine Platzhalter wie "..." – die Strukturbeschreibung oben ist nur das Muster. Was du nicht belegen kannst: „Noch nicht dokumentiert (Stand <heutiges Datum>)“ – erfinde nichts.
              - Nutze die Websuche deines Chats, falls es eine gibt; ohne Websuche kennzeichne die Quellenlage als dünn.
              """;
    }

    /// <summary>The plan the guide builder shows, made from a blueprint answer (real AI call or pasted); shared so both are checked and budgeted the same way.</summary>
    private static GuidePlanDto AssemblePlan(JsonElement root, GameContext context, int existingChapters, string language, bool hasWalkthrough = false)
    {
        var scope = context.Blocks.Scope;
        var extraChapters = ReadExtras(root);
        var picks = PickTopics(root, context.TagProfiles, language, scope, existingChapters, extraChapters.Count, hasWalkthrough);
        var (budgetMin, budgetMax) = ChapterBudget.For(scope);
        var profileIds = root.TryGetProperty("profiles", out var profilesEl) && profilesEl.ValueKind == JsonValueKind.Array
            ? profilesEl.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        profileIds.UnionWith(context.TagProfiles);
        var profiles = Profiles(language).Where(p => profileIds.Contains(p.Id)).ToList();

        var topics = picks.Select(pick => new GuidePlanTopicDto(
            Id: pick.Topic.Id,
            Title: ChapterTitles.Clean(pick.IsObject ? Text(pick.Answer, "title", pick.Topic.Title) : pick.Topic.Title, language),
            Icon: pick.Topic.Icon,
            Focus: pick.IsObject ? Text(pick.Answer, "focus", pick.Topic.Brief) : pick.Topic.Brief,
            // Within the budget of the chosen scope; the rest stays selectable but is not ticked by default
            Recommended: pick.Kept,
            Required: pick.Required,
            Covered: pick.Covered,
            Reason: pick.Include && !pick.Kept && !pick.Covered
                ? ServerTexts.Ai.OverBudget(ChapterBudget.ScopeName(scope, language), budgetMax).Resolve(language)
                : pick.IsObject && !pick.Applicable ? Text(pick.Answer, "reason", string.Empty) : null)).ToList();

        var extras = extraChapters
            .Take(ChapterBudget.Room(scope, existingChapters))
            .Select(e => new GuidePlanTopicDto("extra", ChapterTitles.Clean(e.Title, language), e.Icon, e.Focus, true, false, false, null))
            .ToList();

        return new GuidePlanDto(
            Profiles: profiles.Select(p => new GuidePlanProfileDto(p.Id, p.Name, context.TagProfiles.Contains(p.Id))).ToList(),
            SourceSituation: Text(root, "sourceSituation", EstablishedSource(language)),
            Topics: topics,
            Extras: extras,
            Budget: new GuidePlanBudgetDto(ChapterBudget.Effective(scope), budgetMin, budgetMax, topics.Count(t => t.Recommended && t.Id != WalkthroughTab.TopicId) + extras.Count));
    }

    /// <summary>Checks a pasted blueprint answer; with no errors the plan is the same one the AI path would have produced.</summary>
    public async Task<ManualBlueprintCheckDto> CheckManualBlueprintAsync(GameEntity game, string? text, CancellationToken ct = default)
    {
        var errors = new List<ManualIssueDto>();
        var warnings = new List<ManualIssueDto>();
        if (!ManualAnswers.TryRead(text, errors, out var root) || root is null)
        {
            return new ManualBlueprintCheckDto(false, errors, warnings, null);
        }

        if (root["format"]?.ToString() == GuideImport.FormatName)
        {
            errors.Add(ManualAnswers.Issue("wholeGuide", ServerTexts.Manual.WholeGuideFile));
        }
        else if (root["sections"] is not null && root["topics"] is null && root["extraChapters"] is null)
        {
            errors.Add(ManualAnswers.Issue("chapterForBlueprint", ServerTexts.Manual.ChapterForBlueprint));
        }
        else if (root["topics"] is not JsonArray && root["extraChapters"] is not JsonArray)
        {
            errors.Add(ManualAnswers.Issue("noTopics", ServerTexts.Manual.NoTopics));
        }
        ManualAnswers.CheckGame(root, game.SteamAppId, game.Title, errors, warnings);
        if (errors.Count > 0)
        {
            return new ManualBlueprintCheckDto(false, errors, warnings, null);
        }

        var language = GuideLanguage(game);
        var wrongLanguage = ManualAnswers.GuessLanguage(root["topics"]);
        if (wrongLanguage is not null && wrongLanguage != language)
        {
            warnings.Add(ManualAnswers.Issue("otherLanguage", ServerTexts.Manual.OtherLanguage(language)));
        }

        var context = await ManualContextAsync(game, language, ct);
        using var doc = JsonDocument.Parse(root.ToJsonString());
        var plan = AssemblePlan(doc.RootElement, context, WalkthroughTab.BudgetCount(game.Tabs), language, game.Tabs.Any(WalkthroughTab.Is));
        if (plan.Topics.Count(t => t.Recommended) + plan.Extras.Count == 0)
        {
            warnings.Add(ManualAnswers.Issue("noChapters", ServerTexts.Manual.NoChaptersPlanned));
        }
        return new ManualBlueprintCheckDto(true, [], warnings, plan);
    }

    /// <summary>The result of reading a chapter answer: what the UI shows, plus the strict JSON the tab is built from (null when there are errors).</summary>
    public sealed record ManualChapterChecked(ManualChapterCheckDto Dto, string? CanonicalJson);

    /// <summary>
    /// Checks a pasted chapter answer (structure, game, rows, language) without touching the database. The answer "this topic does not exist in this
    /// game" (top-level "notApplicable": true, same contract as the AI path, <see cref="ChapterApplicability"/>) is a valid answer for a topic that may be
    /// dropped: Ok, no preview, <c>NotApplicable</c> = the sentence for the UI. For walkthrough, roadmap and tips it is an error unless real sections come with it.
    /// </summary>
    public ManualChapterChecked CheckManualChapter(GameEntity game, string? text, string expectedTitle, string? topicId = null, bool replacesPlaceholder = false)
    {
        var errors = new List<ManualIssueDto>();
        var warnings = new List<ManualIssueDto>();
        ManualChapterChecked Failed() => new(new ManualChapterCheckDto(false, errors, warnings, null), null);

        if (!ManualAnswers.TryRead(text, errors, out var root) || root is null)
        {
            return Failed();
        }
        if (root["format"]?.ToString() == GuideImport.FormatName)
        {
            errors.Add(ManualAnswers.Issue("wholeGuide", ServerTexts.Manual.WholeGuideFile));
            return Failed();
        }
        if (root["sections"] is null && (root["topics"] is not null || root["extraChapters"] is not null))
        {
            errors.Add(ManualAnswers.Issue("blueprintForChapter", ServerTexts.Manual.BlueprintForChapter));
            return Failed();
        }
        ManualAnswers.CheckGame(root, game.SteamAppId, game.Title, errors, warnings);
        if (errors.Count > 0)
        {
            return Failed();
        }

        if (ChapterApplicability.Says(root, out var notApplicableReason))
        {
            var topic = string.IsNullOrWhiteSpace(topicId) ? "extra" : topicId.Trim();
            if (ChapterApplicability.CanBeDropped(topic))
            {
                var sentence = ServerTexts.Manual.NotApplicable(game.Title, notApplicableReason, replacesPlaceholder).ToString();
                return new ManualChapterChecked(new ManualChapterCheckDto(true, [], warnings, null, sentence), root.ToJsonString());
            }
            if (root["sections"] is not JsonArray { Count: > 0 })
            {
                errors.Add(ManualAnswers.Issue("notApplicableNotAllowed", ServerTexts.Manual.NotApplicableNotAllowed(expectedTitle)));
                return Failed();
            }
            // A chapter every guide has, with real sections: the flag is ignored (a parser quirk never removes it)
        }

        if (root["sections"] is not JsonArray sections)
        {
            errors.Add(ManualAnswers.Issue(root["sections"] is null ? "noSections" : "sectionsNotList", root["sections"] is null ? ServerTexts.Manual.NoSections : ServerTexts.Manual.SectionsNotList));
            return Failed();
        }
        if (sections.Count == 0)
        {
            errors.Add(ManualAnswers.Issue("noSections", ServerTexts.Manual.NoSections));
            return Failed();
        }

        ManualAnswers.ScanSections(sections, warnings);
        var language = GuideLanguage(game);
        JsonArray normalized;
        using (Lang.Use(language))
        {
            using var doc = JsonDocument.Parse(sections.ToJsonString());
            normalized = GuideSectionNormalizer.Normalize(doc.RootElement);
        }
        if (normalized.Count == 0)
        {
            errors.Add(ManualAnswers.Issue("nothingReadable", ServerTexts.Manual.NothingReadable(sections.Count)));
            return Failed();
        }

        var title = root["title"] is JsonValue titleValue && titleValue.TryGetValue<string>(out var given) && !string.IsNullOrWhiteSpace(given) ? ChapterTitles.Clean(given, language) : null;
        if (title is null)
        {
            warnings.Add(ManualAnswers.Issue("noTitle", ServerTexts.Manual.NoTitle(expectedTitle)));
            title = expectedTitle;
        }

        var wrongLanguage = ManualAnswers.GuessLanguage(normalized);
        if (wrongLanguage is not null && wrongLanguage != language)
        {
            warnings.Add(ManualAnswers.Issue("otherLanguage", ServerTexts.Manual.OtherLanguage(language)));
        }

        var rows = 0;
        var cards = 0;
        foreach (var section in normalized.OfType<JsonObject>())
        {
            rows += (section["table"]?["rows"] as JsonArray)?.Count ?? 0;
            cards += (section["cards"] as JsonArray)?.Count ?? 0;
        }
        var icon = root["icon"]?.ToString() is { Length: > 0 } stated ? stated : "mdi:star-four-points";
        var sectionTitles = normalized.OfType<JsonObject>().Select(s => s["title"]?.ToString()).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).Take(8).ToList();
        var preview = new ManualChapterPreviewDto(title, icon, normalized.Count, rows, cards, sectionTitles);
        return new ManualChapterChecked(new ManualChapterCheckDto(true, [], warnings, preview), root.ToJsonString());
    }

    /// <summary>Builds the tab of a checked answer exactly like an AI result (title cleanup, normalizer, group), at the given sort order.</summary>
    public GuideTabEntity BuildManualChapterTab(GameEntity game, int sortOrder, ManualChapterAnswerRequest request, string canonicalJson)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var plan = new ChapterPlan(request.Title.Trim(), string.IsNullOrWhiteSpace(request.Icon) ? "mdi:star-four-points" : request.Icon.Trim(), (request.Focus ?? request.Title).Trim(), string.IsNullOrWhiteSpace(request.TopicId) ? "extra" : request.TopicId.Trim());
        return BuildTabFromChapterJson(game.Id, sortOrder, plan, canonicalJson);
    }
}
