import type { UiLanguage } from '../i18n'

export interface BrandingInfo {
  productName: string
  tagline: string
  company: string
  // The public source code repository (PlatinVault is AGPL-3.0-only; the licence viewer links to it)
  sourceUrl?: string
  version: string
  // Readable version as the server writes it, e.g. "v2026.11.12 (13.557)", beta "v2026.11.12 (13.557 β)", dev "v2026.11.12 (13.557-dev)"
  // "desktop" = the Windows program shows this UI in its own window and reads Steam on this PC (no bridge needed); "server" otherwise
  host?: 'desktop' | 'server'
  // The provider's own technical text (English); errorMessage is our short explanation. Fold it under "technical details".
  providerMessage?: string | null
}

export type NotificationPosition =
  | 'top-left'
  | 'top-center'
  | 'top-right'
  | 'center-left'
  | 'center'
  | 'center-right'
  | 'bottom-left'
  | 'bottom-center'
  | 'bottom-right'
  // The whole window is taken over by the achievement for a few seconds (components/FullscreenAchievement.tsx)
  | 'fullscreen'

/** The nine spots where the small popup card sits; "fullscreen" is the tenth way and has no spot. */
export type CardPosition = Exclude<NotificationPosition, 'fullscreen'>

/**
 * "Trophäen-Popup anzeigen" (setting live.popup_target): only in the app / the browser, only over the game (overlay of the Windows
 * program), or both. Without the Windows program there is no overlay: the effect is always "app" (utils/popupTarget.ts).
 */
export type PopupTarget = 'app' | 'overlay' | 'both'

export type RarityTier = 'Bronze' | 'Silver' | 'Gold' | 'Platinum'

/** Selectable trophy sound themes (see utils/audio.ts and /public/sounds/<theme>/). */
export type SoundTheme = 'kristall' | 'orchester' | 'spieluhr'

export interface LiveGameInfoDto {
  appId: number
  name: string
  headerImage: string
  backgroundImage: string
  isRunning: boolean
  playtimeForeverMinutes: number
  playtime2WeeksMinutes: number
  formattedPlaytimeTotal: string
  formattedPlaytimeRecent: string
  formattedLastPlayed: string
  linkedGuideSlug: string | null
}

export interface LiveAchievementItemDto {
  apiName: string
  displayName: string
  description: string
  originalDescription: string | null
  isTranslated: boolean
  iconUrl: string
  iconGrayUrl: string
  achieved: boolean
  unlockTime: string | null
  globalPercent: number
  // false = Steam delivered no worldwide share, globalPercent is only a placeholder and must not enter statistics
  hasGlobalPercent: boolean
  rarityTier: RarityTier
  rarityLabel: string
  // Language-neutral rarity step the UI translates: "ultraRare" | "veryRare" | "rare" | "uncommon" | "common"; empty while hasGlobalPercent is false
  rarityBand?: string
  isHidden: boolean
  userNote: string
  // Steam's progress counter ("50 / 400") for games that define one; missing otherwise
  progressCurrent?: number | null
  progressMax?: number | null
}

export interface LiveTrophyTierSummaryDto {
  achieved: number
  total: number
}

export interface LiveGameAchievementsDto {
  game: LiveGameInfoDto
  achievements: LiveAchievementItemDto[]
  totalCount: number
  achievedCount: number
  remainingCount: number
  progressPercentage: number
  is100Percent: boolean
  platinum: LiveTrophyTierSummaryDto
  gold: LiveTrophyTierSummaryDto
  silver: LiveTrophyTierSummaryDto
  bronze: LiveTrophyTierSummaryDto
  latestAchievement: LiveAchievementItemDto | null
  syncedGuideItemsCount: number
}

export interface LiveStatusDto {
  runningAppId: number
  steamId64: string
  runningGameSlug: string | null
  runningGameTracked: boolean
  localSteamAvailable: boolean
  lastBridgeEventAt: string | null
  // Title of the running game when it is in the vault, else null
  runningGameName?: string | null
}

// Second argument of the hub events "AchievementUnlocked"/"PlatinumUnlocked": identifies the event for the catch-up
export interface LiveEventMeta {
  eventId: number
  instanceId: string
}

// One announced live event (GET /api/live/events)
export interface LiveEventDto {
  id: number
  at: string
  kind: 'achievement' | 'platinum'
  achievement: LiveAchievementItemDto | null
  gameName: string | null
  appId: number
  total: number | null
}

// The live events of the last minutes; instanceId changes with every server start
export interface LiveEventsResponseDto {
  instanceId: string
  latestId: number
  events: LiveEventDto[]
}

