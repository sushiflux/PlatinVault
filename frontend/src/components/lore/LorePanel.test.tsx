import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { applyLanguage } from '../../i18n'
import type { GuideTabDto } from '../../api/types'
import { LorePanel } from './LorePanel'

vi.mock('../ai/AiRunButton', () => ({
  AiRunButton: ({ label, onClick }: { label: React.ReactNode; onClick?: () => void }) => (
    <button type="button" onClick={onClick}>
      {label}
    </button>
  ),
}))

const TAB: GuideTabDto = {
  id: 'lore-1',
  slug: 'story-lore',
  title: 'Story & Lore',
  icon: 'mdi:book',
  sortOrder: 900,
  totalCheckableItems: 0,
  checkedItemsCount: 0,
  isAchievementList: false,
  groupKey: 'story',
  sections: [
    { type: 'story', title: 'Die Story', paragraphs: ['Erster Absatz.', 'Zweiter Absatz.'], spoiler: true },
    { type: 'alert', title: 'Hinweis', variant: 'gold', htmlContent: 'Text', spoiler: true },
  ],
}

function renderPanel(over: Partial<React.ComponentProps<typeof LorePanel>> = {}) {
  const props = {
    tab: TAB,
    revealed: false,
    busy: false,
    onReveal: vi.fn(),
    onResearch: vi.fn(),
    renderSection: vi.fn((section: { title?: string | null }) => <div data-testid="other">{section.title}</div>),
    ...over,
  }
  render(
    <QueryClientProvider client={new QueryClient()}>
      <LorePanel {...props} />
    </QueryClientProvider>
  )
  return props
}

describe('LorePanel', () => {
  beforeEach(async () => {
    await applyLanguage('de')
  })

  afterEach(() => cleanup())

  it('asks before showing the story and reports whether to remember the choice', () => {
    const props = renderPanel()
    expect(screen.getByText('Achtung: Story-Spoiler')).toBeTruthy()
    expect(screen.queryByText('Erster Absatz.')).toBeNull()
    fireEvent.click(screen.getByRole('switch'))
    fireEvent.click(screen.getByRole('button', { name: 'Spoiler anzeigen' }))
    expect(props.onReveal).toHaveBeenCalledWith(true)
  })

  it('shows story paragraphs as running text and hands other sections to the guide renderer', () => {
    const props = renderPanel({ revealed: true })
    expect(screen.getByText('Erster Absatz.')).toBeTruthy()
    expect(screen.getByText('Zweiter Absatz.')).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Die Story' })).toBeTruthy()
    expect(props.renderSection).toHaveBeenCalledTimes(1)
    expect(props.renderSection).toHaveBeenCalledWith(TAB.sections[1], 'lore-1:1')
    expect(screen.getByTestId('other').textContent).toBe('Hinweis')
  })

  it('offers the research when there is no Story & Lore tab yet', () => {
    const props = renderPanel({ tab: undefined })
    expect(screen.getByText('Noch keine Story & Lore für dieses Spiel.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Story & Lore recherchieren' }))
    expect(props.onResearch).toHaveBeenCalled()
  })
})
