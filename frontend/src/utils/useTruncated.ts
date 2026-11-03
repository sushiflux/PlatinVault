import { useLayoutEffect, useState, type RefObject } from 'react'

/**
 * True while the element's text is cut off with an ellipsis - on one line (width) or after a line clamp (height) - and re-measured
 * whenever it is resized. Used to show our own tooltip with the full text only where something is actually missing - never a
 * native title tooltip.
 */
export function useTruncated(ref: RefObject<HTMLElement | null>): boolean {
  const [truncated, setTruncated] = useState(false)
  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    const measure = () => setTruncated(el.scrollWidth > el.clientWidth + 1 || el.scrollHeight > el.clientHeight + 1)
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [ref])
  return truncated
}
