import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { applyLanguage } from '../i18n'
import type { GameSummaryDto } from '../api/types'
import { ConfirmProvider } from '../components/ui/ConfirmDialog'
import { LaunchProvider } from '../components/launch/LaunchProvider'
import { AiChoiceProvider } from '../components/ai/AiChoice'
import { DashboardPage } from './DashboardPage'

// useTwitchLiveCounts is called once per render of DashboardPage: the counter tells whether the page (and with it every tile) re-rendered
const pageRenders = vi.hoisted(() => ({ count: 0 }))
vi.mock('../utils/useTwitchLiveCounts', async (importOriginal) => {
  const original = await importOriginal<typeof import('../utils/useTwitchLiveCounts')>()
  return {
    ...original,
    useTwitchLiveCounts: () => {
      pageRenders.count += 1
      return original.useTwitchLiveCounts()
    },
  }
})

// The compact bar of the controls: scroll-linked hand-over from the controls row under the header, same state as the row
// and stays while it is being used.

// The controls row sits at this distance from the top of the window; the header is 60 px high and the row 48 px, so the hand-over runs from
// top = 60 (start) to top = 12 (done)
const HEADER = 60
const ROW_HEIGHT = 48
let rowTop = 300

// Scrolls the page: the row is now at the given top
async function scrollRow(top: number) {
  rowTop = top
  act(() => {
    window.dispatchEvent(new Event('scroll'))
  })
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 5))
  })
}

const game = (n: number, title: string): GameSummaryDto =>
  ({
    id: `id-${n}`, slug: `game-${n}`, title, subtitle: '', steamAppId: 1000 + n, coverUrl: '', heroBannerUrl: '', accentColor: 'emerald', estimatedHours: '',
    missablesInfo: '', difficultyInfo: '', genreTags: '', contentLanguage: 'de', steamGenres: [], totalCheckableItems: 0, checkedItemsCount: 0, progressPercent: 0,
    tabCount: 0, trophiesUnlocked: 0, trophiesTotal: 0, trophiesPercent: 0, platinumAt: null, trophiesCheckedAt: '2026-11-01T10:00:00Z', isFavorite: true,
    playtimeMinutes: null, lastPlayedAt: null, metacriticScore: null, reviewScoreDesc: null, reviewPercent: null, releaseDate: null, completionHours: null,
    igdbRating: null, igdbUserRating: null, updatedAt: '2026-11-01T10:00:00Z',
  }) as GameSummaryDto

function stubServer() {
  const answers: Record<string, unknown> = {
    '/api/branding': { productName: 'PlatinVault', tagline: '', host: 'server' },
    '/api/steam/profile': { steamId: null, personaName: null, avatarUrl: null, profileUrl: null, libraryGameCount: null },
    '/api/live/status': { isRunning: false, runningGameTracked: false },
    '/api/games': [game(1, 'Alpha'), game(2, 'Bravo')],
    '/api/steam/library/summary': { libraryGameCount: 2, gamesWithAchievements: 0, trophiesUnlocked: 0, trophiesTotal: 0, perfectGames: 0, scanning: false, scanDone: 0, scanTotal: 0, metaScanning: false, metaDone: 0, metaTotal: 0 },
    '/api/steam/library': { resolvedSteamId: '1', totalGames: 0, games: [] },
    '/api/settings': { dashboardSortFavorites: 'custom:asc', dashboardSortAll: 'lastPlayed:desc' },
  }
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => {
      const path = url.split('?')[0] ?? url
      if (path in answers) return new Response(JSON.stringify(answers[path]), { status: 200, headers: { 'Content-Type': 'application/json' } })
      return new Response('{}', { status: 404, headers: { 'Content-Type': 'application/json' } })
    })
  )
}

