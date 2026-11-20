import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { applyLanguage } from '../i18n'
import type { AiJobDto, GameDetailDto, GuideSectionDto, GuideTabDto, ManualTrophyListDto, PlatformId, PlatformVariant } from '../api/types'
import { ConfirmProvider } from '../components/ui/ConfirmDialog'
import { AiChoiceProvider } from '../components/ai/AiChoice'
import { statusFixture } from '../components/ai/ai.fixtures'
import { manualList, ps5Items } from '../components/trophies/trophies.fixtures'
import { GameGuidePage } from './GameGuidePage'

// The page of a game added by hand: the trophy area (Trophäen/Erfolge, no statistics), the trophy list ticked by hand, the research of the list, the
// head status instead of Play, the pins of the game id. A Steam game and a Switch game for contrast.

const client = vi.hoisted(() => ({
  fetchGameDetail: vi.fn(),
  fetchAiStatus: vi.fn(),
  fetchGuideTopics: vi.fn(),
  fetchAiJobsForGame: vi.fn(),
  fetchLiveAchievements: vi.fn(),
  fetchLiveStatus: vi.fn(),
  fetchGameTrophies: vi.fn(),
  setTrophyUnlocked: vi.fn(),
  researchTrophies: vi.fn(),
  manualTrophiesPrompt: vi.fn(),
  fetchScopedPins: vi.fn(),
  fetchSettings: vi.fn(),
  startGuideTranslation: vi.fn(),
}))
vi.mock('../api/client', async (importOriginal) => ({ ...(await importOriginal<typeof import('../api/client')>()), ...client }))

vi.mock('../components/GameStatsPanel', () => ({ GameStatsPanel: () => <p>statistics-panel</p> }))
vi.mock('../components/LiveTrackerPanel', () => ({ LiveTrackerPanel: () => <p>steam-live-panel</p> }))
vi.mock('../components/TwitchButton', () => ({ TwitchButton: () => null }))
vi.mock('../components/launch/PlayButton', () => ({ PlayButton: () => <button type="button">Spielen</button> }))
vi.mock('../components/GameHeadStatus', () => ({ GameHeadStatus: () => <p>steam-head-status</p> }))
vi.mock('../components/addgame/ManualGameActions', () => ({ ManualGameActions: ({ game }: { game: GameDetailDto }) => <p data-testid="manual-actions">{game.slug}</p> }))
vi.mock('../components/onboarding/HintTour', () => ({ HintTour: () => null }))
vi.mock('../utils/useGuideTranslation', () => ({ useGuideTranslation: () => ({ phase: 'idle', translated: 0, total: 0, error: null, retry: () => {}, aiNotice: null }) }))

const tab = (id: string, slug: string, title: string, sections: GuideSectionDto[] = []): GuideTabDto =>
  ({ id, slug, title, icon: 'mdi:book', sortOrder: 1, totalCheckableItems: 0, checkedItemsCount: 0, isAchievementList: false, groupKey: 'extras', sections }) as GuideTabDto

const game = (over: { platform?: PlatformId; variant?: PlatformVariant | null; steamAppId?: number | null; tabs?: GuideTabDto[] } = {}): GameDetailDto =>
  ({
    id: 'g-manual-1', slug: 'stellar-blade', title: 'Stellar Blade', subtitle: '', steamAppId: over.steamAppId ?? null, coverUrl: '/api/covers/g-manual-1?v=1', heroBannerUrl: '', accentColor: 'emerald',
    estimatedHours: '', missablesInfo: '', difficultyInfo: '', genreTags: '', contentLanguage: 'de', totalCheckableItems: 0, checkedItemsCount: 0, progressPercent: 0,
    tabs: over.tabs ?? [tab('t1', 'roadmap', 'Roadmap')], checkedItemKeys: [], language: 'de',
    translation: { language: 'de', sourceLanguage: 'de', state: 'original', totalTabs: 0, translatedTabs: 0, running: false, error: null, providerMessage: null },
    platform: over.platform ?? 'playstation', platformVariant: over.variant === undefined ? 'ps5' : over.variant, factsSource: 'igdb', developer: 'Shift Up',
  }) as unknown as GameDetailDto

function LocationProbe() {
  const location = useLocation()
  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>
}

