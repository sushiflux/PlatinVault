using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Configuration;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Settings;
using Microsoft.Extensions.Options;

namespace Companion.Api.Services;

/// <summary>
/// Builds 100% guides with live web research (Gemini + Google Search grounding, Claude + web search, OpenAI as fallback
/// without search). A new game is researched in two stages so the guide fits the game instead of one fixed JRPG layout:
/// 1. Blueprint - the AI studies the game (Steam facts + web) and checks EVERY topic of the fixed standard list
///    (<see cref="Topics"/>) after assigning the game to 1-3 genre profiles (<see cref="Profiles"/>:
///    RPG, shooter, management, sports, ...). The typical topics of the profiles are only a checklist for the research: a topic becomes a
///    chapter only when the research confirms the game has it (see <see cref="PickTopics"/>); plus up to three game-specific extra chapters.
///    Nobody has to type a prompt.
/// 2. Chapters - every chapter is researched in its own call (up to three in parallel, spread over the connected
///    providers). A chapter whose topic the game turns out not to have is answered with "notApplicable" and dropped
///    (<see cref="ChapterApplicability"/>); an answer cut off at the output limit is asked for once more, more compactly.
///    The achievement chapter is built from Steam's official list (item id = Steam API name, so unlocks
///    check it off exactly); the AI only adds tips to it.
/// A guide is written in ONE language (<c>GameEntity.ContentLanguage</c>, "de"/"en"): the one of the UI when the guide is
/// created, and every later addition (complete guide, add chapter, Story &amp; Lore) uses the guide's own language, never the
/// current UI language - so a guide is never half in one language. The prompts, topic lists and the texts that end up
/// inside a guide exist per language (AiGuideContent.cs, AiGuidePrompts.cs).
/// </summary>
public sealed partial class AiGuideGeneratorService(
    IHttpClientFactory httpClientFactory,
    SettingsStore settingsStore,
    SecretStore secretStore,
    SteamClient steamClient,
    SteamStatsService steamStats,
    IgdbService igdbService,
    IOptions<CompanionOptions> companionOptions,
    AiRouter router,
    ILogger<AiGuideGeneratorService> logger)
{
    private const int MaxParallelChapters = 3;

    // Base URLs of the AI providers (the official ones unless the configuration points elsewhere, e.g. a proxy or a test double)
    private readonly AiProviderUrls aiUrls = companionOptions.Value.Ai;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

// Specific Steam community tags (English names, checked against IStoreService/GetTagList on 2026-10-30) that
    // decide a genre profile on their own. Broad tags ("Strategy", "Simulation", "Adventure", "Survival", "Story Rich")
    // are deliberately missing – e.g. Little Big Workshop is tagged "Strategy" and EA FC "Simulation"; those cases are
    // left to the AI. Survival needs a specific combination (see DetectProfilesFromTags).
    private static readonly Dictionary<string, string[]> ProfileTags = new()
    {
        ["rpg"] = ["RPG", "JRPG", "Action RPG", "Party-Based RPG", "CRPG", "Tactical RPG", "MMORPG"],
        ["openworld"] = ["Open World", "Open World Survival Craft"],
        ["shooter"] = ["FPS", "Shooter", "Third-Person Shooter", "Looter Shooter"],
        ["soulslike"] = ["Souls-like"],
        ["metroidvania"] = ["Metroidvania", "Platformer", "2D Platformer", "3D Platformer", "Precision Platformer", "Puzzle Platformer"],
        ["management"] = ["Management", "City Builder", "Automation", "Colony Sim", "Economy", "Resource Management", "Farming Sim", "Life Sim"],
        ["strategy"] = ["Grand Strategy", "4X", "RTS", "Turn-Based Strategy"],
        // "Base Building" alone is no survival game (factory and city builders carry it too: Little Big Workshop), see DetectProfilesFromTags
        ["survival"] = ["Open World Survival Craft"],
        ["roguelike"] = ["Roguelike", "Roguelite", "Action Roguelike", "Traditional Roguelike", "Roguelike Deckbuilder"],
        ["sports"] = ["Sports", "Football (Soccer)", "Football (American)", "Basketball"],
        ["racing"] = ["Racing", "Driving"],
        ["fighting"] = ["Fighting", "2D Fighter", "3D Fighter"],
        ["adventure"] = ["Point & Click", "Puzzle", "Visual Novel", "Choices Matter", "Survival Horror", "Horror"],
        ["cards"] = ["Card Game", "Deckbuilding", "Card Battler", "Trading Card Game", "Roguelike Deckbuilder"],
        ["multiplayer"] = ["Massively Multiplayer", "MMORPG", "Battle Royale"],
    };

    internal static HashSet<string> DetectProfilesFromTags(IReadOnlyCollection<string> tags)
    {
        var set = tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profiles = ProfileTags
            .Where(p => p.Value.Any(set.Contains))
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Survival needs the "Survival" tag itself plus crafting or base building; either of those alone is just as common in building games
        if (set.Contains("Survival") && (set.Contains("Crafting") || set.Contains("Base Building")))
        {
            profiles.Add("survival");
        }
        return profiles;
    }

    // Topics every game gets regardless of its profiles
    private static readonly string[] AlwaysRequired = ["roadmap", "tips"];

    internal sealed record GameContext(
        string Title,
        string FactsBlock,
        List<(string ApiName, string DisplayName, string Description)> Achievements,
        HashSet<string> TagProfiles,
        // The research blocks the player ticked (empty = nothing ticked) and the base wish text with the game, genre and platform filled in (see ResearchBlocks)
        ResearchBlocks.Selection Blocks,
        string ResearchBase,
        // "de" / "en": the language of the guide being written
        string Language,
        // The tickable items of the chapters that already exist (compact list, see GuideLinker.BuildCandidateList); empty = nothing to point at.
        // Used as it is when LinkGroups is null (tests, hand-made contexts); otherwise every prompt renders LinkGroups with its own cap and relevance.
        string LinkCandidates = "",
        IReadOnlyList<GuideLinker.CandidateGroup>? LinkGroups = null,
        // The items the chapter that is being researched again has today ("keep these itemIds for the same entries"); null = a new chapter
        IReadOnlyList<GuideLinker.CandidateGroup>? KeepGroups = null);

    internal sealed record ChapterPlan(string Title, string Icon, string Focus, string TopicId = "extra");

    private sealed record Blueprint(JsonElement Root, List<ChapterPlan> Chapters, string SourceSituation, string Overview);

    /// <summary>The language of an existing guide: its stored content language; guides from before the column existed are German.</summary>
    public static string GuideLanguage(GameEntity game) => Lang.NormalizeOr(game.ContentLanguage, Lang.German);

    /// <summary>Researches a new guide in <paramref name="language"/> (the UI language of the request) and returns the game with that content language.</summary>
    public async Task<(GameEntity Game, List<GuideTabEntity> Tabs, AiUsage Usage)> ResearchAndBuildGameAsync(
        AiGenerateGameRequest request,
        int nextSortOrder,
        string language,
        AiJobProgress? progress,
        IAiChapterSink? sink,
        CancellationToken ct = default)
    {
        language = Lang.NormalizeOr(language);
        // Texts made while researching (a failed chapter's alert) end up inside the guide: they follow the guide's language
        using var languageScope = Lang.Use(language);
        var settings = await settingsStore.GetAsync(ct);
        var run = await router.BeginAsync(research: true, request.ProviderOverride, background: false, ct);
        progress?.Step("facts");
        var context = await BuildGameContextAsync(request.GameTitle.Trim(), request.SteamAppId, language, ct);

        // Stage 1: blueprint over the whole standard topic list
        progress?.Step("blueprint");
        var blueprintRaw = await CallContentAsync(run, run.Primary, BuildBlueprintPrompt(context, request.CustomFocusPrompt, existingChapters: []), settings, language, "blueprint", ct);
        using var blueprintDoc = JsonDocument.Parse(ExtractJsonObject(blueprintRaw));
        var blueprint = ParseBlueprint(blueprintDoc.RootElement, context.TagProfiles, language, context.Blocks.Scope, existingChapters: 0);
        if (blueprint.Chapters.Count == 0)
        {
            throw new LocalizedException(ServerTexts.Ai.BlueprintEmpty);
        }

        var gameId = Guid.NewGuid();

        // The tile is known after the blueprint: with a sink it is saved now, so finished chapters survive an interruption
        var bp = blueprint.Root;
        var resolvedAssets = request.SteamAppId.HasValue
            ? await steamClient.ResolveSingleAppAssetsAsync(request.SteamAppId.Value, ct)
            : (CoverUrl: string.Empty, HeroUrl: string.Empty, HeaderUrl: string.Empty);

        var fallback = BlueprintDefaultsFor(language);
        var game = new GameEntity
        {
            Id = gameId,
            Slug = Slugify(request.GameTitle),
            Title = request.GameTitle.Trim(),
            Subtitle = Text(bp, "subtitle", fallback.Subtitle),
            SteamAppId = request.SteamAppId,
            CoverUrl = !string.IsNullOrWhiteSpace(request.CoverUrl) ? request.CoverUrl.Trim() : resolvedAssets.CoverUrl,
            HeroBannerUrl = !string.IsNullOrWhiteSpace(request.HeroBannerUrl) ? request.HeroBannerUrl.Trim() : resolvedAssets.HeroUrl,
            AccentColor = Text(bp, "accentColor", "crystal"),
            EstimatedHours = Text(bp, "estimatedHours", fallback.EstimatedHours),
            MissablesInfo = Text(bp, "missablesInfo", fallback.MissablesInfo),
            DifficultyInfo = Text(bp, "difficultyInfo", fallback.DifficultyInfo),
            GenreTags = Text(bp, "genreTags", fallback.GenreTags),
            ContentLanguage = language,
            SortOrder = nextSortOrder,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        if (sink is not null)
        {
            await sink.GameReadyAsync(game, ct);
        }

        // The first chapter carries the overview and the note about the sources; it is finished (and saved) like every other one. It is the first
        // chapter that can never be dropped (walkthrough, roadmap, tips), so a chapter that turns out not to exist in the game cannot take the intro with it.
        var introIndex = IntroChapterIndex(blueprint.Chapters);
        async Task ChapterFinishedAsync(int index, GuideTabEntity tab)
        {
            // Not on a placeholder of a failed chapter: it must stay the single error box that FailedChapters recognises
            if (index == introIndex && FailedChapters.Read(tab.SectionsJson) is null)
            {
                PrependIntroAlerts(tab, blueprint.Overview, blueprint.SourceSituation, language);
            }
            if (sink is not null)
            {
                await sink.ChapterDoneAsync(tab, ct);
            }
        }

        // Stage 2: every chapter in its own call; the achievement chapter comes from Steam's official list
        Task<GuideTabEntity?> achievementsTask = context.Achievements.Count > 0
            ? BuildAchievementsTabAsync(gameId, blueprint.Chapters.Count + 1, context, run, settings, ct)
            : Task.FromResult<GuideTabEntity?>(null);
        var tabs = (await ResearchChaptersAsync(gameId, blueprint.Chapters, 1, context, blueprint.SourceSituation, run, settings, progress, ChapterFinishedAsync, ct))
            .OfType<GuideTabEntity>()
            .ToList();
        if (await achievementsTask is { } achievementsTab)
        {
            tabs.Add(achievementsTab);
            if (sink is not null)
            {
                await sink.ChapterDoneAsync(achievementsTab, ct);
            }
        }

        return (game, tabs, run.Usage());
    }

    /// <summary>
    /// "Guide vervollständigen": checks the standard topic list against the chapters the game already has and researches
    /// every applicable topic that is still missing. Returns the new tabs (empty when nothing is missing).
    /// </summary>
    public async Task<(List<GuideTabEntity> Tabs, List<string> DroppedTitles, AiUsage Usage)> CompleteGuideAsync(
        GameEntity game,
        string? providerOverride,
        int firstSortOrder,
        IReadOnlyList<GuideChapterSelectionDto>? selection,
        AiJobProgress? progress,
        IAiChapterSink? sink,
        CancellationToken ct = default)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var settings = await settingsStore.GetAsync(ct);
        var run = await router.BeginAsync(research: true, providerOverride, background: false, ct);
        progress?.Step("facts");
        var context = await BuildGameContextAsync(game.Title, game.SteamAppId, language, ct);
        var (tabs, dropped) = await CompleteGuideWithContextAsync(game, context, run, settings, firstSortOrder, selection, progress, sink, ct);
        return (tabs, dropped, run.Usage());
    }

    /// <summary>"Guide vervollständigen" once the settings, the AI run and the Steam facts are there (the part the tests drive with fake providers).</summary>
    internal async Task<(List<GuideTabEntity> Tabs, List<string> DroppedTitles)> CompleteGuideWithContextAsync(
        GameEntity game,
        GameContext context,
        AiRun run,
        SettingsResponseDto settings,
        int firstSortOrder,
        IReadOnlyList<GuideChapterSelectionDto>? selection,
        AiJobProgress? progress,
        IAiChapterSink? sink,
        CancellationToken ct)
    {
        var language = context.Language;
        // New chapters may point at the rows of the chapters that already exist ("ref", see GuideLinker)
        context = WithLinkCandidates(context, game.Tabs);

        // Guide builder: the player ticked the chapters in the plan – research exactly those
        if (selection is { Count: > 0 })
        {
            var topics = Topics(language);
            var picked = selection
                .Where(c => !string.IsNullOrWhiteSpace(c.Title) && !string.IsNullOrWhiteSpace(c.Focus))
                .Take(30)
                .Select(c =>
                {
                    var topic = topics.FirstOrDefault(t => t.Id == c.TopicId);
                    return new ChapterPlan(c.Title.Trim(), topic?.Icon ?? c.Icon ?? "mdi:star-four-points", c.Focus.Trim(), topic?.Id ?? "extra");
                })
                // A game has one walkthrough (fixed address): a selection that names it again is ignored
                .Where(plan => plan.TopicId != WalkthroughTab.TopicId || !game.Tabs.Any(WalkthroughTab.Is))
                .ToList();
            if (picked.Count == 0)
            {
                return ([], []);
            }
            var pickedResults = await ResearchChaptersAsync(game.Id, picked, firstSortOrder, context, UnknownSource(language), run, settings, progress, ChapterSaver(sink, ct), ct);
            return (pickedResults.OfType<GuideTabEntity>().ToList(), DroppedTitles(picked, pickedResults));
        }

        var existing = game.Tabs
            .Where(t => t.Slug != LoreTab.Slug)
            .OrderBy(t => t.SortOrder)
            .Select(t => t.Title)
            .ToList();

        progress?.Step("blueprint");
        var blueprintRaw = await CallContentAsync(run, run.Primary, BuildBlueprintPrompt(context, null, existing, WalkthroughTab.BudgetCount(game.Tabs)), settings, language, "blueprint", ct);
        using var blueprintDoc = JsonDocument.Parse(ExtractJsonObject(blueprintRaw));
        var blueprint = ParseBlueprint(blueprintDoc.RootElement, context.TagProfiles, language, context.Blocks.Scope, WalkthroughTab.BudgetCount(game.Tabs), game.Tabs.Any(WalkthroughTab.Is));
        if (blueprint.Chapters.Count == 0)
        {
            return ([], []);
        }

        var results = await ResearchChaptersAsync(game.Id, blueprint.Chapters, firstSortOrder, context, blueprint.SourceSituation, run, settings, progress, ChapterSaver(sink, ct), ct);
        return (results.OfType<GuideTabEntity>().ToList(), DroppedTitles(blueprint.Chapters, results));
    }

/// <summary>
    /// "Kapitel erneut recherchieren": researches failed chapters again exactly as they were planned (same title, same focus), with the current
    /// AI selection (incl. fallback). <paramref name="onReplaced"/> gets every result the moment it is done (tab id of the placeholder, new content;
    /// a chapter that fails again arrives as a placeholder with the new reason). When no chapter could be researched at all, the first failure is
    /// thrown - after the placeholders were updated - so the job reports it like any other failed AI action. A chapter whose topic the research now
    /// says the game does not have goes to <paramref name="onDropped"/> (the placeholder is removed) and is listed in DroppedTitles.
    /// </summary>
    public async Task<(int Succeeded, List<string> FailedTitles, List<string> DroppedTitles, AiUsage Usage)> RetryChaptersAsync(
        GameEntity game,
        IReadOnlyList<FailedChapterRef> chapters,
        string? providerOverride,
        AiJobProgress? progress,
        Func<Guid, GuideTabEntity, Task>? onReplaced,
        CancellationToken ct = default,
        Func<Guid, Task>? onDropped = null)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var settings = await settingsStore.GetAsync(ct);
        var run = await router.BeginAsync(research: true, providerOverride, background: false, ct);
        progress?.Step("facts");
        var context = WithLinkCandidates(await BuildGameContextAsync(game.Title, game.SteamAppId, language, ct), game.Tabs, chapters.Select(c => c.TabId).ToHashSet());

        var plans = chapters.Select(c => new ChapterPlan(c.Title, c.Icon, c.Failure.Focus, c.Failure.TopicId ?? "extra")).ToList();
        var failures = new ConcurrentBag<Exception>();
        var results = await ResearchChaptersAsync(
            game.Id, plans, 0, context, UnknownSource(language), run, settings, progress,
            onReplaced is null ? null : (index, tab) => onReplaced(chapters[index].TabId, tab),
            ct, failures,
            onDropped is null ? null : index => onDropped(chapters[index].TabId));

        var failedTitles = new List<string>();
        var droppedTitles = new List<string>();
        for (var i = 0; i < results.Length; i++)
        {
            if (results[i] is not { } tab)
            {
                droppedTitles.Add(chapters[i].Title);
            }
            else if (FailedChapters.Read(ApiEndpoints.DeserializeSections(tab.SectionsJson)) is not null)
            {
                failedTitles.Add(chapters[i].Title);
            }
        }

        if (failedTitles.Count == results.Length && failures.TryPeek(out var first))
        {
            ExceptionDispatchInfo.Capture(first).Throw();
        }
        return (results.Length - failedTitles.Count - droppedTitles.Count, failedTitles, droppedTitles, run.Usage());
    }

    /// <summary>
    /// Guide builder ("Klick-Prompt"): analyses the game once and returns EVERY standard topic with the AI's game-specific
    /// title/brief, whether it is recommended (applicable or required by a genre profile), already covered by an
    /// existing chapter, or why it does not fit. The player ticks topics and the selection is researched via
    /// <see cref="CompleteGuideAsync"/>.
    /// </summary>
    public async Task<(GuidePlanDto Plan, AiUsage Usage)> PlanGuideAsync(GameEntity game, string? providerOverride, AiJobProgress? progress, CancellationToken ct = default)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var settings = await settingsStore.GetAsync(ct);
        var run = await router.BeginAsync(research: true, providerOverride, background: false, ct);
        progress?.Step("facts");
        var context = await BuildGameContextAsync(game.Title, game.SteamAppId, language, ct);
        var existing = game.Tabs
            .Where(t => t.Slug != LoreTab.Slug)
            .OrderBy(t => t.SortOrder)
            .Select(t => t.Title)
            .ToList();

        progress?.Step("blueprint");
        var raw = await CallContentAsync(run, run.Primary, BuildBlueprintPrompt(context, null, existing, WalkthroughTab.BudgetCount(game.Tabs)), settings, language, "blueprint", ct);
        using var doc = JsonDocument.Parse(ExtractJsonObject(raw));
        var root = doc.RootElement;

        var plan = AssemblePlan(root, context, WalkthroughTab.BudgetCount(game.Tabs), language, game.Tabs.Any(WalkthroughTab.Is));
        return (plan, run.Usage());
    }

    /// <summary>Hands a finished chapter of an existing guide to the sink (null = nothing is saved before the end).</summary>
    private static Func<int, GuideTabEntity, Task>? ChapterSaver(IAiChapterSink? sink, CancellationToken ct) =>
        sink is null ? null : (_, tab) => sink.ChapterDoneAsync(tab, ct);

    /// <summary>The titles of the planned chapters that were dropped (null result: the game does not have the topic).</summary>
    private static List<string> DroppedTitles(IReadOnlyList<ChapterPlan> plans, IReadOnlyList<GuideTabEntity?> results) =>
        plans.Where((_, i) => i < results.Count && results[i] is null).Select(p => p.Title).ToList();

    /// <summary>Which planned chapter gets the intro alerts of a new guide: the first that can never be dropped (walkthrough, roadmap, tips), else the first.</summary>
    internal static int IntroChapterIndex(IReadOnlyList<ChapterPlan> plans)
    {
        for (var i = 0; i < plans.Count; i++)
        {
            if (!ChapterApplicability.CanBeDropped(plans[i].TopicId))
            {
                return i;
            }
        }
        return 0;
    }

    /// <summary>
    /// Researches the planned chapters (bounded parallelism, spread round-robin over the run's providers). The result has one entry per plan, in plan
    /// order: the chapter, a placeholder for a failed one, or null for a chapter that was DROPPED because the research says the game does not have its
    /// topic (<see cref="ChapterApplicability"/>; logged with the reason, nothing is saved, <paramref name="onDropped"/> is told).
    /// </summary>
    internal async Task<GuideTabEntity?[]> ResearchChaptersAsync(
        Guid gameId,
        List<ChapterPlan> chapters,
        int firstSortOrder,
        GameContext context,
        string sourceSituation,
        AiRun run,
        SettingsResponseDto settings,
        AiJobProgress? progress,
        Func<int, GuideTabEntity, Task>? onChapter,
        CancellationToken ct,
        ConcurrentBag<Exception>? failures = null,
        Func<int, Task>? onDropped = null)
    {
        using var gate = new SemaphoreSlim(MaxParallelChapters);
        var finished = 0;
        progress?.Step("chapters", 0, chapters.Count, chapters.FirstOrDefault()?.Title);
        var chapterTasks = chapters.Select(async (plan, index) =>
        {
            var sortOrder = firstSortOrder + index;
            GuideTabEntity? tab;
            // The walkthrough is researched in parts that take the shared gate one by one; it does not hold a place itself while it waits for them
            var isWalkthrough = plan.TopicId == WalkthroughTab.TopicId;
            if (!isWalkthrough)
            {
                await gate.WaitAsync(ct);
            }
            try
            {
                // The chapter that is being researched now (up to three at once: the latest one started is named)
                progress?.Step("chapters", Volatile.Read(ref finished), chapters.Count, plan.Title);
                if (isWalkthrough)
                {
                    var others = chapters.Where(c => c.TopicId != WalkthroughTab.TopicId).Select(c => c.Title).ToList();
                    tab = await ResearchWalkthroughAsync(
                        gameId, sortOrder, plan, context, sourceSituation, others, run, settings, gate,
                        title => progress?.Step("chapters", Volatile.Read(ref finished), chapters.Count, title), ct);
                }
                else
                {
                    var answer = await CallContentAnswerAsync(run, run.SpreadFor(index), BuildChapterPrompt(context, plan.Title, plan.Focus, sourceSituation, plan.TopicId), settings, context.Language, plan.Title, ct);
                    tab = BuildTabFromChapterJson(gameId, sortOrder, plan, answer.Text, OptionsFor(context), answer.Sources);
                }
            }
            catch (ChapterNotApplicableException dropped)
            {
                // Not a failure: the research says the game does not have this topic. No tab, no placeholder - only this line says why
                logger.LogInformation("Kapitel '{Chapter}' (Thema {Topic}) entfällt: laut Recherche gibt es das in „{Game}“ nicht – {Reason}",
                    plan.Title, plan.TopicId, context.Title, dropped.Reason.Length > 0 ? dropped.Reason : "ohne Begründung");
                tab = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Kapitel '{Chapter}' konnte nicht recherchiert werden.", plan.Title);
                failures?.Add(ex);
                // The placeholder remembers the topic (to research it again), our short explanation in the guide's language and the provider's own text
                tab = BuildFailedTab(gameId, sortOrder, plan, ex, context.Language);
            }
            finally
            {
                var done = Interlocked.Increment(ref finished);
                progress?.Step("chapters", done, chapters.Count, plan.Title);
                if (!isWalkthrough)
                {
                    gate.Release();
                }
            }

            // Saved the moment it is done (outside the try: a storage problem is not "this chapter could not be researched")
            if (tab is null)
            {
                if (onDropped is not null)
                {
                    await onDropped(index);
                }
            }
            else if (onChapter is not null)
            {
                await onChapter(index, tab);
            }
            return tab;
        }).ToList();

        return await Task.WhenAll(chapterTasks);
    }

    /// <summary>
    /// Reads the blueprint answer: standard topics marked applicable become chapters in list order (the AI may only
    /// rename them and write the brief), followed by at most three game-specific extra chapters.
    /// </summary>
    private static Blueprint ParseBlueprint(JsonElement root, IReadOnlySet<string> tagProfiles, string language, string? scope, int existingChapters, bool hasWalkthrough = false)
    {
        var extras = ReadExtras(root);
        var picks = PickTopics(root, tagProfiles, language, scope, existingChapters, extras.Count, hasWalkthrough);

        var chapters = new List<ChapterPlan>();
        foreach (var pick in picks.Where(p => p.Kept))
        {
            chapters.Add(new ChapterPlan(
                pick.IsObject ? Text(pick.Answer, "title", pick.Topic.Title) : pick.Topic.Title,
                pick.Topic.Icon,
                pick.IsObject ? Text(pick.Answer, "focus", pick.Topic.Brief) : pick.Topic.Brief,
                pick.Topic.Id));
        }
        // The game-specific chapters come on top, as many as the budget leaves room for
        chapters.AddRange(extras.Take(ChapterBudget.Room(scope, existingChapters)));

        // Chapter numbers are assigned by sort order: strip any numbering the AI added to the titles; titles are in the guide's language only
        chapters = chapters
            .Select(c => c with { Title = ChapterTitles.Clean(System.Text.RegularExpressions.Regex.Replace(c.Title, @"^\s*\d+\.\s*", string.Empty), language) })
            .ToList();

        return new Blueprint(root, chapters, Text(root, "sourceSituation", EstablishedSource(language)), Text(root, "overview", string.Empty));
    }

    /// <summary>What the blueprint says about one topic of the catalogue, after the budget of the chosen scope was applied.</summary>
    internal sealed record TopicPick(GuideTopic Topic, JsonElement Answer, bool Applicable, bool Covered, bool Required, int Priority, int Order, bool Include, bool Kept)
    {
        public bool IsObject => Answer.ValueKind == JsonValueKind.Object;
    }

    /// <summary>
    /// Decides which topics become chapters. A topic counts only when the AI confirmed it ("applicable": true); being typical for the genre
    /// never forces a topic in, with or without a verdict. The only safety net against an incomplete answer is for the topics every game has
    /// (roadmap, tips: they count when the answer says nothing about them). Of those, the most important ones that fit the budget of the scope
    /// are kept (the game-specific extra chapters take their slots first); genre-typical only breaks ties. There is no minimum: a small game
    /// with few confirmed topics gets few chapters.
    /// </summary>
    internal static List<TopicPick> PickTopics(JsonElement root, IReadOnlySet<string> tagProfiles, string language, string? scope, int existingChapters, int extraCount, bool hasWalkthrough = false)
    {
        var answers = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("topics", out var topicsEl) && topicsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in topicsEl.EnumerateArray())
            {
                var id = Text(t, "id", string.Empty);
                if (id.Length > 0)
                {
                    answers[id] = t;
                }
            }
        }

        var profileIds = root.TryGetProperty("profiles", out var profilesEl) && profilesEl.ValueKind == JsonValueKind.Array
            ? profilesEl.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        profileIds.UnionWith(tagProfiles);
        var typical = Profiles(language)
            .Where(p => profileIds.Contains(p.Id))
            .SelectMany(p => p.Required)
            .Concat(AlwaysRequired)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var picks = new List<TopicPick>();
        var order = 0;
        foreach (var topic in Topics(language))
        {
            answers.TryGetValue(topic.Id, out var answer);
            var isObject = answer.ValueKind == JsonValueKind.Object;
            var hasVerdict = isObject && answer.TryGetProperty("applicable", out var verdict) && verdict.ValueKind is JsonValueKind.True or JsonValueKind.False;
            var applicable = isObject && answer.TryGetProperty("applicable", out var a) && a.ValueKind == JsonValueKind.True;
            // "covered" = an existing chapter already handles it ("Guide vervollständigen"): never forced again
            var covered = isObject && answer.TryGetProperty("covered", out var c) && c.ValueKind == JsonValueKind.True;
            // The walkthrough has a fixed address (one per game): an existing one counts as covered whatever the AI said, so it is never created twice
            covered |= hasWalkthrough && topic.Id == WalkthroughTab.TopicId;
            var isTypical = typical.Contains(topic.Id);
            var priority = isObject && answer.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? Math.Clamp(n, 1, 3) : 2;
            // Only the research decides: a topic becomes a chapter when the AI confirmed it. The one safety net left is for the topics every game has
            // (roadmap, tips) when the answer says nothing about them. Genre-typical topics without a verdict are NOT added any more - that net put a
            // bestiary into Little Big Workshop (a factory sim tagged "Base Building", which made it a "survival" game whose typical topics include a
            // bestiary). The walkthrough always needs the AI's confirmation that a reliable, complete source exists.
            var universal = AlwaysRequired.Contains(topic.Id, StringComparer.OrdinalIgnoreCase);
            var include = !covered && (applicable || (universal && !hasVerdict));
            picks.Add(new TopicPick(topic, answer, applicable, covered, isTypical, priority, order++, include, Kept: false));
        }

        var room = ChapterBudget.Room(scope, existingChapters);
        var slots = room - Math.Min(extraCount, room);
        // The walkthrough does not count against the budget: it is kept whenever it is included, and takes no slot from the others
        var kept = ChapterBudget.Keep(picks.Where(p => p.Include && p.Topic.Id != WalkthroughTab.TopicId).Select(p => (p.Topic.Id, p.Required, p.Priority, p.Order)), slots);
        return picks.Select(p => p with { Kept = p.Include && (p.Topic.Id == WalkthroughTab.TopicId || kept.Contains(p.Topic.Id)) }).ToList();
    }

    /// <summary>The game-specific chapters of a blueprint answer (at most three, only those with a title).</summary>
    private static List<ChapterPlan> ReadExtras(JsonElement root)
    {
        var extras = new List<ChapterPlan>();
        if (root.TryGetProperty("extraChapters", out var extrasEl) && extrasEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var extra in extrasEl.EnumerateArray().Take(3))
            {
                var title = Text(extra, "title", string.Empty);
                if (title.Length > 0)
                {
                    extras.Add(new ChapterPlan(title, Text(extra, "icon", "mdi:star-four-points"), Text(extra, "focus", title)));
                }
            }
        }
        return extras;
    }

    public async Task<(GuideTabEntity Tab, AiUsage Usage)> ResearchAndBuildSingleTabAsync(
        GameEntity game,
        AiExpandTabRequest request,
        int nextSortOrder,
        AiJobProgress? progress,
        CancellationToken ct = default)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var settings = await settingsStore.GetAsync(ct);
        var run = await router.BeginAsync(research: true, request.ProviderOverride, background: false, ct);
        progress?.Step("facts");
        var context = WithLinkCandidates(await BuildGameContextAsync(game.Title, game.SteamAppId, language, ct), game.Tabs);
        var plan = new ChapterPlan(request.TabTitle.Trim(), "mdi:sparkles", request.TopicPrompt.Trim());
        progress?.Step("chapter", 0, 1, plan.Title);
        var answer = await CallContentAnswerAsync(run, run.Primary, BuildChapterPrompt(context, plan.Title, plan.Focus, UnknownSource(language), plan.TopicId), settings, language, plan.Title, ct);
        try
        {
            return (BuildTabFromChapterJson(game.Id, nextSortOrder, plan, answer.Text, OptionsFor(context), answer.Sources), run.Usage());
        }
        catch (ChapterNotApplicableException dropped)
        {
            // The player asked for this chapter himself: tell him why nothing was created instead of saving filler
            logger.LogInformation("Kapitel '{Chapter}' entfällt: laut Recherche gibt es das in „{Game}“ nicht – {Reason}", plan.Title, game.Title, dropped.Reason);
            throw new LocalizedException(ServerTexts.Ai.ChapterNotApplicable(plan.Title, dropped.Reason), dropped);
        }
    }

    /// <summary>
    /// Researches the optional "Story &amp; Lore" tab (spoilers): four parts, up to three calls in parallel, merged in a fixed
    /// order. A part that fails becomes a danger alert inside the tab so the others are kept; only when ALL parts fail an
    /// exception is thrown (so an existing tab is never replaced by nothing but error boxes).
    /// </summary>
    public async Task<(GuideTabEntity Tab, AiUsage Usage)> ResearchLoreTabAsync(GameEntity game, string? providerOverride, AiJobProgress? progress, CancellationToken ct = default)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var settings = await settingsStore.GetAsync(ct);
        var run = await router.BeginAsync(research: true, providerOverride, background: false, ct);
        progress?.Step("facts");
        var context = await BuildGameContextAsync(game.Title, game.SteamAppId, language, ct);

        using var gate = new SemaphoreSlim(MaxParallelChapters);
        var loreParts = LoreParts(language);
        var finishedParts = 0;
        progress?.Step("loreParts", 0, loreParts.Length, loreParts.FirstOrDefault()?.Title);
        var tasks = loreParts.Select(async (part, index) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                progress?.Step("loreParts", Volatile.Read(ref finishedParts), loreParts.Length, part.Title);
                var raw = await CallContentAsync(run, run.SpreadFor(index), BuildLorePrompt(context, part), settings, language, part.Title, ct);
                var sections = ParseLoreSections(raw);
                return (Part: part, Sections: (JsonArray?)sections, Error: (Exception?)null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Story-&-Lore-Teil '{Part}' konnte nicht recherchiert werden.", part.Title);
                return (Part: part, Sections: (JsonArray?)null, Error: (Exception?)ex);
            }
            finally
            {
                progress?.Step("loreParts", Interlocked.Increment(ref finishedParts), loreParts.Length, part.Title);
                gate.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        if (results.All(r => r.Sections is null))
        {
            // Keep the first provider failure with its raw text, so the UI can show it under "technical details"
            var first = results[0].Error!;
            throw new AiProviderException(
                ServerTexts.Ai.LoreAllPartsFailed(AiProviderException.TextOf(first)),
                (first as AiProviderException)?.ProviderMessage,
                (first as AiProviderException)?.Status ?? 0,
                first,
                (first as AiProviderException)?.Provider,
                (first as AiProviderException)?.RetryAfter,
                (first as AiProviderException)?.Kind);
        }

        var merged = new JsonArray();
        foreach (var (part, sections, error) in results)
        {
            merged.Add(LoreAlert("crystal", "mdi:book-open-page-variant", part.Title, part.Blurb));
            if (sections is null)
            {
                merged.Add(LoreAlert(
                    "danger",
                    "mdi:alert-circle-outline",
                    FailedLoreTitle(part.Title, language),
                    FailedLoreText(error!.Message, language)));
                continue;
            }

            foreach (var section in sections.OfType<JsonObject>())
            {
                // Lore tables are reading material, never a to-do list; everything in the tab is a spoiler
                section["spoiler"] = true;
                if (section["type"]?.ToString() == "checklist_table" && section["table"] is JsonObject table)
                {
                    table["checkable"] = false;
                }
                merged.Add(section.DeepClone());
            }
        }

        var loreTab = new GuideTabEntity
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            Slug = LoreTab.Slug,
            Title = LoreTab.Title,
            Icon = LoreTab.Icon,
            SortOrder = LoreTab.SortOrder,
            SectionsJson = merged.ToJsonString()
        };
        return (loreTab, run.Usage());
    }

    private static JsonArray ParseLoreSections(string raw)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(ExtractJsonObject(raw));
        }
        catch (JsonException)
        {
            throw new LocalizedException(ServerTexts.Ai.LoreNotJson);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("sections", out var sectionsEl) || sectionsEl.ValueKind != JsonValueKind.Array)
            {
                throw new LocalizedException(ServerTexts.Ai.LoreNoSectionList);
            }

            var sections = GuideSectionNormalizer.Normalize(sectionsEl);
            return sections.Count > 0
                ? sections
                : throw new LocalizedException(ServerTexts.Ai.LoreNoReadableSections);
        }
    }

    private static JsonObject LoreAlert(string variant, string icon, string title, string text) => new()
    {
        ["type"] = "alert",
        ["variant"] = variant,
        ["icon"] = icon,
        ["title"] = title,
        ["htmlContent"] = text,
        ["spoiler"] = true
    };

    /// <summary>
    /// Translates achievement descriptions of a game that ships no German texts. Context matters ("Clear chapter 2" means
    /// finishing it, not deleting it), so this goes to the AI instead of a word-by-word translator. Returns source -> German.
    /// </summary>
    public async Task<Dictionary<string, string>> TranslateAchievementTextsAsync(string gameName, IReadOnlyList<string> texts, CancellationToken ct)
    {
        var settings = await settingsStore.GetAsync(ct);
        // Background job: nobody can be asked, so it takes the Standard-KI and may change provider quietly (logged below)
        var run = await router.BeginAsync(research: false, providerOverride: null, background: true, ct);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var chunk in texts.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.Ordinal).Chunk(60))
        {
            var items = JsonSerializer.Serialize(chunk.Select((text, index) => new { id = index, text }));
            var prompt = $$"""
                Übersetze die folgenden Beschreibungen von Steam-Erfolgen des Spiels „{{gameName}}“ ins Deutsche.
                Das Spiel liefert selbst keine deutschen Texte. Regeln:
                - Es sind Videospiel-Erfolge: „Clear“ heißt abschließen/schaffen (nicht löschen), „Obtain“ heißt erhalten,
                  „Defeat“ heißt besiegen.
                - Verwende die offiziellen deutschen Begriffe des Spiels (Orte, Figuren, Gegenstände), wenn es sie gibt;
                  sonst lass Eigennamen unverändert.
                - Kurz und natürlich, im Stil eines Erfolgstextes („Schließe Kapitel 2 ab.“), keine Erklärungen.
                Antworte NUR mit JSON: {"items":[{"id":0,"text":"<deutsche Übersetzung>"}]} – für jede id genau ein Eintrag.

                Texte:
                {{items}}
                """;
            // Plain generation: translating needs no web search (and the search tool is what the free tier rejects with 429)
            var raw = await CallAsync(run, run.Primary, prompt, settings, ct, research: false);
            using var doc = JsonDocument.Parse(ExtractJsonObject(raw));
            if (!doc.RootElement.TryGetProperty("items", out var translated) || translated.ValueKind != JsonValueKind.Array) continue;

            foreach (var item in translated.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id) && id >= 0 && id < chunk.Length &&
                    item.TryGetProperty("text", out var textEl) && textEl.GetString() is { Length: > 0 } text)
                {
                    result[chunk[id]] = text.Trim();
                }
            }
        }

        LogSwitches(run, "Erfolgstexte übersetzen");
        return result;
    }

    /// <summary>Background actions change provider without a notice on screen: at least leave a line in the log.</summary>
    internal void LogSwitches(AiRun run, string what)
    {
        foreach (var change in run.Usage().Switches)
        {
            logger.LogInformation("{What}: {From} nicht nutzbar ({Reason}), weiter mit {To}.", what, change.From, change.Reason.ToId(), change.To);
        }
    }

    /// <summary>Starts an AI action of the translation job: plain generation, nobody to ask, the stored selection decides.</summary>
    public Task<AiRun> BeginTranslationRunAsync(CancellationToken ct) => router.BeginAsync(research: false, providerOverride: null, background: true, ct);

    /// <summary>One request of an action: the preferred provider of the run, and with the fallback on the next ones when it cannot be used.</summary>
    internal async Task<string> CallAsync(AiRun run, string preferred, string prompt, SettingsResponseDto settings, CancellationToken ct, bool research = true) =>
        (await CallAnswerAsync(run, preferred, prompt, settings, ct, research)).Text;

    /// <summary>Like <see cref="CallAsync"/>, with the sources the provider's search metadata names for the answer.</summary>
    internal Task<AiAnswer> CallAnswerAsync(AiRun run, string preferred, string prompt, SettingsResponseDto settings, CancellationToken ct, bool research = true) =>
        run.ExecuteAsync(preferred, provider => CallProviderAnswerAsync(provider, prompt, settings, ct, research), ct);

    /// <summary>
    /// A call whose answer is guide content (blueprint, chapter, walkthrough part, lore part, achievement tips). When the provider stops at its output
    /// limit, the answer is cut off and useless as JSON - the cause of most "could not be researched" chapters (seen 2026-11-02: claude-haiku-5-5 with
    /// max_tokens 16000 cut 9 of 14 chapters of an "Ausführlich" guide, which asks for complete tables). Then the SAME provider is asked once more with
    /// the instruction to answer more compactly; a second cut-off ends with a clear message. Any other failure behaves exactly like <see cref="CallAsync"/>.
    /// </summary>
    internal async Task<string> CallContentAsync(AiRun run, string preferred, string prompt, SettingsResponseDto settings, string language, string what, CancellationToken ct) =>
        (await CallContentAnswerAsync(run, preferred, prompt, settings, language, what, ct)).Text;

    /// <summary><see cref="CallContentAsync"/> with the provider's sources of the answer (chapters, walkthrough parts, lore, achievement tips keep them).</summary>
    internal async Task<AiAnswer> CallContentAnswerAsync(AiRun run, string preferred, string prompt, SettingsResponseDto settings, string language, string what, CancellationToken ct)
    {
        try
        {
            return await CallAnswerAsync(run, preferred, prompt, settings, ct);
        }
        catch (AiProviderException first) when (first.IsTruncated)
        {
            var provider = first.Provider ?? preferred;
            logger.LogWarning("'{What}': Antwort von {Provider} an der Ausgabegrenze abgeschnitten ({Detail}) – zweiter Versuch mit der Bitte um eine kompaktere Antwort.",
                what, provider, first.ProviderMessage);
            try
            {
                return await CallAnswerAsync(run, provider, prompt + "\n\n" + CompactRetryNote(language), settings, ct);
            }
            catch (AiProviderException second) when (second.IsTruncated)
            {
                logger.LogWarning("'{What}': auch der kompaktere Versuch wurde abgeschnitten ({Detail}).", what, second.ProviderMessage);
                throw new AiProviderException(ServerTexts.Ai.AnswerTooLong(second.Provider ?? provider), second.ProviderMessage, 0, second, second.Provider ?? provider,
                    kind: AiFailureKind.Other, truncated: true);
            }
        }
    }

    /// <summary>What the second attempt after a cut-off answer adds to the prompt (it wins over "be exhaustive" of the research rules).</summary>
    internal static string CompactRetryNote(string language) => language == Lang.English
        ? """
          IMPORTANT – second attempt: your previous answer to this task was too long and was cut off at the output limit, so it could not be used.
          This time answer clearly more COMPACTLY so that the JSON object is complete: tables with at most about 30 rows (the most important first; if
          entries are left out because of that, say so in the "Key points first" alert), at most 4 cards per cards_grid with at most 4 bullets each, one
          short sentence per table cell and bullet, no repetition. This instruction takes precedence over "be exhaustive" above.
          """
        : """
          WICHTIG – zweiter Versuch: Deine letzte Antwort auf diese Aufgabe war zu lang und wurde an der Ausgabegrenze abgeschnitten, sie war deshalb unbrauchbar.
          Antworte diesmal deutlich KOMPAKTER, damit das JSON-Objekt vollständig ist: Tabellen mit höchstens etwa 30 Zeilen (die wichtigsten zuerst; fehlen
          dadurch Einträge, sag das im Hinweis „Das Wichtigste vorab“), höchstens 4 Karten je cards_grid mit je höchstens 4 Bullets, ein kurzer Satz je
          Tabellenzelle und Bullet, keine Wiederholungen. Diese Anweisung hat Vorrang vor „Sei ERSCHÖPFEND“ oben.
          """;

    /// <summary>The answer stopped at the provider's output limit: our sentence plus the technical detail (stop reason, model, limit).</summary>
    private static AiProviderException Truncated(string provider, string detail) =>
        new(ServerTexts.Ai.AnswerTruncated(provider), detail, 0, null, provider, kind: AiFailureKind.Other, truncated: true);

    /// <summary>One AI call. <paramref name="research"/> = with live web search (guide research); false = plain generation (translation).</summary>
    private async Task<string> CallProviderAsync(string provider, string prompt, SettingsResponseDto settings, CancellationToken ct, bool research = true) =>
        (await CallProviderAnswerAsync(provider, prompt, settings, ct, research)).Text;

    /// <summary>
    /// One AI call with the sources of its answer. Only Claude's web search delivers sources that may be kept (see <see cref="GuideSources"/>); Gemini's
    /// grounding links are not stored (Gemini API terms, ai.google.dev/gemini-api/terms) and OpenAI researches without web search here.
    /// </summary>
    private async Task<AiAnswer> CallProviderAnswerAsync(string provider, string prompt, SettingsResponseDto settings, CancellationToken ct, bool research = true) => provider switch
    {
        "claude" => await CallClaudeWithWebSearchAsync(prompt, settings.ClaudeModel, ct, research),
        "openai" => new AiAnswer(await CallOpenAiAsync(prompt, settings.OpenAiModel, ct), AiSourceMeta.Empty),
        _ => new AiAnswer(await CallGeminiWithSearchGroundingAsync(prompt, settings.GeminiModel, ct, research), AiSourceMeta.Empty)
    };

    /// <summary>Steam facts that steer the research: genres, release date (new releases are researched differently), achievements.</summary>
    private async Task<GameContext> BuildGameContextAsync(string title, int? steamAppId, string language, CancellationToken ct)
    {
        var english = language == Lang.English;
        var lines = new List<string> { english ? $"Today's date: {DateTime.Now:yyyy-MM-dd}" : $"Heutiges Datum: {DateTime.Now:yyyy-MM-dd}" };
        List<(string ApiName, string DisplayName, string Description)> achievements = [];
        List<string> tags = [];
        string? steamGenres = null;

        if (steamAppId is > 0)
        {
            lines.Add($"Steam-AppID: {steamAppId}");
            // The store facts (genres, categories, release date, description) come in the language of the guide
            var facts = await steamStats.GetStoreFactsAsync(steamAppId.Value, language, ct);
            if (facts is not null)
            {
                if (facts.Genres.Count > 0)
                {
                    steamGenres = string.Join(", ", facts.Genres);
                    lines.Add(english ? $"Genres according to Steam: {steamGenres}" : $"Genres laut Steam: {steamGenres}");
                }
                if (facts.Categories.Count > 0)
                {
                    lines.Add(english
                        ? $"Game modes/features according to Steam: {string.Join(", ", facts.Categories.Take(12))}"
                        : $"Spielmodi/Features laut Steam: {string.Join(", ", facts.Categories.Take(12))}");
                }
                lines.Add(facts.ComingSoon
                    ? english ? $"Release: not released yet ({facts.ReleaseDate})" : $"Release: noch nicht erschienen ({facts.ReleaseDate})"
                    : $"Release: {facts.ReleaseDate}");
                if (facts.Developers.Count > 0) lines.Add(english ? $"Developers: {string.Join(", ", facts.Developers)}" : $"Entwickler: {string.Join(", ", facts.Developers)}");
                if (!string.IsNullOrWhiteSpace(facts.ShortDescription)) lines.Add(english ? $"Short description: {facts.ShortDescription}" : $"Kurzbeschreibung: {facts.ShortDescription}");
                // Whether the game exists in the guide language decides which names the guide uses (see LanguageRules)
                lines.AddRange(LanguageFactLines(facts.Languages ?? [], facts.AudioLanguages ?? [], language));
            }

            tags = await steamStats.GetSteamTagsAsync(steamAppId.Value, ct);
            if (tags.Count > 0)
            {
                lines.Add(english
                    ? $"Steam community tags (sorted by votes): {string.Join(", ", tags)}"
                    : $"Steam-Community-Tags (nach Stimmen sortiert): {string.Join(", ", tags)}");
            }

            lines.AddRange(await BuildIgdbContextLinesAsync(steamAppId.Value, language, ct));

            // Official achievement texts in the guide's language (English = Steam's original texts)
            achievements = await steamClient.FetchSteamAchievementsSchemaAsync(steamAppId.Value, language, ct);
            lines.Add(achievements.Count > 0
                ? english
                    ? $"Official Steam achievements: {achievements.Count} (the app has the list)"
                    : $"Offizielle Steam-Errungenschaften: {achievements.Count} (Liste liegt der App vor)"
                : english
                    ? "Steam achievements: list not available (without a Steam API key) – research them yourself."
                    : "Steam-Errungenschaften: Liste nicht verfügbar (ohne Steam-API-Key) – recherchiere sie selbst.");
        }

        // The wishes: the base text (game, Steam genres - else the top community tags -, PC (Steam)) plus the research blocks the player ticked
        var blocks = await settingsStore.GetResearchBlocksAsync(ct);
        var genre = steamGenres ?? (tags.Count > 0 ? string.Join(", ", tags.Take(3)) : UnknownGenre(language));
        var researchBase = ResearchBlocks.FillBase(language, title, genre, ResearchBlocks.PlatformText);

        return new GameContext(title, string.Join("\n", lines), achievements, DetectProfilesFromTags(tags), blocks, researchBase, language);
    }

    /// <summary>
    /// Extra facts from IGDB (genres, themes, modes, perspectives, average time to beat) when the Twitch credentials are set.
    /// Purely additive: any problem with IGDB just means no extra lines.
    /// </summary>
    private async Task<List<string>> BuildIgdbContextLinesAsync(int steamAppId, string language, CancellationToken ct)
    {
        var english = language == Lang.English;
        var lines = new List<string>();
        if (!igdbService.IsConfigured)
        {
            return lines;
        }

        try
        {
            // IGDB only adds detail; never let it hold up the research for long
            using var igdbTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            igdbTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            if (await igdbService.GetBySteamAppIdAsync(steamAppId, igdbTimeout.Token) is not { } igdb)
            {
                return lines;
            }

            if (igdb.Genres.Count > 0) lines.Add(english ? $"Genres according to IGDB: {string.Join(", ", igdb.Genres)}" : $"Genres laut IGDB: {string.Join(", ", igdb.Genres)}");
            if (igdb.Themes.Count > 0) lines.Add(english ? $"Themes according to IGDB: {string.Join(", ", igdb.Themes)}" : $"Themen laut IGDB: {string.Join(", ", igdb.Themes)}");
            if (igdb.GameModes.Count > 0) lines.Add(english ? $"Game modes according to IGDB: {string.Join(", ", igdb.GameModes)}" : $"Spielmodi laut IGDB: {string.Join(", ", igdb.GameModes)}");
            if (igdb.PlayerPerspectives.Count > 0) lines.Add(english ? $"Perspective according to IGDB: {string.Join(", ", igdb.PlayerPerspectives)}" : $"Perspektive laut IGDB: {string.Join(", ", igdb.PlayerPerspectives)}");

            if (igdb.TimeToBeat is { } ttb)
            {
                var parts = new List<string>();
                if (ttb.HastilyHours is { } hastily) parts.Add(english ? $"main story approx. {FormatHours(hastily, language)} h" : $"Hauptstory ca. {FormatHours(hastily, language)} h");
                if (ttb.NormallyHours is { } normally) parts.Add(english ? $"with side content approx. {FormatHours(normally, language)} h" : $"mit Nebeninhalten ca. {FormatHours(normally, language)} h");
                if (ttb.CompletelyHours is { } completely) parts.Add(english ? $"100 % approx. {FormatHours(completely, language)} h" : $"100 % ca. {FormatHours(completely, language)} h");
                if (parts.Count > 0)
                {
                    var submissions = ttb.Count is { } count ? (english ? $" (according to {count} reports)" : $" (laut {count} Meldungen)") : string.Empty;
                    lines.Add(english
                        ? $"Average playtime according to IGDB: {string.Join(", ", parts)}{submissions}"
                        : $"Durchschnittliche Spielzeit laut IGDB: {string.Join(", ", parts)}{submissions}");
                }
            }
        }
        catch (TwitchApiException ex)
        {
            logger.LogDebug("IGDB-Fakten für den Guide-Kontext von App {AppId} nicht verfügbar: {Message}", steamAppId, ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug("IGDB-Fakten für den Guide-Kontext von App {AppId}: Zeitlimit erreicht.", steamAppId);
        }
        return lines;
    }

    private static string FormatHours(double hours, string language) =>
        hours.ToString(hours >= 10 ? "0" : "0.#", Lang.Culture(language));

    /// <summary>
    /// The tab of a chapter answer (AI or pasted). An answer that says the topic does not exist in the game (top-level "notApplicable": true) throws
    /// <see cref="ChapterNotApplicableException"/> - the caller drops the chapter. For the chapters every guide has (walkthrough, roadmap, tips) such an
    /// answer is never a reason to drop: with real sections the flag is ignored, without them the answer is unusable (the chapter becomes a placeholder).
    /// </summary>
    internal static GuideTabEntity BuildTabFromChapterJson(Guid gameId, int sortOrder, ChapterPlan plan, string raw, NormalizeOptions? options = null, AiSourceMeta? provider = null)
    {
        using var doc = JsonDocument.Parse(ExtractJsonObject(raw));
        var root = doc.RootElement;
        if (ChapterApplicability.Says(root, out var reason))
        {
            if (ChapterApplicability.CanBeDropped(plan.TopicId))
            {
                throw new ChapterNotApplicableException(plan.Title, plan.TopicId, reason);
            }
            if (!(root.TryGetProperty("sections", out var given) && given.ValueKind == JsonValueKind.Array && given.GetArrayLength() > 0))
            {
                throw new LocalizedException(ServerTexts.Ai.ChapterNotApplicableNotAllowed(plan.Title));
            }
        }
        // The title is in the language of the guide only (an AI that adds a translation in brackets is cleaned up)
        var title = ChapterTitles.Clean(Text(root, "title", plan.Title), Lang.Current);
        var sections = root.TryGetProperty("sections", out var sectionsEl) && sectionsEl.ValueKind == JsonValueKind.Array
            ? GuideSectionNormalizer.Normalize(sectionsEl, options)
            : new JsonArray();
        // One "sources" section at the end: the provider's citations first, then the pages the answer names (AI-written ones are never "cited")
        sections = ConsolidateSources(sections, provider, GuideSources.MaxPerBlock);

        return new GuideTabEntity
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            // The walkthrough has a fixed address (one per game, always listed first)
            Slug = plan.TopicId == WalkthroughTab.TopicId ? WalkthroughTab.Slug : $"tab-{sortOrder}-{Slugify(title)}",
            Title = title,
            Icon = Text(root, "icon", plan.Icon),
            SortOrder = sortOrder,
            // The group in the side navigation: the topic's own group, else the group of the planned title
            GroupKey = GuideGroups.KeyFor(plan.TopicId, plan.Title, Lang.Current),
            TopicId = StoredTopicId(plan.TopicId),
            SectionsJson = sections.ToJsonString()
        };
    }

    /// <summary>
    /// Moves every "sources" section of a chapter into ONE at the end, merged with the sources the provider's own metadata names for the answer (cited
    /// first, then the AI-named ones, then a few further search results). Chapters without any source get no section.
    /// </summary>
    internal static JsonArray ConsolidateSources(JsonArray sections, AiSourceMeta? provider, int max)
    {
        var named = new List<GuideSourceDto>();
        var result = new JsonArray();
        foreach (var section in sections.OfType<JsonObject>())
        {
            if (section["type"]?.ToString() == "sources")
            {
                // Already normalized: the origin is what the normalizer allowed
                named.AddRange(GuideSources.FromStored(section["sources"], trustOrigin: true));
                continue;
            }
            result.Add(section.DeepClone());
        }
        var sources = GuideSources.Combine(provider, GuideSources.Distinct(named), max);
        if (sources.Count > 0)
        {
            var list = new JsonArray();
            foreach (var source in sources)
            {
                list.Add(new JsonObject { ["title"] = source.Title, ["url"] = source.Url, ["origin"] = source.Origin });
            }
            result.Add(new JsonObject { ["type"] = "sources", ["sources"] = list });
        }
        return result;
    }

    /// <summary>True for an id of the standard topic list (both languages share the ids).</summary>
    internal static bool IsStandardTopic(string? topicId) =>
        !string.IsNullOrWhiteSpace(topicId) && Topics(Lang.German).Any(t => string.Equals(t.Id, topicId, StringComparison.Ordinal));

    /// <summary>The topic id a tab stores: a standard topic, else null ("extra" and unknown ids say nothing about the chapter).</summary>
    internal static string? StoredTopicId(string? topicId) => IsStandardTopic(topicId) ? topicId : null;

    private static GuideTabEntity BuildFailedTab(Guid gameId, int sortOrder, ChapterPlan plan, Exception error, string language)
    {
        var (message, providerMessage, provider) = FailedChapters.Explain(error, language);
        var sections = new List<GuideSectionDto>
        {
            FailedChapters.CreateSection(language, plan.Focus, plan.TopicId, message, providerMessage, provider)
        };
        return new GuideTabEntity
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            Slug = plan.TopicId == WalkthroughTab.TopicId ? WalkthroughTab.Slug : $"tab-{sortOrder}-{Slugify(plan.Title)}",
            Title = plan.Title,
            Icon = plan.Icon,
            SortOrder = sortOrder,
            GroupKey = GuideGroups.KeyFor(plan.TopicId, plan.Title, language),
            TopicId = StoredTopicId(plan.TopicId),
            SectionsJson = JsonSerializer.Serialize(sections, JsonOptions)
        };
    }

    /// <summary>
    /// The achievement chapter comes from Steam's official list (item id = Steam API name, so unlocks check it off exactly).
    /// The AI only contributes category, missable flag and a tip per achievement; without that answer the list still works.
    /// </summary>
    private async Task<GuideTabEntity?> BuildAchievementsTabAsync(
        Guid gameId,
        int sortOrder,
        GameContext context,
        AiRun run,
        SettingsResponseDto settings,
        CancellationToken ct)
    {
        var language = context.Language;
        var tips = new Dictionary<string, (string Category, bool Missable, string Tip, string Condition)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var raw = await CallContentAsync(run, run.Spread[^1], BuildAchievementTipsPrompt(context), settings, language, "achievement tips", ct);
            using var doc = JsonDocument.Parse(ExtractJsonObject(raw));
            if (doc.RootElement.TryGetProperty("tips", out var tipsEl) && tipsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in tipsEl.EnumerateObject())
                {
                    tips[entry.Name] = (
                        Text(entry.Value, "category", OtherCategory(language)),
                        entry.Value.TryGetProperty("missable", out var m) && m.ValueKind == JsonValueKind.True,
                        Text(entry.Value, "tip", string.Empty),
                        Text(entry.Value, "condition", string.Empty));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Tipps zur Achievement-Liste konnten nicht recherchiert werden - Liste wird ohne Tipps angelegt.");
        }

        var rows = context.Achievements.Select(a =>
        {
            var tip = tips.TryGetValue(a.ApiName, out var t) ? t : (OtherCategory(language), false, string.Empty, string.Empty);
            // Hidden achievements come without a condition from Steam; the researched condition fills the gap
            var description = !string.IsNullOrWhiteSpace(a.Description)
                ? a.Description
                : !string.IsNullOrWhiteSpace(tip.Item4)
                    ? $"{SecretPrefix(language)} {tip.Item4}"
                    : SecretUnknown(language);
            return new GuideTableRowDto(
                ItemId: a.ApiName,
                FilterCategory: tip.Item1,
                FilterSecondary: tip.Item2 ? Missable(language) : Anytime(language),
                Cells: new Dictionary<string, string>
                {
                    ["name"] = a.DisplayName,
                    ["description"] = description,
                    ["category"] = tip.Item1,
                    ["tip"] = tip.Item3
                });
        }).ToList();

        var sections = new List<GuideSectionDto>
        {
            new(
                Type: "alert",
                Variant: "emerald",
                Title: AchievementAlertTitle(rows.Count, language),
                HtmlContent: AchievementAlertText(language)),
            new(
                Type: "checklist_table",
                Table: new GuideTableDto(
                    TableId: "steam-achievements",
                    Caption: AchievementCaption(language),
                    Checkable: true,
                    SearchPlaceholder: AchievementSearch(language),
                    CategoryFilterLabel: AllCategories(language),
                    Categories: rows.Select(r => r.FilterCategory!).Distinct().Order().ToList(),
                    SecondaryFilterLabel: MissableAndAnytime(language),
                    SecondaryCategories: [Missable(language), Anytime(language)],
                    Columns:
                    [
                        new("name", ColumnAchievement(language), IsRowHeader: true),
                        new("description", ColumnCondition(language)),
                        new("category", ColumnCategory(language), BadgeVariant: "s"),
                        new("tip", ColumnTip(language))
                    ],
                    Rows: rows))
        };

        return new GuideTabEntity
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            // Internal id, the same in both languages (nothing looks up this slug, but it is stored in existing guides)
            Slug = $"tab-{sortOrder}-steam-erfolge",
            Title = AchievementTabTitle(sortOrder, language),
            Icon = "mdi:trophy",
            SortOrder = sortOrder,
            GroupKey = GuideGroups.KeyFor(null, AchievementTabTitle(sortOrder, language), language),
            SectionsJson = JsonSerializer.Serialize(sections, JsonOptions)
        };
    }

    private static void PrependIntroAlerts(GuideTabEntity firstTab, string overview, string sourceSituation, string language)
    {
        var sections = JsonNode.Parse(firstTab.SectionsJson) as JsonArray ?? [];
        var intro = new List<GuideSectionDto>();
        if (!string.IsNullOrWhiteSpace(overview))
        {
            intro.Add(new GuideSectionDto(Type: "alert", Variant: "crystal", Title: IntroAlertTitle(language), HtmlContent: overview));
        }
        // The German prompt asks for "etabliert | duenn | neu", the English one for "established | thin | new"
        var isNew = sourceSituation.Trim().ToLowerInvariant() is "neu" or "new";
        var isThin = sourceSituation.Trim().ToLowerInvariant() is "duenn" or "thin";
        if (isNew || isThin)
        {
            intro.Add(new GuideSectionDto(
                Type: "alert",
                Variant: "gold",
                Title: SourceThinTitle(isNew, language),
                HtmlContent: SourceThinText(language)));
        }

        for (var i = intro.Count - 1; i >= 0; i--)
        {
            sections.Insert(0, JsonSerializer.SerializeToNode(intro[i], JsonOptions));
        }
        firstTab.SectionsJson = sections.ToJsonString();
    }

    private static string Text(JsonElement element, string name, string fallback) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var prop) &&
        prop.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(prop.GetString())
            ? prop.GetString()!.Trim()
            : fallback;

    // Output limit of one Gemini request: complete chapters (full lists, not samples) need room; 2.5 models and later allow up to 65k output tokens.
    private const int GeminiMaxOutputTokens = 32768;

    private static bool UsesThinkingLevel(string modelName) =>
        System.Text.RegularExpressions.Regex.IsMatch(modelName, @"^gemini-([3-9]|\d\d)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private async Task<string> CallGeminiWithSearchGroundingAsync(string prompt, string model, CancellationToken ct, bool research = true)
    {
        var apiKey = secretStore.GetSecret(SettingsStore.SecretGemini);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new LocalizedException(ServerTexts.Ai.NoKey("gemini"));
        }

        var client = httpClientFactory.CreateClient("ai");
        var modelName = string.IsNullOrWhiteSpace(model) ? DefaultGeminiModel : model.Trim();
        var url = $"{aiUrls.GeminiBaseUrl}/models/{Uri.EscapeDataString(modelName)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        var contents = new[]
        {
            new
            {
                role = "user",
                parts = new[] { new { text = prompt } }
            }
        };
        // Research uses Google Search grounding (the free tier rejects that tool with 429). A translation needs no search: plain
        // generation, and the answer is asked for as JSON directly.
        object payload = research
            ? new
            {
                contents,
                tools = new object[]
                {
                    new { google_search = new { } }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = GeminiMaxOutputTokens
                }
            }
            : UsesThinkingLevel(modelName)
                // Gemini 3 and later think by default (medium), which makes a plain translation slow; "low" is enough for it.
                // Earlier models reject the field (ai.google.dev/api/generate-content, ThinkingConfig), so it is only sent to those.
                ? new
                {
                    contents,
                    generationConfig = new
                    {
                        temperature = 0.1,
                        maxOutputTokens = GeminiMaxOutputTokens,
                        responseMimeType = "application/json",
                        thinkingConfig = new { thinkingLevel = "LOW" }
                    }
                }
                : new
                {
                    contents,
                    generationConfig = new
                    {
                        temperature = 0.1,
                        maxOutputTokens = GeminiMaxOutputTokens,
                        responseMimeType = "application/json"
                    }
                };

        using var response = await SendToProviderAsync("gemini", () => client.PostAsJsonAsync(url, payload, JsonOptions, ct), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Gemini API Fehler ({Status}): {Body}", response.StatusCode, body);
            throw ProviderFailure("gemini", (int)response.StatusCode, ExtractApiErrorMessage(body), modelName, RetryAfterOf(response, body));
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array &&
            candidates.GetArrayLength() > 0)
        {
            var first = candidates[0];
            // Cut off at maxOutputTokens (ai.google.dev/api/generate-content, FinishReason MAX_TOKENS): the text is incomplete
            if (first.TryGetProperty("finishReason", out var finish) && finish.ValueKind == JsonValueKind.String && finish.GetString() == "MAX_TOKENS")
            {
                logger.LogWarning("Gemini-Antwort ({Model}) wurde bei maxOutputTokens abgeschnitten.", modelName);
                throw Truncated("gemini", $"finishReason: MAX_TOKENS (model {modelName}, maxOutputTokens {GeminiMaxOutputTokens})");
            }
            if (first.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textProp))
                    {
                        sb.Append(textProp.GetString());
                    }
                }
                return sb.ToString();
            }
        }

        throw new LocalizedException(ServerTexts.Ai.NoUsableAnswer("gemini"));
    }

    /// <summary>Sends one provider request; a network failure or timeout becomes an <see cref="AiProviderException"/> with a clear message.</summary>
    private static async Task<HttpResponseMessage> SendToProviderAsync(string provider, Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        try
        {
            return await send();
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            // A TaskCanceledException that nobody asked for is the HttpClient timeout: the provider did not answer in time
            var text = ex is TaskCanceledException ? ServerTexts.Ai.TimedOut(provider) : ServerTexts.Ai.Unreachable(provider);
            throw new AiProviderException(text, ex.GetBaseException().Message, 0, ex, provider, kind: AiFailureKind.Unavailable);
        }
    }

    /// <summary>The provider answered with an error status: our short explanation plus the provider's own text, kept apart.</summary>
    private static AiProviderException ProviderFailure(string provider, int status, string providerMessage, string? model, TimeSpan? retryAfter = null) =>
        new(ServerTexts.Ai.ProviderError(provider, status, providerMessage, model), providerMessage, status, null, provider, retryAfter);

    /// <summary>
    /// How long the provider asked us to wait: the Retry-After header (documented for Anthropic and OpenAI; Anthropic's spend-cap 429 has
    /// none), else the time Google names in its message ("Please retry in 21h35m54.3s.", seen in a real daily-quota answer of Gemini), else
    /// a retry delay field in the error details (Google's RetryInfo; not documented on ai.google.dev, so it is only a bonus).
    /// </summary>
    internal static TimeSpan? RetryAfterOf(HttpResponseMessage response, string body)
    {
        if (response.Headers.RetryAfter is { } header)
        {
            if (header.Delta is { } delta) return delta;
            if (header.Date is { } date) return date - DateTimeOffset.UtcNow;
        }

        var named = System.Text.RegularExpressions.Regex.Match(body, @"[Pp]lease retry in (?:(\d+)h)?(?:(\d+)m)?(?:(\d+(?:\.\d+)?)s)?");
        if (named.Success && named.Length > "Please retry in ".Length)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var hours = named.Groups[1].Success ? double.Parse(named.Groups[1].Value, inv) : 0;
            var minutes = named.Groups[2].Success ? double.Parse(named.Groups[2].Value, inv) : 0;
            var seconds = named.Groups[3].Success ? double.Parse(named.Groups[3].Value, inv) : 0;
            return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        }

        var detail = System.Text.RegularExpressions.Regex.Match(body, @"""retryDelay""\s*:\s*""(\d+(?:\.\d+)?)s""");
        return detail.Success && double.TryParse(detail.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var delay)
            ? TimeSpan.FromSeconds(delay)
            : null;
    }

    /// <summary>
    /// One Claude request (Messages API). No sampling parameters are sent: Claude Haiku 5.5 and Sonnet 5.5 answer a non-default
    /// `temperature` with HTTP 400, the newest models removed it, and the default is fine for a JSON answer (platform.claude.com/docs/en/api/errors,
    /// the claude-api reference "Thinking &amp; Effort"). Research lets Claude search the web with the tool version its model supports
    /// (<see cref="ClaudeWebSearchTool"/>); a translation sends no tool. A search that the server pauses (stop reason `pause_turn`) is
    /// continued by sending the answer so far back, as the API documents.
    /// </summary>
    private async Task<AiAnswer> CallClaudeWithWebSearchAsync(string prompt, string model, CancellationToken ct, bool research = true)
    {
        var apiKey = secretStore.GetSecret(SettingsStore.SecretClaude);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new LocalizedException(ServerTexts.Ai.NoKey("claude"));
        }

        var client = httpClientFactory.CreateClient("ai");
        var modelName = string.IsNullOrWhiteSpace(model) ? "claude-sonnet-4-5" : model.Trim();
        var messages = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = prompt } };
        // Sources of the whole answer: a search turn paused by the server (pause_turn) has its results in the earlier turns
        var cited = new List<GuideSourceDto>();
        var searched = new List<GuideSourceDto>();

        for (var turn = 0; ; turn++)
        {
            var payload = new JsonObject
            {
                ["model"] = modelName,
                ["max_tokens"] = ClaudeMaxTokens(modelName),
                ["messages"] = messages.DeepClone()
            };
            if (research)
            {
                payload["tools"] = new JsonArray
                {
                    new JsonObject { ["type"] = ClaudeWebSearchTool(modelName), ["name"] = "web_search", ["max_uses"] = 5 }
                };
            }

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{aiUrls.ClaudeBaseUrl}/messages");
            requestMessage.Headers.Add("x-api-key", apiKey);
            requestMessage.Headers.Add("anthropic-version", "2023-06-01");
            requestMessage.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

            using var response = await SendToProviderAsync("claude", () => client.SendAsync(requestMessage, ct), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Claude API Fehler ({Status}, Modell {Model}): {Body}", response.StatusCode, modelName, body);
                // error.message of the answer travels as providerMessage ("technical details"), our sentence explains it
                throw ProviderFailure("claude", (int)response.StatusCode, ExtractApiErrorMessage(body), modelName, RetryAfterOf(response, body));
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("content", out var contentArr) || contentArr.ValueKind != JsonValueKind.Array)
            {
                throw new LocalizedException(ServerTexts.Ai.NoUsableAnswer("claude"));
            }

            GuideSources.ReadClaude(contentArr, cited, searched);
            var stopReason = root.TryGetProperty("stop_reason", out var stop) && stop.ValueKind == JsonValueKind.String ? stop.GetString() : null;
            if (stopReason == "pause_turn" && turn < MaxClaudePauseContinuations)
            {
                // The server paused a long search turn: send what Claude produced so far back as its own turn, it carries on
                messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = JsonNode.Parse(contentArr.GetRawText()) });
                continue;
            }
            if (stopReason == "refusal")
            {
                throw new LocalizedException(ServerTexts.Ai.NoUsableAnswer("claude"));
            }
            if (stopReason == "max_tokens")
            {
                logger.LogWarning("Claude-Antwort ({Model}) wurde bei max_tokens abgeschnitten.", modelName);
                // The text stops mid-answer: useless as JSON, so it is no longer returned as if it were complete (see CallContentAsync)
                throw Truncated("claude", $"stop_reason: max_tokens (model {modelName}, max_tokens {ClaudeMaxTokens(modelName)})");
            }

            var sb = new StringBuilder();
            foreach (var block in contentArr.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text" &&
                    block.TryGetProperty("text", out var textProp))
                {
                    sb.Append(textProp.GetString());
                }
            }
            return new AiAnswer(sb.ToString(), new AiSourceMeta(GuideSources.Distinct(cited), GuideSources.Distinct(searched)));
        }
    }

    private const int MaxClaudePauseContinuations = 3;

    /// <summary>
    /// The web search tool version a Claude model supports: the dynamic-filtering one (<c>web_search_20260209</c>) on Opus 5.5/5/4.8/4.7/4.6,
    /// Sonnet 5.5/5/4.6; every other model (Haiku, older ones) keeps the basic <c>web_search_20250305</c> (claude-api reference, "Server Tools").
    /// </summary>
    internal static string ClaudeWebSearchTool(string model) =>
        System.Text.RegularExpressions.Regex.IsMatch(model.Trim().ToLowerInvariant(), @"^claude-(opus-5(-5)?|opus-4-[678]|sonnet-5(-5)?|sonnet-4-6)(?![0-9])")
            ? "web_search_20260209"
            : "web_search_20250305";

    private async Task<string> CallOpenAiAsync(string prompt, string model, CancellationToken ct)
    {
        var apiKey = secretStore.GetSecret(SettingsStore.SecretOpenAi);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new LocalizedException(ServerTexts.Ai.NoKey("openai"));
        }

        var client = httpClientFactory.CreateClient("ai");
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{aiUrls.OpenAiBaseUrl}/chat/completions");
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var modelName = string.IsNullOrWhiteSpace(model) ? "gpt-4o" : model.Trim();
        var payload = new
        {
            model = modelName,
            temperature = 0.2,
            response_format = new { type = "json_object" },
            messages = new[]
            {
                // Ambient language = the guide's language while a guide is being written (see the Lang.Use scopes above)
                new { role = "system", content = Lang.Pick(
                    "Du bist ein 100%-Completionist-Spieleguide-Architekt. Antworte stets als valides JSON-Objekt.",
                    "You are a 100% completionist game guide architect. Always answer as a valid JSON object.") },
                new { role = "user", content = prompt }
            }
        };

        requestMessage.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        using var response = await SendToProviderAsync("openai", () => client.SendAsync(requestMessage, ct), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("OpenAI API Fehler ({Status}): {Body}", response.StatusCode, body);
            throw ProviderFailure("openai", (int)response.StatusCode, ExtractApiErrorMessage(body), modelName, RetryAfterOf(response, body));
        }

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            // finish_reason "length" = stopped at the token limit, the JSON is cut off
            if (first.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String && finish.GetString() == "length")
            {
                logger.LogWarning("OpenAI-Antwort ({Model}) wurde an der Token-Grenze abgeschnitten.", modelName);
                throw Truncated("openai", $"finish_reason: length (model {modelName})");
            }
            if (first.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var contentProp) &&
                !string.IsNullOrWhiteSpace(contentProp.GetString()))
            {
                return contentProp.GetString()!;
            }
        }

        throw new LocalizedException(ServerTexts.Ai.NoUsableAnswer("openai"));
    }

    public async Task<AiProviderModelsResponseDto> ProbeProviderModelsAsync(
        string provider,
        string? apiKeyOverride,
        CancellationToken ct = default)
    {
        var norm = (provider ?? string.Empty).Trim().ToLowerInvariant();
        var key = !string.IsNullOrWhiteSpace(apiKeyOverride)
            ? apiKeyOverride.Trim()
            : norm switch
            {
                "gemini" => secretStore.GetSecret(SettingsStore.SecretGemini),
                "claude" => secretStore.GetSecret(SettingsStore.SecretClaude),
                "openai" => secretStore.GetSecret(SettingsStore.SecretOpenAi),
                _ => null
            };

        if (string.IsNullOrWhiteSpace(key))
        {
            return new AiProviderModelsResponseDto(norm, false, null, []);
        }

        var client = httpClientFactory.CreateClient("ai");

        try
        {
            if (norm == "gemini")
            {
                var url = $"{aiUrls.GeminiBaseUrl}/models?key={Uri.EscapeDataString(key)}";
                using var response = await client.GetAsync(url, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    return ProbeFailure(norm, (int)response.StatusCode, ExtractApiErrorMessage(body));
                }

                var models = new List<AiModelOptionDto>();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("models", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in arr.EnumerateArray())
                    {
                        var rawName = m.TryGetProperty("name", out var np) ? np.GetString() ?? string.Empty : string.Empty;
                        var id = rawName.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? rawName["models/".Length..] : rawName;
                        if (!id.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) || id.Contains("embedding") || id.Contains("imagen") || id.Contains("veo") || id.Contains("tts"))
                        {
                            continue;
                        }

                        var display = m.TryGetProperty("displayName", out var dp) && !string.IsNullOrWhiteSpace(dp.GetString())
                            ? $"{dp.GetString()} ({id})"
                            : id;

                        models.Add(new AiModelOptionDto(id, display));
                    }
                }

                if (models.Count == 0)
                {
                    models.AddRange([
                        new(DefaultGeminiModel, "Gemini 3.8 Flash (gemini-3.8-flash)")
                    ]);
                }

                var sorted = models
                    .DistinctBy(x => x.Id)
                    .OrderByDescending(x => x.Id.Contains("2.5") || x.Id.Contains("3"))
                    .ThenByDescending(x => x.Id.Contains("pro"))
                    .ThenBy(x => x.Id)
                    .ToList();

                return new AiProviderModelsResponseDto(norm, true, null, sorted);
            }

            if (norm == "claude")
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{aiUrls.ClaudeBaseUrl}/models");
                req.Headers.Add("x-api-key", key);
                req.Headers.Add("anthropic-version", "2023-06-01");
                using var response = await client.SendAsync(req, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    return ProbeFailure(norm, (int)response.StatusCode, ExtractApiErrorMessage(body));
                }

                var models = new List<AiModelOptionDto>();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in arr.EnumerateArray())
                    {
                        var id = m.TryGetProperty("id", out var ip) ? ip.GetString() ?? string.Empty : string.Empty;
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        var display = m.TryGetProperty("display_name", out var dp) && !string.IsNullOrWhiteSpace(dp.GetString())
                            ? $"{dp.GetString()} ({id})"
                            : id;
                        models.Add(new AiModelOptionDto(id, display));
                    }
                }

                if (models.Count == 0)
                {
                    models.AddRange([
                        new("claude-sonnet-4-5", "Claude Sonnet 4.5 (claude-sonnet-4-5)"),
                        new("claude-opus-4-1", "Claude Opus 4.1 (claude-opus-4-1)"),
                        new("claude-3-7-sonnet-latest", "Claude 3.7 Sonnet (claude-3-7-sonnet-latest)")
                    ]);
                }

                return new AiProviderModelsResponseDto(norm, true, null, models.DistinctBy(x => x.Id).ToList());
            }

            if (norm == "openai")
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{aiUrls.OpenAiBaseUrl}/models");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var response = await client.SendAsync(req, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    return ProbeFailure(norm, (int)response.StatusCode, ExtractApiErrorMessage(body));
                }

                var models = new List<AiModelOptionDto>();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in arr.EnumerateArray())
                    {
                        var id = m.TryGetProperty("id", out var ip) ? ip.GetString() ?? string.Empty : string.Empty;
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        if ((id.StartsWith("gpt-4", StringComparison.OrdinalIgnoreCase) ||
                             id.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
                             id.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
                             id.StartsWith("o4", StringComparison.OrdinalIgnoreCase)) &&
                            !id.Contains("audio") && !id.Contains("realtime") && !id.Contains("tts") && !id.Contains("transcribe") && !id.Contains("image"))
                        {
                            models.Add(new AiModelOptionDto(id, id));
                        }
                    }
                }

                if (models.Count == 0)
                {
                    models.AddRange([
                        new("gpt-4o", "gpt-4o"),
                        new("gpt-4.1", "gpt-4.1"),
                        new("o3-mini", "o3-mini")
                    ]);
                }

                var sorted = models
                    .DistinctBy(x => x.Id)
                    .OrderByDescending(x => x.Id.StartsWith("gpt-5") || x.Id.StartsWith("gpt-4.1") || x.Id == "gpt-4o")
                    .ThenBy(x => x.Id)
                    .ToList();

                return new AiProviderModelsResponseDto(norm, true, null, sorted);
            }

            return new AiProviderModelsResponseDto(norm, false, ServerTexts.Ai.UnknownProvider, []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Network failure, timeout, unreadable answer: our sentence for the cause, the technical text apart
            var network = ex is HttpRequestException or TaskCanceledException;
            return new AiProviderModelsResponseDto(
                norm,
                false,
                network ? ServerTexts.Ai.Unreachable(norm) : ServerTexts.Ai.NoUsableAnswer(norm),
                [],
                ex.GetBaseException().Message);
        }
    }

    /// <summary>The model list request was refused: ErrorMessage = what happened and what to do, ProviderMessage = the provider's own text.</summary>
    private static AiProviderModelsResponseDto ProbeFailure(string provider, int status, string providerMessage) =>
        new(provider, false, ServerTexts.Ai.ProviderError(provider, status, providerMessage, null), [], providerMessage);

    /// <summary>
    /// Output limit of one non-streaming request: 16000 for the 5.x models (they allow up to 128K, but a long non-streaming answer risks an HTTP
    /// timeout; adaptive thinking counts against it too), 32000 for the 4.x models as before, 8192 for older ones.
    /// </summary>
    internal static int ClaudeMaxTokens(string model)
    {
        var name = model.Trim().ToLowerInvariant();
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^claude-(opus|sonnet|haiku|fable|mythos)-5")) return 16000;
        return System.Text.RegularExpressions.Regex.IsMatch(name, @"claude-(opus|sonnet|haiku)-4|claude-.*-4-") ? 32000 : 8192;
    }

    /// <summary>
    /// The first COMPLETE JSON object of an AI answer (braces inside strings do not count). Models sometimes add a sentence or a second object
    /// after the data; "from the first { to the last }" then yields invalid JSON ("Expected depth to be zero ..."). When no object closes
    /// (the answer was cut off), the old span is returned so the parser reports what is wrong.
    /// </summary>
    internal static string ExtractJsonObject(string text)
    {
        var start = FindObjectStart(text);
        if (start < 0)
        {
            throw new LocalizedException(ServerTexts.Ai.AnswerNotJson);
        }

        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }
            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return text[start..(i + 1)];
                    }
                    break;
            }
        }

        var end = text.LastIndexOf('}');
        if (end > start)
        {
            return text[start..(end + 1)];
        }
        throw new LocalizedException(ServerTexts.Ai.AnswerNotJson);
    }

    /// <summary>
    /// Where the JSON object of an answer starts: the first "{" that is followed by a key (or closes at once) - so a sentence like
    /// "{in curly brackets}" before the data is not taken for it. Without such a place the first "{" counts; -1 when there is none.
    /// </summary>
    internal static int FindObjectStart(string text)
    {
        var first = -1;
        for (var i = text.IndexOf('{'); i >= 0; i = text.IndexOf('{', i + 1))
        {
            first = first < 0 ? i : first;
            var next = i + 1;
            while (next < text.Length && char.IsWhiteSpace(text[next]))
            {
                next++;
            }
            if (next < text.Length && text[next] is '"' or '}')
            {
                return i;
            }
        }
        return first;
    }

    /// <summary>Model used when none is chosen: Google's own 404 for gemini-2.5-flash names it as the replacement for new users.</summary>
    public const string DefaultGeminiModel = "gemini-3.8-flash";

    /// <summary>
    /// Message = our short explanation (language of the request), ProviderMessage = Google's own text (English, technical) or null
    /// when the probe worked. The UI shows the explanation and folds the provider text under "technical details".
    /// </summary>
    public sealed record GeminiProbeResult(bool Ok, int Status, string Message, string? ProviderMessage = null);

    public sealed record GeminiDiagnosis(string Model, GeminiProbeResult Plain, GeminiProbeResult Search);

    /// <summary>
    /// Two tiny real requests ("Antworte nur mit OK") without and with Google Search, so the settings can show what the stored
    /// key may really use. Costs two minimal requests; nothing is stored.
    /// </summary>
    public async Task<GeminiDiagnosis> DiagnoseGeminiAsync(string? model, CancellationToken ct)
    {
        var apiKey = secretStore.GetSecret(SettingsStore.SecretGemini);
        var modelName = string.IsNullOrWhiteSpace(model) ? DefaultGeminiModel : model.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var none = new GeminiProbeResult(false, 0, ServerTexts.Ai.DiagnoseNoKey);
            return new GeminiDiagnosis(modelName, none, none);
        }

        var client = httpClientFactory.CreateClient("ai");
        var url = $"{aiUrls.GeminiBaseUrl}/models/{Uri.EscapeDataString(modelName)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        async Task<GeminiProbeResult> ProbeAsync(bool withSearch)
        {
            object payload = withSearch
                ? new
                {
                    contents = new[] { new { role = "user", parts = new[] { new { text = "Reply only with: OK" } } } },
                    tools = new object[] { new { google_search = new { } } },
                    generationConfig = new { maxOutputTokens = 16 },
                }
                : new
                {
                    contents = new[] { new { role = "user", parts = new[] { new { text = "Reply only with: OK" } } } },
                    generationConfig = new { maxOutputTokens = 16 },
                };
            try
            {
                using var response = await client.PostAsJsonAsync(url, payload, JsonOptions, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                {
                    return new GeminiProbeResult(true, (int)response.StatusCode, withSearch ? ServerTexts.Ai.DiagnoseSearchOk : ServerTexts.Ai.DiagnosePlainOk);
                }

                var providerMessage = ExtractApiErrorMessage(body);
                return new GeminiProbeResult(
                    false,
                    (int)response.StatusCode,
                    ServerTexts.Ai.ProviderError("gemini", (int)response.StatusCode, providerMessage, modelName),
                    providerMessage);
            }
            catch (HttpRequestException ex)
            {
                return new GeminiProbeResult(false, 0, ServerTexts.Ai.DiagnoseUnreachable, ex.Message);
            }
        }

        var plain = await ProbeAsync(withSearch: false);
        var search = await ProbeAsync(withSearch: true);
        return new GeminiDiagnosis(modelName, plain, search);
    }

    private static string ExtractApiErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var msg))
                {
                    return msg.GetString() ?? body;
                }
            }
        }
        catch
        {
            // Ignore parse errors on error payloads.
        }
        // Not a JSON error: the raw body is the explanation. Shown complete (the UI folds it under "technical details"); only a runaway body is cut
        return body.Length > 20_000 ? body[..20_000] : body;
    }

    public static string Slugify(string value)
    {
        var lower = value.Trim().ToLowerInvariant()
            .Replace("ä", "ae")
            .Replace("ö", "oe")
            .Replace("ü", "ue")
            .Replace("ß", "ss");
        var sb = new StringBuilder();
        var prevDash = false;
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                prevDash = false;
            }
            else if (!prevDash && sb.Length > 0)
            {
                sb.Append('-');
                prevDash = true;
            }
        }
        return sb.ToString().Trim('-');
    }
}
