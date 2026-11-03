import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { InstallUpdateResponse, UpdateStatusDto } from '../../api/types'
import { ConfirmProvider } from '../ui/ConfirmDialog'
import { UpdateButton } from './UpdateButton'
import { updateOffer } from './updateOffer'

const fetchUpdateStatus = vi.hoisted(() => vi.fn<() => Promise<UpdateStatusDto>>())
const installUpdate = vi.hoisted(() => vi.fn<(tag: string) => Promise<InstallUpdateResponse>>())
vi.mock('../../api/client', () => ({ fetchUpdateStatus, installUpdate }))

beforeEach(() => {
  fetchUpdateStatus.mockReset()
  installUpdate.mockReset()
})
afterEach(cleanup)

const release = {
  tag: 'v2026.11.12',
  display: 'v2026.11.12 (13.557)',
  isBeta: false,
  notesUrl: 'https://github.com/sushiflux/PlatinVault/releases/tag/v2026.11.12',
  notes: '### New\n- A thing **happened**.',
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
    state: 'available',
    available: release,
    latestRelease: release,
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

function renderButton(onOpenSettings = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <UpdateButton onOpenSettings={onOpenSettings} />
      </ConfirmProvider>
    </QueryClientProvider>
  )
  return onOpenSettings
}

describe('updateOffer (visibility rule)', () => {
  it('offers only a successful check that found something', () => {
    expect(updateOffer(status())).toEqual(release)
    expect(updateOffer(undefined)).toBeNull()
    for (const state of ['upToDate', 'devBuild', 'error', 'unchecked', 'disabled'] as const) {
      expect(updateOffer(status({ state, available: null }))).toBeNull()
    }
    // Even a stray "available" payload does not count in a dev build or after an error
    expect(updateOffer(status({ state: 'devBuild' }))).toBeNull()
    expect(updateOffer(status({ state: 'error', error: 'x' }))).toBeNull()
  })
})

describe('UpdateButton', () => {
  it('the notes button closes the popover and opens the notes dialog; closing returns the focus to the icon', async () => {
    fetchUpdateStatus.mockResolvedValue(status())
    renderButton()
    const icon = await screen.findByTestId('update-button')
    fireEvent.click(icon)
    fireEvent.click(await screen.findByTestId('update-notes'))
    await waitFor(() => expect(screen.queryByTestId('update-panel')).toBeNull())
    const dialog = await screen.findByTestId('release-notes')
    expect(await screen.findByText('happened')).toBeTruthy()
    expect(dialog.textContent).toContain('v2026.11.12 (13.557)')
    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByTestId('release-notes')).toBeNull())
    await waitFor(() => expect(document.activeElement).toBe(icon))
  })

  it('without a notes text the popover offers a plain external link instead of the dialog; without a page nothing', async () => {
    fetchUpdateStatus.mockResolvedValue(status({ available: { ...release, notes: null } }))
    renderButton()
    fireEvent.click(await screen.findByTestId('update-button'))
    const link = await screen.findByTestId('update-notes-link')
    expect(link.getAttribute('href')).toBe(release.notesUrl)
    expect(link.getAttribute('target')).toBe('_blank')
    expect(link.getAttribute('rel')).toBe('noopener noreferrer')
    expect(link.textContent).toBe('Release-Notizen auf GitHub ansehen')
    expect(screen.queryByTestId('update-notes')).toBeNull()
  })

  it('no notes and no page: no notes entry at all', async () => {
    fetchUpdateStatus.mockResolvedValue(status({ available: { ...release, notes: null, notesUrl: null } }))
    renderButton()
    fireEvent.click(await screen.findByTestId('update-button'))
    await screen.findByTestId('update-panel')
    expect(screen.queryByTestId('update-notes')).toBeNull()
    expect(screen.queryByTestId('update-notes-link')).toBeNull()
  })

  it.each(['upToDate', 'devBuild', 'error', 'unchecked'] as const)('shows nothing for state %s', async (state) => {
    fetchUpdateStatus.mockResolvedValue(status({ state, available: null, error: state === 'error' ? 'GitHub down' : null }))
    renderButton()
    await waitFor(() => expect(fetchUpdateStatus).toHaveBeenCalled())
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(screen.queryByTestId('update-button')).toBeNull()
  })

  it('shows the icon with the version when an update is available', async () => {
    fetchUpdateStatus.mockResolvedValue(status())
    renderButton()
    const button = await screen.findByTestId('update-button')
    expect(button.getAttribute('aria-label')).toContain('v2026.11.12 (13.557)')
  })

  it('container host: popover with notes link and the steps, no install button; Esc closes', async () => {
    fetchUpdateStatus.mockResolvedValue(status())
    renderButton()
    fireEvent.click(await screen.findByTestId('update-button'))
    const panel = await screen.findByTestId('update-panel')
    expect(panel.textContent).toContain('v2026.11.12 (13.557)')
    expect(screen.getByRole('button', { name: /Release-Notizen/ })).toBeTruthy()
    expect(screen.queryByRole('link', { name: /Release-Notizen/ })).toBeNull()
    expect(screen.getByTestId('update-container-steps').textContent).toContain('docker compose pull')
    expect(screen.queryByRole('button', { name: /Update installieren/ })).toBeNull()
    fireEvent.keyDown(panel, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByTestId('update-panel')).toBeNull())
  })

  it('container host with a wanted beta on a release image: explains the tag switch', async () => {
    const beta = { ...release, tag: 'v2026.11.12.13.557-beta', display: 'v2026.11.12 (13.557 β)', isBeta: true }
    fetchUpdateStatus.mockResolvedValue(status({ available: beta, dockerAdvice: 'useBeta', betaUpdatesEnabled: true }))
    renderButton()
    fireEvent.click(await screen.findByTestId('update-button'))
    expect((await screen.findByTestId('update-container-steps')).textContent).toContain('ghcr.io/sushiflux/platinvault:beta')
  })

  it('Windows program: install button asks first, then installs the offered tag; errors are shown completely', async () => {
    fetchUpdateStatus.mockResolvedValue(status({ host: 'desktop', canInstall: true }))
    installUpdate.mockRejectedValue(new Error('Der Download ist fehlgeschlagen: Prüfsumme stimmt nicht überein, bitte später erneut versuchen.'))
    renderButton()
    fireEvent.click(await screen.findByTestId('update-button'))
    expect(screen.queryByTestId('update-container-steps')).toBeNull()
    fireEvent.click(await screen.findByRole('button', { name: /Update installieren/ }))
    expect(installUpdate).not.toHaveBeenCalled()
    fireEvent.click(await screen.findByRole('button', { name: 'Installieren' }))
    await waitFor(() => expect(installUpdate).toHaveBeenCalledWith('v2026.11.12'))
    expect((await screen.findByRole('alert')).textContent).toContain('Prüfsumme stimmt nicht überein, bitte später erneut versuchen.')
  })

  it('"Alle Update-Einstellungen" opens the settings', async () => {
    fetchUpdateStatus.mockResolvedValue(status())
    const open = renderButton()
    fireEvent.click(await screen.findByTestId('update-button'))
    fireEvent.click(await screen.findByRole('button', { name: 'Alle Update-Einstellungen' }))
    expect(open).toHaveBeenCalledTimes(1)
  })
})
