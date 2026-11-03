import { describe, expect, it } from 'vitest'
import type { GameSummaryDto } from '../api/types'
import { gameGenres, missingFacts, normalizeEstimate } from './GameTiles'

describe('normalizeEstimate', () => {
  it('writes ranges with an en dash and the short unit of the language', () => {
    expect(normalizeEstimate('ca. 130-160 Stunden', 'Std.', 'ca.')).toBe('130–160 Std.')
    expect(normalizeEstimate('ca. 130-160 Stunden', 'h', 'approx.')).toBe('130–160 h')
    expect(normalizeEstimate('ca. 50–60 Stunden', 'h', 'approx.')).toBe('50–60 h')
    expect(normalizeEstimate('130 bis 160 Std.', 'h', 'approx.')).toBe('130–160 h')
    expect(normalizeEstimate('40 to 60 hours', 'Std.', 'ca.')).toBe('40–60 Std.')
  })

  it('keeps "approximately" for a single value, in the language of the app', () => {
    expect(normalizeEstimate('ca. 40 Stunden', 'Std.', 'ca.')).toBe('ca. 40 Std.')
    expect(normalizeEstimate('about 25 hours', 'h', 'approx.')).toBe('approx. 25 h')
    expect(normalizeEstimate('about 25 hours', 'Std.', 'ca.')).toBe('ca. 25 Std.')
  })

  it('leaves everything else as written', () => {
    expect(normalizeEstimate('100+ Stunden', 'h', 'approx.')).toBe('100+ h')
    expect(normalizeEstimate('Mehrere Wochen', 'h', 'approx.')).toBe('Mehrere Wochen')
  })
})

describe('gameGenres', () => {
  const base = { genreTags: 'JRPG, Turn-Based, 100% Guide' } as GameSummaryDto

  it('prefers the genres of Steam', () => {
    expect(gameGenres({ ...base, steamGenres: ['Role-playing', ' Strategy '] })).toEqual(['Role-playing', 'Strategy'])
  })

  it('falls back to the AI tags without the "100% Guide" tag', () => {
    expect(gameGenres(base)).toEqual(['JRPG', 'Turn-Based'])
    expect(gameGenres({ ...base, genreTags: '100% Guide' })).toEqual([])
    expect(gameGenres({ ...base, steamGenres: [] })).toEqual(['JRPG', 'Turn-Based'])
  })
})

describe('missingFacts', () => {
  const full = {
    playtimeMinutes: 600,
    lastPlayedAt: '2026-10-30T10:00:00Z',
    reviewScoreDesc: 'Very Positive',
    releaseDate: '2024-01-01',
    completionHours: 40,
    sizeOnDiskBytes: 40 * 1024 ** 3,
  } as GameSummaryDto

  it('reports nothing for a game that has every fact', () => {
    expect(missingFacts(full)).toEqual({})
    expect(missingFacts({ ...full, completionHours: null, estimatedHours: 'ca. 40 Stunden' })).toEqual({})
  })

  it('says "not played" for the playtime facts of a game with zero playtime', () => {
    const missing = missingFacts({ ...full, playtimeMinutes: 0, lastPlayedAt: null })
    expect(missing.played).toBe('notPlayed')
    expect(missing.last).toBe('notPlayed')
    expect(missing.rating).toBeUndefined()
    // Steam may stamp a launch without playtime: still not "played"
    expect(missingFacts({ ...full, playtimeMinutes: 0 }).last).toBe('notPlayed')
  })

  it('blames Steam for data it did not deliver', () => {
    const missing = missingFacts({ ...full, playtimeMinutes: null, lastPlayedAt: null, reviewScoreDesc: null, releaseDate: null })
    expect(missing).toEqual({ played: 'steam', last: 'steam', rating: 'steam', release: 'steam' })
  })

  it('knows a size from the real size on disk or from the estimated install size, and blames missing sources otherwise', () => {
    expect(missingFacts({ ...full, sizeOnDiskBytes: null, installSizeBytes: 5 }).size).toBeUndefined()
    expect(missingFacts({ ...full, sizeOnDiskBytes: null, installSizeBytes: null }).size).toBe('size')
    expect(missingFacts({ ...full, sizeOnDiskBytes: 0, installSizeBytes: 0 }).size).toBe('size')
  })

  it('takes the release date or the rating of the library data when the card has none of its own', () => {
    expect(missingFacts({ ...full, releaseDate: null, releaseAt: '2020-01-01T00:00:00Z' }).release).toBeUndefined()
    expect(missingFacts({ ...full, reviewScoreDesc: null, sortReviewPercent: 90 }).rating).toBeUndefined()
    expect(missingFacts({ ...full, reviewScoreDesc: null, sortMetacritic: 80 }).rating).toBeUndefined()
  })

  it('counts a Metacritic or IGDB score as a rating', () => {
    expect(missingFacts({ ...full, reviewScoreDesc: null, metacriticScore: 88 }).rating).toBeUndefined()
    expect(missingFacts({ ...full, reviewScoreDesc: null, igdbRating: 77 }).rating).toBeUndefined()
    expect(missingFacts({ ...full, reviewScoreDesc: null }).rating).toBe('steam')
  })

  it('has no estimate without IGDB hours and with an open guide estimate', () => {
    expect(missingFacts({ ...full, completionHours: null, estimatedHours: 'Offen' }).hours).toBe('estimate')
    // English tiles store the English placeholder
    expect(missingFacts({ ...full, completionHours: null, estimatedHours: 'Open' }).hours).toBe('estimate')
    expect(missingFacts({ ...full, completionHours: null, estimatedHours: '  ' }).hours).toBe('estimate')
    expect(missingFacts({ ...full, completionHours: 0, estimatedHours: '' }).hours).toBe('estimate')
  })
})
