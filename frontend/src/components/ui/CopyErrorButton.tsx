import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { IconAlertTriangle, IconCheck, IconCopy } from '@tabler/icons-react'
import { copyText } from '../../utils/clipboard'
import { buildErrorReport, type ErrorReportInput } from '../../utils/errorReport'
import { StackedLabel } from './StackedLabel'

interface CopyErrorButtonProps {
  /**
   * What to copy: the failure's title, message, technical details and code - or a function that reads them at the moment of the click
   * (a notice whose message is markup reads its text from the page then). The app version and the time are added.
   */
  report: ErrorReportInput | (() => ErrorReportInput)
  className?: string
  /** No error right now: the button keeps its room but is invisible and cannot be reached (a slot that must not change size when an error appears). */
  inactive?: boolean
  /** Text colour of the button (default: the secondary text colour); for a fixed dark surface. */
  color?: string
  /** "text": a frameless text button with a 48 px target (error blocks, dialogs); default "inline" sits within a line of text. */
  variant?: 'inline' | 'text'
  /** Language of the button labels and of the copied text; default: the interface language (status pages pass "en"). */
  language?: string
  /** Visible text of the button when it differs from the default ("Copy details" on the status pages). */
  label?: string
}

/**
 * The one "Copy" button of every error message in the app (rule: an error message can always be copied - the text stays selectable
 * AND this button copies all of it in one click). A text button like the "Technical details" toggle it sits beside. The label swaps
 * inside a fixed slot (Copy / Copied / Copy not possible, stacked in one grid cell), so the button and everything around it keep their
 * size when the confirmation shows (rigid layout). A blocked clipboard says so instead of failing silently.
 */
export function CopyErrorButton({ report, className = '', inactive = false, color = 'var(--text-secondary)', variant = 'inline', language, label }: CopyErrorButtonProps) {
  const { t: translate } = useTranslation('errors')
  // A fixed language (status pages are always English) is passed per call; without one the interface language decides
  const t = ((key: string, options?: object) => translate(key as never, { ...options, lng: language } as never)) as typeof translate
  const [state, setState] = useState<'idle' | 'done' | 'failed'>('idle')
  const timer = useRef<number | undefined>(undefined)

  useEffect(() => () => window.clearTimeout(timer.current), [])

  const copy = async () => {
    const input = typeof report === 'function' ? report() : report
    const ok = await copyText(buildErrorReport(language ? { ...input, language } : input))
    setState(ok ? 'done' : 'failed')
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => setState('idle'), ok ? 2500 : 5000)
  }

  return (
    <span className={`inline-flex items-center ${inactive ? 'invisible' : ''} ${className}`.trim()} aria-hidden={inactive ? true : undefined} data-copy-error="">
      <button
        type="button"
        onClick={() => void copy()}
        disabled={inactive}
        tabIndex={inactive ? -1 : undefined}
        aria-label={t('copy.label')}
        className={
          variant === 'text'
            ? 'pv-btn-text'
            : 'pv-hit inline-flex cursor-pointer items-center text-sm font-medium underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--focus)]'
        }
        style={variant === 'text' ? undefined : { color }}
        data-testid="copy-error"
      >
        <StackedLabel
          active={state === 'idle' ? 0 : state === 'done' ? 1 : 2}
          align="start"
          labels={[
            <span key="copy" className="inline-flex items-center gap-1">
              <IconCopy size={14} aria-hidden="true" />
              {label ?? t('copy.button')}
            </span>,
            <span key="done" className="inline-flex items-center gap-1" style={{ color: 'var(--accent-emerald)' }}>
              <IconCheck size={14} aria-hidden="true" />
              {t('copy.done')}
            </span>,
            <span key="failed" className="inline-flex items-center gap-1" style={{ color: 'var(--accent-crimson)' }}>
              <IconAlertTriangle size={14} aria-hidden="true" />
              {t('copy.failed')}
            </span>,
          ]}
        />
      </button>
      {/* The outcome for screen readers (the visible label is hidden from them while it is not the active one) */}
      <span aria-live="polite" className="sr-only">
        {state === 'done' ? t('copy.doneAnnouncement') : state === 'failed' ? t('copy.failedAnnouncement') : ''}
      </span>
    </span>
  )
}
