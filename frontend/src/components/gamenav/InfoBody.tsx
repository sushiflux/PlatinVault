import { IconLicense } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { AppCopyright, AppVersion, LicensesButton, useBranding } from '../FooterParts'
import { MadeWith } from '../MadeWith'
import { VersionCheck } from '../updates/VersionCheck'
import './gamenav.css'

interface InfoBodyProps {
  // "Lizenzen" was pressed: the owner opens the licence viewer
  onOpenLicenses: () => void
  // The version popover or the "what's new" window opened or closed from inside the card: the card and the nav pause their own Esc/outside handling meanwhile
  onVersionCheckChange?: (open: boolean) => void
  onVersionNotesChange?: (open: boolean) => void
}

/**
 * What the floating info card at the foot of the side navigation shows: the logo and name of the app (from the branding), the
 * version as the interactive VersionCheck pill (e.g. "v2026.11.12 (13.557 β)", never cut off, opens "Version & Updates" incl.
 * "What's new in this version"), "Made with <heart>", the copyright, a fine rule and the licences button. The pieces are the
 * same as the dashboard footer's (FooterParts.tsx, MadeWith.tsx); the type and spacing come from .pv-info-body (gamenav.css).
 */
export function InfoBody({ onOpenLicenses, onVersionCheckChange, onVersionNotesChange }: InfoBodyProps) {
  const { t } = useTranslation('shell')
  const { data: branding } = useBranding()
  return (
    <div className="pv-info-body">
      <div className="pv-info-brand">
        <img src="/favicon.svg" alt="" className="pv-info-logo" />
        <span className="pv-info-name">{branding?.productName ?? 'PlatinVault'}</span>
      </div>
      <VersionCheck onOpenChange={onVersionCheckChange} onNotesOpenChange={onVersionNotesChange}>
        <AppVersion className="pv-info-version" />
      </VersionCheck>
      <div className="pv-info-facts">
        <MadeWith />
        <AppCopyright />
      </div>
      <div className="pv-info-rule" aria-hidden="true" />
      <LicensesButton className="pv-btn pv-info-licenses" side="top" onClick={onOpenLicenses}>
        <IconLicense size={16} aria-hidden="true" />
        <span>{t('footer.licenses')}</span>
      </LicensesButton>
    </div>
  )
}
