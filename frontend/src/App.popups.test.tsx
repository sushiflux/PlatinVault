import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { applyLanguage } from './i18n'
import type { LiveAchievementItemDto, SettingsResponseDto } from './api/types'
import type { LiveHubHandlers } from './utils/liveConnection'
import { animationQueue } from './utils/animationQueue'
import { GlobalLiveOverlayBridge } from './App'

// The popups of this window, fed by the live hub: a trophy ticked by hand in a game added by hand brings its own game name (that game is not running) and
// always shows here - the server never sends it to the Windows overlay, so "only in the game" must not hide it.

const client = vi.hoisted(() => ({ fetchSettings: vi.fn(), fetchLiveStatus: vi.fn(), fetchLiveEvents: vi.fn() }))
vi.mock('./api/client', async (importOriginal) => ({ ...(await importOriginal<typeof import('./api/client')>()), ...client }))
vi.mock('./utils/audio', () => ({
  DEFAULT_SOUND_THEME: 'default',
  soundEngine: { setEnabled: vi.fn(), setVolume: vi.fn(), setTheme: vi.fn(), playTrophyUnlock: vi.fn(), playPlatinumFanfare: vi.fn() },
}))
const hub = vi.hoisted(() => ({ handlers: null as null | LiveHubHandlers }))
vi.mock('./utils/liveConnection', () => ({
  connectLiveHub: (handlers: LiveHubHandlers) => {
    hub.handlers = handlers
    return () => {}
  },
}))
const program = vi.hoisted(() => ({ overlay: true }))
vi.mock('./utils/programWindow', () => ({ connectProgramWindow: () => () => {}, useProgramWindow: () => ({ overlay: program.overlay }) }))

const settings = (over: Partial<SettingsResponseDto> = {}) =>
  ({ popupPosition: 'fullscreen', popupTarget: 'overlay', playSounds: false, soundVolume: 0.1, soundTheme: 'default', popupSeconds: 5, ...over }) as SettingsResponseDto

const trophy = (over: Partial<LiveAchievementItemDto> = {}): LiveAchievementItemDto => ({
  apiName: 'first-blood',
  displayName: 'Erstes Blut',
  description: 'Besiege den ersten Boss.',
  originalDescription: null,
  isTranslated: false,
  iconUrl: '',
  iconGrayUrl: '',
  achieved: true,
  unlockTime: '2026-11-19T11:00:00Z',
  globalPercent: 0,
  hasGlobalPercent: false,
  rarityTier: 'Silver',
  rarityLabel: '',
  isHidden: false,
  userNote: '',
  ...over,
})

function renderBridge() {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <GlobalLiveOverlayBridge />
    </QueryClientProvider>
  )
}

beforeEach(async () => {
  await applyLanguage('de')
  vi.useFakeTimers({ shouldAdvanceTime: true })
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: query.includes('landscape'), media: query, addEventListener: () => {}, removeEventListener: () => {} }))
  hub.handlers = null
  program.overlay = true
  client.fetchSettings.mockReset().mockResolvedValue(settings())
  client.fetchLiveStatus.mockReset().mockResolvedValue({ runningGameName: 'Das laufende Spiel' })
})
afterEach(() => {
  animationQueue.clearQueue()
  cleanup()
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

const settle = () => act(async () => { await vi.advanceTimersByTimeAsync(50) })

describe('the popups of the window', () => {
  it('names the game of a trophy ticked by hand in the full-screen popup (shown here although the target says "overlay"), not the running game, and draws the glyph for its missing icon', async () => {
    renderBridge()
    await settle()
    act(() => hub.handlers!.onAchievement(trophy(), { gameName: 'Stellar Blade', source: 'manual' }))
    await settle()

    const popup = document.querySelector('[data-fullscreen-achievement]')
    expect(popup).toBeTruthy()
    expect(popup?.textContent).toContain('Stellar Blade')
    expect(popup?.textContent).toContain('Erstes Blut')
    expect(popup?.textContent).not.toContain('Das laufende Spiel')
    expect(popup?.querySelector('.pv-trophy-glyph[data-tier="Silver"]')).toBeTruthy()
    // The running game is not even asked for: the event names its game
    expect(client.fetchLiveStatus).not.toHaveBeenCalled()
  })

  it('still leaves the popup of a Steam unlock to the overlay when the setting says "only in the game"', async () => {
    renderBridge()
    await settle()
    act(() => hub.handlers!.onAchievement(trophy({ apiName: 'steam-one', displayName: 'Steam-Erfolg', iconUrl: '/icons/x.png' }), { gameName: null, source: null }))
    await settle()
    expect(document.querySelector('[data-fullscreen-achievement]')).toBeNull()
    expect(screen.queryByText('Steam-Erfolg')).toBeNull()
  })

  it('shows the card popup of a trophy ticked by hand with the tier glyph, whatever the target says', async () => {
    client.fetchSettings.mockResolvedValue(settings({ popupPosition: 'top-right', popupTarget: 'overlay' }))
    renderBridge()
    await settle()
    act(() => hub.handlers!.onAchievement(trophy({ displayName: 'Kettenreaktion' }), { gameName: 'Stellar Blade', source: 'manual' }))
    await settle()

    expect(screen.getByText('Kettenreaktion')).toBeTruthy()
    expect(document.querySelector('.pv-popup-card .pv-trophy-glyph[data-tier="Silver"]')).toBeTruthy()
    expect(document.querySelector('.pv-popup-card img.pv-popup-icon')).toBeNull()
  })

  it('shows the platinum moment of a game added by hand in this window although the target says "overlay"', async () => {
    renderBridge()
    await settle()
    act(() => hub.handlers!.onPlatinum('Stellar Blade', 48, 'manual'))
    await act(async () => { await vi.advanceTimersByTimeAsync(700) })
    expect(screen.getByText(/Stellar Blade/)).toBeTruthy()
  })

  it('does not show the platinum moment of a Steam game here when the target says "overlay"', async () => {
    renderBridge()
    await settle()
    act(() => hub.handlers!.onPlatinum('Stellar Blade', 48, null))
    await act(async () => { await vi.advanceTimersByTimeAsync(700) })
    expect(screen.queryByText(/Stellar Blade/)).toBeNull()
  })
})
