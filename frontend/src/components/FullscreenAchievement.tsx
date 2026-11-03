import { useEffect, useRef, useState } from 'react'
import type { CSSProperties } from 'react'
import { IconDeviceGamepad2 } from '@tabler/icons-react'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import type { LiveAchievementItemDto, RarityTier } from '../api/types'
import { useFormat } from '../i18n/format'
import { useMediaQuery } from '../utils/useMediaQuery'
import { TierTrophy, tierGlow } from './platinum/TierTrophy'

// The "Vollbild" popup position: for a few seconds the whole window belongs to the achievement that was just unlocked.
//
// Self-contained on purpose: it needs only the achievement (its tier decides the colours), an optional game name and a dismiss
// callback, and it never reads the theme (the stage is dark in the light and the dark theme alike, like a cinema moment). The
// Windows desktop host can therefore render the very same component in its transparent overlay window over a game.
// Not the Fullscreen API (that needs a user gesture): it is a normal fixed overlay that covers the page.

interface FullscreenAchievementProps {
  achievement: LiveAchievementItemDto | null
  // Small line at the bottom of the text block; left out when unknown
  gameName?: string | null
  // Click or tap anywhere, or Esc
  onDismiss: () => void
  // False in the in-game overlay of the Windows program: its window lets every click through to the game, so the hint is left out
  dismissHint?: boolean
}

const EASE = [0.22, 1, 0.36, 1] as const

// Under the platinum moment (z 9999) and the card popup (9998): the 100 % celebration stays the biggest thing on screen
const Z_INDEX = 9996

// Colour of the tier for lines of text. Lighter than the tier's border colour so that small text keeps its contrast on the stage
// (the Bronze border colour #d97706 only reaches about 4.5:1 there).
const TIER_TEXT: Record<RarityTier, string> = {
  Bronze: '#fdba74',
  Silver: '#e2e8f0',
  Gold: '#fcd34d',
  Platinum: '#7dd3fc',
}

// Border / ring colour of the tier (the same colours as the card popup)
const TIER_EDGE: Record<RarityTier, string> = {
  Bronze: '#d97706',
  Silver: '#cbd5e1',
  Gold: '#fbbf24',
  Platinum: '#38bdf8',
}

// The five rarity steps the server names (rarityBand) and the statistics texts that spell them out
const BAND_KEY = {
  ultraRare: 'ultra',
  veryRare: 'very',
  rare: 'rare',
  uncommon: 'uncommon',
  common: 'common',
} as const

// Sizes follow the window, not the device: the same rules cover a phone held upright, a tablet, a 16:9 monitor and a 32:9 ultrawide.
// Portrait stacks everything in one centred column, landscape puts the trophy beside the text.
const SIZES = {
  portrait: {
    stage: { flexDirection: 'column', textAlign: 'center', gap: 'clamp(1rem, 3.2vh, 3rem)', maxWidth: 'min(100%, 44rem)' },
    trophy: 'clamp(7rem, min(56vw, 28vh), 22rem)',
    text: { alignItems: 'center', maxWidth: '100%' },
    icon: 'clamp(2.75rem, min(14vw, 6vh), 5rem)',
    eyebrow: 'clamp(0.7rem, min(3.2vw, 1.5vh), 1.25rem)',
    name: 'clamp(1.55rem, min(8vw, 4.4vh), 4rem)',
    description: 'clamp(0.95rem, min(4.2vw, 2.1vh), 1.75rem)',
    game: 'clamp(0.75rem, min(3.4vw, 1.6vh), 1.25rem)',
  },
  landscape: {
    stage: { flexDirection: 'row', textAlign: 'left', gap: 'clamp(1.5rem, 6vh, 7rem)', maxWidth: '100%' },
    trophy: 'clamp(6rem, min(38vh, 26vw), 44rem)',
    text: { alignItems: 'flex-start', maxWidth: 'min(50vw, max(24rem, 76vh))' },
    icon: 'clamp(2.75rem, 6.5vh, 7.5rem)',
    eyebrow: 'clamp(0.7rem, min(1.2vw, 1.7vh), 1.9rem)',
    name: 'clamp(1.5rem, min(3.6vw, 6.4vh), 6.5rem)',
    description: 'clamp(0.95rem, min(1.7vw, 2.5vh), 3rem)',
    game: 'clamp(0.75rem, min(1.1vw, 1.7vh), 1.9rem)',
  },
} as const satisfies Record<string, { stage: CSSProperties; text: CSSProperties } & Record<string, unknown>>

