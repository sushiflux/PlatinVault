import { useTranslation } from 'react-i18next'
import { IconAlertTriangle } from '@tabler/icons-react'
import type { ChapterItem } from './navModel'

/** The "3/12" figure at the end of a chapter row (nothing for a chapter without checklist rows). */
export function ChapterCount({ chapter }: { chapter: ChapterItem }) {
  const { t } = useTranslation('shell')
  if (chapter.total <= 0) return null
  return (
    <span className="pv-ni-count" data-done={chapter.checked >= chapter.total ? 'true' : undefined}>
      <span aria-hidden="true">
        {chapter.checked}/{chapter.total}
      </span>
      <span className="sr-only">{t('nav.chapterProgress', { checked: chapter.checked, total: chapter.total })}</span>
    </span>
  )
}

/** What stands at the end of a chapter row: the warning of a failed research, else the progress figure. */
export function ChapterTrailing({ chapter }: { chapter: ChapterItem }) {
  const { t } = useTranslation('shell')
  if (!chapter.failed) return <ChapterCount chapter={chapter} />
  return (
    <span className="pv-ni-failed">
      <IconAlertTriangle size={16} aria-hidden="true" />
      <span className="sr-only">{t('nav.failed')}</span>
    </span>
  )
}
