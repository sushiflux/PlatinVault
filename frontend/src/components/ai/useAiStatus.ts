import { useCallback } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { fetchAiStatus } from '../../api/client'

export const AI_STATUS_KEY = ['ai-status'] as const

/**
 * The AI selection and how every provider is doing (quota used up, key rejected, unavailable). Cheap, so it is read again
 * after every AI action and every settings change; the buttons, the chooser and the settings all share this one query.
 */
export function useAiStatus() {
  return useQuery({ queryKey: AI_STATUS_KEY, queryFn: fetchAiStatus, staleTime: 10_000 })
}

/** Call after an AI action (worked or failed): the provider states may have changed. */
export function useRefreshAiStatus(): () => void {
  const queryClient = useQueryClient()
  return useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: AI_STATUS_KEY })
  }, [queryClient])
}
