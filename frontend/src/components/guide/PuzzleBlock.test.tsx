import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GuideSectionDto } from '../../api/types'
import { GuideHeading, HintAlert, StepsStory } from './GuideBlocks'
import { PuzzleBlock } from './PuzzleBlock'
import { PuzzleScopeContext } from './puzzleState'

const puzzleSection = (over: Partial<NonNullable<GuideSectionDto['puzzle']>> = {}): GuideSectionDto => ({
  type: 'puzzle',
  title: 'Grabstein in der Rubinhöhle',
  htmlContent: 'Ein Grabstein verlangt drei Rubinfragmente.',
  puzzle: {
    itemId: 'rubin-raetsel',
    location: 'Rubinhöhle, Olderion-Föderation',
    requirements: 'Ab Kapitel 3',
    missable: true,
    hints: ['Die Farben der Fragmente zählen.', 'Fang mit dem roten an.', 'Rot, Blau, Grün.'],
    steps: ['Nimm das rote Fragment am Eingang.', 'Nimm das blaue Fragment im Nordtunnel.', 'Nimm das grüne Fragment am See.'],
    source: 'PowerPyx',
    documented: true,
    ...over,
  },
})

function renderPuzzle(section: GuideSectionDto = puzzleSection(), checked: string[] = [], onToggle = vi.fn(async () => {})) {
  const view = render(
    <PuzzleScopeContext.Provider value="final-fantasy-resonance">
      <PuzzleBlock section={section} checkedSet={new Set(checked)} onToggleCheck={onToggle} />
    </PuzzleScopeContext.Provider>
  )
  return { ...view, onToggle }
}

const hintButton = () => screen.getByRole('button', { name: /Tipp/ })
const solutionButton = () => screen.getByRole('button', { name: /Lösungsweg/ })

