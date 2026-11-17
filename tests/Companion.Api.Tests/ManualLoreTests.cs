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
/// Story &amp; Lore via the user's own AI subscription: the prompt for all four parts, how a pasted answer is read and judged, how it is saved (new tab or
/// in place) and that the API research and the pasted answer produce the same tab. Fake providers only - nothing here talks to a real AI provider.
/// </summary>
public sealed class ManualLoreTests
{
    private const int AppId = 4078110;

    private static GameEntity Game(string language = Lang.German, int? appId = AppId) =>
        new() { Id = Guid.NewGuid(), Slug = "suncraft", Title = "Suncraft", SteamAppId = appId, ContentLanguage = language, Tabs = [] };

    private static AiGuideGeneratorService.GameContext Context(string language = Lang.German) =>
        new("Suncraft", language == Lang.English ? "Steam AppID: 4078110" : "Steam-AppID: 4078110", [], [], ResearchBlocks.Selection.Empty,
            ResearchBlocks.FillBase(language, "Suncraft", "Simulation", ResearchBlocks.PlatformText), language);

    // Only the answer reading is exercised: no provider, Steam or settings collaborator is ever touched
    private static AiGuideGeneratorService Generator() =>
        new(null!, null!, null!, null!, null!, null!, Microsoft.Extensions.Options.Options.Create(new Companion.Api.Configuration.CompanionOptions()), null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<AiGuideGeneratorService>.Instance);

    private static ManualLoreCheckDto Check(string answer, GameEntity? game = null) => Generator().CheckManualLore(game ?? Game(), answer).Dto;

    // ---- the answers ------------------------------------------------------------------------------------------------------------

    private const string GermanText = "Die Geschichte beginnt in einem kleinen Dorf, in dem der Held mit seiner Familie lebt, und sie führt ihn über viele Umwege bis in die Hauptstadt, wo er sich dem Herrscher stellen muss.";
    private const string EnglishText = "The story begins in a small village where the hero lives with his family, and it leads him through many detours to the capital, where he has to face the ruler of the land.";

    /// <summary>The sections of part n (1-4): L2 has a table that claims to be checkable (the merge must switch that off).</summary>
    private static JsonArray Sections(int part, string text = GermanText) => part == 2
        ? new JsonArray(
            new JsonObject { ["type"] = "cards_grid", ["columns"] = 2, ["cards"] = new JsonArray(new JsonObject { ["title"] = "Mira", ["introText"] = text }) },
            new JsonObject
            {
                ["type"] = "checklist_table",
                ["table"] = new JsonObject
                {
                    ["tableId"] = "beziehungen", ["caption"] = "Beziehungen", ["checkable"] = true, ["categories"] = new JsonArray("Familie"),
                    ["columns"] = new JsonArray(new JsonObject { ["key"] = "figur_a", ["label"] = "Figur A", ["isRowHeader"] = true }),
                    ["rows"] = new JsonArray(new JsonObject { ["itemId"] = "mira-vater", ["filterCategory"] = "Familie", ["cells"] = new JsonObject { ["figur_a"] = "Mira" } })
                }
            })
        : new JsonArray(new JsonObject { ["type"] = "story", ["title"] = $"Abschnitt {part}", ["paragraphs"] = new JsonArray(text) });

    private static JsonObject Entry(int part, string text = GermanText) => new() { ["part"] = $"L{part}", ["sections"] = Sections(part, text) };

    private static JsonObject Answer(int? appId = AppId, string game = "Suncraft", params int[] parts) =>
        new()
        {
            ["game"] = game,
            ["steamAppId"] = appId,
            ["parts"] = new JsonArray(parts.Select(p => (JsonNode?)Entry(p)).ToArray())
        };

    private static string Fenced(JsonObject answer) => "Hier ist die Antwort:\n```json\n" + answer.ToJsonString() + "\n```\nALLE TEILE FERTIG: L1–L4";

    private static string Titles(int index) => AiGuideGeneratorService.LoreParts(Lang.German)[index].Title;

