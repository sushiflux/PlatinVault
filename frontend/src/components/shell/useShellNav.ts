import { useCallback, useState } from 'react'

// Below this width the game page's tree navigation is a drawer, opened by the menu button in the header (phones, narrow windows)
export const DRAWER_QUERY = '(max-width: 767px)'

// Remembered per browser: the game navigation collapsed to an icon rail
export const RAIL_KEY = 'pv_nav_rail_v1'

function readRail(): boolean {
  try {
    return localStorage.getItem(RAIL_KEY) === '1'
  } catch {
    return false
  }
}

/** Collapsed-to-rail state of the tree navigation, remembered in this browser (storage refused = for this visit only). */
export function useRailState(): [boolean, () => void] {
  const [rail, setRail] = useState(readRail)
  const toggle = useCallback(() => {
    setRail((previous) => {
      const next = !previous
      try {
        localStorage.setItem(RAIL_KEY, next ? '1' : '0')
      } catch {
        // Storage unavailable (private mode): the choice holds for this visit only
      }
      return next
    })
  }, [])
  return [rail, toggle]
}
