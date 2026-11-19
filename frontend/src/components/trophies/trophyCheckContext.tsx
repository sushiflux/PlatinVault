import { createContext, useContext } from 'react'
import type { LiveAchievementItemDto } from '../../api/types'
import type { TrophyCheck } from './TrophyCheck'

// How the check of a trophy is found from anywhere on a game page: the page of a game added by hand provides it, so the "Im Blick" cards and
// their detail dialog tick the same list as the trophy tab. A page of a Steam game provides none (Steam reports the unlocks).

export type TrophyCheckFor = (ach: LiveAchievementItemDto) => TrophyCheck | undefined

const TrophyCheckContext = createContext<TrophyCheckFor | null>(null)

export const TrophyCheckProvider = TrophyCheckContext.Provider

/** The check of a trophy for the current game page, or null when the page has none (a Steam game). */
export function useTrophyCheckFor(): TrophyCheckFor | null {
  return useContext(TrophyCheckContext)
}