    // ---- the prompt -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_prompt_asks_for_all_four_parts_with_ids_the_game_its_AppID_and_the_rules_of_every_content_prompt()
    {
        var prompt = AiGuideGeneratorService.ComposeManualLorePrompt(Context(), Game(), Lang.German);

        foreach (var (part, index) in AiGuideGeneratorService.LoreParts(Lang.German).Select((p, i) => (p, i)))
        {
            Assert.Contains($"- L{index + 1} | \"{part.Title}\"", prompt);
            Assert.Contains(part.Brief.Split('\n')[0].Trim(), prompt);                   // the assignment of the API prompt of this part
        }
        Assert.Contains("Steam-AppID: 4078110", prompt);                               // the Steam facts
        Assert.Contains("„Suncraft“, Steam-AppID 4078110", prompt);                    // the identification of the contract
        Assert.Contains("\"parts\": [ { \"part\": \"L1\", \"sections\": [...] }", prompt);
        Assert.Contains("platinvault-suncraft-lore.json", prompt);                     // the file the chat may hand back
        Assert.Contains("NOCH OFFEN: L3–L4", prompt);                                  // splitting rule
        Assert.Contains("ALLE TEILE FERTIG: L1–L4", prompt);
        Assert.Contains("SPOILER sind ausdrücklich gewünscht", prompt);
        Assert.Contains(AiGuideGeneratorService.LoreStyleAndBlocks(Lang.German), prompt);          // the same style and blocks as the API prompt
        Assert.Contains("Der ganze Guide ist auf Deutsch", prompt);                    // language rule
        Assert.Contains("Plattform (verbindliche Regel)", prompt);                           // Steam/PC rule
        Assert.Contains("Schritt 1", prompt);                                          // game wiki first
        Assert.DoesNotContain("Begriffe zweisprachig", prompt);
    }

    [Fact]
    public void The_english_prompt_and_a_game_without_AppID()
    {
        var prompt = AiGuideGeneratorService.ComposeManualLorePrompt(Context(Lang.English), Game(Lang.English, appId: null), Lang.English);

        Assert.Contains("- L4 | \"Insider knowledge & development\"", prompt);
        Assert.Contains("\"steamAppId\": null", prompt);
        Assert.Contains("Steam AppID unknown", prompt);
        Assert.Contains("STILL OPEN: L3–L4", prompt);
        Assert.Contains("ALL PARTS DONE: L1–L4", prompt);
        Assert.Contains("Write ALL texts in English", prompt);
    }

    // ---- reading an answer ------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_complete_answer_is_ok_with_a_preview_of_the_four_parts_in_the_fixed_order_and_no_warning()
    {
        var result = Check(Fenced(Answer(parts: [3, 1, 4, 2])));          // the chat may write them in any order

        Assert.True(result.Ok, string.Join(" | ", result.Errors.Select(e => e.Message)));
        Assert.Empty(result.Warnings);
        Assert.Null(result.FixPrompt);
        Assert.Equal(AiGuideGeneratorService.LoreParts(Lang.German).Select(p => p.Title), result.Preview!.Parts.Select(p => p.Title));
        Assert.Equal([1, 2, 1, 1], result.Preview.Parts.Select(p => p.SectionCount));
        Assert.Empty(result.Preview.MissingParts);
    }

    [Fact]
    public void A_missing_part_is_a_warning_and_listed_the_other_parts_stay_saveable()
    {
        var checkedAnswer = Generator().CheckManualLore(Game(), Fenced(Answer(parts: [1, 2, 4])));

        Assert.True(checkedAnswer.Dto.Ok);
        var warning = Assert.Single(checkedAnswer.Dto.Warnings);
        Assert.Equal("partsMissing", warning.Code);
        Assert.Contains("L3", warning.Message);
        Assert.Contains(Titles(2), warning.Message);
        Assert.Equal([Titles(2)], checkedAnswer.Dto.Preview!.MissingParts);
        Assert.Equal(3, checkedAnswer.Dto.Preview.Parts.Count);
        Assert.Equal(3, checkedAnswer.Parts!.Count);
    }

    [Fact]
    public void An_unknown_part_id_and_an_entry_without_one_are_warnings_and_ignored()
    {
        var answer = Answer(parts: [1, 2, 3, 4]);
        var parts = (JsonArray)answer["parts"]!;
        parts.Add(new JsonObject { ["part"] = "L9", ["sections"] = Sections(1) });
        parts.Add(new JsonObject { ["sections"] = Sections(1) });

        var result = Check(Fenced(answer));

        Assert.True(result.Ok);
        Assert.Equal(["unknownPart", "partNoId"], result.Warnings.Select(w => w.Code));
        Assert.Contains("L9", result.Warnings[0].Message);
        Assert.Equal(4, result.Preview!.Parts.Count);
    }

