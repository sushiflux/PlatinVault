using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Companion.Api.Tests;

/// <summary>
/// Batch mode of the manual (own AI subscription) flow: one prompt for many chapters, one field for all collected answers - for every game and every
/// entry point (new-guide plan, "complete guide", failed chapters with their tab ids, mixed). Nothing here uses an AI provider.
/// </summary>
public sealed class ManualBatchTests
{
    private const int AppId = 4078110;

    private static GameEntity Game(string title = "Suncraft", int? appId = AppId, string language = Lang.German) =>
        new() { Id = Guid.NewGuid(), Slug = "suncraft", Title = title, SteamAppId = appId, ContentLanguage = language, Tabs = [] };

    private static AiGuideGeneratorService.GameContext Context(string language = Lang.German, ResearchBlocks.Selection? blocks = null) =>
        new("Suncraft", language == Lang.English ? "Steam AppID: 4078110\nGenres according to Steam: Simulation" : "Steam-AppID: 4078110\nGenres laut Steam: Simulation", [], [], blocks ?? ResearchBlocks.Selection.Empty,
            ResearchBlocks.FillBase(language, "Suncraft", "Simulation", ResearchBlocks.PlatformText), language);

    private static AiGuideGeneratorService Generator() =>
        new(null!, null!, null!, null!, null!, null!, Microsoft.Extensions.Options.Options.Create(new Companion.Api.Configuration.CompanionOptions()), null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<AiGuideGeneratorService>.Instance);

    /// <summary>A plan of n chapters (K01 ...); topic ids make 1 = walkthrough (when asked), 2 = roadmap (never dropped), the rest are extras.</summary>
    private static List<ManualBatchChapter> Items(int count, bool walkthrough = false, bool withKeys = false)
    {
        var items = new List<ManualBatchChapter>();
        for (var i = 1; i <= count; i++)
        {
            var topic = i == 1 && walkthrough ? "walkthrough" : i == 2 ? "roadmap" : "extra";
            items.Add(new ManualBatchChapter(withKeys ? $"K{i:D2}" : null, $"Kapitel Nummer {i}", $"Recherchiere alles zu Thema {i}: Orte, Werte, Reihenfolgen und was verpassbar ist.", topic, "etabliert", null, null));
        }
        return items;
    }

    private static List<BatchChapter> Plan(int count, bool walkthrough = false, List<ManualBatchChapter>? items = null)
    {
        Assert.Null(ManualBatchChapters.Normalize(items ?? Items(count, walkthrough), forPrompt: true, out var chapters));
        return chapters;
    }

    private static string Obj(string? key, string title, string itemId = "bruecke", string text = "Die Brücke ist der erste Ausbauschritt und verbindet das Dorf mit dem Markt, wo man alles für den Anfang findet.", int? appId = AppId) =>
        Node(key, title, itemId, text, appId).ToJsonString();

    private static JsonObject Node(string? key, string title, string itemId = "bruecke", string text = "Die Brücke ist der erste Ausbauschritt und verbindet das Dorf mit dem Markt, wo man alles für den Anfang findet.", int? appId = AppId)
    {
        var node = new JsonObject { ["game"] = "Suncraft", ["steamAppId"] = appId };
        if (key is not null) node["chapter"] = key;
        node["title"] = title;
        node["icon"] = "mdi:home";
        node["sections"] = new JsonArray(
            new JsonObject { ["type"] = "alert", ["variant"] = "gold", ["title"] = "Das Wichtigste vorab", ["htmlContent"] = text },
            new JsonObject
            {
                ["type"] = "checklist_table",
                ["table"] = new JsonObject
                {
                    ["tableId"] = "bau", ["caption"] = "Bauten", ["categories"] = new JsonArray("Dorf"),
                    ["columns"] = new JsonArray(new JsonObject { ["key"] = "name", ["label"] = "Name", ["isRowHeader"] = true }),
                    ["rows"] = new JsonArray(new JsonObject { ["itemId"] = itemId, ["filterCategory"] = "Dorf", ["cells"] = new JsonObject { ["name"] = "Brücke" } })
                }
            });
        return node;
    }

    private static string NotApplicable(string key) =>
        new JsonObject { ["game"] = "Suncraft", ["steamAppId"] = AppId, ["chapter"] = key, ["notApplicable"] = true, ["reason"] = "Suncraft hat keinen Kampf." }.ToJsonString();

    private static string Fenced(params string[] objects) => string.Join("\n\nGleich geht es weiter.\n\n", objects.Select(o => "```json\n" + o + "\n```"));

    private static ManualBatchItemDto Item(ManualBatchCheckDto dto, string key) => dto.Items.Single(i => i.Key == key);

    // ---- the chapter list ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Missing_invalid_and_duplicate_keys_are_assigned_in_list_order_and_valid_ones_are_kept()
    {
        var items = new List<ManualBatchChapter>
        {
            new("K02", "A", "f", null, null, null, null),   // kept
            new(null, "B", "f", null, null, null, null),    // position 2 -> K02 is taken -> K03
            new("k2", "C", "f", null, null, null, null),    // same as K02 ("k2" == "K02") -> assigned: K03 taken -> K04
            new("bad key!", "D", "f", null, null, null, null),
            new("Intro", "E", "f", null, null, null, null), // custom keys are fine
        };

        Assert.Null(ManualBatchChapters.Normalize(items, forPrompt: true, out var chapters));

        Assert.Equal(["K02", "K03", "K04", "K05", "Intro"], chapters.Select(c => c.Key));
        Assert.Equal(chapters.Count, chapters.Select(c => c.KeyForm).Distinct().Count());
    }

