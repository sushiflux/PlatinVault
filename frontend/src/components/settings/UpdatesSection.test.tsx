import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { InstallUpdateResponse, SettingsResponseDto, UpdateSettingsRequest, UpdateStatusDto } from '../../api/types'
import { ConfirmProvider } from '../ui/ConfirmDialog'
import { UpdatesSection } from './UpdatesSection'

const fetchUpdateStatus = vi.hoisted(() => vi.fn<() => Promise<UpdateStatusDto>>())
const checkForUpdates = vi.hoisted(() => vi.fn<() => Promise<UpdateStatusDto>>())
const installUpdate = vi.hoisted(() => vi.fn<(tag: string) => Promise<InstallUpdateResponse>>())
const updateSettings = vi.hoisted(() => vi.fn<(payload: UpdateSettingsRequest) => Promise<SettingsResponseDto>>())
vi.mock('../../api/client', () => ({ fetchUpdateStatus, checkForUpdates, installUpdate, updateSettings }))

beforeEach(() => {
  fetchUpdateStatus.mockReset()
  checkForUpdates.mockReset()
  installUpdate.mockReset()
  updateSettings.mockReset()
})
afterEach(cleanup)

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
    checkedAt: '2026-11-02T10:00:00Z',
    error: null,
    throttled: false,
    nextManualCheckAt: null,
    canInstall: false,
    dockerAdvice: 'none',
    imageRepository: 'ghcr.io/sushiflux/platinvault',
    checkInterval: 'daily',
    ...overrides,
  }
}

const beta = {
  tag: 'v2026.11.12.13.557-beta',
  display: 'v2026.11.12 (13.557 β)',
  isBeta: true,
  notesUrl: 'https://github.com/sushiflux/PlatinVault/releases/tag/v2026.11.12.13.557-beta',
  notes: '### Fixed\n- Something fixed.',
  publishedAt: null,
  hasInstaller: true,
}

function renderSection() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <UpdatesSection />
      </ConfirmProvider>
    </QueryClientProvider>
  )
}

