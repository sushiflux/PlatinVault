import type {
  LaunchCommandDto,
  LaunchResponseDto,
  LaunchStatusDto,
  GeminiDiagnosisDto,
  AiExpandTabRequest,
  AiProviderModelsResponseDto,
  BrandingInfo,
  BridgeTokenCreatedDto,
  BridgeTokenStatusDto,
  GameDetailDto,
  GameStatsDto,
  GuideGroupDto,
  GuideTopicDto,
  GuideChapterSelection,
  AiResultDto,
  AiStatusDto,
  GuidePlanDto,
  LibrarySummaryDto,
  AiCompleteGuideResponse,
  AiRetryChaptersResponse,
  ManualBlueprintCheckDto,
  ManualChapterCheckDto,
  ManualChapterSeed,
  ManualPromptDto,
  ManualSavedDto,
  GameSummaryDto,
  LiveGameAchievementsDto,
  LiveGameInfoDto,
  LiveEventsResponseDto,
  LiveStatusDto,
  SettingsResponseDto,
  SteamLibraryResponseDto,
  SteamProfileDto,
  ShowcaseDto,
  CabinetPresentedDto,
  SteamSearchItemDto,
  SystemStatusDto,
  UpdateSettingsRequest,
  UpdateStatusDto,
  DesktopFolderKind,
  DesktopFoldersDto,
  InstallUpdateResponse,
  IgdbGameDto,
  TwitchLiveCountsDto,
  TwitchStreamsDto,
  TwitchTestResponseDto,
  GameSeriesDto,
  GameHistoryDto,
  GuideTranslationDto,
  AiJobDto,
} from './types'
import i18n, { currentLanguage } from '../i18n'
import { runAiJob, type AiJobProgressHandler } from './aiJobs'
import { isNetworkFailure, networkFailureMessage } from '../utils/apiErrorText'

const MUTATION_HEADERS: HeadersInit = {
  'Content-Type': 'application/json',
  'X-Requested-With': 'PlatinVault',
}

// Every API call tells the server which language the UI speaks right now (Accept-Language). Server texts - error messages, the
// system check, guide topics - then come in that language even before a language switch has been saved. This function shadows
// the global fetch inside this module only, so each call below gets the header without being touched.
// A server that cannot be reached makes the browser throw a bare TypeError ("Failed to fetch" / "NetworkError when attempting to fetch
// resource" / "Load failed"). That text says nothing to the user, so it becomes an ApiError (status 0) with a localized sentence that
// names the cause and what to check. An aborted request (AbortError) is not a failure and passes through unchanged.
async function fetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const headers = new Headers(init?.headers)
  headers.set('Accept-Language', currentLanguage())
  try {
    return await globalThis.fetch(input, { ...init, headers })
  } catch (error) {
    if (isNetworkFailure(error)) throw new ApiError(networkFailureMessage(), 0)
    throw error
  }
}

/**
 * An error answer of the API. `message` is the server's explanation in the UI language (what happened, what to do);
 * `providerMessage` is an AI provider's own technical text (English, can be long) for a collapsible "technical details" part.
 */
export class ApiError extends Error {
  readonly status: number
  readonly providerMessage?: string
  // AI failures: which provider failed ("gemini" / "claude" / "openai") and why ("quota" / "invalidKey" / "unavailable" / "other")
  readonly aiProvider?: string
  readonly aiFailure?: string

  constructor(message: string, status: number, providerMessage?: string, aiProvider?: string, aiFailure?: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.providerMessage = providerMessage
    this.aiProvider = aiProvider
    this.aiFailure = aiFailure
  }
}

// Readable fallback when the server sent no problem details (e.g. the proxy answers while the backend is down)
function statusMessage(status: number): string {
  if (status === 502 || status === 503 || status === 504) return i18n.t('errors:http.unreachable', { status })
  if (status === 404) return i18n.t('errors:http.notFound')
  if (status === 413) return i18n.t('errors:http.tooLarge')
  if (status === 429) return i18n.t('errors:http.tooManyRequests')
  return i18n.t('errors:http.unexpected', { status })
}

