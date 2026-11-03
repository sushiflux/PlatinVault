import { useId } from 'react'
import type { CSSProperties } from 'react'
import { motion } from 'motion/react'
import type { MotionProps } from 'motion/react'
import { useTranslation } from 'react-i18next'
import type { RarityTier } from '../../api/types'

type Stops = [number, string][]

interface Material {
  bowl: Stops
  trim: Stops
  star: Stops
  pedestal: Stops
  // Colour of the halo and of the sparkle burst
  glow: string
  sparks: string[]
}

// Materials per tier: the same trophy shape in bronze, silver, gold and platinum.
// Every material is a stack of gradients (body, trim, emblem, base); shading and rim light are drawn on top of them.
const MATERIALS: Record<RarityTier, Material> = {
  Bronze: {
    bowl: [[0, '#ffe9d1'], [0.3, '#f7c08a'], [0.62, '#d9803a'], [1, '#a4531c']],
    trim: [[0, '#fde2c0'], [0.4, '#d98a45'], [0.75, '#a8581f'], [1, '#6b3410']],
    star: [[0, '#fff0d6'], [0.5, '#f2b36b'], [1, '#c4691f']],
    pedestal: [[0, '#c98a5a'], [0.5, '#7a4a2a'], [1, '#33200f']],
    glow: '#d97706',
    sparks: ['#fdba74', '#fde68a', '#ffffff'],
  },
  Silver: {
    bowl: [[0, '#ffffff'], [0.3, '#f1f5f9'], [0.62, '#cbd5e1'], [1, '#94a3b8']],
    trim: [[0, '#ffffff'], [0.4, '#cbd5e1'], [0.75, '#94a3b8'], [1, '#64748b']],
    star: [[0, '#ffffff'], [0.5, '#e2e8f0'], [1, '#94a3b8']],
    pedestal: [[0, '#cbd5e1'], [0.5, '#64748b'], [1, '#1e293b']],
    glow: '#cbd5e1',
    sparks: ['#ffffff', '#e2e8f0', '#bae6fd'],
  },
  Gold: {
    bowl: [[0, '#fffbeb'], [0.3, '#fde68a'], [0.62, '#fbbf24'], [1, '#d97706']],
    trim: [[0, '#fef9c3'], [0.45, '#fbbf24'], [1, '#b45309']],
    star: [[0, '#ffffff'], [0.45, '#fef08a'], [1, '#f59e0b']],
    pedestal: [[0, '#e5c07b'], [0.5, '#8a6a2f'], [1, '#2b2210']],
    glow: '#fbbf24',
    sparks: ['#fef08a', '#fbbf24', '#ffffff'],
  },
  Platinum: {
    bowl: [[0, '#ffffff'], [0.3, '#f1f5f9'], [0.62, '#bae6fd'], [1, '#7dd3fc']],
    trim: [[0, '#f8fafc'], [0.35, '#c3d0e0'], [0.7, '#8ea2ba'], [1, '#5f738d']],
    star: [[0, '#fef08a'], [0.45, '#fbbf24'], [1, '#d97706']],
    pedestal: [[0, '#cbd5e1'], [0.5, '#64748b'], [1, '#1e293b']],
    glow: '#7dd3fc',
    sparks: ['#ffffff', '#bae6fd', '#fef08a'],
  },
}

export function tierGlow(tier: RarityTier): string {
  return MATERIALS[tier]?.glow ?? MATERIALS.Gold.glow
}

/** The name of a tier in the active language ("Silber" / "Silver"). A hook, so the component renders again on a language switch. */
export function useTierLabel(): (tier: RarityTier) => string {
  const { t } = useTranslation('live')
  return (tier) => t(`tiers.${tier}`)
}

interface TierTrophyProps {
  tier?: RarityTier
  className?: string
  style?: CSSProperties
  // Build the trophy up piece by piece (base, stem, cup outline, metal poured in, rim, handles, emblem), then a glint
  // runs over it and a sparkle burst follows. Without it the finished trophy is simply drawn.
  reveal?: boolean
  // Seconds to wait before the build-up starts (a popup card glides in first)
  delay?: number
  // 1 = tight (popup, about 1.3 s); larger values stretch the sequence (platinum moment)
  speed?: number
  // Let the glint come back every few seconds (platinum moment, gold shimmer)
  repeatGlint?: boolean
  // Radius of the sparkle burst in px
  burstRadius?: number
  // Reduced motion: a calm fade instead of the build-up, no glint and no burst
  calm?: boolean
}

// 4-point star used for the sparkle burst
const SPARK_PATH = 'M4 0L4.9 3.1L8 4L4.9 4.9L4 8L3.1 4.9L0 4L3.1 3.1L4 0Z'

