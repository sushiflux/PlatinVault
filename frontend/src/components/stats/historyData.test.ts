import { describe, expect, it } from 'vitest'
import type { GameHistoryDto } from '../../api/types'
import { calendarWeeks, dailyFromRaw, playerSeries, playtimeSummary, segments, trophyDayMap } from './historyData'

const NOW = Date.parse('2026-10-31T12:00:00Z')

function rawSamples(count: number, everyMs: number, base = 100): GameHistoryDto['playerCounts'] {
  return Array.from({ length: count }, (_, i) => ({ at: new Date(NOW - (count - i) * everyMs).toISOString(), count: base + i }))
}

const emptyHistory: GameHistoryDto = { trackingSince: null, playerCounts: [], playerCountsDaily: [], playtimeDaily: [] }

describe('playerSeries', () => {
  it('shows a single sample (a dot) from the very first recording on', () => {
    const series = playerSeries({ ...emptyHistory, playerCounts: rawSamples(1, 1_800_000) }, 30, NOW)
    expect(series.mode).toBe('raw')
    expect(series.raw).toHaveLength(1)
    // also for the long ranges, where the daily band would be a single day
    expect(playerSeries({ ...emptyHistory, playerCounts: rawSamples(1, 1_800_000) }, 365, NOW).mode).toBe('raw')
  })

  it('says "none" only while nothing at all is recorded', () => {
    expect(playerSeries(emptyHistory, 30, NOW).mode).toBe('none')
  })

  it('uses raw samples up to 30 days', () => {
    const series = playerSeries({ ...emptyHistory, playerCounts: rawSamples(48, 1_800_000) }, 7, NOW)
    expect(series.mode).toBe('raw')
    expect(series.raw).toHaveLength(48)
  })

  it('builds the daily band from server days plus condensed recent raw days', () => {
    const history: GameHistoryDto = {
      ...emptyHistory,
      playerCounts: rawSamples(96, 1_800_000),
      playerCountsDaily: [
        { date: '2026-09-01', min: 10, avg: 20, max: 40 },
        { date: '2026-09-02', min: 12, avg: 22, max: 44 },
      ],
    }
    const series = playerSeries(history, 90, NOW)
    expect(series.mode).toBe('daily')
    expect(series.daily.length).toBeGreaterThanOrEqual(3)
    expect(series.daily[0]?.date).toBe('2026-09-01')
  })

  it('falls back to raw samples for a long range while the band would be a single day', () => {
    const series = playerSeries({ ...emptyHistory, playerCounts: rawSamples(20, 1_800_000) }, 365, NOW)
    expect(series.mode).toBe('raw')
  })
})

describe('segments', () => {
  it('splits at holes longer than the allowed gap', () => {
    expect(segments([0, 10, 20, 100, 110], 30)).toEqual([
      [0, 2],
      [3, 4],
    ])
    expect(segments([], 30)).toEqual([])
  })
})

describe('dailyFromRaw', () => {
  it('condenses samples to min / avg / max per UTC day', () => {
    const days = dailyFromRaw([
      { ts: Date.parse('2026-10-30T01:00:00Z'), count: 10 },
      { ts: Date.parse('2026-10-30T20:00:00Z'), count: 30 },
      { ts: Date.parse('2026-10-31T01:00:00Z'), count: 5 },
    ])
    expect(days).toHaveLength(2)
    expect(days[0]).toMatchObject({ date: '2026-10-30', min: 10, max: 30, avg: 20 })
  })
})

describe('calendar and summary', () => {
  const today = new Date(2026, 9, 31, 15) // Saturday 31 Oct 2026

  it('builds Monday-first columns ending with the current week', () => {
    const weeks = calendarWeeks([{ date: '2026-10-30', minutes: 90 }], 4, today)
    expect(weeks).toHaveLength(4)
    expect(weeks[3]?.[0]?.key).toBe('2026-10-26')
    const friday = weeks[3]?.[4]
    expect(friday).toMatchObject({ key: '2026-10-30', minutes: 90, recorded: true, future: false })
    expect(weeks[3]?.[6]?.future).toBe(true)
  })

  it('marks days with unlocked trophies, also before anything was recorded', () => {
    const unlocks = [
      { ts: new Date(2026, 9, 30, 20, 0).getTime(), name: 'A' },
      { ts: new Date(2026, 9, 30, 21, 0).getTime(), name: 'B' },
      { ts: new Date(2026, 8, 3, 12, 0).getTime(), name: 'C' },
    ]
    const trophyDays = trophyDayMap(unlocks)
    expect(trophyDays.get('2026-10-30')).toEqual(['A', 'B'])
    expect(trophyDays.get('2026-09-03')).toEqual(['C'])
    const weeks = calendarWeeks([{ date: '2026-10-30', minutes: 90 }], 12, today, trophyDays)
    const flat = weeks.flat()
    // recorded day with trophies: both kinds of information
    expect(flat.find((d) => d.key === '2026-10-30')).toMatchObject({ recorded: true, minutes: 90, trophies: ['A', 'B'] })
    // a day long before the recording: not recorded, but evidently played
    expect(flat.find((d) => d.key === '2026-09-03')).toMatchObject({ recorded: false, minutes: 0, trophies: ['C'] })
    expect(flat.find((d) => d.key === '2026-09-04')?.trophies).toEqual([])
  })

  it('sums this week, this month and finds the best day', () => {
    const summary = playtimeSummary(
      [
        { date: '2026-10-05', minutes: 60 },
        { date: '2026-10-27', minutes: 30 },
        { date: '2026-10-30', minutes: 90 },
        { date: '2026-09-30', minutes: 500 },
      ],
      today
    )
    expect(summary.week).toBe(120)
    expect(summary.month).toBe(180)
    expect(summary.total).toBe(680)
    expect(summary.bestDay).toEqual({ key: '2026-09-30', minutes: 500 })
  })
})
