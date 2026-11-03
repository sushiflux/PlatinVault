import { useMemo, useState } from 'react'
import { CopyErrorButton } from './ui/CopyErrorButton'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  IconCheck,
  IconEye,
  IconEyeOff,
  IconRefresh,
  IconSearch,
  IconX,
} from '@tabler/icons-react'
import {
  fetchLiveAchievements,
  fetchLiveStatus,
  saveLiveAchievementNote,
} from '../api/client'
import type { LiveAchievementItemDto, LiveGameAchievementsDto, RarityTier } from '../api/types'
import { useFormat } from '../i18n/format'
import { useAchievementNotes } from '../utils/useAchievementNotes'
import { useSteamReach } from '../utils/useSteamReach'
import { useTierLabel } from './platinum/TierTrophy'
import { GhostIconButton } from './ui/GhostIconButton'
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
  TrophySizeSlider,
  TrophyTile,
  type TrophyDensity,
  type TrophyItemProps,
} from './TrophyViews'

interface LiveTrackerPanelProps {
  defaultExpanded?: boolean
  fixedAppId?: number | null
  // How-to per achievement (Steam API name -> tip/condition), taken from the guide's Steam achievement table
  tips?: Record<string, string>
  // Hands the summary card (progress + tiers) to the page, which watches whether it is on screen (compact bar below the header)
  onSummaryElement?: (element: HTMLElement | null) => void
}

// Status line of the summary card: only facts the player cares about, each one only while it is true, small and on one
// line (it wraps only if three facts meet in a very narrow card). The line keeps its height when nothing applies (rigid
// layout); the playtime only shows when the card is wide enough. The AppID is deliberately not shown here.
function HeroStatus({ data }: { data: LiveGameAchievementsDto | undefined }) {
  const { t } = useTranslation('live')
  const fmt = useFormat()
  // "Running now" is only known through the bridge or a local Steam: without one of them the indicator is not shown
  const { canLaunch } = useSteamReach()
  // Written out here (not taken from the server's text) so it follows the language: "35 min" / "12.5 h"
  const minutes = data?.game.playtimeForeverMinutes ?? 0
  const playtime = minutes <= 0 ? '' : minutes < 60 ? t('tracker.minutes', { n: fmt.number(minutes) }) : t('tracker.hours', { n: fmt.number(minutes / 60, 1) })
  return (
    <div className="flex min-h-6 flex-wrap items-center gap-x-3.5 gap-y-0.5 text-xs">
      {canLaunch && data?.game.isRunning && (
        <Tooltip label={t('tracker.running.label')} description={t('tracker.running.description')}>
          <span className="inline-flex items-center gap-1.5 font-medium" style={{ color: 'var(--accent-emerald)' }}>
            <span className="h-2 w-2 rounded-full motion-safe:animate-pulse" style={{ background: 'var(--accent-emerald)' }} aria-hidden="true" />
            {t('tracker.running.label')}
          </span>
        </Tooltip>
      )}
      {data?.is100Percent && (
        <span className="inline-flex items-center gap-1 font-medium" style={{ color: 'var(--accent-gold-text)' }}>
          <TierIcon tier="Platinum" size={14} /> {t('tracker.platinum')}
        </span>
      )}
      {data && data.syncedGuideItemsCount > 0 && (
        <span className="inline-flex items-center gap-1" style={{ color: 'var(--text-secondary)' }}>
          <IconCheck size={13} aria-hidden="true" /> {t('tracker.syncedGuide', { n: fmt.number(data.syncedGuideItemsCount) })}
        </span>
      )}
      {playtime && (
        <span className="hidden @[34rem]:inline" style={{ color: 'var(--text-muted)' }}>
          {t('tracker.playtime', { time: playtime })}
        </span>
      )}
    </div>
  )
}

// The status filter: one segmented control, the counters are the shared counter badge of the chapter tabs
// (the names are texts: live:tracker.filters.status.<id>)
const STATUS_FILTERS = ['all', 'needed', 'unlocked', 'hidden'] as const

