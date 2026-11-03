import { useMemo, useState, type KeyboardEvent } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { IconCalendarClock } from '@tabler/icons-react'
import { ChartCard, ChartTip, TipRow, TipSub, TipTitle, useChartTip, useStatsFormat } from './chartKit'
import { bestSlot, type Unlock, weekdayHourMatrix, weekdayNames } from './statsData'

const LEVELS = 5
const HOURS = Array.from({ length: 24 }, (_, h) => h)

function levelOf(count: number, max: number): number {
  if (count <= 0 || max <= 0) return 0
  return Math.max(1, Math.min(LEVELS, Math.ceil((count / max) * LEVELS)))
}

const levelColor = (level: number): string => (level === 0 ? 'var(--viz-empty)' : `var(--viz-heat-${level})`)

// A2: when the player is most successful - weekday x hour of the unlock times (local time of this browser).
export function UnlockHeatmap({ unlocks, order }: { unlocks: Unlock[]; order?: number }) {
  const { t } = useTranslation('stats')
  const fmt = useStatsFormat()
  const tip = useChartTip()
  // Weekday names of the active language, Monday first (Intl, so every language works without its own list)
  const weekdays = useMemo(() => weekdayNames(fmt.locale, 'long'), [fmt.locale])
  const weekdaysShort = useMemo(() => weekdayNames(fmt.locale, 'short'), [fmt.locale])
  const hourRange = (hour: number) => t('heatmap.hourRange', { from: hour, to: hour + 1 })
  const [active, setActive] = useState<{ r: number; h: number } | null>(null)

  const model = useMemo(() => {
    const matrix = weekdayHourMatrix(unlocks)
    const max = Math.max(...matrix.flat())
    const best = bestSlot(matrix)
    const perDay = matrix.map((row) => row.reduce((a, b) => a + b, 0))
    const bestDay = perDay.indexOf(Math.max(...perDay))
    return { matrix, max, best, perDay, bestDay }
  }, [unlocks])

  const tipContent = (r: number, h: number) => {
    const count = model.matrix[r]?.[h] ?? 0
    return (
      <>
        <TipTitle>{t('heatmap.slot', { weekday: weekdays[r] ?? '', hours: hourRange(h) })}</TipTitle>
        <TipRow name={t('heatmap.tipTrophies', { count })} value={count} />
        <TipSub>{t('heatmap.tipLocalTime')}</TipSub>
      </>
    )
  }

  const onKey = (e: KeyboardEvent<HTMLDivElement>) => {
    const moves: Record<string, [number, number]> = { ArrowUp: [-1, 0], ArrowDown: [1, 0], ArrowLeft: [0, -1], ArrowRight: [0, 1] }
    const delta = moves[e.key]
    if (!delta) return
    e.preventDefault()
    // The first arrow key lands on the strongest slot, the following ones move from there
    const start = { r: model.best?.weekday ?? 0, h: model.best?.hour ?? 0 }
    const next = active
      ? { r: Math.min(6, Math.max(0, active.r + delta[0])), h: Math.min(23, Math.max(0, active.h + delta[1])) }
      : start
    setActive(next)
    const el = e.currentTarget.querySelector<HTMLElement>(`[data-r="${next.r}"][data-h="${next.h}"]`)
    if (el) {
      const rect = el.getBoundingClientRect()
      tip.showAt(rect.left + rect.width / 2, rect.top, tipContent(next.r, next.h), true)
    }
  }

  const best = model.best
  return (
    <ChartCard
      title={t('heatmap.title')}
      icon={<IconCalendarClock size={18} />}
      subtitle={
        best ? (
          <Trans
            t={t}
            i18nKey="heatmap.subtitle"
            values={{
              slot: t('heatmap.slot', { weekday: weekdays[best.weekday] ?? '', hours: hourRange(best.hour) }),
              n: best.count,
              day: weekdays[model.bestDay] ?? '',
              total: model.perDay[model.bestDay] ?? 0,
            }}
            components={{ strong: <strong /> }}
          />
        ) : undefined
      }
      order={order}
      className="h-full"
      table={{
        caption: t('heatmap.tableCaption'),
        columns: [{ label: t('heatmap.colWeekday') }, { label: t('heatmap.colTime') }, { label: t('heatmap.colTrophies'), numeric: true }],
        rows: model.matrix
          .flatMap((row, r) => row.map((count, h) => ({ r, h, count })))
          .filter((c) => c.count > 0)
          .sort((a, b) => b.count - a.count || a.r - b.r || a.h - b.h)
          .map((c) => [weekdays[c.r], hourRange(c.h), c.count]),
      }}
      footer={
        <div className="mt-3 flex items-center gap-2 text-xs" style={{ color: 'var(--viz-ink-2)' }}>
          <span>{t('heatmap.legendFewer')}</span>
          <span className="flex gap-[2px]" aria-hidden="true">
            <span className="pv-viz-cell inline-block h-3 w-5" style={{ background: 'var(--viz-empty)' }} />
            {Array.from({ length: LEVELS }, (_, i) => (
              <span key={i} className="pv-viz-cell inline-block h-3 w-5" style={{ background: levelColor(i + 1) }} />
            ))}
          </span>
          <span>{t('heatmap.legendMore')}</span>
        </div>
      }
    >
      <div
        className="pv-viz-focusable"
        tabIndex={0}
        role="group"
        aria-label={t('heatmap.group')}
        onKeyDown={onKey}
        onBlur={() => {
          setActive(null)
          tip.hide()
        }}
      >
        <div className="grid items-center gap-[2px]" style={{ gridTemplateColumns: '1.9rem repeat(24, minmax(0, 1fr))' }}>
          <span aria-hidden="true" />
          {HOURS.map((h) => (
            <span key={h} className="relative h-4 overflow-visible text-center text-[12px] leading-4" style={{ color: 'var(--viz-ink-3)' }} aria-hidden="true">
              {h % 3 === 0 ? h : ''}
            </span>
          ))}
          {model.matrix.map((row, r) => (
            <HeatRow key={r} r={r} label={weekdaysShort[r] ?? ''} row={row} max={model.max} active={active} tip={tip} tipContent={tipContent} />
          ))}
        </div>
        <div className="mt-1 text-right text-[12px]" style={{ color: 'var(--viz-ink-3)' }}>
          {t('heatmap.axisHours')}
        </div>
      </div>
      <ChartTip api={tip} />
    </ChartCard>
  )
}

function HeatRow({
  r,
  label,
  row,
  max,
  active,
  tip,
  tipContent,
}: {
  r: number
  label: string
  row: number[]
  max: number
  active: { r: number; h: number } | null
  tip: ReturnType<typeof useChartTip>
  tipContent: (r: number, h: number) => React.ReactNode
}) {
  return (
    <>
      <span className="text-[12px] font-bold" style={{ color: 'var(--viz-ink-3)' }} aria-hidden="true">
        {label}
      </span>
      {row.map((count, h) => (
        <div
          key={h}
          className="pv-viz-cell pv-viz-cell-in pv-viz-mark"
          data-r={r}
          data-h={h}
          data-active={active?.r === r && active.h === h ? 'true' : undefined}
          style={{ background: levelColor(levelOf(count, max)), blockSize: 24, ['--i' as string]: h + r * 2 }}
          {...tip.bind(tipContent(r, h))}
        />
      ))}
    </>
  )
}
