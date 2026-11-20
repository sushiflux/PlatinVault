import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GameDetailDto, ManualBatchCheckDto, ManualBatchPromptDto, ManualOneShotPlanDto, ManualPromptDto } from '../../../api/types'
import { ConfirmProvider } from '../../ui/ConfirmDialog'
import { isOneShotRows, readStoredBatch, readStoredText, rowsFromPlan } from './batch'
import { ManualGuideDialog } from './ManualGuideDialog'

const client = vi.hoisted(() => ({
  checkManualBlueprint: vi.fn(),
  checkManualChapter: vi.fn(),
  checkManualOneShotPlan: vi.fn(),
  fetchGuideTopics: vi.fn(),
  manualBlueprintPrompt: vi.fn(),
  manualChapterPrompt: vi.fn(),
  manualOneShotPrompt: vi.fn(),
  saveManualChapter: vi.fn(),
  manualBatchPrompt: vi.fn(),
  checkManualBatch: vi.fn(),
  saveManualBatch: vi.fn(),
}))
vi.mock('../../../api/client', () => client)

const oneShotPrompt: ManualPromptDto = {
  kind: 'oneshot',
  prompt: 'Recherchiere „Suncraft“, wähle die Kapitel und schreibe alle.',
  fileName: 'platinvault-suncraft-guide.md',
  language: 'de',
  steamAppId: 4078110,
  title: 'Suncraft',
}

const planOk = (): ManualOneShotPlanDto => ({
  ok: true,
  errors: [],
  warnings: [{ code: 'unknownTopic', message: 'Das Thema „fishing“ gibt es nicht in der Liste; es wird als Extra-Kapitel geführt.', details: null }],
  chapters: [
    { key: 'K01', title: 'Komplettlösung', focus: 'Der Weg durch das Spiel', topicId: 'walkthrough', icon: 'mdi:map' },
    { key: 'K02', title: 'Beste Waffen', focus: 'Alle Waffen', topicId: 'weapons', icon: 'mdi:sword' },
    { key: 'K03', title: 'Angeln', focus: 'Fische und Orte', topicId: 'extra', icon: 'mdi:fish', tabId: 'tab-fail' },
  ],
})

const batchPrompt = (keys: string[]): ManualBatchPromptDto => ({
  kind: 'batch',
  prompt: `Batch ${keys.join(', ')}`,
  fileName: 'batch.md',
  language: 'de',
  steamAppId: 4078110,
  gameTitle: 'Suncraft',
  chapters: keys.map((key) => ({ key, title: key, topicId: null })),
})

const batchCheck: ManualBatchCheckDto = {
  found: 2,
  items: [
    { key: 'K01', title: 'Komplettlösung', state: 'ok', errors: [], warnings: [], preview: { title: 'x', icon: 'mdi:x', sectionCount: 3, tableRows: 1, cards: 1, sectionTitles: [] }, notApplicable: null },
    { key: 'K02', title: 'Beste Waffen', state: 'ok', errors: [], warnings: [], preview: { title: 'x', icon: 'mdi:x', sectionCount: 3, tableRows: 1, cards: 1, sectionTitles: [] }, notApplicable: null },
    { key: 'K03', title: 'Angeln', state: 'missing', errors: [], warnings: [], preview: null, notApplicable: null },
  ],
  unmatched: [],
}

const ANSWER = '[{"game":"Suncraft","plan":[{"chapter":"K01"}]},{"chapter":"K01","sections":[]}]'

function open(props: { onClose?: () => void; onSaved?: (g: GameDetailDto) => void } = {}) {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <ConfirmProvider>
        <ManualGuideDialog slug="suncraft" gameTitle="Suncraft" language="de" onClose={props.onClose ?? vi.fn()} onSaved={props.onSaved ?? vi.fn()} />
      </ConfirmProvider>
    </QueryClientProvider>
  )
}

// The answer box of the one-step view is the only textbox that is not read-only
const answerField = () => screen.getAllByRole('textbox').find((el) => !(el as HTMLTextAreaElement).readOnly) as HTMLTextAreaElement
const batchField = () => screen.getByLabelText('Alle eingefügten Antworten des Chats') as HTMLTextAreaElement
const next = () => screen.getByRole('button', { name: /^Weiter: / })

async function pasteAnswer() {
  fireEvent.change(answerField(), { target: { value: ANSWER } })
  // The check is debounced; the mocked server answers at once
  await waitFor(() => expect(client.checkManualOneShotPlan).toHaveBeenCalled(), { timeout: 3000 })
  await waitFor(() => expect(next().hasAttribute('disabled')).toBe(false), { timeout: 3000 })
}

beforeEach(() => {
  window.sessionStorage.clear()
  Object.values(client).forEach((fn) => fn.mockReset())
  client.manualOneShotPrompt.mockResolvedValue(oneShotPrompt)
  client.checkManualOneShotPlan.mockResolvedValue(planOk())
  client.manualBatchPrompt.mockImplementation(async (_slug: string, chapters: { key: string }[]) => batchPrompt(chapters.map((c) => c.key)))
  client.checkManualBatch.mockResolvedValue(batchCheck)
  Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true })
  Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn(async () => undefined) }, configurable: true })
})
afterEach(() => cleanup())

