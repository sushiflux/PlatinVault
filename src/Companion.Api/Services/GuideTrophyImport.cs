using System.Text.Json;
using Companion.Api.Endpoints;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>One validated trophy of a guide file: within the database limits; never carries a tick (a file holds no progress).</summary>
public sealed record ParsedTrophy(
    string Key,
    string Name,
    string? OriginalName,
    string Description,
    string? Grade,
    int? Points,
    double? RarityPercent,
    bool Hidden,
    bool Missable,
    string? Group,
    string Category,
    string Tip);

/// <summary>The trophy list part of a guide file of a game added by hand.</summary>
public sealed record ParsedTrophyList(List<ParsedTrophy> Trophies, List<TrophySourceDto> Sources);

/// <summary>
/// Reads and validates the optional <c>trophies</c> / <c>trophySources</c> of a "platinvault-guide" file (format stays version 1: the fields
/// are new and optional). The file may come from anyone, so every field is checked like the rest of <see cref="GuideImport"/>: type, length
/// (database limits), grades only on PlayStation, URL scheme. Ticks are never part of a file (progress is personal).
/// </summary>
public static class GuideTrophyImport
{
    public const int MaxTrophies = 1000;

    private const int MaxKey = 80;
    private const int MaxName = 240;
    private const int MaxDescription = 1000;
    private const int MaxGroup = 120;
    private const int MaxCategory = 60;
    private const int MaxTip = 1500;
    private const int MaxSourceTitle = 200;
    private const int MaxSources = 40;
    private const int MaxPoints = 100_000;

    /// <summary>
    /// <paramref name="platform"/> / <paramref name="variant"/> = the platform of the game in the file (null = Steam). A list on a platform without
    /// a trophy list (Steam, Nintendo Switch) is refused.
    /// </summary>
    public static ParsedTrophyList Parse(JsonElement root, string? platform)
    {
        var hasList = TryGet(root, "trophies", out var listEl) && listEl.ValueKind != JsonValueKind.Null;
        var hasSources = TryGet(root, "trophySources", out var sourcesEl) && sourcesEl.ValueKind != JsonValueKind.Null;
        if (!hasList && !hasSources)
        {
            return new ParsedTrophyList([], []);
        }
        if (hasList && listEl.ValueKind != JsonValueKind.Array)
        {
            throw new GuideImportException(ServerTexts.ManualGames.ImportTrophiesNotList);
        }

        var count = hasList ? listEl.GetArrayLength() : 0;
        if (count > 0 && (GamePlatforms.IsSteam(platform) || !GamePlatforms.HasTrophies(platform)))
        {
            throw new GuideImportException(ServerTexts.ManualGames.ImportTrophiesOnlyManual);
        }
        if (count > MaxTrophies)
        {
            throw new GuideImportException(ServerTexts.ManualGames.ImportTooManyTrophies(count, MaxTrophies));
        }

        var graded = GamePlatforms.HasPlatinumGrade(platform);
        var trophies = new List<ParsedTrophy>(count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var finalKeys = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        if (hasList)
        {
            foreach (var item in listEl.EnumerateArray())
            {
                var path = $"trophies[{index}]";
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw new GuideImportException(ServerTexts.ManualGames.ImportTrophyNotObject(path));
                }

                var given = Text(item, "key", path, MaxKey);
                if (given.Length == 0)
                {
                    throw new GuideImportException(ServerTexts.ManualGames.ImportTrophyFieldMissing(path, "key"));
                }
                // The same key twice in a file stays an error (a mistake of whoever wrote it); a key that only breaks the key rule is rebuilt below
                if (!seen.Add(given))
                {
                    throw new GuideImportException(ServerTexts.ManualGames.ImportTrophyKeyTwice(path, given));
                }
                var name = Text(item, "name", path, MaxName);
                if (name.Length == 0)
                {
                    throw new GuideImportException(ServerTexts.ManualGames.ImportTrophyFieldMissing(path, "name"));
                }
                var originalName = NullIfEmpty(Text(item, "originalName", path, MaxName));

                // The key rule of the research (TrophyList): lower-case kebab id of 1-64 characters. A file may come from anyone, and guide chapters, pins
                // and notes name a trophy by its key - so an invalid key is rebuilt from the (English) name, and a rebuilt one that is taken gets a suffix.
                var key = given.ToLowerInvariant();
                if (!TrophyList.IsValidKey(key))
                {
                    key = TrophyList.KeyFrom(originalName ?? name, index);
                }
                key = TrophyList.MakeUnique(key, finalKeys);

                string? grade = null;
                var gradeText = Text(item, "grade", path, 12);
                if (gradeText.Length > 0)
                {
                    grade = TrophyTiers.NormalizeGrade(gradeText)
                        ?? throw new GuideImportException(ServerTexts.ManualGames.ImportTrophyGradeInvalid(path, gradeText));
                    // Only PlayStation grades its trophies: on any other platform the field means nothing
                    if (!graded) grade = null;
                }

                trophies.Add(new ParsedTrophy(
                    Key: key,
                    Name: name,
                    OriginalName: originalName,
                    Description: Text(item, "description", path, MaxDescription),
                    Grade: grade,
                    Points: Number(item, "points", path, 0, MaxPoints) is { } points ? (int)points : null,
                    RarityPercent: Number(item, "rarityPercent", path, 0, 100),
                    Hidden: Flag(item, "hidden"),
                    Missable: Flag(item, "missable"),
                    Group: NullIfEmpty(Text(item, "group", path, MaxGroup)),
                    Category: Text(item, "category", path, MaxCategory),
                    Tip: Text(item, "tip", path, MaxTip)));
                index++;
            }
        }

        return new ParsedTrophyList(trophies, hasSources ? Sources(sourcesEl) : []);
    }

    private static List<TrophySourceDto> Sources(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new GuideImportException(ServerTexts.ManualGames.ImportSourcesInvalid("trophySources"));
        }
        var result = new List<TrophySourceDto>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var path = $"trophySources[{index++}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new GuideImportException(ServerTexts.ManualGames.ImportSourcesInvalid("trophySources"));
            }
            var url = Text(item, "url", path, 1000);
            // Only real http(s) addresses are kept; anything else (javascript:, data:, relative) is dropped, not an error
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
            {
                continue;
            }
            result.Add(new TrophySourceDto(Text(item, "title", path, MaxSourceTitle), url));
            if (result.Count == MaxSources) break;
        }
        return result;
    }

    private static string Text(JsonElement obj, string name, string path, int max)
    {
        if (!TryGet(obj, name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }
        if (el.ValueKind != JsonValueKind.String)
        {
            throw new GuideImportException(ServerTexts.ManualGames.ImportTrophyFieldInvalid(path, name, max));
        }
        var value = (el.GetString() ?? string.Empty).Trim();
        if (value.Length > max)
        {
            throw new GuideImportException(ServerTexts.ManualGames.ImportTrophyFieldInvalid(path, name, max));
        }
        return value;
    }

    private static double? Number(JsonElement obj, string name, string path, double min, double max)
    {
        if (!TryGet(obj, name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetDouble(out var value) || !double.IsFinite(value) || value < min || value > max)
        {
            throw new GuideImportException(ServerTexts.ManualGames.ImportTrophyNumberInvalid(path, name, $"{min:0} – {max:0}"));
        }
        return value;
    }

    private static bool Flag(JsonElement obj, string name) =>
        TryGet(obj, name, out var el) && el.ValueKind == JsonValueKind.True;

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
