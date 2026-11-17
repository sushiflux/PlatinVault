import { useCallback, useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { TrophyIcon } from '@phosphor-icons/react'
import { useTranslation } from 'react-i18next'
import { useFlagParam } from '../../utils/useDialogParam'
import { IconButton } from '../ui/IconButton'
import { preloadCovers, showcaseQueryOptions } from './showcaseQuery'
import { TrophyCabinet } from './TrophyCabinet'

/**
 * The trophy cabinet button of the top bar, the same on every page, and the full-screen cabinet it opens (rendered into the body). The open
 * state lives in the address as ?cabinet=1 on the current page, so a reload keeps the cabinet open and Back closes it.
 */
export function CabinetButton() {
  const { t } = useTranslation('cabinet')
  const [open, setOpen] = useFlagParam('cabinet')
  const queryClient = useQueryClient()

  // Warm the cabinet up: the data (and the first covers) are fetched before the button is even clicked. prefetchQuery does
  // nothing while the data is still fresh, so calling it often costs nothing.
  const warm = useCallback(() => {
    void queryClient.prefetchQuery(showcaseQueryOptions).then(() => {
      const data = queryClient.getQueryData(showcaseQueryOptions.queryKey)
      if (data) void preloadCovers(data.games, 4000, 24)
    })
  }, [queryClient])

  // Once on idle after the page has loaded
  useEffect(() => {
    const idle = window as Window & {
      requestIdleCallback?: (callback: () => void, options?: { timeout: number }) => number
      cancelIdleCallback?: (handle: number) => void
    }
    if (idle.requestIdleCallback) {
      const handle = idle.requestIdleCallback(warm, { timeout: 4000 })
      return () => idle.cancelIdleCallback?.(handle)
    }
    const timer = window.setTimeout(warm, 2000)
    return () => window.clearTimeout(timer)
  }, [warm])

  const close = useCallback(() => {
    setOpen(false)
    // The focus goes back to the button that opened the cabinet
    window.setTimeout(() => document.querySelector<HTMLElement>('[data-hint="dashboard-cabinet"]')?.focus(), 0)
  }, [setOpen])

  return (
    <>
      <IconButton
        tone="gold"
        label={t('tool.label')}
        description={t('tool.description')}
        data-hint="dashboard-cabinet"
        aria-haspopup="dialog"
        aria-expanded={open}
        onPointerEnter={warm}
        onFocus={warm}
        onClick={() => setOpen(true)}
      >
        {/* Duotone trophy (Phosphor, MIT) - the user picked it over the rosette, 2026-11-02 */}
        <TrophyIcon size={20} weight="duotone" aria-hidden="true" />
      </IconButton>
      <TrophyCabinet open={open} onClose={close} />
    </>
  )
}
