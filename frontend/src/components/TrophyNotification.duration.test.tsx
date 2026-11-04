import { act, cleanup, render } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { LiveAchievementItemDto } from '../api/types'
import { TrophyNotification } from './TrophyNotification'

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

beforeEach(() => {
  vi.useFakeTimers()
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
  vi.useRealTimers()
})

// The card is the first child of the container; its opacity is 1 while it is in and 0 while it slides out
const opacity = (container: HTMLElement) => (container.firstElementChild as HTMLElement).style.opacity

describe('TrophyNotification: the card slides out shortly before the queue removes it', () => {
  it('after 4.5 s of the default 5 s', async () => {
    const { container } = render(<TrophyNotification achievement={achievement} />)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100)
    })
    expect(opacity(container)).toBe('1')
    await act(async () => {
      await vi.advanceTimersByTimeAsync(4300)
    })
    expect(opacity(container)).toBe('1')
    await act(async () => {
      await vi.advanceTimersByTimeAsync(200)
    })
    expect(opacity(container)).toBe('0')
  })

  it.each([
    [2000, 1500],
    [8000, 7500],
    [10000, 9500],
  ])('after %i ms in total it starts to leave at %i ms', async (visibleMs, exitAt) => {
    const { container } = render(<TrophyNotification achievement={achievement} visibleMs={visibleMs} />)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(exitAt - 100)
    })
    expect(opacity(container)).toBe('1')
    await act(async () => {
      await vi.advanceTimersByTimeAsync(150)
    })
    expect(opacity(container)).toBe('0')
  })
})
