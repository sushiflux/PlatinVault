import { useCallback, useEffect, useId, useMemo, useState, type ReactNode } from 'react'
import { Link, useLocation, useSearchParams } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { TrophyIcon } from '@phosphor-icons/react'
import {
  IconAlertTriangle,
  IconBook,
  IconBooks,
  IconChartBar,
  IconChevronRight,
  IconInfoCircle,
  IconLayoutDashboard,
  IconLayoutSidebarLeftCollapse,
  IconLayoutSidebarLeftExpand,
  IconSearch,
  IconSettings,
  IconTrophy,
  IconUser,
  IconX,
} from '@tabler/icons-react'
import { fetchGameDetail, fetchGames, fetchLibrarySummary, fetchSteamProfile } from '../../api/client'
import type { GameSummaryDto, SteamProfileDto } from '../../api/types'
import { failedChapterTabs } from '../ai/FailedChapter'
import { preloadCovers, showcaseQueryOptions } from '../cabinet/showcaseQuery'
import { Tooltip } from '../ui/Tooltip'
import { steamClientUrl, useOpenInSteamClient } from '../../utils/steamClient'
import {
  LIVE_TROPHIES_TAB_SLUG,
  LORE_TAB_SLUG,
  STATS_TAB_SLUG,
  buildSections,
  chapterItemsOf,
  filterTree,
  gameTabHref,
  resolveGameTab,
  tabParamToSlug,
  treeGames,
  type ChapterItem,
} from './navModel'

interface ShellNavProps {
  // Collapsed to an icon rail (desktop only; the drawer is always full)
  rail: boolean
  onToggleRail: () => void
  // Phone: the tree is a drawer; the toggle button then closes it instead
  drawer: boolean
  onCloseDrawer: () => void
  // Slug of the game page that is open (null elsewhere): its subtree unfolds
  currentSlug: string | null
  id?: string
}

// One entry of the tree. In the rail only the icon shows; its tooltip then names it (to the right of the rail).
function NavEntry({
  to,
  icon,
  label,
  current,
  rail,
  hint,
  trailing,
  expanded,
  onPointerEnter,
  onFocus,
  className = '',
}: {
  to: string
  icon: ReactNode
  label: ReactNode
  // The text of the tooltip and the accessible name in the rail
  current: boolean
  rail: boolean
  hint?: string
  trailing?: ReactNode
  expanded?: boolean
  onPointerEnter?: () => void
  onFocus?: () => void
  className?: string
}) {
  const link = (
    <Link
      to={to}
      className={`pv-ni ${className}`.trim()}
      aria-current={current ? 'page' : undefined}
      data-open={expanded ? 'true' : undefined}
      data-hint={hint}
      onPointerEnter={onPointerEnter}
      onFocus={onFocus}
    >
      <span className="pv-ni-icon" aria-hidden="true">
        {icon}
      </span>
      <span className="pv-ni-label">{label}</span>
      {trailing}
    </Link>
  )
  if (!rail || typeof label !== 'string') return link
  return (
    <Tooltip label={label} side="right" className="pv-ni-tip">
      {link}
    </Tooltip>
  )
}

function Cover({ game }: { game: GameSummaryDto }) {
  const [failed, setFailed] = useState(false)
  if (!game.coverUrl || failed) {
    return <span className="pv-ni-cover pv-ni-cover--empty" aria-hidden="true" />
  }
  return <img className="pv-ni-cover" src={game.coverUrl} alt="" loading="lazy" onError={() => setFailed(true)} />
}

function ChapterCount({ chapter }: { chapter: ChapterItem }) {
  const { t } = useTranslation('shell')
  if (chapter.total <= 0) return null
  return (
    <span className="pv-ni-count" data-done={chapter.checked >= chapter.total ? 'true' : undefined}>
      <span aria-hidden="true">
        {chapter.checked}/{chapter.total}
      </span>
      <span className="sr-only">{t('nav.chapterProgress', { checked: chapter.checked, total: chapter.total })}</span>
    </span>
  )
}

