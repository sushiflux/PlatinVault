import { act } from 'react'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { applyLanguage } from '../../i18n'
import { TOUR_DONE_EVENT } from '../onboarding/useOnboardingTour'
import { WhatsNewGate } from './WhatsNewGate'
import { useWhatsNewOpen } from './whatsNewState'

const ONBOARDING_KEY = 'pv_onboarding_done_v1'
const version = 'v2026.11.18 (9.7263 β)'

const notes = [
  `## ${version} · Unraid, Windows`,
  '',
  '### ✨ Neu',
  '',
  '- **Angeheftet:** Erfolge anheften.',
].join('\n')

interface Call {
  url: string
  method: string
  headers: Record<string, string>
}

// Answers the two calls of the gate: GET /api/whats-new and POST /api/whats-new/seen
function stubServer(answer: { show: boolean; notes: string | null }) {
  const calls: Call[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const headers: Record<string, string> = {}
      new Headers(init?.headers).forEach((value, key) => (headers[key.toLowerCase()] = value))
      calls.push({ url, method: init?.method ?? 'GET', headers })
      if (url === '/api/whats-new/seen') return new Response(null, { status: 204 })
      return new Response(JSON.stringify({ version, ...answer }), { status: 200, headers: { 'Content-Type': 'application/json' } })
    })
  )
  return calls
}

function OpenProbe() {
  return <span data-testid="probe">{String(useWhatsNewOpen())}</span>
}

function renderGate() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <WhatsNewGate />
      <OpenProbe />
    </QueryClientProvider>
  )
}

beforeEach(async () => {
  await applyLanguage('de')
  localStorage.clear()
  localStorage.setItem(ONBOARDING_KEY, '1')
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('WhatsNewGate', () => {
  it('opens by itself with the notes of the new version and a version title', async () => {
    stubServer({ show: true, notes })
    renderGate()
    const dialog = await screen.findByRole('dialog')
    expect(screen.getByRole('heading', { level: 2 }).textContent).toBe(`Das ist neu in ${version}`)
    expect(dialog.textContent).toContain('Erfolge anheften')
    expect(dialog.textContent).toContain('Beta')
    expect(dialog.textContent).toContain('für Unraid, Windows')
    expect(screen.getByTestId('probe').textContent).toBe('true')
  })

  it('closes on the button, tells the server once with the CSRF header and never opens again', async () => {
    const calls = stubServer({ show: true, notes })
    renderGate()
    await screen.findByRole('dialog')
    // The footer button (the focused one); the icon button in the header does the same
    fireEvent.click(screen.getAllByRole('button', { name: /Schließen/ }).at(-1) as HTMLElement)
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    await waitFor(() => expect(calls.filter((c) => c.url === '/api/whats-new/seen')).toHaveLength(1))
    const seen = calls.find((c) => c.url === '/api/whats-new/seen')
    expect(seen?.method).toBe('POST')
    expect(seen?.headers['x-requested-with']).toBe('PlatinVault')
    expect(screen.getByTestId('probe').textContent).toBe('false')
  })

  it('closes on Esc and on a click next to the window', async () => {
    const calls = stubServer({ show: true, notes })
    const { unmount } = renderGate()
    await screen.findByRole('dialog')
    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    await waitFor(() => expect(calls.some((c) => c.url === '/api/whats-new/seen')).toBe(true))
    unmount()

    stubServer({ show: true, notes })
    renderGate()
    const dialog = await screen.findByRole('dialog')
    fireEvent.mouseDown(dialog.parentElement as HTMLElement)
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })

  it('stays closed when the server says there is nothing to show', async () => {
    const calls = stubServer({ show: false, notes })
    renderGate()
    await waitFor(() => expect(calls.some((c) => c.url === '/api/whats-new')).toBe(true))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(calls.some((c) => c.url === '/api/whats-new/seen')).toBe(false)
  })

  it('stays closed without notes in the entry', async () => {
    const calls = stubServer({ show: true, notes: null })
    renderGate()
    await waitFor(() => expect(calls.some((c) => c.url === '/api/whats-new')).toBe(true))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('waits for the first-start tour: no request and no window until the tour is done', async () => {
    localStorage.removeItem(ONBOARDING_KEY)
    const calls = stubServer({ show: true, notes })
    renderGate()
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50))
    })
    expect(calls).toHaveLength(0)
    expect(screen.queryByRole('dialog')).toBeNull()

    localStorage.setItem(ONBOARDING_KEY, '1')
    act(() => {
      window.dispatchEvent(new Event(TOUR_DONE_EVENT))
    })
    expect(await screen.findByRole('dialog')).toBeTruthy()
  })
})
