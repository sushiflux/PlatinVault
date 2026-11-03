import { useEffect, useRef } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useLocation, useNavigate } from 'react-router'
import { useTranslation } from 'react-i18next'
import { fetchLiveStatus, fetchRunningGame, fetchSettings } from '../api/client'
import type { LiveStatusDto } from '../api/types'
import { useLauncher } from '../components/launch/LaunchProvider'
import type { AppNotice } from '../components/launch/LaunchProvider'
import { gameLivePath, isDialogOpen, isGamePagePath, isTypingInTextField, newStartTracker, observeRunningGame } from './gameStart'
import { isWindowBusy } from './windowBusy'

// How often the running game is asked. A start of a game of the vault is pushed by the server (the "GameChanged" event refreshes the
// same query at once, see App.tsx); this poll is what notices a game that is not in the vault, and the safety net for a missed push.
const POLL_MS = 5000
const NOTICE_ID = 'game-start'
// "You are playing X - no guide yet" is information only and goes away by itself; the notice with a button stays until it is used,
// closed, or the game ends
const NO_GUIDE_NOTICE_MS = 25_000
// Looking up the name of a game that is not in the vault needs Steam's store; a slow answer must not hold the notice back for long
const NAME_TIMEOUT_MS = 6000

// What the server answers when the store does not know the game: "Steam App 123456" - no help for the player
const PLACEHOLDER_NAME = /^Steam App \d+$/i

function withTimeout<T>(work: Promise<T>, ms: number): Promise<T | null> {
  let timer: number | undefined
  const timeout = new Promise<null>((resolve) => {
    timer = window.setTimeout(() => resolve(null), ms)
  })
  return Promise.race([work, timeout]).finally(() => window.clearTimeout(timer))
}

/**
 * Switch to the game that was just started. Mounted once for the whole app (inside the router and the launch provider).
 *
 * Every open window runs this on its own: it watches the running game ('live-status': bridge on the gaming PC, or the local Steam of
 * the server) and, when a game is STARTED (see observeRunningGame: never on page load, never while it keeps running, never when it
 * stops), moves the window to that game's page, Live trophies section. Rules around it:
 *  - switch "features.auto_switch_game" (Settings > Live & Sound, on by default) is read fresh at the moment of the start, so a change
 *    in one window is respected by all of them at once;
 *  - already on that game's page: nothing happens;
 *  - the user is busy (a dialog is open, the cursor is in a text field, an AI job runs in this window): no jump, a notice with a
 *    "Switch to the game" button instead;
 *  - the game has no guide (is not in the vault): no jump possible, a calm notice says what is being played.
 */
export function useGameStartSwitch(): void {
  const { t } = useTranslation('live')
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const queryClient = useQueryClient()
  const { notify, dismissNotice } = useLauncher()

  const { data: status, dataUpdatedAt } = useQuery({ queryKey: ['live-status'], queryFn: fetchLiveStatus, refetchInterval: POLL_MS })

  // The latest values for the asynchronous part, without restarting anything when they change
  const latest = useRef({ t, navigate, pathname, notify, dismissNotice })
  useEffect(() => {
    latest.current = { t, navigate, pathname, notify, dismissNotice }
  }, [t, navigate, pathname, notify, dismissNotice])

  const tracker = useRef(newStartTracker())
  const noticeShown = useRef(false)
  const noticeTimer = useRef<number | undefined>(undefined)

  useEffect(() => {
    return () => window.clearTimeout(noticeTimer.current)
  }, [])

  useEffect(() => {
    if (!status) return

    const forgetNotice = () => {
      window.clearTimeout(noticeTimer.current)
      if (noticeShown.current) {
        noticeShown.current = false
        latest.current.dismissNotice(NOTICE_ID)
      }
    }

    const show = (notice: AppNotice, autoDismissMs?: number) => {
      latest.current.notify(notice)
      noticeShown.current = true
      window.clearTimeout(noticeTimer.current)
      if (autoDismissMs) noticeTimer.current = window.setTimeout(forgetNotice, autoDismissMs)
    }

    const handleStart = async (started: LiveStatusDto) => {
      const appId = started.runningAppId
      // The game may stop while an answer is awaited; then there is nothing to switch to any more
      const stillRunning = () => tracker.current.appId === appId

      let enabled: boolean
      try {
        // Fresh on purpose (staleTime 0): the switch may have been changed in another window a moment ago
        const settings = await queryClient.fetchQuery({ queryKey: ['settings'], queryFn: fetchSettings, staleTime: 0 })
        enabled = settings.autoSwitchGameEnabled !== false
      } catch {
        return
      }
      if (!enabled || !stillRunning()) return

      const slug = started.runningGameTracked ? started.runningGameSlug : null
      if (slug) {
        if (isGamePagePath(latest.current.pathname, slug)) return
        const target = gameLivePath(slug)
        if (!isDialogOpen() && !isTypingInTextField() && !isWindowBusy()) {
          latest.current.navigate(target)
          return
        }
        const name = started.runningGameName || slug
        show({
          id: NOTICE_ID,
          tone: 'info',
          title: latest.current.t('gameStart.started.title', { name }),
          message: latest.current.t('gameStart.started.message'),
          actions: [
            {
              label: latest.current.t('gameStart.started.action'),
              onClick: () => {
                forgetNotice()
                latest.current.navigate(target)
              },
            },
          ],
        })
        return
      }

      // Not in the vault: there is no page to go to
      let name = ''
      try {
        const info = await withTimeout(queryClient.fetchQuery({ queryKey: ['running-game', appId], queryFn: fetchRunningGame, staleTime: 60_000 }), NAME_TIMEOUT_MS)
        if (info && info.appId === appId && !PLACEHOLDER_NAME.test(info.name)) name = info.name
      } catch {
        // The notice works without the name
      }
      if (!stillRunning()) return
      show(
        {
          id: NOTICE_ID,
          tone: 'info',
          title: latest.current.t('gameStart.noGuide.title', { name: name || latest.current.t('gameStart.unknownGame') }),
        },
        NO_GUIDE_NOTICE_MS
      )
    }

    const started = observeRunningGame(tracker.current, status.runningAppId, Date.now())
    if (status.runningAppId === 0) {
      // The game ended: a notice about its start is out of date
      forgetNotice()
    } else if (started !== null) {
      // A notice of an earlier start (maybe another game) must not stay next to the new situation
      forgetNotice()
      void handleStart(status)
    }
    // dataUpdatedAt makes this run for every answer of the server, not only when the content changed (the gap rule needs the times)
  }, [status, dataUpdatedAt, queryClient])
}
