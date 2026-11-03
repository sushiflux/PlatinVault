import { useEffect, useRef, useState } from 'react'
import { CopyErrorButton } from './ui/CopyErrorButton'
import type { KeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { useQuery } from '@tanstack/react-query'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { IconBrandGithub, IconDownload, IconExternalLink, IconLicense, IconLoader2, IconRefresh, IconX } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { fetchBranding, fetchLicenseText, licenseUrl } from '../api/client'
import type { LicenseDocument } from '../api/client'
import { licenceParagraphs } from './plainLicence'
import { SimpleMarkdown } from './SimpleMarkdown'
import { IconButton } from './ui/IconButton'
import { Tooltip } from './ui/Tooltip'

// The texts of the two documents (label, hint) live in shell:licenses.<id>; the file names are the downloads
const FILE_NAMES: Record<LicenseDocument, string> = {
  license: 'LICENSE',
  notices: 'THIRD-PARTY-NOTICES.md',
}
const ORDER: LicenseDocument[] = ['license', 'notices']

/** The licence is the official plain text of the GNU AGPL v3: paragraphs, its centred title lines as titles. */
function PlainLicence({ source }: { source: string }) {
  return (
    <div className="pv-licence-plain">
      {licenceParagraphs(source).map((paragraph, index) =>
        paragraph.title ? (
          <p key={index} className="pv-licence-title">
            {paragraph.text}
          </p>
        ) : (
          <p key={index}>{paragraph.text}</p>
        ),
      )}
    </div>
  )
}

function LicenseDialog({ onClose, initial }: { onClose: () => void; initial: LicenseDocument }) {
  const { t } = useTranslation(['shell', 'common'])
  const reduce = useReducedMotion() ?? false
  const dialogRef = useRef<HTMLDivElement>(null)
  const closeRef = useRef(onClose)
  const aliveRef = useRef(false)
  const [opener] = useState(() => (document.activeElement instanceof HTMLElement ? document.activeElement : null))
  const [tab, setTab] = useState<LicenseDocument>(initial)
  const currentLabel = t(`licenses.${tab}.label`)
  const currentFileName = FILE_NAMES[tab]
  const href = licenseUrl(tab)
  // AGPL-3.0-only, section 13: everyone using PlatinVault over the network is offered the source code (URL from branding.json)
  // (same query as useBranding in FooterParts, which imports this file)
  const sourceUrl = useQuery({ queryKey: ['branding'], queryFn: fetchBranding }).data?.sourceUrl

  const { data, error, isPending, isFetching, refetch } = useQuery({
    queryKey: ['license-text', tab],
    queryFn: () => fetchLicenseText(tab),
    staleTime: 5 * 60_000,
    retry: false,
  })

  useEffect(() => {
    closeRef.current = onClose
  }, [onClose])

  // Esc closes, focus goes back to where it came from; the page behind is locked by index.css (html:has(.pv-doc))
  useEffect(() => {
    aliveRef.current = true
    const onKeyDown = (e: globalThis.KeyboardEvent) => {
      if (e.key === 'Escape') closeRef.current()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      aliveRef.current = false
      setTimeout(() => {
        if (!aliveRef.current) opener?.focus()
      }, 0)
    }
  }, [opener])

  // Keep Tab inside the dialog
  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== 'Tab') return
    const nodes = [
      ...(dialogRef.current?.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], [tabindex="0"]') ?? []),
    ].filter((node) => node.offsetParent !== null)
    const first = nodes[0]
    const last = nodes[nodes.length - 1]
    if (!first || !last) return
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault()
      last.focus()
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault()
      first.focus()
    }
  }

  return (
    <motion.div
      ref={dialogRef}
      className="pv-doc"
      role="dialog"
      aria-modal="true"
      aria-labelledby="pv-license-title"
      onKeyDown={onKeyDown}
      initial={reduce ? { opacity: 0 } : { opacity: 0, y: 18 }}
      animate={{ opacity: 1, y: 0 }}
      exit={reduce ? { opacity: 0 } : { opacity: 0, y: 12 }}
      transition={{ duration: reduce ? 0.12 : 0.32, ease: [0.22, 1, 0.36, 1] }}
    >
      <header className="pv-doc-head">
        <div className="pv-doc-title">
          <IconLicense size={20} aria-hidden="true" />
          <h2 id="pv-license-title">{t('licenses.title')}</h2>
        </div>

        <div className="pv-segmented" role="group" aria-label={t('licenses.docGroup')}>
          {ORDER.map((id) => (
            <button key={id} type="button" aria-pressed={tab === id} onClick={() => setTab(id)}>
              {t(`licenses.${id}.label`)}
            </button>
          ))}
        </div>

        <div className="pv-doc-actions">
          {sourceUrl && (
            <Tooltip label={t('licenses.source')} description={t('licenses.sourceHint', { url: sourceUrl })}>
              <a className="pv-btn" href={sourceUrl} target="_blank" rel="noreferrer" aria-label={t('licenses.sourceLabel')}>
                <IconBrandGithub size={15} aria-hidden="true" />
                <span className="max-sm:hidden">{t('licenses.source')}</span>
              </a>
            </Tooltip>
          )}
          <Tooltip label={t('licenses.download')} description={t('licenses.downloadHint', { fileName: currentFileName })}>
            <a className="pv-btn" href={href} download={currentFileName} aria-label={t('licenses.downloadLabel', { document: currentLabel })}>
              <IconDownload size={15} aria-hidden="true" />
              <span className="max-sm:hidden">{t('licenses.download')}</span>
            </a>
          </Tooltip>
          <Tooltip label={t('licenses.openTab')} description={t('licenses.openTabHint')}>
            <a className="pv-btn" href={href} target="_blank" rel="noreferrer" aria-label={t('licenses.openTabLabel', { document: currentLabel })}>
              <IconExternalLink size={15} aria-hidden="true" />
              <span className="max-sm:hidden">{t('licenses.openTab')}</span>
            </a>
          </Tooltip>
        </div>

        <IconButton label={t('licenses.close')} description="Esc" onClick={onClose} autoFocus>
          <IconX size={18} aria-hidden="true" />
        </IconButton>
      </header>

      <div className="pv-doc-body">
        {/* key: switching the document starts a fresh scroll area at the top */}
        <div key={tab} className="pv-doc-scroll" tabIndex={0} role="region" aria-label={currentLabel}>
          <div className="pv-doc-text">
            {isPending ? (
              <p className="pv-doc-state" role="status">
                <IconLoader2 size={16} className="animate-spin" aria-hidden="true" />
                <span>{t('licenses.loading', { document: currentLabel })}</span>
              </p>
            ) : error ? (
              <div className="pv-doc-state pv-doc-state--error" role="alert">
                <p>
                  {t('licenses.loadFailed', {
                    document: currentLabel,
                    reason: error instanceof Error ? error.message : t('common:noServerResponse'),
                  })}
                </p>
                <button type="button" className="pv-btn !py-1 !px-3 !text-xs" onClick={() => void refetch()} disabled={isFetching}>
                  <IconRefresh size={14} className={isFetching ? 'animate-spin' : ''} aria-hidden="true" />
                  <span>{t('licenses.retry')}</span>
                </button>
                <CopyErrorButton
                  className="ms-3"
                  report={{ message: t('licenses.loadFailed', { document: currentLabel, reason: error instanceof Error ? error.message : t('common:noServerResponse') }) }}
                />
              </div>
            ) : (
              <>
                <p className="pv-doc-hint">{t(`licenses.${tab}.hint`)}</p>
                {tab === 'license' && sourceUrl && (
                  <p className="pv-doc-hint pv-doc-source">
                    {t('licenses.sourceLine')}{' '}
                    <a href={sourceUrl} target="_blank" rel="noreferrer">
                      {sourceUrl.replace(/^https?:\/\//, '')}
                    </a>
                  </p>
                )}
                {tab === 'license' ? <PlainLicence source={data ?? ''} /> : <SimpleMarkdown source={data ?? ''} />}
              </>
            )}
          </div>
        </div>
      </div>
    </motion.div>
  )
}

/** The licence documents inside the app: a full-screen overlay with the licence (GNU AGPL v3), the third-party notices and a link to the source code. */
export function LicenseViewer({
  open,
  onClose,
  initial = 'license',
}: {
  open: boolean
  onClose: () => void
  initial?: LicenseDocument
}) {
  return createPortal(<AnimatePresence>{open && <LicenseDialog key="licenses" onClose={onClose} initial={initial} />}</AnimatePresence>, document.body)
}
