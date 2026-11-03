import { useState } from 'react'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AiProviderId, AiSelectionMode, AiStatusDto, ResearchBlocksDto } from '../../api/types'
import { providerStatus, statusFixture } from '../ai/ai.fixtures'
import { AiSelectionSection } from './AiSelectionSection'
import { countResearchBlocks, emptyResearchBlocks, ResearchBlocksSection } from './ResearchBlocksSection'

const fetchAiStatus = vi.hoisted(() => vi.fn<() => Promise<AiStatusDto>>())
const resetAiProvider = vi.hoisted(() => vi.fn<(provider: string) => Promise<AiStatusDto>>())
vi.mock('../../api/client', () => ({ fetchAiStatus, resetAiProvider }))

beforeEach(() => {
  fetchAiStatus.mockReset()
  resetAiProvider.mockReset()
})
afterEach(cleanup)

function SelectionHarness({ initialMode = 'default' as AiSelectionMode, initialFallback = true, hasKey = { gemini: true, claude: true, openai: false } }) {
  const [mode, setMode] = useState<AiSelectionMode>(initialMode)
  const [provider, setProvider] = useState<AiProviderId | null>('gemini')
  const [fallback, setFallback] = useState(initialFallback)
  return (
    <>
      <AiSelectionSection
        mode={mode}
        onModeChange={setMode}
        defaultProvider={provider}
        onDefaultProviderChange={setProvider}
        autoFallback={fallback}
        onAutoFallbackChange={setFallback}
        hasKey={hasKey}
      />
      <output data-testid="state">{`${mode}|${provider}|${fallback}`}</output>
    </>
  )
}

function renderSelection(status: AiStatusDto, props: Parameters<typeof SelectionHarness>[0] = {}) {
  fetchAiStatus.mockResolvedValue(status)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SelectionHarness {...props} />
    </QueryClientProvider>
  )
}

