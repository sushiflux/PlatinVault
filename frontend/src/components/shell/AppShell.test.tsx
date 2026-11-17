import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes, useLocation, useNavigate } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AboutPage } from '../../pages/AboutPage'
import { NotFoundPage } from '../../pages/NotFoundPage'
import { AppRoutes } from '../../App'
import { LaunchProvider } from '../launch/LaunchProvider'
import { ConfirmProvider } from '../ui/ConfirmDialog'
import { AppShell } from './AppShell'
import { RAIL_KEY } from './useShellNav'

// The app shell: the header (cabinet, settings, Steam user, info) on every page, the side navigation ONLY on a game page - the tree of that
// game with its chapters, the rail with group flyouts remembered per browser, the drawer on a phone - and the routes of the shell (about,
// 404, the Steam sign-in round trip landing on the settings, the old /cabinet address).

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
  coverUrl: '',
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

// A window of the given width: every "(max-width: Npx)" query matches when the width is at most N (the header's tiers and the drawer)
function stubWidth(width: number) {
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => {
      const max = /max-width:\s*(\d+)px/.exec(query)
      return {
        matches: max ? width <= Number(max[1]) : false,
        media: query,
        addEventListener: () => {},
        removeEventListener: () => {},
        addListener: () => {},
        removeListener: () => {},
      }
    })
  )
}
const stubMedia = (phone: boolean) => stubWidth(phone ? 390 : 1440)

function LocationProbe() {
  const location = useLocation()
  const navigate = useNavigate()
  return (
    <>
      <p data-testid="location">{`${location.pathname}${location.search}`}</p>
      {/* Stands in for the browser's back button / the game head's back button */}
      <button type="button" onClick={() => void navigate(-1)}>
        probe-back
      </button>
    </>
  )
}

function renderShell(path: string | string[]) {
  const entries = Array.isArray(path) ? path : [path]
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <LaunchProvider>
          <MemoryRouter initialEntries={entries} initialIndex={entries.length - 1}>
            <Routes>
              <Route element={<AppShell theme="light" onToggleTheme={() => {}} />}>
                <Route path="/" element={<LocationProbe />} />
                <Route path="/library" element={<LocationProbe />} />
                <Route path="/games/:slug" element={<LocationProbe />} />
                <Route path="/settings" element={<LocationProbe />} />
                <Route path="/about" element={<AboutPage />} />
                <Route path="/cabinet" element={<LocationProbe />} />
                <Route path="*" element={<NotFoundPage />} />
              </Route>
            </Routes>
          </MemoryRouter>
        </LaunchProvider>
      </ConfirmProvider>
    </QueryClientProvider>
  )
}

const nav = () => screen.getByRole('navigation', { name: 'Spiel-Navigation' })
const header = () => document.querySelector('header') as HTMLElement

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

