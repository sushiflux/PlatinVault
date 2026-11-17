import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GameDetailDto } from '../../../api/types'
import type { ManualLoreCheckDto, ManualLorePromptDto } from '../../../api/client'
import { ConfirmProvider } from '../../ui/ConfirmDialog'
import { ManualLoreDialog } from './ManualLoreDialog'

const client = vi.hoisted(() => ({
  manualLorePrompt: vi.fn(),
  checkManualLore: vi.fn(),
  saveManualLore: vi.fn(),
}))
vi.mock('../../../api/client', () => client)

const prompt = (): ManualLorePromptDto => ({
  kind: 'lore',
  prompt: 'Du schreibst Story & Lore für „Suncraft“. Antworte mit JSON.',
  fileName: 'platinvault-suncraft-story-lore.md',
  language: 'de',
  steamAppId: 4078110,
  title: 'Suncraft',
})

const okCheck = (missingParts: string[] = []): ManualLoreCheckDto => ({
  ok: true,
  errors: [],
  warnings: [],
  preview: {
    parts: [
      { title: 'Die Story', sectionCount: 1 },
      { title: 'Charaktere', sectionCount: 4 },
    ],
    missingParts,
  },
})

const settle = () => act(async () => { await vi.advanceTimersByTimeAsync(600) })

function renderDialog(over: Partial<React.ComponentProps<typeof ManualLoreDialog>> = {}) {
  const props = { slug: 'suncraft', gameTitle: 'Suncraft', replacing: false, onClose: vi.fn(), onSaved: vi.fn(), ...over }
  render(
    <QueryClientProvider client={new QueryClient()}>
      <ConfirmProvider>
        <ManualLoreDialog {...props} />
      </ConfirmProvider>
    </QueryClientProvider>
  )
  return props
}

const paste = async (text: string) => {
  fireEvent.change(screen.getByRole('textbox'), { target: { value: text } })
  await settle()
}

