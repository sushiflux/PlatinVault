import { useMemo, useState, type ReactNode } from 'react'
import { CopyErrorButton } from './ui/CopyErrorButton'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { IconRefresh } from '@tabler/icons-react'
import { fetchGameHistory, fetchGameIgdb, fetchGameSeries, fetchGameStats, fetchLiveAchievements } from '../api/client'
import { ChartSkeleton, useStatsFormat } from './stats/chartKit'
import { FactsCard, NewsCard } from './stats/GameFacts'
import { HeroTiles, heroGridClass } from './stats/HeroTiles'
import { PlayerCountChart, PlaytimeCalendar } from './stats/HistoryCharts'
import { RANGE_CHOICES } from './stats/historyData'
import { PlaytimeCompare, RatingCompare } from './stats/IgdbCharts'
import { EasiestOpen, RarityBands, TierMeters } from './stats/RarityCharts'
import { ReviewsCard } from './stats/ReviewsCard'
import { SeriesTimeline } from './stats/SeriesTimeline'
import { easiestOpen, estimateToPlatinum, exclusivityBound, hasRarityData, unlocksWithTime } from './stats/statsData'
import { TrophyTimeline } from './stats/TrophyTimeline'
import { UnlockHeatmap } from './stats/UnlockHeatmap'
import './stats/stats.css'

// Two-column rows from a container width of 56rem on (the page has a sidebar in portrait, so the container decides, not the screen)
const GRID_2 = 'grid grid-cols-1 gap-4 @[56rem]:grid-cols-2'
const GRID_3 = 'grid grid-cols-1 gap-4 @[56rem]:grid-cols-3'

function Section({ title, show = true, children }: { title: string; show?: boolean; children: ReactNode }) {
  if (!show) return null
  return (
    <section className="space-y-3" aria-label={title}>
      <h2 className="pv-viz-section">{title}</h2>
      {children}
    </section>
  )
}

