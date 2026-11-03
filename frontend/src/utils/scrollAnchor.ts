/**
 * Keeps the reading position while the whole page re-flows (UI size change): remembers the content element at a probe line
 * just below the sticky bars and, after the change, scrolls so that the same point of that element sits on the same line
 * again. Call capture() right before the change and restore() right after the DOM has been updated.
 *
 * Proportional on purpose: when text re-wraps the anchor element gets taller or shorter, and "the same relative spot in it"
 * is what the eye follows. At the very top of the page there is nothing to keep, so nothing is done there.
 */
export interface ScrollAnchor {
  restore: () => void
}

const NOOP: ScrollAnchor = { restore: () => undefined }

function stickyBottom(): number {
  let bottom = 0
  // The header, the game page's trophy bar and the dashboard's sub-bar (the side nav is a column, not a bar across the top)
  for (const selector of ['.top-header', '.pv-gbar', '.pv-subbar']) {
    const element = document.querySelector<HTMLElement>(selector)
    if (!element) continue
    const rect = element.getBoundingClientRect()
    if (rect.width > 0 && rect.top <= bottom + 2) bottom = Math.max(bottom, rect.bottom)
  }
  return bottom
}

export function captureScrollAnchor(): ScrollAnchor {
  if (window.scrollY < 2) return NOOP
  const main = document.querySelector('main')
  if (!main) return NOOP

  const below = 16
  const probeY = Math.min(stickyBottom() + below, window.innerHeight - 8)
  const probeX = Math.round(document.documentElement.clientWidth / 2)
  let element = document.elementsFromPoint(probeX, probeY).find((candidate) => main.contains(candidate)) as HTMLElement | undefined
  if (!element) return NOOP
  // A text span is a poor anchor (it may vanish or split on re-wrapping): climb to a block of a reasonable size
  while (element.parentElement && element.parentElement !== main && element.getBoundingClientRect().height < 48) {
    element = element.parentElement
  }

  const rect = element.getBoundingClientRect()
  const ratio = rect.height > 0 ? Math.min(Math.max((probeY - rect.top) / rect.height, 0), 1) : 0
  const anchor = element

  return {
    restore: () => {
      if (!anchor.isConnected) return
      const now = anchor.getBoundingClientRect()
      // The line is "16px below the sticky bars", and the bars themselves change height with the size
      const delta = now.top + ratio * now.height - Math.min(stickyBottom() + below, window.innerHeight - 8)
      if (Math.abs(delta) >= 0.5) window.scrollBy({ top: delta, behavior: 'instant' })
    },
  }
}
