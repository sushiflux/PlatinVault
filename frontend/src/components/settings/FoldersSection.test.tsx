import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { textWithoutCopyButton } from '../../test/withoutCopyButton'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopFolderKind, DesktopFoldersDto } from '../../api/types'
import { FoldersSection } from './FoldersSection'

const host = vi.hoisted(() => ({ value: 'desktop' as 'desktop' | 'server' }))
const fetchBranding = vi.hoisted(() =>
  vi.fn(async () => ({ productName: 'PlatinVault', tagline: '', company: 'SushiFlux', version: 'v2026.11.14 (15.1)', host: host.value }))
)
const fetchDesktopFolders = vi.hoisted(() => vi.fn<() => Promise<DesktopFoldersDto>>())
const openDesktopFolder = vi.hoisted(() => vi.fn<(kind: DesktopFolderKind) => Promise<void>>())
vi.mock('../../api/client', () => ({ fetchBranding, fetchDesktopFolders, openDesktopFolder }))

const folders: DesktopFoldersDto = {
  folders: [
    { kind: 'program', path: 'C:\\Users\\Someone\\AppData\\Local\\Programs\\PlatinVault' },
    { kind: 'data', path: 'C:\\Users\\Someone\\AppData\\Local\\PlatinVault' },
    { kind: 'backups', path: 'C:\\Users\\Someone\\AppData\\Local\\PlatinVault\\backups' },
    { kind: 'logs', path: 'C:\\Users\\Someone\\AppData\\Local\\PlatinVault\\logs' },
  ],
}

function renderSection() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <FoldersSection />
    </QueryClientProvider>
  )
}

beforeEach(() => {
  host.value = 'desktop'
  fetchDesktopFolders.mockReset()
  openDesktopFolder.mockReset()
})
afterEach(cleanup)

describe('FoldersSection', () => {
  it('is not there in the browser / container host and asks the server for nothing', async () => {
    host.value = 'server'
    renderSection()
    await waitFor(() => expect(fetchBranding).toHaveBeenCalled())
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(screen.queryByTestId('folders-section')).toBeNull()
    expect(fetchDesktopFolders).not.toHaveBeenCalled()
  })

  it('lists the four folders with their complete paths and the reason for the program folder', async () => {
    fetchDesktopFolders.mockResolvedValue(folders)
    renderSection()
    expect(await screen.findByTestId('folders-section')).toBeTruthy()
    for (const label of ['Programmordner', 'Datenordner', 'Sicherungen', 'Protokolle']) expect(screen.getByText(label)).toBeTruthy()
    await waitFor(() => expect(screen.getByTestId('folder-path-program').textContent).toBe(folders.folders[0]?.path))
    expect(screen.getByTestId('folder-path-logs').textContent).toBe(folders.folders[3]?.path)
    expect(screen.getByTestId('folders-section').textContent).toContain('ohne Administratorrechte installiert wird')
  })

  it('shows the four rows already while the paths are read, with "Öffnen" disabled', async () => {
    fetchDesktopFolders.mockReturnValue(new Promise(() => undefined))
    renderSection()
    await screen.findByTestId('folders-section')
    expect(screen.getAllByRole('button', { name: /öffnen$/ })).toHaveLength(4)
    expect((screen.getByRole('button', { name: 'Datenordner öffnen' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByTestId('folder-path-data').textContent).toBe('Wird gelesen …')
  })

  it('opens a folder through the API', async () => {
    fetchDesktopFolders.mockResolvedValue(folders)
    openDesktopFolder.mockResolvedValue(undefined)
    renderSection()
    const button = await screen.findByRole('button', { name: 'Sicherungen öffnen' })
    await waitFor(() => expect((button as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(button)
    await waitFor(() => expect(openDesktopFolder).toHaveBeenCalledWith('backups'))
  })

  it('shows a failed open completely', async () => {
    fetchDesktopFolders.mockResolvedValue(folders)
    openDesktopFolder.mockRejectedValue(new Error('Der Ordner existiert nicht mehr: C:\\Users\\Someone\\AppData\\Local\\PlatinVault\\logs'))
    renderSection()
    const button = await screen.findByRole('button', { name: 'Protokolle öffnen' })
    await waitFor(() => expect((button as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(button)
    const alert = await screen.findByRole('alert')
    expect(textWithoutCopyButton(alert)).toBe(
      'Der Ordner „Protokolle“ konnte nicht geöffnet werden: Der Ordner existiert nicht mehr: C:\\Users\\Someone\\AppData\\Local\\PlatinVault\\logs'
    )
  })

  it('shows a failed load completely and keeps the rows', async () => {
    fetchDesktopFolders.mockRejectedValue(new Error('Server nicht erreichbar: Verbindung abgelehnt.'))
    renderSection()
    expect(textWithoutCopyButton(await screen.findByRole('alert'))).toBe('Die Ordner konnten nicht ermittelt werden: Server nicht erreichbar: Verbindung abgelehnt.')
    expect(screen.getAllByRole('button', { name: /öffnen$/ })).toHaveLength(4)
  })

  it('copies a path and says so', async () => {
    fetchDesktopFolders.mockResolvedValue(folders)
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } })
    renderSection()
    const copyButtons = await screen.findAllByRole('button', { name: 'Pfad kopieren' })
    await waitFor(() => expect((copyButtons[1] as HTMLButtonElement).disabled).toBe(false))
    fireEvent.click(copyButtons[1] as HTMLElement)
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(folders.folders[1]?.path))
    await waitFor(() => expect(screen.getByRole('status').textContent).toBe('Der Pfad von „Datenordner“ wurde in die Zwischenablage kopiert.'))
  })
})
