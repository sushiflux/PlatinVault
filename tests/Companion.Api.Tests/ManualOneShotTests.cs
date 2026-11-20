using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Companion.Api.Tests;

/// <summary>
/// The whole guide from ONE prompt of the user's own AI subscription: the prompt (topic ids, budget, existing chapters, output contract), the plan read from the
/// pasted answer (errors, warnings, failed chapters mapped to their tab ids, topics the guide already has dropped) and the batch reader that skips the plan
/// element. Nothing here uses an AI provider.
/// </summary>
public sealed class ManualOneShotTests
{
    private const int AppId = 4078110;

    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    private static GuideTabEntity RealTab(string title, string? topic, int order = 1, string slug = "") => new()
    {
        Id = Guid.NewGuid(), Slug = slug.Length > 0 ? slug : $"tab-{order}", Title = title, SortOrder = order, TopicId = topic, SectionsJson = "[]"
    };

    private static GuideTabEntity FailedTab(string title, string? topic, string language = Lang.German, int order = 2) => new()
    {
        Id = Guid.NewGuid(), Slug = $"tab-{order}", Title = title, SortOrder = order, TopicId = topic,
        SectionsJson = JsonSerializer.Serialize(new[] { FailedChapters.CreateSection(language, "Auftrag", topic, "Die Recherche ist fehlgeschlagen.", null, null) }, Camel)
    };

    private static GameEntity Game(string language = Lang.German, int? appId = AppId, params GuideTabEntity[] tabs) =>
        new() { Id = Guid.NewGuid(), Slug = "suncraft", Title = "Suncraft", SteamAppId = appId, ContentLanguage = language, Tabs = [.. tabs] };

    private static AiGuideGeneratorService.GameContext Context(string language = Lang.German, ResearchBlocks.Selection? blocks = null) =>
        new("Suncraft", language == Lang.English ? "Steam AppID: 4078110\nGenres according to Steam: Simulation" : "Steam-AppID: 4078110\nGenres laut Steam: Simulation", [], [], blocks ?? ResearchBlocks.Selection.Empty,
            ResearchBlocks.FillBase(language, "Suncraft", "Simulation", ResearchBlocks.PlatformText), language);

    private static AiGuideGeneratorService Generator() =>
        new(null!, null!, null!, null!, null!, null!, Microsoft.Extensions.Options.Options.Create(new Companion.Api.Configuration.CompanionOptions()), null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<AiGuideGeneratorService>.Instance);

    private static string Prompt(GameEntity game, ResearchBlocks.Selection? blocks = null) =>
        AiGuideGeneratorService.ComposeManualOneShotPrompt(Context(game.ContentLanguage!, blocks), game, game.ContentLanguage!);

    // ---- answers --------------------------------------------------------------------------------------------------------------------

    private static JsonObject Entry(string key, string topic, string title, string? focus = "Alles dazu mit Orten, Werten und Reihenfolgen.", string icon = "mdi:home")
    {
        var entry = new JsonObject { ["chapter"] = key, ["topicId"] = topic, ["title"] = title, ["icon"] = icon };
        if (focus is not null) entry["focus"] = focus;
        return entry;
    }

    private static JsonObject Plan(IEnumerable<JsonObject> entries, int? appId = AppId, string source = "etabliert")
    {
        var plan = new JsonObject { ["game"] = "Suncraft", ["steamAppId"] = appId, ["sourceSituation"] = source };
        plan["plan"] = new JsonArray(entries.Select(e => e.DeepClone()).ToArray());
        return plan;
    }

    private static JsonObject Chapter(string key, string title, int? appId = AppId) => new()
    {
        ["game"] = "Suncraft", ["steamAppId"] = appId, ["chapter"] = key, ["title"] = title, ["icon"] = "mdi:home",
        ["sections"] = new JsonArray(
            new JsonObject { ["type"] = "alert", ["variant"] = "gold", ["title"] = "Das Wichtigste vorab", ["htmlContent"] = "Die Brücke ist der erste Ausbauschritt und verbindet das Dorf mit dem Markt, wo man alles für den Anfang findet." },
            new JsonObject
            {
                ["type"] = "checklist_table",
                ["table"] = new JsonObject
                {
                    ["tableId"] = "bau", ["caption"] = "Bauten", ["categories"] = new JsonArray("Dorf"),
                    ["columns"] = new JsonArray(new JsonObject { ["key"] = "name", ["label"] = "Name", ["isRowHeader"] = true }),
                    ["rows"] = new JsonArray(new JsonObject { ["itemId"] = "bruecke-" + key.ToLowerInvariant(), ["filterCategory"] = "Dorf", ["cells"] = new JsonObject { ["name"] = "Brücke" } })
                }
            })
    };