function SparkleBurst({ colors, delay, radius }: { colors: string[]; delay: number; radius: number }) {
  const count = 10
  return (
    <div className="pointer-events-none absolute inset-0 flex items-center justify-center" aria-hidden="true">
      {Array.from({ length: count }, (_, i) => {
        const angle = (i / count) * Math.PI * 2 + 0.3
        // Alternate near and far particles so the burst reads as a ring with depth, not as a perfect circle
        const distance = radius * (i % 2 === 0 ? 1 : 0.68)
        const size = 6 + (i % 3) * 3
        return (
          <motion.svg
            key={i}
            viewBox="0 0 8 8"
            width={size}
            height={size}
            className="absolute"
            initial={{ x: 0, y: 0, scale: 0, opacity: 0, rotate: 0 }}
            animate={{
              x: Math.cos(angle) * distance,
              y: Math.sin(angle) * distance,
              scale: [0, 1.15, 0.2],
              opacity: [0, 1, 0],
              rotate: 90,
            }}
            transition={{ duration: 0.95, delay: delay + (i % 3) * 0.04, ease: 'easeOut' }}
          >
            <path d={SPARK_PATH} fill={colors[i % colors.length]} />
          </motion.svg>
        )
      })}
    </div>
  )
}

const EASE = [0.22, 1, 0.36, 1] as const

