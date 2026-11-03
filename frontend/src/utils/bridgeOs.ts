import type { BridgeOs } from '../api/types'

/**
 * The bridge download that fits the computer the browser runs on: Linux for a Linux desktop or a Steam Deck, otherwise Windows.
 * Android also says "Linux" in its user agent, but a phone is never the gaming PC, so it keeps the Windows default. The user can always
 * switch; this only chooses what is pre-selected.
 */
export function defaultBridgeOs(userAgent: string = typeof navigator === 'undefined' ? '' : navigator.userAgent): BridgeOs {
  return /Linux|X11/i.test(userAgent) && !/Android/i.test(userAgent) ? 'linux' : 'windows'
}
