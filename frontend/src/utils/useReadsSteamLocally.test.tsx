import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { SettingsResponseDto } from '../api/types'
import { useReadsSteamLocally } from './useReadsSteamLocally'

const host = vi.hoisted(() => ({ value: 'server' as 'server' | 'desktop' }))
const readsLocally = vi.hoisted(() => ({ value: false }))
const fetchBranding = vi.hoisted(() => vi.fn(async () => ({ productName: 'PlatinVault', tagline: '', company: 'SushiFlux', version: 'v1', host: host.value })))
const fetchSettings = vi.hoisted(() => vi.fn(async () => ({ steamLocal: { readsLocally: readsLocally.value } }) as unknown as SettingsResponseDto))
vi.mock('../api/client', () => ({ fetchBranding, fetchSettings }))

afterEach(cleanup)

function Probe() {
  return <span data-testid="probe">{useReadsSteamLocally() ? 'local' : 'bridge'}</span>
}

function renderProbe() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <Probe />
    </QueryClientProvider>
  )
}

describe('useReadsSteamLocally', () => {
  it('is false on a server without a local Steam (Unraid, NAS: the bridge is needed)', async () => {
    host.value = 'server'
    readsLocally.value = false
    renderProbe()
    await waitFor(() => expect(fetchSettings).toHaveBeenCalled())
    expect(screen.getByTestId('probe').textContent).toBe('bridge')
  })

  it('is true when the server reads Steam on its own computer (Linux package)', async () => {
    host.value = 'server'
    readsLocally.value = true
    renderProbe()
    await waitFor(() => expect(screen.getByTestId('probe').textContent).toBe('local'))
  })

  it('is always true in the Windows program', async () => {
    host.value = 'desktop'
    readsLocally.value = false
    renderProbe()
    await waitFor(() => expect(screen.getByTestId('probe').textContent).toBe('local'))
  })
})