export interface GameSummaryDto {
  id: string
  slug: string
  title: string
  subtitle: string
  steamAppId: number | null
  coverUrl: string
  heroBannerUrl: string
  accentColor: string
  estimatedHours: string
  missablesInfo: string
  difficultyInfo: string
  genreTags: string
  // Language the guide text is written in ("de" | "en"); guides from before the field existed read as "de"
  contentLanguage: string
  // Genres from the Steam store in the app language (IGDB's English ones for a game Steam has none for); empty until the first card refresh
  steamGenres?: string[]
  totalCheckableItems: number
  checkedItemsCount: number
  progressPercent: number
  tabCount: number
  // Trophy progress from Steam - the only progress shown on cards and in the header
  trophiesUnlocked: number
  trophiesTotal: number
  trophiesPercent: number
  platinumAt: string | null
  // When the trophy state was last read from Steam (null = not yet)
  trophiesCheckedAt: string | null
  // Star on the dashboard; the "Favoriten" view shows only these
  isFavorite: boolean
  // Card facts, null until known (the card hides what is missing)
  playtimeMinutes: number | null
  lastPlayedAt: string | null
  metacriticScore: number | null
  reviewScoreDesc: string | null
  reviewPercent: number | null
  releaseDate: string | null
  // From IGDB (only with Twitch credentials): average hours to 100 % completion, critics rating 0-100
  completionHours: number | null
  igdbRating: number | null
  // Values of the dashboard's sort orders from the library data (null/missing = unknown): release date, size on disk of the installed
  // game, and the Metacritic score / share of positive reviews (the card's own value, else the library's)
  releaseAt?: string | null
  sizeOnDiskBytes?: number | null
  // Install size estimated from Steam's depot data - known for games that are not installed too
  installSizeBytes?: number | null
  sortMetacritic?: number | null
  sortReviewPercent?: number | null
  updatedAt: string
}

export interface GameDetailDto {
  id: string
  slug: string
  title: string
  subtitle: string
  steamAppId: number | null
  coverUrl: string
  heroBannerUrl: string
  accentColor: string
  estimatedHours: string
  missablesInfo: string
  difficultyInfo: string
  genreTags: string
  contentLanguage: string
  steamGenres?: string[]
  totalCheckableItems: number
  checkedItemsCount: number
  progressPercent: number
  tabs: GuideTabDto[]
  checkedItemKeys: string[]
  // Language of the texts in this answer: the requested one once its translation is complete, else the guide's own (contentLanguage)
  language: string
  translation: GuideTranslationDto
}

// Translation of a guide into one language. state: "original" = that is the guide's own language; "complete" = every chapter and the card
// texts are translated (the answer holds the translated version); "missing" = something is not translated yet (the answer holds the
// original) - running: a job works on it; error + providerMessage: why the last job failed (providerMessage = the AI provider's own text).
export interface GuideTranslationDto {
  language: string
  sourceLanguage: string
  state: 'original' | 'complete' | 'missing'
  totalTabs: number
  translatedTabs: number
  running: boolean
  error: string | null
  providerMessage: string | null
  // "Gemini quota used up – continuing with Claude." when the translation job had to change provider
  aiNotice?: string | null
}

export interface GuideTabDto {
  id: string
  slug: string
  title: string
  icon: string
  sortOrder: number
  totalCheckableItems: number
  checkedItemsCount: number
  sections: GuideSectionDto[]
  // The Steam achievement table: no chapter, only the source of the tips on the live trophies tab
  isAchievementList: boolean
  // The group the chapter is listed under in the side navigation: one of GuideGroupKey (explicit choice of the AI/user, else decided from the title)
  groupKey: GuideGroupKey
}

export interface GuideSectionDto {
  // 'story' = readable prose (paragraphs; with variant 'steps' the numbered steps of a walkthrough part), used by the optional Story & Lore tab
  // 'puzzle' = a riddle with its location and a hidden solution (see GuidePuzzleDto), 'heading' = a sub-heading of the walkthrough
  type: 'alert' | 'cards_grid' | 'checklist_table' | 'story' | 'puzzle' | 'heading' | string
  title?: string | null
  variant?: string | null
  icon?: string | null
  htmlContent?: string | null
  columns?: number | null
  cards?: GuideCardDto[] | null
  table?: GuideTableDto | null
  paragraphs?: string[] | null
  // Spoiler sections stay collapsed until the reader opens them
  spoiler?: boolean | null
  // Set on the one placeholder section of a chapter whose research failed: the remembered topic and the reason
  failedChapter?: GuideFailedChapterDto | null
  // Type 'puzzle' only
  puzzle?: GuidePuzzleDto | null
  // Type 'alert' only: a typed hint of the walkthrough (own label and symbol)
  kind?: GuideAlertKind | string | null
}

