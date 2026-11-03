import { useEffect, useState, type RefObject } from 'react'

/**
 * True while the target has been scrolled out of view ABOVE the visible area (i.e. the user scrolled down past it).
 * Built on IntersectionObserver, so there is no scroll handler: the browser reports only the moment the element
 * crosses the edge. The edge is the bottom of the sticky header (`.top-header`), because anything behind the header
 * is as good as gone; the observer is rebuilt whenever the header changes height (one/two-row layout).
 */
export function useScrolledPast(target: RefObject<HTMLElement | null>): boolean {
  const [past, setPast] = useState(false)

  useEffect(() => {
    const element = target.current
    if (!element || typeof IntersectionObserver === 'undefined') return

    const header = document.querySelector<HTMLElement>('.top-header')
    let observer: IntersectionObserver | null = null

    const connect = () => {
      observer?.disconnect()
      const offset = Math.ceil(header?.getBoundingClientRect().height ?? 0)
      observer = new IntersectionObserver(
        ([entry]) => {
          if (!entry) return
          // Scrolled past = not visible AND sitting above the (header-shortened) viewport, not below it
          setPast(!entry.isIntersecting && entry.boundingClientRect.bottom <= (entry.rootBounds?.top ?? 0))
        },
        { rootMargin: `-${offset}px 0px 0px 0px` }
      )
      observer.observe(element)
    }

    connect()
    const resizeObserver = header ? new ResizeObserver(connect) : null
    if (header) resizeObserver?.observe(header)

    return () => {
      observer?.disconnect()
      resizeObserver?.disconnect()
    }
  }, [target])

  return past
}