function renderPage(detail: GameDetailDto, entry = '/games/stellar-blade?tab=live', status = statusFixture({ mode: 'ask' })) {
  client.fetchGameDetail.mockResolvedValue(detail)
  client.fetchAiStatus.mockResolvedValue(status)
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const view = render(
    <QueryClientProvider client={queryClient}>
      <ConfirmProvider>
        <AiChoiceProvider>
          <MemoryRouter initialEntries={[entry]}>
            <LocationProbe />
            <Routes>
              <Route path="/games/:slug" element={<GameGuidePage />} />
            </Routes>
          </MemoryRouter>
        </AiChoiceProvider>
      </ConfirmProvider>
    </QueryClientProvider>
  )
  return { ...view, queryClient }
}

const noList = (over: Partial<ManualTrophyListDto> = {}) => manualList([], { researchedAt: null, sources: [], ...over })

beforeEach(async () => {
  await applyLanguage('de')
  vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} unobserve() {} })
  // jsdom has no matchMedia: a narrow window (the dock of the pins, no column), no reduced motion preference
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query, addEventListener: () => {}, removeEventListener: () => {} }))
  localStorage.clear()
  sessionStorage.clear()
  localStorage.setItem('pv_hints_seen_v1', JSON.stringify({ game: true }))
  Object.values(client).forEach((fn) => fn.mockReset())
  client.fetchGuideTopics.mockResolvedValue([])
  client.fetchAiJobsForGame.mockResolvedValue([])
  client.fetchLiveAchievements.mockResolvedValue(null)
  client.fetchLiveStatus.mockResolvedValue({ isRunning: false, runningGameTracked: false })
  client.fetchSettings.mockResolvedValue({ achievementNotesEnabled: true, popupSeconds: 5 })
  client.fetchScopedPins.mockResolvedValue({ appId: 0, gameId: 'g-manual-1', apiNames: [], max: 10 })
  client.fetchGameTrophies.mockResolvedValue(manualList(ps5Items()))
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const location = () => screen.getByTestId('location').textContent

// The areas of a game added by hand are entries of the game navigation (AppShell.test.tsx tests their names per platform); the page shows what the URL names
describe('the areas of a game added by hand', () => {
  it('has no area bar in the head, and opens the trophy list of a PlayStation game through the URL', async () => {
    renderPage(game())
    expect(await screen.findByText('Letzter Boss')).toBeTruthy()
    expect(screen.queryByRole('group', { name: 'Bereich des Spiels' })).toBeNull()
    expect(document.querySelector('[data-hint="game-live"]')).toBeNull()
    expect(screen.queryByText('statistics-panel')).toBeNull()
  })

  it('shows the list of an Xbox game under ?tab=live as well, and never the statistics (?tab=stats falls back to the guide)', async () => {
    client.fetchGameTrophies.mockResolvedValue(manualList([{ ...ps5Items()[3]!, achieved: true, unlockTime: '2026-11-19T11:00:00Z' }, ps5Items()[2]!], {}, 'xbox', null))
    renderPage(game({ platform: 'xbox', variant: null }), '/games/stellar-blade?tab=live')
    expect((await screen.findAllByText(/Erstes Blut/)).length).toBeGreaterThan(0)
    cleanup()
    renderPage(game({ platform: 'xbox', variant: null }), '/games/stellar-blade?tab=stats')
    await screen.findByRole('heading', { name: 'Stellar Blade' })
    expect(screen.queryByText('statistics-panel')).toBeNull()
    expect(screen.queryByText(/Erstes Blut/)).toBeNull()
  })

  it('has no trophy list for a Switch game (guide only) and shows the guide', async () => {
    renderPage(game({ platform: 'nintendo', variant: null }), '/games/stellar-blade')
    await screen.findByRole('heading', { name: 'Stellar Blade' })
    expect(client.fetchGameTrophies).not.toHaveBeenCalled()
    expect(screen.queryByText('statistics-panel')).toBeNull()
  })

  it('keeps the Steam live panel for a Steam game', async () => {
    renderPage(game({ platform: 'steam', variant: null, steamAppId: 10 }))
    expect(await screen.findByText('steam-live-panel')).toBeTruthy()
    expect(client.fetchGameTrophies).not.toHaveBeenCalled()
  })

  it('opens the trophy tab through the URL, also on a reload', async () => {
    renderPage(game(), '/games/stellar-blade?tab=roadmap')
    await screen.findByRole('heading', { name: 'Stellar Blade' })
    expect(screen.queryByText('Letzter Boss')).toBeNull()
    cleanup()
    renderPage(game(), '/games/stellar-blade?tab=live')
    expect(await screen.findByText('Letzter Boss')).toBeTruthy()
  })
})

