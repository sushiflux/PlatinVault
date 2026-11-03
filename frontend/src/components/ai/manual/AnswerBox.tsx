import { useEffect, useId, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { IconAlertTriangle, IconCheck, IconFileImport, IconLoader2, IconTrash } from '@tabler/icons-react'
import type { ManualIssueDto } from '../../../api/types'
import { describeError } from '../../../utils/apiErrorText'
import { ErrorDetails } from '../../ui/ErrorDetails'

// The answer of a chat is a few kilobytes; a file bigger than this is not one
const MAX_FILE_BYTES = 2_100_000
const CHECK_DELAY_MS = 450

interface Checked {
  ok: boolean
  errors: ManualIssueDto[]
  warnings: ManualIssueDto[]
}

interface AnswerBoxProps<T extends Checked> {
  // Asks the server to read the pasted text (never an AI): errors/warnings come in the language of the UI
  check: (text: string) => Promise<T>
  // The latest result for the text as it is now (null while nothing valid is there), together with that text
  onResult: (result: T | null, text: string) => void
  // What to show when the answer was read (a short preview)
  renderOk: (result: T) => ReactNode
  disabled?: boolean
}

/**
 * The paste area for the answer of a chat: a text field (or a file), checked live while the text is pasted or typed. The result panel has a fixed
 * height and scrolls inside, so a message appearing never moves anything around it. Every error and note is shown completely.
 */
export function AnswerBox<T extends Checked>({ check, onResult, renderOk, disabled = false }: AnswerBoxProps<T>) {
  const { t } = useTranslation(['ai', 'common'])
  const [text, setText] = useState('')
  const [result, setResult] = useState<T | null>(null)
  const [checking, setChecking] = useState(false)
  const [failure, setFailure] = useState<{ message: string; details: string | null } | null>(null)
  const [fileError, setFileError] = useState<string | null>(null)
  const latest = useRef(0)
  const fileInput = useRef<HTMLInputElement>(null)
  const labelId = useId()
  // The latest callbacks without restarting the debounce when a parent re-renders
  const checkRef = useRef(check)
  const resultRef = useRef(onResult)
  useEffect(() => {
    checkRef.current = check
    resultRef.current = onResult
  })

  // Live: a short pause after the last change, then the server reads the text; only the newest answer counts
  useEffect(() => {
    latest.current += 1
    const mine = latest.current
    setFailure(null)
    if (text.trim().length === 0) {
      setResult(null)
      setChecking(false)
      resultRef.current(null, text)
      return
    }
    setChecking(true)
    const timer = window.setTimeout(async () => {
      try {
        const answer = await checkRef.current(text)
        if (mine !== latest.current) return
        setResult(answer)
        resultRef.current(answer.ok ? answer : null, text)
      } catch (err: unknown) {
        if (mine !== latest.current) return
        setResult(null)
        setFailure(describeError(err, t('common:unknownError')))
        resultRef.current(null, text)
      } finally {
        if (mine === latest.current) setChecking(false)
      }
    }, CHECK_DELAY_MS)
    return () => window.clearTimeout(timer)
    // `t` is stable per language; a language switch must not re-check
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [text])

  const loadFile = async (file: File | undefined) => {
    if (!file) return
    setFileError(null)
    if (file.size > MAX_FILE_BYTES) {
      setFileError(t('manual.answer.fileFailed', { reason: `${Math.round(file.size / 1000)} KB` }))
      return
    }
    try {
      setText(await file.text())
    } catch (err: unknown) {
      setFileError(t('manual.answer.fileFailed', { reason: describeError(err, t('common:unknownError')).message }))
    }
  }

  const empty = text.trim().length === 0

  return (
    <div className="space-y-2" data-testid="answer-box">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <label id={labelId} htmlFor={`${labelId}-field`} className="text-xs font-extrabold uppercase tracking-wider" style={{ color: 'var(--text-muted)' }}>
          {t('manual.answer.label')}
        </label>
        <div className="flex gap-2">
          <input ref={fileInput} type="file" accept=".txt,.md,.json,text/plain,text/markdown,application/json" className="sr-only" tabIndex={-1} aria-hidden="true" onChange={(e) => { void loadFile(e.target.files?.[0]); e.target.value = '' }} />
          <button type="button" className="pv-btn !px-2.5 !py-1 !text-xs" disabled={disabled} onClick={() => fileInput.current?.click()}>
            <IconFileImport size={14} className="shrink-0" aria-hidden="true" />
            <span>{t('manual.answer.loadFile')}</span>
          </button>
          <button type="button" className="pv-btn !px-2.5 !py-1 !text-xs" disabled={disabled || empty} onClick={() => { setText(''); setFileError(null) }}>
            <IconTrash size={14} className="shrink-0" aria-hidden="true" />
            <span>{t('manual.answer.clear')}</span>
          </button>
        </div>
      </div>

      <textarea
        id={`${labelId}-field`}
        value={text}
        onChange={(e) => setText(e.target.value)}
        disabled={disabled}
        spellCheck={false}
        rows={6}
        placeholder={t('manual.answer.placeholder')}
        aria-invalid={!empty && result !== null && !result.ok ? true : undefined}
        className="block max-h-64 min-h-[8rem] w-full resize-y rounded-lg p-2.5 font-mono text-xs leading-relaxed"
        style={{ background: 'var(--bg-elevated)', color: 'var(--text-primary)', border: '1px solid var(--border-subtle)' }}
      />

      <div
        className="h-40 space-y-2 overflow-y-auto rounded-lg p-2.5 text-xs"
        style={{ background: 'var(--bg-surface)', border: '1px solid var(--border-subtle)' }}
        role="status"
        aria-live="polite"
        data-testid="answer-result"
      >
        {fileError && (
          <p className="whitespace-pre-line break-words font-semibold" style={{ color: 'var(--accent-crimson)' }}>
            {fileError}
          </p>
        )}
        {empty && !fileError && <p style={{ color: 'var(--text-muted)' }}>{t('manual.answer.empty')}</p>}
        {!empty && checking && (
          <p className="flex items-center gap-1.5 font-semibold" style={{ color: 'var(--text-secondary)' }}>
            <IconLoader2 size={14} className="shrink-0 motion-safe:animate-spin" aria-hidden="true" />
            {t('manual.answer.checking')}
          </p>
        )}
        {!empty && !checking && failure && (
          <div className="whitespace-pre-line break-words font-semibold" style={{ color: 'var(--accent-crimson)' }}>
            {t('manual.answer.checkFailed', { reason: failure.message })}
            <ErrorDetails details={failure.details} />
          </div>
        )}
        {!empty && !checking && result && result.errors.length > 0 && (
          <IssueList tone="error" heading={t('manual.answer.errors')} issues={result.errors} />
        )}
        {!empty && !checking && result && result.ok && (
          <div className="space-y-1">
            <p className="flex items-start gap-1.5 font-extrabold" style={{ color: 'var(--accent-emerald)' }}>
              <IconCheck size={15} className="mt-px shrink-0" aria-hidden="true" />
              <span>{t('manual.answer.ok')}</span>
            </p>
            <div className="break-words font-semibold" style={{ color: 'var(--text-primary)' }}>
              {renderOk(result)}
            </div>
          </div>
        )}
        {!empty && !checking && result && result.warnings.length > 0 && (
          <IssueList tone="warning" heading={t('manual.answer.warnings')} issues={result.warnings} />
        )}
      </div>
    </div>
  )
}

function IssueList({ tone, heading, issues }: { tone: 'error' | 'warning'; heading: string; issues: ManualIssueDto[] }) {
  const color = tone === 'error' ? 'var(--accent-crimson)' : 'var(--accent-gold-text)'
  return (
    <section className="space-y-1">
      <h4 className="flex items-center gap-1.5 font-extrabold" style={{ color }}>
        <IconAlertTriangle size={14} className="shrink-0" aria-hidden="true" />
        {heading}
      </h4>
      <ul className="space-y-1.5">
        {issues.map((issue, index) => (
          <li key={`${issue.code}-${index}`} className="whitespace-pre-line break-words font-semibold" style={{ color: 'var(--text-primary)' }} data-issue={issue.code}>
            {issue.message}
            <ErrorDetails details={issue.details} />
          </li>
        ))}
      </ul>
    </section>
  )
}
