import i18n from '../i18n'

// What the "Copy" button of an error message puts on the clipboard (rule "error messages are always copyable"): the complete message,
// the technical details, the error code, then one line with the app version and the time - everything a bug report needs, in one paste.

export interface ErrorReportInput {
  /** What happened, short ("Guide konnte nicht vervollständigt werden"). */
  title?: string | null
  /** The readable reason, complete. */
  message?: string | null
  /** The provider's / server's own text. */
  details?: string | null
  /** A code that names the failure ("HTTP 502 · gemini · quota"). */
  code?: string | number | null
}

// The version the server reported last (branding answer). Kept in a plain variable so the copy button needs no React Query context;
// unknown (the branding request has not arrived or failed) = the line shows the time only.
let knownAppVersion: string | null = null

export function rememberAppVersion(version: string | null | undefined): void {
  knownAppVersion = version && version.trim() ? version.trim() : null
}

export function getKnownAppVersion(): string | null {
  return knownAppVersion
}

const two = (value: number) => String(value).padStart(2, '0')

/** "2026-11-03 14:05:12 +01:00": local time with its offset, the same in every language (a report is read by a person who may use another one). */
export function formatReportTime(at: Date): string {
  const offset = -at.getTimezoneOffset()
  const sign = offset < 0 ? '-' : '+'
  const abs = Math.abs(offset)
  const date = `${at.getFullYear()}-${two(at.getMonth() + 1)}-${two(at.getDate())}`
  const time = `${two(at.getHours())}:${two(at.getMinutes())}:${two(at.getSeconds())}`
  return `${date} ${time} ${sign}${two(Math.floor(abs / 60))}:${two(abs % 60)}`
}

/** An error code for a failure thrown by the API client: the HTTP status, plus the AI provider and the kind of AI failure when known. */
export function errorCodeOf(error: unknown): string | null {
  if (!error || typeof error !== 'object') return null
  const { status, aiProvider, aiFailure } = error as { status?: unknown; aiProvider?: unknown; aiFailure?: unknown }
  const parts: string[] = []
  if (typeof status === 'number' && status > 0) parts.push(`HTTP ${status}`)
  if (typeof aiProvider === 'string' && aiProvider) parts.push(aiProvider)
  if (typeof aiFailure === 'string' && aiFailure) parts.push(aiFailure)
  return parts.length > 0 ? parts.join(' · ') : null
}

/**
 * The complete text to copy. Parts that are empty are left out; the title is not repeated when the message already starts with it.
 * @param version  defaults to the version the server reported
 * @param at  defaults to now
 */
export function buildErrorReport(input: ErrorReportInput, version: string | null = knownAppVersion, at: Date = new Date()): string {
  const title = input.title?.trim() ?? ''
  const message = input.message?.trim() ?? ''
  const details = input.details?.trim() ?? ''
  const code = input.code === null || input.code === undefined ? '' : String(input.code).trim()

  const lines: string[] = []
  if (title && !message.startsWith(title)) lines.push(title)
  if (message) lines.push(message)
  const blocks: string[] = [lines.join('\n')].filter(Boolean)
  if (details) blocks.push(`${i18n.t('errors:report.details')}:\n${details}`)
  if (code) blocks.push(`${i18n.t('errors:report.code')}: ${code}`)
  blocks.push(`PlatinVault${version ? ` ${version}` : ''} · ${formatReportTime(at)}`)
  return blocks.join('\n\n')
}
