import { useQuery } from '@tanstack/react-query'
import { fetchSettings } from '../api/client'
import { useIsDesktopHost } from '../components/FooterParts'

/**
 * True when PlatinVault reads Steam on its own computer, so the bridge is not needed: the Windows program (always), or a server that
 * found a Steam folder on the computer it runs on (native Linux package; settings.steamLocal.readsLocally, decided by the server).
 * Unraid/NAS containers stay false - there the bridge brings the live trophies. Not guessed from the host name.
 */
export function useReadsSteamLocally(): boolean {
  const desktopHost = useIsDesktopHost()
  const { data } = useQuery({ queryKey: ['settings'], queryFn: fetchSettings, staleTime: 60_000 })
  return desktopHost || data?.steamLocal?.readsLocally === true
}
