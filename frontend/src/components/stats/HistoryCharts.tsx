import { useMemo, useState, type KeyboardEvent, type PointerEvent } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { IconCalendarStats, IconHourglassHigh, IconUsers } from '@tabler/icons-react'
import { motion, useReducedMotion } from 'motion/react'
import type { GameHistoryDto } from '../../api/types'
import {
  ChartCard,
  ChartTip,
  EarlyState,
  Kpi,
  Legend,
  TipRow,
  TipSub,
  TipTitle,
  useChartTip,
  useElementWidth,
  useStatsFormat,
  type StatsFormat,
} from './chartKit'
import {
  DAILY_GAP_DAYS,
  RAW_GAP_MS,
  calendarWeeks,
  localDateOf,
  playerSeries,
  playtimeSummary,
  segments,
  trophyDayMap,
  type CalendarDay,
  type DailyPoint,
  type RawPoint,
} from './historyData'
import { niceTicks, timeTicks, weekdayNames, type Unlock } from './statsData'

const DAY_MS = 86_400_000

function sinceText(trackingSince: string | null, t: TFunction<'stats'>, fmt: StatsFormat): string {
  const ts = trackingSince ? Date.parse(trackingSince) : Number.NaN
  return Number.isNaN(ts) ? t('players.noRecording') : t('players.recordingSince', { date: fmt.dateLong(ts) })
}

// ---- C1: player count over time ---------------------------------------------------------------------------------

const CHART_H = 232
const M = { top: 14, right: 52, bottom: 30, left: 46 }

interface PlayerProps {
  history: GameHistoryDto
  range: number
  currentPlayers: number | null
  order?: number
}

type Pt = { ts: number; value: number; min?: number; max?: number; avg?: number; daily: boolean }