    /// <summary>The whole one-step answer as the chat's file holds it: the array with the plan first, one object per line.</summary>
    private static string Answer(JsonObject plan, params JsonObject[] chapters) =>
        "[\n" + string.Join(",\n", new[] { plan }.Concat(chapters).Select(o => o.ToJsonString())) + "\n]";

    private static ManualOneShotPlanDto Check(GameEntity game, string? text, string? scope = null)
    {
        using var _ = Lang.Use(Lang.German);
        return AiGuideGeneratorService.CheckManualOneShotPlan(game, text, scope).Dto;
    }

    private static ManualIssueDto Single(List<ManualIssueDto> issues, string code) => Assert.Single(issues, i => i.Code == code);

    private static List<JsonObject> StandardPlan() =>
    [
        Entry("K01", "walkthrough", "Komplettlösung", "Das ganze Spiel mit Quellen."),
        Entry("K02", "roadmap", "Roadmap & Verpassbares"),
        Entry("K03", "weapons", "Beste Waffen"),
        Entry("K04", "extra", "Brückenbau im Detail")
    ];

    // ---- the prompt -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_prompt_has_every_topic_id_the_budget_the_plan_contract_and_the_batch_output_rules()
    {
        var prompt = Prompt(Game());

        foreach (var topic in AiGuideGeneratorService.Topics(Lang.German))
        {
            Assert.Contains($"- {topic.Id}: {topic.Title} – ", prompt);
        }
        Assert.Contains("Schritt 1 – Analyse", prompt);
        Assert.Contains("Schritt 3 – Plane die Kapitel", prompt);
        Assert.Contains("Umfang des Guides: „Ausführlich“ – höchstens 14 Kapitel insgesamt", prompt);
        Assert.Contains("Der Guide ist neu.", prompt);
        Assert.Contains("Das Thema \"walkthrough\" ist besonders", prompt);
        Assert.Contains("Sonderfall Komplettlösung", prompt);                         // a new guide may get a walkthrough
        Assert.Contains("Element 0 ist der PLAN", prompt);
        Assert.Contains("\"plan\": [ { \"chapter\": \"K01\", \"topicId\": \"walkthrough\"", prompt);
        Assert.Contains("Der Plan steht IMMER schon in dieser ersten Antwort", prompt);
        Assert.Contains("OHNE den Plan", prompt);
        Assert.Contains("„NOCH OFFEN: K05–K13“", prompt);
        Assert.Contains("als EINE Datei zum Herunterladen mit dem Namen „platinvault-suncraft-kapitel.json“", prompt);
        Assert.Contains("„Suncraft“, Steam-AppID 4078110", prompt);
        Assert.Contains("Schreibe ALLE Texte auf Deutsch", prompt);
        Assert.Equal(1, prompt.Split("Aufbau (Pflicht, in dieser Reihenfolge):").Length - 1);
        Assert.Equal(1, prompt.Split("Rätsel (verbindliche Regel):").Length - 1);
        // The blueprint's own answer words do not appear: the answer is the plan, not a blueprint
        Assert.DoesNotContain("\"applicable\"", prompt);
        Assert.DoesNotContain("mergeInto", prompt);
        Assert.DoesNotContain("extraChapters", prompt);
        Assert.EndsWith("Recherchiere jetzt das Spiel, plane die Kapitel, schreibe sie alle und liefere das JSON-Array.", prompt);
        Assert.DoesNotContain("\r", prompt);
    }

    [Fact]
    public void The_English_prompt_says_the_same()
    {
        var prompt = Prompt(Game(Lang.English));

        Assert.Contains("Step 3 – Plan the chapters", prompt);
        Assert.Contains("Size of the guide: scope \"Detailed\" – at most 14 chapters in total", prompt);
        Assert.Contains("element 0 is the PLAN", prompt);
        Assert.Contains("The plan is ALWAYS in this first answer already", prompt);
        Assert.Contains("\"STILL OPEN: K05–K13\"", prompt);
        Assert.Contains("platinvault-suncraft-chapters.json", prompt);
        Assert.Contains("Write ALL texts in English", prompt);
        Assert.Contains("- weapons: Best weapons – ", prompt);
        Assert.DoesNotContain("\"applicable\"", prompt);
    }

    [Fact]
    public void The_budget_of_the_chosen_scope_is_in_the_prompt()
    {
        var compact = Prompt(Game(), new ResearchBlocks.Selection("compact", [], null, null));
        var exhaustive = Prompt(Game(), new ResearchBlocks.Selection("exhaustive", [], null, null));

        Assert.Contains("„Kompakt“ – höchstens 8 Kapitel insgesamt", compact);
        Assert.Contains("„Sehr ausführlich“ – höchstens 24 Kapitel insgesamt", exhaustive);
    }

