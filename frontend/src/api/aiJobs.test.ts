import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AiJobError, runAiJob } from './aiJobs'
import type { AiJobDto } from './types'

function job(over: Partial<AiJobDto> = {}): AiJobDto {
  return {
    id: 'job-1',
    kind: 'complete',
    slug: 'ff7',
    running: true,
    elapsedSeconds: 0,
    elapsedMilliseconds: 0,
    stepIdleSeconds: 0,
    step: { stage: 'starting', done: 0, total: 0, title: null },
    switchNotice: null,
    error: null,
    providerMessage: null,
    aiProvider: null,
    aiFailure: null,
    result: null,
    ...over,
  }
}

class HttpError extends Error {
  constructor(
    message: string,
    readonly status: number
  ) {
    super(message)
  }
}

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

describe('runAiJob', () => {
  it('starts the job, reports every step and resolves with the result', async () => {
    const answers = [
      job({ step: { stage: 'chapters', done: 1, total: 3, title: 'Waffen' } }),
      job({ step: { stage: 'chapters', done: 3, total: 3, title: 'Bosse' } }),
      job({ running: false, result: { addedCount: 3 } }),
    ]
    const fetchJob = vi.fn(async () => answers.shift()!)
    const seen: string[] = []

    const promise = runAiJob<{ addedCount: number }>(async () => job(), fetchJob, (j) => seen.push(`${j.step.stage}:${j.step.done}:${j.running}`), 2000)
    await vi.advanceTimersByTimeAsync(6000)

    await expect(promise).resolves.toEqual({ addedCount: 3 })
    expect(fetchJob).toHaveBeenCalledTimes(3)
    expect(seen).toEqual(['starting:0:true', 'chapters:1:true', 'chapters:3:true', 'starting:0:false'])
  })

  it('polls about every two seconds and not faster', async () => {
    const fetchJob = vi.fn(async () => job({ running: false, result: 'x' }))
    const promise = runAiJob(async () => job(), fetchJob)
    await vi.advanceTimersByTimeAsync(1900)
    expect(fetchJob).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(200)
    expect(fetchJob).toHaveBeenCalledTimes(1)
    await expect(promise).resolves.toBe('x')
  })

  it('throws our sentence with the provider text, the AI that failed and the note about a change of AI', async () => {
    const failed = job({
      running: false,
      error: 'Claude (Anthropic) hat die Anfrage abgelehnt (HTTP 400).',
      providerMessage: 'temperature: only 1',
      aiProvider: 'claude',
      aiFailure: 'other',
      switchNotice: 'Gemini-Kontingent aufgebraucht – weiter mit Claude.',
    })
    const promise = runAiJob(async () => job(), async () => failed)
    const assertion = expect(promise).rejects.toMatchObject({
      message: 'Claude (Anthropic) hat die Anfrage abgelehnt (HTTP 400).',
      providerMessage: 'temperature: only 1',
      aiProvider: 'claude',
      aiFailure: 'other',
      switchNotice: 'Gemini-Kontingent aufgebraucht – weiter mit Claude.',
      lost: false,
    })
    await vi.advanceTimersByTimeAsync(2000)
    await assertion
  })

  it('says exactly what happened when the server no longer knows the job (restart)', async () => {
    const sentence = 'Der Server wurde neu gestartet, die Recherche wurde unterbrochen – bereits fertige Kapitel sind gespeichert; erneut starten setzt fort.'
    const promise = runAiJob(async () => job(), async () => {
      throw new HttpError(sentence, 404)
    })
    const assertion = expect(promise).rejects.toMatchObject({ message: sentence, lost: true })
    await vi.advanceTimersByTimeAsync(2000)
    await assertion
  })

  it('carries on through a few dropped polls and gives up with a readable text when the connection stays away', async () => {
    let calls = 0
    const fetchJob = vi.fn(async () => {
      calls += 1
      if (calls <= 2) throw new TypeError('Failed to fetch')
      return job({ running: false, result: 'fine' })
    })
    const survived = runAiJob(async () => job(), fetchJob)
    await vi.advanceTimersByTimeAsync(8000)
    await expect(survived).resolves.toBe('fine')

    const gone = runAiJob(async () => job(), async () => {
      throw new TypeError('Failed to fetch')
    })
    const assertion = expect(gone).rejects.toBeInstanceOf(AiJobError)
    await vi.advanceTimersByTimeAsync(12000)
    await assertion
    await expect(gone).rejects.toMatchObject({ providerMessage: 'Failed to fetch' })
    await expect(gone).rejects.toThrow(/Verbindung zum PlatinVault-Server/)
  })

  it('does not start polling when the job cannot be started (404 game, 400 input)', async () => {
    const fetchJob = vi.fn()
    await expect(
      runAiJob(async () => {
        throw new HttpError('Spiel nicht gefunden', 404)
      }, fetchJob)
    ).rejects.toThrow('Spiel nicht gefunden')
    expect(fetchJob).not.toHaveBeenCalled()
  })
})
