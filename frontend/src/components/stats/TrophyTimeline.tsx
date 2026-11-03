import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { IconChartHistogram, IconChevronLeft, IconChevronRight } from '@tabler/icons-react'
import { IconButton } from '../ui/IconButton'
import { ChartCard, ChartTip, TipRow, TipSub, TipTitle, useChartTip, useElementWidth, useStatsFormat } from './chartKit'
import { barWindow, countTicks, dayBuckets, labelIndices, type Unlock } from './statsData'

const HEIGHT = 244
// Room above the plot (the count of the highest bar) and below it (date labels; one more row when the years differ)
const TOP = 24
const BOTTOM = 34
const YEAR_ROW = 14
// Width of the axis column with the counts
const AXIS_W = 34
// Room left and right of the first and last bar: about half the widest date label, so a label centred under an end bar is not cut off
const PAD_X = 18
// Room one date label needs (labels are thinned so none touches another)
const LABEL_ROOM = 64
// A day with an extreme number of trophies lists the first ones and counts the rest
const MAX_TIP_ROWS = 12
// A horizontal swipe of at least this many px pages the chart (touch only)
const SWIPE_MIN = 48

interface Props {
  unlocks: Unlock[]
  // All trophies of the game (the running total in the tooltip reads "n of total")
  total: number
  order?: number
}

const clamp = (value: number, min: number, max: number): number => Math.min(Math.max(value, min), max)

// A bar with a 4 px rounded top, anchored flat to the baseline
function barPath(x: number, top: number, base: number, width: number): string {
  const r = Math.min(4, width / 2, base - top)
  return `M${x} ${base} V${top + r} A${r} ${r} 0 0 1 ${x + r} ${top} H${x + width - r} A${r} ${r} 0 0 1 ${x + width} ${top + r} V${base} Z`
}