    [Fact]
    public void A_guide_with_chapters_lists_the_real_ones_as_done_and_the_failed_ones_as_to_be_written_again_but_not_lore_or_the_achievement_list()
    {
        var achievements = new GuideTabEntity { Id = Guid.NewGuid(), Slug = "achievements", Title = "Erfolge", SortOrder = 3, SectionsJson = "[]" };
        var lore = new GuideTabEntity { Id = Guid.NewGuid(), Slug = LoreTab.Slug, Title = "Story & Lore", SortOrder = 900, SectionsJson = "[]" };
        var game = Game(Lang.German, AppId, RealTab("Beste Waffen", "weapons"), FailedTab("Gebiete & Karte", "regions"), achievements, lore);

        var prompt = Prompt(game);

        Assert.Contains("Der Guide hat schon diese Kapitel – plane und schreibe sie NICHT noch einmal", prompt);
        Assert.Contains("- \"Beste Waffen\" (topicId: weapons)", prompt);
        Assert.Contains("ihre Recherche ist aber fehlgeschlagen – plane sie wieder", prompt);
        Assert.Contains("- \"Gebiete & Karte\" (topicId: regions)", prompt);
        Assert.Contains("Der Guide hat schon 2 Kapitel: Es ist noch Platz für höchstens 12 weitere.", prompt);   // the lore tab and the achievement list do not count
        Assert.DoesNotContain("\"Story & Lore\"", prompt);
        Assert.DoesNotContain("\"Erfolge\"", prompt);
        Assert.DoesNotContain("Der Guide ist neu.", prompt);
    }

    [Fact]
    public void The_walkthrough_block_is_only_there_when_the_guide_has_no_real_walkthrough_yet()
    {
        var withWalkthrough = Game(Lang.German, AppId, RealTab("Komplettlösung", null, 1, WalkthroughTab.Slug));
        var promptDone = Prompt(withWalkthrough);
        Assert.DoesNotContain("Sonderfall Komplettlösung", promptDone);
        Assert.Contains("- \"Komplettlösung\" (topicId: walkthrough)", promptDone);                       // known by its address
        // The walkthrough does not count against the budget
        Assert.Contains("Der Guide ist neu.", Prompt(Game()));
        Assert.DoesNotContain("Der Guide hat schon 1 Kapitel", promptDone);

        var failed = Game(Lang.German, AppId, new GuideTabEntity
        {
            Id = Guid.NewGuid(), Slug = WalkthroughTab.Slug, Title = "Komplettlösung", SortOrder = 1,
            SectionsJson = JsonSerializer.Serialize(new[] { FailedChapters.CreateSection(Lang.German, "Alles", "walkthrough", "Fehlgeschlagen.", null, null) }, Camel)
        });
        Assert.Contains("Sonderfall Komplettlösung", Prompt(failed));
    }

