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
  // Each figure gets our own tooltip with the full sentence
  withTooltips?: boolean
}

// Platinum / Gold / Silver / Bronze with how many of them are unlocked - the four tier figures of the game head: the cup and "3 / 10"
// big, the tier name under it in the tier colour. One component, so wording, colours and icons cannot drift apart.
export function TrophyTierPills({ data, withTooltips = false }: TrophyTierPillsProps) {
  const { t } = useTranslation('live')
  const tiers: { key: RarityTier; achieved: number; total: number }[] = [
    { key: 'Platinum', achieved: data?.platinum.achieved ?? 0, total: 1 },
    { key: 'Gold', achieved: data?.gold.achieved ?? 0, total: data?.gold.total ?? 0 },
    { key: 'Silver', achieved: data?.silver.achieved ?? 0, total: data?.silver.total ?? 0 },
    { key: 'Bronze', achieved: data?.bronze.achieved ?? 0, total: data?.bronze.total ?? 0 },
  ]

  return (
    <>
      {tiers.map((tier) => {
        const name = t(`tiers.${tier.key}`)
        const figure = (
          <span className="pv-fig" data-tier={tier.key}>
            <span className="pv-fig-value">
              <TierIcon tier={tier.key} size={20} />
              <span className="pv-fig-count">
                <Count text={data ? `${tier.achieved} / ${tier.total}` : null} />
              </span>
            </span>
            <span className="pv-fig-name">{name}</span>
          </span>
        )
        return withTooltips && data ? (
          <Tooltip
            key={tier.key}
            label={t('tierTrophy', { tier: name })}
            description={t('tierPills.unlocked', { achieved: tier.achieved, total: tier.total })}
            side="bottom"
            className="pv-fig-wrap"
          >
            {figure}
          </Tooltip>
        ) : (
          <span key={tier.key} className="pv-fig-wrap">
            {figure}
          </span>
        )
      })}
    </>
  )
}
