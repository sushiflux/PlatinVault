import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AppCopyright, AppVersion, LicensesButton } from './FooterParts'

function stubBranding(body: Record<string, unknown>, delayMs = 0) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => {
      if (delayMs) await new Promise((resolve) => setTimeout(resolve, delayMs))
      return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
    })
  )
}

function renderWithClient(ui: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>)
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('AppVersion', () => {
  it.each([
    ['release', 'v2026.11.12 (13.557)'],
    ['beta', 'v2026.11.12 (13.557 β)'],
    ['dev', 'v2026.11.12 (13.557-dev)'],
  ])('shows the %s version exactly as the server wrote it, as plain mono text', async (_kind, version) => {
    stubBranding({ productName: 'PlatinVault', company: 'SushiFlux', version })
    renderWithClient(<AppVersion className="my-class" />)
    const element = await screen.findByText(version)
    expect(element.className).toContain('font-mono')
    expect(element.className).toContain('my-class')
    // Plain text: nothing to focus, no tooltip, no native title
    expect(element.getAttribute('tabindex')).toBeNull()
    expect(element.getAttribute('title')).toBeNull()
  })

  it('reserves its line while the answer is on its way: invisible, hidden from screen readers, never empty', async () => {
    stubBranding({ productName: 'PlatinVault', company: 'SushiFlux', version: 'v2026.11.12 (13.557 β)' }, 30)
    const { container } = renderWithClient(<AppVersion />)
    const placeholder = container.querySelector('span.invisible')
    expect(placeholder).not.toBeNull()
    expect(placeholder?.getAttribute('aria-hidden')).toBe('true')
    expect((placeholder?.textContent ?? '').length).toBeGreaterThan(5)
    await waitFor(() => expect(container.querySelector('span.invisible')).toBeNull())
    expect(screen.getByText('v2026.11.12 (13.557 β)')).toBeTruthy()
  })
})

describe('AppCopyright', () => {
  it('names the year of today and the company from the branding', async () => {
    stubBranding({ productName: 'PlatinVault', company: 'SushiFlux', version: 'v1' })
    renderWithClient(<AppCopyright />)
    expect(await screen.findByText(`© ${new Date().getFullYear()} SushiFlux`)).toBeTruthy()
  })
})

describe('LicensesButton', () => {
  it('is named "Lizenzen" for screen readers even when it shows an icon, and runs the caller on click', () => {
    const onClick = vi.fn()
    render(
      <LicensesButton onClick={onClick}>
        <svg aria-hidden="true" />
      </LicensesButton>
    )
    const button = screen.getByRole('button', { name: 'Lizenzen' })
    expect(button.getAttribute('aria-haspopup')).toBe('dialog')
    fireEvent.click(button)
    expect(onClick).toHaveBeenCalledTimes(1)
  })
})
