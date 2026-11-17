import { IconLoader2, IconTrophy } from '@tabler/icons-react'
import { useTranslation } from 'react-i18next'
import { fixedT } from '../i18n'
import { formatNumber, localeFor, useFormat } from '../i18n/format'
import { LangStack } from './ui/LangStack'
import { ProgressBar } from './ui/ProgressBar'
import { Tooltip } from './ui/Tooltip'

// Shared building blocks of the dashboard controls: the three calm figures and the view switch. The look is in styles/dashboard.css.

export type DashboardView = 'favorites' | 'all'

export interface DashboardFiguresProps {
  // What the first figure counts: the starred games (overview) or the whole Steam library (all games)
  mode: 'favorites' | 'library'
  games: number
  unlocked: number
  total: number
  percent: number
  completed: number
  // Set while the library is evaluated in the background (the figures grow)
  scanning?: { done: number; total: number } | null
}

// Three calm figures side by side, hairlines between them: games, Steam trophies (with a thin progress line), games on platinum.
// Progress = Steam trophies only; the figures follow the chosen view.
export function DashboardFigures({ mode, games, unlocked, total, percent, completed, scanning }: DashboardFiguresProps) {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const scanText = scanning ? t('figures.scanning', { done: fmt.number(scanning.done), total: fmt.number(scanning.total) }) : ''

  return (
    <div className="pv-dfigs">
      <div className="pv-dfig">
        <p className="pv-dfig-v">{fmt.number(games)}</p>
        <p className="pv-dfig-l">{mode === 'library' ? t('figures.libraryLabel', { count: games }) : t('figures.favoritesLabel', { count: games })}</p>
      </div>
      <div className="pv-dfig">
        <p className="pv-dfig-v">
          {fmt.number(unlocked)} / {fmt.number(total)}
        </p>
        <p className="pv-dfig-l">
          <span>{t('figures.trophiesLabel', { percent: fmt.percent(percent) })}</span>
          {/* While the library is evaluated in the background the figures grow - say so instead of looking final. The line is the
              same height with or without it (the spinner sits at the end of the label line). */}
          {scanning && (
            <Tooltip label={scanText} side="bottom" className="pv-dfig-scan">
              <span tabIndex={0} aria-label={scanText}>
                <IconLoader2 size={14} className="animate-spin" aria-hidden="true" />
                <span aria-hidden="true">
                  {fmt.number(scanning.done)} / {fmt.number(scanning.total)}
                </span>
              </span>
            </Tooltip>
          )}
        </p>
        <ProgressBar percent={percent} label={t('header.overall.progress')} />
      </div>
      <div className="pv-dfig">
        <p className="pv-dfig-v">
          <IconTrophy size={22} aria-hidden="true" />
          {fmt.number(completed)}
        </p>
        <p className="pv-dfig-l">{t('figures.platinumLabel', { count: completed })}</p>
      </div>
    </div>
  )
}

interface ViewSwitchProps {
  view: DashboardView
  onSelect: (view: DashboardView) => void
  favoritesCount: number
  libraryCount: number | undefined
  // A copy of the switch (the compact bar) is no coach-mark anchor
  copy?: boolean
}

// "Favourites (n)" | "All games (n)": one connected bar. Both languages' labels are laid on top of each other, so the bar is as wide
// as its longest version and switching the language never moves the search or the slider beside it.
export function ViewSwitch({ view, onSelect, favoritesCount, libraryCount, copy = false }: ViewSwitchProps) {
  const { t } = useTranslation('dashboard')
  const stacked = (key: 'controls.favorites' | 'controls.allGames', value: number | undefined) => (
    <LangStack
      align="center"
      render={(language) => fixedT(language, 'dashboard')(key, { n: value === undefined ? '' : formatNumber(value, 0, { locale: localeFor(language) }) })}
    />
  )
  return (
    <div className="pv-segmented pv-dash-switch" role="group" aria-label={t('controls.viewGroup')} data-hint={copy ? undefined : 'dashboard-add'}>
      <button type="button" aria-pressed={view === 'favorites'} onClick={() => onSelect('favorites')}>
        {stacked('controls.favorites', favoritesCount)}
      </button>
      <button type="button" aria-pressed={view === 'all'} onClick={() => onSelect('all')}>
        {stacked('controls.allGames', libraryCount)}
      </button>
    </div>
  )
}
