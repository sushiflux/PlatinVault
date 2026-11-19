import { describe, expect, it } from 'vitest'
import type { IgdbPlatformDto } from '../../api/types'
import { MAX_COVER_BYTES, MAX_SEARCH_LENGTH, MAX_TITLE_LENGTH, coverProblem, igdbMarks, isMarked, parseSearchInput, startingChoice, titleProblem } from './addGameRules'

const platform = (name: string, abbreviation: string | null, maps: IgdbPlatformDto['maps']): IgdbPlatformDto => ({ name, abbreviation, maps })
const PS5 = platform('PlayStation 5', 'PS5', { platform: 'playstation', variant: 'ps5' })
const PS4 = platform('PlayStation 4', 'PS4', { platform: 'playstation', variant: 'ps4' })
const XBOX = platform('Xbox One', 'XONE', { platform: 'xbox', variant: null })
const SWITCH = platform('Nintendo Switch', 'Switch', { platform: 'nintendo', variant: null })
const PC = platform('PC (Microsoft Windows)', 'PC', null)

describe('titleProblem', () => {
  it('needs at least one character and at most what the server accepts', () => {
    expect(titleProblem('')).toEqual({ kind: 'required' })
    expect(titleProblem('   ')).toEqual({ kind: 'required' })
    expect(titleProblem('G')).toBeNull()
    expect(titleProblem('x'.repeat(MAX_TITLE_LENGTH))).toBeNull()
    expect(titleProblem('x'.repeat(MAX_TITLE_LENGTH + 1))).toEqual({ kind: 'tooLong', length: MAX_TITLE_LENGTH + 1, max: MAX_TITLE_LENGTH })
  })

  it('counts the trimmed text, like the server', () => {
    expect(titleProblem(`  ${'x'.repeat(MAX_TITLE_LENGTH)}  `)).toBeNull()
  })
})

describe('parseSearchInput', () => {
  it('recognises empty, a name, an IGDB number and an igdb.com/games link', () => {
    expect(parseSearchInput('  ')).toEqual({ kind: 'empty' })
    expect(parseSearchInput('Astro Bot')).toEqual({ kind: 'text', text: 'Astro Bot' })
    expect(parseSearchInput('90001')).toEqual({ kind: 'id', id: 90001 })
    expect(parseSearchInput('https://www.igdb.com/games/Astro-Bot')).toEqual({ kind: 'link', slug: 'astro-bot' })
    expect(parseSearchInput('igdb.com/games/hollow-knight?x=1')).toEqual({ kind: 'link', slug: 'hollow-knight' })
  })

  it('refuses an address that is not the address of an IGDB game, without asking the server', () => {
    expect(parseSearchInput('https://example.com/spiel/astro')).toEqual({ kind: 'badLink' })
    expect(parseSearchInput('https://www.igdb.com/platforms/ps5')).toEqual({ kind: 'badLink' })
    expect(parseSearchInput('www.example.com')).toEqual({ kind: 'badLink' })
  })

  it('flags a text over the server limit', () => {
    expect(parseSearchInput('a'.repeat(MAX_SEARCH_LENGTH))).toMatchObject({ kind: 'text' })
    expect(parseSearchInput('a'.repeat(MAX_SEARCH_LENGTH + 1))).toEqual({ kind: 'tooLong', length: MAX_SEARCH_LENGTH + 1, max: MAX_SEARCH_LENGTH })
  })

  it('treats a number that no IGDB id can be as plain text', () => {
    expect(parseSearchInput('0')).toEqual({ kind: 'text', text: '0' })
    expect(parseSearchInput('99999999999999999999')).toMatchObject({ kind: 'text' })
  })
})

describe('coverProblem', () => {
  it('accepts PNG, JPEG, WebP and GIF up to 5 MB (what the server accepts)', () => {
    for (const type of ['image/png', 'image/jpeg', 'image/jpg', 'image/webp', 'image/gif']) expect(coverProblem({ type, size: 1000 })).toBeNull()
    expect(coverProblem({ type: 'image/png', size: MAX_COVER_BYTES })).toBeNull()
  })

  it('refuses SVG, other types and files that are too large, with the reason', () => {
    expect(coverProblem({ type: 'image/svg+xml', size: 10 })).toEqual({ kind: 'type', type: 'image/svg+xml' })
    expect(coverProblem({ type: '', size: 10 })).toEqual({ kind: 'type', type: '' })
    expect(coverProblem({ type: 'image/png', size: MAX_COVER_BYTES + 1 })).toEqual({ kind: 'size', bytes: MAX_COVER_BYTES + 1, max: MAX_COVER_BYTES })
  })
})

describe('what IGDB lists for a game', () => {
  it('marks only the platforms IGDB maps to ours and remembers whether it lists PC', () => {
    const marks = igdbMarks([PC, XBOX, PS4, SWITCH])
    expect(marks.listsPc).toBe(true)
    expect(isMarked(marks, 'xbox')).toBe(true)
    expect(isMarked(marks, 'playstation')).toBe(true)
    expect(isMarked(marks, 'playstation', 'ps4')).toBe(true)
    expect(isMarked(marks, 'playstation', 'ps5')).toBe(false)
    expect(isMarked(marks, 'nintendo')).toBe(true)
  })

  it('marks no PC shop when IGDB only says PC', () => {
    const marks = igdbMarks([PC])
    expect(marks.choices).toEqual([])
    expect(marks.listsPc).toBe(true)
    for (const shop of ['steam', 'epic', 'gog', 'ubisoft', 'ea', 'battlenet'] as const) expect(isMarked(marks, shop)).toBe(false)
  })

  it('starts with a platform only when IGDB leads to exactly one of ours (and with the variant only when one is listed)', () => {
    expect(startingChoice(igdbMarks([PS5]))).toEqual({ platform: 'playstation', variant: 'ps5' })
    expect(startingChoice(igdbMarks([PS5, PS4]))).toEqual({ platform: 'playstation', variant: '' })
    expect(startingChoice(igdbMarks([SWITCH, PC]))).toEqual({ platform: 'nintendo', variant: '' })
    expect(startingChoice(igdbMarks([XBOX, SWITCH]))).toEqual({ platform: '', variant: '' })
    expect(startingChoice(igdbMarks([PC]))).toEqual({ platform: '', variant: '' })
    expect(startingChoice(igdbMarks(null))).toEqual({ platform: '', variant: '' })
  })
})
