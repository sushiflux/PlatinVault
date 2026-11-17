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

// The three rating orders (IGDB critics, IGDB players, overall rating) in both views of the dashboard.

const game = (n: number, patch: Partial<GameSummaryDto>): GameSummaryDto =>
  ({
    id: `id-${n}`, slug: `game-${n}`, title: `Game ${n}`, subtitle: '', steamAppId: 1000 + n, coverUrl: '', heroBannerUrl: '', accentColor: 'emerald', estimatedHours: '',
    missablesInfo: '', difficultyInfo: '', genreTags: '', contentLanguage: 'de', steamGenres: [], totalCheckableItems: 0, checkedItemsCount: 0, progressPercent: 0,
    tabCount: 0, trophiesUnlocked: 0, trophiesTotal: 0, trophiesPercent: 0, platinumAt: null, trophiesCheckedAt: '2026-11-01T10:00:00Z', isFavorite: true,
    playtimeMinutes: null, lastPlayedAt: null, metacriticScore: null, reviewScoreDesc: null, reviewPercent: null, releaseDate: null, completionHours: null,
    igdbRating: null, igdbUserRating: null, updatedAt: '2026-11-01T10:00:00Z', ...patch,
  }) as GameSummaryDto

// Manual order: Charlie, Alpha, Bravo
const FAVORITES = [
  game(3, { title: 'Charlie', reviewPercent: 70, metacriticScore: 60, igdbRating: 65, igdbUserRating: 55 }),
  game(1, { title: 'Alpha', reviewPercent: 90, igdbRating: 95, igdbUserRating: 50 }),
  game(2, { title: 'Bravo' }),
]

const owned = (appId: number, name: string, patch: Partial<SteamOwnedGameDto>): SteamOwnedGameDto =>
  ({ appId, name, playtimeHours: 1, coverUrl: '', heroBannerUrl: '', headerImageUrl: '', alreadyInVault: false, existingSlug: null, isFavorite: false, lastPlayedAt: null, ...patch }) as SteamOwnedGameDto

// Library, without played dates (the default order then falls back to the title): Echo, Foxtrot, Golf
const LIBRARY = [
  owned(5, 'Echo', { sortReviewPercent: 80, sortMetacritic: 90, igdbRating: 40, igdbUserRating: 99 }),
  owned(6, 'Foxtrot', { sortReviewPercent: 95 }),
  owned(7, 'Golf', {}),
]

function stubServer(settings: Record<string, unknown>) {
  const answers: Record<string, unknown> = {
    '/api/branding': { productName: 'PlatinVault', tagline: '', host: 'server' },
    '/api/steam/profile': { steamId: null, personaName: null, avatarUrl: null, profileUrl: null, libraryGameCount: null },
    '/api/live/status': { isRunning: false, runningGameTracked: false },
    '/api/games': FAVORITES,
    '/api/steam/library/summary': { libraryGameCount: 3, gamesWithAchievements: 0, trophiesUnlocked: 0, trophiesTotal: 0, perfectGames: 0, scanning: false, scanDone: 0, scanTotal: 0, metaScanning: false, metaDone: 0, metaTotal: 0 },
    '/api/steam/library': { resolvedSteamId: '1', totalGames: 3, games: LIBRARY },
    '/api/settings': settings,
  }
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const path = url.split('?')[0] ?? url
      if (init?.method === 'PUT' && path === '/api/settings') {
        Object.assign(settings, JSON.parse(String(init.body)))
        return new Response(JSON.stringify(settings), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      if (path in answers) return new Response(JSON.stringify(answers[path]), { status: 200, headers: { 'Content-Type': 'application/json' } })
      return new Response('{}', { status: 404, headers: { 'Content-Type': 'application/json' } })
    })
  )
}

