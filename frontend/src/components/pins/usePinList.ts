import { useEffect, useMemo, useRef, useState } from 'react'
import type { LiveAchievementItemDto } from '../../api/types'

/**
 * How long an unlocked pin that the server removed stays on its card as "done" before it slides out: as long as the trophy popup
 * (setting "Anzeigedauer der Trophäen-Popups"); this is the default of 5 s, used until the setting is known.
 */
export const LEAVE_MS = 5000

export interface PinEntry {
  apiName: string
  /** The live data of the achievement; null while the data is still loading or when the game no longer has such an achievement. */
  ach: LiveAchievementItemDto | null
  /** An unlocked pin the server has already removed: shown as done until it slides out (cannot be unpinned any more). */
  leaving: boolean
}

interface Candidate {
  since: number
  index: number
}

interface Ghost {
  apiName: string
  index: number
  until: number
}

interface TrackerState {
  appId: number
  // The pinned names of the last render, to see which ones left
  names: string[]
  // Names that left the server's list without this window's doing and wait to be recognised as "unlocked"
  pending: Record<string, Candidate>
  ghosts: Ghost[]
}

// A candidate whose achievement is unlocked becomes a leaving card; one that does not unlock in time is forgotten. Returns the SAME object
// when nothing changes, so the render-phase update below settles.
function promote(state: TrackerState, byName: Map<string, LiveAchievementItemDto>, now: number, leaveMs: number): TrackerState {
  const keys = Object.keys(state.pending)
  if (keys.length === 0) return state
  const promoted: Ghost[] = []
  const pending: Record<string, Candidate> = {}
  for (const apiName of keys) {
    const entry = state.pending[apiName]!
    if (state.names.includes(apiName) || now - entry.since >= leaveMs) continue
    if (byName.get(apiName)?.achieved) promoted.push({ apiName, index: entry.index, until: entry.since + leaveMs })
    else pending[apiName] = entry
  }
  if (promoted.length === 0 && Object.keys(pending).length === keys.length) return state
  const ghosts = [...state.ghosts.filter((ghost) => !promoted.some((p) => p.apiName === ghost.apiName)), ...promoted]
  return { ...state, pending, ghosts }
}

/**
 * The cards to draw for the pinned names: every pinned name in order, plus - for leaveMs - the pins the server took away because the
 * achievement was unlocked ("remove after unlock"). Such a pin disappears from the server's list at once; the card is kept as a done
 * card so the player sees the unlock instead of a card vanishing. A pin the player removed here, or one whose achievement is not
 * unlocked, is simply gone (a candidate waits for the unlock to arrive in the live data, which may come after the pin change).
 * The bookkeeping happens while rendering, so the card never flickers through a frame in which it already started to slide out.
 */
export function usePinList(
  appId: number,
  names: string[],
  achievements: LiveAchievementItemDto[] | undefined,
  removedHere: (apiName: string) => boolean,
  leaveMs: number = LEAVE_MS
): PinEntry[] {
  const byName = useMemo(() => new Map((achievements ?? []).map((ach) => [ach.apiName, ach])), [achievements])
  const [tracked, setTracked] = useState<TrackerState>(() => ({ appId, names, pending: {}, ghosts: [] }))
  const removedHereRef = useRef(removedHere)
  removedHereRef.current = removedHere

  let state = tracked
  const now = Date.now()
  if (state.appId !== appId) {
    // Another game: its pins have nothing to do with the cards of the one before
    state = { appId, names, pending: {}, ghosts: [] }
  } else if (state.names !== names) {
    const pending = { ...state.pending }
    state.names.forEach((apiName, index) => {
      if (!names.includes(apiName) && !removedHereRef.current(apiName)) pending[apiName] = { since: now, index }
    })
    state = { ...state, names, pending }
  }
  state = promote(state, byName, now, leaveMs)
  if (state !== tracked) setTracked(state)

  // A timer for the next expiry: a ghost slides out, a candidate is dropped
  const { ghosts, pending } = state
  useEffect(() => {
    const times = [...ghosts.map((ghost) => ghost.until), ...Object.values(pending).map((candidate) => candidate.since + leaveMs)]
    if (times.length === 0) return
    const wait = Math.max(0, Math.min(...times) - Date.now()) + 20
    const timer = window.setTimeout(() => {
      const at = Date.now()
      setTracked((current) => ({
        ...current,
        ghosts: current.ghosts.filter((ghost) => ghost.until > at),
        pending: Object.fromEntries(Object.entries(current.pending).filter(([, candidate]) => candidate.since + leaveMs > at)),
      }))
    }, wait)
    return () => window.clearTimeout(timer)
  }, [ghosts, pending, leaveMs])

  const entries: PinEntry[] = names.map((apiName) => ({ apiName, ach: byName.get(apiName) ?? null, leaving: false }))
  const leaving = ghosts.filter((ghost) => ghost.until > now && !names.includes(ghost.apiName)).sort((a, b) => a.index - b.index)
  for (const ghost of leaving) {
    entries.splice(Math.min(ghost.index, entries.length), 0, { apiName: ghost.apiName, ach: byName.get(ghost.apiName) ?? null, leaving: true })
  }
  return entries
}