describe('AppShell without a game', () => {
  it.each(['/', '/library', '/settings', '/about', '/gibt-es-nicht'])('has no side navigation on %s', async (path) => {
    stubMedia(false)
    renderShell(path)
    // The header is there; no navigation landmark and no menu button for a drawer
    expect(header()).toBeTruthy()
    expect(screen.queryByRole('navigation', { name: 'Spiel-Navigation' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Navigation öffnen' })).toBeNull()
    expect(document.querySelector('.pv-shell-navwrap')).toBeNull()
    expect(document.querySelector('.pv-shell')?.getAttribute('data-nav')).toBeNull()
  })

  it('has no side navigation on a phone either, and no menu button', () => {
    stubMedia(true)
    renderShell('/')
    expect(screen.queryByRole('button', { name: 'Navigation öffnen' })).toBeNull()
    expect(document.querySelector('.pv-shell-navwrap')).toBeNull()
  })
})

describe('AppShell header', () => {
  it('holds the cabinet, settings, language, info and the Steam user (no "Meine Spiele", no cabinet or settings entry in a tree)', async () => {
    stubMedia(false)
    renderShell('/')
    const bar = within(header())
    expect(bar.getByRole('button', { name: 'Trophäenschrank' })).toBeTruthy()
    expect(bar.getByRole('button', { name: 'Einstellungen' })).toBeTruthy()
    expect(bar.getByRole('link', { name: 'Über PlatinVault' }).getAttribute('href')).toBe('/about')
    expect(bar.getByRole('button', { name: 'Handbuch' })).toBeTruthy()
    const user = await bar.findByRole('link', { name: /Player/ })
    expect(user.getAttribute('href')).toBe('https://steamcommunity.com/id/x')
    expect(user.getAttribute('target')).toBe('_blank')
    expect(user.textContent).toContain('Player')
    expect(user.textContent).toContain('12')
    // No avatar URL: the placeholder user glyph, no broken image
    expect(user.querySelector('img')).toBeNull()
    expect(user.querySelector('.pv-avatar--empty')).toBeTruthy()
    expect(screen.queryByText('Meine Spiele')).toBeNull()
  })

  it('shows a plain, non-link account without a profile address', async () => {
    stubMedia(false)
    const stub = globalThis.fetch
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) => {
        if (String(input).split('?')[0] === '/api/steam/profile') {
          return new Response(JSON.stringify({ steamId: '1', personaName: 'Player', avatarUrl: null, profileUrl: null, libraryGameCount: null }), { status: 200, headers: { 'Content-Type': 'application/json' } })
        }
        return stub(input)
      })
    )
    renderShell('/')
    await waitFor(() => expect(header().querySelector('span.pv-user')).toBeTruthy())
    expect(within(header()).queryByRole('link', { name: /Player/ })).toBeNull()
    expect(header().querySelector('.pv-user-name')?.textContent).toBe('Player')
  })

  it('opens the settings page on the tab the user left them on', async () => {
    stubMedia(false)
    localStorage.setItem('pv_settings_tab', 'ki')
    renderShell('/')
    fireEvent.click(within(header()).getByRole('button', { name: 'Einstellungen' }))
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/settings?tab=ki'))
    expect(within(header()).getByRole('button', { name: 'Einstellungen' }).getAttribute('aria-current')).toBe('page')
  })

  it('opens the trophy cabinet as a window over the current page, kept in the address (?cabinet=1), and closes it again', async () => {
    stubMedia(false)
    renderShell('/library?x=1')
    fireEvent.click(within(header()).getByRole('button', { name: 'Trophäenschrank' }))
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/library?x=1&cabinet=1'))
    const dialog = await screen.findByRole('dialog', { name: /Trophäenschrank/ })
    expect(dialog).toBeTruthy()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Trophäenschrank schließen' }))
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/library?x=1'))
  })

  it('is open after a reload on ?cabinet=1', async () => {
    stubMedia(false)
    renderShell('/?cabinet=1')
    expect(await screen.findByRole('dialog', { name: /Trophäenschrank/ })).toBeTruthy()
  })

  it('folds display size, handbook and info into one overflow menu on a narrow window, the rest stays', async () => {
    stubWidth(800)
    renderShell('/')
    const bar = within(header())
    expect(bar.queryByRole('button', { name: /Darstellungsgröße/ })).toBeNull()
    expect(bar.queryByRole('button', { name: 'Handbuch' })).toBeNull()
    expect(bar.getByRole('button', { name: 'Trophäenschrank' })).toBeTruthy()
    expect(bar.getByRole('button', { name: 'Hilfe & Hinweise' })).toBeTruthy()
    // The Steam user shrinks to its avatar
    expect((await bar.findByRole('link', { name: /Player/ })).querySelector('.pv-user-name')).toBeNull()
    fireEvent.click(bar.getByRole('button', { name: 'Weitere Funktionen' }))
    const menu = await screen.findByRole('menu', { name: 'Weitere Funktionen' })
    expect(within(menu).getByRole('menuitem', { name: 'Darstellungsgröße' })).toBeTruthy()
    expect(within(menu).getByRole('menuitem', { name: 'Handbuch' })).toBeTruthy()
    expect(within(menu).getByRole('menuitem', { name: 'Über PlatinVault' }).getAttribute('href')).toBe('/about')
    expect(within(menu).queryByRole('menuitem', { name: /Farbschema/ })).toBeNull()
    // Esc closes it and the focus returns to its button
    fireEvent.keyDown(menu, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull())
    expect(document.activeElement).toBe(bar.getByRole('button', { name: 'Weitere Funktionen' }))
  })

  it('on a phone also moves colour scheme, help and the Steam account into the menu, so every icon keeps its size', async () => {
    stubWidth(390)
    renderShell('/')
    const bar = within(header())
    expect(bar.queryByRole('button', { name: /Farbschema/ })).toBeNull()
    expect(bar.queryByRole('button', { name: 'Hilfe & Hinweise' })).toBeNull()
    expect(bar.queryByRole('link', { name: /Player/ })).toBeNull()
    // What stays: cabinet, settings, language, the menu
    for (const name of ['Trophäenschrank', 'Einstellungen', 'Weitere Funktionen']) expect(bar.getByRole('button', { name })).toBeTruthy()
    expect(bar.getByRole('button', { name: /^Sprache/ })).toBeTruthy()
    // The account is loaded before the menu opens, so it is there when the menu appears
    await waitFor(() => expect(globalThis.fetch).toHaveBeenCalledWith('/api/steam/profile', expect.anything()))
    await new Promise((resolve) => setTimeout(resolve, 50))
    fireEvent.click(bar.getByRole('button', { name: 'Weitere Funktionen' }))
    const menu = await screen.findByRole('menu', { name: 'Weitere Funktionen' })
    expect(within(menu).getByRole('menuitem', { name: /Farbschema: Hell/ })).toBeTruthy()
    expect(within(menu).getByRole('menuitem', { name: 'Hilfe & Hinweise' })).toBeTruthy()
    expect((await within(menu).findByRole('menuitem', { name: /Steam-Profil von Player/ })).getAttribute('href')).toBe('https://steamcommunity.com/id/x')
    // Arrow keys move through the entries
    const items = within(menu).getAllByRole('menuitem')
    expect(document.activeElement).toBe(items[0])
    fireEvent.keyDown(menu, { key: 'ArrowDown' })
    expect(document.activeElement).toBe(items[1])
    fireEvent.keyDown(menu, { key: 'End' })
    expect(document.activeElement).toBe(items[items.length - 1])
  })
})

