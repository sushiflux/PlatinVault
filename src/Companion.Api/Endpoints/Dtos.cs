namespace Companion.Api.Endpoints;

/// <summary>Summary card for a game displayed on the main Dashboard grid.</summary>
public sealed record GameSummaryDto(
    Guid Id,
    string Slug,
    string Title,
    string Subtitle,
    int? SteamAppId,
    string CoverUrl,
    string HeroBannerUrl,
    string AccentColor,
    string EstimatedHours,
    string MissablesInfo,
    string DifficultyInfo,
    string GenreTags,
    // Language of the guide text ("de" / "en"); guides from before the column existed read as "de"
    string ContentLanguage,
    // Genres from the Steam store in the language of the app (IGDB's English ones only for a game Steam has none for); empty until the first card refresh
    List<string> SteamGenres,
    int TotalCheckableItems,
    int CheckedItemsCount,
    int ProgressPercent,
    int TabCount,
    // Trophy progress from Steam - the only progress shown on cards (guide checklists are extras)
    int TrophiesUnlocked,
    int TrophiesTotal,
    int TrophiesPercent,
    DateTimeOffset? PlatinumAt,
    // When the trophy state was last read from Steam (null = not yet; the dashboard polls faster meanwhile)
    DateTimeOffset? TrophiesCheckedAt,
    bool IsFavorite,
    // Card facts (null until the background refresh found them)
    int? PlaytimeMinutes,
    DateTimeOffset? LastPlayedAt,
    int? MetacriticScore,
    string? ReviewScoreDesc,
    int? ReviewPercent,
    string? ReleaseDate,
    // From IGDB (only with Twitch credentials): average hours to 100 % completion, and the critics' rating 0-100
    double? CompletionHours,
    int? IgdbRating,
    DateTimeOffset UpdatedAt
)
{
    // Values for the dashboard's sort orders from the library data (LibraryMetaService), null = unknown. They are separate from the
    // card facts above on purpose: the card keeps showing exactly what it showed, the sort only gets more values to work with.
    // SortMetacritic / SortReviewPercent = the card's own value, else the library's.
    public DateTimeOffset? ReleaseAt { get; init; }
    public long? SizeOnDiskBytes { get; init; }
    // Estimated install size from Steam's depot data (also for games that are not installed); the card shows it when there is no real size
    public long? InstallSizeBytes { get; init; }
    public int? SortMetacritic { get; init; }
    public int? SortReviewPercent { get; init; }
}

/// <summary>Full game details including all custom tabs, dynamic content sections, and checked item keys.</summary>
public sealed record GameDetailDto(
    Guid Id,
    string Slug,
    string Title,
    string Subtitle,
    int? SteamAppId,
    string CoverUrl,
    string HeroBannerUrl,
    string AccentColor,
    string EstimatedHours,
    string MissablesInfo,
    string DifficultyInfo,
    string GenreTags,
    string ContentLanguage,
    List<string> SteamGenres,
    int TotalCheckableItems,
    int CheckedItemsCount,
    int ProgressPercent,
    List<GuideTabDto> Tabs,
    List<string> CheckedItemKeys,
    // Language of the texts in this answer: the requested one when its translation is complete, else the guide's own (ContentLanguage)
    string Language,
    // State of the translation into the requested language (see GuideTranslationDto)
    GuideTranslationDto Translation,
    // Cross-chapter links (see GuideLinker): where a defining row is referenced from, and where a referenced row lives. Always present, empty when nothing is linked.
    Dictionary<string, List<GuideItemLinkDto>> ItemLinks,
    Dictionary<string, GuideRefTargetDto> RefTargets
);

/// <summary>
/// A place that references a defining row: Kind "row" (a checklist row with Ref) or "quest" (a side_quests entry with Ref). Path = "part · area" from the
/// nearest headings before it in the same chapter (may be empty), SectionIndex = index in that chapter's sections, RowItemId = the ref row's own item id (null for a quest).
/// </summary>
public sealed record GuideItemLinkDto(string Kind, Guid TabId, string TabSlug, string TabTitle, int SectionIndex, string? RowItemId, string Path);

/// <summary>Where the defining row behind a Ref lives: its chapter, the section (checklist table or puzzle) in that chapter and the row's name.</summary>
public sealed record GuideRefTargetDto(Guid TabId, string TabSlug, string TabTitle, int SectionIndex, string Name);

/// <summary>
/// Translation of a guide into one language. State: "original" = the language is the guide's own; "complete" = every chapter and the
/// card texts have a current translation (it is what the answer contains); "missing" = something is not translated yet, the answer
/// holds the original - Running tells whether a job works on it, Error (+ ProviderMessage, the AI provider's own text) why the last one failed.
/// </summary>
public sealed record GuideTranslationDto(
    string Language,
    string SourceLanguage,
    string State,
    int TotalTabs,
    int TranslatedTabs,
    bool Running,
    string? Error,
    string? ProviderMessage,
    // "Gemini quota used up - continuing with Claude." when the job had to change provider; null when it did not
    string? AiNotice = null
);

/// <summary>IsAchievementList = the Steam achievement table: only the source of the tips shown on the live trophies tab.</summary>
public sealed record GuideTabDto(
    Guid Id,
    string Slug,
    string Title,
    string Icon,
    int SortOrder,
    int TotalCheckableItems,
    int CheckedItemsCount,
    List<GuideSectionDto> Sections,
    bool IsAchievementList,
    // Group in the chapter navigation (one of the keys of GET /api/guide/groups): the stored value, else decided from the original title
    string GroupKey,
    // The standard topic of the chapter ("walkthrough", "quests", ...) when known, else null (older chapters, extra chapters, chapters added by hand)
    string? TopicId = null
);

