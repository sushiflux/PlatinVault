import { describe, expect, it } from 'vitest'
import type { GameSummaryDto, SteamOwnedGameDto } from '../api/types'
import {
  DEFAULT_CHOICE,
  DEFAULT_DIRECTION,
  ORDERS_BY_VIEW,
  SHARED_ORDERS,
  chooseOrder,
  countMissing,
  favoriteSortValues,
  flipDirection,
  librarySortValues,
  parseSortChoice,
  serializeSortChoice,
  sizeOf,
  sortItems,
  type SortValues,
} from './dashboardSort'

const values = (title: string, patch: Partial<SortValues> = {}): SortValues => ({
  title,
  completion: null,
  playtimeMinutes: null,
  lastPlayed: null,
  release: null,
  sizeBytes: null,
  metacritic: null,
  reviews: null,
  igdbCritics: null,
  igdbPlayers: null,
  overall: null,
  ...patch,
})

const titles = (items: SortValues[]) => items.map((item) => item.title)
const sorted = (items: SortValues[], order: Parameters<typeof sortItems>[2]['order'], direction: 'asc' | 'desc') =>
  titles(sortItems(items, (item) => item, { order, direction }, 'en'))

describe('sortItems', () => {
  const items = [
    values('Beta', { metacritic: 70 }),
    values('Alpha', { metacritic: 90 }),
    values('Gamma'), // no Metacritic score
    values('Delta', { metacritic: 70 }),
  ]

  it('puts games without a value at the end - in BOTH directions', () => {
    expect(sorted(items, 'metacritic', 'desc')).toEqual(['Alpha', 'Beta', 'Delta', 'Gamma'])
    expect(sorted(items, 'metacritic', 'asc')).toEqual(['Beta', 'Delta', 'Alpha', 'Gamma'])
  })

  it('orders equal values by title, always A to Z, so the list is stable', () => {
    expect(sorted(items, 'metacritic', 'asc').slice(0, 2)).toEqual(['Beta', 'Delta'])
    expect(sorted(items, 'metacritic', 'desc').slice(1, 3)).toEqual(['Beta', 'Delta'])
  })

  it('never treats a missing value as 0', () => {
    const list = [values('Zero', { playtimeMinutes: 0 }), values('Unknown'), values('Some', { playtimeMinutes: 30 })]
    // 0 minutes is a real value (never played); unknown is not
    expect(sorted(list, 'playtime', 'asc')).toEqual(['Zero', 'Some', 'Unknown'])
    expect(sorted(list, 'playtime', 'desc')).toEqual(['Some', 'Zero', 'Unknown'])
  })

  it('sorts titles with numbers naturally and respects the direction', () => {
    const list = [values('Level 10'), values('Level 2'), values('level 1')]
    expect(sorted(list, 'alphabetical', 'asc')).toEqual(['level 1', 'Level 2', 'Level 10'])
    expect(sorted(list, 'alphabetical', 'desc')).toEqual(['Level 10', 'Level 2', 'level 1'])
  })

  it('sorts dates, sizes and completion by their numbers', () => {
    const list = [
      values('Old', { release: Date.parse('1999-01-01'), sizeBytes: 5, completion: 10 }),
      values('New', { release: Date.parse('2024-01-01'), sizeBytes: 50, completion: 100 }),
    ]
    expect(sorted(list, 'release', 'desc')).toEqual(['New', 'Old'])
    expect(sorted(list, 'size', 'asc')).toEqual(['Old', 'New'])
    expect(sorted(list, 'completion', 'desc')).toEqual(['New', 'Old'])
  })

  it('leaves the manual order untouched for "custom"', () => {
    const list = [values('B'), values('A')]
    expect(sorted(list, 'custom', 'asc')).toEqual(['B', 'A'])
    expect(sorted(list, 'custom', 'desc')).toEqual(['B', 'A'])
  })

  it('does not change the list it was given', () => {
    const list = [values('B', { metacritic: 1 }), values('A', { metacritic: 2 })]
    sortItems(list, (item) => item, { order: 'metacritic', direction: 'desc' }, 'en')
    expect(titles(list)).toEqual(['B', 'A'])
  })
})

describe('countMissing', () => {
  it('counts the games without a value, and none for the orders every game has', () => {
    const list = [values('A', { sizeBytes: 1 }), values('B')]
    expect(countMissing(list, (item) => item, 'size')).toBe(1)
    expect(countMissing(list, (item) => item, 'alphabetical')).toBe(0)
    expect(countMissing(list, (item) => item, 'custom')).toBe(0)
  })
})

