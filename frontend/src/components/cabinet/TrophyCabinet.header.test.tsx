import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { TrophyCabinet } from './TrophyCabinet'

const data = vi.hoisted(() => ({
  showcase: {
    games: [
      {
        appId: 1,
        name: 'Celeste',
        coverUrl: null,
        heroBannerUrl: null,
        existingSlug: null,
        trophiesTotal: 40,
        platinumAt: '2026-11-13T22:02:32+00:00',
        firstUnlockAt: '2026-10-30T12:41:17+00:00',
        daysToPlatinum: 15,
        playtimeHours: 83.2,
        rarestName: null,
        rarestIconUrl: null,
        rarestPercent: null,
        averageRarityPercent: null,
      },
    ],
    totalTrophies: 65,
    totalPlaytimeHours: 89.3,
  },
}))
vi.mock('../../api/client', () => ({
  fetchShowcase: () => Promise.resolve(data.showcase),
  fetchCabinetPresented: () => Promise.resolve({ initialized: true, appIds: [1] }),
  addCabinetPresented: () => Promise.resolve({ initialized: true, appIds: [1] }),
}))

beforeEach(() => {
  window.localStorage.clear()
  // jsdom has no layout engine and no media queries: the cabinet only needs these to exist
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query, addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {} }))
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

function renderCabinet(onClose = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <TrophyCabinet open onClose={onClose} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return onClose
}

describe('trophy cabinet header', () => {
  it('shows the figures as plain numbers with their labels and no chips', async () => {
    renderCabinet()
    const stats = await screen.findByText('65')
    const header = stats.closest('header') as HTMLElement
    expect(header.className).toContain('pv-cab-head')
    const items = header.querySelectorAll('.pv-cab-stat')
    expect(items).toHaveLength(3)
    expect(within(header).getByText('Platin')).toBeTruthy()
    expect(within(header).getByText('Trophäen')).toBeTruthy()
    expect(within(header).getByText('Std.')).toBeTruthy()
  })

  it('puts size, view and doors into one connected bar and keeps the close button next to it', async () => {
    const onClose = renderCabinet()
    await screen.findByText('65')
    const header = document.querySelector('header.pv-cab-head') as HTMLElement
    const bar = header.querySelector('.pv-cab-bar') as HTMLElement
    expect(within(bar).getByRole('slider')).toBeTruthy()
    expect(within(bar).getByRole('button', { name: 'Ansicht' })).toBeTruthy()
    expect(bar.querySelectorAll('.pv-cab-hbtn')).toHaveLength(2)
    const close = within(header).getByRole('button', { name: 'Trophäenschrank schließen' })
    expect(bar.contains(close)).toBe(false)
    fireEvent.click(close)
    expect(onClose).toHaveBeenCalled()
  })

  it('uses our own tooltips, never a native title, on the header controls', async () => {
    renderCabinet()
    await screen.findByText('65')
    const header = document.querySelector('header.pv-cab-head') as HTMLElement
    expect(header.querySelectorAll('[title]')).toHaveLength(0)
  })

  it('opens the view popover with the theme-following switch (no pinned dark surface)', async () => {
    renderCabinet()
    await screen.findByText('65')
    fireEvent.click(screen.getByRole('button', { name: 'Ansicht' }))
    const menu = document.querySelector('.pv-cab-light-menu') as HTMLElement
    expect(within(menu).getAllByRole('radio')).toHaveLength(2)
    const toggle = within(menu).getByRole('switch')
    expect(toggle.getAttribute('data-surface')).toBeNull()
  })
})
