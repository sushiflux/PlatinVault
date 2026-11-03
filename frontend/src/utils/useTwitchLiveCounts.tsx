import { createContext, useContext, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchSettings, fetchTwitchLiveCounts } from '../api/client'

/** The live streams of one game: the count, and whether it stopped at its limit ("300+"). */
export interface LiveCount {
  count: number
  atLeast: boolean
}

// The most games one request takes (the server caps it the same way)
export const LIVE_COUNT_BATCH = 120

/**
 * The live-stream counts of the games on the page, for the badge on their Twitch buttons. Only with Twitch credentials and the switch
 * "Live streams" on (Settings > Twitch & IGDB): without them nothing is requested and no card shows a number. One background batch
 * for all the cards; while the server is still looking games up it says so and the batch is asked again every 3 s (what it has done
 * is cached there, so that is cheap); afterwards the counts stay for five minutes. A failure is never shown: a card without a count
 * simply has no badge.
 */
export function useTwitchLiveCounts(games: { appId: number; name: string }[]): Map<number, LiveCount> {
  const { data: settings } = useQuery({ queryKey: ['settings'], queryFn: fetchSettings, staleTime: 60_000 })
  const batch = games.filter((game) => game.appId > 0).slice(0, LIVE_COUNT_BATCH)
  const enabled = Boolean(settings?.hasTwitchKey) && settings?.twitchStreamsEnabled !== false && batch.length > 0
  const key = batch.map((game) => game.appId).sort((a, b) => a - b).join(',')

  const { data } = useQuery({
    queryKey: ['twitch-live-counts', key],
    queryFn: () => fetchTwitchLiveCounts(batch),
    enabled,
    staleTime: 5 * 60_000,
    retry: false,
    refetchInterval: (query) => ((query.state.data?.pending.length ?? 0) > 0 ? 3000 : false),
    // The previous batch stays visible while a changed list (filter, view) is looked up, so badges do not blink off and on
    placeholderData: (previous) => previous,
  })

  const counts = new Map<number, LiveCount>()
  if (enabled) for (const entry of data?.counts ?? []) counts.set(entry.appId, { count: entry.count, atLeast: entry.atLeast })
  return counts
}

const LiveCountContext = createContext<ReadonlyMap<number, LiveCount>>(new Map())

/** Hands the counts of the page to the Twitch buttons of its cards. */
export function LiveCountProvider({ counts, children }: { counts: ReadonlyMap<number, LiveCount>; children: ReactNode }) {
  return <LiveCountContext.Provider value={counts}>{children}</LiveCountContext.Provider>
}

/** The count of one game (undefined = unknown: no badge). */
export function useLiveCount(appId: number | null | undefined): LiveCount | undefined {
  const counts = useContext(LiveCountContext)
  return appId ? counts.get(appId) : undefined
}