describe('stored choice', () => {
  it('round-trips and starts every order in its default direction', () => {
    expect(parseSortChoice(serializeSortChoice({ order: 'size', direction: 'asc' }), 'all')).toEqual({ order: 'size', direction: 'asc' })
    expect(DEFAULT_DIRECTION.completion).toBe('desc')
    expect(DEFAULT_DIRECTION.alphabetical).toBe('asc')
    expect(DEFAULT_DIRECTION.release).toBe('desc')
    expect(chooseOrder({ order: 'custom', direction: 'asc' }, 'alphabetical')).toEqual({ order: 'alphabetical', direction: 'asc' })
    expect(chooseOrder({ order: 'playtime', direction: 'asc' }, 'playtime')).toEqual({ order: 'playtime', direction: 'asc' })
    expect(flipDirection({ order: 'playtime', direction: 'asc' })).toEqual({ order: 'playtime', direction: 'desc' })
  })

  it('falls back to the defaults for anything it does not know', () => {
    expect(parseSortChoice(undefined, 'favorites')).toEqual(DEFAULT_CHOICE.favorites)
    expect(parseSortChoice(null, 'all')).toEqual({ order: 'lastPlayed', direction: 'desc' })
    expect(parseSortChoice('dateAdded:desc', 'favorites')).toEqual(DEFAULT_CHOICE.favorites)
    expect(parseSortChoice('size', 'all')).toEqual(DEFAULT_CHOICE.all)
    expect(parseSortChoice('size:up', 'all')).toEqual(DEFAULT_CHOICE.all)
    expect(parseSortChoice('size:asc:x', 'all')).toEqual(DEFAULT_CHOICE.all)
  })

  it('knows the manual order only in the favourites view', () => {
    expect(ORDERS_BY_VIEW.favorites[0]).toBe('custom')
    expect(ORDERS_BY_VIEW.all).not.toContain('custom')
    expect(parseSortChoice('custom:asc', 'all')).toEqual(DEFAULT_CHOICE.all)
    expect(parseSortChoice('custom:asc', 'favorites')).toEqual({ order: 'custom', direction: 'asc' })
  })
})

describe('values of the two kinds of cards', () => {
  const favorite = {
    title: 'Fav',
    trophiesUnlocked: 5,
    trophiesTotal: 20,
    playtimeMinutes: 90,
    lastPlayedAt: '2026-10-01T10:00:00Z',
    releaseAt: '2020-05-05T00:00:00Z',
    sizeOnDiskBytes: 1234,
    metacriticScore: 80,
    sortMetacritic: 82,
    reviewPercent: 90,
    sortReviewPercent: 91,
  } as GameSummaryDto

  it('reads a favourite, preferring the sort values of the library data', () => {
    const v = favoriteSortValues(favorite, true)
    expect(v).toMatchObject({ completion: 25, playtimeMinutes: 90, sizeBytes: 1234, metacritic: 82, reviews: 91 })
    expect(v.release).toBe(Date.parse('2020-05-05T00:00:00Z'))
    expect(favoriteSortValues({ ...favorite, sortMetacritic: null, sortReviewPercent: undefined }, true)).toMatchObject({ metacritic: 80, reviews: 90 })
  })

  it('treats a game without achievements as unknown, not as 0 %', () => {
    expect(favoriteSortValues({ ...favorite, trophiesTotal: 0, trophiesUnlocked: 0 }, true).completion).toBeNull()
    const owned = { name: 'Lib', playtimeHours: 2, lastPlayedAt: null, trophiesTotal: null, trophiesUnlocked: null } as SteamOwnedGameDto
    const v = librarySortValues(owned, true)
    expect(v.completion).toBeNull()
    expect(v.playtimeMinutes).toBe(120)
    expect(v.lastPlayed).toBeNull()
    expect(v.release).toBeNull()
    expect(v.sizeBytes).toBeNull()
  })

  it('sorts by the real size, and by the estimated install size for games that are not installed', () => {
    const installed = favoriteSortValues({ ...favorite, sizeOnDiskBytes: 10, installSizeBytes: 99 }, true)
    const notInstalled = favoriteSortValues({ ...favorite, sizeOnDiskBytes: null, installSizeBytes: 50 }, true)
    const unknown = favoriteSortValues({ ...favorite, sizeOnDiskBytes: null, installSizeBytes: null }, true)
    expect(installed.sizeBytes).toBe(10)
    expect(notInstalled.sizeBytes).toBe(50)
    expect(unknown.sizeBytes).toBeNull()
    expect(librarySortValues({ name: 'L', playtimeHours: 1, installSizeBytes: 7 } as SteamOwnedGameDto, true).sizeBytes).toBe(7)
    expect(sizeOf({ sizeOnDiskBytes: 0, installSizeBytes: 5 })).toEqual({ kind: 'estimate', bytes: 5 })
  })

  it('treats an unparseable date as unknown', () => {
    expect(favoriteSortValues({ ...favorite, releaseAt: 'soon' }, true).release).toBeNull()
  })
})

