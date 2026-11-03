import * as signalR from '@microsoft/signalr'
import type { LiveAchievementItemDto, LiveEventDto, LiveEventMeta, LiveEventsResponseDto } from '../api/types'

// Delays between connection attempts: quick at first (a backend restart takes seconds), then every 10 s - never giving up.
// SignalR's default reconnect stops after 0/2/10/30 s and its first start() is not retried at all; a tab that loaded while the
// backend was restarting stayed without live popups until it was reloaded (bug 2026-11-02).
const RETRY_DELAYS_MS = [0, 1_000, 2_000, 5_000]
const RETRY_STEADY_MS = 10_000

export function retryDelayMs(attempt: number): number {
  return RETRY_DELAYS_MS[attempt] ?? RETRY_STEADY_MS
}

// Per tab (sessionStorage): survives a reload of the tab, but a second tab keeps its own state and shows live popups itself
export const LIVE_SEEN_KEY = 'pv_live_seen_v1'

interface SeenState {
  instanceId: string
  // Every event up to this id was shown or deliberately skipped (ids of one server process have no gaps)
  lastId: number
}

/**
 * Decides which live events a window shows, so an unlock appears exactly once: live over SignalR, or through the catch-up after a
 * (re)connect (GET /api/live/events) when the window was not connected at that moment. A window that has never been connected
 * before only takes the current state - opening PlatinVault never replays older unlocks.
 */
export class LiveEventGate {
  private seen: SeenState | null
  private readonly shown = new Set<number>()
  private readonly storage: Storage | null

  constructor(storage: Storage | null) {
    this.storage = storage
    this.seen = readSeen(storage)
  }

  /** A live event; false when it was already shown (e.g. through the catch-up). Events without meta (test popups) always show. */
  accept(meta: LiveEventMeta | undefined): boolean {
    if (!meta || typeof meta.eventId !== 'number' || typeof meta.instanceId !== 'string') return true
    if (!this.seen) {
      this.update({ instanceId: meta.instanceId, lastId: meta.eventId })
      return true
    }
    if (this.seen.instanceId !== meta.instanceId) {
      // The server restarted; the catch-up shows the earlier events of the new process
      this.shown.clear()
      this.update({ instanceId: meta.instanceId, lastId: 0 })
    }
    if (meta.eventId <= this.seen.lastId || this.shown.has(meta.eventId)) return false
    this.shown.add(meta.eventId)
    this.advance()
    return true
  }

  /** The events to show after a (re)connect, oldest first. */
  catchUp(response: LiveEventsResponseDto): LiveEventDto[] {
    const seen = this.seen
    if (!seen) {
      this.update({ instanceId: response.instanceId, lastId: response.latestId })
      return []
    }
    let lastId = seen.lastId
    if (seen.instanceId !== response.instanceId) {
      this.shown.clear()
      lastId = 0
    }
    const missed = response.events
      .filter((e) => e.id > lastId && !this.shown.has(e.id))
      .sort((a, b) => a.id - b.id)
    for (const e of missed) this.shown.add(e.id)
    this.update({ instanceId: response.instanceId, lastId: Math.max(lastId, response.latestId) })
    return missed
  }

  private advance() {
    if (!this.seen) return
    let lastId = this.seen.lastId
    while (this.shown.has(lastId + 1)) lastId++
    if (lastId !== this.seen.lastId) this.update({ ...this.seen, lastId })
  }

  private update(next: SeenState) {
    this.seen = next
    try {
      this.storage?.setItem(LIVE_SEEN_KEY, JSON.stringify(next))
    } catch {
      // Storage full or blocked: the in-memory state still works for this page
    }
  }
}

