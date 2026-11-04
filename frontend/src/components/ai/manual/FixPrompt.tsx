import { useEffect, useId, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { IconClipboardCopy } from '@tabler/icons-react'
import { ReservedText } from '../../ui/ReservedText'
import { useCopyPrompt } from './useCopyPrompt'

/**
 * Under the warnings of a checked answer: "Copy correction prompt". The server writes a prompt that makes the chat fix exactly what the check found (for
 * example translate the sentences that are in another language); it is pasted into the same chat and the new answer replaces the old one. When the browser
 * blocks copying, the prompt is shown and selected so Ctrl+C works. The confirmation has its room reserved, so nothing around it moves.
 */
export function FixPromptBar({ prompt }: { prompt: string }) {
  const { t } = useTranslation('ai')
  const { state, copy } = useCopyPrompt()
  const fieldRef = useRef<HTMLTextAreaElement>(null)
  const hintId = useId()

  useEffect(() => {
    if (state === 'selected') {
      fieldRef.current?.focus()
      fieldRef.current?.select()
    }
  }, [state])

  return (
    <div className="space-y-1.5" data-testid="fix-prompt">
      <button type="button" className="pv-btn !px-2.5 !py-1 !text-xs" onClick={() => void copy(prompt)} aria-describedby={hintId}>
        <IconClipboardCopy size={14} className="shrink-0" aria-hidden="true" />
        <span>{t('manual.fix.button')}</span>
      </button>
      <p id={hintId} className="text-[11px] leading-snug" style={{ color: 'var(--text-muted)' }}>
        {t('manual.fix.hint')}
      </p>
      <ReservedText
        className="text-[11px] font-semibold"
        style={{ color: state === 'selected' ? 'var(--accent-gold-text)' : 'var(--accent-emerald)' }}
        candidates={[t('manual.fix.copied'), t('manual.fix.selected')]}
        role="status"
        aria-live="polite"
        data-testid="fix-prompt-status"
      >
        {state === 'copied' ? t('manual.fix.copied') : state === 'selected' ? t('manual.fix.selected') : ''}
      </ReservedText>
      {state === 'selected' && (
        <textarea
          ref={fieldRef}
          readOnly
          value={prompt}
          aria-label={t('manual.fix.promptLabel')}
          rows={6}
          className="block max-h-40 w-full resize-y rounded-lg p-2 font-mono text-[11px] leading-relaxed"
          style={{ background: 'var(--bg-elevated)', color: 'var(--text-secondary)', border: '1px solid var(--border-subtle)' }}
        />
      )}
    </div>
  )
}
