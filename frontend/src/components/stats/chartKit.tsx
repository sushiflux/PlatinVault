import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type FocusEvent,
  type PointerEvent as ReactPointerEvent,
  type ReactNode,
} from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { IconChartBar, IconTable } from '@tabler/icons-react'
import { motion, useReducedMotion } from 'motion/react'
import { useFormat } from '../../i18n/format'
import { IconButton } from '../ui/IconButton'
import './stats.css'

// Shared building blocks of the statistics tab: formatting, measuring, the chart tooltip, the card with its
// chart/table switch, the table twin and skeletons. Charts themselves live in their own files.

// ---- formatting -------------------------------------------------------------------------------------------------

// Numbers, percentages, durations and dates of the statistics tab in the language of the app. A hook (not module-level
// formatters), so every chart renders again with the right texts and formats when the language is switched.
export function useStatsFormat() {
  const f = useFormat()
  const { t } = useTranslation('stats')
  return useMemo(() => {
    const int = (n: number): string => f.number(n, 0)
    const dec = (n: number): string => f.number(n, 1)

    // "62 Std." / "62 h", "1,5 Std.", "45 Min.": minutes below one hour, one decimal below ten hours
    const hours = (value: number): string => {
      if (value < 1) return t('units.minutes', { value: int(Math.round(value * 60)) })
      if (value < 10) return t('units.hours', { value: dec(value) })
      return t('units.hours', { value: int(Math.round(value)) })
    }

    const splitMinutes = (minutes: number) => {
      const h = Math.floor(minutes / 60)
      return { h, m: Math.round(minutes - h * 60) }
    }

    return {
      locale: f.locale,
      int,
      dec,
      share: (n: number): string => f.number(n, 2),
      // "48,3 GB": a file size the way Steam writes it
      bytes: (n: number): string => f.bytes(n),
      // "1,8 %", "12 %", "49,6 %": a share given as 0-100 with at most one decimal
      pct: (value: number): string => f.percent(value, 1),
      pctInt: (value: number): string => f.percent(value, 0),
      hours,
      minutes: (minutes: number): string => {
        if (minutes < 60) return t('units.minutes', { value: int(minutes) })
        const { h, m } = splitMinutes(minutes)
        return m > 0 ? t('units.hoursMinutes', { hours: int(h), minutes: int(m) }) : t('units.hours', { value: int(h) })
      },
      // Compact for figures in narrow columns: "7 h 11 min", "45 min"
      minutesShort: (minutes: number): string => {
        if (minutes < 60) return t('units.minutesCompact', { value: int(minutes) })
        const { h, m } = splitMinutes(minutes)
        return m > 0 ? t('units.hoursMinutesCompact', { hours: int(h), minutes: int(m) }) : t('units.hoursCompact', { value: int(h) })
      },
      dateShort: (ts: number): string => f.date(ts, 'dayMonth'),
      dateLong: (ts: number): string => f.date(ts, 'dateLong'),
      monthYear: (ts: number): string => f.date(ts, 'monthYear'),
      month: (ts: number): string => f.date(ts, 'month'),
      dateTime: (ts: number): string => f.date(ts, 'dateTimeShort'),
      weekdayDate: (ts: number): string => f.date(ts, 'dateWithWeekday'),
      time: (ts: number): string => f.date(ts, 'time'),
    }
  }, [f, t])
}

export type StatsFormat = ReturnType<typeof useStatsFormat>

// ---- measuring --------------------------------------------------------------------------------------------------

// Width of an element, kept up to date. A callback ref, so it also works when the element appears later (a chart that
// first shows a placeholder). The first value is read before the first paint, so a chart never flashes at 0.
export function useElementWidth<T extends HTMLElement>(): [(node: T | null) => void, number] {
  const [node, setNode] = useState<T | null>(null)
  const [width, setWidth] = useState(0)

  useLayoutEffect(() => {
    if (!node) return
    setWidth(Math.floor(node.getBoundingClientRect().width))
    const observer = new ResizeObserver((entries) => {
      const next = entries[0]?.contentRect.width
      if (next !== undefined) setWidth(Math.floor(next))
    })
    observer.observe(node)
    return () => observer.disconnect()
  }, [node])

  return [setNode, width]
}