async function handleResponse<T>(res: Response): Promise<T> {
  if (!res.ok) {
    let message = statusMessage(res.status)
    let providerMessage: string | undefined
    let aiProvider: string | undefined
    let aiFailure: string | undefined
    try {
      const problem = (await res.json()) as { detail?: string; title?: string; providerMessage?: string; aiProvider?: string; aiFailure?: string }
      message = problem.detail || problem.title || message
      providerMessage = problem.providerMessage || undefined
      aiProvider = problem.aiProvider || undefined
      aiFailure = problem.aiFailure || undefined
    } catch {
      // Fallback to status text
    }
    throw new ApiError(message, res.status, providerMessage, aiProvider, aiFailure)
  }
  if (res.status === 204) {
    return undefined as T
  }
  try {
    return (await res.json()) as T
  } catch (error) {
    // The connection broke while the body arrived: the same "server not reachable" sentence as a failed request
    if (isNetworkFailure(error)) throw new ApiError(networkFailureMessage(), 0)
    // A success answer that is not JSON (an HTML page of a proxy or a login portal): say so, not the parser's "Unexpected token <"
    if (error instanceof SyntaxError) throw new ApiError(statusMessage(res.status), res.status)
    throw error
  }
}

export async function fetchBranding(): Promise<BrandingInfo> {
  const res = await fetch('/api/branding')
  return handleResponse<BrandingInfo>(res)
}

export async function fetchLiveStatus(): Promise<LiveStatusDto> {
  const res = await fetch('/api/live/status')
  return handleResponse<LiveStatusDto>(res)
}

// The game that is running right now (name, artwork), vault game or not; null when nothing runs (204)
export async function fetchRunningGame(): Promise<LiveGameInfoDto | null> {
  const res = await fetch('/api/live/running-game')
  if (res.status === 204) return null
  return handleResponse<LiveGameInfoDto>(res)
}

export async function fetchLiveAchievements(appId?: number): Promise<LiveGameAchievementsDto> {
  const qs = appId && appId > 0 ? `?appId=${encodeURIComponent(String(appId))}` : ''
  const res = await fetch(`/api/live/achievements${qs}`)
  return handleResponse<LiveGameAchievementsDto>(res)
}

// Unlocks of the last minutes, for catching up after the live connection was down
export async function fetchLiveEvents(): Promise<LiveEventsResponseDto> {
  const res = await fetch('/api/live/events')
  return handleResponse<LiveEventsResponseDto>(res)
}

export async function saveLiveAchievementNote(appId: number, apiName: string, note: string): Promise<{ success: boolean }> {
  const res = await fetch('/api/live/note', {
    method: 'POST',
    headers: MUTATION_HEADERS,
    body: JSON.stringify({ appId, apiName, note }),
  })
  return handleResponse<{ success: boolean }>(res)
}

export async function triggerLiveTestPopup(type: 'bronze' | 'silver' | 'gold' | 'platinum', gameName?: string, total?: number): Promise<void> {
  const params = new URLSearchParams()
  if (gameName) params.set('gameName', gameName)
  if (total) params.set('total', String(total))
  const qs = params.toString() ? `?${params.toString()}` : ''
  const res = await fetch(`/api/live/test/${encodeURIComponent(type)}${qs}`, {
    method: 'POST',
    headers: MUTATION_HEADERS,
  })
  await handleResponse<unknown>(res)
}

export async function fetchGames(): Promise<GameSummaryDto[]> {
  const res = await fetch('/api/games')
  return handleResponse<GameSummaryDto[]>(res)
}

export async function fetchGameDetail(slug: string): Promise<GameDetailDto> {
  // The server answers in the language of the header (the UI language): the translated guide when it is complete
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}`)
  return handleResponse<GameDetailDto>(res)
}

// Starts the translation of a guide into the UI language (or confirms that it is done / running). A failed attempt is not repeated
// by the server on its own for a while; force = "try again".
export async function startGuideTranslation(slug: string, force = false): Promise<GuideTranslationDto> {
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/translate${force ? '?force=true' : ''}`, {
    method: 'POST',
    headers: MUTATION_HEADERS,
  })
  return handleResponse<GuideTranslationDto>(res)
}

