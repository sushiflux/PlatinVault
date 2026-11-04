import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AiProviderId, AiStatusDto } from '../../api/types'
import { providerStatus, statusFixture } from '../ai/ai.fixtures'
import { ConfirmProvider } from '../ui/ConfirmDialog'
import { RemoveChapterBar } from './RemoveChapterBar'
import { useConfirmRedoChapter } from './useRedoChapter'

const fetchAiStatus = vi.hoisted(() => vi.fn<() => Promise<AiStatusDto>>())
vi.mock('../../api/client', () => ({ fetchAiStatus }))

interface Calls {
  redo: (oneOff: AiProviderId | undefined) => void
  manual: () => void
  remove: () => void
}

// The bar and the question exactly as the game page wires them: the confirmation first, only then the action
function Harness({ calls, title = 'Waffen & Rüstungen', withRedo = true, busy = false, disabled = false }: { calls: Calls; title?: string; withRedo?: boolean; busy?: boolean; disabled?: boolean }) {
  const confirmRedo = useConfirmRedoChapter()
  return (
    <RemoveChapterBar
      disabled={disabled}
      onRemove={calls.remove}
      onRedo={withRedo ? (oneOff) => void confirmRedo(title, false).then((ok) => ok && calls.redo(oneOff)) : undefined}
      onRedoManual={withRedo ? () => void confirmRedo(title, true).then((ok) => ok && calls.manual()) : undefined}
      redoBusy={busy}
    />
  )
}

function setup(props: Partial<Parameters<typeof Harness>[0]> = {}, status = statusFixture()) {
  fetchAiStatus.mockResolvedValue(status)
  const calls: Calls = { redo: vi.fn(), manual: vi.fn(), remove: vi.fn() }
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ConfirmProvider>
        <Harness calls={calls} {...props} />
      </ConfirmProvider>
    </QueryClientProvider>
  )
  return calls
}

beforeEach(() => fetchAiStatus.mockReset())
afterEach(cleanup)

describe('Kapitel neu recherchieren (chapter action row)', () => {
  it('offers both ways next to "Kapitel entfernen"', async () => {
    setup()
    expect(await screen.findByRole('button', { name: /Kapitel neu recherchieren/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: /Mit meinem KI-Abo neu recherchieren/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Kapitel entfernen' })).toBeTruthy()
  })

  it('asks first, says the old version stays until the new one is finished, and starts on neither side effect', async () => {
    const calls = setup()
    fireEvent.click(await screen.findByRole('button', { name: /^Kapitel neu recherchieren/ }))

    const dialog = await screen.findByRole('alertdialog')
    expect(dialog.textContent).toContain('Kapitel „Waffen & Rüstungen“ neu recherchieren?')
    expect(dialog.textContent).toContain('ersetzt, sobald die neue Fassung fertig ist')
    expect(dialog.textContent).toContain('Häkchen bleiben bei allen Einträgen, die es in der neuen Fassung weiter gibt')
    expect(dialog.textContent).toContain('verbraucht dein Kontingent')
    expect(calls.redo).not.toHaveBeenCalled()
  })

  it('starts the research only after the confirmation', async () => {
    const calls = setup()
    fireEvent.click(await screen.findByRole('button', { name: /^Kapitel neu recherchieren/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Neu recherchieren' }))
    await waitFor(() => expect(calls.redo).toHaveBeenCalledTimes(1))
    expect(calls.redo).toHaveBeenCalledWith(undefined)
  })

  it('does nothing when the question is cancelled', async () => {
    const calls = setup()
    fireEvent.click(await screen.findByRole('button', { name: /^Kapitel neu recherchieren/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Abbrechen' }))
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    expect(calls.redo).not.toHaveBeenCalled()
  })

  it('hands the one-off AI choice of the button to the action', async () => {
    const calls = setup()
    fireEvent.click(await screen.findByRole('button', { name: /KI für diese Aktion wählen: Gemini/ }))
    fireEvent.click(screen.getByRole('menuitemradio', { name: /Claude/ }))
    fireEvent.click(screen.getByRole('button', { name: /^Kapitel neu recherchieren/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Neu recherchieren' }))
    await waitFor(() => expect(calls.redo).toHaveBeenCalledWith('claude'))
  })

  it('the subscription path says that nothing is replaced before the answer is saved, and opens only after the confirmation', async () => {
    const calls = setup()
    fireEvent.click(await screen.findByRole('button', { name: /Mit meinem KI-Abo neu recherchieren/ }))
    const dialog = await screen.findByRole('alertdialog')
    expect(dialog.textContent).toContain('erst ersetzt, wenn du die Antwort einfügst und speicherst')
    expect(calls.manual).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Prompt vorbereiten' }))
    await waitFor(() => expect(calls.manual).toHaveBeenCalledTimes(1))
    expect(calls.redo).not.toHaveBeenCalled()
  })

  it('waits while another AI action runs, and while its own job runs', async () => {
    setup({ disabled: true })
    const redo = await screen.findByRole('button', { name: /^Kapitel neu recherchieren/ })
    expect((redo as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: /Mit meinem KI-Abo neu recherchieren/ }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Kapitel entfernen' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('has no redo buttons for a chapter that cannot be redone, but keeps the remove button on the right', async () => {
    const calls = setup({ withRedo: false })
    expect(screen.queryByRole('button', { name: /neu recherchieren/ })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Kapitel entfernen' }))
    expect(calls.remove).toHaveBeenCalledTimes(1)
  })

  it('works in the mode "ask" (no second part on the button, the page asks for the AI)', async () => {
    const calls = setup({}, statusFixture({ mode: 'ask', providers: [providerStatus('gemini'), providerStatus('claude'), providerStatus('openai', { hasKey: false })] }))
    expect(screen.queryByRole('button', { name: /KI für diese Aktion wählen/ })).toBeNull()
    fireEvent.click(await screen.findByRole('button', { name: /^Kapitel neu recherchieren/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Neu recherchieren' }))
    await waitFor(() => expect(calls.redo).toHaveBeenCalledWith(undefined))
  })
})
