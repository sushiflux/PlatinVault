import { useState } from 'react'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import {
  IconDeviceGamepad2,
  IconLoader2,
  IconAdjustmentsHorizontal,
  IconSearch,
  IconStarFilled,
  IconTrophy,
} from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { fixedT } from '../i18n'
import { formatNumber, localeFor, useFormat } from '../i18n/format'
import { LangStack } from './ui/LangStack'
import { TileSizeSlider, type TileDensity } from './GameTiles'
import { TierIcon } from './ui/TierIcon'
import { Tooltip } from './ui/Tooltip'

// Shared building blocks of the dashboard controls. The hero card and the sticky sub-bar render the SAME pieces from
// the SAME state (kept in DashboardPage), so the wording and behaviour can never drift apart.

export type DashboardView = 'favorites' | 'all'

export interface DashboardFiguresProps {
  // Full text and a short form for the compact bar ("584 games in the library" / "584 games")
  gamesText: string
  gamesTextShort: string
  unlocked: number
  total: number
  percent: number
  completed: number
  // Set while the library is evaluated in the background (the figures grow)
  scanning?: { done: number; total: number } | null
}

// The three figure badges (games, trophies, platinum) plus the "library is being evaluated" hint
export function DashboardFigures({
  gamesText,
  gamesTextShort,
  unlocked,
  total,
  percent,
  completed,
  scanning,
  compact = false,
}: DashboardFiguresProps & { compact?: boolean }) {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const figures = { unlocked: fmt.number(unlocked), total: fmt.number(total), percent: fmt.percent(percent) }
  const trophiesFull = t('figures.trophies', figures)
  const platinFull = t('figures.platinum', { count: completed })
  const scanFull = scanning ? t('figures.scanning', { done: fmt.number(scanning.done), total: fmt.number(scanning.total) }) : ''

  if (!compact) {
    return (
      <>
        <span className="stat-pill crystal">
          <IconDeviceGamepad2 size={15} />
          {gamesText}
        </span>
        <span className="stat-pill safe">
          <IconTrophy size={15} />
          {trophiesFull}
        </span>
        <span className="stat-pill gold">
          <TierIcon tier="Platinum" size={15} />
          {platinFull}
        </span>
        {/* While the library is evaluated in the background, the figures grow - say so instead of looking final */}
        {scanning && (
          <span className="stat-pill">
            <IconLoader2 size={15} className="animate-spin" />
            {scanFull}
          </span>
        )}
      </>
    )
  }

  // Compact: the icon names the figure, the tooltip carries the full sentence
  return (
    <>
      <Tooltip label={gamesText} side="bottom">
        <span className="stat-pill crystal" aria-label={gamesText}>
          <IconDeviceGamepad2 size={14} />
          {gamesTextShort}
        </span>
      </Tooltip>
      <Tooltip label={trophiesFull} side="bottom">
        <span className="stat-pill safe" aria-label={trophiesFull}>
          <IconTrophy size={14} />
          {t('figures.trophiesShort', figures)}
        </span>
      </Tooltip>
      <Tooltip label={platinFull} side="bottom">
        <span className="stat-pill gold" aria-label={platinFull}>
          <TierIcon tier="Platinum" size={14} />
          {t('figures.platinumShort', { n: fmt.number(completed) })}
        </span>
      </Tooltip>
      {scanning && (
        <Tooltip label={scanFull} side="bottom">
          <span className="stat-pill" aria-label={scanFull}>
            <IconLoader2 size={14} className="animate-spin" />
            {fmt.number(scanning.done)} / {fmt.number(scanning.total)}
          </span>
        </Tooltip>
      )}
    </>
  )
}

interface ViewSwitchProps {
  view: DashboardView
  onSelect: (view: DashboardView) => void
  favoritesCount: number
  libraryCount: number | undefined
  // Sub-bar: below 1280 px only the icon and the number are shown (the full name stays as aria-label)
  compact?: boolean
}

