import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { CopyErrorButton } from '../components/ui/CopyErrorButton'
import { Link, useParams, useSearchParams } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import {
  IconAlertTriangle,
  IconBook,
  IconBook2,
  IconChartBar,
  IconCheck,
  IconClock,
  IconEye,
  IconFileExport,
  IconFileTypePdf,
  IconHome,
  IconInfoCircle,
  IconLanguage,
  IconLink,
  IconRefresh,
  IconSearch,
  IconShieldCheck,
  IconSparkles,
  IconTrophy,
  IconWand,
  IconX,
} from '@tabler/icons-react'
import {
  completeGuide,
  expandTabWithAi,
  fetchAiJob,
  fetchAiJobsForGame,
  fetchGuideTopics,
  gameExportUrl,
  fetchGameDetail,
  fetchLiveAchievements,
  fetchLiveStatus,
  researchLore,
  resetGameProgress,
  redoChapter,
  retryChapters,
  toggleProgressItem,
} from '../api/client'
import { followAiJob } from '../api/aiJobs'
import type { ManualChapterSeed, AiCompleteGuideResponse, AiJobDto, AiJobKind, AiProviderId, AiRedoChapterResponse, AiResultDto, AiRetryChaptersResponse, GameDetailDto, GuideSectionDto, GuideTableDto, GuideTabDto } from '../api/types'
import { useFormat } from '../i18n/format'
import { useGuideTranslation } from '../utils/useGuideTranslation'
import type { UiLanguage } from '../i18n'
import { AppFooter } from '../components/AppFooter'
import { ScrollTopButton } from '../components/ScrollTopButton'
import { useAiPicker } from '../components/ai/AiChoice'
import { AiProgressNotice, AiProgressOverlay } from '../components/ai/AiProgress'
import { AiRunButton, useAiWhoText } from '../components/ai/AiRunButton'
import { FailedChapterBlock, failedChapterSeeds, failedChapterTabs, RetryAllBanner } from '../components/ai/FailedChapter'
import { providersWithKey, retryCandidates } from '../components/ai/aiProviders'
import { ManualGuideDialog } from '../components/ai/manual/ManualGuideDialog'
import { useAiSwitchNotice } from '../components/ai/useAiSwitchNotice'
import { useAiProgress } from '../components/ai/useAiProgress'
import { useAiStatus } from '../components/ai/useAiStatus'
import { ErrorDetails } from '../components/ui/ErrorDetails'
import { useConfirm } from '../components/ui/ConfirmDialog'
import { CabinetToolGroup } from '../components/cabinet/CabinetToolGroup'
import { GameTrophyBar } from '../components/GameTrophyBar'
import { PinsScope, usePinCount, type PinsFailure } from '../components/pins/pinsContext'
import { PinsSurface } from '../components/pins/PinsSurface'
import { ChapterDrawerButton, ChapterSidebar, DRAWER_QUERY } from '../components/gamenav/ChapterSidebar'
import { tabDomId, type ChapterItem } from '../components/gamenav/ChapterTabs'
import { SectionTabs, type SectionItem } from '../components/gamenav/SectionTabs'
import { TwitchButton } from '../components/TwitchButton'
import { GameStatsPanel } from '../components/GameStatsPanel'
import { GuideBuilderDialog } from '../components/GuideBuilderDialog'
import { SteamLinkDialog } from '../components/SteamLinkDialog'
import { LiveTrackerPanel } from '../components/LiveTrackerPanel'
import { HeaderUtilities } from '../components/HeaderUtilities'
import { HintTour } from '../components/onboarding/HintTour'
import { useScreenHints } from '../components/onboarding/useScreenHints'
import { FloatingNotice, FloatingNoticeStack } from '../components/ui/FloatingNotice'
import { ReservedText } from '../components/ui/ReservedText'
import { iconButtonClass, IconButton } from '../components/ui/IconButton'
import { Switch } from '../components/ui/Switch'
import { Tooltip } from '../components/ui/Tooltip'
import { UiScaleControl } from '../components/ui/UiScaleControl'
import { PlayButton } from '../components/launch/PlayButton'
import { AchievementLookupProvider } from '../components/guide/achievements'
import { BossCard } from '../components/guide/BossCard'
import { ChapterBody } from '../components/guide/ChapterBody'
import { GuideHeading, HintAlert, StepsStory, isGuideAlertKind } from '../components/guide/GuideBlocks'
import { OverviewBlock } from '../components/guide/OverviewBlock'
import { ItemLinkContext, RowLinks, useItemLinks } from '../components/guide/ItemLinks'
import type { ItemLinkApi } from '../components/guide/ItemLinks'
import { buildItemLinkIndex } from '../components/guide/linkIndex'
import { SideQuestTargetContext } from '../components/guide/SideQuestsAlert'
import type { SideQuestTarget } from '../components/guide/SideQuestsAlert'
import { SourcesBlock } from '../components/guide/SourcesBlock'
import { findSideQuestChapter } from '../components/guide/sideQuestChapter'
import { PuzzleBlock } from '../components/guide/PuzzleBlock'
import { RemoveChapterBar } from '../components/guide/RemoveChapterBar'
import { useConfirmRedoChapter } from '../components/guide/useRedoChapter'
import { useRemoveChapter } from '../components/guide/useRemoveChapter'
import { PuzzleScopeContext } from '../components/guide/puzzleState'
import { OnboardingTour } from '../components/onboarding/OnboardingTour'
import { useOnboardingTour } from '../components/onboarding/useOnboardingTour'
import { SettingsModal, type SettingsTab } from '../components/SettingsModal'
import { describeError } from '../utils/apiErrorText'
import { errorCodeOf } from '../utils/errorReport'
import { useAchievementNotes } from '../utils/useAchievementNotes'
import { useMediaQuery } from '../utils/useMediaQuery'
import { useStickyHeaderHeight } from '../utils/useStickyHeaderHeight'

interface GameGuidePageProps {
  theme: string
  onToggleTheme: () => void
}

// Remembered per browser: the side nav collapsed to numbers
const NAV_COLLAPSED_KEY = 'pv_game_nav_collapsed_v1'

const LIVE_TROPHIES_TAB_SLUG = '__live_steam_trophies__'
const STATS_TAB_SLUG = '__steam_stats__'
// Slug of the optional Story & Lore tab created by POST /api/games/{slug}/ai-lore
const LORE_TAB_SLUG = 'story-lore'

// The active tab lives in the URL (?tab=...), so a link or a reload lands on the same chapter / section. The sections have
// short names there; a chapter uses its own slug.
const TAB_PARAM_TO_SLUG: Record<string, string> = { live: LIVE_TROPHIES_TAB_SLUG, stats: STATS_TAB_SLUG, story: LORE_TAB_SLUG }
const slugToTabParam = (slug: string) =>
  slug === LIVE_TROPHIES_TAB_SLUG ? 'live' : slug === STATS_TAB_SLUG ? 'stats' : slug === LORE_TAB_SLUG ? 'story' : slug

// The language the guide content is written in (GameDetailDto.contentLanguage); anything unknown counts as "not known" = no hint
function contentLanguageOf(game: GameDetailDto): UiLanguage | null {
  const value = game.contentLanguage?.toLowerCase()
  return value === 'de' || value === 'en' ? value : null
}

// What the floating notices on this page show. The reason (and the provider's raw text) is kept apart from the title.
interface PageNotice {
  title: string
  message?: string
  details?: string | null
  // Offers a button that opens the settings on the AI tab (a failed AI research is mostly a key or quota question)
  aiSettings?: boolean
  // Offers "try again with <another AI>" (only when another provider has a key)
  retry?: { label: string; run: () => void }
  // Offers the guide with the player's own AI subscription (copy the prompt, paste the answer; no API key)
  manual?: boolean
}

