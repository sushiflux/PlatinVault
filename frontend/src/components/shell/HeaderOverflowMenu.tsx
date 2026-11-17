import { useCallback, useEffect, useId, useRef, useState } from 'react'
import type { KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Link, useLocation } from 'react-router'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { useTranslation } from 'react-i18next'
import { IconBook2, IconDots, IconHelp, IconInfoCircle, IconMoon, IconSun, IconTextSize, IconUser } from '@tabler/icons-react'
import { useFormat } from '../../i18n/format'
import { useDialogParam } from '../../utils/useDialogParam'
import { useUiScale } from '../../utils/uiScale'
import { HandbookViewer } from '../HandbookViewer'
import { iconButtonClass } from '../ui/IconButton'
import { Tooltip } from '../ui/Tooltip'
import { ScalePopover } from '../ui/UiScaleControl'
import { useSteamProfile, useSteamUserText } from './HeaderUser'

const MENU_WIDTH = 296
const EDGE = 8

interface MenuItemProps {
  icon: ReactNode
  label: string
  description?: string
}

function ItemBody({ icon, label, description }: MenuItemProps) {
  return (
    <>
      <span className="pv-ovfmenu-icon" aria-hidden="true">
        {icon}
      </span>
      <span className="pv-ovfmenu-text">
        <span className="pv-ovfmenu-label">{label}</span>
        {description && <span className="pv-ovfmenu-sub">{description}</span>}
      </span>
    </>
  )
}

interface PanelProps {
  anchor: HTMLElement
  menuId: string
  tiny: boolean
  theme: string
  onToggleTheme: () => void
  onHelp: () => void
  onScale: () => void
  onHandbook: () => void
  onClose: (returnFocus: boolean) => void
}

/**
 * The menu itself. Its CSS is in px on purpose (index.css .pv-langmenu, shared look), like the language menu: a change of the display size
 * re-flows the page and the menu keeps its size and place meanwhile. The position is taken once, when it opens.
 */
