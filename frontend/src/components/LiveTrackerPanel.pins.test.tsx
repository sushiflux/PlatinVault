import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { LiveAchievementItemDto, LiveGameAchievementsDto, LiveRarityDto } from '../api/types'
import { LiveTrackerPanel } from './LiveTrackerPanel'
import { PinsScope } from './pins/pinsContext'
import { PinsSurface } from './pins/PinsSurface'
import { saveTrophyDensity, TROPHY_DENSITIES, type TrophyDensity } from './TrophyViews'

const APP_ID = 440
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

function ach(apiName: string, patch: Partial<LiveAchievementItemDto> = {}): LiveAchievementItemDto {
  return {
    apiName,
    displayName: `Erfolg ${apiName}`,
    description: `Beschreibung ${apiName}`,
    originalDescription: null,
    isTranslated: false,
    iconUrl: `/icons/${apiName}.png`,
    iconGrayUrl: `/icons/${apiName}-gray.png`,
    achieved: false,
    unlockTime: null,
    globalPercent: 12,
    hasGlobalPercent: true,
    rarityTier: 'Silver',
    rarityLabel: '',
    isHidden: false,
    userNote: '',
    ...patch,
  }
}

function liveData(achievements: LiveAchievementItemDto[], rarity?: LiveRarityDto): LiveGameAchievementsDto {
  const tier = { achieved: 0, total: 0 }
  return {
    game: {
      appId: APP_ID,
      name: 'Team Fortress 2',
      headerImage: '',
      backgroundImage: '',
      isRunning: false,
      playtimeForeverMinutes: 0,
      playtime2WeeksMinutes: 0,
      formattedPlaytimeTotal: '',
      formattedPlaytimeRecent: '',
      formattedLastPlayed: '',
      linkedGuideSlug: null,
    },
    achievements,
    totalCount: achievements.length,
    achievedCount: achievements.filter((a) => a.achieved).length,
    remainingCount: 0,
    progressPercentage: 0,
    is100Percent: false,
    platinum: tier,
    gold: tier,
    silver: tier,
    bronze: tier,
    latestAchievement: null,
    syncedGuideItemsCount: 0,
    ...(rarity ? { rarity } : {}),
  }
}

let pins: string[] = []
let live: LiveGameAchievementsDto

function stubServer() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const target = new URL(String(url), 'http://localhost')
      const method = init?.method ?? 'GET'
      if (target.pathname === '/api/settings') return json({ achievementNotesEnabled: true })
      if (target.pathname === '/api/live/status') return json({ runningAppId: 0, steamId64: '1', runningGameSlug: null, runningGameTracked: false, localSteamAvailable: false, lastBridgeEventAt: null })
      if (target.pathname === '/api/launch/status') return json({ bridgeConnected: false })
      if (target.pathname === '/api/live/achievements') return json(live)
      if (target.pathname === '/api/live/pins') {
        if (method === 'POST') {
          const { apiName } = JSON.parse(String(init?.body)) as { apiName: string }
          if (!pins.includes(apiName)) pins = [...pins, apiName]
        } else if (method === 'DELETE') {
          const apiName = target.searchParams.get('apiName')
          pins = pins.filter((name) => name !== apiName)
        }
        return json({ appId: APP_ID, apiNames: pins, max: 10 })
      }
      return json({}, 404)
    })
  )
}

function renderPanel(withPins: boolean) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      {withPins ? (
        <PinsScope appId={APP_ID} onError={() => {}}>
          <LiveTrackerPanel fixedAppId={APP_ID} tips={{}} />
          <PinsSurface placement="dock" achievements={live.achievements} tips={{}} />
        </PinsScope>
      ) : (
        <LiveTrackerPanel fixedAppId={APP_ID} tips={{}} />
      )}
    </QueryClientProvider>
  )
}

const density = (id: 'list' | 'tiles' | 'compact' | 'large'): TrophyDensity => TROPHY_DENSITIES.find((entry) => entry.id === id)!.value

