import { cleanup, render } from '@testing-library/react'
import { afterAll, afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { LiveAchievementItemDto } from '../api/types'
import { applyLanguage } from '../i18n'
import { FullscreenAchievement } from './FullscreenAchievement'
import { TrophyCard } from './TrophyNotification'

beforeEach(() => {
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
})
afterAll(async () => {
  await applyLanguage('de')
})

// A local time on purpose: the expected text is the same in every time zone of the machine that runs the test
const LOCAL_UNLOCK = new Date(2026, 10, 4, 23, 41).toISOString()

const achievement = (extra: Partial<LiveAchievementItemDto> = {}): LiveAchievementItemDto => ({
  apiName: 'ACH_1',
  displayName: 'Erster Sieg',
  description: 'Gewinne einen Kampf.',
  originalDescription: null,
  isTranslated: false,
  iconUrl: '',
  iconGrayUrl: '',
  achieved: true,
  unlockTime: LOCAL_UNLOCK,
  globalPercent: 2.4,
  hasGlobalPercent: true,
  rarityTier: 'Gold',
  rarityLabel: '',
  rarityBand: 'ultraRare',
  isHidden: false,
  userNote: '',
  ...extra,
})

const stampOf = (root: ParentNode) => root.querySelector('[data-unlock-stamp]')

describe('unlock date and time in the trophy popups', () => {
  it('card, German: 04.11.2026, 23:41', async () => {
    await applyLanguage('de')
    const { container } = render(<TrophyCard achievement={achievement()} animated={false} />)
    expect(stampOf(container)!.textContent).toBe('04.11.2026, 23:41')
  })

  it('card, English: 04/11/2026, 23:41', async () => {
    await applyLanguage('en')
    const { container } = render(<TrophyCard achievement={achievement()} animated={false} />)
    expect(stampOf(container)!.textContent).toBe('04/11/2026, 23:41')
  })

  it.each([null, '', 'not a date', '1970-01-01T00:00:00Z'])('card: without a usable time (%s) the line stays, empty, with its height', async (unlockTime) => {
    await applyLanguage('de')
    const { container } = render(<TrophyCard achievement={achievement({ unlockTime })} animated={false} />)
    const slot = stampOf(container)!
    expect(slot.textContent).toBe('')
    expect(slot.querySelector('svg')).toBeNull()
    // The fixed height class is what keeps the card the same size with and without a time
    expect(slot.className).toContain('h-4')
  })

  it('card: same slot (same height class) with and without a time', async () => {
    await applyLanguage('de')
    const withTime = render(<TrophyCard achievement={achievement()} animated={false} />)
    const without = render(<TrophyCard achievement={achievement({ unlockTime: null })} animated={false} />)
    expect(stampOf(withTime.container)!.className).toBe(stampOf(without.container)!.className)
  })

  it('full screen: shows the stamp in the UI language and puts it into the screen reader announcement', async () => {
    await applyLanguage('en')
    const { container } = render(<FullscreenAchievement achievement={achievement()} onDismiss={() => {}} />)
    expect(stampOf(container)!.textContent).toBe('04/11/2026, 23:41')
    expect(container.querySelector('[role="status"]')!.textContent).toContain('Unlocked on 04/11/2026, 23:41.')

    cleanup()
    await applyLanguage('de')
    const german = render(<FullscreenAchievement achievement={achievement()} onDismiss={() => {}} />)
    expect(stampOf(german.container)!.textContent).toBe('04.11.2026, 23:41')
    expect(german.container.querySelector('[role="status"]')!.textContent).toContain('Freigeschaltet am 04.11.2026, 23:41.')
  })

  it('full screen: no line without a time', async () => {
    await applyLanguage('de')
    const { container } = render(<FullscreenAchievement achievement={achievement({ unlockTime: null })} onDismiss={() => {}} />)
    expect(stampOf(container)).toBeNull()
  })
})
