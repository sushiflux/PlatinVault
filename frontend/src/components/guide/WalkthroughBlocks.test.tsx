import { cleanup, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import type { GuideSectionDto, LiveAchievementItemDto } from '../../api/types'
import { AchievementLookupProvider } from './achievements'
import { BossCard } from './BossCard'
import { HintAlert, isGuideAlertKind } from './GuideBlocks'
import { OverviewBlock } from './OverviewBlock'
import { safeWebUrl, SourcesBlock } from './SourcesBlock'

afterEach(cleanup)

const achievement = (over: Partial<LiveAchievementItemDto> = {}): LiveAchievementItemDto => ({
  apiName: 'ACH_VOLCANO',
  displayName: 'Vulkanbezwinger',
  description: 'Besiege den Magmawächter.',
  originalDescription: null,
  isTranslated: false,
  iconUrl: 'https://cdn.example.test/volcano.jpg',
  iconGrayUrl: 'https://cdn.example.test/volcano-gray.jpg',
  achieved: false,
  unlockTime: null,
  globalPercent: 12,
  hasGlobalPercent: true,
  rarityTier: 'Gold',
  rarityLabel: 'Selten',
  isHidden: false,
  userNote: '',
  ...over,
})

const fullBoss: GuideSectionDto = {
  type: 'boss',
  bossId: 'magma-warden',
  title: 'Magmawächter',
  location: 'Aschenkrater, Tiefe Kammer',
  recommendedLevel: 32,
  hp: [
    { value: '12.500', appliesTo: 'Normal', source: 'Community-Wiki' },
    { value: '18.000', appliesTo: 'Schwer' },
  ],
  weaknesses: ['Eis', 'Wasser'],
  resistances: ['Feuer'],
  immunities: ['Gift'],
  attacks: [{ name: 'Lavawelle', note: 'weicht nach hinten aus' }, { name: 'Erdstoß' }],
  phases: ['Bis 50 % Leben: Nahkampf', 'Danach: ruft Adds'],
  party: 'Drei Heiler und ein Tank',
  tactics: ['Zuerst die Adds ausschalten.', 'Eis-Zauber für die Schwäche sparen.'],
  rewards: ['Magmakern', 'Feuerring'],
  missable: true,
  achievements: ['ACH_VOLCANO'],
  source: 'Offizielles Handbuch',
}

describe('BossCard', () => {
  it('shows every block of a fully documented enemy with its label', () => {
    render(
      <AchievementLookupProvider achievements={[achievement()]}>
        <BossCard section={fullBoss} />
      </AchievementLookupProvider>
    )
    const card = screen.getByRole('article', { name: 'Magmawächter' })
    expect(card.textContent).toContain('Aschenkrater, Tiefe Kammer')
    expect(card.textContent).toContain('Empfohlene Stufe')
    expect(card.textContent).toContain('32')
    expect(within(card).getByText('12.500')).toBeTruthy()
    expect(within(card).getByText('Normal')).toBeTruthy()
    expect(within(card).getByText('Quelle: Community-Wiki')).toBeTruthy()
    expect(within(card).getByText('Verpassbar')).toBeTruthy()
    // The three affinity groups each have their label (the meaning is never colour alone)
    for (const [group, label, entries] of [
      ['weak', 'Schwächen', ['Eis', 'Wasser']],
      ['resist', 'Resistenzen', ['Feuer']],
      ['immune', 'Immunitäten', ['Gift']],
    ] as const) {
      const block = card.querySelector(`[data-group="${group}"]`) as HTMLElement
      expect(within(block).getByRole('heading', { name: label })).toBeTruthy()
      for (const entry of entries) expect(within(block).getByText(entry)).toBeTruthy()
    }
    expect(within(card).getByText('Lavawelle')).toBeTruthy()
    expect(card.textContent).toContain('weicht nach hinten aus')
    expect(card.querySelectorAll('[data-field="phases"] li')).toHaveLength(2)
    expect(card.querySelector('[data-field="party"]')?.textContent).toContain('Drei Heiler und ein Tank')
    expect(card.querySelectorAll('[data-field="tactics"] ol > li')).toHaveLength(2)
    expect(within(card).getByText('Feuerring')).toBeTruthy()
    // The linked achievement is shown with its title from the game's Steam list
    expect(within(card).getByText('Vulkanbezwinger')).toBeTruthy()
    expect(card.textContent).toContain('Quelle: Offizielles Handbuch')
  })

  it('stays compact for a sparse entry: hit points say "nicht dokumentiert", nothing else is drawn empty', () => {
    render(<BossCard section={{ type: 'boss', bossId: 'x', title: 'Schleim' }} />)
    const card = screen.getByRole('article', { name: 'Schleim' })
    expect(card.querySelector('[data-field="hp"]')?.textContent).toContain('nicht dokumentiert')
    for (const field of ['location', 'level', 'missable', 'attacks', 'phases', 'party', 'tactics', 'rewards', 'achievements']) {
      expect(card.querySelector(`[data-field="${field}"]`)).toBeNull()
    }
    for (const group of ['weak', 'resist', 'immune']) expect(card.querySelector(`[data-group="${group}"]`)).toBeNull()
    // No heading without content below it: only the hit-point label remains
    expect(card.querySelectorAll('h4')).toHaveLength(2) // the name and the "Lebenspunkte" label
  })

  it('ignores blank list entries and keeps a group out when only blanks are left', () => {
    render(<BossCard section={{ type: 'boss', title: 'Golem', weaknesses: ['  ', ''], tactics: [' '], hp: [{ value: ' ' }] }} />)
    const card = screen.getByRole('article', { name: 'Golem' })
    expect(card.querySelector('[data-group="weak"]')).toBeNull()
    expect(card.querySelector('[data-field="tactics"]')).toBeNull()
    expect(card.textContent).toContain('nicht dokumentiert')
  })

  it('names an enemy without a title instead of leaving the header empty', () => {
    render(<BossCard section={{ type: 'boss', title: null }} />)
    expect(screen.getByRole('article', { name: 'Gegner' })).toBeTruthy()
  })

  it('shows the API name of an achievement the Steam list does not know', () => {
    render(<BossCard section={{ type: 'boss', title: 'Golem', achievements: ['ACH_UNKNOWN'] }} />)
    expect(screen.getByText('Steam-Erfolg ACH_UNKNOWN')).toBeTruthy()
  })
})

describe('OverviewBlock', () => {
  it('draws only the groups that have content', () => {
    const { container } = render(
      <AchievementLookupProvider achievements={[achievement()]}>
        <OverviewBlock
          section={{
            type: 'overview',
            recommendedLevel: '20–24',
            counts: [{ label: 'Truhen', count: 7 }, { label: 'Nebenaufgaben', count: 3 }],
            achievements: ['ACH_VOLCANO'],
            keyItems: ['Rostiger Schlüssel'],
            missables: ['Händler im Dorf'],
            endsAtPointOfNoReturn: true,
          }}
        />
      </AchievementLookupProvider>
    )
    expect(screen.getByRole('complementary', { name: 'Auf einen Blick' })).toBeTruthy()
    expect(container.querySelector('[data-field="level"]')?.textContent).toContain('20–24')
    expect(container.querySelectorAll('[data-field="count"]')).toHaveLength(2)
    expect(screen.getByText('Rostiger Schlüssel')).toBeTruthy()
    expect(screen.getByText('Händler im Dorf')).toBeTruthy()
    expect(screen.getByText('Vulkanbezwinger')).toBeTruthy()
    expect(container.querySelector('[data-field="ponr"]')?.textContent).toContain('Point of no Return')
  })

  it('uses its own title and leaves out the groups without content', () => {
    const { container } = render(<OverviewBlock section={{ type: 'overview', title: 'Kurzüberblick', counts: [{ label: 'Bosse', count: 2 }] }} />)
    expect(screen.getByRole('complementary', { name: 'Kurzüberblick' })).toBeTruthy()
    expect(container.querySelector('[data-field="keyItems"]')).toBeNull()
    expect(container.querySelector('[data-field="missables"]')).toBeNull()
    expect(container.querySelector('[data-field="achievements"]')).toBeNull()
    expect(container.querySelector('[data-field="ponr"]')).toBeNull()
  })

  it('draws nothing at all when it has nothing to say', () => {
    const { container } = render(<OverviewBlock section={{ type: 'overview', counts: [], keyItems: [' '] }} />)
    expect(container.firstChild).toBeNull()
  })
})

describe('HintAlert kinds', () => {
  it('accepts the two new kinds and rejects unknown ones', () => {
    expect(isGuideAlertKind('achievement')).toBe(true)
    expect(isGuideAlertKind('point_of_no_return')).toBe(true)
    expect(isGuideAlertKind('tip')).toBe(true)
    expect(isGuideAlertKind('nonsense')).toBe(false)
    expect(isGuideAlertKind(undefined)).toBe(false)
  })

  it('shows an achievement hint with the title and icon of the Steam achievement', () => {
    const { container } = render(
      <AchievementLookupProvider achievements={[achievement()]}>
        <HintAlert section={{ type: 'alert', kind: 'achievement', apiName: 'ACH_VOLCANO', htmlContent: 'Nur mit Eis-Zauber.' }} />
      </AchievementLookupProvider>
    )
    const hint = container.querySelector('[data-hint-kind="achievement"]') as HTMLElement
    expect(within(hint).getByText('Erfolg hier möglich')).toBeTruthy()
    expect(within(hint).getByText('Vulkanbezwinger')).toBeTruthy()
    expect(within(hint).getByText('Besiege den Magmawächter.')).toBeTruthy()
    expect(hint.querySelector('img')?.getAttribute('src')).toBe('https://cdn.example.test/volcano-gray.jpg')
    expect(hint.textContent).toContain('Nur mit Eis-Zauber.')
  })

  it('still shows an achievement hint when the list is not available', () => {
    render(<HintAlert section={{ type: 'alert', kind: 'achievement', apiName: 'ACH_X', title: 'Der Ritter' }} />)
    expect(screen.getByText('Steam-Erfolg ACH_X')).toBeTruthy()
    expect(screen.getByText('Der Ritter')).toBeTruthy()
  })

  it('shows a point of no return with its "do this first" list', () => {
    const { container } = render(
      <HintAlert section={{ type: 'alert', kind: 'point_of_no_return', title: 'Ab hier kein Zurück', items: ['Alle Truhen öffnen', ' ', 'Händler besuchen'] }} />
    )
    const hint = container.querySelector('[data-hint-kind="point_of_no_return"]') as HTMLElement
    expect(within(hint).getByText('Point of no Return')).toBeTruthy()
    expect(within(hint).getByText('Erledige das vorher')).toBeTruthy()
    expect(hint.querySelectorAll('.pv-hint-todo li')).toHaveLength(2)
    expect(hint.className).toContain('pv-alert-danger')
  })

  it('shows no "do this first" box without items, and only for a point of no return', () => {
    const { container } = render(
      <>
        <HintAlert section={{ type: 'alert', kind: 'point_of_no_return', title: 'Kein Zurück' }} />
        <HintAlert section={{ type: 'alert', kind: 'tip', items: ['wird ignoriert'] }} />
      </>
    )
    expect(container.querySelector('.pv-hint-todo')).toBeNull()
  })

  it('keeps the four existing kinds as they were', () => {
    const { container } = render(
      <>
        <HintAlert section={{ type: 'alert', kind: 'must_have', title: 'A' }} />
        <HintAlert section={{ type: 'alert', kind: 'missable', title: 'B' }} />
        <HintAlert section={{ type: 'alert', kind: 'optional', title: 'C' }} />
        <HintAlert section={{ type: 'alert', kind: 'tip', title: 'D' }} />
      </>
    )
    expect(container.querySelectorAll('.pv-alert-gold, .pv-alert-danger, .pv-alert-emerald, .pv-alert-crystal')).toHaveLength(4)
  })
})

describe('SourcesBlock', () => {
  const sources: GuideSectionDto = {
    type: 'sources',
    sources: [
      { title: 'Offizielles Handbuch', url: 'https://example.test/handbuch', origin: 'cited' },
      { title: 'Wiki', url: 'http://wiki.example.test/boss', origin: 'ai' },
      { title: 'Gefährlich', url: 'javascript:alert(1)', origin: 'ai' },
      { title: 'Daten', url: 'data:text/html,<script>1</script>', origin: 'cited' },
      { title: 'Mit Passwort', url: 'https://user:pw@example.test/', origin: 'cited' },
      { title: 'Relativ', url: '/intern/pfad' },
    ],
  }

  it('opens real web links outside the app, safely', () => {
    render(<SourcesBlock section={sources} />)
    const link = screen.getByRole('link', { name: /Offizielles Handbuch/ })
    expect(link.getAttribute('href')).toBe('https://example.test/handbuch')
    expect(link.getAttribute('target')).toBe('_blank')
    expect(link.getAttribute('rel')).toContain('noopener')
    expect(link.getAttribute('rel')).toContain('noreferrer')
    expect(screen.getByRole('link', { name: /Wiki/ }).getAttribute('href')).toBe('http://wiki.example.test/boss')
  })

  it('turns every other address into plain text, never into a link', () => {
    render(<SourcesBlock section={sources} />)
    expect(screen.getAllByRole('link')).toHaveLength(2)
    for (const title of ['Gefährlich', 'Daten', 'Mit Passwort', 'Relativ']) {
      expect(screen.getByText(title).closest('a')).toBeNull()
    }
  })

  it('says quietly where each source comes from', () => {
    const { container } = render(<SourcesBlock section={sources} />)
    const origins = Array.from(container.querySelectorAll('[data-origin]')).map((el) => [el.getAttribute('data-origin'), el.textContent])
    expect(origins[0]).toEqual(['cited', 'zitiert'])
    expect(origins[1]).toEqual(['ai', 'von der KI genannt'])
    expect(origins[5]).toEqual(['unknown', 'Herkunft unbekannt'])
  })

  it('uses the host as the name of a source without a title, and draws nothing for an empty list', () => {
    const { container, rerender } = render(<SourcesBlock section={{ type: 'sources', sources: [{ url: 'https://docs.example.test/page' }] }} />)
    expect(screen.getByRole('link', { name: /docs\.example\.test/ })).toBeTruthy()
    rerender(<SourcesBlock section={{ type: 'sources', sources: [] }} />)
    expect(container.firstChild).toBeNull()
  })

  it('safeWebUrl accepts only http(s) without credentials', () => {
    expect(safeWebUrl('https://a.test/x')?.hostname).toBe('a.test')
    expect(safeWebUrl('HTTP://a.test')?.protocol).toBe('http:')
    for (const bad of ['javascript:alert(1)', 'data:text/plain,x', 'file:///c:/x', 'ftp://a.test', '//a.test', 'a.test', '', '  ', null, undefined, 'https://u:p@a.test']) {
      expect(safeWebUrl(bad)).toBeNull()
    }
  })
})
