import { useEffect, useRef, useState } from 'react'
import { cubicBezier, useReducedMotion } from 'motion/react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { useTranslation } from 'react-i18next'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  closestCenter,
  DndContext,
  DragOverlay,
  KeyboardSensor,
  MouseSensor,
  TouchSensor,
  useSensor,
  useSensors,
  type Announcements,
  type DragEndEvent,
  type DragStartEvent,
  type DropAnimation,
  type DropAnimationFunction,
} from '@dnd-kit/core'
import { arrayMove, rectSortingStrategy, SortableContext, sortableKeyboardCoordinates } from '@dnd-kit/sortable'
import {
  IconArrowsSort,
  IconFileImport,
  IconLoader2,
  IconSearch,
  IconTrophy,
} from '@tabler/icons-react'
import {
  fetchBranding,
  fetchGames,
  fetchLiveStatus,
  fetchLibrarySummary,
  fetchSteamLibrary,
  fetchSteamProfile,
  importGuide,
  reorderGames,
  setFavorite,
  setLibraryFavorite,
} from '../api/client'
import type { GameSummaryDto, SteamOwnedGameDto } from '../api/types'
import { localeFor, useFormat } from '../i18n/format'
import { AppFooter } from '../components/AppFooter'
import { DashboardSortControl } from '../components/DashboardSortControl'
import { CabinetToolGroup } from '../components/cabinet/CabinetToolGroup'
import { DashboardFigures, DashboardSubBar, ViewSwitch } from '../components/DashboardControls'
import {
  gameGenres,
  GameTile,
  LibraryTile,
  SortableGameTile,
  loadTileDensity,
  saveTileDensity,
  TILE_CONTAINER,
  TileSizeSlider,
  type TileDensity,
} from '../components/GameTiles'
import { DashboardEmptyHelp } from '../components/DashboardEmptyHelp'
import { HeaderUtilities } from '../components/HeaderUtilities'
import { HintTour } from '../components/onboarding/HintTour'
import { useScreenHints } from '../components/onboarding/useScreenHints'
import { SteamAccountChip } from '../components/SteamAccountChip'
import { FloatingNotice, FloatingNoticeStack } from '../components/ui/FloatingNotice'
import { IconButton } from '../components/ui/IconButton'
import { ProgressBar } from '../components/ui/ProgressBar'
import { ReservedText } from '../components/ui/ReservedText'
import { Tooltip } from '../components/ui/Tooltip'
import { UiScaleControl } from '../components/ui/UiScaleControl'
import { OnboardingTour } from '../components/onboarding/OnboardingTour'
import { useOnboardingTour } from '../components/onboarding/useOnboardingTour'
import { isPlatinumGame } from '../components/platinum/platinum'
import { SettingsModal, type SettingsTab } from '../components/SettingsModal'
import { describeError } from '../utils/apiErrorText'
import {
  ORDERS_BY_VIEW,
  countMissing,
  favoriteSortValues,
  librarySortValues,
  sortItems,
  type SortOrder,
} from '../utils/dashboardSort'
import { useDashboardSort } from '../utils/useDashboardSort'
import { LiveCountProvider, useTwitchLiveCounts } from '../utils/useTwitchLiveCounts'
import { useSteamReach } from '../utils/useSteamReach'
import { useScrolledPast } from '../utils/useScrolledPast'
import { useStickyHeaderHeight } from '../utils/useStickyHeaderHeight'

interface DashboardPageProps {
  theme: string
  onToggleTheme: () => void
}

// Drop of a dragged card (see settleDroppedCard): the glide into the slot takes DROP_MS (position, tilt, scale and shadow
// share the easing, tilt, scale and shadow take the whole time up to the end of the hand-over); in its tail, where the card has all but stopped (at HANDOVER_AT_MS it is less than half a pixel from
// its slot for a move of up to 1000 px), the real card steps to full opacity beneath it and the copy fades out over it for
// HANDOVER_MS. The ring in index.css (.pv-sortable--landed) starts after all of it: keep its delay at the sum.
const DROP_MS = 420
const HANDOVER_AT_MS = 360
const HANDOVER_MS = 200
const DROP_EASING = cubicBezier(0.22, 1, 0.36, 1)
// The real card while it is the empty place of the lifted one (the same value as .pv-sortable--ghost in index.css)
const GHOST_OPACITY = 0.28
// The lifted pose of the copy (the same values as .pv-drag-lift in index.css)
const LIFT_SCALE = 1.04
const LIFT_TILT = -1.2

const lerp = (from: number, to: number, t: number) => from + (to - from) * t
// The shadow of the lifted card at strength 1, fading to nothing at 0; always the same three layers, so it fades layer by layer
const liftShadow = (k: number) =>
  `0 ${32 * k}px ${60 * k}px ${-16 * k}px rgba(15, 23, 42, ${0.55 * k}), ` +
  `0 0 0 ${2 * k}px color-mix(in srgb, var(--accent-crystal-border) ${k * 100}%, transparent), ` +
  `0 0 ${30 * k}px ${-6 * k}px color-mix(in srgb, var(--accent-crystal) ${k * 100}%, transparent)`