/// <summary>
/// Dynamic content block inside a tab.
/// Type can be "alert", "cards_grid", "checklist_table", "story" (readable prose in Paragraphs, used by the optional
/// Story & Lore tab and - with Variant "steps" - as the numbered steps of the walkthrough), "puzzle" (a riddle/sequence with its
/// location and a solution that stays hidden until asked for, see <see cref="GuidePuzzleDto"/>) or "heading" (a sub-heading, e.g. one
/// story chapter or area of the walkthrough). An "alert" may carry a Kind (<see cref="GuideAlertKinds"/>) - the typed hints of the
/// walkthrough. Spoiler marks content the reader has to open deliberately.
/// </summary>
public sealed record GuideSectionDto(
    string Type,
    string? Title = null,
    string? Variant = null,
    string? Icon = null,
    string? HtmlContent = null,
    int? Columns = null,
    List<GuideCardDto>? Cards = null,
    GuideTableDto? Table = null,
    List<string>? Paragraphs = null,
    bool? Spoiler = null,
    GuideFailedChapterDto? FailedChapter = null,
    GuidePuzzleDto? Puzzle = null,
    string? Kind = null,
    // "heading": 1 = part/chapter heading (default, also for older data without the field), 2 = area/sub-heading below it
    int? Level = null,
    // "alert" with kind "achievement": the exact Steam API name of the achievement (dropped when the game's official list does not know it)
    string? ApiName = null,
    // "alert" (mostly kind "point_of_no_return"): what to do before you continue ("before you continue" list)
    List<string>? Items = null,
    // "alert" with kind "side_quests": the side quests that become available here, compact (the full solution is in the side quest chapter)
    List<GuideSideQuestDto>? Quests = null,
    // ---- "boss": a boss card exactly where the fight happens (text only). Title = the boss name. ----
    string? BossId = null,
    string? Location = null,
    // "boss" and "overview": recommended level as the source writes it
    string? RecommendedLevel = null,
    List<GuideBossHpDto>? Hp = null,
    List<string>? Weaknesses = null,
    List<string>? Resistances = null,
    List<string>? Immunities = null,
    List<GuideBossAttackDto>? Attacks = null,
    List<string>? Phases = null,
    string? Party = null,
    List<string>? Tactics = null,
    List<string>? Rewards = null,
    bool? Missable = null,
    // "boss" and "overview": Steam API names of the achievements tied to it (only names from the official list survive)
    List<string>? Achievements = null,
    string? Source = null,
    // ---- "overview": "at a glance" at the start of a walkthrough part ----
    List<GuideOverviewCountDto>? Counts = null,
    List<string>? KeyItems = null,
    List<string>? Missables = null,
    bool? EndsAtPointOfNoReturn = null,
    // ---- "sources": the pages a chapter was researched from (last section of a chapter) ----
    List<GuideSourceDto>? Sources = null
);

/// <summary>The typed hints of a walkthrough: an "alert" section with one of these kinds gets its own label and symbol in the UI.</summary>
public static class GuideAlertKinds
{
    public const string MustHave = "must_have";
    public const string Missable = "missable";
    public const string Optional = "optional";
    public const string Tip = "tip";
    // An achievement that unlocks exactly here (ApiName = its Steam API name, text = what to do)
    public const string Achievement = "achievement";
    // The point of no return is right ahead; Items = what to do before you continue
    public const string PointOfNoReturn = "point_of_no_return";
    // The side quests that become available here, as a compact list (Quests); their solution belongs only in the side quest chapter
    public const string SideQuests = "side_quests";

    public static readonly IReadOnlyList<string> All = [MustHave, Missable, Optional, Tip, Achievement, PointOfNoReturn, SideQuests];

    /// <summary>The colour variant a kind uses when the section names none.</summary>
    public static string DefaultVariant(string kind) => kind switch
    {
        MustHave => "gold",
        Missable => "danger",
        PointOfNoReturn => "danger",
        Optional => "emerald",
        Achievement => "emerald",
        SideQuests => "emerald",
        _ => "crystal"
    };
}

/// <summary>
/// One HP value of a boss, exactly as a source states it: the value, what it applies to (difficulty, phase, version, NG+) and the source. Entries without
/// a value AND a source are dropped by the normalizer - numbers are never invented; "Not documented" as the value is allowed.
/// </summary>
public sealed record GuideBossHpDto(string Value, string? AppliesTo = null, string? Source = null);

/// <summary>
/// One side quest in the compact callout of the walkthrough: name, from when it is available, its missable window ("until the end of chapter 3"), a short
/// reward - nothing more. Ref = the itemId of the quest's row in the side quest chapter (set by the model or by GuideLinker; only kept when that row exists).
/// </summary>
public sealed record GuideSideQuestDto(string Title, string? AvailableFrom = null, string? Missable = null, string? Reward = null,
    // Never written as "ref": null - a chapter without links contains no "ref" at all, so its translation hash stays exactly as before (GuideTranslations.HashTab)
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Ref = null);

/// <summary>One attack of a boss with a short note (what it does, how to avoid it).</summary>
public sealed record GuideBossAttackDto(string Name, string? Note = null);

/// <summary>A collectible type of a walkthrough part with how many there are (Count null = the source names no number).</summary>
public sealed record GuideOverviewCountDto(string Label, int? Count = null);

/// <summary>
/// One page a chapter was researched from. Origin: "cited" = cited by the AI provider for this answer (Claude web search), "search" = a result of the
/// provider's web search for this answer, "ai" = named by the AI in its answer (not verified - models invent URLs). Only http(s) URLs are kept; the server
/// never fetches them.
/// </summary>
public sealed record GuideSourceDto(string Title, string Url, string Origin);

