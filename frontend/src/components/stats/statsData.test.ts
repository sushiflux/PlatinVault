import { describe, expect, it } from 'vitest'
import type { LiveAchievementItemDto, PlaytimeDayDto } from '../../api/types'
import {
  bandOf,
  barWindow,
  bestSlot,
  countTicks,
  dayBuckets,
  easiestOpen,
  estimateToPlatinum,
  exclusivityBound,
  hasRarityData,
  labelIndices,
  missingShareCount,
  niceTicks,
  rarityBands,
  roundHours,
  unlocksWithTime,
  weekdayHourMatrix,
  weekdayNames,
  type EstimateInput,
  type Unlock,
} from './statsData'

function item(over: Partial<LiveAchievementItemDto>): LiveAchievementItemDto {
  return {
    apiName: over.displayName ?? 'x',
    displayName: 'x',
    description: '',
    originalDescription: null,
    isTranslated: false,
    iconUrl: '',
    iconGrayUrl: '',
    achieved: false,
    unlockTime: null,
    globalPercent: 10,
    hasGlobalPercent: true,
    rarityTier: 'Silver',
    rarityLabel: '',
    isHidden: false,
    userNote: '',
    ...over,
  }
}

describe('rarity bands', () => {
  it('puts the boundaries into the upper band', () => {
    expect(bandOf(4.9).key).toBe('ultra')
    expect(bandOf(5).key).toBe('very')
    expect(bandOf(20).key).toBe('uncommon')
    expect(bandOf(50).key).toBe('common')
    expect(bandOf(100).key).toBe('common')
  })

  it('counts unlocked and open per band', () => {
    const rows = rarityBands([
      item({ globalPercent: 1, achieved: true }),
      item({ globalPercent: 2 }),
      item({ globalPercent: 70, achieved: true }),
    ])
    expect(rows[0]).toMatchObject({ unlocked: 1, open: 1, total: 2 })
    expect(rows[4]).toMatchObject({ unlocked: 1, open: 0, total: 1 })
  })

  it('treats a list without a single real worldwide share as no rarity data', () => {
    const missing = { hasGlobalPercent: false, globalPercent: 25 }
    expect(hasRarityData([item(missing), item(missing), item(missing)])).toBe(false)
    expect(hasRarityData([item(missing), item({ globalPercent: 3 }), item(missing)])).toBe(true)
    expect(hasRarityData([])).toBe(false)
  })

  it('does not mistake a real 25 % for a placeholder: the marker decides, not the value', () => {
    expect(hasRarityData([item({ globalPercent: 25 }), item({ globalPercent: 25 }), item({ globalPercent: 25 })])).toBe(true)
  })

  it('treats a missing marker (older backend) as known', () => {
    const legacy = { ...item({ globalPercent: 8 }), hasGlobalPercent: undefined } as unknown as LiveAchievementItemDto
    expect(hasRarityData([legacy])).toBe(true)
    expect(missingShareCount([legacy])).toBe(0)
    expect(rarityBands([legacy])[1]).toMatchObject({ total: 1 })
  })

  it('leaves a single missing value out of bands, exclusivity bound and easiest open', () => {
    const items = [
      item({ displayName: 'real-rare', globalPercent: 3, achieved: true }),
      item({ displayName: 'real-common', globalPercent: 60 }),
      // Steam gave no share for this one; the backend put a placeholder 0.4 here that would otherwise be the "rarest"
      item({ displayName: 'missing', globalPercent: 0.4, hasGlobalPercent: false }),
    ]
    expect(hasRarityData(items)).toBe(true)
    expect(missingShareCount(items)).toBe(1)

    const rows = rarityBands(items)
    expect(rows.reduce((sum, r) => sum + r.total, 0)).toBe(2)
    expect(rows[0]).toMatchObject({ unlocked: 1, open: 0, total: 1 })
    expect(rows[4]).toMatchObject({ unlocked: 0, open: 1, total: 1 })

    expect(exclusivityBound(items)).toEqual({ percent: 3, name: 'real-rare', known: 2, total: 3 })
    expect(easiestOpen(items).map((a) => a.displayName)).toEqual(['real-common'])
  })

  it('carries no worldwide share into the unlock history when Steam gave none', () => {
    const list = unlocksWithTime([
      item({ achieved: true, unlockTime: '2026-05-01T10:00:00Z', globalPercent: 25, hasGlobalPercent: false }),
      item({ achieved: true, unlockTime: '2026-05-02T10:00:00Z', globalPercent: 7.5 }),
    ])
    expect(list.map((u) => u.percent)).toEqual([null, 7.5])
  })
})

