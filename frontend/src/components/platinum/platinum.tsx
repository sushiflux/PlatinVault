import type { CSSProperties } from 'react'
import { TierIcon } from '../ui/TierIcon'
import { useTranslation } from 'react-i18next'
import type { GameSummaryDto } from '../../api/types'

// A game counts as platinum when its guide checklist is completely ticked.
// (The list endpoint carries no live-achievement completion, so achievements-only games cannot show up here.)
// Platinum = every Steam trophy of the game unlocked (the guide checklists do not count)
export function isPlatinumGame(game: Pick<GameSummaryDto, 'trophiesUnlocked' | 'trophiesTotal'>): boolean {
  return game.trophiesTotal > 0 && game.trophiesUnlocked >= game.trophiesTotal
}

// Platinum frame + glow for a finished game's card on the dashboard (replaces the plain border and shadow)
export const PLATINUM_CARD_FRAME: CSSProperties = {
  border: '1px solid var(--accent-crystal-border)',
  boxShadow: '0 0 0 1px var(--accent-crystal-border), var(--shadow-card), 0 16px 36px -12px rgba(56, 189, 248, 0.55)',
}

// Thin metallic bar along the top edge of a finished game's cover
export function PlatinumEdge() {
  return (
    <span
      aria-hidden="true"
      className="pointer-events-none absolute inset-x-0 top-0 z-10 h-[3px]"
      style={{ background: 'linear-gradient(90deg, #e2e8f0, #7dd3fc 28%, #fef08a 50%, #7dd3fc 72%, #e2e8f0)' }}
    />
  )
}

// "Platin" chip: same box as the other badges on the card (text-[11px], px-2.5 py-1), in platinum instead of mint green.
export function PlatinumChip() {
  const { t } = useTranslation('live')
  return (
    <span
      className="inline-flex items-center gap-1 rounded-lg px-2.5 py-1 text-[11px] font-extrabold shadow"
      style={{
        background: 'linear-gradient(135deg, #f8fafc 0%, #dbeafe 45%, #9fd3f2 100%)',
        color: '#0f172a',
        border: '1px solid rgba(255, 255, 255, 0.8)',
        boxShadow: '0 0 0 1px rgba(56, 189, 248, 0.4), 0 4px 14px -2px rgba(56, 189, 248, 0.6)',
      }}
    >
      <TierIcon tier="Platinum" size={13} inherit /> {t('tiers.Platinum')}
    </span>
  )
}
