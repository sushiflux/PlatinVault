import { useTranslation } from 'react-i18next'
import type { PlatformId, PlatformVariant } from '../../api/types'
import { platformLabelKey } from '../../utils/platforms'

export interface PlatformBadgeProps {
  platform: PlatformId
  variant?: PlatformVariant | null
  // 'sm' = on the cover of a tile (11 px), 'md' = in a list row / the game head (12 px)
  size: 'sm' | 'md'
}

// The platform of a game as a short text chip (owner's choice from the mockup: variant B): "PS5", "PS4", "Steam", "Xbox", "Epic", "GOG", "Switch",
// "Ubisoft", "EA", "Battle.net". The short text is for the eye; screen readers get the full name ("PlayStation 5"). The look is in styles/dashboard.css
// (.pv-platform-badge), the classic counterpart in styles/classic/dashboard.css. A chip never changes the size of what it sits in: on a cover it is
// absolutely positioned (.pv-platform-badge-pos), in a row it sits in a line that is always there.
export function PlatformBadge({ platform, variant, size }: PlatformBadgeProps) {
  const { t } = useTranslation('common')
  const key = platformLabelKey(platform, variant)
  return (
    <span className="pv-platform-badge" data-platform={platform} data-variant={variant ?? undefined} data-size={size}>
      <span aria-hidden="true">{t(`platformChips.${key}`)}</span>
      <span className="sr-only">{t(`platforms.${key}`)}</span>
    </span>
  )
}
