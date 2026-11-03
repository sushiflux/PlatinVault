import { useId } from 'react'
import { motion } from 'motion/react'

interface CrystalIntroProps {
  // Seconds until the crystal bursts into light (the trophy starts to build right then)
  burstAt: number
}

// Facets of the platinum crystal (100 x 100 box): [points, gradient id suffix]
const FACETS: { points: string; fill: 'a' | 'b' | 'c' | 'd' }[] = [
  { points: '30,22 70,22 58,42 42,42', fill: 'a' },
  { points: '10,42 30,22 42,42', fill: 'b' },
  { points: '90,42 70,22 58,42', fill: 'c' },
  { points: '10,42 42,42 50,86', fill: 'c' },
  { points: '42,42 58,42 50,86', fill: 'a' },
  { points: '58,42 90,42 50,86', fill: 'd' },
]

// Light motes that drift up the column of light: [x, delay in s, size]
const MOTES: [number, number, number][] = [
  [112, 0.1, 2.2],
  [146, 0.28, 1.8],
  [126, 0.46, 2.6],
  [138, 0.62, 1.6],
  [118, 0.78, 2],
  [132, 0.95, 2.4],
]

// Opening of the platinum moment: a faceted platinum crystal rises on a column of light, turns in its own light, charges
// up and bursts - the trophy assembles out of that burst. Everything is timed against `burstAt`.
export function CrystalIntro({ burstAt }: CrystalIntroProps) {
  const uid = useId().replace(/[^a-zA-Z0-9_-]/g, '')
  const id = (name: string) => `pvc-${uid}-${name}`
  const ref = (name: string) => `url(#${id(name)})`
  const total = burstAt + 0.25

  return (
    <div className="pointer-events-none absolute inset-0" aria-hidden="true">
      <svg viewBox="0 0 260 260" className="h-full w-full overflow-visible" fill="none" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <linearGradient id={id('a')} x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#ffffff" />
            <stop offset="1" stopColor="#e0f2fe" />
          </linearGradient>
          <linearGradient id={id('b')} x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#e0f2fe" />
            <stop offset="1" stopColor="#7dd3fc" />
          </linearGradient>
          <linearGradient id={id('c')} x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#bae6fd" />
            <stop offset="1" stopColor="#38bdf8" />
          </linearGradient>
          <linearGradient id={id('d')} x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#7dd3fc" />
            <stop offset="1" stopColor="#0ea5e9" />
          </linearGradient>
          <radialGradient id={id('halo')} cx="0.5" cy="0.5" r="0.5">
            <stop offset="0" stopColor="#e0f2fe" stopOpacity="0.9" />
            <stop offset="0.45" stopColor="#7dd3fc" stopOpacity="0.35" />
            <stop offset="1" stopColor="#7dd3fc" stopOpacity="0" />
          </radialGradient>
          <radialGradient id={id('flash')} cx="0.5" cy="0.5" r="0.5">
            <stop offset="0" stopColor="#ffffff" stopOpacity="1" />
            <stop offset="0.35" stopColor="#e0f2fe" stopOpacity="0.7" />
            <stop offset="1" stopColor="#bae6fd" stopOpacity="0" />
          </radialGradient>
          <linearGradient id={id('beam')} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stopColor="#bae6fd" stopOpacity="0.55" />
            <stop offset="1" stopColor="#7dd3fc" stopOpacity="0" />
          </linearGradient>
        </defs>

        {/* Column of light under the crystal: it rises along it */}
        <motion.rect
          x="108"
          y="130"
          width="44"
          height="160"
          rx="22"
          fill={ref('beam')}
          initial={{ opacity: 0, scaleY: 0.2 }}
          animate={{ opacity: [0, 0.9, 0.9, 0], scaleY: [0.2, 1, 1, 1.2] }}
          transition={{ duration: total, times: [0, 0.3, 0.88, 1], ease: 'easeOut' }}
          style={{ originX: 0.5, originY: 0 }}
        />
        {MOTES.map(([x, delay, size]) => (
          <motion.circle
            key={x}
            cx={x}
            r={size}
            fill="#e0f2fe"
            initial={{ cy: 270, opacity: 0 }}
            animate={{ cy: [270, 150], opacity: [0, 0.9, 0] }}
            transition={{ duration: 1.1, delay, repeat: 1, repeatDelay: 0.15, ease: 'easeOut' }}
          />
        ))}

        {/* Halo of the crystal: grows while it charges */}
        <motion.circle
          cx="130"
          cy="125"
          r="95"
          fill={ref('halo')}
          initial={{ opacity: 0, scale: 0.4 }}
          animate={{ opacity: [0, 0.5, 0.7, 1, 0], scale: [0.4, 0.9, 1, 1.35, 2.2] }}
          transition={{ duration: total, times: [0, 0.4, 0.65, 0.92, 1], ease: 'easeOut' }}
          style={{ originX: 0.5, originY: 0.5 }}
        />

        {/* The crystal: rises, hovers while turning, charges and bursts */}
        <motion.g
          initial={{ y: 150, opacity: 0, scale: 0.8 }}
          animate={{
            y: [150, 0, -5, 0, 0],
            opacity: [0, 1, 1, 1, 0],
            scale: [0.8, 1, 1.02, 1.2, 1.9],
          }}
          transition={{ duration: total, times: [0, 0.42, 0.62, 0.9, 1], ease: 'easeInOut' }}
          style={{ originX: 0.5, originY: 0.5 }}
        >
          <g transform="translate(82.5 77.5) scale(0.95)">
            {/* The crystal turns around its vertical axis: its width swings, the facets slide past each other */}
            <motion.g
              animate={{ scaleX: [1, 0.42, 1, 0.42, 1] }}
              transition={{ duration: 2.6, repeat: Infinity, ease: 'easeInOut' }}
              style={{ originX: 0.5, originY: 0.5 }}
            >
              {FACETS.map((facet) => (
                <polygon
                  key={facet.points}
                  points={facet.points}
                  fill={ref(facet.fill)}
                  stroke="#ffffff"
                  strokeOpacity="0.7"
                  strokeWidth="1"
                  strokeLinejoin="round"
                />
              ))}
              <path d="M10 42H90" stroke="#ffffff" strokeOpacity="0.85" strokeWidth="1.2" />
              <path d="M30 22L42 42L50 86" stroke="#ffffff" strokeOpacity="0.4" strokeWidth="0.8" />
            </motion.g>
          </g>
        </motion.g>

        {/* Soft flash at the moment of the burst */}
        <motion.circle
          cx="130"
          cy="125"
          r="120"
          fill={ref('flash')}
          initial={{ opacity: 0, scale: 0.2 }}
          animate={{ opacity: [0, 1, 0], scale: [0.2, 1.1, 1.7] }}
          transition={{ duration: 0.6, delay: burstAt - 0.1, ease: 'easeOut' }}
          style={{ originX: 0.5, originY: 0.5 }}
        />
      </svg>
    </div>
  )
}