    [Fact]
    public void The_list_is_validated()
    {
        Assert.NotNull(ManualBatchChapters.Normalize(null, true, out _));
        Assert.NotNull(ManualBatchChapters.Normalize([], true, out _));
        Assert.NotNull(ManualBatchChapters.Normalize(Items(41), true, out _));
        Assert.Null(ManualBatchChapters.Normalize(Items(40), true, out _));
        Assert.NotNull(ManualBatchChapters.Normalize([new(null, "  ", "f", null, null, null, null)], true, out _));
        Assert.NotNull(ManualBatchChapters.Normalize([new(null, "T", null, null, null, null, null)], forPrompt: true, out _));          // the prompt needs the assignment
        Assert.Null(ManualBatchChapters.Normalize([new(null, "T", null, null, null, null, null)], forPrompt: false, out _));            // check and save do not
        Assert.NotNull(ManualBatchChapters.Normalize([new(null, new string('x', 241), "f", null, null, null, null)], true, out _));
        Assert.NotNull(ManualBatchChapters.Normalize([new(null, "T", new string('x', 4001), null, null, null, null)], true, out _));
    }

    // ---- the prompt ---------------------------------------------------------------------------------------------------------------

    private static int Count(string text, string part) => text.Split(part).Length - 1;

    [Fact]
    public void The_batch_prompt_states_the_shared_part_once_and_lists_every_chapter_with_its_key()
    {
        var chapters = Plan(31, walkthrough: true);
        var prompt = AiGuideGeneratorService.ComposeManualBatchPrompt(Context(), Game(), Lang.German, chapters);

        Assert.Equal(1, Count(prompt, "Aufbau (Pflicht, in dieser Reihenfolge):"));
        Assert.Equal(1, Count(prompt, "Rätsel (verbindliche Regel):"));
        Assert.Equal(1, Count(prompt, "Recherche-Regeln:"));
        Assert.Equal(1, Count(prompt, "Steam-AppID: 4078110\nGenres laut Steam"));
        Assert.Contains("Du schreibst 31 Kapitel", prompt);
        Assert.Contains("Schreibe ALLE Texte auf Deutsch", prompt);
        foreach (var chapter in chapters)
        {
            Assert.Contains($"- {chapter.Key} | \"{chapter.Title}\"", prompt);
            Assert.Contains($"Rechercheauftrag: {chapter.Focus}", prompt);
        }
        Assert.Contains("„Suncraft“, Steam-AppID 4078110", prompt);
        Assert.Contains("\"steamAppId\": 4078110", prompt);
        Assert.Contains("```json", prompt);
        Assert.Contains("„weiter“", prompt);
        Assert.Contains("„ALLE KAPITEL FERTIG: K01–K31“", prompt);
        Assert.Contains("platinvault-suncraft-kapitel.json", prompt);
        Assert.Contains("Quellenlage laut Vorab-Analyse: etabliert", prompt);   // one shared value, stated once
        Assert.Equal(1, Count(prompt, "Quellenlage laut Vorab-Analyse"));
        Assert.DoesNotContain("\r", prompt);
    }

    [Fact]
    public void The_batch_prompt_is_much_smaller_than_the_single_prompts_it_replaces()
    {
        var chapters = Plan(31);
        var batch = AiGuideGeneratorService.ComposeManualBatchPrompt(Context(), Game(), Lang.German, chapters);
        var singles = chapters.Sum(c => AiGuideGeneratorService.ComposeManualChapterPrompt(Context(), Game(), Lang.German, c.Title, c.Focus, "etabliert", c.TopicId).Length);

        Assert.True(batch.Length < 30_000, $"batch prompt has {batch.Length} characters");
        Assert.True(batch.Length * 5 < singles, $"batch {batch.Length} vs. {singles} for 31 single prompts");
    }

    [Fact]
    public void The_walkthrough_gets_its_own_block_and_its_own_answer_only_when_it_is_in_the_list()
    {
        var with = AiGuideGeneratorService.ComposeManualBatchPrompt(Context(), Game(), Lang.German, Plan(5, walkthrough: true));
        Assert.Contains("Sonderfall Komplettlösung (K01)", with);
        Assert.Contains("ALLEIN in einer eigenen Antwort", with);
        Assert.Contains("Prüfe zuerst, ob es eine belastbare, VOLLSTÄNDIGE Quelle", with);   // the single-answer instructions
        Assert.Contains("Erlaubte Bausteine (nur diese Typen)", with);
        Assert.Contains("eigene Antwort (Sonderfall Komplettlösung)", with);

        var without = AiGuideGeneratorService.ComposeManualBatchPrompt(Context(), Game(), Lang.German, Plan(5));
        Assert.DoesNotContain("Sonderfall Komplettlösung", without);
        Assert.DoesNotContain("Erlaubte Bausteine (nur diese Typen)", without);
    }

