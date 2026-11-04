using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Companion.Api.Endpoints;

namespace Companion.Api.Services;

/// <summary>
/// The sources of a researched text ("where can I read more"): read from the provider's own search metadata where the provider documents it, or from
/// the AI's JSON answer, cleaned up and stored as a "sources" section at the end of a chapter / walkthrough part / lore part, and in the achievement tips.
/// <para>
/// Trust levels (<see cref="GuideSourceDto.Origin"/>): Claude's web search returns real result URLs and cites them on the text blocks
/// (platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool, "Search results" and "Citations"): cited ones are "cited", the other
/// results of the same answer "search". URLs the model writes into its JSON are "ai" (not verified - models invent URLs). Gemini's grounding
/// links (groundingMetadata.groundingChunks[].web.uri) are deliberately NOT stored: they are vertexaisearch.cloud.google.com redirect links, and the
/// Gemini API terms ("Grounding with Google Search", Use Restrictions) forbid caching/storing Links of Grounded Results and require showing them with
/// the Search Suggestions (ai.google.dev/gemini-api/terms#grounding-with-google-search). The server never fetches any of these URLs (no link checking, no SSRF).
/// </para>
/// </summary>
internal sealed record AiSourceMeta(IReadOnlyList<GuideSourceDto> Cited, IReadOnlyList<GuideSourceDto> Searched)
{
    public static AiSourceMeta Empty { get; } = new([], []);

    public bool IsEmpty => Cited.Count == 0 && Searched.Count == 0;
}

/// <summary>The text of one AI answer plus the sources the provider's own metadata names for it (empty for providers/calls without search).</summary>
internal sealed record AiAnswer(string Text, AiSourceMeta Sources);

internal static partial class GuideSources
{
    public const string Cited = "cited";
    public const string Search = "search";
    public const string Ai = "ai";

    /// <summary>The most sources one block keeps (the first ones are the best: provider citations come first).</summary>
    public const int MaxPerBlock = 10;

    /// <summary>The most sources the walkthrough keeps (it is researched in many parts, each with its own pages).</summary>
    public const int MaxPerWalkthrough = 20;

    private const int MaxUrlLength = 2048;
    private const int MaxTitleLength = 200;

    public static readonly IReadOnlyList<string> Origins = [Cited, Search, Ai];

    // "Name – https://...", "Name - https://...", "Name: https://...", "Name (https://...)" as the model writes a source into one string
    [GeneratedRegex(@"https?://[^\s<>""'\)\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlInText();

    /// <summary>
    /// A URL that may be stored and shown as a link: absolute http/https, a host, no user info, no whitespace or control characters, not too long.
    /// Everything else is dropped (javascript:, data:, relative paths, "www.example.com" without a scheme ...). Returns the normalized string.
    /// </summary>
    public static string? SanitizeUrl(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxUrlLength || text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return null;
        }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }
        if (string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }
        return uri.AbsoluteUri;
    }

    /// <summary>A display title: trimmed, one line, not too long; the host of the URL when there is none.</summary>
    private static string CleanTitle(string? title, string url)
    {
        var text = string.Join(' ', (title ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0)
        {
            text = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
        }
        return text.Length > MaxTitleLength ? text[..MaxTitleLength].TrimEnd() : text;
    }

    /// <summary>One source, or null when the URL is not allowed. An unknown origin counts as "ai" (the weakest claim).</summary>
    public static GuideSourceDto? Create(string? title, string? url, string origin)
    {
        var clean = SanitizeUrl(url);
        if (clean is null)
        {
            return null;
        }
        return new GuideSourceDto(CleanTitle(title, clean), clean, Origins.Contains(origin) ? origin : Ai);
    }

    /// <summary>
    /// Sources the AI named in its JSON (field "sources"): objects { "title", "url" } or strings "Name – URL". Always origin "ai", whatever the
    /// answer claims. Strings without a URL are dropped (a name alone is no link to read more).
    /// </summary>
    public static List<GuideSourceDto> FromModel(JsonNode? node)
    {
        var list = new List<GuideSourceDto>();
        if (node is not JsonArray array)
        {
            return list;
        }
        foreach (var item in array)
        {
            switch (item)
            {
                case JsonObject obj:
                    if (Create(Str(obj["title"]) ?? Str(obj["name"]), Str(obj["url"]) ?? Str(obj["uri"]), Ai) is { } fromObject)
                    {
                        list.Add(fromObject);
                    }
                    break;
                case JsonValue value when value.TryGetValue<string>(out var text):
                    if (FromText(text) is { } fromText)
                    {
                        list.Add(fromText);
                    }
                    break;
            }
        }
        return Distinct(list);
    }

    /// <summary>"PowerPyx – https://www.powerpyx.com/..." → title "PowerPyx", the URL; null without a usable URL.</summary>
    public static GuideSourceDto? FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var match = UrlInText().Match(text);
        if (!match.Success)
        {
            return null;
        }
        var title = (text[..match.Index] + text[(match.Index + match.Length)..]).Trim().Trim('–', '-', ':', '(', ')', '|', ' ').Trim();
        return Create(title, match.Value.TrimEnd('.', ','), Ai);
    }

