using System.Text.Json.Nodes;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>
/// The pure rules of the AI review of a chapter (<see cref="ChapterReview"/>) on the recorded example answers in <c>Fixtures/review</c>: reading the
/// findings, the guard (an entry may not vanish silently), the counts, the "unklar" markers, taking another version (also across different column keys),
/// the hash that tells a stale review and the restored source origins.
/// </summary>
public sealed class ChapterReviewTests
{
    private static string Fx(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "review", name));

    private static JsonObject Root(string file) => JsonNode.Parse(Fx(file))!.AsObject();

    /// <summary>The sections of a chapter file ({chapter:{sections}} or {sections}), as a free-standing copy.</summary>
    private static JsonArray Sections(string file)
    {
        var root = Root(file);
        var chapter = root["chapter"] as JsonObject ?? root;
        return (JsonArray)JsonNode.Parse(chapter["sections"]!.ToJsonString())!;
    }

    private static FindingsRead Findings(string file) => ChapterReview.ReadFindings(Root(file)["findings"]);

    private static JsonObject Row(JsonArray sections, string itemId) =>
        ChapterReview.Entries(sections).Single(e => e.ItemId == itemId).Node;

    private static string Cell(JsonArray sections, string itemId, string key) => (string)Row(sections, itemId)["cells"]![key]!;

    private static ReviewFinding Finding(string kind, ReviewTarget target, string reason = "Grund.", string? column = null, string? a = null, string? b = null) =>
        new(kind, target, column, a, b, null, "https://example.org/x", reason);

    // ---- reading the findings ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_recorded_good_answer_has_a_finding_of_every_kind()
    {
        var read = Findings("review-good.json");

        Assert.Equal(7, read.Findings.Count);
        Assert.Equal(0, read.Dropped);
        Assert.Equal(0, read.WithoutSource);
        Assert.Equal(["added", "corrected", "removed", "unclear"], read.Findings.Select(f => f.Kind).Distinct().Order().ToArray());
        var golem = read.Findings[0];
        Assert.Equal("esper-golem", golem.Target.ItemId);
        Assert.Equal("Fundort", golem.Column);
        Assert.StartsWith("https://", golem.Source);
        Assert.Null(read.Findings[^1].Source);   // "unclear" may come without a source
    }

    [Fact]
    public void Findings_of_an_unknown_kind_or_without_a_reason_are_dropped_and_only_http_sources_survive()
    {
        var node = JsonNode.Parse("""
            [
              { "kind": "improved", "target": { "itemId": "x" }, "reason": "Unknown kind." },
              { "kind": "added", "target": { "itemId": "x" } },
              { "kind": "added", "target": { "itemId": "y" }, "reason": "Fine.", "source": "javascript:alert(1)" },
              { "kind": "CORRECTED", "target": { "itemId": "z" }, "reason": "Fine.", "source": "https://example.org/z" },
              "not an object"
            ]
            """);

        var read = ChapterReview.ReadFindings(node);

        Assert.Equal(3, read.Dropped);
        Assert.Equal(["added", "corrected"], read.Findings.Select(f => f.Kind).ToArray());
        Assert.Null(read.Findings[0].Source);      // not a web address
        Assert.Equal(1, read.WithoutSource);        // the "added" one has none
        Assert.Equal("https://example.org/z", read.Findings[1].Source);
    }

    [Fact]
    public void Every_text_of_a_finding_is_cut_to_its_limit()
    {
        var long800 = new string('a', 2000);
        var node = JsonNode.Parse($$"""[ { "kind": "unclear", "target": { "itemId": "{{new string('i', 500)}}", "section": "{{new string('s', 500)}}" }, "column": "{{new string('c', 500)}}", "a": "{{long800}}", "b": "{{long800}}", "result": "{{long800}}", "reason": "{{long800}}" } ]""");

        var finding = Assert.Single(ChapterReview.ReadFindings(node).Findings);

        Assert.True(finding.Reason.Length <= ChapterReview.MaxReason);
        Assert.True(finding.A!.Length <= ChapterReview.MaxVersionText);
        Assert.True(finding.B!.Length <= ChapterReview.MaxVersionText);
        Assert.True(finding.Result!.Length <= ChapterReview.MaxVersionText);
        Assert.True(finding.Column!.Length <= ChapterReview.MaxColumn);
        Assert.True(finding.Target.ItemId!.Length <= ChapterReview.MaxItemId);
        Assert.True(finding.Target.Section!.Length <= ChapterReview.MaxName);
    }

    [Fact]
    public void A_finding_without_a_source_is_kept_and_counted_so_the_manual_check_can_warn()
    {
        var read = Findings("review-finding-without-source.json");

        Assert.True(read.WithoutSource >= 1);
        Assert.True(read.Findings.Count >= 6);
    }

    [Fact]
    public void An_answer_inside_prose_and_a_code_fence_is_read_and_a_cut_off_one_is_refused_with_a_reason()
    {
        var errors = new List<ManualIssueDto>();
        Assert.True(ManualAnswers.TryRead(Fx("review-in-prose.txt"), errors, out var root));
        Assert.Empty(errors);
        var parts = ChapterReview.ReadAnswer(root!, errors);
        Assert.NotNull(parts);
        Assert.NotEmpty(parts!.Findings.Findings);

        var cutErrors = new List<ManualIssueDto>();
        Assert.False(ManualAnswers.TryRead(Fx("review-truncated.txt"), cutErrors, out _));
        Assert.Contains(cutErrors, e => e.Code == "cutOff");
    }

    [Fact]
    public void An_answer_without_a_chapter_or_without_sections_is_unusable()
    {
        var errors = new List<ManualIssueDto>();
        Assert.Null(ChapterReview.ReadAnswer(JsonNode.Parse("""{ "findings": [] }""")!.AsObject(), errors));
        Assert.Null(ChapterReview.ReadAnswer(JsonNode.Parse("""{ "chapter": { "title": "X", "sections": [] } }""")!.AsObject(), errors));
        Assert.Equal(["noChapter", "noSections"], errors.Select(e => e.Code).ToArray());
    }

    [Fact]
    public void The_not_applicable_flag_is_removed_from_the_chapter_of_a_review_answer()
    {
        var errors = new List<ManualIssueDto>();
        var parts = ChapterReview.ReadAnswer(JsonNode.Parse("""{ "chapter": { "title": "X", "notApplicable": true, "reason": "no", "sections": [ { "type": "alert", "title": "t", "htmlContent": "h" } ] } }""")!.AsObject(), errors);

        Assert.NotNull(parts);
        Assert.DoesNotContain("notApplicable", parts!.ChapterJson);
    }

    // ---- the guard (C2) -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void An_entry_the_reviewer_dropped_without_a_finding_is_put_back_with_an_unclear_finding()
    {
        var a = Sections("answer-a.json");
        var b = Sections("answer-b.json");
        var result = Sections("review-dropped-item.json");
        Assert.DoesNotContain("esper-seraph", ChapterReview.ItemIds(result));

        var merge = ChapterReview.Merge(result, a, b, Findings("review-dropped-item.json"), Lang.German);

        Assert.Equal(1, merge.PutBack);
        Assert.Contains("esper-seraph", ChapterReview.ItemIds(merge.Sections));
        Assert.Equal("Seraph", ChapterReview.Entries(merge.Sections).Single(e => e.ItemId == "esper-seraph").Name);
        var finding = Assert.Single(merge.Findings, f => f.Kind == "unclear" && f.Target.ItemId == "esper-seraph");
        Assert.Contains("Seraph", finding.A);                  // A's version is what came back
        Assert.Equal(ServerTexts.Review.DroppedWithoutReason.De, finding.Reason);
    }

    [Fact]
    public void An_entry_a_removed_finding_names_is_not_put_back_and_nothing_else_is_missing()
    {
        var a = Sections("answer-a.json");
        var b = Sections("answer-b.json");
        var result = Sections("review-good.json");   // esper-kaktor (only in B) is absent, named by a "removed" finding

        var merge = ChapterReview.Merge(result, a, b, Findings("review-good.json"), Lang.German);

        Assert.Equal(0, merge.PutBack);
        Assert.DoesNotContain("esper-kaktor", ChapterReview.ItemIds(merge.Sections));
        var all = ChapterReview.ItemIds(a);
        all.UnionWith(ChapterReview.ItemIds(b));
        all.Remove("esper-kaktor");
        Assert.True(all.IsSubsetOf(ChapterReview.ItemIds(merge.Sections)));
    }

    [Fact]
    public void An_entry_missing_from_only_B_comes_back_with_the_cells_of_the_table_it_is_put_into()
    {
        var a = Sections("answer-a.json");
        var b = Sections("answer-b.json");
        var result = Sections("review-good.json");
        // The reviewer lost the B-only row "esper-fenrir" and did not say why: B's cells use other column keys than the table of the result
        var fenrirRow = Row(result, "esper-fenrir");
        ((JsonArray)fenrirRow.Parent!).Remove(fenrirRow);

        var merge = ChapterReview.Merge(result, a, b, Findings("review-good.json"), Lang.German);

        Assert.Equal(1, merge.PutBack);
        Assert.Equal("Fenrir", Cell(merge.Sections, "esper-fenrir", "name"));
        Assert.Equal("Belohnung nach dem Sieg über Humbaba", Cell(merge.Sections, "esper-fenrir", "ort"));
        Assert.Null(Row(merge.Sections, "esper-fenrir")["cells"]!["fundort"]);
    }

    // ---- counts -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_counts_come_from_the_code_einig_is_what_both_answers_have_without_a_finding()
    {
        var a = Sections("answer-a.json");
        var b = Sections("answer-b.json");

        var merge = ChapterReview.Merge(Sections("review-good.json"), a, b, Findings("review-good.json"), Lang.German);

        // Both answers share 9 rows, 2 cards and the boss = 12 entries; golem, odin, phoenix and the "Auktionshaus Jidoor" card have a finding
        Assert.Equal(new ReviewCounts(8, 2, 3, 1, 1), merge.Counts);
    }

    [Fact]
    public void A_boss_is_matched_by_its_boss_id_and_a_finding_that_names_its_title_flags_it()
    {
        var a = Sections("answer-a.json");
        var b = Sections("answer-b.json");
        var findings = new FindingsRead([Finding("unclear", new ReviewTarget(Section: "Ifrit & Shiva"))], 0, 0);

        var merge = ChapterReview.Merge(Sections("review-good.json"), a, b, findings, Lang.German);

        // Without the finding the boss counts as agreed; with it, it does not
        var without = ChapterReview.Merge(Sections("review-good.json"), a, b, new FindingsRead([], 0, 0), Lang.German);
        Assert.Equal(without.Counts.Agreed - 1, merge.Counts.Agreed);
    }

    // ---- the markers (O4) ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Open_unclear_findings_write_a_marker_on_the_entry_and_one_calm_box_at_the_top()
    {
        var a = Sections("answer-a.json");
        var b = Sections("answer-b.json");

        var merge = ChapterReview.Merge(Sections("review-good.json"), a, b, Findings("review-good.json"), Lang.German);

        var box = (JsonObject)merge.Sections[0]!;
        Assert.Equal("alert", (string)box["type"]!);
        Assert.Equal("review_unclear", (string)box["kind"]!);
        Assert.Equal(ServerTexts.Review.UnclearTitle.De, (string)box["title"]!);
        var line = Assert.Single(box["items"]!.AsArray());
        Assert.StartsWith("Phönix:", (string)line!);
        var marker = (JsonObject)Row(merge.Sections, "esper-phoenix")["unclear"]!;
        Assert.Equal("Welt des Ruins, nach Lockes Rückkehr in die Gruppe", (string)marker["a"]!);
        Assert.Equal("Welt des Ruins, sobald die Falcon verfügbar ist", (string)marker["b"]!);
        Assert.False(string.IsNullOrWhiteSpace((string)marker["reason"]!));
        Assert.Single(merge.Sections.OfType<JsonObject>(), s => (string?)s["kind"] == "review_unclear");
    }

    [Fact]
    public void Markers_the_AI_wrote_itself_never_survive_and_writing_markers_twice_gives_one_box()
    {
        var a = Sections("answer-a.json");
        var b = Sections("answer-b.json");
        var result = Sections("review-good.json");
        Row(result, "esper-ramuh")["unclear"] = JsonNode.Parse("""{ "a": "x", "b": "y", "reason": "AI wrote this" }""");
        result.Insert(0, JsonNode.Parse("""{ "type": "alert", "kind": "review_unclear", "title": "Fake", "htmlContent": "x" }"""));

        var merge = ChapterReview.Merge(result, a, b, Findings("review-good.json"), Lang.German);
        ChapterReview.ApplyMarkers(merge.Sections, merge.Findings, Lang.German);

        Assert.Null(Row(merge.Sections, "esper-ramuh")["unclear"]);
        Assert.Single(merge.Sections.OfType<JsonObject>(), s => (string?)s["kind"] == "review_unclear");
        Assert.NotEqual("Fake", (string)merge.Sections[0]!["title"]!);
    }

    [Fact]
    public void A_decided_point_loses_its_marker_and_the_box_disappears_with_the_last_open_point()
    {
        var merge = ChapterReview.Merge(Sections("review-good.json"), Sections("answer-a.json"), Sections("answer-b.json"), Findings("review-good.json"), Lang.German);
        var decided = merge.Findings.Select(f => f.Kind == "unclear" ? f with { Applied = "result" } : f).ToList();

        ChapterReview.ApplyMarkers(merge.Sections, decided, Lang.German);

        Assert.Null(Row(merge.Sections, "esper-phoenix")["unclear"]);
        Assert.DoesNotContain(merge.Sections.OfType<JsonObject>(), s => (string?)s["kind"] == "review_unclear");
    }

    [Fact]
    public void Markers_are_stripped_from_a_stored_chapter_before_it_becomes_answer_A()
    {
        var merge = ChapterReview.Merge(Sections("review-good.json"), Sections("answer-a.json"), Sections("answer-b.json"), Findings("review-good.json"), Lang.German);

        var clean = ChapterReview.CleanSections(merge.Sections.ToJsonString());

        Assert.DoesNotContain("unclear", clean.ToJsonString());
        Assert.DoesNotContain("review_unclear", clean.ToJsonString());
    }

    // ---- taking another version (C6) --------------------------------------------------------------------------------------------------------

    private static (JsonArray Current, JsonArray A, JsonArray B, List<ReviewFinding> Findings) Reviewed()
    {
        var a = Sections("answer-a.json");
        var b = Sections("answer-b.json");
        var merge = ChapterReview.Merge(Sections("review-good.json"), a, b, Findings("review-good.json"), Lang.German);
        return (merge.Sections, a, b, merge.Findings);
    }

    [Fact]
    public void Taking_the_other_version_of_a_cell_finds_the_column_by_its_label_even_when_the_answers_use_other_keys()
    {
        var (current, a, b, findings) = Reviewed();
        var golem = findings.First(f => f.Target.ItemId == "esper-golem");
        Assert.Equal("Auktionshaus Jidoor, 20.000 Gil", Cell(current, "esper-golem", "ort"));

        // B calls the column key "fundort", the chapter "ort": the finding says "Fundort" (the label)
        Assert.Equal(VersionOutcome.Applied, ChapterReview.ApplyVersion(current, golem, b));

        Assert.Equal("Auktionshaus Jidoor, 10.000 Gil", Cell(current, "esper-golem", "ort"));
        Assert.Equal("Golem", Cell(current, "esper-golem", "name"));   // the rest of the row is untouched
        Assert.Equal(VersionOutcome.Applied, ChapterReview.ApplyVersion(current, golem, a));
        Assert.Equal("Auktionshaus Jidoor, 20.000 Gil", Cell(current, "esper-golem", "ort"));
    }

    [Fact]
    public void Taking_A_for_an_entry_only_B_had_removes_it_and_taking_B_for_a_removed_one_puts_it_back_in_the_tables_columns()
    {
        var (current, a, b, findings) = Reviewed();

        var fenrir = findings.First(f => f.Target.ItemId == "esper-fenrir");
        Assert.Equal(VersionOutcome.Applied, ChapterReview.ApplyVersion(current, fenrir, a));
        Assert.DoesNotContain("esper-fenrir", ChapterReview.ItemIds(current));

        var kaktor = findings.First(f => f.Target.ItemId == "esper-kaktor");
        Assert.Equal(VersionOutcome.Applied, ChapterReview.ApplyVersion(current, kaktor, b));
        Assert.Equal("Kaktor", Cell(current, "esper-kaktor", "name"));
        Assert.Equal("Drachenhöhle (Bonus-Dungeon)", Cell(current, "esper-kaktor", "ort"));
        Assert.Equal("Nach dem Sieg über Kefka", Cell(current, "esper-kaktor", "abWann"));
    }

    [Fact]
    public void Taking_B_for_a_whole_row_keeps_the_link_of_the_row_and_fits_its_cells_to_the_table()
    {
        var (current, _, b, _) = Reviewed();
        var finding = Finding("corrected", new ReviewTarget(ItemId: "esper-alexander"));

        Assert.Equal(VersionOutcome.Applied, ChapterReview.ApplyVersion(current, finding, b));

        var row = Row(current, "esper-alexander");
        Assert.Equal("Schloss Doma, Thronsaal", (string)row["cells"]!["ort"]!);
        Assert.Equal("quest-doma-cyan", (string)row["ref"]!);
    }

    [Fact]
    public void Taking_another_version_of_a_card_or_a_boss_replaces_it_and_a_free_text_finding_cannot_be_taken()
    {
        var (current, a, b, findings) = Reviewed();
        var card = findings.First(f => f.Target.Card == "Auktionshaus Jidoor");
        Assert.Equal(VersionOutcome.Applied, ChapterReview.ApplyVersion(current, card, b));
        Assert.Contains("beim ersten Besuch nach der Oper immer", current.ToJsonString());

        var boss = Finding("unclear", new ReviewTarget(Section: "Ifrit & Shiva"));
        Assert.Equal(VersionOutcome.Applied, ChapterReview.ApplyVersion(current, boss, a));
        Assert.Contains("Greife zuerst Ifrit mit Eis-Zaubern an.", current.ToJsonString());

        Assert.Equal(VersionOutcome.NotStructured, ChapterReview.ApplyVersion(current, Finding("corrected", new ReviewTarget()), a));
        Assert.Equal(VersionOutcome.NotFound, ChapterReview.ApplyVersion(current, Finding("corrected", new ReviewTarget(ItemId: "esper-gibt-es-nicht")), a));
    }

    // ---- hash and stale state (C5) -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_hash_ignores_the_links_to_other_chapters_but_notices_every_other_change()
    {
        var json = Sections("answer-a.json").ToJsonString();
        var hash = ChapterReview.HashSections(json);
        Assert.False(ChapterReview.IsStale(hash, json));

        var relinked = (JsonArray)JsonNode.Parse(json)!;
        Row(relinked, "esper-alexander")["ref"] = "another-quest";
        Row(relinked, "esper-ramuh").Remove("ref");
        Assert.False(ChapterReview.IsStale(hash, relinked.ToJsonString()));   // only a "ref" changed

        var edited = (JsonArray)JsonNode.Parse(json)!;
        Row(edited, "esper-ramuh")["cells"]!["ort"] = "Woanders";
        Assert.True(ChapterReview.IsStale(hash, edited.ToJsonString()));
    }

    // ---- sources --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_source_that_was_cited_in_A_gets_its_origin_back_in_the_merged_chapter()
    {
        var a = Sections("answer-a.json");   // finalweapon.net is origin "cited"
        var b = Sections("answer-b.json");   // arrpeegeez.com is origin "ai"
        var merged = Sections("review-good.json");
        foreach (var source in merged.OfType<JsonObject>().Where(s => (string?)s["type"] == "sources").SelectMany(s => s["sources"]!.AsArray().OfType<JsonObject>()))
        {
            source["origin"] = "ai";   // what the normalizer makes of every source of an answer
        }

        ChapterReview.RestoreSourceOrigins(merged, a, b);

        var origins = merged.OfType<JsonObject>().Where(s => (string?)s["type"] == "sources").SelectMany(s => s["sources"]!.AsArray().OfType<JsonObject>())
            .ToDictionary(s => (string)s["url"]!, s => (string)s["origin"]!);
        Assert.Equal("cited", origins["https://finalweapon.net/2023/04/29/final-fantasy-vi-esper-location-guide/"]);
        Assert.Equal("ai", origins["https://www.arrpeegeez.com/2014/08/final-fantasy-vi-walkthrough-magicite.html"]);
        Assert.Equal("ai", origins["https://www.rpgsite.net/feature/14083-final-fantasy-vi-espers-guide-esper-locations-skills-unlocks"]);
    }

    // ---- the marker survives the normalizer, the reader and the translation -----------------------------------------------------------------------

    [Fact]
    public void A_marker_on_a_row_a_card_and_a_section_survives_the_normalizer_and_a_malformed_one_is_dropped_without_costing_the_table()
    {
        var sections = JsonNode.Parse("""
            [
              { "type": "alert", "kind": "review_unclear", "title": "Noch unklar", "htmlContent": "x", "items": ["A: b"] },
              { "type": "cards_grid", "columns": 1, "cards": [ { "title": "K", "unclear": { "a": "1", "b": "2", "reason": "r" } }, { "title": "K2", "unclear": "kaputt" } ] },
              { "type": "checklist_table", "table": { "tableId": "t", "caption": "T", "columns": [ { "key": "n", "label": "N" } ],
                "rows": [ { "itemId": "r1", "cells": { "n": "x" }, "unclear": { "a": "a", "reason": "r" } }, { "itemId": "r2", "cells": { "n": "y" }, "unclear": [1, 2] } ] } },
              { "type": "boss", "title": "Boss", "unclear": { "b": "nur b", "reason": "r" } }
            ]
            """)!;
        using var doc = System.Text.Json.JsonDocument.Parse(sections.ToJsonString());

        var normalized = GuideSectionNormalizer.Normalize(doc.RootElement, NormalizeOptions.Trusted);
        var read = GuideSectionReader.ReadStored(normalized.ToJsonString());

        Assert.Equal(4, read.Count);
        Assert.Equal("review_unclear", read[0].Kind);
        Assert.Equal("2", read[1].Cards![0].Unclear!.B);
        Assert.Null(read[1].Cards![1].Unclear);
        Assert.Equal("a", read[2].Table!.Rows[0].Unclear!.A);
        Assert.Null(read[2].Table!.Rows[1].Unclear);
        Assert.Equal(2, read[2].Table!.Rows.Count);
        Assert.Equal("nur b", read[3].Unclear!.B);
    }

    [Fact]
    public void The_texts_of_a_marker_and_of_the_box_are_translated_with_the_chapter_and_the_ids_stay()
    {
        var merge = ChapterReview.Merge(Sections("review-good.json"), Sections("answer-a.json"), Sections("answer-b.json"), Findings("review-good.json"), Lang.German);
        var slots = GuideTextExtractor.Collect(merge.Sections).Select(s => s.Text).ToList();

        Assert.Contains("Welt des Ruins, sobald die Falcon verfügbar ist", slots);          // marker b
        Assert.Contains(ServerTexts.Review.UnclearTitle.De, slots);                           // the box
        Assert.Contains(slots, t => t.StartsWith("Die Quellen nennen nur die Welt des Ruins", StringComparison.Ordinal));   // marker reason
    }
}
