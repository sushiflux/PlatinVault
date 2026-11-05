import { useEffect, useState } from 'react'
import type { CSSProperties } from 'react'
import { useReducedMotion } from 'motion/react'

// The thin time bar of a trophy popup: full when the popup appears, linear down to nothing exactly when the queue removes it
// (utils/animationQueue.ts: the popup's whole visible time, slide-in and fade-out included). One component for the card, the
// full-screen popup and the in-game overlay page, which all render the same popup components.
//
// It lies over the popup (absolute, out of the flow), so it never changes the popup's size. Purely decorative: the remaining time
// is not information anybody needs, so it is hidden from screen readers.
//
// Reduced motion: no gliding. The bar drops in whole-second steps instead, so the information stays without any movement.

interface PopupCountdownProps {
  // The popup's total visible time; the bar needs exactly this long to run out
  durationMs: number
  // Colour of the bar (the tier's accent colour); the track behind it is the same colour, faint
  color: string
  // Positions the bar inside the popup (the component itself is absolute)
  style?: CSSProperties
}

export function PopupCountdown({ durationMs, color, style }: PopupCountdownProps) {
  const reduce = useReducedMotion() ?? false
  const [steps, setSteps] = useState(0)

  // Reduced motion: one step per second. The count restarts with a new duration (and, through the caller's key, a new popup).
  useEffect(() => {
    setSteps(0)
    if (!reduce) return
    const timer = setInterval(() => setSteps((n) => n + 1), 1000)
    return () => clearInterval(timer)
  }, [reduce, durationMs])

  const remaining = Math.max(0, 1 - (steps * 1000) / durationMs)

  return (
    <span
      aria-hidden="true"
      data-popup-countdown=""
      data-duration-ms={durationMs}
      className="pointer-events-none absolute overflow-hidden rounded-full"
      style={{ ...style, background: `${color}33` }}
    >
      <span
        data-popup-countdown-bar=""
        className={reduce ? 'block h-full w-full' : 'pv-countdown-bar block h-full w-full'}
        style={
          {
            background: color,
            transformOrigin: '0% 50%',
            '--pv-countdown': `${durationMs}ms`,
            ...(reduce ? { transform: `scaleX(${remaining})` } : {}),
          } as CSSProperties
        }
      />
    </span>
  )
}
