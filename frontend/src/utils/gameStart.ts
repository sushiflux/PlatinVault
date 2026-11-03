// Pure parts of "switch to the game that was just started" (see useGameStartSwitch): telling a real start from everything that only
// looks like one, and the checks that decide whether this window may be moved right now.

/**
 * The same game seen again within this time after it was last seen running is not a new start: a bridge that was silent for a while,
 * a server that was restarted while the game kept running, or a game that crashed and was relaunched at once. The bridge reports at
 * least once a minute, so a restart of the server can show "no game" for up to that long.
 */
export const RESTART_GRACE_MS = 120_000

/**
 * Two observations further apart than this mean the window was not watching (sleeping computer, frozen background tab): whatever
 * changed meanwhile is not a start "just now", the next observation only becomes the new baseline.
 */
export const MAX_OBSERVATION_GAP_MS = 120_000

export interface StartTracker {
  // False until the first observation: the game that is already running when the page opens is the baseline, never a start
  baselined: boolean
  // The running AppID of the last observation, 0 = no game
  appId: number
  // When the last observation was made
  at: number
  // The last game seen running and when (survives the game stopping, for the restart grace)
  lastRunning: { appId: number; at: number } | null
}

export function newStartTracker(): StartTracker {
  return { baselined: false, appId: 0, at: 0, lastRunning: null }
}

/**
 * Feeds one observation of the running game (AppID, 0 = none) and returns the AppID when a game was just STARTED, else null.
 * A start is: nothing (or another game) before, a game now - and not the same game that was running a moment ago. Staying on the same
 * game, a game stopping, the first observation of the page and an observation after a long silence are never starts.
 * The tracker is updated in place.
 */
export function observeRunningGame(tracker: StartTracker, appId: number, now: number): number | null {
  const watching = tracker.baselined && now - tracker.at <= MAX_OBSERVATION_GAP_MS
  const previous = tracker.appId
  const remembered = tracker.lastRunning

  tracker.baselined = true
  tracker.appId = appId
  tracker.at = now
  if (appId > 0) tracker.lastRunning = { appId, at: now }

  if (!watching || appId <= 0 || appId === previous) return null
  const seenJustBefore = remembered !== null && remembered.appId === appId && now - remembered.at < RESTART_GRACE_MS
  return seenJustBefore ? null : appId
}

// Input types in which the user types text; a focused checkbox, button or slider is not "typing"
const TEXT_INPUT_TYPES = new Set(['text', 'search', 'email', 'url', 'tel', 'password', 'number', 'date', 'datetime-local', 'month', 'time', 'week'])

/** True while the cursor is in a text field of this page (a search box, a note, an API key field). */
export function isTypingInTextField(doc: Document = document): boolean {
  const el = doc.activeElement
  if (!(el instanceof HTMLElement)) return false
  if (el.isContentEditable) return true
  if (el instanceof HTMLTextAreaElement) return !el.readOnly && !el.disabled
  if (el instanceof HTMLInputElement) return TEXT_INPUT_TYPES.has(el.type) && !el.readOnly && !el.disabled
  return false
}

/** True while a dialog / modal of the app is open (same markers the floating notices use for their Esc handling). */
export function isDialogOpen(doc: Document = document): boolean {
  return doc.querySelector('.pv-modal-backdrop, [role="dialog"], [role="alertdialog"]') !== null
}

/** Whether `pathname` is the page of the game with this slug (any tab). */
export function isGamePagePath(pathname: string, slug: string): boolean {
  const encoded = /^\/games\/([^/]+)\/?$/.exec(pathname)?.[1]
  if (encoded === undefined) return false
  try {
    return decodeURIComponent(encoded) === slug
  } catch {
    return encoded === slug
  }
}

/** The page to switch to: the game, on the Live trophies section (what matters while playing). */
export function gameLivePath(slug: string): string {
  return `/games/${encodeURIComponent(slug)}?tab=live`
}
