import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { applyLanguage } from '../i18n'
import type { SortChoice } from '../utils/dashboardSort'
import { DashboardSortControl, placeSortMenu } from './DashboardSortControl'

function setup(view: 'favorites' | 'all', choice: SortChoice, loading = false) {
  const onChange = vi.fn()
  render(<DashboardSortControl view={view} choice={choice} onChange={onChange} loading={loading} />)
  return onChange
}

const selectButton = () => screen.getByRole('button', { name: /^(Sortieren nach|Sort by):/ })
const directionButton = () => screen.getByRole('button', { name: /^(Richtung umkehren|Reverse the direction|Die eigene Reihenfolge|Your own order)/ })

describe('DashboardSortControl', () => {
  beforeEach(async () => {
    await applyLanguage('de')
  })

  afterEach(() => cleanup())

  it('shows the active order on the button and its direction in words on the toggle', () => {
    setup('all', { order: 'release', direction: 'desc' })
    expect(selectButton().textContent).toContain('Erscheinungsdatum')
    expect(selectButton().getAttribute('aria-haspopup')).toBe('menu')
    expect(directionButton().getAttribute('aria-label')).toBe('Richtung umkehren, jetzt: neueste zuerst')
  })

  it('lists the manual order only for the favourites', () => {
    setup('favorites', { order: 'custom', direction: 'asc' })
    fireEvent.click(selectButton())
    const names = screen.getAllByRole('menuitemradio').map((item) => item.querySelector('.pv-sortmenu-name')?.textContent)
    expect(names).toEqual([
      'Eigene Reihenfolge',
      'Abschlussquote',
      'Alphabetisch',
      'Spielzeit',
      'Zuletzt gespielt',
      'Erscheinungsdatum',
      'Größe',
      'Metacritic-Wertung',
      'Steam-Bewertungen',
      'IGDB-Kritiker',
      'IGDB-Spieler',
      'Gesamtwertung',
    ])
    cleanup()

    setup('all', { order: 'lastPlayed', direction: 'desc' })
    fireEvent.click(selectButton())
    expect(screen.getAllByRole('menuitemradio')).toHaveLength(11)
    expect(screen.queryByText('Eigene Reihenfolge')).toBeNull()
  })

  it('picking an order starts it in its default direction, picking the active one again changes nothing', () => {
    const onChange = setup('all', { order: 'playtime', direction: 'asc' })
    fireEvent.click(selectButton())
    fireEvent.click(screen.getByRole('menuitemradio', { name: /Alphabetisch/ }))
    expect(onChange).toHaveBeenCalledWith({ order: 'alphabetical', direction: 'asc' })

    fireEvent.click(selectButton())
    fireEvent.click(screen.getByRole('menuitemradio', { name: /Abschlussquote/ }))
    expect(onChange).toHaveBeenLastCalledWith({ order: 'completion', direction: 'desc' })

    onChange.mockClear()
    fireEvent.click(selectButton())
    fireEvent.click(screen.getByRole('menuitemradio', { name: /Spielzeit/ }))
    expect(onChange).not.toHaveBeenCalled()
  })

  it('reverses the direction with the toggle', () => {
    const onChange = setup('all', { order: 'size', direction: 'desc' })
    fireEvent.click(directionButton())
    expect(onChange).toHaveBeenCalledWith({ order: 'size', direction: 'asc' })
  })

  it('has no direction for the manual order', () => {
    const onChange = setup('favorites', { order: 'custom', direction: 'asc' })
    const toggle = directionButton() as HTMLButtonElement
    expect(toggle.disabled).toBe(true)
    fireEvent.click(toggle)
    expect(onChange).not.toHaveBeenCalled()
  })

  it('moves with the arrow keys and closes with Escape, the focus back on the button', async () => {
    const onChange = setup('all', { order: 'completion', direction: 'desc' })
    fireEvent.click(selectButton())
    const items = screen.getAllByRole('menuitemradio')
    expect(document.activeElement).toBe(items[0]) // the order in use
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowDown' })
    expect(document.activeElement).toBe(items[1])
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'End' })
    expect(document.activeElement).toBe(items[items.length - 1])
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull())
    expect(document.activeElement).toBe(selectButton())
    expect(onChange).not.toHaveBeenCalled()
  })

  it('marks a lookup in progress without changing the button', () => {
    setup('all', { order: 'release', direction: 'desc' }, true)
    expect(selectButton().getAttribute('aria-busy')).toBe('true')
    expect(selectButton().textContent).toContain('Erscheinungsdatum')
  })

  it('speaks English when the app does', async () => {
    await applyLanguage('en')
    setup('all', { order: 'size', direction: 'desc' })
    expect(selectButton().textContent).toContain('Size')
    expect(directionButton().getAttribute('aria-label')).toBe('Reverse the direction, now: largest first')
  })
})

