import { useEffect, useState } from 'react'
import { motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import type { CardPosition, LiveAchievementItemDto, RarityTier } from '../api/types'
import { useFormat } from '../i18n/format'
import { TierTrophy, tierGlow } from './platinum/TierTrophy'

interface TrophyNotificationProps {
  achievement: LiveAchievementItemDto | null
  // The full-screen way is a different component (FullscreenAchievement); this card only knows the nine spots
  position?: CardPosition
}

// Trophy slide-in overlay supporting all 9 screen positions.
export function TrophyNotification({
  achievement,
  position = 'top-right',
}: TrophyNotificationProps) {
  const [visible, setVisible] = useState(false)
  const reduce = useReducedMotion() ?? false

  useEffect(() => {
    if (achievement) {
      const enterTimer = setTimeout(() => setVisible(true), 40)
      const exitTimer = setTimeout(() => setVisible(false), 4500)
      return () => {
        clearTimeout(enterTimer)
        clearTimeout(exitTimer)
      }
    } else {
      setVisible(false)
    }
  }, [achievement])

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
      <TrophyCard achievement={achievement} />
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
}

// The card itself: the dark trophy popup without any positioning, shared by the live overlay and the tour.
// Its footprint never changes: the trophy slot, the icon and the three text lines have fixed room.
export function TrophyCard({ achievement, fluid = false, animated = true }: TrophyCardProps) {
  const { t } = useTranslation('live')
  const fmt = useFormat()
  const reduce = useReducedMotion() ?? false
  const tier: RarityTier = achievement.rarityTier in TROPHY_TIER_COLORS ? achievement.rarityTier : 'Gold'
  const accentColor = TROPHY_TIER_COLORS[tier] ?? '#fbbf24'
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
      className="relative flex items-center gap-3.5 rounded-2xl bg-slate-950/95 px-4 py-3 text-white backdrop-blur-xl"
      style={{
        // One fixed width for every tier and every text length, so nothing about the popup moves or resizes
        width: fluid ? undefined : 'min(420px, calc(100vw - 56px))',
        minHeight: '5.75rem',
        border: `1.5px solid ${accentColor}`,
        boxShadow: `0 18px 40px rgba(0, 0, 0, 0.8), 0 0 25px ${accentColor}40`,
      }}
    >
      {/* Gold and platinum get an extra sheen that glides over the card */}
      {animated && !reduce && shiny && (
        <span className="pointer-events-none absolute inset-0 overflow-hidden rounded-2xl" aria-hidden="true">
          <motion.span
            className="absolute inset-y-0 w-1/3"
            style={{ left: '-33%', background: `linear-gradient(105deg, transparent, ${accentColor}33, transparent)` }}
            initial={{ x: '0%' }}
            animate={{ x: '420%' }}
            transition={{ duration: 1.1, delay: 1.15, ease: 'easeInOut', repeat: 1, repeatDelay: 1.3 }}
          />
        </span>
      )}

      {/* The trophy builds itself up in its own slot */}
      <div className="relative h-[3.6rem] w-[3.2rem] shrink-0">
        <motion.span
          className="pointer-events-none absolute -inset-3 rounded-full"
          style={{ background: `radial-gradient(circle, ${tierGlow(tier)}55, transparent 68%)` }}
          initial={animated ? { opacity: 0 } : false}
          animate={animated ? { opacity: 1 } : undefined}
          transition={{ duration: 0.8, delay: 0.5 }}
          aria-hidden="true"
        />
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

      <motion.img
        src={achievement.iconUrl}
        alt={achievement.displayName}
        className="relative h-12 w-12 shrink-0 rounded-xl object-cover"
        style={{ border: `2px solid ${accentColor}`, boxShadow: `0 0 15px ${accentColor}80` }}
        {...line(0.3)}
      />

      <div className="relative min-w-0 flex-1">
        <motion.div className="mb-0.5 flex items-baseline justify-between gap-2" {...line(0.45)}>
          <span className="text-[11px] font-extrabold uppercase tracking-wider" style={{ color: accentColor }}>
            {t('tierTrophy', { tier: t(`tiers.${tier}`) })}
          </span>
          {percent && <span className="shrink-0 text-[11px] font-semibold text-slate-300">{percent}</span>}
        </motion.div>
        <motion.div className="truncate text-base font-extrabold text-white" {...line(0.55)}>
          {achievement.displayName}
        </motion.div>
        <motion.div className="mt-0.5 line-clamp-2 min-h-[2rem] text-xs text-slate-300" {...line(0.65)}>
          {achievement.description}
        </motion.div>
      </div>
    </div>
  )
}
