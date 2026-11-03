import { describe, expect, it } from 'vitest'
import type { GameSummaryDto, SteamOwnedGameDto } from '../api/types'
import {
  DEFAULT_CHOICE,
  DEFAULT_DIRECTION,
  ORDERS_BY_VIEW,
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
    const v = favoriteSortValues(favorite)
    expect(v).toMatchObject({ completion: 25, playtimeMinutes: 90, sizeBytes: 1234, metacritic: 82, reviews: 91 })
    expect(v.release).toBe(Date.parse('2020-05-05T00:00:00Z'))
    expect(favoriteSortValues({ ...favorite, sortMetacritic: null, sortReviewPercent: undefined })).toMatchObject({ metacritic: 80, reviews: 90 })
  })

  it('treats a game without achievements as unknown, not as 0 %', () => {
    expect(favoriteSortValues({ ...favorite, trophiesTotal: 0, trophiesUnlocked: 0 }).completion).toBeNull()
    const owned = { name: 'Lib', playtimeHours: 2, lastPlayedAt: null, trophiesTotal: null, trophiesUnlocked: null } as SteamOwnedGameDto
    const v = librarySortValues(owned)
    expect(v.completion).toBeNull()
    expect(v.playtimeMinutes).toBe(120)
    expect(v.lastPlayed).toBeNull()
    expect(v.release).toBeNull()
    expect(v.sizeBytes).toBeNull()
  })

  it('sorts by the real size, and by the estimated install size for games that are not installed', () => {
    const installed = favoriteSortValues({ ...favorite, sizeOnDiskBytes: 10, installSizeBytes: 99 })
    const notInstalled = favoriteSortValues({ ...favorite, sizeOnDiskBytes: null, installSizeBytes: 50 })
    const unknown = favoriteSortValues({ ...favorite, sizeOnDiskBytes: null, installSizeBytes: null })
    expect(installed.sizeBytes).toBe(10)
    expect(notInstalled.sizeBytes).toBe(50)
    expect(unknown.sizeBytes).toBeNull()
    expect(librarySortValues({ name: 'L', playtimeHours: 1, installSizeBytes: 7 } as SteamOwnedGameDto).sizeBytes).toBe(7)
    expect(sizeOf({ sizeOnDiskBytes: 0, installSizeBytes: 5 })).toEqual({ kind: 'estimate', bytes: 5 })
  })

  it('treats an unparseable date as unknown', () => {
    expect(favoriteSortValues({ ...favorite, releaseAt: 'soon' }).release).toBeNull()
  })
})
