import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { applyLanguage } from '../i18n'
import type { GameSummaryDto, SteamOwnedGameDto } from '../api/types'
import { ConfirmProvider } from '../components/ui/ConfirmDialog'
import { LaunchProvider } from '../components/launch/LaunchProvider'
import { AiChoiceProvider } from '../components/ai/AiChoice'
import { DashboardPage } from './DashboardPage'

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
    updatedAt: '2026-11-01T10:00:00Z',
    ...patch,
  }) as GameSummaryDto

// Manual order (as the server sends it): Charlie, Alpha, Bravo
const FAVORITES = [
  game(3, { title: 'Charlie', playtimeMinutes: 600, sortMetacritic: 70, sizeOnDiskBytes: 48.3 * 1024 ** 3 }),
  game(1, { title: 'Alpha', playtimeMinutes: 60, sortMetacritic: 90, installSizeBytes: 20.5 * 1024 ** 3 }),
  game(2, { title: 'Bravo', playtimeMinutes: null, sortMetacritic: null }),
]

const LIBRARY: SteamOwnedGameDto[] = [
  { appId: 5, name: 'Echo', playtimeHours: 1, coverUrl: '', heroBannerUrl: '', headerImageUrl: '', alreadyInVault: false, existingSlug: null, isFavorite: false, lastPlayedAt: '2026-10-01T00:00:00Z', sizeOnDiskBytes: 5 * 1024 ** 3 },
  { appId: 6, name: 'Delta', playtimeHours: 2, coverUrl: '', heroBannerUrl: '', headerImageUrl: '', alreadyInVault: false, existingSlug: null, isFavorite: false, lastPlayedAt: '2026-10-20T00:00:00Z', sizeOnDiskBytes: null, installSizeBytes: 3.5 * 1024 ** 3 },
]

