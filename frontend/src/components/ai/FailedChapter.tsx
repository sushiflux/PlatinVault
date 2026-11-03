import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { IconAlertTriangle, IconClipboardText, IconRefresh, IconTrash } from '@tabler/icons-react'
import type { AiProviderId, GameDetailDto, GuideSectionDto } from '../../api/types'
import { ErrorDetails } from '../ui/ErrorDetails'
import { AiRunButton } from './AiRunButton'

/** The chapters of a guide whose research failed (the server marks their placeholder section with `failedChapter`). */
export function failedChapterTabs(game: GameDetailDto | undefined): { id: string; title: string }[] {
  if (!game) return []
  return game.tabs.filter((tab) => tab.sections.length === 1 && tab.sections[0]?.failedChapter).map((tab) => ({ id: tab.id, title: tab.title }))
}

interface FailedChapterBlockProps {
  section: GuideSectionDto
  // A research runs on this page: the button waits
  busy: boolean
  onRetry: (oneOff: AiProviderId | undefined) => void
  // The chapter is not needed (the game does not have the topic): take it out of the guide
  onRemove: () => void
  // Research it with the player's own AI subscription instead (copy the prompt, paste the answer; no API key)
  onManual?: () => void
}

/**
 * What a chapter shows whose research failed: what happened in one calm sentence (our explanation, the provider's own text behind "technical
 * details"), that its topic is kept, and one button that researches exactly this chapter again - with the AI selection, fallback included.
 */
export function FailedChapterBlock({ section, busy, onRetry, onRemove, onManual }: FailedChapterBlockProps) {
  const { t } = useTranslation('ai')
  const [oneOff, setOneOff] = useState<AiProviderId | undefined>(undefined)
  const failure = section.failedChapter
  if (!failure) return null
  return (
    <div className="pv-alert pv-alert-gold" data-testid="failed-chapter">
      <div className="mt-0.5 shrink-0" style={{ color: 'var(--accent-gold)' }}>
        <IconAlertTriangle size={22} aria-hidden="true" />
      </div>
      <div className="min-w-0 flex-1 space-y-2">
        <div className="text-sm font-extrabold sm:text-base" style={{ color: 'var(--text-primary)' }}>
          {t('failedChapter.title')}
        </div>
        <p className="whitespace-pre-line break-words text-sm font-semibold" style={{ color: 'var(--text-primary)' }}>
          {failure.message}
        </p>
        <ErrorDetails details={failure.providerMessage} />
        <p className="text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
          {t('failedChapter.hint')}
        </p>
        <div className="no-print flex flex-wrap items-center gap-2 pt-1">
          <AiRunButton
            tone="pv-btn-emerald"
            busy={busy}
            disabled={busy}
            oneOff={oneOff}
            onOneOffChange={setOneOff}
            onClick={() => onRetry(oneOff)}
            icon={<IconRefresh size={15} className="shrink-0" />}
            label={<span>{t('failedChapter.retry')}</span>}
          />
          {onManual && (
            <button type="button" className="pv-btn" disabled={busy} onClick={onManual}>
              <IconClipboardText size={15} className="shrink-0" aria-hidden="true" />
              <span>{t('manual.entry.chapter')}</span>
            </button>
          )}
          <button type="button" className="pv-btn" disabled={busy} onClick={onRemove}>
            <IconTrash size={15} className="shrink-0" aria-hidden="true" />
            <span>{t('failedChapter.remove')}</span>
          </button>
        </div>
      </div>
    </div>
  )
}

interface RetryAllBannerProps {
  count: number
  busy: boolean
  onRetryAll: (oneOff: AiProviderId | undefined) => void
}

/** Once at the top of a guide with several failed chapters: research all of them again. */
export function RetryAllBanner({ count, busy, onRetryAll }: RetryAllBannerProps) {
  const { t } = useTranslation('ai')
  const [oneOff, setOneOff] = useState<AiProviderId | undefined>(undefined)
  return (
    <div className="pv-alert pv-alert-gold no-print" data-testid="retry-all-banner">
      <div className="mt-0.5 shrink-0" style={{ color: 'var(--accent-gold)' }}>
        <IconAlertTriangle size={22} aria-hidden="true" />
      </div>
      <div className="min-w-0 flex-1 space-y-2">
        <div className="text-sm font-extrabold sm:text-base" style={{ color: 'var(--text-primary)' }}>
          {t('failedChapter.allTitle', { count })}
        </div>
        <p className="text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
          {t('failedChapter.allHint')}
        </p>
        <div className="pt-1">
          <AiRunButton
            tone="pv-btn-emerald"
            busy={busy}
            disabled={busy}
            oneOff={oneOff}
            onOneOffChange={setOneOff}
            onClick={() => onRetryAll(oneOff)}
            icon={<IconRefresh size={15} className="shrink-0" />}
            label={<span>{t('failedChapter.retryAll')}</span>}
          />
        </div>
      </div>
    </div>
  )
}
