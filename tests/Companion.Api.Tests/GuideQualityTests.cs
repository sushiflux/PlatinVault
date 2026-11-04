using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Companion.Api.Tests;

/// <summary>
/// Guide quality (owner wishes 2026-11-03): one language per guide, the Steam languages in the facts, the walkthrough structure and reading flow,
/// acquisition paths, boss cards, overview, typed hints (achievement, point of no return, side quests), sources, "redo" for any chapter, the manual
/// replace and the foreign-sentence check with its fix prompt. Fake providers only - nothing here talks to a real AI provider or to Steam.
/// </summary>
public sealed class GuideQualityTests
{
    private static AiGuideGeneratorService.GameContext Context(string language = Lang.German, bool achievements = false) =>
        new("Suncraft", language == Lang.English ? "Steam AppID: 4078110" : "Steam-AppID: 4078110",
            achievements ? [("ACH_KOL", "Gast in Kol", "Übernachte in Kol."), ("ACH_BOSS", "Bezwinger", "Besiege den Wächter.")] : [],
            [], ResearchBlocks.Selection.Empty, ResearchBlocks.FillBase(language, "Suncraft", "RPG", ResearchBlocks.PlatformText), language);

    private static GameEntity Game(string language = Lang.German) =>
        new() { Id = Guid.NewGuid(), Slug = "suncraft", Title = "Suncraft", SteamAppId = 4078110, ContentLanguage = language, Tabs = [] };

    private static JsonArray Normalize(string json, NormalizeOptions? options = null)
    {
        using var doc = JsonDocument.Parse(json);
        using var _ = Lang.Use(Lang.German);
        return GuideSectionNormalizer.Normalize(doc.RootElement, options);
    }

    private static AiGuideGeneratorService Generator() =>
        new(null!, null!, null!, null!, null!, null!, Microsoft.Extensions.Options.Options.Create(new Companion.Api.Configuration.CompanionOptions()), null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<AiGuideGeneratorService>.Instance);

    /// <summary>Every prompt that writes guide content, in one language (real AI path and the manual/batch prompts).</summary>
    private static Dictionary<string, string> ContentPrompts(string language)
    {
        var context = Context(language, achievements: true);
        var game = Game(language);
        var outline = new AiGuideGeneratorService.WalkthroughOutline(["Wiki – https://example.org"], [new("Teil 1", "Fokus"), new("Teil 2", "Fokus")]);
        var prompts = new Dictionary<string, string>
        {
            ["blueprint"] = AiGuideGeneratorService.BuildBlueprintPrompt(context, null, []),
            ["chapter"] = AiGuideGeneratorService.BuildChapterPrompt(context, "Waffen", "Auftrag", "etabliert", "weapons"),
            ["walkthrough outline"] = AiGuideGeneratorService.BuildWalkthroughOutlinePrompt(context, "Auftrag", "etabliert", 6),
            ["walkthrough part"] = AiGuideGeneratorService.BuildWalkthroughPartPrompt(context, outline, 0, []),
            ["walkthrough single"] = AiGuideGeneratorService.BuildWalkthroughSinglePrompt(context, "Komplettlösung", "Auftrag", "etabliert"),
            ["achievement tips"] = AiGuideGeneratorService.BuildAchievementTipsPrompt(context),
            ["manual blueprint"] = AiGuideGeneratorService.ComposeManualBlueprintPrompt(context, game, language),
            ["manual chapter"] = AiGuideGeneratorService.ComposeManualChapterPrompt(context, game, language, "Waffen", "Auftrag", "etabliert", "weapons"),
            ["manual walkthrough"] = AiGuideGeneratorService.ComposeManualChapterPrompt(context, game, language, "Komplettlösung", "Auftrag", "etabliert", "walkthrough"),
            ["batch"] = AiGuideGeneratorService.ComposeManualBatchPrompt(context, game, language,
                [new BatchChapter(0, "K01", "Komplettlösung", "Auftrag", "walkthrough", "etabliert", null, null), new BatchChapter(1, "K02", "Waffen", "Auftrag", "weapons", "etabliert", null, null)]),
        };
        foreach (var part in AiGuideGeneratorService.LoreParts(language))
        {
            prompts["lore " + part.Title] = AiGuideGeneratorService.BuildLorePrompt(context, part);
        }
        return prompts;
    }

    public static TheoryData<string> BothLanguages => [Lang.German, Lang.English];

    // ---- 1: language rule, source hints, side quest rule in every content prompt -----------------------------------------------

    [Theory]
    [MemberData(nameof(BothLanguages))]
    public void Every_content_prompt_carries_the_language_rule_the_source_hints_and_the_side_quest_rule(string language)
    {
        var blocks = new Dictionary<string, string>
        {
            ["language rule"] = AiGuideGeneratorService.LanguageRules(language),
            ["source hints"] = AiGuideGeneratorService.SourceHints(language),
            ["side quest rule"] = AiGuideGeneratorService.SideQuestRule(language),
        };
        foreach (var (name, prompt) in ContentPrompts(language))
        {
            foreach (var (blockName, block) in blocks)
            {
                Assert.True(prompt.Contains(block, StringComparison.Ordinal), $"The {name} prompt ({language}) lacks the {blockName}.");
            }
        }
    }

    [Fact]
    public void The_old_rule_German_name_with_the_English_original_in_brackets_is_gone()
    {
        foreach (var (name, prompt) in ContentPrompts(Lang.German))
        {
            Assert.DoesNotContain("englischem Originalbegriff in Klammern", prompt);
            Assert.DoesNotContain("mit englischem Original in Klammern", prompt);
            Assert.DoesNotContain("Name (DE / EN)", prompt);
        }
        var rule = AiGuideGeneratorService.LanguageRules(Lang.German);
        Assert.Contains("höchstens EINMAL pro Kapitel", rule);
        Assert.Contains("nur bei Eigennamen", rule);
        Assert.Contains("deutschen Fassung des Spiels", rule);
        Assert.Contains("Go to the town", rule);
        Assert.Contains("at most ONCE per chapter", AiGuideGeneratorService.LanguageRules(Lang.English));
    }

    [Fact]
    public void The_source_hints_are_German_first_for_German_guides_and_English_for_English_guides()
    {
        var german = AiGuideGeneratorService.SourceHints(Lang.German);
        var english = AiGuideGeneratorService.SourceHints(Lang.English);

        foreach (var site in new[] { "pcgames.de", "spieletipps.de", "GIGA", "gameswelt.de", "mein-mmo.de", "deutsche Steam-Community-Guides", "Game8", "Fextralife", "PowerPyx", "PSNProfiles", "PlayStationTrophies.org", "Sprachregel" })
        {
            Assert.Contains(site, german);
        }
        Assert.True(german.IndexOf("pcgames.de", StringComparison.Ordinal) < german.IndexOf("Game8", StringComparison.Ordinal), "German sources come first.");
        foreach (var site in new[] { "Steam community guides", "Game8", "Fextralife", "PowerPyx", "PSNProfiles", "PlayStationTrophies.org", "patch notes", "Reddit" })
        {
            Assert.Contains(site, english);
        }
        foreach (var site in new[] { "pcgames.de", "spieletipps", "gameswelt", "mein-mmo" })
        {
            Assert.DoesNotContain(site, english);
        }
        // The old one-for-all list is gone
        Assert.DoesNotContain("Wikis, Fandom, PowerPyx, IGN", AiGuideGeneratorService.BuildChapterPrompt(Context(), "X", "Y", "etabliert", "weapons"));
    }

    [Theory]
    [InlineData(Lang.German, "Schritt 1", "Schritt 2", "/de/", "Erfinde nie eine Wiki-Adresse", "ohne Erwähnung", "zweiten Quelle", "übernimm nie ihren Text", "\"sources\"")]
    [InlineData(Lang.English, "Step 1", "Step 2", "/en/", "Never invent a wiki URL", "without mentioning it", "second source", "never copy their text", "\"sources\"")]
    public void Every_content_prompt_looks_for_a_wiki_of_the_game_first_with_its_guardrails(string language, string first, string second, string languagePath,
        string noInvention, string skipSilently, string crossCheck, string ownWords, string sources)
    {
        var rule = AiGuideGeneratorService.GameWikiRule(language);
        foreach (var expected in new[] { first, languagePath, noInvention, skipSilently, crossCheck, ownWords, sources, "ffresonance.wiki", "Fandom" })
        {
            Assert.Contains(expected, rule);
        }
        foreach (var (name, prompt) in ContentPrompts(language))
        {
            Assert.True(prompt.Contains(rule, StringComparison.Ordinal), $"The {name} prompt ({language}) lacks the game wiki rule.");
            // The wiki step comes before the usual source list
            Assert.True(prompt.IndexOf(first + " –", StringComparison.Ordinal) < prompt.IndexOf(second + " –", StringComparison.Ordinal), $"The {name} prompt ({language}) has the steps in the wrong order.");
        }
        // The manual output contract (single and batch) points at the step as well
        var marker = language == Lang.English ? "first for a wiki dedicated to this game" : "nach einem Wiki speziell zu diesem Spiel";
        var prompts = ContentPrompts(language);
        Assert.Contains(marker, prompts["manual chapter"]);
        Assert.Contains(marker, prompts["batch"]);
    }

