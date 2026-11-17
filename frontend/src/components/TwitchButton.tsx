import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { IconBrandTwitch } from '@tabler/icons-react'
import { fetchSettings } from '../api/client'
import { useFormat } from '../i18n/format'
import { twitchGameName, twitchGameUrl } from '../utils/twitch'
import type { LiveCount } from '../utils/useTwitchLiveCounts'
import { iconButtonClass } from './ui/IconButton'
import { Tooltip } from './ui/Tooltip'

interface TwitchButtonProps {
  // Game title as Steam / the vault has it; cleaned up for Twitch here
  name: string
  size?: number
  // Replaces the default icon-button look (e.g. the small round overlay buttons on a card)
  className?: string
  style?: React.CSSProperties
  // Class of the tooltip wrapper (a card's action bar lays its segments out through it)
  wrapperClassName?: string
  // A visible word after the logo (shown by the card's action bar where the segment is wide enough)
  labelText?: string
  // How many streams are live for this game right now (only known with Twitch credentials): shown inline after the label: a small live dot and the number. Unknown = nothing.
  liveCount?: LiveCount
  // Ref callback of the link itself (a card's action bar reports it to the live-count collector: its count is fetched while it is on screen)
  linkRef?: (element: HTMLAnchorElement | null) => (() => void) | undefined
}

// "Who streams this game live?": opens Twitch in a new tab - no key, no account, PlatinVault sends nothing to Twitch.
// Shows the Twitch logo in Twitch purple so the destination is obvious (user decision 2026-10-31).
// Hidden while the switch in Settings > Twitch & IGDB is off.
export function TwitchButton({ name, size = 18, className, style, wrapperClassName, labelText, liveCount, linkRef }: TwitchButtonProps) {
  const { t } = useTranslation('game')
  const fmt = useFormat()
  const { data: settings } = useQuery({ queryKey: ['settings'], queryFn: fetchSettings, staleTime: 60_000 })
  if (settings?.twitchStreamsEnabled === false) return null

  const url = twitchGameUrl(name)
  const label = t('twitch.label')
  const gameName = twitchGameName(name)
  const description = url.includes('/search?')
    ? t('twitch.searchDescription', { name: gameName })
    : t('twitch.gameDescription', { name: gameName })
  // The live count in words, for the tooltip and for screen readers: "12 Streams live", "Mehr als 300 Streams live" or that nobody streams it
  const liveText = !liveCount
    ? ''
    : liveCount.count === 0
      ? t('twitch.liveNone')
      : liveCount.atLeast
        ? t('twitch.liveMany', { count: liveCount.count })
        : t('twitch.live', { count: liveCount.count })
  // With Twitch credentials the segment is marked live (.pv-bar-seg--live): its logo stays exactly where the logos of the other segments
  // are, and the count (.pv-bar-count) is laid beside it, out of the flow - so nothing shifts when a count arrives, changes or goes away
  const inBar = (className ?? '').includes('pv-bar-seg')
  const reserve = Boolean(settings?.hasTwitchKey) && inBar
  const badge = liveCount && liveCount.count > 0 ? `${fmt.number(liveCount.count)}${liveCount.atLeast ? '+' : ''}` : null

  return (
    <Tooltip label={label} description={liveText ? `${liveText}. ${description}` : description} className={wrapperClassName}>
      <a
        ref={linkRef}
        href={url}
        target="_blank"
        rel="noreferrer noopener"
        aria-label={`${label}: ${gameName}${liveCount && liveCount.count > 0 ? `, ${liveText}` : ''}`}
        className={`${className ?? iconButtonClass()}${inBar ? ' pv-bar-seg--twitch' : ''}${reserve ? ' pv-bar-seg--live' : ''}`}
        style={style}
        onClick={(e) => e.stopPropagation()}
      >
        {/* In a card's action bar the logo is neutral until the segment is hovered or focused (CSS); everywhere else it wears the Twitch violet */}
        <IconBrandTwitch size={size} className={inBar ? 'pv-bar-seg-icon' : undefined} style={inBar ? undefined : { color: 'var(--brand-twitch)' }} />
        {labelText && <span className="pv-bar-label">{labelText}</span>}
        {/* The count: the live dot in Twitch violet and the number, laid beside the logo without taking room in the line (CSS); nothing
            for 0 streams - the tooltip says that nobody is streaming it */}
        {inBar && badge && (
          <span className="pv-bar-count" aria-hidden="true">
            <span className="pv-bar-live-dot" />
            <span className="pv-bar-num">{badge}</span>
          </span>
        )}
      </a>
    </Tooltip>
  )
}
