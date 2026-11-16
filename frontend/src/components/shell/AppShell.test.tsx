import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AboutPage } from '../../pages/AboutPage'
import { NotFoundPage } from '../../pages/NotFoundPage'
import { LaunchProvider } from '../launch/LaunchProvider'
import { ConfirmProvider } from '../ui/ConfirmDialog'
import { AppShell } from './AppShell'
import { RAIL_KEY } from './useShellNav'

// The app shell: the favourites in the tree, the open game unfolding with its areas and chapters, the rail remembered per browser, the
// drawer on a phone, and the routes of the shell (about, 404, the Steam sign-in round trip landing on the settings).

const GAMES = [
  { id: '1', slug: 'alpha', title: 'Alpha Quest', isFavorite: true, coverUrl: null, steamAppId: 10 },
  { id: '2', slug: 'bravo', title: 'Bravo', isFavorite: true, coverUrl: null, steamAppId: 20 },
  { id: '3', slug: 'charlie', title: 'Charlie', isFavorite: false, coverUrl: null, steamAppId: 30 },
]

const tab = (id: string, slug: string, title: string, extra: Record<string, unknown> = {}) => ({
  id,
  slug,
  title,
  icon: '',
  checkedItemsCount: 0,
  totalCheckableItems: 0,
  sections: [],
  isAchievementList: false,
  groupKey: null,
  ...extra,
})

const ALPHA = {
  id: '1',
  slug: 'alpha',
  title: 'Alpha Quest',
  steamAppId: 10,
  checkedItemKeys: [],
  tabs: [
    tab('t1', 'walkthrough', '1. Komplettlösung', { groupKey: 'start', checkedItemsCount: 2, totalCheckableItems: 5 }),
    tab('t2', 'bosses', '2. Bosse', { groupKey: 'combat' }),
    tab('t3', 'quests', '3. Nebenquests', { groupKey: 'combat', sections: [{ type: 'alert', failedChapter: { focus: 'Quests' } }] }),
    tab('t4', 'steam-achievements', 'Erfolge', { isAchievementList: true }),
    tab('t5', 'story-lore', 'Story & Lore'),
  ],
}

function stubServer() {
  const answers: Record<string, unknown> = {
    '/api/games': GAMES,
    '/api/games/alpha': ALPHA,
    '/api/steam/profile': { steamId: '1', personaName: 'Player', avatarUrl: null, profileUrl: 'https://steamcommunity.com/id/x', libraryGameCount: 12 },
    '/api/steam/library/summary': { libraryGameCount: 12, trophiesUnlocked: 0, trophiesTotal: 0, perfectGames: 0, scanning: false, scanDone: 0, scanTotal: 0 },
    '/api/branding': { productName: 'PlatinVault', company: 'SushiFlux', version: 'v2026.11.20.1.2 (α)', sourceUrl: 'https://github.com/sushiflux/PlatinVault' },
  }
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string) => {
      const path = String(input).split('?')[0] ?? ''
      if (path in answers) return new Response(JSON.stringify(answers[path]), { status: 200, headers: { 'Content-Type': 'application/json' } })
      return new Response('{}', { status: 404, headers: { 'Content-Type': 'application/json' } })
    })
  )
}

function stubMedia(phone: boolean) {
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({
      matches: phone && query.includes('767'),
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
    }))
  )
}

function LocationProbe() {
  const location = useLocation()
  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>
}

function renderShell(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
      <LaunchProvider>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route element={<AppShell theme="light" onToggleTheme={() => {}} />}>
            <Route path="/" element={<LocationProbe />} />
            <Route path="/library" element={<LocationProbe />} />
            <Route path="/games/:slug" element={<LocationProbe />} />
            <Route path="/settings" element={<LocationProbe />} />
            <Route path="/about" element={<AboutPage />} />
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </MemoryRouter>
      </LaunchProvider>
      </ConfirmProvider>
    </QueryClientProvider>
  )
}

const nav = () => screen.getByRole('navigation', { name: 'Hauptnavigation' })

