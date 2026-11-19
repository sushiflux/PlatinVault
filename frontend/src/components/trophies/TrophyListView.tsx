import { useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { IconSearch } from '@tabler/icons-react'
import type { LiveAchievementItemDto, RarityTier, TrophyScope } from '../../api/types'
import '../../styles/live.css'
import { useAchievementNotes } from '../../utils/useAchievementNotes'
import { useFitSegments } from '../../utils/useFitSegments'
import { AchievementNoteDialog } from '../AchievementNoteDialog'
import { usePins } from '../pins/pinsContext'
import { useTierLabel } from '../platinum/TierTrophy'
import { Switch } from '../ui/Switch'
import { TierIcon } from '../ui/TierIcon'
import { Tooltip } from '../ui/Tooltip'
import {
  CompactCard,
  LargeCard,
  loadTrophyDensity,
  saveTrophyDensity,
  TROPHY_GRID,
  TrophyDetailDialog,
  TrophyRow,
  TrophySizeControl,
  TrophyTile,
  type TrophyDensity,
  type TrophyItemProps,
} from '../TrophyViews'
import type { TrophyCheck } from './TrophyCheck'

// The status filter: one segmented control, the counters are the shared counter badge of the chapter tabs
// (the names are texts: live:tracker.filters.status.<id>)
const STATUS_FILTERS = ['all', 'needed', 'unlocked', 'hidden'] as const

export type TrophySort = 'default' | 'rarity_asc' | 'rarity_desc' | 'unlocked_recent' | 'name_asc'

/** The tiers a Steam list can filter by (platinum is not a tier of a single Steam achievement); a PlayStation list adds Platinum. */
export const STEAM_TIER_FILTERS: readonly RarityTier[] = ['Gold', 'Silver', 'Bronze']
/** Steam reports a share for every achievement, so every sort applies. */
export const ALL_SORTS: readonly TrophySort[] = ['default', 'rarity_asc', 'rarity_desc', 'unlocked_recent', 'name_asc']

/** What the list needs of a game's trophies: the live list of a Steam game and the researched list of a game added by hand both fit. */
export interface TrophyListData {
  achievements: LiveAchievementItemDto[]
  totalCount: number
  achievedCount: number
}

interface TrophyListViewProps {
  // undefined while the list is on its way
  data: TrophyListData | undefined
  loading: boolean
  // How-to per achievement (API name / trophy key -> tip/condition)
  tips?: Record<string, string>
  // Whose notes the note dialog saves (a Steam AppID or a game id); null = no note can be saved here
  notesScope: TrophyScope | null
  // The check of one trophy (a game added by hand); the lists of Steam games give none
  check?: (ach: LiveAchievementItemDto) => TrophyCheck | undefined
  // Which tiers the tier filter offers
  tierFilters: readonly RarityTier[]
  // Which sorts the sort menu offers (a list without any share leaves the rarity sorts out); the first one is the default order
  sorts: readonly TrophySort[]
  // Shown first, above the filters (e.g. the notice that the rarity values are old)
  notice?: ReactNode
}

// The filters, search, sort, view size and the achievement list of a trophy tab. Pure view: the data comes in from outside (the live list of a Steam
// game, the researched list of a game added by hand), so both look and behave the same.
export function TrophyListView({ data, loading, tips = {}, notesScope, check, tierFilters, sorts, notice }: TrophyListViewProps) {
  const { t } = useTranslation(['live', 'common'])
  // "Im Blick": null on a page without pins (then no list item has a pin button)
  const pins = usePins()
  const tierLabel = useTierLabel()
  // A bar of choices that does not fit its width (five tiers in English on a phone) becomes the connected vertical list instead of being cut or scrolled
  const filtersRef = useRef<HTMLDivElement>(null)
  useFitSegments(filtersRef)
  // Personal notes can be switched off in the settings; off = no note control, text, search or hint anywhere in this list
  const { enabled: notesEnabled, ready: notesSettingReady } = useAchievementNotes()

  // Size of the list (Liste, Kacheln, Kompakt, Groß), remembered per browser
  const [density, setDensity] = useState<TrophyDensity>(loadTrophyDensity)
  // Achievement whose detail dialog is open (tiles and list rows)
  const [detailName, setDetailName] = useState<string | null>(null)
  const [activeTab, setActiveTab] = useState<(typeof STATUS_FILTERS)[number]>('all')
  const [selectedRarity, setSelectedRarity] = useState<RarityTier | 'all'>('all')
  const [revealHidden, setRevealHidden] = useState(true)
  const [searchQuery, setSearchQuery] = useState('')
  const [sortBy, setSortBy] = useState<TrophySort>('default')
  const [editingNoteAch, setEditingNoteAch] = useState<LiveAchievementItemDto | null>(null)
  const [revealedSingleIds, setRevealedSingleIds] = useState<Record<string, boolean>>({})
  const [showOriginalIds, setShowOriginalIds] = useState<Record<string, boolean>>({})

  // A sort that is not offered (any more) is the default order
  const activeSort: TrophySort = sorts.includes(sortBy) ? sortBy : 'default'

  const filteredAchievements = useMemo(() => {
    if (!data?.achievements) return []
    let list = [...data.achievements]

    if (activeTab === 'needed') list = list.filter((a) => !a.achieved)
    else if (activeTab === 'unlocked') list = list.filter((a) => a.achieved)
    else if (activeTab === 'hidden') list = list.filter((a) => a.isHidden)

    if (selectedRarity !== 'all') {
      list = list.filter((a) => a.rarityTier === selectedRarity)
    }

    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase()
      list = list.filter(
        (a) =>
          a.displayName.toLowerCase().includes(q) ||
          a.description.toLowerCase().includes(q) ||
          (notesEnabled && a.userNote.toLowerCase().includes(q))
      )
    }

    // Array.sort is stable: the default order stays the order of the list (for a researched list: the order of the game)
    list.sort((a, b) => {
      if (activeSort === 'rarity_asc') return a.globalPercent - b.globalPercent
      if (activeSort === 'rarity_desc') return b.globalPercent - a.globalPercent
      if (activeSort === 'unlocked_recent') {
        if (!a.unlockTime) return 1
        if (!b.unlockTime) return -1
        return new Date(b.unlockTime).getTime() - new Date(a.unlockTime).getTime()
      }
      if (activeSort === 'name_asc') return a.displayName.localeCompare(b.displayName)
      return 0
    })

    return list
  }, [data, activeTab, selectedRarity, searchQuery, activeSort, notesEnabled])

  // Everything one list item needs: the spoiler cover, the translation toggle and the way to the note editor and the details
  const itemProps = (ach: LiveAchievementItemDto): TrophyItemProps => ({
    ach,
    tip: tips[ach.apiName],
    covered: ach.isHidden && !revealHidden && !revealedSingleIds[ach.apiName] && !ach.achieved,
    showOriginal: !!showOriginalIds[ach.apiName],
    onReveal: () => setRevealedSingleIds((prev) => ({ ...prev, [ach.apiName]: true })),
    onToggleOriginal: () => setShowOriginalIds((prev) => ({ ...prev, [ach.apiName]: !prev[ach.apiName] })),
    notesEnabled: notesEnabled && notesScope !== null,
    onNote: () => setEditingNoteAch(ach),
    onOpen: () => setDetailName(ach.apiName),
    pin: pins
      ? { pinned: pins.isPinned(ach.apiName), canPin: !pins.full, max: pins.max, onToggle: () => pins.toggle(ach.apiName) }
      : undefined,
    check: check?.(ach),
  })
  const detailAch = detailName ? data?.achievements.find((a) => a.apiName === detailName) : undefined

  const hiddenCount = data?.achievements.filter((a) => a.isHidden).length || 0
  const neededCount = data ? data.totalCount - data.achievedCount : 0
  const unlockedCount = data?.achievedCount || 0
  const statusCounts = { all: data?.totalCount ?? 0, needed: neededCount, unlocked: unlockedCount, hidden: hiddenCount }

  const sortLabel: Record<TrophySort, string> = {
    default: t('tracker.sort.default'),
    rarity_asc: t('tracker.sort.rarityAsc'),
    rarity_desc: t('tracker.sort.rarityDesc'),
    unlocked_recent: t('tracker.sort.recent'),
    name_asc: t('tracker.sort.name'),
  }

  return (
    <section className="pv-live">
      {notice}

      {/* Rows of choices: WHAT is shown (status, tier), what is searched and how it is sorted, how it is displayed (view size, secrets).
          Nothing here changes size with its state; the area itself is the container, so the rows wrap by its own width. */}
      <div className="pv-lf" ref={filtersRef}>
        <div className="pv-lf-row">
          <div className="pv-segmented pv-lf-stackable" role="group" aria-label={t('tracker.filters.statusGroup')}>
            {STATUS_FILTERS.map((entry) => (
              <button key={entry} type="button" aria-pressed={activeTab === entry} onClick={() => setActiveTab(entry)}>
                {t(`tracker.filters.status.${entry}`)}
                <span className="pv-tab-count">{statusCounts[entry]}</span>
              </button>
            ))}
          </div>

          <div className="pv-segmented pv-lf-fit" role="group" aria-label={t('tracker.filters.tierGroup')}>
            {(['all', ...tierFilters] as const).map((tier) => (
              <button key={tier} type="button" aria-pressed={selectedRarity === tier} onClick={() => setSelectedRarity(tier)}>
                {tier !== 'all' && <TierIcon tier={tier} size={15} />}
                {tier === 'all' ? t('tracker.filters.tierAll') : tierLabel(tier)}
              </button>
            ))}
          </div>
        </div>

        <div className="pv-lf-row">
          <div className="pv-lf-search">
            <IconSearch size={18} aria-hidden="true" />
            <input
              type="search"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder={notesEnabled && notesScope ? t('tracker.search.placeholderWithNotes') : t('tracker.search.placeholder')}
              aria-label={t('tracker.search.label')}
              className="pv-input"
            />
          </div>
          <select value={activeSort} onChange={(e) => setSortBy(e.target.value as TrophySort)} aria-label={t('tracker.sort.label')} className="pv-select pv-lf-sort">
            {sorts.map((sort) => (
              <option key={sort} value={sort}>
                {sortLabel[sort]}
              </option>
            ))}
          </select>
        </div>

        <div className="pv-lf-row">
          <TrophySizeControl
            className="pv-lf-fit"
            value={density}
            onChange={(value) => {
              setDensity(value)
              saveTrophyDensity(value)
            }}
          />
          <Tooltip label={t('tracker.reveal.label')} description={revealHidden ? t('tracker.reveal.on') : t('tracker.reveal.off')} side="bottom" className="inline-flex shrink-0">
            <Switch checked={revealHidden} onChange={setRevealHidden} className="pv-lf-switch">
              {t('tracker.reveal.label')}
            </Switch>
          </Tooltip>
        </div>
      </div>

      {loading || !notesSettingReady ? (
        <div className="pv-surface pv-live-empty">{t('tracker.loading')}</div>
      ) : filteredAchievements.length === 0 ? (
        <div className="pv-surface pv-live-empty">{t('tracker.empty')}</div>
      ) : (
        <div className={TROPHY_GRID[density]}>
          {filteredAchievements.map((ach) => {
            const item = itemProps(ach)
            return density === 3 ? (
              <LargeCard key={ach.apiName} {...item} />
            ) : density === 2 ? (
              <CompactCard key={ach.apiName} {...item} />
            ) : density === 1 ? (
              <TrophyTile key={ach.apiName} {...item} />
            ) : (
              <TrophyRow key={ach.apiName} {...item} />
            )
          })}
        </div>
      )}

      {/* Details of one achievement (tiles and list rows) */}
      {detailAch && <TrophyDetailDialog item={itemProps(detailAch)} onClose={() => setDetailName(null)} />}

      {/* Personal note of one achievement */}
      {notesEnabled && editingNoteAch && notesScope && (
        <AchievementNoteDialog key={editingNoteAch.apiName} ach={editingNoteAch} scope={notesScope} onClose={() => setEditingNoteAch(null)} />
      )}
    </section>
  )
}
