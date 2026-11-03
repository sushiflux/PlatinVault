import { useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { HandbookViewer } from './HandbookViewer'
import { LicenseViewer } from './LicenseViewer'
import { resetSeenHints } from './onboarding/useScreenHints'
import { START_TOUR_EVENT } from './onboarding/useOnboardingTour'
import { IconAlertTriangle, IconBook2, IconLicense, IconRoute, IconCircleCheck, IconCircleDashed, IconCircleMinus, IconCircleX, IconLoader2, IconRefresh } from '@tabler/icons-react'
import { fetchSystemStatus } from '../api/client'
import type { SystemCheckDto } from '../api/types'
import { useFormat } from '../i18n/format'
import { SteamCandidateList } from './settings/SteamFolderSection'

type RowStatus = SystemCheckDto['status'] | 'checking' | 'unknown'

interface Row {
  key: string
  label: string
  status: RowStatus
  detail: string
  latencyMs: number | null
  // Extra content below the detail line (the places searched for Steam)
  extra?: ReactNode
}

// The words for each status are texts: settings:system.status.<status>
const STATUS_LOOK: Record<RowStatus, { icon: typeof IconCircleCheck; color: string; spin?: boolean }> = {
  ok: { icon: IconCircleCheck, color: 'var(--accent-emerald)' },
  warn: { icon: IconAlertTriangle, color: 'var(--accent-gold)' },
  error: { icon: IconCircleX, color: 'var(--accent-crimson)' },
  off: { icon: IconCircleMinus, color: 'var(--text-muted)' },
  checking: { icon: IconLoader2, color: 'var(--accent-crystal)', spin: true },
  unknown: { icon: IconCircleDashed, color: 'var(--accent-crimson)' },
}

// Everything the check covers, always listed in this order - also while checking and when the backend is down.
// The check key the server sends maps to a label text (settings:system.checks.<id>). Rows the server reports beyond
// this list (new checks) are appended with the label the server sends.
const KNOWN_CHECKS = [
  { key: 'frontend', id: 'frontend' },
  { key: 'backend', id: 'backend' },
  { key: 'database', id: 'database' },
  { key: 'data', id: 'data' },
  { key: 'steam-local', id: 'steamLocal' },
  { key: 'bridge', id: 'bridge' },
  { key: 'steam-key', id: 'steamKey' },
  { key: 'ai', id: 'ai' },
  { key: 'twitch', id: 'twitch' },
] as const

function CheckRow({ row }: { row: Row }) {
  const { t } = useTranslation('settings')
  const look = STATUS_LOOK[row.status]
  const Icon = look.icon
  return (
    <li className="flex items-start gap-3 py-2.5" style={{ borderBottom: '1px solid var(--border-subtle)' }}>
      <Icon size={20} className={`mt-0.5 shrink-0 ${look.spin ? 'animate-spin' : ''}`} style={{ color: look.color }} aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-baseline justify-between gap-x-3">
          <span className="text-sm font-bold" style={{ color: 'var(--text-primary)' }}>
            {row.label}
          </span>
          <span className="text-xs font-extrabold" style={{ color: look.color }}>
            {t(`system.status.${row.status}`)}
            {row.latencyMs != null ? ` · ${row.latencyMs} ms` : ''}
          </span>
        </div>
        {/* The detail line always has its height, so rows do not jump while the check runs */}
        <p className="min-h-[1.25rem] text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
          {row.detail}
        </p>
        {row.extra}
      </div>
    </li>
  )
}

// "System" tab: a small health check of everything PlatinVault depends on - the same on PC, NAS, Unraid or Linux server.
// The interface row is decided here (if this tab renders, the interface is running); the backend answer proves the rest.
export function SystemStatusSection() {
  const { t } = useTranslation('settings')
  const format = useFormat()
  const [handbookOpen, setHandbookOpen] = useState(false)
  const [licensesOpen, setLicensesOpen] = useState(false)
  const [hintsReset, setHintsReset] = useState(false)
  const { data, error, isFetching, refetch } = useQuery({
    queryKey: ['system-status'],
    queryFn: fetchSystemStatus,
    refetchInterval: 30_000,
    retry: false,
  })

  const reported = new Map((data?.checks ?? []).map((c) => [c.key, c]))
  const checking = isFetching
  const backendDown = Boolean(error) && !checking

  const rows: Row[] = [
    ...KNOWN_CHECKS.map(({ key, id }) => ({ key: key as string, label: t(`system.checks.${id}`) })),
    ...(data?.checks ?? []).filter((c) => !KNOWN_CHECKS.some((k) => k.key === c.key)).map((c) => ({ key: c.key, label: c.label })),
  ].map(({ key, label }) => {
    if (key === 'frontend') {
      return { key, label, status: 'ok', detail: t('system.loadedFrom', { host: window.location.host }), latencyMs: null }
    }
    if (checking) {
      return { key, label, status: 'checking', detail: '', latencyMs: null }
    }
    if (backendDown) {
      return key === 'backend'
        ? {
            key,
            label,
            status: 'error',
            detail: t('system.backendDown', { reason: error instanceof Error ? error.message : t('system.noAnswer') }),
            latencyMs: null,
          }
        : { key, label, status: 'unknown', detail: t('system.notCheckable'), latencyMs: null }
    }
    const check = reported.get(key)
    return check
      ? {
          key,
          label: KNOWN_CHECKS.some((k) => k.key === key) ? label : check.label,
          status: check.status,
          detail: check.detail,
          latencyMs: check.latencyMs,
          // Where Steam was looked for on the server's computer (folded, opened on demand)
          extra: key === 'steam-local' && data?.steamLocal ? <SteamCandidateList candidates={data.steamLocal.candidates} /> : undefined,
        }
      : { key, label, status: 'unknown', detail: t('system.notReported'), latencyMs: null }
  })

  return (
    <div className="pv-card space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <div className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
            {t('system.title')}
          </div>
          <div className="min-h-[1rem] text-xs" style={{ color: 'var(--text-muted)' }}>
            {checking
              ? t('system.checking')
              : data && !backendDown
                ? t('system.checked', { version: data.version, time: format.date(data.checkedAt, 'timeSeconds') })
                : t('system.lastFailed')}
          </div>
        </div>
        <button type="button" className="pv-btn !py-1 !px-3 !text-xs" onClick={() => void refetch()} disabled={isFetching}>
          <IconRefresh size={14} className={isFetching ? 'animate-spin' : ''} />
          <span>{t('system.recheck')}</span>
        </button>
      </div>
      <ul>
        {rows.map((row) => (
          <CheckRow key={row.key} row={row} />
        ))}
      </ul>

      {/* The handbook opens inside the app */}
      <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3" style={{ borderColor: 'var(--border-subtle)' }}>
        <div className="min-w-0">
          <div className="text-sm font-bold" style={{ color: 'var(--text-primary)' }}>
            {t('system.handbookTitle')}
          </div>
          <div className="text-xs" style={{ color: 'var(--text-muted)' }}>
            {t('system.handbookText')}
          </div>
        </div>
        <button type="button" className="pv-btn !py-1 !px-3 !text-xs" onClick={() => setHandbookOpen(true)}>
          <IconBook2 size={14} aria-hidden="true" />
          <span>{t('system.handbookOpen')}</span>
        </button>
      </div>
      <HandbookViewer open={handbookOpen} onClose={() => setHandbookOpen(false)} />

      {/* Usage licence and third-party notices open inside the app, too */}
      <div className="flex flex-wrap items-center justify-between gap-2 border-t pt-3" style={{ borderColor: 'var(--border-subtle)' }}>
        <div className="min-w-0">
          <div className="text-sm font-bold" style={{ color: 'var(--text-primary)' }}>
            {t('system.licensesTitle')}
          </div>
          <div className="text-xs" style={{ color: 'var(--text-muted)' }}>
            {t('system.licensesText')}
          </div>
        </div>
        <button type="button" className="pv-btn !py-1 !px-3 !text-xs" onClick={() => setLicensesOpen(true)}>
          <IconLicense size={14} aria-hidden="true" />
          <span>{t('system.licensesOpen')}</span>
        </button>
      </div>
      <LicenseViewer open={licensesOpen} onClose={() => setLicensesOpen(false)} />

      {/* Help: bring the page hints and the intro back */}
      <div className="space-y-2 border-t pt-3" style={{ borderColor: 'var(--border-subtle)' }}>
        <div className="text-sm font-bold" style={{ color: 'var(--text-primary)' }}>
          {t('system.helpTitle')}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            className="pv-btn !py-1 !px-3 !text-xs"
            onClick={() => {
              resetSeenHints()
              setHintsReset(true)
            }}
          >
            <IconRoute size={14} aria-hidden="true" />
            <span>{t('system.resetHints')}</span>
          </button>
          <button
            type="button"
            className="pv-btn !py-1 !px-3 !text-xs"
            onClick={() => window.dispatchEvent(new Event(START_TOUR_EVENT))}
          >
            <IconRefresh size={14} aria-hidden="true" />
            <span>{t('system.restartTour')}</span>
          </button>
        </div>
        {/* The message keeps its place: the row below never jumps */}
        <div className="min-h-[1rem] text-xs" role="status" style={{ color: 'var(--accent-emerald)' }}>
          {hintsReset ? t('system.hintsReset') : ''}
        </div>
      </div>
    </div>
  )
}