beforeEach(() => {
  localStorage.clear()
  // The first-start tour is not what is tested here
  localStorage.setItem('pv_onboarding_done_v1', '1')
  localStorage.setItem('pv_hints_seen', JSON.stringify({ dashboard: true, game: true }))
  vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} unobserve() {} })
  stubServer()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('AppShell tree navigation', () => {
  it('lists the favourites under "Meine Spiele" (not the other games) and marks the overview as the current page', async () => {
    stubMedia(false)
    renderShell('/')
    const tree = nav()
    expect(await within(tree).findByRole('link', { name: /Alpha Quest/ })).toBeTruthy()
    expect(within(tree).getByRole('link', { name: /Bravo/ })).toBeTruthy()
    expect(within(tree).queryByRole('link', { name: /Charlie/ })).toBeNull()
    expect(within(tree).getByText('Meine Spiele')).toBeTruthy()
    expect(within(tree).getByRole('link', { name: /Übersicht/ }).getAttribute('aria-current')).toBe('page')
    // With a readable library there is "Alle Spiele"; the settings and the cabinet are entries of the tree
    expect(await within(tree).findByRole('link', { name: /Alle Spiele/ })).toBeTruthy()
    expect(within(tree).getByRole('link', { name: /Einstellungen/ }).getAttribute('href')).toBe('/settings')
    expect(within(tree).getByRole('link', { name: /Trophäenschrank/ }).getAttribute('href')).toBe('/cabinet')
    expect(within(tree).getByRole('link', { name: /Info zu Version/ }).getAttribute('href')).toBe('/about')
  })

  it('unfolds only the open game: areas, chapters under their groups (achievement list hidden, failed chapter marked), Story & Lore', async () => {
    stubMedia(false)
    renderShell('/games/alpha?tab=bosses')
    const tree = nav()
    const subtree = await within(tree).findByRole('list', { name: 'Kapitel von Alpha Quest' })
    const live = within(subtree).getByRole('link', { name: 'Live-Trophäen' })
    expect(live.getAttribute('href')).toBe('/games/alpha?tab=live')
    expect(within(subtree).getByRole('link', { name: 'Statistiken' }).getAttribute('href')).toBe('/games/alpha?tab=stats')
    expect(within(subtree).getByRole('link', { name: /^Bosse/ }).getAttribute('aria-current')).toBe('page')
    expect(within(subtree).getByRole('link', { name: /^Komplettlösung/ }).getAttribute('aria-current')).toBeNull()
    expect(within(subtree).getByRole('link', { name: /^Komplettlösung/ }).textContent).toContain('2 von 5 erledigt')
    expect(within(subtree).getByRole('link', { name: /Nebenquests/ }).textContent).toContain('Recherche fehlgeschlagen')
    expect(within(subtree).queryByRole('link', { name: /Erfolge/ })).toBeNull()
    expect(within(subtree).getByRole('link', { name: 'Story & Lore' }).getAttribute('href')).toBe('/games/alpha?tab=story')
    // Group labels in the fixed order
    expect(within(subtree).getByText('Kampf & Taktik')).toBeTruthy()
    // One game open at a time
    expect(within(tree).getByRole('link', { name: /Alpha Quest/ }).getAttribute('data-open')).toBe('true')
    expect(within(tree).getByRole('link', { name: /Bravo/ }).getAttribute('data-open')).toBeNull()
  })

  it('opens the first chapter when the URL names no tab, and filters the tree by the search row', async () => {
    stubMedia(false)
    renderShell('/games/alpha')
    const tree = nav()
    const subtree = await within(tree).findByRole('list', { name: 'Kapitel von Alpha Quest' })
    expect(within(subtree).getByRole('link', { name: /^Komplettlösung/ }).getAttribute('aria-current')).toBe('page')

    fireEvent.change(within(tree).getByRole('searchbox', { name: 'Spiel oder Kapitel suchen' }), { target: { value: 'boss' } })
    expect(within(tree).queryByRole('link', { name: /Bravo/ })).toBeNull()
    expect(within(tree).getByRole('link', { name: /^Bosse/ })).toBeTruthy()
    expect(within(tree).queryByRole('link', { name: /^Komplettlösung/ })).toBeNull()

    fireEvent.change(within(tree).getByRole('searchbox', { name: 'Spiel oder Kapitel suchen' }), { target: { value: 'xyz' } })
    expect(within(tree).getByText('Nichts gefunden für „xyz“.')).toBeTruthy()
  })

  it('collapses to an icon rail and remembers it in this browser', async () => {
    stubMedia(false)
    renderShell('/')
    await within(nav()).findByRole('link', { name: /Alpha Quest/ })
    expect(nav().getAttribute('data-rail')).toBeNull()
    fireEvent.click(within(nav()).getByRole('button', { name: 'Navigation einklappen' }))
    expect(nav().getAttribute('data-rail')).toBe('true')
    expect(localStorage.getItem(RAIL_KEY)).toBe('1')

    cleanup()
    renderShell('/')
    expect(nav().getAttribute('data-rail')).toBe('true')
    expect(within(nav()).getByRole('button', { name: 'Navigation ausklappen' })).toBeTruthy()
    // The rail has no search row; the labels stay the accessible names of the entries
    expect(within(nav()).queryByRole('searchbox')).toBeNull()
    expect(within(nav()).getByRole('link', { name: /Übersicht/ })).toBeTruthy()
  })
})