// Players over time: the raw 30-minute samples (up to 30 days) or, for longer ranges, the daily average with the
// min-max band behind it. One axis, holes in the recording stay holes.
export function PlayerCountChart({ history, range, currentPlayers, order }: PlayerProps) {
  const { t } = useTranslation('stats')
  const fmt = useStatsFormat()
  const [wrapRef, width] = useElementWidth<HTMLDivElement>()
  const tip = useChartTip()
  const reduce = useReducedMotion() ?? false
  const [active, setActive] = useState<number | null>(null)
  const series = useMemo(() => playerSeries(history, range, Date.now()), [history, range])

  const points: Pt[] = useMemo(() => {
    if (series.mode === 'raw') return series.raw.map((p: RawPoint) => ({ ts: p.ts, value: p.count, daily: false }))
    if (series.mode === 'daily')
      return series.daily.map((d: DailyPoint) => ({ ts: d.ts, value: d.avg, min: d.min, max: d.max, avg: d.avg, daily: true }))
    return []
  }, [series])

  const isDaily = series.mode === 'daily'
  const trackingTs = history.trackingSince ? Date.parse(history.trackingSince) : Number.NaN
  // While the recording is young, a small honest note says since when it runs (the chart itself is never replaced by it)
  const young = !Number.isNaN(trackingTs) && Date.now() - trackingTs < 3 * DAY_MS

  if (series.mode === 'none') {
    return (
      <ChartCard title={t('players.title')} icon={<IconUsers size={18} />} order={order} className="h-full">
        <EarlyState height={CHART_H} icon={<IconHourglassHigh size={26} />} title={sinceText(history.trackingSince, t, fmt)}>
          {currentPlayers !== null ? t('players.earlyWithNow', { players: fmt.int(currentPlayers) }) : t('players.early')}
        </EarlyState>
      </ChartCard>
    )
  }

  const first = points[0]!.ts
  const last = points[points.length - 1]!.ts
  const start = last - first < 3_600_000 ? first - 1_800_000 : first
  const end = last - first < 3_600_000 ? last + 1_800_000 : last
  const peak = points.reduce((a, p) => ((p.max ?? p.value) > (a.max ?? a.value) ? p : a), points[0]!)
  const peakValue = peak.max ?? peak.value
  const mean = points.reduce((a, p) => a + p.value, 0) / points.length
  const yTicks = niceTicks(peakValue, 4)
  const yMax = yTicks[yTicks.length - 1] ?? peakValue
  const compact = yMax >= 10_000

  const plotW = Math.max(0, width - M.left - M.right)
  const plotH = CHART_H - M.top - M.bottom
  const x = (ts: number) => M.left + ((ts - start) / (end - start)) * plotW
  const y = (v: number) => M.top + plotH - (v / yMax) * plotH

  const times = points.map((p) => p.ts)
  const segs = segments(times, isDaily ? DAILY_GAP_DAYS * DAY_MS : RAW_GAP_MS)
  let linePath = ''
  let areaPath = ''
  let bandPath = ''
  const lonely: Pt[] = []
  if (plotW > 0) {
    for (const [a, b] of segs) {
      if (a === b) {
        lonely.push(points[a]!)
        continue
      }
      const slice = points.slice(a, b + 1)
      const line = slice.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(p.ts).toFixed(1)} ${y(p.value).toFixed(1)}`).join(' ')
      linePath += `${line} `
      if (isDaily) {
        const top = slice.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(p.ts).toFixed(1)} ${y(p.max ?? p.value).toFixed(1)}`).join(' ')
        const bottom = [...slice].reverse().map((p) => `L${x(p.ts).toFixed(1)} ${y(p.min ?? p.value).toFixed(1)}`).join(' ')
        bandPath += `${top} ${bottom} Z `
      } else {
        areaPath += `${line} L${x(slice[slice.length - 1]!.ts).toFixed(1)} ${y(0).toFixed(1)} L${x(slice[0]!.ts).toFixed(1)} ${y(0).toFixed(1)} Z `
      }
    }
  }

  const spanMs = end - start
  const shortSpan = spanMs < 2 * DAY_MS
  const tickCount = Math.max(3, Math.min(6, Math.floor(plotW / 80)))
  const xTicks: number[] = shortSpan
    ? Array.from({ length: tickCount }, (_unused, i) => start + (spanMs * i) / (tickCount - 1))
    : timeTicks(start, end, tickCount)
  const tickLabel = (ts: number) => (shortSpan ? fmt.time(ts) : spanMs >= 60 * DAY_MS ? fmt.monthYear(ts) : fmt.dateShort(ts))
  const axisNumber = (v: number) => (compact ? `${fmt.dec(v / 1000)}K` : fmt.int(v))

  const tipFor = (i: number) => {
    const p = points[i]!
    return p.daily ? (
      <>
        <TipTitle>{fmt.weekdayDate(p.ts)}</TipTitle>
        <TipRow colorVar="--viz-1" name={t('players.tipAverage')} value={fmt.int(Math.round(p.avg ?? p.value))} />
        <TipRow name={t('players.tipPeak')} value={fmt.int(p.max ?? p.value)} />
        <TipRow name={t('players.tipLowest')} value={fmt.int(p.min ?? p.value)} />
        <TipSub>{t('players.tipDailyNote')}</TipSub>
      </>
    ) : (
      <>
        <TipTitle>{t('players.tipPlayers', { count: p.value })}</TipTitle>
        <TipSub>{fmt.dateTime(p.ts)}</TipSub>
      </>
    )
  }

  const pick = (e: PointerEvent<SVGSVGElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const ts = start + ((e.clientX - rect.left - M.left) / plotW) * spanMs
    const i = nearest(times, ts)
    setActive(i)
    tip.showAt(e.clientX, e.clientY, tipFor(i))
  }

  const onKey = (e: KeyboardEvent<HTMLDivElement>) => {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(e.key)) return
    e.preventDefault()
    const cur = active ?? (e.key === 'ArrowLeft' || e.key === 'End' ? points.length : -1)
    const next =
      e.key === 'Home' ? 0 : e.key === 'End' ? points.length - 1 : e.key === 'ArrowLeft' ? Math.max(0, cur - 1) : Math.min(points.length - 1, cur + 1)
    setActive(next)
    const svg = e.currentTarget.querySelector('svg')
    if (svg) {
      const rect = svg.getBoundingClientRect()
      tip.showAt(rect.left + x(points[next]!.ts), rect.top + y(points[next]!.value), tipFor(next), true)
    }
  }

  const activePoint = active !== null ? points[active] : undefined
  const nowCount = currentPlayers ?? points[points.length - 1]!.value

  return (
    <ChartCard
      title={t('players.title')}
      icon={<IconUsers size={18} />}
      subtitle={isDaily ? t('players.subtitleDaily') : t('players.subtitleRaw')}
      order={order}
      className="h-full"
      table={{
        caption: isDaily ? t('players.tableCaptionDaily') : t('players.tableCaptionRaw'),
        columns: isDaily
          ? [
              { label: t('players.colDay') },
              { label: t('players.colLowest'), numeric: true },
              { label: t('players.colAverage'), numeric: true },
              { label: t('players.colPeak'), numeric: true },
            ]
          : [{ label: t('players.colTime') }, { label: t('players.colPlayers'), numeric: true }],
        rows: [...points].reverse().map((p) =>
          p.daily ? [fmt.weekdayDate(p.ts), fmt.int(p.min ?? 0), fmt.int(Math.round(p.avg ?? p.value)), fmt.int(p.max ?? 0)] : [fmt.dateTime(p.ts), fmt.int(p.value)]
        ),
      }}
      footer={
        isDaily || young ? (
          <div className="mt-3 space-y-2">
            {isDaily && (
              <Legend
                entries={[
                  { label: t('players.legendAverage'), colorVar: '--viz-1', kind: 'line' },
                  { label: t('players.legendRange'), colorVar: '--viz-1', css: 'var(--viz-band)' },
                ]}
              />
            )}
            {young && <p className="pv-viz-note !mt-0">{sinceText(history.trackingSince, t, fmt)}</p>}
          </div>
        ) : undefined
      }
    >
      <div className="mb-3 flex flex-wrap gap-x-10 gap-y-3">
        <Kpi label={t('players.kpiNow')} value={fmt.int(Math.round(nowCount))} hint={currentPlayers !== null ? t('players.kpiNowLive') : t('players.kpiNowLast')} />
        {points.length >= 2 && (
          <>
            <Kpi label={isDaily ? t('players.kpiAvgDay') : t('players.kpiAvgPeriod')} value={fmt.int(Math.round(mean))} />
            <Kpi label={t('players.kpiPeak')} value={fmt.int(peakValue)} hint={fmt.dateShort(peak.ts)} />
          </>
        )}
      </div>
      <div
        ref={wrapRef}
        className="pv-viz-focusable"
        style={{ blockSize: CHART_H }}
        tabIndex={0}
        role="group"
        aria-label={t('players.group')}
        onKeyDown={onKey}
        onBlur={() => {
          setActive(null)
          tip.hide()
        }}
      >
        {plotW > 0 && (
          <svg
            width={width}
            height={CHART_H}
            role="img"
            aria-label={t('players.chartLabel', { peak: fmt.int(peakValue), mean: fmt.int(Math.round(mean)) })}
            style={{ display: 'block', touchAction: 'pan-y' }}
            onPointerMove={pick}
            onPointerDown={pick}
            onPointerLeave={(e) => {
              if (e.pointerType === 'mouse' || e.pointerType === 'pen') {
                setActive(null)
                tip.hide()
              }
            }}
            data-viz-mark=""
          >
            {yTicks.map((tick) => (
              <g key={tick}>
                <line x1={M.left} x2={M.left + plotW} y1={y(tick)} y2={y(tick)} stroke="var(--viz-grid)" strokeWidth={1} />
                <text x={M.left - 8} y={y(tick)} dy="0.32em" textAnchor="end" className="pv-viz-axis-text">
                  {axisNumber(tick)}
                </text>
              </g>
            ))}
            <line x1={M.left} x2={M.left + plotW} y1={M.top + plotH} y2={M.top + plotH} stroke="var(--viz-axis)" strokeWidth={1} />
            {xTicks.map((tick, i) => {
              const tx = x(tick)
              const anchor = tx < M.left + 24 ? 'start' : tx > M.left + plotW - 24 ? 'end' : 'middle'
              return (
                <text key={`${tick}-${i}`} x={tx} y={M.top + plotH + 18} textAnchor={anchor} className="pv-viz-axis-text">
                  {tickLabel(tick)}
                </text>
              )
            })}

            {isDaily ? <path d={bandPath} fill="var(--viz-band)" /> : <path d={areaPath} fill="var(--viz-wash)" />}
            <motion.path
              d={linePath}
              fill="none"
              stroke="var(--viz-1)"
              strokeWidth={2}
              strokeLinejoin="round"
              strokeLinecap="round"
              initial={reduce ? false : { opacity: 0 }}
              animate={{ opacity: 1 }}
              transition={{ duration: 0.6 }}
            />
            {lonely.filter((p) => points.length > 1 || p !== points[0]).map((p) => (
              <circle key={p.ts} cx={x(p.ts)} cy={y(p.value)} r={2.5} fill="var(--viz-1)" />
            ))}

            <circle cx={x(last)} cy={y(points[points.length - 1]!.value)} r={5} fill="var(--viz-1)" stroke="var(--bg-surface)" strokeWidth={2} />
            <text x={x(last) + 11} y={y(points[points.length - 1]!.value)} dy="0.32em" className="pv-viz-value-text">
              {points.length === 1
                ? t('players.singleAt', { value: fmt.int(Math.round(points[0]!.value)), time: fmt.time(points[0]!.ts) })
                : fmt.int(Math.round(points[points.length - 1]!.value))}
            </text>

            {activePoint && (
              <g pointerEvents="none">
                <line x1={x(activePoint.ts)} x2={x(activePoint.ts)} y1={M.top} y2={M.top + plotH} stroke="var(--viz-ink-3)" strokeWidth={1} />
                <circle cx={x(activePoint.ts)} cy={y(activePoint.value)} r={5} fill="var(--viz-1)" stroke="var(--bg-surface)" strokeWidth={2} />
              </g>
            )}
          </svg>
        )}
      </div>
      <ChartTip api={tip} />
    </ChartCard>
  )
}

