import { useEffect, useId, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { IconAlertTriangle, IconChevronDown, IconCircleCheck, IconLoader2, IconX } from '@tabler/icons-react'
import { GhostIconButton } from './GhostIconButton'

// The one floating notice of the app: a failed action, a result, a long-running job. It lies over the page (fixed, never part of
// the layout - nothing moves when it comes or goes) and is OPAQUE: the tint is a solid surface colour with the tone's tint
// layered on top, so content behind it never shines through. Light + dark contrast, computed (text on the tinted surface):
//   light  error #1e293b 12.2:1 / #334155 8.6:1   info 12.9 / 9.1   progress 12.8 / 9.0
//   dark   error #f8fafc  9.9:1 / #dbe4f0 8.1:1   info  9.4 / 7.7   progress  9.6 / 7.8
// The tone icon and the left bar are >= 4.8:1 on the surface in both themes.

export type NoticeTone = 'error' | 'info' | 'progress'

const TONES: Record<NoticeTone, { accent: 'crimson' | 'emerald' | 'crystal'; Icon: typeof IconAlertTriangle; role: 'alert' | 'status' }> = {
  error: { accent: 'crimson', Icon: IconAlertTriangle, role: 'alert' },
  info: { accent: 'emerald', Icon: IconCircleCheck, role: 'status' },
  progress: { accent: 'crystal', Icon: IconLoader2, role: 'status' },
}

export interface NoticeAction {
  label: string
  onClick: () => void
}

interface FloatingNoticeProps {
  tone: NoticeTone
  // What happened, short and bold ("Guide konnte nicht vervollständigt werden")
  title: string
  // Why / the details in plain words; may be long (the body scrolls - an error text is never cut off). Text, or a small block such as the live
  // progress of an AI action
  message?: ReactNode
  // The provider's raw text: hidden behind a "Technical details" toggle
  details?: string | null
  // Buttons below the text, e.g. "AI settings"
  actions?: NoticeAction[]
  // Closing: X button and Esc. Leave it out for a notice that ends by itself (progress).
  onClose?: () => void
}

/**
 * Put notices inside <FloatingNoticeStack> (one or several, newest last). Each one is a direct, keyed child, so it can fade out:
 *   <FloatingNoticeStack>{error && <FloatingNotice key="error" tone="error" ... />}</FloatingNoticeStack>
 */
export function FloatingNoticeStack({ children, raised = false }: { children: ReactNode; raised?: boolean }) {
  // raised: a second, independent stack (the Play notices of the whole app) sits above the page's own one instead of on top of it.
  // pv-notice-stack lets index.css move both stacks to the top edge while a dialog is open: a dialog keeps its action buttons at the
  // bottom, so a notice at the bottom edge would lie over them.
  return (
    <div
      className={`pv-notice-stack ${raised ? 'bottom-24' : 'bottom-5'} no-print pointer-events-none fixed inset-x-0 z-[60] flex flex-col items-center gap-2.5 px-3`}
    >
      <AnimatePresence initial={false}>{children}</AnimatePresence>
    </div>
  )
}

export function FloatingNotice({ tone, title, message, details, actions, onClose }: FloatingNoticeProps) {
  const { t } = useTranslation(['game', 'common'])
  const reduce = useReducedMotion() ?? false
  const rootRef = useRef<HTMLDivElement>(null)
  const detailsId = useId()
  const [showDetails, setShowDetails] = useState(false)
  const { accent, Icon, role } = TONES[tone]

  // Esc closes the notice - unless something modal is open (that one gets the key first), or the key was already used
  useEffect(() => {
    if (!onClose) return
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape' || event.defaultPrevented) return
      const inside = rootRef.current?.contains(event.target as Node) ?? false
      if (!inside && document.querySelector('.pv-modal-backdrop, [role="dialog"], [role="alertdialog"]')) return
      event.preventDefault()
      onClose()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  return (
    <motion.div
      ref={rootRef}
      role={role}
      aria-live={role === 'alert' ? 'assertive' : 'polite'}
      className="pointer-events-auto relative w-full max-w-[40rem] overflow-hidden"
      style={{
        // Solid surface first, the tone's tint on top: opaque in both themes
        backgroundColor: 'var(--bg-surface)',
        backgroundImage: `linear-gradient(var(--accent-${accent}-bg), var(--accent-${accent}-bg))`,
        color: 'var(--text-primary)',
        border: `1px solid var(--accent-${accent}-border)`,
        borderInlineStart: `4px solid var(--accent-${accent})`,
        borderRadius: 'var(--radius-md)',
        boxShadow: '0 18px 44px -10px rgba(8, 13, 23, 0.45), 0 4px 14px rgba(8, 13, 23, 0.22)',
      }}
      initial={reduce ? { opacity: 0 } : { opacity: 0, y: 18, scale: 0.98 }}
      animate={reduce ? { opacity: 1 } : { opacity: 1, y: 0, scale: 1 }}
      exit={reduce ? { opacity: 0 } : { opacity: 0, y: 10, scale: 0.98 }}
      transition={reduce ? { duration: 0.12 } : { type: 'spring', stiffness: 460, damping: 36, mass: 0.8 }}
    >
      <div className="flex items-start gap-3 py-3 ps-3.5" style={{ paddingInlineEnd: onClose ? '2.75rem' : '0.875rem' }}>
        <Icon
          size={20}
          aria-hidden="true"
          className={`mt-0.5 shrink-0 ${tone === 'progress' ? 'motion-safe:animate-spin' : ''}`}
          style={{ color: `var(--accent-${accent})` }}
        />
        {/* The body scrolls when a text is very long; the close button stays where it is */}
        <div
          tabIndex={0}
          className="max-h-[min(45vh,20rem)] min-w-0 flex-1 overflow-y-auto focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent-crystal)]"
        >
          <p className="text-sm font-extrabold leading-snug" style={{ color: 'var(--text-primary)' }}>
            {title}
          </p>
          {message && (
            <div className="mt-1 whitespace-pre-line break-words text-[0.8125rem] leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
              {message}
            </div>
          )}

          {details && (
            <div className="mt-2">
              <button
                type="button"
                aria-expanded={showDetails}
                aria-controls={detailsId}
                onClick={() => setShowDetails((open) => !open)}
                className="inline-flex cursor-pointer items-center gap-1 text-xs font-bold underline-offset-2 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--accent-crystal)]"
                style={{ color: 'var(--text-secondary)' }}
              >
                <IconChevronDown size={14} aria-hidden="true" className={`transition-transform ${showDetails ? 'rotate-180' : ''}`} />
                {t('game:notice.details')}
              </button>
              {showDetails && (
                <pre
                  id={detailsId}
                  className="mt-1.5 max-h-48 overflow-auto whitespace-pre-wrap break-words p-2.5 font-mono text-xs leading-relaxed"
                  style={{ background: 'var(--bg-elevated)', color: 'var(--text-secondary)', border: '1px solid var(--border-subtle)', borderRadius: 'var(--radius-sm)' }}
                >
                  {details}
                </pre>
              )}
            </div>
          )}

          {actions && actions.length > 0 && (
            <div className="mt-2.5 flex flex-wrap gap-2">
              {actions.map((action) => (
                <button key={action.label} type="button" className="pv-btn !px-2.5 !py-1 !text-xs" onClick={action.onClick}>
                  {action.label}
                </button>
              ))}
            </div>
          )}
        </div>
      </div>

      {onClose && (
        <div className="absolute end-1.5 top-1.5">
          <GhostIconButton label={t('common:close')} size="md" onClick={onClose}>
            <IconX size={17} aria-hidden="true" />
          </GhostIconButton>
        </div>
      )}
    </motion.div>
  )
}