// The typed hints of a walkthrough: important item, missable from here, optional content available, tip
export type GuideAlertKind = 'must_have' | 'missable' | 'optional' | 'tip'

// A puzzle: location, requirements and the missable flag are always shown; hints and steps (the complete solution) are revealed on request.
// documented = false (no steps): the UI says "solution not documented" instead of offering the solution button.
export interface GuidePuzzleDto {
  itemId?: string | null
  location?: string | null
  requirements?: string | null
  missable?: boolean | null
  hints?: string[] | null
  steps?: string[] | null
  source?: string | null
  documented: boolean
}

// What a failed chapter remembers: the planned topic (so it can be researched again exactly like planned), OUR explanation in the language of the
// guide, and the provider's own text for "technical details"
export interface GuideFailedChapterDto {
  focus: string
  topicId: string | null
  message: string
  providerMessage: string | null
  provider: string | null
}

export interface GuideCardDto {
  title: string
  badgeText?: string | null
  badgeVariant?: string | null
  icon?: string | null
  introText?: string | null
  bullets?: GuideCardBulletDto[] | null
}

export interface GuideCardBulletDto {
  icon: string
  title: string
  body: string
}

export interface GuideTableDto {
  tableId: string
  caption: string
  checkable: boolean
  searchPlaceholder?: string | null
  categoryFilterLabel?: string | null
  categories?: string[] | null
  secondaryFilterLabel?: string | null
  secondaryCategories?: string[] | null
  columns: GuideTableColumnDto[]
  rows: GuideTableRowDto[]
}

export interface GuideTableColumnDto {
  key: string
  label: string
  isRowHeader?: boolean
  badgeVariant?: string | null
}

export interface GuideTableRowDto {
  itemId: string
  filterCategory?: string | null
  filterSecondary?: string | null
  cells: Record<string, string>
}

export interface SteamSearchItemDto {
  appId: number
  name: string
  coverUrl: string
  heroBannerUrl: string
  headerImageUrl: string
}

export interface SteamOwnedGameDto {
  appId: number
  name: string
  playtimeHours: number
  coverUrl: string
  heroBannerUrl: string
  headerImageUrl: string
  alreadyInVault: boolean
  existingSlug: string | null
  // Starred = on the dashboard
  isFavorite: boolean
  lastPlayedAt: string | null
  // From the library evaluation (missing until checked; total 0 = no achievements)
  trophiesUnlocked?: number | null
  trophiesTotal?: number | null
  // Values of the dashboard's sort orders (null/missing = unknown)
  releaseAt?: string | null
  sizeOnDiskBytes?: number | null
  installSizeBytes?: number | null
  sortMetacritic?: number | null
  sortReviewPercent?: number | null
}

// Trophy figures of the whole Steam library (dashboard); scanning while the background evaluation runs
export interface LibrarySummaryDto {
  libraryGameCount: number
  gamesWithAchievements: number
  trophiesUnlocked: number
  trophiesTotal: number
  perfectGames: number
  scanning: boolean
  scanDone: number
  scanTotal: number
  // The background lookup of release dates / reviews in Steam's store (only for apps no local Steam source knows)
  metaScanning?: boolean
  metaDone?: number
  metaTotal?: number
}

export interface SteamLibraryResponseDto {
  resolvedSteamId: string
  totalGames: number
  games: SteamOwnedGameDto[]
}

