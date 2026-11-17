import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { applyLanguage } from '../i18n'
import type { GameSummaryDto, SteamOwnedGameDto } from '../api/types'
import { ConfirmProvider } from '../components/ui/ConfirmDialog'
import { LaunchProvider } from '../components/launch/LaunchProvider'
import { AiChoiceProvider } from '../components/ai/AiChoice'
import { DashboardPage } from './DashboardPage'

// The look of the overview and of "Alle Spiele" (variant "Kristall"): the calm figures, the one controls row, the tiles and the list.

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

const FAVORITES = [
  game(1, { title: 'Alpha', trophiesUnlocked: 10, trophiesTotal: 10, trophiesPercent: 100, playtimeMinutes: 600 }),
  game(2, { title: 'Bravo', trophiesUnlocked: 3, trophiesTotal: 12, trophiesPercent: 25 }),
]

const LIBRARY: SteamOwnedGameDto[] = [
  { appId: 5, name: 'Echo', playtimeHours: 1, coverUrl: '', heroBannerUrl: '', headerImageUrl: '', alreadyInVault: false, existingSlug: null, isFavorite: false, lastPlayedAt: '2026-10-01T00:00:00Z', sizeOnDiskBytes: 5 * 1024 ** 3 },
  { appId: 6, name: 'Delta', playtimeHours: 0, coverUrl: '', heroBannerUrl: '', headerImageUrl: '', alreadyInVault: true, existingSlug: 'delta', isFavorite: true, lastPlayedAt: null, sizeOnDiskBytes: null },
]

