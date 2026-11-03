import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GameDetailDto, GuidePlanDto, ManualBlueprintCheckDto, ManualChapterCheckDto, ManualPromptDto } from '../../../api/types'
import { AnswerBox } from './AnswerBox'
import { ManualGuideDialog } from './ManualGuideDialog'
import { PromptBox } from './PromptBox'

const client = vi.hoisted(() => ({
  checkManualBlueprint: vi.fn(),
  checkManualChapter: vi.fn(),
  fetchGuideTopics: vi.fn(),
  manualBlueprintPrompt: vi.fn(),
  manualChapterPrompt: vi.fn(),
  saveManualChapter: vi.fn(),
}))
vi.mock('../../../api/client', () => client)

const prompt = (over: Partial<ManualPromptDto> = {}): ManualPromptDto => ({
  kind: 'chapter',
  prompt: 'Du schreibst ein Kapitel für „Suncraft“ (Steam-AppID 4078110). Antworte mit JSON.',
  fileName: 'platinvault-suncraft-kapitel-dorfausbau.md',
  language: 'de',
  steamAppId: 4078110,
  title: 'Suncraft',
  ...over,
})

const plan: GuidePlanDto = {
  profiles: [],
  sourceSituation: 'dünn',
  topics: [
    { id: 'roadmap', title: 'Roadmap', icon: 'mdi:compass', focus: 'Reihenfolge', recommended: true, required: true, covered: false, reason: null },
    { id: 'weapons', title: 'Beste Waffen', icon: 'mdi:sword', focus: 'Alle Waffen', recommended: true, required: false, covered: false, reason: null },
    { id: 'bestiary', title: 'Gegner', icon: 'mdi:skull', focus: 'Alle Gegner', recommended: false, required: false, covered: false, reason: 'Passt zum Spiel, ist aber nicht im Vorschlag' },
  ],
  extras: [],
  budget: { scope: 'detailed', min: 8, max: 14, suggested: 2 },
}

const okChapter = (title = 'Beste Waffen'): ManualChapterCheckDto => ({
  ok: true,
  errors: [],
  warnings: [{ code: 'noAppId', message: 'Die Antwort nennt keine Steam-AppID.', details: null }],
  preview: { title, icon: 'mdi:sword', sectionCount: 3, tableRows: 12, cards: 4, sectionTitles: [] },
})

function wrap(node: React.ReactNode) {
  return render(<QueryClientProvider client={new QueryClient()}>{node}</QueryClientProvider>)
}

const settle = () => act(async () => { await vi.advanceTimersByTimeAsync(600) })

beforeEach(() => {
  vi.useFakeTimers()
  Object.values(client).forEach((fn) => fn.mockReset())
  Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true })
  Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn(async () => undefined) }, configurable: true })
  Object.defineProperty(URL, 'createObjectURL', { value: vi.fn(() => 'blob:x'), configurable: true })
  Object.defineProperty(URL, 'revokeObjectURL', { value: vi.fn(), configurable: true })
})
afterEach(() => {
  cleanup()
  vi.useRealTimers()
})

describe('PromptBox', () => {
  it('copies the prompt and says what to do next', async () => {
    const load = vi.fn(async () => prompt())
    wrap(<PromptBox load={load} />)

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Prompt kopieren' })) })

    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(prompt().prompt)
    expect(screen.getByTestId('copy-status').textContent).toBe('Prompt kopiert – füge ihn im Chat deines Abos ein, am besten mit eingeschalteter Websuche.')
    // the prompt is made once and kept
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Prompt kopieren' })) })
    expect(load).toHaveBeenCalledTimes(1)
  })

  it('shows and selects the prompt when the browser does not allow copying', async () => {
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn(async () => { throw new Error('blocked') }) }, configurable: true })
    document.execCommand = vi.fn(() => false)
    wrap(<PromptBox load={async () => prompt()} />)

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Prompt kopieren' })) })

    expect(screen.getByTestId('copy-status').textContent).toContain('Strg+C')
    const field = screen.getByLabelText('Der Prompt') as HTMLTextAreaElement
    expect(field.value).toBe(prompt().prompt)
    expect(field.readOnly).toBe(true)
  })

  it('exports the prompt as a file', async () => {
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    wrap(<PromptBox load={async () => prompt()} />)

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Als Datei exportieren' })) })

    expect(URL.createObjectURL).toHaveBeenCalled()
    expect(click).toHaveBeenCalled()
    expect(screen.getByTestId('copy-status').textContent).toBe('Datei gespeichert: platinvault-suncraft-kapitel-dorfausbau.md')
    click.mockRestore()
  })

  it('names the reason when the prompt cannot be made', async () => {
    wrap(<PromptBox load={async () => { throw new Error('Spiel nicht gefunden') }} />)

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Prompt kopieren' })) })

    expect(screen.getByRole('alert').textContent).toContain('Der Prompt konnte nicht erstellt werden: Spiel nicht gefunden')
  })
})

