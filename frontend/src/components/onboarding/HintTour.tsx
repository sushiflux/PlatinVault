import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { IconBook2, IconRoute } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { HandbookViewer } from '../HandbookViewer'
import { DRAWER_QUERY } from '../gamenav/ChapterSidebar'
import { useMediaQuery } from '../../utils/useMediaQuery'
import { findHintTargets, type HintDefinition, type HintId, type HintScreen } from './useScreenHints'

const SPOT_PADDING = 6
const GAP = 14
const MARGIN = 12

// Live position of the control a hint points at (follows scrolling, resizing and layout changes frame by frame)
function useTargetRect(target: string | null): DOMRect | null {
  const [rect, setRect] = useState<DOMRect | null>(null)
  useEffect(() => {
    if (!target) {
      setRect(null)
      return
    }
    let frame = 0
    const tick = () => {
      const element = document.querySelector<HTMLElement>(`[data-hint="${target}"]`)
      const next = element?.getBoundingClientRect() ?? null
      setRect((previous) => {
        if (!next) return null
        if (
          previous &&
          Math.abs(previous.x - next.x) < 0.5 &&
          Math.abs(previous.y - next.y) < 0.5 &&
          Math.abs(previous.width - next.width) < 0.5 &&
          Math.abs(previous.height - next.height) < 0.5
        ) {
          return previous
        }
        return next
      })
      frame = requestAnimationFrame(tick)
    }
    tick()
    return () => cancelAnimationFrame(frame)
  }, [target])
  return rect
}

// Puts the card next to the target on the side with room (below, above, right, left), kept inside the window
function placeCard(rect: DOMRect | null, card: { w: number; h: number }, vw: number, vh: number): { left: number; top: number } {
  if (!rect) return { left: Math.max(MARGIN, (vw - card.w) / 2), top: Math.max(MARGIN, (vh - card.h) / 2) }
  const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), Math.max(min, max))
  const below = vh - rect.bottom - GAP - MARGIN >= card.h
  const above = rect.top - GAP - MARGIN >= card.h
  const right = vw - rect.right - GAP - MARGIN >= card.w
  const left = rect.left - GAP - MARGIN >= card.w
  // Tall targets (a sidebar) are better served from the side
  const tall = rect.height > vh * 0.5
  const order = tall ? (['right', 'left', 'below', 'above'] as const) : (['below', 'above', 'right', 'left'] as const)
  const fits = { below, above, right, left }
  const side = order.find((s) => fits[s]) ?? 'below'
  const centerX = rect.left + rect.width / 2
  const centerY = rect.top + rect.height / 2
  if (side === 'below') return { left: clamp(centerX - card.w / 2, MARGIN, vw - card.w - MARGIN), top: clamp(rect.bottom + GAP, MARGIN, vh - card.h - MARGIN) }
  if (side === 'above') return { left: clamp(centerX - card.w / 2, MARGIN, vw - card.w - MARGIN), top: clamp(rect.top - GAP - card.h, MARGIN, vh - card.h - MARGIN) }
  if (side === 'right') return { left: clamp(rect.right + GAP, MARGIN, vw - card.w - MARGIN), top: clamp(centerY - card.h / 2, MARGIN, vh - card.h - MARGIN) }
  return { left: clamp(rect.left - GAP - card.w, MARGIN, vw - card.w - MARGIN), top: clamp(centerY - card.h / 2, MARGIN, vh - card.h - MARGIN) }
}

interface HintTourProps {
  screen: HintScreen
  open: boolean
  onClose: () => void
  // Opens the full intro tour (and closes the hints)
  onShowIntro: () => void
}

/**
 * Coach marks: a dimmed screen with a soft cut-out around one real control and a small card next to it, one hint
 * after the other. Own implementation (no tour library); targets are addressed by data-hint attributes.
 */
