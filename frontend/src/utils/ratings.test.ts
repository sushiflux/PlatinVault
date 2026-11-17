import { describe, expect, it } from 'vitest'
import { ratingSlots } from './ratings'

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
