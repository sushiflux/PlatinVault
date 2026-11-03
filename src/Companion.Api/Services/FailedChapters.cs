using System.Text.Json;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>A chapter placeholder that can be researched again: the tab, its title/icon and what it remembers about its topic.</summary>
public sealed record FailedChapterRef(Guid TabId, string Title, string Icon, GuideFailedChapterDto Failure);

/// <summary>
/// A chapter whose research failed is saved as a placeholder: one danger alert that also carries <see cref="GuideFailedChapterDto"/>
/// (planned topic, reason, the provider's own text). From it the UI offers "Research this chapter again". Placeholders from before
/// that existed (only text) are recognised by their structure and get the same data - nothing is rewritten in the database until a retry works.
/// </summary>
public static class FailedChapters
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private const int MaxPlaceholderJson = 12_000;

    /// <summary>The placeholder section of a chapter that could not be researched. Message = our explanation in the guide's language.</summary>
    public static GuideSectionDto CreateSection(string language, string focus, string? topicId, string message, string? providerMessage, string? provider) =>
        new(
            Type: "alert",
            Variant: "danger",
            Title: AiGuideGeneratorService.FailedChapterTitle(language),
            HtmlContent: message,
            FailedChapter: new GuideFailedChapterDto(focus, topicId, message, providerMessage, provider));

    /// <summary>
    /// Our explanation for a failed chapter research, in the guide's language, plus the technical text for "technical details": a provider
    /// refusal keeps its own explanation and the provider's text; an unreadable AI answer and any other error get a plain sentence with the
    /// raw error text behind "technical details" (never a raw .NET error as the explanation).
    /// </summary>
    public static (string Message, string? ProviderMessage, string? Provider) Explain(Exception error, string language) => error switch
    {
        AiProviderException ai => (ai.Message, ai.ProviderMessage, ai.Provider),
        LocalizedException localized => (localized.Message, null, null),
        System.Text.Json.JsonException json => (ServerTexts.Ai.AnswerUnreadable.Resolve(language), json.Message, null),
        _ => (ServerTexts.Ai.ChapterFailedUnexpected.Resolve(language), error.Message, null)
    };

    /// <summary>What a chapter's sections say about a failed research, or null when they are real content.</summary>
    public static GuideFailedChapterDto? Read(IReadOnlyList<GuideSectionDto> sections)
    {
        if (sections.Count != 1)
        {
            return null;
        }
        var section = sections[0];
        if (section.FailedChapter is { } remembered)
        {
            // A placeholder saved while the raw .NET error of an unreadable AI answer was still its explanation: say it plainly now
            if (remembered.Message.Contains("BytePositionInLine", StringComparison.Ordinal))
            {
                var language = section.Title == AiGuideGeneratorService.FailedChapterTitle(Lang.English) ? Lang.English : Lang.German;
                return remembered with { Message = ServerTexts.Ai.AnswerUnreadable.Resolve(language), ProviderMessage = remembered.ProviderMessage ?? remembered.Message };
            }
            return remembered;
        }
        return ReadLegacy(section);
    }

    /// <summary>The same for stored JSON (tolerates unreadable content: such a chapter is simply not a placeholder).</summary>
    public static GuideFailedChapterDto? Read(string sectionsJson)
    {
        // A placeholder is one small danger alert: real chapters (long, or without a danger alert) are skipped without being parsed
        if (string.IsNullOrWhiteSpace(sectionsJson) || sectionsJson.Length > MaxPlaceholderJson || !sectionsJson.Contains("danger", StringComparison.Ordinal))
        {
            return null;
        }
        try
        {
            return Read(JsonSerializer.Deserialize<List<GuideSectionDto>>(sectionsJson, JsonOptions) ?? []);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The failed chapters of a guide in guide order; <paramref name="onlyIds"/> narrows it down (null/empty = all).</summary>
    public static List<FailedChapterRef> Find(IEnumerable<GuideTabEntity> tabs, IReadOnlyCollection<Guid>? onlyIds)
    {
        var result = new List<FailedChapterRef>();
        foreach (var tab in tabs.Where(t => t.Slug != LoreTab.Slug).OrderBy(t => t.SortOrder))
        {
            if (onlyIds is { Count: > 0 } && !onlyIds.Contains(tab.Id))
            {
                continue;
            }
            if (Read(tab.SectionsJson) is { } failure)
            {
                result.Add(new FailedChapterRef(tab.Id, tab.Title, tab.Icon, failure));
            }
        }
        return result;
    }

    /// <summary>The old placeholder text: "{explanation} – Lösche den Tab nicht, sondern ergänze ihn über „Tab ergänzen“ mit dem Thema: {topic}".</summary>
    private static GuideFailedChapterDto? ReadLegacy(GuideSectionDto section)
    {
        if (section is not { Type: "alert", Variant: "danger", HtmlContent: { } text })
        {
            return null;
        }
        var language = section.Title == AiGuideGeneratorService.FailedChapterTitle(Lang.German) ? Lang.German
            : section.Title == AiGuideGeneratorService.FailedChapterTitle(Lang.English) ? Lang.English
            : null;
        if (language is null)
        {
            return null;
        }
        foreach (var separator in AiGuideGeneratorService.LegacyFailedChapterSeparators)
        {
            var at = text.IndexOf(separator, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }
            var focus = text[(at + separator.Length)..].Trim();
            if (focus.Length == 0)
            {
                return null;
            }
            var message = text[..at].Trim();
            // Older placeholders show the raw .NET error of an unreadable AI answer: say it plainly, keep the raw text for "technical details"
            return message.Contains("BytePositionInLine", StringComparison.Ordinal)
                ? new GuideFailedChapterDto(focus, null, ServerTexts.Ai.AnswerUnreadable.Resolve(language), message)
                : new GuideFailedChapterDto(focus, null, message);
        }
        return null;
    }
}
