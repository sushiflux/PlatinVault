import { useEffect, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { IconAlertTriangle, IconCheck, IconInfoCircle, IconLoader2, IconTrophy, IconX } from '@tabler/icons-react'
import { checkManualTrophies, manualTrophiesPrompt, saveManualTrophies } from '../../../api/client'
import type { ManualTrophyCheckDto, ManualTrophyResearchResultDto, RarityTier, TrophyResearchIncludeDto } from '../../../api/types'
import { describeError } from '../../../utils/apiErrorText'
import { useConfirm } from '../../ui/ConfirmDialog'
import { ErrorDetails } from '../../ui/ErrorDetails'
import { TierIcon } from '../../ui/TierIcon'
import { AnswerBox } from './AnswerBox'
import { PromptBox } from './PromptBox'
import { clearStoredBatch, readStoredText, writeStoredText } from './batch'

interface ManualTrophiesDialogProps {
  slug: string
  gameTitle: string
  // The research is for PlayStation trophies ("trophies") or the achievements of any other platform ("achievements"): only the words differ
  kind: 'trophies' | 'achievements'
  // What the prompt asks for (a Nintendo game: only the facts); missing = the server's default
  include?: TrophyResearchIncludeDto
  // The game already has a researched list: saving replaces it, the ticks stay (said calmly before anything is pasted)
  replacing: boolean
  onClose: () => void
  // Called once the answer is saved, with the list as it is now
  onSaved: (result: ManualTrophyResearchResultDto) => void
}

const FOCUSABLE = 'button:not(:disabled):not([tabindex="-1"]), a[href], textarea:not(:disabled), input:not(:disabled):not([tabindex="-1"]), [tabindex]:not([tabindex="-1"])'

const GRADES: { id: string; tier: RarityTier }[] = [
  { id: 'platinum', tier: 'Platinum' },
  { id: 'gold', tier: 'Gold' },
  { id: 'silver', tier: 'Silver' },
  { id: 'bronze', tier: 'Bronze' },
]

// The tier of a grade the server names ("gold"); null for a grade that is none of the four
const gradeTier = (grade: string | null): RarityTier | null => GRADES.find((entry) => entry.id === grade?.toLowerCase())?.tier ?? null

/**
 * The trophy list (or the facts) of a game added by hand without an API key: one prompt goes into the chat of the player's own AI subscription,
 * its answer is pasted back here. The answer is checked while it is pasted (how many trophies per grade, what happens to the ticks) and saved
 * as the list of the game (a new one, or the existing one replaced with every tick kept). Nothing in here ever talks to an AI provider. A pasted
 * answer survives closing the dialog (sessionStorage, same pattern as the lore dialog).
 */
export function ManualTrophiesDialog({ slug, gameTitle, kind, include, replacing, onClose, onSaved }: ManualTrophiesDialogProps) {
  const { t } = useTranslation(['ai', 'common', 'live'])
  const factsOnly = include?.trophies === false
  const [initialText] = useState(() => readStoredText(slug, 'trophies'))
  // The text of the answer that was read without errors (what "Save" sends)
  const [ready, setReady] = useState<ManualTrophyCheckDto | null>(null)
  const [readyText, setReadyText] = useState('')
  const confirm = useConfirm()
  const [saving, setSaving] = useState(false)
  // issues = the single findings of the server when it sent a list (else only the message)
  const [failure, setFailure] = useState<{ message: string; details: string | null; issues: string[] } | null>(null)
  const [saved, setSaved] = useState<ManualTrophyResearchResultDto | null>(null)
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
    // Saving replaces the whole existing list (the ticks are carried over): our own question first, naming what happens to the ticks
    if (replacing && !factsOnly) {
      const ok = await confirm({
        title: t('manual.trophies.confirm.title'),
        message: [
          t('manual.trophies.confirm.message', { title: gameTitle }),
          ready.keepsTicks > 0 ? t('manual.trophies.confirm.keeps', { count: ready.keepsTicks }) : null,
          ready.dropsTicks > 0 ? t('manual.trophies.confirm.drops', { count: ready.dropsTicks }) : null,
        ]
          .filter(Boolean)
          .join(' '),
        confirmLabel: t('manual.trophies.confirm.confirm'),
        tone: 'danger',
      })
      if (!ok) return
    }
    setSaving(true)
    setFailure(null)
    try {
      const result = await saveManualTrophies(slug, readyText)
      clearStoredBatch(slug, 'trophies')
      setSaved(result)
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
    <div className="pv-modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="manual-trophies-title" onKeyDown={onKeyDown}>
      <div ref={panelRef} tabIndex={-1} className="pv-modal relative flex h-[min(90dvh,820px)] w-full max-w-3xl flex-col overflow-hidden outline-none">
        <div className="flex items-center justify-between gap-3 px-4 py-4 sm:px-6" style={{ background: 'var(--bg-elevated)', borderBottom: '1px solid var(--border-subtle)' }}>
          <div className="min-w-0">
            <h2 id="manual-trophies-title" className="flex items-center gap-2 text-base font-extrabold" style={{ color: 'var(--text-primary)' }}>
              <IconTrophy size={20} className="shrink-0" style={{ color: 'var(--accent-crystal)' }} aria-hidden="true" />
              <span className="min-w-0 break-words">
                {factsOnly ? t('manual.trophies.titleFacts') : t(`manual.trophies.title.${kind}`)} · {gameTitle}
              </span>
            </h2>
            <p className="text-sm" style={{ color: 'var(--text-muted)' }}>
              {t('manual.trophies.step')}
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
            <div className="space-y-1.5" role="status" data-testid="trophies-saved">
              <p className="flex items-start gap-2 text-sm font-extrabold" style={{ color: 'var(--accent-emerald)' }}>
                <IconCheck size={18} className="mt-px shrink-0" aria-hidden="true" />
                <span>
                  {factsOnly
                    ? t('manual.trophies.savedFacts')
                    : t(`manual.trophies.saved.${kind}`, { count: saved.list.totalCount })}
                </span>
              </p>
              {!factsOnly && saved.keptTicks > 0 && (
                <p className="text-sm" style={{ color: 'var(--text-secondary)' }}>
                  {t('manual.trophies.savedKept', { count: saved.keptTicks })}
                </p>
              )}
              {!factsOnly && saved.retainedOldTicked > 0 && (
                <p className="text-sm" style={{ color: 'var(--text-secondary)' }}>
                  {t('manual.trophies.savedRetained', { count: saved.retainedOldTicked })}
                </p>
              )}
            </div>
          ) : (
            <>
              {replacing && !factsOnly && (
                <p className="pv-note flex items-start gap-2 text-sm" data-testid="trophies-replace-note">
                  <IconInfoCircle size={16} className="mt-0.5 shrink-0" aria-hidden="true" />
                  <span>{t('manual.trophies.replaceNote')}</span>
                </p>
              )}

              <section className="space-y-2">
                <h3 className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
                  {t('manual.trophies.promptHeading')}
                </h3>
                <PromptBox load={() => manualTrophiesPrompt(slug, include)} />
              </section>

              <section className="space-y-2">
                <h3 className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
                  {t('manual.trophies.answerHeading')}
                </h3>
                <AnswerBox<ManualTrophyCheckDto>
                  initialText={initialText}
                  check={(text) => checkManualTrophies(slug, text)}
                  onResult={(result, text) => {
                    writeStoredText(slug, 'trophies', text)
                    setReady(result)
                    setReadyText(result ? text : '')
                  }}
                  disabled={saving}
                  renderOk={(result) => (
                    <div className="space-y-2" data-testid="trophies-preview">
                      {result.count > 0 ? (
                        <p>{t(`manual.trophies.preview.${kind}`, { count: result.count })}</p>
                      ) : (
                        <p>{t('manual.trophies.previewFactsOnly')}</p>
                      )}
                      {result.count > 0 && Object.keys(result.byGrade).length > 0 && (
                        <p className="flex flex-wrap items-center gap-x-4 gap-y-1" data-testid="trophies-grades">
                          {GRADES.filter((grade) => (result.byGrade[grade.id] ?? 0) > 0).map((grade) => (
                            <span key={grade.id} className="inline-flex items-center gap-1.5 font-semibold">
                              <TierIcon tier={grade.tier} size={15} />
                              {t(`live:tiers.${grade.tier}`)} {result.byGrade[grade.id]}
                            </span>
                          ))}
                        </p>
                      )}
                      {result.count > 0 && (result.hidden > 0 || result.dlc > 0) && (
                        <p>{t('manual.trophies.previewExtras', { hidden: result.hidden, dlc: result.dlc })}</p>
                      )}
                      {result.hasFacts && <p>{t('manual.trophies.previewFacts')}</p>}
                      {result.count > 0 && (result.keepsTicks > 0 || result.dropsTicks > 0) && (
                        <p className="flex items-start gap-1.5 font-semibold" data-testid="trophies-ticks">
                          <IconAlertTriangle size={14} className="mt-0.5 shrink-0" aria-hidden="true" />
                          <span>
                            {[
                              result.keepsTicks > 0 ? t('manual.trophies.previewKeeps', { count: result.keepsTicks }) : null,
                              result.dropsTicks > 0 ? t('manual.trophies.previewDrops', { count: result.dropsTicks }) : null,
                            ]
                              .filter(Boolean)
                              .join(' ')}
                          </span>
                        </p>
                      )}
                      {result.preview.length > 0 && (
                        <ul className="list-disc space-y-0.5 ps-5" data-testid="trophies-names">
                          {result.preview.map((item) => (
                            <li key={item.key} className="break-words">
                              {item.name}
                              {gradeTier(item.grade) ? <span style={{ color: 'var(--text-muted)' }}> · {t(`live:tiers.${gradeTier(item.grade)!}`)}</span> : null}
                            </li>
                          ))}
                          {result.count > result.preview.length && <li style={{ color: 'var(--text-muted)' }}>{t('manual.trophies.previewMore', { count: result.count - result.preview.length })}</li>}
                        </ul>
                      )}
                    </div>
                  )}
                />
              </section>

              {failure && (
                <div ref={failureRef} className="pv-note pv-note--danger whitespace-pre-line break-words text-sm font-semibold" role="alert">
                  {failure.issues.length > 0 ? (
                    <>
                      {t('manual.trophies.saveFailedList')}
                      <ul className="mt-1 list-disc space-y-1 ps-5" data-testid="trophies-save-issues">
                        {failure.issues.map((issue, index) => (
                          <li key={index}>{issue}</li>
                        ))}
                      </ul>
                      <ErrorDetails message={[t('manual.trophies.saveFailedList'), ...failure.issues].join('\n')} details={failure.details} />
                    </>
                  ) : (
                    <>
                      {t('manual.trophies.saveFailed', { reason: failure.message })}
                      <ErrorDetails message={t('manual.trophies.saveFailed', { reason: failure.message })} details={failure.details} />
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
                <span className={saving ? 'invisible' : ''}>{factsOnly ? t('manual.trophies.saveFacts') : t(`manual.trophies.save.${kind}`)}</span>
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
