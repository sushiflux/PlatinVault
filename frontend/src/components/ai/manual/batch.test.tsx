import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GameDetailDto, ManualBatchCheckDto, ManualBatchItemCheckDto, ManualBatchPromptDto, ManualChapterSeed } from '../../../api/types'
import { ConfirmProvider } from '../../ui/ConfirmDialog'
import { assignBatchKeys, formatBatchKey, readStoredBatch, readStoredText } from './batch'
import { ManualGuideDialog } from './ManualGuideDialog'

const client = vi.hoisted(() => ({
  checkManualBlueprint: vi.fn(),
  checkManualChapter: vi.fn(),
  fetchGuideTopics: vi.fn(),
  manualBlueprintPrompt: vi.fn(),
  manualChapterPrompt: vi.fn(),
  saveManualChapter: vi.fn(),
  manualBatchPrompt: vi.fn(),
  checkManualBatch: vi.fn(),
  saveManualBatch: vi.fn(),
}))
vi.mock('../../../api/client', () => client)

const seeds: ManualChapterSeed[] = [
  { title: 'Beste Waffen', icon: 'mdi:sword', focus: 'Alle Waffen', topicId: 'weapons', tabId: 'tab-1' },
  { title: 'Gegner', icon: 'mdi:skull', focus: 'Alle Gegner', topicId: 'bestiary', tabId: 'tab-2' },
  { title: 'Nebenquests', icon: 'mdi:script', focus: 'Alle Quests', topicId: 'quests', tabId: 'tab-3' },
]

const batchPrompt = (keys: string[]): ManualBatchPromptDto => ({
  kind: 'batch',
  prompt: `Batch-Prompt für ${keys.join(', ')}`,
  fileName: 'platinvault-suncraft-batch.md',
  language: 'de',
  steamAppId: 4078110,
  gameTitle: 'Suncraft',
  chapters: keys.map((key) => ({ key, title: key, topicId: null })),
})

const item = (key: string, title: string, over: Partial<ManualBatchItemCheckDto> = {}): ManualBatchItemCheckDto => ({
  key,
  title,
  state: 'missing',
  errors: [],
  warnings: [],
  preview: null,
  notApplicable: null,
  ...over,
})

const preview = { title: 'x', icon: 'mdi:x', sectionCount: 3, tableRows: 12, cards: 4, sectionTitles: [] }

// K01 ready, K02 not in the text, K03 with an error; one answer nobody claims
const mixedCheck: ManualBatchCheckDto = {
  found: 3,
  items: [
    item('K01', 'Beste Waffen', { state: 'ok', preview, warnings: [{ code: 'noAppId', message: 'Die Antwort nennt keine Steam-AppID.', details: null }] }),
    item('K02', 'Gegner'),
    item('K03', 'Nebenquests', { state: 'error', errors: [{ code: 'cutOff', message: 'Die Antwort ist abgeschnitten: Das JSON-Objekt wird nicht geschlossen.', details: 'Expected depth to be zero' }] }),
  ],
  unmatched: [{ index: 2, title: 'Unbekannt', errors: [{ code: 'noKey', message: 'Diese Antwort nennt keine Kapitel-ID.', details: null }] }],
}

const game = { slug: 'suncraft', tabs: [] } as unknown as GameDetailDto

function open(chapters: ManualChapterSeed[] = seeds, props: { onSaved?: (g: GameDetailDto) => void; onClose?: () => void } = {}) {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <ConfirmProvider>
        <ManualGuideDialog slug="suncraft" gameTitle="Suncraft" language="de" chapters={chapters} onClose={props.onClose ?? vi.fn()} onSaved={props.onSaved ?? vi.fn()} />
      </ConfirmProvider>
    </QueryClientProvider>
  )
}

