import { Fragment } from 'react'
import { useTranslation } from 'react-i18next'
import type { LiveGameAchievementsDto, RarityTier } from '../api/types'
import { TierIcon } from './ui/TierIcon'
import { Tooltip } from './ui/Tooltip'

// While the numbers are still loading: a quiet block of the final width instead of a dash
function Count({ text }: { text: string | null }) {
  return text ? <>{text}</> : <span className="pv-skel animate-pulse" aria-hidden="true" />
}

interface TrophyTierPillsProps {
  // Undefined while loading
  data: LiveGameAchievementsDto | undefined
  // Compact bar: each pill gets our own tooltip with the full sentence
  withTooltips?: boolean
}

// Platinum / Gold / Silver / Bronze with how many of them are unlocked - one set of pills for the live trophies summary
// and for the compact bar on the game page, so wording, colours and icons cannot drift apart.
export function TrophyTierPills({ data, withTooltips = false }: TrophyTierPillsProps) {
  const { t } = useTranslation('live')
  const tiers: { key: RarityTier; className: string; achieved: number; total: number }[] = [
    { key: 'Platinum', className: 'stat-pill crystal', achieved: data?.platinum.achieved ?? 0, total: 1 },
    { key: 'Gold', className: 'stat-pill gold', achieved: data?.gold.achieved ?? 0, total: data?.gold.total ?? 0 },
    { key: 'Silver', className: 'stat-pill', achieved: data?.silver.achieved ?? 0, total: data?.silver.total ?? 0 },
    { key: 'Bronze', className: 'stat-pill', achieved: data?.bronze.achieved ?? 0, total: data?.bronze.total ?? 0 },
  ]

  return (
    <>
      {tiers.map((tier) => {
        const name = t(`tiers.${tier.key}`)
        const pill = (
          <span className={tier.className} data-tier={tier.key}>
            <TierIcon tier={tier.key} size={14} />
            {/* The name is only dropped visually in a narrow bar (CSS): it stays the accessible name of the pill */}
            <span className="pv-tier-text">
              <span className="pv-tier-name">{name}: </span>
              <Count text={data ? `${tier.achieved}/${tier.total}` : null} />
            </span>
          </span>
        )
        return withTooltips && data ? (
          <Tooltip
            key={tier.key}
            label={t('tierTrophy', { tier: name })}
            description={t('tierPills.unlocked', { achieved: tier.achieved, total: tier.total })}
            side="bottom"
          >
            {pill}
          </Tooltip>
        ) : (
          <Fragment key={tier.key}>{pill}</Fragment>
        )
      })}
    </>
  )
}