describe('DashboardSortControl menu placement', () => {
  const VIEWPORT = { width: 360, height: 780 }
  const NATURAL = 520

  beforeEach(async () => {
    await applyLanguage('de')
    vi.stubGlobal('innerHeight', VIEWPORT.height)
    Object.defineProperty(document.documentElement, 'clientWidth', { configurable: true, value: VIEWPORT.width })
    // jsdom has no layout: the list's natural height comes from here
    vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(NATURAL)
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    Reflect.deleteProperty(document.documentElement, 'clientWidth')
  })

  const openAt = (top: number, bottom: number) => {
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      const isButton = this.classList.contains('pv-sort-select')
      return (isButton ? { top, bottom, left: 200, right: 340, width: 140, height: bottom - top, x: 200, y: top } : { top: 0, bottom: 0, left: 0, right: 0, width: 0, height: 0, x: 0, y: 0 }) as DOMRect
    })
    setup('all', { order: 'completion', direction: 'desc' })
    fireEvent.click(selectButton())
    return screen.getByRole('menu')
  }

  it('opens under the button when the list fits there', () => {
    const menu = openAt(100, 148)
    expect(menu.getAttribute('data-side')).toBe('down')
    expect(Number.parseFloat(menu.style.top)).toBe(156)
  })

  it('opens above the button at the bottom edge, ending 8 px over it', () => {
    const menu = openAt(732, 780)
    expect(menu.getAttribute('data-side')).toBe('up')
    const top = Number.parseFloat(menu.style.top)
    expect(top).toBeGreaterThanOrEqual(8)
    expect(top + NATURAL).toBe(732 - 8)
  })

  it('never extends past the viewport, whatever the side', () => {
    for (const [top, bottom] of [[0, 48], [100, 148], [400, 448], [600, 648], [732, 780]] as const) {
      const box = placeSortMenu({ top, bottom, right: 340 }, VIEWPORT, NATURAL, 140)
      const height = Math.min(NATURAL, box.room)
      expect(box.top).toBeGreaterThanOrEqual(8)
      expect(box.top + height).toBeLessThanOrEqual(VIEWPORT.height - 8)
      expect(box.room).toBeLessThanOrEqual(VIEWPORT.height - 16)
    }
  })

  it('caps a list that fits on neither side to the larger side and lets it scroll inside', () => {
    // Button in the middle of a short window: 300 px of room on each side, the list wants 520
    const box = placeSortMenu({ top: 340, bottom: 388, right: 340 }, { width: 360, height: 728 }, NATURAL, 140)
    expect(box.room).toBeLessThan(NATURAL)
    expect(box.top + box.room).toBeLessThanOrEqual(728 - 8)
    expect(box.top).toBeGreaterThanOrEqual(8)
  })

  it('keeps the right alignment to the button', () => {
    const below = placeSortMenu({ top: 100, bottom: 148, right: 340 }, VIEWPORT, NATURAL, 140)
    const above = placeSortMenu({ top: 732, bottom: 780, right: 340 }, VIEWPORT, NATURAL, 140)
    expect(below.left + below.width).toBe(340)
    expect(above.left + above.width).toBe(340)
  })
})