export interface SettingsResponseDto {
  geminiModel: string
  claudeModel: string
  openAiModel: string
  steamUserId: string
  autoDetectGame: boolean
  playSounds: boolean
  soundVolume: number
  soundTheme: SoundTheme
  popupPosition: NotificationPosition
  popupTarget: PopupTarget
  // Steam language name for achievement texts ("german" / "english"); follows uiLanguage, never edited on its own
  language: string
  // The language of the whole app ("de" / "en"); null until the user has chosen one (first start: language screen)
  uiLanguage: UiLanguage | null
  autoTranslate: boolean
  hasGeminiKey: boolean
  geminiKeyHint: string
  hasClaudeKey: boolean
  claudeKeyHint: string
  hasOpenAiKey: boolean
  openAiKeyHint: string
  hasSteamKey: boolean
  steamKeyHint: string
  // Twitch developer app (Client-ID + Client-Secret, shared by live streams and IGDB); hint = masked Client-ID
  hasTwitchKey: boolean
  twitchKeyHint: string
  // Switch "features.twitch_streams" (on by default; only has an effect while the keys are stored)
  twitchStreamsEnabled: boolean
  // Switch "features.achievement_notes" (on by default): off hides the personal notes on achievements everywhere (stored notes are kept)
  achievementNotesEnabled: boolean
  // AI selection: "default" = one Standard-KI writes a guide, "mixed" = chapters spread over every provider with a key, "ask" = every
  // AI action asks first. aiDefaultProvider = the Standard-KI that is effective now (null without any key).
  aiSelectionMode: AiSelectionMode
  aiDefaultProvider: AiProviderId | null
  aiAutoFallback: boolean
  // The research building blocks that are ticked (nothing ticked = the built-in default behaviour) and the ids the server accepts
  researchBlocks: ResearchBlocksDto
  researchBlocksPolicy: ResearchBlocksPolicyDto
  // Switch "features.auto_switch_game" (on by default): switch to the game page when a vault game is started
  autoSwitchGameEnabled: boolean
  // Switch "Beta-Versionen erhalten" (updates.beta_channel, off by default): the update check also offers betas
  betaUpdatesEnabled: boolean
  // The dashboard's sort order per view as "<order>:<asc|desc>" (see utils/dashboardSort.ts)
  dashboardSortFavorites?: string
  dashboardSortAll?: string
}

export type AiProviderId = 'gemini' | 'claude' | 'openai'
export type AiSelectionMode = 'default' | 'mixed' | 'ask'

// Research building blocks (Settings > AI research). scope / spoilers / presentation: one id or null; focus: several ids.
export interface ResearchBlocksDto {
  scope: string | null
  focus: string[]
  spoilers: string | null
  presentation: string | null
}

export interface ResearchBlocksPolicyDto {
  scope: string[]
  focus: string[]
  spoilers: string[]
  presentation: string[]
}

// GET /api/ai/status: the stored selection and every provider with its key and how it is doing right now
export type AiProviderState = 'ok' | 'quota' | 'invalidKey' | 'unavailable'

export interface AiProviderStatusDto {
  id: AiProviderId
  hasKey: boolean
  model: string
  webSearch: boolean
  state: AiProviderState
  since: string | null
  until: string | null
  // Our explanation of the failure (language of the request)
  message: string | null
}

export interface AiStatusDto {
  mode: AiSelectionMode
  defaultProvider: AiProviderId | null
  autoFallback: boolean
  // What a plain action would use right now (null in mixed mode or without a key)
  activeProvider: AiProviderId | null
  providers: AiProviderStatusDto[]
}

// What an AI action tells back: who answered, and every change of provider on the way (the text is a ready sentence in the UI language)
export interface AiSwitchDto {
  from: AiProviderId
  to: AiProviderId
  reason: 'quota' | 'invalidKey' | 'unavailable'
  text: string
}

export interface AiUsageDto {
  providers: AiProviderId[]
  switches: AiSwitchDto[]
}

export interface AiResultDto<T> {
  game: T
  ai: AiUsageDto
}

// A background AI action (POST ai-generate / ai-plan / ai-complete / ai-expand-tab / ai-lore start one, GET /api/ai/jobs/{id} reads it).
// The long-running requests are jobs: the answer comes back through polling, so no single HTTP request has to survive minutes.
export type AiJobKind = 'generate' | 'plan' | 'complete' | 'expand' | 'lore' | 'retry'

// "chapters" = Done of Total chapters finished, Title = the latest one started; "loreParts" the same for Story & Lore; "chapter" = one chapter (Title)
export type AiJobStage = 'starting' | 'facts' | 'blueprint' | 'chapters' | 'chapter' | 'loreParts' | 'saving'

export interface AiJobDto {
  id: string
  kind: AiJobKind
  slug: string | null
  running: boolean
  // How long it runs, and how long the step has not changed (seconds, from the server's clock)
  elapsedSeconds: number
  // The same running time without rounding; the progress clock anchors on it
  elapsedMilliseconds: number
  stepIdleSeconds: number
  step: { stage: AiJobStage; done: number; total: number; title: string | null }
  // "Gemini quota used up – continuing with Claude." once the action had to change provider (also when it failed on the next AI as well)
  switchNotice: string | null
  // Finished with a failure: our sentence + the provider's own text + which AI failed and why
  error: string | null
  providerMessage: string | null
  aiProvider: string | null
  aiFailure: string | null
  // Finished: what the request used to answer (AiResultDto<GameDetailDto>, AiCompleteGuideResponse, GuidePlanDto, AiResultDto<GameSummaryDto>)
  result: unknown
}

