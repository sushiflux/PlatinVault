import type { PopupTarget } from '../api/types'

/** The three choices of "Trophäen-Popup anzeigen", in display order; the server has the same list (SettingsStore.PopupTargets). */
export const POPUP_TARGETS: PopupTarget[] = ['app', 'overlay', 'both']

export const DEFAULT_POPUP_TARGET: PopupTarget = 'both'

/** Any stored or submitted value as one of the three choices; unknown or missing values are the default ("both"). */
export function normalizePopupTarget(value: string | null | undefined): PopupTarget {
  const v = (value ?? '').trim().toLowerCase()
  return (POPUP_TARGETS as string[]).includes(v) ? (v as PopupTarget) : DEFAULT_POPUP_TARGET
}

/**
 * The target that actually applies: an overlay exists only in the main window of the Windows program while its overlay works
 * (the program says so, see utils/programWindow.ts). Everywhere else - browser, Unraid/Docker, a phone, the program while its
 * overlay is unavailable - the popup shows in the window ("app"), whatever was chosen.
 */
export function effectivePopupTarget(target: string | null | undefined, overlayAvailable: boolean): PopupTarget {
  return overlayAvailable ? normalizePopupTarget(target) : 'app'
}

/** Where an event comes from: 'manual' = a trophy the player ticked by hand in a game added by hand; anything else = detected by Steam. */
export type PopupSource = 'manual' | null | undefined

/**
 * Whether this window draws the popup itself. Only "only in the game" hands it over to the overlay completely - except for an event the player
 * caused by ticking a trophy by hand (source 'manual'): the server never sends those to the Windows overlay (there is no game running to show
 * them over), so this window always shows them, whatever "Trophäen-Popup anzeigen" says.
 * The SOUND is not decided here: the window always plays it (once per unlock, also with "overlay" and "both"), the overlay of the
 * Windows program is silent - so the sound is never doubled, and it plays even when the overlay is not shown.
 */
export function showsPopupInWindow(target: string | null | undefined, overlayAvailable: boolean, source?: PopupSource): boolean {
  if (source === 'manual') return true
  return effectivePopupTarget(target, overlayAvailable) !== 'overlay'
}
