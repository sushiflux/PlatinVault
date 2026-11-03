import { describe, expect, it } from 'vitest'
import { isFullyVisible, pageScrollTarget, revealScrollTarget, sameEdges, wallEdges, wallLayout } from './wall'

describe('wallLayout', () => {
  it('keeps the case width while everything fits (no sideways scrolling, same as before)', () => {
    expect(wallLayout(18, 10, 3)).toEqual({ columns: 10, rows: 3 })
    expect(wallLayout(30, 10, 3)).toEqual({ columns: 10, rows: 3 })
    expect(wallLayout(0, 10, 3)).toEqual({ columns: 10, rows: 3 })
  })

  it('makes the shelves longer instead of the wall taller when there are more games than places', () => {
    expect(wallLayout(31, 10, 3)).toEqual({ columns: 11, rows: 3 })
    expect(wallLayout(40, 10, 3)).toEqual({ columns: 14, rows: 3 })
    expect(wallLayout(40, 4, 2)).toEqual({ columns: 20, rows: 2 })
  })

  it('survives silly input', () => {
    expect(wallLayout(5, 0, 0)).toEqual({ columns: 5, rows: 1 })
    expect(wallLayout(-3, 3, 2)).toEqual({ columns: 3, rows: 2 })
  })
})

describe('wallEdges', () => {
  it('has no arrows at all when everything fits', () => {
    expect(wallEdges(0, 1000, 1000)).toEqual({ overflow: false, canPrev: false, canNext: false })
    // a rounding sliver is not an overflow
    expect(wallEdges(0, 1000, 1001)).toEqual({ overflow: false, canPrev: false, canNext: false })
  })

  it('enables only the direction that has more', () => {
    expect(wallEdges(0, 1000, 2000)).toEqual({ overflow: true, canPrev: false, canNext: true })
    expect(wallEdges(400, 1000, 2000)).toEqual({ overflow: true, canPrev: true, canNext: true })
    expect(wallEdges(1000, 1000, 2000)).toEqual({ overflow: true, canPrev: true, canNext: false })
  })

  it('treats a fraction of a pixel before the end as the end', () => {
    expect(wallEdges(999.2, 1000, 2000).canNext).toBe(false)
    expect(wallEdges(0.6, 1000, 2000).canPrev).toBe(false)
  })

  it('compares states', () => {
    expect(sameEdges(wallEdges(0, 10, 100), wallEdges(0, 10, 100))).toBe(true)
    expect(sameEdges(wallEdges(0, 10, 100), wallEdges(50, 10, 100))).toBe(false)
  })
})

describe('pageScrollTarget', () => {
  // 10 columns visible of 100 px each, a wall of 25 columns
  const view = 1000
  const wall = 2500

  it('scrolls about one page (one column less than visible) and lands on a column start', () => {
    expect(pageScrollTarget(0, view, wall, 10, 1)).toBe(900)
    expect(pageScrollTarget(900, view, wall, 10, 1)).toBe(1500)
    expect(pageScrollTarget(900, view, wall, 10, -1)).toBe(0)
  })

  it('snaps a position that is between two columns', () => {
    expect(pageScrollTarget(430, view, wall, 10, 1)).toBe(1300)
  })

  it('never leaves the wall', () => {
    expect(pageScrollTarget(1400, view, wall, 10, 1)).toBe(1500)
    expect(pageScrollTarget(100, view, wall, 10, -1)).toBe(0)
  })

  it('moves at least one column even when only one place is visible', () => {
    expect(pageScrollTarget(0, 100, 500, 1, 1)).toBe(100)
  })
})

describe('revealScrollTarget / isFullyVisible', () => {
  it('shows a place with one column of context in front of it', () => {
    expect(revealScrollTarget(1500, 1000, 2500, 10)).toBe(1400)
    expect(revealScrollTarget(50, 1000, 2500, 10)).toBe(0)
    expect(revealScrollTarget(2400, 1000, 2500, 10)).toBe(1500)
  })

  it('knows when a place is only partly in view', () => {
    const view = { left: 0, right: 1000 }
    expect(isFullyVisible({ left: 100, right: 200 }, view)).toBe(true)
    expect(isFullyVisible({ left: 950, right: 1050 }, view)).toBe(false)
    expect(isFullyVisible({ left: -40, right: 60 }, view)).toBe(false)
  })
})
