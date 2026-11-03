import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { applyLanguage, currentLanguage } from '../../i18n'
import { LanguageScreen } from './LanguageScreen'

function renderScreen() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <LanguageScreen />
    </QueryClientProvider>
  )
}

// Answers the two requests the screen makes: the product name and the settings PUT
function stubServer(put: (body: string) => Response) {
  const calls: { url: string; method: string; body: string }[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      calls.push({ url, method, body: String(init?.body ?? '') })
      if (url === '/api/branding') {
        return new Response(JSON.stringify({ productName: 'PlatinVault', tagline: '', company: 'SushiFlux', version: 'v1' }), { status: 200 })
      }
      return put(String(init?.body ?? ''))
    })
  )
  return calls
}

describe('LanguageScreen', () => {
  beforeEach(async () => {
    await applyLanguage('de')
    // A first visit: nothing cached in this browser yet
    localStorage.clear()
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('offers Deutsch and English as radio buttons and preselects the browser language', () => {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['en-GB'])
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-GB')
    stubServer(() => new Response('{}', { status: 200 }))
    renderScreen()

    const radios = screen.getAllByRole('radio') as HTMLInputElement[]
    expect(radios.map((radio) => radio.value)).toEqual(['de', 'en'])
    expect(radios.find((radio) => radio.checked)?.value).toBe('en')
    expect(screen.getByText('Deutsch')).toBeTruthy()
    expect(screen.getByText('English')).toBeTruthy()
  })

  it('applies the chosen language and saves it as ui.language', async () => {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['de-DE'])
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('de-DE')
    const calls = stubServer(() => new Response(JSON.stringify({ uiLanguage: 'en', language: 'english' }), { status: 200 }))
    renderScreen()

    fireEvent.click(screen.getByRole('radio', { name: /English/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }))

    await waitFor(() => expect(calls.some((call) => call.method === 'PUT')).toBe(true))
    const put = calls.find((call) => call.method === 'PUT')!
    expect(put.url).toBe('/api/settings')
    expect(JSON.parse(put.body)).toEqual({ uiLanguage: 'en' })
    expect(currentLanguage()).toBe('en')
    expect(document.documentElement.lang).toBe('en')
    expect(localStorage.getItem('pv_ui_language_v1')).toBe('en')
  })

  it('keeps the screen, names the problem and goes back to the previous language when saving fails', async () => {
    vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['de-DE'])
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('de-DE')
    stubServer(() => new Response(JSON.stringify({ title: 'Einstellung ungültig', detail: 'Datenbank nicht erreichbar' }), { status: 400 }))
    renderScreen()

    fireEvent.click(screen.getByRole('radio', { name: /English/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }))

    const alert = await screen.findByText(/could not be saved: Datenbank nicht erreichbar/)
    expect(alert).toBeTruthy()
    expect(currentLanguage()).toBe('de')
  })
})
