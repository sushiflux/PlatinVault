import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useTwitchLiveCounts } from './useTwitchLiveCounts'

const GAMES = [
  { appId: 620, name: 'Portal 2' },
  { appId: 70, name: 'Half-Life' },
]

function stub(settings: Record<string, unknown>, answers: (call: number) => Response) {
  const bodies: unknown[] = []
  let call = 0
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      if (url === '/api/settings') return new Response(JSON.stringify(settings), { status: 200, headers: { 'Content-Type': 'application/json' } })
      if (url === '/api/twitch/live-counts') {
        bodies.push(JSON.parse(String(init?.body)))
        return answers(call++)
      }
      return new Response('{}', { status: 404, headers: { 'Content-Type': 'application/json' } })
    })
  )
  return bodies
}

const ok = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })

function wrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

describe('useTwitchLiveCounts', () => {
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('asks once for all the games of the page and hands out the counts', async () => {
    const bodies = stub({ hasTwitchKey: true, twitchStreamsEnabled: true }, () =>
      ok({ counts: [{ appId: 620, count: 12, atLeast: false }, { appId: 70, count: 300, atLeast: true }], pending: [] })
    )
    const { result } = renderHook(() => useTwitchLiveCounts(GAMES), { wrapper: wrapper() })

    await waitFor(() => expect(result.current.get(620)).toEqual({ count: 12, atLeast: false }))
    expect(result.current.get(70)).toEqual({ count: 300, atLeast: true })
    expect(bodies).toHaveLength(1)
    expect(bodies[0]).toEqual({ games: GAMES })
  })

  it('asks nothing without Twitch credentials or with the switch off', async () => {
    const bodies = stub({ hasTwitchKey: false, twitchStreamsEnabled: true }, () => ok({ counts: [], pending: [] }))
    const { result } = renderHook(() => useTwitchLiveCounts(GAMES), { wrapper: wrapper() })
    await new Promise((resolve) => setTimeout(resolve, 150))
    expect(result.current.size).toBe(0)
    expect(bodies).toHaveLength(0)
    cleanup()

    const off = stub({ hasTwitchKey: true, twitchStreamsEnabled: false }, () => ok({ counts: [], pending: [] }))
    renderHook(() => useTwitchLiveCounts(GAMES), { wrapper: wrapper() })
    await new Promise((resolve) => setTimeout(resolve, 150))
    expect(off).toHaveLength(0)
  })

  it('shows no counts and no error when the answer fails', async () => {
    const bodies = stub({ hasTwitchKey: true, twitchStreamsEnabled: true }, () => new Response('{"title":"x","detail":"Twitch down"}', { status: 502, headers: { 'Content-Type': 'application/problem+json' } }))
    const { result } = renderHook(() => useTwitchLiveCounts(GAMES), { wrapper: wrapper() })
    await waitFor(() => expect(bodies).toHaveLength(1))
    expect(result.current.size).toBe(0)
  })

  it('asks again while the server reports games as pending', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    try {
      const bodies = stub({ hasTwitchKey: true, twitchStreamsEnabled: true }, (call) =>
        call === 0
          ? ok({ counts: [{ appId: 620, count: 4, atLeast: false }], pending: [70] })
          : ok({ counts: [{ appId: 620, count: 4, atLeast: false }, { appId: 70, count: 9, atLeast: false }], pending: [] })
      )
      const { result } = renderHook(() => useTwitchLiveCounts(GAMES), { wrapper: wrapper() })
      await waitFor(() => expect(result.current.get(620)?.count).toBe(4))
      expect(result.current.get(70)).toBeUndefined()

      await vi.advanceTimersByTimeAsync(3100)
      await waitFor(() => expect(result.current.get(70)?.count).toBe(9))
      expect(bodies).toHaveLength(2)

      // Nothing pending any more: it stops asking
      await vi.advanceTimersByTimeAsync(10_000)
      expect(bodies).toHaveLength(2)
    } finally {
      vi.useRealTimers()
    }
  })

  it('never sends more than a batch of games', async () => {
    const many = Array.from({ length: 150 }, (_, i) => ({ appId: i + 1, name: `Game ${i + 1}` }))
    const bodies = stub({ hasTwitchKey: true, twitchStreamsEnabled: true }, () => ok({ counts: [], pending: [] }))
    renderHook(() => useTwitchLiveCounts(many), { wrapper: wrapper() })
    await waitFor(() => expect(bodies).toHaveLength(1))
    expect((bodies[0] as { games: unknown[] }).games).toHaveLength(120)
  })
})
