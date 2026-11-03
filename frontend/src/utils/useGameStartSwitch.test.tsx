import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Link, MemoryRouter, useLocation } from 'react-router'
import { applyLanguage } from '../i18n'
import { LaunchProvider } from '../components/launch/LaunchProvider'
import { useGameStartSwitch } from './useGameStartSwitch'
import { useWindowBusy } from './windowBusy'

interface World {
  runningAppId: number
  tracked: boolean
  slug: string | null
  name: string | null
  autoSwitch: boolean
  // What /api/live/running-game answers for a game that is not in the vault
  storeName: string
}

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

let world: World

function stubServer() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => {
      if (url === '/api/live/status') {
        return json({
          runningAppId: world.runningAppId,
          steamId64: '1',
          runningGameSlug: world.slug,
          runningGameTracked: world.tracked,
          runningGameName: world.name,
          localSteamAvailable: true,
          lastBridgeEventAt: null,
        })
      }
      if (url === '/api/settings') return json({ autoSwitchGameEnabled: world.autoSwitch })
      if (url === '/api/live/running-game') return json({ appId: world.runningAppId, name: world.storeName })
      return json({}, 404)
    })
  )
}

function Probe() {
  useGameStartSwitch()
  const location = useLocation()
  return (
    <>
      <div data-testid="where">{location.pathname + location.search}</div>
      <Link to="/">home</Link>
    </>
  )
}

function Busy() {
  useWindowBusy()
  return null
}

let client: QueryClient

function renderApp(initialPath = '/', extra: ReactNode = null) {
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initialPath]}>
        <LaunchProvider>
          <Probe />
          {extra}
        </LaunchProvider>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

// One more answer of the server, as the 5 s poll or the "GameChanged" refresh would bring it
async function poll() {
  await act(async () => {
    await client.refetchQueries({ queryKey: ['live-status'] })
  })
}

// Long enough for the asynchronous part (settings answer, name lookup) to have run: needed before asserting that nothing happened
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 80)) })

const where = () => screen.getByTestId('where').textContent

const stopped = (): World => ({ runningAppId: 0, tracked: false, slug: null, name: null, autoSwitch: true, storeName: 'Hades' })
const hadesInVault = (autoSwitch = true): World => ({ runningAppId: 1145360, tracked: true, slug: 'hades', name: 'Hades', autoSwitch, storeName: 'Hades' })

// Everything below starts with "no game" seen once, so the next game counts as a start
async function startWatching(initialPath = '/', extra: ReactNode = null) {
  world = stopped()
  stubServer()
  renderApp(initialPath, extra)
  await waitFor(() => expect(client.getQueryData(['live-status'])).toBeTruthy())
}

describe('useGameStartSwitch', () => {
  beforeEach(async () => {
    await applyLanguage('en')
  })

  afterEach(() => {
    cleanup()
    document.body.innerHTML = ''
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('switches to the Live trophies of the game that was just started', async () => {
    await startWatching()
    world = hadesInVault()
    await poll()
    await waitFor(() => expect(where()).toBe('/games/hades?tab=live'))
  })

  it('does not switch when the page opens while a game is already running', async () => {
    world = hadesInVault()
    stubServer()
    renderApp()
    await waitFor(() => expect(client.getQueryData(['live-status'])).toBeTruthy())
    await poll()
    await poll()
    await settle()
    expect(where()).toBe('/')
    expect(screen.queryByRole('status')).toBeNull()
    // Not even the settings were asked: the first answer is only the baseline
    expect(vi.mocked(fetch).mock.calls.some(([url]) => url === '/api/settings')).toBe(false)
  })

  it('switches only once while the game keeps running, and not when it stops', async () => {
    await startWatching()
    world = hadesInVault()
    await poll()
    await waitFor(() => expect(where()).toBe('/games/hades?tab=live'))

    // The user leaves the page on purpose; further answers must not pull them back
    fireEvent.click(screen.getByText('home'))
    expect(where()).toBe('/')
    await poll()
    await poll()
    await settle()
    expect(where()).toBe('/')

    world = stopped()
    await poll()
    await settle()
    expect(where()).toBe('/')
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('does nothing on the page of that game already', async () => {
    await startWatching('/games/hades?tab=stats')
    world = hadesInVault()
    await poll()
    // The settings answer is awaited before anything happens; give it time
    await waitFor(() => expect(vi.mocked(fetch).mock.calls.some(([url]) => url === '/api/settings')).toBe(true))
    await settle()
    expect(where()).toBe('/games/hades?tab=stats')
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('does nothing when the switch is off', async () => {
    await startWatching()
    world = hadesInVault(false)
    await poll()
    await waitFor(() => expect(vi.mocked(fetch).mock.calls.some(([url]) => url === '/api/settings')).toBe(true))
    await settle()
    expect(where()).toBe('/')
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('shows a notice with a button instead of switching while a dialog is open', async () => {
    await startWatching()
    document.body.insertAdjacentHTML('beforeend', '<div role="dialog"></div>')
    world = hadesInVault()
    await poll()
    expect(await screen.findByText('Hades was started')).toBeTruthy()
    expect(where()).toBe('/')
    fireEvent.click(screen.getByRole('button', { name: 'Switch to the game' }))
    await waitFor(() => expect(where()).toBe('/games/hades?tab=live'))
    await waitFor(() => expect(screen.queryByText('Hades was started')).toBeNull())
  })

  it('shows a notice instead of switching while the cursor is in a text field', async () => {
    await startWatching()
    document.body.insertAdjacentHTML('beforeend', '<input id="search" type="search">')
    ;(document.getElementById('search') as HTMLInputElement).focus()
    world = hadesInVault()
    await poll()
    expect(await screen.findByText('Hades was started')).toBeTruthy()
    expect(where()).toBe('/')
  })

  it('shows a notice instead of switching while a long AI job runs in this window', async () => {
    await startWatching('/', <Busy />)
    world = hadesInVault()
    await poll()
    expect(await screen.findByText('Hades was started')).toBeTruthy()
    expect(where()).toBe('/')
  })

  it('says what is being played when the game has no guide', async () => {
    await startWatching()
    world = { runningAppId: 1086940, tracked: false, slug: null, name: null, autoSwitch: true, storeName: "Baldur's Gate 3" }
    await poll()
    expect(await screen.findByText("You are playing Baldur's Gate 3 – no guide yet.")).toBeTruthy()
    expect(where()).toBe('/')
    expect(screen.queryByRole('button', { name: 'Switch to the game' })).toBeNull()
  })

  it('takes the notice away again when the game ends', async () => {
    await startWatching()
    world = { runningAppId: 1086940, tracked: false, slug: null, name: null, autoSwitch: true, storeName: 'Hades II' }
    await poll()
    expect(await screen.findByText('You are playing Hades II – no guide yet.')).toBeTruthy()
    world = stopped()
    await poll()
    await waitFor(() => expect(screen.queryByText('You are playing Hades II – no guide yet.')).toBeNull())
  })

  it('does not take a store placeholder for the name of the game', async () => {
    await startWatching()
    world = { runningAppId: 99, tracked: false, slug: null, name: null, autoSwitch: true, storeName: 'Steam App 99' }
    await poll()
    expect(await screen.findByText('You are playing a game – no guide yet.')).toBeTruthy()
  })
})
