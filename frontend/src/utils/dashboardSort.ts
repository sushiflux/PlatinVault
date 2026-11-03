import type { GameSummaryDto, SteamOwnedGameDto } from '../api/types'

/**
 * The dashboard's sort orders (user wish 2026-11-02). The ids are the ones the server stores and validates
 * (Companion.Api/Settings/DashboardSort.cs); the sorting itself runs here because every value is already part of the lists the
 * page loads. A game without a value for the chosen order goes to the END, in both directions (never mixed in as 0).
 */
export type SortOrder =
  | 'custom'
  | 'completion'
  | 'alphabetical'
  | 'playtime'
  | 'lastPlayed'
  | 'release'
  | 'size'
  | 'metacritic'
  | 'reviews'

export type SortDirection = 'asc' | 'desc'

export interface SortChoice {
  order: SortOrder
  direction: SortDirection
}

export type DashboardSortView = 'favorites' | 'all'

/** Orders both views have, in the order of the menu. */
export const SHARED_ORDERS: readonly SortOrder[] = [
  'completion',
  'alphabetical',
  'playtime',
  'lastPlayed',
  'release',
  'size',
  'metacritic',
  'reviews',
]

/** "custom" = the hand-made order of the favourites (drag & drop); only the favourites view has it, and it comes first. */
export const ORDERS_BY_VIEW: Record<DashboardSortView, readonly SortOrder[]> = {
  favorites: ['custom', ...SHARED_ORDERS],
  all: SHARED_ORDERS,
}

/** The direction an order starts with: the interesting end first (best, most, newest), A-Z for names. */
export const DEFAULT_DIRECTION: Record<SortOrder, SortDirection> = {
  custom: 'asc',
  completion: 'desc',
  alphabetical: 'asc',
  playtime: 'desc',
  lastPlayed: 'desc',
  release: 'desc',
  size: 'desc',
  metacritic: 'desc',
  reviews: 'desc',
}

export const DEFAULT_CHOICE: Record<DashboardSortView, SortChoice> = {
  favorites: { order: 'custom', direction: 'asc' },
  all: { order: 'lastPlayed', direction: 'desc' },
}

/** The stored form: "<order>:<asc|desc>". */
export function serializeSortChoice(choice: SortChoice): string {
  return `${choice.order}:${choice.direction}`
}

/** Reads a stored value; anything unknown (or an order the view does not have) gives the view's default. */
export function parseSortChoice(value: string | null | undefined, view: DashboardSortView): SortChoice {
  const [order, direction, ...rest] = (value ?? '').trim().split(':')
  const known = (ORDERS_BY_VIEW[view] as readonly string[]).includes(order ?? '')
  if (!known || rest.length > 0 || (direction !== 'asc' && direction !== 'desc')) return DEFAULT_CHOICE[view]
  return { order: order as SortOrder, direction }
}

/** The choice for an order picked in the menu: its default direction. Picking the active order again keeps the direction. */
export function chooseOrder(current: SortChoice, order: SortOrder): SortChoice {
  return order === current.order ? current : { order, direction: DEFAULT_DIRECTION[order] }
}

export function flipDirection(choice: SortChoice): SortChoice {
  return { ...choice, direction: choice.direction === 'asc' ? 'desc' : 'asc' }
}

/** The size values of a game as the server sends them (real size on disk of an installed game, estimated install size of any game). */
export interface SizeSource {
  sizeOnDiskBytes?: number | null
  installSizeBytes?: number | null
}

/**
 * Which size a game has: the real size on disk of an installed game ("disk"), else the install size estimated from Steam's depot
 * data ("estimate"), else none. The cards show this size and the order "Größe" sorts by it, so list and cards agree.
 */
export function sizeOf(game: SizeSource): { kind: 'disk' | 'estimate' | 'none'; bytes: number } {
  if (game.sizeOnDiskBytes != null && game.sizeOnDiskBytes > 0) return { kind: 'disk', bytes: game.sizeOnDiskBytes }
  if (game.installSizeBytes != null && game.installSizeBytes > 0) return { kind: 'estimate', bytes: game.installSizeBytes }
  return { kind: 'none', bytes: 0 }
}

