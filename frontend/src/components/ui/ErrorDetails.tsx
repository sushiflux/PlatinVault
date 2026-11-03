import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { IconChevronDown } from '@tabler/icons-react'

/**
 * "Technical details" under an error: the provider's own text, folded away until asked for, shown COMPLETELY when opened (it wraps, it is never
 * cut off). Nothing renders without details.
 */
export function ErrorDetails({ details }: { details: string | null | undefined }) {
  const { t } = useTranslation('game')
  const [open, setOpen] = useState(false)
  const id = useId()
  if (!details) return null
  return (
    <div className="mt-1.5">
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
      {open && (
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