// ---- chart tooltip ----------------------------------------------------------------------------------------------

interface TipState {
  x: number
  y: number
  content: ReactNode
  // true = placed above the point (keyboard focus), false = next to the pointer
  anchored: boolean
}

export interface ChartTipApi {
  tip: TipState | null
  showAt: (x: number, y: number, content: ReactNode, anchored?: boolean) => void
  hide: () => void
  // Spread onto a mark: hover (mouse, pen), tap (touch) and keyboard focus all show the same tooltip
  bind: (content: ReactNode) => {
    onPointerMove: (e: ReactPointerEvent<Element>) => void
    onPointerDown: (e: ReactPointerEvent<Element>) => void
    onPointerLeave: (e: ReactPointerEvent<Element>) => void
    onFocus: (e: FocusEvent<Element>) => void
    onBlur: () => void
    'data-viz-mark': string
  }
}

export function useChartTip(): ChartTipApi {
  const [tip, setTip] = useState<TipState | null>(null)
  const showAt = useCallback((x: number, y: number, content: ReactNode, anchored = false) => setTip({ x, y, content, anchored }), [])
  const hide = useCallback(() => setTip(null), [])

  const bind = useCallback<ChartTipApi['bind']>(
    (content) => ({
      onPointerMove: (e) => showAt(e.clientX, e.clientY, content),
      onPointerDown: (e) => showAt(e.clientX, e.clientY, content),
      // A finger lifting must not close what was just tapped; touch closes on the next tap elsewhere (see ChartTip)
      onPointerLeave: (e) => {
        if (e.pointerType === 'mouse' || e.pointerType === 'pen') setTip(null)
      },
      onFocus: (e) => {
        if (!e.currentTarget.matches(':focus-visible')) return
        const rect = e.currentTarget.getBoundingClientRect()
        showAt(rect.left + rect.width / 2, rect.top, content, true)
      },
      onBlur: () => setTip(null),
      'data-viz-mark': '',
    }),
    [showAt]
  )

  return { tip, showAt, hide, bind }
}

const TIP_GAP = 14
const TIP_MARGIN = 8

// The tooltip box, rendered into <body>: follows the pointer (or sits above a focused mark) and always stays on screen.
export function ChartTip({ api }: { api: ChartTipApi }) {
  const { tip, hide } = api
  const ref = useRef<HTMLDivElement>(null)

  useLayoutEffect(() => {
    const el = ref.current
    if (!el || !tip) return
    const rect = el.getBoundingClientRect()
    const vw = document.documentElement.clientWidth
    const vh = window.innerHeight
    let left: number
    let top: number
    if (tip.anchored) {
      left = tip.x - rect.width / 2
      top = tip.y - rect.height - 10
      if (top < TIP_MARGIN) top = tip.y + 28
    } else {
      left = tip.x + TIP_GAP
      if (left + rect.width > vw - TIP_MARGIN) left = tip.x - rect.width - TIP_GAP
      top = tip.y - rect.height - 12
      if (top < TIP_MARGIN) top = tip.y + 18
    }
    left = Math.min(Math.max(left, TIP_MARGIN), Math.max(TIP_MARGIN, vw - rect.width - TIP_MARGIN))
    top = Math.min(Math.max(top, TIP_MARGIN), Math.max(TIP_MARGIN, vh - rect.height - TIP_MARGIN))
    el.style.left = `${left}px`
    el.style.top = `${top}px`
  })

  const open = tip !== null
  useEffect(() => {
    if (!open) return
    const onDown = (e: PointerEvent) => {
      if (e.target instanceof Element && e.target.closest('[data-viz-mark]')) return
      hide()
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') hide()
    }
    window.addEventListener('pointerdown', onDown)
    window.addEventListener('keydown', onKey)
    window.addEventListener('scroll', hide, true)
    return () => {
      window.removeEventListener('pointerdown', onDown)
      window.removeEventListener('keydown', onKey)
      window.removeEventListener('scroll', hide, true)
    }
  }, [open, hide])

  if (!tip) return null
  return createPortal(
    <div ref={ref} className="pv-viz-tip" role="tooltip" style={{ left: 0, top: 0 }}>
      {tip.content}
    </div>,
    document.body
  )
}

