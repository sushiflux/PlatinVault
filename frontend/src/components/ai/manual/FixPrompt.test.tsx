import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ManualChapterCheckDto } from '../../../api/types'
import { AnswerBox } from './AnswerBox'

const checkResult = (over: Partial<ManualChapterCheckDto> = {}): ManualChapterCheckDto => ({
  ok: true,
  errors: [],
  warnings: [
    {
      code: 'foreignLanguage',
      message: '9 Sätze sind nicht auf Deutsch.',
      details: null,
      examples: ['The boss is weak to ice.', 'Attack from behind.', 'Save before the fight.', 'Ein vierter Satz, der nicht mehr gezeigt wird.'],
    },
  ],
  fixPrompt: 'Übersetze alle englischen Sätze der Antwort ins Deutsche und gib das komplette JSON zurück.',
  preview: { title: 'Gegner', icon: 'mdi:skull', sectionCount: 2, tableRows: 3, cards: 1, sectionTitles: [] },
  ...over,
})

async function renderBox(result: ManualChapterCheckDto) {
  vi.useFakeTimers()
  const check = vi.fn(async () => result)
  render(<AnswerBox<ManualChapterCheckDto> check={check} onResult={vi.fn()} renderOk={() => <span>gelesen</span>} />)
  fireEvent.change(screen.getByRole('textbox'), { target: { value: '{"x":1}' } })
  await act(async () => {
    await vi.advanceTimersByTimeAsync(600)
  })
  return check
}

beforeEach(() => {
  Object.defineProperty(window, 'isSecureContext', { value: true, configurable: true })
  Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn(async () => undefined) }, configurable: true })
})
afterEach(() => {
  cleanup()
  vi.useRealTimers()
})

describe('foreign-language warning of a checked answer', () => {
  it('shows the warning with at most three examples', async () => {
    await renderBox(checkResult())
    expect(screen.getByText('9 Sätze sind nicht auf Deutsch.')).toBeTruthy()
    const examples = screen.getByTestId('issue-examples')
    expect(examples.querySelectorAll('li')).toHaveLength(3)
    expect(examples.textContent).toContain('The boss is weak to ice.')
    expect(examples.textContent).not.toContain('vierter Satz')
  })

  it('copies the correction prompt and confirms it', async () => {
    await renderBox(checkResult())
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Korrektur-Prompt kopieren' }))
    })
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith('Übersetze alle englischen Sätze der Antwort ins Deutsche und gib das komplette JSON zurück.')
    expect(screen.getByTestId('fix-prompt-status').textContent).toContain('Korrektur-Prompt kopiert')
  })

  it('shows the prompt selected when the browser blocks copying', async () => {
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn(async () => { throw new Error('blocked') }) }, configurable: true })
    document.execCommand = vi.fn(() => false)
    await renderBox(checkResult())
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Korrektur-Prompt kopieren' }))
    })
    const field = screen.getByRole('textbox', { name: 'Der Korrektur-Prompt' }) as HTMLTextAreaElement
    expect(field.value).toContain('Übersetze alle englischen Sätze')
    expect(screen.getByTestId('fix-prompt-status').textContent).toContain('Strg+C')
  })

  it('has no button when the server sends no correction prompt (or an empty one)', async () => {
    await renderBox(checkResult({ fixPrompt: null }))
    expect(screen.queryByRole('button', { name: 'Korrektur-Prompt kopieren' })).toBeNull()
    cleanup()
    await renderBox(checkResult({ fixPrompt: '' }))
    expect(screen.queryByRole('button', { name: 'Korrektur-Prompt kopieren' })).toBeNull()
  })

  it('shows a warning without examples as plain text', async () => {
    await renderBox(checkResult({ warnings: [{ code: 'noAppId', message: 'Die Antwort nennt keine Steam-AppID.', details: null }], fixPrompt: null }))
    expect(screen.getByText('Die Antwort nennt keine Steam-AppID.')).toBeTruthy()
    expect(screen.queryByTestId('issue-examples')).toBeNull()
  })
})
