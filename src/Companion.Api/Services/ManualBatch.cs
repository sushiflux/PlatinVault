using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Companion.Api.Localization;

namespace Companion.Api.Services;

// ---- Batch mode of the manual (own AI subscription) flow ------------------------------------------------------------------------------
// One prompt asks the chat for many chapters; the collected answers (several pastes appended, or one file) go into ONE field. This file holds the
// two pure parts: the chapter list (validation, stable keys) and the reader that finds every chapter object in a messy pasted text.

/// <summary>A validated batch chapter: the key is always set and unique within the list.</summary>
internal sealed record BatchChapter(int Index, string Key, string Title, string Focus, string? TopicId, string? SourceSituation, string? Icon, Guid? TabId, bool Replace = false)
{
    /// <summary>The key in a comparable form: "k07", "K7" and "K-07" style prefixes compare equal ("K07" == "K7").</summary>
    public string KeyForm => ManualBatchChapters.KeyForm(Key);
}

internal static partial class ManualBatchChapters
{
    public const int MaxChapters = 40;
    public const int MaxTitle = 240;
    public const int MaxFocus = 4000;
    private const int MaxKey = 20;

    [GeneratedRegex(@"^[A-Za-z0-9_\-]{1,20}$")]
    private static partial Regex ValidKey();

    [GeneratedRegex(@"^(?<p>[A-Z_\-]*)(?<n>\d+)$")]
    private static partial Regex PrefixAndNumber();

    /// <summary>Upper-case and without leading zeros of the number ("K07" -> "K7"), so a chat that writes "K7" still finds chapter K07.</summary>
    public static string KeyForm(string key)
    {
        var upper = key.Trim().ToUpperInvariant();
        var match = PrefixAndNumber().Match(upper);
        if (!match.Success)
        {
            return upper;
        }
        var digits = match.Groups["n"].Value.TrimStart('0');
        return match.Groups["p"].Value + (digits.Length == 0 ? "0" : digits);
    }

    /// <summary>
    /// Validates the list the browser sends and gives every chapter a unique key: an explicit valid key is kept (the first one wins), a missing, invalid or
    /// duplicate key becomes K01, K02, ... by position (next free number when that one is taken). The prompt needs a research assignment, check and save do not.
    /// </summary>
    public static Msg? Normalize(IReadOnlyList<ManualBatchChapter>? list, bool forPrompt, out List<BatchChapter> chapters)
    {
        chapters = [];
        if (list is null || list.Count == 0 || list.Count > MaxChapters)
        {
            return ServerTexts.Manual.BatchChaptersInvalid(MaxChapters);
        }

        var taken = new HashSet<string>(StringComparer.Ordinal);
        var keys = new string?[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            // A chapter that replaces a stored one (TabId + Replace) needs no assignment: the server builds it from the stored chapter (ResolveStoredChapters)
            if (item is null || string.IsNullOrWhiteSpace(item.Title) || (forPrompt && string.IsNullOrWhiteSpace(item.Focus) && !(item.TabId is not null && item.Replace == true)))
            {
                return ServerTexts.Manual.BatchChaptersInvalid(MaxChapters);
            }
            if (item.Title.Trim().Length > MaxTitle)
            {
                return ServerTexts.Manual.BatchFieldTooLong(i + 1, "title", MaxTitle);
            }
            if ((item.Focus?.Length ?? 0) > MaxFocus)
            {
                return ServerTexts.Manual.BatchFieldTooLong(i + 1, "focus", MaxFocus);
            }
            if ((item.SourceSituation?.Length ?? 0) > MaxFocus)
            {
                return ServerTexts.Manual.BatchFieldTooLong(i + 1, "sourceSituation", MaxFocus);
            }
            if ((item.Icon?.Length ?? 0) > 100)
            {
                return ServerTexts.Manual.BatchFieldTooLong(i + 1, "icon", 100);
            }
            if ((item.TopicId?.Length ?? 0) > 80)
            {
                return ServerTexts.Manual.BatchFieldTooLong(i + 1, "topicId", 80);
            }

            var key = item.Key?.Trim();
            if (!string.IsNullOrEmpty(key) && key.Length <= MaxKey && ValidKey().IsMatch(key) && taken.Add(KeyForm(key)))
            {
                keys[i] = key;
            }
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (keys[i] is not null)
            {
                continue;
            }
            var number = i + 1;
            while (!taken.Add(KeyForm($"K{number:D2}")))
            {
                number++;
            }
            keys[i] = $"K{number:D2}";
        }

        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            chapters.Add(new BatchChapter(
                i, keys[i]!, item.Title!.Trim(), (item.Focus ?? string.Empty).Trim(),
                string.IsNullOrWhiteSpace(item.TopicId) ? null : item.TopicId.Trim(),
                string.IsNullOrWhiteSpace(item.SourceSituation) ? null : item.SourceSituation.Trim(),
                string.IsNullOrWhiteSpace(item.Icon) ? null : item.Icon.Trim(),
                item.TabId is { } tab && tab != Guid.Empty ? tab : null,
                item.Replace == true));
        }
        return null;
    }
}

