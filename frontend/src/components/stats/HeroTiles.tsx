import type { ReactNode } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { IconClock, IconHourglassLow, IconInfoCircle, IconTrophy } from '@tabler/icons-react'
import type { PlayerGameStatsDto } from '../../api/types'
import { ProgressBar } from '../ui/ProgressBar'
import { TierIcon } from '../ui/TierIcon'
import { Tooltip } from '../ui/Tooltip'
import { Reveal, useStatsFormat, type StatsFormat } from './chartKit'
import type { ExclusivityBound, PlatinumEstimate } from './statsData'

function Tile({
  icon,
  label,
  info,
  hero = false,
  order,
  children,
  hint,
}: {
  icon: ReactNode
  label: string
  // Explanation shown in our own tooltip behind a small info button
  info?: { title: string; text: string }
  hero?: boolean
  order: number
  children: ReactNode
  hint?: ReactNode
}) {
  return (
    <Reveal order={order} className="min-w-0">
      <section className={`pv-card pv-viz-tile h-full ${hero ? 'pv-viz-tile--hero' : ''}`} aria-label={label}>
        <div className="pv-viz-tile-label">
          <span className="pv-viz-tile-chip" aria-hidden="true">
            {icon}
          </span>
          <span>{label}</span>
          {info && (
            <span className="ml-auto">
              <Tooltip label={info.title} description={info.text} side="bottom">
                <button
                  type="button"
                  aria-label={`${info.title}: ${info.text}`}
                  className="inline-flex h-7 w-7 items-center justify-center rounded-[var(--radius-sm)] hover:bg-[var(--bg-elevated)]"
                  style={{ color: 'var(--viz-ink-3)' }}
                >
                  <IconInfoCircle size={17} />
                </button>
              </Tooltip>
            </span>
          )}
        </div>
        {children}
        {hint && <div className="pv-viz-tile-hint">{hint}</div>}
      </section>
    </Reveal>
  )
}

// Balanced rows for every number of visible tiles (container queries, so the sidebar layout counts too):
// 4 tiles = one row of 4 or 2 x 2 (never 3 + 1), 3 = three columns, 2 = two columns, 1 = full width; narrow = one column.
const HERO_GRID: Record<number, string> = {
  1: 'grid-cols-1',
  2: 'grid-cols-1 @[30rem]:grid-cols-2',
  3: 'grid-cols-1 @[44rem]:grid-cols-3',
  4: 'grid-cols-1 @[30rem]:grid-cols-2 @[60rem]:grid-cols-4',
}

export function heroGridClass(count: number): string {
  return `grid gap-4 ${HERO_GRID[Math.min(4, Math.max(1, count))]}`
}

interface Props {
  unlocked: number | null
  total: number | null
  platinumDone: boolean
  bound: ExclusivityBound | null
  projection: PlatinumEstimate | null
  // The inputs of the estimate (IGDB, recorded history) are still loading: a tile of the final size holds the place
  projectionPending: boolean
  player: PlayerGameStatsDto | null
}

// What the estimate rests on, in plain words (always shown: no estimate without its basis)
function estimateBasis(e: PlatinumEstimate, fmt: StatsFormat, t: TFunction<'stats'>): { title: string; detail: string; info: string } {
  const open = t('hero.projection.openTrophies', { count: e.remainingCount })
  const paceFor = (rate: number): string => {
    // The rounded value picks the plural form, so "1" reads "1 trophy" and "1.5" reads "1.5 trophies"
    const shown = Math.round(rate * 10) / 10
    return t('hero.projection.pace', { count: shown, rate: fmt.dec(rate) })
  }
  if (e.basis === 'igdb') {
    const values = { igdb: fmt.hours(e.igdbHours ?? 0), own: fmt.hours(e.ownHours ?? 0) }
    return {
      title: t('hero.projection.igdb.title'),
      detail: t('hero.projection.igdb.detail', values),
      info: t('hero.projection.igdb.info', values),
    }
  }
  const detail = t('hero.projection.unlocksIn', { count: e.basisUnlocks ?? 0, time: fmt.hours(e.basisHours ?? 0) })
  if (e.basis === 'recent') {
    const title =
      e.windowDays !== null && e.windowDays >= 28
        ? t('hero.projection.recent.title4Weeks')
        : t('hero.projection.recent.titleDays', { count: e.windowDays ?? 0 })
    return { title, detail, info: t('hero.projection.recent.info', { basis: title, pace: paceFor(e.ratePerHour ?? 0), open }) }
  }
  return {
    title: t('hero.projection.overall.title'),
    detail,
    info: t('hero.projection.overall.info', { pace: paceFor(e.ratePerHour ?? 0), open }),
  }
}