describe('rowsFromPlan', () => {
  it('builds rows with stable "oneshot:" keys, the plan ids and the tabId of a placeholder', () => {
    const { rows, keys } = rowsFromPlan(planOk().chapters)
    expect(rows.map((row) => row.key)).toEqual(['oneshot:K01', 'oneshot:K02', 'oneshot:K03'])
    expect(rows.map((row) => row.batchKey)).toEqual(['K01', 'K02', 'K03'])
    expect(keys).toEqual({ 'oneshot:K01': 'K01', 'oneshot:K02': 'K02', 'oneshot:K03': 'K03' })
    expect(rows[2]!.seed).toMatchObject({ title: 'Angeln', topicId: 'extra', tabId: 'tab-fail' })
    expect(isOneShotRows(rows)).toBe(true)
    expect(isOneShotRows([{ key: 'weapons' }])).toBe(false)
  })
})

describe('one prompt for the whole guide', () => {
  it('opens with the one prompt, explains it in one calm line and leads from the pasted answer to the batch view with the text already in', async () => {
    open()

    expect(screen.getByTestId('oneshot-lead').textContent).toContain('sucht die Kapitel selbst aus')
    expect(screen.getByRole('dialog').textContent).toContain('„weiter“')
    expect(next().hasAttribute('disabled')).toBe(true)

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Prompt kopieren' })) })
    expect(client.manualOneShotPrompt).toHaveBeenCalledWith('suncraft')
    expect(client.manualBlueprintPrompt).not.toHaveBeenCalled()
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(oneShotPrompt.prompt)

    await pasteAnswer()
    expect(client.checkManualOneShotPlan).toHaveBeenCalledWith('suncraft', ANSWER)
    // the plan is shown completely, with its warning
    const result = screen.getByTestId('answer-result')
    expect(result.textContent).toContain('Der Chat hat 3 Kapitel geplant')
    expect(result.textContent).toContain('Komplettlösung')
    expect(result.textContent).toContain('Das Thema „fishing“ gibt es nicht in der Liste')
    expect(next().textContent).toBe('Weiter: 3 Kapitel')

    await act(async () => { fireEvent.click(next()) })

    // the batch view: one row per planned chapter, the pasted text is in its answer field and checked at once
    expect(screen.getAllByTestId('batch-row').map((row) => row.getAttribute('data-key'))).toEqual(['K01', 'K02', 'K03'])
    expect(batchField().value).toBe(ANSWER)
    await waitFor(() => expect(client.checkManualBatch).toHaveBeenCalled(), { timeout: 3000 })
    const sent = client.checkManualBatch.mock.calls[0]!
    expect(sent[1]).toBe(ANSWER)
    expect(sent[2]).toEqual([
      expect.objectContaining({ key: 'K01', title: 'Komplettlösung', topicId: 'walkthrough' }),
      expect.objectContaining({ key: 'K02', title: 'Beste Waffen', topicId: 'weapons' }),
      expect.objectContaining({ key: 'K03', title: 'Angeln', tabId: 'tab-fail' }),
    ])
    await waitFor(() => expect(screen.getByTestId('batch-counter').textContent).toBe('2 von 3 Kapiteln erkannt'), { timeout: 3000 })
    expect(screen.getByTestId('batch-save').textContent).toContain('Kapitel speichern')
    expect(screen.getByRole('dialog').textContent).toContain('Antwort prüfen und Kapitel speichern')
  })

  it('shows every error of an unreadable plan completely and does not offer to go on', async () => {
    const message = 'Die Antwort enthält keinen Plan: Das erste Element des Arrays muss ein Objekt mit dem Feld „plan“ sein. Bitte den Chat, die Antwort mit dem Plan als erstem Element neu auszugeben.'
    client.checkManualOneShotPlan.mockResolvedValue({ ok: false, errors: [{ code: 'noPlan', message, details: 'array[0] has no "plan"' }], warnings: [], chapters: [] } satisfies ManualOneShotPlanDto)
    open()

    fireEvent.change(answerField(), { target: { value: '[{"chapter":"K01"}]' } })
    await waitFor(() => expect(screen.getByTestId('answer-result').textContent).toContain(message), { timeout: 3000 })

    expect(next().hasAttribute('disabled')).toBe(true)
    expect(screen.getByTestId('answer-result').querySelector('[data-issue="noPlan"]')).not.toBeNull()
  })

  it('says so when no chapter is left to write and does not go on', async () => {
    client.checkManualOneShotPlan.mockResolvedValue({ ok: true, errors: [], warnings: [], chapters: [] } satisfies ManualOneShotPlanDto)
    open()

    fireEvent.change(answerField(), { target: { value: ANSWER } })
    await waitFor(() => expect(screen.getByTestId('answer-result').textContent).toContain('kein Kapitel, das noch fehlt'), { timeout: 3000 })
    expect(next().hasAttribute('disabled')).toBe(true)
  })

  it('"Kapitel selbst auswählen" shows the blueprint step, and there is a way back to the one prompt', async () => {
    client.manualBlueprintPrompt.mockResolvedValue({ ...oneShotPrompt, kind: 'blueprint', fileName: 'bauplan.md' })
    open()

    fireEvent.click(screen.getByTestId('manual-choose-myself'))

    expect(screen.getByRole('dialog').textContent).toContain('Schritt 1 von 2 · Kapitelliste')
    expect(screen.getByRole('button', { name: /Ich wähle die Themen selbst/ })).toBeTruthy()
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Prompt kopieren' })) })
    expect(client.manualBlueprintPrompt).toHaveBeenCalledWith('suncraft')
    expect(client.manualOneShotPrompt).not.toHaveBeenCalled()

    fireEvent.click(screen.getByTestId('manual-back-oneshot'))
    expect(screen.getByTestId('oneshot-lead')).toBeTruthy()
    expect(screen.getByTestId('manual-choose-myself')).toBeTruthy()
  })

  it('"back" from the batch view of a plan leads to the one prompt and keeps the pasted text; a reopened dialog continues in the batch view', async () => {
    const first = open()
    await pasteAnswer()
    await act(async () => { fireEvent.click(next()) })
    await waitFor(() => expect(client.checkManualBatch).toHaveBeenCalled(), { timeout: 3000 })
    expect(readStoredText('suncraft', 'plan')).toBe(ANSWER)
    expect(readStoredBatch('suncraft', 'plan')?.rows.map((row) => row.key)).toEqual(['oneshot:K01', 'oneshot:K02', 'oneshot:K03'])

    // closed and opened again: right where it was
    first.unmount()
    open()
    expect(screen.getAllByTestId('batch-row')).toHaveLength(3)
    expect(batchField().value).toBe(ANSWER)

    fireEvent.click(screen.getByRole('button', { name: 'Zurück zum Prompt' }))
    expect(screen.getByTestId('oneshot-lead')).toBeTruthy()
    // the pasted answer is read again
    await waitFor(() => expect(answerField().value).toBe(ANSWER), { timeout: 3000 })
  })

  it('does not hand the old answer of a one-step plan to topics chosen afterwards', async () => {
    client.manualBlueprintPrompt.mockResolvedValue({ ...oneShotPrompt, kind: 'blueprint' })
    client.fetchGuideTopics.mockResolvedValue([{ id: 'weapons', title: 'Beste Waffen', icon: 'mdi:sword', brief: 'Alle Waffen', groupKey: 'combat' }])
    open()
    await pasteAnswer()
    await act(async () => { fireEvent.click(next()) })
    fireEvent.click(screen.getByRole('button', { name: 'Zurück zum Prompt' }))
    fireEvent.click(screen.getByTestId('manual-choose-myself'))
    expect(readStoredText('suncraft', 'plan')).toBe('')
  })
})

