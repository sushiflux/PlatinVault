import { useId, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { IconRestore, IconSparkles } from '@tabler/icons-react'
import { resetAiProvider } from '../../api/client'
import type { AiProviderId, AiProviderStatusDto, AiSelectionMode } from '../../api/types'
import { useFormat } from '../../i18n/format'
import { AI_PROVIDERS, useProviderStateText } from '../ai/aiProviders'
import { AI_STATUS_KEY, useAiStatus } from '../ai/useAiStatus'
import { ReservedText } from '../ui/ReservedText'
import { Switch } from '../ui/Switch'
import { Tooltip } from '../ui/Tooltip'

const MODES: AiSelectionMode[] = ['default', 'mixed', 'ask']

interface AiSelectionSectionProps {
  mode: AiSelectionMode
  onModeChange: (mode: AiSelectionMode) => void
  defaultProvider: AiProviderId | null
  onDefaultProviderChange: (provider: AiProviderId) => void
  autoFallback: boolean
  onAutoFallbackChange: (value: boolean) => void
  // Which providers have a key right now: saved, or typed in the form below and not marked for removal
  hasKey: Record<AiProviderId, boolean>
}

// "KI-Auswahl": how PlatinVault picks the AI (Standard-KI / mixed / ask), the Standard-KI itself with the state of every provider, and the
// automatic fallback. Saved with the rest of the settings. The state column and the hint lines reserve the room of their longest text,
// so switching a mode or a provider failing never moves anything.
export function AiSelectionSection({ mode, onModeChange, defaultProvider, onDefaultProviderChange, autoFallback, onAutoFallbackChange, hasKey }: AiSelectionSectionProps) {
  const { t } = useTranslation('ai')
  const format = useFormat()
  const groupId = useId()
  const queryClient = useQueryClient()
  const { data: status, error: statusError } = useAiStatus()
  const stateText = useProviderStateText()
  const [resetting, setResetting] = useState<AiProviderId | null>(null)
  const [resetError, setResetError] = useState<string | null>(null)

  const byId = new Map<string, AiProviderStatusDto>((status?.providers ?? []).map((p) => [p.id, p]))
  const sample = format.date(new Date(), 'dateTimeShort')
  const stateCandidates = [
    t('noKey'),
    t('state.ok'),
    t('stateSince.quota', { time: sample }),
    t('stateSince.invalidKey', { time: sample }),
    t('stateSince.unavailable', { time: sample }),
  ]

  const reset = async (provider: AiProviderId) => {
    setResetting(provider)
    setResetError(null)
    try {
      queryClient.setQueryData(AI_STATUS_KEY, await resetAiProvider(provider))
    } catch (err: unknown) {
      setResetError(t('selection.resetFailed', { reason: err instanceof Error ? err.message : '?' }))
    } finally {
      setResetting(null)
    }
  }

  return (
    <div className="pv-card space-y-4" data-testid="ai-selection">
      <div className="flex items-center gap-2 text-sm font-extrabold" style={{ color: 'var(--accent-emerald)' }}>
        <IconSparkles size={18} />
        <span>{t('selection.title')}</span>
      </div>
      <p className="text-[11px] leading-snug" style={{ color: 'var(--text-muted)' }}>
        {t('selection.intro')}
      </p>

      <div>
        <div className="pv-segmented w-full" role="group" aria-label={t('selection.modeGroup')}>
          {MODES.map((value) => (
            <button
              key={value}
              type="button"
              aria-pressed={mode === value}
              onClick={() => onModeChange(value)}
              className="flex-1 justify-center text-center leading-tight"
              data-mode={value}
            >
              {t(`selection.modes.${value}.label`)}
            </button>
          ))}
        </div>
        <ReservedText
          className="mt-1.5 text-[11px] leading-snug"
          style={{ color: 'var(--text-secondary)' }}
          candidates={MODES.map((value) => t(`selection.modes.${value}.hint`))}
          aria-live="polite"
        >
          {t(`selection.modes.${mode}.hint`)}
        </ReservedText>
      </div>

      <div className="space-y-2">
        <div className="text-xs font-bold" style={{ color: 'var(--text-secondary)' }}>
          {t('selection.listTitle')}
        </div>
        <div role="radiogroup" aria-label={t('selection.listTitle')} className="space-y-2">
          {AI_PROVIDERS.map((id) => {
            const info = byId.get(id)
            const keyed = hasKey[id]
            const isDefault = defaultProvider === id
            const selectable = keyed && mode === 'default'
            const failing = keyed && info !== undefined && info.state !== 'ok'
            const radioId = `${groupId}-${id}`
            return (
              <div
                key={id}
                className="flex flex-wrap items-center gap-x-3 gap-y-1.5 rounded-xl px-3 py-2.5"
                style={{
                  background: isDefault && keyed ? 'var(--accent-emerald-bg)' : 'var(--bg-elevated)',
                  border: `1px solid ${isDefault && keyed ? 'var(--accent-emerald-border)' : 'var(--border-subtle)'}`,
                  opacity: keyed ? 1 : 0.75,
                }}
                data-provider={id}
              >
                <label htmlFor={radioId} className={`flex min-w-0 flex-1 basis-40 items-center gap-2.5 ${selectable ? 'cursor-pointer' : 'cursor-default'}`}>
                  <input
                    id={radioId}
                    type="radio"
                    name={`${groupId}-default`}
                    checked={isDefault && keyed}
                    disabled={!selectable}
                    onChange={() => onDefaultProviderChange(id)}
                    aria-label={t('selection.defaultRadio', { provider: t(`providers.${id}`) })}
                    style={{ accentColor: 'var(--accent-emerald)' }}
                  />
                  <span className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-0.5">
                    <span className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
                      {t(`providers.${id}`)}
                    </span>
                    {/* Always in the layout; only visible for the Standard-KI */}
                    <span className={`pv-badge pv-badge-ok !py-0 !text-[10px] ${isDefault && keyed ? '' : 'invisible'}`} aria-hidden={!(isDefault && keyed)}>
                      {t('selection.defaultBadge')}
                    </span>
                    {!(info?.webSearch ?? id !== 'openai') && (
                      <Tooltip label={t('noWebSearch')} description={t('noWebSearchHint')}>
                        <span className="pv-badge pv-badge-s !py-0 !text-[10px]">{t('noWebSearch')}</span>
                      </Tooltip>
                    )}
                  </span>
                </label>

                <div className="flex items-center gap-2">
                  <ReservedText
                    className="text-xs font-semibold"
                    style={{ color: failing ? (info.state === 'invalidKey' ? 'var(--accent-crimson)' : 'var(--accent-gold-text)') : 'var(--text-muted)' }}
                    candidates={stateCandidates}
                    data-testid={`ai-state-${id}`}
                  >
                    {!keyed ? t('noKey') : info ? stateText(info) : t('state.ok')}
                  </ReservedText>
                  <Tooltip label={t('selection.reset')} description={t('selection.resetHint')} disabled={!failing}>
                    <button
                      type="button"
                      className={`pv-btn !px-2 !py-1 !text-xs ${failing ? '' : 'invisible'}`}
                      disabled={!failing || resetting !== null}
                      tabIndex={failing ? 0 : -1}
                      aria-hidden={failing ? undefined : true}
                      onClick={() => void reset(id)}
                    >
                      <IconRestore size={14} />
                      <span>{t('selection.reset')}</span>
                    </button>
                  </Tooltip>
                </div>
              </div>
            )
          })}
        </div>
        <p className="text-[11px] leading-snug" style={{ color: 'var(--text-muted)' }}>
          {t('selection.listHint')}
        </p>
        {(resetError || statusError) && (
          <p className="text-[11px] font-semibold" role="alert" style={{ color: 'var(--accent-crimson)' }}>
            {resetError ?? t('selection.statusLoadFailed', { reason: statusError instanceof Error ? statusError.message : '?' })}
          </p>
        )}
      </div>

      <div className="space-y-1.5 pt-1">
        <Switch
          checked={autoFallback}
          onChange={onAutoFallbackChange}
          disabled={mode === 'ask'}
          className="flex w-full items-center gap-2.5 text-xs font-semibold text-[color:var(--text-secondary)]"
        >
          {t('selection.fallback.label')}
        </Switch>
        <ReservedText
          className="text-[11px] leading-snug"
          style={{ color: 'var(--text-muted)' }}
          candidates={[t('selection.fallback.hintOn'), t('selection.fallback.hintOff'), t('selection.fallback.hintAsk')]}
        >
          {mode === 'ask' ? t('selection.fallback.hintAsk') : autoFallback ? t('selection.fallback.hintOn') : t('selection.fallback.hintOff')}
        </ReservedText>
      </div>
    </div>
  )
}