    [Theory]
    [InlineData(Lang.German, "Story, Route, Verpassbares, Boss-Taktiken, Reihenfolge", "kommen NUR von Steam", "gilt Steam")]
    [InlineData(Lang.English, "story, route, missables, boss tactics, order", "come ONLY from Steam", "Steam wins")]
    public void The_console_rule_allows_identical_facts_and_keeps_the_achievements_on_Steam(string language, params string[] phrases)
    {
        var rule = AiGuideGeneratorService.PlatformRules(language);
        Assert.All(phrases, phrase => Assert.Contains(phrase, rule));
    }

    // ---- 2: languages according to Steam -----------------------------------------------------------------------------------------

    // The real field of store appdetails for app 359870 with l=german (read 2026-11-03)
    private const string SteamLanguagesGerman = "Englisch<strong>*</strong>, Französisch, Italienisch, Deutsch, Spanisch – Spanien, Japanisch<strong>*</strong>, Koreanisch, Chinesisch (traditionell)<br><strong>*</strong>Sprachen mit voller Audiounterstützung";

    [Fact]
    public void The_Steam_language_list_is_read_with_the_audio_marks_and_without_the_footnote()
    {
        var (languages, audio) = SteamStatsService.ParseSupportedLanguages(SteamLanguagesGerman);

        Assert.Equal(["Englisch", "Französisch", "Italienisch", "Deutsch", "Spanisch – Spanien", "Japanisch", "Koreanisch", "Chinesisch (traditionell)"], languages);
        Assert.Equal(["Englisch", "Japanisch"], audio);
        Assert.Empty(SteamStatsService.ParseSupportedLanguages(null).Languages);
        Assert.Empty(SteamStatsService.ParseSupportedLanguages("").Languages);
    }

    [Fact]
    public void The_facts_say_whether_a_version_in_the_guide_language_exists()
    {
        var (languages, audio) = SteamStatsService.ParseSupportedLanguages(SteamLanguagesGerman);
        var german = AiGuideGeneratorService.LanguageFactLines(languages, audio, Lang.German);
        Assert.Equal(2, german.Count);
        Assert.StartsWith("Sprachen laut Steam: Englisch, Französisch", german[0]);
        Assert.Contains("mit Sprachausgabe: Englisch, Japanisch", german[0]);
        Assert.StartsWith("Deutsche Fassung laut Steam: ja", german[1]);

        var noGerman = AiGuideGeneratorService.LanguageFactLines(["Englisch", "Japanisch"], [], Lang.German);
        Assert.StartsWith("Deutsche Fassung laut Steam: nein", noGerman[1]);

        var english = AiGuideGeneratorService.LanguageFactLines(["English", "German"], ["English"], Lang.English);
        Assert.StartsWith("English version according to Steam: yes", english[1]);
        Assert.Empty(AiGuideGeneratorService.LanguageFactLines([], [], Lang.German));
    }

    // ---- 3-5: walkthrough structure, reading flow, acquisition, boss -------------------------------------------------------------

    [Theory]
    [MemberData(nameof(BothLanguages))]
    public void The_walkthrough_prompts_carry_the_structure_the_reading_flow_and_the_boss_and_acquisition_rules(string language)
    {
        var prompts = ContentPrompts(language);
        var required = new[]
        {
            AiGuideGeneratorService.WalkthroughStructure(language), AiGuideGeneratorService.WalkthroughReadingRules(language),
            AiGuideGeneratorService.BossRules(language), AiGuideGeneratorService.AcquisitionRules(language), AiGuideGeneratorService.BossBlock(language),
            AiGuideGeneratorService.SourcesBlock(language),
        };
        foreach (var name in new[] { "walkthrough part", "walkthrough single", "manual walkthrough", "batch" })
        {
            Assert.All(required, block => Assert.True(prompts[name].Contains(block, StringComparison.Ordinal), $"The {name} prompt ({language}) lacks a walkthrough block."));
            Assert.Contains("ACH_KOL", prompts[name]);   // the official API names to reference
        }
        Assert.Contains("\"level\": 2", AiGuideGeneratorService.WalkthroughStructure(language));
        Assert.Contains("point_of_no_return", AiGuideGeneratorService.WalkthroughStructure(language));
        Assert.Contains("side_quests", AiGuideGeneratorService.WalkthroughStructure(language));
        Assert.Contains("overview", AiGuideGeneratorService.WalkthroughStructure(language));
    }

    [Theory]
    [MemberData(nameof(BothLanguages))]
    public void Chapter_prompts_carry_boss_card_acquisition_rule_and_sources_and_tips_the_short_acquisition_rule(string language)
    {
        var prompts = ContentPrompts(language);
        foreach (var name in new[] { "chapter", "manual chapter", "batch" })
        {
            Assert.Contains(AiGuideGeneratorService.BossBlock(language), prompts[name]);
            Assert.Contains(AiGuideGeneratorService.BossRules(language), prompts[name]);
            Assert.Contains(AiGuideGeneratorService.AcquisitionRules(language), prompts[name]);
            Assert.Contains(AiGuideGeneratorService.SourcesBlock(language), prompts[name]);
        }
        Assert.Contains(AiGuideGeneratorService.AcquisitionTipRule(language), prompts["achievement tips"]);
    }

    [Fact]
    public void The_reading_flow_rule_asks_for_whole_sentences_without_objective_telegrams()
    {
        var rule = AiGuideGeneratorService.WalkthroughReadingRules(Lang.German);
        Assert.Contains("Geh ins Dorf Kol und übernachte im Gasthaus.", rule);
        Assert.Contains("kein „Ziel:“", rule);
        Assert.Contains("Fasse triviale Ziele", rule);
        Assert.Contains("eigener \"alert\"", rule);
        Assert.Contains("JEDEN nötigen Kampf", AiGuideGeneratorService.AcquisitionRules(Lang.German));
        Assert.Contains("EVERY required fight", AiGuideGeneratorService.AcquisitionRules(Lang.English));
    }

    [Fact]
    public void The_blueprint_recommends_the_side_quest_topic_when_the_game_has_side_quests()
    {
        Assert.Contains("ist das Thema \"quests\" zutreffend", AiGuideGeneratorService.BuildBlueprintPrompt(Context(), null, []));
        Assert.Contains("the topic \"quests\" is applicable", AiGuideGeneratorService.BuildBlueprintPrompt(Context(Lang.English), null, []));
    }

    // ---- normalizer: heading level, boss, overview, alerts, sources ----------------------------------------------------------------

    [Fact]
    public void Heading_level_2_is_kept_everything_else_is_level_1_and_not_stored()
    {
        var sections = Normalize("""
            [ { "type": "heading", "title": "Teil", "level": 1 }, { "type": "heading", "title": "Gebiet", "level": 2 },
              { "type": "heading", "title": "Text", "level": "2" }, { "type": "heading", "title": "Drei", "level": 3 }, { "type": "heading", "title": "Alt" } ]
            """);
        Assert.Null(sections[0]!["level"]);
        Assert.Equal(2, sections[1]!["level"]!.GetValue<int>());
        Assert.Equal(2, sections[2]!["level"]!.GetValue<int>());
        Assert.Null(sections[3]!["level"]);
        Assert.Null(sections[4]!["level"]);
        // Stored data without a level stays byte-identical when normalized again (translation skeleton check)
        Assert.Equal(sections.ToJsonString(), Normalize(sections.ToJsonString()).ToJsonString());
    }

