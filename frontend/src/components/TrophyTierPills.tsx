import { useTranslation } from 'react-i18next'
import { IconTrophy } from '@tabler/icons-react'
import type { LiveGameAchievementsDto, RarityTier, TrophyKind } from '../api/types'
import { TierIcon } from './ui/TierIcon'
import { Tooltip } from './ui/Tooltip'

// While the numbers are still loading: a quiet block of the final width instead of a dash
function Count({ text }: { text: string | null }) {
  return text ? <>{text}</> : <span className="pv-skel animate-pulse" aria-hidden="true" />
}

/** The figures of the game head: the four Steam tiers, the grades of a PlayStation list, or one total for a platform without grades. */
export type TrophyFigures = 'steam' | 'grades' | 'total'

type TierData = Pick<LiveGameAchievementsDto, 'platinum' | 'gold' | 'silver' | 'bronze' | 'achievedCount' | 'totalCount'>

interface TrophyTierPillsProps {
  // Undefined while loading
  data: TierData | undefined
  // Each figure gets our own tooltip with the full sentence
  withTooltips?: boolean
  // steam (default): Platinum counts "x / 1" like a Steam game's 100 %; grades: every grade with its real number (a list without a platinum trophy
  // shows no platinum figure); total: one figure for all trophies / achievements
  variant?: TrophyFigures
  // The word of the total figure (variant "total")
  kind?: TrophyKind
  // A game added by hand whose list was not researched yet: a dash instead of "0 / 0" in every figure
  empty?: boolean
}

// Platinum / Gold / Silver / Bronze with how many of them are unlocked - the four tier figures of the game head: the cup and "3 / 10"
// big, the tier name under it in the tier colour. One component, so wording, colours and icons cannot drift apart.
export function TrophyTierPills({ data, withTooltips = false, variant = 'steam', kind = 'trophies', empty = false }: TrophyTierPillsProps) {
  const { t } = useTranslation('live')

  if (variant === 'total') {
    const text = empty ? '–' : data ? `${data.achievedCount} / ${data.totalCount}` : null
    const name = t(kind === 'achievements' ? 'tierPills.achievements' : 'tierPills.trophies')
    const figure = (
      <span className="pv-fig" data-tier="Total">
        <span className="pv-fig-value">
          <IconTrophy size={20} aria-hidden="true" className="pv-fig-total-icon" />
          <span className="pv-fig-count">
            <Count text={text} />
          </span>
        </span>
        <span className="pv-fig-name">{name}</span>
      </span>
    )
    return withTooltips && data ? (
      <Tooltip label={name} description={t('tierPills.unlocked', { achieved: data.achievedCount, total: data.totalCount })} side="bottom" className="pv-fig-wrap">
        {figure}
      </Tooltip>
    ) : (
      <span className="pv-fig-wrap">{figure}</span>
    )
  }

  const tiers: { key: RarityTier; achieved: number; total: number }[] = [
    { key: 'Platinum', achieved: data?.platinum.achieved ?? 0, total: variant === 'grades' ? (data?.platinum.total ?? 0) : 1 },
    { key: 'Gold', achieved: data?.gold.achieved ?? 0, total: data?.gold.total ?? 0 },
    { key: 'Silver', achieved: data?.silver.achieved ?? 0, total: data?.silver.total ?? 0 },
    { key: 'Bronze', achieved: data?.bronze.achieved ?? 0, total: data?.bronze.total ?? 0 },
  ]

  // A researched PlayStation list without a platinum trophy (small games have none) shows three figures
  const shown = tiers.filter((tier) => empty || !(variant === 'grades' && data && tier.key === 'Platinum' && tier.total === 0))

  return (
    <>
      {shown.map((tier) => {
        const name = t(`tiers.${tier.key}`)
        const figure = (
          <span className="pv-fig" data-tier={tier.key}>
            <span className="pv-fig-value">
              <TierIcon tier={tier.key} size={20} />
              <span className="pv-fig-count">
                <Count text={empty ? '–' : data ? `${tier.achieved} / ${tier.total}` : null} />
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