export function HintTour({ screen, open, onClose, onShowIntro }: HintTourProps) {
  const { t } = useTranslation('onboarding')
  const reduce = useReducedMotion() ?? false
  const cardRef = useRef<HTMLDivElement>(null)
  const primaryRef = useRef<HTMLButtonElement>(null)
  const closeRef = useRef(onClose)
  const [steps, setSteps] = useState<HintDefinition[]>([])
  const [index, setIndex] = useState(0)
  const [card, setCard] = useState({ w: 352, h: 220 })
  const [view, setView] = useState({ w: window.innerWidth, h: window.innerHeight })
  const [handbookOpen, setHandbookOpen] = useState(false)
  // On a phone the chapters are a drawer behind a button, not a panel: the chapter hint says so
  const chaptersAreDrawer = useMediaQuery(DRAWER_QUERY)

  useEffect(() => {
    closeRef.current = onClose
  }, [onClose])

  // When the hints open: the controls that exist right now, from the first one
  useEffect(() => {
    if (!open) return
    setSteps(findHintTargets(screen))
    setIndex(0)
  }, [open, screen])

  const step = open ? steps[index] : undefined
  const rect = useTargetRect(step?.target ?? null)

  // Esc closes; focus returns to where it was
  useEffect(() => {
    if (!open) return
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const onKeyDown = (e: globalThis.KeyboardEvent) => {
      if (e.key === 'Escape') closeRef.current()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      previous?.focus()
    }
  }, [open])

  // Bring a control that is off screen into view, then move the keyboard focus into the card
  useEffect(() => {
    if (!step) return
    const element = document.querySelector<HTMLElement>(`[data-hint="${step.target}"]`)
    const bounds = element?.getBoundingClientRect()
    if (element && bounds && (bounds.top < 80 || bounds.bottom > window.innerHeight - 20)) {
      element.scrollIntoView({ block: 'center', behavior: reduce ? 'auto' : 'smooth' })
    }
    primaryRef.current?.focus({ preventScroll: true })
  }, [step, reduce])

  // Size of the card (for the placement) and of the window
  useLayoutEffect(() => {
    const element = cardRef.current
    if (!element) return
    const measure = () => {
      setCard({ w: element.offsetWidth, h: element.offsetHeight })
      setView({ w: window.innerWidth, h: window.innerHeight })
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    window.addEventListener('resize', measure)
    return () => {
      observer.disconnect()
      window.removeEventListener('resize', measure)
    }
  }, [step])

  const next = useCallback(() => {
    if (index >= steps.length - 1) onClose()
    else setIndex(index + 1)
  }, [index, steps.length, onClose])

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== 'Tab') return
    const nodes = [...(cardRef.current?.querySelectorAll<HTMLElement>('button') ?? [])]
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

  const position = placeCard(rect, card, view.w, view.h)
  const last = index >= steps.length - 1
  // gameNavPhone is only a text of gameNav, never a hint of its own
  const textIdOf = (id: HintId): HintId => (id === 'gameNav' && chaptersAreDrawer ? 'gameNavPhone' : id)

  return (
    <>
      {createPortal(
        <AnimatePresence>
          {open && step && (
            <motion.div
              key="hints"
              className="pv-hint-root"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              transition={{ duration: reduce ? 0.1 : 0.25 }}
              // The page behind is not clickable while a hint is up
              onMouseDown={(e) => e.preventDefault()}
            >
              {rect ? (
                <motion.div
                  className="pv-hint-spot"
                  aria-hidden="true"
                  initial={false}
                  animate={{
                    left: rect.left - SPOT_PADDING,
                    top: rect.top - SPOT_PADDING,
                    width: rect.width + SPOT_PADDING * 2,
                    height: rect.height + SPOT_PADDING * 2,
                  }}
                  transition={reduce ? { duration: 0.01 } : { type: 'spring', stiffness: 260, damping: 30 }}
                />
              ) : (
                <div className="pv-hint-dim" aria-hidden="true" />
              )}

              <motion.div
                ref={cardRef}
                className="pv-hint-card"
                role="dialog"
                aria-modal="true"
                aria-labelledby="pv-hint-title"
                aria-describedby="pv-hint-text"
                onKeyDown={onKeyDown}
                initial={false}
                animate={{ left: position.left, top: position.top }}
                transition={reduce ? { duration: 0.01 } : { type: 'spring', stiffness: 300, damping: 32 }}
              >
                <div className="pv-hint-meta">
                  {t('hints.meta', { current: index + 1, total: steps.length })}
                </div>
                <h2 id="pv-hint-title">{t(`hints.${textIdOf(step.id)}.title`)}</h2>
                <p id="pv-hint-text">{t(`hints.${textIdOf(step.id)}.text`)}</p>
                <div className="pv-hint-actions">
                  <button type="button" className="pv-hint-skip" onClick={onClose}>
                    {t('hints.hide')}
                  </button>
                  <button ref={primaryRef} type="button" className="pv-btn pv-btn-primary" onClick={next}>
                    {last ? t('hints.done') : t('hints.next')}
                  </button>
                </div>
                <div className="pv-hint-links">
                  <button type="button" onClick={onShowIntro}>
                    <IconRoute size={14} aria-hidden="true" />
                    {t('hints.intro')}
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      onClose()
                      setHandbookOpen(true)
                    }}
                  >
                    <IconBook2 size={14} aria-hidden="true" />
                    {t('hints.handbook')}
                  </button>
                </div>
              </motion.div>
            </motion.div>
          )}
        </AnimatePresence>,
        document.body
      )}
      <HandbookViewer open={handbookOpen} onClose={() => setHandbookOpen(false)} />
    </>
  )
}
