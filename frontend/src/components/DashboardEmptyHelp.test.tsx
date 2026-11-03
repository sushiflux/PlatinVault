import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { applyLanguage } from '../i18n'
import { DashboardEmptyHelp } from './DashboardEmptyHelp'

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

// Answers the settings query of the help: which of the first two steps is done
function stubSettings(settings: { steamUserId: string; hasSteamKey: boolean }) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => (url === '/api/settings' ? json({ steamKeyHint: '', ...settings }) : json({}, 404)))
  )
}

function renderHelp(props: { hasLibrary?: boolean } = {}) {
  const onOpenSettings = vi.fn()
  const onShowLibrary = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <DashboardEmptyHelp hasLibrary={props.hasLibrary ?? false} onOpenSettings={onOpenSettings} onShowLibrary={onShowLibrary} />
    </QueryClientProvider>
  )
  return { onOpenSettings, onShowLibrary }
}

const steps = () => within(screen.getByRole('list', { name: 'The three steps' })).getAllByRole('listitem')

describe('DashboardEmptyHelp', () => {
  beforeEach(async () => {
    await applyLanguage('en')
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('explains where the games come from and lists the three steps', async () => {
    stubSettings({ steamUserId: '', hasSteamKey: false })
    renderHelp()
    expect(screen.getByRole('heading', { name: 'Your dashboard is still empty' })).toBeTruthy()
    expect(steps().map((li) => within(li).getByText(/Connect your Steam account|Enter your Steam Web API key|Bring a game onto the dashboard with the star/).textContent)).toEqual([
      'Connect your Steam account',
      'Enter your Steam Web API key',
      'Bring a game onto the dashboard with the star',
    ])
    // no way to "add a game" here: only the steps
    expect(screen.queryByText(/add game|Add another game|My Steam games/i)).toBeNull()
  })

  it('shows nothing done and the sign-in link while neither account nor key exist', async () => {
    stubSettings({ steamUserId: '', hasSteamKey: false })
    renderHelp()
    const login = await screen.findByRole('link', { name: 'Sign in with Steam' })
    expect(login.getAttribute('href')).toMatch(/^\/api\/steam\/openid\/start\?origin=/)
    await waitFor(() => expect(screen.getAllByRole('img', { name: /: open$/ })).toHaveLength(3))
    // the first open step carries the primary button
    expect(login.className).toContain('pv-btn-primary')
    expect(screen.getByRole('button', { name: 'Enter key' }).className).not.toContain('pv-btn-primary')
  })

  it('marks the account done and moves the primary button to the key step', async () => {
    stubSettings({ steamUserId: '76561198000000001', hasSteamKey: false })
    const { onOpenSettings } = renderHelp()
    await waitFor(() => expect(screen.getAllByRole('img', { name: /: done$/ })).toHaveLength(1))
    expect(screen.queryByRole('link', { name: 'Sign in with Steam' })).toBeNull()
    const key = screen.getByRole('button', { name: 'Enter key' })
    expect(key.className).toContain('pv-btn-primary')
    fireEvent.click(key)
    expect(onOpenSettings).toHaveBeenCalledWith('steam')
    // changing the account goes to the same tab
    fireEvent.click(screen.getByRole('button', { name: 'Change' }))
    expect(onOpenSettings).toHaveBeenCalledTimes(2)
  })

  it('links to the page where the Steam key is handed out', async () => {
    stubSettings({ steamUserId: '', hasSteamKey: false })
    renderHelp()
    const link = await screen.findByRole('link', { name: /Get a key from Steam/ })
    expect(link.getAttribute('href')).toBe('https://steamcommunity.com/dev/apikey')
    expect(link.getAttribute('target')).toBe('_blank')
  })

  it('keeps the library step locked, and says why, until the library can be read', async () => {
    stubSettings({ steamUserId: '76561198000000001', hasSteamKey: true })
    const { onShowLibrary } = renderHelp({ hasLibrary: false })
    const locked = await screen.findByRole('button', { name: /Open all games/ })
    expect(locked.getAttribute('aria-disabled')).toBe('true')
    expect(locked.textContent).toContain('Once account and key are set, your library opens here.')
    fireEvent.click(locked)
    expect(onShowLibrary).not.toHaveBeenCalled()
  })

  it('opens "All games" once the library can be read', async () => {
    stubSettings({ steamUserId: '76561198000000001', hasSteamKey: true })
    const { onShowLibrary } = renderHelp({ hasLibrary: true })
    await waitFor(() => expect(screen.getAllByRole('img', { name: /: done$/ })).toHaveLength(2))
    const open = screen.getByRole('button', { name: 'Open all games' })
    expect(open.getAttribute('aria-disabled')).toBeNull()
    expect(open.className).toContain('pv-btn-primary')
    fireEvent.click(open)
    expect(onShowLibrary).toHaveBeenCalledTimes(1)
  })

  it('speaks German in the German interface', async () => {
    await applyLanguage('de')
    stubSettings({ steamUserId: '', hasSteamKey: false })
    renderHelp()
    expect(screen.getByRole('heading', { name: 'Dein Dashboard ist noch leer' })).toBeTruthy()
    expect(await screen.findByRole('link', { name: 'Mit Steam anmelden' })).toBeTruthy()
  })
})