// Statistics tab of a game page: the player's own numbers as charts (progress, rarity, time), what the community does
// (reviews, player counts, recorded history) and, with the player's own Twitch keys, IGDB comparisons and the series.
// Every block shows only what is known; the rest closes up.
export function GameStatsPanel({ slug, steamAppId }: { slug: string; steamAppId?: number | null }) {
  const { t } = useTranslation(['stats', 'common'])
  const fmt = useStatsFormat()
  const errorText = (error: unknown): string => (error instanceof Error ? error.message : t('common:unknownError'))
  const [range, setRange] = useState<number>(30)

  const stats = useQuery({ queryKey: ['game-stats', slug], queryFn: () => fetchGameStats(slug), staleTime: 60_000 })
  const appId = steamAppId ?? stats.data?.appId ?? null
  // Same key as the game page's header query, so this is normally a cache hit
  const live = useQuery({
    queryKey: ['live-achievements', appId ?? 'auto'],
    queryFn: () => fetchLiveAchievements(appId ?? undefined),
    enabled: appId !== null,
    staleTime: 15_000,
  })
  const igdb = useQuery({ queryKey: ['game-igdb', slug], queryFn: () => fetchGameIgdb(slug), staleTime: 300_000, retry: false })
  const series = useQuery({ queryKey: ['game-series', slug], queryFn: () => fetchGameSeries(slug), staleTime: 300_000, retry: false })
  // One request with the longest range; the range switch only slices it, so switching never reloads
  const history = useQuery({
    queryKey: ['game-history', slug],
    queryFn: () => fetchGameHistory(slug, 365),
    staleTime: 60_000,
    refetchInterval: 300_000,
    placeholderData: keepPreviousData,
  })

  const items = useMemo(() => live.data?.achievements ?? [], [live.data])
  const unlocks = useMemo(() => unlocksWithTime(items), [items])
  const rarityOk = useMemo(() => hasRarityData(items), [items])
  const data = stats.data
  const player = data?.player ?? null

  if (stats.isLoading || (appId !== null && live.isLoading)) {
    return <StatsSkeleton />
  }

  if (stats.error || !data) {
    return (
      <div className="pv-alert pv-alert-danger flex-wrap items-center justify-between gap-3">
        <span className="text-sm font-semibold">{t('panel.loadError', { reason: errorText(stats.error) })}</span>
        <span className="inline-flex items-center gap-3">
          <CopyErrorButton report={{ message: t('panel.loadError', { reason: errorText(stats.error) }) }} />
          <button type="button" className="pv-btn" onClick={() => void stats.refetch()}>
            <IconRefresh size={15} />
            <span>{t('common:retry')}</span>
          </button>
        </span>
      </div>
    )
  }

  const liveData = live.data
  const total = liveData ? liveData.totalCount : player && player.achievementsTotal > 0 ? player.achievementsTotal : null
  const unlocked = liveData ? liveData.achievedCount : player && player.achievementsTotal > 0 ? player.achievementsUnlocked : null
  const platinumDone = liveData?.is100Percent ?? (total !== null && unlocked === total)
  const bound = rarityOk ? exclusivityBound(items) : null
  // Time to platinum: IGDB's 100 % time, else the pace of the last 4 weeks (recorded history), else the overall pace.
  // While IGDB or the history still load, a placeholder tile holds the place instead of a number that would change.
  const estimateWanted = !platinumDone && total !== null && unlocked !== null && total > unlocked
  const estimatePending = estimateWanted && (igdb.isLoading || history.isLoading)
  const estimate =
    estimateWanted && !estimatePending && total !== null && unlocked !== null
      ? estimateToPlatinum({
          unlocked,
          total,
          playtimeHours: player?.playtimeHours ?? null,
          igdbCompletelyHours: igdb.data?.timeToBeat?.completelyHours ?? null,
          unlocks,
          playtimeDaily: history.data?.playtimeDaily ?? null,
          now: Date.now(),
        })
      : null
  const hasPersonal = Boolean(liveData) || Boolean(player)
  const showProgress = (unlocks.length >= 2 && total !== null) || Boolean(liveData && liveData.totalCount > 0)
  const showTime = unlocks.length >= 3 || history.isLoading || Boolean(history.data) || igdb.isLoading || Boolean(igdb.data?.timeToBeat)
  const showCommunity = Boolean(history.data) || (data.reviewTotal ?? 0) > 0 || igdb.isLoading || Boolean(igdb.data)
  const showFacts = data.appId > 0 || data.releaseDate !== null || data.developers.length > 0 || data.genres.length > 0 || data.news.length > 0 || data.currentPlayers !== null
  const hasTimeline = unlocks.length >= 2
  const hasHeatmap = unlocks.length >= 3
  const hasOpen = easiestOpen(items, fmt.locale).length > 0

  const refetchAll = () => {
    void stats.refetch()
    void live.refetch()
    void igdb.refetch()
    void series.refetch()
    void history.refetch()
  }
  const refreshing = stats.isFetching || live.isFetching || igdb.isFetching || series.isFetching || history.isFetching

  let order = 4
  const next = () => order++

  return (
    <div className="pv-stats space-y-8">
      {!hasPersonal && (
        <p className="pv-card text-sm" style={{ color: 'var(--text-secondary)' }}>
          {live.error ? t('panel.noPersonalDataWithError', { reason: errorText(live.error) }) : t('panel.noPersonalData')}
          {live.error && <CopyErrorButton className="ms-2 align-middle" report={{ message: t('panel.noPersonalDataWithError', { reason: errorText(live.error) }) }} />}
        </p>
      )}

      <HeroTiles unlocked={unlocked} total={total} platinumDone={platinumDone} bound={bound} projection={estimate} projectionPending={estimatePending} player={player} />

      <Section title={t('panel.section.progress')} show={showProgress}>
        <div className={GRID_3}>
          {hasTimeline && total !== null && (
            <div className="@[56rem]:col-span-2">
              <TrophyTimeline unlocks={unlocks} total={total} order={next()} />
            </div>
          )}
          {liveData && liveData.totalCount > 0 && (
            <div className={hasTimeline ? '' : '@[56rem]:col-span-3'}>
              <TierMeters data={liveData} order={next()} />
            </div>
          )}
        </div>
      </Section>

      <Section title={t('panel.section.rarity')} show={rarityOk}>
        <div className={GRID_2}>
          <div className={hasOpen ? '' : '@[56rem]:col-span-2'}>
            <RarityBands
              items={items}
              rarestUnlockedName={player?.rarestUnlockedName ?? null}
              rarestUnlockedPercent={player?.rarestUnlockedPercent ?? null}
              order={next()}
            />
          </div>
          {hasOpen && <EasiestOpen items={items} order={next()} />}
        </div>
      </Section>

      <Section title={t('panel.section.time')} show={showTime}>
        <div className={GRID_2}>
          {hasHeatmap && <UnlockHeatmap unlocks={unlocks} order={next()} />}
          {history.isLoading ? (
            <div className={hasHeatmap ? '' : '@[56rem]:col-span-2'}>
              <ChartSkeleton height={372} narrowHeight={537} label={t('panel.skeleton.calendar')} />
            </div>
          ) : history.data ? (
            <div className={hasHeatmap ? '' : '@[56rem]:col-span-2'}>
              <PlaytimeCalendar
                history={history.data}
                unlocks={unlocks}
                totalHours={player?.playtimeHours ?? null}
                recentHours={player?.playtime2WeeksHours ?? null}
                order={next()}
              />
            </div>
          ) : null}
          {igdb.isLoading ? (
            <div className="@[56rem]:col-span-2">
              <ChartSkeleton height={247} narrowHeight={442} label={t('panel.skeleton.playtime')} />
            </div>
          ) : igdb.data?.timeToBeat ? (
            <div className="@[56rem]:col-span-2">
              <PlaytimeCompare igdb={igdb.data} ownHours={player?.playtimeHours ?? null} order={next()} />
            </div>
          ) : null}
        </div>
        {history.error && !history.data && (
          <p className="pv-alert pv-alert-danger text-sm font-semibold" role="alert">
            {t('panel.historyError', { reason: errorText(history.error) })}
            <CopyErrorButton className="ms-2 align-middle" report={{ message: t('panel.historyError', { reason: errorText(history.error) }) }} />
          </p>
        )}
      </Section>

      <Section title={t('panel.section.community')} show={showCommunity}>
        {(history.isLoading || history.data) && (
          <div className="space-y-3">
            <div className="flex flex-wrap items-center justify-end gap-3">
              <span className="text-xs font-bold" style={{ color: 'var(--text-muted)' }}>
                {t('panel.playerRange')}
              </span>
              <div className="pv-segmented" role="group" aria-label={t('panel.playerRange')}>
                {RANGE_CHOICES.map((choice) => (
                  <button key={choice.days} type="button" aria-pressed={range === choice.days} onClick={() => setRange(choice.days)}>
                    {t(`range.d${choice.days}`)}
                  </button>
                ))}
              </div>
            </div>
            {history.data ? (
              <PlayerCountChart history={history.data} range={range} currentPlayers={data.currentPlayers} order={next()} />
            ) : (
              <ChartSkeleton height={330} narrowHeight={366} label={t('panel.skeleton.players')} />
            )}
          </div>
        )}
        <div className={GRID_2}>
          {(data.reviewTotal ?? 0) > 0 && (
            <div className={igdb.isLoading || igdb.data ? '' : '@[56rem]:col-span-2'}>
              <ReviewsCard stats={data} order={next()} />
            </div>
          )}
          {igdb.isLoading ? (
            <ChartSkeleton height={240} narrowHeight={436} label={t('panel.skeleton.ratings')} />
          ) : igdb.data ? (
            <RatingCompare igdb={igdb.data} stats={data} order={next()} />
          ) : null}
        </div>
      </Section>

      {series.isLoading ? (
        <Section title={t('panel.section.series')}>
          <ChartSkeleton height={347} narrowHeight={366} label={t('panel.skeleton.series')} />
        </Section>
      ) : series.data && series.data.games.length >= 2 ? (
        <Section title={t('panel.section.series')}>
          <SeriesTimeline series={series.data} order={next()} />
        </Section>
      ) : null}

      <Section title={t('panel.section.about')} show={showFacts}>
        <div className={data.news.length > 0 ? GRID_2 : 'grid grid-cols-1 gap-4'}>
          <FactsCard stats={data} order={next()} />
          <NewsCard stats={data} order={next()} />
        </div>
      </Section>

      <div className="flex justify-end">
        <button type="button" className="pv-btn" onClick={refetchAll} disabled={refreshing}>
          <IconRefresh size={15} className={refreshing ? 'animate-spin' : ''} />
          <span>{t('panel.refresh')}</span>
        </button>
      </div>
    </div>
  )
}