    [Fact]
    public void A_boss_keeps_only_HP_with_value_and_source_and_gets_a_unique_kebab_id()
    {
        var known = new NormalizeOptions(new HashSet<string>(["ACH_BOSS"], StringComparer.Ordinal));
        var sections = Normalize("""
            [ { "type": "boss", "title": "Der Wächter (Guardian)", "location": " Halle der Waffen ", "recommendedLevel": "35",
                "hp": [ { "value": "48.000", "appliesTo": "Normal", "source": "Wiki" }, { "value": "99.999" }, { "source": "Wiki" },
                        { "value": "Nicht dokumentiert" }, { "value": 12000, "source": "Guide" } ],
                "weaknesses": "Feuer", "attacks": [ { "name": "Schlag", "note": "Ausweichen" }, "Brüllen", { "note": "ohne Namen" } ],
                "tactics": ["1. Erst die Arme", "2. Dann der Kopf"], "missable": "ja", "achievements": ["ACH_BOSS", "ACH_FAKE", "ach_boss"],
                "immunities": ["Gift"], "party": "Heiler mitnehmen", "phases": ["Phase 1"], "rewards": ["Klinge"], "source": "Wiki" },
              { "type": "boss", "title": "Der Wächter (Guardian)" },
              { "type": "boss", "boss": { "title": "Verschachtelt", "location": "Turm" } },
              { "type": "boss", "location": "ohne Namen" } ]
            """, known);

        Assert.Equal(3, sections.Count);
        var boss = sections[0]!.AsObject();
        Assert.Equal("der-waechter-guardian", boss["bossId"]!.GetValue<string>());
        Assert.Equal("der-waechter-guardian-2", sections[1]!["bossId"]!.GetValue<string>());
        Assert.Equal("Halle der Waffen", boss["location"]!.GetValue<string>());
        var hp = boss["hp"]!.AsArray();
        Assert.Equal(3, hp.Count);
        Assert.Equal("48.000", hp[0]!["value"]!.GetValue<string>());
        Assert.Equal("Nicht dokumentiert", hp[1]!["value"]!.GetValue<string>());
        Assert.Null(hp[1]!["source"]);
        Assert.Equal("12000", hp[2]!["value"]!.GetValue<string>());
        Assert.Equal(["Feuer"], boss["weaknesses"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(2, boss["attacks"]!.AsArray().Count);
        Assert.Equal(["Erst die Arme", "Dann der Kopf"], boss["tactics"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.True(boss["missable"]!.GetValue<bool>());
        Assert.Equal(["ACH_BOSS"], boss["achievements"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal("Turm", sections[2]!["location"]!.GetValue<string>());
        Assert.Null(sections[2]!["boss"]);
    }

    [Fact]
    public void An_overview_keeps_counts_and_known_achievements_and_an_empty_one_is_dropped()
    {
        var known = new NormalizeOptions(new HashSet<string>(["ACH_KOL"], StringComparer.Ordinal));
        var sections = Normalize("""
            [ { "type": "overview", "recommendedLevel": "10-15", "counts": [ { "label": "Truhen", "count": 12 }, { "label": "Karten", "count": "3" }, { "label": "Unbekannt" }, { "count": 4 } ],
                "achievements": ["ACH_KOL", "ERFUNDEN"], "keyItems": ["Schlüssel"], "missables": "Truhe im Keller", "endsAtPointOfNoReturn": "true" },
              { "type": "overview", "title": "Leer" } ]
            """, known);

        Assert.Single(sections);
        var overview = sections[0]!.AsObject();
        Assert.Equal("Auf einen Blick", overview["title"]!.GetValue<string>());
        var counts = overview["counts"]!.AsArray();
        Assert.Equal(3, counts.Count);
        Assert.Equal(12, counts[0]!["count"]!.GetValue<int>());
        Assert.Equal(3, counts[1]!["count"]!.GetValue<int>());
        Assert.Null(counts[2]!["count"]);
        Assert.Equal(["ACH_KOL"], overview["achievements"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(["Truhe im Keller"], overview["missables"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.True(overview["endsAtPointOfNoReturn"]!.GetValue<bool>());
    }

    [Fact]
    public void Alerts_keep_only_known_API_names_items_and_compact_side_quests()
    {
        var known = new NormalizeOptions(new HashSet<string>(["ACH_KOL"], StringComparer.Ordinal));
        var sections = Normalize("""
            [ { "type": "alert", "kind": "achievement", "title": "Erfolg", "htmlContent": "Übernachte hier.", "apiName": "ach_kol" },
              { "type": "alert", "kind": "achievement", "title": "Erfolg", "htmlContent": "x", "apiName": "ACH_FAKE" },
              { "type": "alert", "kind": "achievement", "title": "Erfolg", "htmlContent": "x", "apiName": "<script>" },
              { "type": "alert", "kind": "point-of-no-return", "title": "Vorher", "htmlContent": "x", "items": ["Truhe holen", " ", "Quest beenden"] },
              { "type": "alert", "kind": "side quests", "title": "Nebenquests hier", "htmlContent": "",
                "quests": [ { "title": "Der verlorene Ring", "availableFrom": "ab Kapitel 2", "missable": "bis Ende Kapitel 3", "reward": "Ring", "ref": "quest-ring" },
                            { "name": "Alte Schuld", "missable": true }, "Kurzer Gang", { "reward": "ohne Namen" } ] } ]
            """, known);

        Assert.Equal("ACH_KOL", sections[0]!["apiName"]!.GetValue<string>());   // the casing of the official list wins
        Assert.Null(sections[1]!["apiName"]);
        Assert.Equal("achievement", sections[1]!["kind"]!.GetValue<string>());     // the hint stays, only the name goes
        Assert.Null(sections[2]!["apiName"]);
        Assert.Equal("point_of_no_return", sections[3]!["kind"]!.GetValue<string>());
        Assert.Equal("danger", sections[3]!["variant"]!.GetValue<string>());
        Assert.Equal(["Truhe holen", "Quest beenden"], sections[3]!["items"]!.AsArray().Select(x => x!.GetValue<string>()));
        var quests = sections[4]!["quests"]!.AsArray();
        Assert.Equal("side_quests", sections[4]!["kind"]!.GetValue<string>());
        Assert.Equal(3, quests.Count);
        Assert.Equal("quest-ring", quests[0]!["ref"]!.GetValue<string>());
        Assert.Equal("bis Ende Kapitel 3", quests[0]!["missable"]!.GetValue<string>());
        Assert.Equal("Alte Schuld", quests[1]!["title"]!.GetValue<string>());
        Assert.Equal("verpassbar", quests[1]!["missable"]!.GetValue<string>());
        Assert.Equal("Kurzer Gang", quests[2]!["title"]!.GetValue<string>());
    }

    [Fact]
    public void Without_a_known_list_API_names_are_only_checked_for_their_syntax()
    {
        var sections = Normalize("""[ { "type": "alert", "kind": "achievement", "title": "E", "htmlContent": "x", "apiName": "NEW_ACHIEVEMENT_1_0" } ]""");
        Assert.Equal("NEW_ACHIEVEMENT_1_0", sections[0]!["apiName"]!.GetValue<string>());
    }

    [Fact]
    public void Sources_keep_only_http_URLs_and_an_AI_answer_can_never_claim_cited()
    {
        const string json = """
            [ { "type": "sources", "sources": [ { "title": "Wiki", "url": "https://example.org/wiki", "origin": "cited" }, { "title": "Böse", "url": "javascript:alert(1)" },
                { "title": "Relativ", "url": "/wiki" }, { "title": "Ohne Schema", "url": "www.example.org" }, "Guide – https://guide.example.com/x.", { "url": "https://user:pw@example.org/" } ] },
              { "type": "sources", "sources": [ { "title": "nur kaputt", "url": "ftp://example.org" } ] } ]
            """;
        var untrusted = Normalize(json);
        Assert.Single(untrusted);
        var list = untrusted[0]!["sources"]!.AsArray();
        Assert.Equal(["https://example.org/wiki", "https://guide.example.com/x"], list.Select(s => s!["url"]!.GetValue<string>()));
        Assert.All(list, s => Assert.Equal("ai", s!["origin"]!.GetValue<string>()));

        var trusted = Normalize(json, NormalizeOptions.Trusted);
        Assert.Equal("cited", trusted[0]!["sources"]![0]!["origin"]!.GetValue<string>());
    }

    [Fact]
    public void One_sources_section_ends_a_chapter_with_the_provider_citations_first()
    {
        var sections = Normalize("""
            [ { "type": "sources", "sources": [ { "title": "A", "url": "https://a.example/" } ] }, { "type": "alert", "title": "X", "htmlContent": "y" },
              { "type": "sources", "sources": [ { "title": "B", "url": "https://b.example/" } ] } ]
            """);
        var provider = new AiSourceMeta([new("Zitat", "https://cited.example/", GuideSources.Cited)], [new("B", "https://b.example/", GuideSources.Search)]);

        var result = AiGuideGeneratorService.ConsolidateSources(sections, provider, GuideSources.MaxPerBlock);

        Assert.Equal(2, result.Count);
        Assert.Equal("alert", result[0]!["type"]!.GetValue<string>());
        var list = result[1]!["sources"]!.AsArray();
        Assert.Equal(["https://cited.example/", "https://a.example/", "https://b.example/"], list.Select(s => s!["url"]!.GetValue<string>()));
        Assert.Equal("search", list[2]!["origin"]!.GetValue<string>());     // named by the AI and found by the search: upgraded
    }

    [Fact]
    public void The_walkthrough_collects_the_sources_of_all_parts_into_one_section_and_the_intro_keeps_names_without_link()
    {
        var outline = new AiGuideGeneratorService.WalkthroughOutline(["Wiki – https://wiki.example/", "PowerPyx"], [new("Teil 1", "f"), new("Teil 2", "f")]);
        var part1 = Normalize("""[ { "type": "heading", "title": "Teil 1" }, { "type": "sources", "sources": [ { "title": "P1", "url": "https://p1.example/" } ] } ]""");
        var part2 = Normalize("""[ { "type": "heading", "title": "Teil 2" }, { "type": "sources", "sources": [ { "title": "P2", "url": "https://p2.example/" } ] } ]""");

        var merged = AiGuideGeneratorService.MergeWalkthroughParts(outline, [(part1, null), (part2, null)], Lang.German);

        Assert.Single(merged, s => s!["type"]!.GetValue<string>() == "sources");
        Assert.Equal("sources", merged[^1]!["type"]!.GetValue<string>());
        Assert.Equal(["https://wiki.example/", "https://p1.example/", "https://p2.example/"], merged[^1]!["sources"]!.AsArray().Select(s => s!["url"]!.GetValue<string>()));
        var intro = merged[0]!["htmlContent"]!.GetValue<string>();
        Assert.Contains("PowerPyx", intro);
        Assert.DoesNotContain("https://", intro);
    }

    // ---- translation extractor --------------------------------------------------------------------------------------------------

    [Fact]
    public void The_extractor_translates_boss_overview_and_hint_texts_but_never_ids_names_values_or_URLs()
    {
        var sections = Normalize("""
            [ { "type": "boss", "bossId": "waechter", "title": "Wächter", "location": "Halle", "recommendedLevel": "Stufe 35", "party": "Heiler",
                "hp": [ { "value": "48.000", "appliesTo": "Normal", "source": "WikiQuelle" } ], "weaknesses": ["Feuer"], "attacks": [ { "name": "Schlag", "note": "Ausweichen" } ],
                "tactics": ["Erst die Arme"], "rewards": ["Klinge"], "achievements": ["ACH_BOSS"], "source": "BossQuelle" },
              { "type": "overview", "counts": [ { "label": "Truhen", "count": 12 } ], "keyItems": ["Schlüssel"], "missables": ["Ring"], "achievements": ["ACH_KOL"] },
              { "type": "alert", "kind": "point_of_no_return", "title": "Vorher", "htmlContent": "x", "items": ["Truhe holen"], "apiName": "ACH_KOL" },
              { "type": "alert", "kind": "side_quests", "title": "Nebenquests", "htmlContent": "y", "quests": [ { "title": "Ring", "availableFrom": "ab Kapitel 2", "missable": "bis Kapitel 3", "reward": "Gold", "ref": "quest-ring" } ] },
              { "type": "sources", "title": "Quellen", "sources": [ { "title": "SeitenTitel", "url": "https://example.org/" } ] } ]
            """);
        var texts = GuideTextExtractor.Collect(sections).Select(s => s.Text).ToList();

        foreach (var text in new[] { "Halle", "Stufe 35", "Heiler", "Normal", "Feuer", "Schlag", "Ausweichen", "Erst die Arme", "Klinge", "Truhen", "Schlüssel", "Truhe holen", "ab Kapitel 2", "bis Kapitel 3", "Gold" })
        {
            Assert.Contains(text, texts);
        }
        foreach (var text in new[] { "waechter", "48.000", "WikiQuelle", "BossQuelle", "ACH_BOSS", "ACH_KOL", "quest-ring", "SeitenTitel", "https://example.org/" })
        {
            Assert.DoesNotContain(text, texts);
        }

        // A translated copy passes the structure check (normalizing it as trusted data keeps origins and every id)
        var translated = (JsonArray)sections.DeepClone();
        GuideTextExtractor.Apply(GuideTextExtractor.Collect(translated), texts.Distinct().ToDictionary(t => t, t => "EN " + t));
        GuideTextExtractor.ValidateTranslated(sections, translated, "Kapitel");
    }

    // ---- manual check: warnings, foreign sentences, fix prompt ---------------------------------------------------------------------

    [Fact]
    public void The_scan_warns_about_a_boss_without_location_incomplete_HP_unknown_API_names_and_bad_URLs()
    {
        var warnings = new List<ManualIssueDto>();
        ManualAnswers.ScanSections(JsonNode.Parse("""
            [ { "type": "boss", "title": "Wächter", "hp": [ { "value": "1.000" }, { "value": "2.000", "source": "Wiki" } ], "achievements": ["ACH_FAKE"] },
              { "type": "boss" },
              { "type": "alert", "kind": "achievement", "title": "E", "htmlContent": "x", "apiName": "ACH_KOL" },
              { "type": "sources", "sources": [ { "title": "x", "url": "javascript:alert(1)" }, { "title": "ok", "url": "https://example.org" } ] } ]
            """)!.AsArray(), warnings, new HashSet<string>(["ACH_KOL"], StringComparer.Ordinal));

        var codes = warnings.Select(w => w.Code).ToList();
        Assert.Contains("bossWithoutLocation", codes);
        Assert.Contains("bossHpDropped", codes);
        Assert.Contains("bossHpWithoutAppliesTo", codes);
        Assert.Contains("sectionSkipped", codes);
        Assert.Contains("unknownApiName", codes);
        Assert.Contains("invalidSourceUrl", codes);
        Assert.Contains("ACH_FAKE", warnings.First(w => w.Code == "unknownApiName").Message);
    }

    [Theory]
    [InlineData("Go to the town and talk to the elder.")]
    [InlineData("Stay at the inn in the village of Kol")]
    [InlineData("Rent a Chocobo at the stables.")]
    [InlineData("Rent a Chocobo")]
    [InlineData("Defeat the guardian before you enter the hall")]
    public void English_objectives_in_a_German_guide_are_found(string sentence)
    {
        Assert.True(ForeignLanguageCheck.IsForeign(sentence, Lang.German), sentence);
    }

    [Theory]
    [InlineData("Geh ins Dorf Kol und übernachte im Gasthaus.")]
    [InlineData("Besiege den Iron Giant im Thronsaal (Throne Room).")]
    [InlineData("Schließe die Quest The Lord of the Rings ab.")]
    [InlineData("Geh zur Taverne The Prancing Pony of Bree.")]
    [InlineData("Save-Punkt nutzen")]
    [InlineData("Go-Kart-Rennen gewinnen")]
    [InlineData("Übernachtung im Gasthaus von Kol")]
    [InlineData("Final Fantasy Resonance")]
    [InlineData("Hol dir „The Ultimate Weapon“ aus der Halle der Waffen.")]
    public void German_text_with_English_proper_names_is_no_false_alarm(string sentence)
    {
        Assert.False(ForeignLanguageCheck.IsForeign(sentence, Lang.German), sentence);
    }

    [Fact]
    public void The_check_is_mirrored_for_English_guides()
    {
        Assert.True(ForeignLanguageCheck.IsForeign("Geh ins Dorf und sprich mit dem Ältesten.", Lang.English));
        Assert.False(ForeignLanguageCheck.IsForeign("Go to the village of Kol and spend the night at the inn.", Lang.English));
        Assert.False(ForeignLanguageCheck.IsForeign("Defeat the Schwarzer Ritter in the Burghof.", Lang.English));
    }

    private const string MixedAnswer = """
        { "game": "Suncraft", "steamAppId": 4078110, "title": "Komplettlösung", "icon": "mdi:map",
          "sections": [ { "type": "heading", "level": 1, "title": "Kapitel 1", "htmlContent": "Du startest im Dorf Kol und brichst zur Burg auf." },
            { "type": "story", "variant": "steps", "paragraphs": [ "Ziel: Go to the town and talk to the elder.", "Stay at the inn in the village of Kol", "Geh danach zum Tor und sprich mit der Wache.", "Rent a Chocobo", "Besiege den Iron Giant im Thronsaal (Throne Room)." ] } ] }
        """;

    [Fact]
    public void A_pasted_German_chapter_with_English_sentences_gets_a_warning_with_examples_and_a_fix_prompt()
    {
        using var _ = Lang.Use(Lang.German);
        var check = Generator().CheckManualChapter(Game(), MixedAnswer, "Komplettlösung", "walkthrough");

        Assert.True(check.Dto.Ok);
        var warning = Assert.Single(check.Dto.Warnings, w => w.Code == "foreignSentences");
        Assert.Contains("3 Satz/Sätze", warning.Message);
        Assert.Contains("„Go to the town and talk to the elder.“", warning.Message);
        Assert.DoesNotContain("Iron Giant", warning.Details);
        Assert.NotNull(check.Dto.FixPrompt);
        Assert.Contains("- Stay at the inn in the village of Kol", check.Dto.FixPrompt);
        Assert.Contains("- Rent a Chocobo", check.Dto.FixPrompt);
        Assert.Contains(AiGuideGeneratorService.LanguageRules(Lang.German), check.Dto.FixPrompt);
        Assert.Contains("„Komplettlösung“", check.Dto.FixPrompt);
        Assert.Contains("„steamAppId“ (4078110)", check.Dto.FixPrompt);
        Assert.Contains("```json", check.Dto.FixPrompt);
        Assert.DoesNotContain("„chapter“", check.Dto.FixPrompt);

        var clean = Generator().CheckManualChapter(Game(), MixedAnswer.Replace("Ziel: Go to the town and talk to the elder.", "Geh in die Stadt.")
            .Replace("Stay at the inn in the village of Kol", "Übernachte im Gasthaus von Kol.").Replace("Rent a Chocobo", "Miete einen Chocobo."), "Komplettlösung", "walkthrough");
        Assert.DoesNotContain(clean.Dto.Warnings, w => w.Code == "foreignSentences");
        Assert.Null(clean.Dto.FixPrompt);
    }

    [Fact]
    public void A_batch_item_carries_the_fix_prompt_with_its_chapter_key()
    {
        using var _ = Lang.Use(Lang.German);
        var answer = JsonNode.Parse(MixedAnswer)!.AsObject();
        answer["chapter"] = "K01";
        var chapters = new List<BatchChapter> { new(0, "K01", "Komplettlösung", "", "walkthrough", null, null, null) };

        var check = Generator().CheckManualBatch(Game(), "```json\n" + answer.ToJsonString() + "\n```", chapters).Dto;

        var item = Assert.Single(check.Items);
        Assert.Equal("warning", item.State);
        Assert.NotNull(item.FixPrompt);
        Assert.Contains("„chapter“ („K01“)", item.FixPrompt);
    }

    // ---- topic id of a tab ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_tab_stores_its_standard_topic_and_extra_chapters_store_none()
    {
        using var _ = Lang.Use(Lang.German);
        const string answer = """{ "title": "Nebenquests", "sections": [ { "type": "alert", "title": "x", "htmlContent": "y" } ] }""";
        var quests = AiGuideGeneratorService.BuildTabFromChapterJson(Guid.NewGuid(), 1, new("Nebenquests", "mdi:x", "f", "quests"), answer);
        var extra = AiGuideGeneratorService.BuildTabFromChapterJson(Guid.NewGuid(), 2, new("Besonderes", "mdi:x", "f", "extra"), answer);
        var walkthrough = AiGuideGeneratorService.BuildTabFromChapterJson(Guid.NewGuid(), 3, new("Komplettlösung", "mdi:x", "f", "walkthrough"), answer);

        Assert.Equal("quests", quests.TopicId);
        Assert.Null(extra.TopicId);
        Assert.Equal("walkthrough", walkthrough.TopicId);
        Assert.Null(AiGuideGeneratorService.StoredTopicId("erfunden"));
    }

    [Fact]
    public void An_import_keeps_a_known_topic_id_and_drops_an_unknown_one()
    {
        const string json = """
        { "format": "platinvault-guide", "formatVersion": 1, "game": { "title": "Test" },
          "tabs": [ { "title": "Nebenquests", "topicId": "quests" }, { "title": "Anderes", "topicId": "erfunden" }, { "title": "Ohne" } ] }
        """;
        var tabs = GuideImport.Parse(JsonDocument.Parse(json).RootElement).Tabs;
        Assert.Equal(["quests", null, null], tabs.Select(t => t.TopicId));
    }

    [Fact]
    public void The_scan_hands_up_to_three_examples_with_a_warning()
    {
        var warnings = new List<ManualIssueDto>();
        ManualAnswers.ScanSections(JsonNode.Parse("""[ { "type": "boss", "title": "B", "location": "L", "achievements": ["X1", "X2", "X3", "X4"] } ]""")!.AsArray(), warnings,
            new HashSet<string>(["ACH_KOL"], StringComparer.Ordinal));
        var warning = Assert.Single(warnings, w => w.Code == "unknownApiName");
        Assert.Equal(["X1", "X2", "X3"], warning.Examples);
    }

    // ---- redo endpoint and manual replace (real server, fake provider) ---------------------------------------------------------------

    [Fact]
    public async Task The_model_has_no_change_without_a_migration_and_the_guide_reports_the_topic_of_its_tabs()
    {
        var (server, _) = await StartAsync(_ => "{}");
        await using (server)
        {
            using (var scope = server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
                Assert.False(db.Database.HasPendingModelChanges(), "The EF model changed without a migration.");
                var game = new GameEntity { Id = Guid.NewGuid(), Slug = "topics", Title = "Topics", ContentLanguage = Lang.German };
                game.Tabs =
                [
                    new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = WalkthroughTab.Slug, Title = "Komplettlösung", SortOrder = 1, SectionsJson = "[]" },
                    new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = "tab-2-quests", Title = "Nebenquests", SortOrder = 2, SectionsJson = "[]", TopicId = "quests" },
                    new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = "tab-3-alt", Title = "Alt", SortOrder = 3, SectionsJson = "[]" },
                ];
                db.Games.Add(game);
                await db.SaveChangesAsync();
            }

            var detail = await server.Client.GetFromJsonAsync<JsonElement>("/api/games/topics");
            var topics = detail.GetProperty("tabs").EnumerateArray().Select(t => t.GetProperty("topicId").ValueKind == JsonValueKind.Null ? null : t.GetProperty("topicId").GetString()).ToList();
            Assert.Equal(["walkthrough", "quests", null], topics);
        }
    }

    private sealed class FakeAi(Func<string, string> answer) : HttpMessageHandler
    {
        public List<string> Prompts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "gemini.test")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            var prompt = body["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>();
            lock (Prompts)
            {
                Prompts.Add(prompt);
            }
            var text = answer(prompt);
            var json = new JsonObject { ["candidates"] = new JsonArray(new JsonObject { ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = text }) }, ["finishReason"] = "STOP" }) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static async Task<(TestServer Server, FakeAi Ai)> StartAsync(Func<string, string> answer)
    {
        var ai = new FakeAi(answer);
        var server = await TestServer.StartAsync(desktop: null, b =>
        {
            b.Configuration["Companion:Ai:GeminiBaseUrl"] = "http://gemini.test/v1beta";
            // Registered before the server's own services: AddHttpClient only adds a factory when there is none
            b.Services.AddSingleton<IHttpClientFactory>(new FakeFactory(ai));
        });
        server.Services.GetRequiredService<SecretStore>().SetSecret(SettingsStore.SecretGemini, "fake-key");
        return (server, ai);
    }

    private sealed record Seeded(Guid Real, Guid Achievements, Guid Lore);

    /// <summary>A game without a Steam AppID (no Steam requests): one chapter with two ticked rows, the achievement list and the lore tab.</summary>
    private static async Task<Seeded> SeedAsync(TestServer server)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var game = new GameEntity { Id = Guid.NewGuid(), Slug = "suncraft", Title = "Suncraft", ContentLanguage = Lang.German };
        var real = new GuideTabEntity
        {
            Id = Guid.NewGuid(), GameId = game.Id, Slug = "tab-1-waffen", Title = "Waffen", SortOrder = 1, GroupKey = "combat",
            SectionsJson = """[ { "type": "checklist_table", "table": { "tableId": "w", "caption": "Waffen", "checkable": true, "columns": [ { "key": "name", "label": "Name" } ], "rows": [ { "itemId": "schwert", "cells": { "name": "Schwert" } }, { "itemId": "bogen", "cells": { "name": "Bogen" } } ] } } ]"""
        };
        var achievements = new GuideTabEntity
        {
            Id = Guid.NewGuid(), GameId = game.Id, Slug = "steam-achievements", Title = "Erfolge", SortOrder = 2,
            SectionsJson = """[ { "type": "checklist_table", "table": { "tableId": "steam-achievements", "caption": "Erfolge", "checkable": true, "columns": [], "rows": [ { "itemId": "ACH_KOL", "cells": {} } ] } } ]"""
        };
        var lore = new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = LoreTab.Slug, Title = LoreTab.Title, SortOrder = LoreTab.SortOrder, SectionsJson = "[]" };
        game.Tabs = [real, achievements, lore];
        game.ProgressChecks =
        [
            new ProgressCheckEntity { Id = Guid.NewGuid(), GameId = game.Id, ItemKey = "schwert", CheckedAt = DateTimeOffset.UtcNow },
            new ProgressCheckEntity { Id = Guid.NewGuid(), GameId = game.Id, ItemKey = "bogen", CheckedAt = DateTimeOffset.UtcNow },
        ];
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return new Seeded(real.Id, achievements.Id, lore.Id);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(TestServer server, string url, object? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("X-Requested-With", "PlatinVault");
        request.Headers.Add("Accept-Language", "de");
        var response = await server.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        return (response.StatusCode, doc.RootElement.Clone());
    }

    private static async Task<JsonElement> WaitForJobAsync(TestServer server, JsonElement job)
    {
        var id = job.GetProperty("id").GetString();
        for (var i = 0; i < 200; i++)
        {
            var state = await server.Client.GetFromJsonAsync<JsonElement>($"/api/ai/jobs/{id}");
            if (!state.GetProperty("running").GetBoolean())
            {
                return state;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("The AI job did not finish.");
    }

    private static async Task<(GuideTabEntity Tab, List<string> Checks)> StoredAsync(TestServer server, Guid tabId)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var tab = await db.GuideTabs.AsNoTracking().FirstAsync(t => t.Id == tabId);
        var checks = await db.ProgressChecks.AsNoTracking().Select(p => p.ItemKey).OrderBy(k => k).ToListAsync();
        return (tab, checks);
    }

    private const string NewWeapons = """
        { "title": "Waffen", "icon": "mdi:sword", "sections": [ { "type": "checklist_table", "table": { "tableId": "w", "caption": "Waffen", "checkable": true,
          "columns": [ { "key": "name", "label": "Name" } ], "rows": [ { "itemId": "schwert", "cells": { "name": "Schwert" } }, { "itemId": "axt", "cells": { "name": "Axt" } } ] } },
          { "type": "boss", "title": "Wächter", "location": "Halle", "achievements": ["ACH_KOL"] } ] }
        """;

    [Fact]
    public async Task Redo_replaces_a_normal_chapter_in_place_and_keeps_only_the_ticks_whose_item_ids_still_exist()
    {
        var (server, ai) = await StartAsync(_ => NewWeapons);
        await using (server)
        {
            var seed = await SeedAsync(server);

            var (status, job) = await PostAsync(server, $"/api/games/suncraft/chapters/{seed.Real}/redo", new { providerOverride = (string?)null });
            Assert.Equal(HttpStatusCode.Accepted, status);
            Assert.Equal("redo", job.GetProperty("kind").GetString());
            var done = await WaitForJobAsync(server, job);
            Assert.True(done.GetProperty("error").ValueKind == JsonValueKind.Null, done.ToString());

            var (tab, checks) = await StoredAsync(server, seed.Real);
            Assert.Equal("tab-1-waffen", tab.Slug);                  // same tab, same address
            Assert.Equal(1, tab.SortOrder);
            Assert.Equal("combat", tab.GroupKey);
            Assert.Contains("\"axt\"", tab.SectionsJson);
            Assert.Contains("\"boss\"", tab.SectionsJson);
            Assert.Equal(["schwert"], checks);                         // "bogen" is gone from the chapter, its tick with it
            var prompt = Assert.Single(ai.Prompts);
            Assert.Contains("„Waffen“", prompt);
            Assert.Contains("bisherige Fassung hat diese Abschnitte: Waffen", prompt);
        }
    }

    [Fact]
    public async Task Redo_keeps_the_chapter_when_the_research_fails_or_says_the_topic_does_not_exist()
    {
        var (server, _) = await StartAsync(_ => """{ "notApplicable": true, "reason": "Gibt es nicht." }""");
        await using (server)
        {
            var seed = await SeedAsync(server);
            var before = (await StoredAsync(server, seed.Real)).Tab.SectionsJson;

            var (_, job) = await PostAsync(server, $"/api/games/suncraft/chapters/{seed.Real}/redo", null);
            var done = await WaitForJobAsync(server, job);

            Assert.Contains("bleibt unverändert", done.GetProperty("error").GetString());
            var (tab, checks) = await StoredAsync(server, seed.Real);
            Assert.Equal(before, tab.SectionsJson);
            Assert.Equal(["bogen", "schwert"], checks);
        }
    }

    [Fact]
    public async Task Redo_refuses_the_achievement_list_Story_and_Lore_and_unknown_chapters()
    {
        var (server, ai) = await StartAsync(_ => NewWeapons);
        await using (server)
        {
            var seed = await SeedAsync(server);
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(server, $"/api/games/suncraft/chapters/{seed.Achievements}/redo", null)).Status);
            Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(server, $"/api/games/suncraft/chapters/{seed.Lore}/redo", null)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(server, $"/api/games/suncraft/chapters/{Guid.NewGuid()}/redo", null)).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(server, $"/api/games/nope/chapters/{seed.Real}/redo", null)).Status);
            Assert.Empty(ai.Prompts);
        }
    }

    [Fact]
    public async Task A_batch_item_with_replace_rewrites_a_normal_chapter_and_without_replace_it_is_skipped()
    {
        var (server, _) = await StartAsync(_ => "{}");
        await using (server)
        {
            var seed = await SeedAsync(server);
            var answer = JsonNode.Parse(NewWeapons)!.AsObject();
            answer["game"] = "Suncraft";
            answer["chapter"] = "K01";
            var text = "```json\n" + answer.ToJsonString() + "\n```";

            var refused = await PostAsync(server, "/api/games/suncraft/manual/batch-answer",
                new { text, chapters = new[] { new { key = "K01", title = "Waffen", topicId = "weapons", tabId = seed.Real } } });
            Assert.Equal(HttpStatusCode.OK, refused.Status);
            Assert.Equal(0, refused.Body.GetProperty("saved").GetArrayLength());
            Assert.Equal(1, refused.Body.GetProperty("skipped").GetArrayLength());

            var achievementList = await PostAsync(server, "/api/games/suncraft/manual/batch-answer",
                new { text, chapters = new[] { new { key = "K01", title = "Waffen", topicId = "weapons", tabId = seed.Achievements, replace = true } } });
            Assert.Equal(0, achievementList.Body.GetProperty("saved").GetArrayLength());

            var replaced = await PostAsync(server, "/api/games/suncraft/manual/batch-answer",
                new { text, chapters = new[] { new { key = "K01", title = "Waffen", topicId = "weapons", tabId = seed.Real, replace = true } } });
            Assert.Equal(HttpStatusCode.OK, replaced.Status);
            var saved = Assert.Single(replaced.Body.GetProperty("saved").EnumerateArray());
            Assert.True(saved.GetProperty("replaced").GetBoolean());
            Assert.Equal(seed.Real, saved.GetProperty("tabId").GetGuid());

            var (tab, checks) = await StoredAsync(server, seed.Real);
            Assert.Contains("\"axt\"", tab.SectionsJson);
            Assert.Contains("ACH_KOL", tab.SectionsJson);           // a name of the game's achievement list survives the manual save
            Assert.Equal(["schwert"], checks);
        }
    }

    // ---- review fixes: redo with other item ids, manual redo prompt -------------------------------------------------------------------

    private const string RenamedWeapons = """
        { "title": "Waffen", "icon": "mdi:sword", "sections": [ { "type": "checklist_table", "table": { "tableId": "w", "caption": "Waffen", "checkable": true,
          "columns": [ { "key": "name", "label": "Name" } ], "rows": [ { "itemId": "waffe-schwert", "cells": { "name": "Schwert" } }, { "itemId": "waffe-bogen", "cells": { "name": "Bogen" } },
          { "itemId": "axt", "cells": { "name": "Axt" } } ] } } ] }
        """;

    /// <summary>Adds a walkthrough whose row points at "bogen" of the weapons chapter.</summary>
    private static async Task<Guid> AddWalkthroughAsync(TestServer server)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var game = await db.Games.FirstAsync(g => g.Slug == "suncraft");
        var walk = new GuideTabEntity
        {
            Id = Guid.NewGuid(), GameId = game.Id, Slug = WalkthroughTab.Slug, Title = "Komplettlösung", SortOrder = 0, TopicId = WalkthroughTab.TopicId,
            SectionsJson = """[ { "type": "heading", "title": "Teil 1" }, { "type": "checklist_table", "table": { "tableId": "wt", "caption": "Teil 1", "checkable": true, "columns": [ { "key": "name", "label": "Name" } ], "rows": [ { "itemId": "wt-bogen", "ref": "bogen", "cells": { "name": "Bogen (Truhe)" } }, { "itemId": "wt-truhe", "cells": { "name": "Goldtruhe" } } ] } } ]"""
        };
        db.GuideTabs.Add(walk);
        await db.SaveChangesAsync();
        return walk.Id;
    }

