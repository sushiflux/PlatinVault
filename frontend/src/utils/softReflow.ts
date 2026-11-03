import { flushSync } from 'react-dom'
import { captureScrollAnchor } from './scrollAnchor'

type TransitionDocument = Document & {
  startViewTransition?: (update: () => void) => { finished: Promise<unknown> }
}

/**
 * Runs a change that re-flows the whole page (UI size) softly: the scroll position is kept on the same content (see
 * scrollAnchor.ts) and the old and the new layout cross-fade for a moment (View Transitions, index.css .pv-vt-reflow)
 * instead of jumping. Without that API, or with "reduce motion", the change simply happens at once - still anchored.
 * `change` runs inside flushSync, so React has rendered the new state before the new layout is captured.
 */
export function softReflow(change: () => void): void {
  // Captured NOW, from the live page: inside the transition callback the overlay of the old snapshot already covers the page
  // and hit testing (which finds the anchor) only sees the root element
  const anchor = captureScrollAnchor()
  const run = () => {
    flushSync(change)
    anchor.restore()
  }

  const doc = document as TransitionDocument
  const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  if (reduce || !doc.startViewTransition) {
    run()
    return
  }

  const root = document.documentElement
  root.classList.add('pv-vt-reflow')
  const transition = doc.startViewTransition(run)
  void transition.finished.finally(() => root.classList.remove('pv-vt-reflow'))
}
