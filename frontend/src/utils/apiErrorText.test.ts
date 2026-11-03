import { describe, expect, it } from 'vitest'
import { applyLanguage } from '../i18n'
import { describeError, isNetworkFailure } from './apiErrorText'

describe('describeError', () => {
  it('moves a trailing provider text into the details', () => {
    const error = new Error(
      'Gemini-Limit erreicht (zu viele Anfragen in kurzer Zeit oder Tageslimit). Später erneut versuchen. (Google: You exceeded your current quota (limit: 0). See https://ai.google.dev/gemini-api/docs/rate-limits.)'
    )
    const result = describeError(error, 'unknown')
    expect(result.message).toBe('Gemini-Limit erreicht (zu viele Anfragen in kurzer Zeit oder Tageslimit). Später erneut versuchen.')
    expect(result.details).toBe('Google: You exceeded your current quota (limit: 0). See https://ai.google.dev/gemini-api/docs/rate-limits.')
  })

  it('also recognises the other providers', () => {
    expect(describeError(new Error('Key abgelehnt. (Anthropic: invalid x-api-key)'), 'x').details).toBe('Anthropic: invalid x-api-key')
    expect(describeError(new Error('Key abgelehnt. (OpenAI: Incorrect API key)'), 'x').details).toBe('OpenAI: Incorrect API key')
  })

  it('prefers the separate providerMessage of the server', () => {
    const error = Object.assign(new Error('Google lehnt den Key ab.'), { providerMessage: 'quota exceeded' })
    expect(describeError(error, 'x')).toEqual({ message: 'Google lehnt den Key ab.', details: 'quota exceeded' })
  })

  it('leaves an ordinary message alone and falls back when there is none', () => {
    expect(describeError(new Error('Nicht gefunden (HTTP 404).'), 'x')).toEqual({ message: 'Nicht gefunden (HTTP 404).', details: null })
    expect(describeError('boom', 'x')).toEqual({ message: 'boom', details: null })
    expect(describeError(undefined, 'unknown error')).toEqual({ message: 'unknown error', details: null })
    expect(describeError(new Error('   '), 'unknown error').message).toBe('unknown error')
  })

  it('does not strip a message that consists only of the provider part', () => {
    const result = describeError(new Error('(Google: nur Rohtext)'), 'x')
    expect(result).toEqual({ message: '(Google: nur Rohtext)', details: null })
  })
})

describe('describeError, unreachable server', () => {
  it('never shows the raw browser text of a failed request', async () => {
    await applyLanguage('de')
    for (const raw of ['Failed to fetch', 'NetworkError when attempting to fetch resource.', 'Load failed']) {
      const result = describeError(new TypeError(raw), 'unknown')
      expect(result.message).toContain('nicht erreichbar')
      expect(result.message).not.toContain(raw)
      expect(result.details).toBeNull()
    }
  })

  it('speaks English with the English interface', async () => {
    await applyLanguage('en')
    expect(describeError(new TypeError('Failed to fetch'), 'unknown').message).toContain('not reachable')
    await applyLanguage('de')
  })

  it('recognises only real network failures, other TypeErrors keep their own text', () => {
    expect(isNetworkFailure(new TypeError('Failed to fetch'))).toBe(true)
    expect(isNetworkFailure(new TypeError('x is not a function'))).toBe(false)
    expect(isNetworkFailure(new Error('Failed to fetch'))).toBe(false)
    expect(describeError(new TypeError('x is not a function'), 'unknown').message).toBe('x is not a function')
  })
})