export async function fetchGuideTranslation(slug: string): Promise<GuideTranslationDto> {
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/translation`)
  return handleResponse<GuideTranslationDto>(res)
}

// The AI actions below take minutes, so the server runs them as background jobs: each function starts the job, follows it (every 2 s) and
// resolves with the result the request used to give - or throws with the reason. `onProgress` hears about every step (the progress display).
async function startAiJob(path: string, body: unknown): Promise<AiJobDto> {
  const res = await fetch(path, { method: 'POST', headers: MUTATION_HEADERS, body: JSON.stringify(body) })
  return handleResponse<AiJobDto>(res)
}

export async function fetchAiJob(id: string): Promise<AiJobDto> {
  const res = await fetch(`/api/ai/jobs/${encodeURIComponent(id)}`)
  return handleResponse<AiJobDto>(res)
}

// What runs (or finished a moment ago) for one game: a reloaded game page picks the progress up again
export async function fetchAiJobsForGame(slug: string): Promise<AiJobDto[]> {
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/ai-jobs`)
  return handleResponse<AiJobDto[]>(res)
}

export function expandTabWithAi(slug: string, payload: AiExpandTabRequest, onProgress?: AiJobProgressHandler): Promise<AiResultDto<GameDetailDto>> {
  return runAiJob(() => startAiJob(`/api/games/${encodeURIComponent(slug)}/ai-expand-tab`, payload), fetchAiJob, onProgress)
}

// The stored AI selection plus every provider with its key and current state (quota used up, key rejected, unavailable)
export async function fetchAiStatus(): Promise<AiStatusDto> {
  const res = await fetch('/api/ai/status')
  return handleResponse<AiStatusDto>(res)
}

// "Reset" in the settings: forget the remembered failure of one provider, the next request tries it again
export async function resetAiProvider(provider: string): Promise<AiStatusDto> {
  const res = await fetch(`/api/ai/providers/${encodeURIComponent(provider)}/reset`, {
    method: 'POST',
    headers: MUTATION_HEADERS,
  })
  return handleResponse<AiStatusDto>(res)
}

export async function deleteGame(id: string): Promise<void> {
  const res = await fetch(`/api/games/${encodeURIComponent(id)}`, {
    method: 'DELETE',
    headers: MUTATION_HEADERS,
  })
  return handleResponse<void>(res)
}

export async function toggleProgressItem(slug: string, itemKey: string, checked: boolean): Promise<{ checkedItemKeys: string[] }> {
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/progress`, {
    method: 'POST',
    headers: MUTATION_HEADERS,
    body: JSON.stringify({ itemKey, checked }),
  })
  return handleResponse<{ checkedItemKeys: string[] }>(res)
}

export async function resetGameProgress(slug: string): Promise<{ checkedItemKeys: string[] }> {
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/progress/reset`, {
    method: 'POST',
    headers: MUTATION_HEADERS,
  })
  return handleResponse<{ checkedItemKeys: string[] }>(res)
}

export async function searchSteamGames(query: string): Promise<SteamSearchItemDto[]> {
  if (!query.trim()) return []
  const res = await fetch(`/api/steam/search?q=${encodeURIComponent(query.trim())}`)
  return handleResponse<SteamSearchItemDto[]>(res)
}

export async function fetchSteamLibrary(steamIdOverride?: string): Promise<SteamLibraryResponseDto> {
  const qs = steamIdOverride?.trim() ? `?steamId=${encodeURIComponent(steamIdOverride.trim())}` : ''
  const res = await fetch(`/api/steam/library${qs}`)
  return handleResponse<SteamLibraryResponseDto>(res)
}

export async function fetchSettings(): Promise<SettingsResponseDto> {
  const res = await fetch('/api/settings')
  return handleResponse<SettingsResponseDto>(res)
}

export async function updateSettings(payload: UpdateSettingsRequest): Promise<SettingsResponseDto> {
  const res = await fetch('/api/settings', {
    method: 'PUT',
    headers: MUTATION_HEADERS,
    body: JSON.stringify(payload),
  })
  return handleResponse<SettingsResponseDto>(res)
}

export async function probeAiModels(
  provider: 'gemini' | 'claude' | 'openai',
  apiKey?: string
): Promise<AiProviderModelsResponseDto> {
  const res = await fetch('/api/ai/models', {
    method: 'POST',
    headers: MUTATION_HEADERS,
    body: JSON.stringify({ provider, apiKey: apiKey?.trim() || undefined }),
  })
  return handleResponse<AiProviderModelsResponseDto>(res)
}

