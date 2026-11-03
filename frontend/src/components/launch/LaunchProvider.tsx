import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { fetchLaunchCommand, launchGame, launchLibraryGame } from '../../api/client'
import type { LaunchCommandDto, LaunchResponseDto } from '../../api/types'
import { describeError } from '../../utils/apiErrorText'
import { FloatingNotice, FloatingNoticeStack } from '../ui/FloatingNotice'
import type { NoticeAction, NoticeTone } from '../ui/FloatingNotice'

/** What a Play button starts: a game of the dashboard (by slug) or a game of the whole Steam library (by AppID). */
export type LaunchTarget = { kind: 'game'; slug: string } | { kind: 'library'; appId: number }

export function launchKey(target: LaunchTarget): string {
  return target.kind === 'game' ? `game:${target.slug}` : `library:${target.appId}`
}

interface LaunchContextValue {
  /** Starts a game (see the flow below). Never throws: every outcome ends in a notice. */
  launch: (target: LaunchTarget, gameName: string) => Promise<void>
  /** True while the request or the bridge command of this target is running (the button then shows its spinner). */
  isBusy: (key: string) => boolean
  /** Shows (or replaces, same id) a floating notice in the app-wide stack, e.g. "a game was started" from useGameStartSwitch. */
  notify: (notice: AppNotice) => void
  /** Removes a notice shown with `notify` (nothing happens when it is already gone). */
  dismissNotice: (id: string) => void
}

const LaunchContext = createContext<LaunchContextValue | null>(null)

export function useLauncher(): LaunchContextValue {
  const value = useContext(LaunchContext)
  if (!value) throw new Error('useLauncher needs a <LaunchProvider> above it')
  return value
}

export interface AppNotice {
  id: string
  tone: NoticeTone
  title: string
  message?: string
  actions?: NoticeAction[]
}

type LaunchNotice = AppNotice

// The bridge polls every few seconds; the server lets a command expire after 30 s. A little more than that is waited for.
const POLL_MS = 1000
const FALLBACK_WAIT_S = 30
const WAIT_MARGIN_S = 15
// A success notice goes away by itself
const INFO_MS = 5000

// Opens a steam:// link with the browser's own handler (it asks "Open Steam?"). A real anchor click, not location.assign: same effect
// for the browser, and the dashboard checks can see the link without launching anything.
function openSteamLink(url: string): void {
  const link = document.createElement('a')
  link.href = url
  link.rel = 'noreferrer'
  link.style.display = 'none'
  document.body.appendChild(link)
  link.click()
  link.remove()
}

/**
 * The one place that starts games: Play buttons call `launch()`, the provider shows what happens in floating notices (so they
 * survive a card being re-rendered or the page being left) and keeps the "busy" state per target.
 *
 * Flow: POST launch -> via "browser": open the steam:// link (the browser asks for permission), nothing more to show;
 * via "bridge": a progress notice "Starting <game> on the gaming PC ..." while the command is polled once a second, then an
 * info notice (it fades by itself) or an error notice with the server's own localized message. "done" only means the start was
 * handed to Windows - it does not say the game is running.
 */
