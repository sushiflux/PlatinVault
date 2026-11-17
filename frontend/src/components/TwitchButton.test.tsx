import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { applyLanguage } from '../i18n'
import { TwitchButton } from './TwitchButton'

function renderButton(liveCount?: { count: number; atLeast: boolean }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <TwitchButton name="Portal 2" labelText="Twitch" className="pv-bar-seg" liveCount={liveCount} />
    </QueryClientProvider>
  )
}

const link = () => screen.getByRole('link')

describe('TwitchButton live count', () => {
  beforeEach(async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ twitchStreamsEnabled: true }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
    await applyLanguage('de')
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('stays exactly as before without a count (no credentials): the plain link, no count', () => {
    renderButton(undefined)
    expect(link().getAttribute('aria-label')).toBe('Live auf Twitch: Portal 2')
    expect(link().querySelector('.pv-bar-count')).toBeNull()
  })

  it('shows the number of live streams beside the logo and says it in words', () => {
    renderButton({ count: 12, atLeast: false })
    const count = link().querySelector('.pv-bar-count')
    expect(count?.textContent).toBe('12')
    // A child of the link next to its label (positioned beside the logo by CSS), not a corner badge laid over it
    expect(count?.previousElementSibling?.className).toBe('pv-bar-label')
    expect(count?.querySelector('.pv-bar-live-dot')).toBeTruthy()
    expect(link().querySelector('.pv-bar-live')).toBeNull()
    expect(link().getAttribute('aria-label')).toBe('Live auf Twitch: Portal 2, 12 Streams live')
    // The badge is decoration for the eye; the words are on the link
    expect(count?.getAttribute('aria-hidden')).toBe('true')
  })

  it('writes one stream in the singular and a capped count as "300+"', () => {
    renderButton({ count: 1, atLeast: false })
    expect(link().getAttribute('aria-label')).toContain('1 Stream live')
    cleanup()
    renderButton({ count: 300, atLeast: true })
    expect(link().querySelector('.pv-bar-count')?.textContent).toBe('300+')
    expect(link().getAttribute('aria-label')).toContain('Mehr als 300 Streams live')
  })

  it('shows no number for zero streams (the tooltip says that nobody is streaming) and keeps the label as it was', () => {
    renderButton({ count: 0, atLeast: false })
    expect(link().querySelector('.pv-bar-count')).toBeNull()
    expect(link().getAttribute('aria-label')).toBe('Live auf Twitch: Portal 2')
  })

  it('marks the segment live with Twitch credentials, with or without a count, and draws a count element only when there is a number', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ twitchStreamsEnabled: true, hasTwitchKey: true }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
    renderButton(undefined)
    await waitFor(() => expect(link().className).toContain('pv-bar-seg--live'))
    // The logo keeps its place either way: nothing is drawn in the line for a missing number
    expect(link().querySelector('.pv-bar-count')).toBeNull()
    expect(link().querySelector('.pv-bar-seg-icon')).toBeTruthy()
    cleanup()
    renderButton({ count: 7, atLeast: false })
    await waitFor(() => expect(link().className).toContain('pv-bar-seg--live'))
    expect(link().querySelector('.pv-bar-count')?.textContent).toBe('7')
  })

  it('speaks English when the app does', async () => {
    await applyLanguage('en')
    renderButton({ count: 2, atLeast: false })
    expect(link().getAttribute('aria-label')).toBe('Live on Twitch: Portal 2, 2 live streams')
  })

  it('is gone while the switch in the settings is off', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ twitchStreamsEnabled: false }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
    renderButton({ count: 5, atLeast: false })
    await waitFor(() => expect(screen.queryByRole('link')).toBeNull())
  })
})