/** What an item offers to the sort orders; null = unknown. Timestamps are milliseconds. */
export interface SortValues {
  title: string
  /** Share of the Steam achievements unlocked, 0-100; null without achievements or before they were checked. */
  completion: number | null
  playtimeMinutes: number | null
  lastPlayed: number | null
  release: number | null
  sizeBytes: number | null
  metacritic: number | null
  reviews: number | null
}

const time = (iso: string | null | undefined): number | null => {
  if (!iso) return null
  const ms = Date.parse(iso)
  return Number.isFinite(ms) ? ms : null
}

const share = (unlocked: number | null | undefined, total: number | null | undefined): number | null =>
  total != null && total > 0 && unlocked != null ? (unlocked / total) * 100 : null

export function favoriteSortValues(game: GameSummaryDto): SortValues {
  return {
    title: game.title,
    completion: share(game.trophiesUnlocked, game.trophiesTotal),
    playtimeMinutes: game.playtimeMinutes,
    lastPlayed: time(game.lastPlayedAt),
    release: time(game.releaseAt),
    sizeBytes: sizeOf(game).bytes || null,
    metacritic: game.sortMetacritic ?? game.metacriticScore,
    reviews: game.sortReviewPercent ?? game.reviewPercent,
  }
}

export function librarySortValues(game: SteamOwnedGameDto): SortValues {
  return {
    title: game.name,
    completion: share(game.trophiesUnlocked, game.trophiesTotal),
    // Steam reports whole minutes; the library answer rounds to tenths of an hour, which is plenty for ordering
    playtimeMinutes: game.playtimeHours * 60,
    lastPlayed: time(game.lastPlayedAt),
    release: time(game.releaseAt),
    sizeBytes: sizeOf(game).bytes || null,
    metacritic: game.sortMetacritic ?? null,
    reviews: game.sortReviewPercent ?? null,
  }
}

/** The number an order sorts by (null = unknown). "custom" and "alphabetical" are not numeric. */
export function numericValue(values: SortValues, order: SortOrder): number | null {
  switch (order) {
    case 'completion':
      return values.completion
    case 'playtime':
      return values.playtimeMinutes
    case 'lastPlayed':
      return values.lastPlayed
    case 'release':
      return values.release
    case 'size':
      return values.sizeBytes
    case 'metacritic':
      return values.metacritic
    case 'reviews':
      return values.reviews
    default:
      return null
  }
}

/** How many of the items have no value for the order (always 0 for the orders every game has: custom, alphabetical). */
export function countMissing<T>(items: readonly T[], valuesOf: (item: T) => SortValues, order: SortOrder): number {
  if (order === 'custom' || order === 'alphabetical') return 0
  return items.filter((item) => numericValue(valuesOf(item), order) === null).length
}

/**
 * The items in the chosen order. Games without a value come last in both directions, ordered by title; equal values are ordered by
 * title too (always A-Z), so the list never jumps around between refreshes. "custom" returns the list as it is (the server's manual order).
 */
export function sortItems<T>(
  items: readonly T[],
  valuesOf: (item: T) => SortValues,
  choice: SortChoice,
  locale: string
): T[] {
  if (choice.order === 'custom') return [...items]
  const collator = new Intl.Collator(locale, { numeric: true, sensitivity: 'base' })
  const sign = choice.direction === 'asc' ? 1 : -1
  const rows = items.map((item) => ({ item, values: valuesOf(item) }))
  rows.sort((a, b) => {
    if (choice.order === 'alphabetical') return sign * collator.compare(a.values.title, b.values.title)
    const x = numericValue(a.values, choice.order)
    const y = numericValue(b.values, choice.order)
    if (x === null && y === null) return collator.compare(a.values.title, b.values.title)
    if (x === null) return 1
    if (y === null) return -1
    return sign * (x - y) || collator.compare(a.values.title, b.values.title)
  })
  return rows.map((row) => row.item)
}