export function LaunchProvider({ children }: { children: ReactNode }) {
  const { t } = useTranslation('dashboard')
  const [notices, setNotices] = useState<LaunchNotice[]>([])
  const [busy, setBusy] = useState<ReadonlySet<string>>(new Set())
  // The texts are read at the moment they are shown; a language switch in between must not restart anything
  const tRef = useRef(t)
  useEffect(() => {
    tRef.current = t
  }, [t])
  const aliveRef = useRef(true)
  const timers = useRef<number[]>([])
  useEffect(() => {
    aliveRef.current = true
    const pending = timers.current
    return () => {
      aliveRef.current = false
      pending.forEach((timer) => window.clearTimeout(timer))
    }
  }, [])

  const setNotice = useCallback((notice: LaunchNotice) => {
    setNotices((all) => (all.some((n) => n.id === notice.id) ? all.map((n) => (n.id === notice.id ? notice : n)) : [...all, notice]))
  }, [])
  const dismiss = useCallback((id: string) => setNotices((all) => all.filter((n) => n.id !== id)), [])
  const setBusyKey = useCallback((key: string, on: boolean) => {
    setBusy((all) => {
      const next = new Set(all)
      if (on) next.add(key)
      else next.delete(key)
      return next
    })
  }, [])

  const wait = (ms: number) =>
    new Promise<void>((resolve) => {
      timers.current.push(window.setTimeout(resolve, ms))
    })

  const launch = useCallback(
    async (target: LaunchTarget, gameName: string) => {
      const key = launchKey(target)
      const id = `launch:${key}`
      const say = (...args: Parameters<typeof t>) => tRef.current(...args)
      setBusyKey(key, true)
      try {
        let response: LaunchResponseDto
        try {
          response = target.kind === 'game' ? await launchGame(target.slug) : await launchLibraryGame(target.appId)
        } catch (error) {
          const { message } = describeError(error, say('play.fallback.failed'))
          setNotice({ id, tone: 'error', title: say('play.failed.title', { name: gameName }), message })
          return
        }

        if (response.via === 'local') {
          // The Windows program started the game itself on this PC (no browser question): a short confirmation
          setNotice({ id, tone: 'info', title: say('play.done.title', { name: gameName }), message: say('play.done.local') })
          timers.current.push(window.setTimeout(() => dismiss(id), INFO_MS))
          return
        }

        if (response.via === 'browser' || !response.commandId) {
          // The local Steam of this PC: the browser's own "open Steam?" prompt is the feedback
          dismiss(id)
          openSteamLink(response.url)
          return
        }

        // The bridge on the gaming PC: follow the command until it is finished
        const deadline = Date.now() + ((response.expiresInSeconds ?? FALLBACK_WAIT_S) + WAIT_MARGIN_S) * 1000
        let state: LaunchCommandDto | null = null
        let status: string = response.status ?? 'pending'
        const progress = () =>
          setNotice({
            id,
            tone: 'progress',
            title: say('play.progress.title', { name: gameName }),
            message: status === 'delivered' ? say('play.progress.delivered') : say('play.progress.pending'),
          })
        progress()
        while (aliveRef.current && Date.now() < deadline) {
          await wait(POLL_MS)
          if (!aliveRef.current) return
          try {
            state = await fetchLaunchCommand(response.commandId)
          } catch (error) {
            // Gone after two minutes (404) is final; a short network hiccup is retried until the deadline
            if ((error as { status?: number } | null)?.status === 404) {
              setNotice({ id, tone: 'error', title: say('play.failed.title', { name: gameName }), message: say('play.fallback.expired') })
              return
            }
            continue
          }
          status = state.status
          if (state.finished) break
          progress()
        }

        if (!aliveRef.current) return
        if (state?.status === 'done') {
          setNotice({ id, tone: 'info', title: say('play.done.title', { name: gameName }), message: state.message || say('play.done.message') })
          timers.current.push(window.setTimeout(() => dismiss(id), INFO_MS))
        } else {
          const unanswered = !state || state.status === 'expired' || state.status === 'pending' || state.status === 'delivered'
          setNotice({
            id,
            tone: 'error',
            title: say('play.failed.title', { name: gameName }),
            message: state?.message || say(unanswered ? 'play.fallback.expired' : 'play.fallback.failed'),
          })
        }
      } finally {
        if (aliveRef.current) setBusyKey(key, false)
      }
    },
    [dismiss, setBusyKey, setNotice]
  )

  const value = useMemo<LaunchContextValue>(
    () => ({ launch, isBusy: (key) => busy.has(key), notify: setNotice, dismissNotice: dismiss }),
    [launch, busy, setNotice, dismiss]
  )

  return (
    <LaunchContext.Provider value={value}>
      {children}
      <FloatingNoticeStack raised>
        {notices.map((notice) => (
          <FloatingNotice
            key={notice.id}
            tone={notice.tone}
            title={notice.title}
            message={notice.message}
            actions={notice.actions}
            // A running start has no close button (it ends by itself); a result can be closed
            onClose={notice.tone === 'progress' ? undefined : () => dismiss(notice.id)}
          />
        ))}
      </FloatingNoticeStack>
    </LaunchContext.Provider>
  )
}