// A window of the given width: every "(max-width: Npx)" query matches when the width is at most N
function stubWidth(width: number) {
  vi.stubGlobal('matchMedia', (query: string) => {
    const max = /max-width:\s*(\d+)px/.exec(query)
    return { matches: max ? width <= Number(max[1]) : false, media: query, addEventListener: () => undefined, removeEventListener: () => undefined, addListener: () => undefined, removeListener: () => undefined, onchange: null, dispatchEvent: () => false }
  })
}

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/']}>
        <ConfirmProvider>
          <AiChoiceProvider>
            <LaunchProvider>
              <Routes>
                <Route path="/" element={<DashboardPage view="favorites" />} />
                <Route path="/library" element={<DashboardPage view="all" />} />
              </Routes>
            </LaunchProvider>
          </AiChoiceProvider>
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const bar = () => screen.queryByRole('group', { name: 'Suchen, Ansicht und Sortierung' })
const originalSearch = (container: HTMLElement) => container.querySelector<HTMLInputElement>('#dashboard-game-search')!


// The copy is mounted from the first bit of progress on, but cannot be reached before it is (nearly) there
const isInert = (element: HTMLElement) => element.hasAttribute('inert')
const progress = (element: HTMLElement) => Number(element.style.getPropertyValue('--pv-cbar-p'))

