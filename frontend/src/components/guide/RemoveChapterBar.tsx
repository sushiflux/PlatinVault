import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { IconClipboardText, IconRefresh, IconTrash } from '@tabler/icons-react'
import type { AiProviderId } from '../../api/types'
import { AiRunButton } from '../ai/AiRunButton'
import './guide.css'

interface RemoveChapterBarProps {
  // An AI action changes the guide right now: the buttons wait (the server would refuse nothing, but the result would land in a moving guide)
  disabled: boolean
  onRemove: () => void
  // "Kapitel neu recherchieren" with the API key (the AI chosen for this one action, or undefined = the settings decide); left out = no redo for this chapter
  onRedo?: (oneOff: AiProviderId | undefined) => void
  // "Mit meinem KI-Abo neu recherchieren" (the prompt/paste dialog); left out = not offered
  onRedoManual?: () => void
  // The redo job of this chapter is running
  redoBusy?: boolean
}

/**
 * The action row of the chapter that is shown: research it again (API key or own AI subscription) and "Kapitel entfernen", quiet until hovered (crimson only on
 * hover and focus, like every destructive action). The row is there for every chapter, so showing or hiding a button never moves the content; the questions
 * themselves are asked by `useRemoveChapter` / `useConfirmRedoChapter`. The hidden Steam achievement list and Story & Lore have no redo here (they pass no handler).
 */
export function RemoveChapterBar({ disabled, onRemove, onRedo, onRedoManual, redoBusy = false }: RemoveChapterBarProps) {
  const { t } = useTranslation('ai')
  const [oneOff, setOneOff] = useState<AiProviderId | undefined>(undefined)
  return (
    <div className="pv-chapter-actions no-print" data-testid="chapter-actions">
      {(onRedo || onRedoManual) && (
        <div className="pv-chapter-redo">
          {onRedo && (
            <AiRunButton
              busy={redoBusy}
              disabled={disabled || redoBusy}
              oneOff={oneOff}
              onOneOffChange={setOneOff}
              onClick={() => onRedo(oneOff)}
              icon={<IconRefresh size={15} className="shrink-0" aria-hidden="true" />}
              label={<span>{t('redoChapter.button')}</span>}
            />
          )}
          {onRedoManual && (
            <button type="button" className="pv-btn" disabled={disabled || redoBusy} onClick={onRedoManual}>
              <IconClipboardText size={15} className="shrink-0" aria-hidden="true" />
              <span>{t('redoChapter.manualButton')}</span>
            </button>
          )}
        </div>
      )}
      <button type="button" className="pv-chapter-remove" disabled={disabled} onClick={onRemove}>
        <IconTrash size={15} className="shrink-0" aria-hidden="true" />
        <span>{t('removeChapter.button')}</span>
      </button>
    </div>
  )
}
