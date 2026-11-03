import { useCallback, useEffect, useId, useRef, useState } from 'react'
import type { KeyboardEvent as ReactKeyboardEvent } from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { IconCheck, IconWorld } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { LANGUAGE_NAMES, SUPPORTED_LANGUAGES } from '../i18n'
import type { UiLanguage } from '../i18n'
import { useUiLanguage } from '../i18n/useUiLanguage'
import { FloatingNotice, FloatingNoticeStack } from './ui/FloatingNotice'
import { iconButtonClass } from './ui/IconButton'
import { LanguageFlag } from './ui/LanguageFlag'
import { Tooltip } from './ui/Tooltip'

const MENU_WIDTH = 248
const EDGE = 8
// How long a failure notice stays when the user does not close it
const NOTICE_MS = 12_000

interface MenuPanelProps {
  anchor: HTMLElement
  menuId: string
  current: UiLanguage
  onSelect: (language: UiLanguage) => void
  onClose: () => void
}

/**
 * The menu itself. Its CSS is in px on purpose (index.css .pv-langmenu), like the display-size popover: choosing a language
 * re-flows the whole page, and the menu keeps its size and place meanwhile. The position is taken once, when it opens.
 */
function MenuPanel({ anchor, menuId, current, onSelect, onClose }: MenuPanelProps) {
  const { t } = useTranslation('shell')
  const reduce = useReducedMotion() ?? false
  const ref = useRef<HTMLDivElement>(null)
  const [place] = useState(() => {
    const rect = anchor.getBoundingClientRect()
    const width = Math.min(MENU_WIDTH, document.documentElement.clientWidth - 2 * EDGE)
    const left = Math.min(Math.max(rect.right - width, EDGE), document.documentElement.clientWidth - width - EDGE)
    return { left, top: Math.round(rect.bottom + 10), width }
  })

  const items = () => [...(ref.current?.querySelectorAll<HTMLElement>('[role="menuitemradio"]') ?? [])]

  // Focus moves into the menu (onto the language in use); Esc and a click outside close it, the focus goes back to the button
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
        event.preventDefault()
        event.stopPropagation()
        onClose()
        anchor.focus()
        return
      case 'Tab':
        // A menu is left with Tab like with Esc: it closes and the focus returns to its button
        event.preventDefault()
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
      aria-label={t('header.language.menu')}
      className="pv-langmenu"
      style={{ left: place.left, top: place.top, width: place.width }}
      onKeyDown={onKeyDown}
      initial={reduce ? { opacity: 0 } : { opacity: 0, y: -6, scale: 0.98 }}
      animate={reduce ? { opacity: 1 } : { opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, transition: { duration: 0.1 } }}
      transition={{ duration: 0.16, ease: [0.22, 1, 0.36, 1] }}
    >
      {SUPPORTED_LANGUAGES.map((language) => {
        const checked = language.code === current
        return (
          <button
            key={language.code}
            type="button"
            role="menuitemradio"
            aria-checked={checked}
            lang={language.code}
            tabIndex={checked ? 0 : -1}
            className="pv-langmenu-item"
            onClick={() => onSelect(language.code)}
          >
            <LanguageFlag language={language.code} />
            <span className="pv-langmenu-name">{language.endonym}</span>
            <IconCheck size={16} stroke={3} className="pv-langmenu-check" aria-hidden="true" />
          </button>
        )
      })}
    </motion.div>,
    document.body
  )
}

/**
 * The language switch in the top bar of every page: a globe with the code of the language in use (DE / EN) as a small badge.
 * A click opens a menu that lists every language of the app (flag + its name in itself, from languages.json); choosing one switches
 * all texts instantly and saves the choice on the server (setting ui.language, see useUiLanguage). When saving fails the previous
 * language comes back and the reason is shown in the app's floating notice (laid over the page, so it moves nothing). The button
 * never changes its size: the badge is an overlay of a fixed width, the same for every code.
 */
export function LanguageMenu() {
  const { t } = useTranslation('shell')
  const { t: tc } = useTranslation()
  const { language, changeLanguage } = useUiLanguage()
  const [open, setOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const savingRef = useRef(false)
  const menuId = useId()
  const close = useCallback(() => setOpen(false), [])

  // The failure notice goes away by itself after a while
  useEffect(() => {
    if (!error) return
    const timer = window.setTimeout(() => setError(null), NOTICE_MS)
    return () => window.clearTimeout(timer)
  }, [error])

  const select = async (next: UiLanguage) => {
    setOpen(false)
    buttonRef.current?.focus()
    // The language in use stays; a second choice while the first is still being saved is ignored (it would race the first request)
    if (next === language || savingRef.current) return
    savingRef.current = true
    setError(null)
    try {
      await changeLanguage(next)
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : tc('unknownError'))
    } finally {
      savingRef.current = false
    }
  }

  return (
    <>
      <Tooltip label={t('header.language.label')} description={t('header.language.description')} disabled={open}>
        <button
          ref={buttonRef}
          type="button"
          data-hint="header-language"
          className={iconButtonClass()}
          aria-label={t('header.language.labelCurrent', { current: LANGUAGE_NAMES[language] })}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-controls={open ? menuId : undefined}
          onClick={() => setOpen((value) => !value)}
        >
          <span className="inline-flex items-center justify-center">
            <IconWorld size={18} />
          </span>
          <span className="pv-hlang-badge" aria-hidden="true">
            {language.toUpperCase()}
          </span>
        </button>
      </Tooltip>

      <AnimatePresence>
        {open && buttonRef.current && (
          <MenuPanel key="language-menu" anchor={buttonRef.current} menuId={menuId} current={language} onSelect={(next) => void select(next)} onClose={close} />
        )}
      </AnimatePresence>

      {/* In the body, not in the header: the header's backdrop filter would make "fixed" relative to the header */}
      {createPortal(
        <FloatingNoticeStack>
          {error && (
            <FloatingNotice key="language-error" tone="error" title={t('header.language.saveFailed')} message={error} onClose={() => setError(null)} />
          )}
        </FloatingNoticeStack>,
        document.body
      )}
    </>
  )
}
