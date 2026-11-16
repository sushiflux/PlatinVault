import { describe, expect, it } from 'vitest'
import type { GameSummaryDto, GuideTabDto } from '../../api/types'
import {
  LIVE_TROPHIES_TAB_SLUG,
  LORE_TAB_SLUG,
  STATS_TAB_SLUG,
  buildSections,
  chapterItemsOf,
  filterTree,
  gameTabHref,
  guideChapters,
  resolveGameTab,
  slugToTabParam,
  tabParamToSlug,
  treeGames,
  type ChapterItem,
} from './navModel'

const tab = (slug: string, extra: Partial<GuideTabDto> = {}): GuideTabDto =>
  ({ id: slug, slug, title: slug, icon: '', checkedItemsCount: 0, totalCheckableItems: 0, sections: [], isAchievementList: false, groupKey: null, ...extra }) as GuideTabDto

const game = (slug: string, title: string, isFavorite = true): GameSummaryDto => ({ id: slug, slug, title, isFavorite }) as unknown as GameSummaryDto

describe('tab params', () => {
  it('returns a crafted ?tab= that names an Object.prototype member as the plain string, never the inherited value', () => {
    for (const name of ['__proto__', 'constructor', 'toString', 'hasOwnProperty', 'valueOf']) {
      expect(tabParamToSlug(name)).toBe(name)
      expect(typeof resolveGameTab(null, tabParamToSlug(name))).toBe('string')
    }
  })

  it('maps the short URL names of the areas both ways and leaves chapter slugs alone', () => {
    expect(tabParamToSlug('live')).toBe(LIVE_TROPHIES_TAB_SLUG)
    expect(tabParamToSlug('stats')).toBe(STATS_TAB_SLUG)
    expect(tabParamToSlug('story')).toBe(LORE_TAB_SLUG)
    expect(tabParamToSlug('quests')).toBe('quests')
    expect(tabParamToSlug(null)).toBe('')
    for (const slug of [LIVE_TROPHIES_TAB_SLUG, STATS_TAB_SLUG, LORE_TAB_SLUG, 'walkthrough']) {
      expect(tabParamToSlug(slugToTabParam(slug))).toBe(slug)
    }
    expect(gameTabHref('final fantasy', LIVE_TROPHIES_TAB_SLUG)).toBe('/games/final%20fantasy?tab=live')
  })
})

describe('guideChapters / resolveGameTab', () => {
  const detail = {
    steamAppId: 10,
    tabs: [tab('walkthrough'), tab('steam-achievements', { isAchievementList: true }), tab(LORE_TAB_SLUG), tab('quests')],
  }

  it('leaves out the lore tab and the hidden achievement list', () => {
    expect(guideChapters(detail).map((t) => t.slug)).toEqual(['walkthrough', 'quests'])
  })

  it('keeps a valid tab, falls back to the first chapter, else live trophies', () => {
    expect(resolveGameTab(detail, 'quests')).toBe('quests')
    expect(resolveGameTab(detail, LIVE_TROPHIES_TAB_SLUG)).toBe(LIVE_TROPHIES_TAB_SLUG)
    expect(resolveGameTab(detail, LORE_TAB_SLUG)).toBe(LORE_TAB_SLUG)
    expect(resolveGameTab(detail, 'gone')).toBe('walkthrough')
    expect(resolveGameTab(detail, 'steam-achievements')).toBe('walkthrough')
    expect(resolveGameTab({ steamAppId: 10, tabs: [] }, '')).toBe(LIVE_TROPHIES_TAB_SLUG)
    // No Steam link: no live trophies and no statistics
    expect(resolveGameTab({ steamAppId: null, tabs: [] }, STATS_TAB_SLUG)).toBe('')
  })
})

describe('buildSections', () => {
  const item = (slug: string, groupKey: string | null): ChapterItem => ({ slug, title: slug, checked: 0, total: 0, groupKey })

  it('groups in the fixed order, numbers across groups and drops empty groups', () => {
    const { sections, headings } = buildSections([item('a', 'extras'), item('b', 'start'), item('c', 'combat'), item('d', 'unknown')])
    expect(headings).toBe(true)
    expect(sections.map((s) => s.key)).toEqual(['start', 'combat', 'extras'])
    expect(sections.flatMap((s) => s.tabs.map((t) => `${t.number}:${t.chapter.slug}`))).toEqual(['1:b', '2:c', '3:a', '4:d'])
  })

  it('is one plain list without headings for a single group or no keys', () => {
    expect(buildSections([item('a', null), item('b', null)]).headings).toBe(false)
    expect(buildSections([item('a', 'start'), item('b', 'start')]).headings).toBe(false)
  })

  it('takes the leading number off the titles and marks failed chapters', () => {
    const items = chapterItemsOf({ tabs: [tab('x', { title: '3. Bosse', id: 't1' })] }, new Set(['t1']))
    expect(items[0]).toMatchObject({ title: 'Bosse', failed: true })
  })
})

describe('treeGames / filterTree', () => {
  const games = [game('alpha', 'Alpha Quest'), game('beta', 'Bravo Pokémon'), game('charlie', 'Charlie', false)]
  const chapters: ChapterItem[] = [
    { slug: 'walkthrough', title: 'Komplettlösung', checked: 0, total: 0, groupKey: null },
    { slug: 'bosses', title: 'Bosse', checked: 0, total: 0, groupKey: null },
  ]

  it('lists the favourites and adds the open game at the end when it is none', () => {
    expect(treeGames(games, 'alpha').map((g) => g.slug)).toEqual(['alpha', 'beta'])
    expect(treeGames(games, 'charlie').map((g) => g.slug)).toEqual(['alpha', 'beta', 'charlie'])
  })

  it('finds games without accents and case, and the open game by one of its chapters', () => {
    const favourites = treeGames(games, 'alpha')
    expect(filterTree(favourites, 'alpha', chapters, 'pokemon').games.map((g) => g.slug)).toEqual(['beta'])
    const byChapter = filterTree(favourites, 'alpha', chapters, 'boss')
    expect(byChapter.games.map((g) => g.slug)).toEqual(['alpha'])
    expect(byChapter.chapters.map((c) => c.slug)).toEqual(['bosses'])
    // The open game matched by its own title shows all of its chapters
    expect(filterTree(favourites, 'alpha', chapters, 'alpha').chapters).toHaveLength(2)
    expect(filterTree(favourites, 'alpha', chapters, 'zzz')).toMatchObject({ games: [], any: false })
    expect(filterTree(favourites, 'alpha', chapters, '  ')).toMatchObject({ games: favourites, chapters, any: true })
  })
})
