import { useEffect } from 'react'

// "This window is busy with something long": while any component that called `useWindowBusy()` is mounted, automatic page changes
// (for example the switch to a game that was just started, see useGameStartSwitch) hold back, because they would pull the
// user away from a running AI job. The counter lives outside React on purpose: the reader is not a component.
let busyCount = 0

/** True while at least one long job of this window runs. */
export function isWindowBusy(): boolean {
  return busyCount > 0
}

/** Marks this window as busy for as long as the calling component is mounted (and `active` is true). */
export function useWindowBusy(active = true): void {
  useEffect(() => {
    if (!active) return
    busyCount += 1
    return () => {
      busyCount -= 1
    }
  }, [active])
}
