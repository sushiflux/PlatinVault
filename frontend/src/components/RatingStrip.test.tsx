import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { applyLanguage } from '../i18n'
import { RatingStrip } from './RatingStrip'
import type { RatingSource } from '../utils/ratings'

function renderStrip(game: RatingSource, settings: { hasTwitchKey: boolean }) {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ twitchStreamsEnabled: true, ...settings }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <RatingStrip game={game} />
    </QueryClientProvider>
  )
}

const all: RatingSource = { reviewPercent: 96, reviewScoreDesc: 'Sehr positiv', metacriticScore: 91, igdbRating: 88, igdbUserRating: 80 }
// Number formats use no-break spaces: compare with plain ones
const textOf = (container: HTMLElement) => (container.textContent ?? '').replace(/\s/g, ' ')
const slotKeys = (container: HTMLElement) => [...container.querySelectorAll<HTMLElement>('.pv-rate-in')].map((slot) => slot.dataset.rating)

describe('RatingStrip', () => {
  beforeEach(async () => {
    await applyLanguage('de')
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('holds its room (and says nothing) while the settings are loading', () => {
    const { container } = renderStrip(all, { hasTwitchKey: true })
    expect(container.querySelector('.pv-rate')).toBeTruthy()
    expect(container.querySelectorAll('.pv-rate-in')).toHaveLength(0)
    expect(screen.queryByText('Noch keine Bewertungen')).toBeNull()
  })

  it('shows four slots with IGDB credentials, every one named for screen readers', async () => {
    const { container } = renderStrip(all, { hasTwitchKey: true })
    await waitFor(() => expect(container.querySelectorAll('.pv-rate-in')).toHaveLength(4))
    expect(slotKeys(container)).toEqual(['steam', 'metacritic', 'igdbCritics', 'igdbPlayers'])
    expect(container.querySelector('.pv-rate')?.getAttribute('data-count')).toBe('4')
    expect(textOf(container)).toContain('Steam-Rezensionen: Sehr positiv: 96 % positive Rezensionen')
    expect(textOf(container)).toContain('Metacritic: 91 von 100 Punkten')
    expect(textOf(container)).toContain('IGDB, Kritiker')
    expect(textOf(container)).toContain('IGDB, Spieler')
    // The visible number is hidden from screen readers (the sentence is theirs)
    expect(container.querySelector('.pv-rate-val')?.getAttribute('aria-hidden')).toBe('true')
    expect(container.querySelector('.pv-rate-pct')?.textContent).toBe('%')
  })

  it('has only the two Steam-side slots without Twitch credentials (no IGDB set up)', async () => {
    const { container } = renderStrip(all, { hasTwitchKey: false })
    await waitFor(() => expect(container.querySelectorAll('.pv-rate-in')).toHaveLength(2))
    expect(slotKeys(container)).toEqual(['steam', 'metacritic'])
  })

  it('leaves a missing rating out, without a dash, and the others share the strip', async () => {
    const { container } = renderStrip({ reviewPercent: 70, metacriticScore: null, igdbRating: null, igdbUserRating: 64 }, { hasTwitchKey: true })
    await waitFor(() => expect(container.querySelectorAll('.pv-rate-in')).toHaveLength(2))
    expect(slotKeys(container)).toEqual(['steam', 'igdbPlayers'])
    expect(textOf(container)).not.toContain('–')
    expect(container.querySelector('.pv-rate')?.getAttribute('data-count')).toBe('2')
  })

  it('shows one calm line when a game has no rating at all, in the same fixed strip', async () => {
    const { container } = renderStrip({ reviewPercent: null, metacriticScore: null, igdbRating: null, igdbUserRating: null }, { hasTwitchKey: true })
    await waitFor(() => expect(screen.getByText('Noch keine Bewertungen')).toBeTruthy())
    expect(container.querySelectorAll('.pv-rate-in')).toHaveLength(0)
    expect(container.querySelector('.pv-rate')?.getAttribute('data-count')).toBe('0')
  })

  it('speaks English when the app does', async () => {
    await applyLanguage('en')
    const { container } = renderStrip({ reviewPercent: 96, reviewScoreDesc: 'Very Positive', metacriticScore: 91 }, { hasTwitchKey: false })
    await waitFor(() => expect(container.querySelectorAll('.pv-rate-in')).toHaveLength(2))
    expect(textOf(container)).toContain('Steam reviews: Very Positive: 96% positive reviews')
    expect(textOf(container)).toContain('Metacritic: 91 out of 100')
  })
})
