using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Configuration;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Companion.Api.Tests;

/// <summary>
/// Chapters fit the game (Little Big Workshop, 2026-11-02: a "bestiary" chapter in a factory sim, PlayStation trophy talk in a Steam guide, 11 of 14
/// chapters cut off at the output limit): a chapter the research says the game does not have is dropped on every path (new guide / retry,
/// "complete guide", the manual subscription route), the Steam/PC rule is in every content prompt, and a cut-off answer is asked for once more,
/// more compactly. All AI answers come from fake providers (the Companion:Ai:*BaseUrl settings), nothing reaches a real provider.
/// </summary>
public sealed class ChapterApplicabilityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pv-applicability-" + Guid.NewGuid().ToString("N"));
    private readonly AiProviderHealth _health = new(new ManualClock(Fakes.Start));
    private readonly SecretStore _secrets;
    // provider -> (prompt -> raw HTTP answer); every request is recorded with its prompt
    private readonly Dictionary<string, Func<string, HttpResponseMessage>> _providers = [];
    private readonly List<(string Provider, string Prompt)> _requests = [];

    public ChapterApplicabilityTests()
    {
        Directory.CreateDirectory(_dir);
        _secrets = new SecretStore(DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_dir, "keys"))), Options.Create(new CompanionOptions { DataDir = _dir }));
        _secrets.SetSecret(SettingsStore.SecretGemini, "gemini-secret");
        _secrets.SetSecret(SettingsStore.SecretClaude, "claude-secret");
        _secrets.SetSecret(SettingsStore.SecretOpenAi, "openai-secret");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* temp files; the OS cleans up */ }
    }

    // ---- fake providers ----------------------------------------------------------------------------------------------------

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private sealed class StubFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHandler(respond));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Gemini(string text, string finishReason = "STOP") =>
        Json(new JsonObject { ["candidates"] = new JsonArray(new JsonObject { ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = text }) }, ["finishReason"] = finishReason }) }.ToJsonString());

    private static HttpResponseMessage Claude(string text, string stopReason = "end_turn") =>
        Json(new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["stop_reason"] = stopReason }.ToJsonString());

    private static HttpResponseMessage OpenAi(string text, string finishReason = "stop") =>
        Json(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["content"] = text }, ["finish_reason"] = finishReason }) }.ToJsonString());

    /// <summary>The prompt text of a request to any of the three providers.</summary>
    private static string PromptOf(string provider, JsonNode body) => provider switch
    {
        "gemini" => body["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>(),
        "claude" => body["messages"]![0]!["content"]!.GetValue<string>(),
        _ => body["messages"]![1]!["content"]!.GetValue<string>()
    };

    private AiGuideGeneratorService Generator()
    {
        var factory = new StubFactory(request =>
        {
            var provider = request.RequestUri!.Host.Split('.')[0];
            var body = JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!;
            var prompt = PromptOf(provider, body);
            lock (_requests)
            {
                _requests.Add((provider, prompt));
            }
            return _providers[provider](prompt);
        });
        var options = Options.Create(new CompanionOptions
        {
            DataDir = _dir,
            Ai = new AiProviderUrls { GeminiBaseUrl = "http://gemini.test/v1beta", ClaudeBaseUrl = "http://claude.test/v1", OpenAiBaseUrl = "http://openai.test/v1" }
        });
        return new AiGuideGeneratorService(factory, null!, _secrets, null!, null!, null!, options, null!, NullLogger<AiGuideGeneratorService>.Instance);
    }

    private static SettingsResponseDto Settings(string claudeModel = "claude-haiku-5-5") => new(
        GeminiModel: "gemini-3.8-flash", ClaudeModel: claudeModel, OpenAiModel: "gpt-4o", SteamUserId: "", AutoDetectGame: true,
        PlaySounds: false, SoundVolume: 0, SoundTheme: "kristall", PopupPosition: "top-right", Language: "german", UiLanguage: "de", AutoTranslate: true,
        HasGeminiKey: true, GeminiKeyHint: "", HasClaudeKey: true, ClaudeKeyHint: "", HasOpenAiKey: true, OpenAiKeyHint: "", HasSteamKey: false, SteamKeyHint: "",
        HasTwitchKey: false, TwitchKeyHint: "", TwitchStreamsEnabled: false, AchievementNotesEnabled: true,
        AiSelectionMode: AiSelectionMode.Default, AiDefaultProvider: "gemini", AiAutoFallback: true,
        ResearchBlocks: new ResearchBlocksDto(null, [], null, null), ResearchBlocksPolicy: ResearchBlocks.Policy());

    private AiRun Run(string defaultProvider = "gemini") =>
        AiRouter.Plan(new AiSelection(AiSelectionMode.Default, defaultProvider, true), AiProvider.All, true, null, false, _health,
            p => AiKeys.Fingerprint(_secrets.GetSecret(AiRouter.SecretOf(p))));

    private static AiGuideGeneratorService.GameContext Context(string language = Lang.German, string title = "Little Big Workshop") =>
        new(title, language == Lang.English ? "Steam AppID: 574720\nGenres according to Steam: Simulation, Strategy" : "Steam-AppID: 574720\nGenres laut Steam: Simulation, Strategie",
            [], [], ResearchBlocks.Selection.Empty, ResearchBlocks.FillBase(language, title, "Simulation", ResearchBlocks.PlatformText), language);

    private static GameEntity Game(string language = Lang.German) =>
        new() { Id = Guid.NewGuid(), Slug = "little-big-workshop", Title = "Little Big Workshop", SteamAppId = 574720, ContentLanguage = language, Tabs = [] };

    // ---- answers -------------------------------------------------------------------------------------------------------------

    private static string ChapterAnswer(string title) => new JsonObject
    {
        ["title"] = title,
        ["icon"] = "mdi:factory",
        ["sections"] = new JsonArray(new JsonObject { ["type"] = "alert", ["variant"] = "gold", ["title"] = "Das Wichtigste vorab", ["htmlContent"] = $"Inhalt zu {title}." })
    }.ToJsonString();

    private const string NotApplicableAnswer = """{ "notApplicable": true, "reason": "Little Big Workshop hat keine Gegner und keine Bosse – es ist eine Fabrik-Simulation ohne Kampf." }""";

    /// <summary>Chapter answers by the chapter title in the prompt: the bestiary does not exist in a factory sim, everything else does.</summary>
    private static string AnswerFor(string prompt)
    {
        if (prompt.Contains("Chef-Redakteur", StringComparison.Ordinal))
        {
            // The blueprint (wrongly) confirms the bestiary; the chapter research then says it does not exist
            return new JsonObject
            {
                ["sourceSituation"] = "etabliert",
                ["profiles"] = new JsonArray("management"),
                ["topics"] = new JsonArray(
                    new JsonObject { ["id"] = "roadmap", ["applicable"] = true, ["priority"] = 1, ["title"] = "Roadmap & Verpassbares", ["focus"] = "Reihenfolge" },
                    new JsonObject { ["id"] = "bestiary", ["applicable"] = true, ["priority"] = 2, ["title"] = "Gegner, Bosse & Bestiarium", ["focus"] = "Gegner" },
                    new JsonObject { ["id"] = "economy", ["applicable"] = true, ["priority"] = 1, ["title"] = "Geld, Verträge & Kosten", ["focus"] = "Geld" })
            }.ToJsonString();
        }
        var match = System.Text.RegularExpressions.Regex.Match(prompt, "Kapitel: „([^“]+)“");
        var title = match.Success ? match.Groups[1].Value : "Kapitel";
        return title.Contains("Bestiarium", StringComparison.Ordinal) ? NotApplicableAnswer : ChapterAnswer(title);
    }

    private sealed class RecordingSink : IAiChapterSink
    {
        public List<GuideTabEntity> Saved { get; } = [];

        public Task GameReadyAsync(GameEntity game, CancellationToken ct) => Task.CompletedTask;

        public Task ChapterDoneAsync(GuideTabEntity tab, CancellationToken ct)
        {
            lock (Saved)
            {
                Saved.Add(tab);
            }
            return Task.CompletedTask;
        }
    }

    private static AiGuideGeneratorService.ChapterPlan Plan(string title, string topicId) => new(title, "mdi:star", "Auftrag zu " + title, topicId);

    // ---- A: dropped chapters -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task New_guide_and_retry_path_drop_a_chapter_the_game_does_not_have_and_save_the_others()
    {
        _providers["gemini"] = prompt => Gemini(AnswerFor(prompt));
        var saved = new List<int>();
        var dropped = new List<int>();
        List<AiGuideGeneratorService.ChapterPlan> plans = [Plan("Roadmap & Verpassbares", "roadmap"), Plan("Gegner, Bosse & Bestiarium", "bestiary"), Plan("Geld, Verträge & Kosten", "economy")];

        var results = await Generator().ResearchChaptersAsync(Guid.NewGuid(), plans, 1, Context(), "etabliert", Run(), Settings(), null,
            (index, _) => { lock (saved) saved.Add(index); return Task.CompletedTask; }, CancellationToken.None, null,
            index => { lock (dropped) dropped.Add(index); return Task.CompletedTask; });

        Assert.Equal(3, results.Length);
        Assert.Null(results[1]);                                   // no tab, no placeholder
        Assert.NotNull(results[0]);
        Assert.NotNull(results[2]);
        Assert.Null(FailedChapters.Read(results[2]!.SectionsJson)); // real content, not an error box
        Assert.Equal([0, 2], saved.Order());
        Assert.Equal([1], dropped);
    }

    [Fact]
    public async Task Complete_guide_drops_a_planned_chapter_the_game_does_not_have_and_names_it()
    {
        _providers["gemini"] = prompt => Gemini(AnswerFor(prompt));
        var sink = new RecordingSink();

        var (tabs, droppedTitles) = await Generator().CompleteGuideWithContextAsync(Game(), Context(), Run(), Settings(), 1, null, null, sink, CancellationToken.None);

        Assert.DoesNotContain(tabs, t => t.Title.Contains("Bestiarium", StringComparison.Ordinal));
        Assert.Equal(["Gegner, Bosse & Bestiarium"], droppedTitles);
        Assert.Contains(tabs, t => t.Title == "Geld, Verträge & Kosten");
        Assert.DoesNotContain(sink.Saved, t => t.Title.Contains("Bestiarium", StringComparison.Ordinal));
        Assert.Equal(tabs.Count, sink.Saved.Count);
    }

    [Fact]
    public async Task The_chapter_prompt_offers_the_not_applicable_answer_only_for_topics_that_may_be_dropped()
    {
        _providers["gemini"] = prompt => Gemini(AnswerFor(prompt));
        await Generator().ResearchChaptersAsync(Guid.NewGuid(), [Plan("Gegner, Bosse & Bestiarium", "bestiary"), Plan("Roadmap & Verpassbares", "roadmap")], 1, Context(), "etabliert", Run(), Settings(), null, null, CancellationToken.None);

        var bestiary = _requests.Single(r => r.Prompt.Contains("Kapitel: „Gegner", StringComparison.Ordinal)).Prompt;
        var roadmap = _requests.Single(r => r.Prompt.Contains("Kapitel: „Roadmap", StringComparison.Ordinal)).Prompt;
        Assert.Contains("\"notApplicable\": true", bestiary);
        Assert.DoesNotContain("\"notApplicable\"", roadmap);
    }

    [Fact]
    public void A_roadmap_answered_with_not_applicable_and_no_sections_is_unusable_not_dropped()
    {
        var error = Assert.Throws<LocalizedException>(() =>
            AiGuideGeneratorService.BuildTabFromChapterJson(Guid.NewGuid(), 1, Plan("Roadmap & Verpassbares", "roadmap"), NotApplicableAnswer));
        Assert.Contains("gehört aber zu jedem Guide", error.Text.De);
    }

    [Theory]
    [InlineData("walkthrough")]
    [InlineData("roadmap")]
    [InlineData("tips")]
    public void A_chapter_every_guide_has_keeps_its_real_sections_even_when_a_stray_flag_says_not_applicable(string topicId)
    {
        var answer = JsonNode.Parse(ChapterAnswer("Komplettlösung"))!.AsObject();
        answer["notApplicable"] = true;

        var tab = AiGuideGeneratorService.BuildTabFromChapterJson(Guid.NewGuid(), 1, Plan("Komplettlösung", topicId), answer.ToJsonString());

        Assert.Contains("Inhalt zu Komplettl", tab.SectionsJson);   // stored JSON escapes umlauts
        if (topicId == "walkthrough")
        {
            Assert.Equal(WalkthroughTab.Slug, tab.Slug);
        }
    }

    [Theory]
    // Only the boolean true at the top level counts: a string, a nested flag or false never removes a chapter
    [InlineData("""{ "notApplicable": "true", "title": "X", "sections": [ { "type": "alert", "variant": "gold", "title": "a", "htmlContent": "b" } ] }""")]
    [InlineData("""{ "notApplicable": false, "title": "X", "sections": [ { "type": "alert", "variant": "gold", "title": "a", "htmlContent": "b" } ] }""")]
    [InlineData("""{ "title": "X", "sections": [ { "type": "alert", "variant": "gold", "title": "a", "htmlContent": "b", "notApplicable": true } ] }""")]
    public void A_parser_quirk_never_drops_a_chapter(string answer)
    {
        var tab = AiGuideGeneratorService.BuildTabFromChapterJson(Guid.NewGuid(), 1, Plan("Gegner, Bosse & Bestiarium", "bestiary"), answer);
        Assert.NotNull(tab);
    }

    [Fact]
    public void The_intro_goes_to_the_first_chapter_that_can_never_be_dropped()
    {
        Assert.Equal(1, AiGuideGeneratorService.IntroChapterIndex([Plan("Früher Start", "earlyop"), Plan("Roadmap", "roadmap")]));
        Assert.Equal(0, AiGuideGeneratorService.IntroChapterIndex([Plan("Komplettlösung", "walkthrough"), Plan("Roadmap", "roadmap")]));
        Assert.Equal(0, AiGuideGeneratorService.IntroChapterIndex([Plan("Geld", "economy"), Plan("Extra", "extra")]));
    }

    // ---- A: the manual subscription route --------------------------------------------------------------------------------------

    private static AiGuideGeneratorService ManualGenerator() =>
        new(null!, null!, null!, null!, null!, null!, Options.Create(new CompanionOptions()), null!, NullLogger<AiGuideGeneratorService>.Instance);

    private static string Pasted(string json, int appId = 574720)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node["game"] = "Little Big Workshop";
        node["steamAppId"] = appId;
        return "```json\n" + node.ToJsonString() + "\n```";
    }

    [Fact]
    public void Manual_a_not_applicable_answer_is_valid_and_says_that_saving_creates_no_chapter()
    {
        using var _ = Lang.Use(Lang.German);
        var check = ManualGenerator().CheckManualChapter(Game(), Pasted(NotApplicableAnswer), "Gegner, Bosse & Bestiarium", "bestiary");

        Assert.True(check.Dto.Ok);
        Assert.Null(check.Dto.Preview);
        Assert.NotNull(check.CanonicalJson);
        Assert.Contains("keine Gegner und keine Bosse", check.Dto.NotApplicable);
        Assert.Contains("kein Kapitel angelegt", check.Dto.NotApplicable);
    }

    [Fact]
    public void Manual_for_a_failed_chapter_it_says_the_placeholder_is_removed_in_the_language_of_the_UI()
    {
        using var _ = Lang.Use(Lang.English);
        var check = ManualGenerator().CheckManualChapter(Game(), Pasted(NotApplicableAnswer), "Enemies", "bestiary", replacesPlaceholder: true);

        Assert.True(check.Dto.Ok);
        Assert.Contains("removes the failed chapter", check.Dto.NotApplicable);
    }

    [Fact]
    public void Manual_a_not_applicable_answer_for_the_roadmap_is_an_error_and_another_game_is_still_caught()
    {
        using var _ = Lang.Use(Lang.German);
        var roadmap = ManualGenerator().CheckManualChapter(Game(), Pasted(NotApplicableAnswer), "Roadmap & Verpassbares", "roadmap");
        Assert.False(roadmap.Dto.Ok);
        Assert.Contains(roadmap.Dto.Errors, e => e.Code == "notApplicableNotAllowed");

        var otherGame = ManualGenerator().CheckManualChapter(Game(), Pasted(NotApplicableAnswer, appId: 1005900), "Gegner", "bestiary");
        Assert.False(otherGame.Dto.Ok);
        Assert.Contains(otherGame.Dto.Errors, e => e.Code == "wrongGame");
    }

    [Fact]
    public void Manual_the_chapter_prompt_shows_the_not_applicable_form_except_for_roadmap_tips_and_walkthrough()
    {
        var bestiary = AiGuideGeneratorService.ComposeManualChapterPrompt(Context(), Game(), Lang.German, "Gegner", "Gegner", "dünn", "bestiary");
        var extra = AiGuideGeneratorService.ComposeManualChapterPrompt(Context(), Game(), Lang.German, "Arbeiter", "Arbeiter", "dünn");
        Assert.Contains("\"notApplicable\": true", bestiary);
        Assert.Contains("\"steamAppId\": 574720, \"notApplicable\": true", bestiary);
        Assert.Contains("\"notApplicable\": true", extra);
        foreach (var topic in new[] { "roadmap", "tips", "walkthrough" })
        {
            Assert.DoesNotContain("notApplicable", AiGuideGeneratorService.ComposeManualChapterPrompt(Context(), Game(), Lang.German, "X", "Y", "dünn", topic));
        }
    }

    // ---- B: Steam / PC only ----------------------------------------------------------------------------------------------------

    public static TheoryData<string> BothLanguages => [Lang.German, Lang.English];

    [Theory]
    [MemberData(nameof(BothLanguages))]
    public void Every_content_prompt_carries_the_Steam_PC_rule(string language)
    {
        var context = Context(language);
        var game = Game(language);
        var outline = new AiGuideGeneratorService.WalkthroughOutline(["Quelle"], [new("Teil 1", "Fokus")]);
        var prompts = new Dictionary<string, string>
        {
            ["blueprint"] = AiGuideGeneratorService.BuildBlueprintPrompt(context, null, []),
            ["chapter"] = AiGuideGeneratorService.BuildChapterPrompt(context, "Kapitel", "Auftrag", "etabliert", "economy"),
            ["walkthrough outline"] = AiGuideGeneratorService.BuildWalkthroughOutlinePrompt(context, "Auftrag", "etabliert", 6),
            ["walkthrough part"] = AiGuideGeneratorService.BuildWalkthroughPartPrompt(context, outline, 0, []),
            ["walkthrough single"] = AiGuideGeneratorService.BuildWalkthroughSinglePrompt(context, "Komplettlösung", "Auftrag", "etabliert"),
            ["achievement tips"] = AiGuideGeneratorService.BuildAchievementTipsPrompt(context with { Achievements = [("A", "Erster", "Tu etwas")] }),
            ["manual blueprint"] = AiGuideGeneratorService.ComposeManualBlueprintPrompt(context, game, language),
            ["manual chapter"] = AiGuideGeneratorService.ComposeManualChapterPrompt(context, game, language, "Kapitel", "Auftrag", "etabliert", "economy"),
        };
        foreach (var part in AiGuideGeneratorService.LoreParts(language))
        {
            prompts["lore " + part.Title] = AiGuideGeneratorService.BuildLorePrompt(context, part);
        }

        var rule = AiGuideGeneratorService.PlatformRules(language);
        foreach (var (name, prompt) in prompts)
        {
            Assert.True(prompt.Contains(rule, StringComparison.Ordinal), $"The {name} prompt ({language}) lacks the Steam/PC rule.");
        }
    }

    [Theory]
    [InlineData(Lang.German, "STEAM-Version auf dem PC", "PSNProfiles", "Platin-Trophäe", "Tastatur und Maus")]
    [InlineData(Lang.English, "STEAM version on PC", "PSNProfiles", "platinum trophy", "keyboard and mouse")]
    public void The_Steam_PC_rule_names_achievements_controls_console_content_and_sources(string language, params string[] phrases)
    {
        var rule = AiGuideGeneratorService.PlatformRules(language);
        Assert.All(phrases, phrase => Assert.Contains(phrase, rule));
    }

    // ---- D: answers cut off at the output limit ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_Claude_answer_cut_at_max_tokens_is_asked_for_once_more_compactly_at_the_same_provider()
    {
        var calls = 0;
        _providers["claude"] = _ => ++calls == 1 ? Claude("{ \"title\": \"Geld\", \"sections\": [ { \"type\": \"alert\"", "max_tokens") : Claude(ChapterAnswer("Geld"));

        var raw = await Generator().CallContentAsync(Run("claude"), "claude", "Kapitel: „Geld“", Settings(), Lang.German, "Geld", CancellationToken.None);

        Assert.Contains("Inhalt zu Geld", raw);
        Assert.Equal(2, _requests.Count);
        Assert.All(_requests, r => Assert.Equal("claude", r.Provider));       // a cut-off answer never switches provider
        Assert.DoesNotContain("zweiter Versuch", _requests[0].Prompt);
        Assert.Contains("WICHTIG – zweiter Versuch", _requests[1].Prompt);
        Assert.Contains("höchstens etwa 30 Zeilen", _requests[1].Prompt);
    }

    [Fact]
    public async Task A_second_cut_off_answer_ends_with_a_clear_message_that_names_the_way_out()
    {
        _providers["claude"] = _ => Claude("{ \"title\": \"Geld\", \"sections\": [", "max_tokens");

        var error = await Assert.ThrowsAsync<AiProviderException>(() =>
            Generator().CallContentAsync(Run("claude"), "claude", "Kapitel: „Geld“", Settings(), Lang.German, "Geld", CancellationToken.None));

        Assert.True(error.IsTruncated);
        Assert.Equal(AiFailureKind.Other, error.Kind);
        Assert.Equal("claude", error.Provider);
        Assert.Contains("auch im zweiten, kompakteren Versuch", error.Text.De);
        Assert.Contains("Kompakt", error.Text.De);
        Assert.Contains("max_tokens 16000", error.ProviderMessage);
        Assert.Equal(2, _requests.Count);
    }

    [Fact]
    public async Task Gemini_MAX_TOKENS_and_OpenAI_length_are_recognised_as_cut_off_too()
    {
        _providers["gemini"] = _ => Gemini("{ \"title\": \"Geld\"", "MAX_TOKENS");
        _providers["openai"] = _ => OpenAi("{ \"title\": \"Geld\"", "length");
        var generator = Generator();

        var gemini = await Assert.ThrowsAsync<AiProviderException>(() => generator.CallAsync(Run("gemini"), "gemini", "p", Settings(), CancellationToken.None));
        var openAi = await Assert.ThrowsAsync<AiProviderException>(() => generator.CallAsync(Run("openai"), "openai", "p", Settings(), CancellationToken.None));

        Assert.True(gemini.IsTruncated);
        Assert.Contains("MAX_TOKENS", gemini.ProviderMessage);
        Assert.True(openAi.IsTruncated);
        Assert.Contains("finish_reason: length", openAi.ProviderMessage);
        // Neither handed the request to another provider
        Assert.Equal(["gemini", "openai"], _requests.Select(r => r.Provider));
    }

    [Fact]
    public async Task A_chapter_whose_first_answer_was_cut_off_is_saved_from_the_compact_second_answer()
    {
        var calls = 0;
        _providers["gemini"] = prompt => Interlocked.Increment(ref calls) == 1 ? Gemini("{ \"title\": \"Geld\", \"sections\": [", "MAX_TOKENS") : Gemini(AnswerFor(prompt));

        var results = await Generator().ResearchChaptersAsync(Guid.NewGuid(), [Plan("Geld, Verträge & Kosten", "economy")], 1, Context(), "etabliert", Run(), Settings(), null, null, CancellationToken.None);

        var tab = Assert.Single(results);
        Assert.NotNull(tab);
        Assert.Null(FailedChapters.Read(tab!.SectionsJson));
        Assert.Contains("Inhalt zu Geld, Vertr", tab.SectionsJson);
    }

    [Fact]
    public async Task A_chapter_cut_off_twice_becomes_a_placeholder_with_the_explanation_and_the_technical_detail()
    {
        _providers["gemini"] = _ => Gemini("{ \"title\": \"Geld\", \"sections\": [", "MAX_TOKENS");

        var results = await Generator().ResearchChaptersAsync(Guid.NewGuid(), [Plan("Geld, Verträge & Kosten", "economy")], 1, Context(), "etabliert", Run(), Settings(), null, null, CancellationToken.None);

        var failure = FailedChapters.Read(Assert.Single(results)!.SectionsJson);
        Assert.NotNull(failure);
        Assert.Contains("zu lang", failure!.Message);
        Assert.Contains("MAX_TOKENS", failure.ProviderMessage);
        Assert.Equal("economy", failure.TopicId);
    }
}