// Dedicated 100% Master Companion Page for a single selected game in the harmonious 100-Prozent Pastel design.
export function GameGuidePage({ theme, onToggleTheme }: GameGuidePageProps) {
  const { t } = useTranslation(['game', 'common', 'ai'])
  const { language } = useFormat()
  const { slug = '' } = useParams()
  const queryClient = useQueryClient()
  const confirm = useConfirm()
  const confirmRemoveChapter = useRemoveChapter(slug)
  const confirmRedoChapter = useConfirmRedoChapter()
  const [removingChapter, setRemovingChapter] = useState(false)
  // "Kapitel neu recherchieren" (API key path) is running
  const [redoing, setRedoing] = useState(false)
  // A side quest callout asked for a row of the side quest chapter: once that chapter is shown, the row is scrolled into view and flashes briefly
  const [pendingRowRef, setPendingRowRef] = useState<string | null>(null)
  // A link asked for a heading of a chapter ("where do I get this"): same handling as the row, with the heading's element id
  const [pendingAnchorId, setPendingAnchorId] = useState<string | null>(null)
  // Which AI does an action: the chooser of the "ask" mode, the state of the providers and the notice about a change of provider
  const pickAi = useAiPicker()
  const { data: aiStatus } = useAiStatus()
  const aiNotice = useAiSwitchNotice()
  const aiWho = useAiWhoText()
  const isDrawer = useMediaQuery(DRAWER_QUERY)

  const [searchParams, setSearchParams] = useSearchParams()
  const [activeTabSlug, setActiveTabSlug] = useState<string>(() => {
    const param = searchParams.get('tab')
    return param ? (TAB_PARAM_TO_SLUG[param] ?? param) : ''
  })
  // Every tab change (chapter bar, section tabs, buttons) goes through here and is written to the URL without a history entry
  const selectTab = (slug: string) => {
    setActiveTabSlug(slug)
    setSearchParams(
      (previous) => {
        const next = new URLSearchParams(previous)
        next.set('tab', slugToTabParam(slug))
        return next
      },
      { replace: true }
    )
  }
  useEffect(() => {
    if (!pendingRowRef) return
    const row = document.querySelector<HTMLElement>(`[data-item-id="${CSS.escape(pendingRowRef)}"]`)
    if (!row) return
    setPendingRowRef(null)
    const calm = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    row.scrollIntoView({ behavior: calm ? 'auto' : 'smooth', block: 'center' })
    // Keyboard users continue from the row they were sent to
    row.setAttribute('tabindex', '-1')
    row.focus({ preventScroll: true })
    row.classList.add('pv-row-flash')
    // No cleanup on purpose: the pending reference is cleared above, which would cancel the timer that takes the highlight away again
    window.setTimeout(() => row.classList.remove('pv-row-flash'), 2400)
  }, [pendingRowRef, activeTabSlug])
  useEffect(() => {
    if (!pendingAnchorId) return
    const heading = document.getElementById(pendingAnchorId)
    if (!heading) return
    setPendingAnchorId(null)
    const calm = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    heading.scrollIntoView({ behavior: calm ? 'auto' : 'smooth', block: 'start' })
    heading.focus({ preventScroll: true })
    heading.classList.add('pv-anchor-flash')
    window.setTimeout(() => heading.classList.remove('pv-anchor-flash'), 2400)
  }, [pendingAnchorId, activeTabSlug])
  const headerRef = useStickyHeaderHeight<HTMLElement>()
  const [coverFailed, setCoverFailed] = useState(false)
  const [navCollapsed, setNavCollapsed] = useState(() => {
    try {
      return localStorage.getItem(NAV_COLLAPSED_KEY) === '1'
    } catch {
      return false
    }
  })
  const toggleNavCollapsed = () =>
    setNavCollapsed((previous) => {
      try {
        localStorage.setItem(NAV_COLLAPSED_KEY, previous ? '0' : '1')
      } catch {
        // Storage unavailable (private mode): the choice holds for this visit only
      }
      return !previous
    })
  // Phone: the chapter drawer
  const [drawerOpen, setDrawerOpen] = useState(false)
  const closeDrawer = useCallback(() => setDrawerOpen(false), [])

  // AI Expand Tab Modal state
  const [expandOpen, setExpandOpen] = useState(false)
  const [newTabTitle, setNewTabTitle] = useState('')
  const [newTabPrompt, setNewTabPrompt] = useState('')
  // The AI picked for this one action in the dialog ("Mit Claude"); the setting itself stays as it is
  const [expandOneOff, setExpandOneOff] = useState<AiProviderId | undefined>(undefined)
  const [expanding, setExpanding] = useState(false)
  // The reason a chapter could not be created, shown completely (with the provider's own text behind "technical details")
  const [expandError, setExpandError] = useState<{ message: string; details: string | null; notice?: string } | null>(null)
  // Live progress of the AI actions: the ones that run as a notice on the page (complete guide, Story & Lore) and the dialog's own
  const pageProgress = useAiProgress()
  const expandProgress = useAiProgress()
  const [pageJobKind, setPageJobKind] = useState<AiJobKind>('complete')
  const [actionError, setActionError] = useState<PageNotice | null>(null)
  const [actionInfo, setActionInfo] = useState<PageNotice | null>(null)
  // A failed pin action ("Im Blick"): the same notice as every other failed action, with the server's complete text and the copy button
  const showPinFailure = useCallback((failure: PinsFailure) => setActionError({ title: failure.title, message: failure.message, details: failure.details }), [])
  const [completing, setCompleting] = useState(false)
  // Failed chapters are being researched again
  const [retrying, setRetrying] = useState(false)
  const [steamLinkOpen, setSteamLinkOpen] = useState(false)
  const [builderOpen, setBuilderOpen] = useState(false)
  // The guide (or one failed chapter) with the player's own AI subscription: prompts to copy, answers to paste
  const [manual, setManual] = useState<{ chapter?: ManualChapterSeed; chapters?: ManualChapterSeed[] } | null>(null)
  // The AI chosen when the builder was opened (mode "ask"); undefined = the settings decide
  const [builderProvider, setBuilderProvider] = useState<string | undefined>(undefined)
  // Onboarding tour ("?" button in the header); its last step can open the settings right here
  const tour = useOnboardingTour()
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [settingsTab, setSettingsTab] = useState<SettingsTab | undefined>(undefined)
  // Coach marks of this page: the first time it is seen (and while no intro / dialog is up), and on "?" any time
  const hints = useScreenHints('game', tour.open || settingsOpen || expandOpen || steamLinkOpen || builderOpen || manual !== null)
  // "Restart the introduction" in the settings closes the settings first, then the tour opens
  useEffect(() => {
    const close = () => setSettingsOpen(false)
    window.addEventListener('pv-start-tour', close)
    return () => window.removeEventListener('pv-start-tour', close)
  }, [])

  const {
    data: game,
    isLoading,
    error,
  } = useQuery({
    queryKey: ['game', slug],
    queryFn: () => fetchGameDetail(slug),
    enabled: Boolean(slug),
  })
  // Who points at whom across the chapters (linked checkboxes and back-references)
  const itemLinkIndex = useMemo(() => (game ? buildItemLinkIndex(game.tabs, game) : null), [game])

  // The standard topics are picked as chapter titles of THIS guide: in the guide's own language
  const topicLanguage = game?.contentLanguage
  const { data: topics } = useQuery({
    queryKey: ['guide-topics', topicLanguage ?? language],
    queryFn: () => fetchGuideTopics(topicLanguage ?? language),
    // Not before the guide is known: its language is what counts, the interface language would ask for the wrong list
    enabled: Boolean(game),
    staleTime: Infinity,
  })

  // Opened in the other language than it was written in, the guide is translated by the user's AI in the background
  const translation = useGuideTranslation(slug, game, language)
  const [failureDismissed, setFailureDismissed] = useState(false)
  useEffect(() => {
    if (translation.phase !== 'failed') setFailureDismissed(false)
  }, [translation.phase])
  // The translation job had to change AI provider: the same notice as for every other AI action (once per sentence)
  const showAiNotice = aiNotice.show
  useEffect(() => {
    if (translation.aiNotice) showAiNotice(translation.aiNotice)
  }, [translation.aiNotice, showAiNotice])

  // Personal notes on achievements can be switched off in the settings (then the tab's description does not mention them)
  const { enabled: notesEnabled } = useAchievementNotes()
  const hasGuide = Boolean(game)
  const failedChapters = failedChapterTabs(game)
  const showLiveTrophiesTab = Boolean(game?.steamAppId)
  const showStatsTab = Boolean(game?.steamAppId)
  // Story & Lore is offered on every game page; its spoiler gate asks per game whether to show it
  const loreTab = game?.tabs.find((t) => t.slug === LORE_TAB_SLUG)
  // Guide chapters without the lore tab (own nav entry) and without the Steam achievement
  // list: Steam reports those unlocks itself, the list only feeds the tips of the live trophies tab
  const guideTabs = game ? game.tabs.filter((t) => t.slug !== LORE_TAB_SLUG && !t.isAchievementList) : []

  // Trophy progress for the header and the "Im Blick" cards, straight from Steam (same query as the live trophies tab, so no extra requests).
  // Faster (3 s) only while THIS game runs and has pinned achievements, so a pinned counter moves on every tab; otherwise the calm 15 s.
  // (The live tab brings its own faster pace while it is open.)
  const pinCount = usePinCount(game?.steamAppId)
  const { data: liveStatus } = useQuery({ queryKey: ['live-status'], queryFn: fetchLiveStatus, refetchInterval: 4000 })
  const { data: liveTrophies } = useQuery({
    queryKey: ['live-achievements', game?.steamAppId ?? 'auto'],
    queryFn: () => fetchLiveAchievements(game?.steamAppId ?? undefined),
    enabled: Boolean(game?.steamAppId),
    refetchInterval: pinCount > 0 && game?.steamAppId && liveStatus?.runningAppId === game.steamAppId ? 3000 : 15000,
  })

  useEffect(() => {
    setCoverFailed(false)
  }, [slug])

  useEffect(() => {
    if (!game) return
    const onVirtualTab =
      (activeTabSlug === LIVE_TROPHIES_TAB_SLUG && showLiveTrophiesTab) ||
      (activeTabSlug === STATS_TAB_SLUG && showStatsTab) ||
      activeTabSlug === LORE_TAB_SLUG
    if (onVirtualTab) return
    if (!guideTabs.some((t) => t.slug === activeTabSlug)) {
      setActiveTabSlug(
        guideTabs[0]?.slug ?? (showLiveTrophiesTab ? LIVE_TROPHIES_TAB_SLUG : showStatsTab ? STATS_TAB_SLUG : '')
      )
    }
  }, [game, activeTabSlug, hasGuide, showLiveTrophiesTab, showStatsTab])

  // Spoiler gate of the lore tab: revealed per game, optionally remembered in this browser
  const spoilerKey = `pv_lore_revealed_${slug}`
  const [loreRevealed, setLoreRevealed] = useState(false)
  useEffect(() => {
    try {
      setLoreRevealed(localStorage.getItem(spoilerKey) === '1')
    } catch {
      setLoreRevealed(false)
    }
  }, [spoilerKey])
  const revealLore = (remember: boolean) => {
    setLoreRevealed(true)
    if (remember) {
      try {
        localStorage.setItem(spoilerKey, '1')
      } catch {
        // Storage unavailable (private mode) - reveal for this visit only
      }
    }
  }

  // "Try again with <another AI>" for a failed AI action: only offered when another provider has a key. `failed` is what the server
  // says failed, else the provider the action was started with, else the one the settings would use.
  const retryOffer = (err: unknown, attempted: string | undefined, run: (provider: string) => void): PageNotice['retry'] => {
    const failed = (err as { aiProvider?: string } | null)?.aiProvider ?? attempted ?? aiStatus?.activeProvider ?? undefined
    const next = retryCandidates(aiStatus, failed)[0]
    return next ? { label: t('ai:notice.retryWith', { provider: t(`ai:providers.${next.id}`) }), run: () => run(next.id) } : undefined
  }

  const [loreBusy, setLoreBusy] = useState(false)
  // `chosen` = the AI was named by the player already (a retry button): no chooser dialog in the mode "ask"
  const handleResearchLore = async (oneOff?: string, chosen = false) => {
    if (!game) return
    const provider = chosen ? oneOff : await pickAi(oneOff)
    if (provider === null) return
    setLoreBusy(true)
    setPageJobKind('lore')
    setActionError(null)
    try {
      const result = await researchLore(game.slug, provider, pageProgress.onProgress)
      queryClient.setQueryData(['game', slug], result.game)
      aiNotice.report(result.ai)
      selectTab(LORE_TAB_SLUG)
    } catch (err: unknown) {
      showAiFailure(err, t('fail.lore'), provider, (next) => void handleResearchLore(next, true))
    } finally {
      setLoreBusy(false)
      pageProgress.reset()
      aiNotice.refresh()
    }
  }

  // A failed AI action: the reason in full (our sentence, the provider's text behind "technical details"), the note that another AI had been
  // tried first, the offers to try again with another AI or to look at the AI settings. What was finished before is saved: read the guide again.
  const showAiFailure = (err: unknown, title: string, attempted: string | undefined, retry: (provider: string) => void, manual = false) => {
    const switched = (err as { switchNotice?: string } | null)?.switchNotice
    if (switched) aiNotice.show(switched)
    void queryClient.invalidateQueries({ queryKey: ['game', slug] })
    setActionError({
      title,
      ...describeError(err, t('common:unknownError')),
      aiSettings: true,
      manual,
      retry: retryOffer(err, attempted, retry),
    })
  }

  // The job of an AI action that was already running when the page opened (reload, other tab): follow it with the same notice and result handling
  const resumedJob = useRef<string | null>(null)
  useEffect(() => {
    if (!slug) return
    let cancelled = false
    void fetchAiJobsForGame(slug)
      .then((jobs) => {
        const running = jobs.find((job) => job.running && (job.kind === 'complete' || job.kind === 'lore' || job.kind === 'expand' || job.kind === 'retry' || job.kind === 'redo'))
        if (!running || cancelled || resumedJob.current === running.id) return
        resumedJob.current = running.id
        void resumeAiJob(running)
      })
      .catch(() => {
        // Nothing to resume (the server may not answer right now): the page works as before
      })
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [slug])

  const resumeAiJob = async (job: AiJobDto) => {
    setPageJobKind(job.kind)
    pageProgress.onProgress(job)
    if (job.kind === 'lore') setLoreBusy(true)
    else if (job.kind === 'retry') setRetrying(true)
    else if (job.kind === 'redo') setRedoing(true)
    else setCompleting(true)
    try {
      const result = await followAiJob<AiCompleteGuideResponse | AiRetryChaptersResponse | AiResultDto<GameDetailDto>>(job, fetchAiJob, pageProgress.onProgress)
      if ('addedCount' in result) {
        handleGuideCompleted(result)
      } else if ('retried' in result) {
        handleChaptersRetried(result)
      } else {
        queryClient.setQueryData(['game', slug], result.game)
        aiNotice.report(result.ai)
        void queryClient.invalidateQueries({ queryKey: ['games'] })
        if (job.kind === 'lore') selectTab(LORE_TAB_SLUG)
      }
    } catch (err: unknown) {
      showAiFailure(err, job.kind === 'lore' ? t('fail.lore') : job.kind === 'retry' ? t('ai:retryChapters.failTitle') : job.kind === 'redo' ? t('ai:redoChapter.failed') : t('fail.complete'), undefined, () => {}, job.kind !== 'lore')
    } finally {
      setLoreBusy(false)
      setCompleting(false)
      setRetrying(false)
      setRedoing(false)
      pageProgress.reset()
      aiNotice.refresh()
    }
  }

  const handleToggleCheck = async (itemKey: string, checked: boolean) => {
    if (!game) return
    setActionError(null)
    // Guide checklists are extras: ticking them never starts the platinum moment (that belongs to Steam's trophies)
    // Optimistic: the key is the one check shared by a row and the rows that refer to it, so every place on the page and in other chapters flips at once
    const previous = queryClient.getQueryData<GameDetailDto>(['game', slug])
    if (previous) {
      const keys = new Set(previous.checkedItemKeys)
      if (checked) keys.add(itemKey)
      else keys.delete(itemKey)
      queryClient.setQueryData<GameDetailDto>(['game', slug], { ...previous, checkedItemKeys: [...keys] })
    }
    try {
      await toggleProgressItem(game.slug, itemKey, checked)
      await queryClient.invalidateQueries({ queryKey: ['game', slug] })
    } catch (err: unknown) {
      if (previous) queryClient.setQueryData<GameDetailDto>(['game', slug], previous)
      setActionError({ title: t('fail.check'), ...describeError(err, t('common:unknownError')) })
    }
  }

  const handleResetProgress = async () => {
    if (!game) return
    const ok = await confirm({
      title: t('resetConfirm.title'),
      message: t('resetConfirm.message', { title: game.title }),
      confirmLabel: t('resetConfirm.confirm'),
      tone: 'danger',
    })
    if (!ok) return
    setActionError(null)
    try {
      await resetGameProgress(game.slug)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['game', slug] }),
        queryClient.invalidateQueries({ queryKey: ['games'] }),
      ])
    } catch (err: unknown) {
      setActionError({ title: t('fail.reset'), ...describeError(err, t('common:unknownError')) })
    }
  }

  // Quick path without the builder (empty guide): every applicable standard topic is researched
  const handleCompleteGuide = async (oneOff?: string, chosen = false) => {
    if (!game) return
    const provider = chosen ? oneOff : await pickAi(oneOff)
    if (provider === null) return
    setCompleting(true)
    setPageJobKind('complete')
    setActionError(null)
    setActionInfo(null)
    try {
      const result = await completeGuide(game.slug, undefined, provider, pageProgress.onProgress)
      handleGuideCompleted(result)
    } catch (err: unknown) {
      showAiFailure(err, t('fail.complete'), provider, (next) => void handleCompleteGuide(next, true), true)
    } finally {
      setCompleting(false)
      pageProgress.reset()
      aiNotice.refresh()
    }
  }

  // The guide builder analyses the game with the AI first, so in the mode "ask" the player chooses before it opens
  // Without any API key the AI analysis cannot run: the guide goes through the player's own subscription
  const noAiKey = aiStatus !== undefined && providersWithKey(aiStatus).length === 0

  const openBuilder = async () => {
    if (noAiKey) {
      setManual({})
      return
    }
    const provider = await pickAi()
    if (provider === null) return
    setBuilderProvider(provider)
    setBuilderOpen(true)
  }

  // "Kapitel erneut recherchieren" (tabIds = the chapters of the buttons; null = every failed chapter of the guide): the job replaces the placeholders
  // chapter by chapter; what is finished stays even if it stops halfway
  const handleRetryChapters = async (tabIds: string[] | null, oneOff?: string, chosen = false) => {
    if (!game) return
    const provider = chosen ? oneOff : await pickAi(oneOff)
    if (provider === null) return
    setRetrying(true)
    setPageJobKind('retry')
    setActionError(null)
    setActionInfo(null)
    try {
      handleChaptersRetried(await retryChapters(game.slug, tabIds, provider, pageProgress.onProgress))
    } catch (err: unknown) {
      showAiFailure(err, t('ai:retryChapters.failTitle'), provider, (next) => void handleRetryChapters(tabIds, next, true), true)
    } finally {
      setRetrying(false)
      pageProgress.reset()
      aiNotice.refresh()
    }
  }

  // "Kapitel entfernen" for the chapter that is shown (placeholder of a failed research or real content): our own question first, then the guide
  // without it; the player lands on the chapter that followed it (else the one before, else the live trophies)
  const handleRemoveChapter = async (tab: GuideTabDto, placeholder: boolean) => {
    if (!game || removingChapter) return
    setRemovingChapter(true)
    setActionError(null)
    setActionInfo(null)
    try {
      const updated = await confirmRemoveChapter({ id: tab.id, title: tab.title, placeholder })
      if (!updated) return
      const at = guideTabs.findIndex((candidate) => candidate.id === tab.id)
      const neighbour = guideTabs[at + 1] ?? guideTabs[at - 1]
      selectTab(neighbour?.slug ?? (showLiveTrophiesTab ? LIVE_TROPHIES_TAB_SLUG : showStatsTab ? STATS_TAB_SLUG : ''))
      setActionInfo({ title: t('ai:removeChapter.doneTitle'), message: t('ai:removeChapter.done', { title: tab.title }) })
    } catch (err: unknown) {
      setActionError({ title: placeholder ? t('ai:failedChapter.removeFailed') : t('ai:removeChapter.failed'), ...describeError(err, t('common:unknownError')) })
    } finally {
      setRemovingChapter(false)
    }
  }

  // "Kapitel neu recherchieren" for the chapter that is shown: our own question first (the old version stays until the new one is finished), then the AI
  // chooser in the mode "ask", then the job; the chapter is replaced in the guide when it is done
  const handleRedoChapter = async (tab: GuideTabDto, oneOff?: string, chosen = false) => {
    if (!game || redoing) return
    if (!chosen && !(await confirmRedoChapter(tab.title, false))) return
    const provider = chosen ? oneOff : await pickAi(oneOff)
    if (provider === null) return
    setRedoing(true)
    setPageJobKind('redo')
    setActionError(null)
    setActionInfo(null)
    try {
      handleChapterRedone(await redoChapter(game.slug, tab.id, provider, pageProgress.onProgress), tab)
    } catch (err: unknown) {
      showAiFailure(err, t('ai:redoChapter.failed'), provider, (next) => void handleRedoChapter(tab, next, true), true)
    } finally {
      setRedoing(false)
      pageProgress.reset()
      aiNotice.refresh()
    }
  }

  // The same with the player's own AI subscription: after the question the dialog opens with this chapter as its one seed (the answer replaces the chapter)
  const handleRedoChapterManual = async (tab: GuideTabDto) => {
    if (!(await confirmRedoChapter(tab.title, true))) return
    setManual({ chapter: { title: tab.title, icon: tab.icon, focus: tab.title, topicId: tab.topicId ?? null, tabId: tab.id, replace: true } })
  }

  const handleChapterRedone = (result: AiRedoChapterResponse, tab: GuideTabDto) => {
    aiNotice.report(result.ai)
    queryClient.setQueryData(['game', slug], result.game)
    void queryClient.invalidateQueries({ queryKey: ['games'] })
    setActionInfo({ title: t('ai:redoChapter.doneTitle'), message: t('ai:redoChapter.done', { title: tab.title }) })
  }

  const handleChaptersRetried = (result: AiRetryChaptersResponse) => {
    aiNotice.report(result.ai)
    queryClient.setQueryData(['game', slug], result.game)
    void queryClient.invalidateQueries({ queryKey: ['games'] })
    // Chapters whose topic the game does not have were removed instead of filled: say which
    const dropped = result.droppedTitles?.length ? ` ${t('ai:retryChapters.dropped', { titles: result.droppedTitles.join(', ') })}` : ''
    setActionInfo(
      result.failedTitles.length === 0
        ? { title: t('ai:retryChapters.doneTitle'), message: result.succeeded > 0 ? t('ai:retryChapters.done', { count: result.succeeded }) + dropped : dropped.trim() }
        : {
            title: t('ai:retryChapters.partialTitle'),
            message: t('ai:retryChapters.partial', { done: result.succeeded, total: result.retried, titles: result.failedTitles.join(', ') }) + dropped,
          }
    )
  }

  const handleGuideCompleted = (result: AiCompleteGuideResponse) => {
    aiNotice.report(result.ai)
    queryClient.setQueryData(['game', slug], result.game)
    void queryClient.invalidateQueries({ queryKey: ['games'] })
    const dropped = result.droppedTitles?.length ? ` ${t('notice.completed.dropped', { titles: result.droppedTitles.join(', ') })}` : ''
    setActionInfo(
      result.addedCount > 0
        ? { title: t('notice.completed.title'), message: t('notice.completed.added', { count: result.addedCount, titles: result.addedTitles.join(', ') }) + dropped }
        : { title: t('notice.nothingToAdd.title'), message: t('notice.nothingToAdd.message') + dropped }
    )
  }

  const handleExpandTab = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!game) return
    if (!newTabTitle.trim() || !newTabPrompt.trim()) {
      setExpandError({ message: t('expand.missing'), details: null })
      return
    }
    const provider = await pickAi(expandOneOff)
    if (provider === null) return
    setExpanding(true)
    setExpandError(null)
    try {
      const result = await expandTabWithAi(
        game.slug,
        { tabTitle: newTabTitle.trim(), topicPrompt: newTabPrompt.trim(), providerOverride: provider },
        expandProgress.onProgress
      )
      const updated = result.game
      queryClient.setQueryData(['game', slug], updated)
      aiNotice.report(result.ai)
      await queryClient.invalidateQueries({ queryKey: ['games'] })
      const lastTab = updated.tabs[updated.tabs.length - 1]
      if (lastTab) selectTab(lastTab.slug)
      setExpandOpen(false)
      setNewTabTitle('')
      setNewTabPrompt('')
      setExpandOneOff(undefined)
    } catch (err: unknown) {
      const { message, details } = describeError(err, t('expand.failedFallback'))
      setExpandError({ message, details, notice: (err as { switchNotice?: string } | null)?.switchNotice })
      void queryClient.invalidateQueries({ queryKey: ['game', slug] })
    } finally {
      setExpanding(false)
      expandProgress.reset()
      aiNotice.refresh()
    }
  }

  if (isLoading) {
    return (
      <div
        className="min-h-screen flex items-center justify-center text-sm font-semibold"
        style={{ color: 'var(--text-muted)' }}
      >
        {t('loading')}
      </div>
    )
  }

  if (error || !game) {
    return (
      <div className="min-h-screen flex flex-col items-center justify-center gap-4 p-6">
        <p
          className="text-base font-bold"
          style={{ color: 'var(--accent-crimson)' }}
        >
          {t('loadFailed')}
        </p>
        <CopyErrorButton report={{ message: t('loadFailed'), details: error instanceof Error ? error.message : null, code: errorCodeOf(error) }} />
        <Link to="/" className="pv-btn pv-btn-primary">
          <IconHome size={16} />
          <span>{t('backHome')}</span>
        </Link>
      </div>
    )
  }

  const checkedSet = new Set(game.checkedItemKeys)
  // Achievement how-tos from the guide's Steam achievement table (item id = Steam API name)
  const achievementTips: Record<string, string> = {}
  for (const tab of game.tabs) {
    for (const section of tab.sections) {
      if (section.table?.tableId !== 'steam-achievements') continue
      for (const row of section.table.rows) {
        const tip = row.cells.tip?.trim()
        const condition = /^\((Geheim|Secret)\)/.test(row.cells.description ?? '') ? row.cells.description : ''
        const text = [condition, tip].filter(Boolean).join(' – ')
        if (text) achievementTips[row.itemId] = text
      }
    }
  }
  const activeTab = hasGuide
    ? (guideTabs.find((tab) => tab.slug === activeTabSlug) ?? guideTabs[0])
    : undefined
  // The guide and lore texts are data in the language they were written in; say so when that is not the language of the interface
  // The side quest chapter of this guide (callouts in the walkthrough link to it, and to a row of its list when the callout names one)
  const questChapter = findSideQuestChapter(guideTabs, activeTab?.id)
  const sideQuestTarget: SideQuestTarget | null = questChapter
    ? {
        slug: questChapter.slug,
        title: questChapter.title,
        hasRow: (ref) => questChapter.sections.some((section) => section.table?.rows.some((row) => row.itemId === ref)),
        open: (ref) => {
          setPendingRowRef(ref ?? null)
          selectTab(questChapter.slug)
        },
      }
    : null
  const itemLinkApi: ItemLinkApi | null = itemLinkIndex
    ? {
        index: itemLinkIndex,
        isChecked: (key) => checkedSet.has(key),
        toggle: handleToggleCheck,
        openRow: (itemId) => {
          const location = itemLinkIndex.target(itemId)
          if (!location) return
          setPendingRowRef(itemId)
          selectTab(location.tabSlug)
        },
        openAnchor: (tabSlug, anchorId) => {
          setPendingAnchorId(anchorId)
          selectTab(tabSlug)
        },
      }
    : null
  const guideContentLanguage = contentLanguageOf(game)
  // The answer holds the translated guide (its texts are in the UI language, not in the language it was written in)
  const isTranslatedView = guideContentLanguage !== null && game.language === language && game.language !== guideContentLanguage
  const onLiveOrStats =
    (activeTabSlug === LIVE_TROPHIES_TAB_SLUG && showLiveTrophiesTab) || (activeTabSlug === STATS_TAB_SLUG && showStatsTab)

  // The two navigation levels: the guide chapters (side nav) and the sections next to the guide (trophy bar, right side)
  const chapterItems: ChapterItem[] = guideTabs.map((tab) => ({
    slug: tab.slug,
    title: tab.title.replace(/^\d+\.\s*/, ''),
    checked: tab.checkedItemsCount,
    total: tab.totalCheckableItems,
    groupKey: tab.groupKey ?? null,
  }))
  const sectionItems: SectionItem[] = [
    ...(showLiveTrophiesTab
      ? [
          {
            slug: LIVE_TROPHIES_TAB_SLUG,
            label: t('sections.live.label'),
            fullLabel: t('sections.live.fullLabel'),
            description: notesEnabled ? t('sections.live.descriptionWithNotes') : t('sections.live.description'),
            icon: <IconTrophy size={16} aria-hidden="true" />,
            tone: 'gold' as const,
            hint: 'game-live',
          },
        ]
      : []),
    ...(showStatsTab
      ? [
          {
            slug: STATS_TAB_SLUG,
            label: t('sections.stats.label'),
            fullLabel: t('sections.stats.fullLabel'),
            description: t('sections.stats.description'),
            icon: <IconChartBar size={16} aria-hidden="true" />,
            tone: 'crystal' as const,
          },
        ]
      : []),
    {
      slug: LORE_TAB_SLUG,
      label: t('sections.lore.label'),
      fullLabel: t('sections.lore.fullLabel'),
      description: t('sections.lore.description'),
      icon: <IconBook size={16} aria-hidden="true" />,
      tone: 'purple',
    },
  ]
  // Exactly one of the two (side nav, trophy bar) shows an active tab
  const activeChapterSlug = guideTabs.some((t) => t.slug === activeTabSlug) ? activeTabSlug : null
  const activeSectionSlug = sectionItems.some((item) => item.slug === activeTabSlug) ? activeTabSlug : null

  return (
    <div className="min-h-screen flex flex-col">
      {/* Sticky Top Header (100% Guide Style) */}
      <header ref={headerRef} className="top-header no-print">
        <div className="pv-hbar pv-hbar--game">
          {/* Brand: way home, cover, title and subtitle */}
          <div className="pv-hbrand">
            <Tooltip label={t('header.home.label')} description={t('header.home.description')}>
              <Link to="/" aria-label={t('header.home.label')} className={iconButtonClass('primary')}>
                <IconHome size={18} />
              </Link>
            </Tooltip>

            {game.coverUrl && !coverFailed ? (
              <img
                src={game.coverUrl}
                alt={game.title}
                onError={() => setCoverFailed(true)}
                className="hidden h-10 w-7 shrink-0 rounded-md object-cover sm:block"
                style={{ border: '1px solid var(--border-subtle)' }}
              />
            ) : (
              <img
                src="/favicon.svg"
                alt={t('header.coverAlt')}
                className="hidden h-10 w-10 shrink-0 rounded-lg sm:block"
              />
            )}

            <div className="min-w-0">
              <h1
                className="truncate text-base font-extrabold leading-tight tracking-tight sm:text-lg"
                style={{ color: 'var(--text-primary)' }}
              >
                {game.title}
                <span className="hidden xl:inline"> — {t('header.titleSuffix')}</span>
              </h1>
              {game.subtitle && (
                <p className="hidden truncate text-xs font-medium sm:block" style={{ color: 'var(--text-muted)' }}>
                  {game.subtitle}
                </p>
              )}
            </div>

            {/* Play: pinned to the end of the brand area, right before the tools - always the same place. It exists only while the
                game can be started (bridge connected or Steam on this PC, and the game has a Steam link), so nothing else moves */}
            <span className="pv-hplay">
              <PlayButton variant="button" target={{ kind: 'game', slug: game.slug }} gameName={game.title} unavailable={!game.steamAppId} showRunning steamAppId={game.steamAppId} />
            </span>
          </div>

          {/* Tools: guide tools | view and export | cabinet (the trophy progress lives in the compact bar below the header) */}
          <div className="pv-htools">
            {hasGuide && (
              <>
                <div className="pv-hgroup" role="toolbar" aria-label={t('header.extendGroup')}>
                  <IconButton
                    tone="emerald"
                    label={t('header.complete.label')}
                    description={[t('header.complete.description'), aiWho].filter(Boolean).join(' ')}
                    busy={completing}
                    onClick={() => (guideTabs.length === 0 && !noAiKey ? void handleCompleteGuide() : void openBuilder())}
                  >
                    <IconWand size={18} />
                  </IconButton>

                  <IconButton
                    label={t('header.addTab.label')}
                    description={[t('header.addTab.description'), aiWho].filter(Boolean).join(' ')}
                    onClick={() => setExpandOpen(true)}
                  >
                    <IconSparkles size={18} />
                  </IconButton>
                </div>
                <span className="pv-hdivider" aria-hidden="true" />
              </>
            )}

            <div className="pv-hgroup" role="toolbar" aria-label={t('header.viewGroup')}>
              {hasGuide && (
                <Tooltip label={t('header.export.label')} description={t('header.export.description')}>
                  <a
                    href={gameExportUrl(game.slug)}
                    download
                    aria-label={t('header.export.label')}
                    className={iconButtonClass()}
                  >
                    <IconFileExport size={18} />
                  </a>
                </Tooltip>
              )}

              {/* Who streams this game right now? Renders nothing while the Twitch switch in the settings is off */}
              <span data-hint="game-twitch" className="inline-flex empty:hidden">
                <TwitchButton name={game.title} />
              </span>

              <IconButton
                tone="primary"
                label={t('header.print.label')}
                description={t('header.print.description')}
                onClick={() => window.print()}
              >
                <IconFileTypePdf size={18} />
              </IconButton>

              {hasGuide && (
                <IconButton
                  label={t('header.reset.label')}
                  description={t('header.reset.description')}
                  onClick={() => void handleResetProgress()}
                >
                  <IconRefresh size={18} />
                </IconButton>
              )}
            </div>

            <CabinetToolGroup />

            <span className="pv-hdivider" aria-hidden="true" />
            <UiScaleControl />
          </div>

          {/* Utilities: the same four, in the same place, on every page */}
          <HeaderUtilities theme={theme} onToggleTheme={onToggleTheme} onOpenSettings={(tab) => { setSettingsTab(tab); setSettingsOpen(true) }} onShowHints={hints.show} />
        </div>
      </header>

      {/* The trophy bar (sections right, the "Kapitel" button on a phone) spans the window; below it the chapter side nav and the content */}
      <PinsScope appId={game.steamAppId} onError={showPinFailure}>
      <div className="app-shell">
        <GameTrophyBar
          data={liveTrophies}
          hasSteam={Boolean(game.steamAppId)}
          sections={<SectionTabs items={sectionItems} activeSlug={activeSectionSlug} onSelect={selectTab} />}
          chaptersButton={isDrawer && chapterItems.length > 0 ? <ChapterDrawerButton expanded={drawerOpen} onClick={() => setDrawerOpen(true)} /> : undefined}
        />

        <div className="pv-gbody">
        <ChapterSidebar
          chapters={chapterItems}
          activeSlug={activeChapterSlug}
          onSelect={selectTab}
          drawer={isDrawer}
          drawerOpen={drawerOpen}
          onDrawerClose={closeDrawer}
          collapsed={navCollapsed}
          onToggleCollapsed={toggleNavCollapsed}
        />

        {/* Content next to the navigation */}
        <div className="content-column flex min-w-0 flex-1 flex-col self-stretch">
        {/* Active Tab Content */}
        <AchievementLookupProvider achievements={liveTrophies?.achievements}>
        <ItemLinkContext.Provider value={itemLinkApi}>
        <SideQuestTargetContext.Provider value={sideQuestTarget}>
        <PuzzleScopeContext.Provider value={game.slug}>
        <main
          id="pv-game-panel"
          role="tabpanel"
          aria-labelledby={activeTabSlug ? tabDomId(activeTabSlug) : undefined}
          className="flex-1 min-w-0 p-4 sm:p-6 lg:p-8 space-y-6"
        >
          {!game.steamAppId && (
            <div className="pv-alert pv-alert-crystal flex flex-wrap items-center justify-between gap-3 no-print">
              <span className="text-sm">
                <strong>{t('notLinked.title')}</strong> {t('notLinked.text')}
              </span>
              <button type="button" className="pv-btn pv-btn-primary shrink-0" onClick={() => setSteamLinkOpen(true)}>
                <IconLink size={15} />
                <span>{t('notLinked.button')}</span>
              </button>
            </div>
          )}

          {/* One quiet line whenever the guide was written in another language than the UI: "translated automatically" once the
              translation is shown, "written in X" until then - there in both states, so the swap moves nothing */}
          {guideContentLanguage && guideContentLanguage !== language && !onLiveOrStats && (
            <p className="flex items-center gap-1.5 text-xs font-medium no-print" style={{ color: 'var(--text-muted)' }}>
              <IconLanguage size={14} aria-hidden="true" />
              {isTranslatedView
                ? t('translate.translatedFrom', { language: t(`translate.from.${guideContentLanguage}`) })
                : t('contentLanguage.hint', { language: t(`contentLanguage.${guideContentLanguage}`) })}
            </p>
          )}

          {activeTabSlug === LIVE_TROPHIES_TAB_SLUG && showLiveTrophiesTab && game.steamAppId ? (
            <LiveTrackerPanel
              defaultExpanded={true}
              fixedAppId={game.steamAppId}
              tips={achievementTips}
            />
          ) : activeTabSlug === STATS_TAB_SLUG && showStatsTab ? (
            <GameStatsPanel slug={game.slug} steamAppId={game.steamAppId} />
          ) : activeTabSlug === LORE_TAB_SLUG ? (
            <LorePanel
              tab={loreTab}
              revealed={loreRevealed}
              busy={loreBusy}
              onReveal={revealLore}
              onResearch={(oneOff) => void handleResearchLore(oneOff)}
              checkedSet={checkedSet}
              onToggleCheck={handleToggleCheck}
            />
          ) : hasGuide && guideTabs.length === 0 ? (
            <div className="pv-card flex flex-col items-center gap-3 py-14 text-center">
              <IconBook2 size={36} style={{ color: 'var(--accent-crystal)' }} />
              <p className="text-sm font-bold" style={{ color: 'var(--text-primary)' }}>
                {t('noGuide.text')}
              </p>
              <button type="button" onClick={() => setExpandOpen(true)} className="pv-btn pv-btn-emerald">
                <IconSparkles size={15} />
                <span>{t('noGuide.button')}</span>
              </button>
            </div>
          ) : (
            <>
              {/* Several chapters whose research failed: research them all again, once at the top of the guide */}
              {failedChapters.length >= 2 && <RetryAllBanner count={failedChapters.length} busy={retrying} onRetryAll={(oneOff) => void handleRetryChapters(null, oneOff)} onManual={() => setManual({ chapters: failedChapterSeeds(game) })} />}

              {/* Hero Card on Tab 1 (Exact 100% Guide Look) */}
              {activeTab && activeTab.id === guideTabs[0]?.id && (
                <div className="hero-card">
                  {/* The buttons wrap below the text when the row is too narrow (rem basis: follows the UI size, unlike the md breakpoint alone) */}
                  <div className="flex flex-col md:flex-row md:flex-wrap md:items-center justify-between gap-4">
                    <div className="min-w-0 space-y-2 max-w-4xl md:flex-[1_1_24rem]">
                      <div
                        className="text-xs font-extrabold uppercase tracking-wider"
                        style={{ color: 'var(--accent-emerald)' }}
                      >
                        {game.genreTags}
                      </div>
                      <h2
                        className="text-xl sm:text-2xl font-black"
                        style={{ color: 'var(--text-primary)' }}
                      >
                        {t('hero.title', { title: game.title })}
                      </h2>
                      <p
                        className="text-xs sm:text-sm leading-relaxed"
                        style={{ color: 'var(--text-secondary)' }}
                      >
                        {game.subtitle}
                      </p>
                      <div className="flex flex-wrap gap-2.5 pt-2">
                        {game.missablesInfo && (
                          <span className="stat-pill safe">
                            <IconShieldCheck size={15} />
                            {game.missablesInfo}
                          </span>
                        )}
                        {game.estimatedHours && (
                          <span className="stat-pill crystal">
                            <IconClock size={15} />
                            {game.estimatedHours}
                          </span>
                        )}
                        {game.difficultyInfo && (
                          <span className="stat-pill gold">
                            <IconTrophy size={15} />
                            {game.difficultyInfo}
                          </span>
                        )}
                      </div>
                    </div>

                    <div className="flex flex-col gap-2 shrink-0 no-print">
                      {showLiveTrophiesTab && (
                        <button
                          type="button"
                          onClick={() => selectTab(LIVE_TROPHIES_TAB_SLUG)}
                          className="pv-btn pv-btn-gold"
                        >
                          <IconTrophy size={16} />
                          <span>{t('hero.openLive')}</span>
                        </button>
                      )}
                      <Link to="/" className="pv-btn">
                        <IconHome size={16} />
                        <span>{t('hero.backToOverview')}</span>
                      </Link>
                    </div>
                  </div>
                </div>
              )}

              {/* The chapter's own action row. A failed-research placeholder has its own button inside its block (same question, same flow) */}
              {activeTab && !activeTab.sections.some((section) => section.failedChapter) && (
                <RemoveChapterBar
                  disabled={removingChapter || completing || retrying || loreBusy || redoing}
                  onRemove={() => void handleRemoveChapter(activeTab, false)}
                  onRedo={(oneOff) => void handleRedoChapter(activeTab, oneOff)}
                  onRedoManual={() => void handleRedoChapterManual(activeTab)}
                  redoBusy={redoing}
                />
              )}

              {/* Render all Sections of the active Tab: a chapter with a failed-research placeholder keeps the plain list, every other chapter gets its
                  structure (parts, areas, table of contents) from ChapterBody */}
              {activeTab && activeTab.sections.some((section) => section.failedChapter) ? (
                activeTab.sections.map((section, idx) =>
                  section.failedChapter ? (
                    <FailedChapterBlock
                      key={`${activeTab.id}-sec-${idx}`}
                      section={section}
                      busy={retrying}
                      onRetry={(oneOff) => void handleRetryChapters([activeTab.id], oneOff)}
                      onRemove={() => void handleRemoveChapter(activeTab, true)}
                      onManual={() =>
                        setManual({
                          chapter: {
                            title: activeTab.title,
                            icon: activeTab.icon,
                            focus: section.failedChapter?.focus ?? activeTab.title,
                            topicId: section.failedChapter?.topicId ?? null,
                            tabId: activeTab.id,
                          },
                        })
                      }
                    />
                  ) : (
                    <SectionRenderer
                      key={`${activeTab.id}-sec-${idx}`}
                      position={`${activeTab.id}:${idx}`}
                      section={section}
                      checkedSet={checkedSet}
                      onToggleCheck={handleToggleCheck}
                    />
                  )
                )
              ) : (
                activeTab && (
                  <ChapterBody
                    tabId={activeTab.id}
                    sections={activeTab.sections}
                    renderSection={(section, idx) => (
                      <SectionRenderer position={`${activeTab.id}:${idx}`} section={section} checkedSet={checkedSet} onToggleCheck={handleToggleCheck} />
                    )}
                  />
                )
              )}
            </>
          )}
        </main>
        </PuzzleScopeContext.Provider>
        </SideQuestTargetContext.Provider>
        </ItemLinkContext.Provider>
        </AchievementLookupProvider>
        </div>
        {/* "Im Blick" in a wide landscape window: the column on the right, the mirror image of the chapter navigation */}
        <PinsSurface placement="column" achievements={liveTrophies?.achievements} tips={achievementTips} />
        </div>
        {/* ... and everywhere else the dock at the bottom of the window (it follows the content, it never covers it) */}
        <PinsSurface placement="dock" achievements={liveTrophies?.achievements} tips={achievementTips} />
      </div>
      </PinsScope>

      {/* The version, credit, copyright and licences are in the info card at the foot of the side navigation (gamenav/NavInfo.tsx). Only a game
          without chapters has no navigation: then the page-end footer carries them (it brings its own "back to top" button) */}
      {chapterItems.length > 0 ? <ScrollTopButton /> : <AppFooter />}

      {/* Overlays over the page (never part of the layout): a running job, a result, a failure */}
      <FloatingNoticeStack>
        {(completing || loreBusy || retrying || redoing) && <AiProgressNotice key="ai-progress" kind={pageJobKind} view={pageProgress.view} />}
        {translation.phase === 'running' && (
          <FloatingNotice
            key="translating"
            tone="progress"
            title={t('translate.running.title', { language: t(`translate.into.${language}`) })}
            message={
              translation.total > 0
                ? t('translate.running.progress', { translated: translation.translated, total: translation.total })
                : t('translate.running.message')
            }
          />
        )}
        {translation.phase === 'failed' && translation.error && !failureDismissed && (
          <FloatingNotice
            key="translate-failed"
            tone="error"
            title={t('translate.failed.title', { language: t(`translate.into.${language}`) })}
            message={translation.error.message || t('common:unknownError')}
            details={translation.error.details}
            actions={[
              {
                label: t('notice.aiSettings'),
                onClick: () => {
                  setSettingsTab('ki')
                  setSettingsOpen(true)
                },
              },
              { label: t('translate.retry'), onClick: translation.retry },
            ]}
            onClose={() => setFailureDismissed(true)}
          />
        )}
        {actionInfo && !completing && !retrying && (
          <FloatingNotice key="info" tone="info" title={actionInfo.title} message={actionInfo.message} onClose={() => setActionInfo(null)} />
        )}
        {aiNotice.element}
        {actionError && (
          <FloatingNotice
            key="error"
            tone="error"
            title={actionError.title}
            message={actionError.message}
            details={actionError.details}
            actions={[
              ...(actionError.retry
                ? [
                    {
                      label: actionError.retry.label,
                      onClick: () => {
                        const run = actionError.retry!.run
                        setActionError(null)
                        run()
                      },
                    },
                  ]
                : []),
              ...(actionError.manual
                ? [
                    {
                      label: t('ai:manual.entry.builder'),
                      onClick: () => {
                        setActionError(null)
                        setManual({})
                      },
                    },
                  ]
                : []),
              ...(actionError.aiSettings
                ? [
                    {
                      label: t('notice.aiSettings'),
                      onClick: () => {
                        setActionError(null)
                        setSettingsTab('ki')
                        setSettingsOpen(true)
                      },
                    },
                  ]
                : []),
            ]}
            onClose={() => setActionError(null)}
          />
        )}
      </FloatingNoticeStack>

      <HintTour
        screen="game"
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
        initialTab={settingsTab}
        onClose={() => {
          setSettingsOpen(false)
          setSettingsTab(undefined)
        }}
        onSaved={() => {
          void queryClient.invalidateQueries({ queryKey: ['settings'] })
          void queryClient.invalidateQueries({ queryKey: ['live-status'] })
          void queryClient.invalidateQueries({ queryKey: ['live-achievements'] })
        }}
      />

      {builderOpen && (
        <GuideBuilderDialog
          slug={game.slug}
          gameTitle={game.title}
          provider={builderProvider}
          onClose={() => setBuilderOpen(false)}
          onDone={(result) => {
            setBuilderOpen(false)
            handleGuideCompleted(result)
          }}
          onManual={() => {
            setBuilderOpen(false)
            setManual({})
          }}
        />
      )}

      {manual !== null && (
        <ManualGuideDialog
          slug={game.slug}
          gameTitle={game.title}
          language={game.contentLanguage ?? language}
          chapter={manual.chapter}
          chapters={manual.chapters}
          onClose={() => setManual(null)}
          onSaved={(updated) => {
            queryClient.setQueryData(['game', slug], updated)
            void queryClient.invalidateQueries({ queryKey: ['games'] })
          }}
        />
      )}

      {steamLinkOpen && (
        <SteamLinkDialog
          slug={game.slug}
          initialQuery={game.title}
          onClose={() => setSteamLinkOpen(false)}
          onLinked={(updated) => {
            queryClient.setQueryData(['game', slug], updated)
            void queryClient.invalidateQueries({ queryKey: ['games'] })
            setSteamLinkOpen(false)
            setCoverFailed(false)
            setActionInfo({ title: t('notice.steamLinked.title'), message: t('notice.steamLinked.message') })
          }}
        />
      )}

      {/* Expand Tab with AI Modal */}
      {expandOpen && (
        <div className="pv-modal-backdrop" role="dialog" aria-modal="true">
          <div className="pv-modal relative w-full max-w-lg">
          <div className="max-h-[92dvh] space-y-4 overflow-y-auto p-6">
            <div
              className="flex items-center justify-between pb-3"
              style={{ borderBottom: '1px solid var(--border-subtle)' }}
            >
              <div
                className="flex items-center gap-2 font-bold text-sm"
                style={{ color: 'var(--accent-emerald)' }}
              >
                <IconSparkles size={20} />
                <span>{t('expand.title', { title: game.title })}</span>
              </div>
              <button
                type="button"
                onClick={() => setExpandOpen(false)}
                disabled={expanding}
                className="pv-btn !p-1.5"
                aria-label={t('common:close')}
              >
                <IconX size={18} />
              </button>
            </div>

            <form onSubmit={handleExpandTab} className="space-y-4">
              {/* Quick picks from the standard topic list – no typing needed */}
              {topics && topics.length > 0 && (
                <div>
                  <div className="text-xs font-bold mb-1.5" style={{ color: 'var(--text-secondary)' }}>
                    {t('expand.pickTopic')}
                  </div>
                  <div className="flex flex-wrap gap-1.5">
                    {topics.map((topic) => (
                      <button
                        key={topic.id}
                        type="button"
                        disabled={expanding}
                        onClick={() => {
                          setNewTabTitle(topic.title)
                          setNewTabPrompt(topic.brief)
                          setExpandError(null)
                        }}
                        aria-pressed={newTabTitle === topic.title}
                        className="pv-badge pv-badge-a cursor-pointer aria-pressed:!bg-[var(--accent-emerald-bg)] aria-pressed:!text-[var(--accent-emerald)]"
                      >
                        {topic.title}
                      </button>
                    ))}
                  </div>
                </div>
              )}

              <div>
                <label
                  className="block text-xs font-bold mb-1"
                  style={{ color: 'var(--text-secondary)' }}
                >
                  {t('expand.titleLabel')}
                </label>
                <input
                  type="text"
                  value={newTabTitle}
                  onChange={(e) => setNewTabTitle(e.target.value)}
                  disabled={expanding}
                  placeholder={t('expand.titlePlaceholder')}
                  className="pv-input w-full"
                />
              </div>

              <div>
                <label
                  className="block text-xs font-bold mb-1"
                  style={{ color: 'var(--text-secondary)' }}
                >
                  {t('expand.promptLabel')}
                </label>
                <textarea
                  rows={3}
                  value={newTabPrompt}
                  onChange={(e) => setNewTabPrompt(e.target.value)}
                  disabled={expanding}
                  placeholder={t('expand.promptPlaceholder')}
                  className="pv-input w-full"
                />
              </div>

              {/* A failure is shown completely (it wraps; the provider's own text sits behind "technical details"); the live progress of a
                  running action is the overlay over this dialog. The room of the one fixed message ("title and question needed") is reserved. */}
              <ReservedText className="text-xs font-semibold" candidates={[t('expand.missing')]}>
                {expandError && (
                  <span role="alert" className="block whitespace-pre-line break-words" style={{ color: 'var(--accent-crimson)' }}>
                    {expandError.notice && <span className="mb-1 block" style={{ color: 'var(--accent-crystal)' }}>{expandError.notice}</span>}
                    {expandError.message}
                  </span>
                )}
              </ReservedText>
              <ErrorDetails reserve title={expandError?.notice} message={expandError?.message} details={expandError?.details} />

              <div
                className="flex justify-end gap-2 pt-3"
                style={{ borderTop: '1px solid var(--border-subtle)' }}
              >
                <button
                  type="button"
                  onClick={() => setExpandOpen(false)}
                  disabled={expanding}
                  className="pv-btn"
                >
                  {t('common:cancel')}
                </button>
                <AiRunButton
                  type="submit"
                  tone="pv-btn-emerald"
                  busy={expanding}
                  oneOff={expandOneOff}
                  onOneOffChange={setExpandOneOff}
                  icon={<IconSparkles size={15} />}
                  label={<span>{t('expand.submit')}</span>}
                />
              </div>
            </form>
          </div>
          {expanding && <AiProgressOverlay kind="expand" view={expandProgress.view} />}
          </div>
        </div>
      )}
    </div>
  )
}

