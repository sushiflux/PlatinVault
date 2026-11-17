import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes } from 'react-router'
import { applyLanguage } from '../i18n'
import type { AiStatusDto, GameDetailDto, GuideTabDto } from '../api/types'
import { ConfirmProvider } from '../components/ui/ConfirmDialog'
import { AiChoiceProvider } from '../components/ai/AiChoice'
import { providerStatus, statusFixture } from '../components/ai/ai.fixtures'
import { GameGuidePage } from './GameGuidePage'

// The three actions that can run with the player's own AI subscription: the wand ("Guide vervollständigen"), "Tab ergänzen" and Story & Lore.
// The choice between a stored API key and the subscription has to be possible BEFORE anything runs.

const client = vi.hoisted(() => ({
  fetchGameDetail: vi.fn(),
  fetchAiStatus: vi.fn(),
  fetchGuideTopics: vi.fn(),
  fetchAiJobsForGame: vi.fn(),
  fetchLiveAchievements: vi.fn(),
  fetchLiveStatus: vi.fn(),
  completeGuide: vi.fn(),
  planGuide: vi.fn(),
  expandTabWithAi: vi.fn(),
  researchLore: vi.fn(),
  manualBlueprintPrompt: vi.fn(),
  checkManualBlueprint: vi.fn(),
  manualChapterPrompt: vi.fn(),
  checkManualChapter: vi.fn(),
  saveManualChapter: vi.fn(),
  manualLorePrompt: vi.fn(),
  checkManualLore: vi.fn(),
  saveManualLore: vi.fn(),
}))
vi.mock('../api/client', async (importOriginal) => ({ ...(await importOriginal<typeof import('../api/client')>()), ...client }))

// Everything around the guide that has nothing to do with the choice
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

const game = (tabs: GuideTabDto[]): GameDetailDto =>
  ({
    id: 'g1', slug: 'suncraft', title: 'Suncraft', subtitle: '', steamAppId: null, coverUrl: '', heroBannerUrl: '', accentColor: 'emerald', estimatedHours: '', missablesInfo: '',
    difficultyInfo: '', genreTags: '', contentLanguage: 'de', totalCheckableItems: 0, checkedItemsCount: 0, progressPercent: 0, tabs, checkedItemKeys: [], language: 'de',
    translation: { language: 'de', sourceLanguage: 'de', state: 'original', totalTabs: 0, translatedTabs: 0, running: false, error: null, providerMessage: null },
  }) as unknown as GameDetailDto

const noKeys = (): AiStatusDto => statusFixture({ activeProvider: null, defaultProvider: null, providers: [providerStatus('gemini', { hasKey: false }), providerStatus('claude', { hasKey: false }), providerStatus('openai', { hasKey: false })] })

function renderPage(detail: GameDetailDto, status: AiStatusDto, search = '') {
  client.fetchGameDetail.mockResolvedValue(detail)
  client.fetchAiStatus.mockResolvedValue(status)
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ConfirmProvider>
        <AiChoiceProvider>
          <MemoryRouter initialEntries={[`/games/suncraft${search}`]}>
            <Routes>
              <Route path="/games/:slug" element={<GameGuidePage />} />
            </Routes>
          </MemoryRouter>
        </AiChoiceProvider>
      </ConfirmProvider>
    </QueryClientProvider>
  )
}

const WITH_GUIDE = () => game([tab('t1', 'roadmap', 'Roadmap')])

beforeEach(async () => {
  await applyLanguage('de')
  localStorage.clear()
  sessionStorage.clear()
  // Hints and the tour must not cover anything
  localStorage.setItem('pv_hints_seen_v1', JSON.stringify({ game: true }))
  Object.values(client).forEach((fn) => fn.mockReset())
  client.fetchGuideTopics.mockResolvedValue([{ id: 'weapons', title: 'Beste Waffen', icon: 'mdi:sword', brief: 'Alle Waffen', groupKey: 'combat' }])
  client.fetchAiJobsForGame.mockResolvedValue([])
  client.fetchLiveAchievements.mockResolvedValue(null)
  client.fetchLiveStatus.mockResolvedValue({ isRunning: false, runningGameTracked: false })
  client.manualBlueprintPrompt.mockResolvedValue({ kind: 'blueprint', prompt: 'p', fileName: 'p.md', language: 'de', steamAppId: null, title: 'Suncraft' })
  client.manualChapterPrompt.mockResolvedValue({ kind: 'chapter', prompt: 'p', fileName: 'p.md', language: 'de', steamAppId: null, title: 'Suncraft' })
  client.manualLorePrompt.mockResolvedValue({ kind: 'lore', prompt: 'p', fileName: 'p.md', language: 'de', steamAppId: null, title: 'Suncraft' })
})
afterEach(cleanup)