function stubServer() {
  const answers: Record<string, unknown> = {
    '/api/branding': { productName: 'PlatinVault', tagline: '', host: 'server' },
    '/api/steam/profile': { steamId: null, personaName: null, avatarUrl: null, profileUrl: null, libraryGameCount: null },
    '/api/live/status': { isRunning: false, runningGameTracked: false },
    '/api/games': FAVORITES,
    '/api/steam/library/summary': {
      libraryGameCount: 2, gamesWithAchievements: 0, trophiesUnlocked: 13, trophiesTotal: 22, perfectGames: 1,
      scanning: false, scanDone: 0, scanTotal: 0, metaScanning: false, metaDone: 0, metaTotal: 0,
    },
    '/api/steam/library': { resolvedSteamId: '1', totalGames: 2, games: LIBRARY },
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

function renderAt(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
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

describe('DashboardPage look', () => {
  beforeEach(async () => {
    localStorage.clear()
    localStorage.setItem('pv_tour_done_v1', '1')
    await applyLanguage('de')
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query, addEventListener: () => undefined, removeEventListener: () => undefined, addListener: () => undefined, removeListener: () => undefined, onchange: null, dispatchEvent: () => false }))
    const Observer = class { observe() {} unobserve() {} disconnect() {} takeRecords() { return [] } }
    vi.stubGlobal('IntersectionObserver', Observer)
    vi.stubGlobal('ResizeObserver', Observer)
    stubServer()
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('shows the title, the three calm figures and the one controls row', async () => {
    const { container } = renderAt('/')
    expect(await screen.findByRole('heading', { level: 1, name: 'Wähle dein Spiel für den 100%-Durchgang' })).toBeTruthy()
    const figures = container.querySelector<HTMLElement>('.pv-dfigs')!
    // Favourites, trophies x / y with its line, games on platinum (13 of 22 = 59 %)
    await waitFor(() => expect(within(figures).getByRole('progressbar').getAttribute('aria-valuenow')).toBe('59'))
    expect(within(figures).getByText('Favoriten')).toBeTruthy()
    expect(within(figures).getByText(/Trophäen \(59\s?%\)/)).toBeTruthy()
    expect(within(figures).getByText('Spiel auf Platin')).toBeTruthy()
    // Search | view | tile size in one row
    const row = container.querySelector('.pv-dash-ctl')!
    expect(row.querySelector('#dashboard-game-search')).toBeTruthy()
    expect(within(row as HTMLElement).getByRole('group', { name: /Welche Spiele angezeigt werden/ })).toBeTruthy()
    expect(within(row as HTMLElement).getByRole('slider')).toBeTruthy()
  })

  it('draws a finished game with a gold line and its own label, the others with the plain progress line', async () => {
    const { container } = renderAt('/')
    await waitFor(() => expect(container.querySelectorAll('.pv-tile').length).toBe(2))
    const tiles = [...container.querySelectorAll<HTMLElement>('.pv-tile')]
    const platinum = tiles.find((tile) => tile.textContent?.includes('Alpha'))!
    const open = tiles.find((tile) => tile.textContent?.includes('Bravo'))!
    expect(platinum.getAttribute('data-platin')).toBe('true')
    expect(platinum.querySelector('.pv-pbar')?.getAttribute('data-complete')).toBe('true')
    // The label of the progress line and the plaque on the cover
    expect(within(platinum).getAllByText('Platin').length).toBe(2)
    expect(platinum.querySelector('.pv-tile-cover .pv-plat-chip')).toBeTruthy()
    // The gold line lives on the trophy progress line at the bottom, only for the finished game
    expect(platinum.querySelector('.pv-pbar .pv-plat-line')).toBeTruthy()
    expect(open.querySelector('.pv-plat-line')).toBeNull()
    expect(open.getAttribute('data-platin')).toBeNull()
    expect(open.querySelector('.pv-pbar')?.getAttribute('data-complete')).toBeNull()
    expect(within(open).getByText(/3 \/ 12/)).toBeTruthy()
    // The whole tile opens the game: the text and the cover both link to its page
    expect(open.querySelectorAll('a[href="/games/game-2"]').length).toBe(2)
  })

  it('shows the list sizes as one hairline list with column heads, a star per row and the values', async () => {
    localStorage.setItem('pv_dashboard_density', '1')
    const { container } = renderAt('/')
    await waitFor(() => expect(container.querySelectorAll('.pv-dl-row').length).toBe(2))
    const list = container.querySelector<HTMLElement>('.pv-dl')!
    for (const head of ['Titel', 'Spielzeit', 'Zuletzt gespielt', 'Trophäen', 'Größe']) {
      expect(within(list.querySelector<HTMLElement>('.pv-dl-head')!).getByText(head)).toBeTruthy()
    }
    const rows = [...container.querySelectorAll<HTMLElement>('.pv-dl-row')]
    // A star that removes the game from the favourites, and the title as the link of the row
    expect(within(rows[0]!).getByRole('button', { name: /„Alpha“ aus den Favoriten nehmen/ }).getAttribute('aria-pressed')).toBe('true')
    expect(within(rows[0]!).getByRole('link', { name: 'Alpha' }).getAttribute('href')).toBe('/games/game-1')
    // Values are named for screen readers, a finished game says "Platin"
    expect(rows[0]!.textContent).toContain('Gespielt: 10 Std.')
    expect(rows[0]!.textContent).toContain('Platin')
    expect(rows[1]!.textContent).toContain('3 / 12')
  })

  it('shows "Alle Spiele" as a list too, with the figures of the whole library and a star to put a game on the dashboard', async () => {
    localStorage.setItem('pv_dashboard_density', '0')
    const { container } = renderAt('/library')
    await waitFor(() => expect(container.querySelectorAll('.pv-dl-row').length).toBe(2))
    expect(screen.getByRole('heading', { level: 1, name: 'Alle Spiele' })).toBeTruthy()
    const figures = container.querySelector<HTMLElement>('.pv-dfigs')!
    expect(within(figures).getByText('Spiele in der Bibliothek')).toBeTruthy()
    expect(within(figures).getByText(/13 \/ 22/)).toBeTruthy()
    const echo = [...container.querySelectorAll<HTMLElement>('.pv-dl-row')].find((row) => row.textContent?.includes('Echo'))!
    const star = within(echo).getByRole('button', { name: /„Echo“ aufs Dashboard/ })
    expect(star.getAttribute('aria-pressed')).toBe('false')
    // Text only: no cover in the row
    expect(echo.querySelector('.pv-dl-cover')).toBeNull()
    fireEvent.click(star)
  })
})