/// <summary>
/// A puzzle, riddle, sequence or hidden mechanism. Location, Requirements and Missable are always visible; the solution is not:
/// Hints (one at a time, from vague to concrete) and Steps (the complete solution, in order) are revealed on request. The section title is the
/// name of the puzzle and its HtmlContent says what the task is. Without Steps the solution is not documented (Documented = false) and the UI
/// says so instead of offering buttons. ItemId (optional) makes the puzzle tickable like a checklist row.
/// </summary>
public sealed record GuidePuzzleDto(
    string? ItemId = null,
    string? Location = null,
    string? Requirements = null,
    bool? Missable = null,
    List<string>? Hints = null,
    List<string>? Steps = null,
    string? Source = null,
    bool Documented = true
);

/// <summary>
/// What a chapter that could not be researched remembers: its planned topic (so it can be researched again exactly like planned) and why it failed.
/// Stored in the chapter's one placeholder section; older placeholders without it are recognised by their text (see FailedChapters).
/// Message = OUR short explanation in the language of the guide, ProviderMessage = the provider's own text for "technical details".
/// </summary>
public sealed record GuideFailedChapterDto(string Focus, string? TopicId, string Message, string? ProviderMessage = null, string? Provider = null);

public sealed record GuideCardDto(
    string Title,
    string? BadgeText = null,
    string? BadgeVariant = null,
    string? Icon = null,
    string? IntroText = null,
    List<GuideCardBulletDto>? Bullets = null
);

public sealed record GuideCardBulletDto(
    string Icon,
    string Title,
    string Body
);

public sealed record GuideTableDto(
    string TableId,
    string Caption,
    bool Checkable,
    string? SearchPlaceholder,
    string? CategoryFilterLabel,
    List<string>? Categories,
    string? SecondaryFilterLabel,
    List<string>? SecondaryCategories,
    List<GuideTableColumnDto> Columns,
    List<GuideTableRowDto> Rows
);

public sealed record GuideTableColumnDto(
    string Key,
    string Label,
    bool IsRowHeader = false,
    string? BadgeVariant = null
);

/// <summary>
/// One checklist row. Ref (optional) = the itemId of the DEFINING row in another chapter of the same game: such a row has no check of its own, its
/// checkbox shows and toggles the check of Ref (see GuideLinker). ItemId stays the row's own identity.
/// </summary>
public sealed record GuideTableRowDto(
    string ItemId,
    string? FilterCategory,
    string? FilterSecondary,
    Dictionary<string, string> Cells,
    // Never written as "ref": null (see GuideSideQuestDto.Ref)
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Ref = null
);

public sealed record ToggleProgressRequest(string ItemKey, bool Checked);

public sealed record CreateGameRequest(
    string Title,
    string? Subtitle,
    int? SteamAppId,
    string? CoverUrl,
    string? HeroBannerUrl,
    string? AccentColor,
    string? EstimatedHours,
    string? MissablesInfo,
    string? DifficultyInfo,
    string? GenreTags
);

public sealed record AiGenerateGameRequest(
    string GameTitle,
    int? SteamAppId,
    string? CoverUrl,
    string? HeroBannerUrl,
    string? CustomFocusPrompt,
    string? ProviderOverride
);

public sealed record AiExpandTabRequest(
    string TabTitle,
    string TopicPrompt,
    string? ProviderOverride
);

public sealed record SteamSearchItemDto(
    int AppId,
    string Name,
    string CoverUrl,
    string HeroBannerUrl,
    string HeaderImageUrl
);

/// <summary>Represents an owned game from the user's personal Steam library via IPlayerService/GetOwnedGames.</summary>
public sealed record SteamOwnedGameDto(
    int AppId,
    string Name,
    double PlaytimeHours,
    string CoverUrl,
    string HeroBannerUrl,
    string HeaderImageUrl,
    bool AlreadyInVault,
    string? ExistingSlug,
    // Starred = on the dashboard; LastPlayedAt from Steam (null when never played)
    bool IsFavorite,
    DateTimeOffset? LastPlayedAt
)
{
    // From the library evaluation (null until the game was checked; total 0 = no achievements)
    public int? TrophiesUnlocked { get; init; }
    public int? TrophiesTotal { get; init; }

    // For the dashboard's sort orders (LibraryMetaService); null = unknown
    public DateTimeOffset? ReleaseAt { get; init; }
    public long? SizeOnDiskBytes { get; init; }
    public long? InstallSizeBytes { get; init; }
    public int? SortMetacritic { get; init; }
    public int? SortReviewPercent { get; init; }
}

/// <summary>Trophy figures of the whole Steam library for the dashboard; Scanning while the background run goes on.</summary>
public sealed record LibrarySummaryDto(
    int LibraryGameCount,
    int GamesWithAchievements,
    int TrophiesUnlocked,
    int TrophiesTotal,
    int PerfectGames,
    bool Scanning,
    int ScanDone,
    int ScanTotal
)
{
    // The background lookup of release date / reviews in Steam's store (only for apps no local Steam source knows)
    public bool MetaScanning { get; init; }
    public int MetaDone { get; init; }
    public int MetaTotal { get; init; }
}

/// <summary>The linked Steam account for the page header; every field is null when Steam does not provide it
/// (no account linked, private profile, no Steam key for the library size) - the UI then hides it.</summary>
public sealed record SteamProfileDto(
    string? SteamId,
    string? PersonaName,
    string? AvatarUrl,
    string? ProfileUrl,
    int? LibraryGameCount
);

public sealed record SteamLibraryResponseDto(
    string ResolvedSteamId,
    int TotalGames,
    List<SteamOwnedGameDto> Games
);

/// <summary>Realtime Steam Game &amp; Trophy Tracker DTOs.</summary>
public sealed record LiveGameInfoDto(
    uint AppId,
    string Name,
    string HeaderImage,
    string BackgroundImage,
    bool IsRunning,
    int PlaytimeForeverMinutes,
    int Playtime2WeeksMinutes,
    string FormattedPlaytimeTotal,
    string FormattedPlaytimeRecent,
    string FormattedLastPlayed,
    string? LinkedGuideSlug
);

