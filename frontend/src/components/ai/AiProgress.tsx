import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { IconLoader2 } from '@tabler/icons-react'
import type { AiJobDto, AiJobKind } from '../../api/types'
import { useWindowBusy } from '../../utils/windowBusy'
import { FloatingNotice } from '../ui/FloatingNotice'
import { ReservedText } from '../ui/ReservedText'
import { formatElapsed, SLOW_STEP_SECONDS } from './useAiProgress'
import type { AiProgressView } from './useAiProgress'

// A long title as the room for the longest step line, and the longest sentence the server gives for a change of AI
const SAMPLE_TITLE = 'Fähigkeiten, Upgrade-Reihenfolge, Level-Empfehlungen & Farming-Routen'
const SAMPLE_SWITCH = 'Gemini-Kontingent aufgebraucht – weiter mit Claude. OpenAI recherchiert hier ohne Websuche.'

function stepText(t: TFunction<'ai'>, step: AiJobDto['step']): string {
  const { done, total, title } = step
  switch (step.stage) {
    case 'chapters':
      return title ? t('progress.step.chapters', { done, total, title }) : t('progress.step.chaptersNoTitle', { done, total })
    case 'loreParts':
      return title ? t('progress.step.loreParts', { done, total, title }) : t('progress.step.loreNoTitle', { done, total })
    case 'chapter':
      return t('progress.step.chapter', { title: title ?? '' })
    case 'facts':
      return t('progress.step.facts')
    case 'blueprint':
      return t('progress.step.blueprint')
    case 'saving':
      return t('progress.step.saving')
    default:
      return t('progress.step.starting')
  }
}

interface AiProgressLinesProps {
  // null = the action was just started and the server has not answered yet
  view: AiProgressView | null
  align?: 'start' | 'center'
}

/**
 * What a running AI action shows: the step that really runs (from the server), a line when it had to change AI, the running time, and one calm
 * general line - which becomes a gentle "slow" hint when the step has not changed for five minutes. Every line has its room reserved for its
 * longest text in the active language, so nothing around it moves while the steps change.
 */
export function AiProgressLines({ view, align = 'start' }: AiProgressLinesProps) {
  const { t } = useTranslation('ai')
  const job = view?.job
  const step = job?.step ?? { stage: 'starting' as const, done: 0, total: 0, title: null }
  const slow = (view?.idleSeconds ?? 0) >= SLOW_STEP_SECONDS
  const alignClass = align === 'center' ? 'text-center justify-items-center' : ''

  const stepCandidates = [
    t('progress.step.starting'),
    t('progress.step.facts'),
    t('progress.step.blueprint'),
    t('progress.step.chapters', { done: 88, total: 88, title: SAMPLE_TITLE }),
    t('progress.step.chapter', { title: SAMPLE_TITLE }),
    t('progress.step.loreParts', { done: 8, total: 8, title: SAMPLE_TITLE }),
    t('progress.step.saving'),
  ]

  return (
    <div className={`space-y-1.5 ${alignClass}`} data-testid="ai-progress">
      <ReservedText className={`text-sm font-bold ${alignClass}`} style={{ color: 'var(--text-primary)' }} candidates={stepCandidates} aria-live="polite">
        {stepText(t, step)}
      </ReservedText>
      <ReservedText className={`text-xs font-semibold ${alignClass}`} style={{ color: 'var(--accent-crystal)' }} candidates={[SAMPLE_SWITCH]} aria-live="polite">
        {job?.switchNotice ?? ''}
      </ReservedText>
      <p className="text-xs font-semibold tabular-nums" style={{ color: 'var(--text-secondary)' }} data-testid="ai-progress-elapsed">
        {t('progress.elapsed', { time: formatElapsed(view?.elapsedSeconds ?? 0) })}
      </p>
      <ReservedText className={`text-xs leading-snug ${alignClass}`} style={{ color: slow ? 'var(--accent-gold-text)' : 'var(--text-muted)' }} candidates={[t('progress.general'), t('progress.slow')]}>
        {slow ? t('progress.slow') : t('progress.general')}
      </ReservedText>
    </div>
  )
}

/** The floating "progress" notice of an AI action on a page (never part of the layout, so nothing moves). */
export function AiProgressNotice({ kind, view }: { kind: AiJobKind; view: AiProgressView | null }) {
  const { t } = useTranslation('ai')
  // A long AI job runs in this window: automatic page changes (switch to a started game) hold back until it is over
  useWindowBusy()
  return <FloatingNotice tone="progress" title={t(`progress.title.${kind}`)} message={<AiProgressLines view={view} />} />
}

/** Laid over the body of a dialog while its AI action runs: the content below keeps its size, so the dialog does not change either. */
export function AiProgressOverlay({ kind, view }: { kind: AiJobKind; view: AiProgressView | null }) {
  const { t } = useTranslation('ai')
  return (
    <div
      role="status"
      aria-label={t(`progress.title.${kind}`)}
      className="absolute inset-0 z-10 flex items-center justify-center overflow-y-auto rounded-[inherit] p-6"
      style={{ background: 'color-mix(in srgb, var(--bg-surface) 94%, transparent)' }}
    >
      <div className="w-full max-w-md space-y-3 text-center">
        <IconLoader2 size={30} className="mx-auto motion-safe:animate-spin" style={{ color: 'var(--accent-crystal)' }} aria-hidden="true" />
        <p className="text-base font-extrabold" style={{ color: 'var(--text-primary)' }}>
          {t(`progress.title.${kind}`)}
        </p>
        <AiProgressLines view={view} align="center" />
      </div>
    </div>
  )
}
