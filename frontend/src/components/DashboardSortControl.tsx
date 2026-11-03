import { useCallback, useEffect, useId, useRef, useState } from 'react'
import type { KeyboardEvent as ReactKeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import {
  IconAward,
  IconCalendar,
  IconCheck,
  IconChevronDown,
  IconClockHour4,
  IconGripVertical,
  IconHistory,
  IconLoader2,
  IconPercentage,
  IconSortAscending,
  IconSortAscendingLetters,
  IconSortDescending,
  IconThumbUp,
  IconDatabase,
} from '@tabler/icons-react'
import type { Icon } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import {
  ORDERS_BY_VIEW,
  chooseOrder,
  flipDirection,
  type DashboardSortView,
  type SortChoice,
  type SortOrder,
} from '../utils/dashboardSort'
import { Tooltip } from './ui/Tooltip'

const ORDER_ICONS: Record<SortOrder, Icon> = {
  custom: IconGripVertical,
  completion: IconPercentage,
  alphabetical: IconSortAscendingLetters,
  playtime: IconClockHour4,
  lastPlayed: IconHistory,
  release: IconCalendar,
  size: IconDatabase,
  metacritic: IconAward,
  reviews: IconThumbUp,
}

const EDGE = 8

interface MenuProps {
  anchor: HTMLElement
  menuId: string
  view: DashboardSortView
  current: SortOrder
  onSelect: (order: SortOrder) => void
  onClose: () => void
}

/** The menu: opens under the button, right-aligned to it; keyboard like the language menu (arrows, Home/End, Enter, Esc). */
function SortMenu({ anchor, menuId, view, current, onSelect, onClose }: MenuProps) {
  const { t } = useTranslation('dashboard')
  const reduce = useReducedMotion() ?? false
  const ref = useRef<HTMLDivElement>(null)
  const [place] = useState(() => {
    const rect = anchor.getBoundingClientRect()
    const viewportWidth = document.documentElement.clientWidth
    const width = Math.min(rect.width + 96, viewportWidth - 2 * EDGE)
    const left = Math.min(Math.max(rect.right - width, EDGE), viewportWidth - width - EDGE)
    const top = Math.round(rect.bottom + 8)
    return { left, top, width, room: Math.max(160, window.innerHeight - top - EDGE) }
  })

  const items = () => [...(ref.current?.querySelectorAll<HTMLElement>('[role="menuitemradio"]') ?? [])]

  // Focus goes onto the order in use; Esc and a press outside close the menu and the focus returns to the button
  useEffect(() => {
    const active = items().find((item) => item.getAttribute('aria-checked') === 'true') ?? items()[0]
    active?.focus()
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node
      if (ref.current?.contains(target) || anchor.contains(target)) return
      onClose()
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
      case 'Tab':
        event.preventDefault()
        event.stopPropagation()
        onClose()
        anchor.focus()
        return
    }
  }

  return createPortal(
    <motion.div
      ref={ref}
      id={menuId}
      role="menu"
      aria-label={t('sort.menu')}
      className="pv-sortmenu"
      style={{ left: place.left, top: place.top, width: place.width, maxBlockSize: place.room }}
      onKeyDown={onKeyDown}
      initial={reduce ? { opacity: 0 } : { opacity: 0, y: -6, scale: 0.98 }}
      animate={reduce ? { opacity: 1 } : { opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, transition: { duration: 0.1 } }}
      transition={{ duration: 0.16, ease: [0.22, 1, 0.36, 1] }}
    >
      {ORDERS_BY_VIEW[view].map((order) => {
        const checked = order === current
        const OrderIcon = ORDER_ICONS[order]
        return (
          <button
            key={order}
            type="button"
            role="menuitemradio"
            aria-checked={checked}
            tabIndex={checked ? 0 : -1}
            className="pv-sortmenu-item"
            onClick={() => onSelect(order)}
          >
            <OrderIcon size={18} className="pv-sortmenu-icon" aria-hidden="true" />
            <span className="pv-sortmenu-text">
              <span className="pv-sortmenu-name">{t(`sort.orders.${order}`)}</span>
              <span className="pv-sortmenu-hint">{t(`sort.hints.${order}`)}</span>
            </span>
            <IconCheck size={16} stroke={3} className="pv-sortmenu-check" aria-hidden="true" />
          </button>
        )
      })}
    </motion.div>,
    document.body
  )
}

interface DashboardSortControlProps {
  view: DashboardSortView
  choice: SortChoice
  onChange: (choice: SortChoice) => void
  // The values of the chosen order are still being looked up (a spinner replaces the order's icon; nothing changes size)
  loading?: boolean
}

/**
 * The sort control of the dashboard's overview panel: a button with the active order that opens a menu of all orders of the view, and
 * a button that reverses the direction. Both keep their size whatever the state is (fixed width, spinner and icon share one box).
 */
export function DashboardSortControl({ view, choice, onChange, loading = false }: DashboardSortControlProps) {
  const { t } = useTranslation('dashboard')
  const reduce = useReducedMotion() ?? false
  const [open, setOpen] = useState(false)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const menuId = useId()
  const close = useCallback(() => setOpen(false), [])

  const OrderIcon = ORDER_ICONS[choice.order]
  const orderName = t(`sort.orders.${choice.order}`)
  const directionText = t(`sort.directions.${choice.order}.${choice.direction}`)
  const fixed = choice.order === 'custom'
  const DirectionIcon = choice.direction === 'asc' ? IconSortAscending : IconSortDescending

  const select = (order: SortOrder) => {
    setOpen(false)
    buttonRef.current?.focus()
    if (order !== choice.order) onChange(chooseOrder(choice, order))
  }

  return (
    <div className="pv-sort" role="group" aria-label={t('sort.group')}>
      <button
        ref={buttonRef}
        type="button"
        className="pv-sort-select"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        aria-label={t('sort.buttonAria', { order: orderName })}
        aria-busy={loading}
        onClick={() => setOpen((value) => !value)}
      >
        <span className="pv-sort-icon" aria-hidden="true">
          {loading ? <IconLoader2 size={16} className={reduce ? undefined : 'animate-spin'} /> : <OrderIcon size={16} />}
        </span>
        <span className="pv-sort-label">{orderName}</span>
        <IconChevronDown size={14} className="pv-sort-chevron" aria-hidden="true" />
      </button>

      <Tooltip
        label={fixed ? t('sort.direction.fixedLabel') : t('sort.direction.label')}
        description={fixed ? t('sort.direction.fixedDescription') : t('sort.direction.description', { direction: directionText })}
      >
        <button
          type="button"
          className="pv-sort-dir"
          disabled={fixed}
          aria-label={fixed ? t('sort.direction.fixedAria') : t('sort.direction.aria', { direction: directionText })}
          onClick={() => onChange(flipDirection(choice))}
        >
          <DirectionIcon size={18} aria-hidden="true" />
        </button>
      </Tooltip>

      <AnimatePresence>
        {open && buttonRef.current && (
          <SortMenu key="sort-menu" anchor={buttonRef.current} menuId={menuId} view={view} current={choice.order} onSelect={select} onClose={close} />
        )}
      </AnimatePresence>
    </div>
  )
}
