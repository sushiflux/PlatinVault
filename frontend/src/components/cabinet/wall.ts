// The cabinet as one wall: the shelves are rows of the same grid, and when there are more games than fit the width, the whole
// wall (all shelves together) scrolls sideways. Pure helpers for the layout and for the arrow state; the component wires them
// to the scroll container.

export interface WallLayout {
  // Columns of the wall (at least the number that fits the case, more when there are more games than places)
  columns: number
  rows: number
}

/**
 * Games are laid out row by row (shelf by shelf, newest first). The number of shelves follows the height of the case; when
 * the games do not fit `rows * visibleColumns`, the shelves get longer (sideways scrolling) instead of the wall growing in
 * height - so the size setting only changes how many places are visible at once, never whether everything can be reached.
 */
export function wallLayout(count: number, visibleColumns: number, rows: number): WallLayout {
  const safeRows = Math.max(1, rows)
  const safeVisible = Math.max(1, visibleColumns)
  return { columns: Math.max(safeVisible, Math.ceil(Math.max(0, count) / safeRows)), rows: safeRows }
}

export interface WallEdges {
  // More places than the case shows: the arrows and edge fades make sense at all
  overflow: boolean
  canPrev: boolean
  canNext: boolean
}

// Sub-pixel rounding of a scrolled wall must not leave an arrow enabled that does nothing
const EDGE_TOLERANCE = 2

export function wallEdges(scrollLeft: number, clientWidth: number, scrollWidth: number): WallEdges {
  const max = scrollWidth - clientWidth
  const overflow = max > EDGE_TOLERANCE
  return {
    overflow,
    canPrev: overflow && scrollLeft > EDGE_TOLERANCE,
    canNext: overflow && scrollLeft < max - EDGE_TOLERANCE,
  }
}

export function sameEdges(a: WallEdges, b: WallEdges): boolean {
  return a.overflow === b.overflow && a.canPrev === b.canPrev && a.canNext === b.canNext
}

/**
 * Where one click on an arrow scrolls to: about one page of columns (one column less than visible, so the last place stays in
 * view as a landmark), landing exactly on a column start so no game is ever stopped half cut, kept inside the wall.
 */
export function pageScrollTarget(
  scrollLeft: number,
  clientWidth: number,
  scrollWidth: number,
  visibleColumns: number,
  direction: -1 | 1
): number {
  const columns = Math.max(1, visibleColumns)
  const columnWidth = clientWidth / columns
  const max = Math.max(0, scrollWidth - clientWidth)
  if (columnWidth <= 0) return Math.min(max, Math.max(0, scrollLeft))
  const page = Math.max(1, columns - 1) * columnWidth
  const snapped = Math.round((scrollLeft + direction * page) / columnWidth) * columnWidth
  return Math.min(max, Math.max(0, Math.round(snapped)))
}

/** Scroll position that brings a place (its left edge, measured in the scrolled content) into view with one column of context before it. */
export function revealScrollTarget(contentX: number, clientWidth: number, scrollWidth: number, visibleColumns: number): number {
  const columnWidth = clientWidth / Math.max(1, visibleColumns)
  const max = Math.max(0, scrollWidth - clientWidth)
  if (columnWidth <= 0) return 0
  return Math.min(max, Math.max(0, Math.round(Math.round((contentX - columnWidth) / columnWidth) * columnWidth)))
}

/** True when a place lies completely inside the visible part of the scroller (rects in the same coordinate space). */
export function isFullyVisible(item: { left: number; right: number }, view: { left: number; right: number }): boolean {
  return item.left >= view.left - 1 && item.right <= view.right + 1
}
