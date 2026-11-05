import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { UpdateStatusDto } from '../../api/types'
import { VersionCheck } from './VersionCheck'

const fetchUpdateStatus = vi.hoisted(() => vi.fn<() => Promise<UpdateStatusDto>>())
const checkForUpdates = vi.hoisted(() => vi.fn<() => Promise<UpdateStatusDto>>())
const fetchWhatsNew = vi.hoisted(() => vi.fn(async () => ({ version: 'v2026.11.10 (31.2840)', show: false, notes: null as string | null })))
const fetchBranding = vi.hoisted(() => vi.fn(async () => ({ productName: 'PlatinVault', tagline: '', company: 'SushiFlux', version: 'v2026.11.10 (31.2840)', host: 'server' })))
vi.mock('../../api/client', () => ({ fetchUpdateStatus, checkForUpdates, fetchBranding, fetchWhatsNew }))

beforeEach(() => {
  fetchUpdateStatus.mockReset()
  checkForUpdates.mockReset()
  fetchWhatsNew.mockClear()
})
afterEach(cleanup)

const release = {
  tag: 'v2026.11.12',
  display: 'v2026.11.12 (13.557)',
  isBeta: false,
  notesUrl: null,
  notes: null,
  publishedAt: null,
  hasInstaller: true,
}

function status(overrides: Partial<UpdateStatusDto> = {}): UpdateStatusDto {
  return {
    currentVersion: 'v2026.11.10 (31.2840)',
    currentTag: 'v2026.11.10',
    currentChannel: 'release',
    betaUpdatesEnabled: false,
    host: 'server',
    state: 'upToDate',
    available: null,
    latestRelease: null,
    latestBeta: null,
    checkedAt: null,
    error: null,
    throttled: false,
    nextManualCheckAt: null,
    canInstall: false,
    dockerAdvice: 'none',
    imageRepository: 'ghcr.io/sushiflux/platinvault',
    checkInterval: 'weekly',
    ...overrides,
  }
}

function renderCheck() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <VersionCheck>
        <span>v2026.11.10 (31.2840)</span>
      </VersionCheck>
    </QueryClientProvider>
  )
  return client
}

describe('VersionCheck', () => {
  it('opens a popover with the interval and checks by hand: up to date', async () => {
    fetchUpdateStatus.mockResolvedValue(status({ state: 'unchecked' }))
    checkForUpdates.mockResolvedValue(status({ checkedAt: '2026-11-03T10:00:00Z' }))
    renderCheck()
    fireEvent.click(screen.getByTestId('version-check'))
    const panel = await screen.findByTestId('version-panel')
    await waitFor(() => expect(panel.textContent).toContain('Wöchentlich'))
    fireEvent.click(screen.getByRole('button', { name: /Nach Updates suchen/ }))
    await waitFor(() => expect(checkForUpdates).toHaveBeenCalledTimes(1))
    expect(await screen.findByText(/Du bist auf dem neuesten Stand/)).toBeTruthy()
  })

  it('a found update is written to the shared status (the header icon appears) and pointed out', async () => {
    fetchUpdateStatus.mockResolvedValue(status())
    checkForUpdates.mockResolvedValue(status({ state: 'available', available: release }))
    const client = renderCheck()
    fireEvent.click(screen.getByTestId('version-check'))
    await screen.findByTestId('version-panel')
    await waitFor(() => expect(client.getQueryData(['update-status'])).toBeTruthy())
    fireEvent.click(screen.getByRole('button', { name: /Nach Updates suchen/ }))
    expect(await screen.findByText(/Neues Release v2026.11.12 \(13.557\) verfügbar/)).toBeTruthy()
    expect(screen.getByText(/Update-Symbol oben rechts/)).toBeTruthy()
    expect((client.getQueryData(['update-status']) as UpdateStatusDto).state).toBe('available')
  })

  it('shows a failed check completely and Esc closes the popover', async () => {
    fetchUpdateStatus.mockResolvedValue(status())
    checkForUpdates.mockResolvedValue(status({ state: 'error', error: 'GitHub ist nicht erreichbar: Zeitüberschreitung nach 20 Sekunden, bitte später erneut versuchen.' }))
    renderCheck()
    fireEvent.click(screen.getByTestId('version-check'))
    const panel = await screen.findByTestId('version-panel')
    await waitFor(() => expect(panel.textContent).toContain('Wöchentlich'))
    fireEvent.click(screen.getByRole('button', { name: /Nach Updates suchen/ }))
    expect(await screen.findByText(/Zeitüberschreitung nach 20 Sekunden, bitte später erneut versuchen\./)).toBeTruthy()
    fireEvent.keyDown(panel, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByTestId('version-panel')).toBeNull())
  })

  it('a dev build cannot check', async () => {
    fetchUpdateStatus.mockResolvedValue(status({ state: 'devBuild', currentChannel: 'dev' }))
    renderCheck()
    fireEvent.click(screen.getByTestId('version-check'))
    await screen.findByTestId('version-panel')
    await waitFor(() => expect((screen.getByRole('button', { name: /Nach Updates suchen/ }) as HTMLButtonElement).disabled).toBe(true))
  })

  it('offers the notes of the running version from the popover (disabled without notes) and opens them without marking anything', async () => {
    fetchUpdateStatus.mockResolvedValue(status())
    fetchWhatsNew.mockResolvedValue({ version: 'v2026.11.10 (31.2840)', show: false, notes: '## v2026.11.10 (31.2840)\n\n### ✨ Neu\n\n- Etwas Neues.' })
    renderCheck()
    fireEvent.click(screen.getByTestId('version-check'))
    await screen.findByTestId('version-panel')
    const button = screen.getByTestId('version-whats-new') as HTMLButtonElement
    await waitFor(() => expect(button.disabled).toBe(false))
    fireEvent.click(button)
    const dialog = await screen.findByTestId('release-notes')
    expect(dialog.textContent).toContain('Das ist neu in v2026.11.10 (31.2840)')
    expect(dialog.textContent).toContain('Etwas Neues.')
    await waitFor(() => expect(screen.queryByTestId('version-panel')).toBeNull())
  })

  it('keeps the notes button disabled when the version has no entry', async () => {
    fetchUpdateStatus.mockResolvedValue(status())
    fetchWhatsNew.mockResolvedValue({ version: 'v2026.11.10 (31.2840)', show: false, notes: null })
    renderCheck()
    fireEvent.click(screen.getByTestId('version-check'))
    await screen.findByTestId('version-panel')
    await waitFor(() => expect(fetchWhatsNew).toHaveBeenCalled())
    expect((screen.getByTestId('version-whats-new') as HTMLButtonElement).disabled).toBe(true)
  })
})
