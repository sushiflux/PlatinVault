using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>One trophy / achievement of a researched list, checked and normalised (what is saved into <c>game_trophies</c>).</summary>
public sealed record TrophyListItem(
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

/// <summary>The game facts of an answer (only when they were asked for), checked against plausible ranges.</summary>
public sealed record TrophyListFacts(
    string? ReleaseDate,
    DateTimeOffset? FirstReleaseAt,
    List<string> Developers,
    List<string> Publishers,
    List<string> Genres,
    string? Summary,
    double? HoursComplete,
    List<TrophySourceDto> Sources)
{
    public bool IsEmpty => ReleaseDate is null && Developers.Count == 0 && Publishers.Count == 0 && Genres.Count == 0 && Summary is null && HoursComplete is null;
}

/// <summary>What the reader expects: the platform and version of the game, its title, what was asked for, and how strict the platinum rule is.</summary>
/// <param name="StrictPlatinum">True on the own-subscription way (the player can fix the answer): more than one platinum trophy is an error. The AI research keeps the first.</param>
public sealed record TrophyListExpectations(string Platform, string? Variant, string GameTitle, bool WantTrophies, bool WantFacts, bool StrictPlatinum);

/// <summary>The result of reading an answer. Ok = there is something to save (Errors empty).</summary>
public sealed record TrophyListRead(
    List<ManualIssueDto> Errors,
    List<ManualIssueDto> Warnings,
    List<TrophyListItem> Trophies,
    List<TrophySourceDto> Sources,
    TrophyListFacts? Facts)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Reads the answer to the trophy-list prompt (<c>AiGuideTrophies.cs</c>) - the SAME reader for the AI research and for the answer pasted from the player's
/// own AI subscription. Pure: no database, no provider. The answer is untrusted input: it is parsed tolerantly (code fences, text around it), checked
/// strictly (types, ranges, lengths), every unknown field is ignored and nothing of it is ever executed or rendered as markup.
/// </summary>
public static partial class TrophyList
{
    public const int MaxTrophies = 1000;
    public const int ManyTrophies = 300;
    public const int MaxSources = 12;
    private const int MaxKeyLength = 64;
    private const int MaxName = 200;
    private const int MaxDescription = 600;
    private const int MaxTip = 1500;
    private const int MaxGroup = 120;
    private const int MaxCategory = 60;
    private const int MaxSummary = 1200;
    private const int MaxCompanies = 5;
    private const int MaxGenres = 8;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex KeyPattern();

    /// <summary>Reads a pasted or provider answer (first JSON object of the text).</summary>
    public static TrophyListRead Read(string? text, TrophyListExpectations expect)
    {
        var errors = new List<ManualIssueDto>();
        if (!ManualAnswers.TryRead(text, errors, out var root) || root is null)
        {
            return new TrophyListRead(errors, [], [], [], null);
        }
        return Read(root, expect);
    }

    /// <summary>Reads an already parsed answer.</summary>
    public static TrophyListRead Read(JsonObject root, TrophyListExpectations expect)
    {
        var errors = new List<ManualIssueDto>();
        var warnings = new List<ManualIssueDto>();
        var platform = GamePlatforms.Normalize(expect.Platform) ?? expect.Platform;

        // Which game: an answer about another game is only a warning (names differ in spelling); it has no Steam AppID to compare
        ManualAnswers.CheckGame(root, null, expect.GameTitle, errors, warnings);
        CheckPlatformEcho(root, platform, expect.Variant, warnings);

        var sources = ReadSources(root["sources"]);
        var trophies = new List<TrophyListItem>();

        if (expect.WantTrophies)
        {
            var documented = root["documented"] is not JsonValue documentedValue || !documentedValue.TryGetValue<bool>(out var isDocumented) || isDocumented;
            if (!documented)
            {
                errors.Add(ManualAnswers.Issue("noReliableList", ServerTexts.Trophies.NoReliableList(StringOf(root["reason"]))));
                return new TrophyListRead(errors, warnings, [], sources, null);
            }
            if (root["trophies"] is null)
            {
                errors.Add(ManualAnswers.Issue("noTrophies", ServerTexts.Trophies.NoTrophyArray));
                return new TrophyListRead(errors, warnings, [], sources, null);
            }
            if (root["trophies"] is not JsonArray list)
            {
                errors.Add(ManualAnswers.Issue("trophiesNotList", ServerTexts.Trophies.TrophiesNotList));
                return new TrophyListRead(errors, warnings, [], sources, null);
            }
            if (list.Count == 0)
            {
                errors.Add(ManualAnswers.Issue("noReliableList", ServerTexts.Trophies.NoReliableList(StringOf(root["reason"]))));
                return new TrophyListRead(errors, warnings, [], sources, null);
            }
            if (list.Count > MaxTrophies)
            {
                errors.Add(ManualAnswers.Issue("tooMany", ServerTexts.Trophies.TooMany(list.Count, MaxTrophies)));
                return new TrophyListRead(errors, warnings, [], sources, null);
            }

            trophies = ReadTrophies(list, platform, expect.StrictPlatinum, errors, warnings);
            if (errors.Count == 0 && trophies.Count > ManyTrophies)
            {
                warnings.Add(ManualAnswers.Issue("manyTrophies", ServerTexts.Trophies.ManyTrophies(trophies.Count)));
            }
            if (sources.Count == 0)
            {
                warnings.Add(ManualAnswers.Issue("noSources", ServerTexts.Trophies.NoSources));
            }
        }

        TrophyListFacts? facts = null;
        if (expect.WantFacts)
        {
            facts = root["facts"] is JsonObject factsObject ? ReadFacts(factsObject, warnings) : null;
            if (facts is null || facts.IsEmpty)
            {
                facts = null;
                warnings.Add(ManualAnswers.Issue("noFacts", ServerTexts.Trophies.FactsMissing));
                // Facts were the only thing asked for: without them there is nothing to save
                if (!expect.WantTrophies)
                {
                    errors.Add(ManualAnswers.Issue("noFacts", ServerTexts.Trophies.FactsMissing));
                }
            }
        }

        return new TrophyListRead(errors, warnings, errors.Count == 0 ? trophies : [], sources, errors.Count == 0 ? facts : null);
    }

    private static List<TrophyListItem> ReadTrophies(JsonArray list, string platform, bool strictPlatinum, List<ManualIssueDto> errors, List<ManualIssueDto> warnings)
    {
        var playStation = platform == GamePlatforms.PlayStation;
        var result = new List<TrophyListItem>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var withoutName = 0;
        var madeUnique = 0;
        var pointsDropped = 0;
        var rarityDropped = 0;
        var shortened = 0;
        var missingGrade = new List<string>();

        string Cut(string value, int max)
        {
            if (value.Length <= max)
            {
                return value;
            }
            shortened++;
            return value[..(max - 1)].TrimEnd() + "…";
        }

        foreach (var node in list)
        {
            if (node is not JsonObject entry)
            {
                withoutName++;
                continue;
            }
            var name = OneLine(StringOf(entry["name"]));
            if (name.Length == 0)
            {
                withoutName++;
                continue;
            }
            name = Cut(name, MaxName);
            var originalName = OneLine(StringOf(entry["originalName"]));

            // The id: the given one when it is a clean kebab id, else one made from the (English, if known) name; always unique
            var given = StringOf(entry["id"])?.Trim().ToLowerInvariant();
            var key = given is { Length: > 0 } && IsValidKey(given) ? given : null;
            if (key is null)
            {
                key = KeyFrom(originalName.Length > 0 ? originalName : name, result.Count);
                madeUnique++;
            }
            var unique = MakeUnique(key, keys);
            if (unique != key)
            {
                key = unique;
                madeUnique++;
            }

            string? grade = null;
            if (playStation)
            {
                grade = TrophyTiers.NormalizeGrade(StringOf(entry["grade"]));
                if (grade is null)
                {
                    missingGrade.Add(name);
                }
            }

            int? points = null;
            if (!playStation && entry["points"] is JsonValue pointsValue)
            {
                if (TryNumber(pointsValue, out var p) && p is >= 0 and <= 1000 && Math.Abs(p - Math.Round(p)) < 0.001)
                {
                    points = (int)Math.Round(p);
                }
                else if (!IsNullOrEmpty(pointsValue))
                {
                    pointsDropped++;
                }
            }

            double? rarity = null;
            if (entry["rarityPercent"] is JsonValue rarityValue)
            {
                if (TryNumber(rarityValue, out var r) && double.IsFinite(r) && r is >= 0 and <= 100)
                {
                    rarity = Math.Round(r, 2);
                }
                else if (!IsNullOrEmpty(rarityValue))
                {
                    rarityDropped++;
                }
            }

            var group = OneLine(StringOf(entry["group"]));
            result.Add(new TrophyListItem(
                Key: key,
                Name: name,
                OriginalName: originalName.Length == 0 || originalName == name ? null : Cut(originalName, MaxName),
                Description: Cut(OneLine(StringOf(entry["description"])), MaxDescription),
                Grade: grade,
                Points: points,
                RarityPercent: rarity,
                Hidden: IsTrue(entry["hidden"]),
                Missable: IsTrue(entry["missable"]),
                // "Base game" in any spelling is the base game, not a DLC
                Group: group.Length == 0 || IsBaseGame(group) ? null : Cut(group, MaxGroup),
                Category: Cut(OneLine(StringOf(entry["category"])), MaxCategory),
                Tip: Cut((StringOf(entry["tip"]) ?? string.Empty).Trim(), MaxTip)));
        }

        if (result.Count == 0)
        {
            errors.Add(ManualAnswers.Issue("noUsableEntries", ServerTexts.Trophies.NoUsableEntries));
            return [];
        }
        if (missingGrade.Count > 0)
        {
            errors.Add(ManualAnswers.Issue("missingGrade", ServerTexts.Trophies.MissingGrade(missingGrade.Count, Examples(missingGrade))));
        }

        if (playStation)
        {
            var platinum = result.Where(t => t.Grade == TrophyTiers.Platinum).ToList();
            if (platinum.Count > 1)
            {
                var extra = platinum.Skip(1).ToList();
                if (strictPlatinum)
                {
                    errors.Add(ManualAnswers.Issue("severalPlatinum", ServerTexts.Trophies.SeveralPlatinum(Examples(platinum.Select(t => t.Name).ToList()))));
                }
                else
                {
                    // Owner decision (2026-11-19): a second platinum trophy is only a WARNING on the API way - the first one is kept - and a hard error
                    // only on the own-subscription way (strictPlatinum), where the player can still ask the chat to fix its answer. On the API way there
                    // is nobody to ask, and a second platinum is almost always another version's or a DLC confusion.
                    result = result.Where(t => !extra.Contains(t)).ToList();
                    warnings.Add(ManualAnswers.Issue("extraPlatinumDropped", ServerTexts.Trophies.ExtraPlatinumDropped(Examples(extra.Select(t => t.Name).ToList()))));
                }
            }
            else if (platinum.Count == 0 && missingGrade.Count == 0)
            {
                warnings.Add(ManualAnswers.Issue("noPlatinum", ServerTexts.Trophies.NoPlatinum));
            }
        }

        if (withoutName > 0) warnings.Add(ManualAnswers.Issue("withoutName", ServerTexts.Trophies.WithoutName(withoutName)));
        if (madeUnique > 0) warnings.Add(ManualAnswers.Issue("idsMadeUnique", ServerTexts.Trophies.DuplicateIds(madeUnique)));
        if (pointsDropped > 0) warnings.Add(ManualAnswers.Issue("pointsDropped", ServerTexts.Trophies.PointsDropped(pointsDropped)));
        if (rarityDropped > 0) warnings.Add(ManualAnswers.Issue("rarityDropped", ServerTexts.Trophies.RarityDropped(rarityDropped)));
        if (shortened > 0) warnings.Add(ManualAnswers.Issue("textsShortened", ServerTexts.Trophies.TextsShortened(shortened)));
        return result;
    }

    private static TrophyListFacts ReadFacts(JsonObject facts, List<ManualIssueDto> warnings)
    {
        string? releaseText = null;
        DateTimeOffset? releaseAt = null;
        var release = StringOf(facts["releaseDate"])?.Trim();
        if (!string.IsNullOrEmpty(release))
        {
            if (DateTime.TryParseExact(release, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day.Year is >= 1970 and <= 2100)
            {
                releaseAt = new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.Zero);
                releaseText = release;
            }
            else if (int.TryParse(release, NumberStyles.None, CultureInfo.InvariantCulture, out var year) && release.Length == 4 && year is >= 1970 and <= 2100)
            {
                // Only the year is known: it sorts as 1 January of that year, the text stays the year
                releaseAt = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero);
                releaseText = release;
            }
            else
            {
                warnings.Add(ManualAnswers.Issue("factsValueDropped", ServerTexts.Trophies.FactsValueDropped("releaseDate")));
            }
        }

        List<string> Names(string field, int max, int maxLength)
        {
            if (facts[field] is not JsonArray array)
            {
                return [];
            }
            var names = array.Select(n => OneLine(StringOf(n))).Where(n => n.Length is > 0 and <= 120).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count > max)
            {
                warnings.Add(ManualAnswers.Issue("factsValueDropped", ServerTexts.Trophies.FactsValueDropped(field)));
                names = names.Take(max).ToList();
            }
            return names.Select(n => n.Length > maxLength ? n[..maxLength] : n).ToList();
        }

        string? summary = (StringOf(facts["summary"]) ?? string.Empty).Trim();
        if (summary.Length == 0)
        {
            summary = null;
        }
        else if (summary.Length > MaxSummary)
        {
            summary = summary[..(MaxSummary - 1)].TrimEnd() + "…";
            warnings.Add(ManualAnswers.Issue("factsValueDropped", ServerTexts.Trophies.FactsValueDropped("summary")));
        }

        double? hours = null;
        if (facts["hoursComplete"] is JsonValue hoursValue && !IsNullOrEmpty(hoursValue))
        {
            if (TryNumber(hoursValue, out var h) && double.IsFinite(h) && h is > 0 and <= 2000)
            {
                hours = Math.Round(h, 1);
            }
            else
            {
                warnings.Add(ManualAnswers.Issue("factsValueDropped", ServerTexts.Trophies.FactsValueDropped("hoursComplete")));
            }
        }

        return new TrophyListFacts(releaseText, releaseAt, Names("developers", MaxCompanies, 120), Names("publishers", MaxCompanies, 120), Names("genres", MaxGenres, 60),
            summary, hours, ReadSources(facts["sources"]));
    }

    /// <summary>"platform" of the answer: a different version is worth a warning (the PS4 list in a PS5 game is the classic mistake).</summary>
    private static void CheckPlatformEcho(JsonObject root, string platform, string? variant, List<ManualIssueDto> warnings)
    {
        var echo = StringOf(root["platform"])?.Trim();
        if (string.IsNullOrEmpty(echo))
        {
            return;
        }
        static string Fold(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var expected = ExpectedPlatformValue(platform, variant);
        var accepted = new[] { expected, GamePlatforms.DisplayName(platform, variant) }.Select(Fold).ToHashSet();
        // A PlayStation answer that only says "playstation" names no version: nothing to compare
        if (platform == GamePlatforms.PlayStation)
        {
            accepted.Add(Fold(GamePlatforms.PlayStation));
        }
        if (!accepted.Contains(Fold(echo)))
        {
            warnings.Add(ManualAnswers.Issue("otherPlatform", ServerTexts.Trophies.OtherPlatform(echo.Length > 60 ? echo[..60] : echo, expected)));
        }
    }

    /// <summary>The value the prompt asks the answer to echo in "platform": the PlayStation variant ("ps5"), else the platform id.</summary>
    public static string ExpectedPlatformValue(string platform, string? variant) =>
        GamePlatforms.Normalize(platform) == GamePlatforms.PlayStation && !string.IsNullOrWhiteSpace(variant) ? variant.Trim().ToLowerInvariant() : GamePlatforms.Normalize(platform) ?? platform;

    /// <summary>
    /// The sources of the answer: the same cleaning as the guide's sources (<see cref="GuideSources.FromModel"/>: only absolute http(s) URLs without user
    /// info, "Name – URL" strings, each address once), at most <see cref="MaxSources"/>. The server never fetches them.
    /// </summary>
    internal static List<TrophySourceDto> ReadSources(JsonNode? node) =>
        GuideSources.FromModel(node).Take(MaxSources).Select(s => new TrophySourceDto(s.Title, s.Url)).ToList();

    /// <summary>The key rule of a trophy: lower-case kebab id of 1-64 characters (the guide's <c>apiName</c>, pins and notes name a trophy by it).</summary>
    internal static bool IsValidKey(string key) => KeyPattern().IsMatch(key);

    /// <summary>Adds <paramref name="key"/> to <paramref name="taken"/>; when it is taken already, a suffix (-2, -3, ...) makes it unique. Returns the key added.</summary>
    internal static string MakeUnique(string key, HashSet<string> taken)
    {
        if (taken.Add(key))
        {
            return key;
        }
        var n = 2;
        var stem = key.Length > MaxKeyLength - 4 ? key[..(MaxKeyLength - 4)].TrimEnd('-') : key;
        while (!taken.Add($"{stem}-{n}"))
        {
            n++;
        }
        return $"{stem}-{n}";
    }

    /// <summary>A stable kebab id made from a name ("A New Beginning!" -> "a-new-beginning"); "trophy-N" when nothing is left.</summary>
    internal static string KeyFrom(string name, int index)
    {
        var slug = AiGuideGeneratorService.Slugify(name);
        slug = new string(slug.Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-').ToArray());
        slug = Regex.Replace(slug, "-{2,}", "-").Trim('-');
        if (slug.Length > MaxKeyLength)
        {
            slug = slug[..MaxKeyLength].TrimEnd('-');
        }
        return slug.Length == 0 ? $"trophy-{index + 1}" : slug;
    }

    private static bool IsBaseGame(string group)
    {
        var folded = new string(group.ToLowerInvariant().Where(char.IsLetter).ToArray());
        return folded is "basegame" or "hauptspiel" or "base" or "grundspiel" or "null" or "none";
    }

    private static string Examples(List<string> names) =>
        string.Join(", ", names.Take(5).Select(n => "„" + n + "“")) + (names.Count > 5 ? $" (+{names.Count - 5})" : string.Empty);

    internal static string? StringOf(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string OneLine(string? value) => value is null ? string.Empty : Regex.Replace(value, @"\s+", " ").Trim();

    private static bool IsTrue(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static bool IsNullOrEmpty(JsonValue value) =>
        value.GetValueKind() == System.Text.Json.JsonValueKind.Null || (value.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text));

    private static bool TryNumber(JsonValue value, out double number)
    {
        if (value.TryGetValue<double>(out number))
        {
            return true;
        }
        if (value.TryGetValue<string>(out var text) && double.TryParse(text.Trim().TrimEnd('%', 'G', ' '), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return true;
        }
        number = 0;
        return false;
    }
}

/// <summary>One entry of the merged list: the new trophy, the key it is saved under and - when it continues an existing row - that row (which keeps its tick).</summary>
public sealed record TrophyMergeEntry(TrophyListItem Item, string Key, GameTrophyEntity? Existing);

/// <summary>
/// How a researched list replaces the stored one. Entries = the new list in its order; Retained = old ticked trophies the new list does not contain (kept,
/// appended - a tick is never lost); Removed = old unticked trophies the new list does not contain.
/// </summary>
public sealed record TrophyMergePlan(List<TrophyMergeEntry> Entries, List<GameTrophyEntity> Retained, List<GameTrophyEntity> Removed)
{
    /// <summary>Ticks of the old list that the new list carries on (same key, or the same trophy by name).</summary>
    public int KeptTicks => Entries.Count(e => e.Existing?.UnlockedAt is not null);

    /// <summary>Ticked old trophies that are not in the new list and stay appended.</summary>
    public int RetainedOldTicked => Retained.Count;
}

/// <summary>
/// The pure merge of a re-researched trophy list (re-research keeps the ticks, owner decision 2026-11-19): the same key continues the old row; otherwise a
/// trophy is matched 1:1 by name (<see cref="GuideLinker.PairOneToOne"/>, first by the name, then by the official English name) and the new entry takes the
/// OLD key - so pins, notes and guide references keep working. Unmatched ticked old trophies are kept, unmatched unticked ones are removed.
/// </summary>
public static class TrophyMerge
{
    public static TrophyMergePlan Plan(IReadOnlyList<GameTrophyEntity> old, IReadOnlyList<TrophyListItem> fresh)
    {
        var matched = new GameTrophyEntity?[fresh.Count];
        var oldByKey = old.GroupBy(o => o.TrophyKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var used = new HashSet<GameTrophyEntity>(ReferenceEqualityComparer.Instance);

        // 1) The same key continues the old row
        for (var i = 0; i < fresh.Count; i++)
        {
            if (oldByKey.TryGetValue(fresh[i].Key, out var row) && used.Add(row))
            {
                matched[i] = row;
            }
        }

        // 2) Strictly one to one by name, then by the official English name (a list researched in the other language: the English name is the
        //    original name where one is stored, else the name itself)
        PairBy(t => t.Name, o => o.Name);
        PairBy(t => t.OriginalName ?? t.Name, o => o.OriginalName ?? o.Name);

        void PairBy(Func<TrophyListItem, string?> freshName, Func<GameTrophyEntity, string?> oldName)
        {
            var sources = Enumerable.Range(0, fresh.Count)
                .Where(i => matched[i] is null && !string.IsNullOrWhiteSpace(freshName(fresh[i])))
                .Select(i => GuideLinker.NameCandidate.Of("n" + i, freshName(fresh[i])))
                .Where(c => c.Keys.Count > 0)
                .ToList();
            var remaining = old.Where(o => !used.Contains(o) && !string.IsNullOrWhiteSpace(oldName(o))).ToList();
            var targets = remaining.Select(o => GuideLinker.NameCandidate.Of(o.TrophyKey, oldName(o))).Where(c => c.Keys.Count > 0).ToList();
            if (sources.Count == 0 || targets.Count == 0)
            {
                return;
            }
            foreach (var (source, target) in GuideLinker.PairOneToOne(sources, targets))
            {
                var index = int.Parse(source[1..], CultureInfo.InvariantCulture);
                var row = remaining.First(o => o.TrophyKey == target);
                if (matched[index] is null && used.Add(row))
                {
                    matched[index] = row;
                }
            }
        }

        var retained = old.Where(o => !used.Contains(o) && o.UnlockedAt is not null).OrderBy(o => o.SortOrder).ToList();
        var removed = old.Where(o => !used.Contains(o) && o.UnlockedAt is null).ToList();

        // Keys: a continued row keeps its own; a new trophy keeps its id unless that is taken by a continued or retained row (then "-2", "-3", ...)
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in matched.OfType<GameTrophyEntity>().Concat(retained))
        {
            taken.Add(row.TrophyKey);
        }
        var entries = new List<TrophyMergeEntry>(fresh.Count);
        for (var i = 0; i < fresh.Count; i++)
        {
            if (matched[i] is { } row)
            {
                entries.Add(new TrophyMergeEntry(fresh[i], row.TrophyKey, row));
                continue;
            }
            var key = fresh[i].Key;
            if (!taken.Add(key))
            {
                var n = 2;
                while (!taken.Add($"{key}-{n}"))
                {
                    n++;
                }
                key = $"{key}-{n}";
            }
            entries.Add(new TrophyMergeEntry(fresh[i], key, null));
        }
        return new TrophyMergePlan(entries, retained, removed);
    }
}