// The PlatinVault trophy (same artwork as favicon.svg, without the app-icon plate), built from separate parts so each
// can arrive on its own: tier material, rim light on the left, inner shadow on the right, contact shadow on the ground.
// All gradient ids carry a per-instance prefix, so several trophies on one page never share each other's defs.
export function TierTrophy({
  tier = 'Platinum',
  className,
  style,
  reveal = false,
  delay = 0,
  speed = 1,
  repeatGlint = false,
  burstRadius = 56,
  calm = false,
}: TierTrophyProps) {
  const { t } = useTranslation('live')
  const uid = useId().replace(/[^a-zA-Z0-9_-]/g, '')
  const id = (name: string) => `pvt-${uid}-${name}`
  const ref = (name: string) => `url(#${id(name)})`
  const material = MATERIALS[tier] ?? MATERIALS.Gold
  const build = reveal && !calm
  // Seconds from the start of the build-up, scaled by `speed`
  const at = (seconds: number) => delay + seconds * speed
  const dur = (seconds: number) => seconds * speed

  // Props of one animated part; a static trophy gets none, so it renders in its final state
  const part = (from: Record<string, number>, to: Record<string, number>, start: number, seconds: number, spring = false): Pick<MotionProps, 'initial' | 'animate' | 'transition'> =>
    build
      ? {
          initial: from,
          animate: to,
          transition: spring
            ? { type: 'spring', stiffness: 360, damping: 13, delay: at(start) }
            : { duration: dur(seconds), delay: at(start), ease: EASE },
        }
      : {}

  const renderStops = (stops: Stops) =>
    stops.map(([offset, color]) => <stop key={`${offset}-${color}`} offset={offset} stopColor={color} />)

  const outline = 'M19 16H45V25.5C45 33.2 39.2 39.2 32 39.2C24.8 39.2 19 33.2 19 25.5V16Z'

  return (
    <div className={`relative ${className ?? ''}`} style={style}>
      <motion.div
        className="h-full w-full"
        // Calm version: the finished trophy simply fades in
        initial={reveal && calm ? { opacity: 0 } : false}
        animate={reveal && calm ? { opacity: 1 } : undefined}
        transition={{ duration: 0.6, delay }}
      >
        <svg
          viewBox="8 6 48 54"
          fill="none"
          role="img"
          aria-label={t('tierTrophy', { tier: t(`tiers.${tier}`) })}
          className="h-full w-full"
          xmlns="http://www.w3.org/2000/svg"
        >
          <defs>
            <linearGradient id={id('bowl')} x1="18" y1="12" x2="46" y2="40" gradientUnits="userSpaceOnUse">
              {renderStops(material.bowl)}
            </linearGradient>
            <linearGradient id={id('trim')} x1="12" y1="14" x2="52" y2="54" gradientUnits="userSpaceOnUse">
              {renderStops(material.trim)}
            </linearGradient>
            <linearGradient id={id('star')} x1="24" y1="20" x2="40" y2="34" gradientUnits="userSpaceOnUse">
              {renderStops(material.star)}
            </linearGradient>
            <linearGradient id={id('pedestal')} x1="20" y1="46" x2="44" y2="56" gradientUnits="userSpaceOnUse">
              {renderStops(material.pedestal)}
            </linearGradient>
            <linearGradient id={id('shine')} x1="22" y1="14" x2="31" y2="36" gradientUnits="userSpaceOnUse">
              <stop offset="0" stopColor="#ffffff" stopOpacity="0.95" />
              <stop offset="1" stopColor="#ffffff" stopOpacity="0.08" />
            </linearGradient>
            {/* Inner shadow: the right/lower side of the body falls into shade */}
            <linearGradient id={id('shade')} x1="30" y1="20" x2="46" y2="38" gradientUnits="userSpaceOnUse">
              <stop offset="0" stopColor="#0f172a" stopOpacity="0" />
              <stop offset="1" stopColor="#0f172a" stopOpacity="0.34" />
            </linearGradient>
            <linearGradient id={id('stemShade')} x1="29" y1="0" x2="35" y2="0" gradientUnits="userSpaceOnUse">
              <stop offset="0" stopColor="#ffffff" stopOpacity="0.28" />
              <stop offset="0.55" stopColor="#0f172a" stopOpacity="0" />
              <stop offset="1" stopColor="#0f172a" stopOpacity="0.34" />
            </linearGradient>
            <linearGradient id={id('sweep')} x1="0" y1="0" x2="1" y2="0">
              <stop offset="0" stopColor="#ffffff" stopOpacity="0" />
              <stop offset="0.5" stopColor="#ffffff" stopOpacity="0.95" />
              <stop offset="1" stopColor="#ffffff" stopOpacity="0" />
            </linearGradient>
            {/* The metal is poured into the cup from the bottom: a mask that grows upwards */}
            <mask id={id('pour')} maskUnits="userSpaceOnUse" x="8" y="6" width="48" height="54">
              <motion.rect
                x="17"
                y="14"
                width="30"
                height="27"
                fill="#ffffff"
                style={{ originX: 0.5, originY: 1 }}
                {...part({ scaleY: 0 }, { scaleY: 1 }, 0.5, 0.34)}
              />
            </mask>
            {/* Silhouette of the whole trophy: the glint is only visible where there is metal */}
            <mask id={id('silhouette')} maskUnits="userSpaceOnUse" x="8" y="6" width="48" height="54">
              <g stroke="#ffffff" strokeLinecap="round" strokeLinejoin="round" fill="#ffffff">
                <path d="M20 17H14.5C12.5 17 11 18.6 11 20.6C11 26.2 15.2 31.2 21.5 32.2" fill="none" strokeWidth="3.2" />
                <path d="M44 17H49.5C51.5 17 53 18.6 53 20.6C53 26.2 48.8 31.2 42.5 32.2" fill="none" strokeWidth="3.2" />
                <path d="M18 13.5L23 16L27.5 12.5L32 16L36.5 12.5L41 16L46 13.5V16.5H18V13.5Z" stroke="none" />
                <path d={outline} strokeWidth="1.6" />
                <path d="M29 39H35V46.5H29V39Z" stroke="none" />
                <path d="M21 46.5H43L45.5 53.5H18.5L21 46.5Z" strokeWidth="1.5" />
                <rect x="16.5" y="53.5" width="31" height="3" rx="1.5" stroke="none" />
              </g>
            </mask>
          </defs>

          {/* 1. Base plate rises in, with its contact shadow */}
          <motion.g style={{ originX: 0.5, originY: 1 }} {...part({ y: 7, opacity: 0, scaleX: 0.78 }, { y: 0, opacity: 1, scaleX: 1 }, 0, 0.28)}>
            <ellipse cx="32" cy="58" rx="17" ry="1.5" fill="#020617" fillOpacity="0.4" />
            <path d="M21 46.5H43L45.5 53.5H18.5L21 46.5Z" fill={ref('pedestal')} stroke={ref('trim')} strokeWidth="1.5" strokeLinejoin="round" />
            <path d="M21.8 47.2H42.2" stroke="#ffffff" strokeOpacity="0.5" strokeWidth="0.6" strokeLinecap="round" />
            <rect x="25" y="48.8" width="14" height="2.6" rx="1" fill={ref('trim')} />
            <rect x="16.5" y="53.5" width="31" height="3" rx="1.5" fill={ref('trim')} />
            <rect x="17.5" y="53.9" width="29" height="0.8" rx="0.4" fill="#ffffff" fillOpacity="0.45" />
          </motion.g>

          {/* 2. Stem grows upwards */}
          <motion.g style={{ originX: 0.5, originY: 1 }} {...part({ scaleY: 0, opacity: 0 }, { scaleY: 1, opacity: 1 }, 0.16, 0.26)}>
            <path d="M29 39H35V46.5H29V39Z" fill={ref('trim')} />
            <path d="M29 39H35V46.5H29V39Z" fill={ref('stemShade')} />
            <rect x="26.5" y="41.5" width="11" height="2.6" rx="1.3" fill="#ffffff" fillOpacity="0.85" />
            <rect x="26.5" y="43" width="11" height="1.1" rx="0.55" fill="#0f172a" fillOpacity="0.16" />
          </motion.g>

          {/* 5. Handles swing in from the sides and snap on (timing: after the rim). Drawn before the cup on purpose:
                 the cup covers their inner ends, so they read as attached behind the body instead of lying on top of it */}
          <motion.g style={{ originX: 1, originY: 0.5 }} {...part({ x: -8, rotate: -26, opacity: 0, scale: 0.7 }, { x: 0, rotate: 0, opacity: 1, scale: 1 }, 0.7, 0, true)}>
            <path d="M20 17H14.5C12.5 17 11 18.6 11 20.6C11 26.2 15.2 31.2 21.5 32.2" stroke={ref('trim')} strokeWidth="3.2" strokeLinecap="round" strokeLinejoin="round" />
            <path d="M19.6 15.9H14.5C13.2 15.9 12.1 17 12.1 18.4" stroke="#ffffff" strokeOpacity="0.65" strokeWidth="0.8" strokeLinecap="round" />
          </motion.g>
          <motion.g style={{ originX: 0, originY: 0.5 }} {...part({ x: 8, rotate: 26, opacity: 0, scale: 0.7 }, { x: 0, rotate: 0, opacity: 1, scale: 1 }, 0.76, 0, true)}>
            <path d="M44 17H49.5C51.5 17 53 18.6 53 20.6C53 26.2 48.8 31.2 42.5 32.2" stroke={ref('trim')} strokeWidth="3.2" strokeLinecap="round" strokeLinejoin="round" />
          </motion.g>

          {/* 3. Cup: first the outline draws itself, then the metal is poured in from the bottom */}
          <g mask={build ? ref('pour') : undefined}>
            <path d={outline} fill={ref('bowl')} />
            <path d="M45 16V25.5C45 33.2 39.2 39.2 32 39.2C27.2 39.2 23.1 36.6 21 32.8C27.4 36.2 36.4 33 40.6 26.6C42.4 23.8 43.4 20.2 43.4 16H45Z" fill={ref('shade')} />
            <path d="M22 18H29.5V36.8C25.2 35.6 22 31.1 22 25.5V18Z" fill={ref('shine')} />
            <path d="M19.9 16.8V25.5C19.9 30.8 23 35.2 27.6 37.2" stroke="#ffffff" strokeOpacity="0.8" strokeWidth="0.7" strokeLinecap="round" />
          </g>
          <motion.path
            d={outline}
            stroke={ref('trim')}
            strokeWidth="1.6"
            strokeLinejoin="round"
            {...part({ pathLength: 0, opacity: 0 }, { pathLength: 1, opacity: 1 }, 0.3, 0.36)}
          />

          {/* 4. Crown rim settles on the cup */}
          <motion.g {...part({ y: 5, opacity: 0 }, { y: 0, opacity: 1 }, 0.62, 0.22)}>
            <path d="M18 13.5L23 16L27.5 12.5L32 16L36.5 12.5L41 16L46 13.5V16.5H18V13.5Z" fill={ref('trim')} />
            <path d="M18.4 13.9L23 16.2L27.5 13" stroke="#ffffff" strokeOpacity="0.55" strokeWidth="0.6" strokeLinecap="round" strokeLinejoin="round" />
          </motion.g>

          {/* 6. Star emblem pops into the cup */}
          <motion.g style={{ originX: 0.5, originY: 0.5 }} {...part({ scale: 0, rotate: -50, opacity: 0 }, { scale: 1, rotate: 0, opacity: 1 }, 0.92, 0, true)}>
            <path
              d="M32 20L34.3 24.6L39.4 25.3L35.7 28.9L36.6 34L32 31.5L27.4 34L28.3 28.9L24.6 25.3L29.7 24.6L32 20Z"
              fill={ref('star')}
              stroke="#0f172a"
              strokeWidth="0.8"
              strokeLinejoin="round"
            />
            <path d="M32 21.8L33.5 24.9" stroke="#ffffff" strokeOpacity="0.7" strokeWidth="0.6" strokeLinecap="round" />
          </motion.g>

          {/* 7. Glint over the finished trophy, only where there is metal */}
          {build && (
            <g mask={ref('silhouette')}>
              <motion.rect
                x={4}
                y={4}
                width={12}
                height={58}
                fill={ref('sweep')}
                style={{ skewX: -16 }}
                initial={{ x: 0, opacity: 0 }}
                animate={{ x: [0, 64], opacity: [0, 1, 1, 0] }}
                transition={{
                  duration: dur(0.9),
                  times: [0, 0.12, 0.88, 1],
                  ease: 'easeInOut',
                  delay: at(1.25),
                  repeat: repeatGlint ? Infinity : 0,
                  repeatDelay: 3.4,
                }}
              />
            </g>
          )}
        </svg>
      </motion.div>

      {/* 8. Sparkle burst in the tier colour once the trophy is complete */}
      {build && <SparkleBurst colors={material.sparks} delay={at(1.2)} radius={burstRadius} />}
    </div>
  )
}
