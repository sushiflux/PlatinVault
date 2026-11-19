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
    /// <summary>IGDB users' average rating (IGDB <c>rating</c>), 0-100, rounded; null = IGDB has none or the card was not refreshed yet.</summary>
    public int? IgdbUserRating { get; set; }

    // The game's genres from the Steam store in the language they were fetched in (SteamGenresLanguage), for the dashboard card; IGDB's (English)
    // only while Steam has none. Refreshed with the other card facts, so the card needs no Steam call per request. GenreTags (AI) stays as is.
    public List<string> SteamGenres { get; set; } = [];
    public string? SteamGenresLanguage { get; set; }
    public DateTimeOffset? FactsUpdatedAt { get; set; }

    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // ---- Platform and manual games (added by hand, with or without IGDB); rules in Services/GamePlatforms.cs ----

    /// <summary>
    /// Where the game is played: one of <c>GamePlatforms.Ids</c> ("steam", "playstation", "xbox", ...). Every game from the Steam library (and every
    /// row from before the column existed) is "steam". A manual game is never "steam" and never gets a <see cref="SteamAppId"/>.
    /// </summary>
    public string Platform { get; set; } = "steam";

    /// <summary>"ps5" / "ps4" for "playstation" (their trophy lists are separate), null for every other platform. Fixed after adding.</summary>
    public string? PlatformVariant { get; set; }

    /// <summary>The IGDB game id the facts and cover came from (manual games added through the IGDB search); null = none.</summary>
    public int? IgdbId { get; set; }

    /// <summary>Where the facts of a manual game come from: "igdb" or "ai"; null = none yet (Steam games get their facts from Steam).</summary>
    public string? FactsSource { get; set; }

    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public string? Summary { get; set; }

    /// <summary>First release (UTC) for the release sort of manual games; <see cref="ReleaseDate"/> stays the readable text.</summary>
    public DateTimeOffset? FirstReleaseAt { get; set; }

    /// <summary>When the trophy list of a manual game was last researched; null = no list yet.</summary>
    public DateTimeOffset? TrophyListResearchedAt { get; set; }

    /// <summary>The sources of the researched trophy list as JSON <c>[{"title":"...","url":"..."}]</c>; null = none.</summary>
    public string? TrophyListSourcesJson { get; set; }

    public List<GuideTabEntity> Tabs { get; set; } = [];
    public List<ProgressCheckEntity> ProgressChecks { get; set; } = [];

    /// <summary>The trophy/achievement list of a manual game (empty for Steam games, whose list comes from Steam).</summary>
    public List<GameTrophyEntity> Trophies { get; set; } = [];
}

/// <summary>
/// One trophy / achievement of a manual game (researched by the AI, ticked by hand). A table of its own and not a hidden guide tab: the
/// ticks are real progress and must survive "reset progress", chapter removal and the guide linker (which all work on progress_checks),
/// and a conditional UPDATE on <see cref="UnlockedAt"/> gives an exactly-once popup even with two servers on one database file
/// (see <c>TrophyProgress</c>). Texts are in the guide language of the game (<see cref="GameEntity.ContentLanguage"/>).
/// </summary>
public sealed class GameTrophyEntity
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }

    /// <summary>Stable id of the trophy within the game ("kebab-id"); used as <c>apiName</c> everywhere (guide alerts, pins, notes).</summary>
    public string TrophyKey { get; set; } = string.Empty;

    public int SortOrder { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>The official English name (null = unknown or the same as <see cref="Name"/>).</summary>
    public string? OriginalName { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>"platinum" / "gold" / "silver" / "bronze" (<c>TrophyTiers.Grades</c>) - PlayStation only, null elsewhere.</summary>
    public string? Grade { get; set; }

    /// <summary>Xbox gamerscore or a store's XP; null = none.</summary>
    public int? Points { get; set; }

    /// <summary>Share of players who have it (0-100), only when a source states it; null = unknown.</summary>
    public double? RarityPercent { get; set; }

    public bool IsHidden { get; set; }
    public bool IsMissable { get; set; }

    /// <summary>Null = base game; else the DLC / add-on the trophy belongs to. The PlayStation platinum counts only base-game trophies.</summary>
    public string? GroupName { get; set; }

    /// <summary>Short category ("Story", "Collectibles", ...); empty = none.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>A short how-to tip; empty = none.</summary>
    public string Tip { get; set; } = string.Empty;

    /// <summary>The tick: when the player marked it as unlocked (UTC); null = not unlocked. Written only by <c>TrophyProgress</c>.</summary>
    public DateTimeOffset? UnlockedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public GameEntity? Game { get; set; }
}

/// <summary>
/// An uploaded cover of a game, stored in the database (not as a file) so the SQLite backups contain it. At most one row per game;
/// served at <c>GET /api/covers/{gameId:N}?v=&lt;ticks of UpdatedAt&gt;</c> (see <c>CoverStore</c>).
/// </summary>
public sealed class GameCoverEntity
{
    public Guid GameId { get; set; }

    /// <summary>"image/png", "image/jpeg", "image/webp", ... (never SVG).</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>The image bytes, at most <c>CoverStore.MaxBytes</c>.</summary>
    public byte[] Data { get; set; } = [];

    /// <summary>SHA-256 of <see cref="Data"/>, lower-case hex.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public GameEntity? Game { get; set; }
}

/// <summary>
/// The trophy list of a manual game translated into one language (the original stays in <see cref="GameTrophyEntity"/>). Same rule as the guide
/// translation: only readable texts (name, description, tip, group, category) are translated, never keys, grades, points or flags.
/// <see cref="SourceHash"/> = hash of the original texts at the time of translation (a different hash now = stale).
/// </summary>
public sealed class GameTrophyTranslationEntity
{
    public Guid GameId { get; set; }

    /// <summary>"de" / "en".</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>JSON with the translated texts per trophy key (shape owned by the translation service).</summary>
    public string ItemsJson { get; set; } = "[]";

    public string SourceHash { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public GameEntity? Game { get; set; }
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

    /// <summary>
    /// The standard topic the chapter was researched for ("walkthrough", "quests", "weapons" ...; see the topic list), or null = unknown (chapters from
    /// before the column existed, extra chapters of the AI, chapters added by hand). The UI finds e.g. the side quest chapter by it.
    /// </summary>
    public string? TopicId { get; set; }

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