public sealed record LiveAchievementItemDto(
    string ApiName,
    string DisplayName,
    string Description,
    string? OriginalDescription,
    bool IsTranslated,
    string IconUrl,
    string IconGrayUrl,
    bool Achieved,
    DateTime? UnlockTime,
    double GlobalPercent,
    string RarityTier,
    string RarityLabel,
    bool IsHidden,
    string UserNote
)
{
    // Steam's progress counter ("50 / 400") for games that define one; null otherwise
    public double? ProgressCurrent { get; init; }
    public double? ProgressMax { get; init; }

    // False = Steam delivered no worldwide unlock share for this achievement and GlobalPercent is only a placeholder
    // (kept so the tier/label logic keeps working). Statistics must ignore such values instead of treating them as data.
    public bool HasGlobalPercent { get; init; }

    // Language-neutral rarity step ("ultraRare", "veryRare", "rare", "uncommon", "common"; empty while HasGlobalPercent is false):
    // the UI translates this key. RarityLabel is the same step as a text in the language of the request, kept for older clients.
    public string RarityBand { get; init; } = string.Empty;
}

public sealed record LiveTrophyTierSummaryDto(int Achieved, int Total);

public sealed record LiveGameAchievementsDto(
    LiveGameInfoDto Game,
    List<LiveAchievementItemDto> Achievements,
    int TotalCount,
    int AchievedCount,
    int RemainingCount,
    double ProgressPercentage,
    bool Is100Percent,
    LiveTrophyTierSummaryDto Platinum,
    LiveTrophyTierSummaryDto Gold,
    LiveTrophyTierSummaryDto Silver,
    LiveTrophyTierSummaryDto Bronze,
    LiveAchievementItemDto? LatestAchievement,
    int SyncedGuideItemsCount,
    // Where the worldwide unlock shares (which decide Gold/Silver/Bronze) come from; see LiveRarityStatusDto
    LiveRarityStatusDto Rarity
);

/// <summary>
/// State of the worldwide unlock shares. "ok" = values from a successful fetch and the last attempt did not fail; "stale" = stored values
/// are used because the last refresh failed (FetchedAt = when those values were fetched); "unavailable" = no values at all, every
/// achievement counts as Silver. Message = a complete sentence in the language of the request (null when ok); Detail = the technical
/// error text (null when ok), shown under "technical details".
/// </summary>
public sealed record LiveRarityStatusDto(string State, DateTimeOffset? FetchedAt, string? Message, string? Detail);

/// <summary>The pinned achievements ("Im Blick") of one game in display order; Max = how many may be pinned per game.</summary>
public sealed record PinnedAchievementsDto(uint AppId, IReadOnlyList<string> ApiNames, int Max);

public sealed record PinAchievementRequest(uint AppId, string? ApiName);

public sealed record UnpinAchievementsRequest(uint AppId, List<string>? ApiNames);

public sealed record LiveStatusDto(
    uint RunningAppId,
    string SteamId64,
    string? RunningGameSlug,
    bool RunningGameTracked,
    bool LocalSteamAvailable,
    DateTimeOffset? LastBridgeEventAt,
    // Title of the running game when it is in the vault (RunningGameTracked); null otherwise
    string? RunningGameName = null
);

/// <summary>
/// The live events of the last minutes (GET /api/live/events), for a window that was not connected when they were sent.
/// InstanceId changes with every server start; the window compares it and Id with the last event it showed.
/// </summary>
public sealed record LiveEventsResponseDto(string InstanceId, long LatestId, List<LiveEventDto> Events);

/// <summary>Kind "achievement" carries Achievement, kind "platinum" carries GameName + Total.</summary>
public sealed record LiveEventDto(
    long Id,
    DateTimeOffset At,
    string Kind,
    LiveAchievementItemDto? Achievement,
    string? GameName,
    int AppId,
    int? Total);

public sealed record SaveAchievementNoteRequest(
    uint AppId,
    string ApiName,
    string Note
);

/// <summary>Realtime bridge ("PlatinVault Bridge", PlatinVault.Bridge.exe): the running game plus the unlocked state of its achievements.</summary>
public sealed record BridgeStateRequest(
    string? SteamId64,
    long RunningAppId,
    string? GameName,
    List<BridgeAchievementDto>? Achievements,
    DateTimeOffset? SentAt,
    // "linux" from the Linux bridge; missing from the Windows bridge. Only decides which bundled bridge the answer announces for self-update.
    string? Platform = null
);

/// <summary>
/// The library facts the bridge reads from the Steam client's files on the gaming PC (<c>POST /api/bridge/library</c>): one entry per app
/// that has any. Every value except the id is optional (null = Steam's files do not know it).
/// </summary>
public sealed record BridgeLibraryRequest(List<BridgeLibraryGameDto>? Games);

public sealed record BridgeLibraryGameDto(long AppId, long? ReleaseUnix, int? Metacritic, int? ReviewPercent, int? ReviewScore, long? SizeBytes, long? InstallSizeBytes = null);

public sealed record BridgeAchievementDto(
    string ApiName,
    bool Achieved,
    DateTimeOffset? UnlockTime
);

/// <summary>BundledVersion = version of the bridge that ships with this server (null when none), so the UI can offer the download.</summary>
public sealed record BridgeTokenStatusDto(
    bool HasToken,
    string? Hint,
    DateTimeOffset? LastSeenAt,
    string? BundledVersion = null,
    string? LinuxBundledVersion = null
);

/// <summary>Body of POST /api/bridge/package (the query parameters "origin" and "os" work as well). Os: "windows" (default) or "linux".</summary>
public sealed record BridgePackageRequest(string? Origin, string? Os = null);

/// <summary>Returned once, when a token is created; only its hash is stored.</summary>
public sealed record BridgeTokenCreatedDto(
    string Token,
    string Hint
);