// Real test of the stored Gemini key: one tiny request without and one with Google Search (two minimal requests)
export async function diagnoseGemini(model?: string): Promise<GeminiDiagnosisDto> {
  const res = await fetch('/api/ai/gemini/diagnose', {
    method: 'POST',
    headers: MUTATION_HEADERS,
    body: JSON.stringify({ model: model?.trim() || undefined }),
  })
  return handleResponse<GeminiDiagnosisDto>(res)
}

export async function fetchGameStats(slug: string): Promise<GameStatsDto> {
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/stats`)
  return handleResponse<GameStatsDto>(res)
}

// "Mit Steam anmelden" is a full-page navigation to Steam and back; the origin tells the backend where to return.
export function steamLoginUrl(): string {
  return `/api/steam/openid/start?origin=${encodeURIComponent(window.location.origin)}`
}

// Realtime bridge (Unraid): the token a bridge program on the gaming PC uses to push Steam unlocks to this server.
export async function fetchBridgeTokenStatus(): Promise<BridgeTokenStatusDto> {
  const res = await fetch('/api/bridge/token')
  return handleResponse<BridgeTokenStatusDto>(res)
}

export async function createBridgeToken(): Promise<BridgeTokenCreatedDto> {
  const res = await fetch('/api/bridge/token', {
    method: 'POST',
    headers: MUTATION_HEADERS,
  })
  return handleResponse<BridgeTokenCreatedDto>(res)
}

export async function deleteBridgeToken(): Promise<void> {
  const res = await fetch('/api/bridge/token', {
    method: 'DELETE',
    headers: MUTATION_HEADERS,
  })
  return handleResponse<void>(res)
}

// Downloads a ready-to-run bridge ZIP (exe + bridge.json with this server address and a NEW token). The old token
// stops working; the response is a file, so it is saved through a temporary object URL.
export async function downloadBridgePackage(): Promise<void> {
  const res = await fetch('/api/bridge/package', {
    method: 'POST',
    headers: MUTATION_HEADERS,
    body: JSON.stringify({ origin: window.location.origin }),
  })
  if (!res.ok) {
    await handleResponse<never>(res)
  }
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = 'PlatinVault-Bridge.zip'
  document.body.appendChild(link)
  link.click()
  link.remove()
  window.setTimeout(() => URL.revokeObjectURL(url), 10_000)
}

// Export is a plain GET with a file response, so a normal link is enough.
export function gameExportUrl(slug: string): string {
  return `/api/games/${encodeURIComponent(slug)}/export`
}

export async function importGuide(document: unknown): Promise<GameSummaryDto> {
  const res = await fetch('/api/games/import', {
    method: 'POST',
    headers: MUTATION_HEADERS,
    body: JSON.stringify(document),
  })
  return handleResponse<GameSummaryDto>(res)
}

// Researches (or re-researches) the optional Story & Lore tab of a game; returns the updated game.
export function researchLore(slug: string, providerOverride?: string, onProgress?: AiJobProgressHandler): Promise<AiResultDto<GameDetailDto>> {
  return runAiJob(() => startAiJob(`/api/games/${encodeURIComponent(slug)}/ai-lore`, { providerOverride }), fetchAiJob, onProgress)
}

// The chapter groups of the side navigation in display order, labelled in the given language (default: the app language of the request)
export async function fetchGuideGroups(language?: string): Promise<GuideGroupDto[]> {
  const res = await fetch(language ? `/api/guide/groups?lang=${encodeURIComponent(language)}` : '/api/guide/groups')
  return handleResponse<GuideGroupDto[]>(res)
}

export async function fetchGuideTopics(language?: string): Promise<GuideTopicDto[]> {
  // The quick picks end up as chapter titles of the guide: in the guide's language, not necessarily the one of the UI
  const res = await fetch(language ? `/api/guide/topics?lang=${encodeURIComponent(language)}` : '/api/guide/topics')
  return handleResponse<GuideTopicDto[]>(res)
}

// "Guide vervollständigen": the server checks every standard topic and researches the missing, applicable ones
export function completeGuide(
  slug: string,
  chapters?: GuideChapterSelection[],
  providerOverride?: string,
  onProgress?: AiJobProgressHandler
): Promise<AiCompleteGuideResponse> {
  return runAiJob(() => startAiJob(`/api/games/${encodeURIComponent(slug)}/ai-complete`, { chapters, providerOverride }), fetchAiJob, onProgress)
}

// "Kapitel erneut recherchieren": the failed chapters (tabIds; null = every failed chapter of the guide) are researched again exactly as planned
export function retryChapters(
  slug: string,
  tabIds: string[] | null,
  providerOverride?: string,
  onProgress?: AiJobProgressHandler
): Promise<AiRetryChaptersResponse> {
  return runAiJob(() => startAiJob(`/api/games/${encodeURIComponent(slug)}/ai-retry-chapters`, { tabIds, providerOverride }), fetchAiJob, onProgress)
}

// ---- The guide via the player's own AI subscription: everything local, nothing goes to an AI provider ----

const manualPath = (slug: string, tail: string) => `/api/games/${encodeURIComponent(slug)}/manual/${tail}`

async function postManual<T>(slug: string, tail: string, body: unknown): Promise<T> {
  const res = await fetch(manualPath(slug, tail), { method: 'POST', headers: MUTATION_HEADERS, body: JSON.stringify(body) })
  return handleResponse<T>(res)
}

export const manualBlueprintPrompt = (slug: string) => postManual<ManualPromptDto>(slug, 'blueprint-prompt', {})

export const checkManualBlueprint = (slug: string, text: string) => postManual<ManualBlueprintCheckDto>(slug, 'blueprint-answer', { text })

export const manualChapterPrompt = (slug: string, seed: ManualChapterSeed) =>
  postManual<ManualPromptDto>(slug, 'chapter-prompt', { title: seed.title, icon: seed.icon, focus: seed.focus, topicId: seed.topicId, sourceSituation: seed.sourceSituation })

export const checkManualChapter = (slug: string, text: string, seed: ManualChapterSeed) =>
  postManual<ManualChapterCheckDto>(slug, 'chapter-check', { text, title: seed.title, icon: seed.icon, focus: seed.focus, topicId: seed.topicId, tabId: seed.tabId })

export const saveManualChapter = (slug: string, text: string, seed: ManualChapterSeed) =>
  postManual<ManualSavedDto>(slug, 'chapter-answer', { text, title: seed.title, icon: seed.icon, focus: seed.focus, topicId: seed.topicId, tabId: seed.tabId })

// "Kapitel entfernen": removes any chapter of the guide (not the Steam achievement list, not Story & Lore) with its translation and its
// own tick marks; answers with the guide as it is now
export async function removeChapter(slug: string, tabId: string): Promise<GameDetailDto> {
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/chapters/${encodeURIComponent(tabId)}`, { method: 'DELETE', headers: MUTATION_HEADERS })
  return handleResponse<GameDetailDto>(res)
}