    [Fact]
    public void Id_spellings_and_a_list_keyed_by_part_id_are_tolerated()
    {
        var spelled = Answer(parts: [1, 2, 3, 4]);
        ((JsonArray)spelled["parts"]!)[0]!["part"] = "l-01";
        ((JsonArray)spelled["parts"]!)[1]!["part"] = " Teil 2 ";
        Assert.Equal(4, Check(spelled.ToJsonString()).Preview!.Parts.Count);

        var keyed = new JsonObject
        {
            ["game"] = "Suncraft", ["steamAppId"] = AppId,
            ["parts"] = new JsonObject { ["L1"] = Sections(1), ["L2"] = new JsonObject { ["sections"] = Sections(2) } }
        };
        var result = Check(keyed.ToJsonString());
        Assert.True(result.Ok);
        Assert.Equal(2, result.Preview!.Parts.Count);
    }

    [Fact]
    public void The_answer_of_another_game_is_an_error()
    {
        var result = Check(Fenced(Answer(appId: 12345, parts: [1, 2, 3, 4])));

        Assert.False(result.Ok);
        Assert.Null(result.Preview);
        var error = Assert.Single(result.Errors);
        Assert.Equal("wrongGame", error.Code);
        Assert.Contains("12345", error.Message);
    }

    [Fact]
    public void An_answer_without_an_AppID_warns_and_the_name_is_checked()
    {
        var result = Check(Fenced(Answer(appId: null, game: "Anderes Spiel", parts: [1, 2, 3, 4])));

        Assert.True(result.Ok);
        Assert.Contains(result.Warnings, w => w.Code == "noAppId");
    }

    [Fact]
    public void The_skeleton_of_the_prompt_pasted_back_is_told_apart_from_a_broken_answer()
    {
        var prompt = AiGuideGeneratorService.ComposeManualLorePrompt(Context(), Game(), Lang.German);
        var skeleton = prompt.Split('\n').First(l => l.Contains("Beginne jeden Teil in einer neuen Zeile")).Split("(verkürzt, schreibe die echten Inhalte): ")[1];

        var pasted = Check(skeleton);
        var withPlaceholders = Check("""{ "game": "Suncraft", "steamAppId": 4078110, "parts": [ { "part": "L1", "sections": ["..."] }, { "part": "L2", "sections": ["…"] } ] }""");

        Assert.False(pasted.Ok);
        Assert.Equal("promptPattern", Assert.Single(pasted.Errors).Code);
        Assert.False(withPlaceholders.Ok);
        Assert.Equal("promptPattern", Assert.Single(withPlaceholders.Errors).Code);
    }

