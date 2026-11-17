import { useCallback, useEffect, useId, useRef, useState } from 'react'
import type { FocusEvent, KeyboardEvent as ReactKeyboardEvent, ReactNode, RefObject } from 'react'
import { createPortal } from 'react-dom'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { IconAdjustmentsHorizontal, IconSearch, IconX } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import type { DashboardSortView, SortChoice } from '../utils/dashboardSort'
import { useScrollHandoff } from '../utils/useScrollHandoff'
import { useMediaQuery } from '../utils/useMediaQuery'
import { DashboardSortControl } from './DashboardSortControl'
import { ViewSwitch, type DashboardView } from './DashboardControls'
import { TileSizeSlider, type TileDensity } from './GameTiles'
import { iconButtonClass } from './ui/IconButton'
import { Tooltip } from './ui/Tooltip'

// Below these widths the bar moves its less important parts: under 1120 px the tile size goes into the "view" popover and the search becomes an
// icon that unfolds; under 768 px the view switch goes into the popover as well. (The bar spans the whole window.)
const MID_QUERY = '(max-width: 1119px)'
const NARROW_QUERY = '(max-width: 767px)'
const EDGE = 8
const POPOVER_WIDTH = 320

export interface DashboardCompactBarProps {
  search: string
  onSearch: (value: string) => void
  hasLibrary: boolean
  view: DashboardView
  onSelectView: (view: DashboardView) => void
  favoritesCount: number
  libraryCount: number | undefined
  density: TileDensity
  onDensity: (value: TileDensity) => void
  sortView: DashboardSortView
  sortChoice: SortChoice
  sortLoading: boolean
  onSortChange: (choice: SortChoice) => void
  // True while focus is inside the bar or one of its popovers is open: the page keeps the bar up then, whatever the scroll position
  onEngagedChange: (engaged: boolean) => void
  // The hand-over from the controls row (useScrollHandoff): the page writes its scroll progress to this element's style. `interactive` = the
  // copy is (nearly) fully there; before that it is only a fading shadow of the row and cannot be reached
  barRef: (element: HTMLElement | null) => void
  interactive: boolean
}

interface PopoverProps {
  anchor: HTMLElement
  id: string
  title: string
  onClose: (returnFocus: boolean) => void
  children: ReactNode
}

/** A small non-modal panel under its button (right-aligned): the view switch and the tile size when the bar is too narrow for them. */
function OptionsPopover({ anchor, id, title, onClose, children }: PopoverProps) {
  const reduce = useReducedMotion() ?? false
  const ref = useRef<HTMLDivElement>(null)
  const [place] = useState(() => {
    const rect = anchor.getBoundingClientRect()
    const viewport = document.documentElement.clientWidth
    const width = Math.min(POPOVER_WIDTH, viewport - 2 * EDGE)
    return { left: Math.min(Math.max(rect.right - width, EDGE), viewport - width - EDGE), top: Math.round(rect.bottom + 8), width }
  })

  // Focus moves to the first control; a press outside closes the popover
  useEffect(() => {
    ref.current?.querySelector<HTMLElement>('button, input')?.focus()
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node
      if (ref.current?.contains(target) || anchor.contains(target)) return
      onClose(false)
    }
    document.addEventListener('pointerdown', onPointerDown, true)
    return () => document.removeEventListener('pointerdown', onPointerDown, true)
  }, [anchor, onClose])

  const onKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Escape') return
    event.preventDefault()
    event.stopPropagation()
    onClose(true)
  }

  return createPortal(
    <motion.div
      ref={ref}
      id={id}
      role="dialog"
      aria-label={title}
      className="pv-cbar-pop"
      style={{ left: place.left, top: place.top, width: place.width }}
      onKeyDown={onKeyDown}
      initial={reduce ? { opacity: 0 } : { opacity: 0, y: -6, scale: 0.98 }}
      animate={reduce ? { opacity: 1 } : { opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, transition: { duration: 0.1 } }}
      transition={{ duration: 0.16, ease: [0.22, 1, 0.36, 1] }}
    >
      {children}
    </motion.div>,
    document.body
  )
}

