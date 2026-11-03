import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import '../../i18n'
import { FloatingNotice, FloatingNoticeStack } from './FloatingNotice'

afterEach(cleanup)

describe('FloatingNotice', () => {
  it('names the failure in bold, shows the reason and is an alert', () => {
    render(
      <FloatingNoticeStack>
        <FloatingNotice key="e" tone="error" title="Guide konnte nicht vervollständigt werden" message="Google lehnt den Gemini-Key ab." onClose={() => {}} />
      </FloatingNoticeStack>
    )
    const alert = screen.getByRole('alert')
    expect(alert.textContent).toContain('Guide konnte nicht vervollständigt werden')
    expect(alert.textContent).toContain('Google lehnt den Gemini-Key ab.')
    // Opaque: a solid surface colour under the tone's tint
    expect(alert.style.backgroundColor).toBe('var(--bg-surface)')
  })

  it('keeps the provider text behind a toggle', () => {
    render(
      <FloatingNoticeStack>
        <FloatingNotice key="e" tone="error" title="Titel" message="Grund" details="Google: raw text" onClose={() => {}} />
      </FloatingNoticeStack>
    )
    expect(screen.queryByText('Google: raw text')).toBeNull()
    const toggle = screen.getByRole('button', { name: /Technische Details|Technical details/ })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(toggle)
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    expect(screen.getByText('Google: raw text')).toBeTruthy()
  })

  it('closes with the X button and with Esc, and runs an action', () => {
    const onClose = vi.fn()
    const onAction = vi.fn()
    render(
      <FloatingNoticeStack>
        <FloatingNotice key="e" tone="error" title="Titel" actions={[{ label: 'KI-Einstellungen', onClick: onAction }]} onClose={onClose} />
      </FloatingNoticeStack>
    )
    fireEvent.click(screen.getByRole('button', { name: 'KI-Einstellungen' }))
    expect(onAction).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getByRole('button', { name: /Schließen|Close/ }))
    expect(onClose).toHaveBeenCalledTimes(1)
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(2)
  })

  it('leaves Esc to an open dialog and has no close button while it is only progress', () => {
    const onClose = vi.fn()
    render(
      <div>
        <div role="dialog" aria-label="other" />
        <FloatingNoticeStack>
          <FloatingNotice key="e" tone="info" title="Fertig" onClose={onClose} />
          <FloatingNotice key="p" tone="progress" title="Läuft" />
        </FloatingNoticeStack>
      </div>
    )
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).not.toHaveBeenCalled()
    // progress is a status without close button: only the info notice has one
    expect(screen.getAllByRole('button', { name: /Schließen|Close/ })).toHaveLength(1)
    expect(screen.getAllByRole('status')).toHaveLength(2)
  })
  it('marks every stack so a dialog can move it away from its footer, the raised one keeps its place above the page stack', () => {
    const { container } = render(
      <div>
        <FloatingNoticeStack>
          <FloatingNotice key="a" tone="error" title="Seite" onClose={() => {}} />
        </FloatingNoticeStack>
        <FloatingNoticeStack raised>
          <FloatingNotice key="b" tone="info" title="App" onClose={() => {}} />
        </FloatingNoticeStack>
      </div>
    )
    const stacks = container.querySelectorAll('.pv-notice-stack')
    expect(stacks).toHaveLength(2)
    // Bottom edge by default (index.css moves them to the top while a .pv-modal-backdrop is open); the raised stack sits above the page's own
    expect(stacks[0]!.className).toContain('bottom-5')
    expect(stacks[1]!.className).toContain('bottom-24')
  })
})