function renderAt(path: string) {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
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

// The titles of the tiles in page order
const order = (container: HTMLElement) =>
  [...container.querySelectorAll<HTMLElement>('.pv-tile')].map((tile) => /Alpha|Bravo|Charlie|Echo|Foxtrot|Golf/.exec(tile.textContent ?? '')?.[0])

describe('DashboardPage rating orders', () => {
  beforeEach(async () => {
    localStorage.clear()
    localStorage.setItem('pv_tour_done_v1', '1')
    await applyLanguage('de')
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query, addEventListener: () => undefined, removeEventListener: () => undefined, addListener: () => undefined, removeListener: () => undefined, onchange: null, dispatchEvent: () => false }))
    const Observer = class { observe() {} unobserve() {} disconnect() {} takeRecords() { return [] } }
    vi.stubGlobal('IntersectionObserver', Observer)
    vi.stubGlobal('ResizeObserver', Observer)
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it.each([
    // Critics: Alpha 95, Charlie 65, Bravo none
    ['IGDB-Kritiker', 'igdbCritics', ['Alpha', 'Charlie', 'Bravo']],
    // Players: Charlie 55, Alpha 50
    ['IGDB-Spieler', 'igdbPlayers', ['Charlie', 'Alpha', 'Bravo']],
    // Overall: Alpha (90+95+50)/3 = 78.3, Charlie (70+60+65+55)/4 = 62.5
    ['Gesamtwertung', 'overall', ['Alpha', 'Charlie', 'Bravo']],
  ] as const)('favourites: %s, best first, games without a value at the end in both directions', async (label, id, expected) => {
    stubServer({ hasTwitchKey: true, dashboardSortFavorites: `${id}:desc`, dashboardSortAll: 'lastPlayed:desc' })
    const { container } = renderAt('/')
    await waitFor(() => expect(order(container)).toEqual([...expected]))
    expect(screen.getAllByText(new RegExp(`Sortiert: ${label} – `)).length).toBeGreaterThan(0)
    expect(screen.getByText(/1 Spiel ohne Wert steht am Ende/)).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: /^Richtung umkehren/ }))
    await waitFor(() => expect(order(container)).toEqual([expected[1], expected[0], 'Bravo']))
  })

  it.each([
    // Library: critics and players only Echo has; Overall: Foxtrot 95 (Steam only), Echo (80+90+40+99)/4 = 77.25, Golf none
    ['igdbCritics', ['Echo', 'Foxtrot', 'Golf'], 2],
    ['igdbPlayers', ['Echo', 'Foxtrot', 'Golf'], 2],
    ['overall', ['Foxtrot', 'Echo', 'Golf'], 1],
  ] as const)('library: %s, best first, games without a value at the end in both directions', async (id, expected, missing) => {
    stubServer({ hasTwitchKey: true, dashboardSortFavorites: 'custom:asc', dashboardSortAll: `${id}:desc` })
    const { container } = renderAt('/library')
    await waitFor(() => expect(order(container)).toEqual([...expected]))
    expect(screen.getAllByText(new RegExp(`${missing} Spiele? ohne Wert`)).length).toBeGreaterThan(0)

    fireEvent.click(screen.getByRole('button', { name: /^Richtung umkehren/ }))
    // Reversed: the games with a value turn around, the ones without stay at the end (ties by title)
    const reversed = id === 'overall' ? ['Echo', 'Foxtrot', 'Golf'] : ['Echo', 'Foxtrot', 'Golf']
    await waitFor(() => expect(order(container)).toEqual(reversed))
  })

  it('without Twitch credentials the IGDB sorts have no values and the overall rating ignores IGDB (like the tiles)', async () => {
    stubServer({ hasTwitchKey: false, dashboardSortFavorites: 'igdbCritics:desc', dashboardSortAll: 'lastPlayed:desc' })
    const { container } = renderAt('/')
    // No values at all: the order falls back to the titles, and the page says so
    await waitFor(() => expect(order(container)).toEqual(['Alpha', 'Bravo', 'Charlie']))
    expect(screen.getAllByText(/Dafür liegen noch keine Werte vor/).length).toBeGreaterThan(0)
  })

  it('offers the three orders in the menu of both views, with their own names in both languages', async () => {
    stubServer({ hasTwitchKey: true, dashboardSortFavorites: 'custom:asc', dashboardSortAll: 'lastPlayed:desc' })
    renderAt('/library')
    fireEvent.click(await screen.findByRole('button', { name: /^Sortieren nach:/ }))
    const menu = await screen.findByRole('menu')
    for (const name of [/^IGDB-Kritiker/, /^IGDB-Spieler/, /^Gesamtwertung/]) expect(within(menu).getByRole('menuitemradio', { name })).toBeTruthy()
    cleanup()

    await applyLanguage('en')
    stubServer({ hasTwitchKey: true, dashboardSortFavorites: 'custom:asc', dashboardSortAll: 'lastPlayed:desc' })
    renderAt('/')
    fireEvent.click(await screen.findByRole('button', { name: /^Sort by:/ }))
    const menuEn = await screen.findByRole('menu')
    for (const name of [/^IGDB critics/, /^IGDB players/, /^Overall rating/]) expect(within(menuEn).getByRole('menuitemradio', { name })).toBeTruthy()
  })
})
