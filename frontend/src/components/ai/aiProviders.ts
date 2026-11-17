import { useTranslation } from 'react-i18next'
import { useFormat } from '../../i18n/format'
import type { AiProviderId, AiProviderStatusDto, AiStatusDto } from '../../api/types'

// Canonical order everywhere (lists, menus, the reserved width of labels)
export const AI_PROVIDERS: readonly AiProviderId[] = ['gemini', 'claude', 'openai']

/** "Mein KI-Abo": no API provider, the prompt goes into the player's own chat and the answer is pasted back. */
export const MANUAL_AI = 'manual' as const

/** How an AI action is carried out: by one of the API providers or by hand with the player's own subscription. */
export type AiHow = AiProviderId | typeof MANUAL_AI

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

// Whether the last AI action that offered "Mein KI-Abo" went that way ('manual') or to an API provider (no entry). Separate from
// LAST_CHOICE_KEY: that one stays the last API provider, so choosing the subscription never loses which AI was used before.
const LAST_WAY_KEY = 'pv_ai_last_way'

/** True when the player took "Mein KI-Abo" the last time an action offered it; a per-viewer convenience, so it only lives in this browser. */
export function readLastWasManual(): boolean {
  try {
    return localStorage.getItem(LAST_WAY_KEY) === MANUAL_AI
  } catch {
    return false
  }
}

/** Remembers how an action that offered "Mein KI-Abo" was carried out: the subscription, or (provider id / undefined = the settings) an API provider. */
export function rememberAiWay(how: string | undefined): void {
  try {
    if (how === MANUAL_AI) localStorage.setItem(LAST_WAY_KEY, MANUAL_AI)
    else localStorage.removeItem(LAST_WAY_KEY)
  } catch {
    // Storage unavailable (private mode) - the choice just is not remembered
  }
}

/** The starting choice of a button that offers "Mein KI-Abo": the subscription if it was taken last time, else the settings decide. */
export function rememberedHow(): AiHow | undefined {
  return readLastWasManual() ? MANUAL_AI : undefined
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
