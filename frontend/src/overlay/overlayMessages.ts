import type { CardPosition, LiveAchievementItemDto, NotificationPosition } from '../api/types'
import { parseUiLanguage } from '../i18n'
import type { UiLanguage } from '../i18n'
import { normalizePopupSeconds } from '../utils/popupSeconds'

// What the Windows program sends to its overlay page (src/PlatinVault.Desktop/Ui/WebOverlay.cs, OverlayMessages):
//   { pvOverlay: { kind: 'achievement', achievement, gameName, position, seconds, language } }   (seconds = setting live.popup_seconds)
//   { pvOverlay: { kind: 'platinum', gameName, total, language } }
// and what the page answers: { pvOverlayState: 'ready' | 'busy' | 'idle', received } (the program shows its window only while
// 'busy'; "received" = how many events the page had at that moment).

export type OverlayEvent =
  | {
      kind: 'achievement'
      achievement: LiveAchievementItemDto
      gameName: string | null
      position: NotificationPosition
      // How long the popup stays, whole seconds 2..10; the program reads the setting at every unlock. Missing = 5.
      seconds: number
      language: UiLanguage | null
    }
  | { kind: 'platinum'; gameName: string; total: number; language: UiLanguage | null }

export type OverlayState = 'ready' | 'busy' | 'idle'

const CARD_POSITIONS: CardPosition[] = [
  'top-left',
  'top-center',
  'top-right',
  'center-left',
  'center',
  'center-right',
  'bottom-left',
  'bottom-center',
  'bottom-right',
]

function readPosition(value: unknown): NotificationPosition {
  if (value === 'fullscreen') return 'fullscreen'
  return typeof value === 'string' && (CARD_POSITIONS as string[]).includes(value) ? (value as CardPosition) : 'top-right'
}

function readAchievement(value: unknown): LiveAchievementItemDto | null {
  if (!value || typeof value !== 'object') return null
  const a = value as Partial<LiveAchievementItemDto>
  return typeof a.apiName === 'string' && typeof a.displayName === 'string' ? (a as LiveAchievementItemDto) : null
}

/** One message of the program as an overlay event, or null for anything else (other messages, broken shapes). */
export function readOverlayEvent(data: unknown): OverlayEvent | null {
  if (!data || typeof data !== 'object') return null
  const payload = (data as { pvOverlay?: unknown }).pvOverlay
  if (!payload || typeof payload !== 'object') return null
  const p = payload as Record<string, unknown>
  const language = parseUiLanguage(typeof p.language === 'string' ? p.language : null)
  const gameName = typeof p.gameName === 'string' && p.gameName.trim() ? p.gameName : null
  if (p.kind === 'achievement') {
    const achievement = readAchievement(p.achievement)
    if (!achievement) return null
    return { kind: 'achievement', achievement, gameName, position: readPosition(p.position), seconds: normalizePopupSeconds(p.seconds), language }
  }
  if (p.kind === 'platinum' && gameName) {
    const total = typeof p.total === 'number' && Number.isFinite(p.total) ? p.total : 0
    return { kind: 'platinum', gameName, total, language }
  }
  return null
}

/** "received" = overlay events received so far: the program hides its window on "idle" only when no newer event is on its way. */
export function overlayStateMessage(state: OverlayState, received: number) {
  return { pvOverlayState: state, received }
}
