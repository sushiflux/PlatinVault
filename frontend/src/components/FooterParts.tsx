import { useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { fetchBranding } from '../api/client'
import { LicenseViewer } from './LicenseViewer'
import { Tooltip } from './ui/Tooltip'

// The pieces of the app's footer block, shared by the dashboard footer (AppFooter) and the info card at the bottom of the game
// page's side navigation (gamenav/NavInfo.tsx), so both show the same texts, the same tooltips and the same licence viewer.
// "Made with <heart>" is MadeWith.tsx and is shared the same way.

/** The branding answer (name, company, version). One query key for the whole app, so the three call sites share one request. */
export function useBranding() {
  return useQuery({ queryKey: ['branding'], queryFn: fetchBranding })
}

/** True when this UI runs inside the Windows program (the server reads Steam on this PC, so the bridge is not needed). */
export function useIsDesktopHost(): boolean {
  return useBranding().data?.host === 'desktop'
}

// Takes the room of a short version while the answer is still on its way, so the text arriving moves nothing
const VERSION_PLACEHOLDER = 'v0000.00.00 (00.0000 β)'

/**
 * The running version in mono type, exactly as the server writes it: release `v2026.11.12 (13.557)`, beta `v2026.11.12 (13.557 β)`,
 * dev `v2026.11.12 (13.557-dev)`. It is plain text and wraps at the space instead of being cut off. The colour is inherited: the
 * caller decides how loud it is.
 */
export function AppVersion({ className = '' }: { className?: string }) {
  const { data: branding } = useBranding()
  const classes = `font-mono font-semibold ${className}`.trim()

  if (!branding?.version) {
    // Not known yet: the same line height and about the same width, invisible and hidden from screen readers
    return (
      <span className={`${classes} invisible`} aria-hidden="true">
        {VERSION_PLACEHOLDER}
      </span>
    )
  }
  return <span className={classes}>{branding.version}</span>
}

/** "© 2026 SushiFlux": the year of today, the company from the branding. */
export function AppCopyright() {
  const { t } = useTranslation('shell')
  const { data: branding } = useBranding()
  return <span>{t('footer.copyright', { year: new Date().getFullYear(), company: branding?.company ?? 'SushiFlux' })}</span>
}

interface LicensesButtonProps {
  // Classes of the button; its children are the visible content (default: the word "Lizenzen")
  className?: string
  children?: ReactNode
  // Where the tooltip opens
  side?: 'top' | 'bottom'
  onClick: () => void
}

/**
 * The "Lizenzen" button with its tooltip; the caller decides what a click does (it opens the viewer). The accessible name is
 * always the word itself, also when the caller shows an icon besides it.
 */
export function LicensesButton({ className = '', children, side = 'top', onClick }: LicensesButtonProps) {
  const { t } = useTranslation('shell')
  return (
    <Tooltip label={t('footer.licenses')} description={t('footer.licensesHint')} side={side}>
      <button type="button" aria-label={t('footer.licenses')} aria-haspopup="dialog" className={className} onClick={onClick}>
        {children ?? t('footer.licenses')}
      </button>
    </Tooltip>
  )
}

/** The "Lizenzen" button together with the viewer it opens (licence, source code link and third-party notices, in the app's own full-screen viewer). */
export function LicensesLink({ className, children, side }: Omit<LicensesButtonProps, 'onClick'>) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <LicensesButton className={className} side={side} onClick={() => setOpen(true)}>
        {children}
      </LicensesButton>
      <LicenseViewer open={open} onClose={() => setOpen(false)} />
    </>
  )
}