// The drop animation of the dragged card, written out instead of dnd-kit's default so that the copy and the real card are
// really the same picture at the moment they swap (a visible snap otherwise). What the default got wrong here:
// - It measured the copy including its tilt and scale, so the copy landed 15-19 px beside the slot and jumped. Now the lift
//   sits on an inner box (.pv-drag-lift) and the measured box is the plain one.
// - It rounds the translation to whole pixels, and a browser animation draws the copy with a lower image quality (jagged
//   cover, softer text, a transformed layer on a half pixel is blurred) than a card at rest, which sharpened in one step at
//   the swap. So the copy is driven frame by frame from script and placed by its position, exactly like the real card; the
//   tilt is the only thing left that softens it, and the fade at the end turns that into a gradual change.
// - It skipped the whole animation when the card was released exactly on its slot (always the case with the keyboard) and
//   swapped the card in its lifted pose.
const settleDroppedCard: DropAnimationFunction = ({ active, dragOverlay }) =>
  new Promise<void>((resolve) => {
    const overlay = dragOverlay.node
    const lifted = overlay.querySelector<HTMLElement>('.pv-drag-lift')
    const placeholder = active.node
    // From where the copy is now (its position on the page, tilt not counted) to where the real card is
    const from = { x: dragOverlay.rect.left, y: dragOverlay.rect.top }
    const to = { x: active.rect.left, y: active.rect.top }
    placeholder.style.opacity = String(GHOST_OPACITY)
    // Placed by position from here on; the keyboard mode's CSS transition on the transform would fight it
    overlay.style.transition = 'none'
    overlay.style.transform = 'none'
    if (lifted) lifted.style.animation = 'none'
    const startedAt = performance.now()
    let handedOver = false
    const frame = (now: number) => {
      const elapsed = now - startedAt
      const e = DROP_EASING(Math.min(1, Math.max(0, elapsed / DROP_MS)))
      overlay.style.left = `${lerp(from.x, to.x, e)}px`
      overlay.style.top = `${lerp(from.y, to.y, e)}px`
      if (lifted) {
        // Tilt and scale reach exactly zero only when the copy is gone: a copy that is tilted at all is drawn resampled and a
        // touch soft, and it would sharpen in one step at the moment it is not tilted any more
        const l = DROP_EASING(Math.min(1, Math.max(0, elapsed / (HANDOVER_AT_MS + HANDOVER_MS))))
        lifted.style.transform = `scale(${lerp(LIFT_SCALE, 1, l)}) rotate(${lerp(LIFT_TILT, 0, l)}deg)`
        lifted.style.boxShadow = liftShadow(1 - l)
      }
      if (elapsed >= HANDOVER_AT_MS) {
        if (!handedOver) {
          handedOver = true
          placeholder.style.opacity = ''
        }
        overlay.style.opacity = String(1 - Math.min(1, (elapsed - HANDOVER_AT_MS) / HANDOVER_MS))
      }
      if (elapsed < HANDOVER_AT_MS + HANDOVER_MS && overlay.isConnected) {
        requestAnimationFrame(frame)
        return
      }
      placeholder.style.opacity = ''
      resolve()
    }
    requestAnimationFrame(frame)
  })

