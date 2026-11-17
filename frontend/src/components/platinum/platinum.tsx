import { useEffect, useRef, useState, type CSSProperties, type RefObject } from 'react'
import { useReducedMotion } from 'motion/react'
import './platinum.css'
import { TierIcon } from '../ui/TierIcon'
import { useTranslation } from 'react-i18next'
import type { GameSummaryDto } from '../../api/types'

// A game counts as platinum when its guide checklist is completely ticked.
// (The list endpoint carries no live-achievement completion, so achievements-only games cannot show up here.)
// Platinum = every Steam trophy of the game unlocked (the guide checklists do not count)
export function isPlatinumGame(game: Pick<GameSummaryDto, 'trophiesUnlocked' | 'trophiesTotal'>): boolean {
  return game.trophiesTotal > 0 && game.trophiesUnlocked >= game.trophiesTotal
}

// Platinum frame + glow for a finished game's card on the dashboard (replaces the plain border and shadow)
export const PLATINUM_CARD_FRAME: CSSProperties = {
  border: '1px solid var(--accent-crystal-border)',
  boxShadow: '0 0 0 1px var(--accent-crystal-border), var(--shadow-card), 0 16px 36px -12px rgba(56, 189, 248, 0.55)',
}

// Thin metallic bar along the top edge of a finished game's cover
export function PlatinumEdge() {
  return (
    <span
      aria-hidden="true"
      className="pointer-events-none absolute inset-x-0 top-0 z-10 h-[3px]"
      style={{ background: 'linear-gradient(90deg, #e2e8f0, #7dd3fc 28%, #fef08a 50%, #7dd3fc 72%, #e2e8f0)' }}
    />
  )
}

// "Platin" plaque: same box as the other badges on the card (text-[11px], px-2.5 py-1), in platinum instead of mint green.
// Weight 600 (the global rule has no 800); the text is fixed dark slate on the light metal, so it reads the same in both
// themes. `compact` = the icon alone (the name stays for screen readers).
export function PlatinumChip({ compact = false, animated = false }: { compact?: boolean; animated?: boolean }) {
  const { t } = useTranslation('live')
  return (
    <span
      className={`pv-plat-chip inline-flex items-center gap-1 rounded-lg text-[11px] font-semibold shadow ${compact ? 'p-1' : 'px-2.5 py-1'}`}
      data-animated={animated ? 'true' : undefined}
      style={{
        background: 'linear-gradient(135deg, #f8fafc 0%, #dbeafe 45%, #9fd3f2 100%)',
        color: '#0f172a',
        border: '1px solid rgba(255, 255, 255, 0.8)',
        boxShadow: '0 0 0 1px rgba(56, 189, 248, 0.4), 0 4px 14px -2px rgba(56, 189, 248, 0.6)',
      }}
    >
      <TierIcon tier="Platinum" size={compact ? 12 : 13} inherit />
      {compact ? <span className="sr-only">{t('tiers.Platinum')}</span> : <> {t('tiers.Platinum')}</>}
    </span>
  )
}

// 4-point star (same shape as the sparkle burst of the platinum moment, TierTrophy)
const SPARK_PATH = 'M4 0L4.9 3.1L8 4L4.9 4.9L4 8L3.1 4.9L0 4L3.1 3.1L4 0Z'

function Spark({ className, style }: { className: string; style: CSSProperties }) {
  return (
    <svg viewBox="0 0 8 8" className={className} style={style} aria-hidden="true">
      <path d={SPARK_PATH} fill="currentColor" />
    </svg>
  )
}

// Stable small number from a text (stagger of the effects per tile, so not all tiles fire together)
function hashOf(text: string): number {
  let h = 0
  for (let i = 0; i < text.length; i += 1) h = (h * 31 + text.charCodeAt(i)) >>> 0
  return h
}

