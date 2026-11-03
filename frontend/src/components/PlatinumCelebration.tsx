import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import confetti from 'canvas-confetti'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { fetchGames } from '../api/client'
import { useFormat } from '../i18n/format'
import type { PlatinumData } from '../utils/animationQueue'
import { CrystalIntro } from './platinum/CrystalIntro'
import { TierTrophy } from './platinum/TierTrophy'

interface PlatinumCelebrationProps {
  data: PlatinumData | null
  onDismiss: () => void
  // False in the in-game overlay of the Windows program: its window lets every click through to the game, so "click to close" is not offered
  dismissHint?: boolean
}

const CONFETTI_COLORS = ['#ffffff', '#e0f2fe', '#bae6fd', '#7dd3fc', '#fef08a', '#fbbf24']
const GOLD_COLORS = ['#fef08a', '#fbbf24', '#ffffff']

// Size of the trophy: big on desktop, still comfortable on a phone held upright or sideways
const TROPHY_SIZE = 'clamp(7.5rem, min(62vw, 36vh), 21rem)'

const EASE_OUT = [0.22, 1, 0.36, 1] as const

// Choreography (seconds after the overlay appears): a crystal is presented, charges and bursts at BURST_AT, the trophy is
// built piece by piece out of that burst (slowed down by TROPHY_SPEED), then the words arrive.
const BURST_AT = 1.75
const TROPHY_SPEED = 1.45
const TROPHY_DONE = BURST_AT + 1.25 * TROPHY_SPEED

// Soft light rays behind the trophy: two slowly counter-rotating layers, faded out towards the edges.
function Rays({ animated, delay }: { animated: boolean; delay: number }) {
  const fade = 'radial-gradient(circle at center, #000 6%, rgba(0,0,0,0.55) 30%, transparent 66%)'
  const rays = (color: string, period: number, peak: number) =>
    `repeating-conic-gradient(from 0deg at 50% 50%, transparent 0deg, ${color} ${peak / 2}deg, transparent ${peak}deg, transparent ${period}deg)`
  const layer = { maskImage: fade, WebkitMaskImage: fade }

  return (
    <div
      className="pointer-events-none absolute left-1/2 top-1/2"
      style={{ width: 'min(170vmin, 1400px)', height: 'min(170vmin, 1400px)', transform: 'translate(-50%, -50%)' }}
      aria-hidden="true"
    >
      <motion.div
        className="absolute inset-0"
        style={{ ...layer, backgroundImage: rays('rgba(186, 230, 253, 0.26)', 20, 8) }}
        initial={{ opacity: 0, scale: 0.6 }}
        animate={animated ? { opacity: 1, scale: 1, rotate: 360 } : { opacity: 1, scale: 1 }}
        transition={{
          opacity: { duration: 1.4, delay },
          scale: { duration: 1.6, delay, ease: EASE_OUT },
          rotate: { duration: 120, repeat: Infinity, ease: 'linear' },
        }}
      />
      <motion.div
        className="absolute inset-0"
        style={{ ...layer, backgroundImage: rays('rgba(253, 224, 71, 0.14)', 30, 6) }}
        initial={{ opacity: 0, scale: 0.6 }}
        animate={animated ? { opacity: 1, scale: 1, rotate: -360 } : { opacity: 1, scale: 1 }}
        transition={{
          opacity: { duration: 1.6, delay: delay + 0.2 },
          scale: { duration: 1.8, delay: delay + 0.2, ease: EASE_OUT },
          rotate: { duration: 180, repeat: Infinity, ease: 'linear' },
        }}
      />
    </div>
  )
}

