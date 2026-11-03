import { useCallback, useEffect, useLayoutEffect, useRef, useState, type RefObject } from 'react'
import { motion } from 'motion/react'

type Mode = 'underline' | 'bar' | 'pill'

interface Box {
  x: number
  y: number
  width: number
  height: number
}

interface SlidingMarkerProps {
  // The tab list: it must be `position: relative` - the marker lives inside it and is positioned relative to it
  containerRef: RefObject<HTMLElement | null>
  // Identity of the active tab; a change slides the marker, null hides it
  activeKey: string | null
  // underline = follows the active tab sideways (width), bar = follows it up and down (height), pill = the whole box
  mode: Mode
  className: string
  reduce: boolean
}

const SPRING = { type: 'spring', stiffness: 520, damping: 40, mass: 0.8 } as const

function sameBox(a: Box | null, b: Box | null): boolean {
  if (a === null || b === null) return a === b
  return Math.abs(a.x - b.x) < 0.25 && Math.abs(a.y - b.y) < 0.25 && Math.abs(a.width - b.width) < 0.25 && Math.abs(a.height - b.height) < 0.25
}

/**
 * The sliding active marker of a tab list (chapter underline, sidebar bar, section pill).
 *
 * It is positioned from the tab's place INSIDE the list (the difference of two rects, so scrolling, a sticky bar or a page that
 * changes height under it never matters) and animates only that: x/width for a row, y/height for a column. The earlier shared
 * layout animation (layoutId) measured page positions, so a scroll jump on a tab change made it fly in from below. The marker
 * slides only when the active tab changes and was visible before; a resize, a new language or a UI size change moves it
 * instantly, and with "reduce motion" it never slides.
 */
export function SlidingMarker({ containerRef, activeKey, mode, className, reduce }: SlidingMarkerProps) {
  // The last place the marker had stays while it is hidden, so it never travels from nowhere when it comes back
  const [state, setState] = useState<{ box: Box | null; shown: boolean; slide: boolean }>({ box: null, shown: false, slide: false })
  const lastKey = useRef<string | null>(null)
  const wasShown = useRef(false)

  const measure = useCallback((): Box | null => {
    const container = containerRef.current
    if (!container || activeKey === null) return null
    const tab = container.querySelector<HTMLElement>('[role="tab"][aria-selected="true"]')
    if (!tab) return null
    const c = container.getBoundingClientRect()
    const r = tab.getBoundingClientRect()
    // + scroll offsets: the marker is positioned inside the list's content, so it scrolls along with the tabs
    let box: Box = {
      x: r.left - c.left - container.clientLeft + container.scrollLeft,
      y: r.top - c.top - container.clientTop + container.scrollTop,
      width: r.width,
      height: r.height,
    }
    if (mode === 'bar') {
      // Inset from the top and bottom of the tab (0.5rem), a bar and not a full-height rule
      const inset = Number.parseFloat(getComputedStyle(document.documentElement).fontSize) * 0.5
      box = { ...box, y: box.y + inset, height: Math.max(0, box.height - 2 * inset) }
    }
    return box
  }, [containerRef, activeKey, mode])

  // After every render of the tab list (the active tab changed, tabs were added, a counter got wider ...)
  useLayoutEffect(() => {
    const box = measure()
    const keyChanged = activeKey !== lastKey.current
    lastKey.current = activeKey
    const slide = keyChanged && box !== null && wasShown.current
    wasShown.current = box !== null
    setState((previous) => {
      const keepBox = box ?? previous.box
      if (sameBox(previous.box, keepBox) && previous.shown === (box !== null)) return previous
      return { box: keepBox, shown: box !== null, slide }
    })
  })

  // A resize, a language or UI size change moves the tabs without a render of the list: follow them at once
  useEffect(() => {
    const container = containerRef.current
    if (!container) return
    const observer = new ResizeObserver(() => {
      const box = measure()
      setState((previous) => (box === null || sameBox(previous.box, box) ? previous : { box, shown: true, slide: false }))
    })
    observer.observe(container)
    return () => observer.disconnect()
  }, [containerRef, measure])

  const { box, shown, slide } = state
  const animate =
    mode === 'underline'
      ? { x: box?.x ?? 0, width: box?.width ?? 0, opacity: shown ? 1 : 0 }
      : mode === 'bar'
        ? { x: box?.x ?? 0, y: box?.y ?? 0, height: box?.height ?? 0, opacity: shown ? 1 : 0 }
        : { x: box?.x ?? 0, y: box?.y ?? 0, width: box?.width ?? 0, height: box?.height ?? 0, opacity: shown ? 1 : 0 }
  const move = slide && !reduce ? SPRING : { duration: 0 }

  return <motion.span aria-hidden="true" className={className} initial={false} animate={animate} transition={{ ...move, opacity: { duration: reduce ? 0 : 0.12 } }} />
}