const field = () => screen.getByLabelText('Alle eingefügten Antworten des Chats') as HTMLTextAreaElement
const pasteInto = (text: string) => fireEvent.paste(field(), { clipboardData: { getData: () => text } })
const rowOf = (key: string) => screen.getAllByTestId('batch-row').find((row) => row.getAttribute('data-key') === key)!
// The debounce is 500 ms and the mocked server answers at once
const checked = (expected: string) => waitFor(() => expect(screen.getByTestId('batch-counter').textContent).toBe(expected), { timeout: 3000 })

beforeEach(() => {
  window.sessionStorage.clear()
  Object.values(client).forEach((fn) => fn.mockReset())
  client.manualBatchPrompt.mockImplementation(async (_slug: string, chapters: { key: string }[]) => batchPrompt(chapters.map((c) => c.key)))
  client.checkManualBatch.mockResolvedValue(mixedCheck)
  Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true })
  Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn(async () => undefined) }, configurable: true })
})
afterEach(() => cleanup())

describe('batch keys', () => {
  it('numbers chapters K01, K02 … and keeps a key when the selection changes', () => {
    expect(formatBatchKey(7)).toBe('K07')
    const first = assignBatchKeys(['a', 'b', 'c'], {})
    expect(first).toEqual({ a: 'K01', b: 'K02', c: 'K03' })
    // b is dropped, d is new: a and c keep their ids, d gets the next free one
    expect(assignBatchKeys(['a', 'c', 'd'], first)).toEqual({ a: 'K01', b: 'K02', c: 'K03', d: 'K04' })
  })
})