// Every field is optional: omitted = keep what is stored (so e.g. { uiLanguage } alone changes nothing else)
export interface UpdateSettingsRequest {
  geminiModel?: string
  claudeModel?: string
  openAiModel?: string
  steamUserId?: string
  autoDetectGame?: boolean
  playSounds?: boolean
  soundVolume?: number
  soundTheme?: SoundTheme
  popupPosition?: NotificationPosition
  popupTarget?: PopupTarget
  // "de" or "en"; the server also sets the Steam achievement language from it
  uiLanguage?: UiLanguage
  autoTranslate?: boolean
  geminiApiKey?: string
  clearGeminiKey?: boolean
  claudeApiKey?: string
  clearClaudeKey?: boolean
  openAiApiKey?: string
  clearOpenAiKey?: boolean
  steamApiKey?: string
  clearSteamKey?: boolean
  // Blank/omitted = keep the stored value; clearTwitchKey removes both Client-ID and Client-Secret
  twitchClientId?: string
  twitchClientSecret?: string
  clearTwitchKey?: boolean
  twitchStreamsEnabled?: boolean
  achievementNotesEnabled?: boolean
  // AI selection (omitted = keep)
  aiSelectionMode?: AiSelectionMode
  aiDefaultProvider?: AiProviderId
  aiAutoFallback?: boolean
  // Omitted = keep; an object replaces the whole selection (everything empty = nothing ticked)
  researchBlocks?: ResearchBlocksDto
  autoSwitchGameEnabled?: boolean
  betaUpdatesEnabled?: boolean
  // "Nach Updates suchen" (see UpdateCheckInterval); the server rejects anything else with 400
  updateCheckInterval?: UpdateCheckInterval
  dashboardSortFavorites?: string
  dashboardSortAll?: string
}

export interface AiExpandTabRequest {
  tabTitle: string
  topicPrompt: string
  providerOverride?: string
}

export interface AiModelOptionDto {
  id: string
  label: string
}

export interface AiProviderModelsResponseDto {
  provider: string
  connected: boolean
  errorMessage: string | null
  models: AiModelOptionDto[]
}

export interface GameNewsItemDto {
  title: string
  url: string
  date: string
  isUpdate: boolean
}

export interface PlayerGameStatsDto {
  playtimeHours: number | null
  playtime2WeeksHours: number | null
  lastPlayed: string | null
  achievementsUnlocked: number
  achievementsTotal: number
  progressPercent: number
  rarestUnlockedName: string | null
  rarestUnlockedPercent: number | null
  firstUnlock: string | null
  lastUnlock: string | null
}

export interface GameStatsDto {
  appId: number
  releaseDate: string | null
  comingSoon: boolean
  developers: string[]
  publishers: string[]
  genres: string[]
  metacriticScore: number | null
  achievementsTotal: number | null
  reviewScoreDesc: string | null
  reviewTotal: number | null
  reviewPositive: number | null
  reviewPercentPositive: number | null
  currentPlayers: number | null
  news: GameNewsItemDto[]
  latestUpdate: GameNewsItemDto | null
  player: PlayerGameStatsDto | null
  // Library data (local Steam files / bridge), null/missing = unknown
  sizeOnDiskBytes?: number | null
  installSizeBytes?: number | null
}

// GET /api/bridge/token
export interface BridgeTokenStatusDto {
  hasToken: boolean
  hint: string | null
  lastSeenAt: string | null
  // Version of the bridge exe bundled with this server (null = not bundled, e.g. not built yet in development)
  bundledVersion?: string | null
}

// POST /api/bridge/token: the plain token is only ever returned here, once
export interface BridgeTokenCreatedDto {
  token: string
  hint: string
}

// The fixed chapter groups of the side navigation, in display order (GET /api/guide/groups returns them with their labels)
export type GuideGroupKey = 'start' | 'story' | 'characters' | 'combat' | 'progress' | 'explore' | 'economy' | 'extras'

export interface GuideGroupDto {
  key: GuideGroupKey
  label: string
}

// One entry of the fixed topic list every guide is checked against (GET /api/guide/topics)
export interface GuideTopicDto {
  id: string
  title: string
  icon: string
  brief: string
  // The group this standard topic belongs to
  groupKey: GuideGroupKey
}

