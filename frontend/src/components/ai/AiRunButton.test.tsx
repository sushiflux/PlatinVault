import { useState } from 'react'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AiProviderId, AiStatusDto } from '../../api/types'
import { AiRunButton } from './AiRunButton'
import { providerStatus, statusFixture } from './ai.fixtures'

const fetchAiStatus = vi.hoisted(() => vi.fn<() => Promise<AiStatusDto>>())
vi.mock('../../api/client', () => ({ fetchAiStatus }))

function Harness({ onRun }: { onRun: (oneOff: AiProviderId | undefined) => void }) {
  const [oneOff, setOneOff] = useState<AiProviderId | undefined>(undefined)
  return <AiRunButton oneOff={oneOff} onOneOffChange={setOneOff} onClick={() => onRun(oneOff)} icon={<span />} label={<span>Kapitel erstellen</span>} />
}

async function renderButton(status: AiStatusDto, onRun = vi.fn()) {
  fetchAiStatus.mockResolvedValue(status)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <Harness onRun={onRun} />
    </QueryClientProvider>
  )
  await screen.findByRole('button', { name: /KI für diese Aktion wählen/ }).catch(() => undefined)
  return onRun
}

beforeEach(() => fetchAiStatus.mockReset())
afterEach(cleanup)

describe('AiRunButton', () => {
  it('names the AI that will do the work in the mode "Standard-KI"', async () => {
    await renderButton(statusFixture({ activeProvider: 'claude', defaultProvider: 'claude' }))
    expect(screen.getByRole('button', { name: /KI für diese Aktion wählen: Claude/ })).toBeTruthy()
  })

  it('offers a one-off switch that is used for the click and does not touch the setting', async () => {
    const onRun = await renderButton(statusFixture())

    fireEvent.click(screen.getByRole('button', { name: /KI für diese Aktion wählen: Gemini/ }))
    const menu = screen.getByRole('menu')
    expect(menu.textContent).toContain('Gemini')
    expect(menu.textContent).toContain('Claude')
    expect(menu.textContent).not.toContain('OpenAI')

    fireEvent.click(screen.getByRole('menuitemradio', { name: /Claude/ }))
    expect(screen.queryByRole('menu')).toBeNull()
    expect(screen.getByRole('button', { name: /KI für diese Aktion wählen: Claude/ })).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Kapitel erstellen' }))
    expect(onRun).toHaveBeenLastCalledWith('claude')
  })

  it('choosing the AI the setting already uses clears the one-off choice again', async () => {
    const onRun = await renderButton(statusFixture())
    fireEvent.click(screen.getByRole('button', { name: /KI für diese Aktion wählen: Gemini/ }))
    fireEvent.click(screen.getByRole('menuitemradio', { name: /Claude/ }))
    fireEvent.click(screen.getByRole('button', { name: /KI für diese Aktion wählen: Claude/ }))
    fireEvent.click(screen.getByRole('menuitemradio', { name: /Gemini/ }))

    fireEvent.click(screen.getByRole('button', { name: 'Kapitel erstellen' }))
    expect(onRun).toHaveBeenLastCalledWith(undefined)
  })

  it('shows "Gemischt" in the mixed mode and lets a single AI be picked for one action', async () => {
    const onRun = await renderButton(statusFixture({ mode: 'mixed', activeProvider: null }))
    fireEvent.click(screen.getByRole('button', { name: /KI für diese Aktion wählen: Gemischt/ }))
    expect(screen.getAllByRole('menuitemradio').length).toBe(3)

    fireEvent.click(screen.getByRole('menuitemradio', { name: /Claude/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Kapitel erstellen' }))
    expect(onRun).toHaveBeenLastCalledWith('claude')
  })

  it('has no AI part in the mode "ask": the click opens the chooser dialog instead', async () => {
    await renderButton(statusFixture({ mode: 'ask' }))
    expect(screen.queryByRole('button', { name: /KI für diese Aktion wählen/ })).toBeNull()
    expect(screen.getByRole('button', { name: 'Kapitel erstellen' })).toBeTruthy()
  })

  it('marks a provider that has a problem in the menu', async () => {
    await renderButton(
      statusFixture({
        providers: [providerStatus('gemini', { state: 'quota', since: new Date().toISOString() }), providerStatus('claude'), providerStatus('openai', { hasKey: false })],
        activeProvider: 'claude',
        defaultProvider: 'gemini',
      })
    )
    fireEvent.click(screen.getByRole('button', { name: /KI für diese Aktion wählen: Claude/ }))
    expect(screen.getByRole('menuitemradio', { name: /Gemini/ }).textContent).toContain('Kontingent erschöpft seit')
  })
})
