import { useLayoutEffect, useRef } from 'react'
import type { CSSProperties } from 'react'

// The thin time bar of a trophy popup: full when the popup appears, gliding smoothly and linearly down to nothing
// exactly when the queue removes it (utils/animationQueue.ts: the popup's whole visible time, slide-in and fade-out included).
// One component for the card, the full-screen popup and the in-game overlay page, which all render the same popup components.
//
// It lies over the popup (absolute, out of the flow), so it never changes the popup's size. Purely decorative: the remaining time
// is not information anybody needs, so it is hidden from screen readers.
//
// Driven both by the CSS @keyframes (`.pv-countdown-bar`) and the Web Animations API (`element.animate`) so the bar glides
// continuously at the display's full refresh rate (60 / 120 / 144 Hz) without whole-second steps or abrupt jumps.

interface PopupCountdownProps {
  // The popup's total visible time; the bar needs exactly this long to run out
  durationMs: number
  // Colour of the bar (the tier's accent colour); the track behind it is the same colour, faint
  color: string
  // Positions the bar inside the popup (the component itself is absolute)
  style?: CSSProperties
}

export function PopupCountdown({ durationMs, color, style }: PopupCountdownProps) {
  const barRef = useRef<HTMLSpanElement | null>(null)

  useLayoutEffect(() => {
    const el = barRef.current
    if (!el || typeof el.animate !== 'function' || durationMs <= 0) return
    const animation = el.animate(
      [{ transform: 'scaleX(1)' }, { transform: 'scaleX(0)' }],
      { duration: durationMs, easing: 'linear', fill: 'both' }
    )
    return () => animation.cancel()
  }, [durationMs])

  return (
    <span
      aria-hidden="true"
      data-popup-countdown=""
      data-duration-ms={durationMs}
      className="pointer-events-none absolute overflow-hidden rounded-full"
      style={{ ...style, background: `${color}33` }}
    >
      <span
        ref={barRef}
        data-popup-countdown-bar=""
        className="pv-countdown-bar block h-full w-full"
        style={
          {
            background: `linear-gradient(90deg, ${color}cc 0%, ${color} 88%, #ffffff 100%)`,
            boxShadow: `0 0 12px ${color}99`,
            transformOrigin: '0% 50%',
            willChange: 'transform',
            '--pv-countdown': `${durationMs}ms`,
          } as CSSProperties
        }
      />
    </span>
  )
}
