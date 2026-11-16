import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import i18n from '../../i18n'
import { rememberAppVersion } from '../../utils/errorReport'
import { ErrorBlock } from './ErrorBlock'

function stubClipboard(writeText: (text: string) => Promise<void>) {
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
}

describe('ErrorBlock', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('de')
    rememberAppVersion('v2026.11.16.1.1')
  })

  afterEach(() => {
    cleanup()
    rememberAppVersion(null)
    Reflect.deleteProperty(navigator, 'clipboard')
  })

  it('keeps the headline with its code visible and unfolds the full description with the arrow', () => {
    render(<ErrorBlock title="Gemini antwortet nicht" code="HTTP 429 · gemini" message="Dein Kontingent ist aufgebraucht." details="Quota exceeded" />)

    expect(screen.getByText(/Gemini antwortet nicht/)).toBeTruthy()
    expect(screen.getByText(/HTTP 429 · gemini/)).toBeTruthy()
    const toggle = screen.getByRole('button', { name: /Gemini antwortet nicht/ })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(screen.queryByText('Dein Kontingent ist aufgebraucht.')).toBeNull()

    fireEvent.click(toggle)
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    expect(screen.getByText('Dein Kontingent ist aufgebraucht.')).toBeTruthy()
    expect(screen.getByText('Quota exceeded')).toBeTruthy()
  })

  it('copies the complete text even while it is folded, and the confirmation takes no extra room', async () => {
    const writeText = vi.fn(async () => {})
    stubClipboard(writeText)
    render(<ErrorBlock title="Speichern fehlgeschlagen" code="HTTP 400" message="Titel bereits vergeben." />)

    fireEvent.click(screen.getByRole('button', { name: 'Fehlermeldung kopieren' }))
    await waitFor(() => expect(writeText).toHaveBeenCalledTimes(1))
    const copied = (writeText.mock.calls[0] as unknown as [string])[0]
    expect(copied).toContain('Speichern fehlgeschlagen')
    expect(copied).toContain('Titel bereits vergeben.')
    expect(copied).toContain('Fehlercode: HTTP 400')
  })

  it('shows a message that has no details open from the start', () => {
    render(<ErrorBlock title="Fehler" message="Nur der Titel." defaultOpen />)
    expect(screen.getByText('Nur der Titel.')).toBeTruthy()
    expect(screen.getByRole('alert')).toBeTruthy()
  })
})
