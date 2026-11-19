import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { applyLanguage } from '../../i18n'
import { PlatformPicker } from './PlatformPicker'

// The slot under the platform grid: the PS5 | PS4 bar and the Nintendo/Steam notes share one cell, so what is not needed is there as room only.

beforeEach(() => applyLanguage('de'))
afterEach(cleanup)

const noop = () => {}

describe('PlatformPicker slot', () => {
  it('keeps the PS5 | PS4 bar out of reach (invisible, inert, hidden from screen readers) unless PlayStation is chosen', () => {
    const { container, rerender } = render(<PlatformPicker platform="xbox" variant="" onChange={noop} />)
    const bar = container.querySelector<HTMLElement>('.pv-pf-sub')!
    expect(bar.hasAttribute('data-off')).toBe(true)
    expect(bar.getAttribute('aria-hidden')).toBe('true')
    expect(bar.hasAttribute('inert')).toBe(true)
    expect(screen.queryByRole('radio', { name: 'PS5' })).toBeNull()

    rerender(<PlatformPicker platform="playstation" variant="" onChange={noop} />)
    const shown = container.querySelector<HTMLElement>('.pv-pf-sub')!
    expect(shown.hasAttribute('data-off')).toBe(false)
    expect(shown.hasAttribute('inert')).toBe(false)
    expect(screen.getByRole('radio', { name: 'PS5' })).toBeTruthy()
  })

  it('takes the room of the notes from text copies that are no DOM text, and shows the real note only for its platform', () => {
    const { container, rerender } = render(<PlatformPicker platform="xbox" variant="" onChange={noop} steamNote={<span>Steam-Hinweis</span>} />)
    expect(container.querySelectorAll('.pv-pf-ghost')).toHaveLength(2)
    expect(container.querySelector('.pv-pf-ghost')!.textContent).toBe('')
    expect(screen.queryByRole('status')).toBeNull()

    rerender(<PlatformPicker platform="nintendo" variant="" onChange={noop} steamNote={<span>Steam-Hinweis</span>} />)
    expect(screen.getByRole('status').textContent).toMatch(/Nintendo Switch/)

    rerender(<PlatformPicker platform="steam" variant="" onChange={noop} steamNote={<span>Steam-Hinweis</span>} />)
    expect(screen.getByRole('status').textContent).toBe('Steam-Hinweis')
  })
})
