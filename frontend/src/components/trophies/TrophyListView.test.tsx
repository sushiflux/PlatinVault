import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { applyLanguage } from '../../i18n'
import type { LiveAchievementItemDto } from '../../api/types'
import { saveTrophyDensity, TROPHY_DENSITIES, type TrophyDensity } from '../TrophyViews'
import type { TrophyCheck } from './TrophyCheck'
import { ALL_SORTS, STEAM_TIER_FILTERS, TrophyListView, type TrophySort } from './TrophyListView'
import { manualItem, manualList, ps5Items } from './trophies.fixtures'

// The one list of every trophy tab, here with the researched list of a game added by hand: the same four views as a Steam list, plus the check that
// ticks a trophy, and a glyph where the Steam list shows the icon file.

const client = vi.hoisted(() => ({ fetchSettings: vi.fn() }))
vi.mock('../../api/client', async (importOriginal) => ({ ...(await importOriginal<typeof import('../../api/client')>()), ...client }))

beforeEach(async () => {
  await applyLanguage('de')
  vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} unobserve() {} })
  localStorage.clear()
  client.fetchSettings.mockReset()
  client.fetchSettings.mockResolvedValue({ achievementNotesEnabled: true })
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const SORTS_WITHOUT_SHARES: readonly TrophySort[] = ['default', 'unlocked_recent', 'name_asc']

function renderList(props: Partial<React.ComponentProps<typeof TrophyListView>> = {}, density: TrophyDensity = 2) {
  saveTrophyDensity(density)
  const list = manualList(ps5Items())
  const toggles: string[] = []
  const check = (ach: LiveAchievementItemDto): TrophyCheck => ({ checked: ach.achieved, busy: false, onToggle: () => toggles.push(ach.apiName) })
  const view = render(
    <QueryClientProvider client={new QueryClient()}>
      <TrophyListView
        data={list}
        loading={false}
        notesScope={{ gameId: list.gameId }}
        check={check}
        tierFilters={['Platinum', 'Gold', 'Silver', 'Bronze']}
        sorts={SORTS_WITHOUT_SHARES}
        {...props}
      />
    </QueryClientProvider>
  )
  return { ...view, toggles }
}

const checks = () => [...document.querySelectorAll<HTMLButtonElement>('.pv-tcheck')]

