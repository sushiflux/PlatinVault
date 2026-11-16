import { useId, useState } from 'react'
import { ErrorBlock } from '../ui/ErrorBlock'
import { SettingsRow, SettingsToggle, SettingsTopic } from './SettingsParts'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { IconRestore } from '@tabler/icons-react'
import { resetAiProvider } from '../../api/client'
import type { AiProviderId, AiProviderStatusDto, AiSelectionMode } from '../../api/types'
import { useFormat } from '../../i18n/format'
import { AI_PROVIDERS, useProviderStateText } from '../ai/aiProviders'
import { AI_STATUS_KEY, useAiStatus } from '../ai/useAiStatus'
import { ReservedText } from '../ui/ReservedText'
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

  const fallbackHint = mode === 'ask' ? t('selection.fallback.hintAsk') : autoFallback ? t('selection.fallback.hintOn') : t('selection.fallback.hintOff')
  const errorText = resetError ?? (statusError ? t('selection.statusLoadFailed', { reason: statusError instanceof Error ? statusError.message : '?' }) : null)

  return (
    <div data-testid="ai-selection">
      <SettingsTopic>{t('selection.title')}</SettingsTopic>
      <SettingsRow
        wide
        title={t('selection.modeGroup')}
        titleId={`${groupId}-mode`}
        help={
          <>
            <span className="block">{t('selection.intro')}</span>
            <ReservedText
              className="mt-1"
              candidates={MODES.map((value) => t(`selection.modes.${value}.hint`))}
              aria-live="polite"
              style={{ color: 'var(--text-secondary)' }}
            >
              {t(`selection.modes.${mode}.hint`)}
            </ReservedText>
          </>
        }
      >
        <div className="pv-segmented w-full" role="group" aria-labelledby={`${groupId}-mode`}>
          {MODES.map((value) => (
            <button key={value} type="button" aria-pressed={mode === value} onClick={() => onModeChange(value)} data-mode={value}>
              {t(`selection.modes.${value}.label`)}
            </button>
          ))}
        </div>
      </SettingsRow>

      <SettingsRow wide title={t('selection.listTitle')} titleId={`${groupId}-list`} help={t('selection.listHint')}>
        <ul role="radiogroup" aria-labelledby={`${groupId}-list`} className="pv-set-list w-full">
          {AI_PROVIDERS.map((id) => {
            const info = byId.get(id)
            const keyed = hasKey[id]
            const isDefault = defaultProvider === id
            const selectable = keyed && mode === 'default'
            const failing = keyed && info !== undefined && info.state !== 'ok'
            const radioId = `${groupId}-${id}`
            return (
              <li key={id} data-provider={id}>
                <label htmlFor={radioId} className={`flex min-h-12 min-w-0 flex-1 basis-40 items-center gap-3 py-2 ${selectable ? 'cursor-pointer' : 'cursor-default'}`}>
                  <input
                    id={radioId}
                    type="radio"
                    name={`${groupId}-default`}
                    checked={isDefault && keyed}
                    disabled={!selectable}
                    onChange={() => onDefaultProviderChange(id)}
                    aria-label={t('selection.defaultRadio', { provider: t(`providers.${id}`) })}
                    style={{ accentColor: 'var(--act)', inlineSize: '1.125rem', blockSize: '1.125rem' }}
                  />
                  <span className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-0.5">
                    <span style={{ fontWeight: isDefault && keyed ? 600 : 500 }}>{t(`providers.${id}`)}</span>
                    {/* Always in the layout; only visible for the Standard-KI */}
                    <span className={`pv-set-chip ${isDefault && keyed ? '' : 'invisible'}`} aria-hidden={!(isDefault && keyed)}>
                      {t('selection.defaultBadge')}
                    </span>
                    {!(info?.webSearch ?? id !== 'openai') && (
                      <Tooltip label={t('noWebSearch')} description={t('noWebSearchHint')}>
                        <span className="pv-set-muted" style={{ fontSize: '0.875rem' }}>
                          {t('noWebSearch')}
                        </span>
                      </Tooltip>
                    )}
                  </span>
                </label>

                <div className="flex items-center gap-2">
                  <ReservedText
                    className="text-sm font-medium"
                    style={{ color: failing ? (info.state === 'invalidKey' ? 'var(--accent-crimson)' : 'var(--accent-gold-text)') : 'var(--text-muted)' }}
                    candidates={stateCandidates}
                    data-testid={`ai-state-${id}`}
                  >
                    {!keyed ? t('noKey') : info ? stateText(info) : t('state.ok')}
                  </ReservedText>
                  <Tooltip label={t('selection.reset')} description={t('selection.resetHint')} disabled={!failing}>
                    <button
                      type="button"
                      className={`pv-btn-text ${failing ? '' : 'invisible'}`}
                      disabled={!failing || resetting !== null}
                      tabIndex={failing ? 0 : -1}
                      aria-hidden={failing ? undefined : true}
                      onClick={() => void reset(id)}
                    >
                      <IconRestore size={17} aria-hidden="true" />
                      <span>{t('selection.reset')}</span>
                    </button>
                  </Tooltip>
                </div>
              </li>
            )
          })}
        </ul>
        {errorText && <ErrorBlock className="w-full" title={t('selection.errorTitle')} message={errorText} defaultOpen />}
      </SettingsRow>

      <SettingsToggle checked={autoFallback} onChange={onAutoFallbackChange} disabled={mode === 'ask'} title={t('selection.fallback.label')}>
        <ReservedText
          className="pv-set-row__help"
          style={{ margin: '0 0 0.625rem', paddingInlineEnd: '4rem' }}
          candidates={[t('selection.fallback.hintOn'), t('selection.fallback.hintOff'), t('selection.fallback.hintAsk')]}
        >
          {fallbackHint}
        </ReservedText>
      </SettingsToggle>
    </div>
  )
}