const wand = () => screen.findByRole('button', { name: 'Guide vervollständigen' })

describe('the wand "Guide vervollständigen"', () => {
  it('asks first, in every selection mode, and nothing runs before the choice', async () => {
    renderPage(WITH_GUIDE(), statusFixture({ mode: 'default' }))
    fireEvent.click(await wand())

    const dialog = await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
    expect(dialog.textContent).toContain('Mein KI-Abo')
    expect(client.completeGuide).not.toHaveBeenCalled()
    expect(client.planGuide).not.toHaveBeenCalled()
  })

  it('an API choice opens the builder with that provider, and the builder does not ask again', async () => {
    client.planGuide.mockReturnValue(new Promise(() => {}))
    renderPage(WITH_GUIDE(), statusFixture({ mode: 'ask' }))
    fireEvent.click(await wand())
    await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
    fireEvent.click(screen.getByRole('radio', { name: /Claude/ }))
    fireEvent.click(screen.getByRole('button', { name: /Mit Claude starten/ }))

    await waitFor(() => expect(client.planGuide).toHaveBeenCalledWith('suncraft', 'claude', expect.anything()))
    // the chooser is gone (it fades out) and was asked exactly once
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })).toBeNull())
  })

  it('an API choice on an empty guide takes the quick path with that provider', async () => {
    client.completeGuide.mockReturnValue(new Promise(() => {}))
    renderPage(game([]), statusFixture({ mode: 'default' }))
    fireEvent.click(await wand())
    await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
    fireEvent.click(screen.getByRole('button', { name: /Mit Gemini starten/ }))

    await waitFor(() => expect(client.completeGuide).toHaveBeenCalledWith('suncraft', undefined, 'gemini', expect.anything()))
  })

  it('the subscription opens its dialog with the blueprint step, nothing goes to an AI', async () => {
    renderPage(WITH_GUIDE(), statusFixture({ mode: 'default' }))
    fireEvent.click(await wand())
    await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
    fireEvent.click(screen.getByRole('radio', { name: /Mein KI-Abo/ }))
    fireEvent.click(screen.getByRole('button', { name: /Mit meinem KI-Abo weiter/ }))

    expect(await screen.findByRole('dialog', { name: /Guide mit deinem KI-Abo/ })).toBeTruthy()
    expect(screen.getByText('Schritt 1 von 2 · Kapitelliste')).toBeTruthy()
    expect(client.planGuide).not.toHaveBeenCalled()
    expect(client.completeGuide).not.toHaveBeenCalled()
  })

  it('cancelling does nothing', async () => {
    renderPage(WITH_GUIDE(), statusFixture({ mode: 'default' }))
    fireEvent.click(await wand())
    const chooser = await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
    fireEvent.keyDown(chooser, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(client.planGuide).not.toHaveBeenCalled()
    expect(client.completeGuide).not.toHaveBeenCalled()
  })

  it('without any API key there is no chooser: the subscription dialog opens at once', async () => {
    renderPage(WITH_GUIDE(), noKeys())
    fireEvent.click(await wand())
    expect(await screen.findByRole('dialog', { name: /Guide mit deinem KI-Abo/ })).toBeTruthy()
    expect(screen.queryByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })).toBeNull()
  })
})

