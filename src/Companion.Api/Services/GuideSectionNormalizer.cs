using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Companion.Api.Endpoints;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>What a normalization needs to know beyond the sections: the game's Steam API names and whether stored source origins are trusted.</summary>
internal sealed record NormalizeOptions(IReadOnlySet<string>? KnownApiNames = null, bool TrustSourceOrigin = false)
{
    public static NormalizeOptions Default { get; } = new();

    /// <summary>Data the app wrote itself (its own export, a translation of a stored chapter): origins stay, API names are only syntax-checked.</summary>
    public static NormalizeOptions Trusted { get; } = new(null, TrustSourceOrigin: true);
}

/// <summary>
/// Makes guide sections from untrusted sources (AI answers, imported guide files) safe for the typed frontend. Shared by
/// <see cref="AiGuideGeneratorService"/> and the guide import so both apply exactly the same rules.
/// </summary>
internal static partial class GuideSectionNormalizer
{
    [GeneratedRegex(@"\r?\n\s*\r?\n")]
    private static partial Regex ParagraphBreak();

    // The number a model put in front of a step ("1. Do this", "Step 2: Then that"); only removed when ALL steps of a list carry the running
    // numbers 1, 2, 3 ... (see StripListNumbering) - a single "1. Stock: pull the lever" is content, an ordinal of the game
    [GeneratedRegex(@"^\s*(?:step\s+|schritt\s+)?(?<n>\d{1,3})\s*[.):]\s+", RegexOptions.IgnoreCase)]
    private static partial Regex StepNumber();

    /// <summary>The section types the app can render; everything else is dropped.</summary>
    public static readonly IReadOnlyList<string> KnownTypes = ["alert", "cards_grid", "checklist_table", "story", "puzzle", "heading", "boss", "overview", "sources"];

    // A Steam API name as Steam uses it (letters, digits, "_", "-", ".", ":"); anything else cannot be the name of an achievement
    [GeneratedRegex(@"^[A-Za-z0-9_.:\-]{1,128}$")]
    private static partial Regex ApiNamePattern();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NotSlug();

    /// <summary>
    /// Cell values become strings, rows get an item id, flags become booleans, story sections get a clean paragraph list.
    /// Unknown section types are dropped instead of breaking the whole tab. <paramref name="options"/>: the Steam API names the game really has
    /// (achievement references to other names are dropped; null or empty = only the syntax is checked) and whether stored source origins are trusted
    /// (data the app wrote itself) - an AI answer can never claim that its own sources were "cited".
    /// </summary>
    public static JsonArray Normalize(JsonElement sectionsEl, NormalizeOptions? options = null)
    {
        options ??= NormalizeOptions.Default;
        var bossIds = new HashSet<string>(StringComparer.Ordinal);
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
                NormalizeAlertExtras(node, options);
            }
            else if (type == "boss")
            {
                if (!NormalizeBoss(node, options, bossIds))
                {
                    continue;
                }
            }
            else if (type == "overview")
            {
                if (!NormalizeOverview(node, options))
                {
                    continue;
                }
            }
            else if (type == "sources")
            {
                if (!NormalizeSources(node, options))
                {
                    continue;
                }
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
                    // "ref" = the itemId of a defining row in another chapter; anything but a plain id string (or the row pointing at itself) is dropped
                    SetOrRemove(row, "ref", CleanRef(row["ref"], row["itemId"]?.ToString()));
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

            // Final guard per section: the frontend deserializes into GuideSectionDto. Fields of another section type are dropped, values of the
            // wrong shape converted or dropped; a section that still cannot be read is skipped - one bad field never costs the whole chapter.
            if (GuideSectionReader.ReadSection(node, stripForeign: true) is null)
            {
                continue;
            }
            result.Add(node);
        }
        return result;
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

