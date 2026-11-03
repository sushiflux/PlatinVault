import { cleanup, render, screen } from '@testing-library/react'
import { afterAll, afterEach, describe, expect, it } from 'vitest'
import { applyLanguage } from '../i18n'
import type { LiveAchievementItemDto, LiveGameAchievementsDto } from '../api/types'
import { TrophyCard } from './TrophyNotification'
import { TrophyTierPills } from './TrophyTierPills'

afterEach(cleanup)
afterAll(async () => {
  await applyLanguage('de')
})

const achievement: LiveAchievementItemDto = {
  apiName: 'X',
  displayName: 'Master of Fate',
  description: 'Finish the game.',
  originalDescription: null,
  isTranslated: false,
  iconUrl: '',
  iconGrayUrl: '',
  achieved: true,
  unlockTime: null,
  globalPercent: 3.4,
  hasGlobalPercent: true,
  rarityTier: 'Gold',
  rarityLabel: '',
  isHidden: false,
  userNote: '',
}

const data = {
  platinum: { achieved: 0, total: 1 },
  gold: { achieved: 3, total: 5 },
  silver: { achieved: 4, total: 9 },
  bronze: { achieved: 5, total: 9 },
  achievements: [],
  totalCount: 0,
  achievedCount: 0,
  remainingCount: 0,
  progressPercentage: 0,
  is100Percent: false,
  latestAchievement: null,
  syncedGuideItemsCount: 0,
  game: {},
} as unknown as LiveGameAchievementsDto

describe('live texts follow the language', () => {
  it('tier pills: Silber / Silver', async () => {
    await applyLanguage('de')
    render(<TrophyTierPills data={data} />)
    expect(screen.getByText(/Silber:/)).toBeTruthy()
    cleanup()
    await applyLanguage('en')
    render(<TrophyTierPills data={data} />)
    expect(screen.getByText(/Silver:/)).toBeTruthy()
    expect(screen.getByText(/Platinum:/)).toBeTruthy()
  })

  it('tier pills keep the tier name in the DOM (read out when the bar shows compact pills) next to the count', async () => {
    await applyLanguage('en')
    const { container } = render(<TrophyTierPills data={data} />)
    const pills = [...container.querySelectorAll('.stat-pill')]
    expect(pills.map((p) => p.querySelector('.pv-tier-name')?.textContent?.trim())).toEqual(['Platinum:', 'Gold:', 'Silver:', 'Bronze:'])
    // The order is fixed and every pill names its tier for the CSS that tints the compact icons
    expect(pills.map((p) => p.getAttribute('data-tier'))).toEqual(['Platinum', 'Gold', 'Silver', 'Bronze'])
    expect(pills.map((p) => p.querySelector('.pv-tier-text')?.textContent)).toEqual(['Platinum: 0/1', 'Gold: 3/5', 'Silver: 4/9', 'Bronze: 5/9'])
  })

  it('popup card: tier headline and the share in the language of the app', async () => {
    await applyLanguage('de')
    render(<TrophyCard achievement={achievement} animated={false} />)
    expect(screen.getByText('Gold-Trophäe')).toBeTruthy()
    // German writes "3,4 %" with a no-break space
    expect(screen.getByText(/^3,4\s%$/)).toBeTruthy()
    cleanup()
    await applyLanguage('en')
    render(<TrophyCard achievement={achievement} animated={false} />)
    expect(screen.getByText('Gold trophy')).toBeTruthy()
    expect(screen.getByText('3.4%')).toBeTruthy()
  })
})
