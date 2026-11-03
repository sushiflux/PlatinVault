import { useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { useQueryClient } from '@tanstack/react-query'
import { removeChapter } from '../../api/client'
import type { GameDetailDto } from '../../api/types'
import { useConfirm } from '../ui/ConfirmDialog'

export interface RemovableChapter {
  id: string
  title: string
  // The chapter only holds the note "could not be researched": nothing but the note is lost, so the question says so
  placeholder?: boolean
}

/**
 * The one flow behind every "Kapitel entfernen" button: our own confirmation (the destructive action, focus on "Abbrechen"), the DELETE, and the
 * refreshed guide in the query cache. Resolves with the guide as it is now, or null when the player cancelled (nothing is sent then). A failed
 * request throws, so the caller can show the reason completely.
 */
export function useRemoveChapter(slug: string) {
  const { t } = useTranslation('ai')
  const confirm = useConfirm()
  const queryClient = useQueryClient()

  return useCallback(
    async (chapter: RemovableChapter): Promise<GameDetailDto | null> => {
      const ok = await confirm(
        chapter.placeholder
          ? {
              title: t('failedChapter.removeTitle', { title: chapter.title }),
              message: t('failedChapter.removeMessage'),
              confirmLabel: t('failedChapter.remove'),
              tone: 'danger',
            }
          : {
              title: t('removeChapter.title', { title: chapter.title }),
              message: (
                <div className="space-y-2">
                  <p>{t('removeChapter.messageWhat')}</p>
                  <p>{t('removeChapter.messageKept')}</p>
                  <p>{t('removeChapter.messageAgain')}</p>
                </div>
              ),
              confirmLabel: t('removeChapter.button'),
              tone: 'danger',
            }
      )
      if (!ok) return null
      const updated = await removeChapter(slug, chapter.id)
      queryClient.setQueryData(['game', slug], updated)
      void queryClient.invalidateQueries({ queryKey: ['games'] })
      return updated
    },
    [confirm, queryClient, slug, t]
  )
}
