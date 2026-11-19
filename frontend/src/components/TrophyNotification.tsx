import { useEffect, useState } from 'react'
import { IconClock } from '@tabler/icons-react'
import { motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import type { CardPosition, LiveAchievementItemDto, RarityTier } from '../api/types'
import { useFormat } from '../i18n/format'
import { ACHIEVEMENT_VISIBLE_MS } from '../utils/animationQueue'
import { useUnlockStamp } from '../utils/unlockStamp'
import { PopupCountdown } from './PopupCountdown'
import { TierTrophy } from './platinum/TierTrophy'
import { TrophyGlyph } from './trophies/TrophyGlyph'
import '../styles/popup.css'

interface TrophyNotificationProps {
  achievement: LiveAchievementItemDto | null
  // The full-screen way is a different component (FullscreenAchievement); this card only knows the nine spots
  position?: CardPosition
  // How long the popup stays in total (the queue removes it then); the card slides out CARD_EXIT_MS before that. Default: 5 s.
  visibleMs?: number
}

// The slide-out takes about this long and must be finished when the queue removes the card
const CARD_EXIT_MS = 500

// Trophy slide-in overlay supporting all 9 screen positions.
export function TrophyNotification({
  achievement,
  position = 'top-right',
  visibleMs = ACHIEVEMENT_VISIBLE_MS,
}: TrophyNotificationProps) {
  const [visible, setVisible] = useState(false)
  const reduce = useReducedMotion() ?? false

  useEffect(() => {
    if (achievement) {
      const enterTimer = setTimeout(() => setVisible(true), 40)
      const exitTimer = setTimeout(() => setVisible(false), Math.max(visibleMs - CARD_EXIT_MS, 0))
      return () => {
        clearTimeout(enterTimer)
        clearTimeout(exitTimer)
      }
    } else {
      setVisible(false)
    }
  }, [achievement, visibleMs])

  if (!achievement) return null

  // Reduced motion: no gliding, the card only fades in
  const settled = visible || reduce

  const getPositionStyle = (): React.CSSProperties => {
    const base: React.CSSProperties = {
      position: 'fixed',
      zIndex: 9998,
      transition:
        'transform 0.5s cubic-bezier(0.16, 1, 0.3, 1), opacity 0.4s ease, filter 0.5s ease',
      opacity: visible ? 1 : 0,
      // The card glides in out of a soft blur
      filter: settled ? 'blur(0px)' : 'blur(10px)',
      pointerEvents: 'none',
    }

    switch (position) {
      case 'top-left':
        return {
          ...base,
          top: '28px',
          left: '28px',
          transform: settled ? 'translateX(0) scale(1)' : 'translateX(-120%) scale(0.92)',
        }
      case 'top-center':
        return {
          ...base,
          top: '28px',
          left: '50%',
          transform: settled ? 'translate(-50%, 0) scale(1)' : 'translate(-50%, -120%) scale(0.92)',
        }
      case 'top-right':
        return {
          ...base,
          top: '28px',
          right: '28px',
          transform: settled ? 'translateX(0) scale(1)' : 'translateX(120%) scale(0.92)',
        }
      case 'center-left':
        return {
          ...base,
          top: '50%',
          left: '28px',
          transform: settled ? 'translate(0, -50%) scale(1)' : 'translate(-120%, -50%) scale(0.92)',
        }
      case 'center':
        return {
          ...base,
          top: '50%',
          left: '50%',
          transform: settled ? 'translate(-50%, -50%) scale(1)' : 'translate(-50%, -50%) scale(0.75)',
        }
      case 'center-right':
        return {
          ...base,
          top: '50%',
          right: '28px',
          transform: settled ? 'translate(0, -50%) scale(1)' : 'translate(120%, -50%) scale(0.92)',
        }
      case 'bottom-left':
        return {
          ...base,
          bottom: '28px',
          left: '28px',
          transform: settled ? 'translateX(0) scale(1)' : 'translateX(-120%) scale(0.92)',
        }
      case 'bottom-center':
        return {
          ...base,
          bottom: '28px',
          left: '50%',
          transform: settled ? 'translate(-50%, 0) scale(1)' : 'translate(-50%, 120%) scale(0.92)',
        }
      case 'bottom-right':
        return {
          ...base,
          bottom: '28px',
          right: '28px',
          transform: settled ? 'translateX(0) scale(1)' : 'translateX(120%) scale(0.92)',
        }
    }
  }

  return (
    <div style={getPositionStyle()}>
      <TrophyCard achievement={achievement} countdownMs={visibleMs} />
    </div>
  )
}

// Accent color per trophy tier (also used by the onboarding tour's mock popup)
export const TROPHY_TIER_COLORS: Record<string, string> = {
  Platinum: '#38bdf8',
  Gold: '#fbbf24',
  Silver: '#cbd5e1',
  Bronze: '#d97706',
}

const EASE = [0.22, 1, 0.36, 1] as const

interface TrophyCardProps {
  achievement: LiveAchievementItemDto
  // Fill the available width instead of the fixed popup width (used inside other layouts)
  fluid?: boolean
  // Build the trophy up and stagger the text in; off = the finished card right away
  animated?: boolean
  // The popup's total visible time: draws the thin time bar along the bottom edge that runs out in exactly that time. None = no bar (tour).
  countdownMs?: number
}

// The card itself: the light trophy popup (surface, hairline frame, the thin tier line at the left edge and the thin time line along the
// bottom) without any positioning, shared by the live overlay and the tour.
// Its footprint never changes: the trophy slot, the icon and the four text lines (tier, name, description, unlock time) have fixed room.
export function TrophyCard({ achievement, fluid = false, animated = true, countdownMs }: TrophyCardProps) {
  const { t } = useTranslation('live')
  const fmt = useFormat()
  const stamp = useUnlockStamp()(achievement)
  const reduce = useReducedMotion() ?? false
  const tier: RarityTier = achievement.rarityTier in TROPHY_TIER_COLORS ? achievement.rarityTier : 'Gold'
  const shiny = tier === 'Gold' || tier === 'Platinum'
  const percent =
    achievement.hasGlobalPercent !== false && achievement.globalPercent > 0 ? fmt.percent(achievement.globalPercent, 1) : null

  // Texts arrive one after the other while the trophy is being built
  const line = (delay: number) =>
    animated
      ? {
          initial: { opacity: 0, y: reduce ? 0 : 6 },
          animate: { opacity: 1, y: 0 },
          transition: { duration: 0.35, delay: reduce ? 0.1 : delay, ease: EASE },
        }
      : {}

  return (
    <div
      className="pv-popup-card"
      data-tier={tier}
      style={{
        // One fixed width for every tier and every text length, so nothing about the popup moves or resizes
        width: fluid ? undefined : 'min(420px, calc(100vw - 56px))',
      }}
    >
      {/* The trophy builds itself up in its own slot */}
      <div className="pv-popup-trophy relative h-[3.6rem] w-[3.2rem] shrink-0">
        <TierTrophy
          tier={tier}
          reveal={animated}
          calm={reduce}
          delay={0.2}
          speed={0.9}
          repeatGlint={shiny}
          burstRadius={44}
          className="relative h-full w-full"
        />
      </div>

      {achievement.iconUrl ? (
        <motion.img
          src={achievement.iconUrl}
          alt={achievement.displayName}
          className="pv-popup-icon"
          {...line(0.3)}
        />
      ) : (
        // A trophy of a game added by hand has no icon file: the tier glyph takes its place and its size
        <motion.span className="inline-flex flex-none" {...line(0.3)}>
          <TrophyGlyph tier={tier} className="pv-popup-icon" />
        </motion.span>
      )}

      <div className="relative min-w-0 flex-1">
        <motion.div className="mb-0.5 flex items-baseline justify-between gap-2" {...line(0.45)}>
          <span className="pv-popup-kicker">{t('tierTrophy', { tier: t(`tiers.${tier}`) })}</span>
          {percent && <span className="pv-popup-percent shrink-0 text-sm text-[var(--text-muted)]">{percent}</span>}
        </motion.div>
        <motion.div className="pv-popup-name" {...line(0.55)}>
          {achievement.displayName}
        </motion.div>
        <motion.div className="pv-popup-desc" {...line(0.65)}>
          {achievement.description}
        </motion.div>
        {/* When it was unlocked. The line always has its room (also without a time), so the card is the same height either way. */}
        <motion.div className="pv-popup-stamp h-5" data-unlock-stamp="" {...line(0.75)}>
          {stamp && (
            <>
              <IconClock size={14} stroke={2} className="shrink-0" aria-hidden="true" />
              <span className="truncate">{stamp}</span>
            </>
          )}
        </motion.div>
      </div>

      {countdownMs !== undefined && (
        <PopupCountdown
          key={`${achievement.apiName}-${achievement.unlockTime ?? ''}-${countdownMs}`}
          durationMs={countdownMs}
          color={`var(--tier-${tier.toLowerCase()})`}
          style={{ insetInlineStart: '1.25rem', insetInlineEnd: '1rem', insetBlockEnd: 0, blockSize: '2px' }}
        />
      )}
    </div>
  )
}
