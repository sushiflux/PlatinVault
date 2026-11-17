import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { LIVE_COUNT_BATCH, LIVE_COUNT_DEBOUNCE_MS, LIVE_COUNT_NAME_MAX, liveCountName, useTwitchLiveCounts } from './useTwitchLiveCounts'

// The live counts of the tiles that are on screen: tiles report themselves (watch), a fake IntersectionObserver says which are visible,
// a fake fetch answers the batches.

type Entry = { target: Element; isIntersecting: boolean }
const observers: FakeObserver[] = []

class FakeObserver {
  options: IntersectionObserverInit | undefined
  targets = new Set<Element>()
  constructor(
    private callback: (entries: Entry[]) => void,
    options?: IntersectionObserverInit
  ) {
    this.options = options
    observers.push(this)
  }
  observe(target: Element) {
    this.targets.add(target)
  }
  unobserve(target: Element) {
    this.targets.delete(target)
  }
  disconnect() {
    this.targets.clear()
  }
  // Test helper: tell the hook which tiles are now visible / hidden
  report(visible: Element[], hidden: Element[] = []) {
    this.callback([...visible.map((target) => ({ target, isIntersecting: true })), ...hidden.map((target) => ({ target, isIntersecting: false }))])
  }
}

const ok = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })

interface Call {
  games: { appId: number; name: string }[]
}

function stub(settings: Record<string, unknown>, answers: (call: number, games: Call['games']) => Response) {
  const bodies: Call[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/settings') return ok(settings)
      if (url === '/api/twitch/live-counts') {
        const body = JSON.parse(String(init?.body)) as Call
        bodies.push(body)
        return answers(bodies.length - 1, body.games)
      }
      return new Response('{}', { status: 404, headers: { 'Content-Type': 'application/json' } })
    })
  )
  return bodies
}

const ENABLED = { hasTwitchKey: true, twitchStreamsEnabled: true }
// Every game has 5 live streams
const everyGameFive = (_call: number, games: Call['games']) => ok({ counts: games.map((g) => ({ appId: g.appId, count: 5, atLeast: false })), pending: [] })

function wrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const tile = () => document.createElement('div')
const game = (appId: number) => ({ appId, name: `Game ${appId}` })

// Mounts the hook and waits until the settings arrived (the batcher is then switched on)
async function mount() {
  const hook = renderHook(() => useTwitchLiveCounts(), { wrapper: wrapper() })
  await act(async () => {
    await vi.advanceTimersByTimeAsync(50)
  })
  return hook
}

const settle = (ms = LIVE_COUNT_DEBOUNCE_MS + 50) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms)
  })

