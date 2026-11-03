import { describe, expect, it } from 'vitest'
import { de, en } from './resources'

// One rarity scale for the whole app (live list, popups, statistics). The statistics bands are the source of the five names; the popup
// preview in the settings and the demo popups of the intro tour must use exactly the same words in each language - they once said
// "Extremely rare" next to "Ultra rare".

const resources = { de, en } as const

describe.each(['de', 'en'] as const)('rarity names (%s)', (language) => {
  const bands = resources[language].stats.rarity.band
  const demo = resources[language].onboarding.steps.live.visual.demo

  it('uses the band names of the statistics in the settings preview', () => {
    expect(resources[language].settings.live.preview.rarity).toBe(bands.ultra.label)
  })

  it('uses the band names of the statistics in the demo popups of the intro tour', () => {
    expect(demo.gold.label).toBe(bands.ultra.label)
    expect(demo.silver.label).toBe(bands.rare.label)
    expect(demo.bronze.label).toBe(bands.uncommon.label)
  })

  it('has five different names', () => {
    const names = Object.values(bands).map((band) => band.label)
    expect(new Set(names).size).toBe(5)
  })
})

describe('rarity names (English wording)', () => {
  it('are the same five words the server sends as ready-made labels', () => {
    // ServerTexts.Live.RarityLabel (Localization/ServerTexts.cs)
    const bands = en.stats.rarity.band
    expect([bands.ultra.label, bands.very.label, bands.rare.label, bands.uncommon.label, bands.common.label]).toEqual([
      'Ultra rare',
      'Very rare',
      'Rare',
      'Uncommon',
      'Common',
    ])
  })

  it('are the German wording of the same scale', () => {
    const bands = de.stats.rarity.band
    expect([bands.ultra.label, bands.very.label, bands.rare.label, bands.uncommon.label, bands.common.label]).toEqual([
      'Extrem selten',
      'Sehr selten',
      'Selten',
      'Ungewöhnlich',
      'Häufig',
    ])
  })
})
