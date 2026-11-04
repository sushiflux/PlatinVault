import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react'
import type { ClipboardEvent, CSSProperties, DragEvent, ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import {
  IconAlertTriangle,
  IconBan,
  IconCheck,
  IconChevronDown,
  IconCircleCheck,
  IconCircleDashed,
  IconFileImport,
  IconLoader2,
  IconTrash,
  IconX,
} from '@tabler/icons-react'
import { checkManualBatch, manualBatchPrompt, saveManualBatch } from '../../../api/client'
import type { GameDetailDto, ManualBatchCheckDto, ManualBatchItemCheckDto, ManualBatchPromptDto, ManualBatchSkippedDto } from '../../../api/types'
import { useFormat } from '../../../i18n/format'
import { describeError } from '../../../utils/apiErrorText'
import { ErrorDetails } from '../../ui/ErrorDetails'
import { ReservedText } from '../../ui/ReservedText'
import { StackedLabel } from '../../ui/StackedLabel'
import { useConfirm } from '../../ui/ConfirmDialog'
import { IssueList } from './AnswerBox'
import { FixPromptBar } from './FixPrompt'
import { PromptBox } from './PromptBox'
import { useCopyPrompt } from './useCopyPrompt'
import { isSaveable, readStoredText, ROW_STATUSES, statusOf, toBatchItem, writeStoredText } from './batch'
import type { BatchRow, BatchScope, RowStatus } from './batch'

// A file bigger than this is not the answer of a chat (31 long chapters are well below a megabyte)
const MAX_FILE_BYTES = 8_000_000
const CHECK_DELAY_MS = 500
// The separator between two pasted answers (the server reads every JSON object it finds)
const JOIN = '\n\n'

interface Failure {
  message: string
  details: string | null
}

interface BatchChaptersProps {
  slug: string
  scope: BatchScope
  rows: BatchRow[]
  // Row keys of the chapters that are saved already
  saved: Set<string>
  // The dialog's footer: the save controls are drawn into it
  footerEl: HTMLElement | null
  // A fixed list from the page (no chapter list step to go back to)
  fixed: boolean
  onSaved: (rowKeys: string[], game: GameDetailDto) => void
  onBack: () => void
  onClose: () => void
}

/** Reads a dropped / chosen file as text (FileReader, so the size limit applies before anything is kept). */
function readFile(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(typeof reader.result === 'string' ? reader.result : '')
    reader.onerror = () => reject(reader.error ?? new Error(file.name))
    reader.readAsText(file)
  })
}

/**
 * "All at once": ONE prompt for every open chapter, ONE text that collects every answer of the chat (each paste and each file is appended),
 * a checklist that shows live which chapters were recognised, and one button that saves everything readable. Nothing here talks to an AI.
 * The collected text survives closing the dialog (sessionStorage). Status chips, counters and the footer have a fixed size, so nothing moves
 * while the check results come in.
 */
