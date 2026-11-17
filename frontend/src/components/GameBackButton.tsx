import { IconArrowLeft } from '@tabler/icons-react'
import { useLocation, useNavigate } from 'react-router'
import { useTranslation } from 'react-i18next'
import { Tooltip } from './ui/Tooltip'

// Is there an entry of this app behind the current one? The browser router keeps the position in the history state (`idx`, 0 = the first
// entry of this page load; a replace keeps it, so switching areas on the game page does not change the answer). Without it (a memory
// router) the key tells: 'default' is the key of the very first entry only.
function hasEntryBehind(locationKey: string): boolean {
  const idx = (window.history.state as { idx?: unknown } | null)?.idx
  return typeof idx === 'number' ? idx > 0 : locationKey !== 'default'
}

/**
 * The way back at the top of a game page's head: a calm frameless text button. With an in-app history entry it steps back to the page the
 * player came from (overview, all games, ...); a game page opened directly (a bookmark, a reload) has none, so it leads to the overview.
 */
export function GameBackButton() {
  const { t } = useTranslation('game')
  const navigate = useNavigate()
  const location = useLocation()
  const goBack = () => {
    if (hasEntryBehind(location.key)) void navigate(-1)
    else void navigate('/')
  }
  return (
    <div className="pv-game-back no-print">
      <Tooltip label={t('back.tooltip')}>
        <button type="button" className="pv-btn-text" onClick={goBack}>
          <IconArrowLeft size={18} aria-hidden="true" />
          <span>{t('back.label')}</span>
        </button>
      </Tooltip>
    </div>
  )
}
