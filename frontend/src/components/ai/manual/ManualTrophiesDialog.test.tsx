import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { applyLanguage } from '../../../i18n'
import type { ManualTrophyCheckDto, ManualTrophyPromptDto, ManualTrophyResearchResultDto } from '../../../api/types'
import { ConfirmProvider } from '../../ui/ConfirmDialog'
import { manualList, ps5Items } from '../../trophies/trophies.fixtures'
import { ManualTrophiesDialog } from './ManualTrophiesDialog'

// The trophy list with the player's own AI subscription: copy the prompt, paste the answer, see what was read, save. Nothing goes to an AI.

const client = vi.hoisted(() => ({ manualTrophiesPrompt: vi.fn(), checkManualTrophies: vi.fn(), saveManualTrophies: vi.fn() }))
vi.mock('../../../api/client', () => client)

const prompt = (): ManualTrophyPromptDto => ({
  kind: 'trophies',
  prompt: 'Du recherchierst die Trophäenliste von „Stellar Blade“ (PlayStation 5). Antworte mit JSON.',
  fileName: 'platinvault-stellar-blade-trophies.md',
  language: 'de',
  steamAppId: null,
  title: 'Stellar Blade',
})

const okCheck = (over: Partial<ManualTrophyCheckDto> = {}): ManualTrophyCheckDto => ({
  ok: true,
  errors: [],
  warnings: [],
  count: 48,
  byGrade: { platinum: 1, gold: 4, silver: 13, bronze: 30 },
  hidden: 6,
  dlc: 2,
  preview: [
    { key: 'stellar-legende', name: 'Stellar Legende', grade: 'platinum', points: null, hidden: false, group: null },
    { key: 'erstes-blut', name: 'Erstes Blut', grade: 'silver', points: null, hidden: false, group: null },
  ],
  keepsTicks: 0,
  dropsTicks: 0,
  ...over,
})

const saved = (): ManualTrophyResearchResultDto => ({ list: manualList(ps5Items()), factsUpdated: false, keptTicks: 3, retainedOldTicked: 1 })

const settle = () => act(async () => { await vi.advanceTimersByTimeAsync(600) })

function renderDialog(over: Partial<React.ComponentProps<typeof ManualTrophiesDialog>> = {}) {
  const props = { slug: 'stellar-blade', gameTitle: 'Stellar Blade', kind: 'trophies' as const, replacing: false, onClose: vi.fn(), onSaved: vi.fn(), ...over }
  render(
    <QueryClientProvider client={new QueryClient()}>
      <ConfirmProvider>
        <ManualTrophiesDialog {...props} />
      </ConfirmProvider>
    </QueryClientProvider>
  )
  return props
}

const paste = async (text: string) => {
  fireEvent.change(screen.getByRole('textbox'), { target: { value: text } })
  await settle()
}

beforeEach(async () => {
  await applyLanguage('de')
  vi.useFakeTimers()
  window.sessionStorage.clear()
  Object.values(client).forEach((fn) => fn.mockReset())
  Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true })
  Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn(async () => undefined) }, configurable: true })
})
afterEach(() => {
  cleanup()
  vi.useRealTimers()
})

