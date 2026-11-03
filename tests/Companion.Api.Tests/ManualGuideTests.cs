using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>
/// The guide via the user's own AI subscription: the prompt a chat gets and how a pasted answer is read, checked and judged. Nothing here uses a provider.
/// </summary>
public sealed class ManualGuideTests
{
    private static GameEntity Game(string title = "Suncraft", int? appId = 4078110, string language = Lang.German) =>
        new() { Id = Guid.NewGuid(), Slug = "suncraft", Title = title, SteamAppId = appId, ContentLanguage = language, Tabs = [] };

    private static AiGuideGeneratorService.GameContext Context(string language = Lang.German, ResearchBlocks.Selection? blocks = null) =>
        new("Suncraft", language == Lang.English ? "Steam AppID: 4078110\nGenres according to Steam: Simulation" : "Steam-AppID: 4078110\nGenres laut Steam: Simulation", [], [], blocks ?? ResearchBlocks.Selection.Empty,
            ResearchBlocks.FillBase(language, "Suncraft", "Simulation", ResearchBlocks.PlatformText), language);

    private static string Chapter(int? appId = 4078110, string title = "Dorfausbau", string text = "Die Brücke ist der erste Ausbauschritt und verbindet das Dorf mit dem Markt, wo man alles für den Anfang findet.") =>
        new JsonObject
        {
            ["game"] = "Suncraft",
            ["steamAppId"] = appId,
            ["title"] = title,
            ["icon"] = "mdi:home",
            ["sections"] = new JsonArray(
                new JsonObject { ["type"] = "alert", ["variant"] = "gold", ["title"] = "Das Wichtigste vorab", ["htmlContent"] = text },
                new JsonObject
                {
                    ["type"] = "checklist_table",
                    ["table"] = new JsonObject
                    {
                        ["tableId"] = "bau", ["caption"] = "Bauten", ["categories"] = new JsonArray("Dorf"),
                        ["columns"] = new JsonArray(new JsonObject { ["key"] = "name", ["label"] = "Name", ["isRowHeader"] = true }),
                        ["rows"] = new JsonArray(
                            new JsonObject { ["itemId"] = "bruecke", ["filterCategory"] = "Dorf", ["cells"] = new JsonObject { ["name"] = "Brücke" } },
                            new JsonObject { ["itemId"] = "markt", ["filterCategory"] = "Dorf", ["cells"] = new JsonObject { ["name"] = "Markt" } })
                    }
                }),
        }.ToJsonString();

    // ---- the prompts -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_chapter_prompt_names_the_game_and_its_AppID_the_blocks_the_language_and_the_output_contract()
    {
        var blocks = new ResearchBlocks.Selection("compact", ["missables"], "none", "tables");
        var prompt = AiGuideGeneratorService.ComposeManualChapterPrompt(Context(blocks: blocks), Game(), Lang.German, "Dorfausbau", "Alle Ausbaustufen des Dorfes", "dünn");

        Assert.Contains("Steam-AppID: 4078110", prompt);                       // the Steam facts of the real AI path
        Assert.Contains("„Suncraft“, Steam-AppID 4078110", prompt);            // the identification of the contract
        Assert.Contains("\"steamAppId\": 4078110", prompt);                    // the field the answer has to echo
        Assert.Contains("Kapitel: „Dorfausbau“", prompt);
        Assert.Contains("Alle Ausbaustufen des Dorfes", prompt);
        Assert.Contains("Umfang – Kompakt", prompt);                           // the research blocks
        Assert.Contains("Verpassbares zuerst", prompt);
        Assert.Contains("spoilerfrei", prompt);
        Assert.Contains("Tabellen bevorzugen", prompt);
        Assert.Contains("```json", prompt);                                    // one JSON object in one code block
        Assert.Contains("Schreibe ALLE Texte auf Deutsch", prompt);            // the guide language
        Assert.DoesNotContain("Begriffe zweisprachig", prompt);
    }

    [Fact]
    public void The_blueprint_prompt_has_the_budget_and_the_identification_in_English_too()
    {
        var prompt = AiGuideGeneratorService.ComposeManualBlueprintPrompt(Context(Lang.English), Game(language: Lang.English), Lang.English);

        Assert.Contains("Steam AppID: 4078110", prompt);
        Assert.Contains("\"Suncraft\", Steam AppID 4078110", prompt);
        Assert.Contains("scope \"Detailed\" – at most 14 chapters", prompt);
        Assert.Contains("\"steamAppId\": 4078110", prompt);
        Assert.Contains("Write ALL texts of your answer in English", prompt);
        Assert.Contains("exactly one code block", prompt);
    }

