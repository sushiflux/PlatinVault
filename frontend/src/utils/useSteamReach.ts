import { useQuery } from '@tanstack/react-query'
import { fetchLaunchStatus, fetchLiveStatus } from '../api/client'

export interface SteamReach {
  // The bridge program on the gaming PC polled the server within the last seconds (server: <= 12 s)
  bridgeConnected: boolean
  // The server itself runs on the PC that has Steam (a Windows install on the gaming PC needs no bridge)
  local: boolean
  // Anything that needs Steam on the gaming PC (starting a game, "running now" hints) can work right now
  canLaunch: boolean
}

// How often the state is asked again: the bridge counts as gone ~12 s after its last poll, so a button appears or disappears
// within a few seconds without a reload
const REACH_REFETCH_MS = 5000

/**
 * Whether the part of the app that needs the gaming PC can work at all: `bridgeConnected || local`. Everything that needs the
 * bridge or a local Steam (the Play button, "is running now" hints) is shown ONLY while this is true and is hidden otherwise,
 * so the rest of the screen closes up. Unknown (first answer not here yet, server unreachable) counts as "cannot".
 * The queries are shared by key, so any number of components asking costs one request per interval.
 */
export function useSteamReach(): SteamReach {
  const launch = useQuery({ queryKey: ['launch-status'], queryFn: fetchLaunchStatus, refetchInterval: REACH_REFETCH_MS, staleTime: 0 })
  const live = useQuery({ queryKey: ['live-status'], queryFn: fetchLiveStatus, refetchInterval: REACH_REFETCH_MS })
  const bridgeConnected = launch.status !== 'error' && launch.data?.bridgeConnected === true
  const local = live.status !== 'error' && live.data?.localSteamAvailable === true
  return { bridgeConnected, local, canLaunch: bridgeConnected || local }
}
