import { useEffect, useRef, useState } from 'react'
import { IconChevronLeft, IconChevronRight, IconListDetails, IconX } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { Tooltip } from '../ui/Tooltip'
import { ChapterTabs, type ChapterItem } from './ChapterTabs'
import { NavFoot } from './NavFoot'
import './gamenav.css'

// Under this width the chapters are a drawer opened by the "Kapitel" button of the trophy bar, above it a side panel (same media query as gamenav.css)
export const DRAWER_QUERY = '(max-width: 767px)'
// The id of the "Kapitel" button, so closing the drawer can hand the focus back to it
export const CHAPTERS_BUTTON_ID = 'pv-chapters-button'

interface ChapterSidebarProps {
  chapters: ChapterItem[]
  activeSlug: string | null
  onSelect: (slug: string) => void
  // Phone: the nav is a drawer that slides in over the page
  drawer: boolean
  drawerOpen: boolean
  onDrawerClose: () => void
  // Desktop: collapsed to numbers (remembered by the page)
  collapsed: boolean
  onToggleCollapsed: () => void
}

const FOCUSABLE = 'button:not([disabled]), [href], [tabindex]:not([tabindex="-1"])'

/**
 * The chapter navigation of the game page: a pinned panel on the left of the content (sticky below the header and the trophy
 * bar, collapsible to numbers), and on a phone a drawer that slides in from the left. The drawer closes on Esc, a click on the
 * dimmed page and after a chapter was chosen, keeps the focus inside while it is open and hands it back to the "Kapitel" button.
 * The foot of the panel is NavFoot: the info block (name, version, credit, copyright, licences), in the collapsed panel one info
 * symbol that opens a card with the same content.
 */
export function ChapterSidebar({ chapters, activeSlug, onSelect, drawer, drawerOpen, onDrawerClose, collapsed, onToggleCollapsed }: ChapterSidebarProps) {
  const { t } = useTranslation('game')
  const navRef = useRef<HTMLElement>(null)
  const open = drawer && drawerOpen
  // The info card and the licence viewer (opened from the foot) have their own Esc and Tab handling: the drawer steps aside meanwhile
  const [overlayOpen, setOverlayOpen] = useState(false)

  const closeDrawer = () => {
    onDrawerClose()
    document.getElementById(CHAPTERS_BUTTON_ID)?.focus()
  }

  // Drawer open: the focus moves in (the active chapter), Tab stays inside, Esc closes
  useEffect(() => {
    if (!open || overlayOpen) return
    const nav = navRef.current
    if (!nav) return
    // Coming back from the info card or the licence viewer the focus is handed back to the symbol that opened it: leave it there
    const target = nav.contains(document.activeElement) ? null : (nav.querySelector<HTMLElement>('[role="tab"][tabindex="0"]') ?? nav.querySelector<HTMLElement>(FOCUSABLE))
    target?.focus()
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation()
        onDrawerClose()
        document.getElementById(CHAPTERS_BUTTON_ID)?.focus()
        return
      }
      if (event.key !== 'Tab') return
      const items = [...nav.querySelectorAll<HTMLElement>(FOCUSABLE)].filter((element) => element.offsetParent !== null)
      const first = items[0]
      const last = items[items.length - 1]
      if (!first || !last) return
      if (!nav.contains(document.activeElement)) {
        event.preventDefault()
        first.focus()
      } else if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', onKeyDown, true)
    return () => document.removeEventListener('keydown', onKeyDown, true)
  }, [open, overlayOpen, onDrawerClose])

  const choose = (slug: string) => {
    onSelect(slug)
    if (drawer) closeDrawer()
  }

  const collapseLabel = collapsed ? t('nav.expandSidebar') : t('nav.collapseSidebar')

  // A game without a guide has no chapters: no empty panel
  if (chapters.length === 0) return null

  return (
    <>
      {open && <div className="pv-gdrawer-backdrop no-print" aria-hidden="true" onClick={closeDrawer} />}
      <nav
        ref={navRef}
        className="pv-gnav no-print"
        aria-label={t('nav.region')}
        data-hint={drawer ? undefined : 'game-nav'}
        data-open={drawer ? String(open) : undefined}
        data-collapsed={!drawer && collapsed ? 'true' : undefined}
        // A closed drawer is out of reach for the keyboard and for screen readers
        inert={drawer && !open ? true : undefined}
      >
        <div className="pv-gnav-head">
          <span className="pv-gnav-title">{t('nav.title')}</span>
          {drawer ? (
            <button type="button" className="pv-gnav-icon-btn" aria-label={t('nav.closeChapters')} onClick={closeDrawer}>
              <IconX size={16} aria-hidden="true" />
            </button>
          ) : (
            <Tooltip label={collapseLabel} side="bottom">
              <button type="button" className="pv-gnav-icon-btn" aria-label={collapseLabel} aria-expanded={!collapsed} onClick={onToggleCollapsed}>
                {collapsed ? <IconChevronRight size={16} aria-hidden="true" /> : <IconChevronLeft size={16} aria-hidden="true" />}
              </button>
            </Tooltip>
          )}
        </div>
        <ChapterTabs chapters={chapters} activeSlug={activeSlug} onSelect={choose} collapsed={!drawer && collapsed} />
        <NavFoot drawer={drawer} collapsed={!drawer && collapsed} onOpenChange={setOverlayOpen} />
      </nav>
    </>
  )
}

/** The "Kapitel" button of the trophy bar on a phone: opens the drawer. */
export function ChapterDrawerButton({ expanded, onClick }: { expanded: boolean; onClick: () => void }) {
  const { t } = useTranslation('game')
  return (
    <Tooltip label={t('nav.openChapters')} description={t('nav.openChaptersDescription')}>
      <button id={CHAPTERS_BUTTON_ID} data-hint="game-nav" type="button" className="pv-gnav-open-btn" aria-haspopup="dialog" aria-expanded={expanded} onClick={onClick}>
        <IconListDetails size={16} aria-hidden="true" />
        <span>{t('nav.openChapters')}</span>
      </button>
    </Tooltip>
  )
}
