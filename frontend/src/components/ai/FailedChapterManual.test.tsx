import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { GameDetailDto } from '../../api/types'
import { failedChapterSeeds, RetryAllBanner } from './FailedChapter'

vi.mock('./AiRunButton', () => ({ AiRunButton: () => <button type="button">retry-all</button> }))

afterEach(() => cleanup())

const failed = (focus: string, topicId: string) => [{ type: 'alert', failedChapter: { focus, topicId, message: 'x', providerMessage: null, provider: null } }]

describe('failed chapters via the AI subscription', () => {
  it('turns every failed chapter into a seed that carries its tab, so the answer replaces the placeholder', () => {
    const game = {
      tabs: [
        { id: 'tab-1', title: 'Gegner', icon: 'mdi:skull', sections: failed('Alle Gegner', 'bestiary') },
        { id: 'tab-2', title: 'Fertig', icon: 'mdi:ok', sections: [{ type: 'story' }] },
        { id: 'tab-3', title: 'Quests', icon: 'mdi:script', sections: failed('Alle Quests', 'quests') },
      ],
    } as unknown as GameDetailDto
    expect(failedChapterSeeds(game)).toEqual([
      { title: 'Gegner', icon: 'mdi:skull', focus: 'Alle Gegner', topicId: 'bestiary', tabId: 'tab-1' },
      { title: 'Quests', icon: 'mdi:script', focus: 'Alle Quests', topicId: 'quests', tabId: 'tab-3' },
    ])
  })

  it('offers "all with my AI subscription" next to the API retry, only when the page can open it', () => {
    const onManual = vi.fn()
    const { rerender } = render(<RetryAllBanner count={3} busy={false} onRetryAll={vi.fn()} onManual={onManual} />)
    fireEvent.click(screen.getByTestId('retry-all-manual'))
    expect(onManual).toHaveBeenCalledTimes(1)
    rerender(<RetryAllBanner count={3} busy={false} onRetryAll={vi.fn()} />)
    expect(screen.queryByTestId('retry-all-manual')).toBeNull()
  })
})
