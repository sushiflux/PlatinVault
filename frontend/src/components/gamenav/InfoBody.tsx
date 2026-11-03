import { IconLicense } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { AppCopyright, AppVersion, LicensesButton, useBranding } from '../FooterParts'
import { MadeWith } from '../MadeWith'
import { VersionCheck } from '../updates/VersionCheck'
import './gamenav.css'

interface InfoBodyProps {
  // The card shows the logo above the name; the block in the full nav shows the name only
  logo?: boolean
  // "Lizenzen" was pressed: the owner opens the licence viewer
  onOpenLicenses: () => void
  // The version popover (block only) opened or closed: the nav pauses its own Esc/Tab handling meanwhile
  onVersionCheckChange?: (open: boolean) => void
}

/**
 * What the info card and the info block of the side navigation show, in one place: the name of the app (from the branding), the
 * version as a mono pill (e.g. "v2026.11.12 (13.557 β)", never cut off),
 * "Made with <heart>", the copyright, a fine rule and the licences button. The pieces are the same as the dashboard footer's
 * (FooterParts.tsx, MadeWith.tsx); the type and spacing come from .pv-info-body (gamenav.css).
 */
export function InfoBody({ logo = false, onOpenLicenses, onVersionCheckChange }: InfoBodyProps) {
  const { t } = useTranslation('shell')
  const { data: branding } = useBranding()
  return (
    <div className="pv-info-body">
      <div className="pv-info-brand">
        {logo && <img src="/favicon.svg" alt="" className="pv-info-logo" />}
        <span className="pv-info-name">{branding?.productName ?? 'PlatinVault'}</span>
      </div>
      {/* In the nav block the version opens the check popover; the floating info card (logo) keeps plain text, its own outside-click rule would close it */}
      {logo ? (
        <AppVersion className="pv-info-version" />
      ) : (
        <VersionCheck onOpenChange={onVersionCheckChange}>
          <AppVersion className="pv-info-version" />
        </VersionCheck>
      )}
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
