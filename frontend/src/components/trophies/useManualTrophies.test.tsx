import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { ApiError } from '../../api/client'
import { applyLanguage } from '../../i18n'
import type { ManualTrophyListDto } from '../../api/types'
import { applyTick, factsMissing, hasRarityShares, tierFiltersOf } from './trophyList'
import { manualItem, manualList, ps5Items } from './trophies.fixtures'
import { manualTrophiesKey, useManualTrophies } from './useManualTrophies'

const client = vi.hoisted(() => ({ fetchGameTrophies: vi.fn(), setTrophyUnlocked: vi.fn(), startGuideTranslation: vi.fn() }))
vi.mock('../../api/client', async (importOriginal) => ({ ...(await importOriginal<typeof import('../../api/client')>()), ...client }))

let queryClient: QueryClient
const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>

beforeEach(async () => {
  await applyLanguage('de')
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  Object.values(client).forEach((fn) => fn.mockReset())
})
afterEach(cleanup)

const useIt = (onTickError = vi.fn(), language = 'de') =>
  renderHook(() => useManualTrophies({ slug: 'stellar-blade', enabled: true, language, onTickError }), { wrapper })

const goldOf = (list: ManualTrophyListDto | undefined) => list?.achievements.find((a) => a.apiName === 'gold-1')

describe('applyTick (the optimistic state)', () => {
  it('ticks one trophy and recomputes the counts, the tier figures and the progress', () => {
    const list = manualList(ps5Items())
    const ticked = applyTick(list, 'gold-1', true, '2026-11-19T12:00:00Z')
    expect(goldOf(ticked)).toMatchObject({ achieved: true, unlockTime: '2026-11-19T12:00:00Z' })
    expect(ticked.achievedCount).toBe(2)
    expect(ticked.remainingCount).toBe(2)
    expect(ticked.progressPercentage).toBe(50)
    expect(ticked.gold).toEqual({ achieved: 1, total: 1 })
    expect(ticked.latestAchievement?.apiName).toBe('gold-1')
    // The original is untouched
    expect(goldOf(list)?.achieved).toBe(false)
  })

  it('un-ticks and reaches 100 % only when everything is ticked', () => {
    const all = manualList(ps5Items().map((item) => ({ ...item, achieved: true, unlockTime: '2026-11-19T11:00:00Z' })))
    expect(all.is100Percent).toBe(true)
    const one = applyTick(all, 'bronze-1', false, 'x')
    expect(one.is100Percent).toBe(false)
    expect(one.achievedCount).toBe(3)
    expect(one.achievements.find((a) => a.apiName === 'bronze-1')?.unlockTime).toBeNull()
  })

  it('returns the list unchanged for an unknown key', () => {
    const list = manualList(ps5Items())
    expect(applyTick(list, 'nope', true, 'x')).toBe(list)
  })
})

describe('trophy list helpers', () => {
  it('offers Platinum as a tier filter only for a list that has a platinum trophy', () => {
    expect(tierFiltersOf(manualList(ps5Items()))).toEqual(['Platinum', 'Gold', 'Silver', 'Bronze'])
    expect(tierFiltersOf(manualList([manualItem('a')], {}, 'xbox', null))).toEqual(['Gold', 'Silver', 'Bronze'])
    expect(tierFiltersOf(undefined)).toEqual(['Gold', 'Silver', 'Bronze'])
  })

  it('sees whether any trophy states a share', () => {
    expect(hasRarityShares(manualList(ps5Items()))).toBe(false)
    expect(hasRarityShares(manualList([manualItem('a', { hasGlobalPercent: true, globalPercent: 3 })]))).toBe(true)
  })

  it('says when the facts of a game are missing: nothing known and not from IGDB', () => {
    expect(factsMissing({ factsSource: null })).toBe(true)
    expect(factsMissing({ factsSource: 'ai' })).toBe(true)
    expect(factsMissing({ factsSource: 'ai', summary: 'Ein Spiel.' })).toBe(false)
    expect(factsMissing({ factsSource: 'igdb' })).toBe(false)
  })
})

