import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { applyLanguage } from '../i18n'
import type { GameSummaryDto, LibrarySummaryDto, SteamOwnedGameDto } from '../api/types'
import { ConfirmProvider } from '../components/ui/ConfirmDialog'
import { LaunchProvider } from '../components/launch/LaunchProvider'
import { AiChoiceProvider } from '../components/ai/AiChoice'
import { DashboardPage } from './DashboardPage'

// Games added by hand on the dashboard: "+ Spiel hinzufügen", "Alle Spiele" with and without Steam, the figures, the star, the empty help.

const game = (n: number, patch: Partial<GameSummaryDto>): GameSummaryDto =>
  ({
    id: `id-${n}`,
    slug: `game-${n}`,
    title: `Game ${n}`,
    subtitle: '',
    steamAppId: 1000 + n,
    coverUrl: '',
    heroBannerUrl: '',
    accentColor: 'emerald',
    estimatedHours: '',
    missablesInfo: '',
    difficultyInfo: '',
    genreTags: '',
    contentLanguage: 'de',
    steamGenres: [],
    totalCheckableItems: 0,
    checkedItemsCount: 0,
    progressPercent: 0,
    tabCount: 0,
    trophiesUnlocked: 0,
    trophiesTotal: 0,
    trophiesPercent: 0,
    platinumAt: null,
    trophiesCheckedAt: '2026-11-01T10:00:00Z',
    isFavorite: true,
    playtimeMinutes: null,
    lastPlayedAt: null,
    metacriticScore: null,
    reviewScoreDesc: null,
    reviewPercent: null,
    releaseDate: null,
    completionHours: null,
    igdbRating: null,
    igdbUserRating: null,
    updatedAt: '2026-11-01T10:00:00Z',
    ...patch,
  }) as GameSummaryDto

const STEAM_GAME = game(1, { title: 'Alpha', trophiesUnlocked: 10, trophiesTotal: 10, trophiesPercent: 100 })
const MANUAL_STARRED = game(2, { title: 'Stellar Blade', steamAppId: null, platform: 'playstation', platformVariant: 'ps5', trophiesUnlocked: 40, trophiesTotal: 40, trophiesPercent: 100, platinumAt: '2026-11-18T20:00:00Z' })
const MANUAL_HIDDEN = game(3, { title: 'Hades', steamAppId: null, platform: 'epic', platformVariant: null, isFavorite: false, trophiesUnlocked: 2, trophiesTotal: 10, trophiesPercent: 20 })

const LIBRARY: SteamOwnedGameDto[] = [
  { appId: 1001, name: 'Alpha', playtimeHours: 5, coverUrl: '', heroBannerUrl: '', headerImageUrl: '', alreadyInVault: true, existingSlug: 'game-1', isFavorite: true, lastPlayedAt: '2026-10-02T00:00:00Z' },
  { appId: 5, name: 'Echo', playtimeHours: 1, coverUrl: '', heroBannerUrl: '', headerImageUrl: '', alreadyInVault: false, existingSlug: null, isFavorite: false, lastPlayedAt: '2026-10-01T00:00:00Z' },
]

interface World {
  games: GameSummaryDto[]
  summary: LibrarySummaryDto | null
  library: unknown
}
let world: World
let calls: { method: string; path: string; body: string | null }[]

const summary = (patch: Partial<LibrarySummaryDto>): LibrarySummaryDto => ({
  libraryGameCount: 2,
  gamesWithAchievements: 0,
  trophiesUnlocked: 5,
  trophiesTotal: 20,
  perfectGames: 0,
  scanning: false,
  scanDone: 0,
  scanTotal: 0,
  metaScanning: false,
  metaDone: 0,
  metaTotal: 0,
  manualGameCount: 2,
  manualTrophiesTotal: 50,
  manualTrophiesUnlocked: 42,
  manualPerfectGames: 1,
  ...patch,
})

