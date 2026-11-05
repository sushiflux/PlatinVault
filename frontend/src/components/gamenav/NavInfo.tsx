import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState, type CSSProperties, type KeyboardEvent as ReactKeyboardEvent, type FocusEvent as ReactFocusEvent } from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { IconInfoCircle } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { useBranding } from '../FooterParts'
import { LicenseViewer } from '../LicenseViewer'
import { InfoBody } from './InfoBody'
import './gamenav.css'

// What opened the card: the pointer resting on the symbol (closes again when it leaves), the keyboard (closes when the focus
// leaves) or a click / tap (stays until Esc, a click elsewhere or the focus moves away)
type Source = 'hover' | 'focus' | 'click'

const HOVER_OPEN_DELAY = 200
const FOCUS_OPEN_DELAY = 60
const LEAVE_CLOSE_DELAY = 260
// A focus event right after a Tab key press is the keyboard arriving at the symbol (a programmatic focus, a mouse click and the
// return from the licence viewer are not)
const TAB_WINDOW = 400
const FOCUSABLE = 'button:not([disabled]), [href], [tabindex]:not([tabindex="-1"])'

const GAP = 10
const MARGIN = 8

interface Placement {
  left: number
  top: number
  side: 'right' | 'top'
  arrow: number
  // The invisible strip that joins the symbol and the card (a pointer crossing the gap never leaves them): its width and its place
  bridge: { width: number; top: number; height: number }
}

function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), Math.max(min, max))
}

/**
 * Where the card goes: beside the navigation (its right edge) and bottom-aligned with the symbol, as a flyout of the nav - above the
 * symbol in the drawer, or when there is no room at the right. Always inside the window.
 */
function place(anchor: DOMRect, edge: number, width: number, height: number, preferred: 'right' | 'top', viewportWidth: number, viewportHeight: number): Placement {
  const side = preferred === 'right' && edge + GAP + width > viewportWidth - MARGIN ? 'top' : preferred
  if (side === 'right') {
    const left = edge + GAP
    const top = clamp(anchor.bottom + 4 - height, MARGIN, viewportHeight - height - MARGIN)
    return {
      left,
      top,
      side,
      arrow: clamp(anchor.top + anchor.height / 2 - top, 16, height - 16),
      bridge: { width: Math.max(0, left - anchor.right), top: anchor.top - top, height: anchor.height },
    }
  }
  const left = clamp(anchor.left + anchor.width / 2 - width / 2, MARGIN, viewportWidth - width - MARGIN)
  const top = clamp(anchor.top - GAP - height, MARGIN, viewportHeight - height - MARGIN)
  return { left, top, side, arrow: clamp(anchor.left + anchor.width / 2 - left, 16, width - 16), bridge: { width: 0, top: 0, height: 0 } }
}

interface InfoCardProps {
  id: string
  anchorRef: React.RefObject<HTMLButtonElement | null>
  cardRef: React.RefObject<HTMLDivElement | null>
  preferred: 'right' | 'top'
  onPointerEnter: () => void
  onPointerLeave: () => void
  onKeyDown: (event: ReactKeyboardEvent<HTMLDivElement>) => void
  onBlur: (event: ReactFocusEvent<HTMLDivElement>) => void
  onOpenLicenses: () => void
  // The version popover and the "what's new" window open from inside the card (they live in <body>, outside of it)
  onVersionPanelChange: (open: boolean) => void
  onVersionNotesChange: (open: boolean) => void
  // The "what's new" window is up: the card stays mounted (it owns that window) but steps out of sight and reach
  suspended: boolean
}

