import { createContext, useCallback, useContext } from 'react'
import { useNavigate } from 'react-router'
import type { SettingsTab } from '../SettingsModal'
import { START_TOUR_EVENT } from '../onboarding/useOnboardingTour'

// What the app shell offers the pages inside it. One onboarding tour for the whole app lives in the shell; pages only need to know
// whether it is open (their coach marks wait for it) and how to start it (the "full introduction" link of a hint card).
export interface ShellApi {
  tourOpen: boolean
  startTour: () => void
}

const fallback: ShellApi = {
  tourOpen: false,
  // Without a shell (tests of a single page): the shell's tour listens to the same event
  startTour: () => window.dispatchEvent(new Event(START_TOUR_EVENT)),
}

export const ShellContext = createContext<ShellApi>(fallback)

export function useShell(): ShellApi {
  return useContext(ShellContext)
}

/** Settings are a page (/settings?tab=...): every "open the settings" of the app goes there. Saving never leaves that page. */
export function settingsHref(tab?: SettingsTab): string {
  return tab ? `/settings?tab=${tab}` : '/settings'
}

export function useOpenSettings(): (tab?: SettingsTab) => void {
  const navigate = useNavigate()
  return useCallback((tab?: SettingsTab) => void navigate(settingsHref(tab)), [navigate])
}

// Fired by the shell's "?" button. A page that has hints for its screen handles it (shows them) and calls preventDefault; when no one
// does, the shell starts the full introduction instead.
export const SHOW_HINTS_EVENT = 'pv-show-hints'

/** Asks the open page to show its hints; false = no page took it. */
export function requestPageHints(): boolean {
  const event = new Event(SHOW_HINTS_EVENT, { cancelable: true })
  return !window.dispatchEvent(event)
}
