import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { LiveAchievementItemDto } from '../../api/types'
import { manualItem, ps5Items } from '../trophies/trophies.fixtures'
import { TrophyCheckProvider } from '../trophies/trophyCheckContext'
import { PinIconButton } from './PinIconButton'
import { PinsScope, pinsKey, usePinCount, usePins } from './pinsContext'
import { PINS_COLUMN_QUERY, PINS_PHONE_QUERY } from './pinsLayout'
import { PinsSurface } from './PinsSurface'

// "Im Blick" for a game added by hand: the pins belong to the game id (no AppID), the cards draw the tier glyph, the detail dialog ticks the trophy.

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const GAME_ID = 'f3a1c2d4-0000-4000-8000-000000000001'

interface Call {
  method: string
  url: string
  body: Record<string, unknown> | null
}

function stubServer(initial: string[] = []) {
  const state = { pins: initial, calls: [] as Call[] }
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const target = new URL(String(url), 'http://localhost')
      const method = init?.method ?? 'GET'
      if (target.pathname === '/api/settings') return json({ achievementNotesEnabled: true })
      if (!target.pathname.startsWith('/api/live/pins')) return json({}, 404)
      const body = init?.body ? (JSON.parse(String(init.body)) as Record<string, unknown>) : null
      state.calls.push({ method, url: `${target.pathname}${target.search}`, body })
      if (method === 'POST' && target.pathname.endsWith('/remove')) {
        const gone = new Set((body?.apiNames as string[]) ?? [])
        state.pins = state.pins.filter((name) => !gone.has(name))
      } else if (method === 'POST') {
        const apiName = body?.apiName as string
        if (!state.pins.includes(apiName)) state.pins = [...state.pins, apiName]
      } else if (method === 'DELETE') {
        const apiName = target.searchParams.get('apiName')
        state.pins = state.pins.filter((name) => name !== apiName)
      }
      return json({ appId: 0, gameId: GAME_ID, apiNames: state.pins, max: 10 })
    })
  )
  return state
}

function PinButtons({ items }: { items: LiveAchievementItemDto[] }) {
  const pins = usePins()
  if (!pins) return null
  return (
    <ul data-testid="pin-list">
      {items.map((entry) => (
        <li key={entry.apiName}>
          <PinIconButton name={entry.displayName} pin={{ pinned: pins.isPinned(entry.apiName), canPin: !pins.full, max: pins.max, onToggle: () => pins.toggle(entry.apiName) }} />
        </li>
      ))}
    </ul>
  )
}

function renderPins(items: LiveAchievementItemDto[], check?: (ach: LiveAchievementItemDto) => { checked: boolean; busy: boolean; onToggle: () => void }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const view = render(
    <QueryClientProvider client={client}>
      <TrophyCheckProvider value={check ?? null}>
        <PinsScope gameId={GAME_ID} onError={() => {}}>
          <PinButtons items={items} />
          <PinsSurface placement="dock" achievements={items} tips={{}} />
        </PinsScope>
      </TrophyCheckProvider>
    </QueryClientProvider>
  )
  return { ...view, client }
}

beforeEach(() => {
  localStorage.clear()
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query === PINS_COLUMN_QUERY ? false : query === PINS_PHONE_QUERY ? false : false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

const pinButton = (name: string) => within(screen.getByTestId('pin-list')).getByRole('button', { name: new RegExp(`„${name}“ (im Blick behalten|nicht mehr im Blick)`) })

describe('"Im Blick" of a game added by hand', () => {
  it('asks for the pins with the game id and pins, unpins and removes with it (appId 0)', async () => {
    const server = stubServer()
    renderPins(ps5Items())
    await waitFor(() => expect(server.calls.length).toBeGreaterThan(0))
    expect(server.calls[0]).toMatchObject({ method: 'GET', url: `/api/live/pins?gameId=${GAME_ID}` })

    fireEvent.click(pinButton('Letzter Boss'))
    await waitFor(() => expect(screen.getByTestId('pins-dock')).toBeTruthy())
    const post = server.calls.find((call) => call.method === 'POST')
    expect(post?.body).toEqual({ appId: 0, gameId: GAME_ID, apiName: 'gold-1' })

    fireEvent.click(pinButton('Letzter Boss'))
    await waitFor(() => expect(server.calls.some((call) => call.method === 'DELETE')).toBe(true))
    expect(server.calls.find((call) => call.method === 'DELETE')?.url).toBe(`/api/live/pins?gameId=${GAME_ID}&apiName=gold-1`)
  })

  it('draws the tier glyph on the card of a trophy that has no icon file, grey while it is open', async () => {
    stubServer(['gold-1', 'silber-1'])
    renderPins(ps5Items())

    const cards = await screen.findAllByTestId('pin-card')
    expect(cards).toHaveLength(2)
    for (const card of cards) expect(card.querySelector('img')).toBeNull()
    const open = cards.find((card) => card.getAttribute('data-api-name') === 'gold-1')!
    const done = cards.find((card) => card.getAttribute('data-api-name') === 'silber-1')!
    expect(open.querySelector('.pv-trophy-glyph[data-tier="Gold"][data-locked]')).toBeTruthy()
    expect(done.querySelector('.pv-trophy-glyph[data-tier="Silver"]')).toBeTruthy()
    expect(done.querySelector('.pv-trophy-glyph[data-locked]')).toBeNull()
  })

  it('ticks the trophy from the detail dialog of a card, with the check of the page', async () => {
    stubServer(['gold-1'])
    const onToggle = vi.fn()
    renderPins(ps5Items(), (ach) => ({ checked: ach.achieved, busy: false, onToggle }))

    fireEvent.click(within((await screen.findAllByTestId('pin-card'))[0]!).getByRole('button', { name: /Anklicken für Details/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Letzter Boss' })
    fireEvent.click(within(dialog).getByRole('button', { name: '„Letzter Boss“ als erledigt abhaken' }))
    expect(onToggle).toHaveBeenCalledTimes(1)
  })

  it('has no check in the detail dialog on a page without one (a Steam game)', async () => {
    stubServer(['gold-1'])
    renderPins(ps5Items())
    fireEvent.click(within((await screen.findAllByTestId('pin-card'))[0]!).getByRole('button', { name: /Anklicken für Details/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Letzter Boss' })
    expect(dialog.querySelector('.pv-tcheck')).toBeNull()
  })

  it('keeps the keys of Steam games and games added by hand apart, and counts pins of either', async () => {
    expect(pinsKey(440)).toEqual(['live-pins', 440])
    expect(pinsKey({ appId: 440 })).toEqual(['live-pins', 440])
    expect(pinsKey({ gameId: GAME_ID })).toEqual(['live-pins', `g:${GAME_ID}`])

    stubServer(['a', 'b'])
    function Count() {
      return <span data-testid="count">{usePinCount({ gameId: GAME_ID })}</span>
    }
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <Count />
      </QueryClientProvider>
    )
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('2'))
  })

  it('gives a page without an AppID and without a game id no pins at all', () => {
    stubServer()
    render(
      <QueryClientProvider client={new QueryClient()}>
        <PinsScope onError={() => {}}>
          <PinsSurface placement="dock" achievements={[manualItem('x')]} tips={{}} />
        </PinsScope>
      </QueryClientProvider>
    )
    expect(screen.queryByTestId('pins-dock')).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
  })
})
