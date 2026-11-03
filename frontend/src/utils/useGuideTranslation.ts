import { useCallback, useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { fetchGuideTranslation, startGuideTranslation } from '../api/client'
import type { GameDetailDto, GuideTranslationDto } from '../api/types'
import { describeError, type ErrorText } from './apiErrorText'
import { useWindowBusy } from './windowBusy'

export type GuideTranslationPhase = 'idle' | 'running' | 'failed'

export interface GuideTranslationState {
  /** idle = nothing to show; running = the server translates the guide into the UI language; failed = it could not (see error). */
  phase: GuideTranslationPhase
  /** Chapters translated so far / chapters in total (for the progress text). */
  translated: number
  total: number
  /** Why it failed: our sentence plus the AI provider's raw text for "technical details". */
  error: ErrorText | null
  /** "Try again" after a failure. */
  retry: () => void
  /** "Gemini quota used up – continuing with Claude." when the job had to change AI provider; null otherwise. */
  aiNotice: string | null
}

/**
 * A guide is written in one language; opened in the other UI language it is translated by the user's AI (server side, in the
 * background). The game answer already says whether the translation is missing (`translation.state === 'missing'`): this hook then
 * asks the server to start it once, follows the progress while it runs and reloads the game when it is done - until then the page
 * keeps showing the original, so nothing jumps. A failure (no AI key, quota used up) stays visible with its reason and is not
 * retried by itself.
 *
 * @param language  the UI language; only a game answer that was made for this language counts (right after a language switch the
 *                  cached answer is still the old one)
 */
export function useGuideTranslation(slug: string, game: GameDetailDto | undefined, language: string): GuideTranslationState {
  const state = useGuideTranslationState(slug, game, language)
  // A running translation is a long AI job of this window: automatic page changes (switch to a started game) hold back meanwhile
  useWindowBusy(state.phase === 'running')
  return state
}

function useGuideTranslationState(slug: string, game: GameDetailDto | undefined, language: string): GuideTranslationState {
  const queryClient = useQueryClient()
  const startedFor = useRef<string | null>(null)
  const [starting, setStarting] = useState(false)
  const [startError, setStartError] = useState<ErrorText | null>(null)

  const answered = game?.translation
  const needed = Boolean(slug && answered && answered.state === 'missing' && answered.language === language)
  const key = `${slug}:${language}`
  const statusKey = ['guide-translation', slug, language] as const

  const { data: status } = useQuery({
    queryKey: statusKey,
    queryFn: () => fetchGuideTranslation(slug),
    enabled: needed,
    staleTime: 0,
    // Follow a running job; a finished one (or a failed one) needs no more asking
    refetchInterval: (query) => (query.state.data?.running ? 2500 : false),
  })

  const start = useCallback(
    (force: boolean) => {
      setStarting(true)
      setStartError(null)
      startGuideTranslation(slug, force)
        .then((result) => queryClient.setQueryData<GuideTranslationDto>(['guide-translation', slug, language], result))
        .catch((err: unknown) => setStartError(describeError(err, '')))
        .finally(() => setStarting(false))
    },
    [slug, language, queryClient]
  )

  // Once per guide and language while a translation is missing: ask the server to translate (it starts a job, or says it already runs /
  // is done / failed). A failure is not retried by itself; once nothing is missing any more, a later change of the guide (new chapters
  // in the original language from "complete guide", "add chapter" or Story & Lore) starts the next translation in the same visit.
  useEffect(() => {
    if (!needed) {
      startedFor.current = null
      return
    }
    if (startedFor.current === key) return
    startedFor.current = key
    start(false)
  }, [needed, key, start])

  // The cached game answer is for the language it was fetched in: a language switch fetches it again
  const previousLanguage = useRef(language)
  useEffect(() => {
    if (previousLanguage.current === language) return
    previousLanguage.current = language
    void queryClient.invalidateQueries({ queryKey: ['game', slug] })
  }, [language, slug, queryClient])

  // Done: the translated version replaces the original (the card texts on the dashboard follow)
  const finished = status?.state === 'complete'
  useEffect(() => {
    if (!finished) return
    void queryClient.invalidateQueries({ queryKey: ['game', slug] })
    void queryClient.invalidateQueries({ queryKey: ['games'] })
  }, [finished, slug, queryClient])

  const current = status ?? answered
  const aiNotice = current?.aiNotice ?? null
  const total = current?.totalTabs ?? 0
  const translated = current?.translatedTabs ?? 0

  if (!needed) return { phase: 'idle', translated, total, error: null, retry: () => start(true), aiNotice }
  if (startError) return { phase: 'failed', translated, total, error: startError, retry: () => start(true), aiNotice }
  if (starting || current?.running) return { phase: 'running', translated, total, error: null, retry: () => start(true), aiNotice }
  if (current?.error) {
    return { phase: 'failed', translated, total, error: { message: current.error, details: current.providerMessage }, retry: () => start(true), aiNotice }
  }
  // Missing, not running, no error: the first answer is still on its way (running), or the job ended with nothing to report - e.g. the
  // guide changed while it ran; the next visit translates what is left (idle: no spinner that never ends)
  return { phase: status ? 'idle' : 'running', translated, total, error: null, retry: () => start(true), aiNotice }
}
