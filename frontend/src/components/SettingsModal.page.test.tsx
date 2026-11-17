import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { SettingsResponseDto, UpdateSettingsRequest } from '../api/types'
import { animationQueue } from '../utils/animationQueue'
import { SettingsModal } from './SettingsModal'

// The popups of the position tests must not play a sound in a test
vi.mock('../utils/audio', async (importOriginal) => {
  const original = await importOriginal<typeof import('../utils/audio')>()
  return {
    ...original,
    soundEngine: { setEnabled: vi.fn(), setVolume: vi.fn(), setTheme: vi.fn(), preview: vi.fn(), playTrophyUnlock: vi.fn(), playPlatinumFanfare: vi.fn() },
  }
})

const fetchSettings = vi.hoisted(() => vi.fn<() => Promise<SettingsResponseDto>>())
const updateSettings = vi.hoisted(() => vi.fn<(payload: UpdateSettingsRequest) => Promise<SettingsResponseDto>>())
vi.mock('../api/client', async (importOriginal) => {
  const original = await importOriginal<typeof import('../api/client')>()
  return {
    ...original,
    fetchSettings,
    updateSettings,
    fetchBridgeTokenStatus: vi.fn(() => Promise.resolve({ configured: false })),
  }
})

function settings(popupSeconds: number): SettingsResponseDto {
  return {
    geminiModel: 'gemini-3.8-flash',
    claudeModel: 'claude-sonnet-4-5',
    openAiModel: 'gpt-4o',
    steamUserId: '',
    autoDetectGame: true,
    playSounds: true,
    soundVolume: 0.15,
    soundTheme: 'kristall',
    popupPosition: 'top-right',
    popupTarget: 'both',
    popupSeconds,
    language: 'german',
    uiLanguage: 'de',
    autoTranslate: true,
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
}

function renderPage(onTabChange = vi.fn(), initialTab: 'live' | 'steam' | 'ki' | 'twitch' | 'system' = 'live') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <SettingsModal page open initialTab={initialTab} onClose={() => {}} onTabChange={onTabChange} />
    </QueryClientProvider>
  )
  return onTabChange
}

beforeEach(() => {
  fetchSettings.mockReset()
  updateSettings.mockReset()
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
  }))
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  animationQueue.clearQueue()
})

describe('Settings page (variant Kristall)', () => {
  it('has one connected tab bar in the order of the mockup and reports every change for the URL', async () => {
    fetchSettings.mockResolvedValue(settings(5))
    const onTabChange = renderPage()
    const bar = await screen.findByRole('tablist', { name: 'Einstellungsbereiche' })
    const tabs = Array.from(bar.querySelectorAll('[role="tab"]')).map((tab) => tab.textContent)
    expect(tabs).toEqual(['Steam', 'KI-Recherche', 'Live & Sound', 'Twitch & IGDB', 'PlatinVault Bridge', 'System'])
    expect(bar.classList.contains('pv-segmented')).toBe(true)
    expect(screen.getByRole('tab', { name: 'Live & Sound' }).getAttribute('aria-selected')).toBe('true')

    fireEvent.click(screen.getByRole('tab', { name: 'Twitch & IGDB' }))
    expect(onTabChange).toHaveBeenCalledWith('twitch')
    expect(screen.getByRole('tab', { name: 'Twitch & IGDB' }).getAttribute('aria-selected')).toBe('true')
  })

  it('shows on/off settings as red/green switches in hairline rows that toggle on one click', async () => {
    fetchSettings.mockResolvedValue(settings(5))
    renderPage()
    const sounds = await screen.findByRole('switch', { name: /Freischalt-Sounds/ })
    expect(sounds.getAttribute('aria-checked')).toBe('true')
    expect(sounds.closest('.pv-set-row')).toBeTruthy()
    fireEvent.click(sounds)
    expect(sounds.getAttribute('aria-checked')).toBe('false')
  })

  it('draws the popup position as a connected 3 x 3 grid of named squares', async () => {
    fetchSettings.mockResolvedValue(settings(5))
    renderPage()
    const grid = (await screen.findByRole('group', { name: 'Popup-Position' })) as HTMLElement
    expect(grid.querySelectorAll('button')).toHaveLength(9)
    expect(screen.getByRole('button', { name: 'Rechts oben' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByRole('button', { name: 'Links oben' }).getAttribute('aria-pressed')).toBe('false')
  })

  it('draws a popup silhouette in every cell at the spot of that cell (a mini screen, no checkbox squares)', async () => {
    fetchSettings.mockResolvedValue(settings(5))
    renderPage()
    const grid = (await screen.findByRole('group', { name: 'Popup-Position' })) as HTMLElement
    const spots = [...grid.querySelectorAll('button')].map((button) => {
      const shape = button.querySelector('.pv-set-pos__shape')
      expect(shape).toBeTruthy()
      expect(shape!.getAttribute('aria-hidden')).toBe('true')
      return shape!.getAttribute('data-pos')
    })
    expect(spots).toEqual([
      'top-left', 'top-center', 'top-right', 'center-left', 'center', 'center-right', 'bottom-left', 'bottom-center', 'bottom-right',
    ])
    expect(grid.querySelector('.pv-set-pos__dot')).toBeNull()
    // The selected spot is the one named by the saved setting; its silhouette sits in the pressed button
    expect(screen.getByRole('button', { name: 'Rechts oben' }).querySelector('[data-pos="top-right"]')).toBeTruthy()
  })

  it('confirms a save in a fixed slot of the header and keeps the user on the same tab', async () => {
    fetchSettings.mockResolvedValue(settings(5))
    updateSettings.mockResolvedValue(settings(5))
    renderPage()
    await screen.findByRole('switch', { name: /Freischalt-Sounds/ })
    fireEvent.click(screen.getByRole('button', { name: /Einstellungen speichern/ }))
    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1))
    expect(await screen.findByText('Einstellungen und API-Keys verschlüsselt gespeichert.')).toBeTruthy()
    // Saving never moves the user: still the same tab
    expect(screen.getByRole('tab', { name: 'Live & Sound' }).getAttribute('aria-selected')).toBe('true')
  })

  it('reports a failed save as headline with code, an arrow for the details and a copy button, laid over the page', async () => {
    fetchSettings.mockResolvedValue(settings(5))
    const failure = Object.assign(new Error('Die Client-ID enthält ungültige Zeichen.'), { status: 400 })
    updateSettings.mockRejectedValue(failure)
    renderPage()
    await screen.findByRole('switch', { name: /Freischalt-Sounds/ })
    fireEvent.click(screen.getByRole('button', { name: /Einstellungen speichern/ }))

    const banner = await screen.findByTestId('settings-error')
    expect(banner.textContent).toContain('Fehler beim Speichern.')
    expect(banner.textContent).toContain('HTTP 400')
    expect(banner.textContent).toContain('Die Client-ID enthält ungültige Zeichen.')
    expect(banner.querySelector('[data-testid="copy-error"]')).toBeTruthy()
    expect(banner.classList.contains('pv-set-banner')).toBe(true)
  })
})
