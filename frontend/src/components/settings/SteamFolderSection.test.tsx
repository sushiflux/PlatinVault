import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { SettingsResponseDto, SteamLocalSettingsDto, SteamRootCandidateDto, SteamRootCheckResponse, UpdateSettingsRequest } from '../../api/types'
import { SteamCandidateList, SteamFolderSection } from './SteamFolderSection'

const fetchSettings = vi.hoisted(() => vi.fn<() => Promise<SettingsResponseDto>>())
const checkSteamRoot = vi.hoisted(() => vi.fn<(path: string, signal?: AbortSignal) => Promise<SteamRootCheckResponse>>())
const updateSettings = vi.hoisted(() => vi.fn<(payload: UpdateSettingsRequest) => Promise<SettingsResponseDto>>())
vi.mock('../../api/client', () => ({ fetchSettings, checkSteamRoot, updateSettings }))

beforeEach(() => {
  fetchSettings.mockReset()
  checkSteamRoot.mockReset()
  updateSettings.mockReset()
})
afterEach(cleanup)

function local(overrides: Partial<SteamLocalSettingsDto> = {}): SteamLocalSettingsDto {
  return {
    platform: 'linux',
    mode: 'native',
    found: false,
    root: null,
    method: 'none',
    methodDetail: null,
    experimental: false,
    configuredRoot: null,
    readsLocally: false,
    ...overrides,
  }
}

function settings(steamLocal: SteamLocalSettingsDto | null, steamRoot: string | null = null): SettingsResponseDto {
  return { steamRoot, steamLocal } as SettingsResponseDto
}

function renderSection() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SteamFolderSection />
    </QueryClientProvider>
  )
}

const field = () => screen.getByLabelText('Steam-Ordner (optional)')