function readSeen(storage: Storage | null): SeenState | null {
  try {
    const raw = storage?.getItem(LIVE_SEEN_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<SeenState>
    return typeof parsed.instanceId === 'string' && typeof parsed.lastId === 'number'
      ? { instanceId: parsed.instanceId, lastId: parsed.lastId }
      : null
  } catch {
    return null
  }
}

/** The part of a SignalR HubConnection this module uses (a fake in tests). */
export interface LiveHubConnection {
  on(name: string, handler: (...args: unknown[]) => void): void
  onreconnected(callback: () => void): void
  onclose(callback: () => void): void
  start(): Promise<void>
  stop(): Promise<void>
}

export interface LiveHubHandlers {
  onAchievement(achievement: LiveAchievementItemDto): void
  onPlatinum(gameName: string, total: number): void
  onGameChanged(): void
  /** After every successful (re)connect: refresh whatever may have changed meanwhile. */
  onConnected?(): void
}

export interface LiveHubDeps {
  createConnection?: () => LiveHubConnection
  fetchEvents: () => Promise<LiveEventsResponseDto>
  storage?: Storage | null
  schedule?: (run: () => void, ms: number) => () => void
}

function sessionStorageOrNull(): Storage | null {
  try {
    return typeof sessionStorage === 'undefined' ? null : sessionStorage
  } catch {
    return null
  }
}

export function createSignalRConnection(): LiveHubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl('/hubs/achievements')
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => retryDelayMs(context.previousRetryCount) })
    .configureLogging(signalR.LogLevel.Warning)
    .build() as unknown as LiveHubConnection
}

/**
 * Connects to the live hub and stays connected: the first start is retried until the backend answers, a lost connection is
 * re-established forever, and after every (re)connect the events sent meanwhile are caught up. Returns the function that stops it.
 */
export function connectLiveHub(handlers: LiveHubHandlers, deps: LiveHubDeps): () => void {
  const connection = (deps.createConnection ?? createSignalRConnection)()
  const gate = new LiveEventGate(deps.storage === undefined ? sessionStorageOrNull() : deps.storage)
  const schedule =
    deps.schedule ??
    ((run: () => void, ms: number) => {
      const timer = setTimeout(run, ms)
      return () => clearTimeout(timer)
    })

  let stopped = false
  let attempt = 0
  let cancelRetry: (() => void) | null = null

  const show = (event: LiveEventDto) => {
    if (event.kind === 'achievement' && event.achievement) handlers.onAchievement(event.achievement)
    else if (event.kind === 'platinum' && event.gameName) handlers.onPlatinum(event.gameName, event.total ?? 0)
  }

  const afterConnect = () => {
    handlers.onConnected?.()
    // Best effort: without the catch-up only events sent while this window was offline are lost, the live channel works
    deps
      .fetchEvents()
      .then((response) => {
        if (!stopped) gate.catchUp(response).forEach(show)
      })
      .catch(() => {})
  }

  // True while start() runs: a close event during a failed start must not schedule a second retry loop
  let starting = false

  const start = () => {
    cancelRetry = null
    if (stopped) return
    starting = true
    connection
      .start()
      .then(() => {
        starting = false
        attempt = 0
        if (stopped) {
          void connection.stop()
          return
        }
        afterConnect()
      })
      .catch(() => {
        starting = false
        if (stopped || cancelRetry) return
        cancelRetry = schedule(start, retryDelayMs(attempt++))
      })
  }

  connection.on('AchievementUnlocked', (achievement, meta) => {
    if (gate.accept(meta as LiveEventMeta | undefined)) handlers.onAchievement(achievement as LiveAchievementItemDto)
  })
  connection.on('PlatinumUnlocked', (payload, meta) => {
    const { gameName, total } = payload as { gameName: string; total: number }
    if (gate.accept(meta as LiveEventMeta | undefined)) handlers.onPlatinum(gameName, total)
  })
  connection.on('GameChanged', () => handlers.onGameChanged())
  connection.onreconnected(afterConnect)
  // Only reached when the automatic reconnect gave up or the server closed the connection: start over
  connection.onclose(() => {
    if (stopped || starting || cancelRetry) return
    cancelRetry = schedule(start, retryDelayMs(attempt++))
  })

  start()

  return () => {
    stopped = true
    cancelRetry?.()
    cancelRetry = null
    void connection.stop()
  }
}
