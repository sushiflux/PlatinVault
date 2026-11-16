import { useTranslation } from 'react-i18next'
import { IconCheck } from '@tabler/icons-react'
import type { LiveGameAchievementsDto } from '../api/types'
import { useFormat } from '../i18n/format'
import { useSteamReach } from '../utils/useSteamReach'
import { TierIcon } from './ui/TierIcon'
import { Tooltip } from './ui/Tooltip'
import '../styles/gameframe.css'

/**
 * The status line under the name in the game head: only facts the player cares about, each one only while it is true ("Live aktiv" while
 * the game runs, 100 % platinum, trophies ticked in the guide, the playtime). The line keeps its height when nothing applies (rigid
 * layout). The AppID is deliberately not shown here.
 */
export function GameHeadStatus({ data }: { data: LiveGameAchievementsDto | undefined }) {
  const { t } = useTranslation('live')
  const fmt = useFormat()
  // "Running now" is only known through the bridge or a local Steam: without one of them the indicator is not shown
  const { canLaunch } = useSteamReach()
  // Written out here (not taken from the server's text) so it follows the language: "35 min" / "12.5 h"
  const minutes = data?.game.playtimeForeverMinutes ?? 0
  const playtime = minutes <= 0 ? '' : minutes < 60 ? t('tracker.minutes', { n: fmt.number(minutes) }) : t('tracker.hours', { n: fmt.number(minutes / 60, 1) })
  return (
    <div className="pv-gstatus">
      {canLaunch && data?.game.isRunning && (
        <Tooltip label={t('tracker.running.label')} description={t('tracker.running.description')}>
          <span className="pv-gstatus-live">
            <span className="pv-live-dot" aria-hidden="true" />
            {t('tracker.running.label')}
          </span>
        </Tooltip>
      )}
      {data?.is100Percent && (
        <span className="pv-gstatus-plat">
          <TierIcon tier="Platinum" size={15} /> {t('tracker.platinum')}
        </span>
      )}
      {data && data.syncedGuideItemsCount > 0 && (
        <span className="inline-flex items-center gap-1">
          <IconCheck size={15} aria-hidden="true" /> {t('tracker.syncedGuide', { n: fmt.number(data.syncedGuideItemsCount) })}
        </span>
      )}
      {playtime && <span>{t('tracker.playtime', { time: playtime })}</span>}
    </div>
  )
}
