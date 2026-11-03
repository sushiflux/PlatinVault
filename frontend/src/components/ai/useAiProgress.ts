import { useCallback, useEffect, useState } from 'react'
import type { AiJobDto } from '../../api/types'

// A step that has not changed for this long gets a calm "the AI is slow" hint (never an error: providers can take long on a step)
export const SLOW_STEP_SECONDS = 300

export interface AiProgressView {
  job: AiJobDto
  // Counting on between two answers of the server, so the clock keeps moving every second
  elapsedSeconds: number
  idleSeconds: number
}

interface ProgressState {
  job: AiJobDto
  receivedAt: number
  // When the job started, on this browser's clock (ms). Never moves later, see `startOf`.
  startedAt: number
}

/**
 * The start of a job on the local clock, from one answer of the server. The answer arrives a little later than the server computed it (and an
 * older server sent only whole seconds, rounded down) - both only ever make `receivedAt - elapsed` LATER than the real start, never earlier. So the earliest value
 * seen so far for the same job is the best one: the clock keeps one steady anchor instead of being nudged by every answer (which made the
 * seconds stall and then jump by two, user report 2026-11-02).
 */
export function startOf(job: AiJobDto, receivedAt: number, previous: ProgressState | null): number {
  const candidate = receivedAt - (job.elapsedMilliseconds ?? job.elapsedSeconds * 1000)
  return previous && previous.job.id === job.id ? Math.min(previous.startedAt, candidate) : candidate
}

/** Whole seconds since `startedAt` at `at` (never negative). */
function secondsSince(startedAt: number, at: number): number {
  return Math.max(0, Math.floor((at - startedAt) / 1000))
}

/**
 * The state of a background AI action for the progress display: hand `onProgress` to the API function (it is called with every answer of the
 * server), render `view`, call `reset()` when the action is over. The elapsed time counts from one steady local start (`startOf`) and the clock
 * wakes up right after each full second of that start, so it moves exactly once per second, without asking the server every second.
 */
export function useAiProgress() {
  const [state, setState] = useState<ProgressState | null>(null)
  const [now, setNow] = useState(() => Date.now())

  const onProgress = useCallback((job: AiJobDto) => {
    const receivedAt = Date.now()
    setState((previous) => ({ job, receivedAt, startedAt: startOf(job, receivedAt, previous) }))
    setNow(receivedAt)
  }, [])
  const reset = useCallback(() => setState(null), [])

  const running = state?.job.running ?? false
  const startedAt = state?.startedAt ?? null
  useEffect(() => {
    if (!running || startedAt === null) return
    let timer = 0
    // Wake up just after the next full second counted from the job's start, so every displayed second lasts one second
    const schedule = () => {
      const intoSecond = (Date.now() - startedAt) % 1000
      timer = window.setTimeout(() => {
        setNow(Date.now())
        schedule()
      }, 1000 - intoSecond + 20)
    }
    schedule()
    return () => window.clearTimeout(timer)
  }, [running, startedAt])

  const view: AiProgressView | null = state
    ? (() => {
        // A finished job stops at the moment of its last answer (not at the server's rounded-down value, which could be one lower)
        const at = state.job.running ? Math.max(now, state.receivedAt) : state.receivedAt
        const sinceAnswer = state.job.running ? secondsSince(state.receivedAt, now) : 0
        return {
          job: state.job,
          elapsedSeconds: Math.max(state.job.elapsedSeconds, secondsSince(state.startedAt, at)),
          idleSeconds: state.job.stepIdleSeconds + sinceAnswer,
        }
      })()
    : null

  return { view, onProgress, reset }
}

/** 2:14, or 1:02:14 from an hour on. */
export function formatElapsed(totalSeconds: number): string {
  const seconds = Math.max(0, Math.floor(totalSeconds))
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = seconds % 60
  const two = (n: number) => String(n).padStart(2, '0')
  return h > 0 ? `${h}:${two(m)}:${two(s)}` : `${m}:${two(s)}`
}