// Library-wide trophy figures; null without a Steam key (the dashboard then shows its own games only)
export async function fetchLibrarySummary(): Promise<LibrarySummaryDto | null> {
  const res = await fetch('/api/steam/library/summary')
  return (await handleResponse<LibrarySummaryDto | undefined>(res)) ?? null
}

// Star in the Steam library: on = the game comes onto the dashboard (its tile is created without AI if needed),
// off = it leaves the dashboard (guide and ticks stay). Returns null when an unknown game was unstarred.
export async function setLibraryFavorite(appId: number, name: string, isFavorite: boolean): Promise<GameSummaryDto | null> {
  const res = await fetch(`/api/steam/library/${encodeURIComponent(String(appId))}/favorite`, {
    method: 'PUT',
    headers: MUTATION_HEADERS,
    body: JSON.stringify({ isFavorite, name }),
  })
  return (await handleResponse<GameSummaryDto | undefined>(res)) ?? null
}

// Star on the dashboard: marks or unmarks a game as favourite
export async function setFavorite(slug: string, isFavorite: boolean): Promise<GameSummaryDto> {
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/favorite`, {
    method: 'PUT',
    headers: MUTATION_HEADERS,
    body: JSON.stringify({ isFavorite }),
  })
  return handleResponse<GameSummaryDto>(res)
}

// Links a game without AppID to its Steam app; the server fetches the official cover/banner
export async function linkSteamApp(slug: string, steamAppId: number): Promise<GameDetailDto> {
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/steam-app`, {
    method: 'PUT',
    headers: MUTATION_HEADERS,
    body: JSON.stringify({ steamAppId }),
  })
  return handleResponse<GameDetailDto>(res)
}

