import { useQuery } from '@tanstack/react-query'
import { fetchSettings } from '../api/client'

// Personal notes on achievements can be switched off in the settings (key "features.achievement_notes", on by default).
// Off hides every trace of them in the UI; the stored notes stay and come back when it is switched on again.
// Same query as the rest of the app, so the answer is normally in the cache before the first trophy list renders.
// `ready` is false only until the first answer (or its final failure) is in: lists wait for it, so nothing flashes and
// nothing shifts once it arrives. A failed load counts as "on" (the default).
export function useAchievementNotes(): { enabled: boolean; ready: boolean } {
  const { data, isPending } = useQuery({ queryKey: ['settings'], queryFn: fetchSettings, staleTime: 60_000 })
  return { enabled: data?.achievementNotesEnabled !== false, ready: !isPending }
}
