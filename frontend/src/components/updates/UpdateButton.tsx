import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { CopyErrorButton } from '../ui/CopyErrorButton'
import type { KeyboardEvent as ReactKeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { useMutation } from '@tanstack/react-query'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { IconCircleArrowUp, IconDownload, IconExternalLink, IconLoader2, IconNotes, IconSettings } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { installUpdate } from '../../api/client'
import type { UpdateReleaseDto, UpdateStatusDto } from '../../api/types'
import { hasReleaseNotes, releasePageUrl } from '../../api/releaseNotes'
import { useConfirm } from '../ui/ConfirmDialog'
import { iconButtonClass } from '../ui/IconButton'
import { Tooltip } from '../ui/Tooltip'
import { ReleaseNotesViewer } from './ReleaseNotesDialog'
import { updateOffer, useHeaderUpdateStatus } from './updateOffer'

const PANEL_WIDTH = 336
const EDGE = 8

// The attention animation plays once per page load, not on every page change
let attentionPlayed = false

type Tone = 'ok' | 'muted' | 'error'
const TONE_COLOR: Record<Tone, string> = { ok: 'var(--accent-emerald)', muted: 'var(--text-secondary)', error: 'var(--accent-crimson)' }

function errorText(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

interface PanelProps {
  anchor: HTMLElement
  panelId: string
  data: UpdateStatusDto
  offer: UpdateReleaseDto
  installing: boolean
  message: { tone: Tone; text: string } | null
  onInstall: () => void
  onShowNotes: () => void
  onOpenSettings: () => void
  onClose: () => void
}

/** The popover: new version, release notes, and the action that fits the platform. Written in px like the language menu. */
function UpdatePanel({ anchor, panelId, data, offer, installing, message, onInstall, onShowNotes, onOpenSettings, onClose }: PanelProps) {
  const { t } = useTranslation('shell')
  const { t: ts } = useTranslation('settings')
  const reduce = useReducedMotion() ?? false
  const ref = useRef<HTMLDivElement>(null)
  const [place] = useState(() => {
    const rect = anchor.getBoundingClientRect()
    const width = Math.min(PANEL_WIDTH, document.documentElement.clientWidth - 2 * EDGE)
    const left = Math.min(Math.max(rect.right - width, EDGE), document.documentElement.clientWidth - width - EDGE)
    return { left, top: Math.round(rect.bottom + 10), width }
  })

  const focusable = () => [...(ref.current?.querySelectorAll<HTMLElement>('a[href], button:not(:disabled)') ?? [])]

  // Focus moves to the first action; a click outside closes (not while our own confirm dialog is on top)
  useEffect(() => {
    focusable()[0]?.focus()
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Element
      if (ref.current?.contains(target) || anchor.contains(target) || target.closest('[role="alertdialog"]')) return
      onClose()
    }
    document.addEventListener('pointerdown', onPointerDown, true)
    return () => document.removeEventListener('pointerdown', onPointerDown, true)
  }, [anchor, onClose])

  const onKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault()
      event.stopPropagation()
      onClose()
      anchor.focus()
      return
    }
    if (event.key !== 'Tab') return
    // Small focus trap: Tab cycles inside the panel
    const list = focusable()
    if (list.length === 0) return
    const first = list[0]
    const last = list[list.length - 1]
    if (!first || !last) return
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }

  const pageUrl = releasePageUrl(offer)
  const desktop = data.host === 'desktop'
  const linuxPackage = data.host === 'linuxPackage'
  const image = data.imageRepository
  // A beta or nightly is wanted but the container follows :latest (or :beta) - a plain "Update" would never deliver it, so the settings text says what to change
  const switchToBeta = data.dockerAdvice === 'useBeta' && offer.isBeta
  const switchToNightly = data.dockerAdvice === 'useNightly' && Boolean(offer.isNightly)
  const muted = { color: 'var(--text-secondary)' }
  const strong = { color: 'var(--text-primary)' }

  return createPortal(
    <motion.div
      ref={ref}
      id={panelId}
      role="dialog"
      aria-label={t('header.update.panel')}
      data-testid="update-panel"
      className="pv-hpop"
      style={{ left: place.left, top: place.top, width: place.width }}
      onKeyDown={onKeyDown}
      initial={reduce ? { opacity: 0 } : { opacity: 0, y: -6, scale: 0.98 }}
      animate={reduce ? { opacity: 1 } : { opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, transition: { duration: 0.1 } }}
      transition={{ duration: 0.16, ease: [0.22, 1, 0.36, 1] }}
    >
      <div className="flex items-center gap-2 font-extrabold" style={{ color: 'var(--accent-crystal)' }}>
        <IconCircleArrowUp size={18} aria-hidden="true" />
        <span>{offer.isNightly ? t('header.update.titleNightly') : offer.isBeta ? t('header.update.titleBeta') : t('header.update.titleRelease')}</span>
      </div>
      <div className="mt-2 flex flex-wrap items-baseline gap-x-2 gap-y-1">
        <span className="font-mono font-bold" style={strong} data-testid="update-version">
          {offer.display}
        </span>
        <span style={muted}>{t('header.update.youHave', { current: data.currentVersion })}</span>
      </div>

      {hasReleaseNotes(offer) ? (
        <button type="button" className="pv-hpop-link mt-3" onClick={onShowNotes} data-testid="update-notes">
          <IconNotes size={14} aria-hidden="true" />
          <span>{ts('updates.notes')}</span>
        </button>
      ) : (
        // No notes text came with the release: a plain external link to its page, if there is one
        pageUrl && (
          <a className="pv-hpop-link mt-3" href={pageUrl} target="_blank" rel="noopener noreferrer" data-testid="update-notes-link">
            <IconExternalLink size={14} aria-hidden="true" />
            <span>{ts('updates.notesOnGitHub')}</span>
          </a>
        )
      )}

      {desktop ? (
        <div className="mt-3 space-y-2">
          {data.canInstall ? (
            <button type="button" className="pv-btn pv-btn-primary relative !px-3 !py-1.5 !text-xs" onClick={onInstall} disabled={installing}>
              <span className={`inline-flex items-center gap-1.5 ${installing ? 'invisible' : ''}`}>
                <IconDownload size={14} aria-hidden="true" />
                <span>{ts('updates.install')}</span>
              </span>
              {installing && (
                <span className="absolute inset-0 flex items-center justify-center" aria-hidden="true">
                  <IconLoader2 size={14} className="animate-spin" />
                </span>
              )}
            </button>
          ) : (
            <p style={muted}>{t('header.update.noInstaller')}</p>
          )}
          {message && (
            <p role={message.tone === 'error' ? 'alert' : 'status'} className="font-semibold" style={{ color: TONE_COLOR[message.tone] }}>
              {message.text}
              {message.tone === 'error' && <CopyErrorButton className="ms-2 align-middle" report={{ message: message.text }} />}
            </p>
          )}
        </div>
      ) : linuxPackage ? (
        <div className="mt-3 space-y-1.5" style={muted} data-testid="update-linux-steps">
          <p>{t('header.update.linux.intro')}</p>
          {offer.linuxPackageUrl && (
            <a className="pv-hpop-link" href={offer.linuxPackageUrl} target="_blank" rel="noopener noreferrer" data-testid="update-linux-download">
              <IconDownload size={14} aria-hidden="true" />
              <span>{t('header.update.linux.download')}</span>
            </a>
          )}
          <p>
            <code>./install.sh</code>
          </p>
        </div>
      ) : (
        <div className="mt-3 space-y-1.5" style={muted} data-testid="update-container-steps">
          {switchToNightly ? (
            <p className="font-semibold" style={strong}>
              {ts('updates.docker.adviceUseNightlyNew', { version: offer.display, nightly: `${image}:nightly` })}
            </p>
          ) : switchToBeta ? (
            <p className="font-semibold" style={strong}>
              {ts('updates.docker.adviceUseBetaNew', { version: offer.display, latest: `${image}:latest`, beta: `${image}:beta` })}
            </p>
          ) : (
            <>
              <p>{t('header.update.container.intro')}</p>
              <p>
                <strong style={strong}>Unraid:</strong> {t('header.update.container.unraid')}
              </p>
              <p>
                <strong style={strong}>Docker Compose:</strong>{' '}
                <code>docker compose pull &amp;&amp; docker compose up -d</code>
              </p>
              <p>
                <strong style={strong}>{t('header.update.container.otherTitle')}:</strong> {t('header.update.container.other')}
              </p>
            </>
          )}
        </div>
      )}

      <div className="mt-3 border-t pt-2" style={{ borderColor: 'var(--border-subtle)' }}>
        <button type="button" className="pv-hpop-link" onClick={onOpenSettings}>
          <IconSettings size={14} aria-hidden="true" />
          <span>{t('header.update.allSettings')}</span>
        </button>
      </div>
    </motion.div>,
    document.body
  )
}