public sealed record SettingsResponseDto(
    string GeminiModel,
    string ClaudeModel,
    string OpenAiModel,
    string SteamUserId,
    bool AutoDetectGame,
    bool PlaySounds,
    double SoundVolume,
    string SoundTheme,
    string PopupPosition,
    // Steam language name for achievement texts ("german" / "english"); follows UiLanguage, never set on its own
    string Language,
    // The language of the whole app ("de" / "en", setting "ui.language"); null until the user has chosen one on first start
    string? UiLanguage,
    bool AutoTranslate,
    bool HasGeminiKey,
    string GeminiKeyHint,
    bool HasClaudeKey,
    string ClaudeKeyHint,
    bool HasOpenAiKey,
    string OpenAiKeyHint,
    bool HasSteamKey,
    string SteamKeyHint,
    // Twitch developer app (Client-ID + Client-Secret, shared by Twitch live streams and IGDB): HasTwitchKey = both are stored,
    // TwitchKeyHint = masked Client-ID. TwitchStreamsEnabled = switch "features.twitch_streams" (on by default; only matters with keys).
    bool HasTwitchKey,
    string TwitchKeyHint,
    bool TwitchStreamsEnabled,
    // Switch "features.achievement_notes" (on by default): off hides the personal notes on achievements everywhere; stored notes are kept.
    bool AchievementNotesEnabled,
    // AI selection: "default" (one Standard-KI writes a guide) / "mixed" (chapters spread over every provider with a key) / "ask"
    // (every AI action asks). AiDefaultProvider = the Standard-KI that is effective now (the stored one if it still has a key, else
    // the first provider with a key; null without any key). AiAutoFallback = continue with the next provider on quota/key/outage.
    string AiSelectionMode,
    string? AiDefaultProvider,
    bool AiAutoFallback,
    // The research building blocks the player ticked (nothing ticked = the built-in default behaviour); Policy = the ids the server accepts
    ResearchBlocksDto ResearchBlocks,
    ResearchBlocksPolicyDto ResearchBlocksPolicy,
    // Switch "features.auto_switch_game" (on by default): every open window switches to the game page when a vault game is started
    bool AutoSwitchGameEnabled = true,
    // Switch "updates.beta_channel" ("Beta-Versionen erhalten", off by default): the update check also offers betas
    bool BetaUpdatesEnabled = false,
    // The dashboard's sort order per view as "<order>:<asc|desc>" (DashboardSort); the defaults when nothing was chosen yet
    string DashboardSortFavorites = Settings.DashboardSort.DefaultFavorites,
    string DashboardSortAll = Settings.DashboardSort.DefaultAll,
    // "Trophäen-Popup anzeigen" (setting "live.popup_target"): "app" / "overlay" / "both" (default). Only the Windows program has an
    // overlay; everywhere else the effect is "app".
    string PopupTarget = Settings.SettingsStore.DefaultPopupTarget,
    // "Steam-Ordner (optional)" (setting "steam.root"): the folder set by hand, null when none is set
    string? SteamRoot = null,
    // What this server can do with a Steam client on its own computer and what it found (null only for callers without the service)
    SteamLocalSettingsDto? SteamLocal = null,
    // What happens to a pinned achievement when it is unlocked (setting "live.pins_after_unlock"): "keep" (default) / "remove"
    string PinsAfterUnlock = Settings.SettingsStore.DefaultPinsAfterUnlock,
    // How long a trophy popup stays on screen, in whole seconds (setting "live.popup_seconds", 2..10, default 5)
    int PopupSeconds = Settings.SettingsStore.DefaultPopupSeconds
);

/// <summary>
/// The local Steam client as the settings need it. Platform: "windows", "linux", "other" (the server's computer). Mode: "native" = PlatinVault runs on a Windows or Linux computer and reads Steam there
/// (the field "Steam-Ordner" makes sense), "container" = inside a container (usually no Steam; a mounted one works with
/// Companion__Steam__Root), "unsupported" = another platform. Method: how Root was found ("manual", "configuration", "environment",
/// "registry", "symlink", "knownPath", "none"). Experimental = Linux (reading Steam there is not verified on a real Linux Steam yet).
/// ReadsLocally = the live trophies come straight from this computer's Steam (found and mode "native"): the UI does not push the bridge.
/// </summary>
public sealed record SteamLocalSettingsDto(string Platform, string Mode, bool Found, string? Root, string Method, string? MethodDetail, bool Experimental, string? ConfiguredRoot, bool ReadsLocally = false);

/// <summary>POST /api/steam/local/check: is this folder usable as the Steam folder? (live check of the settings field)</summary>
public sealed record SteamRootCheckRequest(string? Path);

/// <summary>
/// Valid = it can be saved. Problem: "none", "notAbsolute", "notFound", "noSteamApps", "libraryOnly" (a game library, not the Steam folder); Message = the explanation in the request's
/// language (null when valid); Path = the normalized form that would be stored; ResolvedPath = the folder it leads to (links resolved).
/// </summary>
public sealed record SteamRootCheckResponse(bool Valid, string Problem, string? Message, string Path, string? ResolvedPath);

/// <summary>One place where Steam was looked for (system check). Method as in <see cref="SteamLocalSettingsDto"/>.</summary>
public sealed record SteamRootCandidateDto(string Path, string Method, string? Detail, string? ResolvedPath, bool Exists, bool HasSteamApps, bool IsDuplicate, bool Chosen, bool IsSteamRoot);

/// <summary>
/// The local Steam client in the system check. RunningGameDetection: "registry" (Windows), "processList" (Linux: reaper SteamLaunch
/// AppId=...), "none". AccountDetection: "registry", "loginUsers", "statsFiles" (Linux, a best guess), "none".
/// </summary>
public sealed record SteamLocalStatusDto(
    string Platform,
    string Mode,
    bool Found,
    string? Root,
    string Method,
    string RunningGameDetection,
    string AccountDetection,
    bool Experimental,
    string? StoredRoot,
    string? ConfiguredRoot,
    List<SteamRootCandidateDto> Candidates);

