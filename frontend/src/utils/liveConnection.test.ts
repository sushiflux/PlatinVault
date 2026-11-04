import { describe, expect, it, vi } from 'vitest'
import type { LiveAchievementItemDto, LiveEventDto, LiveEventsResponseDto } from '../api/types'
import { LIVE_SEEN_KEY, LiveEventGate, connectLiveHub, retryDelayMs } from './liveConnection'
import type { LiveHubConnection } from './liveConnection'

function achievement(apiName: string): LiveAchievementItemDto {
  return {
    apiName,
    displayName: apiName,
    description: '',
    originalDescription: null,
    isTranslated: false,
    iconUrl: '',
    iconGrayUrl: '',
    achieved: true,
    unlockTime: null,
    globalPercent: 25,
    rarityTier: 'Silver',
    rarityLabel: '',
    isHidden: false,
    userNote: '',
  } as LiveAchievementItemDto
}

function event(id: number, apiName = `A${id}`): LiveEventDto {
  return { id, at: '2026-11-02T11:50:55Z', kind: 'achievement', achievement: achievement(apiName), gameName: 'Game', appId: 1, total: null }
}

function response(instanceId: string, events: LiveEventDto[], latestId = events.at(-1)?.id ?? 0): LiveEventsResponseDto {
  return { instanceId, latestId, events }
}

class MemoryStorage {
  private data = new Map<string, string>()
  getItem(key: string) {
    return this.data.get(key) ?? null
  }
  setItem(key: string, value: string) {
    this.data.set(key, value)
  }
  removeItem(key: string) {
    this.data.delete(key)
  }
  clear() {
    this.data.clear()
  }
  key() {
    return null
  }
  get length() {
    return this.data.size
  }
}

/** A hub connection whose start() fails until `up` is set - like the backend during a dotnet-watch restart. */
class FakeConnection implements LiveHubConnection {
  up = false
  starts = 0
  stopped = false
  private handlers = new Map<string, (...args: unknown[]) => void>()
  private reconnected: (() => void) | null = null
  private closed: (() => void) | null = null

  on(name: string, handler: (...args: unknown[]) => void) {
    this.handlers.set(name, handler)
  }
  onreconnected(callback: () => void) {
    this.reconnected = callback
  }
  onclose(callback: () => void) {
    this.closed = callback
  }
  start() {
    this.starts++
    return this.up ? Promise.resolve() : Promise.reject(new Error('Failed to complete negotiation with the server'))
  }
  stop() {
    this.stopped = true
    return Promise.resolve()
  }
  emit(name: string, ...args: unknown[]) {
    this.handlers.get(name)?.(...args)
  }
  reconnect() {
    this.reconnected?.()
  }
  close() {
    this.closed?.()
  }
}

function manualScheduler() {
  const pending: { run: () => void; ms: number }[] = []
  return {
    pending,
    schedule: (run: () => void, ms: number) => {
      const entry = { run, ms }
      pending.push(entry)
      return () => {
        const i = pending.indexOf(entry)
        if (i >= 0) pending.splice(i, 1)
      }
    },
    runNext() {
      pending.shift()?.run()
    },
  }
}

const flush = () => new Promise((resolve) => setTimeout(resolve, 0))

