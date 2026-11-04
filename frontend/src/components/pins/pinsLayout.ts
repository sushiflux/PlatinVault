import { useCallback, useState } from 'react'
import { useMediaQuery } from '../../utils/useMediaQuery'

// Landscape AND wide enough for a column beside the chapter navigation and the content: the column on the right (mirror of the chapter
// navigation). Everything else - portrait, a tablet held upright, a phone, a narrow window - gets the dock at the bottom.
export const PINS_COLUMN_QUERY = '(orientation: landscape) and (min-width: 1280px)'

// Phone width (the same breakpoint as the chapter drawer): there the dock starts collapsed to one line
export const PINS_PHONE_QUERY = '(max-width: 767px)'

export type PinsPlacement = 'column' | 'dock'

export function usePinsPlacement(): PinsPlacement {
  return useMediaQuery(PINS_COLUMN_QUERY) ? 'column' : 'dock'
}

// Remembered per browser and per layout: the dock and the column are collapsed independently
export const PINS_COLLAPSED_KEY = 'pv_pins_collapsed_v1'

type CollapsedState = Partial<Record<PinsPlacement, boolean>>

function readCollapsed(): CollapsedState {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(PINS_COLLAPSED_KEY) ?? '{}')
    if (!parsed || typeof parsed !== 'object') return {}
    // Only what was chosen is stored: a layout the player never touched keeps its default
    const { dock, column } = parsed as Record<string, unknown>
    const state: CollapsedState = {}
    if (typeof dock === 'boolean') state.dock = dock
    if (typeof column === 'boolean') state.column = column
    return state
  } catch {
    return {}
  }
}

function writeCollapsed(state: CollapsedState) {
  try {
    localStorage.setItem(PINS_COLLAPSED_KEY, JSON.stringify(state))
  } catch {
    // Storage unavailable (private mode): the choice holds for this visit only
  }
}

/**
 * Collapsed or expanded for one layout, remembered in localStorage once the player has chosen. Default: expanded - except the dock on a phone
 * (below 768 px), which starts as one slim line.
 */
export function usePinsCollapsed(placement: PinsPlacement): [boolean, () => void] {
  const [state, setState] = useState<CollapsedState>(readCollapsed)
  const phone = useMediaQuery(PINS_PHONE_QUERY)
  const fallback = placement === 'dock' && phone
  const collapsed = state[placement] ?? fallback
  const toggle = useCallback(() => {
    setState((previous) => {
      const next = { ...previous, [placement]: !(previous[placement] ?? fallback) }
      writeCollapsed(next)
      return next
    })
  }, [placement, fallback])
  return [collapsed, toggle]
}
