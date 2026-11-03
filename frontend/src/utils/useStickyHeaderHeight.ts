import { useCallback, useEffect, useRef } from 'react'
import { UI_SCALE_APPLIED_EVENT } from './uiScale'

// Publishes the real height of a sticky header as --pv-header-h, so sticky navigation below it (top bar or sidebar)
// starts exactly under the header - the header wraps to several lines on narrow screens. Returns a callback ref,
// because the header may mount later than the page (loading state first).
// The height is EXACT (fractional at 80 %, 110 % ...), not rounded: a bar that sticks at exactly the header's lower edge has
// the same fractional edge as the header, so the browser snaps both to the same device pixel - no hairline gap (the old ceil left
// up to 1px of page showing between them) - and it sits where it sat before it stuck, so nothing shifts when it starts to stick.
export function useStickyHeaderHeight<T extends HTMLElement>() {
  const cleanupRef = useRef<(() => void) | null>(null)

  const ref = useCallback((el: T | null) => {
    cleanupRef.current?.()
    cleanupRef.current = null
    if (!el) return
    const update = () =>
      document.documentElement.style.setProperty('--pv-header-h', `${el.getBoundingClientRect().height}px`)
    update()
    const observer = new ResizeObserver(update)
    observer.observe(el)
    // The UI size changed: measure now, not on the next resize-observer round (one frame later)
    window.addEventListener(UI_SCALE_APPLIED_EVENT, update)
    cleanupRef.current = () => {
      observer.disconnect()
      window.removeEventListener(UI_SCALE_APPLIED_EVENT, update)
    }
  }, [])

  useEffect(
    () => () => {
      cleanupRef.current?.()
      cleanupRef.current = null
      document.documentElement.style.removeProperty('--pv-header-h')
    },
    []
  )

  return ref
}