beforeEach(() => window.localStorage.clear())
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('PuzzleBlock', () => {
  it('shows what and where openly and keeps hints and solution hidden', () => {
    renderPuzzle()

    expect(screen.getByRole('heading', { name: 'Grabstein in der Rubinhöhle' })).toBeTruthy()
    expect(screen.getByText('Rubinhöhle, Olderion-Föderation')).toBeTruthy()
    expect(screen.getByText('Ab Kapitel 3')).toBeTruthy()
    expect(screen.getByText('Verpassbar')).toBeTruthy()
    expect(screen.queryByText('Nimm das rote Fragment am Eingang.')).toBeNull()
    expect(screen.queryByText('Die Farben der Fragmente zählen.')).toBeNull()
    expect(hintButton().getAttribute('aria-expanded')).toBe('false')
    expect(solutionButton().getAttribute('aria-expanded')).toBe('false')
  })

  it('reveals the hints one at a time and hides them again with the same button', async () => {
    renderPuzzle()
    const button = hintButton()

    fireEvent.click(button)
    expect(screen.getByText('Die Farben der Fragmente zählen.')).toBeTruthy()
    expect(screen.queryByText('Fang mit dem roten an.')).toBeNull()
    expect(button.getAttribute('aria-expanded')).toBe('true')

    fireEvent.click(button)
    expect(screen.getByText('Fang mit dem roten an.')).toBeTruthy()
    expect(screen.queryByText('Rot, Blau, Grün.')).toBeNull()
    expect(screen.getByText('Tipp 2 von 3')).toBeTruthy()

    fireEvent.click(button)
    expect(screen.getByText('Rot, Blau, Grün.')).toBeTruthy()

    // All hints are shown: the same button now hides them
    fireEvent.click(button)
    await waitFor(() => expect(screen.queryByText('Die Farben der Fragmente zählen.')).toBeNull())
    expect(button.getAttribute('aria-expanded')).toBe('false')
  })

  it('reveals the complete solution in order, names the source, and hides it again', async () => {
    renderPuzzle()
    const button = solutionButton()
    const controlled = button.getAttribute('aria-controls')
    expect(controlled).toBeTruthy()

    fireEvent.click(button)
    const region = await screen.findByRole('region', { name: 'Lösungsweg' })
    const steps = within(region).getAllByRole('listitem').map((li) => li.textContent)
    expect(steps).toEqual(['Nimm das rote Fragment am Eingang.', 'Nimm das blaue Fragment im Nordtunnel.', 'Nimm das grüne Fragment am See.'])
    expect(within(region).getByText('Quelle: PowerPyx')).toBeTruthy()
    expect(button.getAttribute('aria-expanded')).toBe('true')
    expect(document.getElementById(controlled!)?.contains(region)).toBe(true)

    fireEvent.click(button)
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Lösungsweg' })).toBeNull())
    expect(button.getAttribute('aria-expanded')).toBe('false')
  })

  it('moves nothing but itself when revealing: the buttons stay the same elements in the same place and keep all labels', async () => {
    const { container } = renderPuzzle()
    const article = container.querySelector('article')!
    const actions = container.querySelector('.pv-puzzle-actions')!
    const before = Array.from(article.children).map((el) => el.className)
    const labelsBefore = actions.textContent
    const hintNode = hintButton()
    const solutionNode = solutionButton()

    fireEvent.click(hintNode)
    fireEvent.click(solutionNode)
    await screen.findByRole('region', { name: 'Lösungsweg' })

    // Everything above and including the buttons is unchanged; the revealed areas come after them
    expect(Array.from(article.children).slice(0, before.length).map((el) => el.className)).toEqual(before)
    expect(container.querySelector('.pv-puzzle-actions')).toBe(actions)
    expect(hintButton()).toBe(hintNode)
    expect(solutionButton()).toBe(solutionNode)
    // The label text is stacked, so the button is as wide as its widest label whatever it shows right now
    expect(actions.textContent).toBe(labelsBefore)
    expect(within(solutionNode).getByText('Lösungsweg verbergen')).toBeTruthy()
    expect(within(solutionNode).getByText('Lösungsweg zeigen')).toBeTruthy()
  })

  it('can be operated with the keyboard (native buttons: Enter and Space click them)', () => {
    renderPuzzle()
    const button = solutionButton()
    button.focus()
    expect(document.activeElement).toBe(button)
    expect(button.tagName).toBe('BUTTON')
    expect(button.getAttribute('type')).toBe('button')
  })

  it('says "solution not documented" instead of offering a solution button, but still offers hints', () => {
    renderPuzzle(puzzleSection({ steps: [], documented: false }))

    expect(screen.getByText('Lösung nicht dokumentiert')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Lösungsweg/ })).toBeNull()
    expect(hintButton()).toBeTruthy()
    expect(screen.getByText('Rubinhöhle, Olderion-Föderation')).toBeTruthy()
  })

  it('has no buttons at all without hints and steps', () => {
    renderPuzzle(puzzleSection({ steps: [], hints: [], documented: false }))
    expect(screen.queryAllByRole('button')).toHaveLength(0)
    expect(screen.getByText('Lösung nicht dokumentiert')).toBeTruthy()
  })

  it('remembers what was revealed in this browser, per game', () => {
    const first = renderPuzzle()
    fireEvent.click(hintButton())
    fireEvent.click(solutionButton())
    first.unmount()

    renderPuzzle()
    expect(screen.getByText('Die Farben der Fragmente zählen.')).toBeTruthy()
    expect(screen.getByText('Nimm das rote Fragment am Eingang.')).toBeTruthy()
    cleanup()

    // The same puzzle id in another game starts closed
    render(
      <PuzzleScopeContext.Provider value="another-game">
        <PuzzleBlock section={puzzleSection()} checkedSet={new Set()} onToggleCheck={async () => {}} />
      </PuzzleScopeContext.Provider>
    )
    expect(screen.queryByText('Nimm das rote Fragment am Eingang.')).toBeNull()
  })

  it('keeps the revealed state of a puzzle without item id by its position, not by its title', () => {
    const noId = (title: string): GuideSectionDto => ({ ...puzzleSection({ itemId: null }), title })
    const render2 = (title: string, stateKey: string) =>
      render(
        <PuzzleScopeContext.Provider value="final-fantasy-resonance">
          <PuzzleBlock section={noId(title)} checkedSet={new Set()} onToggleCheck={async () => {}} stateKey={stateKey} />
        </PuzzleScopeContext.Provider>
      )

    // Two puzzles with the same name at different places do not share what was revealed
    const first = render2('Geheimtür', 'tab-1:4')
    fireEvent.click(solutionButton())
    expect(screen.getByText('Nimm das rote Fragment am Eingang.')).toBeTruthy()
    first.unmount()
    render2('Geheimtür', 'tab-1:9')
    expect(screen.queryByText('Nimm das rote Fragment am Eingang.')).toBeNull()
    cleanup()

    // The same place in the other language (translated title) is still revealed
    render2('Secret door', 'tab-1:4')
    expect(screen.getByText('Nimm das rote Fragment am Eingang.')).toBeTruthy()
  })

  it('still works when the browser storage is blocked', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    renderPuzzle()
    fireEvent.click(solutionButton())
    expect(screen.getByText('Nimm das rote Fragment am Eingang.')).toBeTruthy()
  })

  it('can be ticked off like a checklist row when it has an item id', () => {
    const { onToggle } = renderPuzzle()
    const box = screen.getByRole('checkbox', { name: 'Rätsel „Grabstein in der Rubinhöhle“ als erledigt markieren' })
    fireEvent.click(box)
    expect(onToggle).toHaveBeenCalledWith('rubin-raetsel', true)
  })

  it('shows a done puzzle as ticked and has no checkbox without an item id', () => {
    const done = renderPuzzle(puzzleSection(), ['rubin-raetsel'])
    expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(true)
    done.unmount()

    renderPuzzle(puzzleSection({ itemId: null }))
    expect(screen.queryByRole('checkbox')).toBeNull()
  })
})

