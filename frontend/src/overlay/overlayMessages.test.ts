import { describe, expect, it } from 'vitest'
import { isOverlayPage } from './overlayPage'
import { overlayStateMessage, readOverlayEvent } from './overlayMessages'

const achievement = { apiName: 'ACH_1', displayName: 'First Blood', rarityTier: 'Gold' }

describe('overlay messages of the Windows program', () => {
  it('reads an unlock with game, position, duration and language', () => {
    expect(
      readOverlayEvent({ pvOverlay: { kind: 'achievement', achievement, gameName: 'Elden Ring', position: 'fullscreen', seconds: 8, language: 'en' } })
    ).toEqual({ kind: 'achievement', achievement, gameName: 'Elden Ring', position: 'fullscreen', seconds: 8, language: 'en' })
  })

  it('takes the popup duration as whole seconds within 2..10 and 5 when the program sends none', () => {
    const seconds = (value: unknown) => {
      const event = readOverlayEvent({ pvOverlay: { kind: 'achievement', achievement, seconds: value } })
      return event?.kind === 'achievement' ? event.seconds : null
    }
    expect(seconds(2)).toBe(2)
    expect(seconds(10)).toBe(10)
    expect(seconds(1)).toBe(2)
    expect(seconds(99)).toBe(10)
    expect(seconds(6.4)).toBe(6)
    expect(seconds(undefined)).toBe(5)
    expect(seconds('7')).toBe(5)
    expect(seconds(null)).toBe(5)
  })

  it('falls back to the default position and ignores unknown languages', () => {
    const event = readOverlayEvent({ pvOverlay: { kind: 'achievement', achievement, position: 'sideways', language: 'fr' } })
    expect(event).toMatchObject({ position: 'top-right', language: null, gameName: null })
  })

  it('reads the platinum moment and needs its game', () => {
    expect(readOverlayEvent({ pvOverlay: { kind: 'platinum', gameName: 'Elden Ring', total: 42, language: 'de' } })).toEqual({
      kind: 'platinum',
      gameName: 'Elden Ring',
      total: 42,
      language: 'de',
    })
    expect(readOverlayEvent({ pvOverlay: { kind: 'platinum', total: 42 } })).toBeNull()
  })

  it('ignores everything else', () => {
    expect(readOverlayEvent({ pvHost: { overlay: true } })).toBeNull()
    expect(readOverlayEvent({ pvOverlay: { kind: 'achievement', achievement: { apiName: 1 } } })).toBeNull()
    expect(readOverlayEvent({ pvOverlay: { kind: 'other' } })).toBeNull()
    expect(readOverlayEvent('pvOverlay')).toBeNull()
  })

  it('answers with its state and the number of events it has received', () => {
    expect(overlayStateMessage('idle', 3)).toEqual({ pvOverlayState: 'idle', received: 3 })
  })

  it('knows its route', () => {
    expect(isOverlayPage('/overlay')).toBe(true)
    expect(isOverlayPage('/overlay/')).toBe(true)
    expect(isOverlayPage('/')).toBe(false)
    expect(isOverlayPage('/games/overlay')).toBe(false)
  })
})