// "Favourites (n)" | "All games (n)": the starred tiles or the whole Steam library
export function ViewSwitch({ view, onSelect, favoritesCount, libraryCount, compact = false }: ViewSwitchProps) {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const libraryText = libraryCount === undefined ? '' : fmt.number(libraryCount)
  const favoritesLabel = t('controls.favorites', { n: fmt.number(favoritesCount) })
  const allLabel = t('controls.allGames', { n: libraryText })
  // The compact switch is as wide as its content: both languages' labels are laid on top of each other, so the language
  // never changes its width (the full-width switch in the hero splits its width evenly anyway)
  const stacked = (key: 'controls.favorites' | 'controls.allGames', value: number | undefined) => (
    <LangStack
      align="center"
      render={(language) => fixedT(language, 'dashboard')(key, { n: value === undefined ? '' : formatNumber(value, 0, { locale: localeFor(language) }) })}
    />
  )
  return (
    <div className={`pv-segmented ${compact ? '' : 'w-full'}`} role="group" aria-label={t('controls.viewGroup')} data-hint={compact ? undefined : 'dashboard-add'}>
      <button
        type="button"
        aria-pressed={view === 'favorites'}
        aria-label={compact ? favoritesLabel : undefined}
        onClick={() => onSelect('favorites')}
        className="flex-1 justify-center"
      >
        <IconStarFilled size={14} />
        {compact ? (
          <>
            <span className="max-xl:hidden">{stacked('controls.favorites', favoritesCount)}</span>
            <span className="xl:hidden">{fmt.number(favoritesCount)}</span>
          </>
        ) : (
          <span>{favoritesLabel}</span>
        )}
      </button>
      <button
        type="button"
        aria-pressed={view === 'all'}
        aria-label={compact ? allLabel : undefined}
        onClick={() => onSelect('all')}
        className="flex-1 justify-center"
      >
        <IconDeviceGamepad2 size={14} />
        {compact ? (
          <>
            <span className="max-xl:hidden">{stacked('controls.allGames', libraryCount)}</span>
            <span className="xl:hidden">{libraryText}</span>
          </>
        ) : (
          <span>{allLabel}</span>
        )}
      </button>
    </div>
  )
}

interface DashboardSubBarProps {
  // True once the hero (with its own search / switch / figures) has scrolled out of view
  open: boolean
  search: string
  onSearchChange: (value: string) => void
  hasLibrary: boolean
  view: DashboardView
  onSelectView: (view: DashboardView) => void
  favoritesCount: number
  libraryCount: number | undefined
  figures: DashboardFiguresProps
  density: TileDensity
  onDensityChange: (value: TileDensity) => void
}

// Compact bar directly under the sticky header while the hero's controls are out of view. It lays OVER the page (a
// zero-height sticky anchor with an absolutely positioned bar), so showing or hiding it never moves the grid below.
export function DashboardSubBar({
  open,
  search,
  onSearchChange,
  hasLibrary,
  view,
  onSelectView,
  favoritesCount,
  libraryCount,
  figures,
  density,
  onDensityChange,
}: DashboardSubBarProps) {
  const { t } = useTranslation('dashboard')
  const reduce = useReducedMotion() ?? false
  // While the user is typing / dragging in the bar it stays, even if filtering shortened the page so far that the hero
  // is visible again - unmounting the focused field would drop the cursor in the middle of a word
  const [focusWithin, setFocusWithin] = useState(false)
  const shown = open || focusWithin
  // Phone only: the slider row is opened by the tile size toggle (from 768 px it sits in the first row)
  const [sliderOpen, setSliderOpen] = useState(false)

  return (
    <div className="pv-subbar-anchor">
      <AnimatePresence>
        {shown && (
          <motion.div
            key="subbar"
            className="pv-subbar"
            role="region"
            aria-label={t('controls.subBar')}
            initial={reduce ? { opacity: 0 } : { opacity: 0, y: '-100%' }}
            animate={{ opacity: 1, y: 0 }}
            exit={reduce ? { opacity: 0 } : { opacity: 0, y: '-60%' }}
            transition={{ duration: reduce ? 0.12 : 0.28, ease: [0.22, 1, 0.36, 1] }}
            onFocus={() => setFocusWithin(true)}
            onBlur={(e) => {
              if (!e.currentTarget.contains(e.relatedTarget)) setFocusWithin(false)
            }}
          >
            <div className="pv-subbar-inner">
              <div className="pv-subbar-search relative">
                <IconSearch
                  size={15}
                  className="absolute left-3 top-1/2 -translate-y-1/2"
                  style={{ color: 'var(--text-muted)' }}
                />
                <input
                  type="search"
                  value={search}
                  onChange={(e) => onSearchChange(e.target.value)}
                  placeholder={t('controls.searchPlaceholder')}
                  aria-label={t('controls.searchLabel')}
                  className="pv-input pv-input-icon w-full"
                />
              </div>

              {hasLibrary && (
                <div className="pv-subbar-switch">
                  <ViewSwitch
                    view={view}
                    onSelect={onSelectView}
                    favoritesCount={favoritesCount}
                    libraryCount={libraryCount}
                    compact
                  />
                </div>
              )}

              <button
                type="button"
                className="pv-btn pv-icon-btn pv-subbar-toggle md:hidden"
                aria-expanded={sliderOpen}
                aria-controls="dashboard-subbar-slider"
                aria-label={t('controls.changeTileSize')}
                onClick={() => setSliderOpen((value) => !value)}
              >
                <IconAdjustmentsHorizontal size={17} />
              </button>

              <div className="pv-subbar-figures">
                <DashboardFigures {...figures} compact />
              </div>

              <div id="dashboard-subbar-slider" className="pv-subbar-slider" data-open={sliderOpen}>
                <TileSizeSlider value={density} onChange={onDensityChange} compact />
              </div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  )
}
