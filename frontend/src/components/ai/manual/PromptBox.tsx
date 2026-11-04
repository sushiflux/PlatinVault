import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { IconChevronDown, IconClipboardCopy, IconDownload, IconLoader2 } from '@tabler/icons-react'
import { describeError } from '../../../utils/apiErrorText'
import { ReservedText } from '../../ui/ReservedText'
import { Tooltip } from '../../ui/Tooltip'
import { ErrorDetails } from '../../ui/ErrorDetails'
import { downloadText, useCopyPrompt } from './useCopyPrompt'

// What a prompt box needs of a prompt (single chapter, blueprint and batch prompts all fit)
interface PromptSource {
  prompt: string
  fileName: string
}

interface PromptBoxProps {
  // Makes the prompt (once: it is kept until the box goes away)
  load: () => Promise<PromptSource>
  // More buttons in the same row (e.g. "Paste answer")
  extraActions?: ReactNode
}

/**
 * The prompt of a step: "Copy prompt" (with a calm note about what to do next), "Export as file" (.md) and "Show prompt". When the browser
 * does not allow copying, the prompt is shown and selected so Ctrl+C works. The prompt is made on the first click and then kept.
 */
export function PromptBox({ load, extraActions }: PromptBoxProps) {
  const { t } = useTranslation(['ai', 'common'])
  const [prompt, setPrompt] = useState<PromptSource | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<{ message: string; details: string | null } | null>(null)
  const [shown, setShown] = useState(false)
  const { state, copy, show } = useCopyPrompt()
  const fieldRef = useRef<HTMLTextAreaElement>(null)

  // The clipboard was blocked: the prompt is shown with everything selected
  useEffect(() => {
    if (state === 'selected' && shown) {
      fieldRef.current?.focus()
      fieldRef.current?.select()
    }
  }, [state, shown])

  const ensure = async (): Promise<PromptSource | null> => {
    if (prompt) return prompt
    setLoading(true)
    setError(null)
    try {
      const made = await load()
      setPrompt(made)
      return made
    } catch (err: unknown) {
      setError(describeError(err, t('common:unknownError')))
      return null
    } finally {
      setLoading(false)
    }
  }

  const onCopy = async () => {
    const made = await ensure()
    if (!made) return
    if (!(await copy(made.prompt))) setShown(true)
  }

  const onFile = async () => {
    const made = await ensure()
    if (!made) return
    downloadText(made.prompt, made.fileName)
    show('exported')
  }

  const onToggle = async () => {
    if (!shown && !(await ensure())) return
    setShown((value) => !value)
  }

  const message =
    state === 'copied' ? t('manual.copy.copied') : state === 'selected' ? t('manual.copy.selected') : state === 'exported' ? t('manual.copy.exported', { name: prompt?.fileName ?? '' }) : loading ? t('manual.copy.loading') : ''

  return (
    <div className="space-y-2" data-testid="prompt-box">
      <div className="flex flex-wrap items-center gap-2">
        <Tooltip label={t('manual.row.copy')} description={t('manual.copy.hint')}>
          <button type="button" className="pv-btn pv-btn-emerald relative" onClick={() => void onCopy()} disabled={loading}>
            <span className={`inline-flex items-center gap-[inherit] ${loading ? 'invisible' : ''}`}>
              <IconClipboardCopy size={15} className="shrink-0" aria-hidden="true" />
              <span>{t('manual.row.copy')}</span>
            </span>
            {loading && (
              <span className="absolute inset-0 flex items-center justify-center" aria-hidden="true">
                <IconLoader2 size={16} className="motion-safe:animate-spin" />
              </span>
            )}
          </button>
        </Tooltip>
        <button type="button" className="pv-btn" onClick={() => void onFile()} disabled={loading}>
          <IconDownload size={15} className="shrink-0" aria-hidden="true" />
          <span>{t('manual.row.file')}</span>
        </button>
        <button type="button" className="pv-btn" onClick={() => void onToggle()} disabled={loading} aria-expanded={shown}>
          <IconChevronDown size={15} className={`shrink-0 transition-transform ${shown ? 'rotate-180' : ''}`} aria-hidden="true" />
          <span>{shown ? t('manual.copy.hidePreview') : t('manual.copy.preview')}</span>
        </button>
        {extraActions}
      </div>

      <ReservedText
        className="text-xs font-semibold"
        style={{ color: state === 'selected' ? 'var(--accent-gold-text)' : 'var(--accent-emerald)' }}
        candidates={[t('manual.copy.copied'), t('manual.copy.selected'), t('manual.copy.exported', { name: 'platinvault-final-fantasy-resonance-kapitel-beste-waffen-ruestungen.md' }), t('manual.copy.loading')]}
        role="status"
        aria-live="polite"
        data-testid="copy-status"
      >
        {message}
      </ReservedText>

      {error && (
        <div className="pv-alert pv-alert-danger whitespace-pre-line break-words text-xs font-semibold" role="alert">
          {t('manual.copy.failed', { reason: error.message })}
          <ErrorDetails message={t('manual.copy.failed', { reason: error.message })} details={error.details} />
        </div>
      )}

      {shown && prompt && (
        <textarea
          ref={fieldRef}
          readOnly
          value={prompt.prompt}
          aria-label={t('manual.copy.promptLabel')}
          rows={10}
          className="block max-h-72 w-full resize-y rounded-lg p-2.5 font-mono text-xs leading-relaxed"
          style={{ background: 'var(--bg-elevated)', color: 'var(--text-secondary)', border: '1px solid var(--border-subtle)' }}
        />
      )}
    </div>
  )
}