describe('AiSelectionSection', () => {
  it('lets the player pick one of the three modes and explains the chosen one', async () => {
    renderSelection(statusFixture())
    const group = screen.getByRole('group', { name: 'So wählt PlatinVault die KI' })
    expect(group.querySelectorAll('button').length).toBe(3)
    expect(screen.getByRole('button', { name: 'Standard-KI' }).getAttribute('aria-pressed')).toBe('true')

    fireEvent.click(screen.getByRole('button', { name: 'Gemischt' }))
    expect(screen.getByTestId('state').textContent).toBe('mixed|gemini|true')
    expect(screen.getByRole('button', { name: 'Gemischt' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getAllByText(/Schreibstil kann zwischen den Kapiteln schwanken/).length).toBeGreaterThan(0)
  })

  it('makes only providers with a key selectable as Standard-KI, and only in that mode', async () => {
    renderSelection(statusFixture())
    const claude = screen.getByRole('radio', { name: 'Claude als Standard-KI verwenden' }) as HTMLInputElement
    const openai = screen.getByRole('radio', { name: 'OpenAI als Standard-KI verwenden' }) as HTMLInputElement
    expect(claude.disabled).toBe(false)
    expect(openai.disabled).toBe(true)

    fireEvent.click(claude)
    expect(screen.getByTestId('state').textContent).toBe('default|claude|true')

    fireEvent.click(screen.getByRole('button', { name: 'Gemischt' }))
    expect((screen.getByRole('radio', { name: 'Claude als Standard-KI verwenden' }) as HTMLInputElement).disabled).toBe(true)
  })

  it('shows the state of every provider, with the time a problem started, and resets it on request', async () => {
    const since = new Date().toISOString()
    const failing = statusFixture({
      providers: [providerStatus('gemini', { state: 'quota', since }), providerStatus('claude'), providerStatus('openai', { hasKey: false })],
    })
    renderSelection(failing)
    await waitFor(() => expect(screen.getByTestId('ai-state-gemini').textContent).toContain('Kontingent erschöpft seit'))
    expect(screen.getByTestId('ai-state-claude').textContent).toContain('Bereit')
    expect(screen.getByTestId('ai-state-openai').textContent).toContain('Kein Key gespeichert')

    resetAiProvider.mockResolvedValue(statusFixture())
    fireEvent.click(screen.getByRole('button', { name: /Zurücksetzen/ }))
    await waitFor(() => expect(resetAiProvider).toHaveBeenCalledWith('gemini'))
    await waitFor(() => expect(screen.getByTestId('ai-state-gemini').textContent).toContain('Bereit'))
  })

  it('has an automatic-fallback switch that is off-limits in the mode "ask"', async () => {
    renderSelection(statusFixture())
    const fallback = screen.getByRole('switch', { name: /automatisch zur nächsten KI wechseln/ })
    expect(fallback.getAttribute('aria-checked')).toBe('true')
    fireEvent.click(fallback)
    expect(screen.getByTestId('state').textContent).toBe('default|gemini|false')

    fireEvent.click(screen.getByRole('button', { name: 'Vor jeder Anfrage fragen' }))
    expect((screen.getByRole('switch', { name: /automatisch zur nächsten KI wechseln/ }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getAllByText(/wechselt PlatinVault nie von selbst/).length).toBeGreaterThan(0)
  })
})

const policy = {
  scope: ['compact', 'detailed', 'exhaustive'],
  focus: ['missables', 'collectibles', 'builds', 'bosses', 'roadmap', 'farming', 'difficulty', 'online'],
  spoilers: ['none', 'light', 'all'],
  presentation: ['bullets', 'prose', 'tables'],
}

function BlocksHarness({ initial = emptyResearchBlocks() }: { initial?: ResearchBlocksDto }) {
  const [value, setValue] = useState(initial)
  return (
    <>
      <ResearchBlocksSection value={value} onChange={setValue} policy={policy} />
      <output data-testid="json">{JSON.stringify(value)}</output>
    </>
  )
}

describe('ResearchBlocksSection', () => {
  it('shows every block from the server policy with a German label and starts with nothing chosen', () => {
    render(<BlocksHarness />)
    expect(screen.getByRole('button', { name: 'Kompakt' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Sehr ausführlich' })).toBeTruthy()
    expect(screen.getByRole('button', { name: /Verpassbares zuerst/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: /Online-\/Koop-Trophäen/ })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Spoilerfrei' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Tabellen bevorzugen' })).toBeTruthy()
    expect(screen.getByText('Nichts gewählt: Standard-Recherche.')).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Alle abwählen' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('allows one choice per single group (clicking again removes it) and several focus topics', () => {
    render(<BlocksHarness />)
    fireEvent.click(screen.getByRole('button', { name: 'Kompakt' }))
    fireEvent.click(screen.getByRole('button', { name: 'Sehr ausführlich' }))
    expect(JSON.parse(screen.getByTestId('json').textContent!).scope).toBe('exhaustive')

    fireEvent.click(screen.getByRole('button', { name: 'Sehr ausführlich' }))
    expect(JSON.parse(screen.getByTestId('json').textContent!).scope).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /Boss-Strategien/ }))
    fireEvent.click(screen.getByRole('button', { name: /Farming-Routen/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Leichte Spoiler' }))
    fireEvent.click(screen.getByRole('button', { name: 'Stichpunkte' }))
    const value = JSON.parse(screen.getByTestId('json').textContent!) as ResearchBlocksDto
    expect(value).toEqual({ scope: null, focus: ['bosses', 'farming'], spoilers: 'light', presentation: 'bullets' })
    expect(countResearchBlocks(value)).toBe(4)
    expect(screen.getByText('4 Bausteine gewählt.')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: /Boss-Strategien/ }))
    expect(JSON.parse(screen.getByTestId('json').textContent!).focus).toEqual(['farming'])
  })

  it('clears everything with one click', () => {
    render(<BlocksHarness initial={{ scope: 'compact', focus: ['bosses'], spoilers: 'none', presentation: 'tables' }} />)
    expect(screen.getByText('4 Bausteine gewählt.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Alle abwählen' }))
    expect(JSON.parse(screen.getByTestId('json').textContent!)).toEqual({ scope: null, focus: [], spoilers: null, presentation: null })
  })

  it('shows what the chosen option does, and keeps the room for the longest text', () => {
    render(<BlocksHarness initial={{ scope: 'exhaustive', focus: [], spoilers: null, presentation: null }} />)
    const scope = document.querySelector('[data-group="scope"]')!
    expect(scope.textContent).toContain('Maximale Tiefe')
    // The reserved copies of all the other descriptions are drawn invisibly (data attributes), not as text
    expect(scope.querySelectorAll('.pv-reserve-copy').length).toBe(4)
  })
})