describe('the guide of a game added by hand that has no chapter yet', () => {
  it('opens on the trophy list first, and ?tab=guide (the Guide entry of the navigation) shows the "no guide yet" card', async () => {
    renderPage(game({ tabs: [] }), '/games/stellar-blade')
    // A game added by hand starts with its list
    expect(await screen.findByText('Letzter Boss')).toBeTruthy()
    expect(screen.queryByText('Für dieses Spiel gibt es noch keinen Guide.')).toBeNull()
    cleanup()
    renderPage(game({ tabs: [] }), '/games/stellar-blade?tab=guide')
    expect(await screen.findByText('Für dieses Spiel gibt es noch keinen Guide.')).toBeTruthy()
    expect(location()).toBe('/games/stellar-blade?tab=guide')
  })
})

describe('the head of a game added by hand', () => {
  it('shows the platform status instead of Steam status and Play, with the actions of a manual game', async () => {
    renderPage(game())
    const status = await screen.findByTestId('manual-head-status')
    expect(status.textContent).toContain('PlayStation 5: Trophäen')
    expect(status.textContent).toContain('von Hand abgehakt')
    expect(screen.queryByText('steam-head-status')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Spielen' })).toBeNull()
    expect(screen.getByTestId('manual-actions').textContent).toBe('stellar-blade')
    // No "link with Steam" alert: this game never gets a Steam link
    expect(screen.queryByText('Nicht mit Steam verknüpft.')).toBeNull()
  })

  it('keeps Play, the Steam status and the Steam alert for a Steam game', async () => {
    renderPage(game({ platform: 'steam', variant: null, steamAppId: 10 }))
    expect(await screen.findByText('steam-head-status')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Spielen' })).toBeTruthy()
    expect(screen.queryByTestId('manual-actions')).toBeNull()
    cleanup()
    renderPage(game({ platform: 'steam', variant: null, steamAppId: null }))
    expect(await screen.findByText('Nicht mit Steam verknüpft.')).toBeTruthy()
  })

  it('names the platinum once the list reached it', async () => {
    client.fetchGameTrophies.mockResolvedValue(manualList(ps5Items(), { platinumAt: '2026-11-19T12:00:00Z' }))
    renderPage(game())
    const status = await screen.findByTestId('manual-head-status')
    await waitFor(() => expect(status.textContent).toContain('Platin erreicht'))
  })

  it('feeds the trophy bar from the manual list: the sentence, the grades and the progress line', async () => {
    renderPage(game())
    await screen.findByText('Letzter Boss')
    expect(screen.getByText('1 / 4 Trophäen (25 %)')).toBeTruthy()
    expect([...document.querySelectorAll('.pv-fig-count')].map((node) => node.textContent)).toEqual(['0 / 1', '0 / 1', '1 / 1', '0 / 1'])
    expect(screen.getByRole('progressbar', { name: 'Trophäen-Fortschritt' }).getAttribute('aria-valuenow')).toBe('25')
  })

  it('shows one total figure on a platform without grades, in the word of that platform', async () => {
    client.fetchGameTrophies.mockResolvedValue(manualList([{ ...ps5Items()[3]!, achieved: true, unlockTime: '2026-11-19T11:00:00Z' }, ps5Items()[2]!], {}, 'xbox', null))
    renderPage(game({ platform: 'xbox', variant: null }))
    await screen.findAllByText(/Erstes Blut/)
    expect(screen.getByText('2 / 2 Erfolge (100 %)')).toBeTruthy()
    expect([...document.querySelectorAll('.pv-fig-count')].map((node) => node.textContent)).toEqual(['2 / 2'])
  })

  it('says "Noch keine Trophäenliste" in the bar before the first research', async () => {
    client.fetchGameTrophies.mockResolvedValue(noList())
    renderPage(game())
    expect(await screen.findByText('Noch keine Trophäenliste')).toBeTruthy()
    // A dash in every figure, not "0 / 0"
    await waitFor(() => expect([...document.querySelectorAll('.pv-fig-count')].map((node) => node.textContent)).toEqual(['–', '–', '–', '–']))
    expect(screen.queryByText("0 / 0")).toBeNull()
  })

  it('gives the guide the trophies of the manual list (an achievement hint shows its title)', async () => {
    const hint = { type: 'alert', kind: 'achievement', title: 'Erfolg', htmlContent: 'Holen.', apiName: 'gold-1' } as unknown as GuideSectionDto
    renderPage(game({ tabs: [tab('t1', 'roadmap', 'Roadmap', [hint])] }), '/games/stellar-blade?tab=roadmap')
    expect(await screen.findByText('Letzter Boss')).toBeTruthy()
    expect(document.querySelector('[data-achievement="gold-1"][data-known="true"]')).toBeTruthy()
  })

  it('pins by the game id: the pins are asked for with it, not with an AppID', async () => {
    renderPage(game())
    await waitFor(() => expect(client.fetchScopedPins).toHaveBeenCalledWith({ gameId: 'g-manual-1' }))
  })
})

describe('the research of the trophy list', () => {
  it('shows the empty state with the recommended order and asks which AI first (with Mein KI-Abo on offer)', async () => {
    client.fetchGameTrophies.mockResolvedValue(noList())
    renderPage(game())
    expect(await screen.findByRole('heading', { name: 'Noch keine Trophäenliste für Stellar Blade' })).toBeTruthy()
    expect(screen.getByText('Dieser Schritt: die Liste für PlayStation 5 sammeln.')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: /Trophäenliste recherchieren/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
    expect(dialog.textContent).toContain('Mein KI-Abo')
    expect(client.researchTrophies).not.toHaveBeenCalled()
  })

  it('"Zum Guide" leads to the guide', async () => {
    client.fetchGameTrophies.mockResolvedValue(noList())
    renderPage(game())
    fireEvent.click(await screen.findByRole('button', { name: /Zum Guide/ }))
    await waitFor(() => expect(location()).toBe('/games/stellar-blade?tab=roadmap'))
  })

  it('runs the job with the chosen AI, shows its real progress in the area and the notice of a change of AI, then shows the list', async () => {
    client.fetchGameTrophies.mockResolvedValue(noList())
    const job = (stage: AiJobDto['step']['stage']): AiJobDto => ({
      id: 'j1', kind: 'trophies', running: true, step: { stage, done: 0, total: 0, title: null }, elapsedSeconds: 3, elapsedMilliseconds: 3000, stepIdleSeconds: 1,
      switchNotice: null, error: null, providerMessage: null,
    } as unknown as AiJobDto)
    let finish!: (value: unknown) => void
    client.researchTrophies.mockImplementation((_slug, _provider, _include, onProgress) => {
      onProgress(job('trophies'))
      return new Promise((resolve) => (finish = resolve))
    })
    const { queryClient } = renderPage(game())
    fireEvent.click(await screen.findByRole('button', { name: /Trophäenliste recherchieren/ }))
    await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
    fireEvent.click(screen.getByRole('radio', { name: /Claude/ }))
    fireEvent.click(screen.getByRole('button', { name: /Mit Claude starten/ }))

    await waitFor(() => expect(client.researchTrophies).toHaveBeenCalledWith('stellar-blade', 'claude', undefined, expect.any(Function)))
    // The area says what runs and which step (from the server), no floating copy of it while this tab shows it
    expect(await screen.findByRole('heading', { name: 'Trophäenliste wird recherchiert …' })).toBeTruthy()
    expect(screen.getByTestId('ai-progress').textContent).toContain('Recherchiere die Liste und gleiche sie mit einer zweiten Quelle ab')
    expect(document.querySelectorAll('[data-testid="ai-progress"]')).toHaveLength(1)

    client.fetchGameTrophies.mockResolvedValue(manualList(ps5Items()))
    await act(async () => {
      finish({
        game: { list: manualList(ps5Items()), factsUpdated: false, keptTicks: 2, retainedOldTicked: 0 },
        ai: { providers: ['claude'], switches: [{ from: 'gemini', to: 'claude', reason: 'quota', text: 'Gemini-Kontingent aufgebraucht – weiter mit Claude.' }] },
      })
    })
    expect(await screen.findByText('Gemini-Kontingent aufgebraucht – weiter mit Claude.')).toBeTruthy()
    expect(await screen.findByText('Trophäenliste gespeichert')).toBeTruthy()
    expect(screen.getByText('4 Trophäen. 2 Haken wurden übernommen.')).toBeTruthy()
    expect(queryClient.getQueryData(['live-achievements', 'g:stellar-blade'])).toMatchObject({ totalCount: 4 })
    expect(await screen.findByText('Letzter Boss')).toBeTruthy()
  })

  it('shows a failed research completely, with the offer of the own subscription', async () => {
    client.fetchGameTrophies.mockResolvedValue(noList())
    client.researchTrophies.mockRejectedValue(Object.assign(new Error('Es gibt keine verlässliche Trophäenliste für dieses Spiel: Die Quellen widersprechen sich bei der Zahl der Trophäen.'), { providerMessage: 'documented=false' }))
    renderPage(game(), '/games/stellar-blade?tab=live', statusFixture({ mode: 'default' }))
    fireEvent.click(await screen.findByRole('button', { name: /Trophäenliste recherchieren/ }))
    // The mode "Standard-KI" asks nothing: the AI of the settings does it
    await waitFor(() => expect(client.researchTrophies).toHaveBeenCalledWith('stellar-blade', undefined, undefined, expect.any(Function)))

    const notice = await screen.findByRole('alert')
    expect(notice.textContent).toContain('Trophäenliste konnte nicht recherchiert werden')
    expect(notice.textContent).toContain('Die Quellen widersprechen sich bei der Zahl der Trophäen.')
    fireEvent.click(within(notice).getByRole('button', { name: 'Mit deinem KI-Abo (ohne API-Key)' }))
    expect(await screen.findByRole('dialog', { name: /Trophäenliste mit deinem KI-Abo/ })).toBeTruthy()
  })

  it('opens the subscription dialog for Mein KI-Abo, and asks for the prompt only when the player copies it', async () => {
    client.fetchGameTrophies.mockResolvedValue(noList())
    client.manualTrophiesPrompt.mockResolvedValue({ kind: 'trophies', prompt: 'p', fileName: 'p.md', language: 'de', steamAppId: null, title: 'Stellar Blade' })
    renderPage(game())
    fireEvent.click(await screen.findByRole('button', { name: /Trophäenliste recherchieren/ }))
    await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
    fireEvent.click(screen.getByRole('radio', { name: /Mein KI-Abo/ }))
    fireEvent.click(screen.getByRole('button', { name: /Mit meinem KI-Abo weiter/ }))

    expect(await screen.findByRole('dialog', { name: /Trophäenliste mit deinem KI-Abo/ })).toBeTruthy()
    expect(client.researchTrophies).not.toHaveBeenCalled()
    expect(client.manualTrophiesPrompt).not.toHaveBeenCalled()
  })

  it('follows a research that already runs when the page opens (reload)', async () => {
    client.fetchGameTrophies.mockResolvedValue(noList())
    const running = { id: 'j9', kind: 'trophies', running: true, step: { stage: 'saving', done: 0, total: 0, title: null }, elapsedSeconds: 5, stepIdleSeconds: 0, switchNotice: null } as unknown as AiJobDto
    client.fetchAiJobsForGame.mockResolvedValue([running])
    // The job ends at once (fetched again by followAiJob): the list arrives
    client.fetchAiJobsForGame.mockResolvedValue([running])
    renderPage(game())
    expect(await screen.findByRole('heading', { name: 'Trophäenliste wird recherchiert …' })).toBeTruthy()
  })

  it('offers the quiet re-research with the list, naming the sources', async () => {
    renderPage(game())
    const source = await screen.findByTestId('trophy-source')
    expect(source.textContent).toContain('Von der KI recherchiert')
    expect(source.textContent).toContain('PSNProfiles')
    expect(source.textContent).toContain('Die Angaben können Fehler enthalten.')
    expect(within(source).getByRole('button', { name: /Trophäenliste neu recherchieren/ })).toBeTruthy()
  })
})

describe('the facts of a Switch game', () => {
  it('offers "Spiel-Fakten recherchieren" while the facts are missing, and asks with include.facts only', async () => {
    const nintendo = { ...game({ platform: 'nintendo', variant: null }), factsSource: null, developer: null } as GameDetailDto
    client.researchTrophies.mockReturnValue(new Promise(() => {}))
    renderPage(nintendo, '/games/stellar-blade', statusFixture({ mode: 'default' }))
    fireEvent.click(await screen.findByRole('button', { name: /Spiel-Fakten recherchieren/ }))
    await waitFor(() => expect(client.researchTrophies).toHaveBeenCalledWith('stellar-blade', undefined, { trophies: false, facts: true }, expect.any(Function)))
  })

  it('says nothing when the facts are there', async () => {
    renderPage(game({ platform: 'nintendo', variant: null }), '/games/stellar-blade')
    await screen.findByRole('heading', { name: 'Stellar Blade' })
    expect(screen.queryByTestId('manual-facts-note')).toBeNull()
  })
})
