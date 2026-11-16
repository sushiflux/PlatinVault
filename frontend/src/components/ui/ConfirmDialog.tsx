import { createContext, useCallback, useContext, useEffect, useId, useRef, useState } from 'react'
import type { KeyboardEvent, ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import '../../styles/dialogs.css'

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

  return (
    <motion.div
      className="pv-modal-backdrop pv-modal-backdrop--top"
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
        className="pv-modal w-full max-w-lg p-6"
        initial={reduce ? { opacity: 0 } : { opacity: 0, scale: 0.96, y: 10 }}
        animate={reduce ? { opacity: 1 } : { opacity: 1, scale: 1, y: 0 }}
        exit={reduce ? { opacity: 0 } : { opacity: 0, scale: 0.97, y: 6 }}
        transition={reduce ? { duration: 0 } : { type: 'spring', stiffness: 420, damping: 32 }}
      >
        <h2 id={titleId} className="pv-dialog-title">
          {title}
        </h2>
        <div id={messageId} className="pv-dialog-text">
          {message}
        </div>
        <div className="pv-dialog-actions">
          <button ref={cancelRef} type="button" className="pv-btn" onClick={() => onClose(false)}>
            {cancelLabel}
          </button>
          <button
            ref={confirmRef}
            type="button"
            className={`pv-btn ${danger ? 'pv-btn-danger' : 'pv-btn-primary'}`}
            onClick={() => onClose(true)}
          >
            <span>{confirmLabel}</span>
          </button>
        </div>
      </motion.div>
    </motion.div>
  )
}
