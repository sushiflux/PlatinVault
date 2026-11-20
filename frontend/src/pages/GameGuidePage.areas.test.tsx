import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { applyLanguage } from '../i18n'
import type { GameDetailDto, GuideTabDto } from '../api/types'
import { ConfirmProvider } from '../components/ui/ConfirmDialog'
import { AiChoiceProvider } from '../components/ai/AiChoice'
import { statusFixture } from '../components/ai/ai.fixtures'
import { GameGuidePage } from './GameGuidePage'

// The head of the game page: the back button on top, no area bar any more. The areas (live trophies, statistics) and the guide are entries of
// the game navigation (components/shell, tested in AppShell.test.tsx); the page itself shows what ?tab= names, and the URL stays the source of truth.

const client = vi.hoisted(() => ({
  fetchGameDetail: vi.fn(),
  fetchAiStatus: vi.fn(),
  fetchGuideTopics: vi.fn(),
  fetchAiJobsForGame: vi.fn(),
  fetchLiveAchievements: vi.fn(),
  fetchLiveStatus: vi.fn(),
}))
vi.mock('../api/client', async (importOriginal) => ({ ...(await importOriginal<typeof import('../api/client')>()), ...client }))

vi.mock('../components/GameStatsPanel', () => ({ GameStatsPanel: () => <p>statistics-panel</p> }))
vi.mock('../components/LiveTrackerPanel', () => ({ LiveTrackerPanel: () => <p>live-panel</p> }))
vi.mock('../components/TwitchButton', () => ({ TwitchButton: () => null }))
vi.mock('../components/launch/PlayButton', () => ({ PlayButton: () => null }))
vi.mock('../components/GameTrophyBar', () => ({ GameTrophyBar: () => null }))
vi.mock('../components/GameHeadStatus', () => ({ GameHeadStatus: () => null }))
vi.mock('../components/pins/PinsSurface', () => ({ PinsSurface: () => null }))
vi.mock('../components/onboarding/HintTour', () => ({ HintTour: () => null }))
vi.mock('../utils/useGuideTranslation', () => ({ useGuideTranslation: () => ({ translating: false, failure: null, retry: () => {} }) }))

const tab = (id: string, slug: string, title: string): GuideTabDto =>
  ({ id, slug, title, icon: 'mdi:book', sortOrder: 1, totalCheckableItems: 0, checkedItemsCount: 0, isAchievementList: false, groupKey: 'extras', sections: [] }) as GuideTabDto

const game = (steamAppId: number | null, tabs: GuideTabDto[] = [tab('t1', 'roadmap', 'Roadmap'), tab('t2', 'tips', 'Tipps'), tab('t3', 'story-lore', 'Story & Lore')]): GameDetailDto =>
  ({
    id: 'g1', slug: 'suncraft', title: 'Suncraft', subtitle: '', steamAppId, coverUrl: '', heroBannerUrl: '', accentColor: 'emerald', estimatedHours: '', missablesInfo: '',
    difficultyInfo: '', genreTags: '', contentLanguage: 'de', totalCheckableItems: 0, checkedItemsCount: 0, progressPercent: 0,
    tabs,
    checkedItemKeys: [], language: 'de',
    translation: { language: 'de', sourceLanguage: 'de', state: 'original', totalTabs: 0, translatedTabs: 0, running: false, error: null, providerMessage: null },
  }) as unknown as GameDetailDto

function LocationProbe() {
  const location = useLocation()
  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>
}

function renderPage(detail: GameDetailDto, entries: string[]) {
  client.fetchGameDetail.mockResolvedValue(detail)
  client.fetchAiStatus.mockResolvedValue(statusFixture({ mode: 'default' }))
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ConfirmProvider>
        <AiChoiceProvider>
          <MemoryRouter initialEntries={entries} initialIndex={entries.length - 1}>
            <LocationProbe />
            <Routes>
              <Route path="/" element={<p>overview-page</p>} />
              <Route path="/library" element={<p>library-page</p>} />
              <Route path="/games/:slug" element={<GameGuidePage />} />
            </Routes>
          </MemoryRouter>
        </AiChoiceProvider>
      </ConfirmProvider>
    </QueryClientProvider>
  )
}

