import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { applyLanguage } from '../i18n'
import { describeError } from '../utils/apiErrorText'
import { ApiError, fetchGames, fetchSettings, updateSettings } from './client'

// The browser throws a bare TypeError when the server cannot be reached at all. The API client turns it into an ApiError (status 0) with
// a localized sentence, so no screen ever shows "Failed to fetch".

const RAW_TEXTS = ['Failed to fetch', 'NetworkError when attempting to fetch resource.', 'Load failed', 'fetch failed']

beforeEach(async () => {
  await applyLanguage('de')
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('API client, server not reachable', () => {
  it.each(RAW_TEXTS)('replaces the browser text "%s" with a readable German sentence', async (raw) => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError(raw)))
    const error = await fetchGames().catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    const apiError = error as ApiError
    expect(apiError.status).toBe(0)
    expect(apiError.message).toContain('nicht erreichbar')
    expect(apiError.message).toContain('Backend')
    expect(apiError.message).not.toContain(raw)
  })

  it('speaks English when the app is English', async () => {
    await applyLanguage('en')
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))
    const error = (await fetchSettings().catch((e: unknown) => e)) as ApiError
    expect(error.message).toContain('not reachable')
    expect(error.message).not.toContain('Failed to fetch')
  })

  it('also covers requests that write', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))
    await expect(updateSettings({ uiLanguage: 'en' })).rejects.toMatchObject({ name: 'ApiError', status: 0 })
  })

  it('lets an aborted request through unchanged', async () => {
    const abort = new DOMException('The operation was aborted.', 'AbortError')
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(abort))
    await expect(fetchGames()).rejects.toBe(abort)
  })

  it('keeps the server answer of a real HTTP error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ detail: 'Titel bereits vergeben' }), { status: 400 })))
    await expect(fetchGames()).rejects.toMatchObject({ message: 'Titel bereits vergeben', status: 400 })
  })

  it('describeError shows the same sentence for the error the client threw', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))
    const error = await fetchGames().catch((e: unknown) => e)
    const text = describeError(error, 'fallback')
    expect(text.message).toContain('nicht erreichbar')
    expect(text.details).toBeNull()
  })
  it('does not show a parser error when a success answer is not JSON', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('<html><body>Login</body></html>', { status: 200, headers: { 'content-type': 'text/html' } })))
    const error = (await fetchGames().catch((e: unknown) => e)) as ApiError
    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(200)
    expect(error.message).toContain('Unerwartete Antwort')
    expect(error.message).not.toMatch(/Unexpected token|JSON/)
  })

  it('turns a connection that breaks while the body arrives into the same sentence', async () => {
    const broken = { ok: true, status: 200, json: () => Promise.reject(new TypeError('network error')) }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(broken))
    const error = (await fetchGames().catch((e: unknown) => e)) as ApiError
    expect(error.status).toBe(0)
    expect(error.message).toContain('nicht erreichbar')
  })
})