// Four-point stars around the trophy for the calm (reduced motion) version
function StaticSparkles() {
  const spots = [
    { left: '6%', top: '14%', size: 14, color: '#ffffff' },
    { left: '90%', top: '10%', size: 18, color: '#fef08a' },
    { left: '96%', top: '58%', size: 11, color: '#bae6fd' },
    { left: '-2%', top: '64%', size: 16, color: '#fef08a' },
    { left: '78%', top: '92%', size: 12, color: '#ffffff' },
  ]
  return (
    <>
      {spots.map((spot) => (
        <svg
          key={`${spot.left}-${spot.top}`}
          viewBox="0 0 8 8"
          width={spot.size}
          height={spot.size}
          className="absolute"
          style={{ left: spot.left, top: spot.top, opacity: 0.75 }}
          aria-hidden="true"
        >
          <path d="M4 0L4.9 3.1L8 4L4.9 4.9L4 8L3.1 4.9L0 4L3.1 3.1L4 0Z" fill={spot.color} />
        </svg>
      ))}
    </>
  )
}

function CoverThumb({ url }: { url: string }) {
  const [failed, setFailed] = useState(false)
  if (failed) return null
  return (
    <img
      src={url}
      alt=""
      onError={() => setFailed(true)}
      className="h-[4.75rem] w-[3.2rem] shrink-0 rounded-md object-cover"
      style={{ border: '1.5px solid rgba(186, 230, 253, 0.75)', boxShadow: '0 6px 18px rgba(2, 6, 23, 0.55)' }}
    />
  )
}

