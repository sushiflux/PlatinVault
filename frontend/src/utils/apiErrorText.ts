// Splits a failure into what the user should read and the provider's own raw text (Google / Anthropic / OpenAI answers are
// English, long and full of URLs - they belong in a collapsible "technical details" part, not in the sentence).
import i18n from '../i18n'

export interface ErrorText {
  /** The readable reason (the app's own sentence); falls back to the given text when the failure carries none. */
  message: string
  /** The provider's raw text, or null. Shown behind "Technical details". */
  details: string | null
}

// Older server answers carry the provider text at the very end in parentheses: "... Später erneut versuchen. (Google: <raw>)"
const TRAILING_PROVIDER = /\s*\(((?:Google|Anthropic|OpenAI):[\s\S]*)\)\s*$/

// What browsers throw when a request never got an answer: Chrome "Failed to fetch", Firefox "NetworkError when attempting to fetch
// resource.", Safari "Load failed", plus the generic "network error" of an interrupted body
const NETWORK_FAILURE_TEXT = /failed to fetch|networkerror|network error|network request failed|load failed|fetch failed/i

/** True for the bare TypeError a browser throws when the server could not be reached at all (no HTTP answer). */
export function isNetworkFailure(error: unknown): boolean {
  return error instanceof TypeError && NETWORK_FAILURE_TEXT.test(error.message)
}

/** The sentence for an unreachable server (what happened and what to check), in the UI language. */
export function networkFailureMessage(): string {
  return i18n.t('errors:http.network')
}

/**
 * @param error  whatever was caught
 * @param fallback  the text to use when the failure has no message of its own (e.g. a network error without a response body)
 *
 * The server sends the raw provider text as a separate `providerMessage` (carried on the thrown ApiError); an older answer
 * that still has it at the end of the message ("... (Google: ...)") is split here instead.
 */
export function describeError(error: unknown, fallback: string): ErrorText {
  // A raw "Failed to fetch" never reaches the user: it is replaced by the readable "server not reachable" sentence
  const raw = isNetworkFailure(error)
    ? networkFailureMessage()
    : error instanceof Error
      ? error.message.trim()
      : typeof error === 'string'
        ? error.trim()
        : ''
  const provided = (error as { providerMessage?: unknown } | null)?.providerMessage
  const providerMessage = typeof provided === 'string' && provided.trim() ? provided.trim() : null
  const message = raw || fallback

  if (providerMessage) return { message, details: providerMessage }

  const match = TRAILING_PROVIDER.exec(message)
  if (match && match.index > 0) return { message: message.slice(0, match.index).trim(), details: match[1]!.trim() }
  return { message, details: null }
}
