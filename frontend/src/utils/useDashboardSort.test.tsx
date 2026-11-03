import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useDashboardSort } from './useDashboardSort'

interface Call {
  method: string
  body: string
}

function stubServer(settings: Record<string, unknown>, failPut = false) {
  const calls: Call[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      calls.push({ method, body: String(init?.body ?? '') })
      if (method === 'PUT') {
        if (failPut) return new Response(JSON.stringify({ title: 'Ungültig', detail: 'Sortierung "x" ist ungültig.' }), { status: 400, headers: { 'Content-Type': 'application/problem+json' } })
        const patch = JSON.parse(String(init?.body)) as Record<string, unknown>
        Object.assign(settings, patch)
      }
      return new Response(JSON.stringify(settings), { status: 200, headers: { 'Content-Type': 'application/json' } })
    })
  )
  return calls
}

function wrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

describe('useDashboardSort', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('starts with the defaults of each view and then follows what the server has stored', async () => {
    stubServer({ dashboardSortFavorites: 'playtime:asc', dashboardSortAll: 'reviews:desc' })
    const { result } = renderHook(() => useDashboardSort(), { wrapper: wrapper() })
    expect(result.current.choiceFor('favorites')).toEqual({ order: 'custom', direction: 'asc' })
    expect(result.current.choiceFor('all')).toEqual({ order: 'lastPlayed', direction: 'desc' })

    await waitFor(() => expect(result.current.choiceFor('favorites')).toEqual({ order: 'playtime', direction: 'asc' }))
    expect(result.current.choiceFor('all')).toEqual({ order: 'reviews', direction: 'desc' })
  })

  it('shows a change at once and saves it with the normal settings PUT, one view at a time', async () => {
    const calls = stubServer({ dashboardSortFavorites: 'custom:asc', dashboardSortAll: 'lastPlayed:desc' })
    const { result } = renderHook(() => useDashboardSort(), { wrapper: wrapper() })
    await waitFor(() => expect(calls.some((call) => call.method === 'GET')).toBe(true))

    act(() => result.current.setChoice('all', { order: 'size', direction: 'asc' }))
    expect(result.current.choiceFor('all')).toEqual({ order: 'size', direction: 'asc' })
    expect(result.current.choiceFor('favorites')).toEqual({ order: 'custom', direction: 'asc' })

    await waitFor(() => expect(calls.filter((call) => call.method === 'PUT')).toHaveLength(1))
    // Only the field of this view: everything else stays as stored (all fields of the PUT are optional)
    expect(JSON.parse(calls.find((call) => call.method === 'PUT')!.body)).toEqual({ dashboardSortAll: 'size:asc' })
    await waitFor(() => expect(result.current.choiceFor('all')).toEqual({ order: 'size', direction: 'asc' }))
  })

  it('remembers the choice in the browser for the first paint', async () => {
    stubServer({})
    const { result } = renderHook(() => useDashboardSort(), { wrapper: wrapper() })
    act(() => result.current.setChoice('favorites', { order: 'release', direction: 'desc' }))
    await waitFor(() => expect(localStorage.getItem('pv_dashboard_sort_v1')).toContain('release:desc'))

    cleanup()
    // A new page before the settings answer: the remembered choice, not the default
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => undefined)))
    const again = renderHook(() => useDashboardSort(), { wrapper: wrapper() })
    expect(again.result.current.choiceFor('favorites')).toEqual({ order: 'release', direction: 'desc' })
  })

  it('goes back to what the server has and reports the reason when saving fails', async () => {
    stubServer({ dashboardSortFavorites: 'custom:asc', dashboardSortAll: 'lastPlayed:desc' }, true)
    const { result } = renderHook(() => useDashboardSort(), { wrapper: wrapper() })
    await waitFor(() => expect(result.current.choiceFor('all')).toEqual({ order: 'lastPlayed', direction: 'desc' }))

    act(() => result.current.setChoice('all', { order: 'playtime', direction: 'desc' }))
    await waitFor(() => expect(result.current.saveError).not.toBeNull())
    expect(result.current.choiceFor('all')).toEqual({ order: 'lastPlayed', direction: 'desc' })
    expect(String((result.current.saveError as Error).message)).toContain('ungültig')

    act(() => result.current.clearSaveError())
    expect(result.current.saveError).toBeNull()
  })
})
