import type { IgdbPlatformDto, IgdbSearchHitDto, PlatformChoiceDto, PlatformId, PlatformVariant } from '../../api/types'
import { variantsOf } from '../../utils/platforms'

// The rules of the "add a game" dialog in one place. The numbers mirror the server (the client is never stricter than it, the server checks
// everything again): title 1-240 characters (ManualGameEndpoints.MaxTitleLength), IGDB search text 1-120 characters
// (IgdbService.MaxSearchLength), cover at most 5 MB of PNG / JPEG / WebP / GIF (CoverStore.MaxBytes / ContentTypes, never SVG).

export const MAX_TITLE_LENGTH = 240
export const MAX_SEARCH_LENGTH = 120
export const MAX_COVER_BYTES = 5 * 1024 * 1024
export const COVER_TYPES: readonly string[] = ['image/png', 'image/jpeg', 'image/webp', 'image/gif']
// The file picker's filter (the server decides for real: type and the first bytes of the file)
export const COVER_ACCEPT = COVER_TYPES.join(',')

// ---- the title (without IGDB) ----------------------------------------------------------------------------------------

export type TitleProblem = { kind: 'required' } | { kind: 'tooLong'; length: number; max: number }

export function titleProblem(raw: string): TitleProblem | null {
  const length = raw.trim().length
  if (length === 0) return { kind: 'required' }
  if (length > MAX_TITLE_LENGTH) return { kind: 'tooLong', length, max: MAX_TITLE_LENGTH }
  return null
}

// ---- the IGDB search field: a name, an IGDB number or an igdb.com/games/<slug> link --------------------------------------------

export type SearchInput =
  | { kind: 'empty' }
  | { kind: 'text'; text: string }
  | { kind: 'id'; id: number }
  | { kind: 'link'; slug: string }
  // Looks like an address but not like the address of an IGDB game
  | { kind: 'badLink' }
  | { kind: 'tooLong'; length: number; max: number }

const IGDB_GAME_LINK = /igdb\.com\/games\/([a-z0-9][a-z0-9-]*)/i
const LOOKS_LIKE_ADDRESS = /^(?:[a-z][a-z0-9+.-]*:\/\/|www\.)|igdb\.com/i

export function parseSearchInput(raw: string): SearchInput {
  const text = raw.trim()
  if (text.length === 0) return { kind: 'empty' }
  if (text.length > MAX_SEARCH_LENGTH) return { kind: 'tooLong', length: text.length, max: MAX_SEARCH_LENGTH }
  if (/^\d+$/.test(text)) {
    const id = Number(text)
    // A number that is no safe integer cannot be an IGDB number (the server would refuse it as well)
    return Number.isSafeInteger(id) && id > 0 ? { kind: 'id', id } : { kind: 'text', text }
  }
  const link = IGDB_GAME_LINK.exec(text)
  if (link) return { kind: 'link', slug: link[1]!.toLowerCase() }
  if (LOOKS_LIKE_ADDRESS.test(text)) return { kind: 'badLink' }
  return { kind: 'text', text }
}

// ---- the cover file -------------------------------------------------------------------------------------------------------

export type CoverProblem = { kind: 'type'; type: string } | { kind: 'size'; bytes: number; max: number }

export function coverProblem(file: { type: string; size: number }): CoverProblem | null {
  const type = file.type.toLowerCase() === 'image/jpg' ? 'image/jpeg' : file.type.toLowerCase()
  if (!COVER_TYPES.includes(type)) return { kind: 'type', type: file.type }
  if (file.size > MAX_COVER_BYTES) return { kind: 'size', bytes: file.size, max: MAX_COVER_BYTES }
  return null
}

// ---- the platform IGDB lists for a game -------------------------------------------------------------------------------------------

// What IGDB tells about the platforms of a game: the choices of ours it maps to (PS5 / PS4 -> PlayStation + variant, Xbox family -> Xbox,
// Switch -> Nintendo), and whether it lists "PC" - IGDB names no shops, so PC marks no shop (the server maps it to nothing).
export interface IgdbMarks {
  // The distinct platform + variant choices IGDB lists (in IGDB's order)
  choices: PlatformChoiceDto[]
  // IGDB lists PC (Microsoft Windows)
  listsPc: boolean
}

const PC_NAME = /^pc\b|windows/i

export function isPcPlatform(platform: Pick<IgdbPlatformDto, 'name' | 'abbreviation' | 'maps'>): boolean {
  return platform.maps === null && (PC_NAME.test(platform.name) || (platform.abbreviation ?? '').toUpperCase() === 'PC')
}

export function igdbMarks(platforms: readonly IgdbPlatformDto[] | null | undefined): IgdbMarks {
  const choices: PlatformChoiceDto[] = []
  let listsPc = false
  for (const platform of platforms ?? []) {
    if (platform.maps) {
      const { platform: id, variant } = platform.maps
      if (!choices.some((c) => c.platform === id && c.variant === variant)) choices.push({ platform: id, variant })
    } else if (isPcPlatform(platform)) {
      listsPc = true
    }
  }
  return { choices, listsPc }
}

export function isMarked(marks: IgdbMarks, platform: PlatformId, variant?: PlatformVariant): boolean {
  return marks.choices.some((c) => c.platform === platform && (variant === undefined || c.variant === variant))
}

// The platform (and variant) to start with once a game is picked: only when IGDB's list leads to exactly one of our platforms; the variant only
// when exactly one is listed (a game on PS5 and PS4 leaves the choice to the player)
export function startingChoice(marks: IgdbMarks): { platform: PlatformId | ''; variant: PlatformVariant | '' } {
  const platforms = [...new Set(marks.choices.map((c) => c.platform))]
  if (platforms.length !== 1) return { platform: '', variant: '' }
  const platform = platforms[0]!
  const variants = marks.choices.filter((c) => c.platform === platform && c.variant).map((c) => c.variant!)
  return { platform, variant: variantsOf(platform).length > 0 && variants.length === 1 ? variants[0]! : '' }
}

// The hit's platforms as text for the result list ("PlayStation 5, PlayStation VR")
export function platformNames(hit: Pick<IgdbSearchHitDto, 'platforms'>): string[] {
  return hit.platforms.map((p) => p.name)
}
