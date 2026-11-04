import { createContext, useCallback, useContext, useMemo, useRef, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { fetchPins, pinAchievement, removePins, unpinAchievement } from '../../api/client'
import type { LivePinsDto } from '../../api/types'
import { describeError } from '../../utils/apiErrorText'
import { errorCodeOf } from '../../utils/errorReport'

// "Im Blick": the achievements a player keeps in view while playing. This file is the one place that knows the server state
// (query key ['live-pins', appId]); the trophy lists, the dock / column and the detail dialog only read it through usePins().

// What the server allows if it does not say (it always sends `max`)
const FALLBACK_MAX = 10

/** A failed pin action, ready for a notice: our sentence, the server's complete text, and the code for the copy button. */
export interface PinsFailure {
  title: string
  message: string
  details: string | null
  code: string | null
}

export interface PinsApi {
  appId: number
  /** The pinned achievements in the order of the cards. */
  names: string[]
  max: number
  full: boolean
  /** The first answer is still on its way. */
  loading: boolean
  isPinned: (apiName: string) => boolean
  /** Pins, or unpins when it is pinned already. Nothing happens for an unpinned achievement when the maximum is reached. */
  toggle: (apiName: string) => void
  /** Unpins several at once ("Geschaffte entfernen"). */
  removeMany: (apiNames: string[]) => void
  /** True for names the player unpinned just now (so the surfaces do not keep them as "done, leaving"). */
  wasRemovedHere: (apiName: string) => boolean
}

const PinsContext = createContext<PinsApi | null>(null)

export const pinsKey = (appId: number) => ['live-pins', appId] as const

const pinsQueryOptions = (appId: number) => ({
  queryKey: pinsKey(appId),
  queryFn: () => fetchPins(appId),
  // The app turns the refetch on focus off globally; a second window may have changed the pins meanwhile
  refetchOnWindowFocus: 'always' as const,
  staleTime: 10_000,
})

/** How many achievements of this game are pinned (0 while unknown or without a game): the same query as the provider's, so it costs no request. */
export function usePinCount(appId: number | null | undefined): number {
  const { data } = useQuery({ ...pinsQueryOptions(appId ?? 0), enabled: Boolean(appId) })
  return data?.apiNames.length ?? 0
}

/** The pins of the game page, or null when the page has none (a game without a Steam link). */
export function usePins(): PinsApi | null {
  return useContext(PinsContext)
}

interface PinsProviderProps {
  appId: number
  /** A failed action: the page shows it in its notice (complete, with the copy button). */
  onError: (failure: PinsFailure) => void
  children: ReactNode
}

export function PinsProvider({ appId, onError, children }: PinsProviderProps) {
  const { t } = useTranslation('live')
  const queryClient = useQueryClient()
  const key = useMemo(() => pinsKey(appId), [appId])
  // Several actions can be on their way at once: only the last answer may replace the state, an earlier one would flicker it back
  const inFlight = useRef(0)
  // Names unpinned by this window (remembered briefly): the answer of the server does not make them "leaving" cards
  const removedHere = useRef(new Map<string, number>())

  const { data, isPending } = useQuery(pinsQueryOptions(appId))

  const names = useMemo(() => data?.apiNames ?? [], [data])
  const max = data?.max ?? FALLBACK_MAX

  const run = useCallback(
    async (title: string, optimistic: (previous: LivePinsDto) => LivePinsDto, call: () => Promise<LivePinsDto>) => {
      // Answer at once (before any await): stop a refetch that is on its way - it would overwrite the new state with the old one
      void queryClient.cancelQueries({ queryKey: key })
      const previous = queryClient.getQueryData<LivePinsDto>(key) ?? { appId, apiNames: [], max: FALLBACK_MAX }
      queryClient.setQueryData<LivePinsDto>(key, optimistic(previous))
      inFlight.current++
      try {
        const answer = await call()
        inFlight.current--
        if (inFlight.current === 0) queryClient.setQueryData<LivePinsDto>(key, answer)
      } catch (error: unknown) {
        inFlight.current--
        // Back to what was shown before, then the server's truth; the player is told what went wrong, completely
        queryClient.setQueryData<LivePinsDto>(key, previous)
        void queryClient.invalidateQueries({ queryKey: key })
        const { message, details } = describeError(error, t('pins.failed.fallback'))
        onError({ title, message, details, code: errorCodeOf(error) })
      }
    },
    [queryClient, key, appId, onError, t]
  )

  const toggle = useCallback(
    (apiName: string) => {
      const current = queryClient.getQueryData<LivePinsDto>(key)
      const list = current?.apiNames ?? []
      if (list.includes(apiName)) {
        removedHere.current.set(apiName, Date.now())
        void run(
          t('pins.failed.unpin'),
          (previous) => ({ ...previous, apiNames: previous.apiNames.filter((name) => name !== apiName) }),
          () => unpinAchievement(appId, apiName)
        )
        return
      }
      if (list.length >= (current?.max ?? FALLBACK_MAX)) return
      void run(
        t('pins.failed.pin'),
        (previous) => ({ ...previous, apiNames: [...previous.apiNames, apiName] }),
        () => pinAchievement(appId, apiName)
      )
    },
    [queryClient, key, appId, run, t]
  )

  const removeMany = useCallback(
    (apiNames: string[]) => {
      if (apiNames.length === 0) return
      const gone = new Set(apiNames)
      for (const name of apiNames) removedHere.current.set(name, Date.now())
      void run(
        t('pins.failed.remove'),
        (previous) => ({ ...previous, apiNames: previous.apiNames.filter((name) => !gone.has(name)) }),
        () => removePins(appId, apiNames)
      )
    },
    [appId, run, t]
  )

  const api = useMemo<PinsApi>(
    () => ({
      appId,
      names,
      max,
      full: names.length >= max,
      loading: isPending,
      isPinned: (apiName) => names.includes(apiName),
      toggle,
      removeMany,
      wasRemovedHere: (apiName) => {
        const at = removedHere.current.get(apiName)
        return at !== undefined && Date.now() - at < 10_000
      },
    }),
    [appId, names, max, isPending, toggle, removeMany]
  )

  return <PinsContext.Provider value={api}>{children}</PinsContext.Provider>
}

/** The provider for a game that has a Steam link; a game without one has no achievements to pin, so its page gets no pins at all. */
export function PinsScope({ appId, onError, children }: { appId: number | null | undefined; onError: (failure: PinsFailure) => void; children: ReactNode }) {
  if (!appId) return <>{children}</>
  return (
    <PinsProvider appId={appId} onError={onError}>
      {children}
    </PinsProvider>
  )
}
