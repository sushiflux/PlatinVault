import { useCallback, useEffect, useState } from 'react'

// Fired when the seen-flags were cleared (Settings > System), so a page can show its hints again right away
export const HINTS_RESET_EVENT = 'pv-hints-reset'

export type HintScreen = 'dashboard' | 'game'

// Which text a hint shows: onboarding:hints.<id>.title / .text (see src/i18n/de/onboarding.ts). gameNavPhone is no hint of its own: it is the
// text of gameNav on a phone (chosen in HintTour), where the chapters are a drawer opened by a button instead of a panel.
export type HintId =
  | 'dashboardAdd'
  | 'dashboardStar'
  | 'dashboardSize'
  | 'dashboardCabinet'
  | 'headerLanguage'
  | 'headerHandbook'
  | 'gameNav'
  | 'gameNavPhone'
  | 'gameLive'
  | 'gameTwitch'
  | 'settings'

export interface HintDefinition {
  // Value of the data-hint attribute on the real control
  target: string
  id: HintId
}

// The hints of every screen, in the order they are shown. A target that is not on the page right now (no cards on an
// empty dashboard, a switched-off Twitch button) is simply skipped.
export const HINTS: Record<HintScreen, HintDefinition[]> = {
  dashboard: [
    { target: 'dashboard-add', id: 'dashboardAdd' },
    { target: 'dashboard-star', id: 'dashboardStar' },
    { target: 'dashboard-size', id: 'dashboardSize' },
    { target: 'dashboard-cabinet', id: 'dashboardCabinet' },
    { target: 'header-language', id: 'headerLanguage' },
    { target: 'header-handbook', id: 'headerHandbook' },
  ],
  game: [
    { target: 'game-nav', id: 'gameNav' },
    { target: 'game-live', id: 'gameLive' },
    { target: 'game-twitch', id: 'gameTwitch' },
    { target: 'header-language', id: 'headerLanguage' },
    { target: 'settings', id: 'settings' },
    { target: 'header-handbook', id: 'headerHandbook' },
  ],
}

const STORAGE_KEY = 'pv_hints_seen'
// Fallback for browsers that refuse storage: the hints then come once per page session
const seenInThisSession = new Set<HintScreen>()

function isSeen(screen: HintScreen): boolean {
  if (seenInThisSession.has(screen)) return true
  try {
    const stored = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}') as Record<string, boolean> | null
    return Boolean(stored?.[screen])
  } catch {
    return false
  }
}

function markSeen(screen: HintScreen): void {
  seenInThisSession.add(screen)
  try {
    const stored = (JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}') as Record<string, boolean> | null) ?? {}
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ ...stored, [screen]: true }))
  } catch {
    // Storage unavailable: the in-memory flag above keeps it quiet until the page is reloaded
  }
}

/** Forgets that any screen was seen: every page shows its hints again (the next time, or right now on the open page). */
export function resetSeenHints(): void {
  seenInThisSession.clear()
  try {
    localStorage.removeItem(STORAGE_KEY)
  } catch {
    // Storage unavailable: nothing was stored
  }
  window.dispatchEvent(new Event(HINTS_RESET_EVENT))
}

/** Hints of this screen whose control is on the page right now (visible, with a size). */
export function findHintTargets(screen: HintScreen): HintDefinition[] {
  return HINTS[screen].filter((hint) => {
    const element = document.querySelector<HTMLElement>(`[data-hint="${hint.target}"]`)
    if (!element) return false
    const rect = element.getBoundingClientRect()
    return rect.width > 0 && rect.height > 0
  })
}

/**
 * Coach marks of one screen: shown by themselves the first time the screen is seen (once its controls exist), never
 * again on their own afterwards; `show` (the "?" button) brings them back on demand. `suspended` keeps them away
 * while something else owns the screen (intro tour, settings, dialogs).
 */
export function useScreenHints(screen: HintScreen, suspended: boolean) {
  const [open, setOpen] = useState(false)
  // Bumped when the seen-flags are cleared, so the first-visit check below runs again
  const [resetTick, setResetTick] = useState(0)

  useEffect(() => {
    const onReset = () => setResetTick((tick) => tick + 1)
    window.addEventListener(HINTS_RESET_EVENT, onReset)
    return () => window.removeEventListener(HINTS_RESET_EVENT, onReset)
  }, [])

  useEffect(() => {
    if (suspended || open || isSeen(screen)) return
    let tries = 0
    const timer = window.setInterval(() => {
      tries += 1
      if (findHintTargets(screen).length >= 2) {
        window.clearInterval(timer)
        markSeen(screen)
        setOpen(true)
      } else if (tries >= 24) {
        window.clearInterval(timer)
      }
    }, 500)
    return () => window.clearInterval(timer)
  }, [screen, suspended, open, resetTick])

  const show = useCallback(() => {
    if (findHintTargets(screen).length > 0) {
      markSeen(screen)
      setOpen(true)
    }
  }, [screen])
  const close = useCallback(() => setOpen(false), [])

  return { screen, open: open && !suspended, show, close }
}
