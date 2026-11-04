import { useQuery } from '@tanstack/react-query'
import { fetchSettings } from '../api/client'
import { popupSecondsToMs } from './popupSeconds'

/**
 * The trophy popup duration in milliseconds from the shared settings query (the same one the rest of the app reads): 5000 until the
 * settings have loaded or when the server is older and does not send the field.
 */
export function usePopupMs(): number {
  const { data } = useQuery({ queryKey: ['settings'], queryFn: fetchSettings, staleTime: 60_000 })
  return popupSecondsToMs(data?.popupSeconds)
}
