import type { CSSProperties } from 'react'
import { IconTrophyFilled } from '@tabler/icons-react'
import type { RarityTier } from '../../api/types'

// The tier colours of the small cup (index.css: --tier-*; every one is >= 3:1 on the surfaces and tinted pills of both themes)
export const TIER_ICON_COLORS: Record<RarityTier, string> = {
  Platinum: 'var(--tier-platinum)',
  Gold: 'var(--tier-gold)',
  Silver: 'var(--tier-silver)',
  Bronze: 'var(--tier-bronze)',
}

// Fixed colours for a surface that is dark in both themes (cover overlays, the trophy cabinet): the dark-theme values of the tokens
export const TIER_ICON_COLORS_ON_DARK: Record<RarityTier, string> = {
  Platinum: '#7dd3fc',
  Gold: '#fcd34d',
  Silver: '#cbd5e1',
  Bronze: '#e8905a',
}

interface TierIconProps {
  tier: RarityTier
  // Pixels or any CSS length ("1.15em")
  size?: number | string
  // Names the tier for screen readers; without it the icon is decorative (the text next to it names the tier)
  label?: string
  // The surface behind the icon: 'auto' follows the theme, 'dark' is a surface that is dark in both themes (a cover overlay)
  surface?: 'auto' | 'dark'
  // Take the surrounding text colour instead of the tier colour (e.g. on a chip that is filled with the tier colour)
  inherit?: boolean
  className?: string
  style?: CSSProperties
}

// The one trophy cup of the app for a tier icon: a filled cup (Tabler "trophy-filled", MIT) in the tier's colour - platinum, gold,
// silver and bronze all use the same shape, so they read as one family and a cup is never mistaken for a drinking cup or a crown.
// Platinum carries a small sparkle on top (from 13 px up) so it also stands out without colour vision.
export function TierIcon({ tier, size = 16, label, inherit = false, surface = 'auto', className, style }: TierIconProps) {
  const px = typeof size === 'number' ? size : null
  const sparkle = tier === 'Platinum' && (px === null || px >= 13)
  return (
    <span
      className={`pv-tier-icon ${className ?? ''}`}
      data-tier={tier}
      style={{
        position: 'relative',
        display: 'inline-flex',
        flex: 'none',
        color: inherit ? undefined : (surface === 'dark' ? TIER_ICON_COLORS_ON_DARK : TIER_ICON_COLORS)[tier],
        ...style,
      }}
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
    >
      <IconTrophyFilled size={size} aria-hidden="true" />
      {sparkle && (
        <svg
          viewBox="0 0 8 8"
          aria-hidden="true"
          focusable="false"
          style={{ position: 'absolute', insetBlockStart: '-14%', insetInlineEnd: '-20%', inlineSize: '46%', blockSize: '46%' }}
        >
          <path d="M4 0L4.9 3.1L8 4L4.9 4.9L4 8L3.1 4.9L0 4L3.1 3.1L4 0Z" fill="currentColor" />
        </svg>
      )}
    </span>
  )
}