describe('useManualTrophies: the check', () => {
  it('shows the tick at once, marks the trophy busy until the server answers, then takes the answer', async () => {
    const list = manualList(ps5Items())
    client.fetchGameTrophies.mockResolvedValue(list)
    let answer!: (value: ManualTrophyListDto) => void
    client.setTrophyUnlocked.mockReturnValue(new Promise<ManualTrophyListDto>((resolve) => (answer = resolve)))
    const { result } = useIt()
    await waitFor(() => expect(result.current.list).toBeTruthy())

    const check = result.current.checkFor(goldOf(result.current.list)!)!
    expect(check).toMatchObject({ checked: false, busy: false })
    act(() => check.onToggle())

    // Optimistic: ticked and counted before the answer; busy while it is on its way
    await waitFor(() => expect(goldOf(result.current.list)?.achieved).toBe(true))
    expect(result.current.list?.achievedCount).toBe(2)
    expect(result.current.checkFor(goldOf(result.current.list)!)).toMatchObject({ checked: true, busy: true })
    expect(client.setTrophyUnlocked).toHaveBeenCalledWith('stellar-blade', 'gold-1', true)

    // A second click on the same trophy while it waits does nothing
    act(() => result.current.toggle('gold-1', false))
    expect(client.setTrophyUnlocked).toHaveBeenCalledTimes(1)

    // The server's list wins (it also ticked another trophy, e.g. the platinum one)
    const served = applyTick(applyTick(list, 'gold-1', true, '2026-11-19T12:00:00Z'), 'platin', true, '2026-11-19T12:00:01Z')
    await act(async () => answer(served))
    await waitFor(() => expect(result.current.checkFor(goldOf(result.current.list)!)?.busy).toBe(false))
    expect(result.current.list?.achievements.find((a) => a.apiName === 'platin')?.achieved).toBe(true)
  })

  it('puts the old state back when the server refuses and tells the page the complete reason', async () => {
    const list = manualList(ps5Items())
    client.fetchGameTrophies.mockResolvedValue(list)
    const reason = 'Die Trophäe „gold-1“ gibt es in dieser Liste nicht mehr. Lade die Seite neu und versuche es noch einmal.'
    client.setTrophyUnlocked.mockRejectedValue(new ApiError(reason, 404, 'DB error: no such row', undefined, undefined, undefined, undefined))
    const onTickError = vi.fn()
    const { result } = useIt(onTickError)
    await waitFor(() => expect(result.current.list).toBeTruthy())

    act(() => result.current.toggle('gold-1', true))

    await waitFor(() => expect(onTickError).toHaveBeenCalledTimes(1))
    expect(onTickError).toHaveBeenCalledWith({
      title: 'Die Trophäe konnte nicht abgehakt werden',
      message: reason,
      details: 'DB error: no such row',
      code: expect.anything(),
    })
    // Rolled back (and the server's truth is fetched again)
    await waitFor(() => expect(goldOf(result.current.list)?.achieved).toBe(false))
    expect(result.current.list?.achievedCount).toBe(1)
    expect(result.current.checkFor(goldOf(result.current.list)!)?.busy).toBe(false)
  })

  it('says "Haken entfernen" for the un-tick when it fails', async () => {
    client.fetchGameTrophies.mockResolvedValue(manualList(ps5Items()))
    client.setTrophyUnlocked.mockRejectedValue(new Error('x'))
    const onTickError = vi.fn()
    const { result } = useIt(onTickError)
    await waitFor(() => expect(result.current.list).toBeTruthy())
    act(() => result.current.toggle('silber-1', false))
    await waitFor(() => expect(onTickError).toHaveBeenCalled())
    expect(onTickError.mock.calls[0]![0].title).toBe('Der Haken konnte nicht entfernt werden')
  })

  it('locks the PlayStation platinum trophy (the server ticks it itself) and nothing else', async () => {
    client.fetchGameTrophies.mockResolvedValue(manualList(ps5Items()))
    const { result } = useIt()
    await waitFor(() => expect(result.current.list).toBeTruthy())
    expect(result.current.checkFor(result.current.list!.achievements[0]!)?.locked).toBe(true)
    expect(result.current.checkFor(goldOf(result.current.list)!)?.locked).toBe(false)
  })

  it('uses the key of the Steam list family, so the hub refresh of ["live-achievements"] reaches it', () => {
    expect(manualTrophiesKey('stellar-blade')).toEqual(['live-achievements', 'g:stellar-blade'])
  })
})

describe('useManualTrophies: the translation of the list into the UI language (D2)', () => {
  const missing = (over: Partial<NonNullable<ManualTrophyListDto['translation']>> = {}) =>
    manualList(ps5Items(), { language: 'de', translation: { language: 'en', sourceLanguage: 'de', state: 'missing', running: false, error: null, providerMessage: null, ...over } })

  it('starts the translation once when the list is missing in the UI language and follows it', async () => {
    client.fetchGameTrophies.mockResolvedValue(missing())
    client.startGuideTranslation.mockResolvedValue({})
    const { result } = useIt(vi.fn(), 'en')
    await waitFor(() => expect(client.startGuideTranslation).toHaveBeenCalledWith('stellar-blade', false))
    expect(client.startGuideTranslation).toHaveBeenCalledTimes(1)

    // The job runs: the list answer says so; the page keeps the original meanwhile
    client.fetchGameTrophies.mockResolvedValue(missing({ running: true }))
    await act(async () => {
      await queryClient.invalidateQueries({ queryKey: manualTrophiesKey('stellar-blade') })
    })
    await waitFor(() => expect(result.current.translation.phase).toBe('running'))
    expect(result.current.list?.language).toBe('de')

    // Complete: the translated list comes back, nothing is started again
    client.fetchGameTrophies.mockResolvedValue(manualList(ps5Items(), { language: 'en', translation: { language: 'en', sourceLanguage: 'de', state: 'complete', running: false, error: null, providerMessage: null } }))
    await act(async () => {
      await queryClient.invalidateQueries({ queryKey: manualTrophiesKey('stellar-blade') })
    })
    await waitFor(() => expect(result.current.translation.phase).toBe('idle'))
    expect(result.current.list?.language).toBe('en')
    expect(client.startGuideTranslation).toHaveBeenCalledTimes(1)
  })

  it('does nothing when the list is in the UI language already', async () => {
    client.fetchGameTrophies.mockResolvedValue(manualList(ps5Items()))
    const { result } = useIt()
    await waitFor(() => expect(result.current.list).toBeTruthy())
    expect(client.startGuideTranslation).not.toHaveBeenCalled()
    expect(result.current.translation.phase).toBe('idle')
  })

  it('shows a failed translation with the complete reason and tries again only on request', async () => {
    client.fetchGameTrophies.mockResolvedValue(missing({ error: 'Kein KI-Schlüssel hinterlegt.', providerMessage: 'HTTP 401' }))
    client.startGuideTranslation.mockResolvedValue({})
    const { result } = useIt(vi.fn(), 'en')
    await waitFor(() => expect(result.current.translation.phase).toBe('failed'))
    expect(result.current.translation.error).toEqual({ message: 'Kein KI-Schlüssel hinterlegt.', details: 'HTTP 401' })
    expect(client.startGuideTranslation).toHaveBeenCalledTimes(1)

    act(() => result.current.translation.retry())
    await waitFor(() => expect(client.startGuideTranslation).toHaveBeenCalledWith('stellar-blade', true))
  })
})
