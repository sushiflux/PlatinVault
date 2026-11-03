import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { NavInfo } from './NavInfo'

const BRANDING = { productName: 'PlatinVault', company: 'SushiFlux', version: 'v2026.10.7 (12.345 β)' }

function stubServer() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => {
      if (String(url).includes('/api/branding')) return new Response(JSON.stringify(BRANDING), { status: 200, headers: { 'Content-Type': 'application/json' } })
      // The licence documents
      return new Response('# Lizenz', { status: 200, headers: { 'Content-Type': 'text/markdown' } })
    })
  )
}

function renderInfo(drawer = false, onOpenChange?: (open: boolean) => void) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <nav>
        <NavInfo drawer={drawer} onOpenChange={onOpenChange} />
      </nav>
    </QueryClientProvider>
  )
}

const symbol = () => screen.getByRole('button', { name: 'Info zu Version und Lizenzen' })

describe('NavInfo', () => {
  beforeEach(() => stubServer())
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('starts as one quiet symbol: named, closed, no card in the page', () => {
    renderInfo()
    expect(symbol().getAttribute('aria-expanded')).toBe('false')
    expect(symbol().getAttribute('aria-haspopup')).toBe('dialog')
    expect(symbol().getAttribute('aria-controls')).toBeNull()
    expect(symbol().getAttribute('title')).toBeNull()
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('opens on click with brand, version, credit, copyright and the licences button, and names the card', async () => {
    renderInfo()
    fireEvent.click(symbol())
    const card = await screen.findByRole('dialog', { name: 'Über PlatinVault' })
    expect(symbol().getAttribute('aria-expanded')).toBe('true')
    expect(symbol().getAttribute('aria-controls')).toBe(card.id)
    expect(card.textContent).toContain('PlatinVault')
    expect(card.textContent).toContain(`© ${new Date().getFullYear()} SushiFlux`)
    expect(card.textContent).toMatch(/Mit\s*gemacht/)
    // The card lives in <body>, not inside the nav that clips its own content
    expect(card.closest('nav')).toBeNull()
    await waitFor(() => expect(screen.getByText('v2026.10.7 (12.345 β)')).toBeTruthy())
    expect(within(card).getByRole('button', { name: 'Lizenzen' })).toBeTruthy()
  })

  it('closes with Esc, with a press elsewhere and with a second click, and reports the state to the nav', async () => {
    const onOpenChange = vi.fn()
    renderInfo(false, onOpenChange)
    fireEvent.click(symbol())
    await screen.findByRole('dialog')
    expect(onOpenChange).toHaveBeenLastCalledWith(true)

    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(onOpenChange).toHaveBeenLastCalledWith(false)

    fireEvent.click(symbol())
    await screen.findByRole('dialog')
    fireEvent.pointerDown(document.body)
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())

    fireEvent.click(symbol())
    await screen.findByRole('dialog')
    fireEvent.click(symbol())
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })

  it('a press inside the card does not close it', async () => {
    renderInfo()
    fireEvent.click(symbol())
    const card = await screen.findByRole('dialog')
    fireEvent.pointerDown(card)
    expect(screen.getByRole('dialog')).toBeTruthy()
  })

  it('hover opens it after a short delay and leaving closes it again after a short delay (mouse only)', async () => {
    vi.useFakeTimers()
    try {
      renderInfo()
      fireEvent.pointerEnter(symbol(), { pointerType: 'mouse' })
      expect(screen.queryByRole('dialog')).toBeNull()
      act(() => {
        vi.advanceTimersByTime(250)
      })
      expect(screen.getByRole('dialog')).toBeTruthy()
      fireEvent.pointerLeave(symbol(), { pointerType: 'mouse' })
      act(() => {
        vi.advanceTimersByTime(100)
      })
      expect(screen.getByRole('dialog')).toBeTruthy()
      act(() => {
        vi.advanceTimersByTime(400)
      })
      // The exit animation may still be running: the card is no longer the open one
      expect(symbol().getAttribute('aria-expanded')).toBe('false')
    } finally {
      vi.useRealTimers()
    }
  })

  it('a touch pointer does not open it by hovering', () => {
    vi.useFakeTimers()
    try {
      renderInfo()
      fireEvent.pointerEnter(symbol(), { pointerType: 'touch' })
      act(() => {
        vi.advanceTimersByTime(500)
      })
      expect(screen.queryByRole('dialog')).toBeNull()
    } finally {
      vi.useRealTimers()
    }
  })

  it('"Lizenzen" opens the licence viewer and the card steps aside', async () => {
    const onOpenChange = vi.fn()
    renderInfo(false, onOpenChange)
    fireEvent.click(symbol())
    const card = await screen.findByRole('dialog', { name: 'Über PlatinVault' })
    fireEvent.click(within(card).getByRole('button', { name: 'Lizenzen' }))
    // The viewer is a dialog titled "Lizenzen"
    await screen.findByRole('dialog', { name: 'Lizenzen' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Über PlatinVault' })).toBeNull())
    expect(symbol().getAttribute('aria-expanded')).toBe('false')
    // While the viewer is up the nav is told that an overlay is open
    expect(onOpenChange).toHaveBeenLastCalledWith(true)
  })
})

