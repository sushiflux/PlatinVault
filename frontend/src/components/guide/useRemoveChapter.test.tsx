import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GameDetailDto } from '../../api/types'
import { ConfirmProvider } from '../ui/ConfirmDialog'
import { RemoveChapterBar } from './RemoveChapterBar'
import { useRemoveChapter, type RemovableChapter } from './useRemoveChapter'

const removeChapter = vi.hoisted(() => vi.fn<(slug: string, tabId: string) => Promise<GameDetailDto>>())
vi.mock('../../api/client', () => ({ removeChapter }))

const updatedGuide = { slug: 'factory', tabs: [] } as unknown as GameDetailDto

// The button and the flow exactly as the game page wires them
function Harness({ chapter, onResult, disabled = false }: { chapter: RemovableChapter; onResult: (r: GameDetailDto | null | Error) => void; disabled?: boolean }) {
  const remove = useRemoveChapter('factory')
  return <RemoveChapterBar disabled={disabled} onRemove={() => void remove(chapter).then(onResult, onResult)} />
}

function setup(chapter: RemovableChapter, onResult = vi.fn(), disabled = false) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const invalidate = vi.spyOn(client, 'invalidateQueries')
  render(
    <QueryClientProvider client={client}>
      <ConfirmProvider>
        <Harness chapter={chapter} onResult={onResult} disabled={disabled} />
      </ConfirmProvider>
    </QueryClientProvider>
  )
  return { client, invalidate, onResult }
}

beforeEach(() => {
  removeChapter.mockReset()
})
afterEach(cleanup)

describe('Kapitel entfernen', () => {
  it('asks first, says exactly what goes, and starts on "Abbrechen"', async () => {
    setup({ id: 'tab-1', title: 'Gegner, Bosse & Bestiarium' })

    fireEvent.click(screen.getByRole('button', { name: 'Kapitel entfernen' }))

    const dialog = await screen.findByRole('alertdialog')
    expect(dialog.textContent).toContain('Kapitel „Gegner, Bosse & Bestiarium“ wirklich entfernen?')
    expect(dialog.textContent).toContain('seine Übersetzung')
    expect(dialog.textContent).toContain('Häkchen')
    expect(dialog.textContent).toContain('Steam-Erfolge')
    expect(dialog.textContent).toContain('nicht rückgängig')
    expect(dialog.textContent).toContain('Guide vervollständigen')
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Abbrechen' }))
    expect(removeChapter).not.toHaveBeenCalled()
  })

  it('sends nothing when the question is cancelled', async () => {
    const { onResult } = setup({ id: 'tab-1', title: 'Bosse' })

    fireEvent.click(screen.getByRole('button', { name: 'Kapitel entfernen' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Abbrechen' }))

    await waitFor(() => expect(onResult).toHaveBeenCalledWith(null))
    expect(removeChapter).not.toHaveBeenCalled()
  })

  it('sends the DELETE after the confirmation and hands the refreshed guide to the cache', async () => {
    removeChapter.mockResolvedValue(updatedGuide)
    const { client, invalidate, onResult } = setup({ id: 'tab-1', title: 'Bosse' })

    fireEvent.click(screen.getByRole('button', { name: 'Kapitel entfernen' }))
    const dialog = await screen.findByRole('alertdialog')
    fireEvent.click(Array.from(dialog.querySelectorAll('button')).find((b) => b.textContent === 'Kapitel entfernen')!)

    await waitFor(() => expect(onResult).toHaveBeenCalledWith(updatedGuide))
    expect(removeChapter).toHaveBeenCalledExactlyOnceWith('factory', 'tab-1')
    expect(client.getQueryData(['game', 'factory'])).toBe(updatedGuide)
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['games'] })
  })

  it('reports a failed request to the caller instead of swallowing it', async () => {
    const failure = new Error('Dieses Kapitel gibt es in diesem Guide nicht (mehr).')
    removeChapter.mockImplementation(async () => {
      throw failure
    })
    const { client, onResult } = setup({ id: 'tab-1', title: 'Bosse' })

    fireEvent.click(screen.getByRole('button', { name: 'Kapitel entfernen' }))
    const dialog = await screen.findByRole('alertdialog')
    fireEvent.click(Array.from(dialog.querySelectorAll('button')).find((b) => b.textContent === 'Kapitel entfernen')!)

    await waitFor(() => expect(onResult).toHaveBeenCalledTimes(1))
    expect((onResult.mock.calls[0]![0] as Error).message).toBe(failure.message)
    expect(client.getQueryData(['game', 'factory'])).toBeUndefined()
  })

  it('uses the shorter question for a note of a failed research', async () => {
    setup({ id: 'tab-2', title: 'Waffen', placeholder: true })

    fireEvent.click(screen.getByRole('button', { name: 'Kapitel entfernen' }))

    const dialog = await screen.findByRole('alertdialog')
    expect(dialog.textContent).toContain('Kapitel „Waffen“ entfernen?')
    expect(dialog.textContent).toContain('es geht kein Inhalt verloren')
  })

  it('waits while an AI action changes the guide', () => {
    setup({ id: 'tab-1', title: 'Bosse' }, vi.fn(), true)

    expect((screen.getByRole('button', { name: 'Kapitel entfernen' }) as HTMLButtonElement).disabled).toBe(true)
  })
})
