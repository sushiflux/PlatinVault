import { useState } from 'react'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AiProviderId, AiStatusDto } from '../../api/types'
import type { AiHow } from './aiProviders'
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

// The same button for an action that can also be done with the player's own AI subscription
function ManualHarness({ onRun }: { onRun: (oneOff: AiHow | undefined) => void }) {
  const [oneOff, setOneOff] = useState<AiHow | undefined>(undefined)
  return <AiRunButton manual oneOff={oneOff} onOneOffChange={setOneOff} onClick={() => onRun(oneOff)} icon={<span />} label={<span>Kapitel erstellen</span>} />
}

async function renderManualButton(status: AiStatusDto, onRun = vi.fn()) {
  fetchAiStatus.mockResolvedValue(status)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ManualHarness onRun={onRun} />
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

  describe('with the AI subscription', () => {
    it('adds "Mein KI-Abo" after the providers and hands manual to the click', async () => {
      const onRun = await renderManualButton(statusFixture())
      fireEvent.click(screen.getByRole('button', { name: /KI für diese Aktion wählen: Gemini/ }))
      const items = screen.getAllByRole('menuitemradio')
      expect(items).toHaveLength(3)
      expect(items[2]!.textContent).toContain('Mein KI-Abo')
      expect(items[2]!.textContent).toContain('ohne API-Key')

      fireEvent.click(items[2]!)
      expect(screen.queryByRole('menu')).toBeNull()
      expect(screen.getByRole('button', { name: /KI für diese Aktion wählen: KI-Abo/ })).toBeTruthy()
      fireEvent.click(screen.getByRole('button', { name: 'Kapitel erstellen' }))
      expect(onRun).toHaveBeenLastCalledWith('manual')
    })

    it('marks the subscription, not the provider, as chosen and goes back to a provider', async () => {
      const onRun = await renderManualButton(statusFixture())
      fireEvent.click(screen.getByRole('button', { name: /KI für diese Aktion wählen: Gemini/ }))
      fireEvent.click(screen.getAllByRole('menuitemradio')[2]!)
      fireEvent.click(screen.getByRole('button', { name: /KI für diese Aktion wählen: KI-Abo/ }))
      const items = screen.getAllByRole('menuitemradio')
      expect(items.map((item) => item.getAttribute('aria-checked'))).toEqual(['false', 'false', 'true'])

      fireEvent.click(items[0]!)
      fireEvent.click(screen.getByRole('button', { name: 'Kapitel erstellen' }))
      expect(onRun).toHaveBeenLastCalledWith(undefined)
    })

    it('has the subscription in the mixed mode too', async () => {
      await renderManualButton(statusFixture({ mode: 'mixed', activeProvider: null }))
      fireEvent.click(screen.getByRole('button', { name: /KI für diese Aktion wählen: Gemischt/ }))
      expect(screen.getAllByRole('menuitemradio')).toHaveLength(4)
    })

    it('reserves the width of the longest name, the subscription included, so the button never changes size', async () => {
      await renderManualButton(statusFixture())
      const segment = screen.getByRole('button', { name: /KI für diese Aktion wählen/ })
      const labels = Array.from(segment.querySelectorAll('span[aria-hidden]')).map((el) => el.textContent)
      expect(segment.textContent).toContain('KI-Abo')
      // one label is shown, all the others only hold the width
      expect(segment.querySelectorAll('span.invisible').length).toBe(labels.length)
      expect(labels).toContain('KI-Abo')
      expect(labels).toContain('Kein Key')
    })

    it('a button that does not offer the subscription keeps its names and menu exactly as before', async () => {
      await renderButton(statusFixture())
      const segment = screen.getByRole('button', { name: /KI für diese Aktion wählen/ })
      expect(segment.textContent).not.toContain('KI-Abo')
      fireEvent.click(segment)
      expect(screen.getAllByRole('menuitemradio')).toHaveLength(2)
    })

    it('without any API key the part shows the subscription and its menu has only that entry', async () => {
      const onRun = await renderManualButton(statusFixture({ activeProvider: null, defaultProvider: null, providers: [providerStatus('gemini', { hasKey: false }), providerStatus('claude', { hasKey: false })] }))
      const segment = screen.getByRole('button', { name: /KI für diese Aktion wählen: KI-Abo/ })
      expect(segment.hasAttribute('disabled')).toBe(false)
      fireEvent.click(segment)
      expect(screen.getAllByRole('menuitemradio')).toHaveLength(1)
      expect(screen.getByRole('menuitemradio').getAttribute('aria-checked')).toBe('true')
      fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' })
      fireEvent.click(screen.getByRole('button', { name: 'Kapitel erstellen' }))
      expect(onRun).toHaveBeenLastCalledWith(undefined)
    })

    it('has no AI part in the mode "ask": the handler of the caller opens the chooser with the subscription', async () => {
      await renderManualButton(statusFixture({ mode: 'ask' }))
      expect(screen.queryByRole('button', { name: /KI für diese Aktion wählen/ })).toBeNull()
    })
  })
})
