import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { applyLanguage } from '../../i18n'
import { saveTrophyDensity, type TrophyDensity } from '../TrophyViews'
import { STEAM_TIER_FILTERS, TrophyListView, ALL_SORTS } from './TrophyListView'
import { manualItem, manualList, ps5Items } from './trophies.fixtures'

// The "Alle Tipps zeigen" switch of the trophy list: opens the tip of every trophy at once (Kompakt and Liste), the bulb of a card overrides one card.

const client = vi.hoisted(() => ({ fetchSettings: vi.fn() }))
vi.mock('../../api/client', async (importOriginal) => ({ ...(await importOriginal<typeof import('../../api/client')>()), ...client }))

const KEY = 'pv_trophy_tips_all_v1'
const TIPS = { 'gold-1': 'Tipp zum Boss', 'silber-1': 'Tipp zum Blut', 'bronze-1': 'Tipp zum Kapitel', hid: 'Tipp zum Geheimen' }
const ALL_THREE = ['Tipp zum Boss', 'Tipp zum Blut', 'Tipp zum Kapitel']

beforeEach(async () => {
  await applyLanguage('de')
  vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} unobserve() {} })
  localStorage.clear()
  client.fetchSettings.mockReset()
  client.fetchSettings.mockResolvedValue({ achievementNotesEnabled: true })
})
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

function renderList(props: Partial<React.ComponentProps<typeof TrophyListView>> = {}, density: TrophyDensity = 2) {
  saveTrophyDensity(density)
  const list = manualList([...ps5Items(), manualItem('hid', { displayName: 'Geheimnis', isHidden: true })])
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <TrophyListView data={list} loading={false} tips={TIPS} notesScope={null} tierFilters={STEAM_TIER_FILTERS} sorts={ALL_SORTS} {...props} />
    </QueryClientProvider>
  )
}

const tipsSwitch = () => screen.getByRole('switch', { name: 'Alle Tipps zeigen' })
const revealSwitch = () => screen.getByRole('switch', { name: 'Geheime Erfolge aufdecken' })
const shown = (tip: string) => screen.queryByText(tip) !== null
const ready = () => screen.findAllByText(/Erstes Blut/)

describe('the "show all tips" switch', () => {
  it('opens the tip of every card (and none of the hidden trophy while it is covered), and closes them again', async () => {
    renderList()
    await ready()
    expect(tipsSwitch().getAttribute('aria-checked')).toBe('false')
    for (const tip of ALL_THREE) expect(shown(tip)).toBe(false)

    fireEvent.click(revealSwitch()) // the secret trophy is covered now
    fireEvent.click(tipsSwitch())
    expect(tipsSwitch().getAttribute('aria-checked')).toBe('true')
    for (const tip of ALL_THREE) expect(shown(tip)).toBe(true)
    expect(shown('Tipp zum Geheimen')).toBe(false)
    expect(screen.getAllByRole('button', { name: 'Tipp ausblenden' })).toHaveLength(3)

    fireEvent.click(tipsSwitch())
    for (const tip of ALL_THREE) expect(shown(tip)).toBe(false)
  })

  it('lets the bulb of one card override the switch in both directions', async () => {
    renderList()
    await ready()

    // Off: one bulb opens one tip
    fireEvent.click(screen.getAllByRole('button', { name: 'Tipp anzeigen' })[0]!)
    expect(shown('Tipp zum Boss')).toBe(true)
    expect(shown('Tipp zum Blut')).toBe(false)

    // On: one bulb closes one tip, the others stay open
    fireEvent.click(tipsSwitch())
    fireEvent.click(screen.getAllByRole('button', { name: 'Tipp ausblenden' })[1]!)
    expect(shown('Tipp zum Boss')).toBe(true)
    expect(shown('Tipp zum Blut')).toBe(false)
    expect(shown('Tipp zum Kapitel')).toBe(true)
  })

  it('clears every single choice when the switch is used', async () => {
    renderList()
    await ready()
    fireEvent.click(tipsSwitch())
    fireEvent.click(screen.getAllByRole('button', { name: 'Tipp ausblenden' })[0]!) // the first tip is closed by hand
    expect(shown('Tipp zum Boss')).toBe(false)

    fireEvent.click(tipsSwitch()) // off ...
    fireEvent.click(tipsSwitch()) // ... and on again: all open, the earlier choice is gone
    for (const tip of ALL_THREE) expect(shown(tip)).toBe(true)
  })

  it('never shows the tip of a covered secret trophy, not even through its bulb', async () => {
    renderList()
    await ready()
    fireEvent.click(revealSwitch())
    fireEvent.click(tipsSwitch())
    expect(shown('Tipp zum Geheimen')).toBe(false)
    expect(screen.getAllByRole('button', { name: 'Tipp ausblenden' })).toHaveLength(3)
  })

  it('remembers the switch per browser', async () => {
    const first = renderList()
    await ready()
    fireEvent.click(tipsSwitch())
    expect(localStorage.getItem(KEY)).toBe('1')
    first.unmount()

    renderList()
    await ready()
    expect(tipsSwitch().getAttribute('aria-checked')).toBe('true')
    expect(shown('Tipp zum Boss')).toBe(true)

    fireEvent.click(tipsSwitch())
    expect(localStorage.getItem(KEY)).toBe('0')
  })

  it('works when the browser refuses its storage', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('denied')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('denied')
    })
    renderList()
    await ready()
    expect(tipsSwitch().getAttribute('aria-checked')).toBe('false')
    fireEvent.click(tipsSwitch())
    expect(tipsSwitch().getAttribute('aria-checked')).toBe('true')
    expect(shown('Tipp zum Boss')).toBe(true)
  })
})

