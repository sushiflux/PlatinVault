using System.Text;
using System.Text.Json;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>
/// What a trophy list needs to know about its translation into one language (D2). State: "original" = that language IS the list's own (the guide language
/// of the game), "complete" = a current translation exists (its texts are shown), "missing" = not translated yet or outdated (the original is shown, see
/// the job). <see cref="Overlay"/> carries the translated texts per trophy key, only when complete.
/// </summary>
public sealed record TrophyTranslationView(
    string Language,
    string SourceLanguage,
    string State,
    IReadOnlyDictionary<string, TrophyTexts>? Overlay,
    GuideTranslationJobInfo? Job)
{
    public bool UseTranslation => State == "complete";

    /// <summary>The language the list is shown in: the translation's when it is used, else the original's.</summary>
    public string ShownLanguage => UseTranslation ? Language : SourceLanguage;

    private static readonly TimeSpan NoticeWindow = TimeSpan.FromMinutes(10);

    public TrophyTranslationDto ToDto() => new(
        Language,
        SourceLanguage,
        State,
        Running: Job?.Running == true,
        Error: State == "missing" && Job is { Running: false, Error: { } error } ? error.ToString() : null,
        ProviderMessage: State == "missing" && Job is { Running: false, Error: not null } ? Job.ProviderMessage : null,
        AiNotice: Job is { Ai.Switches.Count: > 0 } job && (job.Running || job.FinishedAt is { } at && DateTimeOffset.UtcNow - at < NoticeWindow)
            ? AiUsageMapping.NoticeOf(job.Ai.Switches[^1], job.Ai.Research)
            : null);
}

