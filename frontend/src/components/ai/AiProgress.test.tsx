import { act, cleanup, render, renderHook, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AiJobDto } from '../../api/types'
import { AiProgressLines, AiProgressNotice } from './AiProgress'
import { formatElapsed, SLOW_STEP_SECONDS, useAiProgress } from './useAiProgress'

function job(over: Partial<AiJobDto> = {}): AiJobDto {
  return {
    id: 'j',
    kind: 'complete',
    slug: 'x',
    running: true,
    elapsedSeconds: 0,
    stepIdleSeconds: 0,
    step: { stage: 'starting', done: 0, total: 0, title: null },
    switchNotice: null,
    error: null,
    providerMessage: null,
    aiProvider: null,
    aiFailure: null,
    result: null,
    ...over,
    // Like the server: the same running time without rounding (unless a test sets it itself)
    elapsedMilliseconds: over.elapsedMilliseconds ?? (over.elapsedSeconds ?? 0) * 1000,
  }
}

const view = (j: AiJobDto, idle = j.stepIdleSeconds) => ({ job: j, elapsedSeconds: j.elapsedSeconds, idleSeconds: idle })

afterEach(cleanup)

describe('formatElapsed', () => {
  it('counts minutes and seconds, hours from an hour on', () => {
    expect(formatElapsed(0)).toBe('0:00')
    expect(formatElapsed(134)).toBe('2:14')
    expect(formatElapsed(3725)).toBe('1:02:05')
  })
})

describe('AiProgressLines', () => {
  it('names the step that really runs, with the chapter count and title', () => {
    const { rerender } = render(<AiProgressLines view={view(job({ step: { stage: 'chapters', done: 3, total: 8, title: 'Beste Waffen' } }))} />)
    expect(screen.getByText('3 von 8 Kapiteln fertig – gerade: Beste Waffen …')).toBeTruthy()

    rerender(<AiProgressLines view={view(job({ step: { stage: 'blueprint', done: 0, total: 0, title: null } }))} />)
    expect(screen.getByText('Analysiere das Spiel und prüfe alle Themen …')).toBeTruthy()

    rerender(<AiProgressLines view={view(job({ step: { stage: 'saving', done: 0, total: 0, title: null } }))} />)
    expect(screen.getByText('Speichere …')).toBeTruthy()
  })

  it('shows the start before the server has answered', () => {
    render(<AiProgressLines view={null} />)
    expect(screen.getByText('Starte …')).toBeTruthy()
    expect(screen.getByTestId('ai-progress-elapsed').textContent).toBe('Läuft seit 0:00')
  })

  it('says when the action had to change AI, and never promises a duration', () => {
    render(<AiProgressLines view={view(job({ switchNotice: 'Gemini-Kontingent aufgebraucht – weiter mit Claude.' }))} />)
    expect(screen.getByText('Gemini-Kontingent aufgebraucht – weiter mit Claude.')).toBeTruthy()
    const text = screen.getByTestId('ai-progress').textContent ?? ''
    expect(text).toContain('einige Minuten dauern')
    expect(text).not.toMatch(/\b\d+\s*(–|-)\s*\d+\s*Sekunden/)
  })

  it('turns the general line into a calm hint when the step has not changed for five minutes - not an error', () => {
    const { rerender } = render(<AiProgressLines view={view(job(), SLOW_STEP_SECONDS - 1)} />)
    expect(screen.queryByText(/dauert länger als gewöhnlich/)).toBeNull()

    rerender(<AiProgressLines view={view(job(), SLOW_STEP_SECONDS)} />)
    expect(screen.getByText(/dauert länger als gewöhnlich/)).toBeTruthy()
    expect(screen.getByText(/Das ist kein Fehler/)).toBeTruthy()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('reserves the room of every possible text, so a step change moves nothing', () => {
    render(<AiProgressLines view={view(job())} />)
    const reserved = screen.getByTestId('ai-progress').querySelectorAll('.pv-reserve-copy')
    // step line: 7 candidates, switch line: 1, general/slow line: 2
    expect(reserved.length).toBe(10)
  })
})

describe('AiProgressNotice', () => {
  it('is a progress notice with the title of its kind', () => {
    render(<AiProgressNotice kind="lore" view={view(job({ kind: 'lore', step: { stage: 'loreParts', done: 1, total: 4, title: 'Handlung' } }))} />)
    expect(screen.getByText('Story & Lore wird recherchiert …')).toBeTruthy()
    expect(screen.getByText('1 von 4 Teilen fertig – gerade: Handlung …')).toBeTruthy()
  })
})

describe('useAiProgress', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('keeps the clock moving between two answers of the server and stops when the job is over', () => {
    const { result } = renderHook(() => useAiProgress())
    expect(result.current.view).toBeNull()

    act(() => result.current.onProgress(job({ elapsedSeconds: 134, stepIdleSeconds: 10 })))
    expect(result.current.view?.elapsedSeconds).toBe(134)

    // The clock wakes up just after each full second of the job (+20 ms), so 3.1 s later it shows three more seconds
    act(() => vi.advanceTimersByTime(3100))
    expect(result.current.view?.elapsedSeconds).toBe(137)
    expect(result.current.view?.idleSeconds).toBe(13)

    act(() => result.current.onProgress(job({ running: false, elapsedSeconds: 140, stepIdleSeconds: 0 })))
    act(() => vi.advanceTimersByTime(5000))
    expect(result.current.view?.elapsedSeconds).toBe(140)

    act(() => result.current.reset())
    expect(result.current.view).toBeNull()
  })

  it('counts one second per second although the server answers only every ~2.1 s (no stall, no jump of two)', () => {
    const { result } = renderHook(() => useAiProgress())
    const t0 = Date.now()
    // The job really started 10.4 s before t0; the server computes its answer 80 ms before it arrives
    const serverMs = (at: number) => at - 80 - (t0 - 10_400)
    let nextAnswer = t0
    const shown: { at: number; value: number }[] = []

    for (let step = 0; step <= 250; step++) {
      const at = t0 + step * 100
      if (at >= nextAnswer) {
        act(() => result.current.onProgress(job({ elapsedSeconds: Math.floor(serverMs(at) / 1000), elapsedMilliseconds: serverMs(at), stepIdleSeconds: 0 })))
        nextAnswer += 2130
      }
      shown.push({ at, value: result.current.view!.elapsedSeconds })
      act(() => vi.advanceTimersByTime(100))
    }

    const changes = shown.filter((s, i) => i > 0 && s.value !== shown[i - 1]!.value)
    for (let i = 1; i < shown.length; i++) {
      const delta = shown[i]!.value - shown[i - 1]!.value
      expect(delta === 0 || delta === 1, `value went from ${shown[i - 1]!.value} to ${shown[i]!.value}`).toBe(true)
    }
    // Every displayed second (after the first change) lasts about one second, never two
    for (let i = 1; i < changes.length; i++) {
      const held = changes[i]!.at - changes[i - 1]!.at
      expect(held).toBeGreaterThanOrEqual(900)
      expect(held).toBeLessThanOrEqual(1100)
    }
    expect(changes.length).toBeGreaterThanOrEqual(23)
  })
})