describe('the rating orders: IGDB critics, IGDB players, overall rating', () => {
  const fav = (title: string, patch: Partial<GameSummaryDto>) =>
    favoriteSortValues({ title, trophiesUnlocked: 0, trophiesTotal: 0, ...patch } as GameSummaryDto, true)

  it('are in both views, after the Steam reviews, best first', () => {
    for (const order of ['igdbCritics', 'igdbPlayers', 'overall'] as const) {
      expect(ORDERS_BY_VIEW.favorites).toContain(order)
      expect(ORDERS_BY_VIEW.all).toContain(order)
      expect(DEFAULT_DIRECTION[order]).toBe('desc')
      expect(parseSortChoice(`${order}:asc`, 'all')).toEqual({ order, direction: 'asc' })
    }
    expect(SHARED_ORDERS.slice(-4)).toEqual(['reviews', 'igdbCritics', 'igdbPlayers', 'overall'])
  })

  it('read the IGDB ratings and the mean of the ratings a favourite has', () => {
    const v = fav('A', { reviewPercent: 90, metacriticScore: 80, igdbRating: 70, igdbUserRating: 60 })
    expect(v).toMatchObject({ igdbCritics: 70, igdbPlayers: 60, overall: 75 })
    expect(fav('B', {})).toMatchObject({ igdbCritics: null, igdbPlayers: null, overall: null })
    // Only a Steam rating: that is the overall value
    expect(fav('C', { reviewPercent: 87 }).overall).toBe(87)
  })

  it('read the same for a library game', () => {
    const owned = { name: 'L', playtimeHours: 1, sortReviewPercent: 90, sortMetacritic: 70, igdbRating: 50, igdbUserRating: null } as SteamOwnedGameDto
    expect(librarySortValues(owned, true)).toMatchObject({ igdbCritics: 50, igdbPlayers: null, overall: 70 })
    expect(librarySortValues({ name: 'N', playtimeHours: 0 } as SteamOwnedGameDto, true)).toMatchObject({ igdbCritics: null, igdbPlayers: null, overall: null })
  })

  it('ignore the IGDB ratings without credentials, exactly like the tile strip: no critics/players values, the mean only has Steam and Metacritic', () => {
    const patch = { reviewPercent: 90, metacriticScore: 80, igdbRating: 70, igdbUserRating: 60 } as Partial<GameSummaryDto>
    const base = { title: 'A', trophiesUnlocked: 0, trophiesTotal: 0, ...patch } as GameSummaryDto
    expect(favoriteSortValues(base, false)).toMatchObject({ igdbCritics: null, igdbPlayers: null, overall: 85 })
    expect(favoriteSortValues(base, true)).toMatchObject({ igdbCritics: 70, igdbPlayers: 60, overall: 75 })
    const owned = { name: 'L', playtimeHours: 1, sortReviewPercent: 90, sortMetacritic: 70, igdbRating: 50, igdbUserRating: 40 } as SteamOwnedGameDto
    expect(librarySortValues(owned, false)).toMatchObject({ igdbCritics: null, igdbPlayers: null, overall: 80 })
    const items = [base, { ...base, title: 'B' }]
    expect(countMissing(items, (item) => favoriteSortValues(item, false), 'igdbCritics')).toBe(2)
    expect(countMissing(items, (item) => favoriteSortValues(item, true), 'igdbCritics')).toBe(0)
  })

  it('sort best first, reversible, games without a value last in both directions, ties by title', () => {
    const items = [
      fav('Delta', {}),
      fav('Beta', { igdbRating: 80, reviewPercent: 60 }),
      fav('Alpha', { igdbRating: 90, reviewPercent: 90 }),
      fav('Gamma', { igdbRating: 80, reviewPercent: 60 }),
    ]
    expect(sorted(items, 'igdbCritics', 'desc')).toEqual(['Alpha', 'Beta', 'Gamma', 'Delta'])
    expect(sorted(items, 'igdbCritics', 'asc')).toEqual(['Beta', 'Gamma', 'Alpha', 'Delta'])
    expect(sorted(items, 'overall', 'desc')).toEqual(['Alpha', 'Beta', 'Gamma', 'Delta'])
    expect(sorted(items, 'overall', 'asc')).toEqual(['Beta', 'Gamma', 'Alpha', 'Delta'])
    // Nobody has IGDB players: all missing, ordered by title
    expect(sorted(items, 'igdbPlayers', 'desc')).toEqual(['Alpha', 'Beta', 'Delta', 'Gamma'])
    expect(countMissing(items, (item) => item, 'igdbPlayers')).toBe(4)
    expect(countMissing(items, (item) => item, 'overall')).toBe(1)
  })
})
