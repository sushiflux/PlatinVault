import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { applyLanguage } from '../i18n'
import type { GameDetailDto, GuideTabDto } from '../api/types'
import { ConfirmProvider } from '../components/ui/ConfirmDialog'
import { AiChoiceProvider } from '../components/ai/AiChoice'
import { statusFixture } from '../components/ai/ai.fixtures'
import { GameGuidePage } from './GameGuidePage'

// The head of the game page: the back button on top and the area bar (Guide | Live trophies | Statistics) at the bottom. The areas are
// no entries of the side navigation any more; the URL (?tab=) stays the source of truth for the shown area.

const client = vi.hoisted(() => ({
  fetchGameDetail: vi.fn(),
  fetchAiStatus: vi.fn(),
  fetchGuideTopics: vi.fn(),
  fetchAiJobsForGame: vi.fn(),
  fetchLiveAchievements: vi.fn(),
  fetchLiveStatus: vi.fn(),
}))
vi.mock('../api/client', async (importOriginal) => ({ ...(await importOriginal<typeof import('../api/client')>()), ...client }))

vi.mock('../components/GameStatsPanel', () => ({ GameStatsPanel: () => null }))
vi.mock('../components/LiveTrackerPanel', () => ({ LiveTrackerPanel: () => null }))
vi.mock('../components/TwitchButton', () => ({ TwitchButton: () => null }))
vi.mock('../components/launch/PlayButton', () => ({ PlayButton: () => null }))
vi.mock('../components/GameTrophyBar', () => ({ GameTrophyBar: () => null }))
vi.mock('../components/GameHeadStatus', () => ({ GameHeadStatus: () => null }))
vi.mock('../components/pins/PinsSurface', () => ({ PinsSurface: () => null }))
vi.mock('../components/onboarding/HintTour', () => ({ HintTour: () => null }))
vi.mock('../utils/useGuideTranslation', () => ({ useGuideTranslation: () => ({ translating: false, failure: null, retry: () => {} }) }))

const tab = (id: string, slug: string, title: string): GuideTabDto =>
  ({ id, slug, title, icon: 'mdi:book', sortOrder: 1, totalCheckableItems: 0, checkedItemsCount: 0, isAchievementList: false, groupKey: 'extras', sections: [] }) as GuideTabDto