beforeEach(async () => {
  await applyLanguage('de')
  localStorage.clear()
  sessionStorage.clear()
  localStorage.setItem('pv_hints_seen_v1', JSON.stringify({ game: true }))
  Object.values(client).forEach((fn) => fn.mockReset())
  client.fetchGuideTopics.mockResolvedValue([])
  client.fetchAiJobsForGame.mockResolvedValue([])
  client.fetchLiveAchievements.mockResolvedValue(null)
  client.fetchLiveStatus.mockResolvedValue({ isRunning: false, runningGameTracked: false })
})
afterEach(cleanup)

const location = () => screen.getByTestId('location').textContent

describe('the game head without an area bar', () => {
  it('has no area bar and no coach mark target of its own, for a game with a Steam link', async () => {
    renderPage(game(10), ['/games/suncraft?tab=tips'])
    await screen.findByRole('heading', { name: 'Suncraft' })
    expect(screen.queryByRole('group', { name: 'Bereich des Spiels' })).toBeNull()
    expect(document.querySelector('.pv-game-areas, .pv-game-area-icon')).toBeNull()
    // The coach mark of the trophies lives on the entry of the game navigation now, which is not part of this page
    expect(document.querySelector('[data-hint="game-live"]')).toBeNull()
    // The head still has its pieces: the way back, the title and the action row
    expect(screen.getByRole('button', { name: 'Zurück' })).toBeTruthy()
    expect(document.querySelector('.pv-game-head .pv-game-actions')).toBeTruthy()
  })

  it('has no area bar for a game without a Steam link either (and the page shows the guide)', async () => {
    renderPage(game(null), ['/games/suncraft'])
    await screen.findByRole('heading', { name: 'Suncraft' })
    expect(screen.queryByRole('group', { name: 'Bereich des Spiels' })).toBeNull()
    expect(screen.getByText('Roadmap', { selector: 'h2, h1, h3' })).toBeTruthy()
  })
})

describe('the shown area follows the URL', () => {
  it('shows the live trophies for ?tab=live and the statistics for ?tab=stats of a game with a Steam link', async () => {
    renderPage(game(10), ['/games/suncraft?tab=live'])
    expect(await screen.findByText('live-panel')).toBeTruthy()
    cleanup()
    renderPage(game(10), ['/games/suncraft?tab=stats'])
    expect(await screen.findByText('statistics-panel')).toBeTruthy()
  })

  it('shows neither area without a Steam link, the guide instead', async () => {
    renderPage(game(null), ['/games/suncraft?tab=live'])
    await screen.findByRole('heading', { name: 'Suncraft' })
    expect(screen.queryByText('live-panel')).toBeNull()
    expect(screen.queryByText('statistics-panel')).toBeNull()
    expect(location()).toBe('/games/suncraft?tab=live')
  })

  it('shows the "no guide yet" card for ?tab=guide of a Steam game without any chapter, like for a game added by hand', async () => {
    renderPage(game(10, []), ['/games/suncraft?tab=guide'])
    expect(await screen.findByText('Für dieses Spiel gibt es noch keinen Guide.')).toBeTruthy()
    expect(screen.queryByText('live-panel')).toBeNull()
  })

  it('opens the live trophies first for a Steam game without any chapter when the URL names no tab', async () => {
    renderPage(game(10, []), ['/games/suncraft'])
    expect(await screen.findByText('live-panel')).toBeTruthy()
  })
})

describe('the back button in the game head', () => {
  it('steps back in the history when the player came from another page of the app', async () => {
    renderPage(game(10), ['/library', '/games/suncraft?tab=tips'])
    const back = await screen.findByRole('button', { name: 'Zurück' })
    fireEvent.click(back)
    await screen.findByText('library-page')
    expect(location()).toBe('/library')
  })

  it('goes to the overview when the game page was opened directly (nothing behind it)', async () => {
    renderPage(game(10), ['/games/suncraft?tab=tips'])
    fireEvent.click(await screen.findByRole('button', { name: 'Zurück' }))
    await screen.findByText('overview-page')
    expect(location()).toBe('/')
  })

  it('works for a game without a Steam link too', async () => {
    renderPage(game(null), ['/library', '/games/suncraft'])
    fireEvent.click(await screen.findByRole('button', { name: 'Zurück' }))
    await screen.findByText('library-page')
  })

  it('shows the tooltip "Zur vorherigen Seite"', async () => {
    renderPage(game(10), ['/games/suncraft'])
    const back = await screen.findByRole('button', { name: 'Zurück' })
    // Our own tooltip (never a native title)
    fireEvent.pointerEnter(back, { pointerType: 'mouse' })
    expect(await screen.findByText('Zur vorherigen Seite')).toBeTruthy()
    expect(back.getAttribute('title')).toBeNull()
  })
})