/// <summary>How a JSON object of the pasted text turned out.</summary>
internal enum BatchFailure
{
    None,
    CutOff,
    Invalid
}

/// <summary>One chapter-like JSON object found in the pasted text (Order = position among the kept objects, from 1). Node is null when it could not be read.</summary>
internal sealed record BatchObject(int Order, JsonObject? Node, BatchFailure Failure, List<ManualIssueDto> Errors, string? KeyHint, string? TitleHint);

internal sealed record BatchRead(List<BatchObject> Objects, bool TooMany);

/// <summary>
/// Finds ALL chapter objects in a text the user collected from a chat: code blocks, bare objects, a JSON array, a { "chapters": [...] } wrapper, text in
/// between, CRLF, a cut-off object at the end (or in the middle of several pastes). The prompt's own patterns ("...", "mdi:...", fragments of the
/// building-block list) are recognised and ignored, so copying the WHOLE chat page including the prompt creates neither fake chapters nor errors.
/// </summary>
internal static partial class ManualBatchReader
{
    public const int MaxKilobytes = 4000;
    public const int MaxObjects = 200;

    // Raw objects looked at (patterns and fragments included) before giving up: bounds the work for a 4 MB text of tiny objects
    private const int MaxScanned = 3000;

    // A line that starts a chapter object ({ "game" / "steamAppId" / "chapter" ...): where the next object certainly begins after a cut-off one
    [GeneratedRegex(@"^[ \t]*(?<b>\{)\s*""(?:game|steamAppId|chapter)""\s*:", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Marker();

    [GeneratedRegex(@"""chapter""\s*:\s*""(?<v>[^""\\]{1,40})""")]
    private static partial Regex ChapterKey();

    [GeneratedRegex(@"""title""\s*:\s*""(?<v>(?:[^""\\]|\\.){1,240})""")]
    private static partial Regex TitleValue();

    [GeneratedRegex(@":\s*""(?:\.\.\.|…|mdi:\.\.\.)""")]
    private static partial Regex EllipsisValue();

    // The example reasons of the "does not exist" pattern in the single-chapter prompt (ChapterApplicability.PromptRule); a pasted prompt must not look like an answer
    private static readonly string[] PatternReasons = ["Ein kurzer Satz, warum (was das Spiel stattdessen hat)", "One short sentence why (what the game has instead)"];

    private static readonly string[] ChapterKeys = ["chapter", "sections", "notApplicable", "game", "steamAppId", "title", "format", "topics", "extraChapters", "chapters"];

    public static BatchRead Read(string text)
    {
        text = text.Replace("\r\n", "\n");
        var markers = Marker().Matches(text).Select(m => m.Groups["b"].Index).ToArray();
        var objects = new List<BatchObject>();
        var order = 0;
        var scanned = 0;
        var i = 0;

        while (i < text.Length)
        {
            var start = NextStart(text, i);
            if (start < 0)
            {
                break;
            }
            if (++scanned > MaxScanned || objects.Count > MaxObjects)
            {
                return new BatchRead(objects, TooMany: true);
            }

            var end = BalancedEnd(text, start, out _);
            var next = NextMarker(markers, start);

            if (end >= 0)
            {
                var span = text.Substring(start, end - start + 1);
                if (TryParse(span, out var node))
                {
                    Accept(node, objects, ref order);
                    i = end + 1;
                    continue;
                }
                // Balanced but not valid JSON. When another chapter object starts INSIDE it, the string quoting was thrown off by a cut-off object
                // before (an unterminated string flips the quote parity): split at that start so the later object is not swallowed
                if (next >= 0 && next < end)
                {
                    Fail(text[start..next], objects, ref order);
                    i = next;
                    continue;
                }
                Fail(span, objects, ref order);
                i = end + 1;
                continue;
            }

            // Nothing closes it: the rest of the text, or up to the next chapter object that begins after it
            var limit = next >= 0 ? next : text.Length;
            Fail(text[start..limit], objects, ref order);
            i = limit;
        }
        return new BatchRead(objects, TooMany: false);
    }

    private static bool TryParse(string span, out JsonObject node)
    {
        try
        {
            node = JsonNode.Parse(span, documentOptions: ManualAnswers.Lenient) as JsonObject ?? new JsonObject();
            return true;
        }
        catch (JsonException)
        {
            node = new JsonObject();
            return false;
        }
    }

    private static void Accept(JsonObject node, List<BatchObject> objects, ref int order)
    {
        // { "chapters": [ {...}, {...} ] } as a wrapper around the chapter objects (a whole-guide file has "format" and is reported as such)
        if (node["sections"] is null && node["format"] is null && node["chapters"] is JsonArray wrapped)
        {
            foreach (var child in wrapped.OfType<JsonObject>())
            {
                AcceptOne(child, objects, ref order);
            }
            return;
        }
        AcceptOne(node, objects, ref order);
    }

    private static void AcceptOne(JsonObject obj, List<BatchObject> objects, ref int order)
    {
        if (IsFragment(obj) || IsPattern(obj, 0))
        {
            return;
        }
        order++;
        objects.Add(new BatchObject(order, obj, BatchFailure.None, [], StringOf(obj["chapter"]), StringOf(obj["title"])));
    }

    private static string? StringOf(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }
        var text = value.TryGetValue<string>(out var s) ? s : value.TryGetValue<int>(out var n) ? n.ToString() : null;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>A piece of the prompt's building-block list ({ "type": "alert", ... }) or an object with none of the fields of a chapter, blueprint or guide.</summary>
    private static bool IsFragment(JsonObject obj) =>
        (obj.ContainsKey("type") && !obj.ContainsKey("sections")) || !ChapterKeys.Any(obj.ContainsKey);

    /// <summary>The prompt's own pattern: a value that is a placeholder ("...", "…", "mdi:...", "&lt;key ...&gt;") anywhere in the object.</summary>
    private static bool IsPattern(JsonNode? node, int depth)
    {
        if (depth > 14)
        {
            return false;
        }
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var s):
                var t = s.Trim();
                return t is "..." or "…" || PatternReasons.Contains(t) || t.StartsWith("mdi:...", StringComparison.Ordinal) || (t.Length is > 2 and < 80 && t[0] == '<' && t[^1] == '>');
            case JsonArray array:
                return array.Any(x => IsPattern(x, depth + 1));
            case JsonObject obj:
                return obj.Any(p => IsPattern(p.Value, depth + 1));
            default:
                return false;
        }
    }

