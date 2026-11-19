import { describe, expect, it } from 'vitest'
import type { ShowcaseGameDto } from '../../api/types'
import { announcedNames, cabinetKey, isPresented, landingSchedule, needsBaseline, pendingGames, presentationDurationMs, presentedPayload } from './presentation'

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

// A game added by hand has no AppID (0): the key and its game id identify it
const manual = (id: string): ShowcaseGameDto =>
  ({ appId: 0, key: `game:${id}`, gameId: id, name: `Manual ${id}`, coverUrl: null, heroBannerUrl: null, existingSlug: `m-${id}`, trophiesTotal: 40, platinumAt: '2026-11-18T20:00:00Z', platform: 'playstation', platformVariant: 'ps5' }) as ShowcaseGameDto

const ID_A = '0b8f4e3a-1111-4222-8333-444455556666'
const ID_B = '9c1d2e3f-aaaa-4bbb-8ccc-ddddeeeeffff'

describe('cabinet identity by key', () => {
  it('is the server key, or the AppID for an older server that sends none', () => {
    expect(cabinetKey(game(220))).toBe('steam:220')
    expect(cabinetKey({ appId: 220, key: 'steam:220' })).toBe('steam:220')
    expect(cabinetKey(manual(ID_A))).toBe(`game:${ID_A}`)
  })

  it('tells presented games apart by AppID for Steam and by game id for games added by hand', () => {
    const state = { initialized: true, appIds: [1, 2], gameIds: [ID_A] }
    expect(isPresented(game(1), state)).toBe(true)
    expect(isPresented(game(3), state)).toBe(false)
    expect(isPresented(manual(ID_A), state)).toBe(true)
    expect(isPresented(manual(ID_B), state)).toBe(false)
  })

  it('does not take a manual game for a presented Steam game of AppID 0', () => {
    expect(isPresented(manual(ID_B), { initialized: true, appIds: [0], gameIds: [] })).toBe(false)
  })

  it('compares game ids without dashes and case', () => {
    const state = { initialized: true, appIds: [], gameIds: [ID_A.replace(/-/g, '').toUpperCase()] }
    expect(isPresented(manual(ID_A), state)).toBe(true)
  })

  it('lets a new manual platinum game animate while the stored state has Steam games only (an older state without gameIds)', () => {
    const mixed = [game(1), manual(ID_A)]
    const pending = pendingGames(mixed, { initialized: true, appIds: [1] })
    expect(pending.map(cabinetKey)).toEqual([`game:${ID_A}`])
  })

  it('lists manual games in shelf order next to Steam ones, only the unpresented', () => {
    const mixed = [manual(ID_A), game(1), manual(ID_B), game(2)]
    const pending = pendingGames(mixed, { initialized: true, appIds: [2], gameIds: [ID_B] })
    expect(pending.map(cabinetKey)).toEqual([`game:${ID_A}`, 'steam:1'])
  })

  it('splits keys into the two lists the server stores', () => {
    expect(presentedPayload(['steam:10', `game:${ID_A}`, 'steam:11', 'garbage', 'steam:x'])).toEqual({ appIds: [10, 11], gameIds: [ID_A] })
    expect(presentedPayload([])).toEqual({ appIds: [], gameIds: [] })
  })
})
