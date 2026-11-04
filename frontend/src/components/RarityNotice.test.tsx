import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { LiveRarityDto } from '../api/types'
import { RarityNotice } from './RarityNotice'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const stale: LiveRarityDto = {
  state: 'stale',
  fetchedAt: '2026-11-03T14:05:00Z',
  message: 'Steam hat die Seltenheitswerte gerade nicht geliefert (Zeitüberschreitung). Angezeigt wird der Stand vom letzten erfolgreichen Abruf.',
  detail: 'GetGlobalAchievementPercentagesForApp: HTTP 504 after 15 s',
}

describe('RarityNotice', () => {
  it('shows nothing while the values are fine', () => {
    const { container } = render(<RarityNotice rarity={{ state: 'ok', fetchedAt: null, message: null, detail: null }} />)
    expect(container.textContent).toBe('')
  })

  it('stale: the complete message, the time of the last good state, the details behind the toggle and a copy button', () => {
    render(<RarityNotice rarity={stale} />)
    const notice = screen.getByTestId('rarity-notice')
    expect(notice.getAttribute('data-state')).toBe('stale')
    expect(screen.getByText('Seltenheitswerte sind nicht aktuell')).toBeTruthy()
    expect(screen.getByText(stale.message!)).toBeTruthy()
    expect(notice.textContent).toMatch(/Stand: \d{2}\.\d{2}\.\d{4}/)

    expect(screen.queryByText(stale.detail!)).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: /Technische Details/ }))
    expect(screen.getByText(stale.detail!)).toBeTruthy()
    expect(screen.getByTestId('copy-error')).toBeTruthy()
  })

  it('unavailable: the message without a time, copy button present', () => {
    render(<RarityNotice rarity={{ state: 'unavailable', fetchedAt: null, message: 'Steam antwortet nicht.', detail: null }} />)
    const notice = screen.getByTestId('rarity-notice')
    expect(notice.getAttribute('data-state')).toBe('unavailable')
    expect(screen.getByText('Seltenheitswerte nicht verfügbar')).toBeTruthy()
    expect(screen.getByText('Steam antwortet nicht.')).toBeTruthy()
    expect(notice.textContent).not.toContain('Stand:')
    expect(screen.queryByRole('button', { name: /Technische Details/ })).toBeNull()
    expect(screen.getByTestId('copy-error')).toBeTruthy()
  })

  it('names a reason of its own when the server sent none', () => {
    render(<RarityNotice rarity={{ state: 'unavailable', fetchedAt: null, message: null, detail: null }} />)
    expect(screen.getByText(/Seltenheitswerte konnten nicht geladen werden/)).toBeTruthy()
  })

  it('copies message, details and code in one click', async () => {
    const writeText = vi.fn(async () => {})
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } })
    render(<RarityNotice rarity={stale} />)
    fireEvent.click(screen.getByTestId('copy-error'))
    await vi.waitFor(() => expect(writeText).toHaveBeenCalled())
    const text = (writeText.mock.calls[0] as unknown as [string])[0]
    expect(text).toContain(stale.message!)
    expect(text).toContain(stale.detail!)
    expect(text).toContain('rarity: stale')
  })
})