    [Fact]
    public void The_prompt_of_a_game_without_AppID_names_it_without_one()
    {
        var prompt = Prompt(Game(Lang.German, appId: null));

        Assert.Contains("\"steamAppId\": null", prompt);
        Assert.Contains("Steam-AppID unbekannt", prompt);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void The_prompt_is_not_read_as_an_answer_not_even_with_the_whole_chat_page(string language)
    {
        var game = Game(language, AppId, RealTab("Beste Waffen", "weapons"));
        var prompt = Prompt(game);

        var read = ManualBatchReader.Read(prompt);
        Assert.Empty(read.Objects);
        Assert.Empty(read.Plans.Found);
        Assert.Empty(read.Plans.Errors);
        Assert.True(read.Plans.Patterns > 0);                              // the example plan of the prompt is known as the pattern

        var check = Check(game, prompt);
        Assert.False(check.Ok);
        Assert.Equal("planPattern", Assert.Single(check.Errors).Code);

        var page = prompt + "\n\nClaude:\n```json\n" + Answer(Plan([Entry("K01", "tips", "Tipps & Tricks")]), Chapter("K01", "Tipps & Tricks")) + "\n```";
        var plan = Check(game, page);
        Assert.True(plan.Ok);
        Assert.Equal(["K01"], plan.Chapters.Select(c => c.Key));
    }

    // ---- the plan check -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_good_plan_gives_the_chapters_in_the_shape_of_the_batch_endpoints()
    {
        var game = Game();
        var dto = Check(game, Answer(Plan(StandardPlan()), Chapter("K01", "Komplettlösung")));

        Assert.True(dto.Ok);
        Assert.Empty(dto.Errors);
        Assert.Empty(dto.Warnings);
        Assert.Equal(["K01", "K02", "K03", "K04"], dto.Chapters.Select(c => c.Key));
        Assert.Equal(["walkthrough", "roadmap", "weapons", "extra"], dto.Chapters.Select(c => c.TopicId));
        Assert.Equal("Komplettlösung", dto.Chapters[0].Title);
        Assert.Equal("Das ganze Spiel mit Quellen.", dto.Chapters[0].Focus);
        Assert.Equal("mdi:home", dto.Chapters[3].Icon);
        Assert.All(dto.Chapters, c => Assert.Equal("etabliert", c.SourceSituation));
        Assert.All(dto.Chapters, c => Assert.Null(c.TabId));

        // The list goes back to the batch endpoints as it is: it passes their validation
        Assert.Null(ManualBatchChapters.Normalize(dto.Chapters, forPrompt: true, out var normalized));
        Assert.Equal(dto.Chapters.Select(c => c.Key), normalized.Select(c => c.Key));
    }

    [Fact]
    public void A_missing_focus_or_icon_comes_from_the_standard_topic_and_a_missing_title_too()
    {
        var plan = Plan([
            new JsonObject { ["chapter"] = "K01", ["topicId"] = "weapons" },
            Entry("K02", "extra", "Nur Titel", focus: null, icon: "mdi:...")
        ]);

        var dto = Check(Game(), Answer(plan));

        Assert.True(dto.Ok);
        var weapons = AiGuideGeneratorService.Topics(Lang.German).Single(t => t.Id == "weapons");
        Assert.Equal(weapons.Title, dto.Chapters[0].Title);
        Assert.Equal(weapons.Brief, dto.Chapters[0].Focus);
        Assert.Equal(weapons.Icon, dto.Chapters[0].Icon);
        Assert.Equal("Nur Titel", dto.Chapters[1].Focus);                 // an extra chapter without a focus: the title
        Assert.Equal("mdi:star-four-points", dto.Chapters[1].Icon);       // a placeholder icon is no icon
    }

    [Fact]
    public void An_entry_without_title_and_topic_is_skipped_and_missing_keys_are_assigned_by_position()
    {
        var plan = Plan([new JsonObject { ["focus"] = "x" }, Entry("K01", "tips", "Tipps & Tricks"), new JsonObject { ["topicId"] = "weapons", ["title"] = "Waffen" }]);
        plan["plan"]!.AsArray().Insert(1, JsonValue.Create("kein Objekt"));

        var dto = Check(Game(), Answer(plan));

        Assert.True(dto.Ok);
        Assert.Equal(2, dto.Warnings.Count(w => w.Code == "planEntrySkipped"));
        Assert.Equal(2, dto.Chapters.Count);
        Assert.Equal("K01", dto.Chapters[0].Key);
        Assert.Equal("K02", dto.Chapters[1].Key);                         // position 2 of the kept list; K01 is taken
        Assert.Contains("Eintrag 1 des Plans", dto.Warnings.First(w => w.Code == "planEntrySkipped").Message);
    }

    [Fact]
    public void The_empty_text_and_text_without_JSON_and_without_a_plan_are_told_apart()
    {
        var game = Game();
        Assert.Equal("empty", Assert.Single(Check(game, "  ").Errors).Code);
        Assert.Equal("empty", Assert.Single(Check(game, null).Errors).Code);
        var noJson = Check(game, "Hier ist nichts als Text.");
        Assert.False(noJson.Ok);
        Assert.Equal("noJson", Assert.Single(noJson.Errors).Code);

        // Only chapters (a later "weiter" answer pasted alone, or an answer to the batch prompt): no plan element
        var noPlan = Check(game, Answer(Chapter("K01", "Tipps"), Chapter("K02", "Waffen")));
        Assert.False(noPlan.Ok);
        var error = Assert.Single(noPlan.Errors);
        Assert.Equal("noPlan", error.Code);
        Assert.Contains("keinen Plan", error.Message);
        Assert.Empty(noPlan.Chapters);
    }

    [Fact]
    public void An_empty_plan_is_an_error()
    {
        var dto = Check(Game(), Answer(Plan([])));

        Assert.False(dto.Ok);
        Assert.Equal("emptyPlan", Assert.Single(dto.Errors).Code);
        Assert.Empty(dto.Chapters);
    }

    [Fact]
    public void A_plan_of_another_game_is_an_error_and_a_plan_without_AppID_a_warning()
    {
        var wrong = Check(Game(), Answer(Plan(StandardPlan(), appId: 1)));
        Assert.False(wrong.Ok);
        Assert.Equal("wrongGame", Assert.Single(wrong.Errors).Code);
        Assert.Contains("andere", wrong.Errors[0].Message);
        Assert.Empty(wrong.Chapters);

        var noId = Check(Game(), Answer(Plan(StandardPlan(), appId: null)));
        Assert.True(noId.Ok);
        Assert.Contains(noId.Warnings, w => w.Code == "noAppId");
    }

    [Fact]
    public void Duplicate_keys_are_an_error_that_names_them_also_K7_against_K07()
    {
        var dto = Check(Game(), Answer(Plan([Entry("K02", "tips", "Tipps"), Entry("K1", "roadmap", "Roadmap"), Entry("k01", "weapons", "Waffen"), Entry("K02", "armor", "Rüstung")])));

        Assert.False(dto.Ok);
        var error = Assert.Single(dto.Errors);
        Assert.Equal("duplicateKeys", error.Code);
        Assert.Contains("K02", error.Message);
        Assert.Contains("K1", error.Message);
        Assert.Empty(dto.Chapters);
    }

    [Fact]
    public void A_plan_that_is_only_the_pattern_of_the_prompt_is_an_error_also_in_valid_JSON()
    {
        // Valid JSON with placeholders in the entries
        var pattern = Plan([Entry("K01", "walkthrough", "...", focus: "...", icon: "mdi:...")], appId: AppId);
        var dto = Check(Game(), pattern.ToJsonString());

        Assert.False(dto.Ok);
        Assert.Equal("planPattern", Assert.Single(dto.Errors).Code);
        Assert.Contains("Muster", dto.Errors[0].Message);
    }

    [Fact]
    public void A_cut_off_plan_and_a_broken_plan_are_reported_for_the_plan_not_as_chapters()
    {
        var complete = Plan(StandardPlan()).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var cut = "[\n" + complete[..(complete.Length - 40)];            // the plan never closes
        var game = Game();

        var dto = Check(game, cut);
        Assert.False(dto.Ok);
        var error = Assert.Single(dto.Errors);
        Assert.Equal("planCutOff", error.Code);
        Assert.Contains("Plan ist abgeschnitten", error.Message);

        var read = ManualBatchReader.Read(cut);
        Assert.Empty(read.Objects);                                         // no chapter "K01" with an error comes out of the entries

        var broken = Check(game, complete.Replace("\"focus\"", "focus", StringComparison.Ordinal));
        Assert.False(broken.Ok);
        Assert.Equal("planInvalid", Assert.Single(broken.Errors).Code);
    }

    [Fact]
    public void An_unknown_topic_becomes_extra_with_a_warning_and_several_plans_use_the_first()
    {
        var plan = Plan([Entry("K01", "kochen-und-backen", "Kochen"), Entry("K02", "TIPS", "Tipps & Tricks")]);
        var text = Answer(plan) + "\n" + Plan([Entry("K01", "weapons", "Waffen")]).ToJsonString();

        var dto = Check(Game(), text);

        Assert.True(dto.Ok);
        Assert.Equal(["extra", "tips"], dto.Chapters.Select(c => c.TopicId));      // the id is written like the topic list writes it
        var unknown = Single(dto.Warnings, "unknownTopic");
        Assert.Contains("kochen-und-backen", unknown.Message);
        Assert.Contains("K01", unknown.Message);
        Assert.Contains("Pläne", Single(dto.Warnings, "severalPlans").Message);
    }

    [Fact]
    public void More_chapters_than_the_ceiling_of_the_scope_is_a_warning_and_nothing_is_dropped()
    {
        var entries = Enumerable.Range(1, 10).Select(i => Entry($"K{i:D2}", "extra", $"Eigenes Kapitel {i}")).ToList();

        var compact = Check(Game(), Answer(Plan(entries)), "compact");
        Assert.True(compact.Ok);
        Assert.Equal(10, compact.Chapters.Count);
        var warning = Single(compact.Warnings, "overBudget");
        Assert.Contains("10 Kapiteln", warning.Message);
        Assert.Contains("„Kompakt“", warning.Message);
        Assert.Contains("höchstens 8", warning.Message);

        Assert.DoesNotContain(Check(Game(), Answer(Plan(entries)), "exhaustive").Warnings, w => w.Code == "overBudget");
        // The walkthrough never counts, and the existing chapters do
        var eight = Enumerable.Range(1, 8).Select(i => Entry($"K{i + 1:D2}", "extra", $"Eigenes Kapitel {i}")).Prepend(Entry("K01", "walkthrough", "Komplettlösung")).ToList();
        Assert.DoesNotContain(Check(Game(), Answer(Plan(eight)), "compact").Warnings, w => w.Code == "overBudget");
        Assert.Contains(Check(Game(Lang.German, AppId, RealTab("Vorhanden", "economy")), Answer(Plan(eight)), "compact").Warnings, w => w.Code == "overBudget");
    }

    [Fact]
    public void More_than_the_most_chapters_the_batch_takes_is_an_error()
    {
        var entries = Enumerable.Range(1, ManualBatchChapters.MaxChapters + 1).Select(i => Entry($"K{i:D2}", "extra", $"Kapitel {i}"));

        var dto = Check(Game(), Answer(Plan(entries)));

        Assert.False(dto.Ok);
        Assert.Equal("tooManyChapters", Assert.Single(dto.Errors).Code);
    }

    [Fact]
    public void A_topic_the_guide_already_has_is_dropped_with_a_warning_by_topic_id_or_by_title_and_never_a_failed_one()
    {
        var game = Game(Lang.German, AppId,
            RealTab("Beste Waffen", "weapons", 1),
            RealTab("Brückenbau im Detail", null, 2),                    // a chapter of its own (no stored topic)
            RealTab("Komplettlösung", null, 3, WalkthroughTab.Slug));
        var plan = Plan([
            Entry("K01", "walkthrough", "Komplettlösung"),                // the walkthrough exists once
            Entry("K02", "weapons", "Waffen & Loadouts"),                 // same topic, other title
            Entry("K03", "extra", "Brückenbau im Detail"),                // same title
            Entry("K04", "tips", "Tipps & Tricks")                        // new
        ]);

        var dto = Check(game, Answer(plan));

        Assert.True(dto.Ok);
        Assert.Equal(["K04"], dto.Chapters.Select(c => c.Key));
        Assert.Equal(3, dto.Warnings.Count(w => w.Code == "alreadyCovered"));
        var weapons = dto.Warnings.Single(w => w.Code == "alreadyCovered" && w.Message.Contains("K02"));
        Assert.Contains("Beste Waffen", weapons.Message);
        Assert.Contains("nicht noch einmal gespeichert", weapons.Message);
    }

    [Fact]
    public void A_plan_without_anything_new_is_an_error()
    {
        var game = Game(Lang.German, AppId, RealTab("Beste Waffen", "weapons"));

        var dto = Check(game, Answer(Plan([Entry("K01", "weapons", "Beste Waffen")])));

        Assert.False(dto.Ok);
        Assert.Equal("nothingNew", Assert.Single(dto.Errors).Code);
        Assert.Single(dto.Warnings, w => w.Code == "alreadyCovered");
        Assert.Empty(dto.Chapters);
    }

    [Fact]
    public void An_entry_for_a_failed_chapter_carries_its_tab_id_once_by_topic_or_by_title()
    {
        var regions = FailedTab("Gebiete & Karte", "regions");
        var legacy = FailedTab("Brückenbau im Detail", null, order: 3);          // an own chapter whose topic is unknown
        var game = Game(Lang.German, AppId, RealTab("Beste Waffen", "weapons", 1), regions, legacy);
        var plan = Plan([
            Entry("K01", "regions", "Alle Gebiete"),
            Entry("K02", "extra", "Brückenbau im Detail"),
            Entry("K03", "regions", "Noch ein Gebiete-Kapitel"),                 // the placeholder is taken already: a new chapter
            Entry("K04", "tips", "Tipps & Tricks")
        ]);

        var dto = Check(game, Answer(plan));

        Assert.True(dto.Ok);
        Assert.Equal(regions.Id, dto.Chapters.Single(c => c.Key == "K01").TabId);
        Assert.Equal(legacy.Id, dto.Chapters.Single(c => c.Key == "K02").TabId);
        Assert.Null(dto.Chapters.Single(c => c.Key == "K03").TabId);
        Assert.Null(dto.Chapters.Single(c => c.Key == "K04").TabId);
        Assert.DoesNotContain(dto.Warnings, w => w.Code == "alreadyCovered");
        // A placeholder is replaced without "replace" (the batch endpoints know a failed chapter by itself)
        Assert.All(dto.Chapters, c => Assert.Null(c.Replace));
    }

    [Fact]
    public void English_messages_in_the_request_language()
    {
        using var _ = Lang.Use(Lang.English);
        var game = Game(Lang.English);

        var noPlan = AiGuideGeneratorService.CheckManualOneShotPlan(game, Answer(Chapter("K01", "Tips")), null).Dto;
        Assert.Contains("contains no plan", Assert.Single(noPlan.Errors).Message);
        var empty = AiGuideGeneratorService.CheckManualOneShotPlan(game, Answer(Plan([])), null).Dto;
        Assert.Contains("The plan is empty", Assert.Single(empty.Errors).Message);
        var dupes = AiGuideGeneratorService.CheckManualOneShotPlan(game, Answer(Plan([Entry("K01", "tips", "Tips"), Entry("K01", "roadmap", "Roadmap")])), null).Dto;
        Assert.Contains("occurs several times", Assert.Single(dupes.Errors).Message);
    }

    // ---- the batch reader skips the plan --------------------------------------------------------------------------------------------

    [Fact]
    public void The_batch_check_ignores_the_plan_element_and_matches_the_chapters_by_key()
    {
        var game = Game();
        var plan = Plan(StandardPlan());
        var text = "```json\n" + Answer(plan, Chapter("K01", "Komplettlösung"), Chapter("K02", "Roadmap & Verpassbares"), Chapter("K03", "Beste Waffen")) + "\n```\n\nALLE KAPITEL FERTIG: K01–K04";
        var dto = Check(game, text);
        Assert.True(dto.Ok);

        Assert.Null(ManualBatchChapters.Normalize(dto.Chapters, forPrompt: false, out var chapters));
        var check = Generator().CheckManualBatch(game, text, chapters).Dto;

        Assert.Equal(3, check.Found);                                                    // the plan is no chapter object
        Assert.Empty(check.Unmatched);                                                    // and no unmatched entry, no error
        Assert.Equal(["ok", "ok", "ok", "missing"], check.Items.Select(i => i.State));
        Assert.Equal(["K01", "K02", "K03", "K04"], check.Items.Select(i => i.Key));
    }

    [Fact]
    public void A_later_answer_without_a_plan_is_read_with_the_plan_of_the_first_in_the_same_text()
    {
        var game = Game();
        var first = Answer(Plan(StandardPlan()), Chapter("K01", "Komplettlösung"), Chapter("K02", "Roadmap & Verpassbares")) + "\n\nNOCH OFFEN: K03–K04";
        var second = Answer(Chapter("K03", "Beste Waffen"), Chapter("K04", "Brückenbau im Detail"));
        var text = first + "\n\nweiter\n\n" + second;

        var dto = Check(game, text);
        Assert.True(dto.Ok);
        Assert.Null(ManualBatchChapters.Normalize(dto.Chapters, forPrompt: false, out var chapters));
        var check = Generator().CheckManualBatch(game, text, chapters).Dto;

        Assert.Equal(4, check.Found);
        Assert.Empty(check.Unmatched);
        Assert.All(check.Items, i => Assert.Equal("ok", i.State));
    }

    [Fact]
    public void A_dropped_topics_chapter_object_shows_up_as_unmatched_in_the_batch_check_and_is_never_a_chapter()
    {
        var game = Game(Lang.German, AppId, RealTab("Beste Waffen", "weapons"));
        var plan = Plan([Entry("K01", "tips", "Tipps & Tricks"), Entry("K02", "weapons", "Beste Waffen")]);
        var text = Answer(plan, Chapter("K01", "Tipps & Tricks"), Chapter("K02", "Beste Waffen"));
        var dto = Check(game, text);
        Assert.Equal(["K01"], dto.Chapters.Select(c => c.Key));

        Assert.Null(ManualBatchChapters.Normalize(dto.Chapters, forPrompt: false, out var chapters));
        var check = Generator().CheckManualBatch(game, text, chapters).Dto;

        Assert.Equal("ok", check.Items.Single().State);
        var unmatched = Assert.Single(check.Unmatched);
        Assert.Equal("unmatched", Assert.Single(unmatched.Errors).Code);
    }

    [Fact]
    public void The_plan_element_in_every_form_is_no_chapter_but_a_real_object_with_a_plan_key_inside_a_chapter_is_still_read()
    {
        // Without "game" and "steamAppId" (the old fragment rule would have dropped it as well; now it is a plan)
        var bare = "{ \"plan\": [ { \"chapter\": \"K01\", \"topicId\": \"tips\", \"title\": \"Tipps\" } ] }";
        var read = ManualBatchReader.Read(bare);
        Assert.Empty(read.Objects);
        Assert.Single(read.Plans.Found);

        // Plan entries on their own lines are fragments, never chapters
        var entries = "{ \"chapter\": \"K01\", \"topicId\": \"tips\", \"title\": \"Tipps\" }";
        Assert.Empty(ManualBatchReader.Read(entries).Objects);

        // A chapter that has a "plan" field somewhere inside is a chapter
        var chapter = Chapter("K01", "Tipps");
        chapter["sections"]!.AsArray()[0]!["plan"] = new JsonArray("a");
        var readChapter = ManualBatchReader.Read(chapter.ToJsonString());
        Assert.Single(readChapter.Objects);
        Assert.Empty(readChapter.Plans.Found);

        Assert.True(ManualBatchReader.HasTopLevelPlan("{ \"game\": \"X\", \"plan\": [ { \"a\": 1"));
        Assert.False(ManualBatchReader.HasTopLevelPlan("{ \"game\": \"X\", \"sections\": [ { \"plan\": [ 1"));
        Assert.False(ManualBatchReader.HasTopLevelPlan("{ \"game\": \"plan\": [ "));
    }

    // ---- the endpoints --------------------------------------------------------------------------------------------------------------

    private static async Task SeedAsync(TestServer server, bool withPlaceholder = false)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var game = new GameEntity { Id = Guid.NewGuid(), Slug = "suncraft", Title = "Suncraft", SteamAppId = AppId, ContentLanguage = Lang.German };
        game.Tabs = [new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = "tab-1-waffen", Title = "Beste Waffen", SortOrder = 1, TopicId = "weapons", SectionsJson = "[]" }];
        if (withPlaceholder)
        {
            var placeholder = FailedTab("Gebiete & Karte", "regions");
            placeholder.GameId = game.Id;
            game.Tabs.Add(placeholder);
        }
        db.Games.Add(game);
        await db.SaveChangesAsync();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(TestServer server, string url, object body, string? language = null, bool csrf = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (csrf) request.Headers.Add("X-Requested-With", "PlatinVault");
        if (language is not null) request.Headers.Add("Accept-Language", language);
        var response = await server.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        return (response.StatusCode, doc.RootElement.Clone());
    }

