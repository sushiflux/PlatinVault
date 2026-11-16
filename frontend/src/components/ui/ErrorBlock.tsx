import { useId, useState } from 'react'
import { IconChevronRight } from '@tabler/icons-react'
import { CopyErrorButton } from './CopyErrorButton'
import '../../styles/errorblock.css'

interface ErrorBlockProps {
  /** What failed, short ("Gemini antwortet nicht"). Always visible. */
  title: string
  /** An error code that names the failure ("HTTP 429 · gemini · quota"). Always visible, copied with the text. */
  code?: string | null
  /** The readable explanation: what happened and what to do. Unfolds with the arrow, never cut. */
  message?: string | null
  /** The provider's / server's own text (monospace, under the message). */
  details?: string | null
  /** Start unfolded (a message without any other text is always shown). */
  defaultOpen?: boolean
  className?: string
}

/**
 * The error block of the app's own screens: headline + code stay visible, an arrow unfolds the full description, the copy button copies the
 * complete text (title, message, details, code, version, time) even while folded. Nothing moves when "Copied" shows (reserved slot).
 */
export function ErrorBlock({ title, code, message, details, defaultOpen = false, className = '' }: ErrorBlockProps) {
  const [open, setOpen] = useState(defaultOpen)
  const id = useId()
  const hasBody = Boolean(message || details)
  return (
    <div className={`pv-err ${className}`.trim()} role="alert" data-testid="error-block">
      <div className="pv-err__head">
        {hasBody ? (
          <button type="button" className="pv-err__toggle" aria-expanded={open} aria-controls={id} onClick={() => setOpen((value) => !value)}>
            <IconChevronRight size={18} className="pv-err__chev" aria-hidden="true" />
            <span>
              {title}
              {code ? <span className="pv-err__code"> ({code})</span> : null}
            </span>
          </button>
        ) : (
          <span className="pv-err__toggle" style={{ cursor: 'default' }}>
            <span>
              {title}
              {code ? <span className="pv-err__code"> ({code})</span> : null}
            </span>
          </span>
        )}
        <span className="pv-err__spacer" />
        <CopyErrorButton variant="text" report={{ title, message, details, code }} />
      </div>
      {hasBody && open && (
        <div className="pv-err__body" id={id}>
          {message && <p>{message}</p>}
          {details && <p className="pv-err__tech">{details}</p>}
        </div>
      )}
    </div>
  )
}
