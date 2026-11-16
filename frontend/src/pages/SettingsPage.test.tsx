import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ConfirmProvider } from '../components/ui/ConfirmDialog'
import { SettingsPage } from './SettingsPage'

// The settings page: the result of "Sign in with Steam" (?steamLink=ok|error&reason=...) is shown on the Steam tab and the parameters are
// cleaned from the address, so a reload does not show it again.

const SETTINGS = {
  geminiModel: 'gemini-3.8-flash',
  claudeModel: 'claude',
  openAiModel: 'gpt',
  steamUserId: '',
  autoDetectGame: true,
  playSounds: true,
  soundVolume: 0.5,
  soundTheme: 'classic',
  popupPosition: 'bottom-right',
  popupTarget: 'both',
  popupSeconds: 5,
  language: 'german',
  uiLanguage: 'de',
  autoTranslate: false,
  hasGeminiKey: false,
  geminiKeyHint: '',
  hasClaudeKey: false,
  claudeKeyHint: '',
  hasOpenAiKey: false,
  openAiKeyHint: '',
  hasSteamKey: false,
  steamKeyHint: '',
  hasTwitchKey: false,
  twitchKeyHint: '',
  twitchStreamsEnabled: true,
  achievementNotesEnabled: true,
  aiSelectionMode: 'default',
  aiDefaultProvider: null,
  aiAutoFallback: true,
  researchBlocks: { scope: null, focus: [], spoilers: null, presentation: null },
  researchBlocksPolicy: { scope: [], focus: [], spoilers: [], presentation: [] },
  autoSwitchGameEnabled: true,
  updateChannel: 'none',
}

function LocationProbe() {
  const location = useLocation()
  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>
}

function renderPage(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <MemoryRouter initialEntries={[path]}>
          <Routes>
            <Route
              path="/settings"
              element={
                <>
                  <SettingsPage />
                  <LocationProbe />
                </>
              }
            />
          </Routes>
        </MemoryRouter>
      </ConfirmProvider>
    </QueryClientProvider>
  )
}

beforeEach(() => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string) => {
      const path = String(input).split('?')[0] ?? ''
      if (path === '/api/settings') return new Response(JSON.stringify(SETTINGS), { status: 200, headers: { 'Content-Type': 'application/json' } })
      return new Response('{}', { status: 404, headers: { 'Content-Type': 'application/json' } })
    })
  )
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('SettingsPage', () => {
  it('shows the failed Steam sign-in with its reason on the Steam tab and cleans the address', async () => {
    renderPage('/settings?steamLink=error&reason=denied')
    const banner = await screen.findByTestId('settings-error')
    expect(banner.textContent).toContain('denied')
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/settings?tab=steam'))
    expect(screen.getByRole('tab', { name: 'Steam' }).getAttribute('aria-selected')).toBe('true')
  })

  it('confirms a successful Steam sign-in in the reserved status line', async () => {
    renderPage('/settings?steamLink=ok')
    expect(await screen.findByText(/Steam-Konto verknüpft/)).toBeTruthy()
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/settings?tab=steam'))
  })

  it('keeps the tab named in the address', async () => {
    renderPage('/settings?tab=live')
    await waitFor(() => expect(screen.getByRole('tab', { name: /Live/ }).getAttribute('aria-selected')).toBe('true'))
  })
})
