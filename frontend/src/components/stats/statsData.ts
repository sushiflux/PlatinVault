import type { LiveAchievementItemDto, PlaytimeDayDto } from '../../api/types'

// Pure derivations for the statistics tab. Everything here works on the data Steam already delivered
// (live achievements, player stats) - nothing is invented, and a figure that cannot be computed honestly is null.

// ---- rarity ----------------------------------------------------------------------------------------------------

// The keys double as translation keys: the band name and its range text are `stats:rarity.band.<key>.label|range`
export type RarityBandKey = 'ultra' | 'very' | 'rare' | 'uncommon' | 'common'

export interface RarityBand {
  key: RarityBandKey
  // Lower bound inclusive, upper bound exclusive (percent of all players)
  min: number
  max: number
}

// Five bands of Steam's worldwide unlock share. Lower bound inclusive, upper exclusive: 5.0 % is "very rare".
export const RARITY_BANDS: readonly RarityBand[] = [
  { key: 'ultra', min: 0, max: 5 },
  { key: 'very', min: 5, max: 10 },
  { key: 'rare', min: 10, max: 20 },
  { key: 'uncommon', min: 20, max: 50 },
  { key: 'common', min: 50, max: Number.POSITIVE_INFINITY },
]

export interface BandCount {
  band: RarityBand
  unlocked: number
  open: number
  total: number
}

export function bandOf(percent: number): RarityBand {
  const found = RARITY_BANDS.find((b) => percent >= b.min && percent < b.max)
  return found ?? RARITY_BANDS[RARITY_BANDS.length - 1]!
}

// The backend marks every achievement whose worldwide share Steam really delivered (hasGlobalPercent); for the others
// globalPercent is only a placeholder (25.0 or a made-up value) that keeps the trophy tiers working. Every rarity
// figure below looks at the marked achievements only, so a missing value neither distorts a chart nor is shown as data.
// A missing field (older backend) counts as "known". The marker decides, never the value: a real 25 % is data.
function hasKnownShare(item: LiveAchievementItemDto): boolean {
  return item.hasGlobalPercent !== false
}

function hasShare(item: LiveAchievementItemDto): boolean {
  return hasKnownShare(item) && item.globalPercent > 0
}

// How many achievements Steam gave no worldwide share for (they are left out of every rarity figure)
export function missingShareCount(items: readonly LiveAchievementItemDto[]): number {
  return items.filter((a) => !hasKnownShare(a)).length
}

// False while there is not a single real share: then the rarity charts are hidden instead of showing a lie
export function hasRarityData(items: readonly LiveAchievementItemDto[]): boolean {
  return items.some(hasShare)
}

export function rarityBands(items: readonly LiveAchievementItemDto[]): BandCount[] {
  const rows: BandCount[] = RARITY_BANDS.map((band) => ({ band, unlocked: 0, open: 0, total: 0 }))
  for (const item of items) {
    if (!hasKnownShare(item)) continue
    const row = rows.find((r) => r.band === bandOf(item.globalPercent))
    if (!row) continue
    row.total += 1
    if (item.achieved) row.unlocked += 1
    else row.open += 1
  }
  return rows
}

export interface ExclusivityBound {
  // Steam's unlock share of the rarest known achievement: nobody can have 100 % without it
  percent: number
  name: string
  // Achievements with a real share / all achievements: below "total" the bound rests on the known ones only
  known: number
  total: number
}

// Upper bound for the share of players who can own every achievement: it cannot exceed the share of the rarest one.
// With unknown shares the smallest known one is still a correct upper bound (the unknown rarest could only be lower),
// just not necessarily a tight one - the UI says so via known / total.
export function exclusivityBound(items: readonly LiveAchievementItemDto[]): ExclusivityBound | null {
  let best: LiveAchievementItemDto | null = null
  let known = 0
  for (const item of items) {
    if (!hasShare(item)) continue
    known += 1
    if (!best || item.globalPercent < best.globalPercent) best = item
  }
  return best ? { percent: best.globalPercent, name: best.displayName, known, total: items.length } : null
}

// Open achievements, most common first (= the quickest wins). Equal shares are ordered by name in the given locale
// (the active app language; undefined = the runtime default).
export function easiestOpen(items: readonly LiveAchievementItemDto[], locale?: string): LiveAchievementItemDto[] {
  return items
    .filter((a) => !a.achieved && hasShare(a))
    .sort((a, b) => b.globalPercent - a.globalPercent || a.displayName.localeCompare(b.displayName, locale))
}

// ---- unlock history --------------------------------------------------------------------------------------------

