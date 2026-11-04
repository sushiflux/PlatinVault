import type { LiveAchievementItemDto, NotificationPosition } from '../api/types'
import { soundEngine } from './audio'
import { popupSecondsToMs } from './popupSeconds'

// What the 100% moment is about: all Steam achievements unlocked, or every checklist item of the guide ticked
export interface PlatinumData {
  gameName: string
  total: number
  kind?: 'achievements' | 'guide'
}

type QueueItem =
  // position / seconds: for previews (settings), which must show at the clicked spot and for the chosen duration before it is saved,
  // and for the overlay page, which gets both from the program with every unlock; without them the global setting applies
  | { type: 'achievement'; data: LiveAchievementItemDto; position?: NotificationPosition; seconds?: number }
  | { type: 'platinum'; data: PlatinumData }

export interface QueueState {
  currentAchievement: LiveAchievementItemDto | null
  currentPosition: NotificationPosition | null
  currentPlatinum: PlatinumData | null
  // How long the current trophy popup stays (the card slides out a little before that); the chosen duration when none is showing
  visibleMs: number
  // True from the first item until the last one is gone, its pause included (the overlay window of the Windows program hides
  // itself only after that, so no exit animation is cut off)
  busy: boolean
}

type QueueListener = (state: QueueState) => void

// The platinum moment stays this long if nobody clicks it away
const PLATINUM_VISIBLE_MS = 14000

// A trophy popup (card or full screen) stays this long if nobody dismisses it, until the setting "Anzeigedauer der Trophäen-Popups"
// (live.popup_seconds, 2..10 s) is known: setPopupSeconds()
export const ACHIEVEMENT_VISIBLE_MS = 5000

// Pause after a popup is gone, before the next one starts (also leaves the exit animation time to finish)
const ACHIEVEMENT_GAP_MS = 1000

// Sequential animation queue ensuring regular trophy popups finish before the 100% Platinum celebration starts.
class AnimationQueueManager {
  private queue: QueueItem[] = []
  private isProcessing = false
  private currentAchievement: LiveAchievementItemDto | null = null
  private currentPosition: NotificationPosition | null = null
  private currentPlatinum: PlatinumData | null = null
  // The chosen duration of a trophy popup; read when a popup starts, so a change applies to the next one (also to those already waiting)
  private popupMs = ACHIEVEMENT_VISIBLE_MS
  // The duration of the popup that is showing now (kept apart from popupMs: the card's exit timer must not restart mid-popup)
  private currentVisibleMs = ACHIEVEMENT_VISIBLE_MS
  private listeners: Set<QueueListener> = new Set()
  // Raised by clearQueue: a run that started before must not touch the state afterwards (it would hide the next popup early)
  private generation = 0
  // Ends the visible phase of the current trophy popup right away (set only while that phase is running)
  private endVisiblePhase: (() => void) | null = null

  public subscribe(listener: QueueListener): () => void {
    this.listeners.add(listener)
    listener(this.state())
    return () => {
      this.listeners.delete(listener)
    }
  }

  private state(): QueueState {
    return {
      currentAchievement: this.currentAchievement,
      currentPosition: this.currentPosition,
      currentPlatinum: this.currentPlatinum,
      visibleMs: this.currentAchievement ? this.currentVisibleMs : this.popupMs,
      busy: this.isProcessing || this.queue.length > 0,
    }
  }

  private notify() {
    const state = this.state()
    for (const listener of this.listeners) listener(state)
  }

  // One item is completely done: the next one starts, or the queue reports that it is idle now
  private finishItem() {
    this.isProcessing = false
    if (this.queue.length === 0) this.notify()
    else void this.processNext()
  }

  /** Sets how long trophy popups stay (whole seconds, clamped to 2..10); the platinum moment keeps its own length. */
  public setPopupSeconds(seconds: unknown) {
    this.popupMs = popupSecondsToMs(seconds)
  }

  public enqueueAchievement(achievement: LiveAchievementItemDto, options?: { position?: NotificationPosition; seconds?: number }) {
    this.queue.push({ type: 'achievement', data: achievement, position: options?.position, seconds: options?.seconds })
    void this.processNext()
  }

  public enqueuePlatinum(gameName: string, total: number, kind: PlatinumData['kind'] = 'achievements') {
    this.queue.push({ type: 'platinum', data: { gameName, total, kind } })
    void this.processNext()
  }

  public dismissPlatinum() {
    this.currentPlatinum = null
    this.notify()
    this.finishItem()
  }

  /** Ends the trophy popup that is showing now (click or Esc on the full-screen popup); the queue goes on with the next item. No-op otherwise. */
  public dismissAchievement() {
    this.endVisiblePhase?.()
  }

  public clearQueue() {
    this.generation++
    this.endVisiblePhase = null
    this.queue = []
    this.currentAchievement = null
    this.currentPosition = null
    this.currentPlatinum = null
    this.isProcessing = false
    this.notify()
  }

  private async processNext() {
    if (this.isProcessing || this.queue.length === 0) {
      return
    }

    this.isProcessing = true
    const nextItem = this.queue.shift()!
    const generation = this.generation

    if (nextItem.type === 'achievement') {
      const visibleMs = nextItem.seconds === undefined ? this.popupMs : popupSecondsToMs(nextItem.seconds)
      this.currentVisibleMs = visibleMs
      this.currentAchievement = nextItem.data
      this.currentPosition = nextItem.position ?? null
      this.notify()

      soundEngine.playTrophyUnlock(nextItem.data.rarityTier)

      await this.delayUnlessDismissed(visibleMs)
      if (generation !== this.generation) return

      this.currentAchievement = null
      this.notify()

      await this.delay(ACHIEVEMENT_GAP_MS)
      if (generation !== this.generation) return
      this.currentPosition = null
      this.finishItem()
    } else {
      await this.delay(500)
      if (generation !== this.generation) return

      this.currentPlatinum = nextItem.data
      this.notify()

      soundEngine.playPlatinumFanfare()

      await this.delay(PLATINUM_VISIBLE_MS)

      if (generation === this.generation && this.currentPlatinum === nextItem.data) {
        this.currentPlatinum = null
        this.notify()
        this.finishItem()
      }
    }
  }

  // Like delay, but dismissAchievement() ends the wait early. Only the wait that is current can be ended.
  private delayUnlessDismissed(ms: number): Promise<void> {
    return new Promise((resolve) => {
      const finish = () => {
        clearTimeout(timer)
        if (this.endVisiblePhase === finish) this.endVisiblePhase = null
        resolve()
      }
      const timer = setTimeout(finish, ms)
      this.endVisiblePhase = finish
    })
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms))
  }
}

export const animationQueue = new AnimationQueueManager()

