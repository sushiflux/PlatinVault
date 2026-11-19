import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { useNavigate } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { IconLoader2, IconPhotoEdit, IconTrash, IconX } from '@tabler/icons-react'
import { deleteGame, deleteGameCover, uploadGameCover } from '../../api/client'
import type { GameDetailDto } from '../../api/types'
import { describeError } from '../../utils/apiErrorText'
import { ErrorBlock } from '../ui/ErrorBlock'
import { FloatingNotice, FloatingNoticeStack } from '../ui/FloatingNotice'
import { IconButton } from '../ui/IconButton'
import { ReservedText } from '../ui/ReservedText'
import { useConfirm } from '../ui/ConfirmDialog'
import '../../styles/dialogs.css'
import '../../styles/addgame.css'
import { CoverUpload } from './CoverUpload'
import type { CoverProblem } from './addGameRules'

// The address of an uploaded cover (the server's CoverStore.UrlPrefix): only such a cover can be removed again
const UPLOADED_COVER = '/api/covers/'

interface Failure {
  title: string
  message: string
  details: string | null
}

const FOCUSABLE = 'button:not(:disabled), a[href], input:not(:disabled), [tabindex]:not([tabindex="-1"])'

// The change-cover dialog: the same cover field as the add dialog, "Cover speichern" uploads, "Eigenes Cover entfernen" takes an uploaded cover away
function ChangeCoverDialog({ game, onClose }: { game: GameDetailDto; onClose: () => void }) {
  const { t } = useTranslation(['dashboard', 'common'])
  const queryClient = useQueryClient()
  const reduce = useReducedMotion() ?? false
  const panelRef = useRef<HTMLDivElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [problem, setProblem] = useState<CoverProblem | null>(null)
  const [busy, setBusy] = useState<'save' | 'remove' | null>(null)
  const [failure, setFailure] = useState<Failure | null>(null)
  const hasOwnCover = game.coverUrl.startsWith(UPLOADED_COVER)

  useEffect(() => {
    const before = document.activeElement as HTMLElement | null
    panelRef.current?.querySelector<HTMLElement>('.pv-cover-zone button')?.focus()
    return () => before?.focus?.()
  }, [])

  const refresh = async () => {
    await Promise.all([queryClient.invalidateQueries({ queryKey: ['game', game.slug] }), queryClient.invalidateQueries({ queryKey: ['games'] })])
  }

  const save = async () => {
    if (!file || problem || busy) return
    setBusy('save')
    setFailure(null)
    try {
      await uploadGameCover(game.slug, file)
      await refresh()
      onClose()
    } catch (err: unknown) {
      setFailure({ title: t('dashboard:manualGame.coverDialog.saveFailedTitle'), ...describeError(err, t('common:unknownError')) })
      setBusy(null)
    }
  }

  const removeOwn = async () => {
    if (busy) return
    setBusy('remove')
    setFailure(null)
    try {
      await deleteGameCover(game.slug)
      await refresh()
      onClose()
    } catch (err: unknown) {
      setFailure({ title: t('dashboard:manualGame.coverDialog.removeFailedTitle'), ...describeError(err, t('common:unknownError')) })
      setBusy(null)
    }
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.stopPropagation()
      if (!busy) onClose()
      return
    }
    if (event.key !== 'Tab' || !panelRef.current) return
    const nodes = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((node) => node.offsetParent !== null || node === document.activeElement)
    const first = nodes[0]
    const last = nodes[nodes.length - 1]
    if (!first || !last) return
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }

  // What the line above the buttons says: nothing chosen yet, or what is happening (its room is reserved)
  const stateCandidates = [t('dashboard:manualGame.coverDialog.nothingChosen'), t('dashboard:manualGame.coverDialog.saving')]
  const stateText = busy === 'save' ? t('dashboard:manualGame.coverDialog.saving') : !file ? t('dashboard:manualGame.coverDialog.nothingChosen') : ''

  return createPortal(
    <div className="pv-modal-backdrop pv-modal-backdrop--top">
      <motion.div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="cover-dialog-title"
        className="pv-modal pv-cover-dialog"
        onKeyDown={onKeyDown}
        initial={reduce ? { opacity: 0 } : { opacity: 0, scale: 0.97, y: 10 }}
        animate={reduce ? { opacity: 1 } : { opacity: 1, scale: 1, y: 0 }}
        transition={reduce ? { duration: 0 } : { type: 'spring', stiffness: 420, damping: 32 }}
      >
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 id="cover-dialog-title" className="pv-dialog-title">
              {t('dashboard:manualGame.coverDialog.title')}
            </h2>
            <p className="pv-dialog-text" style={{ marginTop: '0.25rem' }}>
              {t('dashboard:manualGame.coverDialog.text')}
            </p>
          </div>
          <button type="button" className="pv-btn pv-icon-btn pv-icon-btn--ghost shrink-0" disabled={busy !== null} onClick={onClose} aria-label={t('common:close')}>
            <IconX size={20} aria-hidden="true" />
          </button>
        </div>

        <CoverUpload file={file} onChange={setFile} onProblem={setProblem} currentUrl={game.coverUrl || null} disabled={busy !== null} />

        {failure && (
          <div className="mt-3">
            <ErrorBlock title={failure.title} message={failure.message} details={failure.details} defaultOpen />
          </div>
        )}

        <ReservedText className="pv-addgame-reason mt-3" candidates={stateCandidates} aria-live="polite">
          {stateText}
        </ReservedText>

        <div className="pv-dialog-actions" style={{ marginBlockStart: '0.5rem' }}>
          {hasOwnCover && (
            <button type="button" className="pv-btn-text pv-btn-text--danger me-auto" disabled={busy !== null} onClick={() => void removeOwn()}>
              <span className="relative inline-flex items-center">
                <span className={busy === 'remove' ? 'invisible' : undefined}>{t('dashboard:manualGame.coverDialog.removeOwn')}</span>
                {busy === 'remove' && <IconLoader2 size={16} className="absolute inset-0 m-auto animate-spin" aria-hidden="true" />}
              </span>
            </button>
          )}
          <button type="button" className="pv-btn" disabled={busy !== null} onClick={onClose}>
            {t('common:cancel')}
          </button>
          <button type="button" className="pv-btn pv-btn-primary" disabled={!file || problem !== null || busy !== null} onClick={() => void save()}>
            <span className="relative inline-flex items-center justify-center">
              <span className={busy === 'save' ? 'invisible' : undefined}>{t('dashboard:manualGame.coverDialog.save')}</span>
              {busy === 'save' && <IconLoader2 size={16} className="absolute animate-spin" aria-hidden="true" />}
            </span>
          </button>
        </div>
      </motion.div>
    </div>,
    document.body
  )
}

