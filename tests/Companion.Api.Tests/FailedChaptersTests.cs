using System.Text.Json;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Services;

namespace Companion.Api.Tests;

/// <summary>
/// The placeholder of a chapter whose research failed: new ones remember their topic and reason, old ones (only text) are recognised by
/// their structure, real chapters never are.
/// </summary>
public sealed class FailedChaptersTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    private static GuideTabEntity Tab(string title, int sort, string sectionsJson, string? slug = null) =>
        new() { Id = Guid.NewGuid(), GameId = Guid.NewGuid(), Slug = slug ?? $"tab-{sort}", Title = title, Icon = "mdi:star", SortOrder = sort, SectionsJson = sectionsJson };

    private static string Stored(params GuideSectionDto[] sections) => JsonSerializer.Serialize(sections, Json);

    [Fact]
    public void A_new_placeholder_remembers_topic_reason_and_provider_text_through_the_stored_json()
    {
        var section = FailedChapters.CreateSection("de", "Alle Bosse mit Schwächen", "bestiary", "Claude (Anthropic) hat die Anfrage abgelehnt (HTTP 400).", "temperature: not allowed", "claude");

        var failure = FailedChapters.Read(Stored(section));

        Assert.NotNull(failure);
        Assert.Equal("Alle Bosse mit Schwächen", failure.Focus);
        Assert.Equal("bestiary", failure.TopicId);
        Assert.Equal("Claude (Anthropic) hat die Anfrage abgelehnt (HTTP 400).", failure.Message);
        Assert.Equal("temperature: not allowed", failure.ProviderMessage);
        Assert.Equal("claude", failure.Provider);
    }

    [Fact]
    public void An_old_German_placeholder_gets_its_topic_from_the_instruction_text()
    {
        var old = new GuideSectionDto(
            "alert", Variant: "danger", Title: "Dieses Kapitel konnte nicht recherchiert werden",
            HtmlContent: "Claude (Anthropic) hat die Anfrage abgelehnt (HTTP 400). – Lösche den Tab nicht, sondern ergänze ihn über „Tab ergänzen“ mit dem Thema: Beste Waffen, Rüstungen und wo man sie findet");

        var failure = FailedChapters.Read(Stored(old));

        Assert.NotNull(failure);
        Assert.Equal("Beste Waffen, Rüstungen und wo man sie findet", failure.Focus);
        Assert.Equal("Claude (Anthropic) hat die Anfrage abgelehnt (HTTP 400).", failure.Message);
        Assert.Null(failure.TopicId);
        Assert.Null(failure.ProviderMessage);
    }

    [Fact]
    public void An_old_English_placeholder_is_recognised_too()
    {
        var old = new GuideSectionDto(
            "alert", Variant: "danger", Title: "This chapter could not be researched",
            HtmlContent: "Claude (Anthropic) refused the request (HTTP 400). – Do not delete the tab, add it again with “Add chapter” using this topic: Best weapons and where to find them");

        var failure = FailedChapters.Read(Stored(old));

        Assert.NotNull(failure);
        Assert.Equal("Best weapons and where to find them", failure.Focus);
        Assert.Equal("Claude (Anthropic) refused the request (HTTP 400).", failure.Message);
    }

    [Fact]
    public void Real_chapters_are_never_taken_for_placeholders()
    {
        // A danger alert in the middle of a real chapter, an alert with another title, several sections: none of them is a placeholder
        var realAlert = new GuideSectionDto("alert", Variant: "danger", Title: "Verpassbar!", HtmlContent: "Dieser Boss ist nur einmal verfügbar. – Lösche den Tab nicht, sondern ergänze ihn über „Tab ergänzen“ mit dem Thema: X");
        var otherSection = new GuideSectionDto("story", Paragraphs: ["x"]);

        Assert.Null(FailedChapters.Read(Stored(realAlert)));
        Assert.Null(FailedChapters.Read(Stored(FailedChapters.CreateSection("de", "f", null, "m", null, null), otherSection)));
        Assert.Null(FailedChapters.Read("[]"));
        Assert.Null(FailedChapters.Read(""));
        Assert.Null(FailedChapters.Read("{ not json danger"));
    }

    [Fact]
    public void Find_lists_the_placeholders_in_guide_order_narrowed_by_id_and_skips_the_lore_tab()
    {
        var placeholder = (string focus) => Stored(FailedChapters.CreateSection("de", focus, null, "Grund", null, null));
        var second = Tab("Zweites", 2, placeholder("Thema 2"));
        var first = Tab("Erstes", 1, placeholder("Thema 1"));
        var fine = Tab("Fertig", 3, Stored(new GuideSectionDto("story", Paragraphs: ["Text"])));
        var lore = Tab("Story & Lore", 900, placeholder("Thema Lore"), slug: LoreTab.Slug);
        GuideTabEntity[] tabs = [second, fine, first, lore];

        var all = FailedChapters.Find(tabs, null);
        Assert.Equal(["Erstes", "Zweites"], all.Select(c => c.Title));
        Assert.Equal("Thema 1", all[0].Failure.Focus);

        var only = FailedChapters.Find(tabs, [second.Id, fine.Id]);
        Assert.Equal(["Zweites"], only.Select(c => c.Title));
        Assert.Empty(FailedChapters.Find(tabs, [Guid.NewGuid()]));
    }
}

