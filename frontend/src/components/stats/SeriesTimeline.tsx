import { useEffect, useRef, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { IconBooks, IconCheck, IconDeviceGamepad2, IconStack2 } from '@tabler/icons-react'
import type { GameSeriesDto, GameSeriesGameDto } from '../../api/types'
import { Tooltip } from '../ui/Tooltip'
import { ChartCard } from './chartKit'

// Which relation the player has to a game of the series; the texts are `stats:series.status.<kind>.label|detail`
type StatusKind = 'vault' | 'library' | 'notOnSteam' | 'notOwned'

function statusOf(game: GameSeriesGameDto): { kind: StatusKind; tone: 'vault' | 'library' | 'none' } {
  if (game.inVault) return { kind: 'vault', tone: 'vault' }
  if (game.owned) return { kind: 'library', tone: 'library' }
  if (game.steamAppId === null) return { kind: 'notOnSteam', tone: 'none' }
  return { kind: 'notOwned', tone: 'none' }
}

function Cover({ game }: { game: GameSeriesGameDto }) {
  const [failed, setFailed] = useState(false)
  if (!game.coverUrl || failed) {
    return (
      <div
        className="flex aspect-[3/4] w-full items-center justify-center rounded-[var(--radius-sm)]"
        style={{ background: 'var(--viz-empty)', border: '1px solid var(--border-subtle)', color: 'var(--viz-ink-3)' }}
        aria-hidden="true"
      >
        <IconDeviceGamepad2 size={28} />
      </div>
    )
  }
  return (
    <img
      src={game.coverUrl}
      alt=""
      loading="lazy"
      onError={() => setFailed(true)}
      className="aspect-[3/4] w-full rounded-[var(--radius-sm)] object-cover"
      style={{ border: '1px solid var(--border-subtle)' }}
    />
  )
}

function SeriesTile({ game, last }: { game: GameSeriesGameDto; last: boolean }) {
  const { t } = useTranslation('stats')
  const status = statusOf(game)
  const statusLabel = t(`series.status.${status.kind}.label`)
  const hasTrophies = game.trophiesTotal !== null && game.trophiesTotal > 0 && game.trophiesUnlocked !== null
  const pct = hasTrophies ? Math.min(100, ((game.trophiesUnlocked ?? 0) / (game.trophiesTotal ?? 1)) * 100) : 0
  const body = (
    <div className="flex w-[8.75rem] flex-none flex-col" data-current={game.isCurrent ? 'true' : undefined}>
      {/* rail: the dot sits on a hairline that runs on to the next game */}
      <div className="relative mb-2 flex h-4 items-center gap-2">
        <span
          className="relative z-[1] inline-block h-3 w-3 shrink-0 rounded-full"
          style={{
            background: game.isCurrent ? 'var(--viz-1)' : 'var(--bg-surface)',
            border: `2px solid ${game.isCurrent ? 'var(--viz-1)' : 'var(--border-strong)'}`,
          }}
          aria-hidden="true"
        />
        <span className="relative z-[1] bg-[var(--bg-surface)] pr-1.5 text-xs font-extrabold" style={{ color: game.isCurrent ? 'var(--viz-ink)' : 'var(--viz-ink-2)' }}>
          {game.releaseYear ?? t('series.yearUnknown')}
        </span>
        {!last && <span className="absolute left-4 top-1/2 h-px" style={{ right: '-0.85rem', background: 'var(--border-strong)', opacity: 0.55 }} aria-hidden="true" />}
      </div>
      <div
        className="rounded-[var(--radius-sm)]"
        style={game.isCurrent ? { outline: '2px solid var(--viz-1)', outlineOffset: 2 } : undefined}
      >
        <Cover game={game} />
      </div>
      <div className="mt-2 line-clamp-2 h-[2.5em] overflow-hidden text-sm font-bold leading-tight" style={{ color: 'var(--viz-ink)' }}>
        {game.name}
      </div>
      <div className="mt-1 min-h-[2.7rem]">
        <div className="flex items-center gap-1 text-xs font-semibold" style={{ color: status.tone === 'none' ? 'var(--viz-ink-3)' : 'var(--viz-ink-2)' }}>
          {status.tone === 'vault' && <IconCheck size={13} aria-hidden="true" />}
          {status.tone === 'library' && <IconBooks size={13} aria-hidden="true" />}
          <span>{game.isCurrent ? t('series.thisGame') : statusLabel}</span>
        </div>
        {hasTrophies && (
          <div className="mt-1.5">
            <div className="h-1.5 overflow-hidden rounded-full" style={{ background: 'var(--viz-track)' }}>
              <div className="h-full rounded-full" style={{ width: `${pct}%`, background: 'var(--viz-1)' }} />
            </div>
            <div className="mt-0.5 text-xs" style={{ color: 'var(--viz-ink-3)' }}>
              {t('series.trophies', { unlocked: game.trophiesUnlocked ?? 0, total: game.trophiesTotal ?? 0 })}
            </div>
          </div>
        )}
      </div>
    </div>
  )

  const linked = game.slug && !game.isCurrent
  return (
    <Tooltip label={game.name} description={game.isCurrent ? t('series.thisGameDetail') : t(`series.status.${status.kind}.detail`)} className="flex-none" side="top">
      {linked ? (
        <Link to={`/games/${game.slug}`} className="pv-viz-focusable block" aria-label={t('series.openGame', { name: game.name, status: statusLabel })}>
          {body}
        </Link>
      ) : (
        <div className="pv-viz-focusable" tabIndex={0} aria-label={`${game.name}, ${game.isCurrent ? t('series.thisGame') : statusLabel}`}>
          {body}
        </div>
      )}
    </Tooltip>
  )
}

// B4: the games of the series in release order, with cover, year, ownership and trophy progress. The current game is marked.
export function SeriesTimeline({ series, order }: { series: GameSeriesDto; order?: number }) {
  const { t } = useTranslation('stats')
  const rowRef = useRef<HTMLDivElement>(null)

  // Bring the current game into view when the series is longer than the row
  useEffect(() => {
    const row = rowRef.current
    const current = row?.querySelector<HTMLElement>('[data-current="true"]')
    if (!row || !current) return
    row.scrollLeft = Math.max(0, current.offsetLeft - row.clientWidth / 2 + current.clientWidth / 2)
  }, [series])

  const ownedCount = series.games.filter((g) => g.owned || g.inVault).length
  return (
    <ChartCard
      title={series.source === 'collection' ? t('series.titleCollection', { name: series.name }) : t('series.titleFranchise', { name: series.name })}
      icon={<IconStack2 size={18} />}
      subtitle={
        <Trans t={t} i18nKey="series.subtitle" values={{ games: series.games.length, owned: ownedCount }} components={{ strong: <strong /> }} />
      }
      order={order}
      table={{
        caption: t('series.tableCaption', { name: series.name }),
        columns: [{ label: t('series.colGame') }, { label: t('series.colYear'), numeric: true }, { label: t('series.colStatus') }, { label: t('series.colTrophies'), numeric: true }],
        rows: series.games.map((g) => [
          g.name,
          g.releaseYear ?? '',
          g.isCurrent ? t('series.thisGame') : t(`series.status.${statusOf(g).kind}.label`),
          g.trophiesTotal ? `${g.trophiesUnlocked ?? '?'} / ${g.trophiesTotal}` : '',
        ]),
      }}
    >
      <div ref={rowRef} className="pv-viz-series">
        {series.games.map((game, i) => (
          <SeriesTile key={game.igdbId} game={game} last={i === series.games.length - 1} />
        ))}
      </div>
    </ChartCard>
  )
}