// The unfolded game: live trophies, statistics, the chapters under their group labels, Story & Lore - indented on a 1 px guide line
function GameSubtree({ slug, title, chapters, activeSlug, hasSteam }: { slug: string; title: string; chapters: ChapterItem[]; activeSlug: string; hasSteam: boolean }) {
  const { t } = useTranslation(['shell', 'game'])
  const { sections, headings } = buildSections(chapters)
  return (
    <ul className="pv-subtree" aria-label={t('nav.chapters', { game: title })} data-hint="game-nav">
      {hasSteam && (
        <li>
          <Link to={gameTabHref(slug, LIVE_TROPHIES_TAB_SLUG)} className="pv-ni" aria-current={activeSlug === LIVE_TROPHIES_TAB_SLUG ? 'page' : undefined} data-hint="game-live">
            <span className="pv-ni-icon" aria-hidden="true">
              <IconTrophy size={18} />
            </span>
            <span className="pv-ni-label">{t('nav.live')}</span>
          </Link>
        </li>
      )}
      {hasSteam && (
        <li>
          <Link to={gameTabHref(slug, STATS_TAB_SLUG)} className="pv-ni" aria-current={activeSlug === STATS_TAB_SLUG ? 'page' : undefined}>
            <span className="pv-ni-icon" aria-hidden="true">
              <IconChartBar size={18} />
            </span>
            <span className="pv-ni-label">{t('nav.stats')}</span>
          </Link>
        </li>
      )}
      {sections.map((section) => (
        <li key={section.key ?? 'all'} className="pv-subtree-group">
          {headings && section.key && <div className="pv-glabel">{t(`game:chapters.groups.${section.key}`)}</div>}
          <ul>
            {section.tabs.map(({ chapter }) => (
              <li key={chapter.slug}>
                <Link to={gameTabHref(slug, chapter.slug)} className="pv-ni pv-ni--chapter" aria-current={activeSlug === chapter.slug ? 'page' : undefined}>
                  <span className="pv-ni-label">{chapter.title}</span>
                  {chapter.failed ? (
                    <span className="pv-ni-failed">
                      <IconAlertTriangle size={16} aria-hidden="true" />
                      <span className="sr-only">{t('nav.failed')}</span>
                    </span>
                  ) : (
                    <ChapterCount chapter={chapter} />
                  )}
                </Link>
              </li>
            ))}
          </ul>
        </li>
      ))}
      <li>
        <Link to={gameTabHref(slug, LORE_TAB_SLUG)} className="pv-ni" aria-current={activeSlug === LORE_TAB_SLUG ? 'page' : undefined}>
          <span className="pv-ni-icon" aria-hidden="true">
            <IconBook size={18} />
          </span>
          <span className="pv-ni-label">{t('nav.lore')}</span>
        </Link>
      </li>
    </ul>
  )
}

function UserRow({ profile, rail }: { profile: SteamProfileDto | undefined; rail: boolean }) {
  const { t } = useTranslation('shell')
  const openInClient = useOpenInSteamClient()
  const [avatarFailed, setAvatarFailed] = useState(false)
  const avatarUrl = profile?.avatarUrl ?? null
  useEffect(() => setAvatarFailed(false), [avatarUrl])
  const name = profile?.personaName?.trim() || null
  const count = profile?.libraryGameCount ?? null
  if (!name && count === null && !avatarUrl) return <div className="pv-user pv-user--empty" />
  const countText = count === null ? null : t('steamChip.games', { count })
  const profileUrl = profile?.profileUrl ?? null
  const body = (
    <>
      {avatarUrl && !avatarFailed ? (
        <img className="pv-avatar" src={avatarUrl} alt="" width={32} height={32} referrerPolicy="no-referrer" onError={() => setAvatarFailed(true)} />
      ) : (
        <span className="pv-avatar pv-avatar--empty" aria-hidden="true">
          <IconUser size={18} />
        </span>
      )}
      <span className="pv-user-text">
        <span className="pv-user-name">{name ?? t('steamChip.fallbackLabel')}</span>
        {countText && <span className="pv-user-sub">{countText}</span>}
      </span>
    </>
  )
  const label = name ?? t('steamChip.fallbackLabel')
  const description = [
    profileUrl && name ? (openInClient ? t('steamChip.openInClient') : t('steamChip.openInBrowser')) : null,
    countText ? t('steamChip.libraryDescription', { games: countText }) : null,
  ]
    .filter(Boolean)
    .join(' · ')
  const inner =
    profileUrl && name ? (
      <a
        href={openInClient ? steamClientUrl(profileUrl) : profileUrl}
        {...(openInClient ? {} : { target: '_blank', rel: 'noreferrer' })}
        className="pv-user"
        aria-label={`${openInClient ? t('steamChip.linkClient', { name }) : t('steamChip.linkBrowser', { name })}${countText ? `, ${t('steamChip.linkLibrary', { games: countText })}` : ''}`}
      >
        {body}
      </a>
    ) : (
      <span className="pv-user">{body}</span>
    )
  return (
    <Tooltip label={label} description={description || undefined} side={rail ? 'right' : 'top'} className="pv-user-tip">
      {inner}
    </Tooltip>
  )
}

