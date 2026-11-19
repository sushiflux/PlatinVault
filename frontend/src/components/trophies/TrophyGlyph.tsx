import type { CSSProperties } from 'react'
import type { LiveAchievementItemDto, RarityTier } from '../../api/types'
import { TierIcon } from '../ui/TierIcon'
import '../../styles/live.css'

// The picture of a trophy: the achievements of the Steam list have an icon of their own, the trophies of a game added by hand have none
// (iconUrl = ""). Wherever an icon is drawn - the list views, the popups, the "Im Blick" cards - an empty address means this glyph: a calm
// square panel with the tier cup in the middle. It has the size and the frame of the icon it replaces, so nothing around it changes.

/** The address of the icon that applies to the state of the trophy (the grey one while locked); "" = none, draw the glyph. */
export function trophyIconSrc(ach: Pick<LiveAchievementItemDto, 'achieved' | 'iconUrl' | 'iconGrayUrl'>): string {
  return (ach.achieved ? ach.iconUrl : ach.iconGrayUrl || ach.iconUrl) || ''
}

interface TrophyGlyphProps {
  tier: RarityTier
  // Pixels of the square; leave it out when the surrounding class gives the size (e.g. .pv-popup-icon)
  size?: number | string
  // A locked trophy is drawn grey like the grey Steam icon
  achieved?: boolean
  // 'dark' = a surface that is dark in both themes (the full-screen popup)
  surface?: 'auto' | 'dark'
  className?: string
  style?: CSSProperties
}

export function TrophyGlyph({ tier, size, achieved = true, surface = 'auto', className = '', style }: TrophyGlyphProps) {
  return (
    <span
      className={`pv-trophy-glyph ${className}`.trim()}
      data-tier={tier}
      data-locked={achieved ? undefined : ''}
      data-surface={surface === 'dark' ? 'dark' : undefined}
      style={{ ...(size === undefined ? null : { inlineSize: size, blockSize: size }), ...style }}
      aria-hidden="true"
    >
      <TierIcon tier={tier} size={24} surface={surface} />
    </span>
  )
}
