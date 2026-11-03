import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { applyLanguage } from '../../i18n'
import type { GameStatsDto } from '../../api/types'
import { FactsCard } from './GameFacts'

function statsWith(over: Partial<GameStatsDto> = {}): GameStatsDto {
  return {
    appId: 4078110,
    releaseDate: '10. Dez. 2024',
    comingSoon: false,
    developers: ['Studio'],
    publishers: [],
    genres: [],
    metacriticScore: null,
    achievementsTotal: 30,
    reviewScoreDesc: null,
    reviewTotal: null,
    reviewPositive: null,
    reviewPercentPositive: null,
    currentPlayers: null,
    news: [],
    latestUpdate: null,
    player: null,
    ...over,
  }
}

function stubClipboard(writeText: (text: string) => Promise<void>) {
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
}

describe('FactsCard Steam App ID', () => {
  beforeEach(async () => {
    await applyLanguage('de')
  })

  afterEach(async () => {
    cleanup()
    Reflect.deleteProperty(navigator, 'clipboard')
    await applyLanguage('de')
  })

  it('shows the App ID with a copy button and confirms the copy (German)', async () => {
    const writeText = vi.fn(async () => {})
    stubClipboard(writeText)
    render(<FactsCard stats={statsWith()} />)
    expect(screen.getByText('Steam-AppID')).toBeTruthy()
    expect(screen.getByText('4078110')).toBeTruthy()
    // The confirmation slot exists from the start (reserved room), but says nothing yet
    expect(screen.getByRole('status').textContent).toBe('')
    fireEvent.click(screen.getByRole('button', { name: 'AppID kopieren' }))
    await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Kopiert'))
    expect(writeText).toHaveBeenCalledWith('4078110')
  })

  it('speaks English when the app does', async () => {
    await applyLanguage('en')
    stubClipboard(vi.fn(async () => {}))
    render(<FactsCard stats={statsWith()} />)
    expect(screen.getByText('Steam App ID')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Copy App ID' }))
    await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Copied'))
  })

  it('says so, with the reason, when the browser refuses to copy', async () => {
    stubClipboard(vi.fn(async () => Promise.reject(new Error('blocked'))))
    document.execCommand = vi.fn(() => false)
    render(<FactsCard stats={statsWith()} />)
    fireEvent.click(screen.getByRole('button', { name: 'AppID kopieren' }))
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('AppID konnte nicht kopiert werden')
    expect(alert.textContent).toContain('Strg+C')
    expect(screen.getByRole('status').textContent).toBe('')
  })

  it('shows nothing about an App ID the game does not have, and the rest closes up', () => {
    render(<FactsCard stats={statsWith({ appId: 0 })} />)
    expect(screen.queryByText('Steam-AppID')).toBeNull()
    expect(screen.queryByRole('button', { name: 'AppID kopieren' })).toBeNull()
    expect(screen.getByText('Erschienen')).toBeTruthy()
  })
})

describe('FactsCard size', () => {
  beforeEach(async () => {
    await applyLanguage('de')
  })

  afterEach(async () => {
    cleanup()
    await applyLanguage('de')
  })

  it('shows the real size on disk of an installed game', () => {
    render(<FactsCard stats={statsWith({ sizeOnDiskBytes: 48.3 * 1024 ** 3, installSizeBytes: 50 * 1024 ** 3 })} />)
    expect(screen.getByText('Größe auf der Platte')).toBeTruthy()
    expect(screen.getByText(/48,3\sGB/)).toBeTruthy()
    expect(screen.queryByText('Installationsgröße')).toBeNull()
  })

  it('shows the estimated install size of a game that is not installed, marked as an estimate', () => {
    render(<FactsCard stats={statsWith({ sizeOnDiskBytes: null, installSizeBytes: 46.5 * 1024 ** 3 })} />)
    expect(screen.getByText('Installationsgröße')).toBeTruthy()
    expect(screen.getByText(/46,5\sGB/)).toBeTruthy()
    expect(screen.getByText(/geschätzt, nicht installiert/)).toBeTruthy()
  })

  it('shows no size row without a source (the rest closes up)', () => {
    render(<FactsCard stats={statsWith()} />)
    expect(screen.queryByText('Größe auf der Platte')).toBeNull()
    expect(screen.queryByText('Installationsgröße')).toBeNull()
  })
})
