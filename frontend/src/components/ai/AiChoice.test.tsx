import { useState } from 'react'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AiStatusDto } from '../../api/types'
import { AiChoiceProvider, useAiPicker } from './AiChoice'
import type { PickAiOptions } from './AiChoice'
import { providerStatus, statusFixture } from './ai.fixtures'

const fetchAiStatus = vi.hoisted(() => vi.fn<() => Promise<AiStatusDto>>())
vi.mock('../../api/client', () => ({ fetchAiStatus }))

// A button that runs one AI action the way the pages do: ask which AI, show what came back
function Harness({ oneOff, options }: { oneOff?: string; options?: PickAiOptions }) {
  const pickAi = useAiPicker()
  const [result, setResult] = useState('nothing yet')
  return (
    <>
      <button type="button" onClick={() => void pickAi(oneOff, options).then((value) => setResult(String(value)))}>
        run
      </button>
      <output data-testid="result">{result}</output>
    </>
  )
}

function renderHarness(status: AiStatusDto, oneOff?: string, options?: PickAiOptions) {
  fetchAiStatus.mockResolvedValue(status)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AiChoiceProvider>
        <Harness oneOff={oneOff} options={options} />
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

  
  describe('with the AI subscription ("Mein KI-Abo")', () => {
    it('lists "Mein KI-Abo" after the providers and answers manual', async () => {
      renderHarness(statusFixture({ mode: 'ask' }), undefined, { manual: true })
      fireEvent.click(screen.getByText('run'))

      const dialog = await screen.findByRole('dialog', { name: 'Welche KI möchtest du benutzen?' })
      const radios = Array.from(dialog.querySelectorAll('input[type="radio"]'))
      expect(radios).toHaveLength(3)
      // the last entry is the subscription, with its one-line explanation
      expect(dialog.querySelectorAll('.pv-ai-choice__row')[2]?.textContent).toContain('Mein KI-Abo')
      expect(dialog.textContent).toContain('ohne API-Key, hier entstehen keine Kosten')

      fireEvent.click(screen.getByRole('radio', { name: /Mein KI-Abo/ }))
      fireEvent.click(screen.getByRole('button', { name: /Mit meinem KI-Abo weiter/ }))
      await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('manual'))
      // the subscription is not remembered as the last API provider
      expect(localStorage.getItem('pv_ai_last_choice')).toBeNull()
    })

    it('does not list the subscription for an action that does not offer it', async () => {
      renderHarness(statusFixture({ mode: 'ask' }))
      fireEvent.click(screen.getByText('run'))
      const dialog = await screen.findByRole('dialog')
      expect(dialog.textContent).not.toContain('Mein KI-Abo')
    })

    it('keeps the confirm button as wide as its longest label (all labels stacked)', async () => {
      renderHarness(statusFixture({ mode: 'ask' }), undefined, { manual: true })
      fireEvent.click(screen.getByText('run'))
      const dialog = await screen.findByRole('dialog')
      const confirm = Array.from(dialog.querySelectorAll('button')).find((b) => b.classList.contains('pv-btn-primary'))!
      expect(confirm.textContent).toContain('Mit Gemini starten')
      expect(confirm.textContent).toContain('Mit Claude starten')
      expect(confirm.textContent).toContain('Mit meinem KI-Abo weiter')
      // three labels, one of them shown: the other two only hold the width
      expect(confirm.querySelectorAll('span[aria-hidden="true"].invisible').length).toBe(2)
    })

    it('opens the chooser in every mode with always, preselecting the provider of the setting', async () => {
      renderHarness(statusFixture({ mode: 'default', defaultProvider: 'claude', activeProvider: 'claude' }), undefined, { manual: true, always: true })
      fireEvent.click(screen.getByText('run'))
      await screen.findByRole('dialog')
      expect((screen.getByRole('radio', { name: /Claude/ }) as HTMLInputElement).checked).toBe(true)
      fireEvent.click(screen.getByRole('button', { name: /Mit Claude starten/ }))
      await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('claude'))
    })

    it('also asks in the mode mixed with always, and a cancel answers null', async () => {
      renderHarness(statusFixture({ mode: 'mixed', activeProvider: null }), undefined, { manual: true, always: true })
      fireEvent.click(screen.getByText('run'))
      await screen.findByRole('dialog')
      fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
      await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('null'))
    })

    it('preselects a one-off choice of the button, including the subscription itself', async () => {
      renderHarness(statusFixture({ mode: 'default' }), 'manual', { manual: true, always: true })
      fireEvent.click(screen.getByText('run'))
      await screen.findByRole('dialog')
      expect((screen.getByRole('radio', { name: /Mein KI-Abo/ }) as HTMLInputElement).checked).toBe(true)
    })

    it('without always, a one-off manual passes through and the other modes stay silent', async () => {
      renderHarness(statusFixture({ mode: 'default' }), 'manual', { manual: true })
      fireEvent.click(screen.getByText('run'))
      await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('manual'))
      expect(screen.queryByRole('dialog')).toBeNull()
    })

    it('ignores a one-off manual for an action that does not offer the subscription', async () => {
      renderHarness(statusFixture({ mode: 'default' }), 'manual')
      fireEvent.click(screen.getByText('run'))
      await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('undefined'))
    })

    it('without any API key there is nothing to choose: manual comes back at once, in every mode', async () => {
      const noKeys = [providerStatus('gemini', { hasKey: false }), providerStatus('claude', { hasKey: false })]
      renderHarness(statusFixture({ mode: 'ask', providers: noKeys }), undefined, { manual: true, always: true })
      fireEvent.click(screen.getByText('run'))
      await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('manual'))
      expect(screen.queryByRole('dialog')).toBeNull()
    })

    describe('remembers "Mein KI-Abo" as the last choice', () => {
      it('preselects the subscription next time, marked as last used, ahead of the standard AI', async () => {
        const status = statusFixture({ mode: 'default', defaultProvider: 'claude', activeProvider: 'claude' })
        const first = renderHarness(status, undefined, { manual: true, always: true })
        fireEvent.click(screen.getByText('run'))
        await screen.findByRole('dialog')
        fireEvent.click(screen.getByRole('radio', { name: /Mein KI-Abo/ }))
        fireEvent.click(screen.getByRole('button', { name: /Mit meinem KI-Abo weiter/ }))
        await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('manual'))
        expect(localStorage.getItem('pv_ai_last_way')).toBe('manual')
        first.unmount()

        renderHarness(status, undefined, { manual: true, always: true })
        fireEvent.click(screen.getByText('run'))
        const dialog = await screen.findByRole('dialog')
        expect((screen.getByRole('radio', { name: /Mein KI-Abo/ }) as HTMLInputElement).checked).toBe(true)
        const rows = dialog.querySelectorAll('.pv-ai-choice__row')
        expect(rows[2]?.textContent).toContain('Zuletzt benutzt')
        // the badge moves: no provider is marked as last used at the same time
        expect(rows[0]?.textContent).not.toContain('Zuletzt benutzt')
        expect(rows[1]?.textContent).not.toContain('Zuletzt benutzt')
      })

      it('forgets it again as soon as an API provider is taken', async () => {
        localStorage.setItem('pv_ai_last_way', 'manual')
        renderHarness(statusFixture({ mode: 'ask' }), undefined, { manual: true })
        fireEvent.click(screen.getByText('run'))
        await screen.findByRole('dialog')
        fireEvent.click(screen.getByRole('radio', { name: /Gemini/ }))
        fireEvent.click(screen.getByRole('button', { name: /Mit Gemini starten/ }))
        await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('gemini'))
        expect(localStorage.getItem('pv_ai_last_way')).toBeNull()
      })

      it('keeps it when the chooser is cancelled', async () => {
        localStorage.setItem('pv_ai_last_way', 'manual')
        renderHarness(statusFixture({ mode: 'ask' }), undefined, { manual: true })
        fireEvent.click(screen.getByText('run'))
        await screen.findByRole('dialog')
        fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
        await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('null'))
        expect(localStorage.getItem('pv_ai_last_way')).toBe('manual')
      })

      it('remembers the way of a button choice without a dialog, and the standard AI clears it', async () => {
        const status = statusFixture({ mode: 'default' })
        const first = renderHarness(status, 'manual', { manual: true })
        fireEvent.click(screen.getByText('run'))
        await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('manual'))
        expect(localStorage.getItem('pv_ai_last_way')).toBe('manual')
        first.unmount()

        renderHarness(status, undefined, { manual: true })
        fireEvent.click(screen.getByText('run'))
        await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('undefined'))
        expect(localStorage.getItem('pv_ai_last_way')).toBeNull()
      })

      it('does not remember the subscription when it was no choice (no API key at all)', async () => {
        const noKeys = [providerStatus('gemini', { hasKey: false }), providerStatus('claude', { hasKey: false })]
        renderHarness(statusFixture({ mode: 'ask', providers: noKeys }), undefined, { manual: true, always: true })
        fireEvent.click(screen.getByText('run'))
        await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('manual'))
        expect(localStorage.getItem('pv_ai_last_way')).toBeNull()
      })

      it('neither preselects nor touches it for an action that does not offer the subscription', async () => {
        localStorage.setItem('pv_ai_last_way', 'manual')
        localStorage.setItem('pv_ai_last_choice', 'claude')
        renderHarness(statusFixture({ mode: 'ask' }))
        fireEvent.click(screen.getByText('run'))
        await screen.findByRole('dialog')
        expect((screen.getByRole('radio', { name: /Claude/ }) as HTMLInputElement).checked).toBe(true)
        fireEvent.click(screen.getByRole('button', { name: /Mit Claude starten/ }))
        await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('claude'))
        expect(localStorage.getItem('pv_ai_last_way')).toBe('manual')
      })
    })

    it('without any API key and without the subscription option nothing changes: no dialog, the settings decide', async () => {
      const noKeys = [providerStatus('gemini', { hasKey: false })]
      renderHarness(statusFixture({ mode: 'default', providers: noKeys }), undefined, { always: true })
      fireEvent.click(screen.getByText('run'))
      await waitFor(() => expect(screen.getByTestId('result').textContent).toBe('undefined'))
      expect(screen.queryByRole('dialog')).toBeNull()
    })
  })
})