describe('the tip line of the list view', () => {
  const lines = () => [...document.querySelectorAll('.pv-arow-tip')].map((node) => node.textContent)

  it('shows the tip as its own line under the description when the switch is on', async () => {
    renderList({}, 0)
    await ready()
    expect(lines()).toEqual([])
    // No bulb in a row: the switch is the only control
    expect(screen.queryByRole('button', { name: 'Tipp anzeigen' })).toBeNull()

    fireEvent.click(revealSwitch())
    fireEvent.click(tipsSwitch())
    expect(lines()).toEqual(ALL_THREE)
    expect(document.querySelector('.pv-arow-tip svg')).toBeTruthy()

    fireEvent.click(tipsSwitch())
    expect(lines()).toEqual([])
  })

  it('shows no tip line for a covered secret trophy and none for a trophy without a tip', async () => {
    renderList({ tips: { hid: 'Tipp zum Geheimen' } }, 0)
    await ready()
    fireEvent.click(revealSwitch()) // covered
    fireEvent.click(tipsSwitch())
    expect(lines()).toEqual([])

    fireEvent.click(revealSwitch()) // uncovered again: now the line is there
    expect(lines()).toEqual(['Tipp zum Geheimen'])
  })
})

describe('where the switch is disabled (never hidden)', () => {
  const hover = (element: HTMLElement) => fireEvent.pointerEnter(element, { pointerType: 'mouse' })

  it('is disabled when the list has no tip at all, and names the reason', async () => {
    renderList({ tips: {} })
    await ready()
    const control = tipsSwitch()
    expect(control.getAttribute('aria-disabled')).toBe('true')
    expect(control.getAttribute('aria-checked')).toBe('false')
    hover(control)
    expect(await screen.findByText('Für dieses Spiel gibt es noch keine Tipps. Sie kommen aus dem Guide oder aus der recherchierten Trophäenliste.')).toBeTruthy()
  })

  it('is disabled in the tile view and points to the detail window', async () => {
    renderList({}, 1)
    await screen.findAllByRole('button', { name: /Erstes Blut/ })
    const control = tipsSwitch()
    expect(control.getAttribute('aria-disabled')).toBe('true')
    hover(control)
    expect(await screen.findByText('In der Kachelansicht steht der Tipp im Detailfenster einer Trophäe (Klick auf die Kachel).')).toBeTruthy()
  })

  it('is on and disabled with large cards, which always show every tip', async () => {
    renderList({}, 3)
    await ready()
    const control = tipsSwitch()
    expect(control.getAttribute('aria-disabled')).toBe('true')
    expect(control.getAttribute('aria-checked')).toBe('true')
    for (const tip of ALL_THREE) expect(shown(tip)).toBe(true)
    hover(control)
    expect(await screen.findByText('In der Ansicht „Groß“ stehen alle Tipps ohnehin immer da.')).toBeTruthy()
  })

  it('keeps the blocked switch reachable by keyboard: focus shows the reason, Space and Enter change nothing', async () => {
    renderList({ tips: {} })
    await ready()
    const control = tipsSwitch()
    expect((control as HTMLButtonElement).disabled).toBe(false)
    // jsdom does not know ":focus-visible" for a scripted focus; a real Tab press matches it, so say so here
    const realMatches = Element.prototype.matches
    vi.spyOn(Element.prototype, 'matches').mockImplementation(function (this: Element, selector: string) {
      return selector === ':focus-visible' ? this === document.activeElement : realMatches.call(this, selector)
    })
    act(() => control.focus())
    expect(document.activeElement).toBe(control)
    expect(await screen.findByText('Für dieses Spiel gibt es noch keine Tipps. Sie kommen aus dem Guide oder aus der recherchierten Trophäenliste.')).toBeTruthy()

    fireEvent.keyDown(control, { key: ' ' })
    fireEvent.keyUp(control, { key: ' ' })
    fireEvent.keyDown(control, { key: 'Enter' })
    fireEvent.click(control)
    expect(control.getAttribute('aria-checked')).toBe('false')
    expect(localStorage.getItem(KEY)).toBeNull()
  })

  it('is enabled in the compact view and the list view', async () => {
    renderList({}, 2)
    await ready()
    expect(tipsSwitch().getAttribute('aria-disabled')).toBeNull()
    cleanup()
    renderList({}, 0)
    await ready()
    expect(tipsSwitch().getAttribute('aria-disabled')).toBeNull()
  })

  it('keeps the remembered choice when the tile view is left again', async () => {
    renderList({}, 1)
    await screen.findAllByRole('button', { name: /Erstes Blut/ })
    fireEvent.click(screen.getByRole('button', { name: 'Kompakt' }))
    await ready()
    expect(tipsSwitch().getAttribute('aria-checked')).toBe('false')
    fireEvent.click(tipsSwitch())
    fireEvent.click(screen.getByRole('button', { name: 'Kacheln' }))
    expect(tipsSwitch().getAttribute('aria-checked')).toBe('true')
    expect(tipsSwitch().getAttribute('aria-disabled')).toBe('true')
  })
})