// Guide builder: the AI analyses the game and returns every building block with its recommendation
export function planGuide(slug: string, providerOverride?: string, onProgress?: AiJobProgressHandler): Promise<GuidePlanDto> {
  return runAiJob(() => startAiJob(`/api/games/${encodeURIComponent(slug)}/ai-plan`, { providerOverride }), fetchAiJob, onProgress)
}

// Linked Steam account for the dashboard header (name, avatar, profile link, library size)
export async function fetchSteamProfile(): Promise<SteamProfileDto> {
  const res = await fetch('/api/steam/profile')
  return handleResponse<SteamProfileDto>(res)
}

// Health check of backend, database, data folder and the configured sources (settings, "System" tab)
// Update check (Settings > System > Updates): the status answers from the server's cache (it asks GitHub at most every few hours),
// the manual check asks GitHub now (at most once a minute), install only works in the Windows program
export async function fetchUpdateStatus(): Promise<UpdateStatusDto> {
  const res = await fetch('/api/updates/status')
  return handleResponse<UpdateStatusDto>(res)
}

export async function checkForUpdates(): Promise<UpdateStatusDto> {
  const res = await fetch('/api/updates/check', { method: 'POST', headers: MUTATION_HEADERS })
  return handleResponse<UpdateStatusDto>(res)
}

export async function installUpdate(tag: string): Promise<InstallUpdateResponse> {
  const res = await fetch('/api/updates/install', { method: 'POST', headers: MUTATION_HEADERS, body: JSON.stringify({ tag }) })
  return handleResponse<InstallUpdateResponse>(res)
}

// Windows program only: the folders of the installation, and "open in Explorer" (404 on a server, where there is no such program)
export async function fetchDesktopFolders(): Promise<DesktopFoldersDto> {
  const res = await fetch('/api/desktop/folders')
  return handleResponse<DesktopFoldersDto>(res)
}

export async function openDesktopFolder(kind: DesktopFolderKind): Promise<void> {
  const res = await fetch(`/api/desktop/folders/${encodeURIComponent(kind)}/open`, { method: 'POST', headers: MUTATION_HEADERS })
  return handleResponse<void>(res)
}

export async function fetchSystemStatus(): Promise<SystemStatusDto> {
  const res = await fetch('/api/system/status')
  return handleResponse<SystemStatusDto>(res)
}

// Trophy cabinet: all games at 100 % with their key figures (server caches it for ten minutes)
export async function fetchShowcase(): Promise<ShowcaseDto> {
  const res = await fetch('/api/showcase')
  return handleResponse<ShowcaseDto>(res)
}

// The games the cabinet has already presented; the PUT only ever adds (and, without a stored state, creates the baseline)
export async function fetchCabinetPresented(): Promise<CabinetPresentedDto> {
  const res = await fetch('/api/showcase/presented')
  return handleResponse<CabinetPresentedDto>(res)
}

export async function addCabinetPresented(appIds: number[]): Promise<CabinetPresentedDto> {
  const res = await fetch('/api/showcase/presented', {
    method: 'PUT',
    headers: MUTATION_HEADERS,
    body: JSON.stringify({ appIds }),
  })
  return handleResponse<CabinetPresentedDto>(res)
}

// --- Twitch & IGDB (optional): every call resolves to null (HTTP 204) while no credentials are stored or the feature is off ---

// IGDB facts of a vault game (ratings, genres, similar games, languages, time to beat, ...); null = nothing to show
export async function fetchGameIgdb(slug: string): Promise<IgdbGameDto | null> {
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/igdb`)
  return (await handleResponse<IgdbGameDto | undefined>(res)) ?? null
}

// Live Twitch streams of a vault game (first 20, most viewers first); lang is a two-letter code like 'de' (optional)
export async function fetchGameStreams(slug: string, lang?: string): Promise<TwitchStreamsDto | null> {
  const qs = lang ? `?lang=${encodeURIComponent(lang)}` : ''
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/streams${qs}`)
  return (await handleResponse<TwitchStreamsDto | undefined>(res)) ?? null
}

// Live Twitch streams of any library game (dashboard 'Alle Spiele'), found by Steam AppID and/or title
export async function fetchTwitchStreams(game: { appId?: number | null; name?: string; lang?: string }): Promise<TwitchStreamsDto | null> {
  const params = new URLSearchParams()
  if (game.appId) params.set('appId', String(game.appId))
  if (game.name) params.set('name', game.name)
  if (game.lang) params.set('lang', game.lang)
  const res = await fetch(`/api/twitch/streams?${params.toString()}`)
  return (await handleResponse<TwitchStreamsDto | undefined>(res)) ?? null
}