// Tooltip body helpers. Values lead (strong), names follow; series are keyed by a short stroke, never a box.
export function TipTitle({ children }: { children: ReactNode }) {
  return <span className="pv-viz-tip-title">{children}</span>
}

export function TipSub({ children }: { children: ReactNode }) {
  return <span className="pv-viz-tip-sub">{children}</span>
}

export function TipRow({ colorVar, name, value }: { colorVar?: string; name: ReactNode; value: ReactNode }) {
  return (
    <span className="pv-viz-tip-row">
      {colorVar && <span className="pv-viz-tip-key" style={{ background: `var(${colorVar})` }} aria-hidden="true" />}
      <span>{name}</span>
      <b>{value}</b>
    </span>
  )
}

// ---- motion -----------------------------------------------------------------------------------------------------

// Cards fade and slide in one after the other (opacity/transform only, so nothing ever moves the layout)
export function useEntrance(order = 0) {
  const reduce = useReducedMotion() ?? false
  return {
    initial: reduce ? false : ({ opacity: 0, y: 12 } as const),
    animate: { opacity: 1, y: 0 },
    transition: { duration: 0.42, delay: Math.min(order * 0.05, 0.4), ease: [0.22, 1, 0.36, 1] as const },
  }
}

export function Reveal({ order = 0, className, children }: { order?: number; className?: string; children: ReactNode }) {
  const entrance = useEntrance(order)
  return (
    <motion.div className={className} {...entrance}>
      {children}
    </motion.div>
  )
}

// ---- legend -----------------------------------------------------------------------------------------------------

export interface LegendEntry {
  label: string
  // CSS variable name of the series colour, e.g. '--viz-1'
  colorVar: string
  kind?: 'box' | 'line' | 'dot'
  // Optional raw CSS colour instead of a variable (tracks, washes)
  css?: string
}

export function Legend({ entries }: { entries: LegendEntry[] }) {
  return (
    <div className="pv-viz-legend">
      {entries.map((entry) => (
        <span key={entry.label}>
          <span
            className={`pv-viz-key ${entry.kind === 'line' ? 'pv-viz-key--line' : entry.kind === 'dot' ? 'pv-viz-key--dot' : ''}`}
            style={{ background: entry.css ?? `var(${entry.colorVar})` }}
            aria-hidden="true"
          />
          {entry.label}
        </span>
      ))}
    </div>
  )
}

// ---- table twin -------------------------------------------------------------------------------------------------

export interface TableColumn {
  label: string
  numeric?: boolean
}

