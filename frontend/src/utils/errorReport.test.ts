import { describe, expect, it } from 'vitest'
import { buildErrorReport, errorCodeOf, formatReportTime, getKnownAppVersion, rememberAppVersion } from './errorReport'

// 2026-11-03 14:05:12 in the time zone the tests run in
const at = new Date(2026, 10, 3, 14, 5, 12)

describe('formatReportTime', () => {
  it('writes the local time with its offset, the same in every language', () => {
    expect(formatReportTime(at)).toMatch(/^2026-11-03 14:05:12 [+-]\d{2}:\d{2}$/)
  })
})

describe('buildErrorReport', () => {
  it('puts the complete message first, then details, code, and one line with version and time', () => {
    const text = buildErrorReport(
      { title: 'Guide konnte nicht vervollständigt werden', message: 'Google lehnt den Gemini-Key ab.', details: 'Google: API key not valid. Please pass a valid API key.', code: 'HTTP 400 · gemini · invalidKey' },
      'v2026.11.14 (13.557)',
      at,
    )
    const [head, details, code, stamp, ...rest] = text.split('\n\n')
    expect(rest).toEqual([])
    expect(head).toBe('Guide konnte nicht vervollständigt werden\nGoogle lehnt den Gemini-Key ab.')
    expect(details).toBe('Technische Details:\nGoogle: API key not valid. Please pass a valid API key.')
    expect(code).toBe('Fehlercode: HTTP 400 · gemini · invalidKey')
    expect(stamp).toMatch(/^PlatinVault v2026\.11\.14 \(13\.557\) · 2026-11-03 14:05:12 [+-]\d{2}:\d{2}$/)
  })

  it('never shortens a long message or its details', () => {
    const message = 'Die Datenbank konnte nicht eingerichtet werden. '.repeat(200).trim()
    const details = 'SQLite Error 14: unable to open database file. '.repeat(300).trim()
    const text = buildErrorReport({ message, details }, null, at)
    expect(text).toContain(message)
    expect(text).toContain(details)
    expect(text).not.toContain('…')
  })

  it('leaves out what is empty, and writes only the time when the version is not known', () => {
    const text = buildErrorReport({ message: 'Nur eine Meldung', details: '  ', code: null }, null, at)
    expect(text.split('\n\n')).toHaveLength(2)
    expect(text.startsWith('Nur eine Meldung\n\nPlatinVault · 2026-11-03 ')).toBe(true)
    expect(text).not.toContain('Technische Details')
    expect(text).not.toContain('Fehlercode')
  })

  it('does not repeat the title when the message already starts with it', () => {
    const text = buildErrorReport({ title: 'Speichern fehlgeschlagen', message: 'Speichern fehlgeschlagen: Name bereits vergeben' }, null, at)
    expect(text.split('\n\n')[0]).toBe('Speichern fehlgeschlagen: Name bereits vergeben')
  })

  it('uses the version the server reported when none is given', () => {
    rememberAppVersion('v2026.11.14 (13.557 β)')
    expect(getKnownAppVersion()).toBe('v2026.11.14 (13.557 β)')
    expect(buildErrorReport({ message: 'x' }, undefined, at)).toContain('PlatinVault v2026.11.14 (13.557 β) · ')
    rememberAppVersion(null)
    expect(getKnownAppVersion()).toBeNull()
  })
})

describe('errorCodeOf', () => {
  it('names the HTTP status and, for AI failures, the provider and the kind', () => {
    expect(errorCodeOf({ status: 502 })).toBe('HTTP 502')
    expect(errorCodeOf({ status: 429, aiProvider: 'gemini', aiFailure: 'quota' })).toBe('HTTP 429 · gemini · quota')
  })

  it('has no code for a failure without a response (status 0) or something that is no error object', () => {
    expect(errorCodeOf({ status: 0 })).toBeNull()
    expect(errorCodeOf(new Error('x'))).toBeNull()
    expect(errorCodeOf(null)).toBeNull()
    expect(errorCodeOf('text')).toBeNull()
  })
})