describe('AnswerBox', () => {
  it('checks the pasted text live, shows every error completely and ignores an older answer', async () => {
    const longError = 'Die Antwort ist abgeschnitten: Das JSON-Objekt wird nicht geschlossen (es fehlen 3 schließende Klammer(n)). Bitte die KI, ihre Antwort komplett noch einmal auszugeben – oder wähle in den Einstellungen den Baustein „Umfang → Kompakt“, damit sie kürzer wird.'
    const check = vi.fn(async (text: string): Promise<ManualChapterCheckDto> =>
      text.includes('{"title"') ? okChapter() : { ok: false, errors: [{ code: 'cutOff', message: longError, details: 'Expected depth to be zero' }], warnings: [], preview: null })
    const onResult = vi.fn()
    wrap(<AnswerBox<ManualChapterCheckDto> check={check} onResult={onResult} renderOk={(r) => r.preview?.title} />)

    expect(screen.getByTestId('answer-result').textContent).toBe('Noch nichts eingefügt.')
    const field = screen.getByRole('textbox')

    fireEvent.change(field, { target: { value: '{"titl' } })
    // not before the pause: typing is not checked key by key
    expect(check).not.toHaveBeenCalled()
    await settle()
    expect(check).toHaveBeenCalledTimes(1)
    expect(screen.getByTestId('answer-result').textContent).toContain(longError)
    expect(screen.getByTestId('answer-result').querySelector('[data-issue="cutOff"]')).not.toBeNull()
    expect(onResult).toHaveBeenLastCalledWith(null, '{"titl')

    fireEvent.change(field, { target: { value: '{"title": "Beste Waffen", "sections": []}' } })
    await settle()
    expect(screen.getByTestId('answer-result').textContent).toContain('Antwort gelesen.')
    expect(screen.getByTestId('answer-result').textContent).toContain('Beste Waffen')
    expect(screen.getByTestId('answer-result').textContent).toContain('Die Antwort nennt keine Steam-AppID.')
    expect(onResult).toHaveBeenLastCalledWith(expect.objectContaining({ ok: true }), '{"title": "Beste Waffen", "sections": []}')
  })

  it('reports a failed check and clears everything with "Leeren"', async () => {
    const check = vi.fn(async () => { throw new Error('Server nicht erreichbar') })
    const onResult = vi.fn()
    wrap(<AnswerBox<ManualChapterCheckDto> check={check} onResult={onResult} renderOk={() => null} />)

    fireEvent.change(screen.getByRole('textbox'), { target: { value: '{}' } })
    await settle()
    expect(screen.getByTestId('answer-result').textContent).toContain('Die Prüfung ist fehlgeschlagen: Server nicht erreichbar')

    fireEvent.click(screen.getByRole('button', { name: 'Leeren' }))
    expect((screen.getByRole('textbox') as HTMLTextAreaElement).value).toBe('')
    expect(onResult).toHaveBeenLastCalledWith(null, '')
  })

  it('reads a file into the field', async () => {
    const check = vi.fn(async () => okChapter())
    wrap(<AnswerBox<ManualChapterCheckDto> check={check} onResult={vi.fn()} renderOk={() => null} />)
    const file = new File(['{"title":"x"}'], 'antwort.json', { type: 'application/json' })
    const input = document.querySelector('input[type="file"]') as HTMLInputElement

    await act(async () => { fireEvent.change(input, { target: { files: [file] } }) })
    await settle()

    expect((screen.getByRole('textbox') as HTMLTextAreaElement).value).toBe('{"title":"x"}')
    expect(check).toHaveBeenCalledWith('{"title":"x"}')
  })
})