    /// <summary>An object that could not be read: the prompt's pattern (silently ignored) or a real chapter that is cut off or broken (reported).</summary>
    private static void Fail(string span, List<BatchObject> objects, ref int order)
    {
        var balanced = BalancedEnd(span, 0, out var openDepth) >= 0;
        if (IsPatternSpan(span))
        {
            return;
        }

        var errors = new List<ManualIssueDto>();
        BatchFailure failure;
        if (!balanced)
        {
            failure = BatchFailure.CutOff;
            errors.Add(ManualAnswers.Issue("cutOff", ServerTexts.Manual.BatchCutOff(Math.Max(1, openDepth))));
        }
        else
        {
            failure = BatchFailure.Invalid;
            ManualAnswers.TryRead(span, errors, out _);
            if (errors.Count == 0)
            {
                errors.Add(ManualAnswers.Issue("invalidJson", ServerTexts.Manual.InvalidJson(1, 1, string.Empty, ServerTexts.Manual.HintGeneric.ToString())));
            }
        }

        order++;
        var key = ChapterKey().Match(span);
        var title = TitleValue().Match(span);
        objects.Add(new BatchObject(order, null, failure, errors, key.Success ? key.Groups["v"].Value.Trim() : null, title.Success ? Unescape(title.Groups["v"].Value) : null));
    }

    private static string Unescape(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>("\"" + value + "\"") ?? value;
        }
        catch (JsonException)
        {
            return value;
        }
    }

    /// <summary>A span that cannot be parsed and is the prompt's pattern: "..." as a value, or a bare "..." / "…" outside of strings ([...]).</summary>
    private static bool IsPatternSpan(string span)
    {
        if (EllipsisValue().IsMatch(span))
        {
            return true;
        }
        var inString = false;
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
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
            if (c == '"')
            {
                inString = true;
            }
            else if (c == '…' || (c == '.' && i + 2 < span.Length && span[i + 1] == '.' && span[i + 2] == '.'))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Where an object starts: a "{" followed by a key (or closing at once) - a sentence like "{in curly brackets}" is not taken for one.</summary>
    private static int NextStart(string text, int from)
    {
        for (var i = text.IndexOf('{', from); i >= 0; i = text.IndexOf('{', i + 1))
        {
            var next = i + 1;
            while (next < text.Length && char.IsWhiteSpace(text[next]))
            {
                next++;
            }
            if (next < text.Length && text[next] is '"' or '}')
            {
                return i;
            }
        }
        return -1;
    }

    private static int NextMarker(int[] markers, int after)
    {
        var index = Array.BinarySearch(markers, after);
        index = index >= 0 ? index + 1 : ~index;
        return index < markers.Length ? markers[index] : -1;
    }

    /// <summary>Index of the "}" that closes the object starting at <paramref name="start"/> (strings and escapes respected), or -1; openDepth = brackets still open at the end.</summary>
    private static int BalancedEnd(string text, int start, out int openDepth)
    {
        var depth = 0;
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
                case '{': case '[': depth++; break;
                case ']': depth = Math.Max(0, depth - 1); break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        openDepth = 0;
                        return i;
                    }
                    break;
            }
        }
        openDepth = Math.Max(0, depth);
        return -1;
    }
}