beforeEach(() => {
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

describe('ManualLoreDialog', () => {
  it('says calmly that it is the own subscription and makes the prompt on the first click, once', async () => {
    client.manualLorePrompt.mockResolvedValue(prompt())
    renderDialog()

    expect(screen.getByRole('dialog', { name: /Story & Lore mit deinem KI-Abo/ })).toBeTruthy()
    expect(screen.getByTestId('manual-intro').textContent).toContain('schickt nichts an eine KI')
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Prompt kopieren' })) })
    expect(client.manualLorePrompt).toHaveBeenCalledWith('suncraft')
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(prompt().prompt)
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Prompt kopieren' })) })
    expect(client.manualLorePrompt).toHaveBeenCalledTimes(1)
  })

  it('shows every error completely and keeps saving off', async () => {
    const longError = 'Die Antwort ist abgeschnitten: Das JSON wird nicht geschlossen (es fehlen 2 schließende Klammern). Bitte den Chat, seine Antwort komplett noch einmal auszugeben – am besten als Datei, damit nichts verloren geht.'
    client.checkManualLore.mockResolvedValue({ ok: false, errors: [{ code: 'cutOff', message: longError, details: 'Expected depth to be zero' }], warnings: [], preview: null } satisfies ManualLoreCheckDto)
    renderDialog()

    await paste('{"parts": [')

    expect(client.checkManualLore).toHaveBeenCalledWith('suncraft', '{"parts": [')
    expect(screen.getByTestId('answer-result').textContent).toContain(longError)
    expect(screen.getByRole('button', { name: 'Story & Lore speichern' }).hasAttribute('disabled')).toBe(true)
  })

  it('shows the parts that were read, warns about missing parts and still allows saving', async () => {
    client.checkManualLore.mockResolvedValue(okCheck(['Entwicklungsgeschichte']))
    renderDialog()

    await paste('{"parts": []}')

    const result = screen.getByTestId('answer-result').textContent ?? ''
    expect(result).toContain('Antwort gelesen.')
    expect(result).toContain('„Die Story“ (1 Abschnitt)')
    expect(result).toContain('„Charaktere“ (4 Abschnitte)')
    expect(screen.getByTestId('lore-missing').textContent).toContain('Es fehlen noch Teile: Entwicklungsgeschichte')
    expect(screen.getByRole('button', { name: 'Story & Lore speichern' }).hasAttribute('disabled')).toBe(false)
  })

  it('offers the correction prompt of a warning', async () => {
    client.checkManualLore.mockResolvedValue({ ...okCheck(), warnings: [{ code: 'foreignLanguage', message: 'Einige Sätze sind Englisch.', details: null }], fixPrompt: 'Übersetze diese Sätze …' })
    renderDialog()

    await paste('{"parts": []}')

    expect(screen.getByTestId('fix-prompt')).toBeTruthy()
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Korrektur-Prompt kopieren' })) })
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith('Übersetze diese Sätze …')
  })

  it('saves the checked text, hands the guide on and stays calm about it', async () => {
    client.checkManualLore.mockResolvedValue(okCheck())
    const game = { slug: 'suncraft', tabs: [] } as unknown as GameDetailDto
    client.saveManualLore.mockResolvedValue({ game, tabId: 'lore-1', replaced: false })
    const props = renderDialog()

    await paste('{"parts": ["x"]}')
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Story & Lore speichern' })) })

    expect(client.saveManualLore).toHaveBeenCalledWith('suncraft', '{"parts": ["x"]}')
    expect(props.onSaved).toHaveBeenCalledWith(expect.objectContaining({ game, tabId: 'lore-1' }))
    expect(screen.getByTestId('lore-saved').textContent).toBe('Story & Lore ist gespeichert.')
    // the pasted answer is not kept any more, the dialog is closed by the player
    expect(window.sessionStorage.length).toBe(0)
    expect(props.onClose).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Fertig' }))
    expect(props.onClose).toHaveBeenCalled()
  })

  it('tells before pasting that saving replaces an existing Story & Lore, and afterwards that it did', async () => {
    client.checkManualLore.mockResolvedValue(okCheck())
    client.saveManualLore.mockResolvedValue({ game: { slug: 'suncraft', tabs: [] }, tabId: 'lore-1', replaced: true })
    renderDialog({ replacing: true })

    expect(screen.getByTestId('lore-replace-note').textContent).toContain('Speichern ersetzt den Reiter')
    await paste('{"parts": []}')
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Story & Lore speichern' })) })
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Ersetzen' })) })
    expect(screen.getByTestId('lore-saved').textContent).toContain('die bisherige Fassung wurde ersetzt')
    expect(screen.queryByTestId('lore-replace-note')).toBeNull()
  })

  it('has no replace note when there is no Story & Lore yet', () => {
    renderDialog({ replacing: false })
    expect(screen.queryByTestId('lore-replace-note')).toBeNull()
  })

  it('reports a failed save completely with copyable technical details and keeps the answer', async () => {
    client.checkManualLore.mockResolvedValue(okCheck())
    client.saveManualLore.mockRejectedValue(new Error('Das Spiel wurde inzwischen gelöscht.'))
    const props = renderDialog()

    await paste('{"parts": []}')
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Story & Lore speichern' })) })

    expect(screen.getByRole('alert').textContent).toContain('Story & Lore konnte nicht gespeichert werden: Das Spiel wurde inzwischen gelöscht.')
    expect(props.onSaved).not.toHaveBeenCalled()
    expect((screen.getByRole('textbox') as HTMLTextAreaElement).value).toBe('{"parts": []}')
    expect(screen.getByRole('button', { name: 'Story & Lore speichern' }).hasAttribute('disabled')).toBe(false)
  })

  it('gets the pasted answer back after the dialog was closed and opened again', async () => {
    client.checkManualLore.mockResolvedValue(okCheck())
    renderDialog()
    await paste('{"parts": ["kept"]}')
    cleanup()

    renderDialog()
    expect((screen.getByRole('textbox') as HTMLTextAreaElement).value).toBe('{"parts": ["kept"]}')
    await settle()
    expect(client.checkManualLore).toHaveBeenLastCalledWith('suncraft', '{"parts": ["kept"]}')
  })

  it('asks before replacing an existing tab, names the missing parts and saves nothing when the player cancels', async () => {
    client.checkManualLore.mockResolvedValue(okCheck(['Entwicklungsgeschichte', 'Insiderwissen']))
    renderDialog({ replacing: true })

    await paste('{"parts": []}')
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Story & Lore speichern' })) })

    const question = screen.getByRole('alertdialog')
    expect(question.textContent).toContain('Story & Lore ersetzen?')
    expect(question.textContent).toContain('wird vollständig durch diese Antwort ersetzt')
    expect(question.textContent).toContain('Entwicklungsgeschichte, Insiderwissen')
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Abbrechen' })) })
    expect(client.saveManualLore).not.toHaveBeenCalled()
    expect(screen.queryByTestId('lore-saved')).toBeNull()
  })

  it('does not ask when there is nothing to replace', async () => {
    client.checkManualLore.mockResolvedValue(okCheck())
    client.saveManualLore.mockResolvedValue({ game: { slug: 'suncraft', tabs: [] }, tabId: 'lore-1', replaced: false })
    renderDialog({ replacing: false })
    await paste('{"parts": []}')
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Story & Lore speichern' })) })
    expect(screen.queryByRole('alertdialog')).toBeNull()
    expect(client.saveManualLore).toHaveBeenCalled()
  })

  it('lists the single findings of a refused save when the server sent them', async () => {
    client.checkManualLore.mockResolvedValue(okCheck())
    client.saveManualLore.mockRejectedValue(Object.assign(new Error('Teil L2 fehlt. Teil L3 ist leer.'), { issues: [{ code: 'missing', message: 'Teil L2 fehlt.' }, { code: 'empty', message: 'Teil L3 ist leer.' }] }))
    renderDialog()
    await paste('{"parts": []}')
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Story & Lore speichern' })) })

    const items = Array.from(screen.getByTestId('lore-save-issues').querySelectorAll('li')).map((li) => li.textContent)
    expect(items).toEqual(['Teil L2 fehlt.', 'Teil L3 ist leer.'])
    expect(screen.getByRole('alert').textContent).toContain('konnte nicht gespeichert werden')
  })

  it('closes with Escape and keeps Tab inside', () => {
    const props = renderDialog()
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(props.onClose).toHaveBeenCalled()
  })
})
