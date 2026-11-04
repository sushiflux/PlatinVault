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
    betaUpdatesEnabled: false,
  }
}

function renderModal() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SettingsModal open initialTab="live" onClose={() => {}} />
    </QueryClientProvider>
  )
}

const slider = async () => (await screen.findByRole('slider', { name: 'Anzeigedauer der Trophäen-Popups' })) as HTMLInputElement

beforeEach(() => {
  fetchSettings.mockReset()
  updateSettings.mockReset()
  // jsdom has no matchMedia (the popups ask for it)
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

describe('Settings > Live & Sound: Anzeigedauer der Trophäen-Popups', () => {
  it('shows the stored duration, bounded to 2..10 seconds in whole steps, with an explanation', async () => {
    fetchSettings.mockResolvedValue(settings(7))
    renderModal()
    const input = await slider()
    await waitFor(() => expect(input.value).toBe('7'))
    expect(input.min).toBe('2')
    expect(input.max).toBe('10')
    expect(input.step).toBe('1')
    expect(input.getAttribute('aria-valuetext')).toBe('7 s')
    expect(screen.getByText('7 s')).toBeTruthy()
    expect(screen.getByText(/2–10 Sekunden/)).toBeTruthy()
    // No native tooltip anywhere on the control
    expect(input.getAttribute('title')).toBeNull()
  })

  it('shows 5 s for a server that sends no value yet', async () => {
    const old = settings(5) as Partial<SettingsResponseDto>
    delete old.popupSeconds
    fetchSettings.mockResolvedValue(old as SettingsResponseDto)
    renderModal()
    const input = await slider()
    await waitFor(() => expect(input.value).toBe('5'))
  })

  it('changes by one second per step with the keyboard, stops at 2 and at 10, and never resizes the value', async () => {
    fetchSettings.mockResolvedValue(settings(3))
    renderModal()
    const input = await slider()
    await waitFor(() => expect(input.value).toBe('3'))
    const value = document.querySelector('[data-popup-seconds-value]') as HTMLElement
    expect(value.className).toContain('min-w-')

    fireEvent.change(input, { target: { value: '2' } })
    expect(screen.getByText('2 s')).toBeTruthy()
    // A value below the bound is pulled back to 2, one above to 10
    fireEvent.change(input, { target: { value: '1' } })
    expect(input.value).toBe('2')
    fireEvent.change(input, { target: { value: '10' } })
    expect(screen.getByText('10 s')).toBeTruthy()
    fireEvent.change(input, { target: { value: '11' } })
    expect(input.value).toBe('10')
  })

  it('saves the chosen duration with the other settings', async () => {
    fetchSettings.mockResolvedValue(settings(5))
    updateSettings.mockImplementation((payload) => Promise.resolve(settings(payload.popupSeconds ?? 5)))
    renderModal()
    const input = await slider()
    await waitFor(() => expect(input.value).toBe('5'))
    fireEvent.change(input, { target: { value: '8' } })

    fireEvent.click(await screen.findByRole('button', { name: /speichern/i }))

    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1))
    expect(updateSettings.mock.calls[0]![0].popupSeconds).toBe(8)
  })

  it('shows a failed save as a complete message with a copy button', async () => {
    fetchSettings.mockResolvedValue(settings(5))
    updateSettings.mockRejectedValue(new Error('Anzeigedauer der Trophäen-Popups "11" ist ungültig – erlaubt sind ganze Sekunden von 2 bis 10.'))
    renderModal()
    const input = await slider()
    await waitFor(() => expect(input.value).toBe('5'))
    fireEvent.click(await screen.findByRole('button', { name: /speichern/i }))

    expect(await screen.findByText(/erlaubt sind ganze Sekunden von 2 bis 10/)).toBeTruthy()
    expect(screen.getByRole('button', { name: /kopieren/i })).toBeTruthy()
  })

  it('the position test popup uses the chosen (not yet saved) duration', async () => {
    fetchSettings.mockResolvedValue(settings(5))
    renderModal()
    const input = await slider()
    await waitFor(() => expect(input.value).toBe('5'))
    fireEvent.change(input, { target: { value: '9' } })

    const seen: number[] = []
    const stop = animationQueue.subscribe((state) => {
      if (state.currentAchievement) seen.push(state.visibleMs)
    })
    fireEvent.click(screen.getByRole('button', { name: 'Links oben' }))
    stop()
    expect(seen[seen.length - 1]).toBe(9000)
  })
})