// The row of figures on top: progress, how exclusive platinum is (the one hero figure), time to platinum, playtime.
export function HeroTiles({ unlocked, total, platinumDone, bound, projection, projectionPending, player }: Props) {
  const { t } = useTranslation('stats')
  const fmt = useStatsFormat()
  const strong = <strong style={{ color: 'var(--viz-ink)' }} />
  const tiles: ReactNode[] = []
  let order = 0

  if (unlocked !== null && total !== null && total > 0) {
    const percent = (unlocked / total) * 100
    const left = total - unlocked
    const shownPercent = fmt.pct(Math.round(percent * 10) / 10)
    tiles.push(
      <Tile
        key="progress"
        icon={<IconTrophy size={16} />}
        label={t('hero.progress.label')}
        order={order++}
        hint={left === 0 ? t('hero.progress.hintDone', { percent: shownPercent }) : t('hero.progress.hintOpen', { percent: shownPercent, open: fmt.int(left) })}
      >
        <div className="pv-viz-tile-value">
          {fmt.int(unlocked)} <span style={{ color: 'var(--viz-ink-3)', fontWeight: 700 }}>/ {fmt.int(total)}</span>
        </div>
        <ProgressBar percent={percent} label={t('hero.progress.barLabel')} className="mt-3" />
      </Tile>
    )
  }

  if (bound) {
    const partial = bound.known < bound.total
    const infoValues = { percent: fmt.pct(bound.percent), name: bound.name, known: fmt.int(bound.known), total: fmt.int(bound.total) }
    tiles.push(
      <Tile
        key="exclusive"
        hero
        icon={<TierIcon tier="Platinum" size={16} />}
        label={t('hero.exclusive.label')}
        info={{
          title: t('hero.exclusive.infoTitle'),
          text: partial ? t('hero.exclusive.infoPartial', infoValues) : t('hero.exclusive.info', infoValues),
        }}
        order={order++}
        hint={
          <>
            <Trans t={t} i18nKey={partial ? 'hero.exclusive.hintPartial' : 'hero.exclusive.hint'} values={{ name: bound.name }} components={{ strong }} />
            {partial && ` ${t('hero.exclusive.missingShare', { missing: fmt.int(bound.total - bound.known), total: fmt.int(bound.total) })}`}
            {platinumDone && ` ${t('hero.exclusive.youBelong')}`}
          </>
        }
      >
        <div className="mt-2 text-sm font-extrabold" style={{ color: 'var(--viz-ink-2)' }}>
          {t('hero.exclusive.atMost')}
        </div>
        <div className="pv-viz-hero-figure">{fmt.pct(bound.percent)}</div>
      </Tile>
    )
  }

  if (platinumDone) {
    const first = player?.firstUnlock ? Date.parse(player.firstUnlock) : Number.NaN
    const last = player?.lastUnlock ? Date.parse(player.lastUnlock) : Number.NaN
    const days = !Number.isNaN(first) && !Number.isNaN(last) ? Math.max(1, Math.round((last - first) / 86_400_000)) : null
    if (player?.playtimeHours != null || days !== null) {
      tiles.push(
        <Tile
          key="done"
          icon={<TierIcon tier="Platinum" size={16} />}
          label={t('hero.done.label')}
          order={order++}
          hint={days !== null ? t('hero.done.hint', { count: days, from: fmt.dateLong(first), to: fmt.dateLong(last) }) : undefined}
        >
          <div className="pv-viz-tile-value">
            {player?.playtimeHours != null ? t('hero.done.after', { time: fmt.hours(player.playtimeHours) }) : t('hero.done.inDays', { count: days ?? 0 })}
          </div>
        </Tile>
      )
    }
  } else if (projectionPending) {
    tiles.push(
      <Tile key="projection" icon={<IconHourglassLow size={16} />} label={t('hero.projection.label')} order={order++}>
        <div className="pv-viz-skel animate-pulse mt-3" style={{ blockSize: '2rem', inlineSize: '55%', opacity: 0.5 }} aria-hidden="true" />
      </Tile>
    )
  } else if (projection) {
    const basis = estimateBasis(projection, fmt, t)
    tiles.push(
      <Tile
        key="projection"
        icon={<IconHourglassLow size={16} />}
        label={t('hero.projection.label')}
        info={{ title: t('hero.projection.infoTitle'), text: basis.info }}
        order={order++}
        hint={<Trans t={t} i18nKey="hero.projection.hint" values={{ title: basis.title, detail: basis.detail }} components={{ strong }} />}
      >
        <div className="pv-viz-tile-value">{t('hero.projection.value', { time: fmt.hours(projection.shownHours) })}</div>
      </Tile>
    )
  }

  // 0 hours is "not played / not known yet", not a figure worth a tile
  if (player?.playtimeHours != null && player.playtimeHours > 0) {
    const last = player.lastPlayed ? Date.parse(player.lastPlayed) : Number.NaN
    tiles.push(
      <Tile
        key="playtime"
        icon={<IconClock size={16} />}
        label={t('hero.playtime.label')}
        order={order++}
        hint={
          <>
            {player.playtime2WeeksHours ? <div>{t('hero.playtime.recent', { time: fmt.hours(player.playtime2WeeksHours) })}</div> : null}
            {!Number.isNaN(last) ? <div>{t('hero.playtime.lastPlayed', { date: fmt.dateLong(last) })}</div> : null}
          </>
        }
      >
        <div className="pv-viz-tile-value">{fmt.hours(player.playtimeHours)}</div>
      </Tile>
    )
  }

  if (tiles.length === 0) return null
  return <div className={heroGridClass(tiles.length)}>{tiles}</div>
}
