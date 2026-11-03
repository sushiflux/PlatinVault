import { useEffect, useState } from 'react'
import { IconBooks } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { SteamProfileDto } from '../api/types'
import { steamClientUrl, useOpenInSteamClient } from '../utils/steamClient'
import { Tooltip } from './ui/Tooltip'

// The linked Steam account in the dashboard header: avatar + name (link to the profile) and the size of the library.
// Whatever the server cannot tell is simply left out and the rest closes up; with nothing to show there is no chip at all.
export function SteamAccountChip({ profile }: { profile: SteamProfileDto | undefined }) {
  const { t } = useTranslation('shell')
  const [avatarFailed, setAvatarFailed] = useState(false)
  const openInClient = useOpenInSteamClient()
  const avatarUrl = profile?.avatarUrl ?? null

  useEffect(() => {
    setAvatarFailed(false)
  }, [avatarUrl])

  const name = profile?.personaName?.trim() || null
  const avatar = avatarUrl && !avatarFailed ? avatarUrl : null
  const count = profile?.libraryGameCount ?? null
  const profileUrl = profile?.profileUrl ?? null

  if (!name && !avatar && count === null) return null

  // "1.234 Spiele" / "1,234 games": the plural and the number format come from the active language
  const countText = count === null ? null : t('steamChip.games', { count })

  const body = (
    <>
      {avatar && (
        <img
          src={avatar}
          alt=""
          width={28}
          height={28}
          referrerPolicy="no-referrer"
          onError={() => setAvatarFailed(true)}
          className="h-7 w-7 shrink-0 rounded-full object-cover"
          style={{ border: '1px solid var(--border-subtle)' }}
        />
      )}
      {name && (
        <span className="max-w-[10rem] truncate text-sm font-bold max-sm:max-w-[6.5rem]" style={{ color: 'var(--text-primary)' }}>
          {name}
        </span>
      )}
      {name && countText && (
        <span aria-hidden="true" className="pv-hchip-extra" style={{ color: 'var(--border-strong)' }}>
          ·
        </span>
      )}
      {countText && (
        <span className="inline-flex items-center gap-1 whitespace-nowrap text-xs font-semibold pv-hchip-extra" style={{ color: 'var(--text-secondary)' }}>
          <IconBooks size={14} style={{ color: 'var(--accent-crystal)' }} aria-hidden="true" />
          {countText}
        </span>
      )}
    </>
  )

  const label = name ?? t('steamChip.fallbackLabel')
  const description = [
    profileUrl && name ? (openInClient ? t('steamChip.openInClient') : t('steamChip.openInBrowser')) : null,
    countText ? t('steamChip.libraryDescription', { games: countText }) : null,
  ]
    .filter(Boolean)
    .join(' · ')

  const inner =
    profileUrl && name ? (
      <a
        // steam://openurl/ makes the Steam client show the page itself; otherwise the website opens in a new tab
        href={openInClient ? steamClientUrl(profileUrl) : profileUrl}
        {...(openInClient ? {} : { target: '_blank', rel: 'noreferrer' })}
        aria-label={`${openInClient ? t('steamChip.linkClient', { name }) : t('steamChip.linkBrowser', { name })}${
          countText ? `, ${t('steamChip.linkLibrary', { games: countText })}` : ''
        }`}
        className="pv-chip-link"
      >
        {body}
      </a>
    ) : (
      <span className="pv-chip-link">{body}</span>
    )

  return (
    <div className="pv-hchip">
      <Tooltip label={label} description={description || undefined}>
        {inner}
      </Tooltip>
    </div>
  )
}
