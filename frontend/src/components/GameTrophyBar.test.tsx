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
    render(<GameTrophyBar data={data} hasTrophies />)
    expect(screen.getByText(/38 \/ 51 Trophäen/)).toBeTruthy()
    const line = screen.getByRole('progressbar', { name: 'Trophäen-Fortschritt' })
    expect(line.getAttribute('aria-valuenow')).toBe('75')
    expect(line.hasAttribute('data-complete')).toBe(false)
    expect([...document.querySelectorAll('.pv-fig-count')].map((node) => node.textContent)).toEqual(['0 / 1', '2 / 10', '14 / 16', '22 / 25'])
  })

  it('marks a finished game (the line turns gold)', () => {
    render(<GameTrophyBar data={{ ...data, progressPercentage: 100 }} hasTrophies />)
    expect(screen.getByRole('progressbar').hasAttribute('data-complete')).toBe(true)
  })

  it('is left out without a Steam link and keeps its room while the numbers load', () => {
    const { container, rerender } = render(<GameTrophyBar data={undefined} hasTrophies={false} />)
    expect(container.firstChild).toBeNull()
    rerender(<GameTrophyBar data={undefined} hasTrophies />)
    expect(container.querySelector('.pv-gbar-sum')).toBeTruthy()
    expect(container.querySelectorAll('.pv-fig')).toHaveLength(4)
  })

  it('refreshes the Steam data through its button, which exists only when the page gives an action', () => {
    const refresh = vi.fn()
    const { rerender } = render(<GameTrophyBar data={data} hasTrophies onRefresh={refresh} />)
    fireEvent.click(screen.getByRole('button', { name: 'Steam-Erfolge jetzt aktualisieren' }))
    expect(refresh).toHaveBeenCalledTimes(1)
    rerender(<GameTrophyBar data={data} hasTrophies />)
    expect(screen.queryByRole('button', { name: 'Steam-Erfolge jetzt aktualisieren' })).toBeNull()
  })
})

describe('the trophy part for a game added by hand', () => {
  const list = {
    platinum: { achieved: 0, total: 1 },
    gold: { achieved: 0, total: 4 },
    silver: { achieved: 4, total: 13 },
    bronze: { achieved: 8, total: 30 },
    totalCount: 48,
    achievedCount: 12,
    progressPercentage: 25,
  }

  it('shows the grades of a PlayStation list with their real totals', () => {
    render(<GameTrophyBar data={list} hasTrophies figures="grades" kind="trophies" />)
    expect(screen.getByText('12 / 48 Trophäen (25 %)')).toBeTruthy()
    expect([...document.querySelectorAll('.pv-fig-count')].map((node) => node.textContent)).toEqual(['0 / 1', '0 / 4', '4 / 13', '8 / 30'])
  })

  it('leaves the platinum figure out for a list that has no platinum trophy', () => {
    render(<GameTrophyBar data={{ ...list, platinum: { achieved: 0, total: 0 } }} hasTrophies figures="grades" kind="trophies" />)
    expect([...document.querySelectorAll('.pv-fig-name')].map((node) => node.textContent)).toEqual(['Gold', 'Silber', 'Bronze'])
  })

  it('shows one total figure and the word "Erfolge" on a platform without grades', () => {
    render(<GameTrophyBar data={list} hasTrophies figures="total" kind="achievements" />)
    expect(screen.getByText('12 / 48 Erfolge (25 %)')).toBeTruthy()
    expect([...document.querySelectorAll('.pv-fig-count')].map((node) => node.textContent)).toEqual(['12 / 48'])
    expect(document.querySelector('.pv-fig-name')?.textContent).toBe('Erfolge')
  })

  it('says there is no list yet instead of "0 of 0", with a dash in every figure', () => {
    render(<GameTrophyBar data={{ ...list, totalCount: 0, achievedCount: 0, progressPercentage: 0, gold: { achieved: 0, total: 0 }, silver: { achieved: 0, total: 0 }, bronze: { achieved: 0, total: 0 }, platinum: { achieved: 0, total: 0 } }} hasTrophies figures="grades" researched={false} />)
    expect(screen.getByText('Noch keine Trophäenliste')).toBeTruthy()
    expect([...document.querySelectorAll('.pv-fig-count')].map((node) => node.textContent)).toEqual(['–', '–', '–', '–'])
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
