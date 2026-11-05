import { useEffect, useId, useMemo, useRef, useState } from 'react'
import type { CSSProperties, KeyboardEvent as ReactKeyboardEvent, RefObject } from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import {
  IconAlertTriangle,
  IconArrowsExchange,
  IconBrandGithub,
  IconCertificate,
  IconListDetails,
  IconNotes,
  IconShieldLock,
  IconSparkles,
  IconTool,
  IconTrash,
  IconX,
} from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { releasePageUrl } from '../../api/releaseNotes'
import type { UpdateReleaseDto } from '../../api/types'
import { useFormat } from '../../i18n/format'
import { renderInline } from '../SimpleMarkdown'
import { IconButton } from '../ui/IconButton'
import { hasNotesContent, itemCount, parseReleaseNotes } from './releaseNotes'
import type { NotesBlock, NotesCategory, NotesSection } from './releaseNotes'

// The release notes of one version in an own PlatinVault dialog (instead of a link to GitHub): the version, a short summary when
// the entry has one, and the notes sorted by the categories of the customer changelog, in the language of the app. The text is
// parsed (releaseNotes.ts) and rendered as React elements - the GitHub text is never injected as HTML.

interface CategoryLook {
  icon: typeof IconSparkles
  // The accent of the category: CSS custom properties of index.css (all of them exist in light and dark)
  color: string
  background: string
  border: string
}

const LOOK: Record<NotesCategory, CategoryLook> = {
  new: { icon: IconSparkles, color: 'var(--accent-emerald)', background: 'var(--accent-emerald-bg)', border: 'var(--accent-emerald-border)' },
  change: { icon: IconArrowsExchange, color: 'var(--accent-crystal)', background: 'var(--accent-crystal-bg)', border: 'var(--accent-crystal-border)' },
  fix: { icon: IconTool, color: 'var(--accent-gold)', background: 'var(--accent-gold-bg)', border: 'var(--accent-gold-border)' },
  security: { icon: IconShieldLock, color: 'var(--accent-purple)', background: 'var(--accent-purple-bg)', border: 'var(--accent-purple-border)' },
  removed: { icon: IconTrash, color: 'var(--accent-crimson)', background: 'var(--accent-crimson-bg)', border: 'var(--accent-crimson-border)' },
  deprecated: { icon: IconAlertTriangle, color: 'var(--text-secondary)', background: 'var(--bg-elevated)', border: 'var(--border-strong)' },
  other: { icon: IconListDetails, color: 'var(--text-secondary)', background: 'var(--bg-elevated)', border: 'var(--border-subtle)' },
}

const FOCUSABLE = 'button:not(:disabled), a[href], [tabindex="0"]'

function Blocks({ blocks }: { blocks: NotesBlock[] }) {
  return (
    <>
      {blocks.map((block, index) => {
        if (block.kind === 'list') {
          return (
            <ul key={index} className="pv-notes-list">
              {block.items.map((item, i) => (
                <li key={i}>{renderInline(item)}</li>
              ))}
            </ul>
          )
        }
        if (block.kind === 'code') {
          return (
            <pre key={index} className="pv-md-code">
              {block.text}
            </pre>
          )
        }
        return (
          <p key={index} className="pv-notes-p">
            {renderInline(block.text)}
          </p>
        )
      })}
    </>
  )
}