// Startseite (Home) displaying the clean 100% Pastel Game Cards Grid. Clicking any game card opens its dedicated Game Page.
export function DashboardPage({ theme, onToggleTheme }: DashboardPageProps) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()

  const [search, setSearch] = useState('')
  const [density, setDensity] = useState<TileDensity>(loadTileDensity)
  const [settingsOpen, setSettingsOpen] = useState(false)
  // Tab the settings open on when a specific entry asked for it (tour checklist); otherwise the last used tab
  const [settingsTab, setSettingsTab] = useState<SettingsTab | undefined>(undefined)
  const [settingsNotice, setSettingsNotice] = useState<{ type: 'ok' | 'err'; text: string } | null>(null)
  // The failure of the last action (reorder, favourite, delete, import) as a floating notice: what failed, why, the raw text
  const [failure, setFailure] = useState<{ title: string; message: string; details: string | null } | null>(null)
  const fail = (title: string, err: unknown) => setFailure({ title, ...describeError(err, t('notice.unknown')) })
  const [importing, setImporting] = useState(false)
  const importInputRef = useRef<HTMLInputElement>(null)
  // The sticky sub-bar takes over the hero's controls once the hero has scrolled out of view; the header publishes its
  // real height (--pv-header-h) so the sub-bar sits exactly under it
  const headerRef = useStickyHeaderHeight<HTMLElement>()
  const heroRef = useRef<HTMLElement>(null)
  const heroGone = useScrolledPast(heroRef)
  const [searchParams, setSearchParams] = useSearchParams()
  // Onboarding tour: opens by itself on the first visit, the "?" button in the header reopens it
  const tour = useOnboardingTour()
  // Coach marks of this page: the first time it is seen (and while no intro / dialog is up), and on "?" any time
  const hints = useScreenHints('dashboard', tour.open || settingsOpen)
  // "Restart the introduction" in the settings closes the settings first, then the tour opens
  useEffect(() => {
    const close = () => setSettingsOpen(false)
    window.addEventListener('pv-start-tour', close)
    return () => window.removeEventListener('pv-start-tour', close)
  }, [])

  const { data: branding } = useQuery({
    queryKey: ['branding'],
    queryFn: fetchBranding,
  })
  // The branding file carries one tagline (German); the English page uses the translated one instead of showing German
  const tagline = (fmt.language === 'de' ? branding?.tagline : undefined) ?? t('header.tagline')

  // Result of the "Sign in with Steam" round trip (backend redirects to /?steamLink=ok|error&reason=...)
  useEffect(() => {
    const steamLink = searchParams.get('steamLink')
    if (!steamLink) return
    setSettingsNotice(
      steamLink === 'ok'
        ? { type: 'ok', text: t('notice.steamLinked') }
        : {
            type: 'err',
            text: t('notice.steamLinkFailed', { reason: searchParams.get('reason') ?? t('notice.unknownReason') }),
          }
    )
    setSettingsOpen(true)
    setSearchParams({}, { replace: true })
    void queryClient.invalidateQueries({ queryKey: ['settings'] })
    void queryClient.invalidateQueries({ queryKey: ['steam-profile'] })
  }, [searchParams, setSearchParams, queryClient, t])

  const { data: steamProfile } = useQuery({
    queryKey: ['steam-profile'],
    queryFn: fetchSteamProfile,
    staleTime: 5 * 60_000,
  })

  const { data: liveStatus } = useQuery({
    queryKey: ['live-status'],
    queryFn: fetchLiveStatus,
    refetchInterval: 5000,
  })

  // The server refreshes trophy states and card facts in the background: every 4 s while a favourite has not been read
  // from Steam yet (e.g. just starred), otherwise every 30 s
  const { data: games = [], isLoading } = useQuery({
    queryKey: ['games'],
    queryFn: fetchGames,
    refetchInterval: (query) =>
      query.state.data?.some((g) => g.isFavorite && g.steamAppId && !g.trophiesCheckedAt) ? 4000 : 30000,
  })

  // Library-wide figures (Steam key needed); null/missing = the dashboard sticks to its own games
  const { data: librarySummary, isPending: librarySummaryPending } = useQuery({
    queryKey: ['library-summary'],
    queryFn: fetchLibrarySummary,
    // While the background evaluation runs the figures grow, so they are fetched more often
    refetchInterval: (query) => (query.state.data?.scanning ? 5000 : 60_000),
  })
  const hasLibrary = Boolean(librarySummary)
  // While the answer is on its way the view switch keeps its place in the overview panel (an empty cell), so it arriving shifts nothing
  const reserveViewSwitch = !hasLibrary && librarySummaryPending

  // "Favourites" = the starred games as tiles (default), "All games" = the whole Steam library; remembered per browser
  const favoriteGames = games.filter((g) => g.isFavorite)
  const [viewChoice, setViewChoice] = useState<'favorites' | 'all'>(() => {
    try {
      return localStorage.getItem('pv_dashboard_view') === 'all' ? 'all' : 'favorites'
    } catch {
      return 'favorites'
    }
  })
  const view = hasLibrary ? viewChoice : 'favorites'
  const selectView = (next: 'favorites' | 'all') => {
    setViewChoice(next)
    try {
      localStorage.setItem('pv_dashboard_view', next)
    } catch {
      // Storage unavailable (private mode) - the view just isn't remembered
    }
  }

  const { data: library, isLoading: libraryLoading } = useQuery({
    queryKey: ['steam-library'],
    queryFn: () => fetchSteamLibrary(),
    enabled: view === 'all',
    staleTime: 5 * 60_000,
    refetchInterval: librarySummary?.scanning ? 15_000 : false,
  })
  const [busyAppIds, setBusyAppIds] = useState<number[]>([])

  // Drag & drop of the favourites: mouse (small distance, so clicks keep working), long-press on touch, keyboard
  const sensors = useSensors(
    useSensor(MouseSensor, { activationConstraint: { distance: 8 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 250, tolerance: 8 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates })
  )
  const [draggingSlug, setDraggingSlug] = useState<string | null>(null)
  // Saves the new order; the list on screen changes at once and is rolled back when the server says no
  const reduceMotion = useReducedMotion() ?? false
  // The card that was just dropped (gets a short confirming ring)
  const [landedSlug, setLandedSlug] = useState<string | null>(null)
  // After a mouse drop the pointer still rests on the card, and its hover lift would start exactly when the dragged copy hands
  // over to the real card and read as a second hop. So the card stays at rest until the pointer really moves, is pressed,
  // the wheel turns or a key is hit (a small tremor of the hand right after the release does not count).
  const [stillSlug, setStillSlug] = useState<string | null>(null)
  useEffect(() => {
    if (!stillSlug) return
    let origin: { x: number; y: number } | null = null
    const release = () => setStillSlug(null)
    const onMove = (e: PointerEvent) => {
      origin ??= { x: e.clientX, y: e.clientY }
      if (Math.hypot(e.clientX - origin.x, e.clientY - origin.y) > 8) release()
    }
    window.addEventListener('pointermove', onMove)
    window.addEventListener('pointerdown', release)
    window.addEventListener('wheel', release, { passive: true })
    window.addEventListener('keydown', release)
    return () => {
      window.removeEventListener('pointermove', onMove)
      window.removeEventListener('pointerdown', release)
      window.removeEventListener('wheel', release)
      window.removeEventListener('keydown', release)
    }
  }, [stillSlug])
  const reorderMutation = useMutation({
    // The order on screen was already changed synchronously in handleDragEnd (no snap back to the old place while the
    // request runs); this only saves it and puts the previous order back when the server refuses
    mutationFn: ({ slugs }: { slugs: string[]; previous: GameSummaryDto[] | undefined }) => reorderGames(slugs),
    onError: (err, variables) => {
      if (variables.previous) queryClient.setQueryData(['games'], variables.previous)
      fail(t('notice.reorderFailed'), err)
    },
    onSettled: () => void queryClient.invalidateQueries({ queryKey: ['games'] }),
  })

  const needle = search.trim().toLowerCase()
  // The search matches the title and the genres of a card (what the card shows), not the AI's subtitle
  // The chosen sort order of each view (saved on the server, so it follows the user between the Windows window and the browser)
  const dashboardSort = useDashboardSort()
  const favoritesChoice = dashboardSort.choiceFor('favorites')
  const libraryChoice = dashboardSort.choiceFor('all')
  const sortLocale = localeFor(fmt.language)
  // "Own order" (custom) is the server's manual order (games.SortOrder); every other order sorts a copy of it
  const orderedFavorites = sortItems(favoriteGames, favoriteSortValues, favoritesChoice, sortLocale)
  const filteredGames = orderedFavorites.filter((g) => `${g.title} ${gameGenres(g).join(' ')}`.toLowerCase().includes(needle))
  // Dragging needs the full list in the manual order: with a filter the order of a subset would be ambiguous, and in any other
  // sort order a dropped card would have no place that means anything
  const dragOffByOrder = favoritesChoice.order !== 'custom'
  const sortDisabled = needle.length > 0 || favoriteGames.length < 2 || dragOffByOrder
  useEffect(() => {
    if (!dashboardSort.saveError) return
    setFailure({ title: t('notice.sortFailed'), ...describeError(dashboardSort.saveError, t('notice.unknown')) })
    dashboardSort.clearSaveError()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dashboardSort.saveError])
  const favoriteSlugs = favoriteGames.map((g) => g.slug)
  const draggingGame = draggingSlug ? favoriteGames.find((g) => g.slug === draggingSlug) : undefined
  const titleOf = (id: string | number) => favoriteGames.find((g) => g.slug === id)?.title ?? t('card.fallbackTitle')
  const positionOf = (id: string | number) => favoriteSlugs.indexOf(String(id)) + 1
  const handleDragStart = (event: DragStartEvent) => setDraggingSlug(String(event.active.id))
  const handleDragEnd = (event: DragEndEvent) => {
    setDraggingSlug(null)
    const { active, over } = event
    // Also when the card goes back to where it was: the copy glides back and hands over to the card the same way
    setStillSlug(event.activatorEvent instanceof MouseEvent ? String(active.id) : null)
    if (!over || active.id === over.id) return
    const from = favoriteSlugs.indexOf(String(active.id))
    const to = favoriteSlugs.indexOf(String(over.id))
    if (from < 0 || to < 0) return
    const slugs = arrayMove(favoriteSlugs, from, to)
    // Same tick as the drop: the new order is rendered right away, so the card lands in its new place and never flashes
    // at the old one; the games that are not listed keep their relative order behind the favourites
    const previous = queryClient.getQueryData<GameSummaryDto[]>(['games'])
    void queryClient.cancelQueries({ queryKey: ['games'] })
    if (previous) {
      const bySlug = new Map(previous.map((g) => [g.slug, g]))
      const listed = slugs.map((slug) => bySlug.get(slug)).filter((g): g is GameSummaryDto => g !== undefined)
      queryClient.setQueryData<GameSummaryDto[]>(['games'], [...listed, ...previous.filter((g) => !slugs.includes(g.slug))])
    }
    setLandedSlug(String(active.id))
    // Longer than the ring animation in index.css (0.56 s delay + 1.5 s), so the ring is never cut off
    window.setTimeout(() => setLandedSlug((current) => (current === String(active.id) ? null : current)), 2100)
    reorderMutation.mutate({ slugs, previous })
  }
  // With reduced motion there is no animation: the card is swapped at once
  const dropAnimation: DropAnimation | null = reduceMotion ? null : settleDroppedCard
  // Spoken by screen readers while sorting (keyboard: Space picks up, arrows move, Space drops, Esc cancels)
  const total = fmt.number(favoriteSlugs.length)
  const announcements: Announcements = {
    onDragStart: ({ active }) => t('drag.start', { title: titleOf(active.id), position: positionOf(active.id), total }),
    onDragOver: ({ active, over }) => (over ? t('drag.over', { title: titleOf(active.id), position: positionOf(over.id), total }) : undefined),
    onDragEnd: ({ active, over }) =>
      over ? t('drag.drop', { title: titleOf(active.id), position: positionOf(over.id), total }) : t('drag.dropped', { title: titleOf(active.id) }),
    onDragCancel: ({ active }) => t('drag.cancel', { title: titleOf(active.id), position: positionOf(active.id) }),
  }

  // Games already in PlatinVault use their stored (healed) cover
  const vaultCovers = new Map(games.filter((g) => g.steamAppId && g.coverUrl).map((g) => [g.steamAppId, g.coverUrl]))
  const filteredLibrary = sortItems(
    (library?.games ?? [])
      .map((g) => (vaultCovers.has(g.appId) ? { ...g, coverUrl: vaultCovers.get(g.appId)! } : g))
      .filter((g) => g.name.toLowerCase().includes(needle)),
    librarySortValues,
    libraryChoice,
    sortLocale
  )

  // Live Twitch streams per card (the badge on the Twitch buttons): one background batch for the games on the page, only with Twitch credentials
  const liveCounts = useTwitchLiveCounts(
    view === 'all'
      ? filteredLibrary.map((g) => ({ appId: g.appId, name: g.name }))
      : filteredGames.flatMap((g) => (g.steamAppId ? [{ appId: g.steamAppId, name: g.title }] : []))
  )

  // ---- The sort control and the sentence under the count line: how the cards are sorted and what is still missing for it ----
  const activeChoice = view === 'all' ? libraryChoice : favoritesChoice
  const activeOrder: SortOrder = activeChoice.order
  const shownCount = view === 'all' ? filteredLibrary.length : filteredGames.length
  const missingCount =
    view === 'all'
      ? countMissing(filteredLibrary, librarySortValues, activeOrder)
      : countMissing(filteredGames, favoriteSortValues, activeOrder)
  // The server looks release dates / reviews up in the background, and rates the library's trophies; until that is done the list
  // is sorted by what is known so far and says so (instead of silently showing an incomplete order as if it were final)
  let lookup: { done: number; total: number } | null = null
  if ((activeOrder === 'release' || activeOrder === 'metacritic' || activeOrder === 'reviews') && librarySummary?.metaScanning) {
    lookup = { done: librarySummary.metaDone ?? 0, total: librarySummary.metaTotal ?? 0 }
  } else if (activeOrder === 'completion' && view === 'all' && librarySummary?.scanning) {
    lookup = { done: librarySummary.scanDone, total: librarySummary.scanTotal }
  }
  const sortLoading = lookup !== null && missingCount > 0
  const sortSentence = (order: SortOrder, direction: 'asc' | 'desc') =>
    t('sort.sentence', { order: t(`sort.orders.${order}`), direction: t(`sort.directions.${order}.${direction}`) })
  let sortStatus = ''
  if (shownCount > 0 && missingCount > 0) {
    if (lookup) {
      sortStatus = lookup.total > 0
        ? t('sort.status.pending', { done: fmt.number(lookup.done), total: fmt.number(lookup.total) })
        : t('sort.status.pendingShort')
    } else if (missingCount === shownCount) {
      sortStatus = activeOrder === 'size' ? t('sort.status.noSize') : t('sort.status.noData')
    } else {
      sortStatus = activeOrder === 'size'
        ? t('sort.status.missingSize', { count: missingCount })
        : t('sort.status.missing', { count: missingCount })
    }
  }
  // Every text the line can show: the room is reserved for the tallest, so a status appearing or changing moves nothing
  const bigNumber = fmt.number(9999)
  const statusTexts = [
    t('sort.status.pending', { done: bigNumber, total: bigNumber }),
    t('sort.status.pendingShort'),
    t('sort.status.noData'),
    t('sort.status.noSize'),
    t('sort.status.missing', { count: 9999 }),
    t('sort.status.missingSize', { count: 9999 }),
  ]
  const sortLineCandidates = ORDERS_BY_VIEW[view].flatMap((order) => {
    const longest = [sortSentence(order, 'asc'), sortSentence(order, 'desc')].reduce((a, b) => (b.length > a.length ? b : a))
    return [longest, ...statusTexts.map((status) => `${longest} ${status}`)]
  })

  const handleToggleLibraryFavorite = async (game: SteamOwnedGameDto) => {
    if (busyAppIds.includes(game.appId)) return
    setBusyAppIds((prev) => [...prev, game.appId])
    setFailure(null)
    try {
      await setLibraryFavorite(game.appId, game.name, !game.isFavorite)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['games'] }),
        queryClient.invalidateQueries({ queryKey: ['steam-library'] }),
      ])
    } catch (err: unknown) {
      fail(t('notice.favoriteFailed', { name: game.name }), err)
    } finally {
      setBusyAppIds((prev) => prev.filter((id) => id !== game.appId))
    }
  }

  const handleToggleFavorite = async (game: GameSummaryDto) => {
    setFailure(null)
    try {
      await setFavorite(game.slug, !game.isFavorite)
      await queryClient.invalidateQueries({ queryKey: ['games'] })
    } catch (err: unknown) {
      fail(t('notice.favoriteFailed', { name: game.title }), err)
    }
  }

  // Progress = Steam trophies only; the figures follow the chosen view: the favourites, or the whole library
  const showLibraryFigures = view === 'all' && librarySummary != null
  const totalTrophiesAllGames = showLibraryFigures ? librarySummary.trophiesTotal : favoriteGames.reduce((sum, g) => sum + g.trophiesTotal, 0)
  const unlockedTrophiesAllGames = showLibraryFigures ? librarySummary.trophiesUnlocked : favoriteGames.reduce((sum, g) => sum + g.trophiesUnlocked, 0)
  const overallPercent =
    totalTrophiesAllGames > 0 ? Math.floor((unlockedTrophiesAllGames / totalTrophiesAllGames) * 100) : 0
  const completedGamesCount = showLibraryFigures ? librarySummary.perfectGames : favoriteGames.filter(isPlatinumGame).length
  const gamesCountText = showLibraryFigures
    ? t('figures.library', { count: librarySummary.libraryGameCount })
    : t('figures.favorites', { count: favoriteGames.length })
  const gamesCountTextShort = showLibraryFigures ? t('figures.libraryShort', { count: librarySummary.libraryGameCount }) : gamesCountText
  const figures = {
    gamesText: gamesCountText,
    gamesTextShort: gamesCountTextShort,
    unlocked: unlockedTrophiesAllGames,
    total: totalTrophiesAllGames,
    percent: overallPercent,
    completed: completedGamesCount,
    scanning: showLibraryFigures && librarySummary.scanning
      ? { done: librarySummary.scanDone, total: librarySummary.scanTotal }
      : null,
  }
  const handleDensityChange = (value: TileDensity) => {
    setDensity(value)
    saveTileDensity(value)
  }

  // "Is running now" needs the bridge or a local Steam: it is only shown while one of them is there (a stale answer after the
  // bridge went away must not keep the notice up)
  const { canLaunch } = useSteamReach()
  const runningVaultGame = canLaunch && liveStatus?.runningGameTracked
    ? games.find((g) => g.slug === liveStatus.runningGameSlug)
    : undefined

  // Imports a guide file (exported from PlatinVault or written in Claude Code with docs/guide-prompt.md)
  const handleImportFile = async (file: File | undefined) => {
    if (!file) return
    setFailure(null)
    setImporting(true)
    try {
      let document: unknown
      try {
        document = JSON.parse(await file.text())
      } catch {
        throw new Error(t('notice.importInvalid', { name: file.name }))
      }
      const created = await importGuide(document)
      await queryClient.invalidateQueries({ queryKey: ['games'] })
      void navigate(`/games/${created.slug}`)
    } catch (err: unknown) {
      fail(t('notice.importFailed'), err)
    } finally {
      setImporting(false)
      if (importInputRef.current) importInputRef.current.value = ''
    }
  }

  return (
    <div className="min-h-screen flex flex-col">
      {/* Sticky Top Header (100% Guide Design) */}
      <header ref={headerRef} className="top-header">
        <div className="pv-hbar pv-hbar--dash">
          {/* Brand */}
          <div className="pv-hbrand">
            <img
              src="/favicon.svg"
              alt={t('header.logoAlt')}
              className="h-10 w-10 rounded-xl shadow-md shrink-0"
            />
            <div className="min-w-0">
              <h1
                className="truncate text-lg font-extrabold leading-tight tracking-tight"
                style={{ color: 'var(--text-primary)' }}
              >
                {branding?.productName ?? 'PlatinVault'}
              </h1>
              {/* Shown or hidden by the header's container queries (index.css, .pv-htagline), not by viewport breakpoints */}
              <p
                className="pv-htagline truncate text-xs font-medium"
                style={{ color: 'var(--text-muted)' }}
              >
                {tagline}
              </p>
            </div>
          </div>

          {/* Tools: overall trophy progress (fills the free width), Steam account | actions */}
          <div className="pv-htools">
            <Tooltip
              label={t('header.overall.label')}
              description={t('header.overall.description')}
              className="pv-hprogress"
            >
              <div className="progress-widget">
                <div className="progress-info">
                  <IconTrophy size={16} style={{ color: 'var(--accent-gold)' }} />
                  <span>
                    {t('header.overall.text', {
                      unlocked: fmt.number(unlockedTrophiesAllGames),
                      total: fmt.number(totalTrophiesAllGames),
                      percent: fmt.percent(overallPercent),
                    })}
                  </span>
                </div>
                <ProgressBar percent={overallPercent} label={t('header.overall.progress')} />
              </div>
            </Tooltip>

            <SteamAccountChip profile={steamProfile} />

            <span className="pv-hdivider" aria-hidden="true" />

            {/* Games come from the Steam library only (the star in "All games"); what is added here is a guide file */}
            <div className="pv-hgroup" role="toolbar" aria-label={t('header.importGroup')}>
              <IconButton
                label={t('header.importGuide.label')}
                description={t('header.importGuide.description')}
                busy={importing}
                onClick={() => importInputRef.current?.click()}
              >
                <IconFileImport size={18} />
              </IconButton>
            </div>

            <CabinetToolGroup />

            <span className="pv-hdivider" aria-hidden="true" />
            <UiScaleControl />
            <input
              ref={importInputRef}
              type="file"
              accept=".json,application/json"
              className="hidden"
              onChange={(e) => void handleImportFile(e.target.files?.[0])}
            />
          </div>

          {/* Utilities: the same four, in the same place, on every page */}
          <HeaderUtilities theme={theme} onToggleTheme={onToggleTheme} onOpenSettings={(tab) => { setSettingsTab(tab); setSettingsOpen(true) }} onShowHints={hints.show} />
        </div>
      </header>

      {/* Sticky sub-bar: search, view switch, figures and tile size once the hero's controls are out of view */}
      <DashboardSubBar
        open={heroGone}
        search={search}
        onSearchChange={setSearch}
        hasLibrary={hasLibrary}
        view={view}
        onSelectView={selectView}
        favoritesCount={favoriteGames.length}
        libraryCount={librarySummary?.libraryGameCount}
        figures={figures}
        density={density}
        onDensityChange={handleDensityChange}
      />

      {/* Main Content */}
      <LiveCountProvider counts={liveCounts}>
      <main className="max-w-[var(--pv-page-max)] w-full mx-auto px-4 sm:px-6 py-7 flex-1 space-y-6">
        {/* Compact Live Running Game Notice (if a Steam game is currently running) */}
        {runningVaultGame && (
          <div className="pv-alert pv-alert-emerald flex flex-wrap items-center justify-between gap-4">
            <div className="flex items-center gap-3">
              <span
                className="h-3 w-3 rounded-full animate-pulse shrink-0"
                style={{ backgroundColor: 'var(--accent-emerald)' }}
              />
              <div>
                <div className="font-extrabold text-sm">
                  {t('live.title', { title: runningVaultGame.title })}
                </div>
                <div className="text-xs" style={{ color: 'var(--text-secondary)' }}>
                  {t('live.text')}
                </div>
              </div>
            </div>
            <Link
              to={`/games/${runningVaultGame.slug}`}
              className="pv-btn pv-btn-emerald shrink-0"
            >
              <IconTrophy size={15} />
              <span>{t('live.open')}</span>
            </Link>
          </div>
        )}

        {/* The overview panel at the top, in the language of the chapter navigation: a slim head (small caps title, the library figures
            as compact chips), one calm row of controls (search, view, tile size) and a count line. It is what the sub-bar takes over
            from once it has scrolled out of view. */}
        <section ref={heroRef} className="pv-dhub">
          <div className="pv-dhub-head">
            <h2 className="pv-dhub-title">{t('hero.title')}</h2>
            <div className="pv-dhub-figures">
              <DashboardFigures {...figures} compact />
            </div>
          </div>

          <div className="pv-dhub-body" data-library={hasLibrary || reserveViewSwitch ? 'true' : 'false'}>
            <div className="relative">
              <label htmlFor="dashboard-game-search" className="sr-only">
                {t('hero.searchLabel')}
              </label>
              <IconSearch size={16} className="absolute left-3 top-1/2 -translate-y-1/2" style={{ color: 'var(--text-muted)' }} aria-hidden="true" />
              <input
                id="dashboard-game-search"
                type="search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder={t('hero.searchPlaceholder')}
                className="pv-input pv-input-icon w-full"
              />
            </div>
            {/* Favourites = the starred tiles; "All games" = the whole Steam library (needs the Steam key) */}
            {hasLibrary && (
              <ViewSwitch
                view={view}
                onSelect={selectView}
                favoritesCount={favoriteGames.length}
                libraryCount={librarySummary?.libraryGameCount}
              />
            )}
            {reserveViewSwitch && <div className="pv-dhub-switch-slot" aria-hidden="true" />}
            <div className="pv-dhub-slider">
              <TileSizeSlider value={density} onChange={handleDensityChange} />
            </div>
            <div className="pv-dhub-meta">
              <div className="pv-dhub-count">
                <div className="pv-dhub-countline">
                  <span className="inline-flex items-center gap-1.5">
                    {view === 'all'
                      ? t('hero.showingLibrary', { shown: fmt.number(filteredLibrary.length), count: library?.games.length ?? 0 })
                      : t('hero.showingFavorites', { shown: fmt.number(filteredGames.length), count: favoriteGames.length })}
                    {view === 'favorites' && favoriteGames.length > 1 && (
                      <Tooltip
                        label={sortDisabled ? (dragOffByOrder ? t('hero.sortOffOrder.label') : t('hero.sortOff.label')) : t('hero.sortOn.label')}
                        description={
                          sortDisabled ? (dragOffByOrder ? t('hero.sortOffOrder.description') : t('hero.sortOff.description')) : t('hero.sortOn.description')
                        }
                      >
                        <span
                          tabIndex={0}
                          aria-label={sortDisabled ? (dragOffByOrder ? t('hero.sortOffOrder.aria') : t('hero.sortOff.aria')) : t('hero.sortOn.aria')}
                          className="inline-flex"
                          style={{ color: sortDisabled ? 'var(--text-muted)' : 'var(--accent-crystal)', opacity: sortDisabled ? 0.6 : 1 }}
                        >
                          <IconArrowsSort size={14} />
                        </span>
                      </Tooltip>
                    )}
                  </span>
                  {/* Always in the row (invisible while nothing is filtered), so it appearing never re-wraps the count beside it */}
                  <button
                    type="button"
                    onClick={() => setSearch('')}
                    className={`shrink-0 font-bold underline cursor-pointer ${search ? '' : 'invisible'}`}
                    style={{ color: 'var(--accent-crystal)' }}
                    tabIndex={search ? 0 : -1}
                  >
                    {t('hero.resetFilter')}
                  </button>
                </div>
                {/* How the cards are sorted, and what is still missing for it; the room of the tallest possible text is reserved */}
                <ReservedText className="pv-dhub-sortnote" candidates={sortLineCandidates} aria-live="polite">
                  {sortSentence(activeOrder, activeChoice.direction)}
                  {sortStatus ? ` ${sortStatus}` : ''}
                </ReservedText>
              </div>
              <DashboardSortControl
                key={view}
                view={view}
                choice={activeChoice}
                loading={sortLoading}
                onChange={(next) => dashboardSort.setChoice(view, next)}
              />
            </div>
          </div>
        </section>

        {/* Game tiles in the chosen size (slider in the overview panel) */}
        {view === 'all' ? (
          libraryLoading ? (
            <div className="pv-card flex items-center justify-center gap-2 py-16 text-sm" style={{ color: 'var(--text-muted)' }}>
              <IconLoader2 size={18} className="animate-spin" />
              {t('loading.library')}
            </div>
          ) : (
            <div className={TILE_CONTAINER[density]}>
              {filteredLibrary.map((game) => (
                <LibraryTile
                  key={game.appId}
                  game={game}
                  density={density}
                  busy={busyAppIds.includes(game.appId)}
                  onToggleFavorite={(g) => void handleToggleLibraryFavorite(g)}
                />
              ))}
            </div>
          )
        ) : isLoading ? (
          <div className="pv-card py-16 text-center text-sm" style={{ color: 'var(--text-muted)' }}>
            {t('loading.games')}
          </div>
        ) : favoriteGames.length === 0 ? (
          // Games come from the Steam library only: no game yet = where they come from and what to do first
          <div data-hint={hasLibrary ? undefined : 'dashboard-add'}>
            <DashboardEmptyHelp
              hasLibrary={hasLibrary}
              onOpenSettings={(tab) => {
                setSettingsTab(tab)
                setSettingsOpen(true)
              }}
              onShowLibrary={() => selectView('all')}
            />
          </div>
        ) : (
          <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            onDragStart={handleDragStart}
            onDragEnd={handleDragEnd}
            onDragCancel={() => setDraggingSlug(null)}
            accessibility={{
              announcements,
              screenReaderInstructions: {
                draggable: t('drag.instructions'),
              },
            }}
          >
            <SortableContext items={favoriteSlugs} strategy={rectSortingStrategy}>
              <div className={TILE_CONTAINER[density]}>
                {filteredGames.map((game) => (
                  <SortableGameTile
                    key={game.id}
                    game={game}
                    density={density}
                    dragDisabled={sortDisabled}
                    landed={landedSlug === game.slug}
                    still={stillSlug === game.slug}
                    onToggleFavorite={(g) => void handleToggleFavorite(g)}
                  />
                ))}
              </div>
            </SortableContext>
            <DragOverlay dropAnimation={dropAnimation}>
              {draggingGame ? (
                <div className="pv-drag-overlay">
                  <div className="pv-drag-lift" style={density === 2 ? { borderRadius: 'var(--radius-md)' } : undefined}>
                    <GameTile game={draggingGame} density={density} onToggleFavorite={() => undefined} />
                  </div>
                </div>
              ) : null}
            </DragOverlay>
          </DndContext>
        )}
      </main>
      </LiveCountProvider>

      <AppFooter sticky />

      <FloatingNoticeStack>
        {failure && (
          <FloatingNotice
            key="failure"
            tone="error"
            title={failure.title}
            message={failure.message}
            details={failure.details}
            onClose={() => setFailure(null)}
          />
        )}
      </FloatingNoticeStack>

      <HintTour
        screen="dashboard"
        open={hints.open}
        onClose={hints.close}
        onShowIntro={() => {
          hints.close()
          tour.start()
        }}
      />

      <OnboardingTour
        open={tour.open}
        paused={settingsOpen}
        onClose={tour.finish}
        onOpenSettings={(tab) => {
          setSettingsTab(tab)
          setSettingsOpen(true)
        }}
      />

      <SettingsModal
        open={settingsOpen}
        notice={settingsNotice}
        initialTab={settingsTab}
        onClose={() => {
          setSettingsOpen(false)
          setSettingsNotice(null)
          setSettingsTab(undefined)
        }}
        onSaved={() => {
          void queryClient.invalidateQueries({ queryKey: ['settings'] })
          void queryClient.invalidateQueries({ queryKey: ['steam-profile'] })
          void queryClient.invalidateQueries({ queryKey: ['live-status'] })
          void queryClient.invalidateQueries({ queryKey: ['live-achievements'] })
          // A new Steam key / account makes the library readable: the empty-dashboard help and "All games" pick it up at once
          void queryClient.invalidateQueries({ queryKey: ['library-summary'] })
          void queryClient.invalidateQueries({ queryKey: ['steam-library'] })
          void queryClient.invalidateQueries({ queryKey: ['steam-profile'] })
        }}
      />
    </div>
  )
}
