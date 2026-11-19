import { useCallback, useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { fetchGameTrophies, setTrophyUnlocked, startGuideTranslation } from '../../api/client'
import type { LiveAchievementItemDto, ManualTrophyListDto } from '../../api/types'
import { describeError, type ErrorText } from '../../utils/apiErrorText'
import { errorCodeOf } from '../../utils/errorReport'
import { useWindowBusy } from '../../utils/windowBusy'
import type { TrophyCheck } from './TrophyCheck'
import { applyTick } from './trophyList'

// The trophy list of a game added by hand, in one place: the query (same family of keys as the live list of a Steam game, so the hub's
// "something changed" refresh reaches it), the optimistic tick with its rollback, and the translation of the list into the UI language.

/** Query key of the trophy list of a game added by hand; it starts like the Steam list's key, so ['live-achievements'] refreshes both. */
export const manualTrophiesKey = (slug: string) => ['live-achievements', `g:${slug}`] as const

/** A failed tick, ready for the page's notice: our sentence, the server's complete text, the code for the copy button. */
export interface TickFailure {
  title: string
  message: string
  details: string | null
  code: string | null
}

export type TrophyTranslationPhase = 'idle' | 'running' | 'failed'

export interface TrophyTranslationState {
  /** idle = nothing to show; running = the server translates the list into the UI language; failed = it could not (see error). */
  phase: TrophyTranslationPhase
  error: ErrorText | null
  /** "Try again" after a failure. */
  retry: () => void
  /** "Gemini quota used up – continuing with Claude." when the job had to change AI provider; null otherwise. */
  aiNotice: string | null
}

export interface ManualTrophies {
  list: ManualTrophyListDto | undefined
  loading: boolean
  /** The list could not be loaded (the page says so, completely). */
  error: unknown
  fetching: boolean
  refetch: () => void
  /** Ticks or un-ticks one trophy by hand. */
  toggle: (apiName: string, unlocked: boolean) => void
  /** The check of one trophy for the views (a trophy that cannot be ticked by hand has none). */
  checkFor: (ach: LiveAchievementItemDto) => TrophyCheck | undefined
  translation: TrophyTranslationState
}

interface Options {
  slug: string
  // False while the game is not known yet or has no trophy list at all (Steam game, Nintendo)
  enabled: boolean
  // The UI language: the list is shown in it once its translation is complete
  language: string
  onTickError: (failure: TickFailure) => void
}

export function useManualTrophies({ slug, enabled, language, onTickError }: Options): ManualTrophies {
  const { t } = useTranslation('live')
  const queryClient = useQueryClient()
  const key = manualTrophiesKey(slug)
  const [busy, setBusy] = useState<ReadonlySet<string>>(() => new Set())
  const busyRef = useRef(busy)
  busyRef.current = busy

  const query = useQuery({
    queryKey: key,
    queryFn: () => fetchGameTrophies(slug),
    enabled: enabled && Boolean(slug),
    // The list changes through this window (the tick) or another one (the hub refreshes it); a calm refresh covers the silent un-tick of another window
    // and follows a running translation closely
    refetchInterval: (q) => (q.state.data?.translation?.state === 'missing' && q.state.data.translation.running ? 2500 : 30_000),
  })

  // The answer is made for one language (the request's); after a language switch it is fetched again
  const previousLanguage = useRef(language)
  useEffect(() => {
    if (previousLanguage.current === language) return
    previousLanguage.current = language
    void queryClient.invalidateQueries({ queryKey: manualTrophiesKey(slug) })
  }, [language, slug, queryClient])

  const toggle = useCallback(
    (apiName: string, unlocked: boolean) => {
      if (busyRef.current.has(apiName)) return
      setBusy((previous) => new Set(previous).add(apiName))
      void (async () => {
        // An older answer on its way would overwrite the new state with the old one
        await queryClient.cancelQueries({ queryKey: manualTrophiesKey(slug) })
        const previous = queryClient.getQueryData<ManualTrophyListDto>(manualTrophiesKey(slug))
        if (previous) queryClient.setQueryData<ManualTrophyListDto>(manualTrophiesKey(slug), applyTick(previous, apiName, unlocked, new Date().toISOString()))
        try {
          // The popup and the sound come to every window through the hub (this one too): nothing is shown from here
          const answer = await setTrophyUnlocked(slug, apiName, unlocked)
          queryClient.setQueryData<ManualTrophyListDto>(manualTrophiesKey(slug), answer)
          // The tile's trophy figures and the cabinet follow the list
          void queryClient.invalidateQueries({ queryKey: ['games'] })
          void queryClient.invalidateQueries({ queryKey: ['game', slug] })
        } catch (err: unknown) {
          // Back to what was shown before, then the server's truth; the player is told what went wrong, completely
          if (previous) queryClient.setQueryData<ManualTrophyListDto>(manualTrophiesKey(slug), previous)
          void queryClient.invalidateQueries({ queryKey: manualTrophiesKey(slug) })
          const { message, details } = describeError(err, t('trophy.check.failedFallback'))
          onTickError({ title: unlocked ? t('trophy.check.failedMark') : t('trophy.check.failedUnmark'), message, details, code: errorCodeOf(err) })
        } finally {
          setBusy((current) => {
            const next = new Set(current)
            next.delete(apiName)
            return next
          })
        }
      })()
    },
    [slug, queryClient, onTickError, t]
  )

  const list = query.data
  const checkFor = useCallback(
    (ach: LiveAchievementItemDto): TrophyCheck | undefined => {
      const item = list?.achievements.find((a) => a.apiName === ach.apiName)
      if (!item) return undefined
      return {
        checked: item.achieved,
        busy: busy.has(ach.apiName),
        onToggle: () => toggle(ach.apiName, !item.achieved),
        // D3: the PlayStation platinum trophy ticks itself when every other base-game trophy is ticked
        locked: list?.platform === 'playstation' && item.grade === 'platinum',
      }
    },
    [list, busy, toggle]
  )

  const translation = useTrophyTranslation(slug, list, language, () => void queryClient.invalidateQueries({ queryKey: manualTrophiesKey(slug) }))

  return { list, loading: query.isLoading, error: query.error, fetching: query.isFetching, refetch: () => void query.refetch(), toggle, checkFor, translation }
}

/**
 * The trophy list is written in one language (the guide's); opened in the other UI language it is translated by the player's AI in the background -
 * by the same job as the guide (POST /translate), also when the game has no guide at all. The list answer says whether the translation is missing
 * (translation.state === 'missing' for this language): this hook then asks the server to start it once and follows it by reloading the list while it
 * runs; until it is complete the page keeps showing the original. A failure (no AI key, quota) stays visible and is not retried by itself.
 */
function useTrophyTranslation(slug: string, list: ManualTrophyListDto | undefined, language: string, reload: () => void): TrophyTranslationState {
  const [starting, setStarting] = useState(false)
  const [startError, setStartError] = useState<ErrorText | null>(null)
  const startedFor = useRef<string | null>(null)

  const answered = list?.translation
  const needed = Boolean(slug && list && list.achievements.length > 0 && answered && answered.state === 'missing' && answered.language === language)
  const sessionKey = `${slug}:${language}`

  const start = useCallback(
    (force: boolean) => {
      setStarting(true)
      setStartError(null)
      startGuideTranslation(slug, force)
        .catch((err: unknown) => setStartError(describeError(err, '')))
        .finally(() => {
          setStarting(false)
          // The list answer carries the state of the job (running, error)
          reload()
        })
    },
    [slug, reload]
  )

  // Once per game and language while a translation is missing
  useEffect(() => {
    if (!needed) {
      startedFor.current = null
      return
    }
    if (startedFor.current === sessionKey) return
    startedFor.current = sessionKey
    start(false)
  }, [needed, sessionKey, start])

  const running = needed && (starting || answered?.running === true)
  // A long AI job of this window: automatic page changes (switch to a started game) hold back meanwhile
  useWindowBusy(running)

  const aiNotice = answered?.aiNotice ?? null
  const retry = () => start(true)
  if (!needed) return { phase: 'idle', error: null, retry, aiNotice }
  if (startError) return { phase: 'failed', error: startError, retry, aiNotice }
  if (running) return { phase: 'running', error: null, retry, aiNotice }
  if (answered?.error) return { phase: 'failed', error: { message: answered.error, details: answered.providerMessage }, retry, aiNotice }
  return { phase: 'idle', error: null, retry, aiNotice }
}
