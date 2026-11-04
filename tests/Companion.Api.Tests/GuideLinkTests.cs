using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Companion.Api.Tests;

/// <summary>
/// Cross-chapter links (owner wish 2026-11-03): a walkthrough row / side quest entry points at the DEFINING row of another chapter with "ref" and shows its
/// tick. Covers the linker (matching, merge of ticks, clearing), the normalizer and id rules, removal in both directions, redo, translation, import/export,
/// the API maps and the prompt list. No AI provider, no Steam.
/// </summary>
public sealed class GuideLinkTests
{
    // ---- helpers ---------------------------------------------------------------------------------------------------------------------

    private static string Row(string id, string name, string? reference = null) =>
        "{ \"itemId\": \"" + id + "\"," + (reference is null ? "" : " \"ref\": \"" + reference + "\",") + " \"cells\": { \"name\": \"" + name + "\" } }";

    private static string Table(string tableId, params string[] rows) =>
        $$"""{ "type": "checklist_table", "table": { "tableId": "{{tableId}}", "caption": "{{tableId}}", "checkable": true, "columns": [ { "key": "name", "label": "Name", "isRowHeader": true } ], "rows": [ {{string.Join(", ", rows)}} ] } }""";

    private static string Heading(string title, int level) => $$"""{ "type": "heading", "level": {{level}}, "title": "{{title}}" }""";

    private static string Quests(params (string Title, string? Ref)[] quests) =>
        "{ \"type\": \"alert\", \"kind\": \"side_quests\", \"title\": \"Side quests\", \"quests\": [ "
        + string.Join(", ", quests.Select(q => "{ \"title\": \"" + q.Title + "\"" + (q.Ref is null ? "" : ", \"ref\": \"" + q.Ref + "\"") + " }")) + " ] }";

    private static GuideTabEntity Tab(string slug, string title, string? topicId, params string[] sections) => new()
    {
        Id = Guid.NewGuid(), GameId = Guid.NewGuid(), Slug = slug, Title = title, Icon = "mdi:star", SortOrder = 1, TopicId = topicId,
        SectionsJson = "[" + string.Join(",", sections) + "]"
    };

    private static GuideTabEntity Walkthrough(params string[] sections) => Tab(WalkthroughTab.Slug, "Walkthrough", WalkthroughTab.TopicId, sections);

    private static IReadOnlySet<string> Ticks(params string[] keys) => new HashSet<string>(keys, StringComparer.Ordinal);

    /// <summary>The refs of a chapter's rows after a reconcile (itemId -> ref or null).</summary>
    private static Dictionary<string, string?> RowRefs(LinkReconciliation result, GuideTabEntity tab)
    {
        var json = result.ChangedSections.TryGetValue(tab.Id, out var changed) ? changed : tab.SectionsJson;
        return ApiEndpoints.DeserializeSections(json).Where(s => s.Table is not null).SelectMany(s => s.Table!.Rows).ToDictionary(r => r.ItemId, r => r.Ref);
    }

    private static List<string?> QuestRefs(LinkReconciliation result, GuideTabEntity tab)
    {
        var json = result.ChangedSections.TryGetValue(tab.Id, out var changed) ? changed : tab.SectionsJson;
        return ApiEndpoints.DeserializeSections(json).SelectMany(s => s.Quests ?? []).Select(q => q.Ref).ToList();
    }

    /// <summary>Writes a reconcile result into the tabs (like GuideLinkMaintenance does) so a second pass can be run on the new state.</summary>
    private static void Apply(LinkReconciliation result, params GuideTabEntity[] tabs)
    {
        foreach (var tab in tabs.Where(t => result.ChangedSections.ContainsKey(t.Id)))
        {
            tab.SectionsJson = result.ChangedSections[tab.Id];
        }
    }

    // ---- names -------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Ifrit (Esper)", "ifrit")]
    [InlineData("  ÄTHER-Schlüssel!  ", "ather schlussel")]
    [InlineData("Straße", "strasse")]
    [InlineData("Café  [DLC]", "cafe")]
    [InlineData("(Esper)", "esper")]
    [InlineData("", "")]
    public void Names_are_lowercase_without_diacritics_punctuation_and_bracket_notes(string name, string expected) =>
        Assert.Equal(expected, GuideLinker.NormalizeName(name));

    [Fact]
    public void Kind_words_and_articles_in_front_of_a_name_are_stripped_one_by_one_but_never_down_to_nothing()
    {
        Assert.Equal(["quest the lost child", "the lost child", "lost child"], GuideLinker.NameKeys("Quest: The Lost Child"));
        Assert.Equal(["esper ifrit", "ifrit"], GuideLinker.NameKeys("Esper Ifrit"));
        Assert.Equal(["ifrit"], GuideLinker.NameKeys("Ifrit"));
        Assert.Equal(["the quest", "quest"], GuideLinker.NameKeys("The Quest"));
        Assert.Equal(["quest"], GuideLinker.NameKeys("Quest"));
        Assert.Empty(GuideLinker.NameKeys("x"));
    }

    // ---- linking -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_walkthrough_row_is_linked_to_the_one_row_of_another_chapter_with_the_same_name_and_keeps_its_own_item_id()
    {
        var espers = Tab("espers", "Espers", "espers", Table("espers", Row("espers:ifrit", "Esper Ifrit"), Row("espers:shiva", "Esper Shiva")));
        var walk = Walkthrough(Heading("Chapter 3", 1), Heading("Cave", 2), Table("cave", Row("walk:ifrit", "Ifrit (Esper)"), Row("walk:chest", "Chest of Gold")));

        var result = GuideLinker.Reconcile([espers, walk], Ticks());

        var refs = RowRefs(result, walk);
        Assert.Equal("espers:ifrit", refs["walk:ifrit"]);
        Assert.Null(refs["walk:chest"]);               // nothing matches: stays a normal row
        Assert.DoesNotContain(espers.Id, result.ChangedSections.Keys);
        Assert.Equal(1, result.Linked);
    }

    [Fact]
    public void Kind_words_decide_nothing_when_a_full_name_matches_and_help_when_it_does_not()
    {
        var chapter = Tab("quests", "Quests", "quests", Table("q", Row("quests:lost-child", "The Lost Child"), Row("quests:boss-ifrit", "Ifrit"), Row("quests:esper-ifrit", "Esper Ifrit")));
        var walk = Walkthrough(Table("w",
            Row("w:1", "Quest: Lost Child"),     // loose: "lost child" == "the lost child" stripped
            Row("w:2", "Esper Ifrit"),           // exact: the one full match wins, the loose "ifrit" is not asked
            Row("w:3", "Ifrit")));               // exact: "ifrit"

        var refs = RowRefs(GuideLinker.Reconcile([chapter, walk], Ticks()), walk);

        Assert.Equal("quests:lost-child", refs["w:1"]);
        Assert.Equal("quests:esper-ifrit", refs["w:2"]);
        Assert.Equal("quests:boss-ifrit", refs["w:3"]);
    }

