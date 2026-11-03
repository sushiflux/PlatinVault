import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AiStatusDto, GameDetailDto, GuideSectionDto } from '../../api/types'
import { FailedChapterBlock, failedChapterTabs, RetryAllBanner } from './FailedChapter'
import { statusFixture } from './ai.fixtures'

const fetchAiStatus = vi.hoisted(() => vi.fn<() => Promise<AiStatusDto>>())
vi.mock('../../api/client', () => ({ fetchAiStatus }))

const failedSection = (over: Partial<NonNullable<GuideSectionDto['failedChapter']>> = {}): GuideSectionDto => ({
  type: 'alert',
  variant: 'danger',
  title: 'Dieses Kapitel konnte nicht recherchiert werden',
  htmlContent: 'Claude (Anthropic) hat die Anfrage abgelehnt (HTTP 400).',
  failedChapter: {
    focus: 'Beste Waffen, Rüstungen und wo man sie findet',
    topicId: 'weapons',
    message: 'Claude (Anthropic) hat die Anfrage abgelehnt (HTTP 400).',
    providerMessage: 'temperature: this parameter is not supported by claude-haiku-5-5',
    provider: 'claude',
    ...over,
  },
})

function wrap(node: React.ReactNode) {
  fetchAiStatus.mockResolvedValue(statusFixture())
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}>{node}</QueryClientProvider>)
}

beforeEach(() => fetchAiStatus.mockReset())
afterEach(cleanup)

describe('FailedChapterBlock', () => {
  it('says what happened, shows the provider text only behind "technical details", and never asks to copy the topic', async () => {
    wrap(<FailedChapterBlock section={failedSection()} busy={false} onRetry={() => {}} onRemove={() => {}} />)

    expect(screen.getByText('Dieses Kapitel konnte nicht recherchiert werden')).toBeTruthy()
    expect(screen.getByText('Claude (Anthropic) hat die Anfrage abgelehnt (HTTP 400).')).toBeTruthy()
    // the provider's raw text is folded away until it is asked for - then it is shown completely
    expect(screen.queryByText(/temperature: this parameter/)).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: /Technische Details/ }))
    expect(screen.getByText('temperature: this parameter is not supported by claude-haiku-5-5')).toBeTruthy()
    // the old instruction is gone: no "Tab ergänzen", the topic text is not on the screen
    const text = screen.getByTestId('failed-chapter').textContent ?? ''
    expect(text).not.toContain('Tab ergänzen')
    expect(text).not.toContain('Beste Waffen, Rüstungen')
    expect(text).toContain('du musst nichts kopieren')
  })

  it('has one primary button that researches exactly this chapter again, with the AI the buttons name', async () => {
    const onRetry = vi.fn()
    wrap(<FailedChapterBlock section={failedSection()} busy={false} onRetry={onRetry} onRemove={() => {}} />)

    fireEvent.click(screen.getByRole('button', { name: 'Kapitel erneut recherchieren' }))
    expect(onRetry).toHaveBeenCalledTimes(1)
    expect(onRetry).toHaveBeenLastCalledWith(undefined)

    await screen.findByRole('button', { name: /KI für diese Aktion wählen: Gemini/ })
    fireEvent.click(screen.getByRole('button', { name: /KI für diese Aktion wählen: Gemini/ }))
    fireEvent.click(screen.getByRole('menuitemradio', { name: /Claude/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Kapitel erneut recherchieren' }))
    expect(onRetry).toHaveBeenLastCalledWith('claude')
  })

  it('lets the chapter be taken out of the guide when the game does not need it', () => {
    const onRemove = vi.fn()
    wrap(<FailedChapterBlock section={failedSection()} busy={false} onRetry={() => {}} onRemove={onRemove} />)

    fireEvent.click(screen.getByRole('button', { name: 'Kapitel entfernen' }))
    expect(onRemove).toHaveBeenCalledTimes(1)
  })

  it('shows nothing for a section without a remembered failure and waits while a research runs', () => {
    const { container, rerender } = wrap(<FailedChapterBlock section={{ type: 'alert', title: 'x' }} busy={false} onRetry={() => {}} onRemove={() => {}} />)
    expect(container.textContent).toBe('')

    rerender(
      <QueryClientProvider client={new QueryClient()}>
        <FailedChapterBlock section={failedSection()} busy onRetry={() => {}} onRemove={() => {}} />
      </QueryClientProvider>
    )
    expect((screen.getByRole('button', { name: /Kapitel erneut recherchieren/ }) as HTMLButtonElement).disabled).toBe(true)
  })
})

describe('RetryAllBanner', () => {
  it('names how many chapters failed and offers the one button for all of them', () => {
    const onRetryAll = vi.fn()
    wrap(<RetryAllBanner count={3} busy={false} onRetryAll={onRetryAll} />)

    expect(screen.getByText('3 Kapitel konnten nicht recherchiert werden')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Alle fehlgeschlagenen Kapitel erneut recherchieren' }))
    expect(onRetryAll).toHaveBeenCalledWith(undefined)
  })
})

describe('failedChapterTabs', () => {
  it('lists the chapters whose only section is a failed-chapter placeholder', () => {
    const game = {
      tabs: [
        { id: 'a', title: 'Waffen', sections: [failedSection()] },
        { id: 'b', title: 'Bosse', sections: [{ type: 'story', paragraphs: ['Text'] }] },
        { id: 'c', title: 'Mix', sections: [failedSection(), { type: 'story' }] },
      ],
    } as unknown as GameDetailDto

    expect(failedChapterTabs(game)).toEqual([{ id: 'a', title: 'Waffen' }])
    expect(failedChapterTabs(undefined)).toEqual([])
  })
})
