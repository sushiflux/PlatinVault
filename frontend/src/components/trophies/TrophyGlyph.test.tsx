import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { applyLanguage } from '../../i18n'
import { FullscreenAchievement } from '../FullscreenAchievement'
import { TrophyNotification } from '../TrophyNotification'
import { AchievementLookupProvider, AchievementRef } from '../guide/achievements'
import { manualItem, ps5Items } from './trophies.fixtures'
import { TrophyGlyph, trophyIconSrc } from './TrophyGlyph'

// A trophy of a game added by hand has no icon file (iconUrl = ""): every place that draws an icon draws the tier glyph instead.

beforeEach(async () => {
  await applyLanguage('de')
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query, addEventListener: () => {}, removeEventListener: () => {} }))
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('trophyIconSrc', () => {
  it('takes the grey icon while locked and the normal one once unlocked, "" when there is none', () => {
    expect(trophyIconSrc({ achieved: true, iconUrl: '/a.png', iconGrayUrl: '/a-gray.png' })).toBe('/a.png')
    expect(trophyIconSrc({ achieved: false, iconUrl: '/a.png', iconGrayUrl: '/a-gray.png' })).toBe('/a-gray.png')
    expect(trophyIconSrc({ achieved: false, iconUrl: '/a.png', iconGrayUrl: '' })).toBe('/a.png')
    expect(trophyIconSrc({ achieved: true, iconUrl: '', iconGrayUrl: '' })).toBe('')
  })
})

describe('TrophyGlyph', () => {
  it('is a decorative square of the given size with the tier cup, grey while locked, on a dark surface when asked', () => {
    const { container } = render(<TrophyGlyph tier="Gold" size={56} achieved={false} surface="dark" />)
    const glyph = container.querySelector('.pv-trophy-glyph') as HTMLElement
    expect(glyph.getAttribute('aria-hidden')).toBe('true')
    expect(glyph.getAttribute('data-tier')).toBe('Gold')
    expect(glyph.hasAttribute('data-locked')).toBe(true)
    expect(glyph.getAttribute('data-surface')).toBe('dark')
    expect(glyph.style.inlineSize).toBe('56px')
    expect(glyph.querySelector('.pv-tier-icon')).toBeTruthy()
  })
})

describe('the popups', () => {
  it('draws the glyph in the card popup when there is no icon, and the image when there is one', () => {
    const { container, rerender } = render(<TrophyNotification achievement={{ ...manualItem('a'), rarityTier: 'Bronze' }} />)
    expect(container.querySelector('.pv-popup-card .pv-trophy-glyph.pv-popup-icon[data-tier="Bronze"]')).toBeTruthy()
    expect(container.querySelector('img.pv-popup-icon')).toBeNull()

    rerender(<TrophyNotification achievement={{ ...manualItem('a'), iconUrl: '/icons/a.png' }} />)
    expect(container.querySelector('img.pv-popup-icon')?.getAttribute('src')).toBe('/icons/a.png')
    expect(container.querySelector('.pv-trophy-glyph')).toBeNull()
  })

  it('draws the glyph in the full-screen popup, with the game name that was given', () => {
    const { container } = render(<FullscreenAchievement achievement={{ ...manualItem('a'), displayName: 'Letzter Boss', rarityTier: 'Gold' }} gameName="Stellar Blade" onDismiss={() => {}} />)
    const popup = container.querySelector('[data-fullscreen-achievement]')!
    expect(popup.querySelector('.pv-trophy-glyph[data-surface="dark"][data-tier="Gold"]')).toBeTruthy()
    expect(popup.textContent).toContain('Stellar Blade')
  })

  it('keeps the image in the full-screen popup when the icon exists', () => {
    const { container } = render(<FullscreenAchievement achievement={{ ...manualItem('a'), iconUrl: '/icons/a.png' }} onDismiss={() => {}} />)
    expect(container.querySelector('img.pv-fsa-icon')).toBeTruthy()
    expect(container.querySelector('.pv-trophy-glyph')).toBeNull()
  })
})

describe('the guide with the trophies of a manual list', () => {
  it('shows an achievement hint with the title of the researched trophy, the trophy glyph for the missing icon, and "freigeschaltet" once ticked', () => {
    render(
      <AchievementLookupProvider achievements={ps5Items()}>
        <AchievementRef apiName="gold-1" withDescription />
        <AchievementRef apiName="silber-1" />
        <AchievementRef apiName="unbekannt" />
      </AchievementLookupProvider>
    )
    expect(screen.getByText('Letzter Boss')).toBeTruthy()
    expect(screen.getByText('Beschreibung gold-1')).toBeTruthy()
    expect(document.querySelectorAll('.pv-ach-ref[data-known="true"]')).toHaveLength(2)
    expect(screen.getAllByText('freigeschaltet')).toHaveLength(1)
    expect(screen.getByText('Erfolg unbekannt')).toBeTruthy()
    expect(document.querySelectorAll('img')).toHaveLength(0)
  })
})