describe('walkthrough blocks', () => {
  it('labels each hint kind in the language of the app', () => {
    const labels: Record<string, string> = {
      must_have: 'Wichtig – unbedingt mitnehmen',
      missable: 'Verpassbar ab hier',
      optional: 'Optional hier verfügbar',
      tip: 'Tipp',
    }
    for (const [kind, label] of Object.entries(labels)) {
      const { container, unmount } = render(<HintAlert section={{ type: 'alert', kind, title: 'Ramuhs Stab', htmlContent: 'Liegt in der Truhe.' }} />)
      expect(screen.getByText(label)).toBeTruthy()
      expect(container.querySelector(`[data-hint-kind="${kind}"]`)).toBeTruthy()
      expect(screen.getByText('Liegt in der Truhe.')).toBeTruthy()
      unmount()
    }
  })

  it('uses the colour of the variant the server set and falls back to the kind\'s own colour', () => {
    const { container, rerender } = render(<HintAlert section={{ type: 'alert', kind: 'missable', variant: 'danger', title: 'x' }} />)
    expect(container.querySelector('.pv-alert-danger')).toBeTruthy()
    rerender(<HintAlert section={{ type: 'alert', kind: 'must_have', title: 'x' }} />)
    expect(container.querySelector('.pv-alert-gold')).toBeTruthy()
  })

  it('renders a sub-heading with its lead and the steps as a numbered list', () => {
    render(
      <>
        <GuideHeading section={{ type: 'heading', title: 'Kapitel 1 – Grandshelt', htmlContent: 'Empfohlenes Level 11' }} />
        <StepsStory section={{ type: 'story', variant: 'steps', paragraphs: ['Geh nach Norden.', 'Sprich mit Ada.'] }} />
      </>
    )
    expect(screen.getByRole('heading', { name: 'Kapitel 1 – Grandshelt' })).toBeTruthy()
    expect(screen.getByText('Empfohlenes Level 11')).toBeTruthy()
    const items = screen.getAllByRole('listitem').map((li) => li.textContent)
    expect(items).toEqual(['Geh nach Norden.', 'Sprich mit Ada.'])
    expect(screen.getByRole('list', { name: 'Schritte' }).tagName).toBe('OL')
  })
})