    [Fact]
    public async Task The_plan_endpoint_reads_the_plan_saves_nothing_and_answers_in_the_request_language()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server, withPlaceholder: true);
        var plan = Plan([Entry("K01", "weapons", "Beste Waffen"), Entry("K02", "regions", "Alle Gebiete"), Entry("K03", "tips", "Tipps & Tricks")]);
        var text = Answer(plan, Chapter("K02", "Alle Gebiete"), Chapter("K03", "Tipps & Tricks"));

        var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/oneshot-plan", new { text }, "de");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.Equal(["K02", "K03"], body.GetProperty("chapters").EnumerateArray().Select(c => c.GetProperty("key").GetString()));
        Assert.Equal(JsonValueKind.String, body.GetProperty("chapters")[0].GetProperty("tabId").ValueKind);   // the failed chapter
        Assert.Equal("regions", body.GetProperty("chapters")[0].GetProperty("topicId").GetString());
        Assert.Equal(1, body.GetProperty("warnings").GetArrayLength());                                      // "Beste Waffen" exists already
        Assert.Contains("gibt es schon", body.GetProperty("warnings")[0].GetProperty("message").GetString());

        var english = await PostAsync(server, "/api/games/suncraft/manual/oneshot-plan", new { text = "{ \"x\": 1 }" }, "en");
        Assert.False(english.Body.GetProperty("ok").GetBoolean());
        Assert.Contains("no plan", english.Body.GetProperty("errors")[0].GetProperty("message").GetString());

        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        Assert.Equal(2, db.GuideTabs.Count());                                                               // nothing was saved
    }

    [Fact]
    public async Task The_endpoints_refuse_unknown_games_a_missing_csrf_header_and_too_much_text()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server);

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(server, "/api/games/nope/manual/oneshot-plan", new { text = "x" })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(server, "/api/games/nope/manual/oneshot-prompt", new { })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(server, "/api/games/suncraft/manual/oneshot-plan", new { text = "x" }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(server, "/api/games/suncraft/manual/oneshot-prompt", new { }, csrf: false)).Status);

        var big = new string('a', ManualBatchReader.MaxKilobytes * 1000 + 1);
        var tooLarge = await PostAsync(server, "/api/games/suncraft/manual/oneshot-plan", new { text = big }, "de");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.Status);
        Assert.Contains("zu groß", tooLarge.Body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task The_saved_chapters_of_a_one_step_answer_go_through_batch_answer_with_the_plans_list_and_replace_the_failed_chapter()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server, withPlaceholder: true);
        var plan = Plan([Entry("K01", "regions", "Gebiete & Karte"), Entry("K02", "tips", "Tipps & Tricks")]);
        var text = Answer(plan, Chapter("K01", "Gebiete & Karte"), Chapter("K02", "Tipps & Tricks"));

        var planned = await PostAsync(server, "/api/games/suncraft/manual/oneshot-plan", new { text });
        Assert.True(planned.Body.GetProperty("ok").GetBoolean());
        var chapters = planned.Body.GetProperty("chapters");

        var saved = await PostAsync(server, "/api/games/suncraft/manual/batch-answer", new { text, chapters });

        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal(["K01", "K02"], saved.Body.GetProperty("saved").EnumerateArray().Select(s => s.GetProperty("key").GetString()));
        Assert.True(saved.Body.GetProperty("saved")[0].GetProperty("replaced").GetBoolean());                // the placeholder
        Assert.False(saved.Body.GetProperty("saved")[1].GetProperty("replaced").GetBoolean());
        Assert.Equal(0, saved.Body.GetProperty("skipped").GetArrayLength());
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var tabs = db.GuideTabs.OrderBy(t => t.SortOrder).ToList();
        Assert.Equal(3, tabs.Count);
        Assert.All(tabs.Where(t => t.Slug != "tab-1-waffen"), t => Assert.Null(FailedChapters.Read(t.SectionsJson)));
    }
}
