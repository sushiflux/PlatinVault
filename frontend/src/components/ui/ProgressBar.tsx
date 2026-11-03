import { useId, type CSSProperties } from 'react'
import { IconTrophyFilled } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'

/** Deterministic 0..4.7 s offset so many complete bars do not shimmer in lockstep. */
function seedFrom(id: string): number {
  let hash = 0
  for (let i = 0; i < id.length; i++) hash = (hash * 31 + id.charCodeAt(i)) % 997
  return (hash % 48) / 10
}

interface Spark {
  /** Horizontal position along the bar (CSS length / percent of the bar width). */
  x: string
  /** Vertical offset from the bar's centre line; 0 = on the bar, negative = above, positive = below. */
  y: string
  /** Size of the star in px. */
  s: number
  /** Start delay and length of one twinkle cycle in seconds (staggered so only a few shine at once). */
  d: number
  dur: number
  /** Stays visible as a static glint when the user prefers reduced motion. */
  keep?: boolean
}

/** Glints on the bar and around it. Everything is an overlay: no entry changes the bar's size or pushes anything. */
const SPARKS: ReadonlyArray<Spark> = [
  // On the bar
  { x: '11%', y: '0px', s: 13, d: 0.0, dur: 3.4 },
  { x: '29%', y: '0px', s: 17, d: 1.2, dur: 3.8, keep: true },
  { x: '47%', y: '0px', s: 12, d: 2.3, dur: 3.3 },
  { x: '66%', y: '0px', s: 18, d: 0.7, dur: 4.0, keep: true },
  { x: '84%', y: '0px', s: 13, d: 1.8, dur: 3.5 },
  // Around: above, below, and at the ends
  { x: '19%', y: '-9px', s: 11, d: 0.4, dur: 3.6 },
  { x: '57%', y: '-10px', s: 12, d: 1.6, dur: 4.2, keep: true },
  { x: '38%', y: '9px', s: 11, d: 2.6, dur: 3.9 },
  { x: '76%', y: '10px', s: 12, d: 0.9, dur: 3.7, keep: true },
  { x: 'calc(100% - 7px)', y: '-12px', s: 11, d: 2.0, dur: 3.5 },
  { x: '3%', y: '8px', s: 10, d: 3.0, dur: 4.1 },
]

interface ProgressBarProps {
  /** 0..100; values outside are clamped. */
  percent: number
  /** Accessible name; the visible numbers next to the bar usually carry the details. */
  label?: string
  /** Layout hooks only (margins, flex); the look comes from index.css. */
  className?: string
}

/**
 * The shared trophy progress bar. The gradient lives on the track and the fill only reveals it, so the bar turns
 * warmer the closer it gets to 100 %. At 100 % it becomes polished gold with a sheen, a few glints and a small
 * rosette pin laid over the end of the bar (absolutely positioned: it never takes layout space).
 * All the styling is in index.css (`.pv-pbar`), `prefers-reduced-motion` keeps the static gold and the pin.
 */
export function ProgressBar({ percent, label, className }: ProgressBarProps) {
  const { t } = useTranslation('game')
  const id = useId()
  const value = Number.isFinite(percent) ? Math.min(100, Math.max(0, percent)) : 0
  const complete = value >= 100
  const tier = complete ? 4 : value >= 75 ? 3 : value >= 50 ? 2 : value >= 25 ? 1 : 0

  return (
    <div
      className={className ? `pv-pbar ${className}` : 'pv-pbar'}
      role="progressbar"
      aria-label={label ?? t('progressBar.label')}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(value)}
      data-tier={tier}
      data-complete={complete ? 'true' : undefined}
      style={{ '--p': value, '--pv-seed': complete ? seedFrom(id) : 0 } as CSSProperties}
    >
      <div className="progress-bar-track">
        <div className="progress-bar-fill" />
      </div>
      {complete && (
        <>
          {SPARKS.map((spark) => (
            <span
              key={`${spark.x}/${spark.y}`}
              className={spark.keep ? 'pv-pbar-spark pv-pbar-spark--keep' : 'pv-pbar-spark'}
              aria-hidden="true"
              style={
                {
                  '--x': spark.x,
                  '--y': spark.y,
                  '--s': `${spark.s}px`,
                  '--d': `${spark.d}s`,
                  '--dur': `${spark.dur}s`,
                } as CSSProperties
              }
            />
          ))}
          <span className="pv-pbar-pin" aria-hidden="true">
            <span className="pv-pbar-pin__rim">
              <span className="pv-pbar-pin__disc">
                {/* The same trophy cup as every tier icon (TierIcon), never a crown - user decision 2026-11-02 */}
                <IconTrophyFilled focusable="false" />
              </span>
            </span>
          </span>
        </>
      )}
    </div>
  )
}