    /// <summary>
    /// Sources of a stored or imported "sources" list: objects { title, url, origin }. <paramref name="trustOrigin"/> = keep the stored origin (a file
    /// the app exported itself); false = everything is "ai" (an AI answer that writes its own sources section cannot claim "cited").
    /// </summary>
    public static List<GuideSourceDto> FromStored(JsonNode? node, bool trustOrigin)
    {
        var list = new List<GuideSourceDto>();
        if (node is not JsonArray array)
        {
            return list;
        }
        foreach (var item in array.OfType<JsonObject>())
        {
            var origin = trustOrigin ? Str(item["origin"]) ?? Ai : Ai;
            if (Create(Str(item["title"]), Str(item["url"]), origin) is { } source)
            {
                list.Add(source);
            }
        }
        return Distinct(list);
    }

    /// <summary>Provider sources first (they are verified), then the AI's own ones that are not already there; at most <paramref name="max"/>.</summary>
    public static List<GuideSourceDto> Merge(IEnumerable<GuideSourceDto> first, IEnumerable<GuideSourceDto> second, int max = MaxPerBlock) =>
        Distinct(first.Concat(second)).Take(max).ToList();

    /// <summary>One entry per URL (ignoring a trailing slash and the fragment); the first occurrence wins, a stronger origin replaces a weaker one.</summary>
    public static List<GuideSourceDto> Distinct(IEnumerable<GuideSourceDto> sources)
    {
        var result = new List<GuideSourceDto>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            var key = Key(source.Url);
            if (index.TryGetValue(key, out var at))
            {
                if (Rank(source.Origin) < Rank(result[at].Origin))
                {
                    result[at] = result[at] with { Origin = source.Origin };
                }
                continue;
            }
            index[key] = result.Count;
            result.Add(source);
        }
        return result;
    }

    private static int Rank(string origin) => origin switch { Cited => 0, Search => 1, _ => 2 };

    private static string Key(string url)
    {
        var hash = url.IndexOf('#');
        var core = hash >= 0 ? url[..hash] : url;
        return core.TrimEnd('/');
    }

    /// <summary>The "sources" section stored at the end of a block (null when there is nothing to list).</summary>
    public static JsonObject? Section(IReadOnlyList<GuideSourceDto> sources)
    {
        if (sources.Count == 0)
        {
            return null;
        }
        var list = new JsonArray();
        foreach (var source in sources.Take(MaxPerBlock))
        {
            list.Add(new JsonObject { ["title"] = source.Title, ["url"] = source.Url, ["origin"] = source.Origin });
        }
        return new JsonObject { ["type"] = "sources", ["sources"] = list };
    }

    /// <summary>
    /// The sources list of one answer: the provider's citations first, then the sources the model named (a named URL that the provider's search also
    /// returned is upgraded to that origin - it exists), then - only to fill up, at most <paramref name="searchFill"/> - further search results of the
    /// answer (a search returns many pages; most were not used, so they never push out what the model or the citations named).
    /// </summary>
    public static List<GuideSourceDto> Combine(AiSourceMeta? provider, IReadOnlyList<GuideSourceDto> model, int max = MaxPerBlock, int searchFill = 4)
    {
        var cited = provider?.Cited ?? [];
        var searched = provider?.Searched ?? [];
        var known = cited.Concat(searched).GroupBy(s => Key(s.Url), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Min(s => Rank(s.Origin)), StringComparer.OrdinalIgnoreCase);
        var upgraded = model.Select(s => known.TryGetValue(Key(s.Url), out var rank) && rank < Rank(s.Origin) ? s with { Origin = rank == 0 ? Cited : Search } : s);
        var list = Distinct(cited.Concat(upgraded));
        var fill = Distinct(searched).Where(s => list.All(x => !string.Equals(Key(x.Url), Key(s.Url), StringComparison.OrdinalIgnoreCase))).Take(searchFill);
        return list.Concat(fill).Take(max).ToList();
    }

    // ---- Claude ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The sources of one Claude answer (Messages API content blocks): the URLs of "web_search_result_location" citations on text blocks are "cited",
    /// every "web_search_result" inside a "web_search_tool_result" block is "search" (an error result has an object instead of a list and is skipped).
    /// Blocks that dynamic filtering nests through the code execution tool carry a "caller" field but have the same shape, so they are read the same way.
    /// </summary>
    public static void ReadClaude(JsonElement content, List<GuideSourceDto> cited, List<GuideSourceDto> searched)
    {
        if (content.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("type", out var typeEl))
            {
                continue;
            }
            var type = typeEl.GetString();
            if (type == "text" && block.TryGetProperty("citations", out var citations) && citations.ValueKind == JsonValueKind.Array)
            {
                foreach (var citation in citations.EnumerateArray())
                {
                    if (citation.ValueKind == JsonValueKind.Object && Prop(citation, "type") == "web_search_result_location" &&
                        Create(Prop(citation, "title"), Prop(citation, "url"), Cited) is { } source)
                    {
                        cited.Add(source);
                    }
                }
            }
            else if (type == "web_search_tool_result" && block.TryGetProperty("content", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var result in results.EnumerateArray())
                {
                    if (result.ValueKind == JsonValueKind.Object && Prop(result, "type") == "web_search_result" &&
                        Create(Prop(result, "title"), Prop(result, "url"), Search) is { } source)
                    {
                        searched.Add(source);
                    }
                }
            }
        }
    }

    private static string? Prop(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
}
