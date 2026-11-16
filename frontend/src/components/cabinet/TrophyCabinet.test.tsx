import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ShowcaseDto, ShowcaseGameDto } from '../../api/types'
import { applyLanguage } from '../../i18n'
import { TrophyCabinet } from './TrophyCabinet'

const games: ShowcaseGameDto[] = [1, 2, 3].map((n) => ({
  appId: n,
  name: `Spiel ${n}`,
  coverUrl: null,
  heroBannerUrl: null,
  existingSlug: null,
  trophiesTotal: 10 * n,
  platinumAt: new Date(2026, 10, n, 20, 5).toISOString(),
  firstUnlockAt: null,
  daysToPlatinum: n,
  playtimeHours: 5,
  rarestName: null,
  rarestIconUrl: null,
  rarestPercent: null,
  averageRarityPercent: null,
}))
const showcase: ShowcaseDto = { games, totalTrophies: 60, totalPlaytimeHours: 15 }

// The cabinet only reads and (best effort) reports presented games: no network, everything already presented
vi.mock('../../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/client')>()),
  fetchShowcase: vi.fn(async () => showcase),
  fetchCabinetPresented: vi.fn(async () => ({ initialized: true, appIds: [1, 2, 3] })),
  addCabinetPresented: vi.fn(async () => ({ initialized: true, appIds: [1, 2, 3] })),
}))

class FakeResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}

// jsdom has no media queries: reduced motion decides whether the doors slide (they never "finish" here, there are no CSS animations)
function stubMedia(reduceMotion: boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: reduceMotion && query.includes('prefers-reduced-motion'),
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
  }))
}

function renderCabinet() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <TrophyCabinet />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const cabinet = (container: HTMLElement) => container.querySelector<HTMLElement>('.pv-cab')!

beforeEach(async () => {
  localStorage.clear()
  vi.stubGlobal('ResizeObserver', FakeResizeObserver)
  stubMedia(true)
  await applyLanguage('de')
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})
afterAll(async () => {
  await applyLanguage('de')
})

describe('trophy cabinet: shelf size', () => {
  it('starts at "Klein" when no size was chosen', async () => {
    const { container } = renderCabinet()
    await waitFor(() => expect(cabinet(container).getAttribute('data-ready')).toBe('on'))
    expect(cabinet(container).getAttribute('data-size')).toBe('1')
    const slider = screen.getByRole('slider', { name: 'Größe im Schrank' })
    expect((slider as HTMLInputElement).value).toBe('1')
    expect(slider.getAttribute('aria-valuetext')).toBe('Klein')
  })

  it('keeps a size the user chose earlier', async () => {
    localStorage.setItem('pv_cabinet_size', '3')
    const { container } = renderCabinet()
    await waitFor(() => expect(cabinet(container).getAttribute('data-ready')).toBe('on'))
    expect(cabinet(container).getAttribute('data-size')).toBe('3')
  })
})

describe('trophy cabinet: lighting choice', () => {
  it('is one connected bar with Spots chosen by default', async () => {
    const { container } = renderCabinet()
    await waitFor(() => expect(cabinet(container).getAttribute('data-ready')).toBe('on'))
    const group = screen.getByRole('group', { name: 'Beleuchtung' })
    expect(group.classList.contains('pv-segmented')).toBe(true)
    expect(screen.getByRole('button', { name: 'Spots' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByRole('button', { name: 'LED' }).getAttribute('aria-pressed')).toBe('false')
    expect(cabinet(container).getAttribute('data-lighting')).toBe('spots')
    expect(localStorage.getItem('pv_cabinet_lighting')).toBeNull()
  })

  it('switches to LED, remembers it and offers it again next time', async () => {
    const first = renderCabinet()
    await waitFor(() => expect(cabinet(first.container).getAttribute('data-ready')).toBe('on'))
    fireEvent.click(screen.getByRole('button', { name: 'LED' }))
    expect(cabinet(first.container).getAttribute('data-lighting')).toBe('led')
    expect(screen.getByRole('button', { name: 'LED' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByRole('button', { name: 'Spots' }).getAttribute('aria-pressed')).toBe('false')
    expect(localStorage.getItem('pv_cabinet_lighting')).toBe('led')
    first.unmount()

    const second = renderCabinet()
    await waitFor(() => expect(cabinet(second.container).getAttribute('data-ready')).toBe('on'))
    expect(cabinet(second.container).getAttribute('data-lighting')).toBe('led')
  })

  it('ignores an unknown stored value', async () => {
    localStorage.setItem('pv_cabinet_lighting', 'disco')
    const { container } = renderCabinet()
    await waitFor(() => expect(cabinet(container).getAttribute('data-lighting')).toBe('spots'))
  })

  it('shows one strip per shelf for LED and one lamp per game for spots', async () => {
    const { container } = renderCabinet()
    await waitFor(() => expect(cabinet(container).getAttribute('data-ready')).toBe('on'))
    expect(container.querySelectorAll('.pv-cab-slot[data-app-id] > .pv-cab-lamp').length).toBe(games.length)
    expect(container.querySelectorAll('.pv-cab-slot[data-app-id] > .pv-cab-beam').length).toBe(games.length)
    expect(container.querySelectorAll('.pv-cab-led').length).toBeGreaterThan(0)
  })
})

describe('trophy cabinet: glass doors', () => {
  // The sliding doors with real motion are in TrophyCabinet.motion.test.tsx (motion reads the media query once per file)
  it('opens by itself and the lights come on (reduced motion: no sliding)', async () => {
    const { container } = renderCabinet()
    await waitFor(() => expect(cabinet(container).getAttribute('data-doors')).toBe('open'))
    await waitFor(() => expect(cabinet(container).getAttribute('data-lights')).toBe('on'))
    expect(container.querySelectorAll('.pv-cab-door').length).toBe(2)
  })

  it('closes and opens with the button, the wall is inert while closed', async () => {
    const { container } = renderCabinet()
    await waitFor(() => expect(cabinet(container).getAttribute('data-lights')).toBe('on'))
    const button = screen.getByRole('button', { name: 'Türen geschlossen' })
    expect(button.getAttribute('aria-pressed')).toBe('false')
    await act(async () => {
      fireEvent.click(button)
    })
    await waitFor(() => expect(cabinet(container).getAttribute('data-doors')).toBe('closed'))
    expect(cabinet(container).getAttribute('data-lights')).toBe('off')
    expect(button.getAttribute('aria-pressed')).toBe('true')
    expect(container.querySelector('.pv-cab-scroll')!.hasAttribute('inert')).toBe(true)

    // A click on the closed glass opens them again
    await act(async () => {
      fireEvent.pointerDown(container.querySelector('.pv-cab-case')!)
    })
    await waitFor(() => expect(cabinet(container).getAttribute('data-doors')).toBe('open'))
    await waitFor(() => expect(cabinet(container).getAttribute('data-lights')).toBe('on'))
    expect(container.querySelector('.pv-cab-scroll')!.hasAttribute('inert')).toBe(false)
  })

})