describe('UpdatesSection', () => {
  it('shows the version, the switch off by default and the container how-to with both tags', async () => {
    fetchUpdateStatus.mockResolvedValue(status())
    renderSection()
    expect(await screen.findByText('v2026.11.10 (31.2840)')).toBeTruthy()
    const toggle = screen.getByRole('switch', { name: 'Beta-Versionen erhalten' })
    expect(toggle.getAttribute('aria-checked')).toBe('false')
    expect(screen.getByRole('status').textContent).toContain('kein neueres Release')
    expect(screen.getAllByText('ghcr.io/sushiflux/platinvault:latest').length).toBeGreaterThan(0)
    expect(screen.getAllByText('ghcr.io/sushiflux/platinvault:beta').length).toBeGreaterThan(0)
    expect(screen.getByText('Container aktualisieren')).toBeTruthy()
  })

  it('saves the switch at once and reads the status again', async () => {
    fetchUpdateStatus.mockResolvedValueOnce(status())
    fetchUpdateStatus.mockResolvedValue(status({ betaUpdatesEnabled: true, state: 'available', available: beta, dockerAdvice: 'useBeta' }))
    updateSettings.mockResolvedValue({} as SettingsResponseDto)
    renderSection()
    const toggle = await screen.findByRole('switch', { name: 'Beta-Versionen erhalten' })
    await waitFor(() => expect((toggle as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(toggle)
    await waitFor(() => expect(updateSettings).toHaveBeenCalledWith({ betaUpdatesEnabled: true }))
    expect(await screen.findByText('Neue Beta v2026.11.12 (13.557 β) verfügbar.')).toBeTruthy()
    // A release runs while betas are wanted: the container most likely still uses :latest
    expect(screen.getByRole('note').textContent).toContain('ghcr.io/sushiflux/platinvault:beta')
    // The notes open in an own dialog (not a link to GitHub); its footer keeps the GitHub link
    expect(screen.queryByRole('link', { name: 'Release-Notizen ansehen' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Release-Notizen ansehen' }))
    expect(await screen.findByText('Something fixed.')).toBeTruthy()
    expect(screen.getByRole('link', { name: /auf GitHub ansehen/ }).getAttribute('href')).toBe(beta.notesUrl)
  })

  it('without a notes text there is no dialog button, but a plain link to the release page', async () => {
    fetchUpdateStatus.mockResolvedValue(status({ state: 'available', available: { ...beta, notes: null } }))
    renderSection()
    const link = await screen.findByRole('link', { name: 'Release-Notizen auf GitHub ansehen' })
    expect(link.getAttribute('href')).toBe(beta.notesUrl)
    expect(link.getAttribute('target')).toBe('_blank')
    expect(link.getAttribute('rel')).toBe('noopener noreferrer')
    expect(screen.queryByRole('button', { name: 'Release-Notizen ansehen' })).toBeNull()
  })

  it('without notes and without a release page there is nothing to open', async () => {
    fetchUpdateStatus.mockResolvedValue(status({ state: 'available', available: { ...beta, notes: '', notesUrl: null } }))
    renderSection()
    await screen.findByText('Neue Beta v2026.11.12 (13.557 β) verfügbar.')
    expect(screen.queryByRole('link', { name: /Release-Notizen/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /Release-Notizen/ })).toBeNull()
  })

  it('shows a failed save of the switch in full and keeps the old position', async () => {
    fetchUpdateStatus.mockResolvedValue(status())
    updateSettings.mockRejectedValue(new Error('Server nicht erreichbar: Verbindung abgelehnt (127.0.0.1:5231).'))
    renderSection()
    const toggle = await screen.findByRole('switch', { name: 'Beta-Versionen erhalten' })
    await waitFor(() => expect((toggle as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(toggle)
    expect((await screen.findByRole('alert')).textContent).toBe(
      'Die Einstellung „Beta-Versionen erhalten“ wurde nicht gespeichert: Server nicht erreichbar: Verbindung abgelehnt (127.0.0.1:5231).'
    )
    await waitFor(() => expect(toggle.getAttribute('aria-checked')).toBe('false'))
  })

  it('shows a check error completely', async () => {
    fetchUpdateStatus.mockResolvedValue(status())
    const long = 'GitHub lässt ohne Anmeldung nur 60 Abfragen pro Stunde je Internetanschluss zu, und dieses Kontingent ist gerade aufgebraucht (auch andere Programme in deinem Netz zählen mit). Die nächste Prüfung ist in etwa 17 Minuten möglich (ab 12:17 UTC).'
    checkForUpdates.mockResolvedValue(status({ state: 'error', error: long }))
    renderSection()
    const button = (await screen.findByRole('button', { name: /Jetzt nach Updates suchen/ })) as HTMLButtonElement
    await waitFor(() => expect(button.disabled).toBe(false))
    fireEvent.click(button)
    await waitFor(() => expect(screen.getByRole('status').textContent).toBe(`Update-Prüfung fehlgeschlagen: ${long}`))
  })

  it('tells a beta container to stay until a release is at least as new, then to switch to :latest', async () => {
    fetchUpdateStatus.mockResolvedValue(status({ currentChannel: 'beta', currentVersion: 'v2026.11.12 (13.557 β)', dockerAdvice: 'betaStays' }))
    renderSection()
    expect((await screen.findByRole('note')).textContent).toContain('Sie bleibt, bis ein Release erscheint')
  })

  it('installs in the Windows program only after the own confirmation dialog', async () => {
    fetchUpdateStatus.mockResolvedValue(status({ host: 'desktop', state: 'available', available: beta, betaUpdatesEnabled: true, canInstall: true }))
    installUpdate.mockResolvedValue({ started: true, tag: beta.tag })
    renderSection()
    fireEvent.click(await screen.findByRole('button', { name: 'Update installieren' }))
    const dialog = await screen.findByRole('alertdialog')
    expect(dialog.textContent).toContain('v2026.11.12 (13.557 β)')
    expect(installUpdate).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Installieren' }))
    await waitFor(() => expect(installUpdate).toHaveBeenCalledWith(beta.tag))
    expect(await screen.findByText(/Der Installer läuft/)).toBeTruthy()
    // The Windows program installs itself: no image how-to
    expect(screen.queryByText('Container aktualisieren')).toBeNull()
  })

  it('shows a calm note on a dev build and locks the switch and the check (no container how-to, no offer)', async () => {
    fetchUpdateStatus.mockResolvedValue(
      status({ currentChannel: 'dev', currentVersion: 'v2026.11.12 (13.557-dev)', currentTag: 'v2026.11.12.13.557-dev', state: 'devBuild', checkedAt: null })
    )
    renderSection()
    expect(await screen.findByText('v2026.11.12 (13.557-dev)')).toBeTruthy()
    const note = await screen.findByText(/Neue Dev-Builds kommen über den Update-Knopf von Unraid \(Tag :dev\)/)
    expect(note.textContent).toContain('Installer aus dem Entwurf im Quell-Repo')
    expect(screen.getByRole('switch', { name: 'Beta-Versionen erhalten' }).hasAttribute('disabled')).toBe(true)
    expect((screen.getByRole('button', { name: /Jetzt nach Updates suchen/ }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.queryByText('Container aktualisieren')).toBeNull()
    expect(screen.getByText('Dev-Build', { selector: 'span' })).toBeTruthy()
  })

  it('offers the six check intervals (daily by default) and saves a choice at once; a failure is shown completely', async () => {
    fetchUpdateStatus.mockResolvedValue(status())
    updateSettings.mockResolvedValueOnce({} as SettingsResponseDto)
    renderSection()
    const select = (await screen.findByLabelText('Nach Updates suchen')) as HTMLSelectElement
    expect(select.value).toBe('daily')
    expect([...select.options].map((o) => o.textContent)).toEqual(['Bei jedem Start', 'Alle 4 Stunden', 'Täglich', 'Wöchentlich', 'Monatlich', 'Nie (nur von Hand)'])
    fireEvent.change(select, { target: { value: 'weekly' } })
    await waitFor(() => expect(updateSettings).toHaveBeenCalledWith({ updateCheckInterval: 'weekly' }))

    updateSettings.mockRejectedValueOnce(new Error('Update-Intervall "hourly" ist unbekannt – erlaubt sind "startup", "4h", "daily".'))
    fireEvent.change(select, { target: { value: 'never' } })
    expect((await screen.findByRole('alert')).textContent).toContain('erlaubt sind "startup", "4h", "daily".')
  })
})
