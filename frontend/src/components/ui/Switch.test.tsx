import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Switch } from './Switch'

afterEach(cleanup)

describe('Switch', () => {
  it('is a switch named by its label and reports its state', () => {
    render(
      <Switch checked={false} onChange={() => {}}>
        Sounds abspielen
      </Switch>
    )
    const control = screen.getByRole('switch', { name: 'Sounds abspielen' })
    expect(control.getAttribute('aria-checked')).toBe('false')
    expect(control.getAttribute('title')).toBeNull()
  })

  it('asks for the opposite value when clicked, also on the label text', () => {
    const onChange = vi.fn()
    render(
      <Switch checked onChange={onChange}>
        Sounds abspielen
      </Switch>
    )
    fireEvent.click(screen.getByText('Sounds abspielen'))
    expect(onChange).toHaveBeenCalledWith(false)
  })

  it('does nothing while disabled and pins the dark surface colours on request', () => {
    const onChange = vi.fn()
    render(
      <Switch checked={false} onChange={onChange} disabled surface="dark">
        Gesperrt
      </Switch>
    )
    const control = screen.getByRole('switch', { name: 'Gesperrt' })
    fireEvent.click(control)
    expect(onChange).not.toHaveBeenCalled()
    expect(control.getAttribute('data-surface')).toBe('dark')
  })

  it('stays focusable in the blocked mode (aria-disabled), and click, Space and Enter do nothing', () => {
    const onChange = vi.fn()
    render(
      <Switch checked={false} onChange={onChange} blocked>
        Gesperrt
      </Switch>
    )
    const control = screen.getByRole('switch', { name: 'Gesperrt' }) as HTMLButtonElement
    expect(control.getAttribute('aria-disabled')).toBe('true')
    expect(control.disabled).toBe(false)
    act(() => control.focus())
    expect(document.activeElement).toBe(control)
    fireEvent.click(control)
    fireEvent.keyDown(control, { key: ' ' })
    fireEvent.keyDown(control, { key: 'Enter' })
    expect(onChange).not.toHaveBeenCalled()
    expect(control.getAttribute('aria-checked')).toBe('false')
  })

  it('has no aria-disabled unless asked to be blocked, and the native disabled stays native', () => {
    render(
      <>
        <Switch checked={false} onChange={() => {}}>
          Frei
        </Switch>
        <Switch checked={false} onChange={() => {}} disabled>
          Nativ gesperrt
        </Switch>
      </>
    )
    expect(screen.getByRole('switch', { name: 'Frei' }).getAttribute('aria-disabled')).toBeNull()
    const native = screen.getByRole('switch', { name: 'Nativ gesperrt' }) as HTMLButtonElement
    expect(native.disabled).toBe(true)
    expect(native.getAttribute('aria-disabled')).toBeNull()
  })
})
