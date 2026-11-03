using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Companion.Api.Endpoints;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// Makes guide sections from untrusted sources (AI answers, imported guide files) safe for the typed frontend. Shared by
/// <see cref="AiGuideGeneratorService"/> and the guide import so both apply exactly the same rules.
/// </summary>
internal static partial class GuideSectionNormalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex(@"\r?\n\s*\r?\n")]
    private static partial Regex ParagraphBreak();

    // The number a model put in front of a step ("1. Do this", "Step 2: Then that"); only removed when ALL steps of a list carry the running
    // numbers 1, 2, 3 ... (see StripListNumbering) - a single "1. Stock: pull the lever" is content, an ordinal of the game
    [GeneratedRegex(@"^\s*(?:step\s+|schritt\s+)?(?<n>\d{1,3})\s*[.):]\s+", RegexOptions.IgnoreCase)]
    private static partial Regex StepNumber();

    /// <summary>The section types the app can render; everything else is dropped.</summary>
    public static readonly IReadOnlyList<string> KnownTypes = ["alert", "cards_grid", "checklist_table", "story", "puzzle", "heading"];

    /// <summary>
    /// Cell values become strings, rows get an item id, flags become booleans, story sections get a clean paragraph list.
    /// Unknown section types are dropped instead of breaking the whole tab.
    /// </summary>
    public static JsonArray Normalize(JsonElement sectionsEl)
    {
        var result = new JsonArray();
        foreach (var section in sectionsEl.EnumerateArray())
        {
            if (JsonNode.Parse(section.GetRawText()) is not JsonObject node)
            {
                continue;
            }

            var type = node["type"]?.ToString();
            if (type is null || !KnownTypes.Contains(type))
            {
                continue;
            }

            NormalizeSpoilerFlag(node);

            if (type == "story")
            {
                if (!NormalizeStory(node))
                {
                    continue;
                }
            }
            else if (type == "puzzle")
            {
                if (!NormalizePuzzle(node))
                {
                    continue;
                }
            }
            else if (type == "heading")
            {
                if (!NormalizeHeading(node))
                {
                    continue;
                }
            }
            else if (type == "alert")
            {
                NormalizeAlertKind(node);
            }
            else if (type == "checklist_table" && node["table"] is JsonObject table)
            {
                table["checkable"] = table["checkable"] is JsonValue c && c.TryGetValue<bool>(out var checkable) ? checkable : true;
                table["tableId"] ??= Guid.NewGuid().ToString("n")[..8];
                // Ambient language = the language of the guide being written or imported (see AiGuideGeneratorService / GuideImport)
                table["caption"] ??= Lang.Pick("Übersicht", "Overview");
                table["columns"] ??= new JsonArray();
                var rows = table["rows"] as JsonArray ?? [];
                var index = 1;
                foreach (var row in rows.OfType<JsonObject>())
                {
                    if (string.IsNullOrWhiteSpace(row["itemId"]?.ToString()))
                    {
                        row["itemId"] = $"{table["tableId"]}-{index}";
                    }
                    if (row["cells"] is JsonObject cells)
                    {
                        foreach (var key in cells.Select(p => p.Key).ToList())
                        {
                            var value = cells[key];
                            cells[key] = value switch
                            {
                                null => string.Empty,
                                JsonValue v when v.TryGetValue<string>(out var s) => s,
                                JsonArray arr => string.Join(", ", arr.Select(x => x?.ToString())),
                                _ => value.ToJsonString()
                            };
                        }
                    }
                    else
                    {
                        row["cells"] = new JsonObject();
                    }
                    index++;
                }
                table["rows"] = rows;
            }
            else if (type == "checklist_table")
            {
                continue;
            }

            result.Add(node);
        }

        // Final guard: the frontend deserializes into GuideSectionDto - drop the tab content rather than storing something unreadable
        try
        {
            _ = JsonSerializer.Deserialize<List<GuideSectionDto>>(result.ToJsonString(), JsonOptions);
            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>"spoiler" is a plain bool on any section type; text like "true" is accepted, anything else is dropped.</summary>
    private static void NormalizeSpoilerFlag(JsonObject node)
    {
        if (!node.ContainsKey("spoiler"))
        {
            return;
        }

        if (node["spoiler"] is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var flag))
            {
                node["spoiler"] = flag;
                return;
            }
            if (value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed))
            {
                node["spoiler"] = parsed;
                return;
            }
        }
        node.Remove("spoiler");
    }

    /// <summary>
    /// Story prose: "paragraphs" becomes a list of non-empty strings (a single string is split at blank lines); without
    /// paragraphs a single "htmlContent" string counts as one paragraph. Returns false when there is no text at all.
    /// </summary>
    private static bool NormalizeStory(JsonObject node)
    {
        var paragraphs = new JsonArray();
        switch (node["paragraphs"])
        {
            case JsonArray list:
                foreach (var item in list)
                {
                    AddParagraph(paragraphs, item is JsonValue v && v.TryGetValue<string>(out var s) ? s : item?.ToString());
                }
                break;
            case JsonValue single when single.TryGetValue<string>(out var text):
                foreach (var part in ParagraphBreak().Split(text))
                {
                    AddParagraph(paragraphs, part);
                }
                break;
        }

        if (paragraphs.Count == 0 && node["htmlContent"] is JsonValue html && html.TryGetValue<string>(out var htmlText))
        {
            AddParagraph(paragraphs, htmlText);
        }

        node.Remove("htmlContent");
        if (paragraphs.Count == 0)
        {
            return false;
        }

        // "steps" is the only variant of a story: the paragraphs are the numbered steps of a walkthrough part
        if (string.Equals(node["variant"]?.ToString(), "steps", StringComparison.OrdinalIgnoreCase))
        {
            node["variant"] = "steps";
            var cleaned = StripListNumbering(paragraphs.Select(x => x?.ToString() ?? string.Empty).ToList());
            for (var i = 0; i < paragraphs.Count; i++)
            {
                paragraphs[i] = cleaned[i];
            }
        }
        else
        {
            node.Remove("variant");
        }

        node["paragraphs"] = paragraphs;
        return true;
    }

    /// <summary>
    /// A hint of the walkthrough ("alert" with a kind): the kind must be one of <see cref="GuideAlertKinds"/> (spellings like "must-have" are
    /// accepted), otherwise it is dropped and the alert stays a plain alert; a kind without a colour variant gets its default variant.
    /// </summary>
    private static void NormalizeAlertKind(JsonObject node)
    {
        if (!node.ContainsKey("kind"))
        {
            return;
        }

        var kind = node["kind"] is JsonValue v && v.TryGetValue<string>(out var text)
            ? text.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_')
            : null;
        if (kind is null || !GuideAlertKinds.All.Contains(kind))
        {
            node.Remove("kind");
            return;
        }

        node["kind"] = kind;
        if (string.IsNullOrWhiteSpace(node["variant"]?.ToString()))
        {
            node["variant"] = GuideAlertKinds.DefaultVariant(kind);
        }
    }

    /// <summary>A sub-heading needs a title (the optional htmlContent is a one-line lead below it).</summary>
    private static bool NormalizeHeading(JsonObject node)
    {
        var title = node["title"]?.ToString().Trim();
        if (string.IsNullOrEmpty(title))
        {
            return false;
        }

        node["title"] = title;
        if (string.IsNullOrWhiteSpace(node["htmlContent"]?.ToString()))
        {
            node.Remove("htmlContent");
        }
        return true;
    }

    /// <summary>
    /// A puzzle: the details live in the "puzzle" object (a model that put them next to the type is tolerated). Texts become trimmed strings,
    /// hints and steps clean lists (a single string is split into lines, own numbering is removed), "missable" a bool. Documented is TRUE exactly
    /// when there is at least one step - a puzzle without a solution says so in the UI instead of showing empty buttons. A puzzle with neither
    /// a name, a task nor a location is dropped.
    /// </summary>
    private static bool NormalizePuzzle(JsonObject node)
    {
        var puzzle = node["puzzle"] as JsonObject ?? new JsonObject();
        foreach (var key in new[] { "itemId", "location", "requirements", "missable", "hints", "steps", "source", "documented" })
        {
            if (puzzle[key] is null && node[key] is not null)
            {
                puzzle[key] = node[key]!.DeepClone();
            }
            node.Remove(key);
        }

        var title = node["title"]?.ToString().Trim();
        var task = node["htmlContent"]?.ToString().Trim();
        var location = Clean(puzzle["location"]);
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(task) && location is null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(title))
        {
            node.Remove("title");
        }
        else
        {
            node["title"] = title;
        }
        if (string.IsNullOrEmpty(task))
        {
            node.Remove("htmlContent");
        }
        else
        {
            node["htmlContent"] = task;
        }

        var result = new JsonObject();
        if (Clean(puzzle["itemId"]) is { } itemId) result["itemId"] = itemId;
        if (location is not null) result["location"] = location;
        if (Clean(puzzle["requirements"]) is { } requirements) result["requirements"] = requirements;
        if (ReadBool(puzzle["missable"]) is { } missable) result["missable"] = missable;

        var hints = ReadList(puzzle["hints"], stripNumbers: false);
        if (hints.Count > 0) result["hints"] = hints;
        var steps = ReadList(puzzle["steps"], stripNumbers: true);
        if (steps.Count > 0) result["steps"] = steps;
        if (Clean(puzzle["source"]) is { } source) result["source"] = source;
        result["documented"] = steps.Count > 0;

        node["puzzle"] = result;
        return true;
    }

    private static string? Clean(JsonNode? value)
    {
        var text = value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value is JsonValue ? value.ToString() : null;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool? ReadBool(JsonNode? value)
    {
        if (value is JsonValue v)
        {
            if (v.TryGetValue<bool>(out var flag))
            {
                return flag;
            }
            if (v.TryGetValue<string>(out var text))
            {
                var word = text.Trim().ToLowerInvariant();
                if (bool.TryParse(word, out var parsed)) return parsed;
                if (word is "ja" or "yes") return true;
                if (word is "nein" or "no") return false;
            }
        }
        return null;
    }

    private static JsonArray ReadList(JsonNode? value, bool stripNumbers)
    {
        var list = new JsonArray();
        IEnumerable<string?> items = value switch
        {
            JsonArray array => array.Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : x?.ToString()),
            JsonValue single when single.TryGetValue<string>(out var text) => text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries),
            _ => []
        };
        var texts = items.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!.Trim()).ToList();
        foreach (var text in stripNumbers ? StripListNumbering(texts) : texts)
        {
            list.Add(text);
        }
        return list;
    }

    /// <summary>
    /// Removes a numbering the model wrote in front of the steps - but only a real one: there are at least two steps and EVERY step starts with the
    /// next running number (1, 2, 3 ...). Anything else is content and stays: "1. Stock: pull the lever" as a single step, or steps that merely
    /// begin with a number.
    /// </summary>
    internal static List<string> StripListNumbering(IReadOnlyList<string> steps)
    {
        if (steps.Count < 2)
        {
            return [.. steps];
        }

        var stripped = new List<string>(steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            var match = StepNumber().Match(steps[i]);
            var rest = match.Success ? steps[i][match.Length..].Trim() : string.Empty;
            if (!match.Success || int.Parse(match.Groups["n"].Value) != i + 1 || rest.Length == 0)
            {
                return [.. steps];
            }
            stripped.Add(rest);
        }
        return stripped;
    }

    private static void AddParagraph(JsonArray paragraphs, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            paragraphs.Add(text.Trim());
        }
    }
}