/**
 * The two actions of a game added by hand, for the game head (the game page mounts it for every game whose platform is not Steam; it renders two icon
 * buttons, so it belongs inside the head's toolbar):
 *  - "Cover ändern": the cover field of the add dialog in an own dialog; uploads a new image or takes an uploaded one away again.
 *  - "Spiel entfernen": our own confirmation (destructive, focus on "Abbrechen") that names what is deleted - guide, trophy list with its ticks, cover - then the
 *    game is gone and the page goes to the overview.
 * A failure of either is a notice with the complete reason and a copy button.
 */
export function ManualGameActions({ game }: { game: GameDetailDto }) {
  const { t } = useTranslation(['dashboard', 'common'])
  const confirm = useConfirm()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [coverOpen, setCoverOpen] = useState(false)
  const [removing, setRemoving] = useState(false)
  const [failure, setFailure] = useState<Failure | null>(null)

  const remove = async () => {
    const ok = await confirm({
      title: t('dashboard:manualGame.remove.confirmTitle', { title: game.title }),
      message: (
        <>
          <p>{t('dashboard:manualGame.remove.confirmMessage')}</p>
          <ul className="pv-dialog-notes">
            <li>{t('dashboard:manualGame.remove.items.guide')}</li>
            <li>{t('dashboard:manualGame.remove.items.trophies')}</li>
            <li>{t('dashboard:manualGame.remove.items.cover')}</li>
          </ul>
          <p style={{ marginBlockStart: '0.75rem' }}>{t('dashboard:manualGame.remove.confirmNote')}</p>
        </>
      ),
      confirmLabel: t('dashboard:manualGame.remove.confirmLabel'),
      tone: 'danger',
    })
    if (!ok) return
    setRemoving(true)
    setFailure(null)
    try {
      await deleteGame(game.id)
      await queryClient.invalidateQueries({ queryKey: ['games'] })
      void navigate('/')
    } catch (err: unknown) {
      setFailure({ title: t('dashboard:manualGame.remove.failedTitle'), ...describeError(err, t('common:unknownError')) })
      setRemoving(false)
    }
  }

  return (
    <>
      <IconButton label={t('dashboard:manualGame.changeCover.label')} description={t('dashboard:manualGame.changeCover.description')} onClick={() => setCoverOpen(true)}>
        <IconPhotoEdit size={20} />
      </IconButton>
      <IconButton label={t('dashboard:manualGame.remove.label')} description={t('dashboard:manualGame.remove.description')} busy={removing} onClick={() => void remove()}>
        <IconTrash size={20} />
      </IconButton>
      {coverOpen && <ChangeCoverDialog game={game} onClose={() => setCoverOpen(false)} />}
      <FloatingNoticeStack>
        {failure && <FloatingNotice key="manual-failure" tone="error" title={failure.title} message={failure.message} details={failure.details} onClose={() => setFailure(null)} />}
      </FloatingNoticeStack>
    </>
  )
}