describe('AppShell user row', () => {
  it('shows the Steam profile as a link with name and library size, the avatar fallback when there is no picture', async () => {
    stubMedia(false)
    renderShell('/')
    const row = await within(nav()).findByRole('link', { name: /Player/ })
    expect(row.getAttribute('href')).toBe('https://steamcommunity.com/id/x')
    expect(row.getAttribute('target')).toBe('_blank')
    expect(row.textContent).toContain('Player')
    expect(row.textContent).toContain('12')
    // No avatar URL: the placeholder user glyph, no broken image
    expect(row.querySelector('img')).toBeNull()
    expect(row.querySelector('.pv-avatar--empty')).toBeTruthy()
  })

  it('shows a plain, non-link row without a profile address', async () => {
    stubMedia(false)
    const stub = globalThis.fetch
    vi.stubGlobal('fetch', vi.fn(async (input: string) => {
      if (String(input).split('?')[0] === '/api/steam/profile') {
        return new Response(JSON.stringify({ steamId: '1', personaName: 'Player', avatarUrl: null, profileUrl: null, libraryGameCount: null }), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      return stub(input)
    }))
    renderShell('/')
    await waitFor(() => expect(nav().querySelector('span.pv-user')).toBeTruthy())
    expect(within(nav()).queryByRole('link', { name: /Player/ })).toBeNull()
    expect(nav().querySelector('.pv-user-name')?.textContent).toBe('Player')
  })
})

describe('AppShell introduction tour', () => {
  it('is visible on every page, also on the settings page (it never hides behind it)', async () => {
    stubMedia(false)
    localStorage.removeItem('pv_onboarding_done_v1')
    renderShell('/settings')
    expect(await screen.findByRole('dialog', { name: 'PlatinVault kennenlernen' })).toBeTruthy()
  })

  it('can be skipped, and then stays away', async () => {
    stubMedia(false)
    localStorage.removeItem('pv_onboarding_done_v1')
    renderShell('/')
    const dialog = await screen.findByRole('dialog', { name: 'PlatinVault kennenlernen' })
    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'PlatinVault kennenlernen' })).toBeNull())
    expect(dialog.isConnected).toBe(false)
    expect(localStorage.getItem('pv_onboarding_done_v1')).toBe('1')
  })
})

describe('AppShell on a phone', () => {
  it('is a drawer opened by the menu button in the header, closed by Esc and by following a link', async () => {
    stubMedia(true)
    renderShell('/')
    const wrap = nav().parentElement!
    expect(wrap.getAttribute('data-open')).toBe('false')
    const menu = screen.getByRole('button', { name: 'Navigation öffnen' })
    expect(menu.getAttribute('aria-expanded')).toBe('false')

    fireEvent.click(menu)
    expect(wrap.getAttribute('data-open')).toBe('true')
    expect(menu.getAttribute('aria-expanded')).toBe('true')
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(wrap.getAttribute('data-open')).toBe('false')

    fireEvent.click(menu)
    fireEvent.click(await within(nav()).findByRole('link', { name: /Alpha Quest/ }))
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/games/alpha'))
    expect(wrap.getAttribute('data-open')).toBe('false')
    // The rail setting never applies to the drawer
    expect(nav().getAttribute('data-rail')).toBeNull()
  })
})

describe('AppShell routes', () => {
  it('shows the about page with version, credit line, licences and source - and no copyright', async () => {
    stubMedia(false)
    renderShell('/about')
    expect(await screen.findByRole('heading', { name: 'Über PlatinVault' })).toBeTruthy()
    expect(await screen.findByText('v2026.11.20.1.2 (α)')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Lizenzen' })).toBeTruthy()
    expect((await screen.findByRole('link', { name: /Quellcode von PlatinVault/ })).getAttribute('href')).toBe('https://github.com/sushiflux/PlatinVault')
    expect(screen.getByLabelText('Herz')).toBeTruthy()
    expect(document.body.textContent).not.toContain('©')
    expect(within(nav()).getByRole('link', { name: /Info zu Version/ }).getAttribute('aria-current')).toBe('page')
  })

  it('answers an unknown address with the 404 page in English, also in a German interface', () => {
    stubMedia(false)
    renderShell('/gibt-es-nicht')
    expect(screen.getByRole('heading', { name: 'Page not found' })).toBeTruthy()
    expect(screen.getByText(/GET \/gibt-es-nicht/)).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Go to the overview' }).getAttribute('href')).toBe('/')
  })

  it('sends the result of "Sign in with Steam" to the Steam tab of the settings page', async () => {
    stubMedia(false)
    renderShell('/?steamLink=error&reason=denied')
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/settings?tab=steam&steamLink=error&reason=denied'))
  })
})
