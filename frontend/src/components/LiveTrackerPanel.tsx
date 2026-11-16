import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { IconSearch } from '@tabler/icons-react'
import { fetchLiveAchievements, fetchLiveStatus } from '../api/client'
import type { LiveAchievementItemDto, RarityTier } from '../api/types'
import '../styles/live.css'
import { useAchievementNotes } from '../utils/useAchievementNotes'
import { AchievementNoteDialog } from './AchievementNoteDialog'
import { RarityNotice } from './RarityNotice'
import { usePins } from './pins/pinsContext'
import { useTierLabel } from './platinum/TierTrophy'
import { Switch } from './ui/Switch'
import { TierIcon } from './ui/TierIcon'
import { Tooltip } from './ui/Tooltip'
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
} from './TrophyViews'

interface LiveTrackerPanelProps {
  defaultExpanded?: boolean
  fixedAppId?: number | null
  // How-to per achievement (Steam API name -> tip/condition), taken from the guide's Steam achievement table
  tips?: Record<string, string>
}

// The status filter: one segmented control, the counters are the shared counter badge of the chapter tabs
// (the names are texts: live:tracker.filters.status.<id>)
const STATUS_FILTERS = ['all', 'needed', 'unlocked', 'hidden'] as const

// The live trophies tab: filters, search, sort, view size and the achievement list. The game head above (cover, name, status, progress, tiers,
// refresh) belongs to the page and is shown on every tab.
export function LiveTrackerPanel({
  defaultExpanded = true,
  fixedAppId = null,
  tips = {},
}: LiveTrackerPanelProps) {
  const { t } = useTranslation(['live', 'common'])
  // "Im Blick": null on a page without pins (then no list item has a pin button)
  const pins = usePins()
  const tierLabel = useTierLabel()
  // Personal notes can be switched off in the settings; off = no note control, text, search or hint anywhere in this list
  const { enabled: notesEnabled, ready: notesSettingReady } = useAchievementNotes()

  const [expanded] = useState(defaultExpanded)
  // Size of the list (Liste, Kacheln, Kompakt, Groß), remembered per browser
  const [density, setDensity] = useState<TrophyDensity>(loadTrophyDensity)
  // Achievement whose detail dialog is open (tiles and list rows)
  const [detailName, setDetailName] = useState<string | null>(null)
  const [activeTab, setActiveTab] = useState<'all' | 'needed' | 'unlocked' | 'hidden'>('all')
  const [selectedRarity, setSelectedRarity] = useState<RarityTier | 'all'>('all')
  const [revealHidden, setRevealHidden] = useState(true)
  const [searchQuery, setSearchQuery] = useState('')
  const [sortBy, setSortBy] = useState<
    'default' | 'rarity_asc' | 'rarity_desc' | 'unlocked_recent' | 'name_asc'
  >('default')
  const [editingNoteAch, setEditingNoteAch] =
    useState<LiveAchievementItemDto | null>(null)
  const [revealedSingleIds, setRevealedSingleIds] = useState<
    Record<string, boolean>
  >({})
  const [showOriginalIds, setShowOriginalIds] = useState<
    Record<string, boolean>
  >({})

  const { data: status } = useQuery({
    queryKey: ['live-status'],
    queryFn: fetchLiveStatus,
    refetchInterval: 4000,
  })

  const targetAppId = fixedAppId || status?.runningAppId || undefined

  const { data: liveData, isLoading } = useQuery({
    queryKey: ['live-achievements', targetAppId ?? 'auto'],
    queryFn: () => fetchLiveAchievements(targetAppId),
    refetchInterval: status?.runningAppId ? 3000 : 15000,
  })

  const filteredAchievements = useMemo(() => {
    if (!liveData?.achievements) return []
    let list = [...liveData.achievements]

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

    list.sort((a, b) => {
      if (sortBy === 'rarity_asc') return a.globalPercent - b.globalPercent
      if (sortBy === 'rarity_desc') return b.globalPercent - a.globalPercent
      if (sortBy === 'unlocked_recent') {
        if (!a.unlockTime) return 1
        if (!b.unlockTime) return -1
        return (
          new Date(b.unlockTime).getTime() - new Date(a.unlockTime).getTime()
        )
      }
      if (sortBy === 'name_asc')
        return a.displayName.localeCompare(b.displayName)
      return 0
    })

    return list
  }, [liveData, activeTab, selectedRarity, searchQuery, sortBy, notesEnabled])

  // Everything one list item needs: the spoiler cover, the translation toggle and the way to the note editor and the details
  const itemProps = (ach: LiveAchievementItemDto): TrophyItemProps => ({
    ach,
    tip: tips[ach.apiName],
    covered: ach.isHidden && !revealHidden && !revealedSingleIds[ach.apiName] && !ach.achieved,
    showOriginal: !!showOriginalIds[ach.apiName],
    onReveal: () => setRevealedSingleIds((prev) => ({ ...prev, [ach.apiName]: true })),
    onToggleOriginal: () => setShowOriginalIds((prev) => ({ ...prev, [ach.apiName]: !prev[ach.apiName] })),
    notesEnabled,
    onNote: () => setEditingNoteAch(ach),
    onOpen: () => setDetailName(ach.apiName),
    pin: pins
      ? { pinned: pins.isPinned(ach.apiName), canPin: !pins.full, max: pins.max, onToggle: () => pins.toggle(ach.apiName) }
      : undefined,
  })
  const detailAch = detailName ? liveData?.achievements.find((a) => a.apiName === detailName) : undefined

  const hiddenCount =
    liveData?.achievements.filter((a) => a.isHidden).length || 0
  const neededCount = liveData
    ? liveData.totalCount - liveData.achievedCount
    : 0
  const unlockedCount = liveData?.achievedCount || 0
  const statusCounts = { all: liveData?.totalCount ?? 0, needed: neededCount, unlocked: unlockedCount, hidden: hiddenCount }

  return (
    <section className="pv-live">
      {/* The rarity values are old or missing: said once, completely, above the list (nothing while they are fine) */}
      {liveData?.rarity && liveData.rarity.state !== 'ok' && <RarityNotice rarity={liveData.rarity} />}

      {expanded && (
        <>
          {/* Rows of choices: WHAT is shown (status, tier), what is searched and how it is sorted, how it is displayed (view size, secrets).
              Nothing here changes size with its state; the area itself is the container, so the rows wrap by its own width. */}
          <div className="pv-lf">
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
                {(['all', 'Gold', 'Silver', 'Bronze'] as const).map((tier) => (
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
                  placeholder={notesEnabled ? t('tracker.search.placeholderWithNotes') : t('tracker.search.placeholder')}
                  aria-label={t('tracker.search.label')}
                  className="pv-input"
                />
              </div>
              <select value={sortBy} onChange={(e) => setSortBy(e.target.value as typeof sortBy)} aria-label={t('tracker.sort.label')} className="pv-select pv-lf-sort">
                <option value="default">{t('tracker.sort.default')}</option>
                <option value="rarity_asc">{t('tracker.sort.rarityAsc')}</option>
                <option value="rarity_desc">{t('tracker.sort.rarityDesc')}</option>
                <option value="unlocked_recent">{t('tracker.sort.recent')}</option>
                <option value="name_asc">{t('tracker.sort.name')}</option>
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

          {isLoading || !notesSettingReady ? (
            <div className="pv-live-empty">{t('tracker.loading')}</div>
          ) : filteredAchievements.length === 0 ? (
            <div className="pv-live-empty">{t('tracker.empty')}</div>
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
        </>
      )}

      {/* Details of one achievement (tiles and list rows) */}
      {detailAch && <TrophyDetailDialog item={itemProps(detailAch)} onClose={() => setDetailName(null)} />}

      {/* Personal note of one achievement */}
      {notesEnabled && editingNoteAch && liveData && (
        <AchievementNoteDialog key={editingNoteAch.apiName} ach={editingNoteAch} appId={liveData.game.appId} onClose={() => setEditingNoteAch(null)} />
      )}
    </section>
  )
}