    [Fact]
    public async Task Redo_whose_answer_gives_the_same_entries_other_item_ids_keeps_their_ticks_and_the_links_to_them()
    {
        var (server, ai) = await StartAsync(_ => RenamedWeapons);
        await using (server)
        {
            var seed = await SeedAsync(server);
            var walkId = await AddWalkthroughAsync(server);

            var (_, job) = await PostAsync(server, $"/api/games/suncraft/chapters/{seed.Real}/redo", null);
            var done = await WaitForJobAsync(server, job);
            Assert.True(done.GetProperty("error").ValueKind == JsonValueKind.Null, done.ToString());

            var (_, checks) = await StoredAsync(server, seed.Real);
            Assert.Equal(["waffe-bogen", "waffe-schwert"], checks);      // moved with a one-to-one name match, the old ids are gone
            var (walk, _) = await StoredAsync(server, walkId);
            Assert.Contains("\"ref\":\"waffe-bogen\"", walk.SectionsJson.Replace(" ", ""));
            // The prompt asked to keep the ids of the current rows
            var prompt = Assert.Single(ai.Prompts);
            Assert.Contains("Dieses Kapitel wird NEU geschrieben", prompt);
            Assert.Contains("schwert | Schwert", prompt);
            Assert.Contains("bogen | Bogen", prompt);
        }
    }

