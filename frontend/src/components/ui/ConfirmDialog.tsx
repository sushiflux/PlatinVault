import { createContext, useCallback, useContext, useEffect, useId, useRef, useState } from 'react'
import type { KeyboardEvent, ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { IconAlertTriangle, IconTrash } from '@tabler/icons-react'

// Our own confirmation dialog - never window.confirm/alert/prompt (the browser's boxes ignore the app's look).
// Usage: const confirm = useConfirm(); if (!(await confirm({ ... }))) return

export interface ConfirmOptions {
  title: string
  message: ReactNode
  confirmLabel: string
  // Default: "Abbrechen" / "Cancel" (common:cancel) in the active language
  cancelLabel?: string
  // "danger" = destructive action (crimson, focus starts on the cancel button)
  tone?: 'danger' | 'default'
}

type ConfirmFn = (options: ConfirmOptions) => Promise<boolean>

const ConfirmContext = createContext<ConfirmFn | null>(null)

export function useConfirm(): ConfirmFn {
  const confirm = useContext(ConfirmContext)
  if (!confirm) throw new Error('useConfirm needs <ConfirmProvider> above it')
  return confirm
}

interface PendingConfirm {
  options: ConfirmOptions
  resolve: (ok: boolean) => void
}

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState<PendingConfirm | null>(null)
  const pendingRef = useRef<PendingConfirm | null>(null)

  const confirm = useCallback<ConfirmFn>(
    (options) =>
      new Promise<boolean>((resolve) => {
        // A second request replaces an open one; the first counts as cancelled
        pendingRef.current?.resolve(false)
        const next = { options, resolve }
        pendingRef.current = next
        setPending(next)
      }),
    []
  )

  const close = (ok: boolean) => {
    pendingRef.current?.resolve(ok)
    pendingRef.current = null
    setPending(null)
  }

  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      {createPortal(
        <AnimatePresence>{pending && <ConfirmPanel key="confirm" options={pending.options} onClose={close} />}</AnimatePresence>,
        document.body
      )}
    </ConfirmContext.Provider>
  )
}

const FOCUSABLE = 'button:not(:disabled), a[href], [tabindex]:not([tabindex="-1"])'

function ConfirmPanel({ options, onClose }: { options: ConfirmOptions; onClose: (ok: boolean) => void }) {
  const { t } = useTranslation()
  const { title, message, confirmLabel, cancelLabel = t('cancel'), tone = 'default' } = options
  const reduce = useReducedMotion() ?? false
  const panelRef = useRef<HTMLDivElement>(null)
  const cancelRef = useRef<HTMLButtonElement>(null)
  const confirmRef = useRef<HTMLButtonElement>(null)
  const titleId = useId()
  const messageId = useId()
  const danger = tone === 'danger'

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null
    // Destructive: start on the cancel button, so a hasty Enter does not delete anything
    ;(danger ? cancelRef.current : confirmRef.current)?.focus()
    return () => previouslyFocused?.focus?.()
  }, [danger])

  const handleKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Escape') {
      e.stopPropagation()
      onClose(false)
      return
    }
    if (e.key !== 'Tab' || !panelRef.current) return
    const items = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE))
    if (items.length === 0) return
    const first = items[0]!
    const last = items[items.length - 1]!
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault()
      last.focus()
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault()
      first.focus()
    }
  }

  const Icon = danger ? IconTrash : IconAlertTriangle
  const accent = danger ? 'crimson' : 'gold'

  return (
    <motion.div
      className="pv-modal-backdrop"
      style={{ zIndex: 80 }}
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: reduce ? 0 : 0.16 }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose(false)
      }}
    >
      <motion.div
        ref={panelRef}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={messageId}
        onKeyDown={handleKeyDown}
        className="pv-modal w-full max-w-md p-6"
        initial={reduce ? { opacity: 0 } : { opacity: 0, scale: 0.96, y: 10 }}
        animate={reduce ? { opacity: 1 } : { opacity: 1, scale: 1, y: 0 }}
        exit={reduce ? { opacity: 0 } : { opacity: 0, scale: 0.97, y: 6 }}
        transition={reduce ? { duration: 0 } : { type: 'spring', stiffness: 420, damping: 32 }}
      >
        <div className="flex items-start gap-4">
          <motion.div
            className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl"
            style={{
              background: `var(--accent-${accent}-bg)`,
              color: `var(--accent-${accent})`,
              border: `1px solid var(--accent-${accent}-border)`,
            }}
            initial={reduce ? false : { scale: 0.6, rotate: -8 }}
            animate={{ scale: 1, rotate: 0 }}
            transition={reduce ? { duration: 0 } : { type: 'spring', stiffness: 380, damping: 16, delay: 0.05 }}
            aria-hidden="true"
          >
            <Icon size={22} />
          </motion.div>
          <div className="min-w-0 flex-1">
            <h2 id={titleId} className="text-base font-extrabold leading-snug" style={{ color: 'var(--text-primary)' }}>
              {title}
            </h2>
            <div id={messageId} className="mt-1.5 text-sm leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
              {message}
            </div>
          </div>
        </div>
        <div className="mt-6 flex flex-wrap justify-end gap-2">
          <button ref={cancelRef} type="button" className="pv-btn" onClick={() => onClose(false)}>
            {cancelLabel}
          </button>
          <button
            ref={confirmRef}
            type="button"
            className={`pv-btn ${danger ? 'pv-btn-danger' : 'pv-btn-primary'}`}
            onClick={() => onClose(true)}
          >
            {danger && <IconTrash size={15} />}
            <span>{confirmLabel}</span>
          </button>
        </div>
      </motion.div>
    </motion.div>
  )
}
