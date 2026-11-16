import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, fireEvent, render, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ShowcaseDto } from '../../api/types'
import { TrophyCabinet } from './TrophyCabinet'

// With real motion (no reduced-motion preference) the cabinet starts closed and dark, the doors slide open and only then do the
// lights come on. jsdom runs no CSS animations, so the end of the sliding is signalled by hand (animationend on the right pane).
const showcase: ShowcaseDto = {
  games: [
    {
      appId: 1,
      name: 'Spiel 1',
      coverUrl: null,
      heroBannerUrl: null,
      existingSlug: null,
      trophiesTotal: 10,
      platinumAt: new Date(2026, 10, 1, 20, 5).toISOString(),
      firstUnlockAt: null,
      daysToPlatinum: 1,
      playtimeHours: 5,
      rarestName: null,
      rarestIconUrl: null,
      rarestPercent: null,
      averageRarityPercent: null,
    },
  ],
  totalTrophies: 10,
  totalPlaytimeHours: 5,
}

vi.mock('../../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/client')>()),
  fetchShowcase: vi.fn(async () => showcase),
  fetchCabinetPresented: vi.fn(async () => ({ initialized: true, appIds: [1] })),
  addCabinetPresented: vi.fn(async () => ({ initialized: true, appIds: [1] })),
}))

class FakeResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}

beforeEach(() => {
  localStorage.clear()
  vi.stubGlobal('ResizeObserver', FakeResizeObserver)
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
  }))
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('trophy cabinet: doors with motion', () => {
  it('starts closed and dark, slides open, then the lights flicker on and settle', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container } = render(
      <QueryClientProvider client={client}>
        <MemoryRouter>
          <TrophyCabinet />
        </MemoryRouter>
      </QueryClientProvider>
    )
    const cab = () => container.querySelector<HTMLElement>('.pv-cab')!
    await waitFor(() => expect(cab().getAttribute('data-doors')).toBe('opening'))
    expect(cab().getAttribute('data-lights')).toBe('off')

    // The right pane finishes its slide: doors open, the lights start (flicker), later they are steady
    // (jsdom has no AnimationEvent, and React listens to the prefixed name there: the event carries its animation name by hand)
    await act(async () => {
      for (const type of ['animationend', 'webkitAnimationEnd']) {
        const end = new Event(type, { bubbles: true })
        Object.defineProperty(end, 'animationName', { value: 'pv-door-r' })
        container.querySelector('.pv-cab-door--r')!.dispatchEvent(end)
      }
    })
    await waitFor(() => expect(cab().getAttribute('data-doors')).toBe('open'))
    await waitFor(() => expect(cab().getAttribute('data-lights')).toBe('flicker'))
    await waitFor(() => expect(cab().getAttribute('data-lights')).toBe('on'), { timeout: 3000 })
  })

  it('a click on the case while it opens skips to the finished, lit state', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container } = render(
      <QueryClientProvider client={client}>
        <MemoryRouter>
          <TrophyCabinet />
        </MemoryRouter>
      </QueryClientProvider>
    )
    const cab = () => container.querySelector<HTMLElement>('.pv-cab')!
    await waitFor(() => expect(cab().getAttribute('data-doors')).toBe('opening'))
    fireEvent.pointerDown(container.querySelector('.pv-cab-case')!)
    await waitFor(() => expect(cab().getAttribute('data-doors')).toBe('open'))
    expect(cab().getAttribute('data-lights')).toBe('on')
  })
})
