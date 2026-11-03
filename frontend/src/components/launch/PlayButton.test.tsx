import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { applyLanguage } from '../../i18n'
import { LaunchProvider } from './LaunchProvider'
import { PlayButton } from './PlayButton'

interface Server {
  bridgeConnected: boolean
  localSteamAvailable: boolean
  launch: () => Response
  command?: () => Response
  runningSlug?: string | null
  runningAppId?: number
}

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

// Answers what Play asks: the two reach queries, the launch request and the command poll
function stubServer(server: Server) {
  const calls: { url: string; method: string }[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      calls.push({ url, method })
      if (url === '/api/launch/status') return json({ bridgeConnected: server.bridgeConnected, bridgeLastSeenAt: null })
      if (url === '/api/live/status') return json({ runningAppId: server.runningAppId ?? 0, steamId64: '1', runningGameSlug: server.runningSlug ?? null, runningGameTracked: false, localSteamAvailable: server.localSteamAvailable, lastBridgeEventAt: null })
      if (url.startsWith('/api/launch/') && server.command) return server.command()
      if (url.endsWith('/launch') && method === 'POST') return server.launch()
      return json({}, 404)
    })
  )
  return calls
}

function renderButton(extra: { showRunning?: boolean; steamAppId?: number; variant?: 'segment' | 'button' } = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <LaunchProvider>
        <PlayButton target={{ kind: 'game', slug: 'final-fantasy' }} gameName="Final Fantasy" {...extra} />
      </LaunchProvider>
    </QueryClientProvider>
  )
}

const bridgeAnswer = { via: 'bridge', url: 'steam://run/1', appId: 1, commandId: 'c1', status: 'pending', expiresInSeconds: 30, bridgeConnected: true }