// The page at its final structure while the first data loads, so nothing jumps when it arrives
function StatsSkeleton() {
  const { t } = useTranslation('stats')
  return (
    <div className="pv-stats space-y-8" aria-busy="true" aria-label={t('panel.loading')}>
      <div className={heroGridClass(4)}>
        {[0, 1, 2, 3].map((i) => (
          <div key={i} className="pv-card animate-pulse" style={{ minBlockSize: '9.5rem' }}>
            <div className="pv-viz-skel" style={{ blockSize: '0.9rem', inlineSize: '55%', opacity: 0.7 }} />
            <div className="pv-viz-skel mt-4" style={{ blockSize: '2rem', inlineSize: '40%', opacity: 0.5 }} />
          </div>
        ))}
      </div>
      <div className={GRID_3}>
        <div className="@[56rem]:col-span-2">
          <ChartSkeleton height={244} />
        </div>
        <ChartSkeleton height={244} />
      </div>
    </div>
  )
}

// Also used by the onboarding tour's mock statistics
export function StatTile({
  icon,
  label,
  value,
  hint,
  href,
}: {
  icon?: ReactNode
  label: string
  value: string
  hint?: string
  href?: string
}) {
  const body = (
    <>
      <div className="flex items-center gap-2 text-xs font-bold" style={{ color: 'var(--text-muted)' }}>
        {icon && <span style={{ color: 'var(--accent-crystal)' }}>{icon}</span>}
        <span>{label}</span>
      </div>
      <div className="mt-1.5 text-lg font-extrabold leading-snug" style={{ color: 'var(--text-primary)' }}>
        {value}
      </div>
      {hint && (
        <div className="mt-0.5 text-xs line-clamp-2" style={{ color: 'var(--text-secondary)' }}>
          {hint}
        </div>
      )}
    </>
  )
  return href ? (
    <a href={href} target="_blank" rel="noreferrer" className="pv-card block hover:underline">
      {body}
    </a>
  ) : (
    <div className="pv-card">{body}</div>
  )
}