// True only while the element is on screen and the document is visible: the effects are paused otherwise (no cost off screen)
function useEffectsActive(ref: RefObject<Element | null>, enabled: boolean): boolean {
  const [inView, setInView] = useState(false)
  const [visible, setVisible] = useState(() => typeof document === 'undefined' || document.visibilityState !== 'hidden')
  useEffect(() => {
    if (!enabled) return
    const el = ref.current
    if (!el) return
    if (typeof IntersectionObserver === 'undefined') {
      setInView(true)
      return
    }
    const observer = new IntersectionObserver((entries) => setInView(entries.some((e) => e.isIntersecting)), { rootMargin: '80px' })
    observer.observe(el)
    return () => observer.disconnect()
  }, [enabled, ref])
  useEffect(() => {
    if (!enabled) return
    const onChange = () => setVisible(document.visibilityState !== 'hidden')
    document.addEventListener('visibilitychange', onChange)
    return () => document.removeEventListener('visibilitychange', onChange)
  }, [enabled])
  return enabled && inView && visible
}

// Positions of the burst stars around the plaque (percent of the cover, top right) and of the twinkles on the gold bar
const BURST = [
  { x: '84%', y: '3%', s: 14 },
  { x: '66%', y: '8%', s: 10 },
  { x: '92%', y: '13%', s: 11 },
  { x: '74%', y: '1%', s: 9 },
  { x: '56%', y: '4%', s: 8 },
]
const TWINKLE = [
  { x: '14%', s: 12 },
  { x: '38%', s: 10 },
  { x: '61%', s: 13 },
  { x: '84%', s: 11 },
]

// What a finished game adds on its cover: the "Platin" plaque top right and an occasional sparkle burst next to it. All of it is
// absolutely positioned, never changes the size of the tile and ignores the pointer. Place it inside the (position: relative)
// cover. Reduced motion: only the plaque. Animations are CSS (transform/opacity) and run only while the cover is on screen and
// the page is visible.
export function PlatinumCoverFx({ seed, compact = false }: { seed: string; compact?: boolean }) {
  const reduce = useReducedMotion() ?? false
  const ref = useRef<HTMLDivElement>(null)
  const active = useEffectsActive(ref, !reduce)
  const hash = hashOf(seed)
  // Negative delays: every tile starts at its own point of the cycle
  const style = { '--pv-fx-phase': `${-((hash % 70) / 10)}s`, '--pv-fx-phase2': `${-((hash % 37) / 10)}s` } as CSSProperties
  return (
    <>
      <div ref={ref} className="pv-plat-fx" aria-hidden="true" data-pv-fx={reduce ? 'static' : 'animated'} data-active={active ? 'true' : undefined} style={style}>
        {!reduce &&
          BURST.map((b, i) => (
            <Spark key={b.x} className="pv-plat-burst pv-plat-anim" style={{ insetInlineStart: b.x, insetBlockStart: b.y, inlineSize: b.s, blockSize: b.s, '--i': i } as CSSProperties} />
          ))}
      </div>
      <div className="pv-plat-chip-pos">
        <PlatinumChip compact={compact} animated={!reduce && active} />
      </div>
    </>
  )
}

// The gold trophy line of a finished game (rendered inside the ProgressBar, over its 3 px track): 5 px gold line with a slow light
// sweep and tiny twinkling stars that may overhang it. Absolutely positioned (the tile keeps its height), pointer-events none,
// aria-hidden. The gold is a per-theme metallic gradient (tokens in platinum.css; darkest stop 5.9:1 against the light card, 4.6:1 against the dark one). Reduced motion: the
// static line only.
export function PlatinumLineFx({ seed }: { seed: string }) {
  const reduce = useReducedMotion() ?? false
  const ref = useRef<HTMLSpanElement>(null)
  const active = useEffectsActive(ref, !reduce)
  const hash = hashOf(seed)
  const style = { '--pv-fx-phase': `${-((hash % 55) / 10)}s`, '--pv-fx-phase2': `${-((hash % 37) / 10)}s` } as CSSProperties
  return (
    <span ref={ref} className="pv-plat-line" aria-hidden="true" data-pv-fx={reduce ? 'static' : 'animated'} data-active={active ? 'true' : undefined} style={style}>
      <span className="pv-plat-line-gold">{!reduce && <span className="pv-plat-bar-shine pv-plat-anim" />}</span>
      {!reduce &&
        TWINKLE.map((tw, i) => (
          <Spark key={tw.x} className="pv-plat-twinkle pv-plat-anim" style={{ insetInlineStart: tw.x, inlineSize: tw.s, blockSize: tw.s, marginBlockStart: -tw.s / 2, '--i': i } as CSSProperties} />
        ))}
    </span>
  )
}
