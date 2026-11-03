import { Trans, useTranslation } from 'react-i18next'
import { IconThumbDown, IconThumbUp } from '@tabler/icons-react'
import type { GameStatsDto } from '../../api/types'
import { ChartCard, ChartTip, Kpi, Legend, TipRow, TipSub, TipTitle, useChartTip, useStatsFormat } from './chartKit'

// Below this share the number does not fit into its block; it stays in the tooltip, legend line and table
const LABEL_MIN_SHARE = 0.16

// A8: Steam reviews as one tall segmented bar (positive vs negative). Blue/orange instead of green/red: the pair stays
// distinguishable for colour-blind readers, and every share is printed or named anyway.
export function ReviewsCard({ stats, order }: { stats: GameStatsDto; order?: number }) {
  const { t } = useTranslation('stats')
  const fmt = useStatsFormat()
  const tip = useChartTip()
  const total = stats.reviewTotal ?? 0
  if (total <= 0) return null
  const percent = stats.reviewPercentPositive ?? (stats.reviewPositive !== null ? (stats.reviewPositive / total) * 100 : null)
  const positive = stats.reviewPositive ?? (percent !== null ? Math.round((total * percent) / 100) : null)
  if (positive === null || percent === null) return null
  const negative = Math.max(0, total - positive)
  const posShare = positive / total
  const negShare = negative / total

  const block = (kind: 'pos' | 'neg') => {
    const share = kind === 'pos' ? posShare : negShare
    const count = kind === 'pos' ? positive : negative
    const label = kind === 'pos' ? t('reviews.positive') : t('reviews.negative')
    return { share, count, label }
  }
  const pos = block('pos')
  const neg = block('neg')

  const blockProps = (b: ReturnType<typeof block>) =>
    tip.bind(
      <>
        <TipTitle>{b.label}</TipTitle>
        <TipRow name={t('reviews.tipReviews')} value={fmt.int(b.count)} />
        <TipSub>{t('reviews.tipShare', { percent: fmt.pctInt(Math.round(b.share * 100)), total: fmt.int(total) })}</TipSub>
      </>
    )

  return (
    <ChartCard
      title={t('reviews.title')}
      icon={<IconThumbUp size={18} />}
      subtitle={
        <Trans t={t} i18nKey="reviews.subtitle" values={{ percent: fmt.pctInt(Math.round(percent)), total: fmt.int(total) }} components={{ strong: <strong /> }} />
      }
      actions={stats.reviewScoreDesc ? <span className="pv-badge pv-badge-a">{stats.reviewScoreDesc}</span> : undefined}
      order={order}
      className="h-full"
      table={{
        caption: t('reviews.tableCaption'),
        columns: [{ label: t('reviews.colVerdict') }, { label: t('reviews.colReviews'), numeric: true }, { label: t('reviews.colShare'), numeric: true }],
        rows: [
          [t('reviews.positive'), fmt.int(positive), fmt.pctInt(Math.round(posShare * 100))],
          [t('reviews.negative'), fmt.int(negative), fmt.pctInt(Math.round(negShare * 100))],
        ],
      }}
      footer={
        <div className="mt-auto pt-4">
          <Legend entries={[{ label: t('reviews.positive'), colorVar: '--viz-1' }, { label: t('reviews.negative'), colorVar: '--viz-2' }]} />
        </div>
      }
    >
      <div
        className="flex h-7 gap-[2px]"
        role="img"
        aria-label={t('reviews.chartLabel', { positive: fmt.int(positive), negative: fmt.int(negative), percent: fmt.pctInt(Math.round(percent)) })}
      >
        {positive > 0 && (
          <div
            className="pv-viz-mark flex items-center rounded-[4px] px-2 text-[19px] font-extrabold leading-none text-white"
            style={{ flex: positive, background: 'var(--viz-1)', minInlineSize: 6 }}
            {...blockProps(pos)}
          >
            {posShare >= LABEL_MIN_SHARE && fmt.pctInt(Math.round(posShare * 100))}
          </div>
        )}
        {negative > 0 && (
          <div
            className="pv-viz-mark flex items-center rounded-[4px] px-2 text-[19px] font-extrabold leading-none text-white"
            style={{ flex: negative, background: 'var(--viz-2)', minInlineSize: 6 }}
            {...blockProps(neg)}
          >
            {negShare >= LABEL_MIN_SHARE && fmt.pctInt(Math.round(negShare * 100))}
          </div>
        )}
      </div>
      <div className="mt-4 grid grid-cols-3 gap-4">
        <Kpi
          label={t('reviews.positive')}
          value={
            <span className="inline-flex items-center gap-1.5">
              <IconThumbUp size={16} style={{ color: 'var(--viz-ink-3)' }} aria-hidden="true" />
              {fmt.int(positive)}
            </span>
          }
        />
        <Kpi
          label={t('reviews.negative')}
          value={
            <span className="inline-flex items-center gap-1.5">
              <IconThumbDown size={16} style={{ color: 'var(--viz-ink-3)' }} aria-hidden="true" />
              {fmt.int(negative)}
            </span>
          }
        />
        <Kpi label={t('reviews.total')} value={fmt.int(total)} />
      </div>
      <ChartTip api={tip} />
    </ChartCard>
  )
}