export function FullscreenAchievement({ achievement, gameName, onDismiss, dismissHint = true }: FullscreenAchievementProps) {
  const { t } = useTranslation(['live', 'stats'])
  const fmt = useFormat()
  const reduce = useReducedMotion() ?? false
  const portrait = useMediaQuery('(orientation: portrait)')
  const size = portrait ? SIZES.portrait : SIZES.landscape

  // The parent passes a fresh callback on every render; keep the latest one without re-subscribing the key handler
  const onDismissRef = useRef(onDismiss)
  useEffect(() => {
    onDismissRef.current = onDismiss
  })

  // Esc ends the moment. Captured before anything else, so an open dialog behind it does not close with the same key press.
  // The focus is never moved: the popup does not take the keyboard away from what the user was doing.
  const active = achievement !== null
  useEffect(() => {
    if (!active) return
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      e.preventDefault()
      e.stopPropagation()
      onDismissRef.current()
    }
    document.addEventListener('keydown', onKeyDown, true)
    return () => document.removeEventListener('keydown', onKeyDown, true)
  }, [active])

  const tier: RarityTier = achievement && achievement.rarityTier in TIER_EDGE ? achievement.rarityTier : 'Gold'
  const edge = TIER_EDGE[tier]
  const tone = TIER_TEXT[tier]
  const glow = tierGlow(tier)
  const tierName = t(`live:tiers.${tier}`)

  // Rarity: the step's name (translated from the language-neutral key, older data falls back to the server's text) and Steam's
  // worldwide share. Both are left out when Steam delivered no share.
  const hasShare = achievement ? achievement.hasGlobalPercent !== false && achievement.globalPercent > 0 : false
  const bandKey = achievement?.rarityBand ? BAND_KEY[achievement.rarityBand as keyof typeof BAND_KEY] : undefined
  const bandLabel = !achievement || !hasShare ? '' : bandKey ? t(`stats:rarity.band.${bandKey}.label`) : achievement.rarityLabel
  const share = achievement && hasShare ? t('live:fullscreen.share', { percent: fmt.percent(achievement.globalPercent, 1) }) : ''
  const rarity = [bandLabel, share].filter(Boolean).join(' · ')

  // Reduced motion: no gliding, no ring, no build-up - everything only fades
  const rise = (delay: number, distance = 14) => ({
    initial: { opacity: 0, y: reduce ? 0 : distance },
    animate: { opacity: 1, y: 0 },
    transition: { duration: reduce ? 0.35 : 0.6, delay: reduce ? 0.15 : delay, ease: EASE },
  })

  const TEXT_AT = 0.55
  const announcement = achievement
    ? [t('live:fullscreen.unlocked', { tier: tierName }), achievement.displayName, achievement.description, rarity]
        .filter(Boolean)
        // A sentence of its own each; no second full stop after a text that already ends with one
        .map((part) => (/[.!?…]$/.test(part) ? part : `${part}.`))
        .join(' ')
    : ''

  return (
    <>
      {/* The screen reader announcement; mounted all the time so that a text appearing in it is spoken (the card popup is silent for them) */}
      <div className="sr-only" role="status" aria-live="polite">
        {announcement}
      </div>

      <AnimatePresence>
        {achievement && (
          <motion.div
            key={`${achievement.apiName}-${achievement.unlockTime ?? ''}`}
            data-fullscreen-achievement=""
            data-tier={tier}
            onClick={onDismiss}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: reduce ? 0.3 : 0.5, ease: 'easeOut' }}
            className="fixed inset-0 flex cursor-pointer select-none items-center justify-center overflow-hidden outline-none"
            style={{
              zIndex: Z_INDEX,
              padding: 'max(1.25rem, 5vmin)',
              // Dark in both themes: a tier-coloured glow in the middle, a vignette at the edges, the page softly blurred beneath
              background: `radial-gradient(ellipse 62% 56% at 50% 46%, ${glow}2e, rgba(2, 6, 23, 0) 72%), radial-gradient(ellipse 120% 100% at 50% 50%, rgba(2, 6, 23, 0) 45%, rgba(2, 6, 23, 0.55) 100%), rgba(2, 6, 23, 0.86)`,
              backdropFilter: 'blur(14px) saturate(1.1)',
              WebkitBackdropFilter: 'blur(14px) saturate(1.1)',
            }}
          >
            <motion.div
              className="relative flex w-full items-center justify-center"
              style={{ ...size.stage, margin: '0 auto' }}
              exit={reduce ? undefined : { opacity: 0, scale: 0.97 }}
              transition={{ duration: 0.4, ease: 'easeIn' }}
            >
              {/* Trophy: rises out of the dark on a spring, with a soft glow and one ring that spreads once it is complete */}
              <motion.div
                className="relative shrink-0"
                style={{ width: size.trophy, aspectRatio: '48 / 54' }}
                initial={{ opacity: 0, scale: reduce ? 1 : 0.8, y: reduce ? 0 : 28 }}
                animate={{ opacity: 1, scale: 1, y: 0 }}
                transition={reduce ? { duration: 0.4 } : { type: 'spring', stiffness: 150, damping: 17, delay: 0.1 }}
              >
                <motion.span
                  className="pointer-events-none absolute rounded-full"
                  style={{ inset: '-38%', background: `radial-gradient(circle, ${glow}66, ${glow}1a 46%, transparent 70%)` }}
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  transition={{ duration: reduce ? 0.4 : 1.2, delay: reduce ? 0.1 : 0.25 }}
                  aria-hidden="true"
                />
                {!reduce && (
                  <span className="pointer-events-none absolute inset-0 flex items-center justify-center" aria-hidden="true">
                    <motion.span
                      className="block aspect-square rounded-full"
                      style={{ width: '112%', border: `2px solid ${edge}` }}
                      initial={{ opacity: 0, scale: 0.8 }}
                      animate={{ opacity: [0, 0.55, 0], scale: [0.8, 1.55, 1.9] }}
                      transition={{ duration: 1.5, delay: 1.0, times: [0, 0.25, 1], ease: 'easeOut' }}
                    />
                  </span>
                )}
                <TierTrophy
                  tier={tier}
                  reveal
                  calm={reduce}
                  delay={0.3}
                  speed={0.85}
                  burstRadius={150}
                  className="relative h-full w-full"
                  style={{ filter: `drop-shadow(0 0 28px ${glow}80) drop-shadow(0 14px 22px rgba(2, 6, 23, 0.6))` }}
                />
              </motion.div>

              {/* Words */}
              <div className="relative flex min-w-0 flex-col" style={size.text}>
                <motion.div
                  className="flex items-center"
                  style={{ gap: 'clamp(0.7rem, 1.6vh, 1.5rem)', flexDirection: portrait ? 'column' : 'row' }}
                  {...rise(TEXT_AT)}
                >
                  {achievement.iconUrl && <AchievementIcon url={achievement.iconUrl} size={size.icon} edge={edge} />}
                  <div className="min-w-0" style={{ textAlign: portrait ? 'center' : 'left' }}>
                    <div
                      className="font-extrabold uppercase"
                      style={{ color: tone, fontSize: size.eyebrow, letterSpacing: '0.24em' }}
                    >
                      {t('live:fullscreen.unlocked', { tier: tierName })}
                    </div>
                    {rarity && (
                      <div className="mt-1 font-semibold text-slate-200" style={{ fontSize: size.game }}>
                        {rarity}
                      </div>
                    )}
                  </div>
                </motion.div>

                <motion.h1
                  className="font-black text-white"
                  style={{
                    marginTop: 'clamp(0.7rem, 2.4vh, 2.5rem)',
                    fontSize: size.name,
                    lineHeight: 1.08,
                    letterSpacing: '-0.01em',
                    textWrap: 'balance',
                    overflowWrap: 'anywhere',
                    textShadow: '0 4px 28px rgba(2, 6, 23, 0.7)',
                  }}
                  {...rise(TEXT_AT + 0.12, 18)}
                >
                  {achievement.displayName}
                </motion.h1>

                {/* The accent rule under the name: grows out of the left (the middle in portrait) */}
                <motion.span
                  className="block h-[3px] rounded-full"
                  style={{
                    marginTop: 'clamp(0.6rem, 1.6vh, 1.5rem)',
                    width: 'clamp(3rem, 9vh, 9rem)',
                    background: `linear-gradient(90deg, ${edge}, ${tone})`,
                    transformOrigin: portrait ? '50% 50%' : '0% 50%',
                  }}
                  initial={{ opacity: 0, scaleX: reduce ? 1 : 0 }}
                  animate={{ opacity: 1, scaleX: 1 }}
                  transition={{ duration: reduce ? 0.35 : 0.6, delay: reduce ? 0.15 : TEXT_AT + 0.3, ease: EASE }}
                  aria-hidden="true"
                />

                {achievement.description && (
                  <motion.p
                    className="text-slate-200"
                    style={{
                      marginTop: 'clamp(0.6rem, 1.8vh, 1.75rem)',
                      fontSize: size.description,
                      lineHeight: 1.4,
                      textWrap: 'pretty',
                      overflowWrap: 'anywhere',
                    }}
                    {...rise(TEXT_AT + 0.35)}
                  >
                    {achievement.description}
                  </motion.p>
                )}

                {gameName && (
                  <motion.div
                    className="flex min-w-0 max-w-full items-center font-semibold uppercase text-slate-300"
                    style={{
                      marginTop: 'clamp(0.9rem, 2.8vh, 3rem)',
                      gap: '0.5em',
                      fontSize: size.game,
                      letterSpacing: '0.14em',
                    }}
                    {...rise(TEXT_AT + 0.5)}
                  >
                    <IconDeviceGamepad2 size="1.35em" aria-hidden="true" style={{ flexShrink: 0, color: tone }} />
                    <span className="min-w-0" style={{ overflowWrap: 'anywhere' }}>
                      {gameName}
                    </span>
                  </motion.div>
                )}
              </div>
            </motion.div>

            {/* How to leave early; out of the flow, and left out on very short windows and in the click-through overlay */}
            {dismissHint && (
            <motion.p
              className="pointer-events-none absolute inset-x-0 text-center text-xs text-slate-300 [@media(max-height:460px)]:hidden"
              style={{ bottom: 'max(0.9rem, 2.6vh)' }}
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              transition={{ duration: 0.6, delay: reduce ? 0.4 : 1.8 }}
            >
              {t('live:fullscreen.dismiss')}
            </motion.p>
            )}
          </motion.div>
        )}
      </AnimatePresence>
    </>
  )
}

// The achievement's own icon; a broken image link takes the icon away instead of leaving a gap with a frame
function AchievementIcon({ url, size, edge }: { url: string; size: string; edge: string }) {
  const [failedUrl, setFailedUrl] = useState<string | null>(null)
  if (failedUrl === url) return null
  return (
    <img
      src={url}
      alt=""
      onError={() => setFailedUrl(url)}
      className="shrink-0 rounded-xl object-cover"
      style={{ width: size, height: size, border: `2px solid ${edge}`, boxShadow: `0 0 22px ${edge}80` }}
    />
  )
}