// Live Steam achievement & trophy tracker styled in the harmonious 100% Pastel design.
export function LiveTrackerPanel({
  defaultExpanded = true,
  fixedAppId = null,
  tips = {},
  onSummaryElement,
}: LiveTrackerPanelProps) {
  const queryClient = useQueryClient()
  const { t } = useTranslation(['live', 'common'])
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
  const [noteDraft, setNoteDraft] = useState('')
  // A save in flight, and what the server (or the network) answered when it failed: shown in the note dialog, which stays open
  const [noteSaving, setNoteSaving] = useState(false)
  const [noteError, setNoteError] = useState<string | null>(null)
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

  const {
    data: liveData,
    isLoading,
    refetch,
    isFetching,
  } = useQuery({
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

  const closeNoteEditor = () => {
    setEditingNoteAch(null)
    setNoteError(null)
  }

  const handleSaveNote = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!editingNoteAch || !liveData || noteSaving) return
    setNoteSaving(true)
    setNoteError(null)
    try {
      await saveLiveAchievementNote(liveData.game.appId, editingNoteAch.apiName, noteDraft)
    } catch (err: unknown) {
      // The dialog stays open with the draft; say what went wrong (the server's own text, or that it could not be reached)
      const reason =
        err instanceof TypeError
          ? t('noteDialog.serverUnreachable')
          : err instanceof Error
            ? err.message
            : t('common:unknownError')
      setNoteError(t('noteDialog.saveFailed', { reason }))
      return
    } finally {
      setNoteSaving(false)
    }
    closeNoteEditor()
    await queryClient.invalidateQueries({ queryKey: ['live-achievements'] })
  }

  // Everything one list item needs: the spoiler cover, the translation toggle and the way to the note editor and the details
  const itemProps = (ach: LiveAchievementItemDto): TrophyItemProps => ({
    ach,
    tip: tips[ach.apiName],
    covered: ach.isHidden && !revealHidden && !revealedSingleIds[ach.apiName] && !ach.achieved,
    showOriginal: !!showOriginalIds[ach.apiName],
    onReveal: () => setRevealedSingleIds((prev) => ({ ...prev, [ach.apiName]: true })),
    onToggleOriginal: () => setShowOriginalIds((prev) => ({ ...prev, [ach.apiName]: !prev[ach.apiName] })),
    notesEnabled,
    onNote: () => {
      setEditingNoteAch(ach)
      setNoteDraft(ach.userNote)
      setNoteError(null)
    },
    onOpen: () => setDetailName(ach.apiName),
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
    <section className="space-y-5">
      {/* Summary card: cover, title and status share the head. Progress and tiers are not repeated here: the trophy bar of the game page
          above the content shows them (always, on every tab) */}
      <div ref={onSummaryElement} className="pv-live-hero">
        <div className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-3 sm:gap-x-5">
          {liveData?.game.headerImage ? (
            <img
              src={liveData.game.headerImage}
              alt=""
              className="aspect-[460/215] w-28 object-cover sm:row-span-2 sm:w-44 lg:w-56"
              style={{ borderRadius: 'var(--radius-sm)', border: '1px solid var(--border-subtle)' }}
            />
          ) : (
            <div
              className="aspect-[460/215] w-28 sm:row-span-2 sm:w-44 lg:w-56"
              style={{ background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)', borderRadius: 'var(--radius-sm)' }}
              aria-hidden="true"
            />
          )}

          {/* Two title lines are always reserved, so a long name never makes the card taller than a short one */}
          <div className="flex min-w-0 items-start gap-2 self-start">
            <h3
              className="line-clamp-2 min-h-[2.75em] min-w-0 flex-1 text-balance text-lg font-semibold leading-snug sm:text-xl"
              style={{ color: 'var(--text-primary)' }}
            >
              {liveData?.game.name ?? t('tracker.loadingName')}
            </h3>
            <GhostIconButton label={t('tracker.refresh')} size="md" onClick={() => void refetch()}>
              <IconRefresh size={17} className={isFetching ? 'motion-safe:animate-spin' : ''} aria-hidden="true" />
            </GhostIconButton>
          </div>

          <div className="@container col-span-2 self-end sm:col-span-1">
            <HeroStatus data={liveData} />
          </div>
        </div>
      </div>

      {/* Filter Bar & Trophy Grid */}
      {expanded && (
        <div className="space-y-4">
          {/* Row 1 = WHAT is shown (status, tier), row 2 = HOW it is shown (search, sort, view size, secret achievements).
              Nothing here changes size with its state; the rows wrap by the width of the box itself (container queries). */}
          <div className="pv-live-bar">
            <div className="pv-fbar-filters">
              <div className="pv-segmented pv-fbar-status" role="group" aria-label={t('tracker.filters.statusGroup')}>
                {STATUS_FILTERS.map((entry) => (
                  <button key={entry} type="button" aria-pressed={activeTab === entry} onClick={() => setActiveTab(entry)}>
                    {t(`tracker.filters.status.${entry}`)}
                    <span className="pv-tab-count">{statusCounts[entry]}</span>
                  </button>
                ))}
              </div>

              <div className="pv-segmented pv-fbar-tier" role="group" aria-label={t('tracker.filters.tierGroup')}>
                {(['all', 'Gold', 'Silver', 'Bronze'] as const).map((tier) => (
                  <button key={tier} type="button" aria-pressed={selectedRarity === tier} onClick={() => setSelectedRarity(tier)}>
                    {tier !== 'all' && <TierIcon tier={tier} size={13} />}
                    {tier === 'all' ? t('tracker.filters.tierAll') : tierLabel(tier)}
                  </button>
                ))}
              </div>
            </div>

            <div className="pv-fbar-view">
              <div className="pv-fbar-search">
                <IconSearch size={16} className="absolute left-3 top-1/2 -translate-y-1/2" style={{ color: 'var(--text-muted)' }} aria-hidden="true" />
                <input
                  type="search"
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  placeholder={notesEnabled ? t('tracker.search.placeholderWithNotes') : t('tracker.search.placeholder')}
                  aria-label={t('tracker.search.label')}
                  className="pv-input pv-input-icon w-full"
                />
              </div>

              <div className="pv-fbar-tools">
                <select
                  value={sortBy}
                  onChange={(e) => setSortBy(e.target.value as typeof sortBy)}
                  aria-label={t('tracker.sort.label')}
                  className="pv-input pv-fbar-sort font-semibold"
                >
                  <option value="default">{t('tracker.sort.default')}</option>
                  <option value="rarity_asc">{t('tracker.sort.rarityAsc')}</option>
                  <option value="rarity_desc">{t('tracker.sort.rarityDesc')}</option>
                  <option value="unlocked_recent">{t('tracker.sort.recent')}</option>
                  <option value="name_asc">{t('tracker.sort.name')}</option>
                </select>

                <TrophySizeSlider
                  className="pv-fbar-size"
                  value={density}
                  onChange={(value) => {
                    setDensity(value)
                    saveTrophyDensity(value)
                  }}
                />

                <Tooltip
                  label={t('tracker.reveal.label')}
                  description={revealHidden ? t('tracker.reveal.on') : t('tracker.reveal.off')}
                  side="bottom"
                  className="inline-flex shrink-0"
                >
                  <button
                    type="button"
                    aria-label={t('tracker.reveal.label')}
                    aria-pressed={revealHidden}
                    onClick={() => setRevealHidden(!revealHidden)}
                    className={`pv-btn pv-fbar-reveal ${revealHidden ? 'pv-btn-purple' : ''}`}
                  >
                    {revealHidden ? <IconEye size={17} aria-hidden="true" /> : <IconEyeOff size={17} aria-hidden="true" />}
                  </button>
                </Tooltip>
              </div>
            </div>
          </div>

          {isLoading || !notesSettingReady ? (
            <div
              className="pv-card py-12 text-center text-sm"
              style={{ color: 'var(--text-muted)' }}
            >
              {t('tracker.loading')}
            </div>
          ) : filteredAchievements.length === 0 ? (
            <div
              className="pv-card p-8 text-center text-sm"
              style={{ color: 'var(--text-muted)' }}
            >
              {t('tracker.empty')}
            </div>
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
        </div>
      )}

      {/* Details of one achievement (tiles and list rows) */}
      {detailAch && (
        <TrophyDetailDialog
          item={itemProps(detailAch)}
          onClose={() => setDetailName(null)}
        />
      )}

      {/* Personal Note Modal */}
      {notesEnabled && editingNoteAch && (
        <div
          className="pv-modal-backdrop"
          onClick={closeNoteEditor}
        >
          <div
            className="pv-modal w-full max-w-md p-5 space-y-4"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="flex items-center justify-between">
              <div>
                <h4
                  className="text-base font-extrabold"
                  style={{ color: 'var(--text-primary)' }}
                >
                  {t('noteDialog.title')}
                </h4>
                <p
                  className="text-xs"
                  style={{ color: 'var(--text-secondary)' }}
                >
                  {editingNoteAch.displayName}
                </p>
              </div>
              <button
                type="button"
                onClick={closeNoteEditor}
                className="pv-btn !p-1.5"
                aria-label={t('common:close')}
              >
                <IconX size={18} />
              </button>
            </div>

            <form onSubmit={handleSaveNote} className="space-y-3">
              <textarea
                rows={4}
                value={noteDraft}
                onChange={(e) => {
                  setNoteDraft(e.target.value)
                  // A failure answer belongs to the text that was sent
                  setNoteError(null)
                }}
                placeholder={t('noteDialog.placeholder')}
                className="pv-input w-full"
              />
              {noteError && (
                <p role="alert" className="pv-alert pv-alert-danger !px-3 !py-2 text-xs font-semibold">
                  {noteError}
                  <CopyErrorButton className="ms-2 align-middle" report={{ message: noteError }} />
                </p>
              )}
              <div className="flex justify-end gap-2">
                <button
                  type="button"
                  onClick={closeNoteEditor}
                  className="pv-btn"
                >
                  {t('common:cancel')}
                </button>
                <button type="submit" disabled={noteSaving} className="pv-btn pv-btn-emerald">
                  {t('noteDialog.save')}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </section>
  )
}