describe('the compact bar of the dashboard controls', () => {
  beforeEach(async () => {
    rowTop = 300
    localStorage.clear()
    localStorage.setItem('pv_tour_done_v1', '1')
    await applyLanguage('de')
    stubWidth(1440)
    document.documentElement.style.setProperty('--pv-header-h', `${HEADER}px`)
    // Only the controls row has a place on the page in jsdom
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      const isRow = this.classList.contains('pv-dash-ctl')
      return { top: isRow ? rowTop : 0, height: isRow ? ROW_HEIGHT : 0, bottom: 0, left: 0, right: 0, width: 0, x: 0, y: 0, toJSON: () => ({}) }
    })
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => setTimeout(() => callback(0), 0) as unknown as number)
    vi.stubGlobal('cancelAnimationFrame', (id: number) => clearTimeout(id))
    const Observer = class { observe() {} unobserve() {} disconnect() {} takeRecords() { return [] } }
    vi.stubGlobal('IntersectionObserver', Observer)
    vi.stubGlobal('ResizeObserver', Observer)
    stubServer()
  })

  afterEach(() => {
    cleanup()
    document.documentElement.style.removeProperty('--pv-header-h')
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  const loaded = async () => {
    const view = renderPage()
    await waitFor(() => expect(view.container.querySelectorAll('.pv-tile').length).toBe(2))
    return view
  }

  it('is not there while the controls row is on screen, follows the scroll once the row moves under the header and is gone when it is back', async () => {
    await loaded()
    expect(bar()).toBeNull()

    // The row's top reaches the header's bottom edge: nothing yet
    await scrollRow(HEADER)
    expect(bar()).toBeNull()

    // A bit further: the copy is there as a fading shadow of the row, and cannot be reached yet
    await scrollRow(HEADER - 6)
    const fading = (await screen.findByRole('group', { name: 'Suchen, Ansicht und Sortierung', hidden: true })) as HTMLElement
    expect(progress(fading)).toBeCloseTo(0.125, 5)
    expect(isInert(fading)).toBe(true)
    expect(fading.hasAttribute('data-moving')).toBe(true)

    // Half way and beyond: reachable; the row is covered: fully there
    await scrollRow(HEADER - 24)
    const compact = await screen.findByRole('group', { name: 'Suchen, Ansicht und Sortierung' })
    expect(progress(compact)).toBeCloseTo(0.5, 5)
    expect(isInert(compact)).toBe(false)
    await scrollRow(HEADER - ROW_HEIGHT)
    expect(progress(compact)).toBe(1)
    expect(compact.hasAttribute('data-moving')).toBe(false)
    expect(within(compact).getByRole('searchbox')).toBeTruthy()
    expect(within(compact).getByRole('group', { name: /Welche Spiele angezeigt werden/ })).toBeTruthy()
    expect(within(compact).getByRole('slider')).toBeTruthy()
    expect(within(compact).getByRole('button', { name: /Sortierung|Sortieren/ })).toBeTruthy()
    // Laid over the content, never in the flow of the page
    expect(compact.classList.contains('pv-dash-cbar')).toBe(true)

    // Back up: it follows exactly and is gone when the row is back at the header edge - no lingering
    await scrollRow(HEADER - 12)
    expect(progress(compact)).toBeCloseTo(0.25, 5)
    await scrollRow(HEADER + 5)
    await waitFor(() => expect(bar()).toBeNull())
  })

  it('works on the same state as the row: typing in one shows in the other, and so does the tile size', async () => {
    const { container } = await loaded()
    await scrollRow(-300)
    const compact = await screen.findByRole('group', { name: 'Suchen, Ansicht und Sortierung' })

    fireEvent.change(within(compact).getByRole('searchbox'), { target: { value: 'Alp' } })
    expect(originalSearch(container).value).toBe('Alp')
    await waitFor(() => expect(container.querySelectorAll('.pv-tile').length).toBe(1))
    fireEvent.change(originalSearch(container), { target: { value: 'Bra' } })
    expect((within(compact).getByRole('searchbox') as HTMLInputElement).value).toBe('Bra')

    const original = container.querySelector<HTMLInputElement>('#dashboard-tile-size')!
    fireEvent.change(within(compact).getByRole('slider'), { target: { value: '3' } })
    expect(original.value).toBe('3')
  })

  it('has no duplicate id and no coach-mark anchor in the copy', async () => {
    const { container } = await loaded()
    await scrollRow(-300)
    const compact = await screen.findByRole('group', { name: 'Suchen, Ansicht und Sortierung' })
    const ids = [...document.querySelectorAll('[id]')].map((el) => el.id)
    expect(new Set(ids).size).toBe(ids.length)
    expect(compact.querySelector('[data-hint]')).toBeNull()
    // The row keeps its own anchors
    expect(container.querySelector('.pv-dash-ctl [data-hint="dashboard-size"]')).toBeTruthy()
  })

  it('stays fully there while focus is in it, even when the row is back in view, and lets go (after a short ease) when the focus leaves', async () => {
    const { container } = await loaded()
    await scrollRow(-300)
    const compact = await screen.findByRole('group', { name: 'Suchen, Ansicht und Sortierung' })
    const input = within(compact).getByRole('searchbox') as HTMLInputElement
    act(() => input.focus())
    fireEvent.change(input, { target: { value: 'Alp' } })
    await waitFor(() => expect(container.querySelectorAll('.pv-tile').length).toBe(1))

    // Typing shortened the page: the row is back in view, the bar stays under the cursor, reachable and fully visible
    await scrollRow(300)
    expect(bar()).toBeTruthy()
    expect(compact.getAttribute('data-engaged')).toBe('true')
    expect(isInert(compact)).toBe(false)

    act(() => input.blur())
    await waitFor(() => expect(bar()).toBeNull())
  })

  it('stays while its sort menu is open', async () => {
    await loaded()
    await scrollRow(-300)
    const compact = await screen.findByRole('group', { name: 'Suchen, Ansicht und Sortierung' })
    fireEvent.click(within(compact).getByRole('button', { name: /Sortierung|Sortieren/, expanded: false }))
    await screen.findByRole('menu')
    await scrollRow(300)
    expect(bar()).toBeTruthy()
    expect(bar()!.getAttribute('data-engaged')).toBe('true')
  })

  it('on a phone folds the search to an icon and the view and tile size into a popover, nothing is cut', async () => {
    stubWidth(390)
    const { container } = await loaded()
    await scrollRow(-300)
    const compact = await screen.findByRole('group', { name: 'Suchen, Ansicht und Sortierung' })
    // Search icon, sort control and the view button; no field, no switch, no slider in the row
    expect(within(compact).queryByRole('searchbox')).toBeNull()
    expect(within(compact).queryByRole('slider')).toBeNull()
    expect(within(compact).queryByRole('group', { name: /Welche Spiele angezeigt werden/ })).toBeNull()

    fireEvent.click(within(compact).getByRole('button', { name: 'Ansicht und Kachelgröße' }))
    const popover = await screen.findByRole('dialog', { name: 'Ansicht' })
    expect(within(popover).getByRole('group', { name: /Welche Spiele angezeigt werden/ })).toBeTruthy()
    expect(within(popover).getByRole('slider')).toBeTruthy()
    // Esc closes it and the focus goes back to its button
    fireEvent.keyDown(popover, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Ansicht' })).toBeNull())

    // The search unfolds on demand and works on the shared text
    fireEvent.click(within(compact).getByRole('button', { name: 'Suche öffnen' }))
    const field = await within(compact).findByRole('searchbox')
    fireEvent.change(field, { target: { value: 'Bra' } })
    expect(originalSearch(container).value).toBe('Bra')
    fireEvent.click(within(compact).getByRole('button', { name: 'Suche schließen' }))
    // Folded again, with a thin line saying that a filter is active
    const folded = await within(compact).findByRole('button', { name: 'Suche öffnen' })
    expect(folded.getAttribute('data-active')).toBe('true')
  })

  it('on a medium window keeps the view switch in the row and moves only the tile size into the popover', async () => {
    stubWidth(900)
    await loaded()
    await scrollRow(-300)
    const compact = await screen.findByRole('group', { name: 'Suchen, Ansicht und Sortierung' })
    expect(within(compact).getByRole('group', { name: /Welche Spiele angezeigt werden/ })).toBeTruthy()
    expect(within(compact).queryByRole('slider')).toBeNull()
    fireEvent.click(within(compact).getByRole('button', { name: 'Ansicht und Kachelgröße' }))
    const popover = await screen.findByRole('dialog', { name: 'Ansicht' })
    expect(within(popover).getByRole('slider')).toBeTruthy()
    expect(within(popover).queryByRole('group', { name: /Welche Spiele angezeigt werden/ })).toBeNull()
  })

  it('keeps the hand-over state out of the page: a phase change, taking hold and letting go do not re-render the tiles', async () => {
    await loaded()
    // Settle: the queries of the page are in
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 20))
    })
    const before = pageRenders.count

    // off -> fading -> on -> back
    await scrollRow(HEADER - 6)
    await scrollRow(HEADER - ROW_HEIGHT)
    const compact = await screen.findByRole('group', { name: 'Suchen, Ansicht und Sortierung' })
    expect(pageRenders.count).toBe(before)

    // Taking hold and letting go (focus in the bar and out again)
    const input = within(compact).getByRole('searchbox') as HTMLInputElement
    act(() => input.focus())
    expect(compact.getAttribute('data-engaged')).toBe('true')
    act(() => input.blur())
    await scrollRow(HEADER + 5)
    await waitFor(() => expect(bar()).toBeNull())
    expect(pageRenders.count).toBe(before)
  })

  it('letting go at progress 0 keeps the bar mounted and easing until the ease is over, then removes it', async () => {
    await loaded()
    // The row is fully in view (p = 0), the bar is taken hold of by a click into its search... which needs it mounted: scroll in and back
    await scrollRow(-300)
    const compact = await screen.findByRole('group', { name: 'Suchen, Ansicht und Sortierung' })
    const input = within(compact).getByRole('searchbox') as HTMLInputElement
    act(() => input.focus())
    await scrollRow(300)
    expect(progress(compact)).toBe(0)
    expect(compact.getAttribute('data-engaged')).toBe('true')

    act(() => input.blur())
    // The very next state: still mounted, no longer engaged, easing from 1 to 0 (not removed and re-mounted at 0)
    expect(bar()).toBe(compact)
    expect(compact.getAttribute('data-engaged')).toBeNull()
    expect(compact.getAttribute('data-easing')).toBe('true')
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 100))
    })
    expect(bar()).toBe(compact)
    await waitFor(() => expect(bar()).toBeNull())
  })
})
