import { useEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { IconCheck, IconCopy, IconExternalLink, IconInfoSquareRounded, IconNews } from '@tabler/icons-react'
import type { GameStatsDto } from '../../api/types'
import { copyText } from '../../utils/clipboard'
import { sizeOf } from '../../utils/dashboardSort'
import { FloatingNotice, FloatingNoticeStack } from '../ui/FloatingNotice'
import { GhostIconButton } from '../ui/GhostIconButton'
import { Reveal, useStatsFormat } from './chartKit'

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-bold" style={{ color: 'var(--viz-ink-3)' }}>
        {label}
      </dt>
      <dd className="mt-0.5 text-sm font-semibold leading-snug" style={{ color: 'var(--viz-ink)' }}>
        {children}
      </dd>
    </div>
  )
}

// The Steam App ID with a small copy button. The confirmation (check mark + "Copied") sits in a slot that is always reserved and only
// becomes visible, so nothing around it moves; a refused copy says so in a floating notice (and the number can still be selected by hand).
function AppIdValue({ appId }: { appId: number }) {
  const { t } = useTranslation('stats')
  const [copied, setCopied] = useState(false)
  const [failed, setFailed] = useState(false)
  const timer = useRef<number | undefined>(undefined)

  useEffect(() => () => window.clearTimeout(timer.current), [])

  const copy = async () => {
    const ok = await copyText(String(appId))
    window.clearTimeout(timer.current)
    setFailed(!ok)
    setCopied(ok)
    if (ok) timer.current = window.setTimeout(() => setCopied(false), 2000)
  }

  return (
    <span className="inline-flex items-center gap-1.5">
      <span className="select-all tabular-nums">{appId}</span>
      {/* Negative margin: the 24 px button must not make this line taller than the other facts */}
      <GhostIconButton label={t('facts.appIdCopy')} onClick={() => void copy()} className="-my-1 inline-flex shrink-0">
        {copied ? <IconCheck size={15} aria-hidden="true" style={{ color: 'var(--accent-emerald)' }} /> : <IconCopy size={15} aria-hidden="true" />}
      </GhostIconButton>
      <span className="relative inline-flex text-xs font-bold">
        <span className="invisible" aria-hidden="true">
          {t('facts.appIdCopied')}
        </span>
        <span role="status" className="absolute inset-0" style={{ color: 'var(--accent-emerald)' }}>
          {copied ? t('facts.appIdCopied') : ''}
        </span>
      </span>
      {/* In the body, not in the card: the card is animated (a transform would make "fixed" relative to it) */}
      {createPortal(
        <FloatingNoticeStack>
          {failed && (
            <FloatingNotice key="appid-copy" tone="error" title={t('facts.appIdCopyFailed')} message={t('facts.appIdCopyBlocked')} onClose={() => setFailed(false)} />
          )}
        </FloatingNoticeStack>,
        document.body
      )}
    </span>
  )
}

