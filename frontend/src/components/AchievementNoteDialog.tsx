import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { IconX } from '@tabler/icons-react'
import { saveLiveAchievementNote } from '../api/client'
import type { LiveAchievementItemDto } from '../api/types'
import { CopyErrorButton } from './ui/CopyErrorButton'

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
    <div className="pv-modal-backdrop" onClick={onClose}>
      <div className="pv-modal w-full max-w-md p-5 space-y-4" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between">
          <div>
            <h4 className="text-base font-extrabold" style={{ color: 'var(--text-primary)' }}>
              {t('noteDialog.title')}
            </h4>
            <p className="text-xs" style={{ color: 'var(--text-secondary)' }}>
              {ach.displayName}
            </p>
          </div>
          <button type="button" onClick={onClose} className="pv-btn !p-1.5" aria-label={t('common:close')}>
            <IconX size={18} />
          </button>
        </div>

        <form onSubmit={save} className="space-y-3">
          <textarea
            rows={4}
            value={draft}
            onChange={(e) => {
              setDraft(e.target.value)
              // A failure answer belongs to the text that was sent
              setError(null)
            }}
            placeholder={t('noteDialog.placeholder')}
            className="pv-input w-full"
          />
          {error && (
            <p role="alert" className="pv-alert pv-alert-danger !px-3 !py-2 text-xs font-semibold">
              {error}
              <CopyErrorButton className="ms-2 align-middle" report={{ message: error }} />
            </p>
          )}
          <div className="flex justify-end gap-2">
            <button type="button" onClick={onClose} className="pv-btn">
              {t('common:cancel')}
            </button>
            <button type="submit" disabled={saving} className="pv-btn pv-btn-emerald">
              {t('noteDialog.save')}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
