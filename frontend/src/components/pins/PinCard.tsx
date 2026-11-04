import { motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { IconCheck, IconX } from '@tabler/icons-react'
import type { LiveAchievementItemDto } from '../../api/types'
import { useFormat } from '../../i18n/format'
import { GhostIconButton } from '../ui/GhostIconButton'
import { TierIcon } from '../ui/TierIcon'
import { Tooltip } from '../ui/Tooltip'
import type { PinEntry } from './usePinList'
import type { PinsPlacement } from './pinsLayout'

// A counter achievement that is still open shows its progress; an unlocked one is "done" whatever its counter says
function counterOf(ach: LiveAchievementItemDto): { current: number; max: number; percent: number } | null {
  if (ach.achieved || ach.progressMax == null || ach.progressMax <= 1) return null
  const current = Math.max(0, ach.progressCurrent ?? 0)
  const max = ach.progressMax
  return { current, max, percent: Math.min(100, Math.floor((current / max) * 100)) }
}

const iconSrc = (ach: LiveAchievementItemDto) => (ach.achieved ? ach.iconUrl : ach.iconGrayUrl || ach.iconUrl)

interface PinCardProps {
  entry: PinEntry
  placement: PinsPlacement
  onOpen: (apiName: string) => void
  onRemove: (apiName: string) => void
}

/**
 * One pinned achievement: icon, name, tier icon and - for a counter achievement - a progress bar with "312 / 500". The card has a fixed size
 * (the line under the name is always there: counter, "Noch offen" or "Geschafft"), so live counter updates and the unlock change nothing around
 * it. Unlocked = gold card with a check. A click opens the detail dialog (guide tip, note). The x removes the pin (not on a card that is
 * already leaving because the server removed it after the unlock).
 */
export function PinCard({ entry, placement, onOpen, onRemove }: PinCardProps) {
  const { t } = useTranslation('live')
  const fmt = useFormat()
  const reduce = useReducedMotion() ?? false
  const { ach, apiName, leaving } = entry
  const gap = placement === 'dock' ? { marginRight: 0 } : { marginBottom: 0 }
  const exit = reduce ? { opacity: 0 } : placement === 'dock' ? { opacity: 0, scale: 0.92, width: 0, ...gap } : { opacity: 0, scale: 0.94, height: 0, ...gap }

  const name = ach?.displayName ?? apiName
  const tierName = ach ? t(`tiers.${ach.rarityTier}`) : ''
  const counter = ach ? counterOf(ach) : null
  const done = Boolean(ach?.achieved)
  const stateText = done ? t('pins.card.done') : t('pins.card.open')
  const counterText = counter ? `${fmt.number(counter.current)} / ${fmt.number(counter.max)}` : ''
  const label = !ach
    ? t('pins.card.labelUnknown', { name })
    : done
      ? t('pins.card.labelDone', { name, tier: tierName })
      : counter
        ? t('pins.card.labelCounter', { name, tier: tierName, current: fmt.number(counter.current), max: fmt.number(counter.max) })
        : t('pins.card.labelOpen', { name, tier: tierName })
  const description = ach ? [tierName, counter ? counterText : stateText].join(' · ') : undefined

  return (
    <motion.div
      role="listitem"
      className="pv-pin"
      data-done={done ? '' : undefined}
      data-leaving={leaving ? '' : undefined}
      data-testid="pin-card"
      data-api-name={apiName}
      initial={reduce ? { opacity: 0 } : { opacity: 0, scale: 0.94 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={exit}
      transition={{ duration: reduce ? 0 : 0.24, ease: [0.22, 1, 0.36, 1] }}
    >
      <Tooltip label={name} description={description} className="block h-full w-full">
        <button type="button" className="pv-pin-main" aria-label={label} onClick={() => onOpen(apiName)}>
          <span className="pv-pin-icon">
            {ach ? <img src={iconSrc(ach)} alt="" style={{ filter: done ? 'none' : 'grayscale(85%) opacity(75%)' }} /> : <span className="pv-pin-skeleton block animate-pulse" aria-hidden="true" />}
            {done && (
              <span className="pv-pin-check" aria-hidden="true">
                <IconCheck size={12} stroke={3.4} />
              </span>
            )}
          </span>
          <span className="pv-pin-body">
            <span className="pv-pin-name">{name}</span>
            <span className="pv-pin-foot">
              {ach && <TierIcon tier={ach.rarityTier} size={14} />}
              {counter ? (
                <span className="pv-pin-counter">
                  <span className="pv-pin-counter-text text-[0.72rem]">{counterText}</span>
                  <span
                    className="pv-pin-bar"
                    role="progressbar"
                    aria-label={t('pins.card.progress')}
                    aria-valuemin={0}
                    aria-valuemax={100}
                    aria-valuenow={counter.percent}
                  >
                    <span style={{ inlineSize: `${counter.percent}%` }} />
                  </span>
                </span>
              ) : (
                <span className="pv-pin-state">
                  {done && <IconCheck size={13} stroke={3} aria-hidden="true" />}
                  {ach ? stateText : ''}
                </span>
              )}
            </span>
          </span>
        </button>
      </Tooltip>
      {!leaving && (
        <GhostIconButton label={t('pins.card.remove', { name })} onClick={() => onRemove(apiName)} className="pv-pin-remove inline-flex">
          <IconX size={14} aria-hidden="true" />
        </GhostIconButton>
      )}
    </motion.div>
  )
}
