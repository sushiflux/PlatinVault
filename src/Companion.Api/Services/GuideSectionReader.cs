using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Endpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Companion.Api.Services;

/// <summary>
/// Tolerant reading of guide sections. <see cref="GuideSectionDto"/> carries many typed fields; a single wrongly typed value (a model writing
/// <c>"missable": "until chapter 3"</c> on an alert, <c>"level": "2"</c> on a story) used to make the whole chapter unreadable, because the chapter was
/// deserialized in one piece. Here every section is read on its own, and inside a section every field on its own: fields that belong to another section
/// type are dropped, values of the wrong JSON shape are converted where the meaning is obvious (a single string for a list, "2" for 2, "true" for true)
/// and dropped otherwise. A section that still cannot be read costs only itself (logged), never the chapter.
/// </summary>
internal static class GuideSectionReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The typed fields added with the walkthrough/boss/overview/sources blocks and the section types that use them. Every other section type drops them.
    /// The older fields (cards, table, paragraphs, puzzle, failedChapter, kind, columns ...) are deliberately not restricted by type: chapters stored before
    /// this rule may carry them on other types and must stay byte-identical (translation skeletons compare them).
    /// </summary>
    private static readonly Dictionary<string, string[]> Owners = new(StringComparer.OrdinalIgnoreCase)
    {
        ["level"] = ["heading"],
        ["apiName"] = ["alert"],
        ["items"] = ["alert"],
        ["quests"] = ["alert"],
        ["bossId"] = ["boss"],
        ["location"] = ["boss"],
        ["hp"] = ["boss"],
        ["weaknesses"] = ["boss"],
        ["resistances"] = ["boss"],
        ["immunities"] = ["boss"],
        ["attacks"] = ["boss"],
        ["phases"] = ["boss"],
        ["party"] = ["boss"],
        ["tactics"] = ["boss"],
        ["rewards"] = ["boss"],
        ["missable"] = ["boss"],
        ["source"] = ["boss"],
        ["recommendedLevel"] = ["boss", "overview"],
        ["achievements"] = ["boss", "overview"],
        ["counts"] = ["overview"],
        ["keyItems"] = ["overview"],
        ["missables"] = ["overview"],
        ["endsAtPointOfNoReturn"] = ["overview"],
        ["sources"] = ["sources"]
    };

    // camelCase field name -> CLR type of the matching GuideSectionDto parameter (case-insensitive like the serializer)
    private static readonly Dictionary<string, Type> FieldTypes = typeof(GuideSectionDto).GetConstructors()
        .OrderByDescending(c => c.GetParameters().Length).First().GetParameters()
        .ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name!), p => p.ParameterType, StringComparer.OrdinalIgnoreCase);

    // One log line per broken stored section, not one per request (detail pages are read often)
    private static readonly ConcurrentDictionary<int, byte> Logged = new();

    /// <summary>Set once by the host; tests and tools run with the null logger.</summary>
    public static ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>
    /// Stored chapter JSON -> sections. Data that reads in one piece is returned exactly as before (fast path); otherwise section by section, a broken
    /// section is repaired field by field or skipped.
    /// </summary>
    public static List<GuideSectionDto> ReadStored(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<GuideSectionDto>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // fall through to the tolerant path
        }
        catch (NotSupportedException)
        {
        }

        JsonArray array;
        try
        {
            array = JsonNode.Parse(json) as JsonArray ?? [];
        }
        catch (JsonException ex)
        {
            LogOnce(json.GetHashCode(), "A stored chapter is not valid JSON and is shown empty: {Error}", ex.Message);
            return [];
        }

        var result = new List<GuideSectionDto>();
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject node)
            {
                continue;
            }
            var copy = (JsonObject)node.DeepClone();
            if (ReadSection(copy, stripForeign: true) is { } section)
            {
                result.Add(section);
            }
        }
        return result;
    }

    /// <summary>
    /// Makes one section object readable as <see cref="GuideSectionDto"/> in place and returns it, or null when it cannot be (no type, or a value the
    /// field-by-field repair cannot save - then it is logged and the caller drops only this section).
    /// </summary>
    public static GuideSectionDto? ReadSection(JsonObject node, bool stripForeign)
    {
        var type = node["type"] is JsonValue t && t.TryGetValue<string>(out var typeText) ? typeText : null;
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        if (stripForeign)
        {
            StripForeign(node, type);
        }
        if (TryRead(node) is { } fine)
        {
            return fine;
        }

        foreach (var key in node.Select(p => p.Key).ToList())
        {
            if (key.Equals("type", StringComparison.OrdinalIgnoreCase) || !FieldTypes.TryGetValue(key, out var target))
            {
                continue;
            }
            if (FieldReads(type, key, node[key]))
            {
                continue;
            }
            if (Convert(node[key], target) is { } converted && FieldReads(type, key, converted))
            {
                node[key] = converted;
                continue;
            }
            node.Remove(key);
        }

        if (TryRead(node) is { } repaired)
        {
            return repaired;
        }
        LogOnce(node.ToJsonString().GetHashCode(), "A guide section of type {Type} could not be read and was skipped", type);
        return null;
    }

    /// <summary>Removes the typed fields that belong to another section type (see <see cref="Owners"/>).</summary>
    public static void StripForeign(JsonObject node, string type)
    {
        foreach (var key in node.Select(p => p.Key).ToList())
        {
            if (Owners.TryGetValue(key, out var owners) && !owners.Contains(type, StringComparer.OrdinalIgnoreCase))
            {
                node.Remove(key);
            }
        }
    }

    private static GuideSectionDto? TryRead(JsonObject node)
    {
        try
        {
            return node.Deserialize<GuideSectionDto>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static bool FieldReads(string type, string key, JsonNode? value) =>
        TryRead(new JsonObject { ["type"] = type, [key] = value?.DeepClone() }) is not null;

    /// <summary>The obvious conversions between JSON shapes; null when there is none (the field is then dropped).</summary>
    private static JsonNode? Convert(JsonNode? value, Type target)
    {
        var plain = Nullable.GetUnderlyingType(target) ?? target;
        if (plain == typeof(string))
        {
            return value switch
            {
                JsonArray array when array.All(x => x is JsonValue) => JsonValue.Create(string.Join(", ", array.Select(x => x!.ToString()))),
                JsonValue v => JsonValue.Create(v.ToString()),
                _ => null
            };
        }
        if (plain == typeof(int))
        {
            return value is JsonValue v && int.TryParse(v.ToString().Trim(), out var number) ? JsonValue.Create(number) : null;
        }
        if (plain == typeof(bool))
        {
            return value is JsonValue v && bool.TryParse(v.ToString().Trim(), out var flag) ? JsonValue.Create(flag) : null;
        }
        if (plain == typeof(List<string>))
        {
            return value switch
            {
                JsonValue v when !string.IsNullOrWhiteSpace(v.ToString()) => new JsonArray(JsonValue.Create(v.ToString().Trim())),
                JsonArray array => new JsonArray(array.Where(x => x is JsonValue && !string.IsNullOrWhiteSpace(x!.ToString()))
                    .Select(x => (JsonNode?)JsonValue.Create(x!.ToString().Trim())).ToArray()),
                _ => null
            };
        }
        if (plain.IsGenericType && plain.GetGenericTypeDefinition() == typeof(List<>) && value is JsonArray items)
        {
            // A list of objects: keep the entries that read on their own, drop the rest (e.g. one malformed boss attack)
            var element = plain.GetGenericArguments()[0];
            var kept = new JsonArray();
            foreach (var item in items)
            {
                try
                {
                    if (item is JsonObject && item.Deserialize(element, JsonOptions) is not null)
                    {
                        kept.Add(item.DeepClone());
                    }
                }
                catch (JsonException)
                {
                }
                catch (NotSupportedException)
                {
                }
            }
            return kept;
        }
        return null;
    }

    private static void LogOnce(int key, string message, params object?[] args)
    {
        if (Logged.Count < 10_000 && Logged.TryAdd(key, 0))
        {
#pragma warning disable CA2254 // the templates are constants of this class
            Logger.LogWarning(message, args);
#pragma warning restore CA2254
        }
    }
}