// Every chart has this as its WCAG-clean equivalent; the first column is the row header
export function DataTable({ caption, columns, rows }: { caption: string; columns: TableColumn[]; rows: ReactNode[][] }) {
  return (
    <div className="pv-viz-table-wrap" tabIndex={0} role="region" aria-label={caption}>
      <table className="pv-viz-table">
        <caption>{caption}</caption>
        <thead>
          <tr>
            {columns.map((col) => (
              <th key={col.label} scope="col" className={col.numeric ? 'is-num' : undefined}>
                {col.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, r) => (
            <tr key={r}>
              {row.map((cell, c) =>
                c === 0 ? (
                  <th key={c} scope="row">
                    {cell}
                  </th>
                ) : (
                  <td key={c} className={columns[c]?.numeric ? 'is-num' : undefined}>
                    {cell}
                  </td>
                )
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ---- card -------------------------------------------------------------------------------------------------------

export interface TableSpec {
  caption: string
  columns: TableColumn[]
  rows: ReactNode[][]
}

interface ChartCardProps {
  title: string
  icon?: ReactNode
  // One calm sentence that states what the chart shows (and doubles as its text alternative)
  subtitle?: ReactNode
  table?: TableSpec
  // Extra controls on the right of the title (range switch ...) - the table switch is added by the card
  actions?: ReactNode
  order?: number
  className?: string
  children: ReactNode
  footer?: ReactNode
}

export function ChartCard({ title, icon, subtitle, table, actions, order = 0, className = '', children, footer }: ChartCardProps) {
  const { t } = useTranslation('stats')
  const [view, setView] = useState<'chart' | 'table'>('chart')
  const entrance = useEntrance(order)
  const showTable = view === 'table' && table
  return (
    <motion.section className={`pv-card pv-viz-card ${className}`.trim()} aria-label={title} {...entrance}>
      <header className="pv-viz-head">
        <div className="min-w-0">
          <h3 className="pv-viz-title">
            {icon}
            <span>{title}</span>
          </h3>
          {subtitle && <p className="pv-viz-sub">{subtitle}</p>}
        </div>
        <div className="flex shrink-0 items-center gap-1">
          {actions}
          {table && (
            <IconButton
              label={view === 'table' ? t('chart.showChart') : t('chart.showTable')}
              description={t('chart.tableDescription')}
              aria-pressed={view === 'table'}
              onClick={() => setView(view === 'table' ? 'chart' : 'table')}
            >
              {view === 'table' ? <IconChartBar size={18} /> : <IconTable size={18} />}
            </IconButton>
          )}
        </div>
      </header>
      {showTable ? <DataTable {...table} /> : children}
      {footer}
    </motion.section>
  )
}

// ---- skeleton ---------------------------------------------------------------------------------------------------

// A card of the final height while its data loads, so nothing moves when the chart arrives. `height` is the body
// of the card when it is wide, `narrowHeight` when the card is narrow (stacked rows, fewer columns).
export function ChartSkeleton({ height, narrowHeight, className = '', label }: { height: number; narrowHeight?: number; className?: string; label?: string }) {
  const { t } = useTranslation('stats')
  return (
    <section className={`pv-card pv-viz-card ${className}`.trim()} aria-busy="true" aria-label={label ?? t('chart.loading')}>
      <div className="pv-viz-skel animate-pulse" style={{ blockSize: '1.1rem', inlineSize: '38%', opacity: 0.7 }} />
      <div
        className="pv-viz-skel pv-viz-skel-body animate-pulse mt-3"
        style={{ ['--h' as string]: `${height}px`, ['--h-narrow' as string]: `${narrowHeight ?? height}px`, opacity: 0.45 }}
      />
    </section>
  )
}

// ---- small figures ----------------------------------------------------------------------------------------------

// A labelled figure for the row above a chart (this week / this month ...)
export function Kpi({ label, value, hint }: { label: string; value: ReactNode; hint?: ReactNode }) {
  return (
    <div className="min-w-0">
      <div className="text-xs font-bold" style={{ color: 'var(--viz-ink-3)' }}>
        {label}
      </div>
      <div className="text-lg font-extrabold leading-tight" style={{ color: 'var(--viz-ink)' }}>
        {value}
      </div>
      {hint && (
        <div className="text-xs" style={{ color: 'var(--viz-ink-2)' }}>
          {hint}
        </div>
      )}
    </div>
  )
}

// Calm message in the place of a chart (same height as the chart, so nothing moves when data arrives)
export function EarlyState({ height, icon, title, children }: { height: number; icon: ReactNode; title: string; children?: ReactNode }) {
  return (
    <div
      className="flex flex-col items-center justify-center gap-2 rounded-[var(--radius-md)] px-4 text-center"
      style={{ minBlockSize: height, background: 'var(--viz-empty)' }}
    >
      <span style={{ color: 'var(--accent-crystal)' }}>{icon}</span>
      <p className="max-w-md text-sm font-bold" style={{ color: 'var(--viz-ink)' }}>
        {title}
      </p>
      {children && (
        <p className="max-w-md text-xs leading-relaxed" style={{ color: 'var(--viz-ink-2)' }}>
          {children}
        </p>
      )}
    </div>
  )
}
