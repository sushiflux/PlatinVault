using Companion.Api.Services;

namespace Companion.Api.Endpoints;

// Shared contract of manual games (added by hand, with or without IGDB), their platform, the cover upload and the trophy list that is
// researched by the AI and ticked by hand. Routes, rules and status codes are written next to each record; the endpoints live in
// ManualGameEndpoints.cs (games, IGDB search, cover), TrophyEndpoints.cs (list, tick) and ManualTrophyEndpoints.cs (own-subscription way),
// the AI job in AiActionEndpoints.cs. Every error is a problem answer { title, detail, status } with texts from ServerTexts; every
// state-changing call needs the header X-Requested-With: PlatinVault.

// ---------------------------------------------------------------------------------------------------------------------------------------
// Platform
// ---------------------------------------------------------------------------------------------------------------------------------------

/// <summary>A platform choice: Platform = one of <c>GamePlatforms.Ids</c>, Variant = "ps5" / "ps4" for "playstation", else null.</summary>
public sealed record PlatformChoiceDto(string Platform, string? Variant);

/// <summary>A vault game that already has this IGDB game (or title) on a platform: shown as "already added" in the add dialog.</summary>
public sealed record ManualGameRefDto(string Slug, string Platform, string? Variant);

// ---------------------------------------------------------------------------------------------------------------------------------------
// IGDB search for the add dialog. Both calls answer 204 without Twitch credentials and 502 (with detail) when IGDB fails.
// ---------------------------------------------------------------------------------------------------------------------------------------

/// <summary>
/// GET /api/igdb/search?q= (1-120 characters): all digits = the IGDB game with that id, an igdb.com/games/&lt;slug&gt; address = that game,
/// else a name search. Up to 20 hits; only main games, remakes, remasters, standalone expansions, ports and expanded games.
/// </summary>
public sealed record IgdbSearchResponseDto(List<IgdbSearchHitDto> Results);

/// <summary>
/// One hit of the IGDB search. ReleaseYear / FirstReleaseAt = first release (UTC); CoverUrl = complete https address (t_cover_big) or null.
/// Platforms = the platforms IGDB lists, each with the PlatinVault platform it maps to (null = none of ours). Existing = vault games with this IGDB id.
/// </summary>
public sealed record IgdbSearchHitDto(
    int IgdbId,
    string Name,
    int? ReleaseYear,
    DateTimeOffset? FirstReleaseAt,
    string? CoverUrl,
    List<IgdbPlatformDto> Platforms,
    List<ManualGameRefDto> Existing);

/// <summary>A platform as IGDB names it; Maps = our platform (+ variant) by abbreviation / slug / name, never by IGDB's numeric id (D9).</summary>
public sealed record IgdbPlatformDto(string Name, string? Abbreviation, PlatformChoiceDto? Maps);

// GET /api/igdb/games/{igdbId} answers with IgdbGameDto (Dtos.cs), which carries FirstReleaseAt, Platforms, Developers and Publishers as well.

// ---------------------------------------------------------------------------------------------------------------------------------------
// Manual game
// ---------------------------------------------------------------------------------------------------------------------------------------

/// <summary>
/// POST /api/games/manual -> 201 GameSummaryDto.
/// Platform: one of <c>GamePlatforms.ManualIds</c> (not "steam"); PlatformVariant: "ps5" / "ps4" exactly for "playstation", else null/omitted
/// (both checked by <c>GamePlatforms.Validate</c>, 400 with <c>ServerTexts.Platforms</c>). Without IgdbId the Title is required (at most 240
/// characters); with IgdbId the server fills title (a given Title wins), cover, banner and facts from IGDB - 400 when no Twitch credentials are
/// stored. A game with the same IgdbId + platform + variant (or, without IGDB, the same title + platform + variant) already exists -> 409
/// problem with the extension <c>existingSlug</c>. The new game is a favourite, sorted last, its guide language is the UI language; no tabs.
/// </summary>
public sealed record CreateManualGameRequest(string? Title, string? Platform, string? PlatformVariant, int? IgdbId);

// Cover (decision D1: stored in the database table game_covers, never as a file):
//   PUT    /api/games/{slug}/cover   raw image body, Content-Type image/png | image/jpeg | image/webp | image/gif (checked against the magic
//                                    bytes, at most CoverStore.MaxBytes, never SVG) -> 200 GameSummaryDto (CoverUrl = CoverStore.UrlFor(...))
//   DELETE /api/games/{slug}/cover   removes the uploaded cover -> 200 GameSummaryDto (CoverUrl no longer points at the upload)
//   GET    /api/covers/{gameId:N}?v=<ticks>  the image (X-Content-Type-Options: nosniff, Cache-Control: public, max-age=31536000, immutable), 404 if none
// Removing a manual game: DELETE /api/games/{id} (cascade: guide, ticks, trophies, cover, translations).

