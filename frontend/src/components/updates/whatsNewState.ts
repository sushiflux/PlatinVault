import { useSyncExternalStore } from 'react'

// Whether the "What's new" window is open right now. Other first-start helpers (the page hints) stay away while it is, so two
// overlays never compete for the same moment.
let open = false
const listeners = new Set<() => void>()

export function setWhatsNewOpen(value: boolean): void {
  if (open === value) return
  open = value
  listeners.forEach((listener) => listener())
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useWhatsNewOpen(): boolean {
  return useSyncExternalStore(subscribe, () => open, () => false)
}
