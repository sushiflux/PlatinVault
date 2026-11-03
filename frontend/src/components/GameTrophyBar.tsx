import { useEffect, useRef, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { LiveGameAchievementsDto } from '../api/types'
import { useFormat } from '../i18n/format'
import { UI_SCALE_APPLIED_EVENT } from '../utils/uiScale'
import './gamenav/gamenav.css'
import { TrophyTierPills } from './TrophyTierPills'
import { ProgressBar } from './ui/ProgressBar'

interface GameTrophyBarProps {
  // The live trophies of the game (the page's own query); undefined while they load
  data: LiveGameAchievementsDto | undefined
  // False without a Steam link: no trophies to show, only the sections remain
  hasSteam: boolean
  // The section tabs (live trophies, statistics, story): the right side of the bar
  sections: ReactNode
  // Phone only (CSS shows it below 768 px): the button that opens the chapter drawer
  chaptersButton?: ReactNode
}

/**
 * The action bar of the game page, directly under the header: on the left the trophy progress and the four tiers, on the right
 * the section tabs. It is ALWAYS there and sticks under the header on every tab (also the live tab), so it is a real element in
 * the flow - no slide-in, no reserved spacer. Its top edge tucks 1px under the header (the same rule as the dashboard's
 * sub-bar), never a hairline gap, and it sits exactly where it sat in the flow when it starts to stick.
 *
 * It publishes where it ends (--pv-gbar-bottom = header + bar), which is where the sticky chapter nav starts.
 */
export function GameTrophyBar({ data, hasSteam, sections, chaptersButton }: GameTrophyBarProps) {
  const { t } = useTranslation('game')
  const fmt = useFormat()
  const barRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const bar = barRef.current
    const header = document.querySelector<HTMLElement>('.top-header')
    if (!bar) return
    const root = document.documentElement
    const publish = () => {
      const headerHeight = header?.getBoundingClientRect().height ?? 0
      root.style.setProperty('--pv-gbar-bottom', `${headerHeight - 1 + bar.getBoundingClientRect().height}px`)
    }
    publish()
    const observer = new ResizeObserver(publish)
    observer.observe(bar)
    if (header) observer.observe(header)
    window.addEventListener(UI_SCALE_APPLIED_EVENT, publish)
    return () => {
      observer.disconnect()
      window.removeEventListener(UI_SCALE_APPLIED_EVENT, publish)
      root.style.removeProperty('--pv-gbar-bottom')
    }
  }, [])

  return (
    <div ref={barRef} className="pv-gbar no-print" role="region" aria-label={t('trophyBar.region')}>
      <div className="pv-gbar-inner" data-bare={hasSteam ? undefined : ''}>
        {chaptersButton && <div className="pv-gbar-chapters">{chaptersButton}</div>}
        {hasSteam && (
          <>
            <div className="pv-gbar-progress">
              <span className="progress-info">
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
              <ProgressBar percent={data?.progressPercentage ?? 0} label={t('trophyBar.progress')} />
            </div>
            <div className="pv-gbar-tiers">
              <TrophyTierPills data={data} withTooltips />
            </div>
          </>
        )}
        <div className="pv-gbar-sections">{sections}</div>
      </div>
    </div>
  )
}