function stubServer(settings: Record<string, unknown>) {
  const puts: unknown[] = []
  const answers: Record<string, unknown> = {
    '/api/branding': { productName: 'PlatinVault', tagline: '', host: 'server' },
    '/api/steam/profile': { steamId: null, personaName: null, avatarUrl: null, profileUrl: null, libraryGameCount: null },
    '/api/live/status': { isRunning: false, runningGameTracked: false },
    '/api/games': FAVORITES,
    '/api/steam/library/summary': {
      libraryGameCount: 2, gamesWithAchievements: 0, trophiesUnlocked: 0, trophiesTotal: 0, perfectGames: 0,
      scanning: false, scanDone: 0, scanTotal: 0, metaScanning: false, metaDone: 0, metaTotal: 0,
    },
    '/api/steam/library': { resolvedSteamId: '1', totalGames: 2, games: LIBRARY },
    '/api/settings': settings,
  }
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const path = url.split('?')[0] ?? url
      if (init?.method === 'PUT' && path === '/api/settings') {
        const patch = JSON.parse(String(init.body)) as Record<string, unknown>
        puts.push(patch)
        Object.assign(settings, patch)
        return new Response(JSON.stringify(settings), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      if (path in answers) {
        return new Response(JSON.stringify(answers[path]), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      return new Response('{}', { status: 404, headers: { 'Content-Type': 'application/json' } })
    })
  )
  return puts
}

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ConfirmProvider>
          <AiChoiceProvider>
            <LaunchProvider>
              <DashboardPage theme="light" onToggleTheme={() => undefined} />
            </LaunchProvider>
          </AiChoiceProvider>
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const sortButton = () => screen.getByRole('button', { name: /^(Sortieren nach|Sort by):/ })

describe('DashboardPage sort orders', () => {
  beforeEach(async () => {
    localStorage.clear()
    // The tour and the hints are not what is tested here
    localStorage.setItem('pv_tour_done_v1', '1')
    await applyLanguage('de')
    // jsdom has none of these; the page only needs them to exist
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query, addEventListener: () => undefined, removeEventListener: () => undefined, addListener: () => undefined, removeListener: () => undefined, onchange: null, dispatchEvent: () => false }))
    const Observer = class { observe() {} unobserve() {} disconnect() {} takeRecords() { return [] } }
    vi.stubGlobal('IntersectionObserver', Observer)
    vi.stubGlobal('ResizeObserver', Observer)
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('shows the favourites in the manual order, with dragging on, and says so', async () => {
    stubServer({ dashboardSortFavorites: 'custom:asc', dashboardSortAll: 'lastPlayed:desc' })
    renderPage()
    await waitFor(() => expect(screen.getAllByRole('group', { name: /verschiebbar|can be moved/i }).length).toBeGreaterThan(0))
    const text = document.body.textContent ?? ''
    expect(text.indexOf('Charlie')).toBeLessThan(text.indexOf('Alpha'))
    expect(text.indexOf('Alpha')).toBeLessThan(text.indexOf('Bravo'))
    expect(screen.getAllByText(/Sortiert: Eigene Reihenfolge/).length).toBeGreaterThan(0)
    expect(screen.getByLabelText('Karten lassen sich per Ziehen sortieren')).toBeTruthy()
  })

  it('sorts by the chosen order, puts games without a value last and switches dragging off', async () => {
    const puts = stubServer({ dashboardSortFavorites: 'custom:asc', dashboardSortAll: 'lastPlayed:desc' })
    renderPage()
    await waitFor(() => expect(sortButton()).toBeTruthy())
    await waitFor(() => expect(document.body.textContent).toContain('Charlie'))

    fireEvent.click(sortButton())
    fireEvent.click(screen.getByRole('menuitemradio', { name: /Metacritic/ }))

    // Metacritic, best first: Alpha 90, Charlie 70, and Bravo (no score) at the end
    await waitFor(() => {
      const text = document.body.textContent ?? ''
      expect(text.indexOf('Alpha')).toBeLessThan(text.indexOf('Charlie'))
      expect(text.indexOf('Charlie')).toBeLessThan(text.indexOf('Bravo'))
    })
    // Reversed, the game without a score is STILL last
    fireEvent.click(screen.getByRole('button', { name: /^Richtung umkehren/ }))
    await waitFor(() => {
      const text = document.body.textContent ?? ''
      expect(text.indexOf('Charlie')).toBeLessThan(text.indexOf('Alpha'))
      expect(text.indexOf('Alpha')).toBeLessThan(text.indexOf('Bravo'))
    })

    // Dragging only exists in the manual order
    expect(screen.queryByRole('group', { name: /verschiebbar|can be moved/i })).toBeNull()
    expect(screen.getByLabelText('Ziehen ist aus, eine andere Sortierung ist gewählt')).toBeTruthy()
    expect(screen.getByText(/1 Spiel ohne Wert steht am Ende/)).toBeTruthy()

    // Saved on the server, field by field
    await waitFor(() => expect(puts).toEqual([{ dashboardSortFavorites: 'metacritic:desc' }, { dashboardSortFavorites: 'metacritic:asc' }]))
  })

  it('brings the manual order and dragging back with "Eigene Reihenfolge"', async () => {
    stubServer({ dashboardSortFavorites: 'alphabetical:asc', dashboardSortAll: 'lastPlayed:desc' })
    renderPage()
    await waitFor(() => expect(document.body.textContent).toContain('Charlie'))
    expect(screen.queryByRole('group', { name: /verschiebbar|can be moved/i })).toBeNull()
    // Alphabetical: Alpha, Bravo, Charlie
    const text = document.body.textContent ?? ''
    expect(text.indexOf('Alpha')).toBeLessThan(text.indexOf('Bravo'))

    fireEvent.click(sortButton())
    fireEvent.click(screen.getByRole('menuitemradio', { name: /Eigene Reihenfolge/ }))
    await waitFor(() => expect(screen.getAllByRole('group', { name: /verschiebbar|can be moved/i }).length).toBeGreaterThan(0))
    const again = document.body.textContent ?? ''
    expect(again.indexOf('Charlie')).toBeLessThan(again.indexOf('Alpha'))
  })

  it('keeps its own order per view: "Alle Spiele" has no manual order and starts with the last played', async () => {
    stubServer({ dashboardSortFavorites: 'custom:asc', dashboardSortAll: 'lastPlayed:desc' })
    renderPage()
    const viewSwitch = await screen.findByRole('group', { name: /Welche Spiele angezeigt werden/ })
    fireEvent.click(within(viewSwitch).getAllByRole('button')[1]!)

    await waitFor(() => expect(document.body.textContent).toContain('Echo'))
    expect(sortButton().textContent).toContain('Zuletzt gespielt')
    // Delta was played after Echo
    const text = document.body.textContent ?? ''
    expect(text.indexOf('Delta')).toBeLessThan(text.indexOf('Echo'))

    // Size: Echo has one, Delta has none - Delta last in both directions
    fireEvent.click(sortButton())
    expect(screen.queryByRole('menuitemradio', { name: /Eigene Reihenfolge/ })).toBeNull()
    fireEvent.click(screen.getByRole('menuitemradio', { name: /^Größe/ }))
    // Echo is installed (5 GB), Delta is not (estimated 3.5 GB): both have a size, the biggest first
    await waitFor(() => {
      const bySize = document.body.textContent ?? ''
      expect(bySize.indexOf('Echo')).toBeLessThan(bySize.indexOf('Delta'))
    })
    fireEvent.click(screen.getByRole('button', { name: /^Richtung umkehren/ }))
    await waitFor(() => {
      const after = document.body.textContent ?? ''
      expect(after.indexOf('Delta')).toBeLessThan(after.indexOf('Echo'))
    })
  })

  it('shows the size on every favourite card: real size, estimated install size, or a dash with its reason - and no delete button', async () => {
    stubServer({ dashboardSortFavorites: 'custom:asc', dashboardSortAll: 'lastPlayed:desc' })
    renderPage()
    await waitFor(() => expect(document.body.textContent).toContain('Charlie'))
    const text = document.body.textContent ?? ''
    // Charlie is installed: its real size, Steam's way of writing it (GB of 1024^3)
    expect(text).toContain('48,3 GB auf der Platte')
    // Alpha is not installed: the estimate says so
    expect(text).toContain('ca. 20,5 GB Installationsgröße')
    // Bravo has no source: the row keeps its place and says why
    expect(text).toContain('Größe auf der Platte: Größe unbekannt')
    // Three buttons per card: Play (without a bridge there is none), Favourite, Twitch - never a delete button
    expect(screen.queryByRole('button', { name: /löschen|delete/i })).toBeNull()
    expect(screen.getAllByRole('button', { name: /Favorit/ }).length).toBeGreaterThan(0)
  })

  it('shows the same fact rows on the cards of the whole library', async () => {
    stubServer({ dashboardSortFavorites: 'custom:asc', dashboardSortAll: 'lastPlayed:desc' })
    renderPage()
    const viewSwitch = await screen.findByRole('group', { name: /Welche Spiele angezeigt werden/ })
    fireEvent.click(within(viewSwitch).getAllByRole('button')[1]!)
    await waitFor(() => expect(document.body.textContent).toContain('Echo'))
    const text = document.body.textContent ?? ''
    expect(text).toContain('5 GB auf der Platte')
    expect(text).toContain('ca. 3,5 GB Installationsgröße')
    // The rating row of a game without Steam rating says why it is a dash
    expect(text).toContain('Bewertung: Keine Angabe von Steam')
    expect(screen.queryByRole('button', { name: /löschen|delete/i })).toBeNull()
  })

  it('says that values are still loading while the server looks them up', async () => {
    stubServer({ dashboardSortFavorites: 'release:desc', dashboardSortAll: 'lastPlayed:desc' })
    const original = globalThis.fetch
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      if (String(url).startsWith('/api/steam/library/summary')) {
        return new Response(JSON.stringify({
          libraryGameCount: 2, gamesWithAchievements: 0, trophiesUnlocked: 0, trophiesTotal: 0, perfectGames: 0,
          scanning: false, scanDone: 0, scanTotal: 0, metaScanning: true, metaDone: 120, metaTotal: 350,
        }), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      return original(url, init)
    }))
    renderPage()
    await waitFor(() => expect(screen.getByText(/Die Werte werden noch geladen \(120 von 350\)/)).toBeTruthy())
    expect(sortButton().getAttribute('aria-busy')).toBe('true')
  })

})