describe('AppShell game navigation', () => {
  it('is the tree of the open game: chapters under their groups, Story & Lore - no overview, live trophies or statistics entry', async () => {
    stubMedia(false)
    renderShell('/games/alpha?tab=bosses')
    const tree = nav()
    const list = await within(tree).findByRole('list', { name: 'Kapitel von Alpha Quest' })
    // The way back and the areas live in the game head now, not in the tree
    for (const name of [/Übersicht/, /Live-Trophäen/, /Statistiken/]) expect(within(tree).queryByRole('link', { name })).toBeNull()
    expect(tree.querySelector('[data-hint="game-live"]')).toBeNull()
    expect(within(list).getByRole('link', { name: /^Bosse/ }).getAttribute('aria-current')).toBe('page')
    expect(within(list).getByRole('link', { name: /^Komplettlösung/ }).getAttribute('aria-current')).toBeNull()
    expect(within(list).getByRole('link', { name: /^Komplettlösung/ }).textContent).toContain('2 von 5 erledigt')
    expect(within(list).getByRole('link', { name: /Nebenquests/ }).textContent).toContain('Recherche fehlgeschlagen')
    expect(within(list).queryByRole('link', { name: /Erfolge/ })).toBeNull()
    expect(within(list).getByRole('link', { name: 'Story & Lore' }).getAttribute('href')).toBe('/games/alpha?tab=story')
    // Group labels in the fixed order; the open game's title heads the tree
    const labels = [...list.querySelectorAll('.pv-glabel')].map((el) => el.textContent)
    expect(labels).toEqual(['Einstieg', 'Kampf & Taktik'])
    expect(within(tree).getByText('Alpha Quest')).toBeTruthy()
    // No list of games, no cabinet, no settings, no user, no info
    expect(within(tree).queryByRole('link', { name: /Bravo/ })).toBeNull()
    expect(within(tree).queryByText('Meine Spiele')).toBeNull()
    for (const name of [/Trophäenschrank/, /Einstellungen/, /Info/, /Player/]) expect(within(tree).queryByRole('link', { name })).toBeNull()
    expect(within(tree).queryByRole('searchbox')).toBeNull()
  })

  it('opens the first chapter when the URL names no tab', async () => {
    stubMedia(false)
    renderShell('/games/alpha')
    const list = await within(nav()).findByRole('list', { name: 'Kapitel von Alpha Quest' })
    expect(within(list).getByRole('link', { name: /^Komplettlösung/ }).getAttribute('aria-current')).toBe('page')
  })

  it('collapses to an icon rail, remembers it in this browser, and keeps lore and every chapter reachable', async () => {
    stubMedia(false)
    renderShell('/games/alpha?tab=bosses')
    await within(nav()).findByRole('list', { name: 'Kapitel von Alpha Quest' })
    expect(nav().getAttribute('data-rail')).toBeNull()
    fireEvent.click(within(nav()).getByRole('button', { name: 'Navigation einklappen' }))
    expect(nav().getAttribute('data-rail')).toBe('true')
    expect(localStorage.getItem(RAIL_KEY)).toBe('1')

    cleanup()
    renderShell('/games/alpha?tab=bosses')
    const tree = nav()
    expect(tree.getAttribute('data-rail')).toBe('true')
    expect(within(tree).getByRole('button', { name: 'Navigation ausklappen' })).toBeTruthy()
    // Story & Lore stays reachable as an icon named for screen readers (and by tooltip); the rail has no overview / area entries either
    expect((await within(tree).findByRole('link', { name: 'Story & Lore' })).getAttribute('href')).toBe('/games/alpha?tab=story')
    for (const name of ['Übersicht', 'Live-Trophäen', 'Statistiken']) expect(within(tree).queryByRole('link', { name })).toBeNull()
    // One icon per chapter group; the one that holds the open chapter carries the accent line
    const combat = within(tree).getByRole('button', { name: 'Kampf & Taktik' })
    expect(combat.getAttribute('data-active')).toBe('true')
    expect(combat.getAttribute('aria-haspopup')).toBe('menu')
    expect(within(tree).getByRole('button', { name: 'Einstieg' }).getAttribute('data-active')).toBeNull()
    expect(within(tree).queryByRole('link', { name: /^Bosse/ })).toBeNull()
  })

  it('opens the chapters of a group as a flyout from the rail: Enter opens, arrows move, Esc closes and the focus returns', async () => {
    stubMedia(false)
    localStorage.setItem(RAIL_KEY, '1')
    renderShell('/games/alpha?tab=bosses')
    const group = await within(nav()).findByRole('button', { name: 'Kampf & Taktik' })
    expect(group.getAttribute('aria-expanded')).toBe('false')
    group.focus()
    fireEvent.click(group) // Enter / Space on a button end up as a click
    const menu = await screen.findByRole('menu', { name: 'Kampf & Taktik' })
    expect(group.getAttribute('aria-expanded')).toBe('true')
    const items = within(menu).getAllByRole('menuitem')
    expect(items.map((item) => item.textContent)).toEqual([expect.stringContaining('Bosse'), expect.stringContaining('Nebenquests')])
    expect(items[0]!.getAttribute('href')).toBe('/games/alpha?tab=bosses')
    expect(items[0]!.getAttribute('aria-current')).toBe('page')
    expect(items[1]!.textContent).toContain('Recherche fehlgeschlagen')
    // The open chapter has the focus; arrows move (and wrap), Esc closes
    expect(document.activeElement).toBe(items[0])
    fireEvent.keyDown(menu, { key: 'ArrowDown' })
    expect(document.activeElement).toBe(items[1])
    fireEvent.keyDown(menu, { key: 'ArrowDown' })
    expect(document.activeElement).toBe(items[0])
    fireEvent.keyDown(menu, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull())
    expect(document.activeElement).toBe(group)
    expect(group.getAttribute('aria-expanded')).toBe('false')
  })

  it('follows a chapter of the flyout and closes it', async () => {
    stubMedia(false)
    localStorage.setItem(RAIL_KEY, '1')
    renderShell('/games/alpha?tab=bosses')
    fireEvent.click(await within(nav()).findByRole('button', { name: 'Einstieg' }))
    const menu = await screen.findByRole('menu', { name: 'Einstieg' })
    fireEvent.click(within(menu).getByRole('menuitem', { name: /Komplettlösung/ }))
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/games/alpha?tab=walkthrough'))
    await waitFor(() => expect(screen.queryByRole('menu')).toBeNull())
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
  it('shows the game navigation as a drawer opened by the menu button in the header, closed by Esc and by following a link', async () => {
    stubMedia(true)
    renderShell('/games/alpha')
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
    fireEvent.click(await within(nav()).findByRole('link', { name: /^Bosse/ }))
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/games/alpha?tab=bosses'))
    expect(wrap.getAttribute('data-open')).toBe('false')
    // The rail setting never applies to the drawer
    expect(nav().getAttribute('data-rail')).toBeNull()
  })

  it('has no overview, live trophies or statistics entry in the drawer either', async () => {
    stubMedia(true)
    renderShell('/games/alpha')
    fireEvent.click(screen.getByRole('button', { name: 'Navigation öffnen' }))
    await within(nav()).findByRole('link', { name: /^Bosse/ })
    for (const name of [/Übersicht/, /Live-Trophäen/, /Statistiken/]) expect(within(nav()).queryByRole('link', { name })).toBeNull()
  })

  it('does not add history entries when moving between chapters, so the back button leaves the game', async () => {
    stubMedia(false)
    // The overview, then the game: one step back must land on the overview, however many chapters were opened in between
    renderShell(['/', '/games/alpha?tab=bosses'])
    fireEvent.click(await within(nav()).findByRole('link', { name: /Komplettlösung/ }))
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/games/alpha?tab=walkthrough'))
    fireEvent.click(within(nav()).getByRole('link', { name: /Nebenquests/ }))
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/games/alpha?tab=quests'))
    fireEvent.click(screen.getByRole('button', { name: 'probe-back' }))
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/'))
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
    // The info icon of the header marks the page it opened
    expect(within(header()).getByRole('link', { name: 'Über PlatinVault' }).getAttribute('aria-current')).toBe('page')
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

describe('App routes', () => {
  it('sends the old /cabinet address to the overview with the cabinet open', async () => {
    stubMedia(false)
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <ConfirmProvider>
          <LaunchProvider>
            <MemoryRouter initialEntries={['/cabinet']}>
              <AppRoutes theme="light" onToggleTheme={() => {}} />
              <LocationProbe />
            </MemoryRouter>
          </LaunchProvider>
        </ConfirmProvider>
      </QueryClientProvider>
    )
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/?cabinet=1'))
    expect(await screen.findByRole('dialog', { name: /Trophäenschrank/ })).toBeTruthy()
  })
})