    [Fact]
    public void Chapters_that_may_be_dropped_are_marked_and_roadmap_tips_and_walkthrough_never()
    {
        var items = new List<ManualBatchChapter>
        {
            new(null, "Komplettlösung", "f", "walkthrough", null, null, null),
            new(null, "Roadmap", "f", "roadmap", null, null, null),
            new(null, "Tipps", "f", "tips", null, null, null),
            new(null, "Gegner", "f", "bestiary", null, null, null),
            new(null, "Eigenes", "f", null, null, null, null),
        };
        var prompt = AiGuideGeneratorService.ComposeManualBatchPrompt(Context(), Game(), Lang.German, Plan(0, items: items));

        Assert.Contains("- K01 | \"Komplettlösung\" | notApplicable NICHT erlaubt", prompt);
        Assert.Contains("- K02 | \"Roadmap\" | notApplicable NICHT erlaubt", prompt);
        Assert.Contains("- K03 | \"Tipps\" | notApplicable NICHT erlaubt", prompt);
        Assert.Contains("- K04 | \"Gegner\" | notApplicable erlaubt", prompt);
        Assert.Contains("- K05 | \"Eigenes\" | notApplicable erlaubt", prompt);
        Assert.Contains("\"notApplicable\": true", prompt);

        // Only chapters that can never be dropped: the form is not even offered
        var never = AiGuideGeneratorService.ComposeManualBatchPrompt(Context(), Game(), Lang.German, Plan(0, items: items.Take(3).ToList()));
        Assert.DoesNotContain("\"notApplicable\": true", never);
    }

    [Fact]
    public void The_English_prompt_and_a_game_without_AppID_work()
    {
        var game = Game(language: Lang.English, appId: null);
        var prompt = AiGuideGeneratorService.ComposeManualBatchPrompt(Context(Lang.English), game, Lang.English, Plan(3));

        Assert.Contains("You are writing 3 chapters", prompt);
        Assert.Contains("Write ALL texts in English", prompt);
        Assert.Contains("Structure (mandatory, in this order):", prompt);
        Assert.Contains("\"ALL CHAPTERS DONE: K01–K03\"", prompt);
        Assert.Contains("\"continue\"", prompt);
        Assert.Contains("platinvault-suncraft-chapters.json", prompt);
        Assert.Contains("Steam AppID unknown", prompt);
        Assert.Contains("\"steamAppId\": null", prompt);
        Assert.DoesNotContain("Aufbau (Pflicht", prompt);
    }

    [Fact]
    public void Research_blocks_and_different_source_situations_are_respected()
    {
        var blocks = new ResearchBlocks.Selection("compact", ["missables"], "none", "tables");
        var items = new List<ManualBatchChapter>
        {
            new(null, "A", "fa", null, "etabliert", null, null),
            new(null, "B", "fb", null, "dünn", null, null),
        };
        var prompt = AiGuideGeneratorService.ComposeManualBatchPrompt(Context(blocks: blocks), Game(), Lang.German, Plan(0, items: items));

        Assert.Contains("Umfang – Kompakt", prompt);
        Assert.Contains("Verpassbares zuerst", prompt);
        Assert.Equal(0, Count(prompt, "Quellenlage laut Vorab-Analyse"));
        Assert.Contains("Quellenlage: etabliert", prompt);
        Assert.Contains("Quellenlage: dünn", prompt);
    }

