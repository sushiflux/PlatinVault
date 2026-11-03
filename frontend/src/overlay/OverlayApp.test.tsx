import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen } from '@testing-library/react'
import type { WebViewChannel } from '../utils/programWindow'
import { ACHIEVEMENT_VISIBLE_MS, animationQueue } from '../utils/animationQueue'
import { soundEngine } from '../utils/audio'
import { OVERLAY_IDLE_DELAY_MS, OverlayApp } from './OverlayApp'

// The overlay must never play a sound itself (the app window plays it once per unlock)
vi.mock('../utils/audio', () => ({
  soundEngine: { setEnabled: vi.fn(), playTrophyUnlock: vi.fn(), playPlatinumFanfare: vi.fn() },
}))

function fakeChannel() {
  const listeners = new Set<(event: { data: unknown }) => void>()
  const sent: unknown[] = []
  const channel: WebViewChannel = {
    postMessage: (message) => sent.push(message),
    addEventListener: (_type, listener) => listeners.add(listener),
    removeEventListener: (_type, listener) => listeners.delete(listener),
  }
  const send = (data: unknown) => listeners.forEach((listener) => listener({ data }))
  return { channel, sent, send }
}

const achievement = {
  apiName: 'ACH_1',
  displayName: 'Erster Sieg',
  description: 'Gewinne einen Kampf.',
  originalDescription: null,
  isTranslated: false,
  iconUrl: '',
  iconGrayUrl: '',
  achieved: true,
  unlockTime: null,
  globalPercent: 2.4,
  hasGlobalPercent: true,
  rarityTier: 'Gold',
  rarityLabel: '',
  rarityBand: 'ultraRare',
  isHidden: false,
  userNote: '',
}

describe('OverlayApp: the in-game overlay page', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    // jsdom has no matchMedia (the full-screen popup and motion ask for it)
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
    vi.useRealTimers()
  })

  it("says ready, shows the program's unlock with the app's card, reports busy and finally idle with the count", async () => {
    const fake = fakeChannel()
    render(<OverlayApp channel={fake.channel} />)
    expect(soundEngine.setEnabled).toHaveBeenCalledWith(false)
    expect(fake.sent).toEqual([{ pvOverlayState: 'ready', received: 0 }])

    await act(async () => {
      fake.send({ pvOverlay: { kind: 'achievement', achievement, gameName: 'Elden Ring', position: 'top-left', language: 'de' } })
      await vi.advanceTimersByTimeAsync(100)
    })
    expect(screen.getByText('Erster Sieg')).toBeTruthy()
    expect(fake.sent).toContainEqual({ pvOverlayState: 'busy', received: 1 })

    await act(async () => {
      await vi.advanceTimersByTimeAsync(ACHIEVEMENT_VISIBLE_MS + 1000 + OVERLAY_IDLE_DELAY_MS + 50)
    })
    expect(fake.sent[fake.sent.length - 1]).toEqual({ pvOverlayState: 'idle', received: 1 })
  })

  it('a new event during the idle pause keeps it busy (the program must not hide it)', async () => {
    const fake = fakeChannel()
    render(<OverlayApp channel={fake.channel} />)
    await act(async () => {
      fake.send({ pvOverlay: { kind: 'achievement', achievement, position: 'top-right' } })
      await vi.advanceTimersByTimeAsync(ACHIEVEMENT_VISIBLE_MS + 1000 + OVERLAY_IDLE_DELAY_MS - 100)
      fake.send({ pvOverlay: { kind: 'achievement', achievement: { ...achievement, apiName: 'ACH_2', displayName: 'Zweiter Sieg' } } })
      await vi.advanceTimersByTimeAsync(300)
    })
    expect(fake.sent.filter((m) => (m as { pvOverlayState: string }).pvOverlayState === 'idle')).toEqual([])
    expect(screen.getByText('Zweiter Sieg')).toBeTruthy()
  })

  it('ignores messages that are not overlay events', async () => {
    const fake = fakeChannel()
    render(<OverlayApp channel={fake.channel} />)
    await act(async () => {
      fake.send({ pvHost: { overlay: true } })
      await vi.advanceTimersByTimeAsync(100)
    })
    expect(fake.sent).toEqual([{ pvOverlayState: 'ready', received: 0 }])
  })
})
