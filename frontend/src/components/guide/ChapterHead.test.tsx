import { cleanup, render } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import '../../i18n'
import { ChapterHead } from './ChapterHead'

afterEach(() => cleanup())

describe('ChapterHead title', () => {
  it('drops only a leading "3. " chapter number, like the navigation does', () => {
    const { container } = render(<ChapterHead gameTitle="Game" title="3. Quests & Nebenaufgaben" />)
    expect(container.querySelector('h2')?.textContent).toBe('Quests & Nebenaufgaben')
  })

  it('keeps a title that starts with a lower-case "d" untouched', () => {
    const { container } = render(<ChapterHead gameTitle="Game" title="dass du alles findest" />)
    expect(container.querySelector('h2')?.textContent).toBe('dass du alles findest')
  })
})