function stubServer() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const path = url.split('?')[0] ?? url
      const method = init?.method ?? 'GET'
      calls.push({ method, path, body: typeof init?.body === 'string' ? init.body : null })
      const ok = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
      if (path === '/api/games') return ok(world.games)
      if (path === '/api/steam/library/summary') return world.summary ? ok(world.summary) : new Response(null, { status: 204 })
      if (path === '/api/steam/library') return ok(world.library)
      if (path === '/api/settings') return ok({ dashboardSortFavorites: 'custom:asc', dashboardSortAll: 'alphabetical:asc', hasTwitchKey: false, hasSteamKey: false })
      if (path === '/api/live/status') return ok({ isRunning: false, runningGameTracked: false })
      if (/^\/api\/games\/[^/]+\/favorite$/.test(path)) return ok({ ...MANUAL_HIDDEN, isFavorite: true })
      return new Response('{}', { status: 404, headers: { 'Content-Type': 'application/json' } })
    })
  )
}

function Where() {
  const location = useLocation()
  return <span data-testid="where">{`${location.pathname}${location.search}`}</span>
}

function renderAt(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <ConfirmProvider>
          <AiChoiceProvider>
            <LaunchProvider>
              <Where />
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

beforeEach(async () => {
  localStorage.clear()
  localStorage.setItem('pv_tour_done_v1', '1')
  localStorage.setItem('pv_hints_seen', JSON.stringify({ dashboard: true }))
  await applyLanguage('de')
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query, addEventListener: () => undefined, removeEventListener: () => undefined, addListener: () => undefined, removeListener: () => undefined, onchange: null, dispatchEvent: () => false }))
  const Observer = class { observe() {} unobserve() {} disconnect() {} takeRecords() { return [] } }
  vi.stubGlobal('IntersectionObserver', Observer)
  vi.stubGlobal('ResizeObserver', Observer)
  calls = []
  world = {
    games: [STEAM_GAME, MANUAL_STARRED, MANUAL_HIDDEN],
    summary: summary({}),
    library: { resolvedSteamId: '1', totalGames: 2, games: LIBRARY },
  }
  stubServer()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('"+ Spiel hinzufügen"', () => {
  it('is a text button in the page head next to the guide import, a coach-mark target, and opens the dialog through the address', async () => {
    const { container } = renderAt('/')
    const button = await screen.findByRole('button', { name: 'Spiel hinzufügen' })
    expect(button.getAttribute('data-hint')).toBe('dashboard-add-game')
    expect(button.className).toContain('pv-btn')
    const head = container.querySelector('.pv-dash-head')!
    expect(head.contains(button)).toBe(true)
    expect(within(head as HTMLElement).getByRole('button', { name: 'Guide importieren' })).toBeTruthy()

    fireEvent.click(button)
    expect(screen.getByTestId('where').textContent).toBe('/?dialog=addgame')
    expect(await screen.findByRole('dialog', { name: 'Spiel hinzufügen' })).toBeTruthy()
    // Without Twitch credentials: the hand-made form
    expect(await screen.findByLabelText('Titel')).toBeTruthy()
    // Esc closes it and takes the flag out of the address
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    await waitFor(() => expect(screen.getByTestId('where').textContent).toBe('/'))
    expect(screen.queryByRole('dialog', { name: 'Spiel hinzufügen' })).toBeNull()
  })

  it('stays open after a reload: the address carries it', async () => {
    renderAt('/?dialog=addgame')
    expect(await screen.findByRole('dialog', { name: 'Spiel hinzufügen' })).toBeTruthy()
  })
})

