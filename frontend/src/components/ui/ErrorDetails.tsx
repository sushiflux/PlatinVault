import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { IconChevronDown } from '@tabler/icons-react'
import { CopyErrorButton } from './CopyErrorButton'

interface ErrorDetailsProps {
  /** The provider's / server's own text, folded away behind "Technical details". */
  details?: string | null
  /** The readable message exactly as the error block above shows it: given, the row also carries the "Copy" button for it. */
  message?: string | null
  /** What happened, short (the heading of the error block); copied together with the message. */
  title?: string | null
  /** A code that names the failure ("HTTP 502 · gemini · quota"); copied with the message. */
  code?: string | null
  /** Keep the room of the row while there is nothing to show (an error that appears inside a dialog must not move the buttons below it). */
  reserve?: boolean
}

/**
 * The line under an error: "Technical details" (the provider's own text, folded away until asked for, shown COMPLETELY when opened - it
 * wraps, it is never cut off) and the "Copy" button that copies message + details + code + app version + time in one click (rule: every
 * error message is copyable). Nothing renders when there is neither a message nor details.
 */
export function ErrorDetails({ details, message, title, code, reserve = false }: ErrorDetailsProps) {
  const { t } = useTranslation('game')
  const [open, setOpen] = useState(false)
  const id = useId()
  if (!details && !message) return reserve ? <div className="mt-1.5 h-5" aria-hidden="true" /> : null
  return (
    <div className="mt-1.5">
      <div className="flex min-h-5 flex-wrap items-center gap-x-4 gap-y-1">
        {details && (
          <button
            type="button"
            aria-expanded={open}
            aria-controls={id}
            onClick={() => setOpen((value) => !value)}
            className="inline-flex cursor-pointer items-center gap-1 text-xs font-bold underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent-crystal)]"
            style={{ color: 'var(--text-secondary)' }}
          >
            <IconChevronDown size={14} aria-hidden="true" className={`transition-transform ${open ? 'rotate-180' : ''}`} />
            {t('notice.details')}
          </button>
        )}
        <CopyErrorButton report={{ title, message, details, code }} />
      </div>
      {open && details && (
        <pre
          id={id}
          className="mt-1.5 max-h-60 overflow-auto whitespace-pre-wrap break-words p-2.5 font-mono text-xs font-normal leading-relaxed"
          style={{ background: 'var(--bg-elevated)', color: 'var(--text-secondary)', border: '1px solid var(--border-subtle)', borderRadius: 'var(--radius-sm)' }}
        >
          {details}
        </pre>
      )}
    </div>
  )
}