// The 100% moment: a full-screen "platinum trophy earned" stage - spring-in trophy, light rays, a slow sweep of light,
// a few timed particle bursts and the game's name. Reduced motion gets the same picture without the movement.
export function PlatinumCelebration({ data, onDismiss, dismissHint = true }: PlatinumCelebrationProps) {
  const { t } = useTranslation('live')
  const fmt = useFormat()
  const reduce = useReducedMotion() ?? false
  const rootRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  // The parent passes a fresh callback on every render; keep the latest one without re-running the effects below
  const onDismissRef = useRef(onDismiss)
  useEffect(() => {
    onDismissRef.current = onDismiss
  })

  // Cover of the game, looked up in the library (best effort - the live event only carries the title)
  const { data: games } = useQuery({
    queryKey: ['games'],
    queryFn: fetchGames,
    enabled: Boolean(data),
    staleTime: 60_000,
  })
  const wanted = data?.gameName.trim().toLowerCase()
  const cover = wanted ? games?.find((g) => g.title.trim().toLowerCase() === wanted)?.coverUrl : undefined

  // Esc closes, and the dialog takes the focus so keyboard users are not left behind the overlay
  useEffect(() => {
    if (!data) return
    const previouslyFocused = document.activeElement as HTMLElement | null
    rootRef.current?.focus()
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault()
        onDismissRef.current()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      previouslyFocused?.focus?.()
    }
  }, [data])

  // A few deliberate bursts instead of a blizzard: one big opener, two side cannons, three small fireworks, gold rain
  useEffect(() => {
    if (!data || reduce) return
    const canvas = canvasRef.current
    if (!canvas) return
    const fire = confetti.create(canvas, { resize: true, useWorker: false })
    const timers: number[] = []
    const at = (ms: number, run: () => void) => timers.push(window.setTimeout(run, ms))
    const shoot = (options: confetti.Options) =>
      void fire({ colors: CONFETTI_COLORS, shapes: ['circle', 'star'], disableForReducedMotion: true, ...options })

    // Everything waits for the trophy to be complete
    const base = Math.round(TROPHY_DONE * 1000) - 250

    at(base, () =>
      shoot({ particleCount: 90, spread: 360, startVelocity: 34, gravity: 0.8, ticks: 150, origin: { x: 0.5, y: 0.4 } })
    )
    at(base + 650, () => {
      shoot({ particleCount: 38, angle: 60, spread: 58, startVelocity: 56, gravity: 0.9, ticks: 190, origin: { x: 0, y: 0.78 } })
      shoot({ particleCount: 38, angle: 120, spread: 58, startVelocity: 56, gravity: 0.9, ticks: 190, origin: { x: 1, y: 0.78 } })
    })
    ;[1800, 2450, 3100].forEach((ms, i) =>
      at(base + ms, () =>
        shoot({
          particleCount: 55,
          spread: 360,
          startVelocity: 25,
          gravity: 0.7,
          ticks: 130,
          scalar: 0.8,
          origin: { x: [0.24, 0.76, 0.5][i] ?? 0.5, y: [0.3, 0.26, 0.18][i] ?? 0.25 },
        })
      )
    )
    at(base + 4000, () =>
      shoot({
        particleCount: 36,
        spread: 170,
        startVelocity: 12,
        gravity: 0.5,
        ticks: 280,
        scalar: 0.7,
        colors: GOLD_COLORS,
        origin: { x: 0.5, y: -0.05 },
      })
    )

    return () => {
      timers.forEach((timer) => window.clearTimeout(timer))
      fire.reset()
    }
  }, [data, reduce])

  const summary =
    data?.kind === 'guide'
      ? t('celebration.summaryGuide', { count: data.total })
      : t('celebration.summaryAchievements', { count: data?.total ?? 0 })

  return (
    <AnimatePresence>
      {data && (
        <motion.div
          key="platinum-moment"
          ref={rootRef}
          role="dialog"
          aria-modal="true"
          aria-label={t('celebration.ariaLabel', { game: data.gameName })}
          tabIndex={-1}
          onClick={onDismiss}
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: reduce ? 0.4 : 0.6 }}
          className="fixed inset-0 z-[9999] flex cursor-pointer flex-col items-center justify-center overflow-hidden px-5 text-center outline-none"
          style={{
            background:
              'radial-gradient(ellipse 70% 55% at 50% 36%, rgba(56, 189, 248, 0.20), rgba(15, 23, 42, 0) 70%), radial-gradient(ellipse 80% 40% at 50% 115%, rgba(251, 191, 36, 0.12), rgba(15, 23, 42, 0) 70%), rgba(2, 6, 23, 0.93)',
            backdropFilter: 'blur(18px)',
            WebkitBackdropFilter: 'blur(18px)',
          }}
        >
          {/* Trophy with rays, glow and the flash of its landing */}
          <div className="relative z-10 flex items-center justify-center" style={{ width: TROPHY_SIZE, height: TROPHY_SIZE }}>
            <Rays animated={!reduce} delay={reduce ? 0.3 : BURST_AT - 0.6} />

            <motion.div
              className="pointer-events-none absolute rounded-full"
              style={{
                inset: '-22%',
                background: 'radial-gradient(circle, rgba(125, 211, 252, 0.40), rgba(99, 102, 241, 0.14) 45%, transparent 70%)',
              }}
              initial={{ opacity: 0 }}
              animate={reduce ? { opacity: 1 } : { opacity: [0, 0.75, 1, 0.75] }}
              transition={reduce ? { duration: 0.8 } : { duration: 5, delay: BURST_AT - 0.8, times: [0, 0.2, 0.5, 1], repeat: Infinity, ease: 'easeInOut' }}
              aria-hidden="true"
            />

            {!reduce && <CrystalIntro burstAt={BURST_AT} />}

            <div className="relative h-full w-full">
              <motion.div
                className="h-full w-full"
                animate={reduce ? undefined : { y: [0, -8, 0] }}
                transition={{ duration: 5.5, delay: TROPHY_DONE + 0.6, repeat: Infinity, ease: 'easeInOut' }}
              >
                <TierTrophy
                  tier="Platinum"
                  reveal
                  calm={reduce}
                  delay={reduce ? 0.3 : BURST_AT - 0.05}
                  speed={reduce ? 1 : TROPHY_SPEED}
                  repeatGlint
                  burstRadius={190}
                  className="h-full w-full"
                  style={{ filter: 'drop-shadow(0 0 34px rgba(125, 211, 252, 0.55)) drop-shadow(0 12px 22px rgba(2, 6, 23, 0.6))' }}
                />
              </motion.div>
              {reduce && <StaticSparkles />}
            </div>
          </div>

          {/* Words */}
          <div className="relative z-10 flex max-w-full flex-col items-center" style={{ marginTop: 'max(0.9rem, 3.2vh)' }}>
            <motion.div
              className="text-[11px] font-extrabold uppercase text-sky-300 sm:text-xs"
              style={{ letterSpacing: '0.32em' }}
              initial={{ opacity: 0, y: reduce ? 0 : 14 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.6, delay: reduce ? 0.3 : TROPHY_DONE - 0.5, ease: EASE_OUT }}
            >
              {t('celebration.earned')}
            </motion.div>

            <motion.h1
              className="mt-2 font-black"
              style={{
                fontSize: 'clamp(1.75rem, min(9vw, 10.5vh), 5rem)',
                lineHeight: 1.05,
                letterSpacing: '-0.01em',
                backgroundImage:
                  'linear-gradient(105deg, transparent 38%, rgba(255,255,255,0.95) 50%, transparent 62%), linear-gradient(180deg, #ffffff 0%, #e8eef6 30%, #a9b8cc 52%, #dbeafe 72%, #7dd3fc 100%)',
                backgroundSize: '260% 100%, 100% 100%',
                backgroundRepeat: 'no-repeat',
                backgroundClip: 'text',
                WebkitBackgroundClip: 'text',
                color: 'transparent',
                WebkitTextFillColor: 'transparent',
                filter: 'drop-shadow(0 6px 28px rgba(56, 189, 248, 0.35))',
              }}
              initial={{ opacity: 0, y: reduce ? 0 : 22, backgroundPosition: '170% 0%, 0% 0%' }}
              animate={{
                opacity: 1,
                y: 0,
                backgroundPosition: reduce ? '170% 0%, 0% 0%' : ['170% 0%, 0% 0%', '-40% 0%, 0% 0%'],
              }}
              transition={{
                opacity: { duration: 0.7, delay: reduce ? 0.4 : TROPHY_DONE - 0.15, ease: EASE_OUT },
                y: { duration: 0.7, delay: reduce ? 0.4 : TROPHY_DONE - 0.15, ease: EASE_OUT },
                backgroundPosition: { duration: 1.8, delay: TROPHY_DONE + 0.9, ease: 'easeInOut', repeat: Infinity, repeatDelay: 3.2 },
              }}
            >
              {t('celebration.headline')}
            </motion.h1>

            <motion.div
              className="flex max-w-[min(92vw,34rem)] items-center gap-4 rounded-2xl px-4 py-3 text-left"
              style={{
                marginTop: 'max(0.75rem, 2.4vh)',
                border: '1px solid rgba(186, 230, 253, 0.28)',
                background: 'linear-gradient(135deg, rgba(255,255,255,0.10), rgba(255,255,255,0.03))',
                boxShadow: '0 14px 44px rgba(2, 6, 23, 0.55), inset 0 1px 0 rgba(255,255,255,0.18)',
              }}
              initial={{ opacity: 0, y: reduce ? 0 : 18 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.7, delay: reduce ? 0.5 : TROPHY_DONE + 0.2, ease: EASE_OUT }}
            >
              {cover && <CoverThumb key={cover} url={cover} />}
              <div className="min-w-0">
                <div className="truncate text-lg font-extrabold text-white sm:text-xl">{data.gameName}</div>
                <div className="text-sm font-semibold text-slate-200">{summary}</div>
                <div className="text-xs text-slate-400">{fmt.date(new Date(), 'dateLong')}</div>
              </div>
            </motion.div>

            <motion.p
              className={`text-xs text-slate-400 [@media(max-height:460px)]:hidden ${dismissHint ? '' : 'invisible'}`}
              aria-hidden={dismissHint ? undefined : true}
              style={{ marginTop: 'max(0.75rem, 3vh)' }}
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              transition={{ duration: 0.8, delay: reduce ? 0.8 : TROPHY_DONE + 1.2 }}
            >
              {t('celebration.dismiss')}
            </motion.p>
          </div>

          {/* Particle bursts fly in front of everything and never catch a click */}
          <canvas ref={canvasRef} className="pointer-events-none absolute inset-0 z-20 h-full w-full" aria-hidden="true" />
        </motion.div>
      )}
    </AnimatePresence>
  )
}
