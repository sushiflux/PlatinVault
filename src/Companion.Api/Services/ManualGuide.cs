using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Companion.Api.Endpoints;
using Companion.Api.Localization;

namespace Companion.Api.Services;

// ---- Guide via the user's own AI subscription: copy the prompt into the chat, paste the answer back ------------------------------------
// Nothing here talks to an AI provider: the prompts are made from the same building blocks as the real AI calls, the answers are read, checked and
// saved through the same code as AI results (normalizer, group, placeholder replacement).

/// <summary>A prompt for the chat of the user's subscription. Kind = "blueprint" or "chapter".</summary>
public sealed record ManualPromptDto(string Kind, string Prompt, string FileName, string Language, int? SteamAppId, string Title);

/// <summary>One finding about a pasted answer: what is wrong (or worth knowing) in the language of the UI, with the technical text apart.</summary>
public sealed record ManualIssueDto(string Code, string Message, string? Details = null);

/// <summary>What a chapter answer contains once read: shown before saving.</summary>
public sealed record ManualChapterPreviewDto(string Title, string Icon, int SectionCount, int TableRows, int Cards, List<string> SectionTitles);

public sealed record ManualBlueprintCheckDto(bool Ok, List<ManualIssueDto> Errors, List<ManualIssueDto> Warnings, GuidePlanDto? Plan);

/// <summary>NotApplicable = the answer says the topic does not exist in the game (the sentence to show; saving then creates no chapter / removes the placeholder).</summary>
public sealed record ManualChapterCheckDto(bool Ok, List<ManualIssueDto> Errors, List<ManualIssueDto> Warnings, ManualChapterPreviewDto? Preview, string? NotApplicable = null);

public sealed record ManualAnswerRequest(string? Text);

/// <summary>Everything the prompt of one chapter needs; the browser keeps the plan between the steps, the server stores nothing.</summary>
public sealed record ManualChapterPromptRequest(string Title, string? Icon, string Focus, string? TopicId, string? SourceSituation);

/// <summary>Saves an answer: as a new chapter, or (TabId) in place of a failed chapter's placeholder.</summary>
public sealed record ManualChapterAnswerRequest(string? Text, string Title, string? Icon, string? Focus, string? TopicId, Guid? TabId);

/// <summary>TabId = the saved chapter; with Dropped (the answer said the topic does not exist: no chapter was created) the removed placeholder, or null.</summary>
public sealed record ManualSavedDto(GameDetailDto Game, Guid? TabId, bool Replaced, bool Dropped = false);

/// <summary>
/// Reads and checks what a user pasted from a chat: the answer may come with a sentence before or after it or inside a ```json fence; broken or
/// cut-off JSON, the wrong kind of answer and a different game are told apart with a specific sentence each. Internal results carry the
/// canonical JSON (strict, no trailing commas) that the normal chapter code then builds the tab from.
/// </summary>
public static partial class ManualAnswers
{
    public const int MaxAnswerKilobytes = 2000;

    private static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private static readonly string[] KnownSectionTypes = [.. GuideSectionNormalizer.KnownTypes];

    [GeneratedRegex(@"\p{L}{2,}")]
    private static partial Regex Word();

    /// <summary>The first JSON object of the pasted text, parsed leniently and re-written strictly; false with the reasons otherwise.</summary>
    public static bool TryRead(string? text, List<ManualIssueDto> errors, out JsonObject? root)
    {
        root = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add(Issue("empty", ServerTexts.Manual.AnswerEmpty));
            return false;
        }
        if (text.Length > MaxAnswerKilobytes * 1000)
        {
            errors.Add(Issue("tooLarge", ServerTexts.Manual.TooLarge(text.Length / 1000, MaxAnswerKilobytes)));
            return false;
        }

        string json;
        try
        {
            json = AiGuideGeneratorService.ExtractJsonObject(text);
        }
        catch (LocalizedException)
        {
            // An object that starts but never closes is a cut-off answer, not "no JSON at all"
            var open = MissingClosers(text);
            errors.Add(open > 0 ? Issue("cutOff", ServerTexts.Manual.CutOff(open)) : Issue("noJson", ServerTexts.Manual.NoJson));
            return false;
        }

