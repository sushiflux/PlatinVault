using System.Text.Json.Serialization;

namespace Companion.Api.Data;

/// <summary>Represents a game in the 100% Vault dashboard.</summary>
public sealed class GameEntity
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public int? SteamAppId { get; set; }
    public string CoverUrl { get; set; } = string.Empty;
    public string HeroBannerUrl { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "emerald";
    public string EstimatedHours { get; set; } = string.Empty;
    public string MissablesInfo { get; set; } = string.Empty;
    public string DifficultyInfo { get; set; } = string.Empty;
    public string GenreTags { get; set; } = "JRPG, 100% Guide";

    /// <summary>Language of the guide text ("de" / "en"): the UI language when the guide was created. Null is read as "de" (guides from before the column existed). A guide is never mixed: later additions use this language.</summary>
    public string? ContentLanguage { get; set; }

    // Last known trophy state from Steam (dashboard cards, platinum showcase). Refreshed whenever the achievement list
    // of the game is built; PlatinumAt = when all trophies were unlocked (null while incomplete).
    public int TrophiesUnlocked { get; set; }
    public int TrophiesTotal { get; set; }
    public DateTimeOffset? TrophiesUpdatedAt { get; set; }
    public DateTimeOffset? PlatinumAt { get; set; }

    // The star: only favourites are on the dashboard. Removing it hides the game but keeps guide and ticks.
    public bool IsFavorite { get; set; } = true;

    // Facts for the dashboard card, refreshed in the background (own playtime and last session from Steam,
    // Metacritic and the review summary from the Steam store)
    public int? PlaytimeMinutes { get; set; }
    public DateTimeOffset? LastPlayedAt { get; set; }
    public int? MetacriticScore { get; set; }
    public string? ReviewScoreDesc { get; set; }
    public int? ReviewPercent { get; set; }
    public string? ReleaseDate { get; set; }
    // From IGDB (only with Twitch credentials): average hours to 100 % completion and the critics' rating 0-100
    public double? CompletionHours { get; set; }
    public int? IgdbRating { get; set; }

    // The game's genres from the Steam store in the language they were fetched in (SteamGenresLanguage), for the dashboard card; IGDB's (English)
    // only while Steam has none. Refreshed with the other card facts, so the card needs no Steam call per request. GenreTags (AI) stays as is.
    public List<string> SteamGenres { get; set; } = [];
    public string? SteamGenresLanguage { get; set; }
    public DateTimeOffset? FactsUpdatedAt { get; set; }

    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<GuideTabEntity> Tabs { get; set; } = [];
    public List<ProgressCheckEntity> ProgressChecks { get; set; } = [];
}

/// <summary>
/// Represents a customizable tab inside a game's 100% guide.
/// Each game can define completely custom tabs and flexible JSON content blocks.
/// </summary>
public sealed class GuideTabEntity
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = "mdi:compass-outline";
    public int SortOrder { get; set; }

    /// <summary>
    /// The group this chapter is listed under in the navigation (one of <c>GuideGroups.Keys</c>), or null = decided from the title
    /// (<c>GuideGroups.Classify</c>). Set explicitly when the AI creates the chapter from a standard topic, or by a user override.
    /// </summary>
    public string? GroupKey { get; set; }

    /// <summary>Serialized list of GuideSectionDto blocks (alerts, strategy card grids, and custom checklist/bestiary tables).</summary>
    public string SectionsJson { get; set; } = "[]";

    [JsonIgnore]
    public GameEntity? Game { get; set; }
}

/// <summary>Stores checked items per game in the database so progress syncs across PC, Ultrawide, and Tablet/Phone.</summary>
public sealed class ProgressCheckEntity
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public string ItemKey { get; set; } = string.Empty;
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public GameEntity? Game { get; set; }
}

/// <summary>Key-value store for non-secret operator settings in the database.</summary>
public sealed class AppSettingEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}


/// <summary>
/// One reading of "players online right now" for a game (Steam ISteamUserStats/GetNumberOfCurrentPlayers), taken every 30 minutes by
/// <c>HistoryRecorderWorker</c>. Steam only knows the current value, so PlatinVault records the history itself. Raw samples are kept
/// for 30 days, then condensed into <see cref="PlayerCountDailyEntity"/>. SampledAt is always UTC.
/// </summary>
public sealed class PlayerCountSampleEntity
{
    public int AppId { get; set; }
    public DateTimeOffset SampledAt { get; set; }
    public int Count { get; set; }
}

/// <summary>One day (UTC) of condensed player-count samples of a game; kept for two years.</summary>
public sealed class PlayerCountDailyEntity
{
    public int AppId { get; set; }
    public DateOnly Date { get; set; }
    public int MinCount { get; set; }
    public double AvgCount { get; set; }
    public int MaxCount { get; set; }
}

/// <summary>
/// Total playtime of a game (minutes) as last read on that day (UTC). The playtime PER day is the difference between consecutive
/// snapshots. Tiny, kept forever.
/// </summary>
public sealed class PlaytimeSnapshotEntity
{
    public int AppId { get; set; }
    public DateOnly Date { get; set; }
    public int PlaytimeMinutes { get; set; }
}

/// <summary>
/// A guide chapter translated into one language (the original stays in <see cref="GuideTabEntity"/>, in the guide's source language).
/// Same structure as the original, every itemId unchanged - so checklist progress (progress_checks) is shared by all languages.
/// <see cref="SourceHash"/> = hash of the original title + sections at the time of translation: a different hash now means the
/// original changed and this translation is stale.
/// </summary>
public sealed class GuideTabTranslationEntity
{
    public Guid TabId { get; set; }

    /// <summary>"de" / "en".</summary>
    public string Language { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>The translated list of GuideSectionDto blocks (see <see cref="GuideTabEntity.SectionsJson"/>).</summary>
    public string SectionsJson { get; set; } = "[]";

    public string SourceHash { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public GuideTabEntity? Tab { get; set; }
}

/// <summary>The texts of a game's card/header that the AI wrote (subtitle, hours, missables, difficulty, genre tags), translated into one language.</summary>
public sealed class GameTranslationEntity
{
    public Guid GameId { get; set; }

    /// <summary>"de" / "en".</summary>
    public string Language { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;
    public string EstimatedHours { get; set; } = string.Empty;
    public string MissablesInfo { get; set; } = string.Empty;
    public string DifficultyInfo { get; set; } = string.Empty;
    public string GenreTags { get; set; } = string.Empty;

    /// <summary>Hash of the original texts when they were translated (stale when the original changed).</summary>
    public string SourceHash { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public GameEntity? Game { get; set; }
}

/// <summary>A named lease (see <see cref="DatabaseLease"/>): who holds it and until when (Unix milliseconds, UTC).</summary>
public sealed class LeaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public long ExpiresAt { get; set; }
}