/**
 * "Update available" icon of the page header. Invisible unless the shared update status (same query as Settings > System > Updates)
 * reports something newer for this installation; dev builds, failed checks and "up to date" show nothing at all. It sits first in
 * the utilities group, which is right-aligned, so appearing never moves the other icons. A click opens a popover with the new
 * version, the release notes and - Windows program - the install button, otherwise how to update the container.
 */
export function UpdateButton({ onOpenSettings }: { onOpenSettings: () => void }) {
  const { t } = useTranslation('shell')
  const { t: ts } = useTranslation('settings')
  const confirm = useConfirm()
  const status = useHeaderUpdateStatus()
  const data = status.data
  const offer = updateOffer(data)
  const [open, setOpen] = useState(false)
  const [notesOpen, setNotesOpen] = useState(false)
  const [message, setMessage] = useState<{ tone: Tone; text: string } | null>(null)
  const [attention] = useState(() => !attentionPlayed)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const panelId = useId()
  const close = useCallback(() => setOpen(false), [])

  useEffect(() => {
    if (offer) attentionPlayed = true
  }, [offer])

  // Kept here (not in the popover) so a running install and its result survive closing the popover
  const install = useMutation({
    mutationFn: (tag: string) => installUpdate(tag),
    onMutate: () => setMessage({ tone: 'muted', text: ts('updates.result.installing') }),
    onSuccess: () => setMessage({ tone: 'ok', text: ts('updates.result.installStarted') }),
    onError: (error) => setMessage({ tone: 'error', text: ts('updates.result.installFailed', { reason: errorText(error) }) }),
  })

  if (!data || !offer) return null

  const onInstall = async () => {
    const ok = await confirm({
      title: ts('updates.confirm.title'),
      message: ts('updates.confirm.message', { next: offer.display, current: data.currentVersion }),
      confirmLabel: ts('updates.confirm.confirm'),
    })
    if (ok) install.mutate(offer.tag)
  }

  // The popover closes and the notes dialog takes over; closing the dialog returns the focus to the icon
  const showNotes = () => {
    setOpen(false)
    setNotesOpen(true)
  }

  const openAll = () => {
    setOpen(false)
    onOpenSettings()
  }

  return (
    <>
      <Tooltip label={t('header.update.label')} description={t('header.update.description', { version: offer.display })} disabled={open}>
        <button
          ref={buttonRef}
          type="button"
          data-testid="update-button"
          className={`${iconButtonClass()} pv-hupdate${attention ? ' pv-hupdate--attention' : ''}`}
          aria-label={t('header.update.labelVersion', { version: offer.display })}
          aria-haspopup="dialog"
          aria-expanded={open}
          aria-controls={open ? panelId : undefined}
          onClick={() => setOpen((value) => !value)}
        >
          <span className="inline-flex items-center justify-center">
            <IconCircleArrowUp size={18} />
          </span>
          <span className="pv-hupdate-dot" aria-hidden="true" />
        </button>
      </Tooltip>
      <AnimatePresence>
        {open && buttonRef.current && (
          <UpdatePanel
            key="update-panel"
            anchor={buttonRef.current}
            panelId={panelId}
            data={data}
            offer={offer}
            installing={install.isPending}
            message={message}
            onInstall={() => void onInstall()}
            onShowNotes={showNotes}
            onOpenSettings={openAll}
            onClose={close}
          />
        )}
      </AnimatePresence>
      <ReleaseNotesViewer release={notesOpen ? offer : null} onClose={() => setNotesOpen(false)} restoreFocusRef={buttonRef} />
    </>
  )
}