/**
 * The controls of the overview and of "All games" as one compact bar under the header: search, favourites / all games, tile size and the
 * sort control. It shows up while the controls row of the page has scrolled away (the page decides, see DashboardPage) and works on the same
 * state as the originals. Narrow windows fold the less important parts away (see the queries above); nothing is cut.
 */
export function DashboardCompactBar(props: DashboardCompactBarProps) {
  const { t } = useTranslation('dashboard')
  const { search, onSearch, hasLibrary, view, onSelectView, favoritesCount, libraryCount, density, onDensity, sortView, sortChoice, sortLoading, onSortChange, onEngagedChange, barRef, interactive } = props
  const mid = useMediaQuery(MID_QUERY)
  const narrow = useMediaQuery(NARROW_QUERY)

  // Own ids: the page's originals keep theirs, and no id exists twice
  const searchId = useId()
  const sizeId = useId()
  const popoverId = useId()

  const [focusWithin, setFocusWithin] = useState(false)
  const [sortOpen, setSortOpen] = useState(false)
  const [optionsOpen, setOptionsOpen] = useState(false)
  const [searchOpen, setSearchOpen] = useState(false)
  const optionsRef = useRef<HTMLButtonElement>(null)
  const searchButtonRef = useRef<HTMLButtonElement>(null)
  const searchInputRef = useRef<HTMLInputElement>(null)

  const engaged = focusWithin || sortOpen || optionsOpen
  useEffect(() => {
    onEngagedChange(engaged)
  }, [engaged, onEngagedChange])
  // When the bar is taken hold of or let go it eases to where the scroll position wants it instead of snapping: the transition is only
  // switched on for that moment, so the scroll-linked fade itself has no lag. The flag is derived in the SAME render in which `engaged`
  // flips (state adjusted while rendering), so the transition property is already there in the style change that moves the opacity; an effect
  // would set it one commit too late and the opacity would jump.
  const [easing, setEasing] = useState(false)
  const [engagedBefore, setEngagedBefore] = useState(false)
  if (engagedBefore !== engaged) {
    setEngagedBefore(engaged)
    setEasing(true)
  }
  useEffect(() => {
    if (!easing) return
    const timer = window.setTimeout(() => setEasing(false), 260)
    return () => window.clearTimeout(timer)
  }, [easing, engaged])
  // The page must not stay "engaged" when the bar goes away
  useEffect(() => () => onEngagedChange(false), [onEngagedChange])

  const onFocus = () => setFocusWithin(true)
  const onBlur = (event: FocusEvent<HTMLDivElement>) => {
    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setFocusWithin(false)
  }

  const closeOptions = useCallback((returnFocus: boolean) => {
    setOptionsOpen(false)
    if (returnFocus) optionsRef.current?.focus()
  }, [])

  const collapsedSearch = mid && !searchOpen
  // The unfolded search takes the whole row (a close button at its end)
  const searchTakesRow = mid && searchOpen

  const searchField = (
    <div className="pv-dash-search">
      <label htmlFor={searchId} className="sr-only">
        {t('hero.searchLabel')}
      </label>
      <IconSearch size={18} aria-hidden="true" />
      <input
        id={searchId}
        ref={searchInputRef}
        type="search"
        value={search}
        onChange={(event) => onSearch(event.target.value)}
        placeholder={t('hero.searchPlaceholder')}
        className="pv-input pv-input-icon"
      />
    </div>
  )

  const viewSwitch = hasLibrary ? <ViewSwitch copy view={view} onSelect={onSelectView} favoritesCount={favoritesCount} libraryCount={libraryCount} /> : null
  const sizeSlider = <TileSizeSlider copy id={sizeId} value={density} onChange={onDensity} />
  // What the popover holds: the view switch only when the bar is too narrow for it
  const popoverHasView = narrow && viewSwitch !== null
  const hasOptions = mid

  return (
    <div
      ref={barRef}
      role="group"
      aria-label={t('compact.label')}
      className="pv-dash-cbar no-print"
      data-narrow={narrow ? 'true' : undefined}
      data-engaged={engaged ? 'true' : undefined}
      data-easing={easing ? 'true' : undefined}
      // Not reachable (no focus, no clicks) while it is only a fading shadow of the row
      inert={!interactive && !engaged}
      onFocus={onFocus}
      onBlur={onBlur}
    >
      <div className="pv-dash-cbar-in">
        {searchTakesRow ? (
          <>
            {searchField}
            <Tooltip label={t('compact.searchClose')}>
              <button
                type="button"
                className={iconButtonClass()}
                aria-label={t('compact.searchClose')}
                onClick={() => {
                  setSearchOpen(false)
                  // Focus must not be lost with the field
                  requestAnimationFrame(() => searchButtonRef.current?.focus())
                }}
              >
                <IconX size={20} />
              </button>
            </Tooltip>
          </>
        ) : (
          <>
            {collapsedSearch ? (
              <Tooltip label={t('compact.searchOpen')}>
                <button
                  ref={searchButtonRef}
                  type="button"
                  className={`${iconButtonClass()} pv-cbar-search-btn`}
                  aria-label={t('compact.searchOpen')}
                  aria-expanded={false}
                  // A filter is active: the thin accent line says so while the field is folded
                  data-active={search ? 'true' : undefined}
                  onClick={() => {
                    setSearchOpen(true)
                    requestAnimationFrame(() => searchInputRef.current?.focus())
                  }}
                >
                  <IconSearch size={20} />
                </button>
              </Tooltip>
            ) : (
              searchField
            )}
            {!narrow && viewSwitch}
            {!mid && sizeSlider}
            <DashboardSortControl key={sortView} view={sortView} choice={sortChoice} loading={sortLoading} onChange={onSortChange} onOpenChange={setSortOpen} />
            {hasOptions && (
              <>
                <Tooltip label={t('compact.options')} disabled={optionsOpen}>
                  <button
                    ref={optionsRef}
                    type="button"
                    className={iconButtonClass()}
                    aria-label={t('compact.options')}
                    aria-haspopup="dialog"
                    aria-expanded={optionsOpen}
                    aria-controls={optionsOpen ? popoverId : undefined}
                    onClick={() => setOptionsOpen((value) => !value)}
                  >
                    <IconAdjustmentsHorizontal size={20} />
                  </button>
                </Tooltip>
                <AnimatePresence>
                  {optionsOpen && optionsRef.current && (
                    <OptionsPopover key="options" anchor={optionsRef.current} id={popoverId} title={t('compact.optionsTitle')} onClose={closeOptions}>
                      {popoverHasView && viewSwitch}
                      {sizeSlider}
                    </OptionsPopover>
                  )}
                </AnimatePresence>
              </>
            )}
          </>
        )}
      </div>
    </div>
  )
}

