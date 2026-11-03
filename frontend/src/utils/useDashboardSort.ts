import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { fetchSettings, updateSettings } from '../api/client'
import {
  parseSortChoice,
  serializeSortChoice,
  type DashboardSortView,
  type SortChoice,
} from './dashboardSort'

// The last known choice of this browser, only so the first paint (before the settings arrive) already shows the right order
const CACHE_KEY = 'pv_dashboard_sort_v1'

type Stored = Partial<Record<DashboardSortView, string>>

function readCache(): Stored {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(CACHE_KEY) ?? '{}')
    if (typeof parsed !== 'object' || parsed === null) return {}
    const { favorites, all } = parsed as Record<string, unknown>
    return {
      favorites: typeof favorites === 'string' ? favorites : undefined,
      all: typeof all === 'string' ? all : undefined,
    }
  } catch {
    return {}
  }
}

function writeCache(stored: Stored) {
  try {
    localStorage.setItem(CACHE_KEY, JSON.stringify(stored))
  } catch {
    // Storage unavailable (private mode): the server still keeps the choice
  }
}

/**
 * The dashboard's sort order per view. The server (app_settings, one user) is the source of truth, so the choice follows him between
 * the Windows window and the browser; a change shows at once and is saved with the normal settings PUT. When saving fails the previous
 * choice comes back and <c>saveError</c> holds the reason for a notice.
 */
export function useDashboardSort() {
  const queryClient = useQueryClient()
  const { data: settings } = useQuery({ queryKey: ['settings'], queryFn: fetchSettings })
  const [cached, setCached] = useState<Stored>(readCache)
  // Choices that are on their way to the server, shown meanwhile
  const [inFlight, setInFlight] = useState<Stored>({})
  const [saveError, setSaveError] = useState<unknown>(null)

  const mutation = useMutation({
    mutationFn: ({ view, value }: { view: DashboardSortView; value: string }) =>
      updateSettings(view === 'favorites' ? { dashboardSortFavorites: value } : { dashboardSortAll: value }),
    onSuccess: (response) => {
      queryClient.setQueryData(['settings'], response)
      setSaveError(null)
    },
    onError: (error, { view }) => {
      // Back to what the server has; the cache follows
      const server = view === 'favorites' ? settings?.dashboardSortFavorites : settings?.dashboardSortAll
      setCached((current) => {
        const next = { ...current, [view]: server }
        writeCache(next)
        return next
      })
      setSaveError(error)
    },
    onSettled: (_data, _error, { view }) => setInFlight((current) => ({ ...current, [view]: undefined })),
  })

  const serverValue = (view: DashboardSortView) =>
    view === 'favorites' ? settings?.dashboardSortFavorites : settings?.dashboardSortAll

  const choiceFor = (view: DashboardSortView): SortChoice =>
    parseSortChoice(inFlight[view] ?? serverValue(view) ?? cached[view], view)

  const setChoice = (view: DashboardSortView, choice: SortChoice) => {
    const value = serializeSortChoice(choice)
    setSaveError(null)
    setInFlight((current) => ({ ...current, [view]: value }))
    setCached((current) => {
      const next = { ...current, [view]: value }
      writeCache(next)
      return next
    })
    mutation.mutate({ view, value })
  }

  return { choiceFor, setChoice, saveError, clearSaveError: () => setSaveError(null) }
}