describe('TrophyListView with the researched list of a game added by hand', () => {
  it.each(TROPHY_DENSITIES)('draws a check on every trophy in the size "$id", the state in aria-pressed', async ({ value }) => {
    renderList({}, value)
    await screen.findAllByText(/Kapitel 1 abgeschlossen|Erstes Blut/)

    const boxes = checks()
    expect(boxes).toHaveLength(4)
    expect(boxes.map((box) => box.getAttribute('aria-pressed'))).toEqual(['false', 'false', 'true', 'false'])
    expect(screen.getByRole('button', { name: 'Haken bei „Erstes Blut“ entfernen' })).toBeTruthy()
    expect(screen.getByRole('button', { name: '„Letzter Boss“ als erledigt abhaken' })).toBeTruthy()
  })

  it.each(TROPHY_DENSITIES)('draws the tier glyph instead of an icon file in the size "$id" (no broken image)', async ({ value }) => {
    renderList({}, value)
    await screen.findAllByText(/Erstes Blut/)

    expect(document.querySelectorAll('.pv-trophy-glyph').length).toBeGreaterThanOrEqual(4)
    expect(document.querySelectorAll('.pv-trophy-glyph[data-tier="Platinum"]').length).toBeGreaterThanOrEqual(1)
    expect(document.querySelectorAll('img')).toHaveLength(0)
    // The ticked trophy is in colour, the open ones are grey like a locked Steam icon
    expect(document.querySelectorAll('.pv-trophy-glyph[data-locked]').length).toBeGreaterThanOrEqual(3)
  })

  it('ticks the trophy that was clicked and nothing else', async () => {
    const { toggles } = renderList()
    fireEvent.click(await screen.findByRole('button', { name: '„Letzter Boss“ als erledigt abhaken' }))
    expect(toggles).toEqual(['gold-1'])
  })

  it('keeps the size of the check while it waits for the server and ignores clicks meanwhile', async () => {
    const onToggle = vi.fn()
    renderList({ check: (ach) => ({ checked: false, busy: ach.apiName === 'gold-1', onToggle }) })
    const busy = await screen.findByRole('button', { name: '„Letzter Boss“ als erledigt abhaken' })
    expect(busy.getAttribute('aria-busy')).toBe('true')
    expect(busy.hasAttribute('data-busy')).toBe(true)
    // The same box, a spinner inside instead of the check mark
    expect(busy.querySelector('.pv-tcheck-box svg')).toBeTruthy()
    fireEvent.click(busy)
    expect(onToggle).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: '„Erstes Blut“ als erledigt abhaken' }))
    expect(onToggle).toHaveBeenCalledTimes(1)
  })

  it('shows the platinum trophy locked (the server ticks it itself) and does not tick it by a click', async () => {
    const onToggle = vi.fn()
    renderList({ check: (ach) => ({ checked: ach.achieved, busy: false, onToggle, locked: ach.apiName === 'platin' }) })
    const platinum = await screen.findByRole('button', { name: '„Stellar Legende“ als erledigt abhaken' })
    expect(platinum.getAttribute('aria-disabled')).toBe('true')
    fireEvent.click(platinum)
    expect(onToggle).not.toHaveBeenCalled()
  })

  it('has no check at all on a list without one (every Steam list)', async () => {
    renderList({ check: undefined })
    await screen.findAllByText(/Erstes Blut/)
    expect(checks()).toHaveLength(0)
  })

  it('has the check in the detail dialog of a tile, fixed in size and with the same state', async () => {
    renderList({}, 1)
    fireEvent.click(await screen.findByRole('button', { name: /Letzter Boss.*Gold/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Letzter Boss' })
    const check = within(dialog).getByRole('button', { name: '„Letzter Boss“ als erledigt abhaken' })
    expect(check.getAttribute('aria-pressed')).toBe('false')
    expect(check.className).toContain('pv-tcheck')
  })

  it('offers the Platinum tier filter only when the caller gives it, and filters by it', async () => {
    const { rerender } = renderList({ tierFilters: STEAM_TIER_FILTERS })
    await screen.findAllByText(/Erstes Blut/)
    const tiers = screen.getByRole('group', { name: 'Stufe filtern' })
    expect(within(tiers).queryByRole('button', { name: /Platin/ })).toBeNull()

    rerender(
      <QueryClientProvider client={new QueryClient()}>
        <TrophyListView data={manualList(ps5Items())} loading={false} notesScope={null} tierFilters={['Platinum', 'Gold', 'Silver', 'Bronze']} sorts={SORTS_WITHOUT_SHARES} />
      </QueryClientProvider>
    )
    fireEvent.click(within(screen.getByRole('group', { name: 'Stufe filtern' })).getByRole('button', { name: /Platin/ }))
    expect(screen.queryByText('Letzter Boss')).toBeNull()
    expect(screen.getAllByText('Stellar Legende').length).toBeGreaterThan(0)
  })

  it('offers the rarity sorts only for a list that states a share', async () => {
    const { rerender } = renderList()
    await screen.findAllByText(/Erstes Blut/)
    let options = within(screen.getByRole('combobox', { name: 'Sortierung' })).getAllByRole('option').map((option) => option.textContent)
    expect(options).toEqual(['Sortierung: Standard', 'Zuletzt freigeschaltet', 'Alphabetisch (A–Z)'])

    rerender(
      <QueryClientProvider client={new QueryClient()}>
        <TrophyListView
          data={manualList([manualItem('a', { hasGlobalPercent: true, globalPercent: 4 })])}
          loading={false}
          notesScope={null}
          tierFilters={STEAM_TIER_FILTERS}
          sorts={ALL_SORTS}
        />
      </QueryClientProvider>
    )
    options = within(screen.getByRole('combobox', { name: 'Sortierung' })).getAllByRole('option').map((option) => option.textContent)
    expect(options).toHaveLength(5)
  })

  it('keeps the order of the list as the default order', async () => {
    renderList({}, 0)
    await screen.findAllByText(/Erstes Blut/)
    const names = [...document.querySelectorAll('.pv-arow-name')].map((node) => node.textContent)
    expect(names).toEqual(['Stellar Legende', 'Letzter Boss', 'Erstes Blut', 'Kapitel 1 abgeschlossen'])
  })

  it('says "Verpassbar" on a trophy the list marks as missable', async () => {
    renderList({}, 0)
    await screen.findAllByText(/Erstes Blut/)
    expect(screen.getAllByText('Verpassbar')).toHaveLength(1)
  })
})