function nearest(times: readonly number[], ts: number): number {
  let lo = 0
  let hi = times.length - 1
  while (lo < hi) {
    const mid = (lo + hi) >> 1
    if (times[mid]! < ts) lo = mid + 1
    else hi = mid
  }
  if (lo > 0 && Math.abs(times[lo - 1]! - ts) <= Math.abs(times[lo]! - ts)) return lo - 1
  return lo
}

// ---- C2: playtime calendar --------------------------------------------------------------------------------------

const LEVELS = 5
const GAP = 3
const LABEL_W = 30

function levelOf(minutes: number, max: number): number {
  if (minutes <= 0 || max <= 0) return 0
  return Math.max(1, Math.min(LEVELS, Math.ceil((minutes / max) * LEVELS)))
}

const cellColor = (level: number): string => (level === 0 ? 'var(--viz-empty)' : `var(--viz-seq-${level})`)

interface CalendarProps {
  history: GameHistoryDto
  // Every day with an unlocked trophy counts as played, also before PlatinVault recorded anything
  unlocks: readonly Unlock[]
  // Steam's own totals (Steam has no per-day history): all time and the last two weeks, in hours
  totalHours: number | null
  recentHours: number | null
  order?: number
}

// Calendar: fill = recorded minutes per day (from the day the recording started), gold dot = a trophy was unlocked that day
// (known for every past day). Monday-first columns, as many weeks as the card is wide.
export function PlaytimeCalendar({ history, unlocks, totalHours, recentHours, order }: CalendarProps) {
  const { t } = useTranslation('stats')
  const fmt = useStatsFormat()
  const [wrapRef, width] = useElementWidth<HTMLDivElement>()
  const tip = useChartTip()
  const [active, setActive] = useState<string | null>(null)
  const today = useMemo(() => new Date(), [])
  // Short weekday names (Monday first) of the active language; the calendar labels Monday, Wednesday and Friday
  const weekdaysShort = useMemo(() => weekdayNames(fmt.locale, 'short'), [fmt.locale])

  const daily = history.playtimeDaily
  const trophyDays = useMemo(() => trophyDayMap(unlocks), [unlocks])
  const weeks = Math.max(8, Math.min(53, Math.floor((width - LABEL_W + GAP) / (17 + GAP))))
  const cell = width > 0 ? Math.min(22, Math.max(12, Math.floor((width - LABEL_W - (weeks - 1) * GAP) / weeks))) : 14
  const columns = useMemo(() => calendarWeeks(daily, weeks, today, trophyDays), [daily, weeks, today, trophyDays])
  const summary = useMemo(() => playtimeSummary(daily, today), [daily, today])
  const firstRecorded = daily.length > 0 ? daily[0]!.date : null
  const maxMinutes = Math.max(0, ...columns.flat().map((d) => d.minutes))
  const CAL_H = 18 + 7 * (cell + GAP)
  // The figures below only speak about recorded minutes, so they wait until there are some
  const hasRecorded = summary.total > 0
  const hasAnything = daily.length > 0 || trophyDays.size > 0
  const steamLine =
    totalHours !== null && totalHours > 0 ? (
      <p className="pv-viz-sub !mt-0 mb-3">
        {recentHours !== null && recentHours > 0 ? (
          <Trans t={t} i18nKey="calendar.steamTotalRecent" values={{ total: fmt.hours(totalHours), recent: fmt.hours(recentHours) }} components={{ strong: <strong /> }} />
        ) : (
          <Trans t={t} i18nKey="calendar.steamTotal" values={{ total: fmt.hours(totalHours) }} components={{ strong: <strong /> }} />
        )}
      </p>
    ) : null

  if (!hasAnything) {
    return (
      <ChartCard title={t('calendar.title')} icon={<IconCalendarStats size={18} />} order={order} className="h-full">
        {steamLine}
        <EarlyState height={200} icon={<IconHourglassHigh size={26} />} title={sinceText(history.trackingSince, t, fmt)}>
          {t('calendar.earlyBody')}
        </EarlyState>
      </ChartCard>
    )
  }

  const tipFor = (d: CalendarDay) => {
    const shown = d.trophies.slice(0, 3)
    const more = d.trophies.length - shown.length
    const unknown = !d.recorded && d.trophies.length === 0
    return (
      <>
        <TipTitle>{fmt.weekdayDate(d.date.getTime())}</TipTitle>
        {d.recorded && <TipRow name={t('calendar.tipRecorded')} value={d.minutes > 0 ? fmt.minutes(d.minutes) : t('calendar.tipNothingPlayed')} />}
        {d.trophies.length > 0 && (
          <>
            <TipRow name={t('calendar.tipTrophies')} value={d.trophies.length} />
            <TipSub>{more > 0 ? t('calendar.tipAndMore', { names: shown.join(', '), n: more }) : shown.join(', ')}</TipSub>
            {!d.recorded && <TipSub>{t('calendar.tipPlayedUnknown')}</TipSub>}
          </>
        )}
        {unknown && <TipSub>{firstRecorded && d.key < firstRecorded ? t('calendar.tipBeforeRecording') : t('calendar.tipNoEntry')}</TipSub>}
      </>
    )
  }

  const onKey = (e: KeyboardEvent<HTMLDivElement>) => {
    const moves: Record<string, number> = { ArrowLeft: -7, ArrowRight: 7, ArrowUp: -1, ArrowDown: 1 }
    const step = moves[e.key]
    if (step === undefined) return
    e.preventDefault()
    const flat = columns.flat().filter((d) => !d.future)
    const idx = active ? flat.findIndex((d) => d.key === active) : flat.length
    const next = flat[Math.min(flat.length - 1, Math.max(0, idx + step))]
    if (!next) return
    setActive(next.key)
    const el = e.currentTarget.querySelector<HTMLElement>(`[data-day="${next.key}"]`)
    if (el) {
      const rect = el.getBoundingClientRect()
      tip.showAt(rect.left + rect.width / 2, rect.top, tipFor(next), true)
    }
  }

  // Month label above the first column that starts a new month (labels closer than 3 columns are skipped)
  const monthLabels: { col: number; text: string }[] = []
  columns.forEach((column, c) => {
    const monday = column[0]!.date
    const prev = columns[c - 1]?.[0]?.date
    if (c === 0 || (prev && prev.getMonth() !== monday.getMonth())) {
      const lastLabel = monthLabels[monthLabels.length - 1]
      if (!lastLabel || c - lastLabel.col >= 3) monthLabels.push({ col: c, text: fmt.month(monday.getTime()) })
    }
  })

  // Table twin: every day with recorded minutes or an unlocked trophy, newest first
  const tableDays = new Map<string, { minutes: number | null; trophies: string[] }>()
  for (const d of daily) if (d.minutes > 0) tableDays.set(d.date, { minutes: d.minutes, trophies: [] })
  for (const [key, names] of trophyDays) tableDays.set(key, { minutes: tableDays.get(key)?.minutes ?? null, trophies: names })
  const tableRows = [...tableDays.entries()]
    .sort(([x], [y]) => (x < y ? 1 : -1))
    .map(([key, v]) => [fmt.weekdayDate(localDateOf(key).getTime()), v.minutes !== null ? fmt.minutes(v.minutes) : t('calendar.notRecorded'), v.trophies.join(', ')])

  const best = summary.bestDay
  return (
    <ChartCard
      title={t('calendar.title')}
      icon={<IconCalendarStats size={18} />}
      subtitle={t('calendar.subtitle')}
      order={order}
      className="h-full"
      table={{
        caption: t('calendar.tableCaption'),
        columns: [{ label: t('calendar.colDay') }, { label: t('calendar.colPlaytime'), numeric: true }, { label: t('calendar.colTrophies') }],
        rows: tableRows,
      }}
      footer={
        <div className="mt-3 space-y-1.5 text-xs" style={{ color: 'var(--viz-ink-2)' }}>
          <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <span>{t('calendar.legendLess')}</span>
            <span className="flex gap-[2px]" aria-hidden="true">
              {Array.from({ length: LEVELS + 1 }, (_, i) => (
                <span key={i} className="pv-viz-cell inline-block h-3 w-5" style={{ background: cellColor(i) }} />
              ))}
            </span>
            <span className="whitespace-nowrap">{t('calendar.legendMore')}</span>
          </div>
          <div className="flex items-center gap-2">
            <span className="pv-viz-cell inline-block h-3.5 w-5" data-trophy="true" style={{ background: 'var(--viz-empty)' }} aria-hidden="true" />
            <span>{t('calendar.legendTrophy')}</span>
          </div>
          <p style={{ color: 'var(--viz-ink-3)' }}>
            {history.trackingSince && !Number.isNaN(Date.parse(history.trackingSince))
              ? `${t('calendar.recordedSince', { date: fmt.dateLong(Date.parse(history.trackingSince)) })} `
              : ''}
            {t('calendar.unrecordedNote')}
          </p>
        </div>
      }
    >
      {steamLine}
      {hasRecorded && (
        <div className="mb-3 flex flex-wrap gap-x-10 gap-y-3">
          <Kpi label={t('calendar.kpiWeek')} value={fmt.minutesShort(summary.week)} />
          <Kpi label={t('calendar.kpiMonth')} value={fmt.minutesShort(summary.month)} />
          <Kpi
            label={t('calendar.kpiLongest')}
            value={best ? fmt.minutesShort(best.minutes) : t('calendar.kpiLongestNone')}
            hint={best ? fmt.dateShort(localDateOf(best.key).getTime()) : undefined}
          />
        </div>
      )}
      <div ref={wrapRef} style={{ minBlockSize: CAL_H }}>
        {width > 0 && (
          <div
            className="pv-viz-focusable"
            tabIndex={0}
            role="group"
            aria-label={t('calendar.group')}
            onKeyDown={onKey}
            onBlur={() => {
              setActive(null)
              tip.hide()
            }}
            style={{ inlineSize: 'fit-content', maxInlineSize: '100%' }}
          >
            <div className="grid" style={{ gridTemplateColumns: `${LABEL_W}px repeat(${weeks}, ${cell}px)`, columnGap: GAP, rowGap: GAP }}>
              <span aria-hidden="true" style={{ blockSize: 15 }} />
              {columns.map((_, c) => {
                const label = monthLabels.find((m) => m.col === c)
                return (
                  <span key={c} className="relative text-[12px] leading-[15px]" style={{ color: 'var(--viz-ink-3)', blockSize: 15 }} aria-hidden="true">
                    {label && <span className="absolute left-0 whitespace-nowrap">{label.text}</span>}
                  </span>
                )
              })}
              {Array.from({ length: 7 }, (_, r) => (
                <WeekRow
                  key={r}
                  r={r}
                  label={r === 0 || r === 2 || r === 4 ? (weekdaysShort[r] ?? '') : ''}
                  columns={columns}
                  cell={cell}
                  max={maxMinutes}
                  active={active}
                  tip={tip}
                  tipFor={tipFor}
                />
              ))}
            </div>
          </div>
        )}
      </div>
      <ChartTip api={tip} />
    </ChartCard>
  )
}

function WeekRow({
  r,
  label,
  columns,
  cell,
  max,
  active,
  tip,
  tipFor,
}: {
  r: number
  // Weekday label of this row (empty for the rows that carry none)
  label: string
  columns: CalendarDay[][]
  cell: number
  max: number
  active: string | null
  tip: ReturnType<typeof useChartTip>
  tipFor: (d: CalendarDay) => React.ReactNode
}) {
  return (
    <>
      <span className="text-[12px] font-bold leading-none" style={{ color: 'var(--viz-ink-3)', blockSize: cell, display: 'flex', alignItems: 'center' }} aria-hidden="true">
        {label}
      </span>
      {columns.map((column, c) => {
        const d = column[r]!
        if (d.future) return <span key={c} style={{ blockSize: cell }} aria-hidden="true" />
        return (
          <div
            key={c}
            className="pv-viz-cell pv-viz-mark"
            data-day={d.key}
            data-trophy={d.trophies.length > 0 ? 'true' : undefined}
            data-active={active === d.key ? 'true' : undefined}
            style={{ background: cellColor(levelOf(d.minutes, max)), blockSize: cell, inlineSize: cell }}
            {...tip.bind(tipFor(d))}
          />
        )
      })}
    </>
  )
}