describe('fixed lists', () => {
  it('skip both steps: a failed chapter goes straight to its prompt', () => {
    client.manualChapterPrompt.mockResolvedValue(oneShotPrompt)
    render(
      <QueryClientProvider client={new QueryClient()}>
        <ConfirmProvider>
          <ManualGuideDialog slug="suncraft" gameTitle="Suncraft" language="de" chapter={{ title: 'Angeln', icon: 'mdi:fish', focus: 'x', topicId: 'extra', tabId: 'tab-1' }} onClose={vi.fn()} onSaved={vi.fn()} />
        </ConfirmProvider>
      </QueryClientProvider>
    )
    expect(screen.queryByTestId('oneshot-lead')).toBeNull()
    expect(screen.queryByTestId('manual-choose-myself')).toBeNull()
    expect(screen.getByText('Ersetzt den Hinweis „nicht recherchiert“')).toBeTruthy()
    expect(client.manualOneShotPrompt).not.toHaveBeenCalled()
  })

  it('a list of failed chapters opens the batch view without the one prompt', () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <ConfirmProvider>
          <ManualGuideDialog
            slug="suncraft"
            gameTitle="Suncraft"
            language="de"
            chapters={[
              { title: 'A', icon: 'mdi:x', focus: 'a', topicId: null, tabId: 't1' },
              { title: 'B', icon: 'mdi:x', focus: 'b', topicId: null, tabId: 't2' },
              { title: 'C', icon: 'mdi:x', focus: 'c', topicId: null, tabId: 't3' },
            ]}
            onClose={vi.fn()}
            onSaved={vi.fn()}
          />
        </ConfirmProvider>
      </QueryClientProvider>
    )
    expect(screen.getAllByTestId('batch-row')).toHaveLength(3)
    expect(screen.queryByTestId('manual-choose-myself')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Zurück zum Prompt' })).toBeNull()
  })
})
