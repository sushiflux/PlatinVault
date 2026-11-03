import { useState } from 'react'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AiStatusDto } from '../../api/types'
import { AiChoiceProvider, useAiPicker } from './AiChoice'
import { providerStatus, statusFixture } from './ai.fixtures'

const fetchAiStatus = vi.hoisted(() => vi.fn<() => Promise<AiStatusDto>>())
vi.mock('../../api/client', () => ({ fetchAiStatus }))

// A button that runs one AI action the way the pages do: ask which AI, show what came back
function Harness({ oneOff }: { oneOff?: string }) {
  const pickAi = useAiPicker()
  const [result, setResult] = useState('nothing yet')
  return (
    <>
      <button type="button" onClick={() => void pickAi(oneOff).then((value) => setResult(String(value)))}>
        run
      </button>
      <output data-testid="result">{result}</output>
    </>
  )
}

function renderHarness(status: AiStatusDto, oneOff?: string) {
  fetchAiStatus.mockResolvedValue(status)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AiChoiceProvider>
        <Harness oneOff={oneOff} />
      </AiChoiceProvider>
    </QueryClientProvider>
  )
}

beforeEach(() => {
  localStorage.clear()
  fetchAiStatus.mockReset()
})
afterEach(cleanup)

describe('AiChoiceProvider / useAiPicker', () => {
  it('does not ask in the mode "Standard-KI": the one-off choice (or none) comes back at once', async () => {
    renderHarness(statusFixture({ mode: 'default' }), 'claude')
    fireEvent.click(screen.getByText('run'))
    await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('claude'))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('does not ask in the mode "gemischt" either', async () => {
    renderHarness(statusFixture({ mode: 'mixed' }))
    fireEvent.click(screen.getByText('run'))
    await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('undefined'))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('asks in the mode "ask" and lists only the providers that have a key', async () => {
    renderHarness(statusFixture({ mode: 'ask' }))
    fireEvent.click(screen.getByText('run'))

    const dialog = await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
    expect(dialog.querySelectorAll('input[type="radio"]').length).toBe(2)
    expect(dialog.textContent).toContain('Gemini')
    expect(dialog.textContent).toContain('Claude')
    expect(dialog.textContent).not.toContain('OpenAI')

    fireEvent.click(screen.getByRole('button', { name: /Mit Gemini starten/ }))
    await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('gemini'))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })

  it('preselects the last choice and remembers the new one', async () => {
    localStorage.setItem('pv_ai_last_choice', 'claude')
    renderHarness(statusFixture({ mode: 'ask' }))
    fireEvent.click(screen.getByText('run'))

    await screen.findByRole('dialog')
    expect((screen.getByRole('radio', { name: /Claude/ }) as HTMLInputElement).checked).toBe(true)

    fireEvent.click(screen.getByRole('radio', { name: /Gemini/ }))
    fireEvent.click(screen.getByRole('button', { name: /Mit Gemini starten/ }))
    await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('gemini'))
    expect(localStorage.getItem('pv_ai_last_choice')).toBe('gemini')
  })

  it('warns when the AI the player is used to has no quota left and preselects one that works', async () => {
    localStorage.setItem('pv_ai_last_choice', 'gemini')
    const since = new Date(Date.now() - 60_000).toISOString()
    renderHarness(
      statusFixture({
        mode: 'ask',
        providers: [providerStatus('gemini', { state: 'quota', since }), providerStatus('claude'), providerStatus('openai', { hasKey: false })],
      })
    )
    fireEvent.click(screen.getByText('run'))

    const dialog = await screen.findByRole('dialog')
    expect(screen.getByText(/Gemini: Kontingent erschöpft seit/)).toBeTruthy()
    expect(dialog.textContent).toContain('Kontingent erschöpft seit')
    expect((screen.getByRole('radio', { name: /Claude/ }) as HTMLInputElement).checked).toBe(true)
  })

  it('answers null when the player cancels (Escape, Cancel button)', async () => {
    renderHarness(statusFixture({ mode: 'ask' }))
    fireEvent.click(screen.getByText('run'))
    await screen.findByRole('dialog')
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('null'))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())

    fireEvent.click(screen.getByText('run'))
    await screen.findByRole('dialog')
    fireEvent.click(screen.getByRole('button', { name: 'Abbrechen' }))
    await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('null'))
  })

  it('does not open an empty dialog when no provider has a key: the server then explains what is missing', async () => {
    renderHarness(statusFixture({ mode: 'ask', providers: [providerStatus('gemini', { hasKey: false }), providerStatus('claude', { hasKey: false })] }))
    fireEvent.click(screen.getByText('run'))
    await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('undefined'))
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})
