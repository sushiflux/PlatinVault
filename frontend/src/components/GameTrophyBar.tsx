import { IconRefresh } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { LiveGameAchievementsDto } from '../api/types'
import { useFormat } from '../i18n/format'
import '../styles/gameframe.css'
import { RowIconButton } from './pins/RowIconButton'
import { TrophyTierPills } from './TrophyTierPills'

interface GameTrophyBarProps {
  // The live trophies of the game (the page's own query); undefined while they load
  data: LiveGameAchievementsDto | undefined
  // False without a Steam link: there are no trophies to show, the bar is left out
  hasSteam: boolean
  // Fetches the trophies from Steam again; the button is left out when the page gives none
  onRefresh?: () => void
  // The trophies are being fetched right now (the icon turns)
  refreshing?: boolean
}

/**
 * The trophy part of the game head, under the name: the trophy progress as one sentence over a thin line, and the four tiers as figures.
 * Shown on every tab of the game. The areas (live trophies, statistics, story) and the chapters are entries of the shell's tree
 * navigation, so the bar carries nothing else than the refresh action of the Steam data.
 */
export function GameTrophyBar({ data, hasSteam, onRefresh, refreshing = false }: GameTrophyBarProps) {
  const { t } = useTranslation('game')
  const { t: tl } = useTranslation('live')
  const fmt = useFormat()
  if (!hasSteam) return null
  const percent = Math.max(0, Math.min(100, data?.progressPercentage ?? 0))

  return (
    <div className="pv-gbar no-print" role="region" aria-label={t('trophyBar.region')}>
      <div className="pv-gbar-top">
        {/* One text line high from the first frame, so the figures arriving (skeleton -> text) never change the line's height */}
        <span className="pv-gbar-sum progress-info">
          {data ? (
            t('trophyBar.summary', {
              achieved: fmt.number(data.achievedCount),
              total: fmt.number(data.totalCount),
              percent: fmt.percent(data.progressPercentage, 1),
            })
          ) : (
            <span className="pv-skel animate-pulse" aria-hidden="true" />
          )}
        </span>
        {onRefresh && (
          <RowIconButton label={tl('tracker.refresh')} onClick={onRefresh} size="lg" className="pv-gbar-refresh inline-flex">
            <IconRefresh size={20} className={refreshing ? 'motion-safe:animate-spin' : ''} aria-hidden="true" />
          </RowIconButton>
        )}
      </div>
      <div
        className="pv-gbar-track"
        role="progressbar"
        aria-label={t('trophyBar.progress')}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(percent)}
        data-complete={percent >= 100 ? '' : undefined}
      >
        <i style={{ inlineSize: `${percent}%` }} />
      </div>
      <div className="pv-tiers">
        <TrophyTierPills data={data} withTooltips />
      </div>
    </div>
  )
}