        try
        {
            root = JsonNode.Parse(json, documentOptions: Lenient) as JsonObject;
        }
        catch (JsonException ex)
        {
            var missing = MissingClosers(text);
            if (missing > 0)
            {
                errors.Add(Issue("cutOff", ServerTexts.Manual.CutOff(missing), ex.Message));
            }
            else
            {
                errors.Add(Issue("invalidJson", ServerTexts.Manual.InvalidJson((int)(ex.LineNumber ?? 0) + 1, (int)(ex.BytePositionInLine ?? 0) + 1, Excerpt(json, (int)(ex.LineNumber ?? 0), (int)(ex.BytePositionInLine ?? 0)), HintFor(ex, json)), ex.Message));
            }
            return false;
        }

        if (root is null)
        {
            errors.Add(Issue("noJson", ServerTexts.Manual.NoJson));
            return false;
        }
        return true;
    }

    /// <summary>The Steam AppID an answer names, if any ("steamAppId" as number or digits in text).</summary>
    public static int? NamedAppId(JsonObject root)
    {
        if (root["steamAppId"] is not JsonValue value)
        {
            return null;
        }
        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }
        return value.TryGetValue<string>(out var text) && int.TryParse(text.Trim(), out var parsed) ? parsed : null;
    }

    /// <summary>Does the answer belong to this game? An AppID that differs is an error; no AppID (or a different name) only a warning.</summary>
    public static void CheckGame(JsonObject root, int? expectedAppId, string title, List<ManualIssueDto> errors, List<ManualIssueDto> warnings)
    {
        var named = NamedAppId(root);
        if (expectedAppId is > 0)
        {
            if (named is { } found && found != expectedAppId)
            {
                errors.Add(Issue("wrongGame", ServerTexts.Manual.WrongGame(expectedAppId.Value, found, title)));
                return;
            }
            if (named is null)
            {
                warnings.Add(Issue("noAppId", ServerTexts.Manual.NoAppIdEcho(expectedAppId.Value)));
            }
        }

        if (named is null && root["game"] is JsonValue nameValue && nameValue.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name) && !SameName(name, title))
        {
            warnings.Add(Issue("otherName", ServerTexts.Manual.OtherGameName(name.Trim(), title)));
        }
    }

    /// <summary>Is it the same game title (ignoring case, punctuation and a leading "the")? Containing one in the other counts (subtitles, editions).</summary>
    public static bool SameName(string a, string b)
    {
        static string Key(string value) => string.Concat(Word().Matches(value.ToLowerInvariant()).Select(m => m.Value));
        var (x, y) = (Key(a), Key(b));
        return x.Length == 0 || y.Length == 0 || x.Contains(y, StringComparison.Ordinal) || y.Contains(x, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reports, section by section, what the normalizer will drop or repair (it does so silently), and what looks wrong inside the sections.
    /// Returns nothing but warnings: whether the chapter is usable at all is decided after normalizing.
    /// </summary>
    public static void ScanSections(JsonArray sections, List<ManualIssueDto> warnings)
    {
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = 0;
        var withoutId = 0;
        var notListed = 0;
        var number = 0;
        foreach (var node in sections)
        {
            number++;
            if (node is not JsonObject section)
            {
                warnings.Add(Issue("sectionSkipped", ServerTexts.Manual.SectionSkipped(number, ServerTexts.Manual.SectionNotObject)));
                continue;
            }
            if (section["type"] is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var type) || string.IsNullOrWhiteSpace(type))
            {
                warnings.Add(Issue("sectionSkipped", ServerTexts.Manual.SectionSkipped(number, ServerTexts.Manual.SectionNoType)));
                continue;
            }
            if (!KnownSectionTypes.Contains(type))
            {
                warnings.Add(Issue("sectionSkipped", ServerTexts.Manual.SectionSkipped(number, ServerTexts.Manual.SectionUnknownType(type))));
                continue;
            }

            switch (type)
            {
                case "story" when section["paragraphs"] is null && string.IsNullOrWhiteSpace(section["htmlContent"]?.ToString()):
                    warnings.Add(Issue("sectionSkipped", ServerTexts.Manual.SectionSkipped(number, ServerTexts.Manual.SectionStoryNoText)));
                    break;
                case "heading" when string.IsNullOrWhiteSpace(section["title"]?.ToString()):
                    warnings.Add(Issue("sectionSkipped", ServerTexts.Manual.SectionSkipped(number, ServerTexts.Manual.SectionHeadingNoTitle)));
                    break;
                case "puzzle":
                    ScanPuzzle(section, number, warnings);
                    break;
                case "alert" when section["kind"] is JsonValue kindValue && kindValue.TryGetValue<string>(out var kind) && !GuideAlertKinds.All.Contains(kind.Trim().ToLowerInvariant().Replace('-', '_')):
                    warnings.Add(Issue("alertKind", ServerTexts.Manual.AlertKindUnknown(number, kind)));
                    break;
                case "cards_grid" when section["cards"] is not JsonArray { Count: > 0 }:
                    warnings.Add(Issue("noCards", ServerTexts.Manual.CardsWithoutCards(number)));
                    break;
                case "checklist_table":
                    if (section["table"] is not JsonObject table)
                    {
                        warnings.Add(Issue("sectionSkipped", ServerTexts.Manual.SectionSkipped(number, ServerTexts.Manual.SectionTableMissing)));
                        break;
                    }
                    if (table["rows"] is not JsonArray { Count: > 0 } rows)
                    {
                        warnings.Add(Issue("noRows", ServerTexts.Manual.TableWithoutRows(number)));
                        break;
                    }
                    var categories = (table["categories"] as JsonArray)?.Select(c => c?.ToString() ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
                    foreach (var row in rows.OfType<JsonObject>())
                    {
                        var id = row["itemId"]?.ToString();
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            withoutId++;
                        }
                        else if (!itemIds.Add(id))
                        {
                            duplicates++;
                        }
                        var category = row["filterCategory"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(category) && !categories.Contains(category))
                        {
                            notListed++;
                        }
                    }
                    break;
            }
        }

        if (withoutId > 0) warnings.Add(Issue("rowsWithoutId", ServerTexts.Manual.RowsWithoutItemId(withoutId)));
        if (duplicates > 0) warnings.Add(Issue("duplicateIds", ServerTexts.Manual.DuplicateItemIds(duplicates)));
        if (notListed > 0) warnings.Add(Issue("categoryNotListed", ServerTexts.Manual.CategoryNotListed(notListed)));
    }

    /// <summary>
    /// What is wrong with a puzzle before the normalizer repairs it: no steps (it is then shown as "solution not documented" - the point of the
    /// puzzle block is the complete solution), no location, or nothing at all to show. Steps may sit in "puzzle" or, tolerated, beside it.
    /// </summary>
    private static void ScanPuzzle(JsonObject section, int number, List<ManualIssueDto> warnings)
    {
        var puzzle = section["puzzle"] as JsonObject ?? section;
        var name = section["title"]?.ToString();
        var location = puzzle["location"]?.ToString();
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(section["htmlContent"]?.ToString()) && string.IsNullOrWhiteSpace(location))
        {
            warnings.Add(Issue("sectionSkipped", ServerTexts.Manual.SectionSkipped(number, ServerTexts.Manual.SectionPuzzleEmpty)));
            return;
        }

        name = string.IsNullOrWhiteSpace(name) ? $"#{number}" : name.Trim();
        var steps = puzzle["steps"];
        var hasSteps = steps switch
        {
            JsonArray array => array.Any(x => !string.IsNullOrWhiteSpace(x?.ToString())),
            JsonValue => !string.IsNullOrWhiteSpace(steps.ToString()),
            _ => false
        };
        if (!hasSteps)
        {
            warnings.Add(Issue("puzzleWithoutSteps", ServerTexts.Manual.PuzzleWithoutSteps(number, name)));
        }
        if (string.IsNullOrWhiteSpace(location))
        {
            warnings.Add(Issue("puzzleWithoutLocation", ServerTexts.Manual.PuzzleWithoutLocation(number, name)));
        }
    }

    /// <summary>"de" / "en" when the words of the texts clearly belong to one language, else null (too little text or mixed).</summary>
    public static string? GuessLanguage(JsonNode? node)
    {
        var text = new System.Text.StringBuilder();
        Collect(node, text, depth: 0);
        var words = Word().Matches(text.ToString().ToLowerInvariant()).Select(m => m.Value).ToList();
        var german = words.Count(GermanFunctionWords.Contains);
        var english = words.Count(EnglishFunctionWords.Contains);
        if (german + english < 12)
        {
            return null;
        }
        if (german >= english * 2.5)
        {
            return Lang.German;
        }
        return english >= german * 2.5 ? Lang.English : null;
    }

    private static readonly HashSet<string> GermanFunctionWords =
        ["und", "der", "die", "das", "den", "dem", "ein", "eine", "einen", "mit", "für", "nicht", "ist", "sind", "wird", "werden", "auf", "von", "zu", "im", "ab", "nach", "bei", "oder", "wenn", "dann", "auch", "kann", "man"];

    private static readonly HashSet<string> EnglishFunctionWords =
        ["and", "the", "of", "for", "with", "not", "is", "are", "will", "you", "your", "to", "in", "on", "from", "or", "if", "then", "also", "can", "this", "that", "it", "be", "at", "as", "by"];

    private static void Collect(JsonNode? node, System.Text.StringBuilder text, int depth)
    {
        if (depth > 12 || text.Length > 6000)
        {
            return;
        }
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var s) && s.Length > 3:
                text.Append(s).Append(' ');
                break;
            case JsonArray array:
                foreach (var item in array) Collect(item, text, depth + 1);
                break;
            case JsonObject obj:
                foreach (var pair in obj) Collect(pair.Value, text, depth + 1);
                break;
        }
    }

    /// <summary>Closing brackets that are still missing at the end of the pasted text (> 0 = the answer was cut off).</summary>
    private static int MissingClosers(string text)
    {
        var start = AiGuideGeneratorService.FindObjectStart(text);
        if (start < 0)
        {
            return 0;
        }
        var stack = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{': case '[': stack++; break;
                case '}': case ']': stack--; break;
            }
            if (stack == 0)
            {
                return 0;
            }
        }
        return stack;
    }

    private static string Excerpt(string json, int zeroBasedLine, int zeroBasedColumn)
    {
        var lines = json.Split('\n');
        var line = zeroBasedLine >= 0 && zeroBasedLine < lines.Length ? lines[zeroBasedLine].TrimEnd('\r') : string.Empty;
        var from = Math.Max(0, zeroBasedColumn - 30);
        var length = Math.Min(70, Math.Max(0, line.Length - from));
        return length > 0 ? line.Substring(from, length).Trim() : string.Empty;
    }

    private static string HintFor(JsonException error, string json)
    {
        if (json.Contains('“') || json.Contains('”') || json.Contains('‘') || json.Contains('’'))
        {
            // Typographic quotes inside texts are fine; at the structure level they break the JSON - only say so when the parser complained about quotes
            if (error.Message.Contains("invalid start of a property name", StringComparison.Ordinal) || error.Message.Contains("'“'", StringComparison.Ordinal) || error.Message.Contains("'”'", StringComparison.Ordinal))
            {
                return ServerTexts.Manual.HintTypographicQuotes.ToString();
            }
        }
        if (error.Message.Contains("invalid within a JSON string", StringComparison.Ordinal) || error.Message.Contains("control character", StringComparison.OrdinalIgnoreCase))
        {
            return ServerTexts.Manual.HintRawNewline.ToString();
        }
        return ServerTexts.Manual.HintGeneric.ToString();
    }

    public static ManualIssueDto Issue(string code, Msg message, string? details = null) => new(code, message.ToString(), details);
}
