import { useQuery } from '@tanstack/react-query'
import { fetchLiveAchievements, fetchLiveStatus } from '../api/client'
import '../styles/live.css'
import { RarityNotice } from './RarityNotice'
import { ALL_SORTS, STEAM_TIER_FILTERS, TrophyListView } from './trophies/TrophyListView'

interface LiveTrackerPanelProps {
  fixedAppId?: number | null
  // How-to per achievement (Steam API name -> tip/condition), taken from the guide's Steam achievement table
  tips?: Record<string, string>
}

// The live trophies tab of a Steam game: it fetches the achievements from Steam (every 3 s while a game runs, else every 15 s) and hands them to
// the list view that every trophy tab shares (components/trophies/TrophyListView.tsx: filters, search, sort, view size, notes, pins, details).
// The game head above (cover, name, status, progress, tiers, refresh) belongs to the page and is shown on every tab.
export function LiveTrackerPanel({ fixedAppId = null, tips = {} }: LiveTrackerPanelProps) {
  const { data: status } = useQuery({
    queryKey: ['live-status'],
    queryFn: fetchLiveStatus,
    refetchInterval: 4000,
  })

  const targetAppId = fixedAppId || status?.runningAppId || undefined

  const { data: liveData, isLoading } = useQuery({
    queryKey: ['live-achievements', targetAppId ?? 'auto'],
    queryFn: () => fetchLiveAchievements(targetAppId),
    refetchInterval: status?.runningAppId ? 3000 : 15000,
  })

  return (
    <TrophyListView
      data={liveData}
      loading={isLoading}
      tips={tips}
      notesScope={liveData ? { appId: liveData.game.appId } : null}
      tierFilters={STEAM_TIER_FILTERS}
      sorts={ALL_SORTS}
      // The rarity values are old or missing: said once, completely, above the list (nothing while they are fine)
      notice={liveData?.rarity && liveData.rarity.state !== 'ok' ? <RarityNotice rarity={liveData.rarity} /> : undefined}
    />
  )
}
