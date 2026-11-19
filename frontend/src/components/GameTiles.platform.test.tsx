import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { applyLanguage } from '../i18n'
import type { GameSummaryDto, SteamOwnedGameDto } from '../api/types'
import { ConfirmProvider } from './ui/ConfirmDialog'
import { AiChoiceProvider } from './ai/AiChoice'
import { LaunchProvider } from './launch/LaunchProvider'
import { GameTile, LibraryTile, missingFacts, type TileDensity } from './GameTiles'

// The platform chip on every tile (all five sizes), a game added by hand on the tiles: no Play segment, the reasons for the facts it cannot have, and
// the trophy line before / without a researched list.

const game = (patch: Partial<GameSummaryDto>): GameSummaryDto =>
  ({
    id: 'id-1',
    slug: 'stellar-blade',
    title: 'Stellar Blade',
    subtitle: '',
    steamAppId: null,
    coverUrl: '',
    heroBannerUrl: '',
    accentColor: 'emerald',
    estimatedHours: '',
    missablesInfo: '',
    difficultyInfo: '',
    genreTags: '',
    contentLanguage: 'de',
    totalCheckableItems: 0,
    checkedItemsCount: 0,
    progressPercent: 0,
    tabCount: 0,
    trophiesUnlocked: 0,
    trophiesTotal: 0,
    trophiesPercent: 0,
    platinumAt: null,
    trophiesCheckedAt: null,
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
    updatedAt: '2026-11-19T10:00:00Z',
    platform: 'playstation',
    platformVariant: 'ps5',
    ...patch,
  }) as GameSummaryDto

function renderTile(ui: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 404, headers: { 'Content-Type': 'application/json' } })))
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ConfirmProvider>
          <AiChoiceProvider>
            <LaunchProvider>{ui}</LaunchProvider>
          </AiChoiceProvider>
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

