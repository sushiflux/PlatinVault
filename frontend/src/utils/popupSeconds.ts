// "Anzeigedauer der Trophäen-Popups" (setting live.popup_seconds): how long a trophy popup (card or full screen) stays on screen.
// The server has the same bounds (SettingsStore.MinPopupSeconds / MaxPopupSeconds / DefaultPopupSeconds).
export const MIN_POPUP_SECONDS = 2
export const MAX_POPUP_SECONDS = 10
export const DEFAULT_POPUP_SECONDS = 5

/** Any stored, received or typed value as whole seconds within the bounds; missing or unusable values are the default (5). */
export function normalizePopupSeconds(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) return DEFAULT_POPUP_SECONDS
  return Math.min(MAX_POPUP_SECONDS, Math.max(MIN_POPUP_SECONDS, Math.round(value)))
}

export const popupSecondsToMs = (seconds: unknown) => normalizePopupSeconds(seconds) * 1000
