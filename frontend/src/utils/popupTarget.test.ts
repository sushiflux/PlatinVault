import { describe, expect, it } from 'vitest'
import { effectivePopupTarget, normalizePopupTarget, POPUP_TARGETS, showsPopupInWindow } from './popupTarget'

describe('popupTarget: "Trophäen-Popup anzeigen"', () => {
  it('knows the three choices of the server and defaults to "both"', () => {
    expect(POPUP_TARGETS).toEqual(['app', 'overlay', 'both'])
    expect(normalizePopupTarget(undefined)).toBe('both')
    expect(normalizePopupTarget('sideways')).toBe('both')
    expect(normalizePopupTarget(' Overlay ')).toBe('overlay')
  })

  it('without a working overlay (browser, Unraid, the program while its overlay is down) the effect is always "app"', () => {
    for (const target of POPUP_TARGETS) {
      expect(effectivePopupTarget(target, false)).toBe('app')
      expect(showsPopupInWindow(target, false)).toBe(true)
    }
  })

  it('in the program window only "only in the game" hands the popup over to the overlay', () => {
    expect(showsPopupInWindow('overlay', true)).toBe(false)
    expect(showsPopupInWindow('both', true)).toBe(true)
    expect(showsPopupInWindow('app', true)).toBe(true)
    expect(showsPopupInWindow(null, true)).toBe(true)
  })
})
