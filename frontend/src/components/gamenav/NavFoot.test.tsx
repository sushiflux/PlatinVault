import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { NavFoot } from './NavFoot'

const BRANDING = { productName: 'PlatinVault', company: 'SushiFlux', version: 'v2026.10.7 (12.345 β)' }

function stubServer() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string) => {
      if (String(url).includes('/api/branding')) return new Response(JSON.stringify(BRANDING), { status: 200, headers: { 'Content-Type': 'application/json' } })
      return new Response('# Lizenz', { status: 200, headers: { 'Content-Type': 'text/markdown' } })
    })
  )
}

function renderFoot(props: { drawer?: boolean; collapsed?: boolean; onOpenChange?: (open: boolean) => void }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <nav>
        <NavFoot drawer={props.drawer ?? false} collapsed={props.collapsed ?? false} onOpenChange={props.onOpenChange} />
      </nav>
    </QueryClientProvider>
  )
}

describe('NavFoot', () => {
  beforeEach(() => stubServer())
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('always renders the slim info symbol row in the expanded nav, collapsed nav and phone drawer', () => {
    for (const props of [{}, { collapsed: true }, { drawer: true }]) {
      const { container } = renderFoot(props)
      expect(screen.getByRole('button', { name: 'Info zu Version und Lizenzen' })).toBeTruthy()
      expect(screen.queryByRole('button', { name: 'Lizenzen' })).toBeNull()
      expect(container.querySelector('.pv-gnav-foot')).not.toBeNull()
      expect(container.querySelector('.pv-gnav-foot--block')).toBeNull()
      cleanup()
    }
  })

  it('opens the floating info card with logo, name, version button, credit, copyright and licences, and reports to the nav', async () => {
    const onOpenChange = vi.fn()
    renderFoot({ onOpenChange })
    fireEvent.click(screen.getByRole('button', { name: 'Info zu Version und Lizenzen' }))
    const card = await screen.findByRole('dialog', { name: 'Über PlatinVault' })
    expect(onOpenChange).toHaveBeenLastCalledWith(true)
    expect(card.querySelector('img')).not.toBeNull()
    expect(within(card).getByText('PlatinVault')).toBeTruthy()
    expect(await within(card).findByText('v2026.10.7 (12.345 β)')).toBeTruthy()
    expect(within(card).getByTestId('version-check')).toBeTruthy()
    expect(within(card).getByText(`© ${new Date().getFullYear()} SushiFlux`)).toBeTruthy()
    expect(card.textContent).toMatch(/Mit\s*gemacht/)

    fireEvent.click(within(card).getByRole('button', { name: 'Lizenzen' }))
    const viewer = await screen.findByRole('dialog', { name: 'Lizenzen' })
    expect(within(viewer).getByRole('heading', { name: 'Lizenzen' })).toBeTruthy()
    expect(onOpenChange).toHaveBeenLastCalledWith(true)
    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(onOpenChange).toHaveBeenLastCalledWith(false))
  })
})