    [Fact]
    public void A_game_without_AppID_says_so_and_asks_for_null()
    {
        var prompt = AiGuideGeneratorService.ComposeManualChapterPrompt(Context(), Game(appId: null), Lang.German, "Roadmap", "Reihenfolge", "dünn");

        Assert.Contains("Steam-AppID unbekannt", prompt);
        Assert.Contains("\"steamAppId\": null", prompt);
    }

    // ---- reading an answer -----------------------------------------------------------------------------------------------------

    // Only the answer reading is exercised: no provider, Steam or settings collaborator is ever touched
    private static AiGuideGeneratorService Generator() =>
        new(null!, null!, null!, null!, null!, null!, Microsoft.Extensions.Options.Options.Create(new Companion.Api.Configuration.CompanionOptions()), null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<AiGuideGeneratorService>.Instance);

    private static ManualChapterCheckDto Check(string answer, GameEntity? game = null) =>
        Generator()
            .CheckManualChapter(game ?? Game(), answer, "Dorfausbau").Dto;

    [Theory]
    [InlineData("plain")]
    [InlineData("fenced")]
    [InlineData("sentences")]
    [InlineData("trailing")]
    public void An_answer_is_found_in_a_code_block_between_sentences_and_with_trailing_commas(string shape)
    {
        var json = Chapter();
        var answer = shape switch
        {
            "fenced" => "```json\n" + json + "\n```",
            "sentences" => "Klar, hier ist das Kapitel {mit geschweiften Klammern im Satz}:\n\n```json\n" + json + "\n```\n\nViel Erfolg! {Ende}",
            "trailing" => json.Replace("\"Brücke\"}", "\"Brücke\"},").Replace("}}]}", "}}],}"),
            _ => json
        };

        var result = Check(answer);

        Assert.True(result.Ok, string.Join(" | ", result.Errors.Select(e => e.Message)));
        Assert.Equal("Dorfausbau", result.Preview!.Title);
        Assert.Equal(2, result.Preview.SectionCount);
        Assert.Equal(2, result.Preview.TableRows);
    }

    [Fact]
    public void A_cut_off_answer_says_so_with_a_hint_what_to_do()
    {
        var json = Chapter();
        var result = Check(json[..(json.Length - 60)]);

        var error = Assert.Single(result.Errors);
        Assert.Equal("cutOff", error.Code);
        Assert.Contains("abgeschnitten", error.Message);
        Assert.False(string.IsNullOrEmpty(error.Details));
    }

    [Fact]
    public void An_answer_that_starts_an_object_but_stops_before_any_closing_bracket_is_cut_off_too()
    {
        var error = Assert.Single(Check("Hier: ```json\n{\"steamAppId\":4078110,\"sections\":[").Errors);

        Assert.Equal("cutOff", error.Code);
    }