beforeEach(async () => {
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

const DENSITIES: TileDensity[] = [0, 1, 2, 3, 4]

describe('platform chip on the tiles', () => {
  it.each(DENSITIES)('shows the chip of a game added by hand at density %i, in the right place', (density) => {
    const { container } = renderTile(<GameTile game={game({})} density={density} onToggleFavorite={() => undefined} />)
    const chips = container.querySelectorAll('.pv-platform-badge')
    expect(chips).toHaveLength(1)
    expect(chips[0]!.textContent).toContain('PS5')
    expect(chips[0]!.getAttribute('data-platform')).toBe('playstation')
    if (density >= 2) {
      // On the cover, top left, absolutely positioned: the tile never changes its height for it
      expect(chips[0]!.closest('.pv-platform-badge-pos')?.parentElement?.className).toContain('pv-tile-cover')
      expect(chips[0]!.getAttribute('data-size')).toBe('sm')
    } else {
      // In the list: before the title (text only) / under the title (list)
      expect(chips[0]!.getAttribute('data-size')).toBe('md')
      expect(density === 1 ? chips[0]!.closest('.pv-dl-sub') : chips[0]!.closest('.pv-dl-c-title')).not.toBeNull()
    }
  })

  it.each(DENSITIES)('shows "Steam" on a game of the Steam library at density %i (no platform sent = Steam)', (density) => {
    const { container } = renderTile(<GameTile game={game({ platform: undefined, platformVariant: undefined, steamAppId: 220 })} density={density} onToggleFavorite={() => undefined} />)
    expect(container.querySelector('.pv-platform-badge')?.textContent).toContain('Steam')
  })

  it.each(DENSITIES)('shows "Steam" on a tile of the whole Steam library at density %i', (density) => {
    const owned: SteamOwnedGameDto = { appId: 5, name: 'Echo', playtimeHours: 1, coverUrl: '', heroBannerUrl: '', headerImageUrl: '', alreadyInVault: false, existingSlug: null, isFavorite: false, lastPlayedAt: null }
    const { container } = renderTile(<LibraryTile game={owned} density={density} busy={false} onToggleFavorite={() => undefined} />)
    expect(container.querySelectorAll('.pv-platform-badge')).toHaveLength(1)
    expect(container.querySelector('.pv-platform-badge')?.textContent).toContain('Steam')
  })

  it('names the platform for screen readers on the tile link, and writes the platform out in the chip text for the others', () => {
    const { container } = renderTile(<GameTile game={game({ platform: 'xbox', platformVariant: null })} density={4} onToggleFavorite={() => undefined} />)
    const link = container.querySelector('a.pv-tile-link')!
    const labelledBy = link.getAttribute('aria-labelledby')!.split(' ')
    expect(labelledBy).toHaveLength(2)
    expect(labelledBy.map((id) => document.getElementById(id)?.textContent)).toEqual(['Stellar Blade', 'Xbox'])
  })

  it('never offers Play for a game added by hand (there is nothing to start)', () => {
    const { container } = renderTile(<GameTile game={game({})} density={4} onToggleFavorite={() => undefined} />)
    expect(container.querySelector('.pv-play-button, [data-play]')).toBeNull()
    expect(screen.queryByText('Spielen')).toBeNull()
    // The star and Twitch stay
    expect(container.querySelector('.pv-bar-seg--star')).not.toBeNull()
  })
})

describe('a game added by hand on the tile', () => {
  it('says why playtime, last played and size are missing: they are known for Steam games only', () => {
    renderTile(<GameTile game={game({})} density={4} onToggleFavorite={() => undefined} />)
    const reason = 'Nur bei Steam-Spielen bekannt – dieses Spiel hast du von Hand hinzugefügt'
    expect(screen.getByText(`Gespielt: ${reason}`)).toBeTruthy()
    expect(screen.getByText(`Zuletzt gespielt: ${reason}`)).toBeTruthy()
    expect(screen.getByText(`Größe auf der Platte: ${reason}`)).toBeTruthy()
    // The release date has its own reason (it comes from IGDB or the research, not from Steam)
    expect(screen.getByText('Erschienen: Keine Angabe – das Datum kommt aus IGDB oder aus der KI-Recherche')).toBeTruthy()
  })

  it('uses the same reasons in missingFacts, never "Steam" ones', () => {
    expect(missingFacts(game({}))).toEqual({ played: 'notSteam', last: 'notSteam', size: 'notSteam', release: 'manualRelease', hours: 'estimate' })
    // IGDB brought the date and the time to beat
    expect(missingFacts(game({ releaseDate: '06.09.2024', completionHours: 14 }))).toEqual({ played: 'notSteam', last: 'notSteam', size: 'notSteam' })
    // A Steam game keeps its own reasons
    expect(missingFacts(game({ platform: undefined, steamAppId: 1 }))).toMatchObject({ played: 'steam', last: 'steam', size: 'size', release: 'steam' })
  })

  it('says "Noch keine Liste" before the trophy list is researched, and "Nur Guide" on the Switch', () => {
    const { rerender } = renderTile(<GameTile game={game({})} density={4} onToggleFavorite={() => undefined} />)
    expect(screen.getByText('Noch keine Liste')).toBeTruthy()
    const client = new QueryClient()
    rerender(
      <QueryClientProvider client={client}>
        <MemoryRouter>
          <ConfirmProvider>
            <AiChoiceProvider>
              <LaunchProvider>
                <GameTile game={game({ platform: 'nintendo', platformVariant: null })} density={4} onToggleFavorite={() => undefined} />
              </LaunchProvider>
            </AiChoiceProvider>
          </ConfirmProvider>
        </MemoryRouter>
      </QueryClientProvider>
    )
    expect(screen.getByText('Nur Guide')).toBeTruthy()
  })

  it('calls them achievements on platforms that do not have trophies, trophies on PlayStation', () => {
    const { container, rerender } = renderTile(<GameTile game={game({ trophiesTotal: 10, trophiesUnlocked: 4, trophiesPercent: 40 })} density={4} onToggleFavorite={() => undefined} />)
    expect(container.querySelector('.pv-tile-prog-k')?.textContent).toBe('Trophäen')
    const client = new QueryClient()
    rerender(
      <QueryClientProvider client={client}>
        <MemoryRouter>
          <ConfirmProvider>
            <AiChoiceProvider>
              <LaunchProvider>
                <GameTile game={game({ platform: 'xbox', platformVariant: null, trophiesTotal: 10, trophiesUnlocked: 4, trophiesPercent: 40 })} density={4} onToggleFavorite={() => undefined} />
              </LaunchProvider>
            </AiChoiceProvider>
          </ConfirmProvider>
        </MemoryRouter>
      </QueryClientProvider>
    )
    expect(container.querySelector('.pv-tile-prog-k')?.textContent).toBe('Erfolge')
  })

  it('shows the platinum plaque from platinumAt even when the counts say less (the server decides: PlayStation platinum trophy)', () => {
    const { container } = renderTile(<GameTile game={game({ platinumAt: '2026-11-18T20:00:00Z', trophiesTotal: 40, trophiesUnlocked: 40 })} density={4} onToggleFavorite={() => undefined} />)
    expect(container.querySelector('.pv-plat-chip')).not.toBeNull()
    expect(container.querySelector('.pv-plat-chip-label')?.textContent).toBe('Platin')
    // The platform chip and the plaque are both on the cover
    expect(container.querySelector('.pv-tile-cover .pv-platform-badge')).not.toBeNull()
    expect(container.querySelector('.pv-tile-cover .pv-plat-chip')).not.toBeNull()
  })
})
