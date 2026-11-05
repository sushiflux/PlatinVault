import { useEffect, useRef, useState } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { LiveAchievementItemDto } from '../api/types'
import { FullscreenAchievement } from '../components/FullscreenAchievement'
import { PlatinumCelebration } from '../components/PlatinumCelebration'
import { TrophyNotification } from '../components/TrophyNotification'
import { applyLanguage } from '../i18n'
import { ACHIEVEMENT_VISIBLE_MS, animationQueue } from '../utils/animationQueue'
import type { QueueState } from '../utils/animationQueue'
import { soundEngine } from '../utils/audio'
import { webViewChannel } from '../utils/programWindow'
import type { WebViewChannel } from '../utils/programWindow'
import { overlayStateMessage, readOverlayEvent } from './overlayMessages'
import type { OverlayState } from './overlayMessages'

// The page of the in-game overlay of the Windows program (route /overlay of the same frontend build): only the popup layer, on a
// transparent page, in a transparent click-through window over the game. It shows the SAME components as the app window
// (TrophyNotification, FullscreenAchievement, PlatinumCelebration) through its own animation queue, fed by the program
// (window.chrome.webview messages, see overlayMessages.ts) - never by SignalR, so every unlock reaches it exactly once.
// It is silent: the app window plays the sound once per unlock (utils/popupTarget.ts).

// After the queue went idle: room for the exit animations before the program hides the window
export const OVERLAY_IDLE_DELAY_MS = 900

const queryClient = new QueryClient({
  defaultOptions: { queries: { staleTime: 60_000, refetchOnWindowFocus: false, retry: false } },
})

const EMPTY: QueueState = { currentAchievement: null, currentPosition: null, currentPlatinum: null, visibleMs: ACHIEVEMENT_VISIBLE_MS, busy: false }

interface OverlayAppProps {
  // Injected by tests; the real page uses window.chrome.webview
  channel?: WebViewChannel | null
}

export function OverlayApp({ channel = webViewChannel() }: OverlayAppProps) {
  return (
    <QueryClientProvider client={queryClient}>
      <OverlayLayer channel={channel} />
    </QueryClientProvider>
  )
}

function OverlayLayer({ channel }: { channel: WebViewChannel | null }) {
  const [state, setState] = useState<QueueState>(EMPTY)
  // The game the popup belongs to (the full-screen popup names it); kept per achievement object of the queue
  const gameNames = useRef(new WeakMap<LiveAchievementItemDto, string>())
  const reported = useRef<OverlayState | null>(null)
  const received = useRef(0)

  useEffect(() => {
    soundEngine.setEnabled(false)
    const report = (next: OverlayState) => {
      if (reported.current === next) return
      reported.current = next
      try {
        channel?.postMessage(overlayStateMessage(next, received.current))
      } catch {
        // The program is closing
      }
    }

    let idleTimer: ReturnType<typeof setTimeout> | null = null
    const unsubscribe = animationQueue.subscribe((next) => {
      setState(next)
      if (next.busy) {
        if (idleTimer) clearTimeout(idleTimer)
        idleTimer = null
        report('busy')
      } else if (reported.current === 'busy' && !idleTimer) {
        idleTimer = setTimeout(() => {
          idleTimer = null
          report('idle')
        }, OVERLAY_IDLE_DELAY_MS)
      }
    })

    const listener = (event: { data: unknown }) => {
      const overlayEvent = readOverlayEvent(event.data)
      if (!overlayEvent) return
      received.current++
      if (idleTimer) clearTimeout(idleTimer)
      idleTimer = null
      report('busy')
      const enqueue = () => {
        if (overlayEvent.kind === 'achievement') {
          if (overlayEvent.gameName) gameNames.current.set(overlayEvent.achievement, overlayEvent.gameName)
          animationQueue.enqueueAchievement(overlayEvent.achievement, { position: overlayEvent.position, seconds: overlayEvent.seconds })
        } else {
          animationQueue.enqueuePlatinum(overlayEvent.gameName, overlayEvent.total)
        }
      }
      // The texts of the popup follow the app language the program sends with every event
      if (overlayEvent.language) void applyLanguage(overlayEvent.language).finally(enqueue)
      else enqueue()
    }
    channel?.addEventListener('message', listener)
    report('ready')

    return () => {
      if (idleTimer) clearTimeout(idleTimer)
      channel?.removeEventListener('message', listener)
      unsubscribe()
    }
  }, [channel])

  const position = state.currentPosition ?? 'top-right'
  const fullscreen = position === 'fullscreen'
  const achievement = state.currentAchievement
  return (
    <>
      <TrophyNotification achievement={fullscreen ? null : achievement} position={fullscreen ? 'top-right' : position} visibleMs={state.visibleMs} />
      <FullscreenAchievement
        achievement={fullscreen ? achievement : null}
        gameName={achievement ? (gameNames.current.get(achievement) ?? null) : null}
        onDismiss={() => animationQueue.dismissAchievement()}
        dismissHint={false}
        visibleMs={state.visibleMs}
      />
      <PlatinumCelebration
        data={state.currentPlatinum}
        onDismiss={() => animationQueue.dismissPlatinum()}
        dismissHint={false}
      />
    </>
  )
}