// ---- The guide via the player's own AI subscription (prompts to copy, answers to paste; nothing is sent to an AI) ----

// kind = "blueprint" (the chapter list) or "chapter"
export interface ManualPromptDto {
  kind: 'blueprint' | 'chapter'
  prompt: string
  fileName: string
  language: string
  steamAppId: number | null
  title: string
}

// One finding about a pasted answer, in the language of the UI; details = the technical text
export interface ManualIssueDto {
  code: string
  message: string
  details: string | null
}

export interface ManualChapterPreviewDto {
  title: string
  icon: string
  sectionCount: number
  tableRows: number
  cards: number
  sectionTitles: string[]
}

export interface ManualBlueprintCheckDto {
  ok: boolean
  errors: ManualIssueDto[]
  warnings: ManualIssueDto[]
  plan: GuidePlanDto | null
}

export interface ManualChapterCheckDto {
  ok: boolean
  errors: ManualIssueDto[]
  warnings: ManualIssueDto[]
  preview: ManualChapterPreviewDto | null
  // The answer says the topic does not exist in this game: the sentence to show (saving then creates no chapter / removes the placeholder), else null
  notApplicable?: string | null
}

// What the prompt (and the answer) of one chapter is about; tabId = the failed chapter whose placeholder the answer replaces
export interface ManualChapterSeed {
  title: string
  icon: string
  focus: string
  topicId: string | null
  tabId?: string
  sourceSituation?: string
}

export interface ManualSavedDto {
  game: GameDetailDto
  // The saved chapter; with dropped the removed placeholder (or null)
  tabId: string | null
  replaced: boolean
  // The answer said the topic does not exist in the game: no chapter was created
  dropped?: boolean
}

// POST /api/games/{slug}/ai-retry-chapters: retried = chapters asked for, succeeded = replaced by real content, failedTitles = failed again,
// droppedTitles = removed because the research says the game does not have the topic
export interface AiRetryChaptersResponse {
  retried: number
  succeeded: number
  failedTitles: string[]
  droppedTitles?: string[]
  game: GameDetailDto
  ai: AiUsageDto
}

export interface AiCompleteGuideResponse {
  addedCount: number
  addedTitles: string[]
  // Planned chapters left out because the research says the game does not have their topic
  droppedTitles?: string[]
  game: GameDetailDto
  ai: AiUsageDto
}

// Guide builder ("Klick-Prompt"): POST /api/games/{slug}/ai-plan
export interface GuidePlanProfileDto {
  id: string
  name: string
  fromSteamTags: boolean
}

export interface GuidePlanTopicDto {
  id: string
  title: string
  icon: string
  focus: string
  recommended: boolean
  required: boolean
  covered: boolean
  reason: string | null
}

// The size the research scope allows (compact / detailed / exhaustive): min-max chapters, `suggested` = how many the plan ticks by default
export interface GuidePlanBudgetDto {
  scope: 'compact' | 'detailed' | 'exhaustive'
  min: number
  max: number
  suggested: number
}

export interface GuidePlanDto {
  budget?: GuidePlanBudgetDto | null
  profiles: GuidePlanProfileDto[]
  sourceSituation: string
  topics: GuidePlanTopicDto[]
  extras: GuidePlanTopicDto[]
  ai?: AiUsageDto | null
}

export interface GuideChapterSelection {
  topicId: string
  title: string
  icon: string
  focus: string
}

// GET /api/steam/profile: the linked Steam account; every field may be missing (no account, no API key)
export interface SteamProfileDto {
  steamId: string | null
  personaName: string | null
  avatarUrl: string | null
  profileUrl: string | null
  libraryGameCount: number | null
}

// System check in the settings ("System" tab); status "off" = not used / not configured on this installation
export interface SystemCheckDto {
  key: string
  label: string
  status: 'ok' | 'warn' | 'error' | 'off'
  detail: string
  latencyMs: number | null
}

// How often the server looks for updates by itself (setting updates.check_interval); a check by hand always works
export type UpdateCheckInterval = 'startup' | '4h' | 'daily' | 'weekly' | 'monthly' | 'never'

// Settings > System > Updates (GET /api/updates/status, POST /api/updates/check)
export interface UpdateReleaseDto {
  // Technical tag (v2026.11.12 / v2026.11.12.13.557-beta) and readable form (v2026.11.12 (13.557 β))
  tag: string
  display: string
  isBeta: boolean
  // The release page with its notes in the public repository (github.com only), null when unknown
  notesUrl: string | null
  // The release notes text (Markdown: English block, "---", German block, optionally a code signing policy footer) from the GitHub
  // release body; null when the release has no body. The server always sends the field, the app never asks GitHub itself.
  notes: string | null
  publishedAt: string | null
  hasInstaller: boolean
}

