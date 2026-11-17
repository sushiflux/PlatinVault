import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { IconAward, IconBrandSteam, IconPencil, IconUsers } from '@tabler/icons-react'
import type { Icon } from '@tabler/icons-react'
import { fetchSettings } from '../api/client'
import { useFormat } from '../i18n/format'
import { ratingSlots, type RatingKey, type RatingSlot, type RatingSource } from '../utils/ratings'
import { Tooltip } from './ui/Tooltip'

// Calm generic marks, no brand logos (only Steam has its own glyph in the icon set): the aggregator, the critics, the players
const SLOT_ICON: Record<RatingKey, Icon> = {
  steam: IconBrandSteam,
  metacritic: IconAward,
  igdbCritics: IconPencil,
  igdbPlayers: IconUsers,
}

function Slot({ slot }: { slot: RatingSlot }) {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const Mark = SLOT_ICON[slot.key]
  const name = t(`ratings.${slot.key}.name`)
  const sentence =
    slot.key === 'steam'
      ? slot.verdict
        ? t('ratings.steam.verdict', { verdict: slot.verdict, percent: fmt.percent(slot.value) })
        : t('ratings.steam.value', { percent: fmt.percent(slot.value) })
      : t(`ratings.${slot.key}.value`, { score: fmt.number(slot.value) })
  return (
    <Tooltip label={name} description={sentence} side="top" className="pv-rate-slot">
      <span className="pv-rate-in" data-rating={slot.key}>
        <Mark className="pv-rate-ic" aria-hidden="true" />
        {/* The icon and the number are for the eye; screen readers get the source and the sentence */}
        <span className="sr-only">
          {name}: {sentence}
        </span>
        <span className="pv-rate-val" aria-hidden="true">
          {fmt.number(slot.value)}
          {slot.key === 'steam' && <span className="pv-rate-pct">%</span>}
        </span>
      </span>
    </Tooltip>
  )
}

/**
 * The rating strip of a tile (between the cover and the action bar): Steam reviews, Metacritic, IGDB critics, IGDB players. Only the
 * ratings a game has are shown and they share the width; a game without any shows one calm line. The height is fixed, so the tiles of
 * a row stay aligned whatever a game has. IGDB needs Twitch credentials: without them its slots never appear. While the settings are
 * still loading the strip only holds its room.
 */
export function RatingStrip({ game }: { game: RatingSource }) {
  const { t } = useTranslation('dashboard')
  const { data: settings, isPending } = useQuery({ queryKey: ['settings'], queryFn: fetchSettings, staleTime: 60_000 })
  if (isPending) return <div className="pv-rate" aria-hidden="true" />
  const slots = ratingSlots(game, Boolean(settings?.hasTwitchKey))
  return (
    <div className="pv-rate" role="group" aria-label={t('ratings.label')} data-count={slots.length}>
      {slots.length === 0 ? (
        <span className="pv-rate-empty">{t('ratings.none')}</span>
      ) : (
        slots.map((slot) => <Slot key={slot.key} slot={slot} />)
      )}
    </div>
  )
}
