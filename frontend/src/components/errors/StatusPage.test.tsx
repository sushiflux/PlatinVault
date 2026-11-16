import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import i18n from '../../i18n'
import { StatusPage } from './StatusPage'
import { CrashPage } from './ErrorBoundary'

function renderPage(ui: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ productName: 'PlatinVault', tagline: '', company: 'x', version: 'v1.2.3' }), { status: 200 })))
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>
  )
}

describe('status pages and the crash page are always English', () => {
  beforeEach(async () => {
    // A German interface must not leak into these pages
    await i18n.changeLanguage('de')
  })
  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
    Reflect.deleteProperty(navigator, 'clipboard')
  })

  it.each([401, 403, 404, 429, 500, 503, 'offline'] as const)('renders %s with code, title, text and the copy button in English', (kind) => {
    renderPage(<StatusPage kind={kind} request="GET /somewhere" />)
    const page = screen.getByTestId('status-page')
    expect(page.getAttribute('lang')).toBe('en')
    expect(page.querySelector('.pv-status-code')?.textContent).toBe(kind === 'offline' ? 'Offline' : String(kind))
    expect(page.querySelector('h1')?.textContent).not.toMatch(/nicht|Seite|Zugriff/)
    expect(screen.getByRole('button', { name: /Copy error message/i })).toBeTruthy()
    expect(screen.getByText('Copy details')).toBeTruthy()
    expect(screen.getByText('Go to the overview')).toBeTruthy()
  })

  it('offers "Try again" when a retry makes sense and copies code, request and version', async () => {
    const writeText = vi.fn(async () => {})
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    const onRetry = vi.fn()
    renderPage(<StatusPage kind={503} request="GET /api/games" onRetry={onRetry} />)

    fireEvent.click(screen.getByText('Try again'))
    expect(onRetry).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getByRole('button', { name: /Copy error message/i }))
    await waitFor(() => expect(writeText).toHaveBeenCalled())
    const copied = (writeText.mock.calls[0] as unknown as [string])[0]
    expect(copied).toContain('Server unavailable')
    expect(copied).toContain('GET /api/games')
    expect(copied).toContain('Error code: 503')
  })

  it('the crash page is English too: concrete message, folded technical details, reload and copy', () => {
    const error = new TypeError('Cannot read properties of undefined')
    render(<CrashPage error={error} componentStack={'\n    at GameList'} />)
    expect(screen.getByText('An unexpected error occurred')).toBeTruthy()
    expect(screen.getByText('Cannot read properties of undefined')).toBeTruthy()
    expect(screen.getByText('Reload page')).toBeTruthy()
    expect(screen.getByText('Copy details')).toBeTruthy()
    const toggle = screen.getByRole('button', { name: 'Technical details' })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    fireEvent.click(toggle)
    expect(document.getElementById('pv-crash-tech')?.textContent).toContain('at GameList')
  })
})