// A1: trophies unlocked per day. Only days with at least one unlock get a bar (a categorical axis of those dates, nothing in between),
// the count stands above every bar. The bars use the whole width: one day is one calm bar, more days share the room; when they would
// get thinner than 24 px the chart shows a window of days and pages through the rest (newest days first).
export function TrophyTimeline({ unlocks, total, order }: Props) {
  const { t } = useTranslation('stats')
  const fmt = useStatsFormat()
  const tip = useChartTip()
  const [wrapRef, width] = useElementWidth<HTMLDivElement>()
  const plotRef = useRef<HTMLDivElement | null>(null)
  const swipe = useRef<{ x: number; y: number } | null>(null)
  const [active, setActive] = useState<number | null>(null)
  // End (exclusive) of the window in days; null = the newest days
  const [windowEnd, setWindowEnd] = useState<number | null>(null)
  // Set by the keyboard: show the tooltip of this day above its bar once the window has moved to it
  const [anchor, setAnchor] = useState<number | null>(null)

  const setPlot = (node: HTMLDivElement | null) => {
    plotRef.current = node
    wrapRef(node)
  }

  const model = useMemo(() => {
    const buckets = dayBuckets(unlocks)
    let running = 0
    const totals = buckets.map((b) => (running += b.count))
    const max = Math.max(1, ...buckets.map((b) => b.count))
    // One scale for the whole data set, so paging never changes the height of a bar
    const ticks = countTicks(max)
    const years = new Set(buckets.map((b) => new Date(b.ts).getFullYear()))
    return { buckets, totals, ticks, yMax: ticks[ticks.length - 1] ?? max, multiYear: years.size > 1 }
  }, [unlocks])

  const { buckets } = model
  const n = buckets.length
  const room = Math.max(0, width - 2 * PAD_X)
  const { visible, paging, barW, gap } = barWindow(room, n)
  const end = paging ? clamp(windowEnd ?? n, visible, n) : n
  const start = end - visible
  const bottom = BOTTOM + (model.multiYear ? YEAR_ROW : 0)
  const plotH = HEIGHT - TOP - bottom
  const slot = barW + gap
  // The bars sit in the middle of the plot (always when they are capped, otherwise they fill it)
  const offset = Math.floor((room - (visible * slot - gap)) / 2)
  const x = (i: number) => PAD_X + offset + (i - start) * slot
  const y = (v: number) => TOP + plotH - (v / model.yMax) * plotH

  // New data (another game, a new unlock) starts at the newest days again
  const [seenDays, setSeenDays] = useState(n)
  if (seenDays !== n) {
    setSeenDays(n)
    setWindowEnd(null)
    setActive(null)
  }

  const tipFor = (i: number) => {
    const b = buckets[i]!
    const shown = b.unlocks.slice(0, MAX_TIP_ROWS)
    const more = b.unlocks.length - shown.length
    return (
      <>
        <TipTitle>{fmt.weekdayDate(b.ts)}</TipTitle>
        <TipSub>{t('timeline.tipCount', { count: b.count })}</TipSub>
        {shown.map((u) => (
          <TipRow key={`${u.ts}-${u.name}`} name={u.name} value={fmt.time(u.ts)} />
        ))}
        {more > 0 && <TipSub>{t('timeline.tipMore', { n: more })}</TipSub>}
        <TipSub>{t('timeline.tipRunning', { n: model.totals[i]!, total })}</TipSub>
      </>
    )
  }

  // Keyboard: the tooltip of the chosen day above its bar, placed after the render that moved the window to it (runs per request only)
  useEffect(() => {
    if (anchor === null || !plotRef.current || width === 0) return
    const b = buckets[anchor]
    if (!b || anchor < start || anchor >= end) return
    const rect = plotRef.current.getBoundingClientRect()
    tip.showAt(rect.left + x(anchor) + barW / 2, rect.top + y(b.count), tipFor(anchor), true)
  }, [anchor, start, end])

  if (n === 0) return null

  const page = (direction: -1 | 1) => {
    tip.hide()
    setActive(null)
    setAnchor(null)
    setWindowEnd(clamp(end + direction * visible, visible, n))
  }

  const pointer = (i: number) => (e: PointerEvent<SVGRectElement>) => {
    setAnchor(null)
    setActive(i)
    tip.showAt(e.clientX, e.clientY, tipFor(i))
  }

  const onKey = (e: KeyboardEvent<HTMLDivElement>) => {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(e.key)) return
    e.preventDefault()
    const current = active ?? (e.key === 'ArrowLeft' || e.key === 'End' ? n : -1)
    const next =
      e.key === 'Home' ? 0 : e.key === 'End' ? n - 1 : e.key === 'ArrowLeft' ? Math.max(0, current - 1) : Math.min(n - 1, current + 1)
    setActive(next)
    // Moving past the edge of the window turns the page to that day
    if (paging && next < start) setWindowEnd(next + visible)
    else if (paging && next >= end) setWindowEnd(next + 1)
    setAnchor(next)
  }

  // Touch: a swipe to the right shows earlier days, to the left later ones
  const swipeStart = (e: PointerEvent<SVGSVGElement>) => {
    swipe.current = paging && e.pointerType === 'touch' ? { x: e.clientX, y: e.clientY } : null
  }
  const swipeEnd = (e: PointerEvent<SVGSVGElement>) => {
    const from = swipe.current
    swipe.current = null
    if (!from) return
    const dx = e.clientX - from.x
    const dy = e.clientY - from.y
    if (Math.abs(dx) < SWIPE_MIN || Math.abs(dx) < 1.5 * Math.abs(dy)) return
    if (dx > 0 && start > 0) page(-1)
    else if (dx < 0 && end < n) page(1)
  }

  const labelled = labelIndices(visible, slot, LABEL_ROOM).map((j) => start + j)
  const first = buckets[0]!
  const last = buckets[n - 1]!
  const windowText = (from: number, to: number) => t('timeline.window', { from: fmt.int(from), to: fmt.int(to), total: fmt.int(n) })

  return (
    <ChartCard
      title={t('timeline.title')}
      icon={<IconChartHistogram size={18} />}
      subtitle={
        <>
          <Trans
            t={t}
            i18nKey="timeline.subtitle"
            count={n}
            values={{ unlocks: fmt.int(unlocks.length), from: fmt.dateLong(first.ts), to: fmt.dateLong(last.ts) }}
            components={{ strong: <strong /> }}
          />{' '}
          {t('timeline.axisNote')}
        </>
      }
      order={order}
      className="h-full"
      table={{
        caption: t('timeline.tableCaption'),
        columns: [
          { label: t('timeline.colDay') },
          { label: t('timeline.colNew'), numeric: true },
          { label: t('timeline.colTotal'), numeric: true },
          { label: t('timeline.colTrophies') },
        ],
        rows: buckets.map((b, i) => [fmt.dateLong(b.ts), b.count, model.totals[i]!, b.names.join(', ')]),
      }}
    >
      {paging && (
        // The window text always takes the room of its widest form (an invisible copy), so the arrows never move when it changes
        <div className="pv-viz-pager mb-1 flex items-center justify-end gap-1">
          <span className="inline-grid text-xs font-bold tabular-nums" style={{ color: 'var(--viz-ink-2)' }}>
            <span className="invisible col-start-1 row-start-1 text-end" aria-hidden="true">
              {windowText(n, n)}
            </span>
            <span className="col-start-1 row-start-1 text-end" aria-live="polite">
              {windowText(start + 1, end)}
            </span>
          </span>
          <IconButton label={t('timeline.earlier')} disabled={start === 0} onClick={() => page(-1)}>
            <IconChevronLeft size={18} aria-hidden="true" />
          </IconButton>
          <IconButton label={t('timeline.later')} disabled={end === n} onClick={() => page(1)}>
            <IconChevronRight size={18} aria-hidden="true" />
          </IconButton>
        </div>
      )}
      <div className="flex" style={{ blockSize: HEIGHT }}>
        {/* Axis column with the counts */}
        <svg width={AXIS_W} height={HEIGHT} aria-hidden="true" style={{ display: 'block', flexShrink: 0 }}>
          {model.ticks.map((tick) => (
            <text key={tick} x={AXIS_W - 8} y={y(tick)} dy="0.32em" textAnchor="end" className="pv-viz-axis-text">
              {fmt.int(tick)}
            </text>
          ))}
        </svg>
        <div
          ref={setPlot}
          className="pv-viz-focusable min-w-0 flex-1"
          tabIndex={0}
          role="group"
          aria-label={t('timeline.group')}
          onKeyDown={onKey}
          onBlur={() => {
            setActive(null)
            setAnchor(null)
            tip.hide()
          }}
        >
          {width > 0 && (
            <svg
              width={width}
              height={HEIGHT}
              role="img"
              aria-label={t('timeline.chartLabel', { count: n, unlocks: fmt.int(unlocks.length) })}
              style={{ display: 'block', touchAction: 'pan-y' }}
              onPointerDown={swipeStart}
              onPointerUp={swipeEnd}
              onPointerCancel={() => {
                swipe.current = null
              }}
            >
              {/* recessive grid, the baseline a little firmer */}
              {model.ticks.map((tick) => (
                <line key={tick} x1={0} x2={width} y1={y(tick)} y2={y(tick)} stroke={tick === 0 ? 'var(--viz-axis)' : 'var(--viz-grid)'} strokeWidth={1} />
              ))}

              {/* hover layer behind the bars: the day under the pointer */}
              {active !== null && active >= start && active < end && (
                <rect x={x(active) - gap / 2} y={TOP} width={slot} height={plotH} fill="var(--viz-wash)" pointerEvents="none" />
              )}

              {buckets.slice(start, end).map((b, j) => {
                const i = start + j
                const top = Math.min(y(b.count), TOP + plotH - 3)
                return (
                  <g key={b.key} pointerEvents="none">
                    <path
                      d={barPath(x(i), top, TOP + plotH, barW)}
                      fill="var(--viz-1)"
                      className="pv-viz-mark pv-viz-vbar"
                      data-active={active === i ? 'true' : undefined}
                      style={{ ['--i' as string]: Math.min(j, 40) }}
                    />
                    {/* the count above every bar */}
                    <text x={x(i) + barW / 2} y={top - 6} textAnchor="middle" className="pv-viz-value-text">
                      {fmt.int(b.count)}
                    </text>
                  </g>
                )
              })}

              {labelled.map((i, k) => {
                const b = buckets[i]!
                const center = x(i) + barW / 2
                const year = new Date(b.ts).getFullYear()
                const previous = k > 0 ? new Date(buckets[labelled[k - 1]!]!.ts).getFullYear() : null
                return (
                  <g key={b.key}>
                    <text x={center} y={TOP + plotH + 18} textAnchor="middle" className="pv-viz-axis-text">
                      {fmt.dateShort(b.ts)}
                    </text>
                    {model.multiYear && year !== previous && (
                      <text x={center} y={TOP + plotH + 18 + YEAR_ROW} textAnchor="middle" className="pv-viz-axis-text">
                        {String(year)}
                      </text>
                    )}
                  </g>
                )
              })}

              {/* hit targets: a full-height slot per day, never narrower than the bar plus its gap */}
              {buckets.slice(start, end).map((b, j) => {
                const i = start + j
                return (
                  <rect
                    key={b.key}
                    x={x(i) - gap / 2}
                    y={0}
                    width={slot}
                    height={HEIGHT}
                    fill="transparent"
                    data-viz-mark=""
                    data-day={i}
                    onPointerMove={pointer(i)}
                    onPointerDown={pointer(i)}
                    onPointerLeave={(e) => {
                      if (e.pointerType === 'mouse' || e.pointerType === 'pen') {
                        setActive(null)
                        tip.hide()
                      }
                    }}
                  />
                )
              })}
            </svg>
          )}
        </div>
      </div>
      <ChartTip api={tip} />
    </ChartCard>
  )
}
