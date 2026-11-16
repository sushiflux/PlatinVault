import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { applyLanguage } from '../../i18n'
import type { PlayerGameStatsDto } from '../../api/types'
import { HeroTiles } from './HeroTiles'

const PLAYER = { playtimeHours: 12.5, playtime2WeeksHours: null, lastPlayed: null } as unknown as PlayerGameStatsDto

describe('HeroTiles', () => {
  beforeEach(async () => {
    await applyLanguage('de')
  })

  afterEach(() => cleanup())

  it('sets the figures as plain columns (no cards) with one column per figure', () => {
    const { container } = render(
      <HeroTiles unlocked={35} total={49} platinumDone={false} bound={null} projection={null} projectionPending={false} player={PLAYER} />
    )
    const row = container.querySelector('.pv-figs')
    expect(row?.getAttribute('data-count')).toBe('2')
    expect(row?.querySelectorAll('.pv-fig')).toHaveLength(2)
    expect(container.querySelector('.pv-card')).toBeNull()
    expect(screen.getByRole('progressbar')).toBeTruthy()
  })

  it('renders nothing without any known figure', () => {
    const { container } = render(
      <HeroTiles unlocked={null} total={null} platinumDone={false} bound={null} projection={null} projectionPending={false} player={null} />
    )
    expect(container.firstChild).toBeNull()
  })
})
