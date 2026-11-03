import i18n from '../i18n'
import type { AiJobDto } from './types'

// Following a background AI action: start it, ask for its state every two seconds, resolve with its result or throw with its reason.
// No single HTTP request has to survive the minutes such an action takes. (Kept free of client.ts, which imports this file.)

export type AiJobProgressHandler = (job: AiJobDto) => void

const POLL_INTERVAL_MS = 2000
// A dropped connection is tolerated for this many polls in a row (about ten seconds) before the follow-up gives up
const MAX_POLL_FAILURES = 5

/**
 * An AI action that ended with a failure. `message` is our sentence in the UI language, `providerMessage` the provider's own text (for "technical
 * details"), `aiProvider` / `aiFailure` say which AI failed and why, `switchNotice` that the action had left another AI before ("Gemini quota used
 * up – continuing with Claude."). Shaped like ApiError, so describeError() and the retry offers work with it unchanged.
 */
export class AiJobError extends Error {
  readonly status = 400
  readonly providerMessage?: string
  readonly aiProvider?: string
  readonly aiFailure?: string
  readonly switchNotice?: string
  // True when the server no longer knows the job (restarted): what was finished before is saved, the page should read the guide again
  readonly lost: boolean

  constructor(message: string, details: { providerMessage?: string | null; aiProvider?: string | null; aiFailure?: string | null; switchNotice?: string | null; lost?: boolean } = {}) {
    super(message)
    this.name = 'AiJobError'
    this.providerMessage = details.providerMessage ?? undefined
    this.aiProvider = details.aiProvider ?? undefined
    this.aiFailure = details.aiFailure ?? undefined
    this.switchNotice = details.switchNotice ?? undefined
    this.lost = details.lost ?? false
  }
}

const sleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

function statusOf(error: unknown): number | undefined {
  const status = (error as { status?: unknown } | null)?.status
  return typeof status === 'number' ? status : undefined
}

/**
 * @param start     starts the job on the server (the answer is the job, HTTP 202); a refusal (404 game, 400 input) throws here as usual
 * @param fetchJob  reads the job again
 * @param onProgress  is called with every state, also the first and the last one
 */
export async function runAiJob<T>(
  start: () => Promise<AiJobDto>,
  fetchJob: (id: string) => Promise<AiJobDto>,
  onProgress?: AiJobProgressHandler,
  pollIntervalMs: number = POLL_INTERVAL_MS
): Promise<T> {
  let job = await start()
  onProgress?.(job)
  return followAiJob<T>(job, fetchJob, onProgress, pollIntervalMs)
}

/** Follows a job that is already running (a reloaded page picks it up again). */
export async function followAiJob<T>(
  first: AiJobDto,
  fetchJob: (id: string) => Promise<AiJobDto>,
  onProgress?: AiJobProgressHandler,
  pollIntervalMs: number = POLL_INTERVAL_MS
): Promise<T> {
  let job = first
  let failures = 0
  while (job.running) {
    await sleep(pollIntervalMs)
    try {
      job = await fetchJob(job.id)
      failures = 0
    } catch (error: unknown) {
      // 404: the server restarted and forgot the job - its message says what that means (what was finished is saved, start again to continue)
      if (statusOf(error) === 404) {
        throw new AiJobError(error instanceof Error ? error.message : '', { lost: true })
      }
      failures += 1
      if (failures >= MAX_POLL_FAILURES) {
        throw new AiJobError(i18n.t('ai:job.unreachable'), { providerMessage: error instanceof Error ? error.message : undefined })
      }
      continue
    }
    onProgress?.(job)
  }

  if (job.error) {
    throw new AiJobError(job.error, {
      providerMessage: job.providerMessage,
      aiProvider: job.aiProvider,
      aiFailure: job.aiFailure,
      switchNotice: job.switchNotice,
    })
  }
  return job.result as T
}
