import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { applyLanguage } from '../../i18n'
import { TrophyTimeline } from './TrophyTimeline'
import type { Unlock } from './statsData'

const at = (iso: string, name: string): Unlock => ({ ts: new Date(iso).getTime(), name, percent: 10 })

const mockWidth = (width: number) =>
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width, height: 256, x: 0, y: 0, top: 0, left: 0, right: width, bottom: 256, toJSON: () => ({}) })

// Two unlocks on the first day, one on the third, none on the second
const UNLOCKS = [at('2026-05-01T10:00:00', 'Alpha'), at('2026-05-01T11:30:00', 'Beta'), at('2026-05-03T09:00:00', 'Gamma')]

describe('TrophyTimeline (trophies per day)', () => {
  beforeEach(async () => {
    // jsdom has no layout engine: the chart measures its element, so give it a resize observer that never fires
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        unobserve() {}
        disconnect() {}
      }
    )
    await applyLanguage('de')
  })

  afterEach(async () => {
    cleanup()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
    await applyLanguage('de')
  })

  it('describes the days with unlocks, with no breakthrough wording and no legend', () => {
    const { container } = render(<TrophyTimeline unlocks={UNLOCKS} total={10} />)
    const text = container.textContent ?? ''
    expect(text).toContain('Trophäen pro Tag')
    expect(text).toContain('3 Trophäen an 2 Tagen')
    expect(text).toContain('Eingezeichnet sind nur Tage')
    expect(text.toLowerCase()).not.toContain('durchbruch')
    // one series: no legend box
    expect(container.querySelector('.pv-viz-legend')).toBeNull()
  })

  it('has a table twin with one row per day that has an unlock (no empty days) and a running total', () => {
    render(<TrophyTimeline unlocks={UNLOCKS} total={10} />)
    fireEvent.click(screen.getByRole('button', { name: 'Als Tabelle anzeigen' }))
    const rows = screen.getAllByRole('row').slice(1)
    expect(rows).toHaveLength(2)
    expect(rows[0]?.textContent).toContain('Alpha, Beta')
    expect(rows[0]?.textContent).toContain('2')
    // the running total of the second day is 3
    expect(rows[1]?.textContent).toMatch(/Gamma/)
    expect(rows[1]?.textContent).toMatch(/3/)
  })

  it('draws one bar and one full-height hit target per day, none for the empty day in between', () => {
    mockWidth(400)
    const { container } = render(<TrophyTimeline unlocks={UNLOCKS} total={10} />)
    expect(container.querySelectorAll('path.pv-viz-vbar')).toHaveLength(2)
    const hits = container.querySelectorAll('rect[data-viz-mark]')
    expect(hits).toHaveLength(2)
    // never narrower than 8 px: the slot is the bar plus its gap
    expect(Number(hits[0]?.getAttribute('width'))).toBeGreaterThanOrEqual(10)
    // the count stands above EVERY bar, not only the highest
    expect(Array.from(container.querySelectorAll('text.pv-viz-value-text')).map((el) => el.textContent)).toEqual(['2', '1'])
  })

  it('writes the count above every bar, also when all days have a single trophy', () => {
    mockWidth(400)
    const { container } = render(<TrophyTimeline unlocks={[at('2026-05-01T10:00:00', 'A'), at('2026-05-02T10:00:00', 'B'), at('2026-05-03T10:00:00', 'C')]} total={10} />)
    expect(container.querySelectorAll('path.pv-viz-vbar')).toHaveLength(3)
    expect(Array.from(container.querySelectorAll('text.pv-viz-value-text')).map((el) => el.textContent)).toEqual(['1', '1', '1'])
  })

  it('draws a single day as one bar and does not page', () => {
    mockWidth(1100)
    const { container } = render(<TrophyTimeline unlocks={UNLOCKS.slice(0, 2)} total={10} />)
    expect(container.querySelectorAll('path.pv-viz-vbar')).toHaveLength(1)
    expect(container.querySelector('.pv-viz-pager')).toBeNull()
  })

  it('pages through the days when the bars would get thinner than 24 px, newest first', () => {
    mockWidth(400)
    // 60 days with one unlock each
    const many = Array.from({ length: 60 }, (_, i) => at(new Date(2026, 0, 1 + i, 12).toISOString(), 'T' + i))
    const { container } = render(<TrophyTimeline unlocks={many} total={100} />)
    const shown = container.querySelectorAll('path.pv-viz-vbar').length
    expect(shown).toBeGreaterThan(8)
    expect(shown).toBeLessThan(20)
    expect(container.textContent).toContain('Tage ' + (60 - shown + 1) + '–60 von 60')
    const earlier = screen.getByRole('button', { name: 'Frühere Tage' }) as HTMLButtonElement
    const later = screen.getByRole('button', { name: 'Spätere Tage' }) as HTMLButtonElement
    expect(earlier.disabled).toBe(false)
    expect(later.disabled).toBe(true)
    fireEvent.click(earlier)
    expect(container.textContent).toContain('Tage ' + (60 - 2 * shown + 1) + '–' + (60 - shown) + ' von 60')
    expect(later.disabled).toBe(false)
    // all the way back: the arrow to earlier days is dimmed and the first window shows the first days
    for (let i = 0; i < 10; i++) fireEvent.click(earlier)
    expect(earlier.disabled).toBe(true)
    expect(container.textContent).toContain('Tage 1–' + shown + ' von 60')
  })

  it('does not page while all days fit', () => {
    mockWidth(1100)
    const days = Array.from({ length: 15 }, (_, i) => at(new Date(2026, 0, 1 + i, 12).toISOString(), 'T' + i))
    const { container } = render(<TrophyTimeline unlocks={days} total={100} />)
    expect(container.querySelectorAll('path.pv-viz-vbar')).toHaveLength(15)
    expect(container.querySelector('.pv-viz-pager')).toBeNull()
  })

  it('speaks English with the same structure', async () => {
    await applyLanguage('en')
    const { container } = render(<TrophyTimeline unlocks={UNLOCKS} total={10} />)
    const text = container.textContent ?? ''
    expect(text).toContain('Trophies per day')
    expect(text).toContain('3 trophies on 2 days')
    expect(text).toContain('Only days on which you unlocked at least one trophy are shown.')
  })

  it('uses the singular for a single day', () => {
    const { container } = render(<TrophyTimeline unlocks={UNLOCKS.slice(0, 2)} total={10} />)
    expect(container.textContent).toContain('2 Trophäen an einem Tag')
  })
})