    /// <summary>
    /// A sub-heading needs a title (the optional htmlContent is a one-line lead below it - in the walkthrough the orientation sentence). "level" 2 = an
    /// area heading below a part heading; every other value means level 1 and is not stored (older data has no level and must stay byte-identical,
    /// e.g. for the skeleton check of a translation).
    /// </summary>
    private static bool NormalizeHeading(JsonObject node)
    {
        var title = node["title"]?.ToString().Trim();
        if (string.IsNullOrEmpty(title))
        {
            return false;
        }

        if (ReadInt(node["level"]) == 2)
        {
            node["level"] = 2;
        }
        else
        {
            node.Remove("level");
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

    /// <summary>
    /// The extra fields of an alert: "apiName" (kept only when it is a Steam API name the game has; the casing of the official list wins) and "items"
    /// (a clean list, e.g. what to do before a point of no return).
    /// </summary>
    private static void NormalizeAlertExtras(JsonObject node, NormalizeOptions options)
    {
        if (node.ContainsKey("apiName"))
        {
            SetOrRemove(node, "apiName", KnownApiName(node["apiName"], options));
        }
        if (node.ContainsKey("items"))
        {
            SetOrRemove(node, "items", ReadList(node["items"], stripNumbers: false));
        }
        if (node.ContainsKey("quests"))
        {
            // { title, availableFrom?, missable? (a time window as text), reward?, ref? }; a plain string is a title. Entries without a title are dropped.
            var quests = new JsonArray();
            IEnumerable<JsonNode?> items = node["quests"] as JsonArray ?? [];
            foreach (var item in items)
            {
                if (item is JsonValue && Clean(item) is { } name)
                {
                    quests.Add(new JsonObject { ["title"] = name });
                    continue;
                }
                if (item is not JsonObject quest || (Clean(quest["title"]) ?? Clean(quest["name"])) is not { } title)
                {
                    continue;
                }
                var clean = new JsonObject { ["title"] = title };
                foreach (var key in new[] { "availableFrom", "missable", "reward", "ref" })
                {
                    // "missable": true from a model that ignored the text form becomes a short marker; false means nothing to warn about
                    var value = key == "missable" && ReadBool(quest[key]) is { } flag ? (flag ? Lang.Pick("verpassbar", "missable") : null) : (key == "ref" ? CleanRef(quest[key], null) : Clean(quest[key]));
                    if (value is not null) clean[key] = value;
                }
                quests.Add(clean);
            }
            SetOrRemove(node, "quests", quests);
        }
    }

    /// <summary>The longest item id a link may name (the same limit as the tick marks, see GuideTransferEndpoints).</summary>
    internal const int MaxRefLength = 200;

    /// <summary>
    /// A cross-chapter reference as the model wrote it: a trimmed id string without control characters, at most <see cref="MaxRefLength"/> long and different from the
    /// row's own id; null otherwise. Whether the target exists is decided later by <see cref="GuideLinker"/>.
    /// </summary>
    internal static string? CleanRef(JsonNode? value, string? ownItemId)
    {
        var text = Clean(value);
        if (text is null || text.Length > MaxRefLength || text.Any(char.IsControl) || string.Equals(text, ownItemId, StringComparison.Ordinal))
        {
            return null;
        }
        return text;
    }

    /// <summary>The API name as the official list writes it, or null (no API name, wrong syntax, or a name the game's list does not have).</summary>
    internal static string? KnownApiName(JsonNode? value, NormalizeOptions options)
    {
        var name = Clean(value);
        if (name is null || !ApiNamePattern().IsMatch(name))
        {
            return null;
        }
        if (options.KnownApiNames is not { Count: > 0 } known)
        {
            return name;
        }
        return known.Contains(name) ? name : known.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonArray ApiNames(JsonNode? value, NormalizeOptions options)
    {
        var list = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<JsonNode?> items = value switch
        {
            JsonArray array => array,
            JsonValue single => [single],
            _ => []
        };
        foreach (var item in items)
        {
            if (KnownApiName(item, options) is { } name && seen.Add(name))
            {
                list.Add(name);
            }
        }
        return list;
    }

    /// <summary>
    /// A boss card (text only). The fields sit on the section itself (a model that nested them in "boss" is tolerated). It needs a name (title); bossId
    /// becomes kebab-case and unique within the chapter (from the name when missing). HP entries: the value as the source states it plus the source -
    /// an entry without a value or without a source is dropped (numbers are never invented); "not documented" as the value needs no source. Lists
    /// become clean string lists, attacks objects { name, note }, achievements only API names the game has.
    /// </summary>
    private static bool NormalizeBoss(JsonObject node, NormalizeOptions options, HashSet<string> bossIds)
    {
        if (node["boss"] is JsonObject nested)
        {
            foreach (var pair in nested.ToList())
            {
                if (node[pair.Key] is null && pair.Value is not null)
                {
                    node[pair.Key] = pair.Value.DeepClone();
                }
            }
        }
        node.Remove("boss");

        var title = Clean(node["title"]) ?? Clean(node["name"]);
        node.Remove("name");
        if (title is null)
        {
            return false;
        }
        node["title"] = title;

        var id = Slug(Clean(node["bossId"]) ?? title);
        if (id.Length == 0)
        {
            id = "boss";
        }
        var unique = id;
        for (var counter = 2; !bossIds.Add(unique); counter++)
        {
            unique = $"{id}-{counter}";
        }
        node["bossId"] = unique;

        foreach (var key in new[] { "location", "recommendedLevel", "party", "source", "htmlContent" })
        {
            SetOrRemove(node, key, Clean(node[key]));
        }
        foreach (var key in new[] { "weaknesses", "resistances", "immunities", "phases", "rewards" })
        {
            SetOrRemove(node, key, ReadList(node[key], stripNumbers: false));
        }
        SetOrRemove(node, "tactics", ReadList(node["tactics"], stripNumbers: true));

        var hp = new JsonArray();
        IEnumerable<JsonNode?> hpItems = node["hp"] switch
        {
            JsonArray array => array,
            JsonObject single => [single],
            _ => []
        };
        foreach (var entry in hpItems.OfType<JsonObject>())
        {
            var value = Clean(entry["value"]);
            var source = Clean(entry["source"]);
            if (value is null || (source is null && !IsNotDocumented(value)))
            {
                continue;
            }
            var clean = new JsonObject { ["value"] = value };
            if (Clean(entry["appliesTo"]) is { } appliesTo) clean["appliesTo"] = appliesTo;
            if (source is not null) clean["source"] = source;
            hp.Add(clean);
        }
        SetOrRemove(node, "hp", hp);

        var attacks = new JsonArray();
        IEnumerable<JsonNode?> attackItems = node["attacks"] as JsonArray ?? [];
        foreach (var attack in attackItems)
        {
            switch (attack)
            {
                case JsonObject obj when Clean(obj["name"]) is { } name:
                    var item = new JsonObject { ["name"] = name };
                    if (Clean(obj["note"]) is { } note) item["note"] = note;
                    attacks.Add(item);
                    break;
                case JsonValue value when Clean(value) is { } text:
                    attacks.Add(new JsonObject { ["name"] = text });
                    break;
            }
        }
        SetOrRemove(node, "attacks", attacks);

        SetOrRemove(node, "missable", ReadBool(node["missable"]));
        SetOrRemove(node, "achievements", ApiNames(node["achievements"], options));
        return true;
    }

    /// <summary>
    /// "At a glance" of a walkthrough part: recommended level, counts { label, count } (count a whole number, absent when the source names none),
    /// achievements (API names the game has), key items, missables and whether the part ends at a point of no return. Without any content besides the title
    /// it is dropped; without a title it gets "Auf einen Blick" / "At a glance".
    /// </summary>
    private static bool NormalizeOverview(JsonObject node, NormalizeOptions options)
    {
        SetOrRemove(node, "recommendedLevel", Clean(node["recommendedLevel"]));
        SetOrRemove(node, "keyItems", ReadList(node["keyItems"], stripNumbers: false));
        SetOrRemove(node, "missables", ReadList(node["missables"], stripNumbers: false));
        SetOrRemove(node, "achievements", ApiNames(node["achievements"], options));

        var counts = new JsonArray();
        IEnumerable<JsonNode?> countItems = node["counts"] as JsonArray ?? [];
        foreach (var entry in countItems.OfType<JsonObject>())
        {
            if (Clean(entry["label"]) is not { } label)
            {
                continue;
            }
            var item = new JsonObject { ["label"] = label };
            if (ReadInt(entry["count"]) is { } count and >= 0)
            {
                item["count"] = count;
            }
            counts.Add(item);
        }
        SetOrRemove(node, "counts", counts);
        SetOrRemove(node, "endsAtPointOfNoReturn", ReadBool(node["endsAtPointOfNoReturn"]));

        var hasContent = new[] { "recommendedLevel", "keyItems", "missables", "achievements", "counts", "endsAtPointOfNoReturn" }.Any(node.ContainsKey);
        if (!hasContent)
        {
            return false;
        }
        node["title"] = Clean(node["title"]) ?? Lang.Pick("Auf einen Blick", "At a glance");
        return true;
    }

    /// <summary>
    /// The sources of a chapter: { title, url, origin } objects (or "Name – URL" strings). Only absolute http(s) URLs survive (GuideSources.SanitizeUrl);
    /// the origin is kept only when the data is trusted, otherwise every entry is "ai". Without a usable entry the section is dropped.
    /// </summary>
    private static bool NormalizeSources(JsonObject node, NormalizeOptions options)
    {
        var list = new List<GuideSourceDto>();
        if (node["sources"] is JsonArray array)
        {
            foreach (var item in array)
            {
                var source = item switch
                {
                    JsonObject obj => GuideSources.Create(Clean(obj["title"]) ?? Clean(obj["name"]), Clean(obj["url"]) ?? Clean(obj["uri"]),
                        options.TrustSourceOrigin ? Clean(obj["origin"]) ?? GuideSources.Ai : GuideSources.Ai),
                    JsonValue value when value.TryGetValue<string>(out var text) => GuideSources.FromText(text),
                    _ => null
                };
                if (source is not null)
                {
                    list.Add(source);
                }
            }
        }
        var clean = GuideSources.Distinct(list).Take(GuideSources.MaxPerWalkthrough).ToList();
        if (clean.Count == 0)
        {
            return false;
        }
        var result = new JsonArray();
        foreach (var source in clean)
        {
            result.Add(new JsonObject { ["title"] = source.Title, ["url"] = source.Url, ["origin"] = source.Origin });
        }
        node["sources"] = result;
        SetOrRemove(node, "title", Clean(node["title"]));
        return true;
    }

    internal static bool IsNotDocumented(string value)
    {
        var lower = value.ToLowerInvariant();
        return lower.Contains("dokumentiert") || lower.Contains("documented") || lower.Contains("unbekannt") || lower.Contains("unknown");
    }

    internal static string Slug(string value) => NotSlug().Replace(value.ToLowerInvariant()
        .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss"), "-").Trim('-');

    private static void SetOrRemove(JsonObject node, string key, string? value)
    {
        if (value is null) node.Remove(key);
        else node[key] = value;
    }

    private static void SetOrRemove(JsonObject node, string key, bool? value)
    {
        if (value is null) node.Remove(key);
        else node[key] = value.Value;
    }

    private static void SetOrRemove(JsonObject node, string key, JsonArray value)
    {
        if (value.Count == 0) node.Remove(key);
        else node[key] = value;
    }

    private static int? ReadInt(JsonNode? value)
    {
        if (value is not JsonValue v)
        {
            return null;
        }
        if (v.TryGetValue<int>(out var number))
        {
            return number;
        }
        if (v.TryGetValue<double>(out var real) && real == Math.Floor(real) && real is >= int.MinValue and <= int.MaxValue)
        {
            return (int)real;
        }
        return v.TryGetValue<string>(out var text) && int.TryParse(text.Trim(), out var parsed) ? parsed : null;
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
