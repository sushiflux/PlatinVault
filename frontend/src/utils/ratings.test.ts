import { describe, expect, it } from 'vitest'
import { overallRating, ratingSlots } from './ratings'

describe('ratingSlots', () => {
  it('lists the ratings a game has in the fixed order Steam, Metacritic, IGDB critics, IGDB players', () => {
    const slots = ratingSlots({ reviewPercent: 96, reviewScoreDesc: 'Overwhelmingly Positive', metacriticScore: 91, igdbRating: 88.4, igdbUserRating: 79.6 }, true)
    expect(slots.map((slot) => slot.key)).toEqual(['steam', 'metacritic', 'igdbCritics', 'igdbPlayers'])
    expect(slots.map((slot) => slot.value)).toEqual([96, 91, 88, 80])
    expect(slots[0]?.verdict).toBe('Overwhelmingly Positive')
  })

  it('leaves a missing rating out instead of keeping a place for it', () => {
    expect(ratingSlots({ reviewPercent: 80, metacriticScore: null, igdbRating: null, igdbUserRating: 70 }, true).map((slot) => slot.key)).toEqual(['steam', 'igdbPlayers'])
    expect(ratingSlots({}, true)).toEqual([])
  })

  it('never lists the IGDB ratings without IGDB credentials', () => {
    expect(ratingSlots({ reviewPercent: 80, igdbRating: 70, igdbUserRating: 60 }, false).map((slot) => slot.key)).toEqual(['steam'])
  })

  it('falls back to the library values when the card has none of its own', () => {
    expect(ratingSlots({ reviewPercent: null, sortReviewPercent: 90, metacriticScore: null, sortMetacritic: 75 }, false).map((slot) => slot.value)).toEqual([90, 75])
  })

  it('keeps a rating of zero (it is a value, not a missing one)', () => {
    expect(ratingSlots({ metacriticScore: 0 }, false).map((slot) => slot.value)).toEqual([0])
  })
})

describe('overallRating', () => {
  it('is the plain mean of the four ratings a game has', () => {
    expect(overallRating({ reviewPercent: 90, metacriticScore: 80, igdbRating: 70, igdbUserRating: 60 }, true)).toBe(75)
  })

  it('counts only the ratings that exist (no weighting, a missing one is not a zero)', () => {
    expect(overallRating({ reviewPercent: 90, metacriticScore: 70 }, true)).toBe(80)
    expect(overallRating({ reviewPercent: 90, igdbUserRating: 80, igdbRating: null, metacriticScore: null }, true)).toBe(85)
  })

  it('gives a single rating as it is, a rating of zero included', () => {
    expect(overallRating({ reviewPercent: 87 }, true)).toBe(87)
    expect(overallRating({ metacriticScore: 0 }, true)).toBe(0)
  })

  it('averages only Steam and Metacritic without IGDB credentials, even when the server sent stored IGDB values', () => {
    const game = { reviewPercent: 90, metacriticScore: 80, igdbRating: 70, igdbUserRating: 60 }
    expect(overallRating(game, true)).toBe(75)
    expect(overallRating(game, false)).toBe(85)
    expect(overallRating({ igdbRating: 70, igdbUserRating: 60 }, false)).toBeNull()
  })

  it('has no value for a game without any rating', () => {
    expect(overallRating({}, true)).toBeNull()
    expect(overallRating({ reviewPercent: null, metacriticScore: null, igdbRating: null, igdbUserRating: null }, true)).toBeNull()
  })

  it('reads the same values as the strip: whole numbers per rating, library fallbacks, IGDB only when configured; the mean has one decimal', () => {
    // 85.6 -> 86 and 70.4 -> 70 like in the strip, mean 78
    expect(overallRating({ reviewPercent: 85.6, metacriticScore: 70.4 }, true)).toBe(78)
    // 91 / 80 / 70: 80.333... -> 80.3
    expect(overallRating({ reviewPercent: 91, metacriticScore: 80, igdbRating: 70 }, true)).toBe(80.3)
    // The library's values count when the card has none
    expect(overallRating({ sortReviewPercent: 90, sortMetacritic: 80 }, true)).toBe(85)
    expect(overallRating({ reviewPercent: 90, igdbRating: 10, igdbUserRating: 10 }, false)).toBe(90)
  })
})