export interface Unlock {
  ts: number
  name: string
  // Steam's worldwide share, null when Steam delivered none (placeholders never leak into the timeline data)
  percent: number | null
}

// Steam reports 0 / very early timestamps for unlocks it has no time for; those are left out of every time chart
const EARLIEST_PLAUSIBLE = Date.parse('2008-01-01T00:00:00Z')

export function unlocksWithTime(items: readonly LiveAchievementItemDto[]): Unlock[] {
  const result: Unlock[] = []
  for (const item of items) {
    if (!item.achieved || !item.unlockTime) continue
    const ts = Date.parse(item.unlockTime)
    if (Number.isNaN(ts) || ts < EARLIEST_PLAUSIBLE) continue
    result.push({ ts, name: item.displayName, percent: hasKnownShare(item) ? item.globalPercent : null })
  }
  return result.sort((a, b) => a.ts - b.ts)
}

// Local calendar day, e.g. 2026-06-05
export function dayKey(ts: number): string {
  const d = new Date(ts)
  const month = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${month}-${day}`
}

export interface DayBucket {
  key: string
  // Start of the local day
  ts: number
  count: number
  names: string[]
  // The unlocks of that day, earliest first
  unlocks: Unlock[]
}

export function dayBuckets(unlocks: readonly Unlock[]): DayBucket[] {
  const map = new Map<string, DayBucket>()
  for (const u of unlocks) {
    const key = dayKey(u.ts)
    let bucket = map.get(key)
    if (!bucket) {
      const d = new Date(u.ts)
      bucket = { key, ts: new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime(), count: 0, names: [], unlocks: [] }
      map.set(key, bucket)
    }
    bucket.count += 1
    bucket.names.push(u.name)
    bucket.unlocks.push(u)
  }
  return [...map.values()].sort((a, b) => a.ts - b.ts)
}

// Whole-number axis ticks 0..max for a count: every step up to 4, otherwise round steps (2, 5, 10 ...), always covering max
export function countTicks(max: number): number[] {
  if (!(max > 0)) return [0]
  const top = Math.ceil(max)
  if (top <= 4) return Array.from({ length: top + 1 }, (_, i) => i)
  return niceTicks(top, 4)
}

// ---- bar chart window ------------------------------------------------------------------------------------------

// One bar per day, as wide as the room allows: a single day is one calm bar (capped, centred), more days share the width, and
// once the bars would get thinner than BAR_MIN_WIDTH the chart shows a window of days and pages through the rest.
export const BAR_MIN_WIDTH = 24
export const BAR_MAX_WIDTH = 120
// A window never shows more days than this, however wide the screen is
export const BAR_WINDOW_MAX = 45

export interface BarWindow {
  // How many days fit at the minimum bar width
  windowSize: number
  // How many days are drawn at once: all of them while they fit, otherwise one window
  visible: number
  // More days than a window: the chart pages
  paging: boolean
  barW: number
  // Space between neighbouring bars: 2 px for thin bars, a little more for wide ones
  gap: number
}

// room = width of the plot without its side padding, n = number of days with an unlock
export function barWindow(room: number, n: number): BarWindow {
  const clamp = (value: number, min: number, max: number): number => Math.min(Math.max(value, min), max)
  const windowSize = clamp(Math.floor((room + 2) / (BAR_MIN_WIDTH + 2)), 1, BAR_WINDOW_MAX)
  const visible = Math.max(0, Math.min(n, windowSize))
  if (visible === 0) return { windowSize, visible, paging: false, barW: BAR_MIN_WIDTH, gap: 2 }
  const first = (room - (visible - 1) * 2) / visible
  const gap = clamp(Math.round(first * 0.08), 2, 8)
  const barW = clamp(Math.floor((room - (visible - 1) * gap) / visible), 8, BAR_MAX_WIDTH)
  return { windowSize, visible, paging: n > windowSize, barW, gap }
}

// Which of n evenly spaced categories get an axis label so that no two labels collide: every k-th one, k chosen from the slot width
// and the room one label needs, always starting with the first. The last one is added when there is room left for it.
export function labelIndices(n: number, slotWidth: number, minGap: number): number[] {
  if (n <= 0) return []
  const step = Math.max(1, Math.ceil(minGap / Math.max(1, slotWidth)))
  const indices: number[] = []
  for (let i = 0; i < n; i += step) indices.push(i)
  const last = n - 1
  if (indices[indices.length - 1] !== last && (last - indices[indices.length - 1]!) * slotWidth >= minGap) indices.push(last)
  return indices
}

// ---- weekday x hour ---------------------------------------------------------------------------------------------

// Weekday names Monday..Sunday in the given locale (the app language), taken from Intl so no language needs its own list.
// 2024-01-01 was a Monday. The short form drops the abbreviation dot some locales add ("Mo." -> "Mo").
export function weekdayNames(locale: string, style: 'long' | 'short'): string[] {
  const formatter = new Intl.DateTimeFormat(locale, { weekday: style })
  return Array.from({ length: 7 }, (_, i) => {
    const name = formatter.format(new Date(2024, 0, 1 + i, 12))
    return style === 'short' ? name.replace(/\.$/, '') : name
  })
}

// Rows Monday..Sunday, columns hour 0..23, in the browser's local time
export function weekdayHourMatrix(unlocks: readonly Unlock[]): number[][] {
  const matrix = Array.from({ length: 7 }, () => Array.from({ length: 24 }, () => 0))
  for (const u of unlocks) {
    const d = new Date(u.ts)
    const row = matrix[(d.getDay() + 6) % 7]
    if (row) row[d.getHours()] = (row[d.getHours()] ?? 0) + 1
  }
  return matrix
}

export interface BestSlot {
  weekday: number
  hour: number
  count: number
}

export function bestSlot(matrix: readonly (readonly number[])[]): BestSlot | null {
  let best: BestSlot | null = null
  matrix.forEach((row, weekday) =>
    row.forEach((count, hour) => {
      if (count > 0 && (!best || count > best.count)) best = { weekday, hour, count }
    })
  )
  return best
}

// ---- time to platinum -------------------------------------------------------------------------------------------

export type EstimateBasis = 'igdb' | 'recent' | 'overall'

export interface PlatinumEstimate {
  basis: EstimateBasis
  // The exact result and the rounded value that is shown ("approx. 40 h")
  hours: number
  shownHours: number
  remainingCount: number
  // Own playtime in hours (known for every basis but may be null for 'recent')
  ownHours: number | null
  // Basis 'igdb': IGDB's average time for 100 %
  igdbHours: number | null
  // Bases 'recent' / 'overall': the pace in trophies per played hour and what it rests on
  ratePerHour: number | null
  basisUnlocks: number | null
  basisHours: number | null
  // Basis 'recent': days the window really covers (28, or fewer while PlatinVault has recorded less)
  windowDays: number | null
}

export interface EstimateInput {
  unlocked: number
  total: number
  // Own playtime in hours (null = unknown)
  playtimeHours: number | null
  // IGDB's average time for 100 % (null = no IGDB data)
  igdbCompletelyHours: number | null
  unlocks: readonly Unlock[]
  // Recorded minutes per (UTC) day; null = history unavailable
  playtimeDaily: readonly PlaytimeDayDto[] | null
  now: number
}

export const RECENT_WINDOW_DAYS = 28
export const RECENT_MIN_UNLOCKS = 3
export const RECENT_MIN_HOURS = 2
export const OVERALL_MIN_UNLOCKS = 3
export const OVERALL_MIN_HOURS = 1
// An estimate above this (or above 3 x IGDB's 100 % time) says nothing useful and is not shown
export const MAX_PLAUSIBLE_HOURS = 300
export const MAX_IGDB_FACTOR = 3

const DAY_MS = 86_400_000

// No fake precision: halves below 10 h, whole hours below 25, steps of 5 below 100, of 10 below 250, of 25 beyond
export function roundHours(hours: number): number {
  const step = hours < 10 ? 0.5 : hours < 25 ? 1 : hours < 100 ? 5 : hours < 250 ? 10 : 25
  return Math.max(step, Math.round(hours / step) * step)
}

const utcDayStart = (ts: number): number => Math.floor(ts / DAY_MS) * DAY_MS

// Unlocks per played hour in the last 28 days. The window never reaches back before the first recorded playtime day,
// otherwise unlocks from before the recording would be set against minutes that were never counted.
function recentPace(input: EstimateInput): { rate: number; unlocks: number; hours: number; days: number } | null {
  const daily = input.playtimeDaily
  if (!daily || daily.length === 0) return null
  const today = utcDayStart(input.now)
  const dated = daily
    .map((d) => ({ ts: Date.parse(`${d.date}T00:00:00Z`), minutes: d.minutes }))
    .filter((d) => !Number.isNaN(d.ts) && d.ts <= today)
  if (dated.length === 0) return null
  const firstRecorded = Math.min(...dated.map((d) => d.ts))
  const windowStart = Math.max(today - (RECENT_WINDOW_DAYS - 1) * DAY_MS, firstRecorded)
  const days = Math.round((today - windowStart) / DAY_MS) + 1
  const hours = dated.filter((d) => d.ts >= windowStart).reduce((sum, d) => sum + d.minutes, 0) / 60
  const unlocks = input.unlocks.filter((u) => u.ts >= windowStart && u.ts <= input.now).length
  if (unlocks < RECENT_MIN_UNLOCKS || hours < RECENT_MIN_HOURS) return null
  return { rate: unlocks / hours, unlocks, hours, days }
}

// Remaining hours to platinum, by the best basis that is available AND plausible:
//  1. IGDB: the average 100 % time minus the own playtime (while the own time is still below it),
//  2. the recent pace: unlocks per played hour in the last 28 days (needs >= 3 unlocks and >= 2 h in that window),
//  3. the overall pace (needs >= 3 unlocks and >= 1 h).
// A result above 300 h or above 3 x IGDB's 100 % time is implausible and skipped; with nothing left the answer is null
// and the tile stays hidden. Null as well when nothing is left to do.
export function estimateToPlatinum(input: EstimateInput): PlatinumEstimate | null {
  const remainingCount = input.total - input.unlocked
  if (input.total <= 0 || remainingCount <= 0) return null
  const igdb = input.igdbCompletelyHours !== null && input.igdbCompletelyHours > 0 ? input.igdbCompletelyHours : null
  const plausible = (hours: number): boolean =>
    Number.isFinite(hours) && hours > 0 && hours <= MAX_PLAUSIBLE_HOURS && (igdb === null || hours <= MAX_IGDB_FACTOR * igdb)
  const base = { remainingCount, ownHours: input.playtimeHours, igdbHours: igdb, ratePerHour: null, basisUnlocks: null, basisHours: null, windowDays: null }
  const result = (basis: EstimateBasis, hours: number, extra: Partial<PlatinumEstimate>): PlatinumEstimate => ({
    ...base,
    basis,
    hours,
    shownHours: roundHours(hours),
    ...extra,
  })

  if (igdb !== null && input.playtimeHours !== null && input.playtimeHours < igdb) {
    const hours = igdb - input.playtimeHours
    if (plausible(hours)) return result('igdb', hours, {})
  }

  const recent = recentPace(input)
  if (recent) {
    const hours = remainingCount / recent.rate
    if (plausible(hours)) {
      return result('recent', hours, { ratePerHour: recent.rate, basisUnlocks: recent.unlocks, basisHours: recent.hours, windowDays: recent.days })
    }
  }

  if (input.playtimeHours !== null && input.playtimeHours >= OVERALL_MIN_HOURS && input.unlocked >= OVERALL_MIN_UNLOCKS) {
    const rate = input.unlocked / input.playtimeHours
    const hours = remainingCount / rate
    if (plausible(hours)) return result('overall', hours, { ratePerHour: rate, basisUnlocks: input.unlocked, basisHours: input.playtimeHours })
  }
  return null
}

// ---- axis helpers -----------------------------------------------------------------------------------------------

// Round tick values (1, 2, 5 x 10^n) covering 0..max with at most about `count` steps
export function niceTicks(max: number, count = 4): number[] {
  if (!(max > 0)) return [0]
  const rough = max / count
  const magnitude = 10 ** Math.floor(Math.log10(rough))
  const normalized = rough / magnitude
  const step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude
  const ticks: number[] = []
  for (let v = 0; v <= max + step * 0.999; v += step) ticks.push(Math.round(v * 1e6) / 1e6)
  return ticks
}

// Month starts (or evenly spaced days for short ranges) inside [from, to], at most `maxTicks` of them
export function timeTicks(from: number, to: number, maxTicks: number): number[] {
  const span = to - from
  if (span <= 0 || maxTicks < 2) return [from]
  const dayMs = 86_400_000
  if (span < 60 * dayMs) {
    // The smallest "round" step in days that keeps the labels under the limit
    const spanDays = span / dayMs
    const stepDays = [1, 2, 3, 5, 7, 10, 14, 30].find((s) => Math.floor(spanDays / s) + 1 <= maxTicks) ?? 30
    const start = new Date(from)
    let t = new Date(start.getFullYear(), start.getMonth(), start.getDate()).getTime()
    if (t < from) t += dayMs
    const ticks: number[] = []
    for (; t <= to; t += stepDays * dayMs) ticks.push(t)
    return ticks.length > 0 ? ticks : [from]
  }
  const first = new Date(from)
  const months: number[] = []
  let cursor = new Date(first.getFullYear(), first.getMonth() + 1, 1)
  while (cursor.getTime() <= to) {
    months.push(cursor.getTime())
    cursor = new Date(cursor.getFullYear(), cursor.getMonth() + 1, 1)
  }
  const every = Math.max(1, Math.ceil(months.length / maxTicks))
  return months.filter((_, i) => i % every === 0)
}
