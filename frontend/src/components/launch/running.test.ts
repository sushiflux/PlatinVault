import { describe, expect, it } from 'vitest'
import type { LiveStatusDto } from '../../api/types'
import { isRunningGame } from './running'

const status = (over: Partial<LiveStatusDto>): LiveStatusDto => ({
  runningAppId: 0,
  steamId64: '1',
  runningGameSlug: null,
  runningGameTracked: false,
  localSteamAvailable: true,
  lastBridgeEventAt: null,
  ...over,
})

describe('isRunningGame', () => {
  it('is false while the status is unknown', () => {
    expect(isRunningGame(undefined, { kind: 'game', slug: 'a' }, 5)).toBe(false)
  })

  it('matches a vault game by its slug', () => {
    expect(isRunningGame(status({ runningGameSlug: 'portal-2', runningAppId: 620 }), { kind: 'game', slug: 'portal-2' })).toBe(true)
    expect(isRunningGame(status({ runningGameSlug: 'other', runningAppId: 99 }), { kind: 'game', slug: 'portal-2' })).toBe(false)
  })

  it('matches a vault game by its Steam AppID when the server reports no slug', () => {
    expect(isRunningGame(status({ runningAppId: 620 }), { kind: 'game', slug: 'portal-2' }, 620)).toBe(true)
    expect(isRunningGame(status({ runningAppId: 620 }), { kind: 'game', slug: 'portal-2' }, 70)).toBe(false)
    expect(isRunningGame(status({ runningAppId: 620 }), { kind: 'game', slug: 'portal-2' }, null)).toBe(false)
  })

  it('matches a library game (no vault entry) by the AppID of its target', () => {
    expect(isRunningGame(status({ runningAppId: 620 }), { kind: 'library', appId: 620 })).toBe(true)
    expect(isRunningGame(status({ runningAppId: 620 }), { kind: 'library', appId: 70 })).toBe(false)
  })

  it('never matches when nothing runs (AppID 0)', () => {
    expect(isRunningGame(status({ runningAppId: 0 }), { kind: 'library', appId: 0 })).toBe(false)
    expect(isRunningGame(status({}), { kind: 'game', slug: 'a' }, 0)).toBe(false)
  })
})