    [Fact]
    public void Broken_JSON_names_the_place_and_typographic_quotes_get_their_own_hint()
    {
        var broken = Chapter().Replace("\"title\":\"Dorfausbau\"", "“title”:\"Dorfausbau\"");

        var result = Check(broken);

        var error = Assert.Single(result.Errors);
        Assert.Equal("invalidJson", error.Code);
        Assert.Contains("Zeile 1", error.Message);
        Assert.Contains("typografische Anführungszeichen", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_pasted_is_not_an_answer(string text)
    {
        Assert.Equal("empty", Assert.Single(Check(text).Errors).Code);
    }

    [Fact]
    public void Text_without_a_JSON_object_is_refused_with_advice()
    {
        var error = Assert.Single(Check("Leider kann ich dir dabei nicht helfen.").Errors);

        Assert.Equal("noJson", error.Code);
        Assert.Contains("Codeblock", error.Message);
    }

    [Fact]
    public void The_wrong_kind_of_answer_is_told_apart()
    {
        Assert.Equal("blueprintForChapter", Assert.Single(Check("""{"topics":[{"id":"weapons","applicable":true}]}""").Errors).Code);
        Assert.Equal("wholeGuide", Assert.Single(Check("""{"format":"platinvault-guide","formatVersion":1,"tabs":[]}""").Errors).Code);
        Assert.Equal("noSections", Assert.Single(Check("""{"steamAppId":4078110,"title":"x"}""").Errors).Code);
        Assert.Equal("noSections", Assert.Single(Check("""{"steamAppId":4078110,"sections":[]}""").Errors).Code);
        Assert.Equal("sectionsNotList", Assert.Single(Check("""{"steamAppId":4078110,"sections":"text"}""").Errors).Code);
    }

    [Fact]
    public void An_answer_for_another_game_is_an_error_a_missing_AppID_only_a_warning()
    {
        var wrong = Check(Chapter(appId: 1091500));
        var error = Assert.Single(wrong.Errors);
        Assert.Equal("wrongGame", error.Code);
        Assert.Contains("1091500", error.Message);
        Assert.Contains("4078110", error.Message);

        var missing = Check(Chapter(appId: null));
        Assert.True(missing.Ok);
        Assert.Contains(missing.Warnings, w => w.Code == "noAppId");
    }

    [Fact]
    public void Unreadable_sections_are_named_one_by_one_and_a_chapter_without_any_readable_section_is_an_error()
    {
        var oneBad = Check("""{"steamAppId":4078110,"title":"T","sections":[{"type":"alert","htmlContent":"Ein Hinweis, der lang genug ist."},{"type":"poem","htmlContent":"x"},{"htmlContent":"y"},"text"]}""");
        Assert.True(oneBad.Ok);
        Assert.Equal(3, oneBad.Warnings.Count(w => w.Code == "sectionSkipped"));
        Assert.Contains(oneBad.Warnings, w => w.Message.Contains("Abschnitt 2") && w.Message.Contains("poem"));

        var none = Check("""{"steamAppId":4078110,"title":"T","sections":[{"type":"poem"},{"type":"table"}]}""");
        Assert.False(none.Ok);
        Assert.Equal("nothingReadable", Assert.Single(none.Errors).Code);
    }

    [Fact]
    public void Item_ids_that_are_missing_or_doubled_and_categories_that_are_not_listed_are_reported()
    {
        var answer = """
            {"steamAppId":4078110,"title":"T","sections":[{"type":"checklist_table","table":{"categories":["A"],"columns":[{"key":"n","label":"N"}],
            "rows":[{"itemId":"x","filterCategory":"A","cells":{"n":"1"}},{"itemId":"x","filterCategory":"B","cells":{"n":"2"}},{"filterCategory":"A","cells":{"n":"3"}}]}}]}
            """;

        var result = Check(answer);

        Assert.True(result.Ok);
        Assert.Contains(result.Warnings, w => w.Code == "duplicateIds");
        Assert.Contains(result.Warnings, w => w.Code == "rowsWithoutId");
        Assert.Contains(result.Warnings, w => w.Code == "categoryNotListed");
        Assert.Equal(3, result.Preview!.TableRows);
    }

    [Fact]
    public void A_chapter_in_the_wrong_language_is_a_warning_not_an_error()
    {
        var english = "The bridge is the first big upgrade of the village and it connects the village with the market where you can find everything for the start of the game and you should build it before you do anything else in the game.";

        var result = Check(Chapter(text: english));

        Assert.True(result.Ok);
        Assert.Contains(result.Warnings, w => w.Code == "otherLanguage" && w.Message.Contains("deutsch"));
    }

    [Fact]
    public void A_title_with_a_bracketed_translation_is_cleaned_and_a_missing_title_falls_back_to_the_planned_one()
    {
        Assert.Equal("Dorfausbau", Check(Chapter(title: "Dorfausbau (Village Building)")).Preview!.Title);

        var untitled = Check("""{"steamAppId":4078110,"sections":[{"type":"alert","htmlContent":"Ein Hinweis, der lang genug ist."}]}""");
        Assert.Equal("Dorfausbau", untitled.Preview!.Title);
        Assert.Contains(untitled.Warnings, w => w.Code == "noTitle");
    }

    [Fact]
    public void An_answer_that_is_read_becomes_a_tab_like_an_AI_result()
    {
        var generator = Generator();
        var game = Game();
        var checkedAnswer = generator.CheckManualChapter(game, "```json\n" + Chapter(title: "Dorfausbau (Village Building)") + "\n```", "Dorfausbau");
        Assert.NotNull(checkedAnswer.CanonicalJson);

        var tab = generator.BuildManualChapterTab(game, 7, new ManualChapterAnswerRequest(null, "Dorfausbau", "mdi:home", "Alle Ausbaustufen", "extra", null), checkedAnswer.CanonicalJson);

        Assert.Equal("Dorfausbau", tab.Title);
        Assert.Equal(7, tab.SortOrder);
        Assert.StartsWith("tab-7-", tab.Slug);
        Assert.False(string.IsNullOrEmpty(tab.GroupKey));
        using var sections = JsonDocument.Parse(tab.SectionsJson);
        Assert.Equal(2, sections.RootElement.GetArrayLength());
    }

    // ---- is it the same game? --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Suncraft", "Suncraft", true)]
    [InlineData("SUNCRAFT: Gold Edition", "Suncraft", true)]
    [InlineData("The Witcher 3", "Witcher 3", true)]
    [InlineData("Stardew Valley", "Suncraft", false)]
    public void Names_are_compared_loosely(string a, string b, bool same)
    {
        Assert.Equal(same, ManualAnswers.SameName(a, b));
    }
}
