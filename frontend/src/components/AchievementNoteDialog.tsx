import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { IconX } from '@tabler/icons-react'
import { saveLiveAchievementNote } from '../api/client'
import type { LiveAchievementItemDto } from '../api/types'
import { ErrorBlock } from './ui/ErrorBlock'
import { IconButton } from './ui/IconButton'
import '../styles/dialogs.css'

interface AchievementNoteDialogProps {
  ach: LiveAchievementItemDto
  appId: number
  onClose: () => void
}

// The personal note of one achievement: shared by the live trophies tab and the "Im Blick" dock, which both open it from the detail dialog.
// A failed save keeps the dialog open with the draft and says what went wrong (the server's own text, or that it could not be reached).
export function AchievementNoteDialog({ ach, appId, onClose }: AchievementNoteDialogProps) {
  const { t } = useTranslation(['live', 'common'])
  const queryClient = useQueryClient()
  const [draft, setDraft] = useState(ach.userNote)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const save = async (e: React.FormEvent) => {
    e.preventDefault()
    if (saving) return
    setSaving(true)
    setError(null)
    try {
      await saveLiveAchievementNote(appId, ach.apiName, draft)
    } catch (err: unknown) {
      const reason =
        err instanceof TypeError
          ? t('noteDialog.serverUnreachable')
          : err instanceof Error
            ? err.message
            : t('common:unknownError')
      setError(t('noteDialog.saveFailed', { reason }))
      return
    } finally {
      setSaving(false)
    }
    onClose()
    await queryClient.invalidateQueries({ queryKey: ['live-achievements'] })
  }

  return (
    <div
      className="pv-modal-backdrop pv-modal-backdrop--top"
      onClick={onClose}
      onKeyDown={(e) => {
        if (e.key === 'Escape') {
          e.stopPropagation()
          onClose()
        }
      }}
    >
      <div
        className="pv-modal w-full max-w-lg p-6"
        role="dialog"
        aria-modal="true"
        aria-labelledby="achievement-note-title"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 id="achievement-note-title" className="pv-dialog-title">
              {t('noteDialog.title')}
            </h2>
            <p className="pv-dialog-text" style={{ marginTop: '0.25rem' }}>
              {ach.displayName}
            </p>
          </div>
          <IconButton label={t('common:close')} onClick={onClose} className="shrink-0">
            <IconX size={20} aria-hidden="true" />
          </IconButton>
        </div>

        <form onSubmit={save} className="mt-4 space-y-3">
          <textarea
            rows={4}
            value={draft}
            onChange={(e) => {
              setDraft(e.target.value)
              // A failure answer belongs to the text that was sent
              setError(null)
            }}
            placeholder={t('noteDialog.placeholder')}
            aria-label={t('noteDialog.title')}
            className="pv-input w-full"
            autoFocus
          />
          {error && <ErrorBlock title={t('noteDialog.failedTitle')} message={error} defaultOpen />}
          <div className="pv-dialog-actions" style={{ marginBlockStart: '0.5rem' }}>
            <button type="button" onClick={onClose} className="pv-btn">
              {t('common:cancel')}
            </button>
            <button type="submit" disabled={saving} className="pv-btn pv-btn-primary">
              {t('noteDialog.save')}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
