import { useEffect } from 'react'
import type { RefObject } from 'react'

/**
 * A choice that does not fit at full length becomes a connected vertical list in the same style, never cut, hidden or scrolled sideways
 * (rule "Visual design"). Watches every `.pv-segmented` below `root`: when its parts overflow, it gets `pv-segmented--stack`; it goes back
 * as soon as the room is there again (window size, a language switch, a changed label). Opt out with `data-no-fit` (a bar that has its own
 * narrow-screen rule, like the settings tabs).
 */
export function useFitSegments(root: RefObject<HTMLElement | null>): void {
  useEffect(() => {
    const element = root.current
    if (!element) return
    let frame = 0
    let disposed = false
    const fit = () => {
      frame = 0
      element.querySelectorAll<HTMLElement>('.pv-segmented').forEach((bar) => {
        if (bar.hasAttribute('data-no-fit')) return
        // Measure the horizontal layout: the vertical one is only ever the fallback
        const wasStacked = bar.classList.contains('pv-segmented--stack')
        if (wasStacked) bar.classList.remove('pv-segmented--stack')
        const cut = bar.scrollWidth > bar.clientWidth + 1
        if (cut) bar.classList.add('pv-segmented--stack')
      })
    }
    const schedule = () => {
      if (!frame && !disposed) frame = window.requestAnimationFrame(fit)
    }
    schedule()
    const resize = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(schedule)
    resize?.observe(element)
    const mutations = new MutationObserver(schedule)
    mutations.observe(element, { childList: true, subtree: true, characterData: true })
    // The labels get wider when the web font arrives after the first measurement: measure again then (the bar keeps its box, so nothing else would tell)
    const fonts = typeof document === 'undefined' ? undefined : document.fonts
    void fonts?.ready?.then(schedule)
    fonts?.addEventListener?.('loadingdone', schedule)
    return () => {
      disposed = true
      if (frame) window.cancelAnimationFrame(frame)
      resize?.disconnect()
      mutations.disconnect()
      fonts?.removeEventListener?.('loadingdone', schedule)
    }
  }, [root])
}
