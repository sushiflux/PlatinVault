import { act, cleanup, fireEvent, render } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { LiveAchievementItemDto } from '../api/types'
import { OverlayApp } from '../overlay/OverlayApp'
import { animationQueue } from '../utils/animationQueue'
import type { WebViewChannel } from '../utils/programWindow'
import { FullscreenAchievement } from './FullscreenAchievement'
import { PopupCountdown } from './PopupCountdown'
import { TrophyNotification } from './TrophyNotification'

// motion caches the reduced-motion preference once per page, so the tests switch it through this flag instead of matchMedia
const motionPrefs = vi.hoisted(() => ({ reduce: false }))
vi.mock('motion/react', async (importOriginal) => ({
  ...(await importOriginal<typeof import('motion/react')>()),
  useReducedMotion: () => motionPrefs.reduce,
}))

// The overlay page and the queue must not play real sounds in a test
vi.mock('../utils/audio', () => ({
  soundEngine: { setEnabled: vi.fn(), playTrophyUnlock: vi.fn(), playPlatinumFanfare: vi.fn() },
}))

const achievement: LiveAchievementItemDto = {
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

// jsdom has no matchMedia: the full-screen popup's orientation query asks for it
function stubMatchMedia() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
  }))
}

beforeEach(() => {
  vi.useFakeTimers()
  stubMatchMedia()
  motionPrefs.reduce = false
})
afterEach(() => {
  cleanup()
  animationQueue.clearQueue()
  animationQueue.setPopupSeconds(5)
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

const bar = (root: ParentNode) => root.querySelector<HTMLElement>('[data-popup-countdown-bar]')
const track = (root: ParentNode) => root.querySelector<HTMLElement>('[data-popup-countdown]')

describe('PopupCountdown', () => {
  it('runs smoothly and linearly for the given time, and is hidden from screen readers', () => {
    const { container } = render(<PopupCountdown durationMs={7000} color="#fbbf24" />)
    expect(track(container)!.getAttribute('aria-hidden')).toBe('true')
    expect(track(container)!.dataset.durationMs).toBe('7000')
    expect(bar(container)!.style.getPropertyValue('--pv-countdown')).toBe('7000ms')
    expect(bar(container)!.style.transformOrigin).toBe('0% 50%')
    // The smooth linear keyframe class is always active (no abrupt 1s step jumps)
    expect(bar(container)!.classList.contains('pv-countdown-bar')).toBe(true)
  })

  it('keeps the smooth linear glide even when reduced motion is active, without whole-second jumps', () => {
    motionPrefs.reduce = true
    const { container } = render(<PopupCountdown durationMs={4000} color="#fbbf24" />)
    expect(bar(container)!.classList.contains('pv-countdown-bar')).toBe(true)
    expect(bar(container)!.style.getPropertyValue('--pv-countdown')).toBe('4000ms')
  })

  it('starts a Web Animations API linear glide when element.animate is available and cancels on duration change', () => {
    const cancel = vi.fn()
    const animate = vi.fn(() => ({ cancel }) as unknown as Animation)
    HTMLElement.prototype.animate = animate
    try {
      const { rerender } = render(<PopupCountdown durationMs={4000} color="#fbbf24" />)
      expect(animate).toHaveBeenCalledWith(
        [{ transform: 'scaleX(1)' }, { transform: 'scaleX(0)' }],
        { duration: 4000, easing: 'linear', fill: 'both' }
      )
      rerender(<PopupCountdown durationMs={2000} color="#fbbf24" />)
      expect(cancel).toHaveBeenCalledTimes(1)
      expect(animate).toHaveBeenLastCalledWith(
        [{ transform: 'scaleX(1)' }, { transform: 'scaleX(0)' }],
        { duration: 2000, easing: 'linear', fill: 'both' }
      )
    } finally {
      // @ts-expect-error remove test stub
      delete HTMLElement.prototype.animate
    }
  })
})

describe('the countdown in the popups', () => {
  it('card: the bar takes the popup duration (default 5 s) and does not touch the card', () => {
    const { container } = render(<TrophyNotification achievement={achievement} />)
    expect(track(container)!.dataset.durationMs).toBe('5000')
    // Out of the flow: absolute, so it cannot change the size of the card
    expect(track(container)!.className).toContain('absolute')
  })

  it.each([2000, 8000, 10000])('card: follows the chosen duration %i ms', (ms) => {
    const { container } = render(<TrophyNotification achievement={achievement} visibleMs={ms} />)
    expect(track(container)!.dataset.durationMs).toBe(String(ms))
    expect(bar(container)!.style.getPropertyValue('--pv-countdown')).toBe(`${ms}ms`)
  })

  it('card: uses the colour of the tier', () => {
    const { container } = render(<TrophyNotification achievement={{ ...achievement, rarityTier: 'Bronze' }} />)
    expect(bar(container)!.style.background).toMatch(/#d97706|rgb\(217, 119, 6\)/)
  })

  it('full screen: bar along the bottom edge for the given time; none without a time; click still ends it early', () => {
    const onDismiss = vi.fn()
    const { container, rerender } = render(<FullscreenAchievement achievement={achievement} onDismiss={onDismiss} visibleMs={6000} />)
    expect(track(container)!.dataset.durationMs).toBe('6000')
    expect(track(container)!.style.insetBlockEnd).toMatch(/^0/)
    fireEvent.click(container.querySelector('[data-fullscreen-achievement]')!)
    expect(onDismiss).toHaveBeenCalledTimes(1)
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onDismiss).toHaveBeenCalledTimes(2)

    rerender(<FullscreenAchievement achievement={achievement} onDismiss={onDismiss} />)
    expect(track(container)).toBeNull()
  })

  it('the queue and the bar agree: a popup with its own seconds gets exactly that time, the global setting is not used', async () => {
    animationQueue.setPopupSeconds(9)
    let state = { visibleMs: 0, current: null as LiveAchievementItemDto | null }
    const unsubscribe = animationQueue.subscribe((next) => {
      state = { visibleMs: next.visibleMs, current: next.currentAchievement }
    })
    animationQueue.enqueueAchievement(achievement, { seconds: 3 })
    const { container } = render(<TrophyNotification achievement={state.current} visibleMs={state.visibleMs} />)
    expect(track(container)!.dataset.durationMs).toBe('3000')

    // ... and the queue really removes the popup after that time
    await vi.advanceTimersByTimeAsync(2999)
    expect(state.current).not.toBeNull()
    await vi.advanceTimersByTimeAsync(1)
    expect(state.current).toBeNull()
    unsubscribe()
  })

  it('overlay page: card and full screen both get the duration that arrived with the unlock', async () => {
    const listeners = new Set<(event: { data: unknown }) => void>()
    const channel: WebViewChannel = {
      postMessage: () => {},
      addEventListener: (_type, listener) => listeners.add(listener),
      removeEventListener: (_type, listener) => listeners.delete(listener),
    }
    const send = (data: unknown) => listeners.forEach((listener) => listener({ data }))
    const { container } = render(<OverlayApp channel={channel} />)

    await act(async () => {
      send({ pvOverlay: { kind: 'achievement', achievement, gameName: 'Elden Ring', position: 'top-left', language: 'de', seconds: 4 } })
      await vi.advanceTimersByTimeAsync(100)
    })
    expect(track(container)!.dataset.durationMs).toBe('4000')

    animationQueue.clearQueue()
    await act(async () => {
      send({ pvOverlay: { kind: 'achievement', achievement, gameName: 'Elden Ring', position: 'fullscreen', language: 'de', seconds: 8 } })
      await vi.advanceTimersByTimeAsync(100)
    })
    expect(track(container)!.dataset.durationMs).toBe('8000')
    expect(container.querySelectorAll('[data-popup-countdown]')).toHaveLength(1)
  })
})
