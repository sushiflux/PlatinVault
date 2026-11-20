import { describe, expect, it } from 'vitest'
import type { GuideTabDto } from '../../api/types'
import {
  LIVE_TROPHIES_TAB_SLUG,
  LORE_TAB_SLUG,
  STATS_TAB_SLUG,
  buildSections,
  chapterItemsOf,
  gameAreas,
  gameTabHref,
  guideChapters,
  resolveGameTab,
  slugToTabParam,
  GUIDE_TAB_SLUG,
  tabParamToSlug,
  type ChapterItem,
} from './navModel'

const tab = (slug: string, extra: Partial<GuideTabDto> = {}): GuideTabDto =>
  ({ id: slug, slug, title: slug, icon: '', checkedItemsCount: 0, totalCheckableItems: 0, sections: [], isAchievementList: false, groupKey: null, ...extra }) as GuideTabDto

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
    expect(tabParamToSlug('guide')).toBe(GUIDE_TAB_SLUG)
    expect(tabParamToSlug('quests')).toBe('quests')
    expect(tabParamToSlug(null)).toBe('')
    for (const slug of [LIVE_TROPHIES_TAB_SLUG, STATS_TAB_SLUG, LORE_TAB_SLUG, GUIDE_TAB_SLUG, 'walkthrough']) {
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

  it('gives a game added by hand its trophy tab when the platform has trophies, but never the statistics', () => {
    const ps5 = { steamAppId: null, platform: 'playstation', tabs: [tab('walkthrough')] }
    expect(resolveGameTab(ps5, LIVE_TROPHIES_TAB_SLUG)).toBe(LIVE_TROPHIES_TAB_SLUG)
    expect(resolveGameTab(ps5, STATS_TAB_SLUG)).toBe('walkthrough')
    expect(resolveGameTab({ steamAppId: null, platform: 'xbox', tabs: [] }, '')).toBe(LIVE_TROPHIES_TAB_SLUG)
    // The guide itself: its first chapter, or nothing (the page's "no guide yet" card) - never the trophy tab
    expect(resolveGameTab(ps5, GUIDE_TAB_SLUG)).toBe('walkthrough')
    expect(resolveGameTab({ steamAppId: null, platform: 'playstation', tabs: [] }, GUIDE_TAB_SLUG)).toBe('')
    // A Switch game has none: only the guide
    expect(resolveGameTab({ steamAppId: null, platform: 'nintendo', tabs: [tab('walkthrough')] }, LIVE_TROPHIES_TAB_SLUG)).toBe('walkthrough')
    expect(resolveGameTab({ steamAppId: null, platform: 'nintendo', tabs: [] }, LIVE_TROPHIES_TAB_SLUG)).toBe('')
  })
})

describe('gameAreas', () => {
  it('gives a Steam game the live trophies and the statistics', () => {
    expect(gameAreas({ steamAppId: 10 })).toEqual({ trophies: 'live', stats: true })
    expect(gameAreas({ steamAppId: 10, platform: 'steam' })).toEqual({ trophies: 'live', stats: true })
  })

  it('names the trophy area of a game added by hand after its platform, without statistics', () => {
    expect(gameAreas({ steamAppId: null, platform: 'playstation' })).toEqual({ trophies: 'trophies', stats: false })
    for (const platform of ['xbox', 'epic', 'gog', 'ubisoft', 'ea', 'battlenet']) {
      expect(gameAreas({ steamAppId: null, platform })).toEqual({ trophies: 'achievements', stats: false })
    }
  })

  it('has no area at all for a Switch game, a Steam game without a link and a game that is not loaded yet', () => {
    expect(gameAreas({ steamAppId: null, platform: 'nintendo' })).toEqual({ trophies: null, stats: false })
    expect(gameAreas({ steamAppId: null, platform: 'steam' })).toEqual({ trophies: null, stats: false })
    expect(gameAreas({ steamAppId: null })).toEqual({ trophies: null, stats: false })
    expect(gameAreas(undefined)).toEqual({ trophies: null, stats: false })
    expect(gameAreas(null)).toEqual({ trophies: null, stats: false })
  })

  it('agrees with resolveGameTab on which areas can be opened', () => {
    for (const game of [{ steamAppId: 10, tabs: [] }, { steamAppId: null, platform: 'xbox', tabs: [] }, { steamAppId: null, platform: 'nintendo', tabs: [] }]) {
      const areas = gameAreas(game)
      expect(resolveGameTab(game, LIVE_TROPHIES_TAB_SLUG) === LIVE_TROPHIES_TAB_SLUG).toBe(areas.trophies !== null)
      expect(resolveGameTab(game, STATS_TAB_SLUG) === STATS_TAB_SLUG).toBe(areas.stats)
    }
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
