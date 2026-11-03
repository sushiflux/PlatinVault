import { useSyncExternalStore } from 'react'

// The channel between a page and the Windows program that shows it (WebView2: window.chrome.webview). In a browser it does not
// exist and everything here stays quiet. Messages are plain objects with one "pv..." key, so they never collide with other uses:
//   page -> program: { pvHostHello: true }                      the main window asks for the program's state
//   program -> page: { pvHost: { overlay: boolean } }           whether the in-game overlay works right now
//   program -> page: { pvSettingsChanged: true }                a setting was changed outside the page (tray menu)

/** The part of window.chrome.webview this module uses (a fake in tests). */
export interface WebViewChannel {
  postMessage(message: unknown): void
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void
  removeEventListener(type: 'message', listener: (event: { data: unknown }) => void): void
}

/** The WebView2 channel of the page, or null in a normal browser. */
export function webViewChannel(): WebViewChannel | null {
  if (typeof window === 'undefined') return null
  const chrome = (window as unknown as { chrome?: { webview?: WebViewChannel } }).chrome
  return chrome?.webview ?? null
}

export interface ProgramWindowState {
  // True only inside the main window of the Windows program, once it said that its overlay works
  overlay: boolean
}

const NOT_IN_PROGRAM: ProgramWindowState = { overlay: false }

let state: ProgramWindowState = NOT_IN_PROGRAM
const listeners = new Set<() => void>()

function setState(next: ProgramWindowState) {
  if (next.overlay === state.overlay) return
  state = next
  for (const listener of listeners) listener()
}

/** Reads one message of the program; returns what it means for this page (null = not for this module). */
export function readProgramMessage(data: unknown): { host: ProgramWindowState } | { settingsChanged: true } | null {
  if (!data || typeof data !== 'object') return null
  const message = data as { pvHost?: unknown; pvSettingsChanged?: unknown }
  if (message.pvHost && typeof message.pvHost === 'object') {
    const overlay = (message.pvHost as { overlay?: unknown }).overlay
    return { host: { overlay: overlay === true } }
  }
  if (message.pvSettingsChanged === true) return { settingsChanged: true }
  return null
}

/**
 * Listens to the program (main window only, once per page): keeps the state current and calls onSettingsChanged when a setting
 * was changed outside the page. Returns the function that stops listening. Does nothing in a browser.
 */
export function connectProgramWindow(
  onSettingsChanged: () => void,
  channel: WebViewChannel | null = webViewChannel()
): () => void {
  if (!channel) return () => {}
  const listener = (event: { data: unknown }) => {
    const message = readProgramMessage(event.data)
    if (!message) return
    if ('host' in message) setState(message.host)
    else onSettingsChanged()
  }
  channel.addEventListener('message', listener)
  try {
    channel.postMessage({ pvHostHello: true })
  } catch {
    // The program closes: nothing to ask any more
  }
  return () => {
    channel.removeEventListener('message', listener)
  }
}

/** The program's state for components (re-renders when it changes). */
export function useProgramWindow(): ProgramWindowState {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener)
      return () => {
        listeners.delete(listener)
      }
    },
    () => state,
    () => NOT_IN_PROGRAM
  )
}

/** Tests only: back to "not in the program". */
export function resetProgramWindowForTests() {
  state = NOT_IN_PROGRAM
  for (const listener of listeners) listener()
}