// The card itself: lives in <body> (never inside the layout, the nav clips its own content), measured once and then placed
// next to the symbol; it follows the symbol when the window is resized or scrolled.
function InfoCard({
  id,
  anchorRef,
  cardRef,
  preferred,
  onPointerEnter,
  onPointerLeave,
  onKeyDown,
  onBlur,
  onOpenLicenses,
  onVersionPanelChange,
  onVersionNotesChange,
  suspended,
}: InfoCardProps) {
  const { t } = useTranslation('shell')
  const reduce = useReducedMotion() ?? false
  const { data: branding } = useBranding()
  const name = branding?.productName ?? 'PlatinVault'
  const [placement, setPlacement] = useState<Placement | null>(null)

  useLayoutEffect(() => {
    const update = () => {
      const anchor = anchorRef.current
      const card = cardRef.current
      if (!anchor || !card) return
      // The untransformed layout size: the entrance animation scales the card while it is measured
      const anchorRect = anchor.getBoundingClientRect()
      const edge = anchor.closest('nav')?.getBoundingClientRect().right ?? anchorRect.right
      const next = place(anchorRect, edge, card.offsetWidth, card.offsetHeight, preferred, document.documentElement.clientWidth, window.innerHeight)
      setPlacement((previous) => (previous && JSON.stringify(previous) === JSON.stringify(next) ? previous : next))
    }
    update()
    let frame = 0
    const schedule = () => {
      window.cancelAnimationFrame(frame)
      frame = window.requestAnimationFrame(update)
    }
    window.addEventListener('resize', schedule)
    window.addEventListener('scroll', schedule, true)
    return () => {
      window.cancelAnimationFrame(frame)
      window.removeEventListener('resize', schedule)
      window.removeEventListener('scroll', schedule, true)
    }
  }, [anchorRef, cardRef, preferred])

  const side = placement?.side ?? preferred
  const shift = reduce ? {} : side === 'right' ? { x: -8 } : { y: 8 }
  return (
    <motion.div
      ref={cardRef}
      id={id}
      role="dialog"
      aria-label={t('footer.about', { name })}
      tabIndex={-1}
      className="pv-info-card no-print"
      data-side={side}
      style={
        {
          left: placement?.left ?? 0,
          top: placement?.top ?? 0,
          visibility: placement ? 'visible' : 'hidden',
          pointerEvents: suspended ? 'none' : undefined,
          transformOrigin: side === 'right' ? 'left bottom' : 'center bottom',
          '--pv-info-bridge-w': `${placement?.bridge.width ?? 0}px`,
          '--pv-info-bridge-y': `${placement?.bridge.top ?? 0}px`,
          '--pv-info-bridge-h': `${placement?.bridge.height ?? 0}px`,
        } as CSSProperties
      }
      initial={{ opacity: 0, scale: reduce ? 1 : 0.97, ...shift }}
      animate={{ opacity: suspended ? 0 : 1, scale: 1, x: 0, y: 0 }}
      exit={{ opacity: 0, scale: reduce ? 1 : 0.97, ...shift }}
      transition={{ duration: reduce ? 0.12 : 0.18, ease: [0.22, 1, 0.36, 1] }}
      onPointerEnter={onPointerEnter}
      onPointerLeave={onPointerLeave}
      onKeyDown={onKeyDown}
      onBlur={onBlur}
    >
      <span
        className="pv-info-arrow"
        aria-hidden="true"
        style={side === 'right' ? { top: (placement?.arrow ?? 24) - 5 } : { left: (placement?.arrow ?? 24) - 5 }}
      />
      <InfoBody onOpenLicenses={onOpenLicenses} onVersionCheckChange={onVersionPanelChange} onVersionNotesChange={onVersionNotesChange} />
    </motion.div>
  )
}

interface NavInfoProps {
  // Phone drawer: the card opens above the symbol instead of beside it
  drawer: boolean
  // The card or the licence viewer opened or closed (the drawer pauses its own focus trap and Esc while one of them is up)
  onOpenChange?: (open: boolean) => void
}

/**
 * The info symbol at the foot of the side navigation and the card it opens: brand, version, "Made with <heart>", copyright and the
 * licences button (the page footer's content, shared via FooterParts). Opens when the pointer rests on the symbol (short delay), when the
 * keyboard arrives at it or on a click / tap; stays while the pointer is on the symbol or the card (the licences button must be
 * reachable); closes on Esc, a click elsewhere, when the pointer or the focus leaves (short delay).
 */
