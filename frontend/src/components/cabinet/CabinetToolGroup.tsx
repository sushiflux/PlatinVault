import { useCallback, useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { TrophyIcon } from '@phosphor-icons/react'
import { useTranslation } from 'react-i18next'
import { IconButton } from '../ui/IconButton'
import { preloadCovers, showcaseQueryOptions } from './showcaseQuery'
import { TrophyCabinet } from './TrophyCabinet'

/**
 * The trophy cabinet button for the header's tool row, the same on every page: a divider, its own one-button group
 * and the cabinet it opens (rendered into the body). Place it as the last group before the utilities.
 */
export function CabinetToolGroup() {
  const { t } = useTranslation('cabinet')
  const [open, setOpen] = useState(false)
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
  return (
    <>
      <span className="pv-hdivider" aria-hidden="true" />
      <div className="pv-hgroup" role="toolbar" aria-label={t('tool.group')}>
        <IconButton
          tone="gold"
          label={t('tool.label')}
          description={t('tool.description')}
          data-hint="dashboard-cabinet"
          aria-haspopup="dialog"
          onPointerEnter={warm}
          onFocus={warm}
          onClick={() => setOpen(true)}
        >
          {/* Duotone trophy (Phosphor, MIT) - the user picked it over the rosette, 2026-11-02 */}
          <TrophyIcon size={18} weight="duotone" aria-hidden="true" />
        </IconButton>
      </div>
      <TrophyCabinet open={open} onClose={() => setOpen(false)} />
    </>
  )
}
