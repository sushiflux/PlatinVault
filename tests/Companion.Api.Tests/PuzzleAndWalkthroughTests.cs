using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>
/// The puzzle block (location + complete solution, hidden until asked for), the typed hints / sub-headings / numbered steps of the walkthrough, and the
/// walkthrough chapter itself (first in the guide, outside the budget, only with a reliable source, researched in parts).
/// </summary>
public sealed class PuzzleAndWalkthroughTests
{
    private static JsonArray Normalize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        using var scope = Lang.Use(Lang.German);
        return GuideSectionNormalizer.Normalize(doc.RootElement);
    }

    private static AiGuideGeneratorService.GameContext Context(string language, ResearchBlocks.Selection? blocks = null) => new(
        "Final Fantasy Resonance",
        "- Genre: RPG",
        [],
        [],
        blocks ?? ResearchBlocks.Selection.Empty,
        ResearchBlocks.FillBase(language, "Final Fantasy Resonance", "RPG", ResearchBlocks.PlatformText),
        language);

    // ---- Normalizer -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_puzzle_keeps_location_requirements_hints_and_steps_and_is_documented()
    {
        var result = Normalize("""
            [ { "type": "puzzle", "title": "Grabstein in der Rubinhöhle", "htmlContent": "Sammle die Fragmente.",
                "puzzle": { "itemId": "rubin-raetsel", "location": "Rubinhöhle, Olderion", "requirements": "Ab Kapitel 3", "missable": "ja",
                            "hints": ["Die Farben zählen.", " Rot zuerst. "], "steps": ["1. Nimm das rote Fragment.", "Schritt 2: Dann das blaue."], "source": "PowerPyx" } } ]
            """);

        var section = Assert.IsType<JsonObject>(Assert.Single(result));
        var puzzle = section["puzzle"]!.AsObject();
        Assert.Equal("rubin-raetsel", puzzle["itemId"]!.ToString());
        Assert.Equal("Rubinhöhle, Olderion", puzzle["location"]!.ToString());
        Assert.True(puzzle["missable"]!.GetValue<bool>());
        Assert.True(puzzle["documented"]!.GetValue<bool>());
        Assert.Equal(["Die Farben zählen.", "Rot zuerst."], puzzle["hints"]!.AsArray().Select(x => x!.ToString()));
        // The list numbers the steps itself: a number the model wrote is removed
        Assert.Equal(["Nimm das rote Fragment.", "Dann das blaue."], puzzle["steps"]!.AsArray().Select(x => x!.ToString()));
    }

    [Fact]
    public void A_puzzle_without_steps_is_marked_undocumented_instead_of_showing_empty_buttons()
    {
        var puzzle = Normalize("""[ { "type": "puzzle", "title": "Geheimtür", "puzzle": { "location": "Keller", "steps": [], "hints": [] } } ]""")[0]!["puzzle"]!.AsObject();

        Assert.False(puzzle["documented"]!.GetValue<bool>());
        Assert.Null(puzzle["steps"]);
        Assert.Null(puzzle["hints"]);
    }

    [Fact]
    public void Puzzle_fields_next_to_the_type_are_tolerated_and_steps_as_one_text_are_split_into_lines()
    {
        var puzzle = Normalize("""[ { "type": "puzzle", "title": "Code", "location": "Labor", "steps": "Eins\nZwei\n\nDrei" } ]""")[0]!["puzzle"]!.AsObject();

        Assert.Equal("Labor", puzzle["location"]!.ToString());
        Assert.Equal(["Eins", "Zwei", "Drei"], puzzle["steps"]!.AsArray().Select(x => x!.ToString()));
        Assert.True(puzzle["documented"]!.GetValue<bool>());
    }

    [Fact]
    public void An_empty_puzzle_and_a_heading_without_title_are_dropped()
    {
        Assert.Empty(Normalize("""[ { "type": "puzzle", "puzzle": { "steps": ["x"] } }, { "type": "heading", "htmlContent": "nur Lead" } ]"""));
    }

    [Theory]
    [InlineData("must_have", "gold")]
    [InlineData("must-have", "gold")]
    [InlineData("MISSABLE", "danger")]
    [InlineData("optional", "emerald")]
    [InlineData("tip", "crystal")]
    public void A_hint_kind_is_cleaned_up_and_gets_its_colour_when_none_is_given(string kind, string variant)
    {
        var section = Normalize($$"""[ { "type": "alert", "kind": "{{kind}}", "title": "x", "htmlContent": "y" } ]""")[0]!.AsObject();

        Assert.Equal(variant, section["variant"]!.ToString());
        Assert.Equal(kind.ToLowerInvariant().Replace('-', '_'), section["kind"]!.ToString());
    }

    [Fact]
    public void An_unknown_hint_kind_is_dropped_and_an_explicit_variant_wins()
    {
        var sections = Normalize("""
            [ { "type": "alert", "kind": "sparkle", "variant": "gold", "title": "a" },
              { "type": "alert", "kind": "missable", "variant": "crystal", "title": "b" } ]
            """);

        Assert.Null(sections[0]!["kind"]);
        Assert.Equal("gold", sections[0]!["variant"]!.ToString());
        Assert.Equal("crystal", sections[1]!["variant"]!.ToString());
    }

    [Fact]
    public void Steps_stories_are_numbered_by_the_list_and_other_story_variants_are_removed()
    {
        var sections = Normalize("""
            [ { "type": "story", "variant": "steps", "paragraphs": ["1. Geh nach Norden.", "2) Sprich mit Ada."] },
              { "type": "story", "variant": "fancy", "paragraphs": ["Text"] } ]
            """);

        Assert.Equal("steps", sections[0]!["variant"]!.ToString());
        Assert.Equal(["Geh nach Norden.", "Sprich mit Ada."], sections[0]!["paragraphs"]!.AsArray().Select(x => x!.ToString()));
        Assert.Null(sections[1]!["variant"]);
    }

    [Fact]
    public void Only_a_real_running_numbering_is_removed_from_steps_never_content_like_an_ordinal()
    {
        // "1. Stock" is content: a single step, steps that do not run 1, 2, 3 ..., or a list that does not start at 1 stay as written
        var single = Normalize("""[ { "type": "puzzle", "title": "Aufzug", "puzzle": { "location": "Turm", "steps": ["1. Stock: Hebel ziehen"] } } ]""")[0]!["puzzle"]!["steps"]!.AsArray();
        Assert.Equal(["1. Stock: Hebel ziehen"], single.Select(x => x!.ToString()));

        var mixed = Normalize("""[ { "type": "puzzle", "title": "Aufzug", "puzzle": { "location": "Turm", "steps": ["1. Stock: Hebel ziehen", "Dann in den Keller"] } } ]""")[0]!["puzzle"]!["steps"]!.AsArray();
        Assert.Equal(["1. Stock: Hebel ziehen", "Dann in den Keller"], mixed.Select(x => x!.ToString()));

        var notFromOne = Normalize("""[ { "type": "story", "variant": "steps", "paragraphs": ["2. Stock: Schlüssel holen", "3. Stock: Tür öffnen"] } ]""")[0]!["paragraphs"]!.AsArray();
        Assert.Equal(["2. Stock: Schlüssel holen", "3. Stock: Tür öffnen"], notFromOne.Select(x => x!.ToString()));

        // a numbering the model really wrote (1, 2, 3 ...) goes, in puzzle steps and in the steps of the walkthrough, and normalizing twice changes nothing
        var numbered = Normalize("""[ { "type": "story", "variant": "steps", "paragraphs": ["1. Geh nach Norden.", "2. Sprich mit Ada.", "3. Nimm den Schlüssel."] } ]""");
        Assert.Equal(["Geh nach Norden.", "Sprich mit Ada.", "Nimm den Schlüssel."], numbered[0]!["paragraphs"]!.AsArray().Select(x => x!.ToString()));
        using var again = JsonDocument.Parse(numbered.ToJsonString());
        using var scope = Lang.Use(Lang.German);
        Assert.Equal(numbered.ToJsonString(), GuideSectionNormalizer.Normalize(again.RootElement).ToJsonString());
    }

    [Fact]
    public void Old_guides_stay_valid_and_the_new_content_survives_the_dto_round_trip()
    {
        var sections = Normalize("""
            [ { "type": "alert", "variant": "danger", "title": "Verpassbar", "htmlContent": "Alt" },
              { "type": "story", "paragraphs": ["Alt"] },
              { "type": "puzzle", "title": "R", "puzzle": { "itemId": "r-1", "steps": ["a"] } } ]
            """);

        Assert.Equal(3, sections.Count);
        Assert.Null(sections[0]!["kind"]);
        var dtos = JsonSerializer.Deserialize<List<GuideSectionDto>>(sections.ToJsonString(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("r-1", dtos[2].Puzzle!.ItemId);
        Assert.Equal(["a"], dtos[2].Puzzle!.Steps);
        Assert.True(dtos[2].Puzzle!.Documented);
    }

    // ---- Progress ids -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_tickable_puzzle_counts_like_a_checklist_row_and_its_id_is_made_unique_per_game()
    {
        var sections = new List<GuideSectionDto> { new("puzzle", Title: "R", Puzzle: new GuidePuzzleDto(ItemId: "p-1", Steps: ["a"])), new("puzzle", Title: "ohne id", Puzzle: new GuidePuzzleDto()) };
        Assert.Equal(["p-1"], ApiEndpoints.CheckableItemIds(sections));

        var tab = new GuideTabEntity { Slug = "walkthrough", SectionsJson = JsonSerializer.Serialize(sections, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        var taken = new HashSet<string> { "p-1" };
        ApiEndpoints.MakeItemIdsUnique([tab], taken);

        var ids = ApiEndpoints.CollectItemIds([tab]);
        Assert.DoesNotContain("p-1", ids);
        Assert.Contains("walkthrough:p-1", ids);
        Assert.Single(ids);
    }

    // ---- Translation --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_texts_of_a_puzzle_are_translatable_and_its_structure_is_not()
    {
        var original = Normalize("""
            [ { "type": "puzzle", "title": "Rubinrätsel", "htmlContent": "Löse es.",
                "puzzle": { "itemId": "rubin", "location": "Rubinhöhle", "requirements": "Kapitel 3", "missable": true,
                            "hints": ["Zähle die Farben."], "steps": ["Nimm Rot.", "Nimm Blau."], "source": "PowerPyx" } } ]
            """);

        var slots = GuideTextExtractor.Collect(original);
        var texts = slots.Select(s => s.Text).ToList();
        foreach (var expected in new[] { "Rubinrätsel", "Löse es.", "Rubinhöhle", "Kapitel 3", "Zähle die Farben.", "Nimm Rot.", "Nimm Blau." })
        {
            Assert.Contains(expected, texts);
        }
        Assert.DoesNotContain("rubin", texts);
        Assert.DoesNotContain("PowerPyx", texts);
        Assert.Equal(["rubin"], GuideTextExtractor.ItemIds(original));

        var translated = (JsonArray)original.DeepClone();
        var map = new Dictionary<string, string>
        {
            ["Rubinrätsel"] = "Ruby puzzle", ["Löse es."] = "Solve it.", ["Rubinhöhle"] = "Ruby Cave", ["Kapitel 3"] = "Chapter 3",
            ["Zähle die Farben."] = "Count the colours.", ["Nimm Rot."] = "Take red.", ["Nimm Blau."] = "Take blue."
        };
        Assert.Equal(7, GuideTextExtractor.Apply(GuideTextExtractor.Collect(translated), map));

        var validated = GuideTextExtractor.ValidateTranslated(original, translated, "Walkthrough");
        var puzzle = validated[0]!["puzzle"]!.AsObject();
        Assert.Equal("rubin", puzzle["itemId"]!.ToString());
        Assert.True(puzzle["missable"]!.GetValue<bool>());
        Assert.Equal("Take red.", puzzle["steps"]![0]!.ToString());
        Assert.Equal("Ruby Cave", puzzle["location"]!.ToString());
    }

    // ---- Manual answer check ------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_manual_answer_check_warns_about_a_puzzle_without_a_solution_or_location_and_about_an_unknown_hint_kind()
    {
        var sections = JsonNode.Parse("""
            [ { "type": "puzzle", "title": "Geheimtür", "puzzle": { "hints": ["x"] } },
              { "type": "puzzle", "title": "Vollständig", "puzzle": { "location": "Keller", "steps": ["a", "b"] } },
              { "type": "alert", "kind": "sparkle", "title": "x" },
              { "type": "heading" },
              { "type": "heading", "title": "Kapitel 1" } ]
            """)!.AsArray();

        var warnings = new List<ManualIssueDto>();
        ManualAnswers.ScanSections(sections, warnings);

        var codes = warnings.Select(w => w.Code).ToList();
        Assert.Contains("puzzleWithoutSteps", codes);
        Assert.Contains("puzzleWithoutLocation", codes);
        Assert.Contains("alertKind", codes);
        Assert.Contains("sectionSkipped", codes);
        // exactly one warning per problem: the complete puzzle and the valid heading cause none
        Assert.Equal(1, codes.Count(c => c == "puzzleWithoutSteps"));
        Assert.Equal(1, codes.Count(c => c == "puzzleWithoutLocation"));
        Assert.DoesNotContain(warnings, w => w.Message.Contains("Vollständig"));
    }

    [Fact]
    public void The_manual_flow_accepts_the_new_section_types()
    {
        Assert.Contains("puzzle", GuideSectionNormalizer.KnownTypes);
        Assert.Contains("heading", GuideSectionNormalizer.KnownTypes);
        var warnings = new List<ManualIssueDto>();
        ManualAnswers.ScanSections(JsonNode.Parse("""[ { "type": "puzzle", "title": "P", "puzzle": { "location": "L", "steps": ["a"] } } ]""")!.AsArray(), warnings);
        Assert.Empty(warnings);
    }

    // ---- Prompts ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_chapter_prompt_carries_the_puzzle_rule_in_german_and_english_and_keeps_its_structure()
    {
        var german = AiGuideGeneratorService.BuildChapterPrompt(Context(Lang.German), "Bosse", "Alle Bosse", "etabliert");
        Assert.Contains("Rätsel (verbindliche Regel)", german);
        Assert.Contains("VOLLSTÄNDIGE LÖSUNG", german);
        Assert.Contains("Erfinde NIEMALS Codes, Positionen oder Reihenfolgen", german);
        Assert.Contains("WO genau", german);
        Assert.Contains("Lösung nicht dokumentiert", german);
        Assert.Contains("\"type\": \"puzzle\"", german);
        Assert.Contains("Aufbau (Pflicht, in dieser Reihenfolge)", german);

        var english = AiGuideGeneratorService.BuildChapterPrompt(Context(Lang.English), "Bosses", "All bosses", "established");
        Assert.Contains("Puzzles and riddles (mandatory rule)", english);
        Assert.Contains("COMPLETE SOLUTION", english);
        Assert.Contains("NEVER invent codes, positions or orders", english);
        Assert.Contains("WHERE exactly", english);
        Assert.Contains("Solution not documented", english);
        Assert.Contains("\"type\": \"puzzle\"", english);
        Assert.Contains("Structure (mandatory, in this order)", english);
        Assert.DoesNotContain("Rätsel (verbindliche Regel)", english);
    }

    [Fact]
    public void The_blueprint_tells_the_walkthrough_rule_and_that_it_does_not_count_against_the_budget()
    {
        var german = AiGuideGeneratorService.BuildBlueprintPrompt(Context(Lang.German), null, []);
        Assert.Contains("walkthrough", german);
        Assert.Contains("belastbare, VOLLSTÄNDIGE", german);
        Assert.Contains("zählt NICHT zur Kapitelzahl", german);
        Assert.Contains("steht im Guide immer an erster Stelle", german);

        var english = AiGuideGeneratorService.BuildBlueprintPrompt(Context(Lang.English), null, []);
        Assert.Contains("reliable, COMPLETE", english);
        Assert.Contains("does NOT count against the chapter total", english);
        Assert.Contains("always comes first in the guide", english);
    }

    [Fact]
    public void The_walkthrough_part_prompt_has_the_four_hint_kinds_the_puzzle_rule_and_the_own_words_rule()
    {
        var outline = new AiGuideGeneratorService.WalkthroughOutline(["PowerPyx – https://example.invalid"], [new("Kapitel 1 – Grandshelt", "Start"), new("Kapitel 2 – Dirnado", "Ramuh")]);
        foreach (var (language, own) in new[] { (Lang.German, "EIGENEN WORTEN"), (Lang.English, "YOUR OWN WORDS") })
        {
            var prompt = AiGuideGeneratorService.BuildWalkthroughPartPrompt(Context(language), outline, 1, ["Beste Waffen"]);
            foreach (var kind in new[] { "must_have", "missable", "optional", "tip" })
            {
                Assert.Contains($"\"{kind}\"", prompt);
            }
            Assert.Contains(own, prompt);
            Assert.Contains("-> 2. Kapitel 2 – Dirnado", prompt);
            Assert.Contains("PowerPyx", prompt);
            Assert.Contains("Beste Waffen", prompt);
            Assert.Contains("\"puzzle\"", prompt);
            Assert.Contains(language == Lang.English ? "NEVER invent codes" : "Erfinde NIEMALS Codes", prompt);
        }
    }

    [Fact]
    public void The_scope_and_the_spoiler_block_reach_the_walkthrough_prompts()
    {
        var selection = new ResearchBlocks.Selection("compact", [], "none", null);
        var outline = new AiGuideGeneratorService.WalkthroughOutline([], [new("Teil", "Fokus")]);

        var part = AiGuideGeneratorService.BuildWalkthroughPartPrompt(Context(Lang.English, selection), outline, 0, []);
        Assert.Contains("Depth: compact", part);
        Assert.Contains("spoiler-free", part);

        var exhaustive = AiGuideGeneratorService.BuildWalkthroughPartPrompt(Context(Lang.German, new ResearchBlocks.Selection("exhaustive", [], null, null)), outline, 0, []);
        Assert.Contains("Tiefe: sehr ausführlich", exhaustive);
    }

    [Fact]
    public void The_outline_prompt_asks_for_sources_and_allows_no_walkthrough_at_all()
    {
        var english = AiGuideGeneratorService.BuildWalkthroughOutlinePrompt(Context(Lang.English), "focus", "established", 12);
        Assert.Contains("\"reliable\": false", english);
        Assert.Contains("at most 12 parts", english);
        Assert.Contains("never invent a walkthrough", english);

        var german = AiGuideGeneratorService.BuildWalkthroughOutlinePrompt(Context(Lang.German), "focus", "etabliert", 6);
        Assert.Contains("höchstens 6 Teile", german);
        Assert.Contains("erfinde", german);
    }

    [Fact]
    public void The_manual_flow_uses_a_single_compact_walkthrough_prompt_for_the_walkthrough_topic()
    {
        var game = new GameEntity { Title = "Final Fantasy Resonance", SteamAppId = 1462040, ContentLanguage = Lang.English };
        var prompt = AiGuideGeneratorService.ComposeManualChapterPrompt(Context(Lang.English), game, Lang.English, "Walkthrough", "focus", "established", "walkthrough");
        Assert.Contains("WALKTHROUGH chapter", prompt);
        Assert.Contains("fit into ONE answer", prompt);
        Assert.Contains("Output in this chat", prompt);

        var other = AiGuideGeneratorService.ComposeManualChapterPrompt(Context(Lang.English), game, Lang.English, "Bosses", "focus", "established", "bestiary");
        Assert.DoesNotContain("WALKTHROUGH chapter", other);
    }

    // ---- The walkthrough chapter -----------------------------------------------------------------------------------------------

    private static JsonElement Blueprint(params (string Id, bool Applicable)[] topics)
    {
        var root = new JsonObject
        {
            ["profiles"] = new JsonArray("adventure"),
            ["topics"] = new JsonArray(topics.Select(t => (JsonNode)new JsonObject { ["id"] = t.Id, ["applicable"] = t.Applicable }).ToArray())
        };
        return JsonDocument.Parse(root.ToJsonString()).RootElement;
    }

    [Fact]
    public void The_walkthrough_is_the_first_standard_topic_in_both_languages_and_belongs_to_the_start_group()
    {
        foreach (var language in new[] { Lang.German, Lang.English })
        {
            Assert.Equal("walkthrough", AiGuideGeneratorService.Topics(language)[0].Id);
        }
        Assert.Equal(GuideGroups.Start, GuideGroups.ForTopic("walkthrough"));
    }

    [Fact]
    public void Puzzles_are_a_standard_topic_with_a_group_so_an_existing_guide_can_get_them_via_complete_guide()
    {
        foreach (var language in new[] { Lang.German, Lang.English })
        {
            var topic = Assert.Single(AiGuideGeneratorService.Topics(language), t => t.Id == "puzzles");
            Assert.Contains(language == Lang.English ? "complete solution" : "vollständigen Lösung", topic.Brief);
        }
        Assert.Equal(GuideGroups.Explore, GuideGroups.ForTopic("puzzles"));
    }

    [Fact]
    public void The_walkthrough_needs_the_confirmation_of_the_research_even_for_a_genre_that_usually_has_one()
    {
        // "adventure" lists the walkthrough as typical, but the AI said nothing about it: no chapter without a verdict
        var silent = AiGuideGeneratorService.PickTopics(Blueprint(("endings", true)), new HashSet<string>(), Lang.German, null, 0, 0);
        Assert.DoesNotContain(silent, p => p.Topic.Id == "walkthrough" && p.Kept);

        var denied = AiGuideGeneratorService.PickTopics(Blueprint(("walkthrough", false)), new HashSet<string>(), Lang.German, null, 0, 0);
        Assert.DoesNotContain(denied, p => p.Topic.Id == "walkthrough" && p.Kept);

        var confirmed = AiGuideGeneratorService.PickTopics(Blueprint(("walkthrough", true)), new HashSet<string>(), Lang.German, null, 0, 0);
        Assert.Contains(confirmed, p => p.Topic.Id == "walkthrough" && p.Kept);
    }

    [Fact]
    public void An_existing_walkthrough_counts_as_covered_whatever_the_ai_says_so_it_is_never_created_twice()
    {
        var picks = AiGuideGeneratorService.PickTopics(Blueprint(("walkthrough", true)), new HashSet<string>(), Lang.German, null, 0, 0, hasWalkthrough: true);

        var walkthrough = Assert.Single(picks, p => p.Topic.Id == "walkthrough");
        Assert.True(walkthrough.Covered);
        Assert.False(walkthrough.Include);
        Assert.False(walkthrough.Kept);
    }

    [Fact]
    public void The_walkthrough_takes_no_slot_of_the_chapter_budget()
    {
        // compact = at most 8 chapters: eight other applicable topics plus the walkthrough must all be kept
        var others = AiGuideGeneratorService.Topics(Lang.German).Select(t => t.Id).Where(id => id != "walkthrough").Take(8).ToArray();
        var root = Blueprint([.. others.Select(id => (id, true)), ("walkthrough", true)]);

        var kept = AiGuideGeneratorService.PickTopics(root, new HashSet<string>(), Lang.German, "compact", 0, 0).Where(p => p.Kept).Select(p => p.Topic.Id).ToList();

        Assert.Contains("walkthrough", kept);
        Assert.Equal(9, kept.Count);
        Assert.Equal(8, kept.Count(id => id != "walkthrough"));
    }

    [Fact]
    public void The_walkthrough_is_listed_first_whatever_its_sort_order_and_does_not_count_for_the_budget()
    {
        var tabs = new List<GuideTabEntity>
        {
            new() { Slug = "tab-1-roadmap", SortOrder = 1 },
            new() { Slug = "tab-2-bosse", SortOrder = 2 },
            new() { Slug = WalkthroughTab.Slug, SortOrder = 9 },
            new() { Slug = LoreTab.Slug, SortOrder = 900 }
        };

        Assert.Equal(WalkthroughTab.Slug, WalkthroughTab.InGuideOrder(tabs).First().Slug);
        Assert.Equal(LoreTab.Slug, WalkthroughTab.InGuideOrder(tabs).Last().Slug);
        Assert.Equal(2, WalkthroughTab.BudgetCount(tabs));
    }

    [Theory]
    [InlineData("compact", 6)]
    [InlineData(null, 12)]
    [InlineData("detailed", 12)]
    [InlineData("exhaustive", 20)]
    public void The_scope_sets_how_finely_the_walkthrough_is_split(string? scope, int parts)
    {
        Assert.Equal(parts, WalkthroughTab.MaxParts(scope));
    }

    [Fact]
    public void The_outline_is_read_cleaned_and_cut_to_the_limit()
    {
        using var scope = Lang.Use(Lang.German);
        var parts = string.Join(",", Enumerable.Range(1, 9).Select(i => $$"""{ "title": "{{i}}. Kapitel {{i}}", "focus": "f{{i}}" }"""));
        var outline = AiGuideGeneratorService.ParseWalkthroughOutline($$"""{ "reliable": true, "sources": ["A", " ", "B"], "parts": [ {{parts}} ] }""", 6);

        Assert.Equal(6, outline.Parts.Count);
        Assert.Equal("Kapitel 1", outline.Parts[0].Title);
        Assert.Equal(["A", "B"], outline.Sources);
    }

    [Fact]
    public void No_reliable_source_means_no_walkthrough_and_the_reason_is_told()
    {
        using var scope = Lang.Use(Lang.German);
        var unreliable = Assert.Throws<LocalizedException>(() =>
            AiGuideGeneratorService.ParseWalkthroughOutline("""{ "reliable": false, "reason": "Spiel erst gestern erschienen", "parts": [] }""", 12));
        Assert.Contains("keine belastbare, vollständige Quelle", unreliable.Message);
        Assert.Contains("Spiel erst gestern erschienen", unreliable.Message);

        Assert.Throws<LocalizedException>(() => AiGuideGeneratorService.ParseWalkthroughOutline("""{ "reliable": true, "parts": [] }""", 12));
        Assert.Throws<LocalizedException>(() => AiGuideGeneratorService.ParseWalkthroughOutline("das ist kein JSON", 12));
    }

    [Fact]
    public void The_parts_are_merged_in_outline_order_with_the_intro_first_and_a_failed_part_becomes_an_alert()
    {
        var outline = new AiGuideGeneratorService.WalkthroughOutline(["PowerPyx"], [new("Teil A", "a"), new("Teil B", "b"), new("Teil C", "c")]);
        JsonArray Part(string heading) => new() { new JsonObject { ["type"] = "heading", ["title"] = heading } };

        var merged = AiGuideGeneratorService.MergeWalkthroughParts(
            outline,
            [(Part("Teil A"), null), (null, new InvalidOperationException("kaputt")), (Part("Teil C"), null)],
            Lang.German);

        Assert.Equal(["alert", "heading", "alert", "heading"], merged.Select(n => n!["type"]!.ToString()));
        Assert.Contains("PowerPyx", merged[0]!["htmlContent"]!.ToString());
        Assert.Equal("danger", merged[2]!["variant"]!.ToString());
        Assert.Contains("Teil B", merged[2]!["title"]!.ToString());
        // the merged chapter survives the normalizer: nothing is lost on the way into the database
        using var doc = JsonDocument.Parse(merged.ToJsonString());
        Assert.Equal(4, GuideSectionNormalizer.Normalize(doc.RootElement).Count);
    }
}