function Section({ section, index, reduce }: { section: NotesSection; index: number; reduce: boolean }) {
  const { t } = useTranslation('shell')
  const look = LOOK[section.category]
  const Icon = look.icon
  const count = itemCount(section)
  const title = section.category === 'other' ? section.title : t(`releaseNotes.categories.${section.category}`)
  return (
    <motion.section
      className="pv-notes-section"
      data-category={section.category}
      style={{ '--cat': look.color, '--cat-bg': look.background, '--cat-border': look.border } as CSSProperties}
      initial={reduce ? false : { opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      transition={reduce ? { duration: 0 } : { duration: 0.28, delay: Math.min(index, 6) * 0.05, ease: [0.22, 1, 0.36, 1] }}
    >
      <h3 className="pv-notes-section-head">
        <span className="pv-notes-tile" aria-hidden="true">
          <Icon size={17} />
        </span>
        <span className="min-w-0 flex-1 break-words">{title}</span>
        {count > 0 && <span className="pv-notes-count">{t('releaseNotes.itemCount', { count })}</span>}
      </h3>
      <div className="pv-notes-section-body">
        <Blocks blocks={section.blocks} />
      </div>
    </motion.section>
  )
}

interface DialogProps {
  release: UpdateReleaseDto
  onClose: () => void
  restoreFocusRef?: RefObject<HTMLElement | null>
  // The window that opens by itself after an update: titled "What's new in <version>" (the version moves into the title)
  intro?: boolean
}

function NotesDialog({ release, onClose, restoreFocusRef, intro = false }: DialogProps) {
  const { t } = useTranslation('shell')
  const format = useFormat()
  const reduce = useReducedMotion() ?? false
  const dialogRef = useRef<HTMLDivElement>(null)
  const closeRef = useRef<HTMLButtonElement>(null)
  const onCloseRef = useRef(onClose)
  const titleId = useId()
  const [opener] = useState(() => (document.activeElement instanceof HTMLElement ? document.activeElement : null))

  // The text comes with the update status (release.notes); the dialog makes no request of its own
  const parsed = useMemo(() => (release.notes ? parseReleaseNotes(release.notes, format.language) : null), [release.notes, format.language])

  useEffect(() => {
    onCloseRef.current = onClose
  }, [onClose])

  // The focus target is read when the dialog goes away, not when it opens (the opener can still change while it is open)
  const restoreRef = useRef<HTMLElement | null>(null)
  const aliveRef = useRef(false)
  useEffect(() => {
    restoreRef.current = opener
  }, [opener])

  // Focus starts on 'Schließen'; Esc closes from anywhere; afterwards the focus goes back to what opened the dialog
  useEffect(() => {
    aliveRef.current = true
    closeRef.current?.focus()
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation()
        onCloseRef.current()
      }
    }
    document.addEventListener('keydown', onKeyDown, true)
    return () => {
      document.removeEventListener('keydown', onKeyDown, true)
      aliveRef.current = false
      // Resolved now, when the dialog goes away: the ref points at the opener as it is by then
      const target = restoreFocusRef?.current ?? restoreRef.current
      // Not when the effect only re-ran (React strict mode in development mounts twice): then the dialog is still there
      setTimeout(() => {
        if (!aliveRef.current) target?.focus?.()
      }, 0)
    }
  }, [])

  const onKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Tab') return
    const nodes = [...(dialogRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? [])].filter((node) => node.offsetParent !== null)
    const first = nodes[0]
    const last = nodes[nodes.length - 1]
    if (!first || !last) return
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }

  const pageUrl = releasePageUrl(release)
  const published = release.publishedAt ? format.date(release.publishedAt, 'dateLong') : ''
  const hasContent = parsed ? hasNotesContent(parsed) : false

  return (
    <motion.div
      className="pv-modal-backdrop"
      style={{ zIndex: 70 }}
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: reduce ? 0 : 0.16 }}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <motion.div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        data-testid="release-notes"
        className="pv-modal pv-notes"
        onKeyDown={onKeyDown}
        initial={reduce ? { opacity: 0 } : { opacity: 0, scale: 0.96, y: 12 }}
        animate={reduce ? { opacity: 1 } : { opacity: 1, scale: 1, y: 0 }}
        exit={reduce ? { opacity: 0 } : { opacity: 0, scale: 0.98, y: 6 }}
        transition={reduce ? { duration: 0 } : { type: 'spring', stiffness: 420, damping: 34 }}
      >
        <header className="pv-notes-head">
          <span className="pv-notes-headtile" aria-hidden="true">
            <IconNotes size={22} />
          </span>
          <div className="min-w-0 flex-1">
            <h2 id={titleId} className="pv-notes-title">
              {intro ? t('releaseNotes.introTitle', { version: release.display }) : t('releaseNotes.title')}
            </h2>
            <div className="pv-notes-meta">
              {!intro && (
                <span className="font-mono font-bold" style={{ color: 'var(--text-primary)' }} data-testid="release-notes-version">
                  {release.display}
                </span>
              )}
              {release.isNightly && <span className="pv-notes-pill">{t('releaseNotes.nightlyPill')}</span>}
              {release.isBeta && <span className="pv-notes-pill">{t('releaseNotes.betaPill')}</span>}
              {published && <span>{t('releaseNotes.published', { date: published })}</span>}
              {parsed?.platforms && <span>{t('releaseNotes.platforms', { platforms: parsed.platforms })}</span>}
            </div>
          </div>
          <IconButton label={t('releaseNotes.close')} description={t('releaseNotes.closeHint')} onClick={onClose}>
            <IconX size={18} aria-hidden="true" />
          </IconButton>
        </header>

        <div className="pv-notes-body" tabIndex={0} role="region" aria-label={t('releaseNotes.titleLabel', { version: release.display })}>
          {parsed && hasContent ? (
            <div className="space-y-3.5">
              {parsed.summary.length > 0 && (
                <div className="pv-notes-summary">
                  <Blocks blocks={parsed.summary} />
                </div>
              )}
              {parsed.sections.map((section, index) => (
                <Section key={`${section.category}-${index}`} section={section} index={index} reduce={reduce} />
              ))}
            </div>
          ) : (
            <p className="pv-notes-state" role="status">
              {t('releaseNotes.empty')}
            </p>
          )}
        </div>

        <footer className="pv-notes-foot">
          <div className="flex min-w-0 flex-wrap items-center gap-x-4 gap-y-1">
            {pageUrl && (
              <a
                className="pv-notes-ghlink"
                href={pageUrl}
                target="_blank"
                rel="noopener noreferrer"
                aria-label={t('releaseNotes.githubLabel', { version: release.display })}
              >
                <IconBrandGithub size={15} aria-hidden="true" />
                <span>{t('releaseNotes.github')}</span>
              </a>
            )}
            {/* Releases from the code signing era end with a link to the policy; older ones have none */}
            {parsed?.policyUrl && (
              <a className="pv-notes-ghlink" href={parsed.policyUrl} target="_blank" rel="noopener noreferrer" aria-label={t('releaseNotes.policyLabel')}>
                <IconCertificate size={15} aria-hidden="true" />
                <span>{t('releaseNotes.policy')}</span>
              </a>
            )}
          </div>
          <button type="button" ref={closeRef} className="pv-btn pv-btn-primary" onClick={onClose}>
            {t('releaseNotes.close')}
          </button>
        </footer>
      </motion.div>
    </motion.div>
  )
}

/**
 * The release notes dialog. `release` null = closed. Rendered into the body, so it lies over popovers and the settings window alike.
 * `restoreFocusRef`: where the focus goes on closing when the opener is gone by then (the update popover closes before it opens);
 * a ref object, read when the dialog closes.
 */
export function ReleaseNotesViewer({
  release,
  onClose,
  restoreFocusRef,
  intro = false,
}: {
  release: UpdateReleaseDto | null
  onClose: () => void
  restoreFocusRef?: RefObject<HTMLElement | null>
  // The window that opens by itself after an update: titled "What's new in <version>" (the version moves into the title)
  intro?: boolean
}) {
  return createPortal(
    <AnimatePresence>{release && <NotesDialog key={release.tag} release={release} onClose={onClose} restoreFocusRef={restoreFocusRef} intro={intro} />}</AnimatePresence>,
    document.body
  )
}