// 'devBuild': a dev build or development run, never offered a beta or release and does not ask GitHub
export type UpdateState = 'disabled' | 'unchecked' | 'upToDate' | 'available' | 'error' | 'devBuild'

// What a container should do about its image tag (only for host "server"; the Windows program installs itself)
export type DockerAdvice = 'none' | 'useBeta' | 'betaStays' | 'switchToLatest'

export interface UpdateStatusDto {
  currentVersion: string
  currentTag: string
  currentChannel: 'release' | 'beta' | 'dev'
  betaUpdatesEnabled: boolean
  host: 'desktop' | 'server'
  state: UpdateState
  available: UpdateReleaseDto | null
  latestRelease: UpdateReleaseDto | null
  // The newest beta that is newer than the newest release (null otherwise)
  latestBeta: UpdateReleaseDto | null
  checkedAt: string | null
  // Complete, in the UI language; only for state "error"
  error: string | null
  // A manual check came within a minute of the previous one and was answered from memory
  throttled: boolean
  nextManualCheckAt: string | null
  // Windows program: "Installieren" downloads, verifies and runs the installer
  canInstall: boolean
  dockerAdvice: DockerAdvice
  // ghcr.io/sushiflux/platinvault - ":latest" = releases, ":beta" = betas and releases
  imageRepository: string
  checkInterval: UpdateCheckInterval
}

export interface InstallUpdateResponse {
  started: boolean
  tag: string
}

export interface SystemStatusDto {
  version: string
  environment: string
  checkedAt: string
  checks: SystemCheckDto[]
}

// Trophy cabinet: every game of the Steam library at 100 % (missing figures are null and hidden)
export interface ShowcaseGameDto {
  appId: number
  name: string
  coverUrl: string | null
  heroBannerUrl: string | null
  existingSlug: string | null
  trophiesTotal: number
  platinumAt: string | null
  firstUnlockAt: string | null
  daysToPlatinum: number | null
  playtimeHours: number | null
  rarestName: string | null
  rarestIconUrl: string | null
  rarestPercent: number | null
  averageRarityPercent: number | null
}

// Which completed games the cabinet has already presented (animated into their slot); initialized = the state exists on the server
export interface CabinetPresentedDto {
  initialized: boolean
  appIds: number[]
}

export interface ShowcaseDto {
  games: ShowcaseGameDto[]
  totalTrophies: number
  totalPlaytimeHours: number | null
}

// --- Twitch & IGDB (optional; every endpoint answers 204 without credentials) -------------------------------------------

// GET /api/games/{slug}/igdb - lists are empty and other fields null when IGDB does not know the value (the UI hides them).
// All image URLs are complete https URLs; ratings are 0-100.
export interface IgdbGameDto {
  igdbId: number
  name: string
  url: string | null
  summary: string | null
  storyline: string | null
  aggregatedRating: number | null
  aggregatedRatingCount: number | null
  totalRating: number | null
  totalRatingCount: number | null
  genres: string[]
  themes: string[]
  gameModes: string[]
  playerPerspectives: string[]
  franchises: string[]
  collections: string[]
  similarGames: IgdbSimilarGameDto[]
  coverUrl: string | null
  artworks: IgdbImageDto[]
  screenshots: IgdbImageDto[]
  languages: IgdbLanguageDto[]
  timeToBeat: IgdbTimeToBeatDto | null
  fetchedAt: string
}

export interface IgdbSimilarGameDto {
  igdbId: number
  name: string
  coverUrl: string | null
}

// thumbUrl = small preview (569x320), fullUrl = 1080p
export interface IgdbImageDto {
  thumbUrl: string
  fullUrl: string
}

export interface IgdbLanguageDto {
  name: string
  nativeName: string | null
  audio: boolean
  subtitles: boolean
  interface: boolean
}

// Average hours: hastily = main story only, normally = with some extras, completely = 100 %; count = submissions
export interface IgdbTimeToBeatDto {
  hastilyHours: number | null
  normallyHours: number | null
  completelyHours: number | null
  count: number | null
}

export interface TwitchStreamDto {
  userName: string
  userLogin: string
  title: string
  viewerCount: number
  language: string
  startedAt: string | null
  // Ready to use (440x248) or null; render the title as plain text
  thumbnailUrl: string | null
  channelUrl: string
}

