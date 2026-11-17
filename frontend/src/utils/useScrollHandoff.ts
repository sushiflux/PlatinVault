import { useCallback, useEffect, useRef, useState } from 'react'
import type { RefObject } from 'react'

const FALLBACK_HEADER_PX = 60

function headerHeight(): number {
  // Published by the shell header (exact, never rounded)
  const value = Number.parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--pv-header-h'))
  return Number.isFinite(value) && value > 0 ? value : FALLBACK_HEADER_PX
}

/** off = the row is fully in view (nothing of the copy is there), fading = on its way, on = the row is (nearly) covered and the copy there. */
export type HandoffPhase = 'off' | 'fading' | 'on'

/**
 * The progress of the hand-over from a row to its compact copy: 0 while the row's top is at or below the header's bottom edge, 1 once it
 * has moved up by its own height. Under reduced motion there is no fade: it switches at half way.
 */
export function handoffProgress(rowTop: number, rowHeight: number, headerBottom: number, reduce: boolean): number {
  // A row without a size is not laid out (hidden, or no layout at all): there is nothing to hand over from
  if (!(rowHeight > 0)) return 0
  const p = Math.min(1, Math.max(0, (headerBottom - rowTop) / rowHeight))
  return reduce ? (p >= 0.5 ? 1 : 0) : p
}

/**
 * Scroll-linked hand-over from a row of controls to its compact copy under the header. The progress follows the row's position, not a
 * timer: it starts when the row's top edge reaches the header's bottom edge and ends when the row is fully covered; scrolling back reverses
 * it exactly. One passive scroll and resize listener, throttled with requestAnimationFrame, writes the progress straight to the copy's style
 * (`--pv-cbar-p`, plus `data-moving` while it is between 0 and 1, for `will-change`) - nothing re-renders per frame. React only learns
 * the phase (off / fading / on), which changes a few times per hand-over, to mount the copy and to make it reachable.
 */
export function useScrollHandoff(
  rowRef: RefObject<HTMLElement | null>,
  reduce: boolean
): { barRef: (element: HTMLElement | null) => void; phase: HandoffPhase } {
  const [phase, setPhase] = useState<HandoffPhase>('off')
  const progress = useRef(0)
  const bar = useRef<HTMLElement | null>(null)

  const apply = useCallback(() => {
    const element = bar.current
    if (!element) return
    element.style.setProperty('--pv-cbar-p', String(progress.current))
    element.toggleAttribute('data-moving', progress.current > 0 && progress.current < 1)
  }, [])

  // A copy that mounts in the middle of a hand-over starts at the current progress
  const barRef = useCallback(
    (element: HTMLElement | null) => {
      bar.current = element
      apply()
    },
    [apply]
  )

  useEffect(() => {
    let frame = 0
    const measure = () => {
      frame = 0
      const row = rowRef.current
      if (!row) return
      const rect = row.getBoundingClientRect()
      progress.current = handoffProgress(rect.top, rect.height, headerHeight(), reduce)
      apply()
      setPhase(progress.current <= 0 ? 'off' : progress.current >= 0.5 ? 'on' : 'fading')
    }
    const schedule = () => {
      if (!frame) frame = window.requestAnimationFrame(measure)
    }
    measure()
    window.addEventListener('scroll', schedule, { passive: true })
    window.addEventListener('resize', schedule)
    return () => {
      if (frame) window.cancelAnimationFrame(frame)
      window.removeEventListener('scroll', schedule)
      window.removeEventListener('resize', schedule)
    }
  }, [rowRef, reduce, apply])

  return { barRef, phase }
}
