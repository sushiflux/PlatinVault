import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterAll, afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { applyLanguage } from '../i18n'
import type { LiveAchievementItemDto } from '../api/types'
import { FullscreenAchievement } from './FullscreenAchievement'

// jsdom has no matchMedia: portrait or landscape is all the component asks
beforeEach(() => {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query.includes('portrait'),
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})
afterAll(async () => {
  await applyLanguage('de')
})

const achievement = (extra: Partial<LiveAchievementItemDto> = {}): LiveAchievementItemDto => ({
  apiName: 'X',
  displayName: 'Master of Fate',
  description: 'Finish the game on the hardest setting.',
  originalDescription: null,
  isTranslated: false,
  iconUrl: '',
  iconGrayUrl: '',
  achieved: true,
  unlockTime: null,
  globalPercent: 3.1,
  hasGlobalPercent: true,
  rarityTier: 'Gold',
  rarityLabel: '',
  rarityBand: 'ultraRare',
  isHidden: false,
  userNote: '',
  ...extra,
})

describe('FullscreenAchievement', () => {
  it('shows nothing (but keeps its empty announcement region) without an achievement', () => {
    const { container } = render(<FullscreenAchievement achievement={null} onDismiss={() => {}} />)
    expect(container.querySelector('[data-fullscreen-achievement]')).toBeNull()
    expect(container.querySelector('[role="status"][aria-live="polite"]')?.textContent).toBe('')
  })

  it('shows name, description, tier, rarity step with share and the game, in English', async () => {
    await applyLanguage('en')
    const { container } = render(<FullscreenAchievement achievement={achievement()} gameName="Example Game" onDismiss={() => {}} />)
    const stage = within(container.querySelector<HTMLElement>('[data-fullscreen-achievement]')!)
    expect(screen.getByRole('heading', { name: 'Master of Fate' })).toBeTruthy()
    expect(screen.getByText('Finish the game on the hardest setting.')).toBeTruthy()
    expect(screen.getByText('Gold trophy unlocked')).toBeTruthy()
    expect(stage.getByText(/Ultra rare · 3\.1\s?% of players/)).toBeTruthy()
    expect(screen.getByText('Example Game')).toBeTruthy()
  })

  it('speaks German in the German UI', async () => {
    await applyLanguage('de')
    const { container } = render(<FullscreenAchievement achievement={achievement({ rarityTier: 'Silver' })} onDismiss={() => {}} />)
    const stage = within(container.querySelector<HTMLElement>('[data-fullscreen-achievement]')!)
    expect(stage.getByText('Silber-Trophäe freigeschaltet')).toBeTruthy()
    expect(stage.getByText(/Extrem selten · 3,1\s?% der Spieler/)).toBeTruthy()
  })

  it('leaves out the rarity line when Steam gave no worldwide share, and the empty description and game', async () => {
    await applyLanguage('en')
    const { container } = render(
      <FullscreenAchievement achievement={achievement({ hasGlobalPercent: false, globalPercent: 25, description: '' })} onDismiss={() => {}} />
    )
    expect(screen.queryByText(/players/)).toBeNull()
    expect(container.querySelector('[data-fullscreen-achievement] p:not(:last-child)')).toBeNull()
  })

  it('announces the achievement through the live region as plain sentences', async () => {
    await applyLanguage('en')
    const { container } = render(<FullscreenAchievement achievement={achievement()} onDismiss={() => {}} />)
    const live = container.querySelector('[role="status"][aria-live="polite"]')?.textContent ?? ''
    expect(live).toContain('Gold trophy unlocked.')
    expect(live).toContain('Master of Fate.')
    expect(live).toContain('Finish the game on the hardest setting.')
    expect(live).not.toContain('..')
  })

  it('a click anywhere dismisses', async () => {
    await applyLanguage('en')
    const onDismiss = vi.fn()
    const { container } = render(<FullscreenAchievement achievement={achievement()} onDismiss={onDismiss} />)
    fireEvent.click(container.querySelector('[data-fullscreen-achievement]')!)
    expect(onDismiss).toHaveBeenCalledTimes(1)
  })

  it('Esc dismisses before other listeners see it, and takes no focus', async () => {
    await applyLanguage('en')
    const onDismiss = vi.fn()
    const bubbling = vi.fn()
    document.addEventListener('keydown', bubbling)
    const input = document.createElement('input')
    document.body.appendChild(input)
    input.focus()
    render(<FullscreenAchievement achievement={achievement()} onDismiss={onDismiss} />)
    act(() => {
      fireEvent.keyDown(input, { key: 'Escape' })
    })
    expect(onDismiss).toHaveBeenCalledTimes(1)
    expect(bubbling).not.toHaveBeenCalled()
    expect(document.activeElement).toBe(input)
    // Other keys are left alone
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(onDismiss).toHaveBeenCalledTimes(1)
    document.removeEventListener('keydown', bubbling)
    input.remove()
  })

  it('does not listen for Esc while nothing is shown', () => {
    const onDismiss = vi.fn()
    render(<FullscreenAchievement achievement={null} onDismiss={onDismiss} />)
    fireEvent.keyDown(document.body, { key: 'Escape' })
    expect(onDismiss).not.toHaveBeenCalled()
  })

  it('uses the tier colours by data attribute (unknown tiers fall back to gold)', async () => {
    const { container } = render(
      <FullscreenAchievement achievement={achievement({ rarityTier: 'Mythic' as unknown as 'Gold' })} onDismiss={() => {}} />
    )
    expect(container.querySelector('[data-fullscreen-achievement]')?.getAttribute('data-tier')).toBe('Gold')
  })
})
