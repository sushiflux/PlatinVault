import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { applyLanguage } from '../i18n'
import type { LiveGameAchievementsDto } from '../api/types'
import { GameTrophyBar } from './GameTrophyBar'
import { TROPHY_DENSITIES, TrophySizeControl, type TrophyDensity } from './TrophyViews'

beforeAll(async () => {
  await applyLanguage('de')
})
afterEach(cleanup)

const data = {
  platinum: { achieved: 0, total: 1 },
  gold: { achieved: 2, total: 10 },
  silver: { achieved: 14, total: 16 },
  bronze: { achieved: 22, total: 25 },
  achievements: [],
  totalCount: 51,
  achievedCount: 38,
  remainingCount: 13,
  progressPercentage: 74.5,
  is100Percent: false,
  latestAchievement: null,
  syncedGuideItemsCount: 0,
  game: {},
} as unknown as LiveGameAchievementsDto

describe('the trophy part of the game head', () => {
  it('says the progress in one sentence over a thin line and shows the four tiers', () => {
    render(<GameTrophyBar data={data} hasSteam />)
    expect(screen.getByText(/38 \/ 51 Trophäen/)).toBeTruthy()
    const line = screen.getByRole('progressbar', { name: 'Trophäen-Fortschritt' })
    expect(line.getAttribute('aria-valuenow')).toBe('75')
    expect(line.hasAttribute('data-complete')).toBe(false)
    expect([...document.querySelectorAll('.pv-fig-count')].map((node) => node.textContent)).toEqual(['0 / 1', '2 / 10', '14 / 16', '22 / 25'])
  })

  it('marks a finished game (the line turns gold)', () => {
    render(<GameTrophyBar data={{ ...data, progressPercentage: 100 }} hasSteam />)
    expect(screen.getByRole('progressbar').hasAttribute('data-complete')).toBe(true)
  })

  it('is left out without a Steam link and keeps its room while the numbers load', () => {
    const { container, rerender } = render(<GameTrophyBar data={undefined} hasSteam={false} />)
    expect(container.firstChild).toBeNull()
    rerender(<GameTrophyBar data={undefined} hasSteam />)
    expect(container.querySelector('.pv-gbar-sum')).toBeTruthy()
    expect(container.querySelectorAll('.pv-fig')).toHaveLength(4)
  })

  it('refreshes the Steam data through its button, which exists only when the page gives an action', () => {
    const refresh = vi.fn()
    const { rerender } = render(<GameTrophyBar data={data} hasSteam onRefresh={refresh} />)
    fireEvent.click(screen.getByRole('button', { name: 'Steam-Erfolge jetzt aktualisieren' }))
    expect(refresh).toHaveBeenCalledTimes(1)
    rerender(<GameTrophyBar data={data} hasSteam />)
    expect(screen.queryByRole('button', { name: 'Steam-Erfolge jetzt aktualisieren' })).toBeNull()
  })
})

describe('the view size choice', () => {
  it('is one connected segmented bar with the four sizes; the chosen one is pressed and a click reports the new one', () => {
    const chosen: TrophyDensity[] = []
    render(<TrophySizeControl value={2} onChange={(value) => chosen.push(value)} />)
    const group = screen.getByRole('group', { name: 'Ansicht' })
    expect(group.classList.contains('pv-segmented')).toBe(true)
    const buttons = [...group.querySelectorAll('button')]
    expect(buttons.map((button) => button.textContent)).toEqual(['Liste', 'Kacheln', 'Kompakt', 'Groß'])
    expect(buttons.map((button) => button.getAttribute('aria-pressed'))).toEqual(['false', 'false', 'true', 'false'])
    fireEvent.click(buttons[0]!)
    fireEvent.click(buttons[3]!)
    expect(chosen).toEqual([TROPHY_DENSITIES[0]!.value, TROPHY_DENSITIES[3]!.value])
  })
})
