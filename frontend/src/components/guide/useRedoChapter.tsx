import { useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { useConfirm } from '../ui/ConfirmDialog'

/**
 * The question in front of every "Kapitel neu recherchieren" button: our own confirmation (never the browser's), saying that the chapter is replaced only once the
 * new version is finished and what happens to the check marks. `manual` = the path through the player's own AI subscription (the text then explains that nothing
 * is replaced until the pasted answer is saved). Resolves true when the player confirms; nothing is sent here.
 */
export function useConfirmRedoChapter() {
  const { t } = useTranslation('ai')
  const confirm = useConfirm()
  return useCallback(
    (title: string, manual: boolean) =>
      confirm({
        title: t('redoChapter.title', { title }),
        message: (
          <div className="space-y-2">
            <p>{t('redoChapter.messageWhat')}</p>
            <p>{t('redoChapter.messageKept')}</p>
            <p>{manual ? t('redoChapter.messageManual') : t('redoChapter.messageCost')}</p>
          </div>
        ),
        confirmLabel: manual ? t('redoChapter.confirmManual') : t('redoChapter.confirm'),
        tone: 'default',
      }),
    [confirm, t]
  )
}
