import { useLayoutEffect, useState, type RefObject } from 'react'

/**
 * True while the element's text is cut off at its last visible line (line-clamp) - the multi-line twin of useTruncated.
 * Re-measured whenever the element is resized. Used to show our own tooltip with the whole text only where something is missing.
 */
export function useClamped(ref: RefObject<HTMLElement | null>): boolean {
  const [clamped, setClamped] = useState(false)
  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    const measure = () => setClamped(el.scrollHeight > el.clientHeight + 1)
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [ref])
  return clamped
}
