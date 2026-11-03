import { describe, expect, it } from 'vitest'
import type { ShowcaseGameDto } from '../../api/types'
import { announcedNames, landingSchedule, needsBaseline, pendingGames, presentationDurationMs } from './presentation'

const game = (appId: number): ShowcaseGameDto =>
  ({ appId, name: `Game ${appId}`, coverUrl: null, heroBannerUrl: null, existingSlug: null, trophiesTotal: 10, platinumAt: null }) as ShowcaseGameDto

const games = [1, 2, 3, 4, 5, 6].map(game)

describe('pendingGames', () => {
  it('is empty while the state is unknown (loading or failed) - never animate on a guess', () => {
    expect(pendingGames(games, undefined)).toEqual([])
  })

  it('is empty without a stored state: the first use records a baseline instead of animating the collection', () => {
    expect(pendingGames(games, { initialized: false, appIds: [] })).toEqual([])
    expect(needsBaseline({ initialized: false, appIds: [] })).toBe(true)
  })

  it('lists exactly the games that were never presented, in shelf order', () => {
    const pending = pendingGames(games, { initialized: true, appIds: [2, 4, 99] })
    expect(pending.map((g) => g.appId)).toEqual([1, 3, 5, 6])
  })

  it('is empty when everything was presented', () => {
    expect(pendingGames(games, { initialized: true, appIds: [1, 2, 3, 4, 5, 6] })).toEqual([])
  })

  it('needs no baseline once the state exists, and none while it is unknown', () => {
    expect(needsBaseline({ initialized: true, appIds: [] })).toBe(false)
    expect(needsBaseline(undefined)).toBe(false)
  })
})

describe('landingSchedule', () => {
  it('lands up to five games one after another with a clear beat', () => {
    for (const count of [1, 2, 5]) expect(landingSchedule(count, false).stepMs).toBe(850)
  })

  it('speeds up beyond five games and keeps the whole presentation short', () => {
    for (const count of [6, 12, 30, 200]) {
      expect(landingSchedule(count, false).stepMs).toBeLessThan(850)
      expect(landingSchedule(count, false).stepMs).toBeGreaterThanOrEqual(100)
    }
    expect(presentationDurationMs(5, false)).toBeLessThan(5500)
    expect(presentationDurationMs(30, false)).toBeLessThan(7000)
    expect(presentationDurationMs(0, false)).toBe(0)
  })

  it('reduced motion is a quick calm fade, not the flight', () => {
    const reduced = landingSchedule(5, true)
    expect(reduced.stepMs).toBeLessThan(landingSchedule(5, false).stepMs)
    expect(presentationDurationMs(5, true)).toBeLessThan(1500)
  })
})

describe('announcedNames', () => {
  it('keeps the first three names in order', () => {
    expect(announcedNames(['a', 'b', 'c', 'd'])).toEqual(['a', 'b', 'c'])
    expect(announcedNames(['a'])).toEqual(['a'])
  })
})