// POST /api/twitch/live-counts: the live streams per game (the count badge of the Twitch buttons). atLeast = the count stopped at its
// limit ("300+"). pending = games that are still being looked up (ask again); a game that is in neither list gets no badge.
export interface TwitchLiveCountsDto {
  counts: { appId: number; count: number; atLeast: boolean }[]
  pending: number[]
}

// GET /api/games/{slug}/streams and GET /api/twitch/streams. categoryFound=false: Twitch has no category for the game
// (different from nobody being live, which is an empty list). streams are sorted by viewers, first 20.
export interface TwitchStreamsDto {
  categoryName: string | null
  categoryFound: boolean
  language: string | null
  streams: TwitchStreamDto[]
  fetchedAt: string
}

// POST /api/twitch/test: message is a ready-to-show German text (success or the specific problem)
export interface TwitchTestResponseDto {
  connected: boolean
  message: string
  tokenOk: boolean
  igdbOk: boolean
  streamsOk: boolean
}

// POST /api/ai/gemini/diagnose: what the stored Gemini key may really use (message = German explanation when not ok)
export interface GeminiProbeResultDto {
  ok: boolean
  status: number
  message: string
  // Google's own technical text when not ok; message is our short explanation
  providerMessage?: string | null
}

export interface GeminiDiagnosisDto {
  model: string
  plain: GeminiProbeResultDto
  search: GeminiProbeResultDto
}

// --- Statistics charts -----------------------------------------------------------------------------------------------

// GET /api/games/{slug}/series (IGDB, needs the player's Twitch keys; 204 = no series to show). source: IGDB collection, else franchise.
// owned = in the Steam library (always false without a Steam key); trophies are null when unknown.
export interface GameSeriesGameDto {
  igdbId: number
  name: string
  coverUrl: string | null
  releaseYear: number | null
  steamAppId: number | null
  owned: boolean
  inVault: boolean
  slug: string | null
  trophiesUnlocked: number | null
  trophiesTotal: number | null
  isCurrent: boolean
}

export interface GameSeriesDto {
  name: string
  source: 'collection' | 'franchise'
  games: GameSeriesGameDto[]
}

// GET /api/games/{slug}/history?days=90 (days 7..730): what PlatinVault recorded itself. All lists are empty while nothing is recorded.
export interface PlayerCountPointDto {
  at: string // ISO 8601, UTC; raw samples every 30 minutes, at most the last 30 days
  count: number
}

export interface PlayerCountDayDto {
  date: string // yyyy-MM-dd (UTC); older days, condensed
  min: number
  avg: number
  max: number
}

export interface PlaytimeDayDto {
  date: string // yyyy-MM-dd (UTC)
  minutes: number // minutes played that day (difference of consecutive daily snapshots); days without a snapshot are left out
}

export interface GameHistoryDto {
  trackingSince: string | null // first recording of this game
  playerCounts: PlayerCountPointDto[]
  playerCountsDaily: PlayerCountDayDto[]
  playtimeDaily: PlaytimeDayDto[]
}

// Start a game on the gaming PC (the bridge) or in the browser's own Steam handler
export interface LaunchResponseDto {
  // "bridge" = the bridge program on the gaming PC starts it; "browser" = this browser opens the steam:// link itself;
  // "local" = the Windows program already started it on this PC
  via: 'bridge' | 'browser' | 'local'
  url: string
  appId: number
  // Only with via = "bridge": the command to follow with GET /api/launch/{commandId}
  commandId: string | null
  status: 'pending' | 'delivered' | null
  expiresInSeconds: number | null
  bridgeConnected: boolean
}

export type LaunchCommandStatus = 'pending' | 'delivered' | 'done' | 'failed' | 'expired'

export interface LaunchCommandDto {
  commandId: string
  appId: number
  gameName: string
  status: LaunchCommandStatus
  finished: boolean
  // "steam-not-found" / "start-failed" when it failed
  reason: string | null
  // Localized text of the server for the final state
  message: string
}

// Whether a game can be started at all right now: the bridge polled recently (<= 12 s) - or the server runs on the PC with Steam
export interface LaunchStatusDto {
  bridgeConnected: boolean
  bridgeLastSeenAt: string | null
}

// Windows program only (GET /api/desktop/folders answers 404 anywhere else): where PlatinVault keeps its files on this PC
export type DesktopFolderKind = 'program' | 'data' | 'backups' | 'logs'

export interface DesktopFolderDto {
  kind: DesktopFolderKind
  path: string
}

export interface DesktopFoldersDto {
  folders: DesktopFolderDto[]
}
