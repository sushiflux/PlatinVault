import type { ReactNode } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { IconClockHour4, IconStars } from '@tabler/icons-react'
import type { GameStatsDto, IgdbGameDto } from '../../api/types'
import { ChartCard, ChartTip, Legend, TipRow, TipSub, TipTitle, useChartTip, useStatsFormat, type StatsFormat } from './chartKit'
import { niceTicks } from './statsData'
import { Grow } from './Grow'

// Width reserved right of a full-length bar for its value label
const GUTTER = '4.2rem'

// ---- B1: playtime comparison ------------------------------------------------------------------------------------

interface PlaytimeProps {
  igdb: IgdbGameDto
  // Own playtime in hours (null = unknown, then only the usual times are shown)
  ownHours: number | null
  order?: number
}

// IGDB's usual playtimes as thin bars, the player's own time as one marker line across them.
export function PlaytimeCompare({ igdb, ownHours, order }: PlaytimeProps) {
  const { t } = useTranslation('stats')
  const fmt = useStatsFormat()
  const tip = useChartTip()
  const ttb = igdb.timeToBeat
  const rows = [
    { key: 'story', label: t('playtimeCompare.story.label'), sub: t('playtimeCompare.story.sub'), hours: ttb?.hastilyHours ?? null },
    { key: 'normal', label: t('playtimeCompare.normal.label'), sub: t('playtimeCompare.normal.sub'), hours: ttb?.normallyHours ?? null },
    { key: 'full', label: t('playtimeCompare.full.label'), sub: t('playtimeCompare.full.sub'), hours: ttb?.completelyHours ?? null },
  ].filter((r): r is typeof r & { hours: number } => r.hours !== null && r.hours > 0)
  if (rows.length === 0) return null

  const reference = rows.find((r) => r.key === 'full') ?? rows[rows.length - 1]!
  const max = Math.max(...rows.map((r) => r.hours), ownHours ?? 0)
  const ticks = niceTicks(max * 1.05, 4)
  const scale = ticks[ticks.length - 1] ?? max
  const at = (hours: number) => `calc((100% - ${GUTTER}) * ${Math.min(1, hours / scale)})`
  const ownFrac = ownHours !== null ? ownHours / scale : null
  const ownPercent = ownHours !== null ? Math.round((ownHours / reference.hours) * 100) : null
  const subtitleKey = reference.key === 'full' ? 'playtimeCompare.subtitleFull' : reference.key === 'normal' ? 'playtimeCompare.subtitleNormal' : 'playtimeCompare.subtitleStory'

  return (
    <ChartCard
      title={t('playtimeCompare.title')}
      icon={<IconClockHour4 size={18} />}
      subtitle={
        ownPercent !== null ? (
          <>
            <Trans
              t={t}
              i18nKey={subtitleKey}
              values={{ percent: fmt.pctInt(ownPercent), hours: fmt.hours(reference.hours) }}
              components={{ strong: <strong /> }}
            />
            {ttb?.count ? ` ${t('playtimeCompare.basis', { count: ttb.count })}` : ''}
          </>
        ) : ttb?.count ? (
          t('playtimeCompare.subtitleNoOwnBased', { count: ttb.count })
        ) : (
          t('playtimeCompare.subtitleNoOwn')
        )
      }
      order={order}
      className="h-full"
      table={{
        caption: t('playtimeCompare.tableCaption'),
        columns: [{ label: t('playtimeCompare.colGoal') }, { label: t('playtimeCompare.colUsual'), numeric: true }, { label: t('playtimeCompare.colYou'), numeric: true }],
        rows: rows.map((r) => [r.label, fmt.hours(r.hours), ownHours !== null ? fmt.pctInt(Math.round((ownHours / r.hours) * 100)) : '']),
      }}
      footer={
        <div className="mt-auto pt-4">
          <Legend
            entries={[
              { label: t('playtimeCompare.legendUsual'), colorVar: '--viz-1' },
              ...(ownHours !== null ? [{ label: t('playtimeCompare.legendOwn', { hours: fmt.hours(ownHours) }), colorVar: '--viz-2', kind: 'line' as const }] : []),
            ]}
          />
        </div>
      }
    >
      <div className="pv-viz-rows" style={{ ['--rows-cols' as string]: 'minmax(5.5rem, 8rem) minmax(0, 1fr)' }}>
        {/* header row: label of the marker */}
        <span data-ghost aria-hidden="true" />
        <div className="relative h-6" aria-hidden="true">
          {ownFrac !== null && ownHours !== null && (
            <>
              <span
                className="absolute top-0 whitespace-nowrap text-xs font-bold"
                style={{
                  left: at(ownHours),
                  transform: `translateX(${ownFrac > 0.82 ? '-100%' : ownFrac < 0.1 ? '0' : '-50%'})`,
                  color: 'var(--viz-ink)',
                }}
              >
                {t('playtimeCompare.markerOwn', { hours: fmt.hours(ownHours) })}
              </span>
              <GuideLines at={at} ticks={[]} ownHours={ownHours} top="1.15rem" />
            </>
          )}
        </div>
        {rows.map((r, i) => (
          <PlaytimeRow key={r.key} row={r} i={i} width={at(r.hours)} tip={tip} fmt={fmt} guides={<GuideLines at={at} ticks={ticks} ownHours={ownHours} />} />
        ))}
        {/* axis */}
        <span data-ghost aria-hidden="true" />
        <div className="relative h-6" aria-hidden="true">
          {ownHours !== null && <GuideLines at={at} ticks={[]} ownHours={ownHours} bottom="0.9rem" />}
          {ticks.map((tick) => (
            <span key={tick} className="absolute top-1.5 -translate-x-1/2 text-[12px]" style={{ left: at(tick), color: 'var(--viz-ink-3)' }}>
              {tick === ticks[ticks.length - 1] ? fmt.hours(tick) : fmt.dec(tick)}
            </span>
          ))}
        </div>
      </div>
      <ChartTip api={tip} />
    </ChartCard>
  )
}