/// <summary>An AI answer is read as the first complete JSON object; an unreadable answer becomes a plain sentence, not a raw .NET error.</summary>
public sealed class AnswerReadingTests
{
    [Theory]
    [InlineData("""{"a":1}""", """{"a":1}""")]
    [InlineData("""Hier ist das Ergebnis: {"a":{"b":2}} Viel Erfolg!""", """{"a":{"b":2}}""")]
    [InlineData("""{"a":"x } y"} und noch {"zweites":true}""", """{"a":"x } y"}""")]
    [InlineData("```json\n{\"a\":\"quote \\\" and } brace\"}\n```", "{\"a\":\"quote \\\" and } brace\"}")]
    public void The_first_complete_object_is_taken_even_when_text_or_a_second_object_follows(string answer, string expected)
    {
        Assert.Equal(expected, AiGuideGeneratorService.ExtractJsonObject(answer));
        using var parsed = JsonDocument.Parse(AiGuideGeneratorService.ExtractJsonObject(answer));
    }

    [Fact]
    public void A_cut_off_answer_still_fails_in_the_parser_and_text_without_an_object_is_refused()
    {
        var cut = AiGuideGeneratorService.ExtractJsonObject("""{"a":{"b":1}, "c": [1,2""" + "}");
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(cut));
        Assert.Throws<LocalizedException>(() => AiGuideGeneratorService.ExtractJsonObject("no json here"));
    }

    [Fact]
    public void An_unreadable_answer_is_explained_in_plain_words_with_the_raw_error_for_technical_details()
    {
        var error = Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse("""{"a":1} {"b":2}"""));

        var (message, details, provider) = FailedChapters.Explain(error, Lang.German);

        Assert.Contains("unvollständig oder nicht lesbar", message);
        Assert.DoesNotContain("JSON", message);
        Assert.Equal(error.Message, details);
        Assert.Null(provider);
        Assert.Contains("incomplete or unreadable", FailedChapters.Explain(error, Lang.English).Message);
    }

    [Fact]
    public void An_old_placeholder_with_a_raw_json_error_gets_the_plain_sentence_and_keeps_the_raw_text_as_details()
    {
        var raw = "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. LineNumber: 63 | BytePositionInLine: 341.";
        var old = new GuideSectionDto("alert", Variant: "danger", Title: "Dieses Kapitel konnte nicht recherchiert werden",
            HtmlContent: raw + " – Lösche den Tab nicht, sondern ergänze ihn über „Tab ergänzen“ mit dem Thema: Alle Waffen");

        var failure = FailedChapters.Read([old]);

        Assert.NotNull(failure);
        Assert.Contains("unvollständig oder nicht lesbar", failure.Message);
        Assert.Equal(raw, failure.ProviderMessage);
        Assert.Equal("Alle Waffen", failure.Focus);
    }

    [Fact]
    public void Another_error_gets_a_generic_sentence_and_its_text_as_details()
    {
        var (message, details, _) = FailedChapters.Explain(new InvalidOperationException("kaputt"), Lang.German);

        Assert.Contains("unerwarteter Fehler", message);
        Assert.Equal("kaputt", details);
    }
}

public sealed class StoredRawErrorTests
{
    [Fact]
    public void A_saved_placeholder_whose_explanation_is_a_raw_json_error_is_shown_plainly()
    {
        var raw = "Expected depth to be zero at the end of the JSON payload. LineNumber: 63 | BytePositionInLine: 341.";
        var section = FailedChapters.CreateSection(Lang.German, "Alle Waffen", "extra", raw, null, null);

        var failure = FailedChapters.Read([section]);

        Assert.NotNull(failure);
        Assert.Contains("unvollständig oder nicht lesbar", failure.Message);
        Assert.Equal(raw, failure.ProviderMessage);
        Assert.Equal("Alle Waffen", failure.Focus);
    }
}
