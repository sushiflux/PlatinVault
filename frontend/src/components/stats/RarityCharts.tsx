import { useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { IconChevronDown, IconChevronUp, IconDiamond, IconLock, IconTrophy } from '@tabler/icons-react'
import type { LiveAchievementItemDto, LiveGameAchievementsDto } from '../../api/types'
import { ChartCard, ChartTip, Legend, TipRow, TipSub, TipTitle, useChartTip, useStatsFormat } from './chartKit'
import { TierIcon } from '../ui/TierIcon'
import { Grow } from './Grow'
import { easiestOpen, missingShareCount, rarityBands } from './statsData'

// Width reserved right of the longest bar for its value label
const GUTTER = '4.4rem'

// ---- A3: rarity bands -------------------------------------------------------------------------------------------

interface BandsProps {
  items: LiveAchievementItemDto[]
  rarestUnlockedName: string | null
  rarestUnlockedPercent: number | null
  order?: number
}

// Unlocked vs open per rarity band: ordered rows, one baseline, shared scale so lengths compare across bands.
export function RarityBands({ items, rarestUnlockedName, rarestUnlockedPercent, order }: BandsProps) {
  const { t } = useTranslation('stats')
  const fmt = useStatsFormat()
  const tip = useChartTip()
  const rows = rarityBands(items)
  const maxTotal = Math.max(1, ...rows.map((r) => r.total))
  const missing = missingShareCount(items)
  const veryRare = rows.slice(0, 2)
  const veryRareTotal = veryRare.reduce((a, r) => a + r.total, 0)
  const veryRareDone = veryRare.reduce((a, r) => a + r.unlocked, 0)

  return (
    <ChartCard
      title={t('rarity.title')}
      icon={<IconDiamond size={18} />}
      subtitle={
        veryRareTotal > 0 ? (
          <Trans t={t} i18nKey="rarity.subtitleVeryRare" values={{ done: veryRareDone, total: veryRareTotal }} components={{ strong: <strong /> }} />
        ) : (
          t('rarity.subtitle')
        )
      }
      order={order}
      className="h-full"
      table={{
        caption: t('rarity.tableCaption'),
        columns: [
          { label: t('rarity.colTier') },
          { label: t('rarity.colShare') },
          { label: t('rarity.colUnlocked'), numeric: true },
          { label: t('rarity.colOpen'), numeric: true },
          { label: t('rarity.colTotal'), numeric: true },
        ],
        rows: rows.map((r) => [t(`rarity.band.${r.band.key}.label`), t(`rarity.band.${r.band.key}.range`), r.unlocked, r.open, r.total]),
      }}
      footer={
        <div className="mt-auto pt-4">
          <Legend
            entries={[
              { label: t('rarity.legendUnlocked'), colorVar: '--viz-1' },
              { label: t('rarity.legendOpen'), colorVar: '--viz-1', css: 'var(--viz-track)' },
            ]}
          />
          {missing > 0 && <p className="pv-viz-note">{t('rarity.missing', { count: missing })}</p>}
          {rarestUnlockedName && rarestUnlockedPercent !== null && (
            <p className="pv-viz-note">
              <Trans
                t={t}
                i18nKey="rarity.rarest"
                values={{ name: rarestUnlockedName, percent: fmt.pct(rarestUnlockedPercent) }}
                components={{ strong: <strong style={{ color: 'var(--viz-ink)' }} /> }}
              />
            </p>
          )}
        </div>
      }
    >
      <div
        className="grid flex-1 items-center gap-x-3"
        style={{ gridTemplateColumns: 'minmax(6.5rem, 9rem) minmax(0, 1fr)', gridAutoRows: 'minmax(2.5rem, 1fr)', maxBlockSize: '21rem' }}
      >
        {rows.map((row, i) => {
          const frac = row.total / maxTotal
          const bandWidth = `calc((100% - ${GUTTER}) * ${frac})`
          return (
            <BandRow key={row.band.key} index={i} bandWidth={bandWidth} row={row} tip={tip} />
          )
        })}
      </div>
      <ChartTip api={tip} />
    </ChartCard>
  )
}

function BandRow({
  row,
  index,
  bandWidth,
  tip,
}: {
  row: ReturnType<typeof rarityBands>[number]
  index: number
  bandWidth: string
  tip: ReturnType<typeof useChartTip>
}) {
  const { t } = useTranslation('stats')
  const label = t(`rarity.band.${row.band.key}.label`)
  const range = t(`rarity.band.${row.band.key}.range`)
  const content = (
    <>
      <TipTitle>
        {label} ({range})
      </TipTitle>
      <TipRow colorVar="--viz-1" name={t('rarity.tipUnlocked')} value={row.unlocked} />
      <TipRow name={t('rarity.tipOpen')} value={row.open} />
    </>
  )
  return (
    <>
      <div className="py-1.5">
        <div className="text-sm font-bold leading-tight" style={{ color: 'var(--viz-ink)' }}>
          {label}
        </div>
        <div className="text-xs" style={{ color: 'var(--viz-ink-3)' }}>
          {range}
        </div>
      </div>
      <div
        className="pv-viz-focusable relative flex h-full min-h-9 items-center"
        tabIndex={0}
        aria-label={t('rarity.rowLabel', { label, range, unlocked: row.unlocked, total: row.total })}
        {...tip.bind(content)}
      >
        {row.total > 0 && (
          <Grow delay={index * 0.06} className="flex gap-[2px]" style={{ width: bandWidth, minWidth: 6 }}>
            {row.unlocked > 0 && <div className="pv-viz-bar pv-viz-mark" style={{ flex: row.unlocked, minWidth: 4, borderRadius: row.open > 0 ? '0' : undefined }} />}
            {row.open > 0 && <div className="pv-viz-bar pv-viz-bar--track pv-viz-mark" style={{ flex: row.open, minWidth: 4 }} />}
          </Grow>
        )}
        <span
          className="absolute text-xs font-bold"
          style={{ left: `calc(${bandWidth} + 8px)`, color: row.total > 0 ? 'var(--viz-ink)' : 'var(--viz-ink-3)', whiteSpace: 'nowrap' }}
        >
          {row.total > 0 ? `${row.unlocked} / ${row.total}` : '0'}
        </span>
      </div>
    </>
  )
}

// ---- A4: trophy tiers -------------------------------------------------------------------------------------------

interface TierProps {
  data: LiveGameAchievementsDto
  order?: number
}

// Platinum / Gold / Silver / Bronze as four meters: one hue, the track is a quiet step of the same ramp
export function TierMeters({ data, order }: TierProps) {
  const { t } = useTranslation('stats')
  const tip = useChartTip()
  const tiers = [
    { key: 'platinum', label: t('tiers.platinum'), icon: <TierIcon tier="Platinum" size={16} />, ...data.platinum, rule: t('tiers.rulePlatinum') },
    { key: 'gold', label: t('tiers.gold'), icon: <TierIcon tier="Gold" size={16} />, ...data.gold, rule: t('tiers.ruleGold') },
    { key: 'silver', label: t('tiers.silver'), icon: <TierIcon tier="Silver" size={16} />, ...data.silver, rule: t('tiers.ruleSilver') },
    { key: 'bronze', label: t('tiers.bronze'), icon: <TierIcon tier="Bronze" size={16} />, ...data.bronze, rule: t('tiers.ruleBronze') },
  ].filter((tier) => tier.total > 0)

  return (
    <ChartCard
      title={t('tiers.title')}
      icon={<IconTrophy size={18} />}
      subtitle={t('tiers.subtitle')}
      order={order}
      className="h-full"
      table={{
        caption: t('tiers.tableCaption'),
        columns: [{ label: t('tiers.colTier') }, { label: t('tiers.colUnlocked'), numeric: true }, { label: t('tiers.colTotal'), numeric: true }],
        rows: tiers.map((tier) => [tier.label, tier.achieved, tier.total]),
      }}
    >
      <div className="flex flex-col gap-4">
        {tiers.map((tier, i) => {
          const pct = tier.total > 0 ? (tier.achieved / tier.total) * 100 : 0
          return (
            <div
              key={tier.key}
              className="pv-viz-focusable"
              tabIndex={0}
              aria-label={t('tiers.rowLabel', { tier: tier.label, unlocked: tier.achieved, total: tier.total })}
              {...tip.bind(
                <>
                  <TipTitle>{t('tiers.tipTitle', { tier: tier.label })}</TipTitle>
                  <TipRow name={t('tiers.tipUnlocked')} value={t('tiers.tipValue', { unlocked: tier.achieved, total: tier.total })} />
                  <TipSub>{tier.rule}</TipSub>
                </>
              )}
            >
              <div className="mb-1.5 flex items-center justify-between gap-3">
                <span className="inline-flex items-center gap-2 text-sm font-bold" style={{ color: 'var(--viz-ink)' }}>
                  <span style={{ color: 'var(--viz-ink-3)' }}>{tier.icon}</span>
                  {tier.label}
                </span>
                <span className="text-sm font-extrabold" style={{ color: 'var(--viz-ink)' }}>
                  {tier.achieved} <span style={{ color: 'var(--viz-ink-3)', fontWeight: 700 }}>/ {tier.total}</span>
                </span>
              </div>
              <div className="h-2.5 overflow-hidden rounded-full" style={{ background: 'var(--viz-track)' }}>
                {pct > 0 && (
                  <Grow delay={i * 0.07} className="h-full rounded-full" style={{ width: `${pct}%`, background: 'var(--viz-1)' }} />
                )}
              </div>
            </div>
          )
        })}
      </div>
      <ChartTip api={tip} />
    </ChartCard>
  )
}

// ---- A6: easiest open -------------------------------------------------------------------------------------------

const INITIAL_ROWS = 10

// Open trophies, most common first: the quickest wins. One series, one hue, value at the tip.
export function EasiestOpen({ items, order }: { items: LiveAchievementItemDto[]; order?: number }) {
  const { t } = useTranslation('stats')
  const fmt = useStatsFormat()
  const tip = useChartTip()
  const [expanded, setExpanded] = useState(false)
  const all = easiestOpen(items, fmt.locale)
  const openTotal = items.filter((a) => !a.achieved).length
  const shown = expanded ? all : all.slice(0, INITIAL_ROWS)
  const scale = Math.max(1, ...all.map((a) => a.globalPercent))

  return (
    <ChartCard
      title={t('easiest.title')}
      icon={<IconLock size={18} />}
      subtitle={
        <>
          {t('easiest.intro')}{' '}
          {all.length < openTotal ? (
            <Trans t={t} i18nKey="easiest.partial" values={{ known: all.length, open: openTotal }} components={{ strong: <strong /> }} />
          ) : (
            <Trans t={t} i18nKey="easiest.all" count={all.length} components={{ strong: <strong /> }} />
          )}
        </>
      }
      order={order}
      className="h-full"
      table={{
        caption: t('easiest.tableCaption'),
        columns: [{ label: t('easiest.colTrophy') }, { label: t('easiest.colShare'), numeric: true }, { label: t('easiest.colCondition') }],
        rows: all.map((a) => [a.displayName, fmt.pct(a.globalPercent), a.isHidden ? t('easiest.secret') : a.description]),
      }}
      footer={
        all.length > INITIAL_ROWS ? (
          <div className="mt-auto pt-4">
            <button type="button" className="pv-btn" aria-expanded={expanded} onClick={() => setExpanded(!expanded)}>
              {expanded ? <IconChevronUp size={15} /> : <IconChevronDown size={15} />}
              <span>{expanded ? t('easiest.showFewer') : t('easiest.showAll', { n: all.length })}</span>
            </button>
          </div>
        ) : undefined
      }
    >
      <ul className="flex flex-col gap-1.5">
        {shown.map((a, i) => {
          const frac = a.globalPercent / scale
          const barWidth = `calc((100% - ${GUTTER}) * ${frac})`
          return (
            <li
              key={a.apiName}
              className="pv-viz-focusable grid items-center gap-x-3"
              style={{ gridTemplateColumns: '2rem minmax(0, 1fr)' }}
              tabIndex={0}
              aria-label={t('easiest.rowLabel', { name: a.displayName, percent: fmt.pct(a.globalPercent) })}
              {...tip.bind(
                <>
                  <TipTitle>{a.displayName}</TipTitle>
                  <TipRow name={t('easiest.tipShare')} value={fmt.pct(a.globalPercent)} />
                  {a.isHidden ? <TipSub>{t('easiest.secretTip')}</TipSub> : a.description ? <TipSub>{a.description}</TipSub> : null}
                </>
              )}
            >
              <img
                src={a.iconGrayUrl || a.iconUrl}
                alt=""
                loading="lazy"
                width={32}
                height={32}
                className="row-span-2 h-8 w-8 rounded-[var(--radius-sm)] object-cover"
                style={{ border: '1px solid var(--border-subtle)' }}
              />
              <div className="min-w-0 truncate text-sm font-semibold leading-tight" style={{ color: 'var(--viz-ink)' }}>
                {a.displayName}
              </div>
              <div className="relative flex h-3.5 items-center">
                <Grow delay={Math.min(i, 12) * 0.03} className="pv-viz-bar pv-viz-mark" style={{ width: barWidth, minWidth: 4, blockSize: 8 }} />
                <span className="absolute text-xs font-bold" style={{ left: `calc(${barWidth} + 8px)`, color: 'var(--viz-ink)', whiteSpace: 'nowrap' }}>
                  {fmt.pct(a.globalPercent)}
                </span>
              </div>
            </li>
          )
        })}
      </ul>
      <ChartTip api={tip} />
    </ChartCard>
  )
}