describe('exclusivity bound and easiest open', () => {
  it('uses the smallest positive share', () => {
    const bound = exclusivityBound([
      item({ displayName: 'a', globalPercent: 40 }),
      item({ displayName: 'b', globalPercent: 1.8 }),
      item({ displayName: 'c', globalPercent: 0 }),
    ])
    expect(bound).toEqual({ percent: 1.8, name: 'b', known: 2, total: 3 })
    expect(exclusivityBound([])).toBeNull()
  })

  it('lists open achievements from most to least common', () => {
    const list = easiestOpen([
      item({ displayName: 'rare', globalPercent: 2 }),
      item({ displayName: 'done', globalPercent: 90, achieved: true }),
      item({ displayName: 'easy', globalPercent: 60 }),
    ])
    expect(list.map((a) => a.displayName)).toEqual(['easy', 'rare'])
  })
})

describe('unlock history', () => {
  const at = (iso: string) => item({ achieved: true, unlockTime: iso, displayName: iso })

  it('ignores unlocks without a plausible time', () => {
    const list = unlocksWithTime([at('2026-05-01T10:00:00Z'), item({ achieved: true, unlockTime: null }), at('1970-01-01T00:00:00Z')])
    expect(list).toHaveLength(1)
  })

  it('groups by local day, one bucket per day with an unlock and nothing in between', () => {
    const unlocks = unlocksWithTime([
      at('2026-05-01T10:00:00'),
      at('2026-05-01T10:30:00'),
      at('2026-05-01T11:00:00'),
      at('2026-05-02T10:00:00'),
      at('2026-05-03T10:00:00'),
      at('2026-05-03T10:10:00'),
    ])
    const buckets = dayBuckets(unlocks)
    expect(buckets.map((b) => b.count)).toEqual([3, 1, 2])
    // the unlocks of a day are kept in order (the tooltip lists them)
    expect(buckets[0]?.unlocks.map((u) => u.name)).toEqual(['2026-05-01T10:00:00', '2026-05-01T10:30:00', '2026-05-01T11:00:00'])
    expect(buckets[0]?.names).toEqual(buckets[0]?.unlocks.map((u) => u.name))
  })

  it('skips days without an unlock instead of filling them with empty buckets', () => {
    const buckets = dayBuckets(unlocksWithTime([at('2026-05-01T10:00:00'), at('2026-05-20T10:00:00')]))
    expect(buckets).toHaveLength(2)
    expect(buckets.map((b) => b.key)).toEqual(['2026-05-01', '2026-05-20'])
  })

  it('builds the weekday x hour matrix Monday first and finds the best slot', () => {
    // 2026-06-01 is a Monday; local time of the test machine is used on both sides
    const monday = new Date(2026, 5, 1, 20, 15).toISOString()
    const sunday = new Date(2026, 5, 7, 3, 0).toISOString()
    const matrix = weekdayHourMatrix(unlocksWithTime([at(monday), at(monday), at(sunday)]))
    expect(matrix[0]?.[20]).toBe(2)
    expect(matrix[6]?.[3]).toBe(1)
    expect(bestSlot(matrix)).toEqual({ weekday: 0, hour: 20, count: 2 })
    expect(bestSlot(weekdayHourMatrix([]))).toBeNull()
  })
})

