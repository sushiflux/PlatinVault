import { renderHook, waitFor, cleanup } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GameDetailDto, GuideTranslationDto } from '../api/types'
import { useGuideTranslation } from './useGuideTranslation'

const client = vi.hoisted(() => ({ startGuideTranslation: vi.fn(), fetchGuideTranslation: vi.fn() }))
vi.mock('../api/client', () => client)

const dto = (patch: Partial<GuideTranslationDto> = {}): GuideTranslationDto => ({
  language: 'en',
  sourceLanguage: 'de',
  state: 'missing',
  totalTabs: 4,
  translatedTabs: 0,
  running: false,
  error: null,
  providerMessage: null,
  ...patch,
})
const game = (translation: GuideTranslationDto) => ({ slug: 'g', translation, language: 'de', contentLanguage: 'de' }) as unknown as GameDetailDto

let queryClient: QueryClient
const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>

beforeEach(() => {
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  client.startGuideTranslation.mockReset()
  client.fetchGuideTranslation.mockReset()
})
afterEach(cleanup)

describe('useGuideTranslation', () => {
  it('asks the server to translate a guide whose translation is missing, once, and reports progress', async () => {
    client.startGuideTranslation.mockResolvedValue(dto({ running: true, translatedTabs: 1 }))
    client.fetchGuideTranslation.mockResolvedValue(dto({ running: true, translatedTabs: 2 }))
    const { result, rerender } = renderHook(() => useGuideTranslation('g', game(dto()), 'en'), { wrapper })

    await waitFor(() => expect(result.current.phase).toBe('running'))
    expect(client.startGuideTranslation).toHaveBeenCalledTimes(1)
    expect(client.startGuideTranslation).toHaveBeenCalledWith('g', false)
    await waitFor(() => expect(result.current.translated).toBeGreaterThanOrEqual(1))
    expect(result.current.total).toBe(4)

    rerender()
    expect(client.startGuideTranslation).toHaveBeenCalledTimes(1)
  })

  it('reloads the game when the translation is complete', async () => {
    client.startGuideTranslation.mockResolvedValue(dto({ running: true }))
    client.fetchGuideTranslation.mockResolvedValue(dto({ state: 'complete', translatedTabs: 4 }))
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    renderHook(() => useGuideTranslation('g', game(dto()), 'en'), { wrapper })

    await waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: ['game', 'g'] }))
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['games'] })
  })

  it('shows a failure with its reason and the provider text, keeps it, and "try again" forces a new attempt', async () => {
    const failed = dto({ error: 'Gemini (Google): Kontingent erschöpft.', providerMessage: 'You exceeded your current quota' })
    client.startGuideTranslation.mockResolvedValue(failed)
    client.fetchGuideTranslation.mockResolvedValue(failed)
    const { result } = renderHook(() => useGuideTranslation('g', game(dto()), 'en'), { wrapper })

    await waitFor(() => expect(result.current.phase).toBe('failed'))
    expect(result.current.error).toEqual({ message: 'Gemini (Google): Kontingent erschöpft.', details: 'You exceeded your current quota' })
    expect(client.startGuideTranslation).toHaveBeenCalledTimes(1)

    client.startGuideTranslation.mockResolvedValue(dto({ running: true }))
    result.current.retry()
    await waitFor(() => expect(client.startGuideTranslation).toHaveBeenCalledWith('g', true))
  })

  it('translates again in the same visit when new original chapters arrive after a finished translation', async () => {
    client.startGuideTranslation.mockResolvedValue(dto({ running: true }))
    client.fetchGuideTranslation.mockResolvedValue(dto({ running: true }))
    const { rerender } = renderHook(({ t }) => useGuideTranslation('g', game(t), 'en'), { wrapper, initialProps: { t: dto() } })
    await waitFor(() => expect(client.startGuideTranslation).toHaveBeenCalledTimes(1))

    // Done, then "complete guide" adds chapters in the original language: the game answer says "missing" again
    rerender({ t: dto({ state: 'complete', translatedTabs: 4 }) })
    rerender({ t: dto({ totalTabs: 6, translatedTabs: 4 }) })
    await waitFor(() => expect(client.startGuideTranslation).toHaveBeenCalledTimes(2))
    expect(client.startGuideTranslation).toHaveBeenLastCalledWith('g', false)
  })

  it('reports a failed start call (server not reachable) as a failure with the readable reason', async () => {
    client.startGuideTranslation.mockRejectedValue(new Error('Server nicht erreichbar'))
    const { result } = renderHook(() => useGuideTranslation('g', game(dto()), 'en'), { wrapper })
    await waitFor(() => expect(result.current.phase).toBe('failed'))
    expect(result.current.error?.message).toBe('Server nicht erreichbar')
  })

  it('does nothing for a guide that is translated, or already in the UI language', () => {
    const complete = renderHook(() => useGuideTranslation('g', game(dto({ state: 'complete', translatedTabs: 4 })), 'en'), { wrapper })
    const original = renderHook(() => useGuideTranslation('g', game(dto({ state: 'original', language: 'de' })), 'de'), { wrapper })
    expect(complete.result.current.phase).toBe('idle')
    expect(original.result.current.phase).toBe('idle')
    expect(client.startGuideTranslation).not.toHaveBeenCalled()
  })

  it('ignores a cached game answer that was made for the other language (right after a language switch)', () => {
    const { result } = renderHook(() => useGuideTranslation('g', game(dto({ language: 'de', state: 'original' })), 'en'), { wrapper })
    expect(result.current.phase).toBe('idle')
    expect(client.startGuideTranslation).not.toHaveBeenCalled()
  })

  it('fetches the game again when the UI language changes', () => {
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    const { rerender } = renderHook(({ lang }) => useGuideTranslation('g', game(dto({ state: 'original', language: lang })), lang), {
      wrapper,
      initialProps: { lang: 'de' },
    })
    expect(invalidate).not.toHaveBeenCalled()
    rerender({ lang: 'en' })
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['game', 'g'] })
  })
})
