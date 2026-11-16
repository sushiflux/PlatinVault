import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { IconClipboardText, IconRefresh, IconTrash } from '@tabler/icons-react'
import type { AiProviderId, GameDetailDto, GuideSectionDto, ManualChapterSeed } from '../../api/types'
import { ErrorDetails } from '../ui/ErrorDetails'
import { AiRunButton } from './AiRunButton'
import '../guide/guide.css'

/** The chapters of a guide whose research failed (the server marks their placeholder section with `failedChapter`). */
export function failedChapterTabs(game: GameDetailDto | undefined): { id: string; title: string }[] {
  if (!game) return []
  return game.tabs.filter((tab) => tab.sections.length === 1 && tab.sections[0]?.failedChapter).map((tab) => ({ id: tab.id, title: tab.title }))
}

/** The failed chapters as seeds for the subscription flow: each carries its tabId, so the saved answer replaces its placeholder. */
export function failedChapterSeeds(game: GameDetailDto | undefined): ManualChapterSeed[] {
  if (!game) return []
  return game.tabs
    .filter((tab) => tab.sections.length === 1 && tab.sections[0]?.failedChapter)
    .map((tab) => ({
      title: tab.title,
      icon: tab.icon,
      focus: tab.sections[0]?.failedChapter?.focus ?? tab.title,
      topicId: tab.sections[0]?.failedChapter?.topicId ?? null,
      tabId: tab.id,
    }))
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
    <div className="pv-note pv-note--warn pv-failed" data-testid="failed-chapter">
      <p className="pv-failed__title">{t('failedChapter.title')}</p>
      <p className="pv-failed__message whitespace-pre-line">{failure.message}</p>
      <ErrorDetails title={t('failedChapter.title')} message={failure.message} details={failure.providerMessage} />
      <p className="pv-failed__hint">{t('failedChapter.hint')}</p>
      <div className="pv-failed__actions no-print">
        <AiRunButton
          quiet
          busy={busy}
          disabled={busy}
          oneOff={oneOff}
          onOneOffChange={setOneOff}
          onClick={() => onRetry(oneOff)}
          icon={<IconRefresh size={18} className="shrink-0" />}
          label={<span>{t('failedChapter.retry')}</span>}
        />
        {onManual && (
          <button type="button" className="pv-btn-text pv-btn-text--calm" disabled={busy} onClick={onManual}>
            <IconClipboardText size={18} className="shrink-0" aria-hidden="true" />
            <span>{t('manual.entry.chapter')}</span>
          </button>
        )}
        <button type="button" className="pv-btn-text pv-btn-text--calm pv-btn-text--danger" disabled={busy} onClick={onRemove}>
          <IconTrash size={18} className="shrink-0" aria-hidden="true" />
          <span>{t('failedChapter.remove')}</span>
        </button>
      </div>
    </div>
  )
}

interface RetryAllBannerProps {
  count: number
  busy: boolean
  onRetryAll: (oneOff: AiProviderId | undefined) => void
  // Research all of them with the player's own AI subscription instead (one prompt, one pasted text; no API key)
  onManual?: () => void
}

/** Once at the top of a guide with several failed chapters: research all of them again. */
export function RetryAllBanner({ count, busy, onRetryAll, onManual }: RetryAllBannerProps) {
  const { t } = useTranslation('ai')
  const [oneOff, setOneOff] = useState<AiProviderId | undefined>(undefined)
  return (
    <div className="pv-note pv-note--warn pv-failed no-print" data-testid="retry-all-banner">
      <p className="pv-failed__title">{t('failedChapter.allTitle', { count })}</p>
      <p className="pv-failed__hint">{t('failedChapter.allHint')}</p>
      <div className="pv-failed__actions">
        <AiRunButton
          quiet
          busy={busy}
          disabled={busy}
          oneOff={oneOff}
          onOneOffChange={setOneOff}
          onClick={() => onRetryAll(oneOff)}
          icon={<IconRefresh size={18} className="shrink-0" />}
          label={<span>{t('failedChapter.retryAll')}</span>}
        />
        {onManual && (
          <button type="button" className="pv-btn-text pv-btn-text--calm" disabled={busy} onClick={onManual} data-testid="retry-all-manual">
            <IconClipboardText size={18} className="shrink-0" aria-hidden="true" />
            <span>{t('manual.entry.all')}</span>
          </button>
        )}
      </div>
    </div>
  )
}
