import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { IconClipboardText, IconLoader2, IconSparkles, IconWand, IconX } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { completeGuide, planGuide } from '../api/client'
import type { AiCompleteGuideResponse, AiProviderId, GuidePlanDto } from '../api/types'
import { describeError } from '../utils/apiErrorText'
import { useAiPicker } from './ai/AiChoice'
import { AiProgressLines, AiProgressOverlay } from './ai/AiProgress'
import { useAiProgress } from './ai/useAiProgress'
import { ErrorDetails } from './ui/ErrorDetails'
import { AiRunButton } from './ai/AiRunButton'
import { keyOf, TopicGroup, usePlanSelection } from './ai/PlanTopics'
import { Tooltip } from './ui/Tooltip'

interface GuideBuilderDialogProps {
  slug: string
  gameTitle: string
  // The AI chosen when the builder was opened (mode "ask"); undefined = the settings decide
  provider?: string
  onClose: () => void
  onDone: (result: AiCompleteGuideResponse) => void
  // The same guide without an API key: the prompt goes into the chat of the player's own subscription (see ManualGuideDialog)
  onManual?: () => void
}

// "Guide-Baukasten": the AI analyses the game once, recommended building blocks are pre-ticked, the player adjusts
// (or takes everything) and the selection is researched – no prompt typing.
export function GuideBuilderDialog({ slug, gameTitle, provider, onClose, onDone, onManual }: GuideBuilderDialogProps) {
  const { t } = useTranslation(['game', 'common', 'ai'])
  const pickAi = useAiPicker()
  // The AI picked for this one research ("Mit Claude"); the setting itself stays as it is
  const [oneOff, setOneOff] = useState<AiProviderId | undefined>(undefined)
  const [plan, setPlan] = useState<GuidePlanDto | null>(null)
  const { groups, selectable, chosen, selected, toggle, setSelected, tickRecommended } = usePlanSelection(plan)
  // A failure is shown completely at the top of the dialog (never in a cut-off line): our sentence, a note that another AI was tried first, the
  // provider's own text behind "technical details"
  const [failure, setFailure] = useState<{ message: string; details: string | null; notice?: string } | null>(null)
  const [researching, setResearching] = useState(false)
  const [switchNotice, setSwitchNotice] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const planProgress = useAiProgress()
  const researchProgress = useAiProgress()

  useEffect(() => {
    let cancelled = false
    planGuide(slug, provider, planProgress.onProgress)
      .then((result) => {
        if (cancelled) return
        // The analysis may have had to change AI provider: said inside the dialog (a notice over it would cover the buttons on a phone)
        setSwitchNotice(result.ai?.switches[result.ai.switches.length - 1]?.text ?? null)
        setPlan(result)
        tickRecommended(result)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        const { message, details } = describeError(err, t('common:unknownError'))
        setFailure({ message: t('game:builder.analysisFailed', { reason: message }), details, notice: (err as { switchNotice?: string } | null)?.switchNotice })
      })
    return () => {
      cancelled = true
    }
      // The analysis runs once per game: a language switch (a new `t`) must not restart it
  }, [slug])

  const research = async () => {
    const picked = await pickAi(oneOff)
    if (picked === null) return
    setResearching(true)
    setFailure(null)
    try {
      onDone(
        await completeGuide(
          slug,
          chosen.map((t) => ({ topicId: t.id, title: t.title, icon: t.icon, focus: t.focus })),
          picked,
          researchProgress.onProgress
        )
      )
    } catch (err: unknown) {
      const { message, details } = describeError(err, t('common:unknownError'))
      setFailure({ message: t('game:builder.researchFailed', { reason: message }), details, notice: (err as { switchNotice?: string } | null)?.switchNotice })
      // Chapters that were finished before the failure are saved: the page shows them (and the next analysis finds them as covered)
      void queryClient.invalidateQueries({ queryKey: ['game', slug] })
    } finally {
      setResearching(false)
      researchProgress.reset()
    }
  }

  return (
    <div className="pv-modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="guide-builder-title">
      <div className="pv-modal relative flex h-[min(88dvh,760px)] w-full max-w-3xl flex-col overflow-hidden">
        <div
          className="flex items-center justify-between px-6 py-4"
          style={{ background: 'var(--bg-elevated)', borderBottom: '1px solid var(--border-subtle)' }}
        >
          <div className="min-w-0">
            <h2 id="guide-builder-title" className="flex items-center gap-2 text-base font-extrabold" style={{ color: 'var(--text-primary)' }}>
              <IconWand size={20} style={{ color: 'var(--accent-emerald)' }} />
              <span className="truncate">{t('game:builder.title', { game: gameTitle })}</span>
            </h2>
            <p className="text-xs" style={{ color: 'var(--text-muted)' }}>
              {t('game:builder.intro')}
            </p>
          </div>
          <button type="button" onClick={onClose} disabled={researching} className="pv-btn !p-1.5" aria-label={t('common:close')}>
            <IconX size={18} />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto px-6 py-5 space-y-5">
          {!plan && !failure && (
            <div className="flex h-full flex-col items-center justify-center gap-3 text-center" style={{ color: 'var(--text-secondary)' }}>
              <IconLoader2 size={30} className="motion-safe:animate-spin" style={{ color: 'var(--accent-crystal)' }} aria-hidden="true" />
              <p className="text-sm font-semibold">{t('ai:progress.title.plan')}</p>
              <div className="w-full max-w-md">
                <AiProgressLines view={planProgress.view} align="center" />
              </div>
            </div>
          )}

          {failure && (
            <div className="pv-alert pv-alert-danger whitespace-pre-line break-words text-sm font-semibold" role="alert">
              {failure.notice && (
                <p className="mb-1.5 text-xs" style={{ color: 'var(--accent-crystal)' }}>
                  {failure.notice}
                </p>
              )}
              {failure.message}
              <ErrorDetails details={failure.details} />
              {onManual && (
                <button type="button" className="pv-btn mt-2" onClick={onManual}>
                  <IconClipboardText size={15} className="shrink-0" aria-hidden="true" />
                  <span>{t('ai:manual.entry.builder')}</span>
                </button>
              )}
            </div>
          )}

          {plan && groups && (
            <>
              {switchNotice && (
                <div className="pv-alert pv-alert-crystal text-xs font-semibold" role="status">
                  {switchNotice}
                </div>
              )}
              <div className="flex flex-wrap items-center gap-2 text-xs" style={{ color: 'var(--text-secondary)' }}>
                <span className="font-bold">{t('game:builder.genre')}</span>
                {plan.profiles.length > 0 ? (
                  plan.profiles.map((p) => (
                    <Tooltip key={p.id} label={p.fromSteamTags ? t('game:builder.genreFromSteam') : t('game:builder.genreFromAi')}>
                      <span className="pv-badge pv-badge-a">
                        {p.name}
                        {p.fromSteamTags ? ' · Steam' : ''}
                      </span>
                    </Tooltip>
                  ))
                ) : (
                  <span>{t('game:builder.noGenre')}</span>
                )}
              </div>

              {plan.budget && (
                <p className="text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }} data-testid="plan-budget">
                  {t('ai:planBudget.note', { suggested: plan.budget.suggested, scope: t(`ai:planBudget.scope.${plan.budget.scope}`), min: plan.budget.min, max: plan.budget.max })}
                </p>
              )}

              <TopicGroup title={t('game:builder.groups.recommended')} topics={groups.recommended} selected={selected} onToggle={toggle} disabled={researching} />
              <TopicGroup title={t('game:builder.groups.other')} hint={t('game:builder.groups.otherHint')} topics={groups.other} selected={selected} onToggle={toggle} disabled={researching} />
              {groups.covered.length > 0 && (
                <TopicGroup title={t('game:builder.groups.covered')} topics={groups.covered} selected={selected} onToggle={toggle} disabled />
              )}
            </>
          )}
        </div>

        <div
          className="flex flex-wrap items-center justify-between gap-3 px-6 py-4"
          style={{ background: 'var(--bg-elevated)', borderTop: '1px solid var(--border-subtle)' }}
        >
          <div className="flex flex-wrap gap-2">
            {onManual && (
              <button type="button" className="pv-btn" disabled={researching} onClick={onManual}>
                <IconClipboardText size={15} className="shrink-0" aria-hidden="true" />
                <span>{t('ai:manual.entry.builder')}</span>
              </button>
            )}
            <button
              type="button"
              className="pv-btn"
              disabled={!plan || researching}
              onClick={() => setSelected(new Set(selectable.map(keyOf)))}
            >
              {t('game:builder.all')}
            </button>
            <button
              type="button"
              className="pv-btn"
              disabled={!plan || researching}
              onClick={() => groups && setSelected(new Set(groups.recommended.map(keyOf)))}
            >
              {t('game:builder.onlyRecommended')}
            </button>
          </div>
          <div className="flex w-full min-w-0 items-center justify-end gap-3 sm:w-auto sm:flex-1">
            <AiRunButton
              tone="pv-btn-emerald"
              busy={researching}
              disabled={!plan || chosen.length === 0}
              oneOff={oneOff}
              onOneOffChange={setOneOff}
              onClick={() => void research()}
              icon={<IconSparkles size={15} />}
              label={<span>{t('game:builder.research', { count: chosen.length })}</span>}
            />
          </div>
        </div>
        {researching && <AiProgressOverlay kind="complete" view={researchProgress.view} />}
      </div>
    </div>
  )
}