describe('"Tab ergänzen"', () => {
  const open = async () => {
    fireEvent.click(await screen.findByRole('button', { name: 'Tab ergänzen' }))
  }

  it('the AI button of the dialog offers the subscription', async () => {
    renderPage(WITH_GUIDE(), statusFixture({ mode: 'default' }))
    await open()
    await screen.findByRole('button', { name: 'Tab jetzt erstellen' })
    // the chapter bar behind the dialog has such a part too: the one of the dialog comes last in the page
    fireEvent.click(screen.getAllByRole('button', { name: /KI für diese Aktion wählen/ }).at(-1)!)
    expect(screen.getByRole('menuitemradio', { name: /Mein KI-Abo/ })).toBeTruthy()
  })

  it('still needs a title and a question before anything happens', async () => {
    renderPage(WITH_GUIDE(), statusFixture({ mode: 'default' }))
    await open()
    fireEvent.click(screen.getByRole('button', { name: 'Tab jetzt erstellen' }))
    expect(await screen.findByText(/Tab-Titel und deine Frage/)).toBeTruthy()
    expect(screen.queryByRole('dialog', { name: /KI-Abo/ })).toBeNull()
  })

  it('with the subscription, the dialog gives way to one NEW chapter seeded with title, question and topic', async () => {
    renderPage(WITH_GUIDE(), statusFixture({ mode: 'default' }))
    await open()
    fireEvent.click(await screen.findByRole('button', { name: 'Beste Waffen' }))
    fireEvent.change(screen.getByDisplayValue('Alle Waffen'), { target: { value: 'Alle Waffen mit Fundorten' } })
    fireEvent.click(screen.getAllByRole('button', { name: /KI für diese Aktion wählen/ }).at(-1)!)
    fireEvent.click(screen.getByRole('menuitemradio', { name: /Mein KI-Abo/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Tab jetzt erstellen' }))

    expect(await screen.findByRole('dialog', { name: /Kapitel mit deinem KI-Abo recherchieren/ })).toBeTruthy()
    expect(screen.getByText('Beste Waffen')).toBeTruthy()
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Prompt kopieren' })) })
    expect(client.manualChapterPrompt).toHaveBeenCalledWith('suncraft', expect.objectContaining({ title: 'Beste Waffen', focus: 'Alle Waffen mit Fundorten', topicId: 'weapons', icon: 'mdi:sword' }))
    expect(client.expandTabWithAi).not.toHaveBeenCalled()
    // the expand dialog is gone, the new chapter has its own storage scope
    expect(screen.queryByPlaceholderText(/Titel/)).toBeNull()
    expect(sessionStorage.getItem('pv_manual_batch_v1:new:suncraft')).not.toBeNull()
    expect(sessionStorage.getItem('pv_manual_batch_v1:failed:suncraft')).toBeNull()
  })

  it('starts on "Mein KI-Abo" when the subscription was the way taken last time', async () => {
    localStorage.setItem('pv_ai_last_way', 'manual')
    renderPage(WITH_GUIDE(), statusFixture({ mode: 'default' }))
    await open()
    fireEvent.click(await screen.findByRole('button', { name: 'Beste Waffen' }))
    // no menu touched: the remembered way is already chosen
    fireEvent.click(screen.getByRole('button', { name: 'Tab jetzt erstellen' }))
    expect(await screen.findByRole('dialog', { name: /Kapitel mit deinem KI-Abo recherchieren/ })).toBeTruthy()
    expect(client.expandTabWithAi).not.toHaveBeenCalled()
  })

  it('the wand preselects "Mein KI-Abo" after it was taken there', async () => {
    renderPage(WITH_GUIDE(), statusFixture({ mode: 'default', defaultProvider: 'gemini', activeProvider: 'gemini' }))
    fireEvent.click(await wand())
    await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
    expect((screen.getByRole('radio', { name: /Gemini/ }) as HTMLInputElement).checked).toBe(true)
    fireEvent.click(screen.getByRole('radio', { name: /Mein KI-Abo/ }))
    fireEvent.click(screen.getByRole('button', { name: /Mit meinem KI-Abo weiter/ }))
    await waitFor(() => expect(localStorage.getItem('pv_ai_last_way')).toBe('manual'))
    cleanup()

    renderPage(WITH_GUIDE(), statusFixture({ mode: 'default', defaultProvider: 'gemini', activeProvider: 'gemini' }))
    fireEvent.click(await wand())
    await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
    expect((screen.getByRole('radio', { name: /Mein KI-Abo/ }) as HTMLInputElement).checked).toBe(true)
  })

  it('the API way still runs with the chosen provider', async () => {
    client.expandTabWithAi.mockReturnValue(new Promise(() => {}))
    renderPage(WITH_GUIDE(), statusFixture({ mode: 'ask' }))
    await open()
    fireEvent.click(await screen.findByRole('button', { name: 'Beste Waffen' }))
    fireEvent.click(screen.getByRole('button', { name: 'Tab jetzt erstellen' }))
    await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
    expect(screen.getByRole('radio', { name: /Mein KI-Abo/ })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /Mit Gemini starten/ }))
    await waitFor(() => expect(client.expandTabWithAi).toHaveBeenCalledWith('suncraft', expect.objectContaining({ providerOverride: 'gemini' }), expect.anything()))
  })
})

describe('Story & Lore', () => {
  it('the research button offers the subscription and opens the lore dialog', async () => {
    renderPage(WITH_GUIDE(), statusFixture({ mode: 'default' }), '?tab=story')
    fireEvent.click(await screen.findByRole('button', { name: /KI für diese Aktion wählen/ }))
    fireEvent.click(screen.getByRole('menuitemradio', { name: /Mein KI-Abo/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Story & Lore recherchieren' }))

    expect(await screen.findByRole('dialog', { name: /Story & Lore mit deinem KI-Abo/ })).toBeTruthy()
    expect(screen.queryByTestId('lore-replace-note')).toBeNull()
    expect(client.researchLore).not.toHaveBeenCalled()
  })

  it('in the mode "ask" the chooser lists the subscription and the dialog says an existing tab is replaced', async () => {
    const lore = tab('lore-1', 'story-lore', 'Story & Lore')
    renderPage(game([tab('t1', 'roadmap', 'Roadmap'), lore]), statusFixture({ mode: 'ask' }), '?tab=story')
    // the tab exists: first the spoiler gate, then the redo button
    fireEvent.click(await screen.findByRole('button', { name: 'Spoiler anzeigen' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Neu recherchieren' }))
    await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
    fireEvent.click(screen.getByRole('radio', { name: /Mein KI-Abo/ }))
    fireEvent.click(screen.getByRole('button', { name: /Mit meinem KI-Abo weiter/ }))

    expect(await screen.findByRole('dialog', { name: /Story & Lore mit deinem KI-Abo/ })).toBeTruthy()
    expect(screen.getByTestId('lore-replace-note')).toBeTruthy()
    expect(client.researchLore).not.toHaveBeenCalled()
  })

  it('saving selects the Story & Lore tab and updates the game', async () => {
    client.checkManualLore.mockResolvedValue({ ok: true, errors: [], warnings: [], preview: { parts: [{ title: 'Die Story', sectionCount: 2 }], missingParts: [] } })
    const saved = game([tab('t1', 'roadmap', 'Roadmap'), tab('lore-1', 'story-lore', 'Story & Lore')])
    client.saveManualLore.mockResolvedValue({ game: saved, tabId: 'lore-1', replaced: false })
    renderPage(WITH_GUIDE(), noKeys(), '?tab=story')
    fireEvent.click(await screen.findByRole('button', { name: 'Story & Lore recherchieren' }))
    await screen.findByRole('dialog', { name: /Story & Lore mit deinem KI-Abo/ })

    fireEvent.change(screen.getByRole('textbox'), { target: { value: '{"parts":[]}' } })
    await waitFor(() => expect(screen.getByRole('button', { name: 'Story & Lore speichern' }).hasAttribute('disabled')).toBe(false), { timeout: 3000 })
    fireEvent.click(screen.getByRole('button', { name: 'Story & Lore speichern' }))

    expect(await screen.findByTestId('lore-saved')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Fertig' }))
    // the tab exists now: the page shows its spoiler gate instead of the research offer
    expect(await screen.findByText('Achtung: Story-Spoiler')).toBeTruthy()
  })
})