// Hairline grid values and the marker of the player's own time; every row draws its own piece, so they join up
function GuideLines({
  at,
  ticks,
  ownHours,
  top = 0,
  bottom = 0,
}: {
  at: (v: number) => string
  ticks: number[]
  ownHours: number | null
  top?: string | number
  bottom?: string | number
}) {
  return (
    <>
      {ticks.map((t) => (
        <span key={t} className="pointer-events-none absolute w-px" style={{ left: at(t), top, bottom, background: 'var(--viz-grid)' }} aria-hidden="true" />
      ))}
      {ownHours !== null && (
        <span
          className="pointer-events-none absolute z-[2] w-[2px]"
          style={{ left: at(ownHours), top, bottom, marginLeft: -1, background: 'var(--viz-2)' }}
          aria-hidden="true"
        />
      )}
    </>
  )
}

function PlaytimeRow({
  row,
  i,
  width,
  tip,
  fmt,
  guides,
}: {
  row: { label: string; sub: string; hours: number }
  i: number
  width: string
  tip: ReturnType<typeof useChartTip>
  fmt: StatsFormat
  guides: ReactNode
}) {
  const { t } = useTranslation('stats')
  return (
    <>
      <div className="py-1.5">
        <div className="text-sm font-bold leading-tight" style={{ color: 'var(--viz-ink)' }}>
          {row.label}
        </div>
        <div className="text-xs" style={{ color: 'var(--viz-ink-3)' }}>
          {row.sub}
        </div>
      </div>
      <div
        className="pv-viz-focusable relative flex h-11 items-center"
        tabIndex={0}
        aria-label={t('playtimeCompare.rowLabel', { label: row.label, hours: fmt.hours(row.hours) })}
        {...tip.bind(
          <>
            <TipTitle>{row.label}</TipTitle>
            <TipRow colorVar="--viz-1" name={t('playtimeCompare.tipUsual')} value={fmt.hours(row.hours)} />
            <TipSub>{row.sub}</TipSub>
          </>
        )}
      >
        {guides}
        <Grow delay={i * 0.07} className="pv-viz-bar pv-viz-mark" style={{ width, minWidth: 4, position: 'relative', zIndex: 1 }} />
        <span className="absolute z-[1] text-xs font-bold" style={{ left: `calc(${width} + 8px)`, color: 'var(--viz-ink)', whiteSpace: 'nowrap' }}>
          {fmt.hours(row.hours)}
        </span>
      </div>
    </>
  )
}

// ---- B2: rating comparison --------------------------------------------------------------------------------------

interface RatingsProps {
  igdb: IgdbGameDto
  stats: GameStatsDto
  order?: number
}

interface RatingRow {
  key: string
  label: string
  sub: string
  value: number
  text: string
}