    [Theory]
    [InlineData("Das ist kein JSON, sondern nur ein Satz ohne Klammern.", "noJson")]
    [InlineData("", "empty")]
    [InlineData("{ \"game\": \"Suncraft\", \"steamAppId\": 4078110, \"parts\": [ { \"part\": \"L1\", \"sections\": [ { \"type\": \"story\"", "cutOff")]
    [InlineData("{ \"game\": \"Suncraft\", \"parts\": [ { part: L1 } ] }", "invalidJson")]
    public void Garbage_is_an_error_with_a_specific_message(string text, string code)
    {
        var result = Check(text);

        Assert.False(result.Ok);
        Assert.Equal(code, Assert.Single(result.Errors).Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Errors[0].Message));
    }

    [Fact]
    public void A_chapter_answer_a_whole_guide_and_an_answer_without_readable_parts_are_errors()
    {
        var chapter = Check("""{ "game": "Suncraft", "steamAppId": 4078110, "title": "Waffen", "sections": [ { "type": "story", "paragraphs": ["x"] } ] }""");
        var whole = Check("""{ "format": "platinvault-guide", "parts": [] }""");
        var emptyParts = Check("""{ "game": "Suncraft", "steamAppId": 4078110, "parts": [] }""");
        var unusable = Check("""{ "game": "Suncraft", "steamAppId": 4078110, "parts": [ { "part": "L1", "sections": [ { "type": "unbekannt" } ] }, { "part": "L2", "sections": [] } ] }""");

        Assert.Equal("sectionsWithoutParts", Assert.Single(chapter.Errors).Code);
        Assert.Contains(whole.Errors, e => e.Code == "wholeGuide");
        Assert.Equal("noParts", Assert.Single(emptyParts.Errors).Code);
        Assert.Equal("noReadableParts", Assert.Single(unusable.Errors).Code);
        Assert.Equal(["sectionSkipped", "partUnreadable", "partUnreadable"], unusable.Warnings.Select(w => w.Code));
        Assert.StartsWith("Teil L1", unusable.Warnings[0].Message);               // the notes of the normalizer say which part they are about
    }

    [Fact]
    public void English_sentences_in_a_german_guide_are_a_warning_with_a_ready_fix_prompt()
    {
        var answer = Answer(parts: [1, 2, 3, 4]);
        ((JsonArray)answer["parts"]!)[0] = Entry(1, "Go to the town and talk to the elder. Then you return to the gate and wait for the guard to open it.");

        var result = Check(Fenced(answer));

        Assert.True(result.Ok);
        var warning = Assert.Single(result.Warnings, w => w.Code == "foreignSentences");
        Assert.NotEmpty(warning.Examples!);
        Assert.NotNull(result.FixPrompt);
        Assert.Contains("Story & Lore", result.FixPrompt);
        Assert.Contains("Go to the town", result.FixPrompt);
        Assert.Contains("L1–L4", result.FixPrompt);
        Assert.Contains("„parts“", result.FixPrompt);
    }

    [Fact]
    public void A_whole_answer_in_the_other_language_is_a_warning()
    {
        var answer = new JsonObject { ["game"] = "Suncraft", ["steamAppId"] = AppId, ["parts"] = new JsonArray(Entry(1, EnglishText), Entry(3, EnglishText), Entry(4, EnglishText)) };

        var result = Check(Fenced(answer));

        Assert.Contains(result.Warnings, w => w.Code == "otherLanguage");
    }

    [Fact]
    public void A_continuation_after_STILL_OPEN_is_merged_and_a_part_that_comes_twice_uses_the_last_one()
    {
        var first = Answer(parts: [1, 2]);
        var second = Answer(parts: [3, 4]);
        ((JsonArray)second["parts"]!).Add(Entry(1, "Eine neuere Fassung des ersten Teils, die ebenfalls auf Deutsch geschrieben ist und mit mehr Worten als vorher erzählt, was im Dorf geschah."));
        var text = "```json\n" + first.ToJsonString() + "\n```\nNOCH OFFEN: L3–L4\n\nweiter\n\n```json\n" + second.ToJsonString() + "\n```";

        var checkedAnswer = Generator().CheckManualLore(Game(), text);

        Assert.True(checkedAnswer.Dto.Ok, string.Join(" | ", checkedAnswer.Dto.Errors.Select(e => e.Message)));
        Assert.Equal(4, checkedAnswer.Dto.Preview!.Parts.Count);
        Assert.Empty(checkedAnswer.Dto.Preview.MissingParts);
        Assert.Equal(["partTwice"], checkedAnswer.Dto.Warnings.Select(w => w.Code));
        Assert.Contains("neuere Fassung", checkedAnswer.Parts![0].Sections!.ToJsonString());
    }

    [Fact]
    public void Parts_written_as_objects_of_their_own_or_as_a_bare_list_add_up_too()
    {
        var bareList = "[\n" + string.Join(",\n", new[] { 1, 2, 3, 4 }.Select(p => Entry(p).ToJsonString())) + "\n]";
        var separate = string.Join("\n\n", new[] { 1, 2, 3 }.Select(p => "```json\n" + Entry(p).ToJsonString() + "\n```"));

        var list = Check(bareList);
        var objects = Check(separate);

        Assert.True(list.Ok, string.Join(" | ", list.Errors.Select(e => e.Message)));
        Assert.Equal(4, list.Preview!.Parts.Count);
        Assert.True(objects.Ok);
        Assert.Equal(3, objects.Preview!.Parts.Count);
        Assert.Equal([Titles(3)], objects.Preview.MissingParts);
    }

    [Fact]
    public void A_broken_later_block_is_mentioned_and_does_not_hide_the_readable_first_one()
    {
        var text = Fenced(Answer(parts: [1, 2])) + "\n\n{ \"game\": \"Suncraft\", \"parts\": [ { \"part\": \"L3\", \"sections\": [ { \"type\": \"story\"";

        var result = Check(text);

        Assert.True(result.Ok);
        Assert.Contains(result.Warnings, w => w.Code is "extraBlockBroken" or "partsMissing");
        Assert.Equal(2, result.Preview!.Parts.Count);
    }

    [Fact]
    public async Task Many_small_objects_after_the_answer_are_not_read_one_by_one_without_end()
    {
        // Objects without "parts" used to be free of the object limit, so ~1 million "{}" after the answer (still under the 2 MB size limit) made
        // the reader take the rest of the text apart object by object, copying the remainder each time (quadratic: hours of CPU per request)
        var text = Fenced(Answer(parts: [1, 2, 3, 4])) + "\n" + string.Concat(Enumerable.Repeat("{}", 950_000));
        Assert.True(text.Length < ManualAnswers.MaxAnswerKilobytes * 1000);

        var check = Task.Run(() => Check(text));
        var finished = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));

        Assert.Same(check, finished);
        var result = await check;
        Assert.True(result.Ok, string.Join(" | ", result.Errors.Select(e => e.Message)));
        Assert.Equal(4, result.Preview!.Parts.Count);
    }

    [Fact]
    public void Hundreds_of_thousands_of_entries_in_parts_give_a_short_answer_and_not_one_warning_each()
    {
        // ~1.8 MB of "{}" entries used to come back as 600,000 warnings (~100 MB of JSON from the live check)
        var noIds = "{\"game\":\"Suncraft\",\"steamAppId\":4078110,\"parts\":[" + string.Join(",", Enumerable.Repeat("{}", 600_000)) + "]}";
        var withParts = Answer(parts: [1, 2, 3, 4]);
        foreach (var _ in Enumerable.Range(0, 200))
        {
            ((JsonArray)withParts["parts"]!).Add(new JsonObject { ["part"] = "L9" });
        }

        var junk = Check(noIds);
        var mixed = Check(withParts.ToJsonString());

        Assert.False(junk.Ok);
        Assert.True(junk.Warnings.Count <= 40, $"{junk.Warnings.Count} warnings");
        Assert.Contains(junk.Warnings, w => w.Code == "tooManyParts");
        Assert.True(JsonSerializer.Serialize(junk).Length < 50_000);
        Assert.True(mixed.Ok);                                   // the four real parts come first and are still read
        Assert.Equal(4, mixed.Preview!.Parts.Count);
        Assert.Contains(mixed.Warnings, w => w.Code == "tooManyParts");
    }

    [Fact]
    public void The_messages_follow_the_language_of_the_request()
    {
        using var english = Lang.Use(Lang.English);
        var result = Check(Fenced(Answer(appId: 1, parts: [1])), Game());

        Assert.Contains("different game", result.Errors[0].Message);
    }

    // ---- the tab ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_merge_puts_a_heading_before_each_part_marks_everything_as_spoiler_and_never_checkable_and_shows_a_failed_part_as_a_danger_alert()
    {
        var parts = AiGuideGeneratorService.LoreParts(Lang.German);
        var tab = AiGuideGeneratorService.MergeLoreTab(Guid.NewGuid(),
        [
            new(parts[0], Sections(1), null),
            new(parts[1], Sections(2), null),
            new(parts[2], null, "Die KI-Antwort war kein gültiges JSON."),
        ], Lang.German);

        Assert.Equal(LoreTab.Slug, tab.Slug);
        Assert.Equal(LoreTab.SortOrder, tab.SortOrder);
        var sections = JsonNode.Parse(tab.SectionsJson)!.AsArray();
        Assert.Equal(["alert", "story", "alert", "cards_grid", "checklist_table", "alert", "alert"], sections.Select(s => s!["type"]!.ToString()));
        Assert.Equal("crystal", sections[0]!["variant"]!.ToString());
        Assert.Equal(parts[0].Title, sections[0]!["title"]!.ToString());
        Assert.Equal(parts[0].Blurb, sections[0]!["htmlContent"]!.ToString());
        Assert.Equal("danger", sections[6]!["variant"]!.ToString());
        Assert.Contains("kein gültiges JSON", sections[6]!["htmlContent"]!.ToString());
        Assert.All(sections, s => Assert.True(s!["spoiler"]!.GetValue<bool>()));
        Assert.False(sections[4]!["table"]!["checkable"]!.GetValue<bool>());
    }

    // ---- the endpoints ----------------------------------------------------------------------------------------------------------

    private static async Task<GuideTabEntity?> LoreAsync(TestServer server, string slug)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        return await db.GuideTabs.AsNoTracking().FirstOrDefaultAsync(t => t.Game!.Slug == slug && t.Slug == LoreTab.Slug);
    }

    private static async Task<Guid> SeedAsync(TestServer server, string slug = "suncraft", int? appId = AppId, bool withLore = false, bool withChapter = true)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var game = new GameEntity { Id = Guid.NewGuid(), Slug = slug, Title = "Suncraft", SteamAppId = appId, ContentLanguage = Lang.German };
        var tabs = new List<GuideTabEntity>();
        if (withChapter)
        {
            tabs.Add(new GuideTabEntity
            {
                Id = Guid.NewGuid(), GameId = game.Id, Slug = "tab-1-bau", Title = "Bau", SortOrder = 1,
                SectionsJson = """[ { "type": "checklist_table", "table": { "tableId": "b", "caption": "Bau", "checkable": true, "columns": [ { "key": "name", "label": "Name" } ], "rows": [ { "itemId": "mira-vater", "cells": { "name": "Brücke" } } ] } } ]"""
            });
        }
        var lore = new GuideTabEntity { Id = Guid.NewGuid(), GameId = game.Id, Slug = LoreTab.Slug, Title = LoreTab.Title, Icon = LoreTab.Icon, SortOrder = LoreTab.SortOrder, SectionsJson = """[ { "type": "story", "title": "Alt", "paragraphs": ["Der alte Reiter."] } ]""" };
        if (withLore)
        {
            tabs.Add(lore);
        }
        game.Tabs = tabs;
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return lore.Id;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(TestServer server, string url, object body, string language = "de")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Requested-With", "PlatinVault");
        request.Headers.Add("Accept-Language", language);
        var response = await server.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text.Length == 0 ? "{}" : text);
        return (response.StatusCode, doc.RootElement.Clone());
    }

    [Fact]
    public async Task The_prompt_endpoint_answers_with_kind_lore_and_unknown_games_are_404_everywhere()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server, appId: null);

        var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/lore-prompt", new { });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("lore", body.GetProperty("kind").GetString());
        Assert.Equal("de", body.GetProperty("language").GetString());
        Assert.Equal("platinvault-suncraft-lore.md", body.GetProperty("fileName").GetString());
        Assert.Equal("Suncraft", body.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("steamAppId").ValueKind);
        Assert.Contains("- L1 |", body.GetProperty("prompt").GetString());

        foreach (var action in new[] { "lore-prompt", "lore-check", "lore-answer" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(server, $"/api/games/nope/manual/{action}", new { text = "x" })).Status);
        }
        var noHeader = await server.Client.PostAsJsonAsync("/api/games/suncraft/manual/lore-check", new { text = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, noHeader.StatusCode);              // the CSRF header is required like everywhere
    }

    [Fact]
    public async Task The_check_endpoint_has_the_agreed_shape_and_saves_nothing()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server);

        var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/lore-check", new { text = Fenced(Answer(parts: [1, 2, 4])) });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.Equal(0, body.GetProperty("errors").GetArrayLength());
        Assert.Equal("partsMissing", body.GetProperty("warnings")[0].GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("fixPrompt").ValueKind);
        var preview = body.GetProperty("preview");
        Assert.Equal(3, preview.GetProperty("parts").GetArrayLength());
        Assert.Equal(Titles(0), preview.GetProperty("parts")[0].GetProperty("title").GetString());
        Assert.Equal(1, preview.GetProperty("parts")[0].GetProperty("sectionCount").GetInt32());
        Assert.Equal([Titles(2)], preview.GetProperty("missingParts").EnumerateArray().Select(p => p.GetString()));
        Assert.Null(await LoreAsync(server, "suncraft"));

        var (_, failed) = await PostAsync(server, "/api/games/suncraft/manual/lore-check", new { text = "kein JSON" });
        Assert.False(failed.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("preview").ValueKind);
        Assert.Equal("noJson", failed.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Saving_creates_the_lore_tab_last_with_unique_item_ids_and_the_merged_content()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server);

        var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/lore-answer", new { text = Fenced(Answer(parts: [1, 2, 3, 4])) });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("replaced").GetBoolean());
        Assert.False(body.GetProperty("dropped").GetBoolean());
        var tab = (await LoreAsync(server, "suncraft"))!;
        Assert.Equal(tab.Id, body.GetProperty("tabId").GetGuid());
        Assert.Equal(LoreTab.SortOrder, tab.SortOrder);
        Assert.Equal(LoreTab.Title, tab.Title);
        var sections = JsonNode.Parse(tab.SectionsJson)!.AsArray();
        var headings = sections.Where(s => s!["type"]!.ToString() == "alert").Select(s => s!["title"]!.ToString()).ToList();
        Assert.Equal(AiGuideGeneratorService.LoreParts(Lang.German).Select(p => p.Title), headings);
        Assert.All(sections, s => Assert.True(s!["spoiler"]!.GetValue<bool>()));
        var rowId = sections.Select(s => s!["table"]?["rows"]?[0]?["itemId"]?.ToString()).Single(i => i is not null);
        Assert.NotEqual("mira-vater", rowId);                                     // "mira-vater" is used by the chapter: made unique
        Assert.Equal(LoreTab.Slug, body.GetProperty("game").GetProperty("tabs").EnumerateArray().Last().GetProperty("slug").GetString());
    }

    [Fact]
    public async Task Saving_replaces_an_existing_lore_tab_in_place_and_its_translation_stops_counting_as_current()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        var loreId = await SeedAsync(server, withLore: true);
        string oldHash;
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var old = await db.GuideTabs.AsNoTracking().FirstAsync(t => t.Id == loreId);
            oldHash = GuideTranslations.HashTab(old);
            db.GuideTabTranslations.Add(new GuideTabTranslationEntity { TabId = loreId, Language = "en", Title = "Story & Lore", SectionsJson = "[]", SourceHash = oldHash });
            await db.SaveChangesAsync();
        }

        var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/lore-answer", new { text = Fenced(Answer(parts: [1, 2, 3, 4])) });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("replaced").GetBoolean());
        Assert.Equal(loreId, body.GetProperty("tabId").GetGuid());
        var tab = (await LoreAsync(server, "suncraft"))!;
        Assert.Equal(loreId, tab.Id);                                              // same tab ...
        Assert.Equal(LoreTab.SortOrder, tab.SortOrder);
        Assert.DoesNotContain("Der alte Reiter", tab.SectionsJson);                // ... new content
        using var check = server.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var row = await checkDb.GuideTabTranslations.AsNoTracking().SingleAsync(t => t.TabId == loreId);
        Assert.NotEqual(GuideTranslations.HashTab(tab), row.SourceHash);          // the stored translation belongs to the old text: stale, not current
        Assert.Equal(1, await checkDb.GuideTabs.CountAsync(t => t.Slug == LoreTab.Slug));
    }

    [Fact]
    public async Task Saving_an_answer_with_errors_is_a_400_with_the_full_list_and_changes_nothing()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        var loreId = await SeedAsync(server, withLore: true);
        var before = (await LoreAsync(server, "suncraft"))!.SectionsJson;

        var (status, body) = await PostAsync(server, "/api/games/suncraft/manual/lore-answer", new { text = Fenced(Answer(appId: 999, parts: [1, 2, 3, 4])) });
        var (_, english) = await PostAsync(server, "/api/games/suncraft/manual/lore-answer", new { text = "kein JSON" }, "en");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Antwort nicht gespeichert", body.GetProperty("title").GetString());
        Assert.Contains("nicht gespeichert", body.GetProperty("detail").GetString());
        Assert.Contains("andere", body.GetProperty("detail").GetString());
        var error = body.GetProperty("errors")[0];
        Assert.Equal("wrongGame", error.GetProperty("code").GetString());
        Assert.Contains("999", error.GetProperty("message").GetString());
        Assert.Contains("not saved", english.GetProperty("detail").GetString());
        Assert.Equal("Answer not saved", english.GetProperty("title").GetString());
        Assert.Equal(before, (await LoreAsync(server, "suncraft"))!.SectionsJson);
        Assert.Equal(loreId, (await LoreAsync(server, "suncraft"))!.Id);
    }

    // ---- API and manual produce the same tab -------------------------------------------------------------------------------------

    private sealed class FakeAi(Func<string, string> answer) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host != "gemini.test")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            var prompt = body["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>();
            var json = new JsonObject { ["candidates"] = new JsonArray(new JsonObject { ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = answer(prompt) }) }, ["finishReason"] = "STOP" }) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static async Task<TestServer> StartWithFakeAiAsync(Func<string, string> answer)
    {
        var server = await TestServer.StartAsync(desktop: null, b =>
        {
            b.Configuration["Companion:Ai:GeminiBaseUrl"] = "http://gemini.test/v1beta";
            b.Services.AddSingleton<IHttpClientFactory>(new FakeFactory(new FakeAi(answer)));
        });
        server.Services.GetRequiredService<SecretStore>().SetSecret(SettingsStore.SecretGemini, "fake-key");
        return server;
    }

    /// <summary>The fake provider's answer to the API prompt of one part: that part's sections as { "sections": [...] }; failing = the given parts answer with no JSON.</summary>
    private static Func<string, string> Provider(params int[] failing) => prompt =>
    {
        var parts = AiGuideGeneratorService.LoreParts(Lang.German);
        for (var i = 0; i < parts.Length; i++)
        {
            if (prompt.Contains($"Teil: „{parts[i].Title}“", StringComparison.Ordinal))
            {
                return failing.Contains(i + 1) ? "Das kann ich leider nicht." : new JsonObject { ["sections"] = Sections(i + 1) }.ToJsonString();
            }
        }
        return "{}";
    };

    private static async Task<JsonElement> RunLoreJobAsync(TestServer server, string slug)
    {
        var (status, job) = await PostAsync(server, $"/api/games/{slug}/ai-lore", new { });
        Assert.Equal(HttpStatusCode.Accepted, status);
        for (var i = 0; i < 200; i++)
        {
            var state = await server.Client.GetFromJsonAsync<JsonElement>($"/api/ai/jobs/{job.GetProperty("id").GetString()}");
            if (!state.GetProperty("running").GetBoolean())
            {
                return state;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("The AI job did not finish.");
    }

    [Fact]
    public async Task The_API_research_and_the_pasted_answer_produce_the_same_tab()
    {
        await using var server = await StartWithFakeAiAsync(Provider());
        await SeedAsync(server, "api-game", appId: null, withChapter: false);
        await SeedAsync(server, "manual-game", appId: null, withChapter: false);

        var done = await RunLoreJobAsync(server, "api-game");
        Assert.True(done.GetProperty("error").ValueKind == JsonValueKind.Null, done.ToString());
        var (status, _) = await PostAsync(server, "/api/games/manual-game/manual/lore-answer", new { text = Fenced(Answer(appId: null, parts: [1, 2, 3, 4])) });
        Assert.Equal(HttpStatusCode.OK, status);

        var api = (await LoreAsync(server, "api-game"))!;
        var manual = (await LoreAsync(server, "manual-game"))!;
        Assert.Equal(api.SectionsJson, manual.SectionsJson);
        Assert.Equal((api.Title, api.Icon, api.SortOrder, api.Slug), (manual.Title, manual.Icon, manual.SortOrder, manual.Slug));
    }

    [Fact]
    public async Task The_API_research_still_keeps_a_failed_part_as_a_danger_alert_between_the_other_parts()
    {
        await using var server = await StartWithFakeAiAsync(Provider(failing: 3));
        await SeedAsync(server, "api-game", appId: null, withChapter: false);

        var done = await RunLoreJobAsync(server, "api-game");

        Assert.True(done.GetProperty("error").ValueKind == JsonValueKind.Null, done.ToString());
        var sections = JsonNode.Parse((await LoreAsync(server, "api-game"))!.SectionsJson)!.AsArray();
        var titles = AiGuideGeneratorService.LoreParts(Lang.German).Select(p => p.Title).ToList();
        // heading, story | heading, cards, table | heading, danger alert | heading, story
        Assert.Equal(["alert", "story", "alert", "cards_grid", "checklist_table", "alert", "alert", "alert", "story"], sections.Select(s => s!["type"]!.ToString()));
        Assert.Equal(titles[2], sections[5]!["title"]!.ToString());
        Assert.Equal("danger", sections[6]!["variant"]!.ToString());
        Assert.Contains(titles[2], sections[6]!["title"]!.ToString());
        Assert.Equal(titles[3], sections[7]!["title"]!.ToString());
        Assert.All(sections, s => Assert.True(s!["spoiler"]!.GetValue<bool>()));
        Assert.False(sections[4]!["table"]!["checkable"]!.GetValue<bool>());
    }
}