describe('mode switch', () => {
  it('defaults to "All at once" from three chapters and to "One by one" below', () => {
    const { unmount } = open(seeds)
    expect(screen.getByRole('button', { name: 'Alle auf einmal' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByRole('button', { name: 'Einzeln' }).getAttribute('aria-pressed')).toBe('false')
    expect(screen.getByTestId('batch-view')).toBeTruthy()
    unmount()
    window.sessionStorage.clear()

    open(seeds.slice(0, 2))
    expect(screen.getByRole('button', { name: 'Einzeln' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.queryByTestId('batch-view')).toBeNull()
    expect(screen.getAllByTestId('manual-row')).toHaveLength(2)
  })

  it('switches between both without losing the pasted text', async () => {
    open(seeds)
    pasteInto('{"a":1}')
    fireEvent.click(screen.getByRole('button', { name: 'Einzeln' }))
    expect(screen.getAllByTestId('manual-row')).toHaveLength(3)
    fireEvent.click(screen.getByRole('button', { name: 'Alle auf einmal' }))
    expect(field().value).toBe('{"a":1}')
  })
})

describe('batch view', () => {
  it('explains the steps, offers the prompt for all chapters and shows its size', async () => {
    open()
    expect(screen.getByTestId('batch-view').textContent).toContain('weiter')
    await waitFor(() => expect(screen.getByTestId('batch-prompt-info').textContent).toBe('3 Kapitel · Prompt ca. 1 KB'))
    expect(client.manualBatchPrompt).toHaveBeenCalledWith('suncraft', expect.arrayContaining([expect.objectContaining({ key: 'K01', title: 'Beste Waffen', tabId: 'tab-1', topicId: 'weapons' })]))
    expect(client.manualBatchPrompt.mock.calls[0]![1].map((c: { key: string }) => c.key)).toEqual(['K01', 'K02', 'K03'])

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Prompt kopieren' })) })
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(expect.stringContaining('K01, K02, K03'))
  })

  it('appends every paste instead of replacing the text', () => {
    open()
    expect(screen.getByTestId('batch-meta').textContent).toContain('Noch nichts eingefügt')
    pasteInto('{"key":"K01"}')
    pasteInto('{"key":"K02"}')
    expect(field().value).toBe('{"key":"K01"}\n\n{"key":"K02"}')
    expect(screen.getByTestId('batch-meta').textContent).toContain('Zeichen gesammelt')
    expect(readStoredText('suncraft', 'failed')).toBe('{"key":"K01"}\n\n{"key":"K02"}')
  })

  it('reads dropped files and appends them', async () => {
    open()
    pasteInto('{"key":"K01"}')
    const file = new File(['{"key":"K02"}'], 'antwort.json', { type: 'application/json' })
    fireEvent.drop(screen.getByTestId('batch-dropzone'), { dataTransfer: { files: [file], types: ['Files'] } })
    await waitFor(() => expect(field().value).toBe('{"key":"K01"}\n\n{"key":"K02"}'))
    // a file chosen with the button goes the same way
    const input = screen.getByTestId('batch-file') as HTMLInputElement
    fireEvent.change(input, { target: { files: [new File(['# K03'], 'rest.md')] } })
    await waitFor(() => expect(field().value).toContain('# K03'))
  })

  it('shows the status of every chapter from the check, with complete and copyable details', async () => {
    open()
    expect(within(rowOf('K01')).getByText('Ausstehend')).toBeTruthy()
    pasteInto('irgendwas')
    await checked('2 von 3 Kapiteln erkannt')

    expect(client.checkManualBatch).toHaveBeenCalledWith('suncraft', 'irgendwas', expect.arrayContaining([expect.objectContaining({ key: 'K03' })]))
    expect(rowOf('K01').getAttribute('data-status')).toBe('ok')
    expect(rowOf('K02').getAttribute('data-status')).toBe('pending')
    expect(rowOf('K03').getAttribute('data-status')).toBe('error')
    expect(within(rowOf('K03')).getByText('Fehler')).toBeTruthy()
    // the screen reader gets the count politely
    expect(screen.getByTestId('batch-announce').textContent).toBe('2 von 3 Kapiteln erkannt')

    // errors and notes are folded in per row and shown completely, each with the copy button
    fireEvent.click(within(rowOf('K03')).getByRole('button', { name: /Details zu „Nebenquests“/ }))
    const details = within(rowOf('K03')).getByTestId('batch-row-details')
    expect(details.textContent).toContain('Die Antwort ist abgeschnitten: Das JSON-Objekt wird nicht geschlossen.')
    expect(within(details).getAllByTestId('copy-error').length).toBeGreaterThan(0)
    fireEvent.click(within(rowOf('K01')).getByRole('button', { name: /Details zu „Beste Waffen“/ }))
    expect(within(rowOf('K01')).getByTestId('batch-row-details').textContent).toContain('Die Antwort nennt keine Steam-AppID.')
    expect(within(rowOf('K01')).getByTestId('batch-row-details').textContent).toContain('Vorschau')

    // the answer nobody claims is listed with its errors
    expect(screen.getByTestId('batch-unmatched').textContent).toContain('Antwort 2: „Unbekannt“')
    expect(screen.getByTestId('batch-unmatched').textContent).toContain('Diese Antwort nennt keine Kapitel-ID.')
  })

  it('shows the foreign-language warning with its examples and copies the correction prompt of that chapter', async () => {
    client.checkManualBatch.mockResolvedValue({
      found: 1,
      items: [
        item('K01', 'Beste Waffen', {
          state: 'warning',
          preview,
          warnings: [{ code: 'foreignLanguage', message: '12 Sätze sind nicht auf Deutsch.', details: null, examples: ['The sword deals fire damage.', 'Use it against the boss.', 'Found in the cellar.'] }],
          fixPrompt: 'Übersetze diese Sätze ins Deutsche: K01',
        }),
        item('K02', 'Gegner'),
        item('K03', 'Nebenquests'),
      ],
      unmatched: [],
    } satisfies ManualBatchCheckDto)
    open()
    pasteInto('x')
    await checked('1 von 3 Kapiteln erkannt')

    fireEvent.click(within(rowOf('K01')).getByRole('button', { name: /Details zu „Beste Waffen“/ }))
    const details = within(rowOf('K01')).getByTestId('batch-row-details')
    expect(details.textContent).toContain('12 Sätze sind nicht auf Deutsch.')
    const examples = within(details).getByTestId('issue-examples')
    expect(examples.querySelectorAll('li')).toHaveLength(3)
    expect(examples.textContent).toContain('The sword deals fire damage.')

    await act(async () => { fireEvent.click(within(details).getByRole('button', { name: 'Korrektur-Prompt kopieren' })) })
    expect(navigator.clipboard.writeText).toHaveBeenLastCalledWith('Übersetze diese Sätze ins Deutsche: K01')
    expect(within(details).getByTestId('fix-prompt-status').textContent).toContain('Korrektur-Prompt kopiert')
    // a chapter without a correction prompt has no such button
    fireEvent.click(within(rowOf('K02')).queryByRole('button', { name: /Details zu „Gegner“/ }) ?? document.body)
    expect(within(rowOf('K02')).queryByRole('button', { name: 'Korrektur-Prompt kopieren' })).toBeNull()
  })

  it('sends the replace flag of a normal chapter that is researched again with every batch request', async () => {
    const redo: ManualChapterSeed[] = [
      { title: 'Waffen', icon: 'mdi:sword', focus: 'Waffen', topicId: null, tabId: 'tab-9', replace: true },
      { title: 'Gegner', icon: 'mdi:skull', focus: 'Gegner', topicId: null, tabId: 'tab-8', replace: true },
      { title: 'Quests', icon: 'mdi:script', focus: 'Quests', topicId: null, tabId: 'tab-7', replace: true },
    ]
    open(redo)
    pasteInto('x')
    await checked('2 von 3 Kapiteln erkannt')
    expect(client.manualBatchPrompt.mock.calls[0]![1]).toEqual(expect.arrayContaining([expect.objectContaining({ key: 'K01', tabId: 'tab-9', replace: true })]))
    expect(client.checkManualBatch.mock.calls[0]![2]).toEqual(expect.arrayContaining([expect.objectContaining({ key: 'K02', tabId: 'tab-8', replace: true })]))
  })

  it('labels a topic the game does not have', async () => {
    client.checkManualBatch.mockResolvedValue({
      found: 1,
      items: [item('K01', 'Beste Waffen'), item('K02', 'Gegner', { state: 'notApplicable', notApplicable: 'Laut dieser Antwort gibt es das Thema nicht.' }), item('K03', 'Nebenquests')],
      unmatched: [],
    } satisfies ManualBatchCheckDto)
    open()
    pasteInto('x')
    await checked('1 von 3 Kapiteln erkannt')
    expect(within(rowOf('K02')).getByText('Gibt es im Spiel nicht')).toBeTruthy()
    expect((screen.getByTestId('batch-save') as HTMLButtonElement).disabled).toBe(false)
    expect(screen.getByTestId('batch-save').textContent).toContain('1 Kapitel speichern')
  })

  it('names the reason while save is not possible', async () => {
    open()
    const save = screen.getByTestId('batch-save') as HTMLButtonElement
    expect(save.disabled).toBe(true)
    expect(screen.getByTestId('batch-footer-message').textContent).toContain('Noch keine Antwort eingefügt')
    client.checkManualBatch.mockResolvedValue({ found: 0, items: seeds.map((s, i) => item(`K0${i + 1}`, s.title)), unmatched: [] } satisfies ManualBatchCheckDto)
    pasteInto('nichts brauchbares')
    await waitFor(() => expect(screen.getByTestId('batch-footer-message').textContent).toContain('Keine der Antworten ist speicherbar'), { timeout: 3000 })
    expect(save.disabled).toBe(true)
  })

  it('saves the readable chapters with their keys, marks them and keeps the text that still has problems', async () => {
    client.saveManualBatch.mockResolvedValue({
      detail: game,
      saved: [{ key: 'K01', tabId: 'tab-1', replaced: true, dropped: false }],
      skipped: [],
    })
    const onSaved = vi.fn()
    open(seeds, { onSaved })
    pasteInto('{"k":1}')
    await checked('2 von 3 Kapiteln erkannt')

    await act(async () => { fireEvent.click(screen.getByTestId('batch-save')) })

    expect(client.saveManualBatch).toHaveBeenCalledWith('suncraft', '{"k":1}', expect.any(Array), ['K01'])
    expect(onSaved).toHaveBeenCalledWith(game)
    expect(rowOf('K01').getAttribute('data-status')).toBe('saved')
    expect(within(rowOf('K01')).getByText('Gespeichert')).toBeTruthy()
    expect(screen.getByTestId('manual-progress').textContent).toBe('Gespeichert: 1 von 3')
    // K03 still has an error: the text stays so the player can look at it
    expect(field().value).toBe('{"k":1}')
    expect(readStoredBatch('suncraft', 'failed')?.saved).toEqual(['tab-1'])
  })

  it('clears the text after a clean save and asks for the rest with the same keys', async () => {
    client.checkManualBatch.mockResolvedValue({
      found: 2,
      items: [item('K01', 'Beste Waffen', { state: 'ok', preview }), item('K02', 'Gegner', { state: 'ok', preview }), item('K03', 'Nebenquests')],
      unmatched: [],
    } satisfies ManualBatchCheckDto)
    client.saveManualBatch.mockResolvedValue({
      detail: game,
      saved: [
        { key: 'K01', tabId: 'tab-1', replaced: true, dropped: false },
        { key: 'K02', tabId: 'tab-2', replaced: true, dropped: false },
      ],
      skipped: [],
    })
    open()
    pasteInto('{"k":1}')
    await checked('2 von 3 Kapiteln erkannt')
    // before saving, the prompt for the rest asks only for what is neither saved nor ready
    await act(async () => { fireEvent.click(screen.getByTestId('batch-rest')) })
    expect(client.manualBatchPrompt).toHaveBeenLastCalledWith('suncraft', [expect.objectContaining({ key: 'K03', title: 'Nebenquests', tabId: 'tab-3' })])
    expect(navigator.clipboard.writeText).toHaveBeenLastCalledWith('Batch-Prompt für K03')
    expect(screen.getByTestId('batch-footer-message').textContent).toContain('Prompt für die restlichen Kapitel kopiert')

    await act(async () => { fireEvent.click(screen.getByTestId('batch-save')) })
    expect(client.saveManualBatch).toHaveBeenCalledWith('suncraft', '{"k":1}', expect.any(Array), ['K01', 'K02'])
    expect(field().value).toBe('')
    expect(rowOf('K03').getAttribute('data-status')).toBe('pending')

    // after saving, the rest prompt still asks for exactly the open chapter, with its stable key
    client.manualBatchPrompt.mockClear()
    await act(async () => { fireEvent.click(screen.getByTestId('batch-rest')) })
    expect(client.manualBatchPrompt).toHaveBeenCalledTimes(1)
    expect(client.manualBatchPrompt).toHaveBeenLastCalledWith('suncraft', [expect.objectContaining({ key: 'K03' })])
  })

  it('lists chapters the server skipped with its reason, completely', async () => {
    client.saveManualBatch.mockResolvedValue({
      detail: game,
      saved: [],
      skipped: [{ key: 'K01', reason: 'Das Kapitel wurde inzwischen entfernt, der Platzhalter existiert nicht mehr.' }],
    })
    open()
    pasteInto('{"k":1}')
    await checked('2 von 3 Kapiteln erkannt')
    await act(async () => { fireEvent.click(screen.getByTestId('batch-save')) })
    const skipped = screen.getByTestId('batch-skipped')
    expect(skipped.textContent).toContain('Das Kapitel wurde inzwischen entfernt, der Platzhalter existiert nicht mehr.')
    expect(within(skipped).getAllByTestId('copy-error').length).toBe(1)
    expect(rowOf('K01').getAttribute('data-status')).toBe('ok')
  })

  it('names the reason when saving fails, with a copy button, and keeps everything', async () => {
    client.saveManualBatch.mockRejectedValue(new Error('Datenbank gesperrt'))
    open()
    pasteInto('{"k":1}')
    await checked('2 von 3 Kapiteln erkannt')
    await act(async () => { fireEvent.click(screen.getByTestId('batch-save')) })
    const alert = within(screen.getByTestId('batch-messages')).getAllByRole('alert')[0]!
    expect(alert.textContent).toContain('Die Kapitel konnten nicht gespeichert werden: Datenbank gesperrt')
    expect(within(alert).getByTestId('copy-error')).toBeTruthy()
    expect(field().value).toBe('{"k":1}')
  })

  it('shows a failed check completely and copyable', async () => {
    client.checkManualBatch.mockRejectedValue(new Error('Server nicht erreichbar: Zeitüberschreitung beim Lesen der Antworten'))
    open()
    pasteInto('{"k":1}')
    await waitFor(() => expect(screen.getByTestId('batch-messages').textContent).toContain('Die Prüfung ist fehlgeschlagen: Server nicht erreichbar: Zeitüberschreitung beim Lesen der Antworten'), { timeout: 3000 })
    expect(within(screen.getByTestId('batch-messages')).getByTestId('copy-error')).toBeTruthy()
  })

  it('asks before the collected text is cleared, in its own dialog', async () => {
    open()
    pasteInto('{"k":1}')
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Eingefügtes leeren' })) })
    const dialog = screen.getByRole('alertdialog')
    expect(dialog.textContent).toContain('Bereits gespeicherte Kapitel bleiben gespeichert')
    await act(async () => { fireEvent.click(within(dialog).getByRole('button', { name: 'Abbrechen' })) })
    expect(field().value).toBe('{"k":1}')

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Eingefügtes leeren' })) })
    await act(async () => { fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Leeren' })) })
    expect(field().value).toBe('')
    expect(readStoredText('suncraft', 'failed')).toBe('')
  })

  it('restores the collected text and the saved chapters after the dialog was closed', async () => {
    client.saveManualBatch.mockResolvedValue({ detail: game, saved: [{ key: 'K01', tabId: 'tab-1', replaced: true, dropped: false }], skipped: [] })
    const first = open()
    pasteInto('{"k":1}')
    await checked('2 von 3 Kapiteln erkannt')
    await act(async () => { fireEvent.click(screen.getByTestId('batch-save')) })
    first.unmount()

    open()
    expect(field().value).toBe('{"k":1}')
    expect(rowOf('K01').getAttribute('data-status')).toBe('saved')
    expect(screen.getByTestId('manual-progress').textContent).toBe('Gespeichert: 1 von 3')
  })

  it('forgets the stored work of a different list of chapters', () => {
    const first = open()
    pasteInto('{"k":1}')
    first.unmount()
    open(seeds.slice(0, 1).concat(seeds.slice(2)).concat([{ title: 'Neu', icon: 'mdi:x', focus: 'x', topicId: 'new', tabId: 'tab-9' }]))
    expect(field().value).toBe('')
  })

  it('uses only texts from props: the game title is in the header', () => {
    open()
    expect(screen.getByRole('dialog').textContent).toContain('Suncraft')
  })
})

describe('separate storage per list', () => {
  it('opening one chapter does not clear the pasted text of the failed-chapter list', () => {
    const first = open()
    pasteInto('{"key":"K01"}')
    expect(readStoredText('suncraft', 'failed')).toBe('{"key":"K01"}')
    first.unmount()

    const single = { ...seeds[0]!, replace: true }
    render(
      <QueryClientProvider client={new QueryClient()}>
        <ConfirmProvider>
          <ManualGuideDialog slug="suncraft" gameTitle="Suncraft" language="de" chapter={single} onClose={vi.fn()} onSaved={vi.fn()} />
        </ConfirmProvider>
      </QueryClientProvider>
    )
    expect(readStoredText('suncraft', 'failed')).toBe('{"key":"K01"}')
    expect(readStoredBatch('suncraft', 'failed')).not.toBeNull()
    expect(readStoredBatch('suncraft', 'chapter:tab-1')).not.toBeNull()
  })
})