// Critics, players, Steam reviews and Metacritic on one 0-100 scale. One series (the sources are nominal): one hue.
export function RatingCompare({ igdb, stats, order }: RatingsProps) {
  const { t } = useTranslation('stats')
  const fmt = useStatsFormat()
  const tip = useChartTip()
  const rows: RatingRow[] = []
  if (igdb.aggregatedRating !== null)
    rows.push({
      key: 'critics',
      label: t('ratings.critics'),
      sub: igdb.aggregatedRatingCount ? t('ratings.count', { count: igdb.aggregatedRatingCount }) : t('ratings.average'),
      value: igdb.aggregatedRating,
      text: String(Math.round(igdb.aggregatedRating)),
    })
  if (igdb.totalRating !== null)
    rows.push({
      key: 'players',
      label: t('ratings.players'),
      sub: igdb.totalRatingCount ? t('ratings.count', { count: igdb.totalRatingCount }) : t('ratings.average'),
      value: igdb.totalRating,
      text: String(Math.round(igdb.totalRating)),
    })
  if (stats.reviewPercentPositive !== null)
    rows.push({
      key: 'steam',
      label: t('ratings.steam'),
      sub: stats.reviewTotal ? t('ratings.steamCount', { count: stats.reviewTotal }) : t('ratings.steamShare'),
      value: stats.reviewPercentPositive,
      text: fmt.pctInt(Math.round(stats.reviewPercentPositive)),
    })
  if (stats.metacriticScore !== null)
    rows.push({ key: 'meta', label: t('ratings.metacritic'), sub: t('ratings.metacriticSub'), value: stats.metacriticScore, text: String(stats.metacriticScore) })
  if (rows.length < 2) return null

  const ticks = [0, 25, 50, 75, 100]
  const at = (v: number) => `calc((100% - ${GUTTER}) * ${v / 100})`

  return (
    <ChartCard
      title={t('ratings.title')}
      icon={<IconStars size={18} />}
      subtitle={t('ratings.subtitle')}
      order={order}
      className="h-full"
      table={{
        caption: t('ratings.tableCaption'),
        columns: [{ label: t('ratings.colSource') }, { label: t('ratings.colScore'), numeric: true }, { label: t('ratings.colVotes') }],
        rows: rows.map((r) => [r.label, r.text, r.sub]),
      }}
      footer={
        <p className="pv-viz-note mt-auto">
          {t('ratings.note')}
        </p>
      }
    >
      <div className="pv-viz-rows" style={{ ['--rows-cols' as string]: 'minmax(6.5rem, 10rem) minmax(0, 1fr)' }}>
        {rows.map((r, i) => (
          <RatingRowView key={r.key} row={r} i={i} width={at(r.value)} tip={tip} guides={<GuideLines at={at} ticks={ticks} ownHours={null} />} />
        ))}
        <span data-ghost aria-hidden="true" />
        <div className="relative h-6" aria-hidden="true">
          {ticks.map((tick) => (
            <span key={tick} className="absolute top-1.5 -translate-x-1/2 text-[12px]" style={{ left: at(tick), color: 'var(--viz-ink-3)' }}>
              {tick}
            </span>
          ))}
        </div>
      </div>
      <ChartTip api={tip} />
    </ChartCard>
  )
}

function RatingRowView({ row, i, width, tip, guides }: { row: RatingRow; i: number; width: string; tip: ReturnType<typeof useChartTip>; guides: ReactNode }) {
  const { t } = useTranslation('stats')
  return (
    <>
      <div className="py-1.5">
        <div className="text-sm font-bold leading-tight" style={{ color: 'var(--viz-ink)' }}>
          {row.label}
        </div>
        <div className="text-xs" style={{ color: 'var(--viz-ink-3)' }}>
          {row.sub}
        </div>
      </div>
      <div
        className="pv-viz-focusable relative flex h-12 items-center"
        tabIndex={0}
        aria-label={t('ratings.rowLabel', { label: row.label, value: row.text, sub: row.sub })}
        {...tip.bind(
          <>
            <TipTitle>{row.label}</TipTitle>
            <TipRow colorVar="--viz-1" name={t('ratings.tipScore')} value={row.text} />
            <TipSub>{row.sub}</TipSub>
          </>
        )}
      >
        {guides}
        <Grow delay={i * 0.07} className="pv-viz-bar pv-viz-mark" style={{ width, minWidth: 4, position: 'relative', zIndex: 1 }} />
        <span className="absolute z-[1] text-sm font-extrabold" style={{ left: `calc(${width} + 8px)`, color: 'var(--viz-ink)', whiteSpace: 'nowrap' }}>
          {row.text}
        </span>
      </div>
    </>
  )
}