// ---------------------------------------------------------------------------------------------------------------------------------------
// Trophy list of a manual game
// ---------------------------------------------------------------------------------------------------------------------------------------

/// <summary>One source of the researched list (only real http(s) addresses).</summary>
public sealed record TrophySourceDto(string Title, string Url);

/// <summary>
/// Translation state of the trophy list into the requested language, same pattern as <see cref="GuideTranslationDto"/>: State "original" (the list is
/// in that language), "complete" (the answer holds the translation), "missing" (the answer holds the original; Running / Error tell why).
/// </summary>
public sealed record TrophyTranslationDto(
    string Language,
    string SourceLanguage,
    string State,
    bool Running,
    string? Error,
    string? ProviderMessage,
    string? AiNotice = null);

/// <summary>Which popup a tick caused: Trophy = the normal popup of the ticked trophy, Platinum = the platinum moment.</summary>
public sealed record TrophyAnnouncedDto(bool Trophy, bool Platinum);

/// <summary>
/// GET /api/games/{slug}/trophies -> the trophy list of a manual game (400 for a Steam game; 404 unknown slug). A superset of the fields of
/// <see cref="LiveGameAchievementsDto"/> the trophy UI reads (no LiveGameInfoDto: the game is the page's own).
/// Supported = false for Nintendo (no trophies; Achievements empty). Kind "trophies" (PlayStation) | "achievements" (everything else).
/// ResearchedAt null = no list yet. Language = language of the texts in this answer (the translation when complete, else the original's).
/// Platinum/Gold/Silver/Bronze count by tier (<c>TrophyTiers.For</c>). PlatinumAt = the platinum moment (D3; on platforms without a platinum
/// grade: 100 %). Announced is set only on the answer of a tick (POST .../unlock), null otherwise.
/// </summary>
public sealed record ManualTrophyListDto(
    Guid GameId,
    string Slug,
    string Platform,
    string? PlatformVariant,
    bool Supported,
    string Kind,
    DateTimeOffset? ResearchedAt,
    List<TrophySourceDto> Sources,
    string Language,
    TrophyTranslationDto? Translation,
    List<ManualTrophyItemDto> Achievements,
    int TotalCount,
    int AchievedCount,
    int RemainingCount,
    double ProgressPercentage,
    bool Is100Percent,
    LiveTrophyTierSummaryDto Platinum,
    LiveTrophyTierSummaryDto Gold,
    LiveTrophyTierSummaryDto Silver,
    LiveTrophyTierSummaryDto Bronze,
    DateTimeOffset? PlatinumAt,
    // The most recently ticked trophy (like LiveGameAchievementsDto.LatestAchievement); null when nothing is ticked
    ManualTrophyItemDto? LatestAchievement = null)
{
    public TrophyAnnouncedDto? Announced { get; init; }
}

/// <summary>
/// One trophy: every field of <see cref="LiveAchievementItemDto"/> (ApiName = TrophyKey, IconUrl/IconGrayUrl = "" - the UI draws its own glyph,
/// Achieved = ticked, UnlockTime = UnlockedAt, GlobalPercent = the stated share or 0 with HasGlobalPercent, RarityTier = <c>TrophyTiers.For</c>,
/// RarityBand/RarityLabel from the Steam scale only with a share) plus the fields of the researched list.
/// Grade only on PlayStation; Points = gamerscore / XP; Group null = base game, else the DLC; Category / Tip "" = none.
/// </summary>
public sealed record ManualTrophyItemDto(
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
    bool HasGlobalPercent,
    string RarityTier,
    string RarityLabel,
    string RarityBand,
    bool IsHidden,
    string UserNote,
    string? Grade,
    int? Points,
    bool Missable,
    string? Group,
    string Category,
    string Tip,
    int SortOrder,
    string? OriginalName)
{
    /// <summary>The same trophy as a live item, e.g. for <c>LiveAnnouncer.AnnounceUnlockAsync</c> (popup, event log, catch-up).</summary>
    public LiveAchievementItemDto ToLiveItem() => new(
        ApiName, DisplayName, Description, OriginalDescription, IsTranslated, IconUrl, IconGrayUrl, Achieved, UnlockTime,
        GlobalPercent, RarityTier, RarityLabel, IsHidden, UserNote)
    {
        HasGlobalPercent = HasGlobalPercent,
        RarityBand = RarityBand,
    };
}