// How many streams are live per game right now (up to 120 games per call); null = no Twitch credentials or the feature is off
export async function fetchTwitchLiveCounts(games: { appId: number; name: string }[], lang?: string): Promise<TwitchLiveCountsDto | null> {
  const res = await fetch('/api/twitch/live-counts', {
    method: 'POST',
    headers: MUTATION_HEADERS,
    body: JSON.stringify({ games, lang }),
  })
  return (await handleResponse<TwitchLiveCountsDto | undefined>(res)) ?? null
}

// Checks Client-ID/Secret (blank values test the stored ones); resolves with a German message, also when the check fails
export async function testTwitchConnection(clientId?: string, clientSecret?: string): Promise<TwitchTestResponseDto> {
  const res = await fetch('/api/twitch/test', {
    method: 'POST',
    headers: MUTATION_HEADERS,
    body: JSON.stringify({ clientId: clientId?.trim() || undefined, clientSecret: clientSecret?.trim() || undefined }),
  })
  return handleResponse<TwitchTestResponseDto>(res)
}

// Dashboard drag & drop: saves the new order. The listed games (in this order) come first; games not in the list
// (e.g. unstarred ones) keep their relative order behind them. Resolves with nothing (HTTP 204); rejects with the
// server's German message when a slug is unknown or listed twice.
export async function reorderGames(slugs: string[]): Promise<void> {
  const res = await fetch('/api/games/order', {
    method: 'PUT',
    headers: MUTATION_HEADERS,
    body: JSON.stringify({ slugs }),
  })
  return handleResponse<void>(res)
}

// The end-user handbook as PDF (German or English), served inline by the server - use it as a link target (new tab)
export function handbookUrl(lang: 'de' | 'en'): string {
  return `/api/docs/handbook?lang=${lang}`
}

// The licence documents that ship with the product: the licence (GNU AGPL v3, plain text) and the third-party notices (Markdown)
export type LicenseDocument = 'license' | 'notices'

export function licenseUrl(file: LicenseDocument): string {
  return `/api/docs/licenses?file=${file}`
}

export async function fetchLicenseText(file: LicenseDocument): Promise<string> {
  const res = await fetch(licenseUrl(file))
  // A failed answer throws with the server's own message (which document is missing and how it is produced)
  if (!res.ok) return handleResponse<string>(res)
  return res.text()
}

// The series (IGDB collection, else franchise) a game belongs to; null = nothing to show (no Twitch keys, no AppID, no series)
export async function fetchGameSeries(slug: string): Promise<GameSeriesDto | null> {
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/series`)
  return (await handleResponse<GameSeriesDto | undefined>(res)) ?? null
}

// Player counts and daily playtime that PlatinVault recorded over time (days 7..730, server default 90); empty lists until recorded
export async function fetchGameHistory(slug: string, days = 90): Promise<GameHistoryDto | null> {
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/history?days=${encodeURIComponent(String(days))}`)
  return (await handleResponse<GameHistoryDto | undefined>(res)) ?? null
}

// "Play": starts a game of the dashboard (slug) or of the whole Steam library (AppID). The answer says who starts it: the bridge on the
// gaming PC (follow the command) or this browser (open the steam:// link).
export async function launchGame(slug: string): Promise<LaunchResponseDto> {
  const res = await fetch(`/api/games/${encodeURIComponent(slug)}/launch`, { method: 'POST', headers: MUTATION_HEADERS })
  return handleResponse<LaunchResponseDto>(res)
}

export async function launchLibraryGame(appId: number): Promise<LaunchResponseDto> {
  const res = await fetch(`/api/steam/library/${encodeURIComponent(String(appId))}/launch`, { method: 'POST', headers: MUTATION_HEADERS })
  return handleResponse<LaunchResponseDto>(res)
}

export async function fetchLaunchCommand(commandId: string): Promise<LaunchCommandDto> {
  const res = await fetch(`/api/launch/${encodeURIComponent(commandId)}`)
  return handleResponse<LaunchCommandDto>(res)
}

export async function fetchLaunchStatus(): Promise<LaunchStatusDto> {
  const res = await fetch('/api/launch/status')
  return handleResponse<LaunchStatusDto>(res)
}
