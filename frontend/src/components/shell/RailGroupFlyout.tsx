import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import type { KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Link } from 'react-router'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { Tooltip } from '../ui/Tooltip'
import { ChapterTrailing } from './ChapterMarks'
import { gameTabHref, type ChapterItem } from './navModel'

const EDGE = 8
const GAP = 6

interface FlyoutProps {
  anchor: HTMLElement
  menuId: string
  label: string
  gameSlug: string
  chapters: ChapterItem[]
  activeSlug: string
  onClose: (returnFocus: boolean) => void
}

/**
 * The list itself: a small own menu to the right of the rail, in px (like the language menu) so a change of the UI size does not move
 * it while it is open. Arrow keys move, Home/End jump, Esc and Tab close it and return the focus to the group's icon.
 */
function Flyout({ anchor, menuId, label, gameSlug, chapters, activeSlug, onClose }: FlyoutProps) {
  const reduce = useReducedMotion() ?? false
  const ref = useRef<HTMLDivElement>(null)
  const [place, setPlace] = useState<{ left: number; top: number } | null>(null)

  const items = () => [...(ref.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? [])]

  // Placed once, when it opens: beside the icon, pushed up when it would run out of the window
  useLayoutEffect(() => {
    const panel = ref.current
    if (!panel) return
    const a = anchor.getBoundingClientRect()
    const height = panel.getBoundingClientRect().height
    const top = Math.min(Math.max(a.top, EDGE), Math.max(EDGE, window.innerHeight - height - EDGE))
    setPlace({ left: Math.round(a.right + GAP), top: Math.round(top) })
  }, [anchor])

  // Focus moves in (onto the open chapter, else the first); a click outside closes it
  useEffect(() => {
    const list = items()
    ;(list.find((item) => item.getAttribute('aria-current') === 'page') ?? list[0])?.focus({ preventScroll: true })
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node
      if (ref.current?.contains(target) || anchor.contains(target)) return
      onClose(false)
    }
    document.addEventListener('pointerdown', onPointerDown, true)
    return () => document.removeEventListener('pointerdown', onPointerDown, true)
  }, [anchor, onClose])

  const onKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    const list = items()
    const index = list.indexOf(document.activeElement as HTMLElement)
    const move = (to: number) => {
      event.preventDefault()
      list[(to + list.length) % list.length]?.focus()
    }
    switch (event.key) {
      case 'ArrowDown':
        return move(index + 1)
      case 'ArrowUp':
        return move(index <= 0 ? list.length - 1 : index - 1)
      case 'Home':
        return move(0)
      case 'End':
        return move(list.length - 1)
      case 'Escape':
      case 'ArrowLeft':
        event.preventDefault()
        event.stopPropagation()
        onClose(true)
        return
      case 'Tab':
        // A menu is left with Tab like with Esc: it closes and the focus returns to its icon
        event.preventDefault()
        onClose(true)
        return
    }
  }

  return createPortal(
    <motion.div
      ref={ref}
      id={menuId}
      role="menu"
      aria-label={label}
      className="pv-flyout"
      data-pv-scroll="flyout"
      style={{ left: place?.left ?? -9999, top: place?.top ?? 0 }}
      onKeyDown={onKeyDown}
      initial={reduce ? { opacity: 0 } : { opacity: 0, x: -6 }}
      animate={reduce ? { opacity: 1 } : { opacity: 1, x: 0 }}
      exit={{ opacity: 0, transition: { duration: 0.1 } }}
      transition={{ duration: 0.14, ease: [0.22, 1, 0.36, 1] }}
    >
      <div className="pv-flyout-head" role="presentation">
        {label}
      </div>
      {chapters.map((chapter) => (
        <Link
          key={chapter.slug}
          to={gameTabHref(gameSlug, chapter.slug)}
          role="menuitem"
          className="pv-flyout-item"
          aria-current={activeSlug === chapter.slug ? 'page' : undefined}
          tabIndex={-1}
          onClick={() => onClose(true)}
        >
          <span className="pv-ni-label">{chapter.title}</span>
          <ChapterTrailing chapter={chapter} />
        </Link>
      ))}
    </motion.div>,
    document.body
  )
}

interface RailGroupFlyoutProps {
  icon: ReactNode
  // Name of the chapter group (also the accessible name and the tooltip of the icon)
  label: string
  gameSlug: string
  chapters: ChapterItem[]
  activeSlug: string
}

/**
 * One chapter group in the collapsed navigation: a single icon that opens a small flyout with that group's chapters. Enter, Space (and
 * the arrow keys) open it, the arrows move inside, Esc closes it and the focus returns to the icon. The icon carries the thin accent
 * line while the open chapter belongs to this group.
 */
export function RailGroupFlyout({ icon, label, gameSlug, chapters, activeSlug }: RailGroupFlyoutProps) {
  const [open, setOpen] = useState(false)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const menuId = useId()
  const active = chapters.some((chapter) => chapter.slug === activeSlug)

  const close = useCallback((returnFocus: boolean) => {
    setOpen(false)
    if (returnFocus) buttonRef.current?.focus()
  }, [])

  return (
    <>
      <Tooltip label={label} side="right" className="pv-ni-tip" disabled={open}>
        <button
          ref={buttonRef}
          type="button"
          className="pv-ni pv-ni--group"
          aria-label={label}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-controls={open ? menuId : undefined}
          data-active={active ? 'true' : undefined}
          onClick={() => setOpen((value) => !value)}
          onKeyDown={(event) => {
            if (!open && (event.key === 'ArrowRight' || event.key === 'ArrowDown')) {
              event.preventDefault()
              setOpen(true)
            }
          }}
        >
          <span className="pv-ni-icon" aria-hidden="true">
            {icon}
          </span>
        </button>
      </Tooltip>
      <AnimatePresence>
        {open && buttonRef.current && (
          <Flyout key="flyout" anchor={buttonRef.current} menuId={menuId} label={label} gameSlug={gameSlug} chapters={chapters} activeSlug={activeSlug} onClose={close} />
        )}
      </AnimatePresence>
    </>
  )
}
