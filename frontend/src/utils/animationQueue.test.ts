import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { LiveAchievementItemDto } from '../api/types'
import { ACHIEVEMENT_VISIBLE_MS, animationQueue } from './animationQueue'

// The queue must not play real sounds in a test
vi.mock('./audio', () => ({
  soundEngine: { playTrophyUnlock: vi.fn(), playPlatinumFanfare: vi.fn() },
}))

const achievement = (apiName: string): LiveAchievementItemDto => ({
  apiName,
  displayName: apiName,
  description: '',
  originalDescription: null,
  isTranslated: false,
  iconUrl: '',
  iconGrayUrl: '',
  achieved: true,
  unlockTime: null,
  globalPercent: 3,
  hasGlobalPercent: true,
  rarityTier: 'Gold',
  rarityLabel: '',
  isHidden: false,
  userNote: '',
})

describe('animationQueue: trophy popups', () => {
  let shown: (string | null)[]
  let unsubscribe: () => void

  beforeEach(() => {
    vi.useFakeTimers()
    shown = []
    unsubscribe = animationQueue.subscribe((state) => {
      shown.push(state.currentAchievement?.apiName ?? null)
    })
  })

  afterEach(() => {
    animationQueue.clearQueue()
    unsubscribe()
    vi.useRealTimers()
  })

  const current = () => shown[shown.length - 1]

  it('shows a popup for the normal visible time, then pauses before the next one', async () => {
    animationQueue.enqueueAchievement(achievement('A'))
    animationQueue.enqueueAchievement(achievement('B'))
    expect(current()).toBe('A')

    await vi.advanceTimersByTimeAsync(ACHIEVEMENT_VISIBLE_MS - 1)
    expect(current()).toBe('A')
    await vi.advanceTimersByTimeAsync(1)
    expect(current()).toBeNull()

    await vi.advanceTimersByTimeAsync(1000)
    expect(current()).toBe('B')
  })

  it('dismissAchievement ends the popup at once and the queue goes on with the next one', async () => {
    animationQueue.enqueueAchievement(achievement('A'))
    animationQueue.enqueueAchievement(achievement('B'))
    await vi.advanceTimersByTimeAsync(1200)
    expect(current()).toBe('A')

    animationQueue.dismissAchievement()
    await vi.advanceTimersByTimeAsync(0)
    expect(current()).toBeNull()

    // Same pause as after a popup that ran out, so the exit animation can finish
    await vi.advanceTimersByTimeAsync(999)
    expect(current()).toBeNull()
    await vi.advanceTimersByTimeAsync(1)
    expect(current()).toBe('B')

    // B then runs its full time again, not the rest of A's
    await vi.advanceTimersByTimeAsync(ACHIEVEMENT_VISIBLE_MS - 1)
    expect(current()).toBe('B')
    await vi.advanceTimersByTimeAsync(1)
    expect(current()).toBeNull()
  })

  it('dismissAchievement does nothing when no popup is showing', async () => {
    animationQueue.dismissAchievement()
    animationQueue.enqueueAchievement(achievement('A'))
    await vi.advanceTimersByTimeAsync(10)
    expect(current()).toBe('A')
  })

  it('dismissing twice in a row does not skip the following popup', async () => {
    animationQueue.enqueueAchievement(achievement('A'))
    animationQueue.enqueueAchievement(achievement('B'))
    animationQueue.dismissAchievement()
    animationQueue.dismissAchievement()
    await vi.advanceTimersByTimeAsync(1000)
    expect(current()).toBe('B')
    await vi.advanceTimersByTimeAsync(2000)
    expect(current()).toBe('B')
  })

  it('a dismiss during the pause between two popups does not cut the next one short', async () => {
    animationQueue.enqueueAchievement(achievement('A'))
    animationQueue.enqueueAchievement(achievement('B'))
    animationQueue.dismissAchievement()
    await vi.advanceTimersByTimeAsync(500)
    animationQueue.dismissAchievement()
    await vi.advanceTimersByTimeAsync(500)
    expect(current()).toBe('B')
    await vi.advanceTimersByTimeAsync(ACHIEVEMENT_VISIBLE_MS - 1)
    expect(current()).toBe('B')
  })

  it('a preview replaces a running popup: the old one cannot be dismissed into the new one', async () => {
    animationQueue.enqueueAchievement(achievement('A'))
    await vi.advanceTimersByTimeAsync(100)
    animationQueue.clearQueue()
    animationQueue.enqueueAchievement(achievement('P'), { position: 'fullscreen' })
    expect(current()).toBe('P')

    // The abandoned wait of A runs out in the background and must not touch P
    await vi.advanceTimersByTimeAsync(ACHIEVEMENT_VISIBLE_MS - 200)
    expect(current()).toBe('P')
    await vi.advanceTimersByTimeAsync(200)
    expect(current()).toBeNull()
  })
})

describe('animationQueue: busy state', () => {
  afterEach(() => {
    animationQueue.clearQueue()
    vi.useRealTimers()
  })

  it('is busy from the first item until the last pause is over, then reports idle once', async () => {
    vi.useFakeTimers()
    const busy: boolean[] = []
    const unsubscribe = animationQueue.subscribe((state) => busy.push(state.busy))
    expect(busy).toEqual([false])
    animationQueue.enqueueAchievement(achievement('A'))
    expect(busy[busy.length - 1]).toBe(true)
    await vi.advanceTimersByTimeAsync(ACHIEVEMENT_VISIBLE_MS)
    expect(busy[busy.length - 1]).toBe(true)
    await vi.advanceTimersByTimeAsync(1000)
    expect(busy[busy.length - 1]).toBe(false)
    unsubscribe()
  })
})