describe('useTwitchLiveCounts', () => {
  beforeEach(() => {
    observers.length = 0
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.stubGlobal('IntersectionObserver', FakeObserver)
  })

  afterEach(() => {
    cleanup()
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('asks for nothing until a tile is on screen, then once for the visible ones, and hands out the counts', async () => {
    const bodies = stub(ENABLED, () => ok({ counts: [{ appId: 620, count: 12, atLeast: false }, { appId: 70, count: 300, atLeast: true }], pending: [] }))
    const { result } = await mount()
    const [a, b] = [tile(), tile()]
    act(() => {
      result.current.watch(a, game(620))
      result.current.watch(b, game(70))
    })
    await settle()
    expect(bodies).toHaveLength(0)

    act(() => observers[0]!.report([a, b]))
    await settle()
    expect(bodies).toHaveLength(1)
    expect(bodies[0]!.games.map((g) => g.appId).sort((x, y) => x - y)).toEqual([70, 620])
    await waitFor(() => expect(result.current.counts.get(620)).toEqual({ count: 12, atLeast: false }))
    expect(result.current.counts.get(70)).toEqual({ count: 300, atLeast: true })
  })

  it('watches a screen above and below the window (the margin of the observer)', async () => {
    stub(ENABLED, everyGameFive)
    const { result } = await mount()
    act(() => {
      result.current.watch(tile(), game(1))
    })
    expect(observers[0]!.options?.rootMargin).toBe('100% 0px')
  })

  it('requests a tile far down the list (beyond the first 120) as soon as it becomes visible', async () => {
    const bodies = stub(ENABLED, everyGameFive)
    const { result } = await mount()
    const tiles = Array.from({ length: 583 }, () => tile())
    act(() => {
      tiles.forEach((el, index) => result.current.watch(el, game(index + 1)))
    })
    // The first screenful is visible: only those are asked for
    act(() => observers[0]!.report(tiles.slice(0, 20)))
    await settle()
    expect(bodies).toHaveLength(1)
    expect(bodies[0]!.games).toHaveLength(20)

    // Scrolled down to tile 145 (Far Cry 6 in the owner's library): it is requested, the first ones are not asked again
    act(() => observers[0]!.report(tiles.slice(140, 150), tiles.slice(0, 20)))
    await settle()
    expect(bodies).toHaveLength(2)
    expect(bodies[1]!.games.map((g) => g.appId)).toContain(145)
    expect(bodies[1]!.games).toHaveLength(10)
    await waitFor(() => expect(result.current.counts.get(145)?.count).toBe(5))
    // What was fetched before stays
    expect(result.current.counts.get(1)?.count).toBe(5)
  })

  it('never sends more than 120 games in one request, one request at a time', async () => {
    const bodies = stub(ENABLED, everyGameFive)
    const { result } = await mount()
    const tiles = Array.from({ length: 300 }, () => tile())
    act(() => {
      tiles.forEach((el, index) => result.current.watch(el, game(index + 1)))
      observers[0]!.report(tiles)
    })
    await settle(LIVE_COUNT_DEBOUNCE_MS + 500)
    await waitFor(() => expect(bodies).toHaveLength(3))
    expect(Math.max(...bodies.map((body) => body.games.length))).toBe(LIVE_COUNT_BATCH)
    expect(bodies.reduce((sum, body) => sum + body.games.length, 0)).toBe(300)
    // Nothing twice
    expect(new Set(bodies.flatMap((body) => body.games.map((g) => g.appId))).size).toBe(300)
  })

  it('collects fast scrolling into one request after the debounce, and leaves out tiles that scrolled past again', async () => {
    const bodies = stub(ENABLED, everyGameFive)
    const { result } = await mount()
    const tiles = Array.from({ length: 60 }, () => tile())
    act(() => tiles.forEach((el, index) => result.current.watch(el, game(index + 1))))

    // Ten scroll steps, 100 ms apart: each shows a new group of tiles and hides the previous one
    for (let step = 0; step < 6; step += 1) {
      const shown = tiles.slice(step * 10, step * 10 + 10)
      const gone = step > 0 ? tiles.slice((step - 1) * 10, step * 10) : []
      act(() => observers[0]!.report(shown, gone))
      await settle(100)
    }
    expect(bodies).toHaveLength(0)
    await settle(LIVE_COUNT_DEBOUNCE_MS)
    expect(bodies).toHaveLength(1)
    // Only the group that is on screen when the scrolling stops
    expect(bodies[0]!.games.map((g) => g.appId)).toEqual(expect.arrayContaining([51, 60]))
    expect(bodies[0]!.games).toHaveLength(10)
  })

  it('does not ask again for a game whose answer is fresh, but does after five minutes', async () => {
    const bodies = stub(ENABLED, everyGameFive)
    const { result } = await mount()
    const a = tile()
    act(() => {
      result.current.watch(a, game(7))
    })
    act(() => observers[0]!.report([a]))
    await settle()
    expect(bodies).toHaveLength(1)

    act(() => observers[0]!.report([], [a]))
    act(() => observers[0]!.report([a]))
    await settle()
    expect(bodies).toHaveLength(1)

    await settle(5 * 60_000 + 1000)
    act(() => observers[0]!.report([], [a]))
    act(() => observers[0]!.report([a]))
    await settle()
    expect(bodies).toHaveLength(2)
  })

  it('asks nothing without Twitch credentials or with the switch off', async () => {
    const bodies = stub({ hasTwitchKey: false, twitchStreamsEnabled: true }, everyGameFive)
    const { result } = await mount()
    const a = tile()
    act(() => {
      result.current.watch(a, game(1))
      observers[0]!.report([a])
    })
    await settle()
    expect(result.current.counts.size).toBe(0)
    expect(bodies).toHaveLength(0)
    cleanup()

    const off = stub({ hasTwitchKey: true, twitchStreamsEnabled: false }, everyGameFive)
    const second = await mount()
    const b = tile()
    act(() => {
      second.result.current.watch(b, game(1))
      observers[observers.length - 1]!.report([b])
    })
    await settle()
    expect(off).toHaveLength(0)
  })

  it('shows no counts and no error when the answer fails', async () => {
    const bodies = stub(ENABLED, () => new Response('{"title":"x","detail":"Twitch down"}', { status: 502, headers: { 'Content-Type': 'application/problem+json' } }))
    const { result } = await mount()
    const a = tile()
    act(() => {
      result.current.watch(a, game(1))
      observers[0]!.report([a])
    })
    await settle()
    expect(bodies).toHaveLength(1)
    expect(result.current.counts.size).toBe(0)
  })

  it('asks again after 3 s while the server reports games as pending, then stops', async () => {
    const bodies = stub(ENABLED, (call) =>
      call === 0
        ? ok({ counts: [{ appId: 620, count: 4, atLeast: false }], pending: [70] })
        : ok({ counts: [{ appId: 70, count: 9, atLeast: false }], pending: [] })
    )
    const { result } = await mount()
    const [a, b] = [tile(), tile()]
    act(() => {
      result.current.watch(a, game(620))
      result.current.watch(b, game(70))
      observers[0]!.report([a, b])
    })
    await settle()
    expect(result.current.counts.get(620)?.count).toBe(4)
    expect(result.current.counts.get(70)).toBeUndefined()

    await settle(3000 + LIVE_COUNT_DEBOUNCE_MS + 100)
    expect(bodies).toHaveLength(2)
    // Only the pending one is asked again
    expect(bodies[1]!.games.map((g) => g.appId)).toEqual([70])
    expect(result.current.counts.get(70)?.count).toBe(9)

    await settle(20_000)
    expect(bodies).toHaveLength(2)
  })

  it('drops the number of a game the server no longer reports (no Twitch category) and keeps the others while a new batch is on its way', async () => {
    const bodies = stub(ENABLED, (call) => (call === 0 ? ok({ counts: [{ appId: 1, count: 3, atLeast: false }, { appId: 2, count: 8, atLeast: false }], pending: [] }) : ok({ counts: [{ appId: 1, count: 4, atLeast: false }], pending: [] })))
    const { result } = await mount()
    const [a, b] = [tile(), tile()]
    act(() => {
      result.current.watch(a, game(1))
      result.current.watch(b, game(2))
      observers[0]!.report([a, b])
    })
    await settle()
    expect(result.current.counts.get(2)?.count).toBe(8)

    await settle(5 * 60_000 + 1000)
    act(() => observers[0]!.report([], [a, b]))
    act(() => observers[0]!.report([a, b]))
    await settle()
    expect(bodies).toHaveLength(2)
    expect(result.current.counts.get(1)?.count).toBe(4)
    expect(result.current.counts.has(2)).toBe(false)
  })
})

describe('liveCountName', () => {
  beforeEach(() => {
    observers.length = 0
    vi.useFakeTimers({ shouldAdvanceTime: true })
    vi.stubGlobal('IntersectionObserver', FakeObserver)
  })

  afterEach(() => {
    cleanup()
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('keeps titles up to the server limit and clips longer ones without leaving half a surrogate pair', () => {
    expect(liveCountName('Portal 2')).toBe('Portal 2')
    expect(liveCountName('x'.repeat(LIVE_COUNT_NAME_MAX))).toHaveLength(LIVE_COUNT_NAME_MAX)
    expect(liveCountName('x'.repeat(250))).toHaveLength(LIVE_COUNT_NAME_MAX)
    // An emoji (two units) across the limit is dropped as a whole
    const clipped = liveCountName('x'.repeat(LIVE_COUNT_NAME_MAX - 1) + '\u{1F3C6}' + 'y')
    expect(clipped).toBe('x'.repeat(LIVE_COUNT_NAME_MAX - 1))
    expect(JSON.stringify(clipped)).not.toContain('\\ud8')
  })

  it('never sends a title the server would refuse the whole batch for', async () => {
    const bodies = stub(ENABLED, everyGameFive)
    const { result } = await mount()
    const [a, b] = [tile(), tile()]
    act(() => {
      result.current.watch(a, { appId: 1, name: 'A'.repeat(260) })
      result.current.watch(b, game(2))
      observers[0]!.report([a, b])
    })
    await settle()
    expect(bodies).toHaveLength(1)
    expect(bodies[0]!.games.every((g) => g.name.length <= LIVE_COUNT_NAME_MAX)).toBe(true)
    await waitFor(() => expect(result.current.counts.get(1)?.count).toBe(5))
  })
})