    [Fact]
    public void An_ambiguous_name_is_never_guessed()
    {
        var a = Tab("a", "A", null, Table("a", Row("a:ifrit", "Ifrit")));
        var b = Tab("b", "B", null, Table("b", Row("b:ifrit", "Ifrit")));
        var inside = Tab("c", "C", null, Table("c", Row("c:1", "Shiva"), Row("c:2", "Shiva")));
        var walk = Walkthrough(Table("w", Row("w:1", "Ifrit"), Row("w:2", "Shiva"), Row("w:3", "Esper Ifrit")), Quests(("Ifrit", null)));

        var result = GuideLinker.Reconcile([a, b, inside, walk], Ticks());

        Assert.All(RowRefs(result, walk).Values, Assert.Null);
        Assert.Equal([null], QuestRefs(result, walk));
        Assert.Equal(0, result.Linked);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Side_quest_entries_are_linked_by_title_and_keep_a_ref_the_model_set()
    {
        var quests = Tab("quests", "Quests", "quests", Table("q", Row("quests:lost-child", "The Lost Child"), Row("quests:wolf", "Wolf Hunt")));
        var walk = Walkthrough(Quests(("Lost Child", null), ("Wolf Hunt", "quests:wolf"), ("Unknown Quest", null)));

        var result = GuideLinker.Reconcile([quests, walk], Ticks());

        Assert.Equal(["quests:lost-child", "quests:wolf", null], QuestRefs(result, walk));
    }

    [Fact]
    public void Only_the_walkthrough_is_linked_by_name_and_never_to_itself_the_achievement_list_or_a_ref_row()
    {
        var weapons = Tab("weapons", "Weapons", "weapons", Table("w", Row("weapons:sword", "Sword")));
        var other = Tab("other", "Other", null, Table("o", Row("other:sword", "Sword of Doom")));       // a normal chapter is not touched
        var achievements = Tab("steam-achievements", "Erfolge", null, Table("steam-achievements", Row("ACH_SWORD", "Master Sword")));
        var walk = Walkthrough(Table("w", Row("walk:sword", "Sword"), Row("walk:master", "Master Sword")));

        var result = GuideLinker.Reconcile([weapons, other, achievements, walk], Ticks());

        var refs = RowRefs(result, walk);
        Assert.Equal("weapons:sword", refs["walk:sword"]);
        Assert.Null(refs["walk:master"]);                          // the achievement list is no target
        Assert.DoesNotContain(other.Id, result.ChangedSections.Keys);
    }

    [Fact]
    public void A_non_checkable_table_is_not_a_target_and_a_puzzle_with_an_item_id_is()
    {
        var info = Tab("info", "Info", null, """{ "type": "checklist_table", "table": { "tableId": "i", "caption": "i", "checkable": false, "columns": [ { "key": "name", "label": "Name" } ], "rows": [ { "itemId": "info:gate", "cells": { "name": "Old Gate" } } ] } }""");
        var puzzles = Tab("puzzles", "Puzzles", "puzzles", """{ "type": "puzzle", "title": "Old Gate", "puzzle": { "itemId": "puzzles:gate", "steps": ["Pull"], "documented": true } }""");
        var walk = Walkthrough(Table("w", Row("w:gate", "Old Gate")));

        Assert.Equal("puzzles:gate", RowRefs(GuideLinker.Reconcile([info, puzzles, walk], Ticks()), walk)["w:gate"]);
    }

    // ---- ticks -------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, false, true)]      // own tick only -> the target gets it
    [InlineData(true, true, false)]      // both ticked -> nothing to add
    [InlineData(false, true, false)]     // only the target -> it stays ticked
    [InlineData(false, false, false)]
    public void Linking_a_row_that_was_ticked_merges_the_ticks_so_nothing_ticked_is_lost(bool ownTicked, bool targetTicked, bool targetIsAdded)
    {
        var espers = Tab("espers", "Espers", "espers", Table("espers", Row("espers:ifrit", "Ifrit")));
        var walk = Walkthrough(Table("w", Row("walk:ifrit", "Ifrit")));
        var ticks = new List<string>();
        if (ownTicked) ticks.Add("walk:ifrit");
        if (targetTicked) ticks.Add("espers:ifrit");

        var result = GuideLinker.Reconcile([espers, walk], Ticks([.. ticks]));

        Assert.Equal(targetIsAdded, result.AddChecks.Contains("espers:ifrit"));
        Assert.Equal(ownTicked, result.RemoveChecks.Contains("walk:ifrit"));
        Assert.Equal(ownTicked || targetTicked, ticks.Concat(result.AddChecks).Except(result.RemoveChecks).Contains("espers:ifrit"));
    }

    [Fact]
    public void A_ref_to_a_row_that_is_gone_is_cleared_and_the_row_keeps_the_tick_its_target_had()
    {
        var walk = Walkthrough(Table("w", Row("walk:ifrit", "Ifrit", "espers:ifrit"), Row("walk:shiva", "Shiva", "espers:shiva")), Quests(("Wolf", "quests:wolf")));

        var result = GuideLinker.Reconcile([walk], Ticks("espers:ifrit"));

        var refs = RowRefs(result, walk);
        Assert.Null(refs["walk:ifrit"]);
        Assert.Null(refs["walk:shiva"]);
        Assert.Equal([null], QuestRefs(result, walk));
        Assert.Equal(["walk:ifrit"], result.AddChecks.Order());        // espers:ifrit was ticked -> the row is ticked on its own now
        Assert.Empty(result.RemoveChecks);
        Assert.Equal(3, result.Cleared);
    }

    [Fact]
    public void A_cleared_row_that_finds_a_new_match_by_name_is_linked_again_with_its_tick_moved_to_the_new_target()
    {
        var quests = Tab("quests", "Quests", "quests", Table("q", Row("quests:ifrit", "Ifrit")));
        var walk = Walkthrough(Table("w", Row("walk:ifrit", "Ifrit", "espers:ifrit")));

        var result = GuideLinker.Reconcile([quests, walk], Ticks("espers:ifrit"));

        Assert.Equal("quests:ifrit", RowRefs(result, walk)["walk:ifrit"]);
        Assert.Equal(["quests:ifrit"], result.AddChecks.Order());
        Assert.Empty(result.RemoveChecks);            // the walkthrough row never got a tick of its own
    }

    [Fact]
    public void A_ref_to_a_ref_row_to_the_same_chapter_or_to_itself_is_cleared()
    {
        var a = Tab("a", "A", null, Table("a", Row("a:1", "One"), Row("a:2", "Two", "a:1"), Row("a:3", "Three", "b:ref")));
        var b = Tab("b", "B", null, Table("b", Row("b:ref", "Ref row", "a:1")));

        var result = GuideLinker.Reconcile([a, b], Ticks());

        var refs = RowRefs(result, a);
        Assert.Null(refs["a:2"]);                       // same chapter
        Assert.Null(refs["a:3"]);                       // b:ref is itself a ref row
        Assert.Equal("a:1", RowRefs(result, b)["b:ref"]);
    }

    [Fact]
    public void Running_the_linker_again_on_its_own_result_changes_nothing()
    {
        var espers = Tab("espers", "Espers", "espers", Table("espers", Row("espers:ifrit", "Ifrit")));
        var walk = Walkthrough(Table("w", Row("walk:ifrit", "Ifrit"), Row("walk:x", "Gone", "nowhere")), Quests(("Ifrit", null)));
        var first = GuideLinker.Reconcile([espers, walk], Ticks("walk:ifrit"));
        Apply(first, espers, walk);

        var second = GuideLinker.Reconcile([espers, walk], Ticks("espers:ifrit"));

        Assert.True(second.IsEmpty);
    }

    [Fact]
    public void Many_walkthrough_rows_with_the_same_name_are_never_linked_to_one_target_and_keep_their_own_ticks()
    {
        // Review finding: five "Elixir (...)" rows all became refs to one row and their own ticks were merged away
        var items = Tab("items", "Items", "items", Table("items", Row("items:elixir", "Elixir")));
        var walk = Walkthrough(Table("w",
            Row("w:1", "Elixir (Höhle)"), Row("w:2", "Elixir (Turm)"), Row("w:3", "Elixir (Brücke)"), Row("w:4", "Elixir"), Row("w:5", "Elixir [Truhe]")));

        var result = GuideLinker.Reconcile([items, walk], Ticks("w:1", "w:3"));

        Assert.All(RowRefs(result, walk).Values, Assert.Null);
        Assert.Empty(result.AddChecks);
        Assert.Empty(result.RemoveChecks);
        Assert.Equal(0, result.Linked);
    }

    [Fact]
    public void A_bracket_note_that_is_the_only_difference_between_targets_decides_the_link()
    {
        var items = Tab("items", "Items", "items", Table("items", Row("items:elixir-cave", "Elixir (Höhle)"), Row("items:elixir-tower", "Elixir (Turm)")));
        var walk = Walkthrough(Table("w", Row("w:cave", "Elixir (Höhle)"), Row("w:tower", "Elixir (Turm)"), Row("w:plain", "Elixir")));

        var refs = RowRefs(GuideLinker.Reconcile([items, walk], Ticks()), walk);

        Assert.Equal("items:elixir-cave", refs["w:cave"]);
        Assert.Equal("items:elixir-tower", refs["w:tower"]);
        Assert.Null(refs["w:plain"]);                   // "Elixir" names two targets: no guess
    }

    [Fact]
    public void A_new_row_never_joins_a_target_that_a_walkthrough_row_already_points_at()
    {
        var espers = Tab("espers", "Espers", "espers", Table("espers", Row("espers:ifrit", "Ifrit")));
        var walk = Walkthrough(Table("w", Row("w:linked", "Esper Ifrit", "espers:ifrit"), Row("w:new", "Ifrit (Boss)")));

        var result = GuideLinker.Reconcile([espers, walk], Ticks("w:new"));

        Assert.Null(RowRefs(result, walk)["w:new"]);
        Assert.Empty(result.RemoveChecks);              // its own tick stays where it is
    }

    [Fact]
    public void A_side_quest_entry_that_repeats_a_rows_name_is_the_same_thing_and_both_are_linked()
    {
        var quests = Tab("quests", "Quests", "quests", Table("q", Row("quests:wolf", "Wolf Hunt")));
        var walk = Walkthrough(Table("w", Row("w:wolf", "Wolf Hunt")), Quests(("Wolf Hunt", null)), Quests(("Wolf Hunt", null)));

        var result = GuideLinker.Reconcile([quests, walk], Ticks());

        Assert.Equal("quests:wolf", RowRefs(result, walk)["w:wolf"]);
        Assert.Equal(["quests:wolf", "quests:wolf"], QuestRefs(result, walk));
    }

    [Fact]
    public void A_ref_chain_is_resolved_to_the_final_definer_before_anything_is_released()
    {
        // W (walkthrough) -> T (quests, itself a ref row) -> U (espers): W ends at U and keeps showing a tick; T keeps its ref
        var espers = Tab("espers", "Espers", "espers", Table("espers", Row("espers:ifrit", "Ifrit")));
        var quests = Tab("quests", "Quests", "quests", Table("q", Row("quests:ifrit", "Ifrit quest", "espers:ifrit")));
        var walk = Walkthrough(Table("w", Row("w:ifrit", "Something", "quests:ifrit")), Quests(("Other", "quests:ifrit")));

        var result = GuideLinker.Reconcile([espers, quests, walk], Ticks("espers:ifrit"));

        Assert.Equal("espers:ifrit", RowRefs(result, walk)["w:ifrit"]);
        Assert.Equal(["espers:ifrit"], QuestRefs(result, walk));
        Assert.Equal("espers:ifrit", RowRefs(result, quests)["quests:ifrit"]);
        Assert.Equal(0, result.Cleared);
        Assert.True(result.AddChecks.Count == 0 && result.RemoveChecks.Count == 0);
    }

    [Fact]
    public void A_chain_whose_end_is_gone_releases_the_middle_row_and_the_first_row_points_at_it()
    {
        var quests = Tab("quests", "Quests", "quests", Table("q", Row("quests:ifrit", "Ifrit quest", "espers:gone")));
        var walk = Walkthrough(Table("w", Row("w:ifrit", "Something", "quests:ifrit")));

        var result = GuideLinker.Reconcile([quests, walk], Ticks("espers:gone"));

        Assert.Null(RowRefs(result, quests)["quests:ifrit"]);
        Assert.Equal("quests:ifrit", RowRefs(result, walk)["w:ifrit"]);
        Assert.Equal(["quests:ifrit"], result.AddChecks.Order());     // the released row keeps the tick it showed
    }

    // ---- ids, counts, normalizer -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Rows_with_a_ref_are_not_definers_not_counted_and_their_ref_is_never_renamed_by_the_unique_id_rule()
    {
        var espers = Tab("espers", "Espers", "espers", Table("espers", Row("espers:ifrit", "Ifrit")));
        var walk = Walkthrough(Table("w", Row("espers:ifrit", "Ifrit", "espers:ifrit"), Row("w:2", "Chest")));       // its own id collides on purpose

        Assert.Equal(["espers:ifrit", "w:2"], ApiEndpoints.CollectItemIds([espers, walk]).Order());
        Assert.Equal(["w:2"], ApiEndpoints.CollectItemIds([walk]).Order());
        Assert.Equal(["espers:ifrit", "w:2"], ApiEndpoints.CollectTakenItemIds([walk]).Order());
        Assert.Equal(["w:2"], ApiEndpoints.CheckableItemIds(ApiEndpoints.DeserializeSections(walk.SectionsJson)).ToList());

        ApiEndpoints.MakeItemIdsUnique([walk], ApiEndpoints.CollectTakenItemIds([espers]));

        var rows = ApiEndpoints.DeserializeSections(walk.SectionsJson)[0].Table!.Rows;
        Assert.Equal("walkthrough:espers:ifrit", rows[0].ItemId);    // the row's own identity is made unique ...
        Assert.Equal("espers:ifrit", rows[0].Ref);                    // ... the ref still names the defining row
    }

    [Fact]
    public void The_normalizer_keeps_a_plain_ref_and_drops_everything_else_instead_of_breaking_the_chapter()
    {
        using var doc = JsonDocument.Parse("""
            [ { "type": "checklist_table", "table": { "tableId": "t", "columns": [ { "key": "name", "label": "Name" } ], "rows": [
                { "itemId": "a", "ref": "  espers:ifrit  ", "cells": { "name": "A" } },
                { "itemId": "b", "ref": 42, "cells": { "name": "B" } },
                { "itemId": "c", "ref": { "id": "x" }, "cells": { "name": "C" } },
                { "itemId": "d", "ref": "d", "cells": { "name": "D" } },
                { "itemId": "e", "ref": "line\nbreak", "cells": { "name": "E" } },
                { "itemId": "f", "ref": null, "cells": { "name": "F" } } ] } },
              { "type": "alert", "kind": "side_quests", "title": "Q", "quests": [ { "title": "One", "ref": "quests:one" }, { "title": "Two", "ref": ["x"] }, "Three" ] } ]
            """);
        using var _ = Lang.Use(Lang.German);

        var sections = GuideSectionNormalizer.Normalize(doc.RootElement);

        var dto = JsonSerializer.Deserialize<List<GuideSectionDto>>(sections.ToJsonString(), ApiEndpoints.JsonOptions)!;
        Assert.Equal(["espers:ifrit", "42", null, null, null, null], dto[0].Table!.Rows.Select(r => r.Ref));
        Assert.Equal(["quests:one", null, null], dto[1].Quests!.Select(q => q.Ref));
    }

    // ---- removal and redo (real database) -----------------------------------------------------------------------------------------------

    private sealed record Seed(string Slug, Guid Espers, Guid Walkthrough);

    /// <summary>A game with an espers chapter and a walkthrough whose row "Ifrit" points at it; ticks as given.</summary>
    private static async Task<Seed> SeedAsync(TestServer server, params string[] ticks)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var game = new GameEntity { Id = Guid.NewGuid(), Slug = "ffx", Title = "FFX", ContentLanguage = Lang.English };
        var espers = Tab("tab-1-espers", "Espers", "espers", Table("espers", Row("espers:ifrit", "Esper Ifrit"), Row("espers:shiva", "Esper Shiva")));
        var walk = Walkthrough(
            Heading("Part 1: Besaid", 1), Heading("Temple", 2),
            Table("temple", Row("walk:ifrit", "Esper Ifrit", "espers:ifrit"), Row("walk:chest", "Chest")),
            Quests(("Esper Shiva", "espers:shiva")));
        espers.GameId = walk.GameId = game.Id;
        espers.SortOrder = 2;
        game.Tabs = [espers, walk];
        game.ProgressChecks = ticks.Select(k => new ProgressCheckEntity { Id = Guid.NewGuid(), GameId = game.Id, ItemKey = k, CheckedAt = DateTimeOffset.UtcNow }).ToList();
        db.Games.Add(game);
        await db.SaveChangesAsync();
        return new Seed(game.Slug, espers.Id, walk.Id);
    }