export function BatchChapters({ slug, scope, rows, saved, footerEl, fixed, onSaved, onBack, onClose }: BatchChaptersProps) {
  const { t } = useTranslation(['ai', 'common'])
  const fmt = useFormat()
  const confirm = useConfirm()
  const listId = useId()
  const fieldId = useId()

  const [text, setText] = useState(() => readStoredText(slug, scope))
  const [result, setResult] = useState<ManualBatchCheckDto | null>(null)
  const [checking, setChecking] = useState(false)
  const [checkFailure, setCheckFailure] = useState<Failure | null>(null)
  const [saving, setSaving] = useState(false)
  const [saveFailure, setSaveFailure] = useState<Failure | null>(null)
  const [skipped, setSkipped] = useState<ManualBatchSkippedDto[]>([])
  const [fileFailure, setFileFailure] = useState<Failure | null>(null)
  const [dragging, setDragging] = useState(false)
  const [restLoading, setRestLoading] = useState(false)
  const [restFailure, setRestFailure] = useState<Failure | null>(null)
  const [restPrompt, setRestPrompt] = useState<string | null>(null)
  const [promptChars, setPromptChars] = useState<number | null>(null)
  const latest = useRef(0)
  const fileInput = useRef<HTMLInputElement>(null)
  const fieldRef = useRef<HTMLTextAreaElement>(null)
  const restFieldRef = useRef<HTMLTextAreaElement>(null)
  const rest = useCopyPrompt()

  const allItems = useMemo(() => rows.map(toBatchItem), [rows])
  const unsavedRows = useMemo(() => rows.filter((row) => !saved.has(row.key)), [rows, saved])
  const unsavedSignature = unsavedRows.map((row) => row.batchKey).join('|')

  // ---- The prompt for the chapters that are not saved yet (made once per set of chapters, also to show its size) ----
  const promptCache = useRef<{ signature: string; promise: Promise<ManualBatchPromptDto> } | null>(null)
  const loadPrompt = useCallback(() => {
    if (promptCache.current?.signature !== unsavedSignature) {
      const promise = manualBatchPrompt(slug, unsavedRows.map(toBatchItem))
      const entry = { signature: unsavedSignature, promise }
      promptCache.current = entry
      promise.then(
        (made) => {
          if (promptCache.current === entry) setPromptChars(made.prompt.length)
        },
        () => {
          // The next click on "Copy" asks again and shows the reason
          if (promptCache.current === entry) promptCache.current = null
        }
      )
    }
    return promptCache.current.promise
    // unsavedRows is derived from the signature
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [slug, unsavedSignature])

  useEffect(() => {
    if (unsavedRows.length > 0) loadPrompt().catch(() => undefined)
  }, [loadPrompt, unsavedRows.length])

  // ---- What survives closing the dialog ----
  useEffect(() => {
    writeStoredText(slug, scope, text)
  }, [slug, scope, text])

  // ---- Live check: a short pause after the last change, then the server reads the whole text; only the newest answer counts ----
  useEffect(() => {
    latest.current += 1
    const mine = latest.current
    setCheckFailure(null)
    if (text.trim().length === 0) {
      setResult(null)
      setChecking(false)
      return
    }
    setChecking(true)
    const timer = window.setTimeout(async () => {
      try {
        const answer = await checkManualBatch(slug, text, allItems)
        if (mine !== latest.current) return
        setResult(answer)
      } catch (err: unknown) {
        if (mine !== latest.current) return
        setResult(null)
        setCheckFailure(describeError(err, t('common:unknownError')))
      } finally {
        if (mine === latest.current) setChecking(false)
      }
    }, CHECK_DELAY_MS)
    return () => window.clearTimeout(timer)
    // `t` is stable per language; a language switch must not re-check
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [text, slug, allItems])

  // The clipboard was blocked for the prompt of the remaining chapters: it is shown selected
  useEffect(() => {
    if (rest.state === 'selected' && restPrompt) {
      restFieldRef.current?.focus()
      restFieldRef.current?.select()
    }
  }, [rest.state, restPrompt])

  // ---- Per chapter: its answer (if any) and its status ----
  const byKey = useMemo(() => new Map((result?.items ?? []).map((item) => [item.key, item])), [result])
  const infos = rows.map((row) => {
    const item = byKey.get(row.batchKey)
    return { row, item, status: statusOf(row, saved, item) }
  })
  const total = rows.length
  const recognized = infos.filter((info) => info.status !== 'pending').length
  const saveable = infos.filter((info) => isSaveable(info.status))
  const savedCount = infos.filter((info) => info.status === 'saved').length
  const openRows = infos.filter((info) => info.status === 'pending' || info.status === 'error').map((info) => info.row)
  const allDone = total > 0 && savedCount === total
  const unmatched = result?.unmatched ?? []
  const counterText = t('manual.batch.checklist.recognized', { found: recognized, count: total })

  // ---- Adding text: every paste and every file is appended ----
  const append = (addition: string) => {
    if (!addition.trim()) return
    setText((prev) => (prev.trim() ? prev.replace(/\s+$/, '') + JOIN + addition.trim() : addition.trim()))
    window.requestAnimationFrame(() => {
      const field = fieldRef.current
      if (field) field.scrollTop = field.scrollHeight
    })
  }

  const onPaste = (event: ClipboardEvent<HTMLTextAreaElement>) => {
    const pasted = event.clipboardData.getData('text')
    if (!pasted) return
    event.preventDefault()
    append(pasted)
  }

  const addFiles = async (files: File[]) => {
    setFileFailure(null)
    for (const file of files) {
      if (file.size > MAX_FILE_BYTES) {
        setFileFailure({ message: t('manual.answer.fileFailed', { reason: `${file.name}: ${Math.round(file.size / 1000)} KB` }), details: null })
        continue
      }
      try {
        append(await readFile(file))
      } catch (err: unknown) {
        const failure = describeError(err, t('common:unknownError'))
        setFileFailure({ message: t('manual.answer.fileFailed', { reason: `${file.name}: ${failure.message}` }), details: failure.details })
      }
    }
  }

  const onDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault()
    setDragging(false)
    void addFiles(Array.from(event.dataTransfer.files))
  }

  const clearText = async () => {
    const ok = await confirm({
      title: t('manual.batch.answer.clearTitle'),
      message: t('manual.batch.answer.clearMessage'),
      confirmLabel: t('manual.batch.answer.clearConfirm'),
      tone: 'danger',
    })
    if (!ok) return
    setText('')
    setFileFailure(null)
    setSkipped([])
    setSaveFailure(null)
  }

  // ---- Saving: everything readable, in one request ----
  const save = async () => {
    if (saveable.length === 0 || saving) return
    setSaving(true)
    setSaveFailure(null)
    setSkipped([])
    try {
      const answer = await saveManualBatch(
        slug,
        text,
        allItems,
        saveable.map((info) => info.row.batchKey)
      )
      const rowOf = new Map(rows.map((row) => [row.batchKey, row.key]))
      const savedKeys = answer.saved.map((entry) => rowOf.get(entry.key)).filter((key): key is string => key !== undefined)
      onSaved(savedKeys, answer.detail)
      setSkipped(answer.skipped)
      // The answers of several chapters sit in one text and cannot be cut apart reliably, so the text is only dropped when nothing in it
      // is left to look at; otherwise it stays (saved chapters are marked and never saved twice) and the player can fix or clear it.
      const leftover = (result?.items.some((item) => item.state === 'error') ?? false) || (result?.unmatched.length ?? 0) > 0 || answer.skipped.length > 0
      if (!leftover) setText('')
    } catch (err: unknown) {
      setSaveFailure(describeError(err, t('common:unknownError')))
    } finally {
      setSaving(false)
    }
  }

  // ---- The prompt for the rest: only the chapters that are neither saved nor ready to save ----
  const copyRest = async () => {
    if (openRows.length === 0 || restLoading) return
    setRestLoading(true)
    setRestFailure(null)
    setRestPrompt(null)
    try {
      const made = await manualBatchPrompt(slug, openRows.map(toBatchItem))
      if (!(await rest.copy(made.prompt))) setRestPrompt(made.prompt)
    } catch (err: unknown) {
      setRestFailure(describeError(err, t('common:unknownError')))
    } finally {
      setRestLoading(false)
    }
  }
  const restEnabled = openRows.length > 0 && recognized > 0

  const saveDisabledReason = saving
    ? ''
    : allDone
      ? ''
      : checking
        ? t('manual.batch.footer.reasonChecking')
        : text.trim().length === 0
          ? t('manual.batch.footer.reasonEmpty')
          : saveable.length === 0
            ? t('manual.batch.footer.reasonNone')
            : ''
  const footerMessage = rest.state === 'copied' ? t('manual.batch.footer.restCopied') : rest.state === 'selected' ? t('manual.copy.selected') : saveDisabledReason
  // Both labels share one cell: the button is as wide as the larger count ("9 chapters" -> "31 chapters" never changes its width)
  const saveLabels = [t('manual.batch.footer.save', { count: saveable.length }), t('manual.batch.footer.save', { count: total })]
  const restLabels = [
    t('manual.batch.footer.restPrompt', { count: openRows.length }),
    t('manual.batch.footer.restLoading'),
    t('manual.batch.footer.restDone'),
    t('manual.batch.footer.restPrompt', { count: total }),
  ]
  const restActive = restLoading ? 1 : rest.state === 'copied' ? 2 : 0

  const sizeKb = promptChars === null ? null : Math.max(1, Math.round(promptChars / 1000))
  const promptInfo = sizeKb === null ? t('manual.batch.promptInfoLoading') : t('manual.batch.promptInfo', { count: unsavedRows.length, size: fmt.number(sizeKb) })

  const stepKeys = ['step1', 'step2', 'step3'] as const

  const footer = (
    <div className="grid w-full gap-2">
      <ReservedText
        className="text-xs font-semibold"
        style={{ color: rest.state === 'copied' ? 'var(--accent-emerald)' : 'var(--text-secondary)' }}
        candidates={[
          t('manual.batch.footer.reasonChecking'),
          t('manual.batch.footer.reasonEmpty'),
          t('manual.batch.footer.reasonNone'),
          t('manual.batch.footer.restCopied'),
          t('manual.copy.selected'),
        ]}
        role="status"
        aria-live="polite"
        data-testid="batch-footer-message"
      >
        {footerMessage}
      </ReservedText>
      <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
        <div className="flex min-w-0 items-center gap-3">
          {!fixed && (
            <button type="button" className="pv-btn" onClick={onBack}>
              <span>{t('manual.chapters.back')}</span>
            </button>
          )}
          <span className="text-xs font-semibold tabular-nums" style={{ color: 'var(--text-secondary)' }} data-testid="manual-progress">
            {t('manual.chapters.progress', { done: savedCount, total })}
          </span>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          <button type="button" className="pv-btn max-sm:w-full max-sm:justify-center" disabled={!restEnabled || restLoading} onClick={() => void copyRest()} data-testid="batch-rest">
            <StackedLabel active={restActive} labels={restLabels} />
          </button>
          <button type="button" className="pv-btn pv-btn-emerald relative" disabled={saveable.length === 0 || saving || checking} onClick={() => void save()} data-testid="batch-save">
            <span className={`inline-flex ${saving ? 'invisible' : ''}`}>
              <StackedLabel active={0} labels={saveLabels} />
            </span>
            {saving && (
              <span className="absolute inset-0 flex items-center justify-center" aria-hidden="true">
                <IconLoader2 size={16} className="motion-safe:animate-spin" />
              </span>
            )}
          </button>
          <button type="button" className="pv-btn" onClick={onClose}>
            <span>{savedCount > 0 ? t('manual.chapters.finished') : t('manual.close')}</span>
          </button>
        </div>
      </div>
    </div>
  )

  return (
    <div className="space-y-5" data-testid="batch-view">
      {/* (a) What to do, in three plain steps */}
      <section className="space-y-2" aria-labelledby={`${listId}-how`}>
        <h3 id={`${listId}-how`} className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
          {t('manual.batch.how.heading')}
        </h3>
        <ol className="space-y-1.5 text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
          {stepKeys.map((key, index) => (
            <li key={key} className="flex gap-2">
              <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full text-[11px] font-extrabold" style={{ background: 'var(--accent-emerald-bg)', color: 'var(--accent-emerald)', border: '1px solid var(--accent-emerald-border)' }} aria-hidden="true">
                {index + 1}
              </span>
              <span className="min-w-0">{t(`manual.batch.how.${key}`)}</span>
            </li>
          ))}
        </ol>
      </section>

      {/* (b) The prompt */}
      {!allDone && (
        <section className="space-y-2" aria-labelledby={`${listId}-prompt`}>
          <h3 id={`${listId}-prompt`} className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
            {t('manual.batch.promptHeading')}
          </h3>
          <ReservedText className="text-xs font-semibold" style={{ color: 'var(--text-secondary)' }} candidates={[t('manual.batch.promptInfoLoading'), t('manual.batch.promptInfo', { count: total, size: fmt.number(999) })]} data-testid="batch-prompt-info">
            {promptInfo}
          </ReservedText>
          <PromptBox key={unsavedSignature} load={loadPrompt} />
        </section>
      )}

      {/* (c) The checklist */}
      <section className="space-y-2" aria-labelledby={`${listId}-list`}>
        <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
          <h3 id={`${listId}-list`} className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
            {t('manual.batch.checklist.heading', { count: total })}
          </h3>
          <ReservedText className="text-xs font-bold tabular-nums" style={{ color: 'var(--text-secondary)' }} candidates={[t('manual.batch.checklist.recognized', { found: total, count: total })]} data-testid="batch-counter">
            {counterText}
          </ReservedText>
        </div>
        {/* The announcement for screen readers: only when the check is through, politely */}
        <p className="sr-only" role="status" aria-live="polite" data-testid="batch-announce">
          {checking ? '' : counterText}
        </p>
        <ul className="space-y-2" aria-labelledby={`${listId}-list`} data-testid="batch-list">
          {infos.map((info) => (
            <BatchRowItem key={info.row.key} row={info.row} status={info.status} item={info.item} />
          ))}
        </ul>

        {/* Answers no chapter claims: a box of fixed height that scrolls inside, so one appearing never pushes the answer field down */}
        <div className="space-y-1.5 pt-1" data-testid="batch-unmatched">
          <h4 className="text-xs font-extrabold uppercase tracking-wider tabular-nums" style={{ color: unmatched.length > 0 ? 'var(--accent-crimson)' : 'var(--text-muted)' }}>
            {t('manual.batch.unmatched.heading', { count: unmatched.length })}
          </h4>
          <div className="h-32 space-y-2 overflow-y-auto rounded-lg p-2.5 text-xs" style={{ background: 'var(--bg-surface)', border: '1px solid var(--border-subtle)' }}>
            {unmatched.length === 0 ? (
              <p style={{ color: 'var(--text-muted)' }}>{result ? t('manual.batch.unmatched.none') : t('manual.batch.unmatched.idle')}</p>
            ) : (
              <>
                <p style={{ color: 'var(--text-muted)' }}>{t('manual.batch.unmatched.hint')}</p>
                <ul className="space-y-2">
                  {unmatched.map((entry) => (
                    <li key={entry.index} className="space-y-1.5 rounded-lg p-2" style={{ background: 'var(--accent-crimson-bg)', border: '1px solid var(--accent-crimson-border)' }}>
                      <div className="break-words font-extrabold" style={{ color: 'var(--text-primary)' }}>
                        {entry.title ? t('manual.batch.unmatched.itemTitled', { n: entry.index, title: entry.title }) : t('manual.batch.unmatched.item', { n: entry.index })}
                      </div>
                      {entry.errors.length > 0 && <IssueList tone="error" heading={t('manual.answer.errors')} issues={entry.errors} />}
                    </li>
                  ))}
                </ul>
              </>
            )}
          </div>
        </div>
      </section>

      {/* (d) The answers */}
      {!allDone && (
        <section className="space-y-2" aria-labelledby={`${listId}-answers`}>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 id={`${listId}-answers`} className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
              {t('manual.batch.answer.heading')}
            </h3>
            <div className="flex gap-2">
              <input
                ref={fileInput}
                type="file"
                multiple
                accept=".txt,.md,.json,text/plain,text/markdown,application/json"
                className="sr-only"
                tabIndex={-1}
                aria-hidden="true"
                data-testid="batch-file"
                onChange={(e) => {
                  void addFiles(Array.from(e.target.files ?? []))
                  e.target.value = ''
                }}
              />
              <button type="button" className="pv-btn !px-2.5 !py-1 !text-xs" disabled={saving} onClick={() => fileInput.current?.click()}>
                <IconFileImport size={14} className="shrink-0" aria-hidden="true" />
                <span>{t('manual.batch.answer.chooseFile')}</span>
              </button>
              <button type="button" className="pv-btn !px-2.5 !py-1 !text-xs" disabled={saving || text.length === 0} onClick={() => void clearText()}>
                <IconTrash size={14} className="shrink-0" aria-hidden="true" />
                <span>{t('manual.batch.answer.clear')}</span>
              </button>
            </div>
          </div>

          <div
            className="relative"
            onDragOver={(e) => {
              if (e.dataTransfer.types.includes('Files')) {
                e.preventDefault()
                setDragging(true)
              }
            }}
            onDragLeave={(e) => {
              if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setDragging(false)
            }}
            onDrop={onDrop}
            data-testid="batch-dropzone"
          >
            <textarea
              id={fieldId}
              ref={fieldRef}
              value={text}
              onChange={(e) => setText(e.target.value)}
              onPaste={onPaste}
              disabled={saving}
              spellCheck={false}
              rows={8}
              aria-label={t('manual.batch.answer.label')}
              aria-describedby={`${fieldId}-meta`}
              placeholder={t('manual.batch.answer.placeholder')}
              className="block max-h-72 min-h-[9rem] w-full resize-y rounded-lg p-2.5 font-mono text-xs leading-relaxed"
              style={{ background: 'var(--bg-elevated)', color: 'var(--text-primary)', border: `1px solid ${dragging ? 'var(--accent-crystal)' : 'var(--border-subtle)'}`, outline: dragging ? '2px dashed var(--accent-crystal)' : undefined }}
            />
            {dragging && (
              <div className="pointer-events-none absolute inset-0 flex items-center justify-center rounded-lg text-sm font-extrabold" style={{ background: 'var(--accent-crystal-bg)', color: 'var(--accent-crystal)' }}>
                {t('manual.batch.answer.dropHint')}
              </div>
            )}
          </div>
          <ReservedText
            id={`${fieldId}-meta`}
            className="text-[11px] font-semibold tabular-nums"
            style={{ color: 'var(--text-muted)' }}
            candidates={[t('manual.batch.answer.empty'), t('manual.batch.answer.collected', { size: fmt.number(9_999_999) })]}
            data-testid="batch-meta"
          >
            {text.length === 0 ? t('manual.batch.answer.empty') : t('manual.batch.answer.collected', { size: fmt.number(text.length) })}
          </ReservedText>

          {/* Messages about the text: fixed height, scrolls inside, so nothing around it moves */}
          <div className="h-40 space-y-2 overflow-y-auto rounded-lg p-2.5 text-xs" style={{ background: 'var(--bg-surface)', border: '1px solid var(--border-subtle)' }} role="status" aria-live="polite" data-testid="batch-messages">
            {fileFailure && <FailureLine failure={fileFailure} />}
            {saveFailure && <FailureLine failure={{ message: t('manual.batch.footer.saveFailed', { reason: saveFailure.message }), details: saveFailure.details }} />}
            {restFailure && <FailureLine failure={{ message: t('manual.batch.footer.restFailed', { reason: restFailure.message }), details: restFailure.details }} />}
            {skipped.length > 0 && (
              <section className="space-y-1" data-testid="batch-skipped">
                <h4 className="flex items-center gap-1.5 font-extrabold" style={{ color: 'var(--accent-gold-text)' }}>
                  <IconAlertTriangle size={14} className="shrink-0" aria-hidden="true" />
                  {t('manual.batch.skipped.heading', { count: skipped.length })}
                </h4>
                <ul className="space-y-1.5">
                  {skipped.map((entry) => {
                    const row = rows.find((candidate) => candidate.batchKey === entry.key)
                    const message = `${entry.key}${row ? ` · ${row.seed.title}` : ''}: ${entry.reason}`
                    return (
                      <li key={entry.key} className="whitespace-pre-line break-words font-semibold" style={{ color: 'var(--text-primary)' }}>
                        {message}
                        <ErrorDetails message={message} />
                      </li>
                    )
                  })}
                </ul>
              </section>
            )}
            {text.trim().length === 0 && !fileFailure && <p style={{ color: 'var(--text-muted)' }}>{t('manual.answer.empty')}</p>}
            {text.trim().length > 0 && checking && (
              <p className="flex items-center gap-1.5 font-semibold" style={{ color: 'var(--text-secondary)' }}>
                <IconLoader2 size={14} className="shrink-0 motion-safe:animate-spin" aria-hidden="true" />
                {t('manual.answer.checking')}
              </p>
            )}
            {text.trim().length > 0 && !checking && checkFailure && <FailureLine failure={{ message: t('manual.answer.checkFailed', { reason: checkFailure.message }), details: checkFailure.details }} />}
            {text.trim().length > 0 && !checking && result && (
              <p className="font-semibold" style={{ color: 'var(--text-secondary)' }} data-testid="batch-found">
                {t('manual.batch.panel.found', { found: result.found, unmatched: result.unmatched.length })}
              </p>
            )}
          </div>
        </section>
      )}

      {restPrompt && rest.state === 'selected' && (
        <section className="space-y-1.5">
          <h3 className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
            {t('manual.copy.promptLabel')}
          </h3>
          <textarea
            ref={restFieldRef}
            readOnly
            value={restPrompt}
            aria-label={t('manual.copy.promptLabel')}
            rows={8}
            className="block max-h-60 w-full resize-y rounded-lg p-2.5 font-mono text-xs leading-relaxed"
            style={{ background: 'var(--bg-elevated)', color: 'var(--text-secondary)', border: '1px solid var(--border-subtle)' }}
          />
        </section>
      )}

      {allDone && (
        <p className="pv-alert pv-alert-emerald text-xs font-bold" role="status" data-testid="batch-all-done">
          {t('manual.batch.allDone')}
        </p>
      )}

      {footerEl && createPortal(footer, footerEl)}
    </div>
  )
}

function FailureLine({ failure }: { failure: Failure }) {
  return (
    <div className="whitespace-pre-line break-words font-semibold" style={{ color: 'var(--accent-crimson)' }} role="alert">
      {failure.message}
      <ErrorDetails message={failure.message} details={failure.details} />
    </div>
  )
}

const STATUS_ICON: Record<RowStatus, ReactNode> = {
  pending: <IconCircleDashed size={13} aria-hidden="true" />,
  ok: <IconCheck size={13} aria-hidden="true" />,
  warning: <IconAlertTriangle size={13} aria-hidden="true" />,
  error: <IconX size={13} aria-hidden="true" />,
  notApplicable: <IconBan size={13} aria-hidden="true" />,
  saved: <IconCircleCheck size={13} aria-hidden="true" />,
}

// Pending and error have no ready-made badge class: neutral and crimson from the same tokens the other badges use
const STATUS_STYLE: Record<RowStatus, { className: string; style?: CSSProperties }> = {
  pending: { className: 'pv-badge', style: { background: 'transparent', color: 'var(--text-secondary)', borderColor: 'var(--border-strong)' } },
  ok: { className: 'pv-badge pv-badge-a' },
  warning: { className: 'pv-badge pv-badge-s' },
  error: { className: 'pv-badge', style: { background: 'var(--accent-crimson-bg)', color: 'var(--accent-crimson)', borderColor: 'var(--accent-crimson-border)' } },
  notApplicable: { className: 'pv-badge pv-badge-b' },
  saved: { className: 'pv-badge pv-badge-ok' },
}

/** The status chip: all six labels are drawn on top of each other, so the chip has the same size in every state. */
function StatusChip({ status }: { status: RowStatus }) {
  const { t } = useTranslation('ai')
  const look = STATUS_STYLE[status]
  return (
    <span className={`${look.className} justify-self-start whitespace-nowrap`} style={look.style} data-status-chip={status}>
      <StackedLabel
        active={ROW_STATUSES.indexOf(status)}
        labels={ROW_STATUSES.map((name) => (
          <span key={name} className="inline-flex items-center gap-1">
            {STATUS_ICON[name]}
            {t(`manual.batch.status.${name}`)}
          </span>
        ))}
      />
    </span>
  )
}

function BatchRowItem({ row, status, item }: { row: BatchRow; status: RowStatus; item: ManualBatchItemCheckDto | undefined }) {
  const { t } = useTranslation('ai')
  const [open, setOpen] = useState(false)
  const panelId = useId()
  const errors = item?.errors ?? []
  const warnings = item?.warnings ?? []
  const fixPrompt = item?.fixPrompt?.trim() ? item.fixPrompt : null
  const hasDetails = errors.length > 0 || warnings.length > 0 || !!item?.preview || !!item?.notApplicable || !!fixPrompt
  // The toggle keeps its room in every row, so a result appearing never changes the row's height
  const shown = open && hasDetails
  return (
    <li className="rounded-xl p-2.5" style={{ background: 'var(--bg-surface)', border: '1px solid var(--border-subtle)' }} data-testid="batch-row" data-key={row.batchKey} data-status={status}>
      <div className="grid grid-cols-[auto_minmax(0,1fr)_auto] items-start gap-x-2.5 gap-y-1.5 sm:grid-cols-[auto_minmax(0,1fr)_auto_auto]">
        <span className="pt-0.5 font-mono text-[11px] font-extrabold" style={{ color: 'var(--text-muted)' }}>
          {row.batchKey}
        </span>
        <span className="min-w-0 break-words text-sm font-bold" style={{ color: 'var(--text-primary)' }}>
          {row.seed.title}
        </span>
        <div className="col-start-2 row-start-2 sm:col-start-3 sm:row-start-1">
          <StatusChip status={status} />
        </div>
        <button
          type="button"
          className={`col-start-3 row-start-1 flex h-6 w-6 items-center justify-center rounded sm:col-start-4 ${hasDetails ? '' : 'invisible'}`}
          style={{ color: 'var(--text-secondary)' }}
          aria-expanded={shown}
          aria-controls={panelId}
          aria-label={t('manual.batch.row.details', { title: row.seed.title })}
          disabled={!hasDetails}
          tabIndex={hasDetails ? undefined : -1}
          onClick={() => setOpen((value) => !value)}
        >
          <IconChevronDown size={16} className={`transition-transform ${shown ? 'rotate-180' : ''}`} aria-hidden="true" />
        </button>
      </div>
      {shown && item && (
        <div id={panelId} className="mt-2 space-y-2 text-xs" data-testid="batch-row-details">
          {item.notApplicable && (
            <p className="whitespace-pre-line break-words font-semibold" style={{ color: 'var(--text-primary)' }}>
              {item.notApplicable}
            </p>
          )}
          {item.preview && (
            <p className="break-words font-semibold" style={{ color: 'var(--text-primary)' }}>
              {t('manual.answer.preview', { title: item.preview.title, sections: item.preview.sectionCount, rows: item.preview.tableRows, cards: item.preview.cards })}
            </p>
          )}
          {errors.length > 0 && <IssueList tone="error" heading={t('manual.answer.errors')} issues={errors} />}
          {warnings.length > 0 && <IssueList tone="warning" heading={t('manual.answer.warnings')} issues={warnings} copyable />}
          {fixPrompt && <FixPromptBar prompt={fixPrompt} />}
        </div>
      )}
    </li>
  )
}
