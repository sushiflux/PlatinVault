import type { ReactNode } from 'react'
import { Link } from 'react-router'
import i18n from '../../i18n'
import { CopyErrorButton } from '../ui/CopyErrorButton'
import { getKnownAppVersion } from '../../utils/errorReport'
import { useBranding } from '../FooterParts'
import '../../styles/status.css'

export type StatusKind = 401 | 403 | 404 | 429 | 500 | 503 | 'offline'

interface StatusPageProps {
  /** Which screen: the texts come from errors:status.<kind>. The number is also the big code (offline shows "Offline"). */
  kind: StatusKind
  /** Where the primary action leads (default: the overview). */
  homeHref?: string
  /** A "Try again" action (offline, 429, 500, 503) instead of / next to the link home. */
  onRetry?: () => void
  /** What was asked for, shown in the detail line and copied ("GET /games/unknown"). */
  request?: string
  /** Extra technical text that is copied with the report. */
  details?: string | null
  children?: ReactNode
}

/**
 * A full-page status screen: big code, a thin gold line, what happened, what happens next, a primary action and "Copy details". These screens
 * are ALWAYS in English, whatever the interface language is (project rule), polished and theme-aware. One component for 401, 403, 404, 429,
 * 500, 503 and "offline"; the 404 page of the app uses it.
 */
export function StatusPage({ kind, homeHref = '/', onRetry, request, details, children }: StatusPageProps) {
  // Keys are built from the kind (status.<kind>.title), which the typed t cannot follow
  const t = i18n.getFixedT('en', 'errors') as unknown as (key: string, options?: object) => string
  const title = t(`status.${kind}.title`)
  const text = t(`status.${kind}.text`)
  const code = kind === 'offline' ? 'Offline' : String(kind)
  const { data: branding } = useBranding()
  const version = branding?.version ?? getKnownAppVersion()
  const detailLine = [t('status.errorLine', { code }), request, version].filter(Boolean).join(' · ')
  return (
    <div className="pv-status" lang="en" data-testid="status-page" data-status={kind}>
      <div className="pv-status-in">
        <p className="pv-status-code">{code}</p>
        <div className="pv-status-bar" aria-hidden="true" />
        <h1 className="pv-page-title">{title}</h1>
        <p className="pv-status-text">{text}</p>
        {children}
        <div className="pv-status-acts">
          {onRetry && (
            <button type="button" className="pv-btn pv-btn-primary" onClick={onRetry}>
              {t('status.retry')}
            </button>
          )}
          <Link to={homeHref} className={`pv-btn ${onRetry ? '' : 'pv-btn-primary'}`.trim()}>
            {t('status.home')}
          </Link>
          <CopyErrorButton variant="text" language="en" label={t('status.copy')} report={{ title, message: text, code, details: [request, details].filter(Boolean).join('\n') || null }} />
        </div>
        <p className="pv-status-detail">{detailLine}</p>
      </div>
    </div>
  )
}