export function NavInfo({ drawer, onOpenChange }: NavInfoProps) {
  const { t } = useTranslation('shell')
  const cardId = useId()
  const triggerRef = useRef<HTMLButtonElement>(null)
  const cardRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [viewerOpen, setViewerOpen] = useState(false)
  // Something opened from inside the card: the version popover and, after it, the "what's new" window. While one is up it owns Esc,
  // the presses and the focus; the card waits underneath and is the next thing Esc closes.
  const [panelOpen, setPanelOpen] = useState(false)
  const [notesOpen, setNotesOpen] = useState(false)
  // The latest values for the timers and listeners
  const openRef = useRef(false)
  const innerOpenRef = useRef(false)
  const sourceRef = useRef<Source>('hover')
  const timerRef = useRef<number | undefined>(undefined)
  const lastTabRef = useRef(0)
  const quietFocusRef = useRef(false)

  useEffect(() => {
    openRef.current = open
  }, [open])

  useEffect(() => {
    onOpenChange?.(open || viewerOpen || panelOpen || notesOpen)
  }, [open, viewerOpen, panelOpen, notesOpen, onOpenChange])

  // Gone from the nav: the nav is no longer told about an overlay of this symbol
  useEffect(() => () => onOpenChange?.(false), [onOpenChange])

  // Opened from inside the card: it stays until it is closed by hand, whatever opened it (the pointer is on the popover then, not on the
  // card). Stable functions: the version button reports through effects that depend on them.
  const panelRef = useRef(false)
  const notesRef = useRef(false)
  const onVersionPanelChange = useCallback((value: boolean) => {
    if (value) {
      window.clearTimeout(timerRef.current)
      sourceRef.current = 'click'
    }
    panelRef.current = value
    innerOpenRef.current = value || notesRef.current
    setPanelOpen(value)
  }, [])
  const onVersionNotesChange = useCallback((value: boolean) => {
    if (value) {
      window.clearTimeout(timerRef.current)
      sourceRef.current = 'click'
    }
    notesRef.current = value
    innerOpenRef.current = value || panelRef.current
    setNotesOpen(value)
  }, [])

  useEffect(() => () => window.clearTimeout(timerRef.current), [])

  const clearTimer = () => window.clearTimeout(timerRef.current)
  const show = (source: Source, delay = 0) => {
    clearTimer()
    const run = () => {
      sourceRef.current = source
      openRef.current = true
      setOpen(true)
    }
    if (delay > 0) timerRef.current = window.setTimeout(run, delay)
    else run()
  }
  const hide = (delay = 0) => {
    clearTimer()
    const run = () => {
      openRef.current = false
      panelRef.current = false
      notesRef.current = false
      innerOpenRef.current = false
      setPanelOpen(false)
      setNotesOpen(false)
      setOpen(false)
    }
    if (delay > 0) timerRef.current = window.setTimeout(run, delay)
    else run()
  }

  // Focus back on the symbol without that counting as "the keyboard arrived" (which would open the card again)
  const focusTrigger = () => {
    const trigger = triggerRef.current
    if (!trigger || document.activeElement === trigger) return
    quietFocusRef.current = true
    trigger.focus()
    quietFocusRef.current = false
  }

  // The last Tab key press: tells a keyboard arrival at the symbol from every other kind of focus
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Tab') lastTabRef.current = performance.now()
    }
    document.addEventListener('keydown', onKeyDown, true)
    return () => document.removeEventListener('keydown', onKeyDown, true)
  }, [])

  // Open: Esc and a press anywhere outside the symbol and the card close it (while an inner popover or dialog is open, it handles Esc and outside presses first)
  useEffect(() => {
    if (!open) return
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape' || innerOpenRef.current) return
      event.stopPropagation()
      const inside = cardRef.current?.contains(document.activeElement) ?? false
      hide()
      if (inside) focusTrigger()
    }
    const onPointerDown = (event: PointerEvent) => {
      if (innerOpenRef.current) return
      const target = event.target as Node | null
      if (target && (triggerRef.current?.contains(target) || cardRef.current?.contains(target))) return
      hide()
    }
    document.addEventListener('keydown', onKeyDown, true)
    document.addEventListener('pointerdown', onPointerDown, true)
    return () => {
      document.removeEventListener('keydown', onKeyDown, true)
      document.removeEventListener('pointerdown', onPointerDown, true)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const inGroup = (node: EventTarget | null) => node instanceof Node && Boolean(triggerRef.current?.contains(node) || cardRef.current?.contains(node))

  // Focus left the symbol and the card: a real target elsewhere always closes, a click on nothing is handled by the press listener
  // (closes a card the keyboard opened, too)
  const onGroupBlur = (event: ReactFocusEvent<HTMLElement>) => {
    if (!openRef.current || innerOpenRef.current) return
    const next = event.relatedTarget
    const leaving = next === null ? sourceRef.current === 'focus' : !inGroup(next)
    if (leaving) hide()
  }

  const onPointerEnter = (event: React.PointerEvent) => {
    if (event.pointerType !== 'mouse') return
    clearTimer()
    if (!openRef.current) show('hover', HOVER_OPEN_DELAY)
  }
  const onPointerLeave = (event: React.PointerEvent) => {
    if (event.pointerType !== 'mouse') return
    clearTimer()
    if (openRef.current && sourceRef.current === 'hover' && !innerOpenRef.current) hide(LEAVE_CLOSE_DELAY)
  }

  const onTriggerClick = () => {
    if (openRef.current && sourceRef.current === 'click') hide()
    else show('click')
  }

  const onTriggerFocus = () => {
    if (quietFocusRef.current || openRef.current) return
    if (performance.now() - lastTabRef.current < TAB_WINDOW) show('focus', FOCUS_OPEN_DELAY)
  }

  // Tab from the symbol goes into the card (it is in <body>, not next to the symbol in the page order)
  const onTriggerKeyDown = (event: ReactKeyboardEvent<HTMLButtonElement>) => {
    if (event.key !== 'Tab' || event.shiftKey || !openRef.current) return
    const first = cardRef.current?.querySelector<HTMLElement>(FOCUSABLE)
    if (!first) return
    event.preventDefault()
    first.focus()
  }

  // Tab inside the card: back from its first control to the symbol, on from its last control to what follows the symbol
  const onCardKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Tab') return
    const items = [...(cardRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? [])]
    const first = items[0]
    const last = items[items.length - 1]
    const active = document.activeElement
    if (event.shiftKey && (active === first || active === cardRef.current)) {
      event.preventDefault()
      focusTrigger()
    } else if (!event.shiftKey && active === last) {
      hide()
      const nav = triggerRef.current?.closest('nav')
      if (drawer && nav) {
        // The drawer keeps the focus inside itself: on to its first control
        event.preventDefault()
        nav.querySelector<HTMLElement>(FOCUSABLE)?.focus()
      } else {
        // The default Tab continues from the symbol
        focusTrigger()
      }
    }
  }

  // "Lizenzen": the viewer opens, the card steps aside. The symbol takes the focus first, so the viewer hands it back there.
  const openLicenses = () => {
    focusTrigger()
    hide()
    setViewerOpen(true)
  }

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className="pv-gnav-info-btn"
        aria-label={t('footer.infoLabel')}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-controls={open ? cardId : undefined}
        onClick={onTriggerClick}
        onFocus={onTriggerFocus}
        onBlur={onGroupBlur}
        onKeyDown={onTriggerKeyDown}
        onPointerEnter={onPointerEnter}
        onPointerLeave={onPointerLeave}
      >
        <IconInfoCircle size={20} aria-hidden="true" />
      </button>
      {createPortal(
        <AnimatePresence>
          {open && (
            <InfoCard
              key="info-card"
              id={cardId}
              anchorRef={triggerRef}
              cardRef={cardRef}
              preferred={drawer ? 'top' : 'right'}
              onPointerEnter={() => {
                clearTimer()
              }}
              onPointerLeave={() => {
                clearTimer()
                if (openRef.current && sourceRef.current === 'hover' && !innerOpenRef.current) hide(LEAVE_CLOSE_DELAY)
              }}
              onKeyDown={onCardKeyDown}
              onBlur={onGroupBlur}
              onOpenLicenses={openLicenses}
              onVersionPanelChange={onVersionPanelChange}
              onVersionNotesChange={onVersionNotesChange}
              suspended={notesOpen}
            />
          )}
        </AnimatePresence>,
        document.body
      )}
      <LicenseViewer open={viewerOpen} onClose={() => setViewerOpen(false)} />
    </>
  )
}
