import { createContext, useCallback, useContext, useMemo, useRef, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { fetchPins, fetchScopedPins, pinAchievement, pinScopedAchievement, removePins, removeScopedPins, unpinAchievement, unpinScopedAchievement } from '../../api/client'
import type { LivePinsDto, TrophyScope } from '../../api/types'
import { describeError } from '../../utils/apiErrorText'
import { errorCodeOf } from '../../utils/errorReport'

// "Im Blick": the achievements a player keeps in view while playing. This file is the one place that knows the server state
// (query key ['live-pins', appId], or ['live-pins', 'g:<game id>'] for a game added by hand); the trophy lists, the dock / column and the detail dialog only read it through usePins().

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
  // The game the pins belong to: a Steam game by its AppID, a game added by hand (appId 0) by its game id
  scope: TrophyScope
  appId: number
  gameId: string | null
  // One string that names the game (for keys: a switch of game starts the cards over)
  scopeKey: string
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

// A Steam game is named by its AppID (a bare number is fine), a game added by hand by its game id
type ScopeLike = TrophyScope | number
const asScope = (scope: ScopeLike): TrophyScope => (typeof scope === 'number' ? { appId: scope } : scope)

export const pinsKey = (scope: ScopeLike) => {
  const named = asScope(scope)
  return ('gameId' in named ? ['live-pins', `g:${named.gameId}`] : ['live-pins', named.appId]) as readonly [string, string | number]
}

// Steam games keep their original calls, a game added by hand names its game id
const fetchFor = (scope: TrophyScope) => ('gameId' in scope ? fetchScopedPins(scope) : fetchPins(scope.appId))
const pinFor = (scope: TrophyScope, apiName: string) => ('gameId' in scope ? pinScopedAchievement(scope, apiName) : pinAchievement(scope.appId, apiName))
const unpinFor = (scope: TrophyScope, apiName: string) => ('gameId' in scope ? unpinScopedAchievement(scope, apiName) : unpinAchievement(scope.appId, apiName))
const removeFor = (scope: TrophyScope, apiNames: string[]) => ('gameId' in scope ? removeScopedPins(scope, apiNames) : removePins(scope.appId, apiNames))

const scopeId = (scope: TrophyScope) => ('gameId' in scope ? scope.gameId : String(scope.appId))

const pinsQueryOptions = (scope: TrophyScope) => ({
  queryKey: pinsKey(scope),
  queryFn: () => fetchFor(scope),
  // The app turns the refetch on focus off globally; a second window may have changed the pins meanwhile
  refetchOnWindowFocus: 'always' as const,
  staleTime: 10_000,
})

/** How many achievements of this game are pinned (0 while unknown or without a game): the same query as the provider's, so it costs no request. */
export function usePinCount(scope: ScopeLike | null | undefined): number {
  const named = scope === null || scope === undefined ? { appId: 0 } : asScope(scope)
  const { data } = useQuery({ ...pinsQueryOptions(named), enabled: scopeId(named) !== '' && scopeId(named) !== '0' })
  return data?.apiNames.length ?? 0
}

/** The pins of the game page, or null when the page has none (a game without a Steam link). */
export function usePins(): PinsApi | null {
  return useContext(PinsContext)
}

interface PinsProviderProps {
  // One of the two: a Steam game by its AppID, a game added by hand by its game id
  appId?: number
  gameId?: string
  /** A failed action: the page shows it in its notice (complete, with the copy button). */
  onError: (failure: PinsFailure) => void
  children: ReactNode
}

export function PinsProvider({ appId, gameId, onError, children }: PinsProviderProps) {
  const { t } = useTranslation('live')
  const queryClient = useQueryClient()
  // The scope object stays the same while the ids do (it is the identity of the query and of the callbacks)
  const scope = useMemo<TrophyScope>(() => (gameId ? { gameId } : { appId: appId ?? 0 }), [appId, gameId])
  const key = useMemo(() => pinsKey(scope), [scope])
  // Several actions can be on their way at once: only the last answer may replace the state, an earlier one would flicker it back
  const inFlight = useRef(0)
  // Names unpinned by this window (remembered briefly): the answer of the server does not make them "leaving" cards
  const removedHere = useRef(new Map<string, number>())

  const { data, isPending } = useQuery(pinsQueryOptions(scope))

  const names = useMemo(() => data?.apiNames ?? [], [data])
  const max = data?.max ?? FALLBACK_MAX

  const run = useCallback(
    async (title: string, optimistic: (previous: LivePinsDto) => LivePinsDto, call: () => Promise<LivePinsDto>) => {
      // Answer at once (before any await): stop a refetch that is on its way - it would overwrite the new state with the old one
      void queryClient.cancelQueries({ queryKey: key })
      const previous = queryClient.getQueryData<LivePinsDto>(key) ?? { appId: 'appId' in scope ? scope.appId : 0, apiNames: [], max: FALLBACK_MAX }
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
    [queryClient, key, scope, onError, t]
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
          () => unpinFor(scope, apiName)
        )
        return
      }
      if (list.length >= (current?.max ?? FALLBACK_MAX)) return
      void run(
        t('pins.failed.pin'),
        (previous) => ({ ...previous, apiNames: [...previous.apiNames, apiName] }),
        () => pinFor(scope, apiName)
      )
    },
    [queryClient, key, scope, run, t]
  )

  const removeMany = useCallback(
    (apiNames: string[]) => {
      if (apiNames.length === 0) return
      const gone = new Set(apiNames)
      for (const name of apiNames) removedHere.current.set(name, Date.now())
      void run(
        t('pins.failed.remove'),
        (previous) => ({ ...previous, apiNames: previous.apiNames.filter((name) => !gone.has(name)) }),
        () => removeFor(scope, apiNames)
      )
    },
    [scope, run, t]
  )

  const api = useMemo<PinsApi>(
    () => ({
      scope,
      appId: 'appId' in scope ? scope.appId : 0,
      gameId: 'gameId' in scope ? scope.gameId : null,
      scopeKey: scopeId(scope),
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
    [scope, names, max, isPending, toggle, removeMany]
  )

  return <PinsContext.Provider value={api}>{children}</PinsContext.Provider>
}

/**
 * The provider for a game that has trophies to pin: a game with a Steam link (by its AppID) or a game added by hand (by its game id). A game
 * without either has no achievements to pin, so its page gets no pins at all.
 */
export function PinsScope({ appId, gameId, onError, children }: { appId?: number | null; gameId?: string | null; onError: (failure: PinsFailure) => void; children: ReactNode }) {
  if (!appId && !gameId) return <>{children}</>
  return (
    <PinsProvider appId={appId ?? undefined} gameId={gameId ?? undefined} onError={onError}>
      {children}
    </PinsProvider>
  )
}
