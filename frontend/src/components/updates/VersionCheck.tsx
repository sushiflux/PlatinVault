import { useCallback, useEffect, useId, useRef, useState } from 'react'
import type { KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { IconCloudDownload, IconLoader2, IconRefresh } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { checkForUpdates } from '../../api/client'
import { useFormat } from '../../i18n/format'
import { useBranding } from '../FooterParts'
import { ReservedText } from '../ui/ReservedText'
import { StackedLabel } from '../ui/StackedLabel'
import { Tooltip } from '../ui/Tooltip'
import { UPDATE_STATUS_KEY, useHeaderUpdateStatus } from './updateOffer'
import { RESULT_TONE_COLOR, updateResult, updateResultCandidates } from './updateResult'

const PANEL_WIDTH = 336
const EDGE = 8

interface PanelProps {
  anchor: HTMLElement
  panelId: string
  onClose: () => void
}

/** The popover of the version: version, when the last check was, the button "check now" and what it found. Opens upwards (the version sits at the foot of the page). */
function VersionPanel({ anchor, panelId, onClose }: PanelProps) {
  const { t } = useTranslation('shell')
  const { t: ts } = useTranslation('settings')
  const format = useFormat()
  const queryClient = useQueryClient()
  const reduce = useReducedMotion() ?? false
  const ref = useRef<HTMLDivElement>(null)
  const { data: branding } = useBranding()
  const status = useHeaderUpdateStatus()
  const data = status.data

  const check = useMutation({
    mutationFn: checkForUpdates,
    // The same cache entry as the header icon: a found update makes the icon appear at once
    onSuccess: (fresh) => queryClient.setQueryData(UPDATE_STATUS_KEY, fresh),
  })

  const [place] = useState(() => {
    const rect = anchor.getBoundingClientRect()
    const width = Math.min(PANEL_WIDTH, document.documentElement.clientWidth - 2 * EDGE)
    const left = Math.min(Math.max(rect.left, EDGE), document.documentElement.clientWidth - width - EDGE)
    return { left, bottom: Math.round(window.innerHeight - rect.top + 10), width, maxHeight: Math.max(160, Math.round(rect.top - 24)) }
  })

  const focusable = () => [...(ref.current?.querySelectorAll<HTMLElement>('button:not(:disabled)') ?? [])]

  useEffect(() => {
    focusable()[0]?.focus()
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node
      if (ref.current?.contains(target) || anchor.contains(target)) return
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
    // The panel has one action: Tab leaves it like Esc does
    event.preventDefault()
    onClose()
    anchor.focus()
  }

  const devBuild = data?.state === 'devBuild'
  const checking = check.isPending
  const result = updateResult(ts, { data, statusError: status.error, checkError: check.error, checking })
  const interval = data?.checkInterval ?? 'daily'
  const throttled = data?.throttled && data.nextManualCheckAt ? ts('updates.result.throttled', { time: format.date(data.nextManualCheckAt, 'timeSeconds') }) : ''
  const muted = { color: 'var(--text-secondary)' }

  return createPortal(
    <motion.div
      ref={ref}
      id={panelId}
      role="dialog"
      aria-label={t('footer.version.title')}
      data-testid="version-panel"
      className="pv-hpop"
      style={{ left: place.left, bottom: place.bottom, width: place.width, maxBlockSize: place.maxHeight }}
      onKeyDown={onKeyDown}
      initial={reduce ? { opacity: 0 } : { opacity: 0, y: 6, scale: 0.98 }}
      animate={reduce ? { opacity: 1 } : { opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, transition: { duration: 0.1 } }}
      transition={{ duration: 0.16, ease: [0.22, 1, 0.36, 1] }}
    >
      <div className="flex items-center gap-2 font-extrabold" style={{ color: 'var(--accent-crystal)' }}>
        <IconCloudDownload size={18} aria-hidden="true" />
        <span>{t('footer.version.title')}</span>
      </div>
      <div className="mt-2 font-mono font-bold" style={{ color: 'var(--text-primary)' }}>
        {branding?.version ?? data?.currentVersion ?? '…'}
      </div>
      <div className="mt-1" style={muted}>
        {data?.checkedAt ? ts('updates.checkedAt', { time: format.date(data.checkedAt, 'dateTimeSeconds') }) : ts('updates.neverChecked')}
      </div>
      <div style={muted}>{t('footer.version.auto', { interval: ts(`updates.interval.options.${interval}`) })}</div>

      <button
        type="button"
        className="pv-btn mt-3 !px-3 !py-1.5 !text-xs"
        onClick={() => check.mutate()}
        disabled={checking || !data || data.state === 'disabled' || devBuild}
      >
        {checking ? <IconLoader2 size={14} className="animate-spin" aria-hidden="true" /> : <IconRefresh size={14} aria-hidden="true" />}
        <StackedLabel active={checking ? 1 : 0} labels={[t('footer.version.check'), ts('updates.checking')]} />
      </button>

      {/* Every possible result has its room from the start, so the panel never changes its height when the answer arrives */}
      <ReservedText
        role="status"
        className="mt-2 font-semibold"
        style={{ color: RESULT_TONE_COLOR[result.tone] }}
        candidates={[...updateResultCandidates(ts), result.text]}
      >
        {result.text}
      </ReservedText>
      <ReservedText className="text-xs" style={{ color: 'var(--text-muted)' }} candidates={[ts('updates.result.throttled', { time: '00:00:00' }), t('footer.version.seeIcon')]}>
        {data?.state === 'available' ? t('footer.version.seeIcon') : throttled}
      </ReservedText>
      <p className="mt-2 border-t pt-2 text-xs" style={{ borderColor: 'var(--border-subtle)', color: 'var(--text-muted)' }}>
        {t('footer.version.settingsHint')}
      </p>
    </motion.div>,
    document.body
  )
}

/**
 * The version at the foot of a page as a button: it opens a small "Version & Updates" popover with the last check, the button to
 * check by hand and its result. The version keeps its look (the caller passes it as children); only hover/focus show that it can be pressed.
 */
export function VersionCheck({ children, onOpenChange }: { children: ReactNode; onOpenChange?: (open: boolean) => void }) {
  const { t } = useTranslation('shell')
  const [open, setOpen] = useState(false)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const panelId = useId()
  const close = useCallback(() => setOpen(false), [])

  useEffect(() => {
    onOpenChange?.(open)
  }, [open, onOpenChange])
  // Gone from the page while open: whoever listens is told that nothing is open any more
  useEffect(() => () => onOpenChange?.(false), [onOpenChange])

  return (
    <>
      <Tooltip label={t('footer.version.title')} description={t('footer.version.hint')} side="top" disabled={open}>
        <button
          ref={buttonRef}
          type="button"
          data-testid="version-check"
          className="pv-vercheck"
          aria-haspopup="dialog"
          aria-expanded={open}
          aria-controls={open ? panelId : undefined}
          onClick={() => setOpen((value) => !value)}
        >
          {children}
        </button>
      </Tooltip>
      <AnimatePresence>
        {open && buttonRef.current && <VersionPanel key="version-panel" anchor={buttonRef.current} panelId={panelId} onClose={close} />}
      </AnimatePresence>
    </>
  )
}
