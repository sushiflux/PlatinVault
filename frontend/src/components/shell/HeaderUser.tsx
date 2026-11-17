import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { IconUser } from '@tabler/icons-react'
import { fetchSteamProfile } from '../../api/client'
import type { SteamProfileDto } from '../../api/types'
import { steamClientUrl, useOpenInSteamClient } from '../../utils/steamClient'
import { Tooltip } from '../ui/Tooltip'

/** The linked Steam account for the header and the overflow menu (one query, shared with the settings and the dashboard). */
export function useSteamProfile(): SteamProfileDto | undefined {
  return useQuery({ queryKey: ['steam-profile'], queryFn: fetchSteamProfile, staleTime: 5 * 60_000 }).data
}

/** What the account link says: the name and the size of the library, and where the profile opens. Shared by the chip and the menu entry. */
export function useSteamUserText(profile: SteamProfileDto | undefined) {
  const { t } = useTranslation('shell')
  const openInClient = useOpenInSteamClient()
  const name = profile?.personaName?.trim() || null
  const count = profile?.libraryGameCount ?? null
  const countText = count === null ? null : t('steamChip.games', { count })
  const profileUrl = profile?.profileUrl ?? null
  const href = profileUrl && name ? (openInClient ? steamClientUrl(profileUrl) : profileUrl) : null
  const label = name ?? t('steamChip.fallbackLabel')
  const description = [
    href ? (openInClient ? t('steamChip.openInClient') : t('steamChip.openInBrowser')) : null,
    countText ? t('steamChip.libraryDescription', { games: countText }) : null,
  ]
    .filter(Boolean)
    .join(' · ')
  const ariaLabel =
    name && href ? `${openInClient ? t('steamChip.linkClient', { name }) : t('steamChip.linkBrowser', { name })}${countText ? `, ${t('steamChip.linkLibrary', { games: countText })}` : ''}` : undefined
  return { name, countText, href, openInClient, label, description, ariaLabel }
}

/**
 * The Steam user in the top bar (avatar, name, size of the library), the old account chip in the light look. It is a link to the profile when
 * the server knows its address. Narrow windows show the avatar only (name and details stay in the tooltip). With nothing to show there is no chip.
 */
export function HeaderUser({ avatarOnly }: { avatarOnly: boolean }) {
  const profile = useSteamProfile()
  const [avatarFailed, setAvatarFailed] = useState(false)
  const avatarUrl = profile?.avatarUrl ?? null
  useEffect(() => setAvatarFailed(false), [avatarUrl])
  const { name, countText, href, openInClient, label, description, ariaLabel } = useSteamUserText(profile)
  if (!name && !avatarUrl && countText === null) return null

  const body = (
    <>
      {avatarUrl && !avatarFailed ? (
        <img className="pv-avatar" src={avatarUrl} alt="" width={32} height={32} referrerPolicy="no-referrer" onError={() => setAvatarFailed(true)} />
      ) : (
        <span className="pv-avatar pv-avatar--empty" aria-hidden="true">
          <IconUser size={18} />
        </span>
      )}
      {!avatarOnly && (
        <span className="pv-user-text">
          <span className="pv-user-name">{label}</span>
          {countText && <span className="pv-user-sub">{countText}</span>}
        </span>
      )}
    </>
  )
  const inner = href ? (
    <a href={href} {...(openInClient ? {} : { target: '_blank', rel: 'noreferrer' })} className="pv-user" aria-label={ariaLabel}>
      {body}
    </a>
  ) : (
    <span className="pv-user" role="group" aria-label={avatarOnly ? label : undefined}>
      {body}
    </span>
  )
  return (
    <Tooltip label={label} description={description || undefined} side="bottom" className="pv-user-tip">
      {inner}
    </Tooltip>
  )
}
