import { createContext, useCallback, useContext, useEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchSettings, fetchTwitchLiveCounts } from '../api/client'

/** The live streams of one game: the count, and whether it stopped at its limit ("300+"). */
export interface LiveCount {
  count: number
  atLeast: boolean
}

export interface LiveCountGame {
  appId: number
  name: string
}

// The most games one request takes (the server caps it the same way)
export const LIVE_COUNT_BATCH = 120
// The longest title the server accepts (UTF-16 units, counted like the server does); one longer title makes it refuse the whole batch
export const LIVE_COUNT_NAME_MAX = 200
// Tiles that become visible are collected for this long before one request goes out (fast scrolling = one request, not one per tile) ...
export const LIVE_COUNT_DEBOUNCE_MS = 300
// ... but never longer than this after the first one of a collection (a scroll that never stops must not starve the tiles)
const MAX_WAIT_MS = 1500
// While the server is still looking games up it says so, and they are asked again after this time
export const LIVE_COUNT_RETRY_MS = 3000
// A count (or "nobody streams it") stays valid this long; a failed request is tried again after a minute
const FRESH_MS = 5 * 60_000
const FAILED_MS = 60_000
// Tiles that stay on screen are refreshed when their count has expired; checked at this pace
const REFRESH_CHECK_MS = 60_000
// A tile counts as visible up to one screen height above and below the window, so its number is there before it scrolls in
const VISIBLE_MARGIN = '100% 0px'

const NO_COUNTS: ReadonlyMap<number, LiveCount> = new Map()

// The title as it is sent: at most LIVE_COUNT_NAME_MAX units and never ending in half a surrogate pair (the server cannot read a lone
// surrogate and would refuse the whole batch as well)
export function liveCountName(name: string): string {
  if (name.length <= LIVE_COUNT_NAME_MAX) return name
  const cut = name.slice(0, LIVE_COUNT_NAME_MAX)
  return /[\uD800-\uDBFF]$/.test(cut) ? cut.slice(0, -1) : cut
}

/**
 * Collects the games whose tiles are (nearly) on screen and asks for their live-stream counts in few, small requests: at most 120 games
 * each, one request at a time, after a short debounce. Counts that were fetched stay (no blinking on scroll, filter or view change) until a
 * newer answer replaces them; a game is asked again only when its answer is older than five minutes. Games the server is still looking up
 * (`pending`) are asked again after 3 s. A failure is never shown: a tile without a count simply has no number.
 */
export class LiveCountBatcher {
  private counts = new Map<number, LiveCount>()
  private snapshot: ReadonlyMap<number, LiveCount> = new Map()
  // appId -> time until which the last answer counts (no number counts as an answer too)
  private validUntil = new Map<number, number>()
  // Games whose tile is (nearly) on screen
  private visible = new Map<number, string>()
  // Games waiting for the next request; the most recently seen are asked first
  private queue = new Map<number, string>()
  private inFlight = new Set<number>()
  private retry = new Set<number>()
  private listeners = new Set<() => void>()
  private enabled = false
  private running = false
  private flushTimer: ReturnType<typeof setTimeout> | null = null
  private firstQueuedAt: number | null = null
  private retryTimer: ReturnType<typeof setTimeout> | null = null
  private refreshTimer: ReturnType<typeof setInterval> | null = null
  private observer: IntersectionObserver | null = null
  private elements = new Map<Element, LiveCountGame>()

  readonly subscribe = (listener: () => void) => {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }

  readonly getSnapshot = () => (this.enabled ? this.snapshot : NO_COUNTS)

  /** Only with Twitch credentials and the streams switch on; off stops everything that is waiting. */
  setEnabled(enabled: boolean) {
    if (enabled === this.enabled) return
    this.enabled = enabled
    if (enabled) {
      this.refreshTimer = setInterval(() => this.visible.forEach((name, appId) => this.want({ appId, name })), REFRESH_CHECK_MS)
      this.visible.forEach((name, appId) => this.want({ appId, name }))
    } else {
      this.queue.clear()
      this.retry.clear()
      this.clearTimers()
    }
    this.emit()
  }

  /** Reports a tile: it is watched until the returned function is called. */
  readonly watch = (element: Element, reported: LiveCountGame): (() => void) => {
    const game = { appId: reported.appId, name: liveCountName(reported.name) }
    this.elements.set(element, game)
    if (typeof IntersectionObserver === 'undefined') {
      // No way to know what is on screen: every tile counts as visible
      this.markVisible(game)
    } else {
      this.observer ??= new IntersectionObserver((entries) => this.onIntersect(entries), { rootMargin: VISIBLE_MARGIN })
      this.observer.observe(element)
    }
    return () => {
      const known = this.elements.get(element)
      this.elements.delete(element)
      this.observer?.unobserve(element)
      if (known) this.markHidden(known.appId)
      if (this.elements.size === 0) {
        this.observer?.disconnect()
        this.observer = null
      }
    }
  }

  private onIntersect(entries: IntersectionObserverEntry[]) {
    for (const entry of entries) {
      const game = this.elements.get(entry.target)
      if (!game) continue
      if (entry.isIntersecting) this.markVisible(game)
      else this.markHidden(game.appId)
    }
  }

  private markVisible(game: LiveCountGame) {
    this.visible.set(game.appId, game.name)
    this.want(game)
  }

  private markHidden(appId: number) {
    this.visible.delete(appId)
    // A tile that scrolled past before the request went out is not asked for
    this.queue.delete(appId)
    this.retry.delete(appId)
  }

