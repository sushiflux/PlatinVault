import { useEffect, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { IconCheck, IconCircle, IconClipboardText, IconListNumbers, IconLoader2, IconStack2, IconX } from '@tabler/icons-react'
import { checkManualBlueprint, checkManualChapter, fetchGuideTopics, manualBlueprintPrompt, manualChapterPrompt, saveManualChapter } from '../../../api/client'
import type { GameDetailDto, GuidePlanDto, GuidePlanTopicDto, ManualBlueprintCheckDto, ManualChapterCheckDto, ManualChapterSeed } from '../../../api/types'
import { describeError } from '../../../utils/apiErrorText'
import { ErrorDetails } from '../../ui/ErrorDetails'
import { keyOf, TopicGroup, usePlanSelection } from '../PlanTopics'
import { AnswerBox } from './AnswerBox'
import { BatchChapters } from './BatchChapters'
import { PromptBox } from './PromptBox'
import { assignBatchKeys, BATCH_DEFAULT_MIN, clearStoredBatch, readStoredBatch, readStoredText, rowsSignature, writeStoredBatch } from './batch'
import type { BatchRow, BatchScope, ManualMode } from './batch'

interface ManualGuideDialogProps {
  slug: string
  gameTitle: string
  // The language the guide is written in (the standard topics are listed in it)
  language: string
  // One chapter (a failed one, researched again) instead of the whole guide
  chapter?: ManualChapterSeed
  // Several given chapters (the failed ones of the guide, each with its tabId) instead of the whole guide: no blueprint step
  chapters?: ManualChapterSeed[]
  onClose: () => void
  // Called after every saved chapter with the guide as it is now
  onSaved: (game: GameDetailDto) => void
}

type ChapterRowState = BatchRow

const FOCUSABLE = 'button:not(:disabled):not([tabindex="-1"]), a[href], textarea:not(:disabled), input:not(:disabled):not([tabindex="-1"]), [tabindex]:not([tabindex="-1"])'

const defaultMode = (count: number): ManualMode => (count >= BATCH_DEFAULT_MIN ? 'batch' : 'single')

interface Start {
  step: 'plan' | 'chapters'
  rows: ChapterRowState[]
  keys: Record<string, string>
  saved: Set<string>
  mode: ManualMode
}

/** The state the dialog opens with: a given list of chapters, what the player left behind in this browser tab, or the empty first step. */
function startState(slug: string, scope: BatchScope, seeds: ManualChapterSeed[] | undefined): Start {
  const stored = readStoredBatch(slug, scope)
  if (seeds && seeds.length > 0) {
    const defs = seeds.map((seed, index) => ({ key: seed.tabId ?? `seed-${index}`, seed }))
    const same = stored !== null && rowsSignature(stored.rows) === rowsSignature(defs)
    if (!same) clearStoredBatch(slug, scope)
    const keys = assignBatchKeys(defs.map((def) => def.key), same ? stored.keys : {})
    return { step: 'chapters', rows: defs.map((def) => ({ ...def, batchKey: keys[def.key] ?? '' })), keys, saved: new Set(same ? stored.saved : []), mode: same ? stored.mode : defaultMode(defs.length) }
  }
  // Only work that is worth coming back to is restored (pasted answers or saved chapters), not a chapter list that was merely looked at
  if (stored && stored.rows.length > 0 && (stored.saved.length > 0 || readStoredText(slug, scope).length > 0)) {
    return {
      step: 'chapters',
      rows: stored.rows.map((row) => ({ ...row, batchKey: stored.keys[row.key] ?? '' })),
      keys: stored.keys,
      saved: new Set(stored.saved),
      mode: stored.mode,
    }
  }
  clearStoredBatch(slug, scope)
  return { step: 'plan', rows: [], keys: {}, saved: new Set(), mode: 'single' }
}

/**
 * The guide without an API key: the prompt goes into the chat of the player's own AI subscription, its answer is pasted back here. Step 1 is the
 * chapter list (a blueprint prompt, or the player ticks the standard topics himself), step 2 one prompt and one pasted answer per chapter; every
 * answer is checked while it is pasted and saved like an AI result. Nothing in here ever talks to an AI provider.
 */
export function ManualGuideDialog({ slug, gameTitle, language, chapter, chapters, onClose, onSaved }: ManualGuideDialogProps) {
  const { t } = useTranslation(['ai', 'common', 'game'])
  // A given list (one failed chapter or several) has no blueprint step; its stored state is kept apart from the one of a whole guide
  const given = chapter ? [chapter] : chapters && chapters.length > 0 ? chapters : undefined
  const fixed = given !== undefined
  const single = chapter !== undefined
  const scope: BatchScope = !fixed ? 'plan' : chapter?.tabId ? `chapter:${chapter.tabId}` : 'failed'
  const [start] = useState(() => startState(slug, scope, given))
  const [step, setStep] = useState<'plan' | 'chapters'>(start.step)
  const [plan, setPlan] = useState<GuidePlanDto | null>(null)
  const { groups, selectable, chosen, selected, setSelected, toggle, tickRecommended } = usePlanSelection(plan)
  const [rows, setRows] = useState<ChapterRowState[]>(start.rows)
  const [keyMap, setKeyMap] = useState<Record<string, string>>(start.keys)
  const [mode, setMode] = useState<ManualMode>(start.mode)
  const [saved, setSaved] = useState<Set<string>>(start.saved)
  const [footerEl, setFooterEl] = useState<HTMLElement | null>(null)
  const panelRef = useRef<HTMLDivElement>(null)
  const [topicsFailure, setTopicsFailure] = useState<{ message: string; details: string | null } | null>(null)
  const [loadingTopics, setLoadingTopics] = useState(false)
  // The plan the player ticked in is kept when the answer is edited later: the newest valid answer replaces it
  const planFromAnswer = useRef(false)

  const onBlueprint = (result: ManualBlueprintCheckDto | null) => {
    if (result?.plan) {
      planFromAnswer.current = true
      setPlan(result.plan)
      tickRecommended(result.plan)
    } else if (planFromAnswer.current) {
      planFromAnswer.current = false
      setPlan(null)
    }
  }

  // No blueprint prompt: the standard topic list, the player ticks what the guide needs
  const chooseTopicsMyself = async () => {
    setLoadingTopics(true)
    setTopicsFailure(null)
    try {
      const topics = await fetchGuideTopics(language)
      planFromAnswer.current = false
      setPlan({
        profiles: [],
        sourceSituation: '',
        topics: topics.map((topic): GuidePlanTopicDto => ({ id: topic.id, title: topic.title, icon: topic.icon, focus: topic.brief, recommended: false, required: false, covered: false, reason: null })),
        extras: [],
        budget: null,
      })
      setSelected(new Set())
    } catch (err: unknown) {
      setTopicsFailure(describeError(err, t('common:unknownError')))
    } finally {
      setLoadingTopics(false)
    }
  }

  const toChapters = () => {
    if (!plan) return
    const defs = chosen.map((topic) => ({
      key: keyOf(topic),
      seed: { title: topic.title, icon: topic.icon, focus: topic.focus, topicId: topic.id, sourceSituation: plan.sourceSituation || undefined },
    }))
    // A chapter keeps its id ("K01") when the player comes back and changes the selection, so pasted answers stay valid
    const keys = assignBatchKeys(defs.map((def) => def.key), keyMap)
    setKeyMap(keys)
    setRows(defs.map((def) => ({ ...def, batchKey: keys[def.key] ?? '' })))
    setMode(defaultMode(defs.filter((def) => !saved.has(def.key)).length))
    setStep('chapters')
  }

  const markSaved = (key: string, game: GameDetailDto) => {
    setSaved((prev) => new Set(prev).add(key))
    onSaved(game)
  }

  const markAllSaved = (keys: string[], game: GameDetailDto) => {
    setSaved((prev) => new Set([...prev, ...keys]))
    onSaved(game)
  }

  // What survives closing the dialog (pasted answers are kept by the batch view itself)
  useEffect(() => {
    if (step !== 'chapters' || rows.length === 0) return
    if (rows.every((row) => saved.has(row.key))) {
      clearStoredBatch(slug, scope)
      return
    }
    writeStoredBatch(slug, scope, { rows: rows.map(({ key, seed }) => ({ key, seed })), keys: keyMap, saved: [...saved], mode })
  }, [slug, scope, step, rows, keyMap, saved, mode])

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

  const batch = step === 'chapters' && mode === 'batch'

  return (
    <div className="pv-modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="manual-guide-title" onKeyDown={onKeyDown}>
      <div ref={panelRef} tabIndex={-1} className="pv-modal relative flex h-[min(90dvh,820px)] w-full max-w-3xl flex-col overflow-hidden outline-none">
        <div className="flex items-center justify-between gap-3 px-4 py-4 sm:px-6" style={{ background: 'var(--bg-elevated)', borderBottom: '1px solid var(--border-subtle)' }}>
          <div className="min-w-0">
            <h2 id="manual-guide-title" className="flex items-center gap-2 text-base font-extrabold" style={{ color: 'var(--text-primary)' }}>
              <IconClipboardText size={20} className="shrink-0" style={{ color: 'var(--accent-emerald)' }} aria-hidden="true" />
              <span className="truncate">{single ? t('ai:manual.chapterTitle') : t('ai:manual.title')} · {gameTitle}</span>
            </h2>
            <p className="text-xs" style={{ color: 'var(--text-muted)' }}>
              {single ? t('ai:manual.step.single') : fixed ? t('ai:manual.step.list') : step === 'plan' ? t('ai:manual.step.plan') : t('ai:manual.step.chapters')}
            </p>
          </div>
          <button type="button" onClick={onClose} className="pv-btn !p-1.5" aria-label={t('common:close')}>
            <IconX size={18} />
          </button>
        </div>

        <div className="flex-1 space-y-5 overflow-y-auto px-4 py-5 sm:px-6">
          <p className="text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }} data-testid="manual-intro">
            {t('ai:manual.intro')}
          </p>

          {step === 'plan' && (
            <>
              <section className="space-y-2">
                <h3 className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
                  {t('ai:manual.plan.promptHeading')}
                </h3>
                <PromptBox load={() => manualBlueprintPrompt(slug)} />
              </section>

              <section className="space-y-2">
                <h3 className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
                  {t('ai:manual.plan.answerHeading')}
                </h3>
                <AnswerBox<ManualBlueprintCheckDto>
                  check={(text) => checkManualBlueprint(slug, text)}
                  onResult={(result) => onBlueprint(result)}
                  renderOk={(result) => (
                    <span>
                      {t('ai:manual.answer.planPreview')}{' '}
                      {result.plan?.budget && t('ai:planBudget.note', { suggested: result.plan.budget.suggested, scope: t(`ai:planBudget.scope.${result.plan.budget.scope}`), min: result.plan.budget.min, max: result.plan.budget.max })}
                    </span>
                  )}
                />
              </section>

              {plan && groups && (
                <div className="space-y-4" data-testid="manual-plan">
                  <TopicGroup title={t('game:builder.groups.recommended')} topics={groups.recommended} selected={selected} onToggle={toggle} />
                  <TopicGroup title={t('game:builder.groups.other')} hint={t('game:builder.groups.otherHint')} topics={groups.other} selected={selected} onToggle={toggle} />
                  {groups.covered.length > 0 && <TopicGroup title={t('game:builder.groups.covered')} topics={groups.covered} selected={selected} onToggle={toggle} disabled />}
                </div>
              )}

              {topicsFailure && (
                <div className="pv-alert pv-alert-danger whitespace-pre-line break-words text-xs font-semibold" role="alert">
                  {topicsFailure.message}
                  <ErrorDetails message={topicsFailure.message} details={topicsFailure.details} />
                </div>
              )}
            </>
          )}

          {step === 'chapters' && (
            <>
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
                <div className="pv-segmented" role="group" aria-label={t('ai:manual.mode.label')} data-testid="manual-mode">
                  <button type="button" aria-pressed={mode === 'batch'} onClick={() => setMode('batch')}>
                    <IconStack2 size={14} aria-hidden="true" />
                    <span>{t('ai:manual.mode.batch')}</span>
                  </button>
                  <button type="button" aria-pressed={mode === 'single'} onClick={() => setMode('single')}>
                    <IconListNumbers size={14} aria-hidden="true" />
                    <span>{t('ai:manual.mode.single')}</span>
                  </button>
                </div>
              </div>
              {mode === 'batch' ? (
                <BatchChapters
                  slug={slug}
                  scope={scope}
                  rows={rows}
                  saved={saved}
                  footerEl={footerEl}
                  fixed={fixed}
                  onSaved={markAllSaved}
                  onBack={() => setStep('plan')}
                  onClose={onClose}
                />
              ) : (
                <>
                  {rows.length > 1 && (
                    <p className="text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
                      {t('ai:manual.chapters.intro')}
                    </p>
                  )}
                  <ul className="space-y-3">
                    {rows.map((row) => (
                      <ChapterRow key={row.key} slug={slug} row={row} done={saved.has(row.key)} startOpen={rows.length === 1} onSaved={(game) => markSaved(row.key, game)} />
                    ))}
                  </ul>
                </>
              )}
            </>
          )}
        </div>

        <div className="flex flex-wrap items-center justify-between gap-3 px-4 py-4 sm:px-6" style={{ background: 'var(--bg-elevated)', borderTop: '1px solid var(--border-subtle)' }}>
          {batch ? (
            // The batch view draws its own controls into this slot (it owns the state they depend on)
            <div ref={setFooterEl} className="w-full" data-testid="manual-footer-slot" />
          ) : step === 'plan' ? (
            <>
              <div className="min-w-0">
                {!plan && (
                  <button type="button" className="pv-btn" onClick={() => void chooseTopicsMyself()} disabled={loadingTopics} aria-describedby="manual-skip-hint">
                    {loadingTopics && <IconLoader2 size={15} className="shrink-0 motion-safe:animate-spin" aria-hidden="true" />}
                    <span>{t('ai:manual.plan.skip')}</span>
                  </button>
                )}
                {!plan && (
                  <p id="manual-skip-hint" className="mt-1 text-[11px]" style={{ color: 'var(--text-muted)' }}>
                    {t('ai:manual.plan.skipHint')}
                  </p>
                )}
                {plan && (
                  <div className="flex flex-wrap items-center gap-3">
                    <button type="button" className="pv-btn" onClick={() => setSelected(new Set(selectable.map(keyOf)))}>
                      {t('game:builder.all')}
                    </button>
                    {chosen.length === 0 && (
                      <span className="text-xs font-semibold" style={{ color: 'var(--text-muted)' }}>
                        {t('ai:manual.plan.noneChosen')}
                      </span>
                    )}
                  </div>
                )}
              </div>
              <button type="button" className="pv-btn pv-btn-emerald" disabled={!plan || chosen.length === 0} onClick={toChapters}>
                <span>{t('ai:manual.plan.next', { count: chosen.length })}</span>
              </button>
            </>
          ) : (
            <>
              <div className="flex min-w-0 items-center gap-3">
                {!fixed && (
                  <button type="button" className="pv-btn" onClick={() => setStep('plan')}>
                    <span>{t('ai:manual.chapters.back')}</span>
                  </button>
                )}
                {rows.length > 1 && (
                  <span className="text-xs font-semibold" style={{ color: 'var(--text-secondary)' }} data-testid="manual-progress">
                    {t('ai:manual.chapters.progress', { done: saved.size, total: rows.length })}
                  </span>
                )}
              </div>
              <button type="button" className="pv-btn pv-btn-emerald" onClick={onClose}>
                <span>{saved.size > 0 ? t('ai:manual.chapters.finished') : t('ai:manual.close')}</span>
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  )
}

function ChapterRow({ slug, row, done, startOpen, onSaved }: { slug: string; row: ChapterRowState; done: boolean; startOpen: boolean; onSaved: (game: GameDetailDto) => void }) {
  const { t } = useTranslation(['ai', 'common'])
  const [open, setOpen] = useState(startOpen)
  const [saving, setSaving] = useState(false)
  const [failure, setFailure] = useState<{ message: string; details: string | null } | null>(null)
  // The text of the answer that was read without errors (what "Save" sends)
  const [ready, setReady] = useState<string | null>(null)
  // The saved answer said the topic does not exist in the game: no chapter was created
  const [dropped, setDropped] = useState(false)

  const save = async () => {
    if (!ready) return
    setSaving(true)
    setFailure(null)
    try {
      const result = await saveManualChapter(slug, ready, row.seed)
      setDropped(result.dropped === true)
      onSaved(result.game)
      setOpen(false)
    } catch (err: unknown) {
      setFailure(describeError(err, t('common:unknownError')))
    } finally {
      setSaving(false)
    }
  }

  return (
    <li className="pv-card space-y-3 !p-4" data-testid="manual-row" data-done={done || undefined}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex min-w-0 items-center gap-2">
          {done ? <IconCheck size={18} className="shrink-0" style={{ color: 'var(--accent-emerald)' }} aria-hidden="true" /> : <IconCircle size={18} className="shrink-0" style={{ color: 'var(--text-muted)' }} aria-hidden="true" />}
          <span className="min-w-0 break-words text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
            {row.seed.title}
          </span>
        </div>
        <span className="text-xs font-bold" style={{ color: done ? 'var(--accent-emerald)' : 'var(--text-muted)' }}>
          {done ? (dropped ? t('manual.row.dropped') : t('manual.row.done')) : row.seed.tabId ? t('manual.row.replaces') : t('manual.row.open')}
        </span>
      </div>

      {!done && (
        <>
          <PromptBox
            load={() => manualChapterPrompt(slug, row.seed)}
            extraActions={
              <>
                <button type="button" className="pv-btn" onClick={() => setOpen((value) => !value)} aria-expanded={open}>
                  <span>{open ? t('manual.row.hidePaste') : t('manual.row.paste')}</span>
                </button>
                {open && (
                  <button type="button" className="pv-btn pv-btn-emerald relative" disabled={!ready || saving} onClick={() => void save()}>
                    <span className={saving ? 'invisible' : ''}>{t('manual.row.save')}</span>
                    {saving && (
                      <span className="absolute inset-0 flex items-center justify-center" aria-hidden="true">
                        <IconLoader2 size={16} className="motion-safe:animate-spin" />
                      </span>
                    )}
                  </button>
                )}
              </>
            }
          />
          {failure && (
            <div className="pv-alert pv-alert-danger whitespace-pre-line break-words text-xs font-semibold" role="alert">
              {t('manual.row.saveFailed', { reason: failure.message })}
              <ErrorDetails message={t('manual.row.saveFailed', { reason: failure.message })} details={failure.details} />
            </div>
          )}
          {open && (
            <AnswerBox<ManualChapterCheckDto>
              check={(text) => checkManualChapter(slug, text, row.seed)}
              onResult={(result, text) => setReady(result?.preview || result?.notApplicable ? text : null)}
              disabled={saving}
              renderOk={(result) =>
                // "Does not exist in this game": the server's sentence says what saving does (no chapter / the placeholder is removed)
                result.notApplicable ?? (result.preview && t('manual.answer.preview', { title: result.preview.title, sections: result.preview.sectionCount, rows: result.preview.tableRows, cards: result.preview.cards }))
              }
            />
          )}
        </>
      )}
    </li>
  )
}
