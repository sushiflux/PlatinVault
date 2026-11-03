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

  it('stays exactly as before without a count (no credentials): the plain link, no badge', () => {
    renderButton(undefined)
    expect(link().getAttribute('aria-label')).toBe('Live auf Twitch: Portal 2')
    expect(link().querySelector('.pv-bar-live')).toBeNull()
  })

  it('shows the number of live streams as a badge and says it in words', () => {
    renderButton({ count: 12, atLeast: false })
    expect(link().querySelector('.pv-bar-live')?.textContent).toBe('12')
    expect(link().getAttribute('aria-label')).toBe('Live auf Twitch: Portal 2, 12 Streams live')
    // The badge is decoration for the eye; the words are on the link
    expect(link().querySelector('.pv-bar-live')?.getAttribute('aria-hidden')).toBe('true')
  })

  it('writes one stream in the singular and a capped count as "300+"', () => {
    renderButton({ count: 1, atLeast: false })
    expect(link().getAttribute('aria-label')).toContain('1 Stream live')
    cleanup()
    renderButton({ count: 300, atLeast: true })
    expect(link().querySelector('.pv-bar-live')?.textContent).toBe('300+')
    expect(link().getAttribute('aria-label')).toContain('Mehr als 300 Streams live')
  })

  it('shows no badge for zero streams (the tooltip says that nobody is streaming) and keeps the label as it was', () => {
    renderButton({ count: 0, atLeast: false })
    expect(link().querySelector('.pv-bar-live')).toBeNull()
    expect(link().getAttribute('aria-label')).toBe('Live auf Twitch: Portal 2')
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