    private static async Task<(Dictionary<string, string?> Refs, List<string> Ticks, List<string?> QuestRefs)> StateAsync(TestServer server)
    {
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var tabs = await db.GuideTabs.AsNoTracking().ToListAsync();
        var sections = tabs.SelectMany(t => ApiEndpoints.DeserializeSections(t.SectionsJson)).ToList();
        var refs = sections.Where(s => s.Table is not null).SelectMany(s => s.Table!.Rows).ToDictionary(r => r.ItemId, r => r.Ref);
        var quests = sections.SelectMany(s => s.Quests ?? []).Select(q => q.Ref).ToList();
        return (refs, await db.ProgressChecks.AsNoTracking().Select(p => p.ItemKey).OrderBy(k => k).ToListAsync(), quests);
    }

    private static async Task<HttpResponseMessage> DeleteChapterAsync(TestServer server, string slug, Guid tab)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/games/{slug}/chapters/{tab}");
        request.Headers.Add("X-Requested-With", "PlatinVault");
        return await server.Client.SendAsync(request);
    }

    [Fact]
    public async Task A_tick_changed_in_parallel_never_fails_the_relink_and_the_wanted_state_is_stored_once()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        var seed = await SeedAsync(server, "walk:ifrit");
        using (var setup = server.Services.CreateScope())
        {
            // The walkthrough row is not linked yet and has its own tick
            var db = setup.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var walk = await db.GuideTabs.FirstAsync(t => t.Id == seed.Walkthrough);
            walk.SectionsJson = walk.SectionsJson.Replace("\"ref\":\"espers:ifrit\",", "").Replace("\"ref\": \"espers:ifrit\",", "");
            await db.SaveChangesAsync();
        }

        using var scopeA = server.Services.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<CompanionDbContext>();
        var game = await dbA.Games.Include(g => g.Tabs).Include(g => g.ProgressChecks).FirstAsync(g => g.Slug == seed.Slug);
        Assert.True(GuideLinkMaintenance.Apply(dbA, game));        // wants: add espers:ifrit, remove walk:ifrit

        // Meanwhile a second window (or the second dev server) does exactly the same tick change
        using (var scopeB = server.Services.CreateScope())
        {
            var dbB = scopeB.ServiceProvider.GetRequiredService<CompanionDbContext>();
            dbB.ProgressChecks.Add(new ProgressCheckEntity { Id = Guid.NewGuid(), GameId = game.Id, ItemKey = "espers:ifrit", CheckedAt = DateTimeOffset.UtcNow });
            dbB.ProgressChecks.RemoveRange(dbB.ProgressChecks.Where(p => p.ItemKey == "walk:ifrit"));
            await dbB.SaveChangesAsync();
        }

        await GuideLinkMaintenance.SaveAsync(dbA, CancellationToken.None);

        var (refs, ticks, _) = await StateAsync(server);
        Assert.Equal("espers:ifrit", refs["walk:ifrit"]);          // the link itself is saved
        Assert.Equal(["espers:ifrit"], ticks);                      // once, and the own tick is gone
    }

    [Fact]
    public async Task Ticking_the_own_id_of_a_ref_row_ticks_the_row_it_points_at()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        var seed = await SeedAsync(server);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/games/{seed.Slug}/progress") { Content = JsonContent.Create(new { itemKey = "walk:ifrit", @checked = true }) };
        request.Headers.Add("X-Requested-With", "PlatinVault");
        var response = await server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["espers:ifrit"], (await StateAsync(server)).Ticks);   // no hidden tick under the ref row's own id

        // A normal row's own id stays as it is
        var chest = new HttpRequestMessage(HttpMethod.Post, $"/api/games/{seed.Slug}/progress") { Content = JsonContent.Create(new { itemKey = "walk:chest", @checked = true }) };
        chest.Headers.Add("X-Requested-With", "PlatinVault");
        await server.Client.SendAsync(chest);
        Assert.Equal(["espers:ifrit", "walk:chest"], (await StateAsync(server)).Ticks);
    }

    [Fact]
    public async Task Removing_the_defining_chapter_turns_the_ref_rows_into_normal_rows_that_keep_the_tick()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        var seed = await SeedAsync(server, "espers:ifrit");

        var response = await DeleteChapterAsync(server, seed.Slug, seed.Espers);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (refs, ticks, quests) = await StateAsync(server);
        Assert.Null(refs["walk:ifrit"]);
        Assert.Equal([null], quests);
        Assert.Equal(["walk:ifrit"], ticks);              // espers:ifrit went with its chapter, the walkthrough row took the tick over
    }

    [Fact]
    public async Task Removing_the_referencing_chapter_keeps_the_defining_row_and_its_tick()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        var seed = await SeedAsync(server, "espers:ifrit", "walk:chest");

        var response = await DeleteChapterAsync(server, seed.Slug, seed.Walkthrough);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (refs, ticks, _) = await StateAsync(server);
        Assert.Equal(["espers:ifrit", "espers:shiva"], refs.Keys.Order());
        Assert.Equal(["espers:ifrit"], ticks);              // the walkthrough's own tick went with it, the defining row keeps its tick
    }

    [Fact]
    public async Task Researching_the_defining_chapter_again_keeps_refs_to_ids_it_reuses_and_frees_the_others_with_their_tick()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        var seed = await SeedAsync(server, "espers:ifrit", "espers:shiva");

        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var game = await db.Games.Include(g => g.Tabs).Include(g => g.ProgressChecks).FirstAsync(g => g.Slug == seed.Slug);
            var existing = game.Tabs.First(t => t.Id == seed.Espers);
            // The new version keeps espers:ifrit and drops espers:shiva
            var fresh = Tab("tab-1-espers", "Espers", "espers", Table("espers", Row("espers:ifrit", "Esper Ifrit"), Row("espers:odin", "Esper Odin")));
            fresh.GameId = game.Id;
            AiActionEndpoints.ReplaceChapterInPlace(db, game, existing, fresh);
            await db.SaveChangesAsync();
        }

        var (refs, ticks, quests) = await StateAsync(server);
        Assert.Equal("espers:ifrit", refs["walk:ifrit"]);   // still valid
        Assert.Equal([null], quests);                          // the quest entry pointed at the vanished espers:shiva
        Assert.Equal(["espers:ifrit"], ticks);                // shiva's tick: the quest entry had none of its own to take over
    }

    [Fact]
    public async Task Researching_the_defining_chapter_again_lets_a_freed_row_link_to_a_new_row_of_the_same_name()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        var seed = await SeedAsync(server, "espers:ifrit");

        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var game = await db.Games.Include(g => g.Tabs).Include(g => g.ProgressChecks).FirstAsync(g => g.Slug == seed.Slug);
            var existing = game.Tabs.First(t => t.Id == seed.Espers);
            // The model renamed the id of the same esper
            var fresh = Tab("tab-1-espers", "Espers", "espers", Table("espers", Row("summons:ifrit", "Esper Ifrit"), Row("espers:shiva", "Esper Shiva")));
            fresh.GameId = game.Id;
            AiActionEndpoints.ReplaceChapterInPlace(db, game, existing, fresh);
            await db.SaveChangesAsync();
        }

        var (refs, ticks, _) = await StateAsync(server);
        Assert.Equal("summons:ifrit", refs["walk:ifrit"]);
        Assert.Equal(["summons:ifrit"], ticks);              // the tick followed the link
    }

    [Fact]
    public async Task The_game_detail_carries_ref_item_links_and_ref_targets_and_does_not_count_ref_rows()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        await SeedAsync(server, "espers:ifrit");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/games/ffx");
        request.Headers.Add("Accept-Language", "en");
        var detail = await (await server.Client.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();

        var walk = detail.GetProperty("tabs").EnumerateArray().First(t => t.GetProperty("slug").GetString() == "walkthrough");
        var row = walk.GetProperty("sections")[2].GetProperty("table").GetProperty("rows")[0];
        Assert.Equal("espers:ifrit", row.GetProperty("ref").GetString());
        Assert.Equal(1, walk.GetProperty("totalCheckableItems").GetInt32());           // only "Chest"; the ref row counts at the espers chapter
        Assert.Equal(3, detail.GetProperty("totalCheckableItems").GetInt32());         // chest + 2 espers
        Assert.Equal(1, detail.GetProperty("checkedItemsCount").GetInt32());

        var links = detail.GetProperty("itemLinks");
        var ifrit = Assert.Single(links.GetProperty("espers:ifrit").EnumerateArray());
        Assert.Equal("row", ifrit.GetProperty("kind").GetString());
        Assert.Equal("walkthrough", ifrit.GetProperty("tabSlug").GetString());
        Assert.Equal("Walkthrough", ifrit.GetProperty("tabTitle").GetString());
        Assert.Equal(2, ifrit.GetProperty("sectionIndex").GetInt32());
        Assert.Equal("walk:ifrit", ifrit.GetProperty("rowItemId").GetString());
        Assert.Equal("Part 1: Besaid · Temple", ifrit.GetProperty("path").GetString());
        var shiva = Assert.Single(links.GetProperty("espers:shiva").EnumerateArray());
        Assert.Equal("quest", shiva.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, shiva.GetProperty("rowItemId").ValueKind);
        Assert.Equal(3, shiva.GetProperty("sectionIndex").GetInt32());

        var target = detail.GetProperty("refTargets").GetProperty("espers:ifrit");
        Assert.Equal("tab-1-espers", target.GetProperty("tabSlug").GetString());
        Assert.Equal("Espers", target.GetProperty("tabTitle").GetString());
        Assert.Equal(0, target.GetProperty("sectionIndex").GetInt32());
        Assert.Equal("Esper Ifrit", target.GetProperty("name").GetString());
        Assert.Equal(2, detail.GetProperty("refTargets").EnumerateObject().Count());
    }

    [Fact]
    public async Task A_game_without_links_has_empty_maps()
    {
        await using var server = await TestServer.StartAsync(desktop: null);
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var game = new GameEntity { Id = Guid.NewGuid(), Slug = "plain", Title = "Plain", ContentLanguage = Lang.English };
            game.Tabs = [Tab("tab-1-x", "X", null, Table("x", Row("x:1", "One")))];
            game.Tabs[0].GameId = game.Id;
            db.Games.Add(game);
            await db.SaveChangesAsync();
        }

        var detail = await server.Client.GetFromJsonAsync<JsonElement>("/api/games/plain");

        Assert.Empty(detail.GetProperty("itemLinks").EnumerateObject());
        Assert.Empty(detail.GetProperty("refTargets").EnumerateObject());
    }

    // ---- a new chapter through the real job (fake provider) -------------------------------------------------------------------------------

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
            var json = new JsonObject { ["candidates"] = new JsonArray(new JsonObject { ["content"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = answer(prompt) }) }, ["finishReason"] = "STOP" }) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static async Task<JsonElement> ExpandAsync(TestServer server, string title)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/games/ffx/ai-expand-tab") { Content = JsonContent.Create(new { tabTitle = title, topicPrompt = "Everything about " + title }) };
        request.Headers.Add("X-Requested-With", "PlatinVault");
        request.Headers.Add("Accept-Language", "en");
        var response = await server.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<JsonElement>();
        for (var i = 0; i < 200; i++)
        {
            var state = await server.Client.GetFromJsonAsync<JsonElement>($"/api/ai/jobs/{job.GetProperty("id").GetString()}");
            if (!state.GetProperty("running").GetBoolean())
            {
                Assert.True(state.GetProperty("error").ValueKind == JsonValueKind.Null, state.ToString());
                return state;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException("The AI job did not finish.");
    }

    [Fact]
    public async Task A_new_chapter_gets_the_walkthrough_rows_linked_with_their_tick_and_the_next_prompt_lists_its_rows()
    {
        const string Espers = """
            { "title": "Espers", "icon": "mdi:star", "sections": [ { "type": "checklist_table", "table": { "tableId": "espers", "caption": "Espers", "checkable": true,
              "columns": [ { "key": "name", "label": "Name", "isRowHeader": true } ],
              "rows": [ { "itemId": "esper-ifrit", "cells": { "name": "Esper Ifrit" } }, { "itemId": "esper-shiva", "cells": { "name": "Esper Shiva" } } ] } } ] }
            """;
        var ai = new FakeAi(_ => Espers);
        await using var server = await TestServer.StartAsync(desktop: null, b =>
        {
            b.Configuration["Companion:Ai:GeminiBaseUrl"] = "http://gemini.test/v1beta";
            b.Services.AddSingleton<IHttpClientFactory>(new FakeFactory(ai));
        });
        server.Services.GetRequiredService<Companion.Api.Settings.SecretStore>().SetSecret(Companion.Api.Settings.SettingsStore.SecretGemini, "fake-key");

        // Only the walkthrough exists; "Esper Ifrit" is ticked on its own row and the quest entry has no ref yet
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompanionDbContext>();
            var game = new GameEntity { Id = Guid.NewGuid(), Slug = "ffx", Title = "FFX", ContentLanguage = Lang.English };
            var walk = Walkthrough(Heading("Part 1", 1), Table("temple", Row("walk:ifrit", "Esper Ifrit"), Row("walk:chest", "Chest")), Quests(("Esper Shiva", null)));
            walk.GameId = game.Id;
            game.Tabs = [walk];
            game.ProgressChecks = [new ProgressCheckEntity { Id = Guid.NewGuid(), GameId = game.Id, ItemKey = "walk:ifrit", CheckedAt = DateTimeOffset.UtcNow }];
            db.Games.Add(game);
            await db.SaveChangesAsync();
        }

        await ExpandAsync(server, "Espers");

        var (refs, ticks, quests) = await StateAsync(server);
        Assert.Equal("esper-ifrit", refs["walk:ifrit"]);
        Assert.Null(refs["walk:chest"]);
        Assert.Equal(["esper-shiva"], quests);
        Assert.Equal(["esper-ifrit"], ticks);                     // the walkthrough row's own tick moved to the espers row
        Assert.DoesNotContain("esper-ifrit |", ai.Prompts[0]);     // nothing to point at yet when the first chapter was written

        await ExpandAsync(server, "Bosses");
        Assert.Contains("## Espers\nesper-ifrit | Esper Ifrit\nesper-shiva | Esper Shiva", ai.Prompts[1]);
    }

    // ---- ChapterRemoval (pure) ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Ref_rows_own_no_tick_so_removing_their_chapter_orphans_nothing_and_the_defining_chapter_orphans_its_ids()
    {
        var espers = Tab("espers", "Espers", "espers", Table("espers", Row("espers:ifrit", "Ifrit")));
        var walk = Walkthrough(Table("w", Row("walk:ifrit", "Ifrit", "espers:ifrit"), Row("walk:chest", "Chest")));

        Assert.Equal(["walk:chest"], ChapterRemoval.OrphanedCheckKeys(walk, [espers]).Order());
        Assert.Equal(["espers:ifrit"], ChapterRemoval.OrphanedCheckKeys(espers, [walk]).Order());
    }

    // ---- translation, import, export ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_translation_never_touches_a_ref_and_a_changed_ref_is_rejected()
    {
        // A stored chapter has been through the normalizer already
        using var raw = JsonDocument.Parse("[" + Table("w", Row("walk:ifrit", "Ifrit", "espers:ifrit")) + "," + Quests(("Lost Child", "quests:lost-child")) + "]");
        using var language = Lang.Use(Lang.German);
        var original = GuideSectionNormalizer.Normalize(raw.RootElement, NormalizeOptions.Trusted);

        var slots = GuideTextExtractor.Collect(original);
        Assert.DoesNotContain(slots, s => s.Text.Contains(':'));          // "espers:ifrit" / "quests:lost-child" are ids, not text

        var translated = (JsonArray)original.DeepClone();
        translated[0]!["table"]!["rows"]![0]!["cells"]!["name"] = "Ifrit (übersetzt)";
        translated[1]!["quests"]![0]!["title"] = "Verlorenes Kind";
        var ok = GuideTextExtractor.ValidateTranslated(original, translated, "Walkthrough");
        Assert.Equal("espers:ifrit", ok[0]!["table"]!["rows"]![0]!["ref"]!.GetValue<string>());
        Assert.Equal("quests:lost-child", ok[1]!["quests"]![0]!["ref"]!.GetValue<string>());

        var tampered = (JsonArray)translated.DeepClone();
        tampered[0]!["table"]!["rows"]![0]!["ref"] = "espers:shiva";
        Assert.ThrowsAny<Exception>(() => GuideTextExtractor.ValidateTranslated(original, tampered, "Walkthrough"));
    }

    [Fact]
    public void Export_and_import_keep_the_ref_of_rows_and_side_quest_entries()
    {
        var walk = Walkthrough(Table("w", Row("walk:ifrit", "Ifrit", "espers:ifrit")), Quests(("Lost Child", "quests:lost-child")));
        var exported = new GuideExportDocument(GuideImport.FormatName, GuideImport.SupportedFormatVersion, DateTimeOffset.UtcNow, "test",
            new GuideExportGame("Game", "", null, "", "", "emerald", "", "", "", "", Lang.English),
            [new GuideExportTab(walk.Slug, walk.Title, walk.Icon, 1, ApiEndpoints.DeserializeSections(walk.SectionsJson), null, WalkthroughTab.TopicId)]);
        var json = JsonSerializer.Serialize(exported, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

        using var doc = JsonDocument.Parse(json);
        var parsed = GuideImport.Parse(doc.RootElement);

        var sections = ApiEndpoints.DeserializeSections(Assert.Single(parsed.Tabs).SectionsJson);
        Assert.Equal("espers:ifrit", sections[0].Table!.Rows[0].Ref);
        Assert.Equal("quests:lost-child", sections[1].Quests![0].Ref);
    }

    [Fact]
    public void Importing_clears_a_dangling_ref_and_links_by_name_without_any_ticks()
    {
        var espers = Tab("espers", "Espers", "espers", Table("espers", Row("espers:ifrit", "Ifrit")));
        var walk = Walkthrough(Table("w", Row("walk:ifrit", "Ifrit"), Row("walk:gone", "Gone", "nowhere")));
        var tabs = new List<GuideTabEntity> { espers, walk };

        Assert.True(GuideLinkMaintenance.ApplyToTabs(tabs));

        var refs = ApiEndpoints.DeserializeSections(walk.SectionsJson)[0].Table!.Rows.ToDictionary(r => r.ItemId, r => r.Ref);
        Assert.Equal("espers:ifrit", refs["walk:ifrit"]);
        Assert.Null(refs["walk:gone"]);
    }

    // ---- links in the maps ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Paths_use_the_nearest_headings_and_a_link_inside_its_own_chapter_is_not_listed()
    {
        var espers = Tab("espers", "Espers", "espers", Table("espers", Row("espers:ifrit", "Ifrit"), Row("espers:self", "Self", "espers:ifrit")));
        var walk = Walkthrough(
            Table("before", Row("w:0", "Ifrit", "espers:ifrit")),                    // no heading yet
            Heading("Part 1", 1), Table("t1", Row("w:1", "Ifrit", "espers:ifrit")),   // part only
            Heading("Area A", 2), Table("t2", Row("w:2", "Ifrit", "espers:ifrit")),   // part + area
            Heading("Part 2", 1), Table("t3", Row("w:3", "Ifrit", "espers:ifrit")),   // area is reset by a new part
            Heading("Area B", 2));
        var tabs = new[] { espers, walk }.Select((t, i) => new GuideTabDto(t.Id, t.Slug, t.Title, t.Icon, i, 0, 0, ApiEndpoints.DeserializeSections(t.SectionsJson), false, "start")).ToList();

        var (links, targets) = GuideLinker.BuildMaps(tabs);

        Assert.Equal(["", "Part 1", "Part 1 · Area A", "Part 2"], links["espers:ifrit"].Select(l => l.Path));
        Assert.Equal([0, 2, 4, 6], links["espers:ifrit"].Select(l => l.SectionIndex));
        Assert.Equal("espers:ifrit", Assert.Single(targets).Key);
    }

    // ---- the list in the prompts -----------------------------------------------------------------------------------------------------------------

    private static GuideTabEntity Chapter(string title, string? topic, int sort, int rows, string idPrefix) =>
        new()
        {
            Id = Guid.NewGuid(), GameId = Guid.NewGuid(), Slug = "tab-" + sort, Title = title, SortOrder = sort, TopicId = topic,
            SectionsJson = "[" + Table(idPrefix, Enumerable.Range(1, rows).Select(i => Row($"{idPrefix}:{i}", $"{title} entry {i}")).ToArray()) + "]"
        };

    [Fact]
    public void The_candidate_list_is_grouped_by_chapter_and_leaves_out_the_walkthrough_ref_rows_and_the_achievement_list()
    {
        var quests = Tab("quests", "Quests", "quests", Table("q", Row("quests:lost-child", "The Lost Child"), Row("quests:ref", "Linked", "elsewhere:1")));
        var walk = Walkthrough(Table("w", Row("walk:1", "Chest")));
        var achievements = Tab("steam-achievements", "Erfolge", null, Table("steam-achievements", Row("ACH_1", "First")));

        var list = GuideLinker.BuildCandidateList([walk, achievements, quests]);

        Assert.Equal("## Quests\nquests:lost-child | The Lost Child", list);
        Assert.Equal(string.Empty, GuideLinker.BuildCandidateList([walk, achievements]));
    }

    [Fact]
    public void A_long_list_is_cut_fairly_across_the_chapters_and_the_important_ones_come_first()
    {
        var tabs = new[]
        {
            Chapter("Misc", null, 1, 400, "m"),
            Chapter("Collectibles", "collectibles", 2, 400, "c"),
            Chapter("Quests", "quests", 3, 30, "q"),
        };

        var list = GuideLinker.BuildCandidateList(tabs, cap: 300);

        var lines = list.Split('\n');
        Assert.Equal(300 + 3, lines.Length);                                   // 300 rows + 3 headings
        Assert.Equal(["## Quests", "## Collectibles", "## Misc"], lines.Where(l => l.StartsWith("## ")));
        Assert.Equal(30, lines.Count(l => l.StartsWith("q:")));                // the small chapter is complete ...
        Assert.Equal(135, lines.Count(l => l.StartsWith("c:")));               // ... the big ones share the rest
        Assert.Equal(135, lines.Count(l => l.StartsWith("m:")));
    }

    [Fact]
    public void The_link_rule_is_only_in_the_prompts_when_there_is_something_to_point_at()
    {
        var empty = Context();
        var withList = Context() with { LinkCandidates = "## Espers\nespers:ifrit | Esper Ifrit" };

        Assert.Equal(string.Empty, AiGuideGeneratorService.LinkRules(empty));
        Assert.Contains("espers:ifrit | Esper Ifrit", AiGuideGeneratorService.BuildChapterPrompt(withList, "Quests", "All quests", "thin", "quests"));
        Assert.Contains("\"ref\"", AiGuideGeneratorService.BuildChapterPrompt(withList, "Quests", "All quests", "thin", "quests"));
        Assert.Contains("espers:ifrit | Esper Ifrit", AiGuideGeneratorService.BuildWalkthroughSinglePrompt(withList, "Walkthrough", "Everything", "ok"));
        var outline = new AiGuideGeneratorService.WalkthroughOutline([], [new("Part 1", "Start")]);
        Assert.Contains("espers:ifrit | Esper Ifrit", AiGuideGeneratorService.BuildWalkthroughPartPrompt(withList, outline, 0, []));
        Assert.DoesNotContain("\"ref\"", AiGuideGeneratorService.BuildChapterPrompt(empty, "Quests", "All quests", "thin", "quests"));
        // German variant
        var german = Context(Lang.German) with { LinkCandidates = "## Espers\nespers:ifrit | Esper Ifrit" };
        Assert.Contains("Verknüpfungen zwischen Kapiteln", AiGuideGeneratorService.BuildChapterPrompt(german, "Quests", "Alle Quests", "dünn", "quests"));
    }

    [Fact]
    public void The_candidate_list_of_300_rows_stays_small()
    {
        var tabs = Enumerable.Range(1, 12).Select(i => Chapter($"Chapter {i}", null, i, 60, $"c{i}")).ToArray();
        var list = GuideLinker.BuildCandidateList(tabs);
        // Measured size of the worst realistic case (300 rows, ids and names of ordinary length); reported in the hand-over
        TestContext.Current.SendDiagnosticMessage($"candidate list: {list.Length} characters for {list.Count(c => c == '\n') + 1 - 12} rows");
        Assert.InRange(list.Length, 1, 20_000);
    }

    // ---- translation: refs do not make a translation stale ----------------------------------------------------------------------------

    [Fact]
    public void A_ref_set_or_cleared_by_the_linker_does_not_change_the_translation_hash_and_a_chapter_without_refs_hashes_as_before()
    {
        var plain = Walkthrough(Table("w", Row("walk:ifrit", "Ifrit")), Quests(("Wolf", null)));
        var before = GuideTranslations.HashTab(plain);
        Assert.Equal(GuideTranslations.HashText(plain.Title + "\n" + plain.SectionsJson), before);     // unchanged formula without refs

        // The linker writes compact JSON with the ref added to the row / entry
        var espers = Tab("espers", "Espers", "espers", Table("espers", Row("espers:ifrit", "Ifrit")));
        var quests = Tab("quests", "Quests", "quests", Table("q", Row("quests:wolf", "Wolf")));
        plain.SectionsJson = JsonNode.Parse(plain.SectionsJson)!.ToJsonString();
        var compact = GuideTranslations.HashTab(plain);
        var result = GuideLinker.Reconcile([espers, quests, plain], Ticks());
        Assert.Equal(2, result.Linked);
        Apply(result, plain);

        Assert.Contains("\"ref\"", plain.SectionsJson);
        Assert.Equal(compact, GuideTranslations.HashTab(plain));
    }

    public static TheoryData<string> WritePaths => ["serializer", "normalizer", "manual"];

    /// <summary>
    /// Review: the hash of a chapter WITH refs is computed from re-serialized JSON, the chapters are written by different code (JsonSerializer of the DTOs,
    /// the normalizer's JsonNode, the manual save). The first link must not make a translation that was made before it look outdated - checked with German
    /// umlauts, „“ quotes and characters the encoder escapes, for each real write path.
    /// </summary>
    [Theory]
    [MemberData(nameof(WritePaths))]
    public void The_first_link_keeps_the_translation_hash_for_every_way_a_chapter_is_written(string path)
    {
        const string Name = "Ätherkristall „Blau“ <&'> Größe";
        var rowSection = new GuideSectionDto("checklist_table", Title: "Teil „1“ – Höhle",
            Table: new GuideTableDto("wt", "Fundstücke „Höhle“", true, null, null, null, null, null,
                [new GuideTableColumnDto("name", "Name", IsRowHeader: true), new GuideTableColumnDto("hint", "Hinweis")],
                [new GuideTableRowDto("wt:kristall", null, null, new Dictionary<string, string> { ["name"] = Name, ["hint"] = "Hinter dem Wasserfall – „links“" })]));
        var alert = new GuideSectionDto("alert", Title: "Übersicht", Kind: "side_quests", HtmlContent: "<b>Größe</b> & „mehr“",
            Quests: [new GuideSideQuestDto("Wölfe „jagen“", AvailableFrom: "ab Kapitel 2")]);
        string json;
        using (Lang.Use(Lang.German))
        {
            json = path switch
            {
                "serializer" => JsonSerializer.Serialize(new List<GuideSectionDto> { rowSection, alert }, ApiEndpoints.JsonOptions),
                "normalizer" => GuideSectionNormalizer.Normalize(JsonDocument.Parse(JsonSerializer.Serialize(new List<GuideSectionDto> { rowSection, alert }, ApiEndpoints.JsonOptions)).RootElement).ToJsonString(),
                _ => ManualSaved(rowSection, alert),
            };
        }
        var walk = Walkthrough();
        walk.SectionsJson = json;
        var before = GuideTranslations.HashTab(walk);
        Assert.Equal(GuideTranslations.HashText(walk.Title + "\n" + json), before);       // without refs: exactly today's formula

        var items = Tab("items", "Items", "items", Table("items", Row("items:kristall", Name.Replace("„", "\\u201E").Replace("“", "\\u201C"))));
        var quests = Tab("quests", "Quests", "quests", Table("q", Row("quests:wolf", "Wölfe „jagen“".Replace("„", "\\u201E").Replace("“", "\\u201C"))));
        var result = GuideLinker.Reconcile([items, quests, walk], Ticks());
        Assert.Equal(2, result.Linked);
        Apply(result, walk);

        Assert.Contains("\"ref\"", walk.SectionsJson);
        Assert.Equal(before, GuideTranslations.HashTab(walk));
    }

    /// <summary>The sections exactly as the manual save stores them (pasted answer -> check -> BuildTabFromChapterJson).</summary>
    private static string ManualSaved(params GuideSectionDto[] sections)
    {
        var answer = "{ \"title\": \"Komplettlösung\", \"icon\": \"mdi:map\", \"sections\": " + JsonSerializer.Serialize(sections.ToList(), ApiEndpoints.JsonOptions) + " }";
        var plan = new AiGuideGeneratorService.ChapterPlan("Komplettlösung", "mdi:map", "Das ganze Spiel", WalkthroughTab.TopicId);
        return AiGuideGeneratorService.BuildTabFromChapterJson(Guid.NewGuid(), 0, plan, answer, NormalizeOptions.Default).SectionsJson;
    }

    [Fact]
    public void A_served_translation_shows_the_refs_of_the_original()
    {
        var original = ApiEndpoints.DeserializeSections("[" + Table("w", Row("walk:ifrit", "Ifrit", "espers:ifrit"), Row("walk:chest", "Chest")) + "," + Quests(("Wolf", "quests:wolf")) + "]");
        var translated = ApiEndpoints.DeserializeSections("[" + Table("w", Row("walk:ifrit", "Ifrit DE"), Row("walk:chest", "Truhe", "old:gone")) + "," + Quests(("Wolf DE", null)) + "]");

        var shown = GuideTranslations.ApplyOriginalRefs(translated, original);

        Assert.Equal(["espers:ifrit", null], shown[0].Table!.Rows.Select(r => r.Ref));
        Assert.Equal("Ifrit DE", shown[0].Table!.Rows[0].Cells["name"]);
        Assert.Equal("quests:wolf", shown[1].Quests![0].Ref);
        Assert.Equal("Wolf DE", shown[1].Quests![0].Title);
    }

    // ---- prompt size (review: the list repeated in every part prompt) ---------------------------------------------------------------------

    [Fact]
    public void Part_and_chapter_prompts_carry_a_capped_list_with_the_relevant_items_first()
    {
        var tabs = Enumerable.Range(1, 12).Select(i => Chapter($"Chapter {i}", null, i, 60, $"c{i}")).ToList();
        var espers = Tab("espers", "Espers", "espers", Table("espers", Enumerable.Range(1, 40).Select(i => Row($"espers:{i}", $"Esper Number{i}")).ToArray()));
        tabs.Add(espers);
        var context = AiGuideGeneratorService.WithLinkCandidates(Context(), tabs);

        var part = AiGuideGeneratorService.LinkRules(context, "Part 3: The cave of the espers", GuideLinker.PartPromptCap);
        var chapter = AiGuideGeneratorService.LinkRules(context, "Bosses", GuideLinker.ChapterPromptCap);

        static int Rows(string text) => text.Split('\n').Count(l => System.Text.RegularExpressions.Regex.IsMatch(l.Trim(), @"^\S+ \| "));
        Assert.Equal(GuideLinker.PartPromptCap, Rows(part));
        Assert.Equal(GuideLinker.ChapterPromptCap, Rows(chapter));
        Assert.All(Enumerable.Range(1, 40), i => Assert.Contains($"espers:{i} | ", part));      // the chapter named in the part comes whole
        TestContext.Current.SendDiagnosticMessage($"link rules: part prompt {part.Length} characters, chapter prompt {chapter.Length} characters");
    }

    private static AiGuideGeneratorService.GameContext Context(string language = Lang.English) =>
        new("Suncraft", "Steam AppID: 1", [], [], ResearchBlocks.Selection.Empty, ResearchBlocks.FillBase(language, "Suncraft", "RPG", ResearchBlocks.PlatformText), language);
}