/// <summary>
/// The translation of the trophy list of a game added by hand into the other app language (table <c>game_trophy_translations</c>, one row per game and
/// language). It is part of the guide translation job (<see cref="GuideTranslationService"/>, started by <c>POST /api/games/{slug}/translate</c>):
/// only the readable texts are translated - name, condition, tip, DLC group, category; never keys, grades, points, shares or flags. The row's hash covers
/// exactly what is translated, so a re-researched list is "missing" again until it is translated anew.
/// </summary>
public static class TrophyTranslations
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>One translated trophy as stored in <c>ItemsJson</c>.</summary>
    internal sealed record Item(string Key, string Name, string Description, string Tip, string? Group, string Category);

    private static IEnumerable<GameTrophyEntity> InOrder(IReadOnlyList<GameTrophyEntity> trophies) =>
        trophies.OrderBy(t => t.SortOrder).ThenBy(t => t.TrophyKey, StringComparer.Ordinal);

    /// <summary>The hash of the translated texts (and keys, in list order); the original's other values do not make a translation stale.</summary>
    public static string Hash(IReadOnlyList<GameTrophyEntity> trophies)
    {
        var text = new StringBuilder();
        foreach (var t in InOrder(trophies))
        {
            text.Append(t.TrophyKey).Append('\u001f').Append(t.Name).Append('\u001f').Append(t.Description).Append('\u001f')
                .Append(t.Tip).Append('\u001f').Append(t.GroupName ?? string.Empty).Append('\u001f').Append(t.Category).Append('\u001e');
        }
        return GuideTranslations.HashText(text.ToString());
    }

    /// <summary>Loads the stored row and compares it (never writes). <paramref name="trophies"/> = the current list of the game.</summary>
    public static async Task<TrophyTranslationView> EvaluateAsync(
        CompanionDbContext db, GameEntity game, IReadOnlyList<GameTrophyEntity> trophies, string language, GuideTranslationJobInfo? job, CancellationToken ct)
    {
        var source = AiGuideGeneratorService.GuideLanguage(game);
        var row = language == source || trophies.Count == 0
            ? null
            : await db.GameTrophyTranslations.AsNoTracking().FirstOrDefaultAsync(t => t.GameId == game.Id && t.Language == language, ct);
        return Evaluate(game, trophies, language, row, job);
    }

    /// <summary>The pure comparison of <see cref="EvaluateAsync"/>.</summary>
    public static TrophyTranslationView Evaluate(GameEntity game, IReadOnlyList<GameTrophyEntity> trophies, string language, GameTrophyTranslationEntity? row, GuideTranslationJobInfo? job)
    {
        var source = AiGuideGeneratorService.GuideLanguage(game);
        if (language == source)
        {
            return new TrophyTranslationView(language, source, "original", null, null);
        }
        if (trophies.Count == 0)
        {
            // Nothing to translate: an empty list is the same in every language
            return new TrophyTranslationView(language, source, "complete", new Dictionary<string, TrophyTexts>(), job);
        }
        if (row is null || row.SourceHash != Hash(trophies) || ReadItems(row.ItemsJson) is not { } items)
        {
            return new TrophyTranslationView(language, source, "missing", null, job);
        }
        // Every trophy needs its translation (a row with holes counts as missing, the original is shown instead)
        if (trophies.Any(t => !items.ContainsKey(t.TrophyKey)))
        {
            return new TrophyTranslationView(language, source, "missing", null, job);
        }
        return new TrophyTranslationView(language, source, "complete", items, job);
    }

    /// <summary>Whether the list of a game needs a (new) translation into <paramref name="language"/> (for the translation job and its endpoint).</summary>
    public static bool IsPending(GameEntity game, IReadOnlyList<GameTrophyEntity> trophies, string language, GameTrophyTranslationEntity? row) =>
        Evaluate(game, trophies, language, row, null).State == "missing";

    internal static Dictionary<string, TrophyTexts>? ReadItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var items = JsonSerializer.Deserialize<List<Item>>(json, Json);
            if (items is null)
            {
                return null;
            }
            var result = new Dictionary<string, TrophyTexts>(StringComparer.Ordinal);
            foreach (var item in items.Where(i => i is not null && !string.IsNullOrEmpty(i.Key)))
            {
                result[item.Key] = new TrophyTexts(item.Name ?? string.Empty, item.Description ?? string.Empty, item.Tip ?? string.Empty,
                    string.IsNullOrWhiteSpace(item.Group) ? null : item.Group, item.Category ?? string.Empty);
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string WriteItems(IEnumerable<Item> items) => JsonSerializer.Serialize(items.ToList(), Json);

    /// <summary>
    /// The distinct texts the AI translates. A name with an official English name is not sent when the target is English: the official name is used
    /// (<see cref="Apply"/>) - a translated name would differ from the one the game shows.
    /// </summary>
    public static List<string> TextsToTranslate(IReadOnlyList<GameTrophyEntity> trophies, string targetLanguage)
    {
        var texts = new List<string>();
        foreach (var t in InOrder(trophies))
        {
            if (!(targetLanguage == Lang.English && !string.IsNullOrWhiteSpace(t.OriginalName)))
            {
                texts.Add(t.Name);
            }
            texts.Add(t.Description);
            texts.Add(t.Tip);
            if (t.GroupName is { } group)
            {
                texts.Add(group);
            }
            texts.Add(t.Category);
        }
        return texts.Where(GuideTextExtractor.NeedsTranslation).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The translated items from the AI's map (source text -> translation). A text the map lacks (nothing to translate: numbers, empty) stays as it is;
    /// keys, grades, points and flags are not part of an item at all.
    /// </summary>
    internal static List<Item> Apply(IReadOnlyList<GameTrophyEntity> trophies, IReadOnlyDictionary<string, string> map, string targetLanguage)
    {
        string Tr(string value) => map.TryGetValue(value, out var translated) && !string.IsNullOrWhiteSpace(translated) ? Cut(translated.Trim(), Math.Max(value.Length * 3, 200)) : value;
        return InOrder(trophies).Select(t => new Item(
            t.TrophyKey,
            targetLanguage == Lang.English && !string.IsNullOrWhiteSpace(t.OriginalName) ? t.OriginalName!.Trim() : Tr(t.Name),
            Tr(t.Description),
            Tr(t.Tip),
            t.GroupName is { } group ? Tr(group) : null,
            Tr(t.Category))).ToList();
    }

    private static string Cut(string value, int max) => value.Length > max ? value[..max].TrimEnd() : value;

    /// <summary>Writes (or replaces) the row of a game and language; a game deleted meanwhile is no error (nothing to keep).</summary>
    internal static async Task SaveAsync(CompanionDbContext db, Guid gameId, string language, string itemsJson, string sourceHash, CancellationToken ct)
    {
        var row = await db.GameTrophyTranslations.FirstOrDefaultAsync(t => t.GameId == gameId && t.Language == language, ct);
        if (row is null)
        {
            row = new GameTrophyTranslationEntity { GameId = gameId, Language = language };
            db.GameTrophyTranslations.Add(row);
        }
        row.ItemsJson = itemsJson;
        row.SourceHash = sourceHash;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