function Panel({ anchor, menuId, tiny, theme, onToggleTheme, onHelp, onScale, onHandbook, onClose }: PanelProps) {
  const { t } = useTranslation('shell')
  const reduce = useReducedMotion() ?? false
  const location = useLocation()
  const ref = useRef<HTMLDivElement>(null)
  const profile = useSteamProfile()
  const user = useSteamUserText(profile)
  const [place] = useState(() => {
    const rect = anchor.getBoundingClientRect()
    const width = Math.min(MENU_WIDTH, document.documentElement.clientWidth - 2 * EDGE)
    const left = Math.min(Math.max(rect.right - width, EDGE), document.documentElement.clientWidth - width - EDGE)
    return { left, top: Math.round(rect.bottom + 10), width }
  })

  const items = () => [...(ref.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') ?? [])]

  // Focus moves into the menu (first entry); Esc and a click outside close it, the focus goes back to the button
  useEffect(() => {
    items()[0]?.focus()
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
      case 'Tab':
        event.preventDefault()
        event.stopPropagation()
        onClose(true)
        return
    }
  }

  const dark = theme === 'dark'
  const themeKey = dark ? 'dark' : 'light'
  const nextKey = dark ? 'light' : 'dark'
  const run = (action: () => void) => () => {
    onClose(false)
    action()
  }

  return createPortal(
    <motion.div
      ref={ref}
      id={menuId}
      role="menu"
      aria-label={t('header.more.menu')}
      className="pv-langmenu pv-ovfmenu"
      style={{ left: place.left, top: place.top, width: place.width }}
      onKeyDown={onKeyDown}
      initial={reduce ? { opacity: 0 } : { opacity: 0, y: -6, scale: 0.98 }}
      animate={reduce ? { opacity: 1 } : { opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, transition: { duration: 0.1 } }}
      transition={{ duration: 0.16, ease: [0.22, 1, 0.36, 1] }}
    >
      {tiny && user.href && (
        <a
          role="menuitem"
          className="pv-ovfmenu-item"
          href={user.href}
          {...(user.openInClient ? {} : { target: '_blank', rel: 'noreferrer' })}
          aria-label={user.ariaLabel}
          tabIndex={-1}
          onClick={() => onClose(false)}
        >
          <ItemBody
            icon={profile?.avatarUrl ? <img className="pv-ovfmenu-avatar" src={profile.avatarUrl} alt="" width={24} height={24} referrerPolicy="no-referrer" /> : <IconUser size={20} />}
            label={user.label}
            description={user.countText ?? undefined}
          />
        </a>
      )}
      <button type="button" role="menuitem" className="pv-ovfmenu-item" tabIndex={-1} onClick={run(onScale)}>
        <ItemBody icon={<IconTextSize size={20} />} label={t('header.scale.label')} />
      </button>
      {tiny && (
        <button type="button" role="menuitem" className="pv-ovfmenu-item" tabIndex={-1} onClick={run(onToggleTheme)}>
          <ItemBody
            icon={dark ? <IconMoon size={20} /> : <IconSun size={20} />}
            label={t('header.theme.label', { current: t(`header.theme.${themeKey}`) })}
            description={t('header.theme.description', { next: t(`header.theme.${nextKey}`) })}
          />
        </button>
      )}
      {tiny && (
        <button type="button" role="menuitem" className="pv-ovfmenu-item" tabIndex={-1} onClick={run(onHelp)}>
          <ItemBody icon={<IconHelp size={20} />} label={t('header.help.label')} />
        </button>
      )}
      <button type="button" role="menuitem" className="pv-ovfmenu-item" tabIndex={-1} aria-haspopup="dialog" onClick={run(onHandbook)}>
        <ItemBody icon={<IconBook2 size={20} />} label={t('header.handbook.label')} />
      </button>
      <Link
        to="/about"
        role="menuitem"
        className="pv-ovfmenu-item"
        tabIndex={-1}
        aria-current={location.pathname === '/about' ? 'page' : undefined}
        onClick={() => onClose(false)}
      >
        <ItemBody icon={<IconInfoCircle size={20} />} label={t('header.info.label')} />
      </Link>
    </motion.div>,
    document.body
  )
}

interface HeaderOverflowMenuProps {
  // Phones: the colour scheme, the hints and the Steam user move in here as well
  tiny: boolean
  theme: string
  onToggleTheme: () => void
  onHelp: () => void
}

/**
 * The one overflow menu of the top bar ("More"), same component style as the language menu. Narrow windows move the less frequent entries in
 * here (display size, handbook, about - on phones also colour scheme, help and the Steam account), so every icon that stays keeps its 48 px
 * target and nothing is cut. It also owns the windows those entries open (the size popover under its button, the handbook).
 */
export function HeaderOverflowMenu({ tiny, theme, onToggleTheme, onHelp }: HeaderOverflowMenuProps) {
  const { t } = useTranslation('shell')
  const format = useFormat()
  const { scale, isDefault } = useUiScale()
  // Loaded before the menu opens (on phones the account chip is not in the bar), so the Steam entry is there from the first frame
  useSteamProfile()
  const [open, setOpen] = useState(false)
  const [scaleOpen, setScaleOpen] = useState(false)
  const [handbookOpen, setHandbookOpen] = useDialogParam('handbook')
  const buttonRef = useRef<HTMLButtonElement>(null)
  const menuId = useId()
  const scaleId = useId()

  const close = useCallback((returnFocus: boolean) => {
    setOpen(false)
    if (returnFocus) buttonRef.current?.focus()
  }, [])

  return (
    <>
      <Tooltip
        label={t('header.more.label')}
        description={isDefault ? undefined : t('header.scale.descriptionCurrent', { percent: format.percent(scale) })}
        disabled={open}
      >
        <button
          ref={buttonRef}
          type="button"
          className={iconButtonClass()}
          aria-label={t('header.more.label')}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-controls={open ? menuId : undefined}
          onClick={() => {
            setScaleOpen(false)
            setOpen((value) => !value)
          }}
        >
          <span className="inline-flex items-center justify-center">
            <IconDots size={20} />
          </span>
          {!isDefault && (
            <span className="pv-hscale-badge" aria-hidden="true">
              {scale}%
            </span>
          )}
        </button>
      </Tooltip>
      <AnimatePresence>
        {open && buttonRef.current && (
          <Panel
            key="overflow-menu"
            anchor={buttonRef.current}
            menuId={menuId}
            tiny={tiny}
            theme={theme}
            onToggleTheme={onToggleTheme}
            onHelp={onHelp}
            onScale={() => setScaleOpen(true)}
            onHandbook={() => setHandbookOpen(true)}
            onClose={close}
          />
        )}
      </AnimatePresence>
      <AnimatePresence>
        {scaleOpen && buttonRef.current && <ScalePopover key="overflow-scale" anchor={buttonRef.current} popoverId={scaleId} onClose={() => setScaleOpen(false)} />}
      </AnimatePresence>
      <HandbookViewer open={handbookOpen} onClose={() => setHandbookOpen(false)} />
    </>
  )
}