beforeEach(() => {
  localStorage.clear()
  pins = []
  live = liveData([ach('A1'), ach('A2')])
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query, addEventListener: () => {}, removeEventListener: () => {} }))
  vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} unobserve() {} })
  stubServer()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('the rarity notice in the live trophies tab', () => {
  const stale: LiveRarityDto = { state: 'stale', fetchedAt: '2026-11-03T14:05:00Z', message: 'Die Seltenheitswerte sind vom Vortag.', detail: 'HTTP 504' }

  it('is shown for "stale", before the list, with the complete message', async () => {
    live = liveData([ach('A1')], stale)
    renderPanel(false)
    const notice = await screen.findByTestId('rarity-notice')
    expect(notice.textContent).toContain('Die Seltenheitswerte sind vom Vortag.')
    expect(notice.textContent).toContain('Stand:')
    expect(await screen.findByText('Erfolg A1')).toBeTruthy()
  })

  it('is shown for "unavailable"', async () => {
    live = liveData([ach('A1')], { state: 'unavailable', fetchedAt: null, message: 'Keine Werte.', detail: null })
    renderPanel(false)
    expect((await screen.findByTestId('rarity-notice')).getAttribute('data-state')).toBe('unavailable')
  })

  it('is not shown for "ok", nor when an older server sends no rarity at all', async () => {
    live = liveData([ach('A1')], { state: 'ok', fetchedAt: null, message: null, detail: null })
    renderPanel(false)
    await screen.findByText('Erfolg A1')
    expect(screen.queryByTestId('rarity-notice')).toBeNull()
    cleanup()

    live = liveData([ach('A1')])
    expect('rarity' in live).toBe(false)
    renderPanel(false)
    await screen.findByText('Erfolg A1')
    expect(screen.queryByTestId('rarity-notice')).toBeNull()
  })
})

describe('the pin button in the four views of the trophy list', () => {
  for (const view of ['list', 'tiles', 'compact', 'large'] as const) {
    it(`${view}: every achievement has one; pinning puts the card into the dock and the button turns to "pinned"`, async () => {
      saveTrophyDensity(density(view))
      renderPanel(true)
      await screen.findByText('Erfolg A1')

      const buttons = await screen.findAllByRole('button', { name: /im Blick behalten$/ })
      expect(buttons).toHaveLength(2)
      expect(screen.queryByTestId('pins-dock')).toBeNull()

      fireEvent.click(screen.getByRole('button', { name: '„Erfolg A2“ im Blick behalten' }))
      const dock = await screen.findByTestId('pins-dock')
      expect(within(dock).getAllByTestId('pin-card').map((card) => card.getAttribute('data-api-name'))).toEqual(['A2'])
      expect(screen.getAllByRole('button', { name: '„Erfolg A2“ nicht mehr im Blick' }).length).toBeGreaterThanOrEqual(1)
      expect(pins).toEqual(['A2'])
    })
  }

  it('has no pin button on a page without pins (a game without a Steam link)', async () => {
    renderPanel(false)
    await screen.findByText('Erfolg A1')
    expect(screen.queryByRole('button', { name: /im Blick behalten/ })).toBeNull()
  })

  it('at 10 pins the button of an unpinned achievement is disabled in every view', async () => {
    pins = ['P1', 'P2', 'P3', 'P4', 'P5', 'P6', 'P7', 'P8', 'P9', 'P10']
    saveTrophyDensity(density('tiles'))
    renderPanel(true)
    await screen.findByText('Erfolg A1')
    await waitFor(() => expect(screen.getByRole('button', { name: '„Erfolg A1“ im Blick behalten' }).getAttribute('aria-disabled')).toBe('true'))
  })

  it('the dialog of a list entry has the pin button too', async () => {
    saveTrophyDensity(density('tiles'))
    renderPanel(true)
    fireEvent.click(await screen.findByRole('button', { name: /^Erfolg A1, Silber, noch offen/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Erfolg A1' })
    fireEvent.click(within(dialog).getByRole('button', { name: '„Erfolg A1“ im Blick behalten' }))
    await waitFor(() => expect(pins).toEqual(['A1']))
    await waitFor(() => expect(within(dialog).getByRole('button', { name: '„Erfolg A1“ nicht mehr im Blick' })).toBeTruthy())
  })
})
