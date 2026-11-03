import { useTranslation } from 'react-i18next'
import { useFormat } from '../../i18n/format'
import type { AiProviderId, AiProviderStatusDto, AiStatusDto } from '../../api/types'

// Canonical order everywhere (lists, menus, the reserved width of labels)
export const AI_PROVIDERS: readonly AiProviderId[] = ['gemini', 'claude', 'openai']

const LAST_CHOICE_KEY = 'pv_ai_last_choice'

export function isAiProvider(value: unknown): value is AiProviderId {
  return value === 'gemini' || value === 'claude' || value === 'openai'
}

/** The provider chosen last in the chooser dialog; a per-viewer convenience, so it only lives in this browser. */
export function readLastAiChoice(): AiProviderId | null {
  try {
    const value = localStorage.getItem(LAST_CHOICE_KEY)
    return isAiProvider(value) ? value : null
  } catch {
    return null
  }
}

export function writeLastAiChoice(provider: AiProviderId): void {
  try {
    localStorage.setItem(LAST_CHOICE_KEY, provider)
  } catch {
    // Storage unavailable (private mode) - the choice just is not remembered
  }
}

/** The providers that have a saved key, in the canonical order. */
export function providersWithKey(status: AiStatusDto | undefined): AiProviderStatusDto[] {
  return (status?.providers ?? []).filter((p) => p.hasKey)
}

/**
 * Providers a failed action can be retried with: every provider with a key except the one that failed, the ones that are fine first.
 * `failed` is what the server said failed (or, without that, the provider the action was started with).
 */
export function retryCandidates(status: AiStatusDto | undefined, failed: string | undefined): AiProviderStatusDto[] {
  const others = providersWithKey(status).filter((p) => p.id !== failed)
  return [...others.filter((p) => p.state === 'ok'), ...others.filter((p) => p.state !== 'ok')]
}

/** "quota used up since 10:12": the time alone for today, date and time for an older state. */
export function useProviderStateText(): (provider: AiProviderStatusDto) => string {
  const { t } = useTranslation('ai')
  const format = useFormat()
  return (provider) => {
    if (provider.state === 'ok') return t('state.ok')
    const since = provider.since ? new Date(provider.since) : null
    const today = since !== null && since.toDateString() === new Date().toDateString()
    const time = since === null ? '' : format.date(since, today ? 'time' : 'dateTimeShort')
    return time ? t(`stateSince.${provider.state}`, { time }) : t(`state.${provider.state}`)
  }
}