/// <summary>
/// POST /api/games/{slug}/trophies/{trophyKey}/unlock -> ManualTrophyListDto with Announced. Unlocked = true ticks (popup + sound like a
/// live unlock, exactly once even with two servers; the platinum trophy / 100 % gives the platinum moment), false un-ticks silently.
/// 400 for a Steam game, 404 unknown slug or trophy key.
/// </summary>
public sealed record SetTrophyUnlockedRequest(bool Unlocked);

/// <summary>What one AI research of a manual game should produce; null = the server's default (see <see cref="AiTrophiesRequest"/>).</summary>
public sealed record TrophyResearchIncludeDto(bool? Trophies, bool? Facts);

/// <summary>
/// POST /api/games/{slug}/ai-trophies -> 202 AiJobDto (kind "trophies"), result <c>AiResultDto&lt;ManualTrophyResearchResultDto&gt;</c>.
/// Include defaults: Trophies = the platform has trophies; Facts = FactsSource is not "igdb" and facts are missing (or asked for). Both false
/// (e.g. Nintendo with complete facts) -> 400. A Steam game -> 400.
/// </summary>
public sealed record AiTrophiesRequest(string? ProviderOverride, TrophyResearchIncludeDto? Include);

/// <summary>
/// The saved result of a research (AI job or own subscription): the list as stored now, whether facts were written, how many existing ticks
/// were carried over (same key or 1:1 by name), and how many ticked trophies the new list did not contain but were kept (appended).
/// </summary>
public sealed record ManualTrophyResearchResultDto(ManualTrophyListDto List, bool FactsUpdated, int KeptTicks, int RetainedOldTicked);

// Own subscription (ManualTrophyEndpoints.cs; nothing is ever sent to an AI provider):
//   POST /api/games/{slug}/manual/trophies-prompt  ManualTrophiesPromptRequest -> ManualPromptDto (Kind "trophies", FileName "platinvault-<slug>-trophies.json")
//   POST /api/games/{slug}/manual/trophies-check   ManualAnswerRequest         -> ManualTrophyCheckDto
//   POST /api/games/{slug}/manual/trophies-answer  ManualAnswerRequest         -> ManualTrophyResearchResultDto (400 with errors[] when unreadable)

/// <summary>Body of the trophies prompt: what the prompt asks for (null = the same defaults as <see cref="AiTrophiesRequest"/>).</summary>
public sealed record ManualTrophiesPromptRequest(TrophyResearchIncludeDto? Include);

/// <summary>One trophy of a pasted answer as it would be saved (preview before saving).</summary>
public sealed record ManualTrophyPreviewDto(string Key, string Name, string? Grade, int? Points, bool Hidden, string? Group);

/// <summary>
/// What a pasted trophy answer contains. Count = trophies read; ByGrade = count per grade (PlayStation: platinum/gold/silver/bronze, else empty);
/// Hidden / Dlc = hidden and DLC trophies; Preview = the first 12; KeepsTicks = existing ticks that would be carried over, DropsTicks = unticked
/// old trophies that would be removed (ticked ones are always kept). HasFacts = the answer also carries game facts that would be written.
/// </summary>
public sealed record ManualTrophyCheckDto(
    bool Ok,
    List<ManualIssueDto> Errors,
    List<ManualIssueDto> Warnings,
    int Count,
    Dictionary<string, int> ByGrade,
    int Hidden,
    int Dlc,
    List<ManualTrophyPreviewDto> Preview,
    int KeepsTicks,
    int DropsTicks,
    bool HasFacts = false);

// ---------------------------------------------------------------------------------------------------------------------------------------
// Notes, pins, cabinet with a game id (manual games have no Steam AppID)
// ---------------------------------------------------------------------------------------------------------------------------------------
//   POST /api/live/note           SaveAchievementNoteRequest with GameId (AppId 0) -> stored as note.g.{gameId:N}.{trophyKey}
//   GET|DELETE /api/live/pins     ?gameId=<guid> instead of ?appId=; POST /api/live/pins and /api/live/pins/remove with GameId instead of AppId
//                                 -> PinnedAchievementsDto with GameId (AppId 0); stored as pins.g.{gameId:N}
//   hub "PinsChanged"             { appId, gameId } (gameId null for Steam games, appId 0 for manual games)
//   GET /api/showcase             ShowcaseGameDto + Key ("steam:{appId}" | "game:{guid}"), GameId, Platform, PlatformVariant (AppId 0 for manual)
//   GET|PUT /api/showcase/presented  CabinetPresentedDto + GameIds (string[] of guids); request + GameIds; stored as a union in cabinet.presented_games
