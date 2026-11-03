import { afterEach, describe, expect, it, vi } from 'vitest'
import { detectBrowserLanguage, parseUiLanguage } from './index'
import { formatBytes, formatDate, formatNumber, formatPercent, formatRelativeTime, localeFor } from './format'

const de = { locale: localeFor('de'), timeZone: 'UTC' }
const en = { locale: localeFor('en'), timeZone: 'UTC' }
const moment = '2026-10-31T14:05:33Z'

describe('locales', () => {
  it('uses de-DE for German and en-GB (day first, 24 hours) for English', () => {
    expect(localeFor('de')).toBe('de-DE')
    expect(localeFor('en')).toBe('en-GB')
  })
})

describe('formatDate', () => {
  it('writes the date in the language of the app', () => {
    expect(formatDate(moment, 'date', de)).toBe('31.10.2026')
    expect(formatDate(moment, 'date', en)).toBe('31/10/2026')
    expect(formatDate(moment, 'dateLong', de)).toBe('31. Oktober 2026')
    expect(formatDate(moment, 'dateLong', en)).toBe('31 October 2026')
  })

  it('uses 24 hours in both languages', () => {
    expect(formatDate('2026-10-31T15:07:09Z', 'time', de)).toBe('15:07')
    expect(formatDate('2026-10-31T15:07:09Z', 'time', en)).toBe('15:07')
    expect(formatDate('2026-10-31T15:07:09Z', 'timeSeconds', en)).toBe('15:07:09')
    expect(formatDate(moment, 'dateTimeSeconds', de)).toBe('31.10.2026, 14:05:33')
    expect(formatDate(moment, 'dateTimeSeconds', en)).toBe('31/10/2026, 14:05:33')
  })

  it('gives an empty text for missing input and returns unreadable text unchanged', () => {
    expect(formatDate(null, 'date', de)).toBe('')
    expect(formatDate(undefined, 'date', de)).toBe('')
    expect(formatDate('not a date', 'date', de)).toBe('not a date')
  })
})

describe('formatNumber and formatPercent', () => {
  it('groups and separates numbers per language', () => {
    expect(formatNumber(1234.5, 1, de)).toBe('1.234,5')
    expect(formatNumber(1234.5, 1, en)).toBe('1,234.5')
    expect(formatNumber(54210, 0, de)).toBe('54.210')
    expect(formatNumber(54210, 0, en)).toBe('54,210')
  })

  it('writes percentages with a no-break space in German and without in English', () => {
    expect(formatPercent(45, 0, de)).toBe('45 %')
    expect(formatPercent(45, 0, en)).toBe('45%')
    expect(formatPercent(3.14, 1, de)).toBe('3,1 %')
    expect(formatPercent(3.14, 1, en)).toBe('3.1%')
  })
})

describe('formatRelativeTime', () => {
  const now = Date.parse('2026-10-31T12:00:00Z')

  it('says hours and days in the language of the app', () => {
    expect(formatRelativeTime(now - 3 * 3600_000, now, { locale: localeFor('de') })).toBe('vor 3 Stunden')
    expect(formatRelativeTime(now - 3 * 3600_000, now, { locale: localeFor('en') })).toBe('3 hours ago')
    expect(formatRelativeTime(now - 24 * 3600_000, now, { locale: localeFor('de') })).toBe('gestern')
    expect(formatRelativeTime(now - 24 * 3600_000, now, { locale: localeFor('en') })).toBe('yesterday')
    expect(formatRelativeTime(now + 2 * 24 * 3600_000, now, { locale: localeFor('en') })).toBe('in 2 days')
  })
})

describe('language detection', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  const browserLanguages = (languages: string[], language = languages[0] ?? '') => {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(languages)
    vi.spyOn(navigator, 'language', 'get').mockReturnValue(language)
  }

  it('takes the first browser language we speak', () => {
    browserLanguages(['de-AT', 'en'])
    expect(detectBrowserLanguage()).toBe('de')
    browserLanguages(['fr-FR', 'de'])
    expect(detectBrowserLanguage()).toBe('de')
    browserLanguages(['en-US'])
    expect(detectBrowserLanguage()).toBe('en')
  })

  it('falls back to English for any other language', () => {
    browserLanguages(['fr-FR', 'es'])
    expect(detectBrowserLanguage()).toBe('en')
  })

  it('accepts only the two supported languages', () => {
    expect(parseUiLanguage('de')).toBe('de')
    expect(parseUiLanguage('en')).toBe('en')
    expect(parseUiLanguage('fr')).toBeNull()
    expect(parseUiLanguage('german')).toBeNull()
    expect(parseUiLanguage(null)).toBeNull()
  })
})

describe('formatBytes', () => {
  const de = { locale: localeFor('de') }
  const en = { locale: localeFor('en') }
  const GB = 1024 ** 3

  it('writes sizes like Steam: steps of 1024 labelled GB, with the decimal sign of the language', () => {
    expect(formatBytes(48.3 * GB, de)).toBe('48,3 GB')
    expect(formatBytes(48.3 * GB, en)).toBe('48.3 GB')
    // 12436299617 bytes is the real SizeOnDisk of a game; Steam shows 11.58 GB for it
    expect(formatBytes(12436299617, en)).toBe('11.6 GB')
  })

  it('switches the unit with the size', () => {
    expect(formatBytes(512 * 1024 ** 2, en)).toBe('512 MB')
    expect(formatBytes(900 * 1024, en)).toBe('900 KB')
    expect(formatBytes(2.5 * 1024 ** 4, en)).toBe('2.5 TB')
  })
})
