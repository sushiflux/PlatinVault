import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { applyLanguage } from '../i18n'
import type { SortChoice } from '../utils/dashboardSort'
import { DashboardSortControl } from './DashboardSortControl'

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
    ])
    cleanup()

    setup('all', { order: 'lastPlayed', direction: 'desc' })
    fireEvent.click(selectButton())
    expect(screen.getAllByRole('menuitemradio')).toHaveLength(8)
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
