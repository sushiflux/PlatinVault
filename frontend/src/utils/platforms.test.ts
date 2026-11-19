import { describe, expect, it } from 'vitest'
import { hasPlatinumGrade, hasTrophies, isManualGame, isPlatformId, MANUAL_PLATFORM_IDS, PLATFORM_IDS, platformLabelKey, platformOf, trophyKind, variantsOf } from './platforms'
import de from '../i18n/de/common'
import en from '../i18n/en/common'

describe('platforms', () => {
  it('knows the same ids as the server and a name for each', () => {
    expect(PLATFORM_IDS).toEqual(['steam', 'playstation', 'xbox', 'epic', 'gog', 'nintendo', 'ubisoft', 'ea', 'battlenet'])
    expect(MANUAL_PLATFORM_IDS).not.toContain('steam')
    for (const id of PLATFORM_IDS) {
      expect(de.platforms[platformLabelKey(id)]).toBeTruthy()
      expect(en.platforms[platformLabelKey(id)]).toBeTruthy()
    }
  })

  it('names the PlayStation variants and ignores a variant elsewhere', () => {
    expect(variantsOf('playstation')).toEqual(['ps5', 'ps4'])
    expect(variantsOf('xbox')).toEqual([])
    expect(platformLabelKey('playstation', 'ps5')).toBe('ps5')
    expect(platformLabelKey('playstation', null)).toBe('playstation')
    expect(platformLabelKey('xbox', 'ps4')).toBe('xbox')
    expect(de.platforms.ps4).toBe('PlayStation 4')
  })

  it('treats a missing platform as Steam', () => {
    expect(platformOf({})).toBe('steam')
    expect(platformOf({ platform: 'stadia' })).toBe('steam')
    expect(isManualGame({ platform: 'steam' })).toBe(false)
    expect(isManualGame({ platform: 'gog' })).toBe(true)
    expect(isPlatformId('ea')).toBe(true)
  })

  it('follows the trophy rules of each platform', () => {
    expect(hasTrophies('nintendo')).toBe(false)
    expect(hasTrophies('xbox')).toBe(true)
    expect(trophyKind('playstation')).toBe('trophies')
    expect(trophyKind('epic')).toBe('achievements')
    expect(hasPlatinumGrade('playstation')).toBe(true)
    expect(hasPlatinumGrade('xbox')).toBe(false)
  })
})