    [Fact]
    public async Task The_manual_chapter_prompt_for_a_replace_takes_topic_and_assignment_from_the_stored_chapter()
    {
        var (server, ai) = await StartAsync(_ => "{}");
        await using (server)
        {
            var seed = await SeedAsync(server);
            var walkId = await AddWalkthroughAsync(server);

            // Only title + tabId + replace: the server builds the assignment like "redo" and lists the chapter's current ids
            var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/chapter-prompt", new { title = "Waffen", tabId = seed.Real, replace = true });
            Assert.Equal(HttpStatusCode.OK, status);
            var prompt = body.GetProperty("prompt").GetString()!;
            Assert.Contains("bisherige Fassung hat diese Abschnitte: Waffen", prompt);
            Assert.Contains("schwert | Schwert", prompt);
            Assert.DoesNotContain("Vorhandene Einträge dieses Guides", prompt);        // its own rows are no link candidates (the walkthrough is none either)

            // The walkthrough by its stored address gets the walkthrough prompt, whatever topic the client sends
            var walk = await PostAsync(server, "/api/games/suncraft/manual/chapter-prompt", new { title = "Komplettlösung", tabId = walkId, replace = true, topicId = (string?)null });
            Assert.Equal(HttpStatusCode.OK, walk.Status);
            var walkPrompt = walk.Body.GetProperty("prompt").GetString()!;
            Assert.Contains("Kapitel KOMPLETTLÖSUNG", walkPrompt);
            Assert.Contains("wt-truhe | Goldtruhe", walkPrompt);                      // keep list of the walkthrough
            Assert.Contains("schwert | Schwert", walkPrompt);                          // the weapons rows are link candidates here

            // Without replace an assignment is still required; unknown and non-replaceable chapters are refused
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(server, "/api/games/suncraft/manual/chapter-prompt", new { title = "Waffen", tabId = seed.Real })).Status);
            Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(server, "/api/games/suncraft/manual/chapter-prompt", new { title = "X", tabId = Guid.NewGuid(), replace = true })).Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(server, "/api/games/suncraft/manual/chapter-prompt", new { title = "Erfolge", tabId = seed.Achievements, replace = true })).Status);
            Assert.Empty(ai.Prompts);                                                  // nothing was sent to an AI
        }
    }

    [Fact]
    public async Task A_batch_prompt_item_with_replace_needs_no_assignment_and_lists_the_ids_to_keep()
    {
        var (server, _) = await StartAsync(_ => "{}");
        await using (server)
        {
            var seed = await SeedAsync(server);

            var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/batch-prompt",
                new { chapters = new[] { new { key = "K01", title = "Waffen", tabId = seed.Real, replace = true } } });

            Assert.Equal(HttpStatusCode.OK, status);
            var prompt = body.GetProperty("prompt").GetString()!;
            Assert.Contains("K01: Waffen", prompt);
            Assert.Contains("schwert | Schwert", prompt);
            Assert.Contains("bisherige Fassung hat diese Abschnitte", prompt);
        }
    }

    [Fact]
    public async Task A_manual_answer_that_replaces_the_walkthrough_goes_through_the_same_release_and_cleanup_as_redo()
    {
        var (server, _) = await StartAsync(_ => "{}");
        await using (server)
        {
            var seed = await SeedAsync(server);
            var walkId = await AddWalkthroughAsync(server);
            using (var scope = server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
                var game = await db.Games.FirstAsync(g => g.Slug == "suncraft");
                db.ProgressChecks.Add(new ProgressCheckEntity { Id = Guid.NewGuid(), GameId = game.Id, ItemKey = "wt-truhe", CheckedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            }
            // The new walkthrough calls the chest "wt-gold" now (same name) and has no row for the bow any more
            var answer = new JsonObject
            {
                ["game"] = "Suncraft", ["chapter"] = "K01", ["title"] = "Komplettlösung", ["icon"] = "mdi:map",
                ["sections"] = JsonNode.Parse("""[ { "type": "heading", "level": 1, "title": "Teil 1" }, { "type": "checklist_table", "table": { "tableId": "wt", "caption": "Teil 1", "checkable": true, "columns": [ { "key": "name", "label": "Name" } ], "rows": [ { "itemId": "wt-gold", "cells": { "name": "Goldtruhe" } } ] } }, { "type": "sources", "sources": [ { "title": "Wiki", "url": "https://example.org/wiki" } ] } ]""")
            };
            var text = "```json\n" + answer.ToJsonString() + "\n```";

            var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/batch-answer",
                new { text, chapters = new[] { new { key = "K01", title = "Komplettlösung", topicId = WalkthroughTab.TopicId } } });

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(walkId, Assert.Single(body.GetProperty("saved").EnumerateArray()).GetProperty("tabId").GetGuid());
            var (walk, checks) = await StoredAsync(server, walkId);
            Assert.Contains("wt-gold", walk.SectionsJson);
            Assert.Contains("wt-gold", checks);                // the tick moved with the one-to-one name match
            Assert.DoesNotContain("wt-truhe", checks);         // the old id's tick is cleaned up
            Assert.Contains("bogen", checks);                  // the bow's own tick (weapons chapter) is untouched
        }
    }

    // ---- second review: a ticked definer that comes back as a ref row, two walkthrough answers in one batch -----------------------------

    /// <summary>An espers chapter (not ticked) and a walkthrough with an unlinked, ticked row "Ifrit" next to "Ifrit (Boss)".</summary>
    private static async Task<(Guid Walkthrough, Guid Espers)> SeedRefComebackAsync(TestServer server)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var game = await db.Games.FirstAsync(g => g.Slug == "suncraft");
        const string Columns = """ "columns": [ { "key": "name", "label": "Name" } ] """;
        var espers = new GuideTabEntity
        {
            Id = Guid.NewGuid(), GameId = game.Id, Slug = "tab-5-espers", Title = "Esper", SortOrder = 5, TopicId = "espers",
            SectionsJson = "[ { \"type\": \"checklist_table\", \"table\": { \"tableId\": \"e\", \"caption\": \"Esper\", \"checkable\": true, " + Columns + ", \"rows\": [ { \"itemId\": \"espers:ifrit\", \"cells\": { \"name\": \"Ifrit\" } } ] } } ]"
        };
        var walk = new GuideTabEntity
        {
            Id = Guid.NewGuid(), GameId = game.Id, Slug = WalkthroughTab.Slug, Title = "Komplettlösung", SortOrder = 0, TopicId = WalkthroughTab.TopicId,
            SectionsJson = "[ { \"type\": \"heading\", \"title\": \"Teil 1\" }, { \"type\": \"checklist_table\", \"table\": { \"tableId\": \"wt\", \"caption\": \"Teil 1\", \"checkable\": true, " + Columns
                + ", \"rows\": [ { \"itemId\": \"wt-ifrit\", \"cells\": { \"name\": \"Ifrit\" } }, { \"itemId\": \"wt-ifrit-boss\", \"cells\": { \"name\": \"Ifrit (Boss)\" } } ] } } ]"
        };
        db.GuideTabs.AddRange(espers, walk);
        db.ProgressChecks.Add(new ProgressCheckEntity { Id = Guid.NewGuid(), GameId = game.Id, ItemKey = "wt-ifrit", CheckedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return (walk.Id, espers.Id);
    }

    private static string WalkthroughAnswer(string ifritRow) =>
        "```json\n{ \"game\": \"Suncraft\", \"title\": \"Komplettlösung\", \"icon\": \"mdi:map\", \"sections\": [ { \"type\": \"heading\", \"level\": 1, \"title\": \"Teil 1\" }, "
        + "{ \"type\": \"checklist_table\", \"table\": { \"tableId\": \"wt\", \"caption\": \"Teil 1\", \"checkable\": true, \"columns\": [ { \"key\": \"name\", \"label\": \"Name\" } ], \"rows\": [ "
        + ifritRow + ", { \"itemId\": \"wt-ifrit-boss\", \"cells\": { \"name\": \"Ifrit (Boss)\" } } ] } }, "
        + "{ \"type\": \"sources\", \"sources\": [ { \"title\": \"Wiki\", \"url\": \"https://example.org/wiki\" } ] } ] }\n```";

    [Theory]
    [InlineData("{ \"itemId\": \"wt-ifrit\", \"ref\": \"espers:ifrit\", \"cells\": { \"name\": \"Ifrit\" } }")]          // same own id, now a ref row
    [InlineData("{ \"itemId\": \"wt-ifrit-neu\", \"ref\": \"espers:ifrit\", \"cells\": { \"name\": \"Ifrit\" } }")]      // new own id plus a ref
    public async Task Walkthrough_redone_a_ticked_definer_that_comes_back_as_a_ref_row_hands_its_tick_to_the_target(string ifritRow)
    {
        var (server, _) = await StartAsync(_ => "{}");
        await using (server)
        {
            await SeedAsync(server);
            var (walkId, _) = await SeedRefComebackAsync(server);

            var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/chapter-answer",
                new { text = WalkthroughAnswer(ifritRow), title = "Komplettlösung", topicId = WalkthroughTab.TopicId, tabId = walkId, replace = true });

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.True(body.GetProperty("replaced").GetBoolean(), body.ToString());
            var (walk, checks) = await StoredAsync(server, walkId);
            Assert.Contains("espers:ifrit", walk.SectionsJson);
            Assert.Contains("espers:ifrit", checks);          // the row shows the tick of its target - the progress is kept
            Assert.DoesNotContain("wt-ifrit", checks);        // no hidden tick under the ref row's own id
            Assert.DoesNotContain("wt-ifrit-neu", checks);
        }
    }

    [Fact]
    public async Task A_batch_with_the_walkthrough_placeholder_and_a_second_walkthrough_answer_replaces_the_walkthrough_once()
    {
        var (server, _) = await StartAsync(_ => "{}");
        await using (server)
        {
            await SeedAsync(server);
            Guid placeholderId;
            using (var scope = server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
                var game = await db.Games.FirstAsync(g => g.Slug == "suncraft");
                var placeholder = new GuideTabEntity
                {
                    Id = Guid.NewGuid(), GameId = game.Id, Slug = WalkthroughTab.Slug, Title = "Komplettlösung", SortOrder = 0, TopicId = WalkthroughTab.TopicId,
                    SectionsJson = """[ { "type": "alert", "variant": "danger", "title": "Fehlgeschlagen", "htmlContent": "x", "failedChapter": { "focus": "Das ganze Spiel", "topicId": "walkthrough", "message": "x" } } ]"""
                };
                db.GuideTabs.Add(placeholder);
                await db.SaveChangesAsync();
                placeholderId = placeholder.Id;
            }
            string Answer(string key, string chest) => WalkthroughAnswer("{ \"itemId\": \"" + chest + "\", \"cells\": { \"name\": \"" + chest + "\" } }")
                .Replace("\"game\": \"Suncraft\",", "\"game\": \"Suncraft\", \"chapter\": \"" + key + "\",");
            var text = Answer("K01", "truhe-eins") + "\n" + Answer("K02", "truhe-zwei");

            var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/batch-answer", new
            {
                text,
                chapters = new object[]
                {
                    new { key = "K01", title = "Komplettlösung", topicId = WalkthroughTab.TopicId, tabId = placeholderId },
                    new { key = "K02", title = "Komplettlösung", topicId = WalkthroughTab.TopicId }
                }
            });

            Assert.Equal(HttpStatusCode.OK, status);
            var saved = Assert.Single(body.GetProperty("saved").EnumerateArray());
            Assert.Equal("K01", saved.GetProperty("key").GetString());
            var skipped = Assert.Single(body.GetProperty("skipped").EnumerateArray());
            Assert.Equal("K02", skipped.GetProperty("key").GetString());
            var (walk, _) = await StoredAsync(server, placeholderId);
            Assert.Contains("truhe-eins", walk.SectionsJson);
            Assert.DoesNotContain("truhe-zwei", walk.SectionsJson);
        }
    }

    // ---- second review: stale state after a long job, a redo attaching to another chapter's job ---------------------------------------

    /// <summary>An AI answer that waits until the test lets it go (so the test can change the guide while the job runs).</summary>
    private sealed class HeldAnswer(string answer)
    {
        public ManualResetEventSlim Reached { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public string Answer(string _)
        {
            Reached.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
            return answer;
        }
    }

    private static async Task<GuideTabEntity> AddTabAsync(TestServer server, string slug, string title, int sort, string sectionsJson, string? topicId = null)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var game = await db.Games.FirstAsync(g => g.Slug == "suncraft");
        var tab = new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = slug, Title = title, SortOrder = sort, TopicId = topicId, SectionsJson = sectionsJson };
        db.GuideTabs.Add(tab);
        await db.SaveChangesAsync();
        return tab;
    }

    private static string WalkRows(params (string Id, string Name, string? Ref)[] rows) =>
        "[ { \"type\": \"checklist_table\", \"table\": { \"tableId\": \"wt\", \"caption\": \"Teil 1\", \"checkable\": true, \"columns\": [ { \"key\": \"name\", \"label\": \"Name\" } ], \"rows\": [ "
        + string.Join(", ", rows.Select(r => "{ \"itemId\": \"" + r.Id + "\"," + (r.Ref is null ? "" : " \"ref\": \"" + r.Ref + "\",") + " \"cells\": { \"name\": \"" + r.Name + "\" } }"))
        + " ] } } ]";

    private static async Task ChangeDuringJobAsync(TestServer server, Guid walkId, string newSections, string? addTick, string? removeTick)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var walk = await db.GuideTabs.FirstAsync(t => t.Id == walkId);
        walk.SectionsJson = newSections;
        if (addTick is not null)
        {
            db.ProgressChecks.Add(new ProgressCheckEntity { Id = Guid.NewGuid(), GameId = walk.GameId, ItemKey = addTick, CheckedAt = DateTimeOffset.UtcNow });
        }
        if (removeTick is not null)
        {
            db.ProgressChecks.RemoveRange(db.ProgressChecks.Where(p => p.ItemKey == removeTick));
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_redo_saves_on_the_fresh_state_and_never_writes_back_what_changed_while_it_ran()
    {
        var held = new HeldAnswer(NewWeapons);                      // the new version has no bow any more
        var (server, _) = await StartAsync(held.Answer);
        await using (server)
        {
            var seed = await SeedAsync(server);                     // ticks: schwert, bogen
            var walk = await AddTabAsync(server, WalkthroughTab.Slug, "Komplettlösung", 0, WalkRows(("wt-bogen", "Bogen (Truhe)", "bogen")), WalkthroughTab.TopicId);

            var (_, job) = await PostAsync(server, $"/api/games/suncraft/chapters/{seed.Real}/redo", null);
            Assert.True(held.Reached.Wait(TimeSpan.FromSeconds(30)));
            // Meanwhile (e.g. the other dev server): the walkthrough gets a row, the bow is unticked
            await ChangeDuringJobAsync(server, walk.Id, WalkRows(("wt-bogen", "Bogen (Truhe)", "bogen"), ("wt-neu", "Neue Truhe", null)), addTick: "wt-neu", removeTick: "bogen");
            held.Release.Set();
            var done = await WaitForJobAsync(server, job);
            Assert.True(done.GetProperty("error").ValueKind == JsonValueKind.Null, done.ToString());

            var (storedWalk, checks) = await StoredAsync(server, walk.Id);
            Assert.Contains("wt-neu", storedWalk.SectionsJson);     // the change made during the job survives
            Assert.DoesNotContain("\"ref\"", storedWalk.SectionsJson.Replace(" ", ""));   // the bow is gone: its ref row is a normal row again
            Assert.Contains("wt-neu", checks);
            Assert.DoesNotContain("wt-bogen", checks);              // the bow was unticked during the job: the freed row takes no stale tick
            Assert.DoesNotContain("bogen", checks);
        }
    }

    [Fact]
    public async Task A_redo_of_a_chapter_removed_while_it_ran_saves_nothing_and_says_why()
    {
        var held = new HeldAnswer(NewWeapons);
        var (server, _) = await StartAsync(held.Answer);
        await using (server)
        {
            var seed = await SeedAsync(server);
            var (_, job) = await PostAsync(server, $"/api/games/suncraft/chapters/{seed.Real}/redo", null);
            Assert.True(held.Reached.Wait(TimeSpan.FromSeconds(30)));
            using (var scope = server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
                db.GuideTabs.Remove(await db.GuideTabs.FirstAsync(t => t.Id == seed.Real));
                await db.SaveChangesAsync();
            }
            held.Release.Set();
            var done = await WaitForJobAsync(server, job);

            Assert.Contains("in der Zwischenzeit entfernt", done.GetProperty("error").GetString());
            using var check = server.Services.CreateScope();
            Assert.False(await check.ServiceProvider.GetRequiredService<CompanionDbContext>().GuideTabs.AnyAsync(t => t.Id == seed.Real));
        }
    }

    [Fact]
    public async Task Adding_a_chapter_saves_only_the_new_chapter_and_links_on_the_fresh_state()
    {
        const string Axes = """{ "title": "Äxte", "icon": "mdi:axe", "sections": [ { "type": "checklist_table", "table": { "tableId": "a", "caption": "Äxte", "checkable": true, "columns": [ { "key": "name", "label": "Name" } ], "rows": [ { "itemId": "axt", "cells": { "name": "Axt" } } ] } } ] }""";
        var held = new HeldAnswer(Axes);
        var (server, _) = await StartAsync(held.Answer);
        await using (server)
        {
            await SeedAsync(server);
            var walk = await AddTabAsync(server, WalkthroughTab.Slug, "Komplettlösung", 0, WalkRows(("wt-axt", "Axt", null)), WalkthroughTab.TopicId);

            var (status, job) = await PostAsync(server, "/api/games/suncraft/ai-expand-tab", new { tabTitle = "Äxte", topicPrompt = "Alle Äxte" });
            Assert.Equal(HttpStatusCode.Accepted, status);
            Assert.True(held.Reached.Wait(TimeSpan.FromSeconds(30)));
            await ChangeDuringJobAsync(server, walk.Id, WalkRows(("wt-axt", "Axt", null), ("wt-neu", "Neue Truhe", null)), addTick: null, removeTick: null);
            held.Release.Set();
            var done = await WaitForJobAsync(server, job);
            Assert.True(done.GetProperty("error").ValueKind == JsonValueKind.Null, done.ToString());

            var (storedWalk, _) = await StoredAsync(server, walk.Id);
            Assert.Contains("wt-neu", storedWalk.SectionsJson);                         // not overwritten by the state from before the research
            Assert.Contains("\"ref\":\"axt\"", storedWalk.SectionsJson.Replace(" ", "")); // and linked to the new chapter
        }
    }

    [Fact]
    public async Task A_redo_of_another_chapter_while_one_runs_is_refused_and_the_same_chapter_attaches()
    {
        var held = new HeldAnswer(NewWeapons);
        var (server, _) = await StartAsync(held.Answer);
        await using (server)
        {
            var seed = await SeedAsync(server);
            var other = await AddTabAsync(server, "tab-3-ruestung", "Rüstung", 3, WalkRows(("helm", "Helm", null)));

            var (_, first) = await PostAsync(server, $"/api/games/suncraft/chapters/{seed.Real}/redo", null);
            Assert.True(held.Reached.Wait(TimeSpan.FromSeconds(30)));
            var (otherStatus, otherBody) = await PostAsync(server, $"/api/games/suncraft/chapters/{other.Id}/redo", null);
            var (sameStatus, sameBody) = await PostAsync(server, $"/api/games/suncraft/chapters/{seed.Real}/redo", null);
            held.Release.Set();
            await WaitForJobAsync(server, first);

            Assert.Equal(HttpStatusCode.Conflict, otherStatus);
            Assert.Contains("„Waffen“", otherBody.GetProperty("detail").GetString());   // names the chapter that is being researched
            Assert.Equal(HttpStatusCode.Accepted, sameStatus);
            Assert.Equal(first.GetProperty("id").GetString(), sameBody.GetProperty("id").GetString());
        }
    }

    [Fact]
    public async Task A_replace_answer_that_says_the_topic_does_not_exist_never_deletes_a_chapter_with_content()
    {
        var (server, _) = await StartAsync(_ => "{}");
        await using (server)
        {
            var seed = await SeedAsync(server);
            var text = "```json\n{ \"game\": \"Suncraft\", \"chapter\": \"K01\", \"notApplicable\": true, \"reason\": \"Gibt es nicht.\" }\n```";

            var result = await PostAsync(server, "/api/games/suncraft/manual/batch-answer",
                new { text, chapters = new[] { new { key = "K01", title = "Waffen", topicId = "weapons", tabId = seed.Real, replace = true } } });

            Assert.Equal(0, result.Body.GetProperty("saved").GetArrayLength());
            Assert.Contains("nicht gelöscht", result.Body.GetProperty("skipped")[0].GetProperty("reason").GetString());
            Assert.Equal(["bogen", "schwert"], (await StoredAsync(server, seed.Real)).Checks);
        }
    }
}