    [Fact]
    public void The_single_chapter_prompt_is_byte_identical_to_before_the_split_into_shared_parts()
    {
        // The golden file was rendered from the code before the refactoring; this test is its twin in the batch suite so a failing run points here
        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "prompts.txt"));
        var chapter = AiGuideGeneratorService.BuildChapterPrompt(Context(), "Dorfausbau", "Alle Ausbaustufen des Dorfes", "dünn", "weapons").Replace("\r\n", "\n");
        Assert.Contains(chapter, golden.Replace("\r\n", "\n"));
    }

    // ---- reading a text with many chapters --------------------------------------------------------------------------------------

    [Fact]
    public void Several_code_blocks_between_sentences_are_all_found()
    {
        var read = ManualBatchReader.Read("Hier die ersten:\n" + Fenced(Obj("K01", "Kapitel Nummer 1"), Obj("K02", "Kapitel Nummer 2")) + "\n\nSage \"weiter\" {wenn du mehr willst}.");

        Assert.False(read.TooMany);
        Assert.Equal(["K01", "K02"], read.Objects.Select(o => o.KeyHint));
        Assert.All(read.Objects, o => Assert.NotNull(o.Node));
    }

    [Fact]
    public void Appended_pastes_bare_objects_and_CRLF_are_read()
    {
        var text = ("Antwort 1\n" + Fenced(Obj("K01", "Kapitel Nummer 1")) + "\n\nAntwort 2:\n" + Obj("K02", "Kapitel Nummer 2") + "\nund\n\n" + Fenced(Obj("K03", "Kapitel Nummer 3"))).Replace("\n", "\r\n");

        var read = ManualBatchReader.Read(text);

        Assert.Equal(["K01", "K02", "K03"], read.Objects.Select(o => o.KeyHint));
    }

    [Fact]
    public void A_JSON_array_and_a_chapters_wrapper_are_read_as_their_chapters()
    {
        var array = "[\n" + string.Join(",\n", new[] { Obj("K01", "Kapitel Nummer 1"), Obj("K02", "Kapitel Nummer 2") }) + "\n]";
        Assert.Equal(["K01", "K02"], ManualBatchReader.Read(array).Objects.Select(o => o.KeyHint));

        var wrapper = $"{{ \"chapters\": [ {Obj("K05", "Kapitel Nummer 5")}, {Obj("K06", "Kapitel Nummer 6")} ] }}";
        Assert.Equal(["K05", "K06"], ManualBatchReader.Read(wrapper).Objects.Select(o => o.KeyHint));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void Copying_the_whole_chat_page_including_the_prompt_creates_neither_chapters_nor_errors(string language)
    {
        var chapters = Plan(6, walkthrough: true);
        var prompt = AiGuideGeneratorService.ComposeManualBatchPrompt(Context(language), Game(language: language), language, chapters);
        var page = "Du hast gesagt:\n" + prompt + "\n\nClaude hat gesagt:\nGern!\n\n" + Fenced(Obj("K03", "Kapitel Nummer 3")) + "\n\nSchreibe \"weiter\".\n\nDu hast gesagt:\nweiter\n\n" + Fenced(Obj("K04", "Kapitel Nummer 4")) + "\n\nALLE KAPITEL FERTIG: K01–K06";

        var promptOnly = ManualBatchReader.Read(prompt);
        Assert.Empty(promptOnly.Objects);

        var read = ManualBatchReader.Read(page);
        Assert.Equal(["K03", "K04"], read.Objects.Select(o => o.KeyHint));

        var check = Generator().CheckManualBatch(Game(language: language), page, chapters).Dto;
        Assert.Equal(2, check.Found);
        Assert.Empty(check.Unmatched);
    }

    [Fact]
    public void The_pattern_of_the_blueprint_and_chapter_prompts_is_ignored_too()
    {
        var blueprint = AiGuideGeneratorService.ComposeManualBlueprintPrompt(Context(), Game(), Lang.German);
        var chapter = AiGuideGeneratorService.ComposeManualChapterPrompt(Context(), Game(), Lang.German, "Dorfausbau", "Alles", "dünn", "weapons");
        var walkthrough = AiGuideGeneratorService.ComposeManualChapterPrompt(Context(Lang.English), Game(language: Lang.English), Lang.English, "Walkthrough", "Alles", "thin", "walkthrough");

        foreach (var text in new[] { blueprint, chapter, walkthrough })
        {
            Assert.Empty(ManualBatchReader.Read(text).Objects);
        }
    }

    [Fact]
    public void A_cut_off_last_object_is_reported_for_its_key()
    {
        var cut = Obj("K02", "Kapitel Nummer 2");
        var text = Fenced(Obj("K01", "Kapitel Nummer 1")) + "\n\n```json\n" + cut[..(cut.Length - 80)];

        var read = ManualBatchReader.Read(text);

        Assert.Equal(2, read.Objects.Count);
        Assert.Equal(BatchFailure.CutOff, read.Objects[1].Failure);
        Assert.Equal("K02", read.Objects[1].KeyHint);

        var check = Generator().CheckManualBatch(Game(), text, Plan(3)).Dto;
        Assert.Equal("ok", Item(check, "K01").State);
        var broken = Item(check, "K02");
        Assert.Equal("error", broken.State);
        Assert.Contains("abgeschnitten", Assert.Single(broken.Errors).Message);
        Assert.Equal("missing", Item(check, "K03").State);
    }

    [Fact]
    public void A_cut_off_object_in_the_middle_does_not_swallow_the_complete_ones_after_it()
    {
        // Typical for several pastes: the first answer was cut inside a text, the next paste starts a new code block
        var cut = Obj("K02", "Kapitel Nummer 2");
        var text = Fenced(Obj("K01", "Kapitel Nummer 1")) + "\n\n```json\n" + cut[..(cut.IndexOf("Das Wichtigste", StringComparison.Ordinal) + 30)] + "\n```\n\nweiter\n\n" + Fenced(Obj("K03", "Kapitel Nummer 3"), Obj("K04", "Kapitel Nummer 4"));

        var check = Generator().CheckManualBatch(Game(), text, Plan(4)).Dto;

        Assert.Equal(["ok", "error", "ok", "ok"], check.Items.Select(i => i.State));
        Assert.Empty(check.Unmatched);
    }

    [Fact]
    public void Too_many_objects_and_too_much_text_are_refused()
    {
        var many = string.Join("\n", Enumerable.Range(0, ManualBatchReader.MaxObjects + 5).Select(i => Obj($"K{i}", "T" + i)));
        Assert.True(ManualBatchReader.Read(many).TooMany);
    }

    // ---- matching and judging ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_chapter_is_found_by_key_case_insensitive_and_K7_equals_K07_else_by_cleaned_title()
    {
        var chapters = Plan(10);
        var text = Fenced(
            Obj("k7", "ein anderer Titel"),                                // key wins over the title
            Obj(null, "Kapitel Nummer 3"),                                // no key: exact title
            Obj("K99", "kapitel nummer 4 (Chapter Four)"),                // unknown key: cleaned title
            Obj("K88", "Etwas ganz anderes"),                             // unmatched
            Obj(null, "Noch etwas anderes"));                             // unmatched

        var check = Generator().CheckManualBatch(Game(), text, chapters).Dto;

        Assert.Equal(5, check.Found);
        Assert.Equal("ok", Item(check, "K07").State);
        Assert.Equal("ok", Item(check, "K03").State);
        Assert.Equal("ok", Item(check, "K04").State);
        Assert.Equal(["missing"], check.Items.Where(i => i.Key is "K01").Select(i => i.State));
        Assert.Equal(2, check.Unmatched.Count);
        Assert.Equal([4, 5], check.Unmatched.Select(u => u.Index));
        Assert.Equal("Etwas ganz anderes", check.Unmatched[0].Title);
        Assert.Contains(check.Unmatched[0].Errors, e => e.Code == "unmatched" && e.Message.Contains("K88"));
    }

    [Fact]
    public void A_duplicate_key_takes_the_last_complete_object()
    {
        var first = Obj("K01", "Kapitel Nummer 1", text: "Erste Fassung, die später ersetzt wird, weil die KI sie noch einmal geschickt hat und besser gemacht hat.");
        var second = Obj("K01", "Kapitel Nummer 1", text: "Zweite Fassung, die gewinnt, weil sie später im eingefügten Text steht und vollständig ist.");
        var cutThird = Obj("K01", "Kapitel Nummer 1");

        var check = Generator().CheckManualBatch(Game(), Fenced(first, second), Plan(1)).Dto;
        var item = Item(check, "K01");
        Assert.Equal("warning", item.State);
        Assert.Contains(item.Warnings, w => w.Code == "duplicateInText");
        Assert.Equal(2, check.Found);

        // A later copy that is cut off does not replace the complete one
        var withBroken = Generator().CheckManualBatch(Game(), Fenced(second) + "\n\n```json\n" + cutThird[..^40], Plan(1)).Dto;
        Assert.Equal("warning", Item(withBroken, "K01").State);
        Assert.Contains(Item(withBroken, "K01").Warnings, w => w.Code == "laterCopyBroken");
        Assert.NotNull(Item(withBroken, "K01").Preview);
    }

    [Fact]
    public void The_states_ok_warning_error_notApplicable_and_missing_are_told_apart()
    {
        var chapters = Plan(6);
        var text = Fenced(
            Obj("K01", "Kapitel Nummer 1"),                                   // ok
            Obj("K02", "Kapitel Nummer 2", appId: null),                      // warning: no AppID echo
            Obj("K03", "Kapitel Nummer 3", appId: 1091500),                   // error: other game
            NotApplicable("K04"),                                             // notApplicable
            """{ "game": "Suncraft", "steamAppId": 4078110, "chapter": "K05", "title": "Kapitel Nummer 5" }""");   // error: no sections

        var check = Generator().CheckManualBatch(Game(), text, chapters).Dto;

        Assert.Equal(["ok", "warning", "error", "notApplicable", "error", "missing"], check.Items.Select(i => i.State));
        Assert.Contains(Item(check, "K02").Warnings, w => w.Code == "noAppId");
        Assert.Contains(Item(check, "K03").Errors, e => e.Code == "wrongGame");
        Assert.Contains("Suncraft hat keinen Kampf", Item(check, "K04").NotApplicable);
        Assert.Null(Item(check, "K04").Preview);
        Assert.Contains(Item(check, "K05").Errors, e => e.Code == "noSections");
        Assert.NotNull(Item(check, "K01").Preview);
        Assert.Equal(2, Item(check, "K01").Preview!.TableRows + 1);
    }

    [Fact]
    public void A_not_applicable_answer_for_a_chapter_every_guide_has_is_an_error()
    {
        var chapters = Plan(2);   // K02 is the roadmap
        var check = Generator().CheckManualBatch(Game(), Fenced(NotApplicable("K02")), chapters).Dto;

        Assert.Equal("error", Item(check, "K02").State);
        Assert.Contains(Item(check, "K02").Errors, e => e.Code == "notApplicableNotAllowed");
    }

    [Fact]
    public void A_whole_guide_file_and_a_blueprint_are_unmatched_with_their_own_error()
    {
        var text = Fenced("""{"format":"platinvault-guide","formatVersion":1,"tabs":[]}""", """{"game":"Suncraft","topics":[{"id":"weapons","applicable":true}]}""");

        var check = Generator().CheckManualBatch(Game(), text, Plan(2)).Dto;

        Assert.Equal(2, check.Unmatched.Count);
        Assert.Contains(check.Unmatched[0].Errors, e => e.Code == "wholeGuide");
        Assert.Contains(check.Unmatched[1].Errors, e => e.Code == "blueprintForChapter");
    }

    [Fact]
    public void A_game_without_AppID_is_checked_by_title_only()
    {
        var game = Game(appId: null);
        var text = Fenced(new JsonObject { ["game"] = "Anderes Spiel", ["chapter"] = "K01", ["title"] = "Kapitel Nummer 1", ["sections"] = Node("K01", "x")["sections"]!.DeepClone() }.ToJsonString());

        var check = Generator().CheckManualBatch(game, text, Plan(1)).Dto;

        Assert.Equal("warning", Item(check, "K01").State);
        Assert.Contains(Item(check, "K01").Warnings, w => w.Code == "otherName");
    }

    // ---- the endpoints --------------------------------------------------------------------------------------------------------

    private sealed record Seed(string Slug, Guid PlaceholderA, Guid PlaceholderB, Guid RealTab);

    /// <summary>A game with one real chapter (item id "bruecke"), two failed chapters and the lore tab.</summary>
    private static async Task<Seed> SeedAsync(TestServer server, string slug = "suncraft", int? appId = AppId, string language = Lang.German)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
        var game = new GameEntity { Id = Guid.NewGuid(), Slug = slug, Title = "Suncraft", SteamAppId = appId, ContentLanguage = language };
        string Placeholder() => JsonSerializer.Serialize(new[] { FailedChapters.CreateSection(language, "Auftrag", "extra", "Die Recherche ist fehlgeschlagen.", null, null) }, json);
        var real = new GuideTabEntity
        {
            Id = Guid.NewGuid(), GameId = game.Id, Slug = "tab-1-bestehend", Title = "Bestehend", SortOrder = 1,
            SectionsJson = Node("K00", "Bestehend")["sections"]!.ToJsonString()
        };
        var a = new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = "tab-2-fehlt-a", Title = "Fehlt A", SortOrder = 2, SectionsJson = Placeholder() };
        var b = new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = "tab-3-fehlt-b", Title = "Fehlt B", SortOrder = 3, SectionsJson = Placeholder() };
        var lore = new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = LoreTab.Slug, Title = LoreTab.Title, SortOrder = LoreTab.SortOrder, SectionsJson = "[]" };
        game.Tabs = [real, a, b, lore];
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return new Seed(slug, a.Id, b.Id, real.Id);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(TestServer server, string url, object body, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Requested-With", "PlatinVault");
        if (language is not null) request.Headers.Add("Accept-Language", language);
        var response = await server.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        return (response.StatusCode, doc.RootElement.Clone());
    }

    private static async Task<List<GuideTabEntity>> TabsAsync(TestServer server, string slug = "suncraft")
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        return await db.GuideTabs.AsNoTracking().Where(t => t.Game!.Slug == slug).OrderBy(t => t.SortOrder).ToListAsync();
    }

    private static IEnumerable<string> ItemIds(GuideTabEntity tab) =>
        JsonNode.Parse(tab.SectionsJson)!.AsArray().Select(s => s?["table"]?["rows"]).OfType<JsonArray>().SelectMany(r => r).Select(r => r!["itemId"]!.GetValue<string>());

    [Fact]
    public async Task The_endpoints_refuse_bad_lists_unknown_games_and_a_missing_csrf_header()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server);

        var empty = await PostAsync(server, "/api/games/suncraft/manual/batch-prompt", new { chapters = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.Status);
        var tooMany = await PostAsync(server, "/api/games/suncraft/manual/batch-check", new { text = "x", chapters = Items(41) });
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.Status);
        var noGame = await PostAsync(server, "/api/games/nope/manual/batch-check", new { text = "x", chapters = Items(1) });
        Assert.Equal(HttpStatusCode.NotFound, noGame.Status);

        var noHeader = await server.Client.PostAsJsonAsync("/api/games/suncraft/manual/batch-check", new { text = "x", chapters = Items(1) });
        Assert.Equal(HttpStatusCode.BadRequest, noHeader.StatusCode);
    }

    [Fact]
    public async Task Too_much_text_is_refused_with_a_complete_bilingual_message()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server);
        var text = new string('a', ManualBatchReader.MaxKilobytes * 1000 + 1);

        var german = await PostAsync(server, "/api/games/suncraft/manual/batch-check", new { text, chapters = Items(1) }, "de");
        var english = await PostAsync(server, "/api/games/suncraft/manual/batch-check", new { text, chapters = Items(1) }, "en");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, german.Status);
        Assert.Contains("zu groß", german.Body.GetProperty("detail").GetString());
        Assert.Contains("too large", english.Body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Check_judges_the_text_and_saves_nothing()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server);
        var before = (await TabsAsync(server)).Count;

        var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/batch-check", new { text = Fenced(Obj("K01", "Kapitel Nummer 1"), Obj("K03", "Kapitel Nummer 3", appId: 1)), chapters = Items(3, withKeys: true) });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, body.GetProperty("found").GetInt32());
        var states = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("state").GetString()).ToList();
        Assert.Equal(["ok", "missing", "error"], states);
        Assert.Equal("K01", body.GetProperty("items")[0].GetProperty("key").GetString());
        Assert.Equal(JsonValueKind.Object, body.GetProperty("items")[0].GetProperty("preview").ValueKind);
        Assert.Equal(before, (await TabsAsync(server)).Count);
    }

    [Fact]
    public async Task Answer_saves_all_valid_chapters_in_list_order_with_unique_item_ids_and_never_one_with_errors()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server);
        var text = Fenced(
            Obj("K02", "Kapitel Nummer 2"),                       // same item id "bruecke" as the existing chapter and as the others
            Obj("K01", "Kapitel Nummer 1"),
            Obj("K03", "Kapitel Nummer 3", appId: 1),             // other game: never saved
            Obj("K04", "Kapitel Nummer 4"));

        var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/batch-answer", new { text, chapters = Items(5, withKeys: true) });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["K01", "K02", "K04"], body.GetProperty("saved").EnumerateArray().Select(s => s.GetProperty("key").GetString()));
        Assert.All(body.GetProperty("saved").EnumerateArray(), s => Assert.False(s.GetProperty("replaced").GetBoolean()));
        var skipped = body.GetProperty("skipped").EnumerateArray().ToDictionary(s => s.GetProperty("key").GetString()!, s => s.GetProperty("reason").GetString()!);
        Assert.Equal(["K03", "K05"], skipped.Keys.Order());
        Assert.Contains("andere", skipped["K03"]);                // the German "wrong game" sentence is part of the reason
        Assert.Contains("keine Antwort", skipped["K05"]);

        var tabs = await TabsAsync(server);
        var added = tabs.Where(t => t.Title.StartsWith("Kapitel Nummer", StringComparison.Ordinal)).ToList();
        Assert.Equal(["Kapitel Nummer 1", "Kapitel Nummer 2", "Kapitel Nummer 4"], added.Select(t => t.Title));
        Assert.Equal(added.Select(t => t.SortOrder).Order(), added.Select(t => t.SortOrder));
        Assert.True(added.All(t => t.SortOrder < LoreTab.SortOrder));
        Assert.Equal(LoreTab.Slug, tabs[^1].Slug);                // the lore tab stays last
        var ids = tabs.SelectMany(ItemIds).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(4, ids.Count(i => i.EndsWith("bruecke", StringComparison.Ordinal)));
        Assert.Equal(1, ids.Count(i => i == "bruecke"));           // the existing chapter keeps its id
        Assert.Equal(AppId, body.GetProperty("detail").GetProperty("steamAppId").GetInt32());
        Assert.Equal(tabs.Count, body.GetProperty("detail").GetProperty("tabs").GetArrayLength());
    }

    [Fact]
    public async Task Answer_with_keys_saves_only_those_and_reports_unknown_and_unsavable_ones()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server);
        var text = Fenced(Obj("K01", "Kapitel Nummer 1"), Obj("K02", "Kapitel Nummer 2"), Obj("K03", "Kapitel Nummer 3", appId: 1));

        var (_, body) = await PostAsync(server, "/api/games/suncraft/manual/batch-answer", new { text, chapters = Items(4, withKeys: true), keys = new[] { "k02", "K03", "K04", "K77" } });

        Assert.Equal(["K02"], body.GetProperty("saved").EnumerateArray().Select(s => s.GetProperty("key").GetString()));
        Assert.Equal(["K03", "K04", "K77"], body.GetProperty("skipped").EnumerateArray().Select(s => s.GetProperty("key").GetString()).Order());
        Assert.Equal(["Kapitel Nummer 2"], (await TabsAsync(server)).Where(t => t.Title.StartsWith("Kapitel", StringComparison.Ordinal)).Select(t => t.Title));
    }

    [Fact]
    public async Task Failed_chapters_with_tab_ids_are_replaced_in_place_and_a_not_applicable_answer_removes_the_placeholder_mixed_with_new_chapters()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        var seed = await SeedAsync(server);
        var items = new[]
        {
            new ManualBatchChapter("K01", "Fehlt A", "Auftrag A", "extra", null, null, seed.PlaceholderA),   // failed chapter -> replaced in place
            new ManualBatchChapter("K02", "Fehlt B", "Auftrag B", "extra", null, null, seed.PlaceholderB),   // failed chapter -> the topic does not exist -> removed
            new ManualBatchChapter("K03", "Neu dazu", "Auftrag C", "extra", null, null, null),               // new chapter at the end
        };
        var text = Fenced(Obj("K01", "Fehlt A"), NotApplicable("K02"), Obj("K03", "Neu dazu"));
        var before = await TabsAsync(server);

        var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/batch-answer", new { text, chapters = items });

        Assert.Equal(HttpStatusCode.OK, status);
        var saved = body.GetProperty("saved").EnumerateArray().ToDictionary(s => s.GetProperty("key").GetString()!);
        Assert.True(saved["K01"].GetProperty("replaced").GetBoolean());
        Assert.Equal(seed.PlaceholderA, saved["K01"].GetProperty("tabId").GetGuid());
        Assert.True(saved["K02"].GetProperty("dropped").GetBoolean());
        Assert.Equal(seed.PlaceholderB, saved["K02"].GetProperty("tabId").GetGuid());
        Assert.False(saved["K03"].GetProperty("replaced").GetBoolean());

        var after = await TabsAsync(server);
        Assert.Equal(before.Count, after.Count);                                         // one replaced, one removed, one added
        var replaced = after.Single(t => t.Id == seed.PlaceholderA);
        Assert.Equal(2, replaced.SortOrder);                                             // same place
        Assert.Null(FailedChapters.Read(replaced.SectionsJson));                         // real content now
        Assert.DoesNotContain(after, t => t.Id == seed.PlaceholderB);
        Assert.Equal("Neu dazu", after[^2].Title);
        Assert.Equal(LoreTab.Slug, after[^1].Slug);
        var ids = after.SelectMany(ItemIds).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Tab_ids_that_are_not_failed_chapters_or_are_used_twice_are_skipped_not_saved()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        var seed = await SeedAsync(server);
        var items = new[]
        {
            new ManualBatchChapter("K01", "Echt", "f", "extra", null, null, seed.RealTab),             // a real chapter is never overwritten
            new ManualBatchChapter("K02", "Weg", "f", "extra", null, null, Guid.NewGuid()),            // unknown tab
            new ManualBatchChapter("K03", "Fehlt A", "f", "extra", null, null, seed.PlaceholderA),
            new ManualBatchChapter("K04", "Fehlt A noch mal", "f", "extra", null, null, seed.PlaceholderA),   // the same placeholder twice
        };
        var text = Fenced(Obj("K01", "Echt"), Obj("K02", "Weg"), Obj("K03", "Fehlt A"), Obj("K04", "Fehlt A noch mal"));
        var before = await TabsAsync(server);

        var (_, body) = await PostAsync(server, "/api/games/suncraft/manual/batch-answer", new { text, chapters = items });

        Assert.Equal(["K03"], body.GetProperty("saved").EnumerateArray().Select(s => s.GetProperty("key").GetString()));
        Assert.Equal(["K01", "K02", "K04"], body.GetProperty("skipped").EnumerateArray().Select(s => s.GetProperty("key").GetString()));
        var after = await TabsAsync(server);
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(before.Single(t => t.Id == seed.RealTab).SectionsJson, after.Single(t => t.Id == seed.RealTab).SectionsJson);
    }

    [Fact]
    public async Task The_walkthrough_replaces_an_existing_one_and_a_game_without_AppID_and_an_English_guide_work_end_to_end()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server, slug: "english", appId: null, language: Lang.English);
        var items = new[]
        {
            new ManualBatchChapter("K01", "Walkthrough", "Whole game", "walkthrough", null, null, null),
            new ManualBatchChapter("K02", "Other", "f", "extra", null, null, null),
        };
        string Answer(string key, string title, string text) => new JsonObject { ["game"] = "Suncraft", ["chapter"] = key, ["title"] = title, ["sections"] = Node(key, title, text: text)["sections"]!.DeepClone() }.ToJsonString();
        const string english = "The bridge is the first big upgrade of the village and it connects the village with the market where you can find everything.";

        var first = await PostAsync(server, "/api/games/english/manual/batch-answer", new { text = Fenced(Answer("K01", "Walkthrough", english), Answer("K02", "Other", english)), chapters = items }, "en");
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(2, first.Body.GetProperty("saved").GetArrayLength());

        // A second walkthrough answer replaces the first one in place (fixed address, once per game)
        var second = await PostAsync(server, "/api/games/english/manual/batch-answer", new { text = Fenced(Answer("K01", "Walkthrough", english)), chapters = items, keys = new[] { "K01" } }, "en");
        Assert.True(second.Body.GetProperty("saved")[0].GetProperty("replaced").GetBoolean());
        Assert.Single(await TabsAsync(server, "english"), t => t.Slug == WalkthroughTab.Slug);
    }

    [Fact]
    public async Task The_prompt_endpoint_needs_a_research_assignment_for_every_chapter()
    {
        // The prompt needs Steam facts (a web call), so only the refusal path runs through HTTP in this suite; the composer itself is covered above
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server);

        var (status, _) = await PostAsync(server, "/api/games/suncraft/manual/batch-prompt", new { chapters = new[] { new { title = "Nur Titel" } } });

        Assert.Equal(HttpStatusCode.BadRequest, status);   // the research assignment is mandatory for the prompt
    }

    [Fact]
    public void A_realistic_31_chapter_batch_prompt_is_reasonable_in_size()
    {
        var chapters = Plan(31, walkthrough: true);
        var german = AiGuideGeneratorService.ComposeManualBatchPrompt(Context(), Game(), Lang.German, chapters);
        var english = AiGuideGeneratorService.ComposeManualBatchPrompt(Context(Lang.English), Game(language: Lang.English), Lang.English, chapters);
        var path = Environment.GetEnvironmentVariable("PV_BATCH_SIZE_FILE");
        if (!string.IsNullOrEmpty(path))
        {
            // Opt-in (PV_BATCH_SIZE_FILE): writes the sizes and both prompts next to the given file so the text can be read by a person
            var singles = chapters.Sum(c => AiGuideGeneratorService.ComposeManualChapterPrompt(Context(), Game(), Lang.German, c.Title, c.Focus, "etabliert", c.TopicId).Length);
            var one = AiGuideGeneratorService.ComposeManualBatchPrompt(Context(), Game(), Lang.German, chapters.Take(1).ToList()).Length;
            File.WriteAllText(path, $"de={german.Length}\nen={english.Length}\nsingleDe31={singles}\nbatchDe1Chapter={one}\n", new UTF8Encoding(false));
            File.WriteAllText(path + ".de.txt", german, new UTF8Encoding(false));
            File.WriteAllText(path + ".en.txt", english, new UTF8Encoding(false));
        }
        Assert.InRange(german.Length, 3_000, 40_000);
        Assert.InRange(english.Length, 3_000, 40_000);
    }
}
