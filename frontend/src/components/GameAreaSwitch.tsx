import { useEffect, useRef } from 'react'
import { IconBook2, IconChartBar, IconTrophy } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { fixedT } from '../i18n'
import { useFitSegments } from '../utils/useFitSegments'
import { LIVE_TROPHIES_TAB_SLUG, LORE_TAB_SLUG, STATS_TAB_SLUG } from './shell/navModel'
import { LangStack } from './ui/LangStack'
import type { ReactNode } from 'react'

type Area = 'guide' | 'live' | 'stats'

interface GameAreaSwitchProps {
  gameSlug: string
  // The tab the page shows right now (already resolved against the guide); the URL stays the source of truth
  activeTab: string
  // Slugs of the guide chapters in display order (without Story & Lore)
  chapterSlugs: string[]
  onSelect: (tabSlug: string) => void
  // The name of the trophy area: a Steam game's live trophies, or the trophies / achievements list of a game added by hand
  liveLabel?: 'live' | 'trophies' | 'achievements'
  // The statistics need a Steam link; a game added by hand has none
  showStats?: boolean
  // Where Guide leads for a game without any chapter: Story & Lore (default), or another slug, e.g. the page's "no guide yet" card
  emptyGuideSlug?: string
}

const AREAS: { area: Area; icon: ReactNode }[] = [
  { area: 'guide', icon: <IconBook2 size={18} aria-hidden="true" className="pv-game-area-icon" /> },
  { area: 'live', icon: <IconTrophy size={18} aria-hidden="true" className="pv-game-area-icon" /> },
  { area: 'stats', icon: <IconChartBar size={18} aria-hidden="true" className="pv-game-area-icon" /> },
]

/**
 * The area switch of a game page, one connected thin bar in the game head: Guide | Live trophies | Statistics (a game added by hand: Guide | Trophies, the statistics need a Steam link). Guide is active on every
 * chapter and on Story & Lore, and leads back to the guide tab that was open last for this game (else the first chapter, else Story &
 * Lore). Rendered only for games with a trophy area (a Steam link, or a platform with trophies). Every label is drawn in both languages on top of each
 * other, so the bar never changes its width; when it does not fit the window it becomes the connected vertical list (useFitSegments).
 */
export function GameAreaSwitch({ gameSlug, activeTab, chapterSlugs, onSelect, liveLabel = 'live', showStats = true, emptyGuideSlug = LORE_TAB_SLUG }: GameAreaSwitchProps) {
  const { t } = useTranslation('game')
  const rootRef = useRef<HTMLDivElement>(null)
  useFitSegments(rootRef)

  const current: Area = activeTab === LIVE_TROPHIES_TAB_SLUG ? 'live' : activeTab === STATS_TAB_SLUG ? 'stats' : 'guide'

  // The last guide tab shown for this game in this session (a ref: remembering it must not re-render anything)
  const lastGuideTab = useRef<{ game: string; tab: string } | null>(null)
  useEffect(() => {
    if (current === 'guide' && activeTab) lastGuideTab.current = { game: gameSlug, tab: activeTab }
  }, [current, activeTab, gameSlug])

  const select = (area: Area) => {
    if (area === current) return
    if (area === 'live') return onSelect(LIVE_TROPHIES_TAB_SLUG)
    if (area === 'stats') return onSelect(STATS_TAB_SLUG)
    const remembered = lastGuideTab.current?.game === gameSlug ? lastGuideTab.current.tab : ''
    const stillThere = remembered === LORE_TAB_SLUG || chapterSlugs.includes(remembered)
    onSelect(stillThere ? remembered : (chapterSlugs[0] ?? emptyGuideSlug))
  }

  const label = (area: Area) => <LangStack align="center" render={(language) => fixedT(language, 'game')(`areas.${area === 'live' ? liveLabel : area}`)} />

  return (
    <div ref={rootRef} className="pv-game-areas no-print">
      <div className="pv-segmented" role="group" aria-label={t('areas.group')}>
        {AREAS.filter(({ area }) => showStats || area !== 'stats').map(({ area, icon }) => (
          <button
            key={area}
            type="button"
            aria-pressed={current === area}
            // The coach mark of the live trophies points at its own segment
            data-hint={area === 'live' ? 'game-live' : undefined}
            onClick={() => select(area)}
          >
            {icon}
            {/* Nothing may change size when the area changes: the selected segment is set in weight 600, so every label also holds the
                room of its bold version (an invisible copy stacked under it) and the bar is exactly as wide in every state */}
            <span className="grid justify-items-center">
              <span className="col-start-1 row-start-1">{label(area)}</span>
              <span className="invisible col-start-1 row-start-1 font-semibold" aria-hidden="true">
                {label(area)}
              </span>
            </span>
          </button>
        ))}
      </div>
    </div>
  )
}
