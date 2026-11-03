import { useQuery } from '@tanstack/react-query'
import { fetchLiveStatus } from '../api/client'

// A browser cannot tell whether a protocol handler exists, so the rule is deterministic: the Steam client is only
// used when this page runs on the PC itself and the server reports a local Steam installation.
function runsOnThisPc(): boolean {
  return ['localhost', '127.0.0.1', '[::1]', '::1'].includes(window.location.hostname)
}

// True when Steam links can open in the Steam client (steam://openurl/<url>) instead of a browser tab.
export function useOpenInSteamClient(): boolean {
  const { data: liveStatus } = useQuery({ queryKey: ['live-status'], queryFn: fetchLiveStatus, refetchInterval: 5000 })
  return runsOnThisPc() && liveStatus?.localSteamAvailable === true
}

// steam://openurl/ makes the Steam client show the page itself (used by Steam's own store pages)
export function steamClientUrl(url: string): string {
  return `steam://openurl/${url}`
}
