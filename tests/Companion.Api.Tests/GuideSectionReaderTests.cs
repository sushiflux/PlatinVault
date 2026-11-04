using System.Text.Json;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>
/// Review finding: one wrongly typed field used to empty a whole chapter (normalizer final guard and the reading of stored data deserialized the chapter
/// in one piece). Now a field of another section type is dropped, a value of the wrong shape converted or dropped, and a section that still cannot be read
/// costs only itself.
/// </summary>
public sealed class GuideSectionReaderTests
{
    // The reviewer's probes: each used to throw for the whole chapter
    private const string Probes = """
        [
          { "type": "heading", "title": "Part 1" },
          { "type": "alert", "title": "Sources", "htmlContent": "a", "sources": ["PowerPyx - https://www.powerpyx.com/guide"] },
          { "type": "cards_grid", "title": "Cards", "columns": 2, "cards": [ { "title": "One" } ], "items": [ { "name": "x" } ] },
          { "type": "alert", "title": "Missable", "htmlContent": "b", "missable": "until chapter 3" },
          { "type": "story", "title": "Story", "paragraphs": ["Once upon a time"], "level": "2" },
          { "type": "alert", "title": "Achievement", "htmlContent": "c", "achievements": "ACH_1" },
          { "type": "checklist_table", "table": { "tableId": "t", "caption": "T", "columns": [ { "key": "name", "label": "Name" } ], "rows": [ { "itemId": "t-1", "cells": { "name": "Row" } } ] } }
        ]
        """;

    [Fact]
    public void The_normalizer_keeps_every_section_of_the_probes_and_drops_only_the_foreign_fields()
    {
        using var doc = JsonDocument.Parse(Probes);
        using var _ = Lang.Use(Lang.English);

        var sections = GuideSectionNormalizer.Normalize(doc.RootElement);
        var dto = JsonSerializer.Deserialize<List<GuideSectionDto>>(sections.ToJsonString(), ApiEndpoints.JsonOptions)!;

        Assert.Equal(["heading", "alert", "cards_grid", "alert", "story", "alert", "checklist_table"], dto.Select(s => s.Type));
        Assert.Null(dto[1].Sources);
        Assert.Null(dto[2].Items);
        Assert.Single(dto[2].Cards!);
        Assert.Null(dto[3].Missable);
        Assert.Null(dto[4].Level);
        Assert.Equal(["Once upon a time"], dto[4].Paragraphs);
        Assert.Null(dto[5].Achievements);
        Assert.Equal("t-1", dto[6].Table!.Rows[0].ItemId);
    }

    [Fact]
    public void Stored_data_with_wrongly_typed_fields_keeps_all_other_sections_and_converts_obvious_values()
    {
        var stored = """
            [
              { "type": "heading", "title": "Part 1", "level": "2" },
              { "type": "boss", "title": "Ifrit", "missable": "true", "weaknesses": "Ice", "hp": [ { "value": "1200", "source": "Wiki" }, { "value": { "x": 1 } } ], "attacks": "Hellfire" },
              { "type": "overview", "title": "At a glance", "counts": "three", "keyItems": ["Key"] },
              { "type": "alert", "title": "Plain", "htmlContent": "still here", "missable": "until chapter 3" }
            ]
            """;

        var sections = ApiEndpoints.DeserializeSections(stored);

        Assert.Equal(["heading", "boss", "overview", "alert"], sections.Select(s => s.Type));
        Assert.Equal(2, sections[0].Level);                   // "2" -> 2
        Assert.True(sections[1].Missable);                     // "true" -> true
        Assert.Equal(["Ice"], sections[1].Weaknesses);         // a single string -> a list
        Assert.Equal("1200", Assert.Single(sections[1].Hp!).Value);
        Assert.Null(sections[2].Counts);                       // nothing to convert: only this field is dropped
        Assert.Equal(["Key"], sections[2].KeyItems);
        Assert.Equal("still here", sections[3].HtmlContent);
        Assert.Null(sections[3].Missable);                     // belongs to "boss" only
    }

    [Fact]
    public void A_section_that_cannot_be_read_costs_only_itself()
    {
        var stored = """
            [
              { "type": "alert", "title": "Before", "htmlContent": "a" },
              { "type": "checklist_table", "table": "not a table" },
              { "title": "no type" },
              { "type": "alert", "title": "After", "htmlContent": "b" }
            ]
            """;

        var sections = ApiEndpoints.DeserializeSections(stored);

        Assert.Equal(["Before", "After"], sections.Where(s => s.Type == "alert").Select(s => s.Title));
    }

    [Fact]
    public void An_old_format_chapter_that_was_valid_before_reads_exactly_as_before()
    {
        // A chapter as the app stored it before the walkthrough blocks existed (also stray fields that never broke anything)
        var stored = """
            [
              { "type": "alert", "variant": "gold", "title": "Important", "icon": "mdi:star", "htmlContent": "<b>Read me</b>", "columns": 2 },
              { "type": "cards_grid", "columns": 3, "cards": [ { "title": "Card", "badgeText": "New", "bullets": [ { "icon": "mdi:a", "title": "T", "body": "B" } ] } ] },
              { "type": "checklist_table", "table": { "tableId": "t", "caption": "C", "checkable": true, "searchPlaceholder": null, "categoryFilterLabel": null, "categories": null,
                "secondaryFilterLabel": null, "secondaryCategories": null, "columns": [ { "key": "name", "label": "Name", "isRowHeader": true } ],
                "rows": [ { "itemId": "t-1", "filterCategory": null, "filterSecondary": null, "cells": { "name": "Row" } } ] } },
              { "type": "story", "spoiler": true, "paragraphs": ["One", "Two"] },
              { "type": "puzzle", "title": "Gate", "puzzle": { "itemId": "p-1", "steps": ["Pull"], "documented": true } }
            ]
            """;

        var expected = JsonSerializer.Deserialize<List<GuideSectionDto>>(stored, ApiEndpoints.JsonOptions)!;
        var actual = ApiEndpoints.DeserializeSections(stored);

        Assert.Equal(JsonSerializer.Serialize(expected, ApiEndpoints.JsonOptions), JsonSerializer.Serialize(actual, ApiEndpoints.JsonOptions));
    }

    [Fact]
    public void Invalid_json_or_an_empty_chapter_reads_as_no_sections()
    {
        Assert.Empty(ApiEndpoints.DeserializeSections(""));
        Assert.Empty(ApiEndpoints.DeserializeSections("{ not json"));
        Assert.Empty(ApiEndpoints.DeserializeSections("{}"));
    }
}