describe('ManualGuideDialog', () => {
  it('goes from the blueprint answer through the chapter prompts to saved chapters', async () => {
    client.manualBlueprintPrompt.mockResolvedValue(prompt({ kind: 'blueprint', fileName: 'bauplan.md' }))
    client.checkManualBlueprint.mockResolvedValue({ ok: true, errors: [], warnings: [], plan } satisfies ManualBlueprintCheckDto)
    client.manualChapterPrompt.mockResolvedValue(prompt())
    client.checkManualChapter.mockResolvedValue(okChapter())
    const game = { slug: 'suncraft', tabs: [] } as unknown as GameDetailDto
    client.saveManualChapter.mockResolvedValue({ game, tabId: 't1', replaced: false })
    const onSaved = vi.fn()
    const onClose = vi.fn()

    wrap(<ManualGuideDialog slug="suncraft" gameTitle="Suncraft" language="de" onClose={onClose} onSaved={onSaved} />)

    // step 1: one calm line says this is the player's own subscription; the answer to the blueprint prompt is pasted
    expect(screen.getByTestId('manual-intro').textContent).toContain('erlaubt')
    expect(screen.getByTestId('manual-intro').textContent).toContain('schickt nichts an eine KI')
    fireEvent.change(screen.getAllByRole('textbox')[0]!, { target: { value: '```json\n{"topics": []}\n```' } })
    await settle()
    expect(client.checkManualBlueprint).toHaveBeenCalledWith('suncraft', expect.stringContaining('"topics"'))
    // the recommended chapters of the plan are ticked
    const next = screen.getByRole('button', { name: /Weiter: 2 Kapitel/ })
    await act(async () => { fireEvent.click(next) })

    // step 2: one row per ticked chapter
    expect(screen.getAllByTestId('manual-row')).toHaveLength(2)
    expect(screen.getByTestId('manual-progress').textContent).toBe('Gespeichert: 0 von 2')
    const row = screen.getAllByTestId('manual-row')[1]!
    await act(async () => { fireEvent.click(row.querySelector('button')!) }) // first button of the row = copy
    expect(client.manualChapterPrompt).toHaveBeenCalledWith('suncraft', expect.objectContaining({ title: 'Beste Waffen', focus: 'Alle Waffen', topicId: 'weapons' }))

    fireEvent.click(Array.from(row.querySelectorAll('button')).find((b) => b.textContent === 'Antwort einfügen')!)
    fireEvent.change(row.querySelector('textarea:not([readonly])')!, { target: { value: '{"title":"Beste Waffen","sections":[{}]}' } })
    await settle()
    expect(row.textContent).toContain('Vorschau: „Beste Waffen“ – Abschnitte: 3 · Tabellenzeilen: 12 · Karten: 4')
    await act(async () => { fireEvent.click(Array.from(row.querySelectorAll('button')).find((b) => b.textContent === 'Kapitel speichern')!) })

    expect(client.saveManualChapter).toHaveBeenCalledWith('suncraft', '{"title":"Beste Waffen","sections":[{}]}', expect.objectContaining({ title: 'Beste Waffen' }))
    expect(onSaved).toHaveBeenCalledWith(game)
    expect(row.getAttribute('data-done')).toBe('true')
    expect(screen.getByTestId('manual-progress').textContent).toBe('Gespeichert: 1 von 2')
  })

  it('lets the player choose the topics himself without a blueprint prompt', async () => {
    client.manualBlueprintPrompt.mockResolvedValue(prompt({ kind: 'blueprint' }))
    client.fetchGuideTopics.mockResolvedValue([
      { id: 'weapons', title: 'Beste Waffen', icon: 'mdi:sword', brief: 'Alle Waffen', groupKey: 'combat' },
      { id: 'quests', title: 'Nebenquests', icon: 'mdi:script', brief: 'Alle Quests', groupKey: 'explore' },
    ])
    wrap(<ManualGuideDialog slug="suncraft" gameTitle="Suncraft" language="de" onClose={vi.fn()} onSaved={vi.fn()} />)

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: /Ich wähle die Themen selbst/ })) })

    expect(client.fetchGuideTopics).toHaveBeenCalledWith('de')
    expect(screen.getByRole('button', { name: /Weiter: 0 Kapitel/ }).hasAttribute('disabled')).toBe(true)
    fireEvent.click(screen.getByRole('checkbox', { name: /Nebenquests/ }))
    expect(screen.getByRole('button', { name: /Weiter: 1 Kapitel/ }).hasAttribute('disabled')).toBe(false)
  })

  it('researches one failed chapter again: its answer replaces the placeholder', async () => {
    client.manualChapterPrompt.mockResolvedValue(prompt())
    client.checkManualChapter.mockResolvedValue(okChapter('Dorfausbau'))
    const game = { slug: 'suncraft', tabs: [] } as unknown as GameDetailDto
    client.saveManualChapter.mockResolvedValue({ game, tabId: 'tab-1', replaced: true })
    const seed = { title: 'Dorfausbau', icon: 'mdi:home', focus: 'Alle Ausbaustufen', topicId: 'extra', tabId: 'tab-1' }

    wrap(<ManualGuideDialog slug="suncraft" gameTitle="Suncraft" language="de" chapter={seed} onClose={vi.fn()} onSaved={vi.fn()} />)

    expect(screen.getByText('Ersetzt den Hinweis „nicht recherchiert“')).toBeTruthy()
    // the paste area is open at once; there is no blueprint step
    fireEvent.change(screen.getByRole('textbox'), { target: { value: '{"title":"Dorfausbau","sections":[{}]}' } })
    await settle()
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Kapitel speichern' })) })

    expect(client.saveManualChapter).toHaveBeenCalledWith('suncraft', expect.any(String), expect.objectContaining({ tabId: 'tab-1', focus: 'Alle Ausbaustufen' }))
    expect(screen.getByText('Gespeichert')).toBeTruthy()
  })

  it('accepts the answer "this topic does not exist in the game": shows the reason, saves nothing and marks the chapter as left out', async () => {
    client.manualChapterPrompt.mockResolvedValue(prompt())
    const sentence = 'Laut dieser Antwort gibt es das Thema in „Suncraft“ nicht: keine Gegner. Beim Speichern wird das fehlgeschlagene Kapitel entfernt, statt es zu füllen.'
    client.checkManualChapter.mockResolvedValue({ ok: true, errors: [], warnings: [], preview: null, notApplicable: sentence } satisfies ManualChapterCheckDto)
    const game = { slug: 'suncraft', tabs: [] } as unknown as GameDetailDto
    client.saveManualChapter.mockResolvedValue({ game, tabId: 'tab-1', replaced: false, dropped: true })
    const onSaved = vi.fn()
    const seed = { title: 'Gegner', icon: 'mdi:skull', focus: 'Alle Gegner', topicId: 'bestiary', tabId: 'tab-1' }

    wrap(<ManualGuideDialog slug="suncraft" gameTitle="Suncraft" language="de" chapter={seed} onClose={vi.fn()} onSaved={onSaved} />)

    fireEvent.change(screen.getByRole('textbox'), { target: { value: '{"notApplicable":true,"reason":"keine Gegner"}' } })
    await settle()
    // the complete sentence of the server, and the save button is ready although there is no chapter preview
    expect(screen.getByTestId('answer-result').textContent).toContain(sentence)
    const save = screen.getByRole('button', { name: 'Kapitel speichern' })
    expect(save.hasAttribute('disabled')).toBe(false)
    await act(async () => { fireEvent.click(save) })

    expect(onSaved).toHaveBeenCalledWith(game)
    expect(screen.getByText('Entfällt – gibt es in diesem Spiel nicht')).toBeTruthy()
  })
})
