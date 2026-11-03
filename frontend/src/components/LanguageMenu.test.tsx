import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { applyLanguage, currentLanguage } from '../i18n'
import { LanguageMenu } from './LanguageMenu'

function renderMenu() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <LanguageMenu />
    </QueryClientProvider>
  )
}

function stubServer(answer: () => Response) {
  const calls: { method: string; body: string }[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_url: string, init?: RequestInit) => {
      calls.push({ method: init?.method ?? 'GET', body: String(init?.body ?? '') })
      return answer()
    })
  )
  return calls
}

const button = () => screen.getByRole('button', { name: /^(Sprache|Language):/ })

describe('LanguageMenu', () => {
  beforeEach(async () => {
    await applyLanguage('de')
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('shows the language in use on the button (name for screen readers, code as badge)', () => {
    renderMenu()
    expect(button().getAttribute('aria-label')).toBe('Sprache: Deutsch')
    expect(button().textContent).toContain('DE')
    expect(button().getAttribute('aria-haspopup')).toBe('menu')
    expect(button().getAttribute('aria-expanded')).toBe('false')
  })

  it('opens a menu with a row per language, the one in use marked, and the focus on it', () => {
    renderMenu()
    fireEvent.click(button())
    const menu = screen.getByRole('menu')
    expect(menu.getAttribute('aria-label')).toBe('Sprache wählen')
    const items = screen.getAllByRole('menuitemradio')
    expect(items.map((item) => item.textContent)).toEqual(['Deutsch', 'English'])
    expect(items.map((item) => item.getAttribute('aria-checked'))).toEqual(['true', 'false'])
    expect(document.activeElement).toBe(items[0])
    expect(button().getAttribute('aria-expanded')).toBe('true')
  })

  it('moves with the arrow keys, closes with Escape and gives the focus back to the button', () => {
    renderMenu()
    fireEvent.click(button())
    const items = screen.getAllByRole('menuitemradio')
    fireEvent.keyDown(items[0]!, { key: 'ArrowDown' })
    expect(document.activeElement).toBe(items[1])
    fireEvent.keyDown(items[1]!, { key: 'ArrowDown' })
    expect(document.activeElement).toBe(items[0])
    fireEvent.keyDown(items[0]!, { key: 'End' })
    expect(document.activeElement).toBe(items[1])
    fireEvent.keyDown(items[1]!, { key: 'Escape' })
    expect(document.activeElement).toBe(button())
  })

  it('switches the language at once and saves { uiLanguage } on the server', async () => {
    const calls = stubServer(() => new Response(JSON.stringify({ uiLanguage: 'en', language: 'english' }), { status: 200 }))
    renderMenu()
    fireEvent.click(button())
    await act(async () => {
      fireEvent.click(screen.getByRole('menuitemradio', { name: 'English' }))
    })
    await waitFor(() => expect(calls.some((call) => call.method === 'PUT')).toBe(true))
    expect(JSON.parse(calls.find((call) => call.method === 'PUT')!.body)).toEqual({ uiLanguage: 'en' })
    expect(currentLanguage()).toBe('en')
    expect(button().getAttribute('aria-label')).toBe('Language: English')
    expect(button().textContent).toContain('EN')
  })

  it('names the reason in a notice and keeps the previous language when saving fails', async () => {
    stubServer(() => new Response(JSON.stringify({ title: 'Einstellung ungültig', detail: 'Datenbank nicht erreichbar' }), { status: 400 }))
    renderMenu()
    fireEvent.click(button())
    await act(async () => {
      fireEvent.click(screen.getByRole('menuitemradio', { name: 'English' }))
    })
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('Die Sprache konnte nicht gespeichert werden')
    expect(alert.textContent).toContain('Datenbank nicht erreichbar')
    expect(currentLanguage()).toBe('de')
  })
})