interface SectionRendererProps {
  section: GuideSectionDto
  checkedSet: Set<string>
  onToggleCheck: (itemKey: string, checked: boolean) => Promise<void>
  // True inside the already revealed lore tab: spoiler sections render directly
  spoilersOpen?: boolean
  // Where the section sits in the guide (tab id + position): stays the same in every language, so a puzzle without an item id keeps its revealed state
  position?: string
}

// Optional Story & Lore tab: research button when empty, one spoiler gate for the whole tab, then the sections.
function LorePanel({
  tab,
  revealed,
  busy,
  onReveal,
  onResearch,
  checkedSet,
  onToggleCheck,
}: {
  tab: GuideTabDto | undefined
  revealed: boolean
  busy: boolean
  onReveal: (remember: boolean) => void
  onResearch: (oneOff?: string) => void
  checkedSet: Set<string>
  onToggleCheck: (itemKey: string, checked: boolean) => Promise<void>
}) {
  const { t } = useTranslation('game')
  const [remember, setRemember] = useState(false)
  // The AI picked for this one research ("Mit Claude"); the setting itself stays as it is
  const [oneOff, setOneOff] = useState<AiProviderId | undefined>(undefined)

  if (!tab) {
    return (
      <div className="pv-card flex flex-col items-center gap-3 py-14 text-center">
        <IconBook size={36} style={{ color: 'var(--accent-crimson)' }} />
        <p className="text-sm font-bold" style={{ color: 'var(--text-primary)' }}>
          {t('lore.none')}
        </p>
        <p className="max-w-xl text-xs" style={{ color: 'var(--text-secondary)' }}>
          {t('lore.noneHint')}
        </p>
        <AiRunButton
          tone="pv-btn-emerald"
          busy={busy}
          oneOff={oneOff}
          onOneOffChange={setOneOff}
          onClick={() => onResearch(oneOff)}
          icon={<IconSparkles size={15} />}
          label={<span>{t('lore.research')}</span>}
        />
      </div>
    )
  }

  if (!revealed) {
    return (
      <div className="pv-card flex flex-col items-center gap-3 py-14 text-center">
        <IconAlertTriangle size={36} style={{ color: 'var(--accent-gold)' }} />
        <p className="text-base font-extrabold" style={{ color: 'var(--text-primary)' }}>
          {t('lore.gateTitle')}
        </p>
        <p className="max-w-xl text-sm" style={{ color: 'var(--text-secondary)' }}>
          {t('lore.gateText')}
        </p>
        <Switch checked={remember} onChange={setRemember} className="flex items-center gap-2.5 text-xs font-semibold text-[color:var(--text-secondary)]">
          {t('lore.remember')}
        </Switch>
        <button type="button" onClick={() => onReveal(remember)} className="pv-btn pv-btn-gold">
          <IconEye size={15} />
          <span>{t('lore.reveal')}</span>
        </button>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex justify-end no-print">
        <Tooltip label={t('lore.redo.label')} description={t('lore.redo.description')}>
          <AiRunButton
            busy={busy}
            oneOff={oneOff}
            onOneOffChange={setOneOff}
            onClick={() => onResearch(oneOff)}
            icon={<IconRefresh size={15} />}
            label={<span>{t('lore.redo.button')}</span>}
          />
        </Tooltip>
      </div>
      {tab.sections.map((section, idx) => (
        <SectionRenderer
          key={`${tab.id}-lore-${idx}`}
          position={`${tab.id}:${idx}`}
          section={section}
          checkedSet={checkedSet}
          onToggleCheck={onToggleCheck}
          spoilersOpen
        />
      ))}
    </div>
  )
}

function SectionRenderer(props: SectionRendererProps) {
  // Spoiler sections outside the lore tab stay collapsed until opened one by one
  if (props.section.spoiler && !props.spoilersOpen) {
    return <SpoilerSection {...props} />
  }
  return <SectionContent {...props} />
}

function SpoilerSection(props: SectionRendererProps) {
  const { t } = useTranslation('game')
  const [open, setOpen] = useState(false)
  if (open) return <SectionContent {...props} />
  return (
    <button
      type="button"
      onClick={() => setOpen(true)}
      className="pv-card flex w-full items-center justify-between gap-3 text-left cursor-pointer"
    >
      <span className="text-sm font-extrabold" style={{ color: 'var(--text-primary)' }}>
        {props.section.title || t('spoilerSection.fallbackTitle')}
      </span>
      <span className="pv-badge pv-badge-b inline-flex items-center gap-1">
        <IconEye size={13} /> {t('spoilerSection.show')}
      </span>
    </button>
  )
}

function SectionContent({ section, checkedSet, onToggleCheck, position }: SectionRendererProps) {
  if (section.type === 'puzzle' && section.puzzle) {
    return <PuzzleBlock section={section} checkedSet={checkedSet} onToggleCheck={onToggleCheck} stateKey={position} />
  }

  if (section.type === 'boss') {
    return <BossCard section={section} />
  }

  if (section.type === 'overview') {
    return <OverviewBlock section={section} />
  }

  if (section.type === 'sources') {
    return <SourcesBlock section={section} />
  }

  if (section.type === 'heading' && section.title) {
    return <GuideHeading section={section} />
  }

  if (section.type === 'story' && section.variant === 'steps') {
    return <StepsStory section={section} />
  }

  if (section.type === 'alert' && isGuideAlertKind(section.kind)) {
    return <HintAlert section={section} />
  }

  if (section.type === 'story') {
    const paragraphs = section.paragraphs ?? (section.htmlContent ? [section.htmlContent] : [])
    return (
      <article className="pv-card space-y-3">
        {section.title && (
          <h3 className="text-base font-extrabold" style={{ color: 'var(--text-primary)' }}>
            {section.title}
          </h3>
        )}
        {paragraphs.map((paragraph, i) => (
          <p key={i} className="text-sm leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
            {paragraph}
          </p>
        ))}
      </article>
    )
  }

  if (section.type === 'alert') {
    const variantClass =
      section.variant === 'gold'
        ? 'pv-alert-gold'
        : section.variant === 'danger'
          ? 'pv-alert-danger'
          : section.variant === 'emerald'
            ? 'pv-alert-emerald'
            : 'pv-alert-crystal'

    const iconColor =
      section.variant === 'gold'
        ? 'var(--accent-gold)'
        : section.variant === 'danger'
          ? 'var(--accent-crimson)'
          : section.variant === 'emerald'
            ? 'var(--accent-emerald)'
            : 'var(--accent-crystal)'

    return (
      <div className={`pv-alert ${variantClass}`}>
        <div className="shrink-0 mt-0.5" style={{ color: iconColor }}>
          {section.variant === 'danger' ? (
            <IconAlertTriangle size={22} />
          ) : (
            <IconInfoCircle size={22} />
          )}
        </div>
        <div className="space-y-1">
          {section.title && (
            <div
              className="font-extrabold text-sm sm:text-base"
              style={{ color: 'var(--text-primary)' }}
            >
              {section.title}
            </div>
          )}
          {section.htmlContent && (
            <p
              className="text-xs sm:text-sm leading-relaxed"
              style={{ color: 'var(--text-secondary)' }}
            >
              {section.htmlContent}
            </p>
          )}
        </div>
      </div>
    )
  }

  if (section.type === 'cards_grid' && section.cards) {
    const gridCols =
      section.columns === 3
        ? 'grid-cols-1 lg:grid-cols-3'
        : 'grid-cols-1 lg:grid-cols-2'

    return (
      <div className={`grid ${gridCols} gap-5`}>
        {section.cards.map((card, i) => {
          const badgeClass =
            card.badgeVariant === 's'
              ? 'pv-badge-s'
              : card.badgeVariant === 'b'
                ? 'pv-badge-b'
                : card.badgeVariant === 'ok'
                  ? 'pv-badge-ok'
                  : 'pv-badge-a'

          return (
            <article key={i} className="pv-card space-y-3">
              <div className="pv-card-header">
                <h3
                  className="text-base font-extrabold"
                  style={{ color: 'var(--text-primary)' }}
                >
                  {card.title}
                </h3>
                {card.badgeText && (
                  <span className={`pv-badge ${badgeClass}`}>
                    {card.badgeText}
                  </span>
                )}
              </div>
              {card.introText && (
                <p
                  className="text-xs sm:text-sm leading-relaxed"
                  style={{ color: 'var(--text-secondary)' }}
                >
                  {card.introText}
                </p>
              )}
              {card.bullets && card.bullets.length > 0 && (
                <ul className="space-y-2.5 text-xs sm:text-sm">
                  {card.bullets.map((b, bi) => (
                    <li
                      key={bi}
                      className="flex items-start gap-2.5 leading-relaxed"
                      style={{ color: 'var(--text-secondary)' }}
                    >
                      <IconCheck
                        size={16}
                        className="shrink-0 mt-1"
                        style={{ color: 'var(--accent-crystal)' }}
                      />
                      <span>
                        <strong style={{ color: 'var(--text-primary)' }}>
                          {b.title}
                        </strong>{' '}
                        {b.body}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </article>
          )
        })}
      </div>
    )
  }

  if (section.type === 'checklist_table' && section.table) {
    return (
      <ChecklistTableSection
        table={section.table}
        checkedSet={checkedSet}
        onToggleCheck={onToggleCheck}
      />
    )
  }

  return null
}

export function ChecklistTableSection({
  table,
  checkedSet,
  onToggleCheck,
}: {
  table: GuideTableDto
  checkedSet: Set<string>
  onToggleCheck: (itemKey: string, checked: boolean) => Promise<void>
}) {
  const { t } = useTranslation('game')
  const itemLinks = useItemLinks()
  const [search, setSearch] = useState('')
  const [catFilter, setCatFilter] = useState('ALL')
  const [secFilter, setSecFilter] = useState('ALL')

  const filteredRows = table.rows.filter((row) => {
    if (catFilter !== 'ALL' && row.filterCategory !== catFilter) return false
    if (secFilter !== 'ALL' && row.filterSecondary !== secFilter) return false
    if (search.trim()) {
      const hay = Object.values(row.cells).join(' ').toLowerCase()
      if (!hay.includes(search.trim().toLowerCase())) return false
    }
    return true
  })

  return (
    <div className="space-y-3">
      {/* Filter Bar */}
      <div className="pv-card !p-3.5 flex flex-wrap items-center gap-3 no-print">
        <div className="relative flex-1 min-w-[220px]">
          <IconSearch
            size={16}
            className="absolute left-3 top-1/2 -translate-y-1/2"
            style={{ color: 'var(--text-muted)' }}
          />
          <input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={table.searchPlaceholder ?? t('table.searchPlaceholder')}
            className="pv-input pv-input-icon w-full"
          />
        </div>

        {table.categories && table.categories.length > 0 && (
          <select
            value={catFilter}
            onChange={(e) => setCatFilter(e.target.value)}
            aria-label={t('table.categoryFilter')}
            className="pv-input font-semibold"
          >
            <option value="ALL">
              {table.categoryFilterLabel ?? t('table.allCategories')}
            </option>
            {table.categories.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        )}

        {table.secondaryCategories && table.secondaryCategories.length > 0 && (
          <select
            value={secFilter}
            onChange={(e) => setSecFilter(e.target.value)}
            aria-label={t('table.secondaryFilter')}
            className="pv-input font-semibold"
          >
            <option value="ALL">
              {table.secondaryFilterLabel ?? t('table.allFilters')}
            </option>
            {table.secondaryCategories.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        )}
      </div>

      {/* Responsive Container-Query Table */}
      <div
        tabIndex={0}
        role="region"
        aria-label={table.caption}
        className="vault-table-wrapper"
      >
        <table className="vault-table">
          <caption
            className="text-left px-4 py-3 font-extrabold text-sm"
            style={{
              background: 'var(--bg-elevated)',
              borderBottom: '1px solid var(--border-subtle)',
              color: 'var(--text-primary)',
            }}
          >
            {t('table.caption', { caption: table.caption, count: filteredRows.length })}
          </caption>
          <thead>
            <tr>
              {table.checkable && <th className="w-12 text-center">{t('table.done')}</th>}
              {table.columns.map((col) => (
                <th key={col.key}>{col.label}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {filteredRows.map((row) => {
              // A row that refers to a row of another chapter is the same check as that row
              const checkKey = itemLinks ? itemLinks.index.resolveKey(row.itemId, row.ref) : row.itemId
              const isChecked = checkedSet.has(checkKey)
              return (
                <tr
                  key={row.itemId}
                  className={isChecked ? 'row-checked' : ''}
                  data-item-id={row.itemId}
                >
                  {table.checkable && (
                    <td className="w-12 text-center" data-label={t('table.done')}>
                      <input
                        type="checkbox"
                        aria-label={t('table.doneFor', { name: Object.values(row.cells)[0] ?? row.itemId })}
                        checked={isChecked}
                        onChange={(e) =>
                          void onToggleCheck(checkKey, e.target.checked)
                        }
                        className="h-5 w-5 rounded cursor-pointer"
                        style={{ accentColor: 'var(--accent-emerald)' }}
                      />
                    </td>
                  )}
                  {table.columns.map((col, colIndex) => {
                    const cellVal = row.cells[col.key] ?? ''
                    // The links sit in the first (name) column
                    const rowLinks = colIndex === 0 ? <RowLinks itemId={row.itemId} refId={row.ref} /> : null
                    if (col.isRowHeader) {
                      return (
                        <th key={col.key} scope="row">
                          {cellVal}
                          {rowLinks}
                        </th>
                      )
                    }
                    const badgeClass =
                      col.badgeVariant === 's'
                        ? 'pv-badge-s'
                        : col.badgeVariant === 'b'
                          ? 'pv-badge-b'
                          : col.badgeVariant === 'ok'
                            ? 'pv-badge-ok'
                            : 'pv-badge-a'
                    return (
                      <td key={col.key} data-label={col.label}>
                        {col.badgeVariant ? (
                          <span className={`pv-badge ${badgeClass}`}>
                            {cellVal}
                          </span>
                        ) : (
                          cellVal
                        )}
                        {rowLinks}
                      </td>
                    )
                  })}
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </div>
  )
}
