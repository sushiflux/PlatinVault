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
    animationQueue.setPopupSeconds(5)
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

describe('animationQueue: the game and source of an event named by the hub', () => {
  let states: { gameName: string | null | undefined; source: string | null | undefined; achievement: string | null; platinumSource: string | null | undefined }[]
  let unsubscribe: () => void

  beforeEach(() => {
    vi.useFakeTimers()
    states = []
    unsubscribe = animationQueue.subscribe((state) =>
      states.push({ gameName: state.currentGameName, source: state.currentSource, achievement: state.currentAchievement?.apiName ?? null, platinumSource: state.currentPlatinum?.source })
    )
  })

  afterEach(() => {
    animationQueue.clearQueue()
    unsubscribe()
    vi.useRealTimers()
  })

  const last = () => states[states.length - 1]!

  it('carries the game name and the source of a trophy ticked by hand while its popup shows, and drops them with it', async () => {
    animationQueue.enqueueAchievement(achievement('A'), { gameName: 'Stellar Blade', source: 'manual' })
    expect(last()).toMatchObject({ achievement: 'A', gameName: 'Stellar Blade', source: 'manual' })

    await vi.advanceTimersByTimeAsync(ACHIEVEMENT_VISIBLE_MS)
    expect(last()).toMatchObject({ achievement: null, gameName: null, source: null })
  })

  it('knows no game and no source for a Steam unlock, and keeps the values of each item in a row', async () => {
    animationQueue.enqueueAchievement(achievement('A'), { gameName: 'Stellar Blade', source: 'manual' })
    animationQueue.enqueueAchievement(achievement('B'))
    await vi.advanceTimersByTimeAsync(ACHIEVEMENT_VISIBLE_MS + 1000)
    expect(last()).toMatchObject({ achievement: 'B', gameName: null, source: null })
  })

  it('marks the platinum moment of a game added by hand with its source', async () => {
    animationQueue.enqueuePlatinum('Stellar Blade', 48, 'achievements', 'manual')
    await vi.advanceTimersByTimeAsync(500)
    expect(last().platinumSource).toBe('manual')
    animationQueue.dismissPlatinum()
  })
})

describe('animationQueue: duration of the trophy popups (setting live.popup_seconds)', () => {
  let shown: (string | null)[]
  let visible: number[]
  let unsubscribe: () => void

  beforeEach(() => {
    vi.useFakeTimers()
    shown = []
    visible = []
    unsubscribe = animationQueue.subscribe((state) => {
      shown.push(state.currentAchievement?.apiName ?? null)
      visible.push(state.visibleMs)
    })
  })

  afterEach(() => {
    animationQueue.clearQueue()
    animationQueue.setPopupSeconds(5)
    unsubscribe()
    vi.useRealTimers()
  })

  const current = () => shown[shown.length - 1]

  it('keeps 5 seconds until the setting is known', () => {
    animationQueue.enqueueAchievement(achievement('A'))
    expect(visible[visible.length - 1]).toBe(5000)
  })

  it.each([2, 3, 10])('shows a popup for the chosen %i seconds, then the pause, then the next one for the same time', async (seconds) => {
    animationQueue.setPopupSeconds(seconds)
    animationQueue.enqueueAchievement(achievement('A'))
    animationQueue.enqueueAchievement(achievement('B'))
    expect(current()).toBe('A')
    expect(visible[visible.length - 1]).toBe(seconds * 1000)

    await vi.advanceTimersByTimeAsync(seconds * 1000 - 1)
    expect(current()).toBe('A')
    await vi.advanceTimersByTimeAsync(1)
    expect(current()).toBeNull()

    // Several unlocks in a row still play one after another, each for the chosen time
    await vi.advanceTimersByTimeAsync(1000)
    expect(current()).toBe('B')
    await vi.advanceTimersByTimeAsync(seconds * 1000 - 1)
    expect(current()).toBe('B')
    await vi.advanceTimersByTimeAsync(1)
    expect(current()).toBeNull()
  })

  it('uses the same time for the full-screen popup', async () => {
    animationQueue.setPopupSeconds(3)
    animationQueue.enqueueAchievement(achievement('F'), { position: 'fullscreen' })
    await vi.advanceTimersByTimeAsync(2999)
    expect(current()).toBe('F')
    await vi.advanceTimersByTimeAsync(1)
    expect(current()).toBeNull()
  })

  it('a click or Esc (dismissAchievement) still ends the popup early, whatever the chosen time', async () => {
    animationQueue.setPopupSeconds(10)
    animationQueue.enqueueAchievement(achievement('F'), { position: 'fullscreen' })
    animationQueue.enqueueAchievement(achievement('G'))
    await vi.advanceTimersByTimeAsync(500)
    animationQueue.dismissAchievement()
    await vi.advanceTimersByTimeAsync(0)
    expect(current()).toBeNull()
    await vi.advanceTimersByTimeAsync(1000)
    expect(current()).toBe('G')
  })

  it('a time given with the item (settings preview, overlay page) wins over the global one', async () => {
    animationQueue.setPopupSeconds(10)
    animationQueue.enqueueAchievement(achievement('A'), { seconds: 2 })
    expect(visible[visible.length - 1]).toBe(2000)
    await vi.advanceTimersByTimeAsync(2000)
    expect(current()).toBeNull()
  })

  it('clamps nonsense to the allowed 2..10 seconds', () => {
    animationQueue.setPopupSeconds(99)
    animationQueue.enqueueAchievement(achievement('A'))
    expect(visible[visible.length - 1]).toBe(10000)
    animationQueue.clearQueue()
    animationQueue.setPopupSeconds(0)
    animationQueue.enqueueAchievement(achievement('B'))
    expect(visible[visible.length - 1]).toBe(2000)
    animationQueue.clearQueue()
    animationQueue.setPopupSeconds(undefined)
    animationQueue.enqueueAchievement(achievement('C'))
    expect(visible[visible.length - 1]).toBe(5000)
  })

  it('a change applies to the next popup, not to the one that is showing', async () => {
    animationQueue.setPopupSeconds(4)
    animationQueue.enqueueAchievement(achievement('A'))
    animationQueue.enqueueAchievement(achievement('B'))
    await vi.advanceTimersByTimeAsync(1000)
    animationQueue.setPopupSeconds(8)
    expect(visible[visible.length - 1]).toBe(4000)
    await vi.advanceTimersByTimeAsync(3000)
    expect(current()).toBeNull()
    await vi.advanceTimersByTimeAsync(1000)
    expect(current()).toBe('B')
    expect(visible[visible.length - 1]).toBe(8000)
  })

  it('does not touch the platinum moment', async () => {
    animationQueue.setPopupSeconds(2)
    const platinum: (string | null)[] = []
    const stop = animationQueue.subscribe((state) => platinum.push(state.currentPlatinum?.gameName ?? null))
    animationQueue.enqueuePlatinum('Game', 10)
    await vi.advanceTimersByTimeAsync(500 + 13000)
    expect(platinum[platinum.length - 1]).toBe('Game')
    await vi.advanceTimersByTimeAsync(1100)
    expect(platinum[platinum.length - 1]).toBeNull()
    stop()
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
