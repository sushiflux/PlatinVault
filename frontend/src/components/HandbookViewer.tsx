import { useEffect, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { IconBook2, IconDownload, IconExternalLink, IconX } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { currentLanguage, LANGUAGE_NAMES } from '../i18n'

type HandbookLang = 'de' | 'en'

// The end-user handbook as a PDF, served by the backend (GET /api/docs/handbook?lang=de|en)
function handbookHref(lang: HandbookLang): string {
  return `/api/docs/handbook?lang=${lang}`
}

// The handbook opens in the language of the app (the switch in the header of the viewer can still pick the other one)
function defaultLang(): HandbookLang {
  return currentLanguage()
}

// Browsers without a built-in PDF viewer (some tablets) report it; they get the two buttons instead of a blank frame
function canShowPdfInline(): boolean {
  return typeof navigator.pdfViewerEnabled === 'boolean' ? navigator.pdfViewerEnabled : true
}

function HandbookDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation('shell')
  const reduce = useReducedMotion() ?? false
  const dialogRef = useRef<HTMLDivElement>(null)
  const closeRef = useRef(onClose)
  const aliveRef = useRef(false)
  const [opener] = useState(() => (document.activeElement instanceof HTMLElement ? document.activeElement : null))
  const [lang, setLang] = useState<HandbookLang>(defaultLang)
  const inline = canShowPdfInline()
  const href = handbookHref(lang)

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

  // Keep Tab inside the dialog (the PDF frame brings its own focus handling)
  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== 'Tab') return
    const nodes = [...(dialogRef.current?.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], iframe') ?? [])].filter(
      (node) => node.offsetParent !== null
    )
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
      aria-labelledby="pv-doc-title"
      onKeyDown={onKeyDown}
      initial={reduce ? { opacity: 0 } : { opacity: 0, y: 18 }}
      animate={{ opacity: 1, y: 0 }}
      exit={reduce ? { opacity: 0 } : { opacity: 0, y: 12 }}
      transition={{ duration: reduce ? 0.12 : 0.32, ease: [0.22, 1, 0.36, 1] }}
    >
      <header className="pv-doc-head">
        <div className="pv-doc-title">
          <IconBook2 size={20} aria-hidden="true" />
          <h2 id="pv-doc-title">{t('handbook.title')}</h2>
        </div>

        <div className="pv-segmented" role="group" aria-label={t('handbook.languageGroup')}>
          <button type="button" lang="de" aria-pressed={lang === 'de'} onClick={() => setLang('de')}>
            {LANGUAGE_NAMES.de}
          </button>
          <button type="button" lang="en" aria-pressed={lang === 'en'} onClick={() => setLang('en')}>
            {LANGUAGE_NAMES.en}
          </button>
        </div>

        <div className="pv-doc-actions">
          <a className="pv-btn" href={href} download aria-label={t('handbook.downloadLabel')}>
            <IconDownload size={15} aria-hidden="true" />
            <span className="max-sm:hidden">{t('handbook.download')}</span>
          </a>
          <a className="pv-btn" href={href} target="_blank" rel="noreferrer" aria-label={t('handbook.openTabLabel')}>
            <IconExternalLink size={15} aria-hidden="true" />
            <span className="max-sm:hidden">{t('handbook.openTab')}</span>
          </a>
        </div>

        <button type="button" className="pv-btn pv-icon-btn" onClick={onClose} aria-label={t('handbook.close')} autoFocus>
          <IconX size={18} aria-hidden="true" />
        </button>
      </header>

      <div className="pv-doc-body">
        {inline ? (
          // The browser's own PDF viewer; no PDF library
          <iframe key={lang} title={t('handbook.frameTitle', { language: LANGUAGE_NAMES[lang] })} src={href} className="pv-doc-frame" />
        ) : (
          <div className="pv-doc-fallback">
            <IconBook2 size={36} aria-hidden="true" />
            <p>{t('handbook.noPdfViewer')}</p>
            <div className="pv-doc-actions">
              <a className="pv-btn pv-btn-primary" href={href} target="_blank" rel="noreferrer">
                <IconExternalLink size={15} aria-hidden="true" />
                <span>{t('handbook.openTab')}</span>
              </a>
              <a className="pv-btn" href={href} download>
                <IconDownload size={15} aria-hidden="true" />
                <span>{t('handbook.download')}</span>
              </a>
            </div>
          </div>
        )}
      </div>
    </motion.div>
  )
}

/** The handbook PDF inside the app: a full-screen overlay with the browser's built-in PDF viewer. */
export function HandbookViewer({ open, onClose }: { open: boolean; onClose: () => void }) {
  return createPortal(<AnimatePresence>{open && <HandbookDialog key="handbook" onClose={onClose} />}</AnimatePresence>, document.body)
}