describe('SteamFolderSection', () => {
  it('is not shown in a container without Steam (Unraid, NAS: the bridge brings the trophies there)', async () => {
    fetchSettings.mockResolvedValue(settings(local({ mode: 'container' })))
    const { container } = renderSection()
    await waitFor(() => expect(fetchSettings).toHaveBeenCalled())
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(container.querySelector('[data-testid="steam-folder-section"]')).toBeNull()
  })

  it('shows the folder in use and how it was found, with the experimental note on Linux', async () => {
    fetchSettings.mockResolvedValue(
      settings(local({ found: true, root: '/home/player/.local/share/Steam', method: 'symlink', methodDetail: 'native', experimental: true }))
    )
    renderSection()
    expect(await screen.findByText('Benutzt wird: /home/player/.local/share/Steam (gefunden über: Verknüpfung (native Installation))')).toBeTruthy()
    expect(screen.getByText(/noch experimentell/)).toBeTruthy()
    expect(screen.getByText(/Nur nötig, wenn PlatinVault deinen Steam-Ordner nicht selbst findet/)).toBeTruthy()
    expect((field() as HTMLInputElement).placeholder).toContain('/home/')
  })

  it('names the problem while typing and never sends a folder the server rejects', async () => {
    fetchSettings.mockResolvedValue(settings(local()))
    checkSteamRoot.mockResolvedValue({ valid: false, problem: 'noSteamApps', message: 'x', path: '/games', resolvedPath: '/games' })
    renderSection()

    fireEvent.change(await screen.findByLabelText('Steam-Ordner (optional)'), { target: { value: '/games' } })
    // While the server checks, the line says so (its room is reserved, nothing moves)
    expect(screen.getByText('Wird geprüft …')).toBeTruthy()
    expect(await screen.findByText('„/games“ ist kein Steam-Ordner: darin fehlt der Unterordner „steamapps“.')).toBeTruthy()
    expect(field().getAttribute('aria-invalid')).toBe('true')
    expect((screen.getByRole('button', { name: 'Übernehmen' }) as HTMLButtonElement).disabled).toBe(true)
    expect(checkSteamRoot).toHaveBeenCalledTimes(1)
    expect(checkSteamRoot.mock.calls[0]?.[0]).toBe('/games')

    // Editing again drops the old answer at once
    fireEvent.change(field(), { target: { value: '/games/Steam' } })
    expect(screen.queryByText(/kein Steam-Ordner/)).toBeNull()
    expect(field().getAttribute('aria-invalid')).toBeNull()
    expect(updateSettings).not.toHaveBeenCalled()
  })

  it('saves a valid folder in the form the server checked', async () => {
    fetchSettings.mockResolvedValue(settings(local({ platform: 'windows' })))
    checkSteamRoot.mockResolvedValue({ valid: true, problem: 'none', message: null, path: 'D:\\Steam', resolvedPath: 'D:\\Steam' })
    updateSettings.mockResolvedValue(settings(local({ platform: 'windows', found: true, root: 'D:\\Steam', method: 'manual' }), 'D:\\Steam'))
    renderSection()

    expect(((await screen.findByLabelText('Steam-Ordner (optional)')) as HTMLInputElement).placeholder).toContain('C:\\')
    fireEvent.change(field(), { target: { value: 'D:/Steam/' } })
    expect(await screen.findByText('Steam-Ordner gefunden: D:\\Steam')).toBeTruthy()
    const apply = screen.getByRole('button', { name: 'Übernehmen' }) as HTMLButtonElement
    await waitFor(() => expect(apply.disabled).toBe(false))
    fireEvent.click(apply)

    await waitFor(() => expect(updateSettings).toHaveBeenCalledWith({ steamRoot: 'D:\\Steam' }))
    expect(await screen.findByText('Gespeichert – PlatinVault liest Steam jetzt aus D:\\Steam.')).toBeTruthy()
  })

  it('removes a stored folder with "Wieder selbst suchen"', async () => {
    fetchSettings.mockResolvedValue(settings(local({ found: true, root: '/data/Steam', method: 'manual' }), '/data/Steam'))
    checkSteamRoot.mockResolvedValue({ valid: true, problem: 'none', message: null, path: '/data/Steam', resolvedPath: '/data/Steam' })
    updateSettings.mockResolvedValue(settings(local()))
    renderSection()

    await waitFor(() => expect((screen.getByLabelText('Steam-Ordner (optional)') as HTMLInputElement).value).toBe('/data/Steam'))
    // The stored folder itself is nothing new to apply
    await screen.findByText('Steam-Ordner gefunden: /data/Steam')
    expect((screen.getByRole('button', { name: 'Übernehmen' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Wieder selbst suchen' }))

    await waitFor(() => expect(updateSettings).toHaveBeenCalledWith({ steamRoot: '' }))
    expect(await screen.findByText('Entfernt – PlatinVault sucht Steam wieder selbst.')).toBeTruthy()
  })
})

describe('SteamCandidateList', () => {
  const candidates: SteamRootCandidateDto[] = [
    { path: '/home/p/.steam/steam', method: 'symlink', detail: 'native', resolvedPath: '/home/p/.local/share/Steam', exists: true, hasSteamApps: true, isDuplicate: false, chosen: true, isSteamRoot: true },
    { path: '/home/p/.local/share/Steam', method: 'knownPath', detail: 'native', resolvedPath: '/home/p/.local/share/Steam', exists: true, hasSteamApps: true, isDuplicate: true, chosen: false, isSteamRoot: true },
    { path: '/home/p/snap/steam/common/.local/share/Steam', method: 'knownPath', detail: 'snap', resolvedPath: '/home/p/snap/steam/common/.local/share/Steam', exists: false, hasSteamApps: false, isDuplicate: false, chosen: false, isSteamRoot: false },
    { path: '/mnt/games/SteamLibrary', method: 'manual', detail: 'setting', resolvedPath: '/mnt/games/SteamLibrary', exists: true, hasSteamApps: true, isDuplicate: false, chosen: false, isSteamRoot: false },
  ]

  it('lists every place searched on demand, the one in use marked', () => {
    render(<SteamCandidateList candidates={candidates} />)
    const toggle = screen.getByRole('button', { name: 'Gesuchte Orte anzeigen (4)' })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(toggle)

    expect(screen.getByRole('button', { name: 'Gesuchte Orte ausblenden' }).getAttribute('aria-expanded')).toBe('true')
    expect(screen.getByText('benutzt')).toBeTruthy()
    expect(screen.getByText('derselbe Ordner wie weiter oben')).toBeTruthy()
    expect(screen.getByText('nicht vorhanden')).toBeTruthy()
    expect(screen.getByText('nur eine Spielebibliothek (weder „appcache“ noch „config“)')).toBeTruthy()
    expect(screen.getByText(/führt zu \/home\/p\/\.local\/share\/Steam/)).toBeTruthy()
  })
})
