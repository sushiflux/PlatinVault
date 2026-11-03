import type { MouseEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchLiveStatus } from '../../api/client'
import { IconLoader2, IconPlayerPlayFilled } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { useSteamReach } from '../../utils/useSteamReach'
import { Tooltip } from '../ui/Tooltip'
import { launchKey, useLauncher } from './LaunchProvider'
import type { LaunchTarget } from './LaunchProvider'
import { isRunningGame } from './running'

/**
 * The forms of the Play button:
 *  - "segment": (quiet surface, emerald icon and label) a segment of a card's action bar or of the group at the end of a list row (.pv-bar-seg in index.css): icon, and the label
 *               "Spielen" / "Play" where the segment is wide enough. Equal width with its neighbours.
 *  - "button":  emerald-filled, icon + label at natural width - for the game page and anywhere it stands alone.
 */
export type PlayVariant = 'segment' | 'button'

interface PlayButtonProps {
  // What it starts
  target: LaunchTarget
  // Name of the game, for the notices and the accessible name
  gameName: string
  variant?: PlayVariant
  // The game cannot be started (it has no Steam link): nothing is rendered, like without a bridge
  unavailable?: boolean
  // Show "Game running" (not clickable) while THIS game runs. Off by default: the game page (variant "button") and the cards (variant "segment")
  // opt in.
  showRunning?: boolean
  // Steam AppID of the game, matched against the running AppID besides the slug
  steamAppId?: number | null
}

/**
 * "Play": starts the game on the gaming PC (through the bridge) or in the Steam client of this PC (the server runs there).
 * It exists ONLY while that can work (`bridgeConnected || local`, see useSteamReach) and is not rendered otherwise, so whatever
 * sits around it closes up - a card's action bar then shares its width between the remaining segments, at the same height. It
 * appears and disappears by itself within a few seconds, without a reload.
 *
 * The size never changes: while the start is running a spinner lies over the icon / label (aria-busy, not clickable). What happens
 * after the click (progress, result, errors) is shown by the <LaunchProvider> as floating notices.
 *
 * Drop-in for the game page: <PlayButton variant="button" target={{ kind: 'game', slug }} gameName={title} />
 */
export function PlayButton({ target, gameName, variant = 'button', unavailable = false, showRunning = false, steamAppId }: PlayButtonProps) {
  const { t } = useTranslation('dashboard')
  const reach = useSteamReach()
  const { launch, isBusy } = useLauncher()
  // Same query key as everywhere else: no extra polling loop
  const live = useQuery({ queryKey: ['live-status'], queryFn: fetchLiveStatus, refetchInterval: 5000, enabled: showRunning })
  if (!reach.canLaunch || unavailable) return null

  const status = live.status === 'error' ? undefined : live.data
  const running = showRunning && isRunningGame(status, target, steamAppId)

  if (running && variant === 'segment') {
    // A segment of a card: the same calm look as Play, only the content changes (pulsing dot instead of the play icon). Short label ("Läuft"), the full sentence in the
    // tooltip; both labels are stacked so the segment keeps its size, and a narrow segment shows the dot alone.
    const runningLabel = t('play.running.label')
    return (
      <Tooltip label={runningLabel} description={reach.bridgeConnected ? t('play.running.viaBridge') : t('play.running.viaSteam')} className="pv-bar-cell">
        <button
          type="button"
          className="pv-bar-seg pv-bar-seg--play pv-bar-seg--running"
          data-play-button=""
          data-running=""
          aria-disabled="true"
          aria-label={t('play.running.aria', { name: gameName })}
          onClick={(event) => {
            // A second start would only focus Steam; the click must not open the card either
            event.preventDefault()
            event.stopPropagation()
          }}
        >
          <span className="inline-flex size-[14px] shrink-0 items-center justify-center" aria-hidden="true">
            <span className="pv-live-dot" />
          </span>
          <span className="pv-bar-label">
            <span className="pv-play-stack">
              <span>{t('play.running.short')}</span>
              <span className="invisible" aria-hidden="true">{t('play.label')}</span>
            </span>
          </span>
        </button>
      </Tooltip>
    )
  }

  if (running) {
    const runningLabel = t('play.running.label')
    return (
      <Tooltip label={runningLabel} description={reach.bridgeConnected ? t('play.running.viaBridge') : t('play.running.viaSteam')}>
        <button
          type="button"
          className="pv-play-button pv-play-button--running"
          data-play-button=""
          data-running=""
          aria-disabled="true"
          aria-label={t('play.running.aria', { name: gameName })}
          onClick={(event) => {
            // A second start would only focus Steam
            event.preventDefault()
            event.stopPropagation()
          }}
        >
          <span className="inline-flex min-w-0 items-center gap-[0.45rem]">
            <span className="inline-flex size-4 shrink-0 items-center justify-center" aria-hidden="true">
              <span className="pv-live-dot" />
            </span>
            <span className="pv-play-stack pv-play-label">
              <span>{runningLabel}</span>
              <span className="invisible" aria-hidden="true">{t('play.label')}</span>
            </span>
          </span>
        </button>
      </Tooltip>
    )
  }

  const busy = isBusy(launchKey(target))
  const label = t('play.label')
  // Through the bridge the game starts on the gaming PC, otherwise in the Steam client of this PC
  const description = reach.bridgeConnected ? t('play.viaBridge') : t('play.viaSteam')
  const onClick = (event: MouseEvent) => {
    // The button sits next to links and sortable cards: the click is only for it
    event.preventDefault()
    event.stopPropagation()
    if (!busy) void launch(target, gameName)
  }
  const segment = variant === 'segment'

  return (
    <Tooltip label={label} description={description} className={segment ? 'pv-bar-cell' : undefined}>
      <button
        type="button"
        className={segment ? 'pv-bar-seg pv-bar-seg--play' : 'pv-play-button'}
        data-play-button=""
        onClick={onClick}
        aria-busy={busy || undefined}
        aria-disabled={busy || undefined}
        aria-label={t('play.aria', { name: gameName })}
      >
        {/* The spinner lies over the content: the button keeps its size while it starts */}
        <span className={`inline-flex min-w-0 items-center ${segment ? 'gap-[0.35rem]' : 'gap-[0.45rem]'} ${busy ? 'invisible' : ''}`}>
          <IconPlayerPlayFilled size={segment ? 14 : 16} className="shrink-0" aria-hidden="true" />
          {showRunning && !segment ? (
            // Holds the width of the longer "Game running" label, so the button does not resize when the state changes
            <span className="pv-play-stack pv-play-label">
              <span>{label}</span>
              <span className="invisible" aria-hidden="true">{t('play.running.label')}</span>
            </span>
          ) : (
            <span className={segment ? 'pv-bar-label' : 'pv-play-label'}>{label}</span>
          )}
        </span>
        {busy && <IconLoader2 size={segment ? 16 : 18} className="absolute animate-spin" aria-hidden="true" />}
      </button>
    </Tooltip>
  )
}
