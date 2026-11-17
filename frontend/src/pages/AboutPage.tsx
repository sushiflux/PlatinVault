import { useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { IconExternalLink } from '@tabler/icons-react'
import { AppVersion, LicensesButton, useBranding } from '../components/FooterParts'
import { LicenseViewer } from '../components/LicenseViewer'
import { MadeWith } from '../components/MadeWith'
import { ReleaseNotesViewer } from '../components/updates/ReleaseNotesDialog'
import { VersionCheck } from '../components/updates/VersionCheck'
import { useWhatsNew, whatsNewRelease } from '../components/updates/WhatsNewGate'
import { useDialogParam } from '../utils/useDialogParam'
import '../styles/pages.css'

/**
 * About PlatinVault (route /about, the info icon in the top bar): logo and name, then one light list of hairline
 * rows - the running version with its channel symbol (a button: "Version & Updates"), what is new in this version, the credit line,
 * the licence and the source code. No copyright line in the UI.
 */
export function AboutPage() {
  const { t } = useTranslation('shell')
  const { data: branding } = useBranding()
  // Both windows live in the URL (?dialog=licenses|whatsnew), so a reload keeps them open
  const [licensesOpen, setLicensesOpen] = useDialogParam('licenses')
  const [notesOpen, setNotesOpen] = useDialogParam('whatsnew')
  const notesButtonRef = useRef<HTMLButtonElement>(null)
  const whatsNew = useWhatsNew()
  const release = whatsNew.data?.notes ? whatsNewRelease(whatsNew.data) : null
  const name = branding?.productName ?? 'PlatinVault'
  const sourceUrl = branding?.sourceUrl ?? null

  return (
    <div className="pv-about">
      <div className="pv-about-head">
        <img src="/favicon.svg" alt="" className="pv-about-logo" width={72} height={72} />
        <div className="min-w-0">
          <h1 className="pv-page-title">{t('footer.about', { name })}</h1>
          <p className="pv-page-sub">{t('about.intro')}</p>
        </div>
      </div>

      <div className="pv-surface pv-about-list">
        <div className="pv-list-row pv-about-row">
          <span className="pv-about-k">{t('about.version')}</span>
          <span className="pv-about-v">
            <VersionCheck>
              <AppVersion />
            </VersionCheck>
          </span>
        </div>
        <div className="pv-list-row pv-about-row">
          <span className="pv-about-k">{t('about.newsLabel')}</span>
          <span className="pv-about-v">
            <button ref={notesButtonRef} type="button" className="pv-btn-text" aria-haspopup="dialog" disabled={!release} onClick={() => setNotesOpen(true)}>
              {t('footer.version.whatsNew')}
            </button>
          </span>
        </div>
        <div className="pv-list-row pv-about-row">
          <span className="pv-about-k">{t('about.madeLabel')}</span>
          <span className="pv-about-v">
            <MadeWith size={18} />
          </span>
        </div>
        <div className="pv-list-row pv-about-row">
          <span className="pv-about-k">{t('about.licenseLabel')}</span>
          <span className="pv-about-v">
            <span>{t('about.licenseLine')}</span>
            <LicensesButton className="pv-btn-text" side="top" onClick={() => setLicensesOpen(true)}>
              {t('footer.licenses')}
            </LicensesButton>
          </span>
        </div>
        {sourceUrl && (
          <div className="pv-list-row pv-about-row">
            <span className="pv-about-k">{t('about.sourceTitle')}</span>
            <span className="pv-about-v">
              <a className="pv-btn-text" href={sourceUrl} target="_blank" rel="noreferrer" aria-label={t('licenses.sourceLabel')}>
                <span>{sourceUrl.replace(/^https?:\/\//, '')}</span>
                <IconExternalLink size={17} aria-hidden="true" />
              </a>
            </span>
          </div>
        )}
      </div>

      <LicenseViewer open={licensesOpen} onClose={() => setLicensesOpen(false)} />
      <ReleaseNotesViewer release={notesOpen ? release : null} onClose={() => setNotesOpen(false)} restoreFocusRef={notesButtonRef} intro />
    </div>
  )
}
