import type { TFunction } from 'i18next'
import type { UpdateStatusDto } from '../../api/types'

export type ResultTone = 'ok' | 'new' | 'muted' | 'error'

export const RESULT_TONE_COLOR: Record<ResultTone, string> = {
  ok: 'var(--accent-emerald)',
  new: 'var(--accent-gold)',
  muted: 'var(--text-secondary)',
  error: 'var(--accent-crimson)',
}

export function errorText(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

interface ResultInput {
  data: UpdateStatusDto | undefined
  // The status query failed / the manual check failed (a network error, not a GitHub error, which comes in data.error)
  statusError: unknown
  checkError: unknown
  checking: boolean
}

/** The one-line result of the update check, in the words of Settings > System > Updates (shared with the version popover). */
export function updateResult(t: TFunction<'settings'>, { data, statusError, checkError, checking }: ResultInput): { tone: ResultTone; text: string } {
  if (checking) return { tone: 'muted', text: t('updates.result.checking') }
  if (checkError) return { tone: 'error', text: t('updates.result.error', { reason: errorText(checkError) }) }
  if (!data) {
    return statusError
      ? { tone: 'error', text: t('updates.result.error', { reason: errorText(statusError) }) }
      : { tone: 'muted', text: t('updates.result.loading') }
  }
  switch (data.state) {
    case 'devBuild':
      return { tone: 'muted', text: t('updates.result.devBuild') }
    case 'disabled':
      return { tone: 'muted', text: t('updates.result.disabled') }
    case 'unchecked':
      return { tone: 'muted', text: t('updates.result.unchecked') }
    case 'error':
      return { tone: 'error', text: t('updates.result.error', { reason: data.error ?? '' }) }
    case 'available':
      return data.available?.isBeta
        ? { tone: 'new', text: t('updates.result.availableBeta', { version: data.available.display }) }
        : { tone: 'new', text: t('updates.result.availableRelease', { version: data.available?.display ?? '' }) }
    default:
      return { tone: 'ok', text: data.betaUpdatesEnabled ? t('updates.result.upToDateBetas') : t('updates.result.upToDate') }
  }
}

/** Every text the result line can show in normal operation, so a reserved slot never changes its height. */
export function updateResultCandidates(t: TFunction<'settings'>): string[] {
  return [
    t('updates.result.loading'),
    t('updates.result.checking'),
    t('updates.result.unchecked'),
    t('updates.result.upToDate'),
    t('updates.result.upToDateBetas'),
    t('updates.result.disabled'),
    t('updates.result.availableBeta', { version: 'v0000.00.000 (00.0000 β)' }),
    t('updates.result.availableRelease', { version: 'v0000.00.000 (00.0000)' }),
  ]
}