describe('"Alle Spiele" with games added by hand', () => {
  it('lists the Steam library and every game added by hand (starred or not) in one list', async () => {
    const { container } = renderAt('/library')
    await waitFor(() => expect(container.querySelectorAll('.pv-tile').length).toBe(4))
    const titles = [...container.querySelectorAll('.pv-tile-title')].map((node) => node.textContent)
    // Sorted together (A-Z in this test's settings)
    expect(titles).toEqual(['Alpha', 'Echo', 'Hades', 'Stellar Blade'])
    // The Steam-library tile of Echo says Steam, the manual ones their platform
    const chips = [...container.querySelectorAll('.pv-platform-badge')].map((node) => node.firstElementChild?.textContent)
    expect(chips.sort()).toEqual(['Epic', 'PS5', 'Steam', 'Steam'])
    // The count line counts both kinds: 2 Steam library games + 2 by hand
    expect(screen.getByText(/Zeige 4 von 4 Spielen/)).toBeTruthy()
  })

  it('adds the figures of the games added by hand to the library figures', async () => {
    const { container } = renderAt('/library')
    const figures = await waitFor(() => {
      const found = container.querySelector<HTMLElement>('.pv-dfigs')
      expect(found).not.toBeNull()
      return found!
    })
    // 2 Steam games + 2 by hand; 5 + 42 of 20 + 50 trophies; 0 + 1 games on platinum
    await waitFor(() => expect(within(figures).getByText('4')).toBeTruthy())
    expect(within(figures).getByText(/47 \/ 70/)).toBeTruthy()
    expect(figures.querySelectorAll('.pv-dfig')[2]!.textContent).toContain('1')
    expect(screen.getByRole('button', { name: /Alle Spiele \(4\)/ })).toBeTruthy()
  })

  it('works without a Steam key when games were added by hand', async () => {
    world.summary = summary({ libraryGameCount: 0, trophiesTotal: 0, trophiesUnlocked: 0, perfectGames: 0 })
    world.library = { resolvedSteamId: '', totalGames: 0, games: [] }
    const { container } = renderAt('/library')
    await waitFor(() => expect(container.querySelectorAll('.pv-tile').length).toBe(2))
    expect(screen.getByTestId('where').textContent).toBe('/library')
    expect([...container.querySelectorAll('.pv-tile-title')].map((node) => node.textContent).sort()).toEqual(['Hades', 'Stellar Blade'])
  })

  it('goes back to the overview without a Steam library and without games added by hand', async () => {
    world.summary = null
    world.games = []
    renderAt('/library')
    await waitFor(() => expect(screen.getByTestId('where').textContent).toBe('/'))
  })

  it('stars a game added by hand through the favourite flag of the games list', async () => {
    const { container } = renderAt('/library')
    await waitFor(() => expect(container.querySelectorAll('.pv-tile').length).toBe(4))
    const hades = [...container.querySelectorAll<HTMLElement>('.pv-tile')].find((tile) => tile.textContent?.includes('Hades'))!
    const star = within(hades).getByRole('button', { name: /„Hades“ zu den Favoriten/ })
    expect(star.getAttribute('aria-pressed')).toBe('false')
    fireEvent.click(star)
    await waitFor(() => expect(calls.some((c) => c.method === 'PUT' && c.path === '/api/games/game-3/favorite')).toBe(true))
    expect(JSON.parse(calls.find((c) => c.path === '/api/games/game-3/favorite')!.body!)).toEqual({ isFavorite: true })
  })

  it('searches the games added by hand by title like the others', async () => {
    const { container } = renderAt('/library')
    await waitFor(() => expect(container.querySelectorAll('.pv-tile').length).toBe(4))
    fireEvent.change(container.querySelector('#dashboard-game-search')!, { target: { value: 'hades' } })
    await waitFor(() => expect(container.querySelectorAll('.pv-tile').length).toBe(1))
    expect(container.querySelector('.pv-tile-title')?.textContent).toBe('Hades')
  })
})

describe('the overview with games added by hand', () => {
  it('shows the starred ones among the favourites with their platinum and chip, and counts them in the figures', async () => {
    const { container } = renderAt('/')
    await waitFor(() => expect(container.querySelectorAll('.pv-tile').length).toBe(2))
    const stellar = [...container.querySelectorAll<HTMLElement>('.pv-tile')].find((tile) => tile.textContent?.includes('Stellar Blade'))!
    // 40 of 40 and the server's platinumAt: the plaque and the gold line
    expect(stellar.getAttribute('data-platin')).toBe('true')
    expect(stellar.querySelector('.pv-platform-badge')?.firstElementChild?.textContent).toBe('PS5')
    expect(container.querySelector<HTMLElement>('.pv-dfigs')!.textContent).toContain('2')
  })
})

describe('the empty overview', () => {
  it('offers the way in by hand next to the Steam steps', async () => {
    world.games = []
    world.summary = null
    renderAt('/')
    const section = await screen.findByRole('region', { name: 'Dein Dashboard ist noch leer' })
    expect(within(section).getByText('Oder ein Spiel von Hand hinzufügen')).toBeTruthy()
    fireEvent.click(within(section).getByRole('button', { name: 'Spiel hinzufügen' }))
    expect(screen.getByTestId('where').textContent).toBe('/?dialog=addgame')
  })
})