/** Quiet placeholder rows (48 px each, no text) for a list whose answer is still on its way. */
function SkeletonRows({ count, subtree = false }: { count: number; subtree?: boolean }) {
  return (
    <ul className={`pv-snav-skel${subtree ? ' pv-subtree' : ''}`} aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <li key={index}>
          <span className="pv-ni-skel" />
        </li>
      ))}
    </ul>
  )
}

/** The tree navigation of the app shell (left side; an icon rail when collapsed, a drawer on phones). */
export function ShellNav({ rail: railWanted, onToggleRail, drawer, onCloseDrawer, currentSlug, id }: ShellNavProps) {
  const { t } = useTranslation('shell')
  const location = useLocation()
  const [searchParams] = useSearchParams()
  const queryClient = useQueryClient()
  const searchId = useId()
  const [query, setQuery] = useState('')
  // The drawer always shows the full tree
  const rail = railWanted && !drawer

  const { data: games = [], isPending: gamesPending } = useQuery({ queryKey: ['games'], queryFn: fetchGames })
  const { data: librarySummary } = useQuery({ queryKey: ['library-summary'], queryFn: fetchLibrarySummary })
  const { data: profile } = useQuery({ queryKey: ['steam-profile'], queryFn: fetchSteamProfile, staleTime: 5 * 60_000 })
  // Same query as the game page: the open game's chapters come from the cache the page fills anyway
  const { data: game } = useQuery({
    queryKey: ['game', currentSlug ?? ''],
    queryFn: () => fetchGameDetail(currentSlug ?? ''),
    enabled: Boolean(currentSlug),
  })
  const openGame = game && currentSlug && game.slug === currentSlug ? game : undefined

  const failedIds = useMemo(() => new Set(failedChapterTabs(openGame).map((tab) => tab.id)), [openGame])
  const chapters = useMemo(() => chapterItemsOf(openGame, failedIds), [openGame, failedIds])
  const activeTab = currentSlug ? resolveGameTab(openGame, tabParamToSlug(searchParams.get('tab'))) : ''
  const tree = filterTree(treeGames(games, currentSlug), currentSlug, chapters, query)

  // Warm the trophy cabinet up (its data and first covers) before it is opened: once on idle, and on hover / focus of its entry
  const warmCabinet = useCallback(() => {
    void queryClient.prefetchQuery(showcaseQueryOptions).then(() => {
      const data = queryClient.getQueryData(showcaseQueryOptions.queryKey)
      if (data) void preloadCovers(data.games, 4000, 24)
    })
  }, [queryClient])
  useEffect(() => {
    const idle = window as Window & {
      requestIdleCallback?: (callback: () => void, options?: { timeout: number }) => number
      cancelIdleCallback?: (handle: number) => void
    }
    if (idle.requestIdleCallback) {
      const handle = idle.requestIdleCallback(warmCabinet, { timeout: 4000 })
      return () => idle.cancelIdleCallback?.(handle)
    }
    const timer = window.setTimeout(warmCabinet, 2000)
    return () => window.clearTimeout(timer)
  }, [warmCabinet])

  const path = location.pathname
  const toggleLabel = drawer ? t('nav.menuClose') : rail ? t('nav.expand') : t('nav.collapse')

  return (
    <nav id={id} className="pv-snav" data-rail={rail ? 'true' : undefined} data-drawer={drawer ? 'true' : undefined} aria-label={t('nav.label')}>
      <div className="pv-snav-top">
        {!rail && (
          <div className="pv-snav-search">
            <label htmlFor={searchId} className="sr-only">
              {t('nav.search.label')}
            </label>
            <IconSearch size={18} className="pv-snav-search-icon" aria-hidden="true" />
            <input
              id={searchId}
              type="search"
              className="pv-input pv-snav-search-input"
              placeholder={t('nav.search.placeholder')}
              value={query}
              autoComplete="off"
              onChange={(event) => setQuery(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Escape' && query) {
                  event.stopPropagation()
                  setQuery('')
                }
              }}
            />
            {query && (
              <button type="button" className="pv-snav-search-clear" aria-label={t('nav.search.clear')} onClick={() => setQuery('')}>
                <IconX size={16} aria-hidden="true" />
              </button>
            )}
          </div>
        )}
        <Tooltip label={toggleLabel} side={rail ? 'right' : 'bottom'}>
          <button
            type="button"
            className="pv-btn pv-icon-btn pv-icon-btn--ghost"
            aria-label={toggleLabel}
            aria-expanded={drawer ? true : !rail}
            aria-controls={id}
            onClick={drawer ? onCloseDrawer : onToggleRail}
          >
            {drawer ? <IconX size={20} /> : rail ? <IconLayoutSidebarLeftExpand size={20} /> : <IconLayoutSidebarLeftCollapse size={20} />}
          </button>
        </Tooltip>
      </div>

      <div className="pv-snav-list" data-pv-scroll="nav">
        <ul>
          <li>
            <NavEntry to="/" icon={<IconLayoutDashboard size={20} />} label={t('nav.overview')} current={path === '/'} rail={rail} />
          </li>
          {librarySummary && (
            <li>
              <NavEntry to="/library" icon={<IconBooks size={20} />} label={t('nav.library')} current={path === '/library'} rail={rail} />
            </li>
          )}
        </ul>

        <div className="pv-glabel" role="presentation">
          {t('nav.myGames')}
        </div>
        {gamesPending ? (
          // The rows of the answer keep their room while it is on its way, so the entries below never jump
          <SkeletonRows count={4} />
        ) : tree.games.length === 0 ? (
          !rail && <p className="pv-snav-empty">{query ? t('nav.search.none', { query }) : t('nav.noFavorites')}</p>
        ) : (
          <ul>
            {tree.games.map((entry) => {
              const open = entry.slug === currentSlug
              return (
                <li key={entry.slug}>
                  <NavEntry
                    to={`/games/${encodeURIComponent(entry.slug)}`}
                    icon={<Cover game={entry} />}
                    label={entry.title}
                    current={rail && open}
                    rail={rail}
                    expanded={open}
                    className={`pv-ni--game${open ? ' pv-ni--open' : ''}`}
                    trailing={
                      <span className="pv-ni-chev" aria-hidden="true">
                        <IconChevronRight size={18} />
                      </span>
                    }
                  />
                  {open && !rail && openGame && (
                    <GameSubtree slug={entry.slug} title={entry.title} chapters={tree.chapters} activeSlug={activeTab} hasSteam={Boolean(openGame.steamAppId)} />
                  )}
                  {open && !rail && !openGame && <SkeletonRows count={6} subtree />}
                </li>
              )
            })}
          </ul>
        )}

        <div className="pv-nsep" role="presentation" />
        <ul>
          <li>
            <NavEntry
              to="/cabinet"
              icon={<TrophyIcon size={20} weight="duotone" />}
              label={t('nav.cabinet')}
              current={path === '/cabinet'}
              rail={rail}
              hint="dashboard-cabinet"
              onPointerEnter={warmCabinet}
              onFocus={warmCabinet}
            />
          </li>
        </ul>
      </div>

      <div className="pv-snav-foot">
        <NavEntry to="/settings" icon={<IconSettings size={20} />} label={t('nav.settings')} current={path === '/settings'} rail={rail} hint="settings" />
        <div className="pv-snav-inforow">
          <UserRow profile={profile} rail={rail} />
          <Tooltip label={t('nav.info')} side={rail ? 'right' : 'top'}>
            <Link to="/about" className="pv-btn pv-icon-btn pv-icon-btn--ghost" aria-label={t('nav.info')} aria-current={path === '/about' ? 'page' : undefined}>
              <IconInfoCircle size={20} />
            </Link>
          </Tooltip>
        </div>
      </div>
    </nav>
  )
}
