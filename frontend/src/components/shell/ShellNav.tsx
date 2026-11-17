import { useMemo, useState, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  IconBook,
  IconCoins,
  IconCompass,
  IconListDetails,
  IconLayoutSidebarLeftCollapse,
  IconLayoutSidebarLeftExpand,
  IconLockOpen,
  IconMapSearch,
  IconPuzzle,
  IconSwords,
  IconUsers,
  IconWorld,
  IconX,
} from '@tabler/icons-react'
import { fetchGameDetail } from '../../api/client'
import { failedChapterTabs } from '../ai/FailedChapter'
import { Tooltip } from '../ui/Tooltip'
import { TruncatedText } from '../ui/TruncatedText'
import { ChapterTrailing } from './ChapterMarks'
import { RailGroupFlyout } from './RailGroupFlyout'
import {
  LORE_TAB_SLUG,
  buildSections,
  chapterItemsOf,
  gameTabHref,
  resolveGameTab,
  tabParamToSlug,
  type ChapterGroup,
} from './navModel'

interface ShellNavProps {
  // Collapsed to an icon rail (desktop only; the drawer is always full)
  rail: boolean
  onToggleRail: () => void
  // Phone: the tree is a drawer; the toggle button then closes it instead
  drawer: boolean
  onCloseDrawer: () => void
  // Slug of the game page that is open: the navigation is the tree of exactly this game
  slug: string
  id?: string
}

// One icon per chapter group for the rail. The chapters' own `icon` values are Material Design names from the research ("mdi:map-legend"),
// which this app does not ship, so the groups use fixed Tabler icons that fit their topic.
const GROUP_ICONS: Record<ChapterGroup, ReactNode> = {
  start: <IconCompass size={20} />,
  story: <IconWorld size={20} />,
  characters: <IconUsers size={20} />,
  combat: <IconSwords size={20} />,
  progress: <IconLockOpen size={20} />,
  explore: <IconMapSearch size={20} />,
  economy: <IconCoins size={20} />,
  extras: <IconPuzzle size={20} />,
}

// One entry of the tree. In the rail only the icon shows; its tooltip then names it (to the right of the rail).
function NavEntry({
  to,
  icon,
  label,
  current,
  rail,
}: {
  to: string
  icon: ReactNode
  label: string
  current: boolean
  rail: boolean
}) {
  // replace: moving between the chapters of a game never adds history entries (like the page's own tab changes), so the page's
  // back button leaves the game instead of stepping through the chapters seen before
  const link = (
    <Link to={to} replace className="pv-ni" aria-current={current ? 'page' : undefined} aria-label={rail ? label : undefined}>
      <span className="pv-ni-icon" aria-hidden="true">
        {icon}
      </span>
      <span className="pv-ni-label">{label}</span>
    </Link>
  )
  if (!rail) return link
  return (
    <Tooltip label={label} side="right" className="pv-ni-tip">
      {link}
    </Tooltip>
  )
}

function Cover({ url, large = false }: { url: string | undefined; large?: boolean }) {
  const [failed, setFailed] = useState(false)
  const cls = `pv-ni-cover${large ? ' pv-ni-cover--head' : ''}`
  if (!url || failed) return <span className={`${cls} pv-ni-cover--empty`} aria-hidden="true" />
  return <img className={cls} src={url} alt="" loading="lazy" onError={() => setFailed(true)} />
}

/** Quiet placeholder rows (48 px each, no text) for a list whose answer is still on its way. */
function SkeletonRows({ count }: { count: number }) {
  return (
    <ul className="pv-snav-skel" aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <li key={index}>
          <span className="pv-ni-skel" />
        </li>
      ))}
    </ul>
  )
}

/**
 * The navigation of one game page (left side): the tree of this game - the guide chapters under their group headings, Story & Lore.
 * (Live trophies and statistics are the area bar in the game head, the way back is its back button.) Collapsed it is an icon rail
 * (the chapters of a group open as a small flyout), on a phone it is a drawer. Pages without a game have no side navigation at all.
 */
export function ShellNav({ rail: railWanted, onToggleRail, drawer, onCloseDrawer, slug, id }: ShellNavProps) {
  const { t } = useTranslation(['shell', 'game'])
  const [searchParams] = useSearchParams()
  // The drawer always shows the full tree
  const rail = railWanted && !drawer

  // Same query as the game page: the chapters come from the cache the page fills anyway
  const { data: game } = useQuery({ queryKey: ['game', slug], queryFn: () => fetchGameDetail(slug) })
  const openGame = game && game.slug === slug ? game : undefined

  const failedIds = useMemo(() => new Set(failedChapterTabs(openGame).map((tab) => tab.id)), [openGame])
  const chapters = useMemo(() => chapterItemsOf(openGame, failedIds), [openGame, failedIds])
  const { sections, headings } = useMemo(() => buildSections(chapters), [chapters])
  const activeTab = resolveGameTab(openGame, tabParamToSlug(searchParams.get('tab')))

  const toggleLabel = drawer ? t('nav.menuClose') : rail ? t('nav.expand') : t('nav.collapse')
  const title = openGame?.title ?? ''

  return (
    <nav id={id} className="pv-snav" data-rail={rail ? 'true' : undefined} data-drawer={drawer ? 'true' : undefined} aria-label={t('nav.label')}>
      <div className="pv-snav-top">
        {!rail && (
          <div className="pv-snav-game">
            <Cover url={openGame?.coverUrl} large />
            {title ? <TruncatedText text={title} className="pv-snav-game-title" wrapperClassName="flex min-w-0 flex-1" /> : <span className="pv-ni-skel pv-snav-game-skel" aria-hidden="true" />}
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

      <div className="pv-snav-list" data-pv-scroll="nav" data-hint="game-nav">
        {!openGame ? (
          // The rows of the answer keep their room while it is on its way, so the entries below never jump
          <SkeletonRows count={rail ? 5 : 8} />
        ) : (
          <ul aria-label={t('nav.chapters', { game: title })} className="pv-gametree">
            {rail
              ? sections.map((section) => (
                  <li key={section.key ?? 'all'}>
                    <RailGroupFlyout
                      icon={section.key ? GROUP_ICONS[section.key] : <IconListDetails size={20} />}
                      label={section.key ? t(`game:chapters.groups.${section.key}`) : t('nav.chaptersGroup')}
                      gameSlug={slug}
                      chapters={section.tabs.map((entry) => entry.chapter)}
                      activeSlug={activeTab}
                    />
                  </li>
                ))
              : sections.map((section) => (
                  <li key={section.key ?? 'all'} className="pv-gametree-group">
                    {headings && section.key && <div className="pv-glabel">{t(`game:chapters.groups.${section.key}`)}</div>}
                    <ul>
                      {section.tabs.map(({ chapter }) => (
                        <li key={chapter.slug}>
                          <Link to={gameTabHref(slug, chapter.slug)} replace className="pv-ni pv-ni--chapter" aria-current={activeTab === chapter.slug ? 'page' : undefined}>
                            <span className="pv-ni-label">{chapter.title}</span>
                            <ChapterTrailing chapter={chapter} />
                          </Link>
                        </li>
                      ))}
                    </ul>
                  </li>
                ))}
            <li>
              <NavEntry to={gameTabHref(slug, LORE_TAB_SLUG)} icon={<IconBook size={20} />} label={t('nav.lore')} current={activeTab === LORE_TAB_SLUG} rail={rail} />
            </li>
          </ul>
        )}
      </div>
    </nav>
  )
}
