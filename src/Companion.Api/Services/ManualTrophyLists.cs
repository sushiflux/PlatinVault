using System.Text.Json;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>The readable texts of one trophy in another language (what a translation of the list replaces): never keys, grades, points or flags.</summary>
public sealed record TrophyTexts(string Name, string Description, string Tip, string? Group, string Category);

/// <summary>
/// Builds the answer of <c>GET /api/games/{slug}/trophies</c> from the tables of a manual game: the one place that turns
/// <see cref="GameTrophyEntity"/> rows (+ the notes of the player) into <see cref="ManualTrophyListDto"/>, so the endpoint, the tick and the
/// result of a (re-)research all show the same list. Pure apart from <see cref="BuildAsync"/>, which only reads.
/// </summary>
public static class ManualTrophyLists
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Longest trophy key (the column <c>game_trophies.TrophyKey</c>); a note names its trophy by it.</summary>
    public const int MaxKeyLength = 80;

    /// <summary>The <c>app_settings</c> keys of the player's notes on a game's trophies start with this: <c>note.g.{gameId:N}.{trophyKey}</c>.</summary>
    public static string NotePrefix(Guid gameId) => $"note.g.{gameId:N}.";

    /// <summary>Loads the game, its trophies and notes and builds the list; null when the game does not exist. Never writes.</summary>
    /// <param name="language">The language the texts are wanted in (null = of the request); the list itself is in the guide language of the game.</param>
    /// <param name="translations">The guide translation service (its job state is part of the answer); null = no running job is known.</param>
    public static async Task<ManualTrophyListDto?> BuildAsync(CompanionDbContext db, Guid gameId, string? language, GuideTranslationService? translations, CancellationToken ct)
    {
        var game = await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, ct);
        if (game is null)
        {
            return null;
        }
        var trophies = await db.GameTrophies.AsNoTracking()
            .Where(t => t.GameId == gameId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.TrophyKey)
            .ToListAsync(ct);
        var prefix = NotePrefix(gameId);
        var notes = NotesByKey((await db.AppSettings.AsNoTracking().Where(x => x.Key.StartsWith(prefix)).ToListAsync(ct))
            .Select(x => (Key: x.Key[prefix.Length..], x.Value, x.UpdatedAt)));

        // D2: the list is shown in the wanted language only when its translation is complete (like a guide); until then the original is shown and
        // the state says "missing" (plus the running job, if any). The overlay carries the translated texts per trophy key.
        var wanted = Lang.NormalizeOr(language, Lang.Current);
        var view = await TrophyTranslations.EvaluateAsync(db, game, trophies, wanted, translations?.GetJob(game.Id, wanted), ct);
        return Build(game, trophies, notes, view.ShownLanguage, view.Overlay, view.ToDto());
    }

    /// <summary>
    /// The notes of a list by trophy key. Keys that differ only in case (written before the spelling of the key was fixed) must not throw:
    /// the most recently saved note wins.
    /// </summary>
    public static Dictionary<string, string> NotesByKey(IEnumerable<(string Key, string Value, DateTimeOffset UpdatedAt)> rows)
    {
        var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.OrderBy(r => r.UpdatedAt))
        {
            notes[row.Key] = row.Value;
        }
        return notes;
    }

    /// <summary>
    /// The answer for already loaded rows. <paramref name="language"/> = language of the texts as passed (the original's, or the translation's
    /// when <paramref name="overlay"/> carries its texts); <paramref name="overlay"/> replaces name, description, tip, group and category per trophy key.
    /// </summary>
    public static ManualTrophyListDto Build(
        GameEntity game,
        IReadOnlyList<GameTrophyEntity> trophies,
        IReadOnlyDictionary<string, string> notes,
        string language,
        IReadOnlyDictionary<string, TrophyTexts>? overlay = null,
        TrophyTranslationDto? translation = null)
    {
        var platform = game.Platform;
        var items = trophies
            .OrderBy(t => t.SortOrder).ThenBy(t => t.TrophyKey, StringComparer.Ordinal)
            .Select(t => ToItem(platform, t, notes, overlay))
            .ToList();

        var total = items.Count;
        var achieved = items.Count(i => i.Achieved);
        var reached = game.PlatinumAt is not null;

        // Platinum: the PlayStation platinum trophy by its own tier; everywhere else the whole game counts as the one platinum (like a Steam game)
        var platinumItems = items.Where(i => i.RarityTier == TrophyTiers.TierPlatinum).ToList();
        var platinum = platinumItems.Count > 0
            ? new LiveTrophyTierSummaryDto(platinumItems.Count(i => i.Achieved), platinumItems.Count)
            : new LiveTrophyTierSummaryDto(total > 0 && reached ? 1 : 0, total > 0 ? 1 : 0);

        var latest = items
            .Where(i => i.Achieved && i.UnlockTime.HasValue)
            .OrderByDescending(i => i.UnlockTime)
            .FirstOrDefault();

        return new ManualTrophyListDto(
            GameId: game.Id,
            Slug: game.Slug,
            Platform: game.Platform,
            PlatformVariant: game.PlatformVariant,
            Supported: GamePlatforms.HasTrophies(game.Platform),
            Kind: GamePlatforms.TrophyKind(game.Platform),
            ResearchedAt: game.TrophyListResearchedAt,
            Sources: ReadSources(game.TrophyListSourcesJson),
            Language: language,
            Translation: translation,
            Achievements: items,
            TotalCount: total,
            AchievedCount: achieved,
            RemainingCount: total - achieved,
            ProgressPercentage: total > 0 ? Math.Round((double)achieved / total * 100.0, 1) : 0.0,
            Is100Percent: total > 0 && achieved == total,
            Platinum: platinum,
            Gold: Summary(items, TrophyTiers.TierGold),
            Silver: Summary(items, TrophyTiers.TierSilver),
            Bronze: Summary(items, TrophyTiers.TierBronze),
            PlatinumAt: game.PlatinumAt,
            LatestAchievement: latest);
    }

    /// <summary>One trophy as the list (and the popup of a tick) shows it.</summary>
    public static ManualTrophyItemDto ToItem(
        string platform,
        GameTrophyEntity t,
        IReadOnlyDictionary<string, string> notes,
        IReadOnlyDictionary<string, TrophyTexts>? overlay = null)
    {
        var hasShare = t.RarityPercent is { } share && double.IsFinite(share) && share is >= 0 and <= 100;
        var percent = hasShare ? t.RarityPercent!.Value : 0;
        var texts = overlay is not null && overlay.TryGetValue(t.TrophyKey, out var translated) ? translated : null;

        var name = texts?.Name ?? t.Name;
        var description = texts?.Description ?? t.Description;
        return new ManualTrophyItemDto(
            ApiName: t.TrophyKey,
            DisplayName: name,
            Description: description,
            OriginalDescription: texts is null ? null : t.Description,
            IsTranslated: texts is not null,
            IconUrl: string.Empty,
            IconGrayUrl: string.Empty,
            Achieved: t.UnlockedAt is not null,
            UnlockTime: t.UnlockedAt?.UtcDateTime,
            GlobalPercent: percent,
            HasGlobalPercent: hasShare,
            // The tier is decided on the ORIGINAL texts (a "complete chapter N" rule must not depend on the language shown)
            RarityTier: TrophyTiers.For(platform, t.Grade, t.Points, hasShare ? percent : null, t.Name, t.Description),
            // Band and label only from a real share (the Steam scale); without one the UI shows none
            RarityLabel: SteamLiveTrackerService.CalculateRarityLabel(percent, hasShare),
            RarityBand: SteamLiveTrackerService.CalculateRarityBand(percent, hasShare),
            IsHidden: t.IsHidden,
            UserNote: notes.TryGetValue(t.TrophyKey, out var note) ? note : string.Empty,
            Grade: GamePlatforms.HasPlatinumGrade(platform) ? TrophyTiers.NormalizeGrade(t.Grade) : null,
            Points: t.Points,
            Missable: t.IsMissable,
            // Whether a trophy belongs to the base game never depends on a translation
            Group: t.GroupName is null ? null : texts?.Group ?? t.GroupName,
            Category: texts?.Category ?? t.Category,
            Tip: texts?.Tip ?? t.Tip,
            SortOrder: t.SortOrder,
            OriginalName: t.OriginalName);
    }

    private static LiveTrophyTierSummaryDto Summary(List<ManualTrophyItemDto> items, string tier)
    {
        var inTier = items.Where(i => i.RarityTier == tier).ToList();
        return new LiveTrophyTierSummaryDto(inTier.Count(i => i.Achieved), inTier.Count);
    }

    /// <summary>The sources stored with the list (<c>[{title,url}]</c>); anything unreadable or not http(s) is left out.</summary>
    public static List<TrophySourceDto> ReadSources(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            return (JsonSerializer.Deserialize<List<TrophySourceDto>>(json, Json) ?? [])
                .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Url) && IsHttpUrl(s.Url))
                .Select(s => new TrophySourceDto((s.Title ?? string.Empty).Trim(), s.Url.Trim()))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string? WriteSources(IEnumerable<TrophySourceDto>? sources)
    {
        var list = (sources ?? []).Where(s => IsHttpUrl(s.Url)).Select(s => new TrophySourceDto((s.Title ?? string.Empty).Trim(), s.Url.Trim())).ToList();
        return list.Count == 0 ? null : JsonSerializer.Serialize(list, Json);
    }

    private static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host);
}