describe('weekdayNames', () => {
  it('lists the weekdays Monday first in the language of the app', () => {
    expect(weekdayNames('de-DE', 'long')).toEqual(['Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag', 'Sonntag'])
    expect(weekdayNames('en-GB', 'long')).toEqual(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'])
  })

  it('drops the abbreviation dot of the short form', () => {
    expect(weekdayNames('de-DE', 'short')).toEqual(['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'])
    expect(weekdayNames('en-GB', 'short')).toEqual(['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'])
  })
})

describe('easiestOpen ties', () => {
  it('orders equal shares by name in the given locale', () => {
    const names = (locale: string) =>
      easiestOpen([item({ displayName: 'Zebra', globalPercent: 10 }), item({ displayName: 'Äpfel', globalPercent: 10 })], locale).map((a) => a.displayName)
    // German sorts "Ä" next to "A"; Swedish sorts it after "Z"
    expect(names('de-DE')).toEqual(['Äpfel', 'Zebra'])
    expect(names('sv-SE')).toEqual(['Zebra', 'Äpfel'])
  })
})

describe('roundHours', () => {
  it('keeps the precision honest', () => {
    expect(roundHours(7.3)).toBe(7.5)
    expect(roundHours(0.2)).toBe(0.5)
    expect(roundHours(18.4)).toBe(18)
    expect(roundHours(29.3)).toBe(30)
    expect(roundHours(126)).toBe(130)
    expect(roundHours(262)).toBe(250)
  })
})

describe('estimateToPlatinum', () => {
  const NOW = Date.parse('2026-10-31T12:00:00Z')
  const DAY = 86_400_000
  const unlocksAgo = (...days: number[]): Unlock[] => days.map((d, i) => ({ ts: NOW - d * DAY, name: `t${i}`, percent: 10 }))
  const dayOf = (daysAgo: number) => new Date(NOW - daysAgo * DAY).toISOString().slice(0, 10)
  // One recorded day per entry: [daysAgo, minutes]
  const played = (...rows: [number, number][]): PlaytimeDayDto[] => rows.map(([d, m]) => ({ date: dayOf(d), minutes: m }))

  const base: EstimateInput = {
    unlocked: 10,
    total: 40,
    playtimeHours: 20,
    igdbCompletelyHours: null,
    unlocks: [],
    playtimeDaily: null,
    now: NOW,
  }

  it('prefers the IGDB 100 % time while the own playtime is below it', () => {
    const result = estimateToPlatinum({ ...base, igdbCompletelyHours: 60 })
    expect(result).toMatchObject({ basis: 'igdb', hours: 40, shownHours: 40, igdbHours: 60, ownHours: 20 })
  })

  it('uses the pace of the last 28 days once the own time has passed IGDB', () => {
    // 6 unlocks and 6 h in the window (recording started 60 days ago) -> 1 trophy per hour, 30 left -> 30 h
    const result = estimateToPlatinum({
      ...base,
      playtimeHours: 80,
      igdbCompletelyHours: 60,
      unlocks: unlocksAgo(1, 2, 3, 4, 5, 6, 40),
      playtimeDaily: played([60, 0], [10, 180], [3, 180], [40, 600]),
    })
    expect(result).toMatchObject({ basis: 'recent', windowDays: 28, basisUnlocks: 6, hours: 30 })
    expect(result?.basisHours).toBeCloseTo(6)
    expect(result?.ratePerHour).toBeCloseTo(1)
  })

  it('shortens the window to what was recorded and ignores unlocks from before it', () => {
    // Recording started 5 days ago: 6 days (today included); the unlocks 20 days ago do not count
    const result = estimateToPlatinum({
      ...base,
      unlocks: unlocksAgo(0, 1, 2, 20, 20, 20, 20),
      playtimeDaily: played([5, 60], [2, 120], [0, 120]),
    })
    expect(result).toMatchObject({ basis: 'recent', windowDays: 6, basisUnlocks: 3 })
    expect(result?.basisHours).toBeCloseTo(5)
  })

  it('falls back to the overall pace when the last 28 days hold too little', () => {
    // only 2 unlocks in the window
    const fewUnlocks = estimateToPlatinum({ ...base, unlocks: unlocksAgo(1, 2, 50), playtimeDaily: played([60, 0], [3, 300]) })
    expect(fewUnlocks).toMatchObject({ basis: 'overall', hours: 60, ratePerHour: 0.5 })
    // 3 unlocks but only 1.5 h in the window
    const fewHours = estimateToPlatinum({ ...base, unlocks: unlocksAgo(1, 2, 3), playtimeDaily: played([60, 0], [3, 90]) })
    expect(fewHours?.basis).toBe('overall')
    // no history at all
    expect(estimateToPlatinum({ ...base, unlocks: unlocksAgo(1, 2, 3), playtimeDaily: null })?.basis).toBe('overall')
  })

  it('skips an implausible recent pace and takes the overall one if that is plausible', () => {
    // recent: 3 unlocks in 20 h = 0.15 per hour, 60 left -> 400 h (implausible); overall: 30 in 60 h -> 120 h... 60 left at 0.5 -> 120 h
    const result = estimateToPlatinum({
      unlocked: 30,
      total: 90,
      playtimeHours: 60,
      igdbCompletelyHours: null,
      unlocks: unlocksAgo(1, 2, 3),
      playtimeDaily: played([60, 0], [3, 1200]),
      now: NOW,
    })
    expect(result).toMatchObject({ basis: 'overall', hours: 120 })
  })

  it('hides the estimate when it is implausible: above 300 h or above 3 x the IGDB time', () => {
    expect(estimateToPlatinum({ ...base, unlocked: 3, total: 100, playtimeHours: 100 })).toBeNull()
    // IGDB 10 h, the player is past it; overall pace says 60 h = 6 x IGDB
    expect(estimateToPlatinum({ ...base, igdbCompletelyHours: 10 })).toBeNull()
    // the IGDB remainder itself is too long to be useful
    expect(estimateToPlatinum({ ...base, unlocked: 1, playtimeHours: 100, igdbCompletelyHours: 500 })).toBeNull()
  })

  it('stays silent on thin data or when nothing is left to do', () => {
    expect(estimateToPlatinum({ ...base, unlocked: 2 })).toBeNull()
    expect(estimateToPlatinum({ ...base, playtimeHours: 0.5 })).toBeNull()
    expect(estimateToPlatinum({ ...base, playtimeHours: null })).toBeNull()
    expect(estimateToPlatinum({ ...base, unlocked: 40 })).toBeNull()
    expect(estimateToPlatinum({ ...base, total: 0 })).toBeNull()
  })
})

describe('countTicks', () => {
  it('counts in whole steps up to four and in round steps beyond', () => {
    expect(countTicks(1)).toEqual([0, 1])
    expect(countTicks(3)).toEqual([0, 1, 2, 3])
    expect(countTicks(4)).toEqual([0, 1, 2, 3, 4])
    expect(countTicks(5)).toEqual([0, 2, 4, 6])
    expect(countTicks(23)).toEqual([0, 10, 20, 30])
    expect(countTicks(0)).toEqual([0])
  })

  it('only ever produces whole numbers that cover the maximum', () => {
    for (let max = 1; max <= 400; max++) {
      const ticks = countTicks(max)
      expect(ticks.every(Number.isInteger)).toBe(true)
      expect(ticks[ticks.length - 1]).toBeGreaterThanOrEqual(max)
    }
  })
})

describe('barWindow', () => {
  it('draws one day as one calm bar: capped at 120 px, no paging', () => {
    expect(barWindow(1000, 1)).toMatchObject({ visible: 1, paging: false, barW: 120 })
    expect(barWindow(1000, 2)).toMatchObject({ visible: 2, paging: false, barW: 120 })
  })

  it('lets a handful of days share the width, capped, with a wider gap for wide bars', () => {
    const seven = barWindow(1000, 7)
    expect(seven).toMatchObject({ visible: 7, paging: false, barW: 120 })
    expect(seven.gap).toBeGreaterThan(2)
    const fifteen = barWindow(1000, 15)
    expect(fifteen).toMatchObject({ visible: 15, paging: false })
    expect(fifteen.barW).toBeGreaterThan(24)
    expect(fifteen.barW * 15 + fifteen.gap * 14).toBeLessThanOrEqual(1000)
  })

  it('pages once the bars would be thinner than 24 px: a window of days with bars of 24 px or more', () => {
    const sixty = barWindow(1000, 60)
    expect(sixty.paging).toBe(true)
    expect(sixty.visible).toBe(sixty.windowSize)
    expect(sixty.barW).toBeGreaterThanOrEqual(24)
    expect(sixty.barW * sixty.visible + sixty.gap * (sixty.visible - 1)).toBeLessThanOrEqual(1000)
    // a narrow phone card pages earlier
    const phone = barWindow(200, 15)
    expect(phone.paging).toBe(true)
    expect(phone.visible).toBeLessThan(10)
  })

  it('never shows more than 45 days at once and never fewer than one', () => {
    expect(barWindow(6000, 200).visible).toBe(45)
    expect(barWindow(10, 5).visible).toBe(1)
    expect(barWindow(500, 0).visible).toBe(0)
  })
})

describe('labelIndices', () => {
  it('labels every day while the slots are wide enough', () => {
    expect(labelIndices(4, 60, 58)).toEqual([0, 1, 2, 3])
  })

  it('thins the labels to every k-th day so none collides', () => {
    // 10 px slots, 58 px per label -> every 6th
    expect(labelIndices(20, 10, 58)).toEqual([0, 6, 12, 18])
  })

  it('adds the last day only when there is room for its label', () => {
    expect(labelIndices(14, 10, 58)).toEqual([0, 6, 12])
    expect(labelIndices(19, 10, 58)).toEqual([0, 6, 12, 18])
    expect(labelIndices(1, 10, 58)).toEqual([0])
    expect(labelIndices(0, 10, 58)).toEqual([])
  })
})

describe('niceTicks', () => {
  it('returns round steps covering the maximum', () => {
    expect(niceTicks(42)).toEqual([0, 20, 40, 60])
    expect(niceTicks(0)).toEqual([0])
    expect(niceTicks(1197).at(-1)).toBeGreaterThanOrEqual(1197)
  })
})
