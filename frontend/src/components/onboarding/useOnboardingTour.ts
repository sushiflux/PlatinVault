import { useCallback, useEffect, useState } from 'react'

// Fired by the "Restart the introduction" button in the settings (System tab)
export const START_TOUR_EVENT = 'pv-start-tour'

// Fired when the tour was finished or skipped, for whoever waits until the introduction is over ("What's new" after an update)
export const TOUR_DONE_EVENT = 'pv-tour-done'

// Per-browser flag: set once the tour was finished or skipped (the "_v1" lets a future, bigger tour show up again).
const STORAGE_KEY = 'pv_onboarding_done_v1'

// Fallback for browsers that refuse storage (private mode): the tour then shows once per page session.
let doneInThisSession = false

function isDone(): boolean {
  if (doneInThisSession) return true
  try {
    return localStorage.getItem(STORAGE_KEY) === '1'
  } catch {
    return false
  }
}

function markDone(): void {
  doneInThisSession = true
  try {
    localStorage.setItem(STORAGE_KEY, '1')
  } catch {
    // Storage unavailable: the in-memory flag above keeps it quiet until the page is reloaded
  }
}

// Open state of the onboarding tour. Starts open on the very first visit of this browser; `start` reopens it at
// step 1 (the "?" button), `finish` closes it and remembers that the visitor has seen it.
export function useOnboardingTour() {
  const [open, setOpen] = useState<boolean>(() => {
    // Returning from the Steam login (?steamLink=...) means the visitor is in the middle of something: stay out of the way
    if (new URLSearchParams(window.location.search).has('steamLink')) return false
    return !isDone()
  })

  const start = useCallback(() => setOpen(true), [])
  useEffect(() => {
    window.addEventListener(START_TOUR_EVENT, start)
    return () => window.removeEventListener(START_TOUR_EVENT, start)
  }, [start])
  const finish = useCallback(() => {
    markDone()
    window.dispatchEvent(new Event(TOUR_DONE_EVENT))
    setOpen(false)
  }, [])

  return { open, start, finish }
}


/** True once the first-start tour was finished or skipped in this browser; follows the tour while it is running. */
export function useOnboardingDone(): boolean {
  const [done, setDone] = useState<boolean>(() => isDone())
  useEffect(() => {
    const onDone = () => setDone(true)
    window.addEventListener(TOUR_DONE_EVENT, onDone)
    return () => window.removeEventListener(TOUR_DONE_EVENT, onDone)
  }, [])
  return done
}
