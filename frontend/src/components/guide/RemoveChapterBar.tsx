import { useTranslation } from 'react-i18next'
import { IconTrash } from '@tabler/icons-react'
import './guide.css'

interface RemoveChapterBarProps {
  // An AI action changes the guide right now: the button waits (the server would refuse nothing, but the result would land in a moving guide)
  disabled: boolean
  onRemove: () => void
}

/**
 * The action row of the chapter that is shown: "Kapitel entfernen", quiet until hovered (crimson only on hover and focus, like every destructive
 * action). The row is there for every chapter, so showing or hiding the button never moves the content; the question itself is asked by
 * `useRemoveChapter`.
 */
export function RemoveChapterBar({ disabled, onRemove }: RemoveChapterBarProps) {
  const { t } = useTranslation('ai')
  return (
    <div className="pv-chapter-actions no-print" data-testid="chapter-actions">
      <button type="button" className="pv-chapter-remove" disabled={disabled} onClick={onRemove}>
        <IconTrash size={15} className="shrink-0" aria-hidden="true" />
        <span>{t('removeChapter.button')}</span>
      </button>
    </div>
  )
}
