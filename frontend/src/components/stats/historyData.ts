import type { GameHistoryDto, PlayerCountDayDto, PlaytimeDayDto } from '../../api/types'

// Derivations for the recorded history (player counts, playtime per day). Pure and tested; the charts only draw.

const DAY_MS = 86_400_000

export interface RawPoint {
  ts: number
  count: number
}

export interface DailyPoint {
  // yyyy-MM-dd, the recorded (UTC) day, used as a plain calendar date
  date: string
  // Noon UTC of that day: a stable x position that cannot jump to the neighbouring day in any local time zone
  ts: number
  min: number
  avg: number
  max: number
}

// The button text of each range is `stats:range.d<days>`
export const RANGE_CHOICES = [{ days: 7 }, { days: 30 }, { days: 90 }, { days: 365 }] as const

// Raw samples are 30 minutes apart; a longer hole means PlatinVault was not running, and the line must not bridge it
export const RAW_GAP_MS = 3 * 3_600_000
export const DAILY_GAP_DAYS = 2

// Even a single raw sample is shown (as a dot); two or more make a line
export const MIN_RAW_POINTS = 1
export const MIN_DAILY_POINTS = 2

const noonOf = (date: string): number => Date.parse(`${date}T12:00:00Z`)

export function utcDate(ts: number): string {
  return new Date(ts).toISOString().slice(0, 10)
}

// min / avg / max per UTC day, computed from raw samples
export function dailyFromRaw(points: readonly RawPoint[]): DailyPoint[] {
  const groups = new Map<string, number[]>()
  for (const p of points) {
    const date = utcDate(p.ts)
    const list = groups.get(date)
    if (list) list.push(p.count)
    else groups.set(date, [p.count])
  }
  return [...groups.entries()]
    .map(([date, counts]) => ({
      date,
      ts: noonOf(date),
      min: Math.min(...counts),
      max: Math.max(...counts),
      avg: counts.reduce((a, b) => a + b, 0) / counts.length,
    }))
    .sort((a, b) => a.ts - b.ts)
}

export type PlayerSeries =
  | { mode: 'raw'; raw: RawPoint[]; daily: [] }
  | { mode: 'daily'; raw: RawPoint[]; daily: DailyPoint[] }
  | { mode: 'none'; raw: RawPoint[]; daily: DailyPoint[] }

// What to draw for a range: the raw samples up to 30 days, the daily band beyond. The condensed days come from the
// server; the recent days that exist only as raw samples are condensed here, so the band reaches today.
// While little is recorded (the band would be a single day) it falls back to the raw samples, then to "none" (nothing at all).
export function playerSeries(history: GameHistoryDto, days: number, now: number): PlayerSeries {
  const cutoff = now - days * DAY_MS
  const raw = history.playerCounts
    .map((p) => ({ ts: Date.parse(p.at), count: p.count }))
    .filter((p) => !Number.isNaN(p.ts) && p.ts >= cutoff)
    .sort((a, b) => a.ts - b.ts)

  if (days > 30) {
    const fromServer: DailyPoint[] = history.playerCountsDaily
      .map((d: PlayerCountDayDto) => ({ date: d.date, ts: noonOf(d.date), min: d.min, avg: d.avg, max: d.max }))
      .filter((d) => d.ts >= cutoff - DAY_MS / 2)
    const known = new Set(fromServer.map((d) => d.date))
    const fromRaw = dailyFromRaw(raw).filter((d) => !known.has(d.date))
    const daily = [...fromServer, ...fromRaw].sort((a, b) => a.ts - b.ts)
    if (daily.length >= MIN_DAILY_POINTS) return { mode: 'daily', raw, daily }
  }
  if (raw.length >= MIN_RAW_POINTS) return { mode: 'raw', raw, daily: [] }
  return { mode: 'none', raw, daily: [] }
}

// Index ranges [from, to] of consecutive points that are close enough in time to be joined by a line
export function segments(times: readonly number[], maxGap: number): [number, number][] {
  const result: [number, number][] = []
  let start = 0
  for (let i = 1; i <= times.length; i++) {
    if (i === times.length || times[i]! - times[i - 1]! > maxGap) {
      if (times.length > 0) result.push([start, i - 1])
      start = i
    }
  }
  return result
}

// ---- playtime calendar -----------------------------------------------------------------------------------------

export function dateKey(d: Date): string {
  const month = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${month}-${day}`
}

// A recorded yyyy-MM-dd as a local date at noon (no time zone shift)
export function localDateOf(key: string): Date {
  const [y, m, d] = key.split('-').map(Number)
  return new Date(y ?? 1970, (m ?? 1) - 1, d ?? 1, 12)
}

export interface CalendarDay {
  key: string
  date: Date
  minutes: number
  // true = something was recorded that day (even 0 minutes); false = no snapshot / future
  recorded: boolean
  future: boolean
  // Names of the trophies unlocked that (local) day: the player evidently played, also before the recording began
  trophies: string[]
}

// Local calendar day -> names of the trophies unlocked on it
export function trophyDayMap(unlocks: readonly { ts: number; name: string }[]): Map<string, string[]> {
  const map = new Map<string, string[]>()
  for (const u of unlocks) {
    const key = dateKey(new Date(u.ts))
    const list = map.get(key)
    if (list) list.push(u.name)
    else map.set(key, [u.name])
  }
  return map
}

// Monday-first weeks ending with the current week; columns of 7 days
export function calendarWeeks(daily: readonly PlaytimeDayDto[], weeks: number, today: Date, trophyDays?: ReadonlyMap<string, string[]>): CalendarDay[][] {
  const minutes = new Map(daily.map((d) => [d.date, d.minutes]))
  const end = new Date(today.getFullYear(), today.getMonth(), today.getDate(), 12)
  const mondayOffset = (end.getDay() + 6) % 7
  const firstMonday = new Date(end.getFullYear(), end.getMonth(), end.getDate() - mondayOffset - (weeks - 1) * 7, 12)
  const result: CalendarDay[][] = []
  for (let w = 0; w < weeks; w++) {
    const column: CalendarDay[] = []
    for (let d = 0; d < 7; d++) {
      const date = new Date(firstMonday.getFullYear(), firstMonday.getMonth(), firstMonday.getDate() + w * 7 + d, 12)
      const key = dateKey(date)
      column.push({
        key,
        date,
        minutes: minutes.get(key) ?? 0,
        recorded: minutes.has(key),
        future: date.getTime() > end.getTime(),
        trophies: trophyDays?.get(key) ?? [],
      })
    }
    result.push(column)
  }
  return result
}

export interface PlaytimeSummary {
  week: number
  month: number
  total: number
  bestDay: { key: string; minutes: number } | null
}

export function playtimeSummary(daily: readonly PlaytimeDayDto[], today: Date): PlaytimeSummary {
  const end = new Date(today.getFullYear(), today.getMonth(), today.getDate(), 12)
  const monday = new Date(end.getFullYear(), end.getMonth(), end.getDate() - ((end.getDay() + 6) % 7), 12)
  let week = 0
  let month = 0
  let total = 0
  let bestDay: PlaytimeSummary['bestDay'] = null
  for (const d of daily) {
    const date = localDateOf(d.date)
    total += d.minutes
    if (date >= monday && date <= end) week += d.minutes
    if (date.getFullYear() === end.getFullYear() && date.getMonth() === end.getMonth()) month += d.minutes
    if (d.minutes > 0 && (!bestDay || d.minutes > bestDay.minutes)) bestDay = { key: d.date, minutes: d.minutes }
  }
  return { week, month, total, bestDay }
}
