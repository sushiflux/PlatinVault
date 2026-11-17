import { useEffect, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { IconAlertTriangle, IconBook, IconCheck, IconInfoCircle, IconLoader2, IconX } from '@tabler/icons-react'
import { checkManualLore, manualLorePrompt, saveManualLore } from '../../../api/client'
import type { ManualLoreCheckDto, ManualLoreSavedDto } from '../../../api/client'
import { describeError } from '../../../utils/apiErrorText'
import { useConfirm } from '../../ui/ConfirmDialog'
import { ErrorDetails } from '../../ui/ErrorDetails'
import { AnswerBox } from './AnswerBox'
import { PromptBox } from './PromptBox'
import { clearStoredBatch, readStoredText, writeStoredText } from './batch'

interface ManualLoreDialogProps {
  slug: string
  gameTitle: string
  // The game already has a Story & Lore tab: saving replaces it (said calmly before anything is pasted)
  replacing: boolean
  onClose: () => void
  // Called once the answer is saved, with the guide as it is now
  onSaved: (result: ManualLoreSavedDto) => void
}

const FOCUSABLE = 'button:not(:disabled):not([tabindex="-1"]), a[href], textarea:not(:disabled), input:not(:disabled):not([tabindex="-1"]), [tabindex]:not([tabindex="-1"])'

/**
 * Story & Lore without an API key: one prompt goes into the chat of the player's own AI subscription, its answer is pasted back here. The answer is
 * checked while it is pasted (what was read, which parts are missing) and saved as the tab "Story & Lore" (a new one, or the existing one replaced).
 * Nothing in here ever talks to an AI provider. A pasted answer survives closing the dialog (sessionStorage, same pattern as the guide dialog).
 */
export function ManualLoreDialog({ slug, gameTitle, replacing, onClose, onSaved }: ManualLoreDialogProps) {
  const { t } = useTranslation(['ai', 'common'])
  const [initialText] = useState(() => readStoredText(slug, 'lore'))
  // The text of the answer that was read without errors (what "Save" sends)
  const [ready, setReady] = useState<string | null>(null)
  // The parts of the prompt the ready answer does not contain (named in the question before an existing tab is replaced)
  const [missing, setMissing] = useState<string[]>([])
  const confirm = useConfirm()
  const [saving, setSaving] = useState(false)
  // issues = the single findings of the server when it sent a list (else only the message)
  const [failure, setFailure] = useState<{ message: string; details: string | null; issues: string[] } | null>(null)
  const [saved, setSaved] = useState<{ replaced: boolean } | null>(null)
  const panelRef = useRef<HTMLDivElement>(null)
  const failureRef = useRef<HTMLDivElement>(null)

  // A refused save is at the end of the scrolling body: bring it into view so it is never missed
  useEffect(() => {
    if (failure) failureRef.current?.scrollIntoView?.({ block: 'nearest' })
  }, [failure])

  // Focus: into the dialog when it opens, back to where it was when it closes; Tab stays inside, Esc closes
  useEffect(() => {
    const before = document.activeElement as HTMLElement | null
    panelRef.current?.focus()
    return () => before?.focus?.()
  }, [])

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Escape') {
      e.stopPropagation()
      onClose()
      return
    }
    if (e.key !== 'Tab' || !panelRef.current) return
    const items = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE))
    if (items.length === 0) return
    const first = items[0]!
    const last = items[items.length - 1]!
    if (e.shiftKey && (document.activeElement === first || document.activeElement === panelRef.current)) {
      e.preventDefault()
      last.focus()
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault()
      first.focus()
    }
  }

  const save = async () => {
    if (!ready || saving) return
    // Saving replaces the whole existing tab, also when parts are missing: our own question first, naming what goes
    if (replacing) {
      const ok = await confirm({
        title: t('manual.lore.confirm.title'),
        message: [t('manual.lore.confirm.message', { title: gameTitle }), missing.length > 0 ? t('manual.lore.confirm.missing', { parts: missing.join(', ') }) : null].filter(Boolean).join(' '),
        confirmLabel: t('manual.lore.confirm.confirm'),
        tone: 'danger',
      })
      if (!ok) return
    }
    setSaving(true)
    setFailure(null)
    try {
      const result = await saveManualLore(slug, ready)
      clearStoredBatch(slug, 'lore')
      setSaved({ replaced: result.replaced })
      setReady(null)
      onSaved(result)
    } catch (err: unknown) {
      const issues = ((err as { issues?: { message: string }[] } | null)?.issues ?? []).map((issue) => issue.message)
      setFailure({ ...describeError(err, t('common:unknownError')), issues })
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="pv-modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="manual-lore-title" onKeyDown={onKeyDown}>
      <div ref={panelRef} tabIndex={-1} className="pv-modal relative flex h-[min(90dvh,820px)] w-full max-w-3xl flex-col overflow-hidden outline-none">
        <div className="flex items-center justify-between gap-3 px-4 py-4 sm:px-6" style={{ background: 'var(--bg-elevated)', borderBottom: '1px solid var(--border-subtle)' }}>
          <div className="min-w-0">
            <h2 id="manual-lore-title" className="flex items-center gap-2 text-base font-extrabold" style={{ color: 'var(--text-primary)' }}>
              <IconBook size={20} className="shrink-0" style={{ color: 'var(--accent-crystal)' }} aria-hidden="true" />
              <span className="min-w-0 break-words">{t('manual.lore.title')} · {gameTitle}</span>
            </h2>
            <p className="text-sm" style={{ color: 'var(--text-muted)' }}>
              {t('manual.lore.step')}
            </p>
          </div>
          <button type="button" onClick={onClose} className="pv-btn pv-icon-btn" aria-label={t('common:close')}>
            <IconX size={18} />
          </button>
        </div>

        <div className="flex-1 space-y-5 overflow-y-auto px-4 py-5 sm:px-6">
          <p className="text-sm leading-relaxed" style={{ color: 'var(--text-secondary)' }} data-testid="manual-intro">
            {t('manual.intro')}
          </p>

          {saved ? (
            <p className="flex items-start gap-2 text-sm font-extrabold" style={{ color: 'var(--accent-emerald)' }} role="status" data-testid="lore-saved">
              <IconCheck size={18} className="mt-px shrink-0" aria-hidden="true" />
              <span>{saved.replaced ? t('manual.lore.savedReplaced') : t('manual.lore.saved')}</span>
            </p>
          ) : (
            <>
              {replacing && (
                <p className="pv-note flex items-start gap-2 text-sm" data-testid="lore-replace-note">
                  <IconInfoCircle size={16} className="mt-0.5 shrink-0" aria-hidden="true" />
                  <span>{t('manual.lore.replaceNote')}</span>
                </p>
              )}

              <section className="space-y-2">
                <h3 className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
                  {t('manual.lore.promptHeading')}
                </h3>
                <PromptBox load={() => manualLorePrompt(slug)} />
              </section>

              <section className="space-y-2">
                <h3 className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
                  {t('manual.lore.answerHeading')}
                </h3>
                <AnswerBox<ManualLoreCheckDto>
                  initialText={initialText}
                  check={(text) => checkManualLore(slug, text)}
                  onResult={(result, text) => {
                    writeStoredText(slug, 'lore', text)
                    setReady(result?.preview ? text : null)
                    setMissing(result?.preview?.missingParts ?? [])
                  }}
                  disabled={saving}
                  renderOk={(result) => {
                    const preview = result.preview
                    if (!preview) return null
                    return (
                      <>
                        <span>{t('manual.lore.preview', { parts: preview.parts.map((part) => t('manual.lore.part', { title: part.title, count: part.sectionCount })).join(' · ') })}</span>
                        {preview.missingParts.length > 0 && (
                          <span className="mt-1.5 flex items-start gap-1.5 font-semibold" style={{ color: 'var(--accent-gold-text)' }} data-testid="lore-missing">
                            <IconAlertTriangle size={14} className="mt-0.5 shrink-0" aria-hidden="true" />
                            <span>{t('manual.lore.missing', { parts: preview.missingParts.join(', ') })}</span>
                          </span>
                        )}
                      </>
                    )
                  }}
                />
              </section>

              {failure && (
                <div ref={failureRef} className="pv-note pv-note--danger whitespace-pre-line break-words text-sm font-semibold" role="alert">
                  {failure.issues.length > 0 ? (
                    <>
                      {t('manual.lore.saveFailedList')}
                      <ul className="mt-1 list-disc space-y-1 ps-5" data-testid="lore-save-issues">
                        {failure.issues.map((issue, index) => (
                          <li key={index}>{issue}</li>
                        ))}
                      </ul>
                      <ErrorDetails message={[t('manual.lore.saveFailedList'), ...failure.issues].join('\n')} details={failure.details} />
                    </>
                  ) : (
                    <>
                      {t('manual.lore.saveFailed', { reason: failure.message })}
                      <ErrorDetails message={t('manual.lore.saveFailed', { reason: failure.message })} details={failure.details} />
                    </>
                  )}
                </div>
              )}
            </>
          )}
        </div>

        <div className="flex flex-wrap items-center justify-end gap-3 px-4 py-4 sm:px-6" style={{ background: 'var(--bg-elevated)', borderTop: '1px solid var(--border-subtle)' }}>
          {saved ? (
            <button type="button" className="pv-btn pv-btn-primary" onClick={onClose}>
              <span>{t('manual.chapters.finished')}</span>
            </button>
          ) : (
            <>
              <button type="button" className="pv-btn" onClick={onClose}>
                <span>{t('manual.close')}</span>
              </button>
              <button type="button" className="pv-btn pv-btn-primary relative" disabled={!ready || saving} onClick={() => void save()}>
                <span className={saving ? 'invisible' : ''}>{t('manual.lore.save')}</span>
                {saving && (
                  <span className="absolute inset-0 flex items-center justify-center" aria-hidden="true">
                    <IconLoader2 size={16} className="motion-safe:animate-spin" />
                  </span>
                )}
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