describe('connectLiveHub', () => {
  it('keeps trying when the page loaded before the backend was up and then shows the next unlock', async () => {
    // Reproduces 2026-11-02: page (re)loaded 12:45:34, backend listening 12:46:39, unlock 12:50:55 - the old code called
    // start() once, swallowed the error and never connected, so the server sent the popup to nobody
    const connection = new FakeConnection()
    const scheduler = manualScheduler()
    const onAchievement = vi.fn()
    const fetchEvents = vi.fn(async () => response('server-a', []))

    connectLiveHub(
      { onAchievement, onPlatinum: vi.fn(), onGameChanged: vi.fn() },
      { createConnection: () => connection, fetchEvents, storage: new MemoryStorage(), schedule: scheduler.schedule }
    )
    await flush()
    expect(connection.starts).toBe(1)
    expect(scheduler.pending).toHaveLength(1)

    // Several failed attempts while dotnet watch rebuilds; the loop never gives up
    for (let i = 0; i < 6; i++) {
      scheduler.runNext()
      await flush()
    }
    expect(connection.starts).toBe(7)
    expect(scheduler.pending).toHaveLength(1)

    connection.up = true
    scheduler.runNext()
    await flush()
    expect(scheduler.pending).toHaveLength(0)
    expect(fetchEvents).toHaveBeenCalledTimes(1)

    connection.emit('AchievementUnlocked', achievement('0003'), { eventId: 1, instanceId: 'server-a' })
    expect(onAchievement).toHaveBeenCalledTimes(1)
    expect(onAchievement.mock.calls[0]?.[0].apiName).toBe('0003')
  })

  it('catches up on an unlock that was sent while the connection was down, and only once', async () => {
    const connection = new FakeConnection()
    connection.up = true
    const storage = new MemoryStorage()
    const onAchievement = vi.fn()
    let server = response('server-a', [])
    connectLiveHub(
      { onAchievement, onPlatinum: vi.fn(), onGameChanged: vi.fn() },
      { createConnection: () => connection, fetchEvents: async () => server, storage, schedule: manualScheduler().schedule }
    )
    await flush()

    connection.emit('AchievementUnlocked', achievement('0001'), { eventId: 1, instanceId: 'server-a' })
    // Connection lost; event 2 goes to nobody; SignalR reconnects
    server = response('server-a', [event(1, '0001'), event(2, '0003')])
    connection.reconnect()
    await flush()

    expect(onAchievement.mock.calls.map((c) => c[0].apiName)).toEqual(['0001', '0003'])

    // A late live copy of the same event is not shown twice
    connection.emit('AchievementUnlocked', achievement('0003'), { eventId: 2, instanceId: 'server-a' })
    expect(onAchievement).toHaveBeenCalledTimes(2)
    expect(JSON.parse(storage.getItem(LIVE_SEEN_KEY) ?? '{}')).toEqual({ instanceId: 'server-a', lastId: 2 })
  })

  it('restarts the connection when it closes for good and stops cleanly', async () => {
    const connection = new FakeConnection()
    connection.up = true
    const scheduler = manualScheduler()
    const stop = connectLiveHub(
      { onAchievement: vi.fn(), onPlatinum: vi.fn(), onGameChanged: vi.fn() },
      { createConnection: () => connection, fetchEvents: async () => response('a', []), storage: null, schedule: scheduler.schedule }
    )
    await flush()

    connection.close()
    expect(scheduler.pending).toHaveLength(1)
    scheduler.runNext()
    await flush()
    expect(connection.starts).toBe(2)

    stop()
    connection.close()
    expect(scheduler.pending).toHaveLength(0)
    expect(connection.stopped).toBe(true)
  })

  it('reports a PinsChanged event with its app id and ignores a payload without one', async () => {
    const connection = new FakeConnection()
    connection.up = true
    const onPinsChanged = vi.fn()
    connectLiveHub(
      { onAchievement: vi.fn(), onPlatinum: vi.fn(), onGameChanged: vi.fn(), onPinsChanged },
      { createConnection: () => connection, fetchEvents: async () => response('a', []), storage: null, schedule: manualScheduler().schedule }
    )
    await flush()

    connection.emit('PinsChanged', { appId: 440 })
    connection.emit('PinsChanged', {})
    connection.emit('PinsChanged', null)
    expect(onPinsChanged).toHaveBeenCalledTimes(1)
    expect(onPinsChanged).toHaveBeenCalledWith(440)
  })

  it('never stops retrying (delays settle at 10 s)', () => {
    expect(retryDelayMs(0)).toBe(0)
    expect(retryDelayMs(3)).toBe(5000)
    expect(retryDelayMs(500)).toBe(10000)
  })
})

describe('LiveEventGate', () => {
  it('a window that was never connected does not replay older unlocks', () => {
    const gate = new LiveEventGate(new MemoryStorage())
    expect(gate.catchUp(response('a', [event(1), event(2)]))).toEqual([])
    expect(gate.accept({ eventId: 3, instanceId: 'a' })).toBe(true)
  })

  it('after a server restart the reloaded tab shows the unlocks of the new server process it missed', () => {
    const storage = new MemoryStorage()
    storage.setItem(LIVE_SEEN_KEY, JSON.stringify({ instanceId: 'old', lastId: 9 }))
    const gate = new LiveEventGate(storage)

    const missed = gate.catchUp(response('new', [event(1, '0003')]))

    expect(missed.map((e) => e.achievement?.apiName)).toEqual(['0003'])
    expect(gate.accept({ eventId: 1, instanceId: 'new' })).toBe(false)
    expect(gate.accept({ eventId: 2, instanceId: 'new' })).toBe(true)
  })

  it('a live event that arrives before the catch-up answer is not shown twice and its predecessor is not lost', () => {
    const gate = new LiveEventGate(new MemoryStorage())
    gate.catchUp(response('a', [], 4))
    // Event 5 was missed while offline, 6 arrives live right after the reconnect, then the catch-up answer comes
    expect(gate.accept({ eventId: 6, instanceId: 'a' })).toBe(true)
    expect(gate.catchUp(response('a', [event(5), event(6)])).map((e) => e.id)).toEqual([5])
  })

  it('events without meta (test popups, older servers) always show', () => {
    const gate = new LiveEventGate(null)
    expect(gate.accept(undefined)).toBe(true)
    expect(gate.accept(undefined)).toBe(true)
  })
})
