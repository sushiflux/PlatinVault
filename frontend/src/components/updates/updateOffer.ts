import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchUpdateStatus } from '../../api/client'
import type { UpdateCheckInterval, UpdateReleaseDto, UpdateStatusDto } from '../../api/types'

// One query key for the whole app: the header icon and Settings > System > Updates read and update the same entry.
export const UPDATE_STATUS_KEY = ['update-status'] as const

// The ids of the setting "Nach Updates suchen" in the order the select shows them (the server validates and decides when GitHub is asked)
export const UPDATE_CHECK_INTERVALS: UpdateCheckInterval[] = ['startup', '4h', 'daily', 'weekly', 'monthly', 'never']

// The SERVER decides when GitHub is asked (the user's interval); the header only re-reads its cached status, which is cheap. So it re-reads
// hourly and when the window comes back after a quarter of an hour - it never forces a GitHub request.
const MINUTE_MS = 60_000
export const UPDATE_POLL_MS = 60 * MINUTE_MS
export const UPDATE_FOCUS_MS = 15 * MINUTE_MS

/**
 * What the header icon may offer. Only a successful check that found something newer for this installation counts
 * (the server already respects the beta switch). Dev builds, a disabled check, "unchecked", up to date and every failure
 * give null: failures stay in the settings section, they never become a red icon.
 */
export function updateOffer(data: UpdateStatusDto | undefined): UpdateReleaseDto | null {
  if (!data || data.state !== 'available') return null
  return data.available ?? null
}

/** Status for the header: loaded at start, then hourly, and again when the window regains focus after a quarter of an hour (always from the server's cache). */
export function useHeaderUpdateStatus() {
  const query = useQuery({
    queryKey: UPDATE_STATUS_KEY,
    queryFn: fetchUpdateStatus,
    retry: false,
    staleTime: UPDATE_POLL_MS,
    refetchInterval: UPDATE_POLL_MS,
    refetchOnWindowFocus: false,
  })
  const { dataUpdatedAt, refetch } = query
  useEffect(() => {
    const onFocus = () => {
      if (dataUpdatedAt > 0 && Date.now() - dataUpdatedAt > UPDATE_FOCUS_MS) void refetch()
    }
    window.addEventListener('focus', onFocus)
    return () => window.removeEventListener('focus', onFocus)
  }, [dataUpdatedAt, refetch])
  return query
}