/// <summary>The building blocks for the AI research (Settings > AI research). Scope/Spoilers/Presentation: one id or null; Focus: several ids.</summary>
public sealed record ResearchBlocksDto(string? Scope, List<string> Focus, string? Spoilers, string? Presentation);

/// <summary>The ids the server accepts per group, so the UI never keeps a second hand-typed copy.</summary>
public sealed record ResearchBlocksPolicyDto(List<string> Scope, List<string> Focus, List<string> Spoilers, List<string> Presentation);

public sealed record UpdateSettingsRequest(
    // Every field is optional: null = keep what is stored, so a small change (e.g. only UiLanguage) never resets the rest
    string? GeminiModel,
    string? ClaudeModel,
    string? OpenAiModel,
    string? SteamUserId,
    bool? AutoDetectGame,
    bool? PlaySounds,
    double? SoundVolume,
    string? SoundTheme,
    string? PopupPosition,
    // "de" or "en" (anything else is rejected with 400); also sets the Steam achievement language (de = german, en = english)
    string? UiLanguage,
    bool? AutoTranslate,
    string? GeminiApiKey,
    bool ClearGeminiKey,
    string? ClaudeApiKey,
    bool ClearClaudeKey,
    string? OpenAiApiKey,
    bool ClearOpenAiKey,
    string? SteamApiKey,
    bool ClearSteamKey,
    // Twitch & IGDB credentials: blank = keep what is stored, ClearTwitchKey removes both. All optional so older clients keep working.
    string? TwitchClientId = null,
    string? TwitchClientSecret = null,
    bool ClearTwitchKey = false,
    bool? TwitchStreamsEnabled = null,
    // Personal notes on achievements: null = keep the stored switch
    bool? AchievementNotesEnabled = null,
    // AI selection: "default" / "mixed" / "ask"; the Standard-KI ("gemini" / "claude" / "openai"); the automatic-fallback switch. null = keep.
    string? AiSelectionMode = null,
    string? AiDefaultProvider = null,
    bool? AiAutoFallback = null,
    // Research building blocks: null = keep; an object replaces the whole selection (all fields empty = nothing ticked = default behaviour)
    ResearchBlocksDto? ResearchBlocks = null,
    // Switch to the game page when a game is started: null = keep the stored switch
    bool? AutoSwitchGameEnabled = null,
    // "Beta-Versionen erhalten": null = keep the stored switch
    bool? BetaUpdatesEnabled = null,
    // Dashboard sort order per view ("<order>:<asc|desc>", see DashboardSort; anything else is rejected with 400): null = keep
    string? DashboardSortFavorites = null,
    string? DashboardSortAll = null,
    // "app" / "overlay" / "both" (anything else is rejected with 400): null = keep
    string? PopupTarget = null,
    // "Nach Updates suchen": "startup" | "4h" | "daily" | "weekly" | "monthly" | "never" (anything else is rejected with 400): null = keep
    string? UpdateCheckInterval = null,
    // "Steam-Ordner (optional)": null = keep, "" = remove (the automatic search applies again), else a folder that contains "steamapps"
    // (anything else is rejected with 400 and a message that says what is wrong)
    string? SteamRoot = null,
    // "keep" / "remove" (anything else is rejected with 400): null = keep what is stored
    string? PinsAfterUnlock = null,
    // Trophy popup duration in whole seconds, 2..10 (anything else, a fraction included, is rejected with 400): null = keep what is stored.
    // A double on purpose: "5.5" reaches the validation and gets the bilingual message instead of a generic binding error.
    double? PopupSeconds = null
);

public sealed record AiProviderModelsRequestDto(
    string Provider,
    string? ApiKey
);

public sealed record AiModelOptionDto(
    string Id,
    string Label
);

/// <summary>
/// ErrorMessage = our short explanation (what happened, what to do) in the language of the request; ProviderMessage = the provider's
/// own technical text (English), which the UI folds under "technical details". Both are null when the key works.
/// </summary>
public sealed record AiProviderModelsResponseDto(
    string Provider,
    bool Connected,
    string? ErrorMessage,
    List<AiModelOptionDto> Models,
    string? ProviderMessage = null
);



/// <summary>Per-game statistics page: general Steam facts plus the player's own numbers (fields are null when a source is unavailable).</summary>
public sealed record GameStatsDto(
    int AppId,
    string? ReleaseDate,
    bool ComingSoon,
    List<string> Developers,
    List<string> Publishers,
    List<string> Genres,
    int? MetacriticScore,
    int? AchievementsTotal,
    string? ReviewScoreDesc,
    int? ReviewTotal,
    int? ReviewPositive,
    int? ReviewPercentPositive,
    int? CurrentPlayers,
    List<GameNewsItemDto> News,
    GameNewsItemDto? LatestUpdate,
    PlayerGameStatsDto? Player
)
{
    // From the library data (local Steam files / bridge); null = unknown. SizeOnDiskBytes = real size of the installed game,
    // InstallSizeBytes = install size estimated from Steam's depot data (also for games that are not installed)
    public long? SizeOnDiskBytes { get; init; }
    public long? InstallSizeBytes { get; init; }
}

public sealed record GameNewsItemDto(string Title, string Url, DateTimeOffset Date, bool IsUpdate);

public sealed record PlayerGameStatsDto(
    double? PlaytimeHours,
    double? Playtime2WeeksHours,
    DateTimeOffset? LastPlayed,
    int AchievementsUnlocked,
    int AchievementsTotal,
    double ProgressPercent,
    string? RarestUnlockedName,
    double? RarestUnlockedPercent,
    DateTime? FirstUnlock,
    DateTime? LastUnlock
);

/// <summary>Shareable guide file ("platinvault-guide", format version 1): one game plus its guide tabs - no progress, no settings.</summary>
public sealed record GuideExportDocument(
    string Format,
    int FormatVersion,
    DateTimeOffset ExportedAt,
    string ExportedBy,
    GuideExportGame Game,
    List<GuideExportTab> Tabs
);