describe('ManualTrophiesDialog', () => {
  it('says calmly that it is the own subscription and makes the prompt on the first click, once, with what the page asked for', async () => {
    client.manualTrophiesPrompt.mockResolvedValue(prompt())
    renderDialog({ include: { trophies: true, facts: true } })

    expect(screen.getByRole('dialog', { name: /Trophäenliste mit deinem KI-Abo/ })).toBeTruthy()
    expect(screen.getByTestId('manual-intro').textContent).toContain('schickt nichts an eine KI')
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Prompt kopieren' })) })
    expect(client.manualTrophiesPrompt).toHaveBeenCalledWith('stellar-blade', { trophies: true, facts: true })
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(prompt().prompt)
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Prompt kopieren' })) })
    expect(client.manualTrophiesPrompt).toHaveBeenCalledTimes(1)
  })

  it('names achievements for a platform that has no trophies, and the facts for a Switch game', () => {
    renderDialog({ kind: 'achievements' })
    expect(screen.getByRole('dialog', { name: /Erfolgsliste mit deinem KI-Abo/ })).toBeTruthy()
    cleanup()
    renderDialog({ kind: 'achievements', include: { trophies: false, facts: true } })
    expect(screen.getByRole('dialog', { name: /Spiel-Fakten mit deinem KI-Abo/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Spiel-Fakten speichern' })).toBeTruthy()
  })

  it('shows what was read per grade, how many are hidden or DLC and the first trophies, and keeps saving on', async () => {
    client.checkManualTrophies.mockResolvedValue(okCheck({ keepsTicks: 5, dropsTicks: 2 }))
    renderDialog()
    await paste('{"trophies": []}')

    expect(client.checkManualTrophies).toHaveBeenCalledWith('stellar-blade', '{"trophies": []}')
    const preview = screen.getByTestId('trophies-preview')
    expect(preview.textContent).toContain('48 Trophäen gelesen.')
    expect(within(screen.getByTestId('trophies-grades')).getByText(/Platin 1/)).toBeTruthy()
    expect(within(screen.getByTestId('trophies-grades')).getByText(/Bronze 30/)).toBeTruthy()
    expect(preview.textContent).toContain('Davon 6 versteckt und 2 aus DLC.')
    expect(screen.getByTestId('trophies-ticks').textContent).toContain('5 deiner Haken bleiben erhalten.')
    expect(screen.getByTestId('trophies-ticks').textContent).toContain('2 nicht abgehakte Einträge der bisherigen Liste entfallen.')
    expect(within(screen.getByTestId('trophies-names')).getByText(/Stellar Legende/)).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Trophäenliste speichern' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('shows every error completely and keeps saving off', async () => {
    const longError = 'Die Antwort enthält mehrere Platin-Trophäen: „Stellar Legende“, „Zweite Legende“. Eine PlayStation-Liste hat höchstens eine. Bitte den Chat, die Liste zu korrigieren und komplett noch einmal auszugeben.'
    client.checkManualTrophies.mockResolvedValue({ ...okCheck(), ok: false, errors: [{ code: 'severalPlatinum', message: longError, details: null }] })
    renderDialog()
    await paste('{}')

    expect(screen.getByText(longError)).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Trophäenliste speichern' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('saves the answer, says how many ticks were kept and hands the list to the page', async () => {
    client.checkManualTrophies.mockResolvedValue(okCheck())
    client.saveManualTrophies.mockResolvedValue(saved())
    const props = renderDialog()
    await paste('{"trophies": []}')

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Trophäenliste speichern' })) })

    expect(client.saveManualTrophies).toHaveBeenCalledWith('stellar-blade', '{"trophies": []}')
    expect(props.onSaved).toHaveBeenCalledWith(saved())
    const message = screen.getByTestId('trophies-saved').textContent ?? ''
    expect(message).toContain('Die Trophäenliste ist gespeichert: 4 Trophäen.')
    expect(message).toContain('3 Haken wurden übernommen.')
    expect(message).toContain('1 bereits abgehakter Eintrag fehlt in der neuen Liste und bleibt erhalten.')
    expect(window.sessionStorage.length).toBe(0)
  })

  it('asks before an existing list is replaced, names what happens to the ticks, and saves nothing on "Abbrechen"', async () => {
    client.checkManualTrophies.mockResolvedValue(okCheck({ keepsTicks: 7, dropsTicks: 1 }))
    renderDialog({ replacing: true })
    expect(screen.getByTestId('trophies-replace-note')).toBeTruthy()
    await paste('{}')

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Trophäenliste speichern' })) })
    const ask = screen.getByRole('alertdialog')
    expect(ask.textContent).toContain('„Stellar Blade“')
    expect(ask.textContent).toContain('7 Haken bleiben erhalten.')
    expect(ask.textContent).toContain('Ein nicht abgehakter Eintrag der alten Liste entfällt.')

    await act(async () => { fireEvent.click(within(ask).getByRole('button', { name: 'Abbrechen' })) })
    expect(client.saveManualTrophies).not.toHaveBeenCalled()

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Trophäenliste speichern' })) })
    client.saveManualTrophies.mockResolvedValue(saved())
    await act(async () => { fireEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Ersetzen' })) })
    expect(client.saveManualTrophies).toHaveBeenCalledTimes(1)
  })

  it('lists every finding of a refused save, completely', async () => {
    client.checkManualTrophies.mockResolvedValue(okCheck())
    client.saveManualTrophies.mockRejectedValue(Object.assign(new Error('Nichts gespeichert.'), { issues: [{ message: 'Es gibt keine verlässliche Liste.' }, { message: 'Die Antwort ist abgeschnitten.' }] }))
    renderDialog()
    await paste('{}')

    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Trophäenliste speichern' })) })

    const issues = screen.getByTestId('trophies-save-issues')
    expect(within(issues).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['Es gibt keine verlässliche Liste.', 'Die Antwort ist abgeschnitten.'])
    expect(screen.getByRole('alert').textContent).toContain('Die Liste konnte nicht gespeichert werden:')
  })

  it('closes on Escape and keeps the pasted answer for the next time', async () => {
    client.checkManualTrophies.mockResolvedValue(okCheck())
    const props = renderDialog()
    await paste('{"trophies": [1]}')
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(props.onClose).toHaveBeenCalled()
    cleanup()

    renderDialog()
    expect((screen.getByRole('textbox') as HTMLTextAreaElement).value).toBe('{"trophies": [1]}')
  })
})
