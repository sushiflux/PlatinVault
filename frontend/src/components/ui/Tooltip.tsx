import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { motion, useReducedMotion } from 'motion/react'

interface TooltipProps {
  // Short name of the action; also what a screen reader says (set it as aria-label on the child as well)
  label: string
  // Optional second line with more detail
  description?: string
  // Preferred side; it flips by itself when there is no room
  side?: 'top' | 'bottom'
  // Milliseconds of hovering before the tooltip appears
  delay?: number
  // Switches the tooltip off without unmounting the child (e.g. a title that only needs it while it is cut off)
  disabled?: boolean
  // Classes of the wrapper around the child (default: inline-flex)
  className?: string
  children: ReactNode
}

const GAP = 10
const MARGIN = 8

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), Math.max(min, max))
}

// The tooltip itself: lives in <body> (never inside the layout), is measured once and then placed next to its anchor,
// flipped to the other side or pushed sideways so it always stays inside the viewport.
function Bubble({
  anchor,
  label,
  description,
  side,
}: {
  anchor: HTMLElement
  label: string
  description?: string
  side: 'top' | 'bottom'
}) {
  const reduce = useReducedMotion() ?? false
  const ref = useRef<HTMLDivElement>(null)
  const [placement, setPlacement] = useState<{ left: number; top: number; side: 'top' | 'bottom'; arrow: number } | null>(null)

  useLayoutEffect(() => {
    const bubble = ref.current
    if (!bubble) return
    const a = anchor.getBoundingClientRect()
    const b = bubble.getBoundingClientRect()
    const viewportWidth = document.documentElement.clientWidth
    const viewportHeight = window.innerHeight

    let chosen = side
    const fitsBelow = a.bottom + GAP + b.height <= viewportHeight - MARGIN
    const fitsAbove = a.top - GAP - b.height >= MARGIN
    if (chosen === 'bottom' && !fitsBelow && fitsAbove) chosen = 'top'
    else if (chosen === 'top' && !fitsAbove && fitsBelow) chosen = 'bottom'

    const center = a.left + a.width / 2
    const left = clamp(center - b.width / 2, MARGIN, viewportWidth - b.width - MARGIN)
    const top = chosen === 'bottom' ? a.bottom + GAP : a.top - GAP - b.height
    setPlacement({ left, top, side: chosen, arrow: clamp(center - left, 14, b.width - 14) })
  }, [anchor, label, description, side])

  const shown = placement?.side ?? side
  return createPortal(
    <motion.div
      ref={ref}
      aria-hidden="true"
      className="pv-tooltip"
      style={{
        left: placement?.left ?? 0,
        top: placement?.top ?? 0,
        visibility: placement ? 'visible' : 'hidden',
      }}
      initial={reduce ? { opacity: 0 } : { opacity: 0, scale: 0.96, y: shown === 'bottom' ? -4 : 4 }}
      animate={reduce ? { opacity: 1 } : { opacity: 1, scale: 1, y: 0 }}
      transition={{ duration: 0.14, ease: 'easeOut' }}
    >
      <span className="pv-tooltip-label">{label}</span>
      {description && <span className="pv-tooltip-description">{description}</span>}
      <span
        className="pv-tooltip-arrow"
        style={{
          left: (placement?.arrow ?? 14) - 4,
          [shown === 'bottom' ? 'top' : 'bottom']: -4,
        }}
      />
    </motion.div>,
    document.body
  )
}

// Our own tooltip for icon buttons: appears after a short hover delay (mouse only) or on keyboard focus, closes on
// Esc, click, scroll and when the pointer or focus leaves. Touch devices rely on the aria-label of the control instead.
export function Tooltip({ label, description, side = 'bottom', delay = 350, disabled = false, className = 'inline-flex', children }: TooltipProps) {
  const anchorRef = useRef<HTMLSpanElement>(null)
  const timerRef = useRef<number | undefined>(undefined)
  const [open, setOpen] = useState(false)

  const hide = () => {
    window.clearTimeout(timerRef.current)
    setOpen(false)
  }
  const showAfter = (ms: number) => {
    window.clearTimeout(timerRef.current)
    if (disabled) return
    timerRef.current = window.setTimeout(() => setOpen(true), ms)
  }

  useEffect(() => () => window.clearTimeout(timerRef.current), [])

  useEffect(() => {
    if (!open) return
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') hide()
    }
    window.addEventListener('keydown', onKeyDown)
    window.addEventListener('scroll', hide, true)
    window.addEventListener('resize', hide)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('scroll', hide, true)
      window.removeEventListener('resize', hide)
    }
  }, [open])

  return (
    <span
      ref={anchorRef}
      className={className}
      onPointerEnter={(e) => {
        if (e.pointerType === 'mouse') showAfter(delay)
      }}
      onPointerLeave={hide}
      onPointerDown={hide}
      onFocus={(e) => {
        // Only keyboard focus: a mouse click also focuses the button but must not pop the tooltip up
        if (e.target instanceof HTMLElement && e.target.matches(':focus-visible')) showAfter(120)
      }}
      onBlur={hide}
    >
      {children}
      {open && anchorRef.current && (
        <Bubble anchor={anchorRef.current} label={label} description={description} side={side} />
      )}
    </span>
  )
}
