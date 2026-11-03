import type { LiveStatusDto } from '../../api/types'
import type { LaunchTarget } from './LaunchProvider'

/**
 * Is THIS game the one that runs right now? A vault game matches by its slug or by its Steam AppID; a library game (a card of "Alle
 * Spiele" that has no vault entry) only has the AppID. Unknown status (not loaded, error) counts as "not running".
 */
export function isRunningGame(status: LiveStatusDto | undefined, target: LaunchTarget, steamAppId?: number | null): boolean {
  if (!status) return false
  if (target.kind === 'game' && status.runningGameSlug && status.runningGameSlug === target.slug) return true
  const appId = target.kind === 'library' ? target.appId : steamAppId
  return !!appId && status.runningAppId > 0 && status.runningAppId === appId
}