  private want(game: LiveCountGame) {
    if (!this.enabled || game.appId <= 0) return
    const until = this.validUntil.get(game.appId)
    if (until !== undefined && until > Date.now()) return
    if (this.inFlight.has(game.appId)) return
    // Re-inserted: moves to the end, which is the front of the line
    this.queue.delete(game.appId)
    this.queue.set(game.appId, game.name)
    this.schedule()
  }

  private schedule() {
    const now = Date.now()
    this.firstQueuedAt ??= now
    if (this.flushTimer) clearTimeout(this.flushTimer)
    const delay = Math.min(LIVE_COUNT_DEBOUNCE_MS, Math.max(0, this.firstQueuedAt + MAX_WAIT_MS - now))
    this.flushTimer = setTimeout(() => {
      this.flushTimer = null
      this.firstQueuedAt = null
      void this.run()
    }, delay)
  }

  // One request at a time; what is left after a batch of 120 follows right away
  private async run() {
    if (this.running) return
    this.running = true
    try {
      while (this.enabled && this.queue.size > 0) {
        const batch = [...this.queue].slice(-LIVE_COUNT_BATCH).map(([appId, name]) => ({ appId, name }))
        for (const game of batch) {
          this.queue.delete(game.appId)
          this.inFlight.add(game.appId)
        }
        try {
          const answer = await fetchTwitchLiveCounts(batch)
          if (this.enabled) this.apply(batch, answer)
        } catch {
          // Never shown: the tiles keep what they have, the games are tried again after a minute
          for (const game of batch) this.validUntil.set(game.appId, Date.now() + FAILED_MS)
        } finally {
          for (const game of batch) this.inFlight.delete(game.appId)
        }
      }
    } finally {
      this.running = false
    }
  }

  private apply(batch: LiveCountGame[], answer: { counts: { appId: number; count: number; atLeast: boolean }[]; pending: number[] } | null) {
    // 204 (no credentials / switch off on the server): nothing to show, and nothing to ask again soon
    const counts = answer?.counts ?? []
    const pending = new Set(answer?.pending ?? [])
    const answered = new Set<number>()
    for (const entry of counts) {
      this.counts.set(entry.appId, { count: entry.count, atLeast: entry.atLeast })
      answered.add(entry.appId)
    }
    const until = Date.now() + FRESH_MS
    for (const game of batch) {
      if (pending.has(game.appId)) {
        this.retry.add(game.appId)
        continue
      }
      this.validUntil.set(game.appId, until)
      // Asked and not in the answer: the game has no number (no Twitch category)
      if (!answered.has(game.appId)) this.counts.delete(game.appId)
    }
    if (this.retry.size > 0 && !this.retryTimer) {
      this.retryTimer = setTimeout(() => {
        this.retryTimer = null
        const again = [...this.retry]
        this.retry.clear()
        for (const appId of again) {
          const name = this.visible.get(appId)
          if (name !== undefined) this.want({ appId, name })
        }
      }, LIVE_COUNT_RETRY_MS)
    }
    this.snapshot = new Map(this.counts)
    this.emit()
  }

  private clearTimers() {
    if (this.flushTimer) clearTimeout(this.flushTimer)
    if (this.retryTimer) clearTimeout(this.retryTimer)
    if (this.refreshTimer) clearInterval(this.refreshTimer)
    this.flushTimer = this.retryTimer = this.refreshTimer = null
    this.firstQueuedAt = null
  }

  private emit() {
    this.listeners.forEach((listener) => listener())
  }
}

interface LiveCountsValue {
  counts: ReadonlyMap<number, LiveCount>
  watch: (element: Element, game: LiveCountGame) => () => void
}

const NO_WATCH = () => () => {}
const LiveCountContext = createContext<LiveCountsValue>({ counts: NO_COUNTS, watch: NO_WATCH })

/**
 * The live-stream counts of the tiles that are on screen, for the numbers on their Twitch buttons. Only with Twitch credentials and the
 * switch "Live streams" on (Settings > Twitch & IGDB): without them nothing is requested and no tile shows a number. The tiles report
 * themselves (`useLiveCountWatch`); see LiveCountBatcher for how the requests are collected.
 */
export function useTwitchLiveCounts(): LiveCountsValue {
  const { data: settings } = useQuery({ queryKey: ['settings'], queryFn: fetchSettings, staleTime: 60_000 })
  const enabled = Boolean(settings?.hasTwitchKey) && settings?.twitchStreamsEnabled !== false
  const [batcher] = useState(() => new LiveCountBatcher())
  const counts = useSyncExternalStore(batcher.subscribe, batcher.getSnapshot)
  useEffect(() => {
    batcher.setEnabled(enabled)
    return () => batcher.setEnabled(false)
  }, [batcher, enabled])
  return useMemo(() => ({ counts, watch: batcher.watch }), [counts, batcher])
}

/** Hands the counts of the page to the Twitch buttons of its tiles. */
export function LiveCountProvider({ value, children }: { value: LiveCountsValue; children: ReactNode }) {
  return <LiveCountContext.Provider value={value}>{children}</LiveCountContext.Provider>
}

/** The count of one game (undefined = unknown: no number). */
export function useLiveCount(appId: number | null | undefined): LiveCount | undefined {
  const { counts } = useContext(LiveCountContext)
  return appId ? counts.get(appId) : undefined
}

/** A ref callback for the element of a tile: while it is (nearly) on screen the game's count is fetched. */
export function useLiveCountWatch(appId: number | null | undefined, name: string): (element: Element | null) => (() => void) | undefined {
  const { watch } = useContext(LiveCountContext)
  return useCallback((element: Element | null) => (element && appId ? watch(element, { appId, name }) : undefined), [watch, appId, name])
}
