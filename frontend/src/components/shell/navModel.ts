import type { GameDetailDto, GuideTabDto } from '../../api/types'

// Pure model of the game navigation (the side tree of a game page) and of the game page's tabs. Shared by the tree (components/shell/ShellNav.tsx) and the
// game page (pages/GameGuidePage.tsx), so both always agree on which tab is active and in which order the chapters stand.

/** Internal slugs of the three areas next to the guide chapters (the guide chapters use their own slug). */
export const LIVE_TROPHIES_TAB_SLUG = '__live_steam_trophies__'
export const STATS_TAB_SLUG = '__steam_stats__'
// Slug of the optional Story & Lore tab created by POST /api/games/{slug}/ai-lore
export const LORE_TAB_SLUG = 'story-lore'

// The active tab lives in the URL (?tab=...), so a link or a reload lands on the same chapter / area. The areas have short names
// there; a chapter uses its own slug.
const TAB_PARAM_TO_SLUG: Record<string, string> = { live: LIVE_TROPHIES_TAB_SLUG, stats: STATS_TAB_SLUG, story: LORE_TAB_SLUG }

export function tabParamToSlug(param: string | null | undefined): string {
  if (!param) return ''
  // Own keys only: a crafted ?tab=__proto__ / constructor would otherwise return an inherited member of Object.prototype instead of a string
  return (Object.hasOwn(TAB_PARAM_TO_SLUG, param) ? TAB_PARAM_TO_SLUG[param] : undefined) ?? param
}

export function slugToTabParam(slug: string): string {
  return slug === LIVE_TROPHIES_TAB_SLUG ? 'live' : slug === STATS_TAB_SLUG ? 'stats' : slug === LORE_TAB_SLUG ? 'story' : slug
}

/** Link to one tab of a game page. */
export function gameTabHref(gameSlug: string, tabSlug: string): string {
  return `/games/${encodeURIComponent(gameSlug)}?tab=${encodeURIComponent(slugToTabParam(tabSlug))}`
}

type GameTabsSource = Pick<GameDetailDto, 'tabs' | 'steamAppId'>

/** The guide chapters of the navigation: without the lore tab (own entry) and without the hidden Steam achievement list. */
export function guideChapters(game: Pick<GameDetailDto, 'tabs'> | null | undefined): GuideTabDto[] {
  return game ? game.tabs.filter((tab) => tab.slug !== LORE_TAB_SLUG && !tab.isAchievementList) : []
}

/**
 * The tab a game page shows for the requested slug: the requested one when it exists, else the first chapter, else the live trophies
 * (games with a Steam link), else the statistics, else nothing. Story & Lore always exists (its gate offers the research).
 */
export function resolveGameTab(game: GameTabsSource | null | undefined, requestedSlug: string): string {
  if (!game) return requestedSlug
  const hasSteam = Boolean(game.steamAppId)
  if (requestedSlug === LORE_TAB_SLUG) return requestedSlug
  if ((requestedSlug === LIVE_TROPHIES_TAB_SLUG || requestedSlug === STATS_TAB_SLUG) && hasSteam) return requestedSlug
  const chapters = guideChapters(game)
  if (chapters.some((tab) => tab.slug === requestedSlug)) return requestedSlug
  return chapters[0]?.slug ?? (hasSteam ? LIVE_TROPHIES_TAB_SLUG : '')
}

// The groups of the chapter navigation, in the fixed order they are shown in (a key the server sends that is not listed is
// treated as "extras"; chapters without a key as well, as soon as the guide has groups at all)
export const CHAPTER_GROUPS = ['start', 'story', 'characters', 'combat', 'progress', 'explore', 'economy', 'extras'] as const
export type ChapterGroup = (typeof CHAPTER_GROUPS)[number]

export interface ChapterItem {
  slug: string
  // Title without its leading "3. "
  title: string
  checked: number
  total: number
  // The group key the server assigned to the chapter (null = none)
  groupKey: string | null
  // The research of this chapter failed (a placeholder that offers a retry)
  failed?: boolean
}

export interface ChapterSection {
  key: ChapterGroup | null
  // Position in the display order (1-based), one running number across all groups
  tabs: { chapter: ChapterItem; number: number }[]
}

function groupOf(key: string | null): ChapterGroup {
  return (CHAPTER_GROUPS as readonly string[]).includes(key ?? '') ? (key as ChapterGroup) : 'extras'
}

/**
 * The chapters in display order: grouped in the fixed group order (chapters keep their own order inside a group), empty groups
 * left out. A guide with one group only - or with no group keys at all - is one plain list without headings.
 */
export function buildSections(chapters: ChapterItem[]): { sections: ChapterSection[]; headings: boolean } {
  const hasKeys = chapters.some((chapter) => chapter.groupKey)
  const grouped = hasKeys
    ? CHAPTER_GROUPS.map((key) => ({ key, items: chapters.filter((chapter) => groupOf(chapter.groupKey) === key) })).filter((g) => g.items.length > 0)
    : []
  const headings = grouped.length > 1
  const raw = headings ? grouped.map((g) => ({ key: g.key as ChapterGroup | null, items: g.items })) : [{ key: null, items: chapters }]
  let counter = 0
  const sections: ChapterSection[] = raw.map((g) => ({ key: g.key, tabs: g.items.map((chapter) => ({ chapter, number: ++counter })) }))
  return { sections, headings }
}

/** The chapter items of a game for the navigation (titles without their leading number). */
export function chapterItemsOf(game: Pick<GameDetailDto, 'tabs'> | null | undefined, failedTabIds: ReadonlySet<string> = new Set()): ChapterItem[] {
  return guideChapters(game).map((tab) => ({
    slug: tab.slug,
    title: tab.title.replace(/^\d+\.\s*/, ''),
    checked: tab.checkedItemsCount,
    total: tab.totalCheckableItems,
    groupKey: tab.groupKey ?? null,
    failed: failedTabIds.has(tab.id),
  }))
}
