import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import i18n from '../../i18n'
import { rememberAppVersion } from '../../utils/errorReport'
import { CopyErrorButton } from './CopyErrorButton'
import { ErrorDetails } from './ErrorDetails'
import { FloatingNotice, FloatingNoticeStack } from './FloatingNotice'

function stubClipboard(writeText: (text: string) => Promise<void>) {
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
}

const firstCopied = (writeText: ReturnType<typeof vi.fn>) => (writeText.mock.calls[0] as unknown as [string])[0]

describe('CopyErrorButton', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('de')
    rememberAppVersion('v2026.11.14 (13.557)')
  })

  afterEach(() => {
    cleanup()
    rememberAppVersion(null)
    Reflect.deleteProperty(navigator, 'clipboard')
  })

  it('copies the complete message with details, code, version and time in one click and says it was copied', async () => {
    const writeText = vi.fn(async () => {})
    stubClipboard(writeText)
    render(<CopyErrorButton report={{ title: 'Titel', message: 'Der Server ist nicht erreichbar.', details: 'ECONNREFUSED 127.0.0.1:5231', code: 'HTTP 502' }} />)

    fireEvent.click(screen.getByRole('button', { name: 'Fehlermeldung kopieren' }))

    await waitFor(() => expect(writeText).toHaveBeenCalledTimes(1))
    const copied = firstCopied(writeText)
    expect(copied).toContain('Titel\nDer Server ist nicht erreichbar.')
    expect(copied).toContain('Technische Details:\nECONNREFUSED 127.0.0.1:5231')
    expect(copied).toContain('Fehlercode: HTTP 502')
    expect(copied).toMatch(/PlatinVault v2026\.11\.14 \(13\.557\) · \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [+-]\d{2}:\d{2}$/)
    expect(await screen.findByText('Die Fehlermeldung wurde in die Zwischenablage kopiert.')).toBeTruthy()
  })

  it('reads a report function at the moment of the click', async () => {
    const writeText = vi.fn(async () => {})
    stubClipboard(writeText)
    let current = 'erste Fassung'
    render(<CopyErrorButton report={() => ({ message: current })} />)
    current = 'zweite Fassung'
    fireEvent.click(screen.getByTestId('copy-error'))
    await waitFor(() => expect(writeText).toHaveBeenCalled())
    expect(firstCopied(writeText)).toContain('zweite Fassung')
  })

  it('says so when the browser refuses to copy', async () => {
    stubClipboard(vi.fn(async () => Promise.reject(new Error('blocked'))))
    document.execCommand = vi.fn(() => false)
    render(<CopyErrorButton report={{ message: 'x' }} />)
    fireEvent.click(screen.getByTestId('copy-error'))
    expect(await screen.findByText(/konnte nicht in die Zwischenablage kopiert werden/)).toBeTruthy()
  })

  it('has the labels of all three states in the same place, so the button never changes size', () => {
    render(<CopyErrorButton report={{ message: 'x' }} />)
    const button = screen.getByTestId('copy-error')
    // Copy / Copied / Copy not possible lie on top of each other in one grid cell; only one of them is visible
    const slots = Array.from(button.querySelectorAll('span.col-start-1'))
    expect(slots).toHaveLength(3)
    expect(slots.filter((slot) => !slot.classList.contains('invisible'))).toHaveLength(1)
    expect(button.textContent).toContain('Kopieren')
    expect(button.textContent).toContain('Kopiert')
    expect(button.textContent).toContain('Kopieren nicht möglich')
  })

  it('keeps its room but cannot be reached while there is no error (inactive)', () => {
    render(<CopyErrorButton inactive report={{ message: 'x' }} />)
    const button = screen.getByTestId('copy-error') as HTMLButtonElement
    expect(button.disabled).toBe(true)
    expect(button.tabIndex).toBe(-1)
    expect(button.closest('[data-copy-error]')?.className).toContain('invisible')
  })

  it('speaks English in the English interface', async () => {
    await i18n.changeLanguage('en')
    render(<CopyErrorButton report={{ message: 'x' }} />)
    expect(screen.getByRole('button', { name: 'Copy error message' })).toBeTruthy()
    await i18n.changeLanguage('de')
  })
})

describe('where the copy button appears', () => {
  beforeEach(async () => {
    await i18n.changeLanguage('de')
  })

  afterEach(() => {
    cleanup()
    Reflect.deleteProperty(navigator, 'clipboard')
  })

  it('an error notice has it and copies title, message and details; an info notice has none', async () => {
    const writeText = vi.fn(async () => {})
    stubClipboard(writeText)
    render(
      <FloatingNoticeStack>
        <FloatingNotice key="e" tone="error" title="Guide konnte nicht vervollständigt werden" message="Google lehnt den Gemini-Key ab." details="Google: API key not valid" onClose={() => {}} />
        <FloatingNotice key="i" tone="info" title="Fertig" message="Alles gespeichert." onClose={() => {}} />
      </FloatingNoticeStack>,
    )
    expect(screen.getAllByTestId('copy-error')).toHaveLength(1)
    fireEvent.click(screen.getByTestId('copy-error'))
    await waitFor(() => expect(writeText).toHaveBeenCalled())
    const copied = firstCopied(writeText)
    expect(copied).toContain('Guide konnte nicht vervollständigt werden\nGoogle lehnt den Gemini-Key ab.')
    expect(copied).toContain('Google: API key not valid')
  })

  it('a notice whose message is markup copies the text the page shows', async () => {
    const writeText = vi.fn(async () => {})
    stubClipboard(writeText)
    render(
      <FloatingNoticeStack>
        <FloatingNotice key="e" tone="error" title="Titel" message={<span>Teil eins, <b>Teil zwei</b></span>} onClose={() => {}} />
      </FloatingNoticeStack>,
    )
    fireEvent.click(screen.getByTestId('copy-error'))
    await waitFor(() => expect(writeText).toHaveBeenCalled())
    expect(firstCopied(writeText)).toContain('Titel\nTeil eins, Teil zwei')
  })

  it('"Technische Details" and the copy button sit in one row under an error; nothing renders without a message and details', async () => {
    const writeText = vi.fn(async () => {})
    stubClipboard(writeText)
    const { container, rerender } = render(<ErrorDetails message="Kapitel fehlgeschlagen" details="Anbieter: 429" />)
    expect(screen.getByRole('button', { name: /Technische Details/ })).toBeTruthy()
    fireEvent.click(screen.getByTestId('copy-error'))
    await waitFor(() => expect(writeText).toHaveBeenCalled())
    const copied = firstCopied(writeText)
    expect(copied).toContain('Kapitel fehlgeschlagen')
    expect(copied).toContain('Anbieter: 429')

    rerender(<ErrorDetails />)
    expect(container.textContent).toBe('')
    // A dialog can keep the room of the row
    rerender(<ErrorDetails reserve />)
    expect(container.querySelector('[aria-hidden="true"]')).toBeTruthy()
    expect(screen.queryByTestId('copy-error')).toBeNull()
  })
})
