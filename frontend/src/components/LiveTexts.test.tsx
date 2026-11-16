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
  it('tier figures: Silber / Silver', async () => {
    await applyLanguage('de')
    render(<TrophyTierPills data={data} />)
    expect(screen.getByText('Silber')).toBeTruthy()
    cleanup()
    await applyLanguage('en')
    render(<TrophyTierPills data={data} />)
    expect(screen.getByText('Silver')).toBeTruthy()
    expect(screen.getByText('Platinum')).toBeTruthy()
  })

  it('tier figures show the tier name under the count, in a fixed order, and name their tier for the CSS that colours them', async () => {
    await applyLanguage('en')
    const { container } = render(<TrophyTierPills data={data} />)
    const figures = [...container.querySelectorAll('.pv-fig')]
    expect(figures.map((f) => f.querySelector('.pv-fig-name')?.textContent)).toEqual(['Platinum', 'Gold', 'Silver', 'Bronze'])
    expect(figures.map((f) => f.getAttribute('data-tier'))).toEqual(['Platinum', 'Gold', 'Silver', 'Bronze'])
    expect(figures.map((f) => f.querySelector('.pv-fig-count')?.textContent)).toEqual(['0 / 1', '3 / 5', '4 / 9', '5 / 9'])
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