public sealed record GuideExportGame(
    string Title,
    string Subtitle,
    int? SteamAppId,
    string CoverUrl,
    string HeroBannerUrl,
    string AccentColor,
    string EstimatedHours,
    string MissablesInfo,
    string DifficultyInfo,
    string GenreTags,
    // "de" / "en": the language the guide text is written in (optional in files; files without it are read as German)
    string? ContentLanguage = null
);

public sealed record GuideExportTab(
    string Slug,
    string Title,
    string Icon,
    int SortOrder,
    List<GuideSectionDto> Sections,
    // Only an explicit choice is exported (null = decided from the title when the file is read)
    string? GroupKey = null,
    // The standard topic of the chapter when known (kept on import when it names a known topic)
    string? TopicId = null
);

/// <summary>Body of POST /api/games/{slug}/ai-lore (optional; empty body = use the configured provider).</summary>
public sealed record AiLoreRequest(string? ProviderOverride);

/// <summary>"Kapitel neu recherchieren" for any chapter: the AI to use for this one action (null = the normal selection).</summary>
public sealed record AiRedoChapterRequest(string? ProviderOverride);

public sealed record GuideTopicDto(string Id, string Title, string Icon, string Brief, string GroupKey);

/// <summary>One group of the chapter navigation (GET /api/guide/groups): the stable key and its label in the language of the request.</summary>
public sealed record GuideGroupDto(string Key, string Label);

public sealed record AiCompleteGuideRequest(string? ProviderOverride, List<GuideChapterSelectionDto>? Chapters);

/// <summary>A chapter the player ticked in the guide builder (topic id "extra" for game-specific extras).</summary>
public sealed record GuideChapterSelectionDto(string? TopicId, string Title, string? Icon, string Focus);

public sealed record GuidePlanProfileDto(string Id, string Name, bool FromSteamTags);

public sealed record GuidePlanTopicDto(string Id, string Title, string Icon, string Focus, bool Recommended, bool Required, bool Covered, string? Reason);

/// <summary>The size the research scope allows: Scope = compact / detailed / exhaustive, Min-Max chapters, Suggested = how many the plan ticks by default.</summary>
public sealed record GuidePlanBudgetDto(string Scope, int Min, int Max, int Suggested);

public sealed record GuidePlanDto(List<GuidePlanProfileDto> Profiles, string SourceSituation, List<GuidePlanTopicDto> Topics, List<GuidePlanTopicDto> Extras, AiUsageDto? Ai = null, GuidePlanBudgetDto? Budget = null);

public sealed record AiPlanRequest(string? ProviderOverride);

/// <summary>
/// A background AI action (POST ai-generate / ai-plan / ai-complete / ai-expand-tab / ai-lore answer with it, GET /api/ai/jobs/{id} reads it).
/// Step.Stage: "starting" / "facts" / "blueprint" / "chapters" (Done of Total, Title = the latest started) / "chapter" / "loreParts" / "saving".
/// ElapsedSeconds = how long it runs (or ran), ElapsedMilliseconds the same without rounding (the UI anchors its clock on it); StepIdleSeconds = how long the step has not changed. SwitchNotice = "Gemini quota used up - continuing
/// with Claude." once the action had to change provider. While Running is true the rest is empty; afterwards either Error (+ ProviderMessage,
/// AiProvider, AiFailure) or Result (the answer the request used to give) is set.
/// </summary>
public sealed record AiJobDto(
    Guid Id,
    string Kind,
    string? Slug,
    bool Running,
    int ElapsedSeconds,
    long ElapsedMilliseconds,
    int StepIdleSeconds,
    AiJobStepDto Step,
    string? SwitchNotice,
    string? Error,
    string? ProviderMessage,
    string? AiProvider,
    string? AiFailure,
    object? Result);

public sealed record AiJobStepDto(string Stage, int Done, int Total, string? Title);

/// <summary>Body of POST /api/games/{slug}/ai-retry-chapters: TabIds = the failed chapters to research again (null/empty = every failed chapter of the guide).</summary>
public sealed record AiRetryChaptersRequest(List<Guid>? TabIds, string? ProviderOverride);

/// <summary>
/// Retried = chapters asked for, Succeeded = replaced by real content, FailedTitles = chapters that failed again (their placeholder now names the new
/// reason), DroppedTitles = chapters whose topic the research says the game does not have (their placeholder was removed).
/// </summary>
public sealed record AiRetryChaptersResponse(int Retried, int Succeeded, List<string> FailedTitles, GameDetailDto Game, AiUsageDto Ai, List<string> DroppedTitles);

/// <summary>AddedTitles = the new chapters; DroppedTitles = planned chapters that were left out because the research says the game does not have their topic.</summary>
public sealed record AiCompleteGuideResponse(int AddedCount, List<string> AddedTitles, GameDetailDto Game, AiUsageDto Ai, List<string> DroppedTitles);

/// <summary>The answer of an AI action that returns a game: the game plus which AI actually answered (and whether it had to change).</summary>
public sealed record AiResultDto<T>(T Game, AiUsageDto Ai);

/// <summary>Providers = who answered (one, or several in mixed mode). Switches = every change of provider on the way, with a ready sentence for the notice.</summary>
public sealed record AiUsageDto(List<string> Providers, List<AiSwitchDto> Switches);

/// <summary>Reason: "quota" / "invalidKey" / "unavailable". Text: "Gemini quota used up – continuing with Claude." in the language of the request.</summary>
public sealed record AiSwitchDto(string From, string To, string Reason, string Text);