describe('PlayButton', () => {
  beforeEach(async () => {
    await applyLanguage('en')
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('is there while the bridge is connected, named after the game', async () => {
    stubServer({ bridgeConnected: true, localSteamAvailable: false, launch: () => json(bridgeAnswer) })
    renderButton()
    expect(await screen.findByRole('button', { name: 'Play “Final Fantasy”' })).toBeTruthy()
  })

  it('is there on the PC that has Steam itself, without a bridge', async () => {
    stubServer({ bridgeConnected: false, localSteamAvailable: true, launch: () => json(bridgeAnswer) })
    renderButton()
    expect(await screen.findByRole('button', { name: 'Play “Final Fantasy”' })).toBeTruthy()
  })

  it('is not rendered at all without a bridge and without a local Steam', async () => {
    const calls = stubServer({ bridgeConnected: false, localSteamAvailable: false, launch: () => json(bridgeAnswer) })
    const { container } = renderButton()
    await waitFor(() => expect(calls.some((call) => call.url === '/api/launch/status')).toBe(true))
    await waitFor(() => expect(calls.some((call) => call.url === '/api/live/status')).toBe(true))
    expect(screen.queryByRole('button')).toBeNull()
    expect(container.querySelector('button')).toBeNull()
  })

  it('opens the steam:// link in the browser when the server says "browser", and shows no notice', async () => {
    const calls = stubServer({
      bridgeConnected: false,
      localSteamAvailable: true,
      launch: () => json({ via: 'browser', url: 'steam://run/1', appId: 1, commandId: null, status: null, expiresInSeconds: null, bridgeConnected: false }),
    })
    const opened: string[] = []
    const onClick = (event: Event) => {
      const link = (event.target as HTMLElement).closest('a')
      if (link) {
        opened.push(link.getAttribute('href') ?? '')
        event.preventDefault()
      }
    }
    document.addEventListener('click', onClick, true)
    renderButton()
    fireEvent.click(await screen.findByRole('button', { name: 'Play “Final Fantasy”' }))
    await waitFor(() => expect(opened).toEqual(['steam://run/1']))
    expect(calls.filter((call) => call.method === 'POST').map((call) => call.url)).toEqual(['/api/games/final-fantasy/launch'])
    expect(screen.queryByRole('status')).toBeNull()
    document.removeEventListener('click', onClick, true)
  })

  it('follows a bridge command: progress, then a success notice', async () => {
    stubServer({
      bridgeConnected: true,
      localSteamAvailable: false,
      launch: () => json(bridgeAnswer),
      command: () => json({ commandId: 'c1', appId: 1, gameName: 'Final Fantasy', status: 'done', finished: true, reason: null, message: 'Handed to Windows.' }),
    })
    renderButton()
    fireEvent.click(await screen.findByRole('button', { name: 'Play “Final Fantasy”' }))
    expect(await screen.findByText('Starting “Final Fantasy” on the gaming PC …')).toBeTruthy()
    expect(await screen.findByText('“Final Fantasy” is starting', {}, { timeout: 4000 })).toBeTruthy()
    expect(screen.getByText('Handed to Windows.')).toBeTruthy()
  })

  it('shows the localized message of the server when the start failed', async () => {
    stubServer({
      bridgeConnected: true,
      localSteamAvailable: false,
      launch: () => json(bridgeAnswer),
      command: () => json({ commandId: 'c1', appId: 1, gameName: 'Final Fantasy', status: 'failed', finished: true, reason: 'steam-not-found', message: 'Steam was not found on the gaming PC.' }),
    })
    renderButton()
    fireEvent.click(await screen.findByRole('button', { name: 'Play “Final Fantasy”' }))
    expect(await screen.findByText('Could not start “Final Fantasy”', {}, { timeout: 4000 })).toBeTruthy()
    expect(screen.getByText('Steam was not found on the gaming PC.')).toBeTruthy()
  })

  describe('game running state', () => {
    it('shows "Game running", not clickable, with both labels stacked for a stable size', async () => {
      const calls = stubServer({ bridgeConnected: true, localSteamAvailable: false, launch: () => json(bridgeAnswer), runningSlug: 'final-fantasy', runningAppId: 5 })
      renderButton({ showRunning: true, steamAppId: 5 })
      const button = await screen.findByRole('button', { name: '“Final Fantasy” is running' })
      expect(button.getAttribute('aria-disabled')).toBe('true')
      expect(button.className).toContain('pv-play-button--running')
      expect(button.textContent).toContain('Game running')
      // The hidden twin label keeps the width of the normal state
      expect(button.querySelector('.invisible')?.textContent).toBe('Play')
      fireEvent.click(button)
      expect(calls.filter((call) => call.method === 'POST')).toEqual([])
    })

    it('matches by Steam AppID too', async () => {
      stubServer({ bridgeConnected: true, localSteamAvailable: false, launch: () => json(bridgeAnswer), runningSlug: null, runningAppId: 5 })
      renderButton({ showRunning: true, steamAppId: 5 })
      expect(await screen.findByRole('button', { name: '“Final Fantasy” is running' })).toBeTruthy()
    })

    it('shows Play (with the running label as hidden twin) when another game or nothing runs', async () => {
      stubServer({ bridgeConnected: true, localSteamAvailable: false, launch: () => json(bridgeAnswer), runningSlug: 'other', runningAppId: 9 })
      renderButton({ showRunning: true, steamAppId: 5 })
      const button = await screen.findByRole('button', { name: 'Play “Final Fantasy”' })
      expect(button.querySelector('.invisible')?.textContent).toBe('Game running')
    })

    it('on a card segment: "Running" state, not clickable, short label with the Play label stacked as the hidden twin', async () => {
      const calls = stubServer({ bridgeConnected: true, localSteamAvailable: false, launch: () => json(bridgeAnswer), runningSlug: null, runningAppId: 5 })
      renderButton({ showRunning: true, steamAppId: 5, variant: 'segment' })
      const button = await screen.findByRole('button', { name: '“Final Fantasy” is running' })
      expect(button.className).toContain('pv-bar-seg--running')
      expect(button.getAttribute('aria-disabled')).toBe('true')
      expect(button.querySelector('.pv-live-dot')).toBeTruthy()
      expect(button.textContent).toContain('Running')
      expect(button.querySelector('.invisible')?.textContent).toBe('Play')
      fireEvent.click(button)
      expect(calls.filter((call) => call.method === 'POST')).toEqual([])
    })

    it('on a card segment of another game the Play segment stays as it is', async () => {
      stubServer({ bridgeConnected: true, localSteamAvailable: false, launch: () => json(bridgeAnswer), runningSlug: 'other', runningAppId: 9 })
      renderButton({ showRunning: true, steamAppId: 5, variant: 'segment' })
      const button = await screen.findByRole('button', { name: 'Play “Final Fantasy”' })
      expect(button.className).not.toContain('pv-bar-seg--running')
    })

    it('ignores the running game without the showRunning prop (cards stay unchanged)', async () => {
      stubServer({ bridgeConnected: true, localSteamAvailable: false, launch: () => json(bridgeAnswer), runningSlug: 'final-fantasy', runningAppId: 5 })
      renderButton({ steamAppId: 5 })
      expect(await screen.findByRole('button', { name: 'Play “Final Fantasy”' })).toBeTruthy()
    })
  })

  it('names the reason when the request itself is refused (too many pending starts)', async () => {
    stubServer({
      bridgeConnected: true,
      localSteamAvailable: false,
      launch: () => json({ title: 'Too many', detail: 'There are already too many starts waiting.' }, 429),
    })
    renderButton()
    fireEvent.click(await screen.findByRole('button', { name: 'Play “Final Fantasy”' }))
    expect(await screen.findByText('Could not start “Final Fantasy”')).toBeTruthy()
    expect(screen.getByText('There are already too many starts waiting.')).toBeTruthy()
  })
})
