import { useCallback } from 'react'
import type { LiveAchievementItemDto } from '../api/types'
import { useFormat } from '../i18n/format'

// Steam reports 0 / very early timestamps for unlocks it has no time for (the same bound as the statistics charts use)
const EARLIEST_PLAUSIBLE = Date.parse('2008-01-01T00:00:00Z')

/** True when the achievement carries a usable unlock time (present, readable, not one of Steam's placeholder values). */
export function hasUnlockTime(achievement: Pick<LiveAchievementItemDto, 'unlockTime'>): boolean {
  if (!achievement.unlockTime) return false
  const ts = Date.parse(achievement.unlockTime)
  return !Number.isNaN(ts) && ts >= EARLIEST_PLAUSIBLE
}

/**
 * Date and time of the unlock in the language of the app ("04.11.2026, 23:41" / "04/11/2026, 23:41"), or '' when the time is
 * missing or implausible. Used by the trophy popups (card, full screen, in-game overlay).
 */
export function useUnlockStamp(): (achievement: Pick<LiveAchievementItemDto, 'unlockTime'>) => string {
  const fmt = useFormat()
  return useCallback((achievement) => (hasUnlockTime(achievement) ? fmt.date(achievement.unlockTime, 'dateTime') : ''), [fmt])
}
