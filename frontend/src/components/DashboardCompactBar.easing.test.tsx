import { Profiler, useState } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { applyLanguage } from '../i18n'
import { DashboardCompactBar } from './DashboardCompactBar'

// The bar eases when it is taken hold of or let go: the transition flag must be in the very commit in which the engaged state flips,
// otherwise the opacity jumps within one frame instead of easing.

function Harness({ onCommit }: { onCommit: () => void }) {
  const [search, setSearch] = useState('')
  return (
    <Profiler id="bar" onRender={onCommit}>
      <DashboardCompactBar
        search={search}
        onSearch={setSearch}
        hasLibrary={false}
        view="favorites"
        onSelectView={() => undefined}
        favoritesCount={2}
        libraryCount={undefined}
        density={1}
        onDensity={() => undefined}
        sortView="favorites"
        sortChoice={{ order: 'custom', direction: 'asc' }}
        sortLoading={false}
        onSortChange={() => undefined}
        onEngagedChange={() => undefined}
        barRef={() => undefined}
        interactive
      />
    </Profiler>
  )
}

describe('DashboardCompactBar easing', () => {
  beforeEach(async () => {
    await applyLanguage('de')
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query, addEventListener: () => undefined, removeEventListener: () => undefined, addListener: () => undefined, removeListener: () => undefined, onchange: null, dispatchEvent: () => false }))
    vi.useFakeTimers({ shouldAdvanceTime: false })
  })

  afterEach(() => {
    cleanup()
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('has data-easing in the same commit in which data-engaged flips, on taking hold and on letting go, and clears it afterwards', () => {
    // Every commit, as the DOM looks right after it: [engaged, easing]
    const commits: Array<[string | null, string | null]> = []
    const bar = () => screen.queryByRole('group', { name: 'Suchen, Ansicht und Sortierung' })
    render(<Harness onCommit={() => commits.push([bar()?.getAttribute('data-engaged') ?? null, bar()?.getAttribute('data-easing') ?? null])} />)
    const group = bar()!
    // Calm at rest: no transition (the scroll-linked fade must follow the scroll without lag)
    expect(group.getAttribute('data-easing')).toBeNull()

    commits.length = 0
    act(() => screen.getByRole('searchbox').focus())
    expect(commits.some(([engaged]) => engaged === 'true')).toBe(true)
    // No commit shows engaged without easing
    expect(commits.filter(([engaged, easing]) => engaged === 'true' && easing !== 'true')).toEqual([])
    expect(group.getAttribute('data-easing')).toBe('true')

    act(() => {
      vi.advanceTimersByTime(300)
    })
    expect(group.getAttribute('data-easing')).toBeNull()
    expect(group.getAttribute('data-engaged')).toBe('true')

    // Let go: the commit in which engaged goes away carries the easing flag
    commits.length = 0
    act(() => {
      fireEvent.blur(screen.getByRole('searchbox'), { relatedTarget: null })
    })
    expect(commits.some(([engaged]) => engaged === null)).toBe(true)
    expect(commits.filter(([engaged, easing]) => engaged === null && easing !== 'true')).toEqual([])
    expect(group.getAttribute('data-engaged')).toBeNull()

    act(() => {
      vi.advanceTimersByTime(300)
    })
    expect(group.getAttribute('data-easing')).toBeNull()
  })
})
