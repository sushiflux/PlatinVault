import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { applyLanguage } from '../../i18n'
import type { GameDetailDto } from '../../api/types'
import { ConfirmProvider } from '../ui/ConfirmDialog'
import { ManualGameActions } from './ManualGameActions'

const GAME = { id: '0b8f4e3a-1111-4222-8333-444455556666', slug: 'stellar-blade', title: 'Stellar Blade', coverUrl: '', platform: 'playstation', platformVariant: 'ps5' } as GameDetailDto

let calls: { method: string; path: string }[]
let failDelete = false

function stubServer() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      calls.push({ method, path: url })
      if (method === 'DELETE' && url === `/api/games/${GAME.id}`) {
        return failDelete
          ? new Response(JSON.stringify({ detail: 'Die Datenbank ist gesperrt.' }), { status: 500, headers: { 'Content-Type': 'application/json' } })
          : new Response(null, { status: 204 })
      }
      return new Response(JSON.stringify({ slug: GAME.slug }), { status: 200, headers: { 'Content-Type': 'application/json' } })
    })
  )
}

function Where() {
  return <span data-testid="where">{useLocation().pathname}</span>
}

function renderActions(game: GameDetailDto = GAME) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/games/stellar-blade']}>
        <ConfirmProvider>
          <Where />
          <Routes>
            <Route path="*" element={<ManualGameActions game={game} />} />
          </Routes>
        </ConfirmProvider>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

beforeEach(async () => {
  await applyLanguage('de')
  calls = []
  failDelete = false
  stubServer()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('ManualGameActions: remove the game', () => {
  it('asks in our own dialog what is deleted, starts on "Abbrechen" and deletes nothing when cancelled', async () => {
    renderActions()
    fireEvent.click(screen.getByRole('button', { name: 'Spiel entfernen' }))
    const dialog = await screen.findByRole('alertdialog')
    expect(within(dialog).getByText('„Stellar Blade“ entfernen?')).toBeTruthy()
    // It names everything that goes with the game
    expect(within(dialog).getByText('der Guide mit allen Kapiteln')).toBeTruthy()
    expect(within(dialog).getByText('die Trophäenliste mit allen deinen Häkchen')).toBeTruthy()
    expect(within(dialog).getByText('das Titelbild')).toBeTruthy()
    expect(within(dialog).getByText(/lässt sich nicht rückgängig machen/)).toBeTruthy()
    // Destructive: focus on the harmless button
    expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: 'Abbrechen' }))
    expect(within(dialog).getByRole('button', { name: 'Spiel entfernen' }).className).toContain('pv-btn-danger')

    fireEvent.click(within(dialog).getByRole('button', { name: 'Abbrechen' }))
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    expect(calls.filter((c) => c.method === 'DELETE')).toHaveLength(0)
    expect(screen.getByTestId('where').textContent).toBe('/games/stellar-blade')
  })

  it('deletes the game by its id and goes to the overview', async () => {
    renderActions()
    fireEvent.click(screen.getByRole('button', { name: 'Spiel entfernen' }))
    const dialog = await screen.findByRole('alertdialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Spiel entfernen' }))
    await waitFor(() => expect(screen.getByTestId('where').textContent).toBe('/'))
    expect(calls.filter((c) => c.method === 'DELETE').map((c) => c.path)).toEqual([`/api/games/${GAME.id}`])
  })

  it('stays on the page and shows the complete reason with a copy button when deleting fails', async () => {
    failDelete = true
    renderActions()
    fireEvent.click(screen.getByRole('button', { name: 'Spiel entfernen' }))
    fireEvent.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Spiel entfernen' }))
    expect(await screen.findByText('Das Spiel konnte nicht entfernt werden')).toBeTruthy()
    expect(screen.getByText('Die Datenbank ist gesperrt.')).toBeTruthy()
    expect(screen.getByTestId('copy-error')).toBeTruthy()
    expect(screen.getByTestId('where').textContent).toBe('/games/stellar-blade')
  })
})

describe('ManualGameActions: change the cover', () => {
  it('uploads a new image with the same cover field as the add dialog', async () => {
    renderActions()
    fireEvent.click(screen.getByRole('button', { name: 'Cover ändern' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cover ändern' })
    const save = within(dialog).getByRole('button', { name: 'Cover speichern' }) as HTMLButtonElement
    expect(save.disabled).toBe(true)
    expect(within(dialog).getByText('Wähle zuerst ein Bild aus.')).toBeTruthy()

    fireEvent.drop(dialog.querySelector('.pv-cover-zone')!, { dataTransfer: { files: [new File([new Uint8Array(10)], 'neu.webp', { type: 'image/webp' })] } })
    await within(dialog).findByText('neu.webp')
    expect(save.disabled).toBe(false)
    fireEvent.click(save)
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Cover ändern' })).toBeNull())
    expect(calls.find((c) => c.method === 'PUT')?.path).toBe('/api/games/stellar-blade/cover')
  })

  it('takes an uploaded cover away again, but offers that only for an uploaded cover', async () => {
    renderActions({ ...GAME, coverUrl: 'https://images.igdb.com/x.jpg' })
    fireEvent.click(screen.getByRole('button', { name: 'Cover ändern' }))
    expect(within(await screen.findByRole('dialog', { name: 'Cover ändern' })).queryByRole('button', { name: 'Eigenes Cover entfernen' })).toBeNull()
    cleanup()

    renderActions({ ...GAME, coverUrl: `/api/covers/${GAME.id.replace(/-/g, '')}?v=1` })
    fireEvent.click(screen.getByRole('button', { name: 'Cover ändern' }))
    fireEvent.click(within(await screen.findByRole('dialog', { name: 'Cover ändern' })).getByRole('button', { name: 'Eigenes Cover entfernen' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Cover ändern' })).toBeNull())
    expect(calls.find((c) => c.method === 'DELETE')?.path).toBe('/api/games/stellar-blade/cover')
  })

  it('closes with Escape', async () => {
    renderActions()
    fireEvent.click(screen.getByRole('button', { name: 'Cover ändern' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cover ändern' })
    fireEvent.keyDown(dialog, { key: 'Escape' })
    expect(screen.queryByRole('dialog', { name: 'Cover ändern' })).toBeNull()
  })
})