// What Steam knows about the game itself: release, studio, genres, latest update. Only what is known is shown.
export function FactsCard({ stats, order }: { stats: GameStatsDto; order?: number }) {
  const { t } = useTranslation('stats')
  const fmt = useStatsFormat()
  const publishers = stats.publishers.filter((p) => !stats.developers.includes(p))
  const update = stats.latestUpdate
  const updateTs = update?.date ? Date.parse(update.date) : Number.NaN
  const updateDate = Number.isNaN(updateTs) ? null : fmt.dateLong(updateTs)
  const facts: ReactNode[] = []

  if (stats.releaseDate) {
    facts.push(
      <Fact key="release" label={t('facts.released')}>
        {stats.releaseDate}
        {stats.comingSoon && <span style={{ color: 'var(--viz-ink-2)', fontWeight: 600 }}> {t('facts.comingSoon')}</span>}
      </Fact>
    )
  }
  if (stats.developers.length > 0) {
    facts.push(
      <Fact key="dev" label={t('facts.developer')}>
        {stats.developers.join(', ')}
      </Fact>
    )
  }
  if (publishers.length > 0) {
    facts.push(
      <Fact key="pub" label={t('facts.publisher')}>
        {publishers.join(', ')}
      </Fact>
    )
  }
  if (stats.genres.length > 0) {
    facts.push(
      <Fact key="genres" label={t('facts.genres')}>
        {stats.genres.join(', ')}
      </Fact>
    )
  }
  if (stats.achievementsTotal !== null) {
    facts.push(
      <Fact key="ach" label={t('facts.achievements')}>
        {fmt.int(stats.achievementsTotal)}
      </Fact>
    )
  }
  if (stats.metacriticScore !== null) {
    facts.push(
      <Fact key="meta" label={t('facts.metacritic')}>
        {stats.metacriticScore}
      </Fact>
    )
  }
  // The real size on disk of an installed game, else the install size estimated from Steam's data (the same choice as the cards)
  const size = sizeOf(stats)
  if (size.kind !== 'none') {
    facts.push(
      <Fact key="size" label={size.kind === 'disk' ? t('facts.sizeOnDisk') : t('facts.installSize')}>
        {size.kind === 'estimate' && <span style={{ color: 'var(--viz-ink-2)', fontWeight: 600 }}>{t('facts.approx')} </span>}
        {fmt.bytes(size.bytes)}
        {size.kind === 'estimate' && <span style={{ color: 'var(--viz-ink-2)', fontWeight: 600 }}> {t('facts.installEstimate')}</span>}
      </Fact>
    )
  }
  // No App ID (a game that is not on Steam): nothing to show, the rest closes up
  if (stats.appId > 0) {
    facts.push(
      <Fact key="appid" label={t('facts.appId')}>
        <AppIdValue appId={stats.appId} />
      </Fact>
    )
  }
  if (stats.currentPlayers !== null) {
    facts.push(
      <Fact key="players" label={t('facts.playingNow')}>
        {fmt.int(stats.currentPlayers)} <span style={{ color: 'var(--viz-ink-2)', fontWeight: 600 }}>{t('facts.playingNowLive')}</span>
      </Fact>
    )
  }
  if (update && updateDate) {
    facts.push(
      <Fact key="update" label={t('facts.latestUpdate')}>
        <a href={update.url} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1.5 hover:underline">
          <span>{updateDate}</span>
          <IconExternalLink size={13} aria-hidden="true" />
        </a>
        <div className="text-xs font-medium" style={{ color: 'var(--viz-ink-2)' }}>
          {update.title}
        </div>
      </Fact>
    )
  }

  if (facts.length === 0) return null
  return (
    <Reveal order={order} className="h-full min-w-0">
      <section className="pv-card pv-viz-card h-full" aria-label={t('facts.title')}>
        <header className="pv-viz-head">
          <h3 className="pv-viz-title">
            <IconInfoSquareRounded size={18} />
            <span>{t('facts.title')}</span>
          </h3>
        </header>
        <dl className="grid gap-x-6 gap-y-4" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 12rem), 1fr))' }}>
          {facts}
        </dl>
      </section>
    </Reveal>
  )
}

export function NewsCard({ stats, order }: { stats: GameStatsDto; order?: number }) {
  const { t } = useTranslation('stats')
  const fmt = useStatsFormat()
  if (stats.news.length === 0) return null
  return (
    <Reveal order={order} className="h-full min-w-0">
      <section className="pv-card pv-viz-card h-full" aria-label={t('news.title')}>
        <header className="pv-viz-head">
          <h3 className="pv-viz-title">
            <IconNews size={18} />
            <span>{t('news.title')}</span>
          </h3>
        </header>
        <ul className="divide-y divide-[var(--border-subtle)]">
          {stats.news.map((item) => (
            <li key={item.url} className="flex items-center justify-between gap-3 py-2.5 first:pt-0 last:pb-0">
              <a
                href={item.url}
                target="_blank"
                rel="noreferrer"
                className="inline-flex min-w-0 items-center gap-2 text-sm font-semibold hover:underline"
                style={{ color: 'var(--text-primary)' }}
              >
                {item.isUpdate && <span className="pv-badge pv-badge-ok shrink-0">{t('news.updateBadge')}</span>}
                <span className="truncate">{item.title}</span>
                <IconExternalLink size={14} className="shrink-0" aria-hidden="true" />
              </a>
              <span className="shrink-0 text-xs" style={{ color: 'var(--text-muted)' }}>
                {newsDate(item.date, fmt.dateLong)}
              </span>
            </li>
          ))}
        </ul>
      </section>
    </Reveal>
  )
}

// A news date as long date, empty when Steam sent something unreadable
function newsDate(value: string, format: (ts: number) => string): string | null {
  const ts = Date.parse(value)
  return Number.isNaN(ts) ? null : format(ts)
}