/// <summary>
/// GET /api/ai/status: the stored selection and, per provider, whether it has a key and how it is doing. ActiveProvider = what a plain
/// action would use right now (null in mixed mode or without a key). State: "ok" / "quota" / "invalidKey" / "unavailable"; Since/Until are
/// UTC; Message is our explanation of the failure (language of the request).
/// </summary>
public sealed record AiStatusDto(string Mode, string? DefaultProvider, bool AutoFallback, string? ActiveProvider, List<AiProviderStatusDto> Providers);

public sealed record AiProviderStatusDto(
    string Id,
    bool HasKey,
    string Model,
    bool WebSearch,
    string State,
    DateTimeOffset? Since,
    DateTimeOffset? Until,
    string? Message);

public sealed record LinkSteamAppRequest(int SteamAppId);

public sealed record SetFavoriteRequest(bool IsFavorite);

/// <summary>Star in the Steam library: Name is used when the game is not in the vault yet and gets its tile now.</summary>
public sealed record LibraryFavoriteRequest(bool IsFavorite, string? Name);

/// <summary>
/// Facts about a game from IGDB (GET /api/games/{slug}/igdb). Every list is empty and every other field null when IGDB does
/// not know the value, so the UI simply hides what is missing. Ratings are 0-100. All image URLs are complete https URLs.
/// </summary>
public sealed record IgdbGameDto(
    int IgdbId,
    string Name,
    string? Url,
    string? Summary,
    string? Storyline,
    double? AggregatedRating,
    int? AggregatedRatingCount,
    double? TotalRating,
    int? TotalRatingCount,
    List<string> Genres,
    List<string> Themes,
    List<string> GameModes,
    List<string> PlayerPerspectives,
    List<string> Franchises,
    List<string> Collections,
    List<IgdbSimilarGameDto> SimilarGames,
    string? CoverUrl,
    List<IgdbImageDto> Artworks,
    List<IgdbImageDto> Screenshots,
    List<IgdbLanguageDto> Languages,
    IgdbTimeToBeatDto? TimeToBeat,
    DateTimeOffset FetchedAt
);

public sealed record IgdbSimilarGameDto(int IgdbId, string Name, string? CoverUrl);

/// <summary>ThumbUrl = small preview (569x320), FullUrl = 1080p version.</summary>
public sealed record IgdbImageDto(string ThumbUrl, string FullUrl);

/// <summary>One supported language with the kinds of support IGDB lists for it.</summary>
public sealed record IgdbLanguageDto(string Name, string? NativeName, bool Audio, bool Subtitles, bool Interface);

/// <summary>Average hours IGDB users need: hastily = main story only, normally = with some extras, completely = 100 %. Count = number of submissions.</summary>
public sealed record IgdbTimeToBeatDto(double? HastilyHours, double? NormallyHours, double? CompletelyHours, int? Count);

/// <summary>One live Twitch stream of a game. ThumbnailUrl is ready to use (440x248) or null; ChannelUrl opens the stream on twitch.tv.</summary>
public sealed record TwitchStreamDto(
    string UserName,
    string UserLogin,
    string Title,
    int ViewerCount,
    string Language,
    DateTimeOffset? StartedAt,
    string? ThumbnailUrl,
    string ChannelUrl
);

/// <summary>
/// Live streams of a game, most viewers first (GET /api/games/{slug}/streams, GET /api/twitch/streams). CategoryFound = false
/// means Twitch has no category for the game (different from "nobody is live", which is an empty Streams list).
/// </summary>
public sealed record TwitchStreamsDto(
    string? CategoryName,
    bool CategoryFound,
    string? Language,
    List<TwitchStreamDto> Streams,
    DateTimeOffset FetchedAt
);

/// <summary>Body of POST /api/twitch/test: blank values test the stored credentials instead.</summary>
public sealed record TwitchTestRequestDto(string? ClientId, string? ClientSecret);

/// <summary>Result of the credentials check; Message is a ready-to-show text in the language of the request (success or the specific problem).</summary>
public sealed record TwitchTestResponseDto(bool Connected, string Message, bool TokenOk, bool IgdbOk, bool StreamsOk);

/// <summary>Body of PUT /api/games/order: the game slugs in the new dashboard order (first = top left).</summary>
/// <summary>Body of POST /api/ai/gemini/diagnose; blank model = the default model.</summary>
public sealed record GeminiDiagnoseRequest(string? Model);

public sealed record ReorderGamesRequest(List<string> Slugs);

/// <summary>
/// The series (IGDB collection, else franchise) a game belongs to, main games sorted by release (GET /api/games/{slug}/series).
/// Source = "collection" | "franchise". Owned = in the Steam library (false without a Steam key); InVault/Slug/trophies come from
/// the vault, trophies of owned games outside the vault from the library evaluation (null when unknown).
/// </summary>
public sealed record GameSeriesDto(string Name, string Source, List<GameSeriesGameDto> Games);

public sealed record GameSeriesGameDto(
    int IgdbId,
    string Name,
    string? CoverUrl,
    int? ReleaseYear,
    int? SteamAppId,
    bool Owned,
    bool InVault,
    string? Slug,
    int? TrophiesUnlocked,
    int? TrophiesTotal,
    bool IsCurrent
);

/// <summary>
/// What PlatinVault recorded about a game over time (GET /api/games/{slug}/history). PlayerCounts = raw samples (30 minutes apart,
/// at most the last 30 days), PlayerCountsDaily = condensed older days, PlaytimeDaily = minutes played per day (UTC). All lists are
/// empty while nothing is recorded; TrackingSince = the first recording of this game.
/// </summary>
public sealed record GameHistoryDto(
    DateTimeOffset? TrackingSince,
    List<PlayerCountPointDto> PlayerCounts,
    List<PlayerCountDayDto> PlayerCountsDaily,
    List<PlaytimeDayDto> PlaytimeDaily
);

public sealed record PlayerCountPointDto(DateTimeOffset At, int Count);

public sealed record PlayerCountDayDto(DateOnly Date, int Min, double Avg, int Max);

public sealed record PlaytimeDayDto(DateOnly Date, int Minutes);