const game = (steamAppId: number | null): GameDetailDto =>
  ({
    id: 'g1', slug: 'suncraft', title: 'Suncraft', subtitle: '', steamAppId, coverUrl: '', heroBannerUrl: '', accentColor: 'emerald', estimatedHours: '', missablesInfo: '',
    difficultyInfo: '', genreTags: '', contentLanguage: 'de', totalCheckableItems: 0, checkedItemsCount: 0, progressPercent: 0,
    tabs: [tab('t1', 'roadmap', 'Roadmap'), tab('t2', 'tips', 'Tipps'), tab('t3', 'story-lore', 'Story & Lore')],
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

const areaBar = () => screen.findByRole('group', { name: 'Bereich des Spiels' })
const location = () => screen.getByTestId('location').textContent

describe('the area bar in the game head', () => {
  it('offers Guide, Live-Trophäen and Statistiken as one connected bar for a game with a Steam link', async () => {
    renderPage(game(10), ['/games/suncraft?tab=tips'])
    const bar = await areaBar()
    expect(bar.classList.contains('pv-segmented')).toBe(true)
    expect(within(bar).getAllByRole('button')).toHaveLength(3)
    expect(within(bar).getByRole('button', { name: 'Guide' }).getAttribute('aria-pressed')).toBe('true')
    expect(within(bar).getByRole('button', { name: 'Live-Trophäen' }).getAttribute('aria-pressed')).toBe('false')
    expect(within(bar).getByRole('button', { name: 'Statistiken' }).getAttribute('aria-pressed')).toBe('false')
    // The coach mark of the live trophies points at its segment
    expect(within(bar).getByRole('button', { name: 'Live-Trophäen' }).getAttribute('data-hint')).toBe('game-live')
  })

  it('holds the room of the selected weight in every segment, so the bar never changes its width', async () => {
    renderPage(game(10), ['/games/suncraft?tab=tips'])
    const bar = await areaBar()
    for (const button of within(bar).getAllByRole('button')) {
      // An invisible copy in weight 600 under the label, hidden from screen readers, in both languages
      const copy = button.querySelector('.font-semibold.invisible')
      expect(copy?.getAttribute('aria-hidden')).toBe('true')
      expect(copy?.textContent?.length ?? 0).toBeGreaterThan(3)
    }
    // The icons carry the class the phone layout hides
    expect(bar.querySelectorAll('.pv-game-area-icon')).toHaveLength(3)
  })

  it('is not there at all without a Steam link (and the page shows the guide)', async () => {
    renderPage(game(null), ['/games/suncraft'])
    await screen.findByRole('heading', { name: 'Suncraft' })
    expect(screen.queryByRole('group', { name: 'Bereich des Spiels' })).toBeNull()
    expect(document.querySelector('[data-hint="game-live"]')).toBeNull()
  })

  it('switches to the live trophies and the statistics through the URL, and marks the shown area', async () => {
    renderPage(game(10), ['/games/suncraft?tab=tips'])
    const bar = await areaBar()
    fireEvent.click(within(bar).getByRole('button', { name: 'Live-Trophäen' }))
    await waitFor(() => expect(location()).toBe('/games/suncraft?tab=live'))
    expect(within(bar).getByRole('button', { name: 'Live-Trophäen' }).getAttribute('aria-pressed')).toBe('true')
    expect(within(bar).getByRole('button', { name: 'Guide' }).getAttribute('aria-pressed')).toBe('false')
    fireEvent.click(within(bar).getByRole('button', { name: 'Statistiken' }))
    await waitFor(() => expect(location()).toBe('/games/suncraft?tab=stats'))
    expect(within(bar).getByRole('button', { name: 'Statistiken' }).getAttribute('aria-pressed')).toBe('true')
  })

  it('shows the bar on a reload straight onto the statistics (the URL is the source of truth)', async () => {
    renderPage(game(10), ['/games/suncraft?tab=stats'])
    const bar = await areaBar()
    expect(within(bar).getByRole('button', { name: 'Statistiken' }).getAttribute('aria-pressed')).toBe('true')
  })

  it('Guide returns to the last guide tab that was open', async () => {
    renderPage(game(10), ['/games/suncraft?tab=tips'])
    const bar = await areaBar()
    fireEvent.click(within(bar).getByRole('button', { name: 'Live-Trophäen' }))
    await waitFor(() => expect(location()).toBe('/games/suncraft?tab=live'))
    fireEvent.click(within(bar).getByRole('button', { name: 'Guide' }))
    // "Tipps", not the first chapter "Roadmap"
    await waitFor(() => expect(location()).toBe('/games/suncraft?tab=tips'))
    expect(within(bar).getByRole('button', { name: 'Guide' }).getAttribute('aria-pressed')).toBe('true')
  })

  it('Guide also returns to Story & Lore, which counts as part of the guide', async () => {
    renderPage(game(10), ['/games/suncraft?tab=story'])
    const bar = await areaBar()
    expect(within(bar).getByRole('button', { name: 'Guide' }).getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(within(bar).getByRole('button', { name: 'Statistiken' }))
    await waitFor(() => expect(location()).toBe('/games/suncraft?tab=stats'))
    fireEvent.click(within(bar).getByRole('button', { name: 'Guide' }))
    await waitFor(() => expect(location()).toBe('/games/suncraft?tab=story'))
  })

  it('Guide falls back to the first chapter when opened straight on the live trophies', async () => {
    renderPage(game(10), ['/games/suncraft?tab=live'])
    const bar = await areaBar()
    fireEvent.click(within(bar).getByRole('button', { name: 'Guide' }))
    await waitFor(() => expect(location()).toBe('/games/suncraft?tab=roadmap'))
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

  it('changing the area adds no history entry: one step back still leaves the game', async () => {
    renderPage(game(10), ['/', '/games/suncraft?tab=tips'])
    const bar = await areaBar()
    fireEvent.click(within(bar).getByRole('button', { name: 'Live-Trophäen' }))
    await waitFor(() => expect(location()).toBe('/games/suncraft?tab=live'))
    fireEvent.click(screen.getByRole('button', { name: 'Zurück' }))
    await screen.findByText('overview-page')
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