export type DashboardCompactBarHostProps = Omit<DashboardCompactBarProps, 'onEngagedChange' | 'barRef' | 'interactive'> & {
  // The controls row of the page the bar takes over from
  rowRef: RefObject<HTMLElement | null>
}

/**
 * Owns everything that changes during the hand-over from the controls row (the scroll phase, engaged, lingering) and mounts the bar. These
 * states live HERE and not in the page: a phase or engage change re-renders only the bar, never the page's tiles (hundreds in "All games").
 * Letting go sets lingering in the same state update in which engagement ends, so the bar never unmounts for a render (which would make it
 * come back at opacity 0 and snap): it eases out first and unmounts afterwards.
 */
export function DashboardCompactBarHost({ rowRef, ...props }: DashboardCompactBarHostProps) {
  const reduce = useReducedMotion() ?? false
  const handoff = useScrollHandoff(rowRef, reduce)
  const [engaged, setEngaged] = useState(false)
  const [lingers, setLingers] = useState(false)
  const engagedRef = useRef(false)
  const onEngagedChange = useCallback((next: boolean) => {
    if (engagedRef.current === next) return
    engagedRef.current = next
    setEngaged(next)
    setLingers(!next)
  }, [])
  useEffect(() => {
    if (!lingers) return
    const timer = window.setTimeout(() => setLingers(false), 300)
    return () => window.clearTimeout(timer)
  }, [lingers])

  if (handoff.phase === 'off' && !engaged && !lingers) return null
  return <DashboardCompactBar {...props} onEngagedChange={onEngagedChange} barRef={handoff.barRef} interactive={handoff.phase === 'on'} />
}
