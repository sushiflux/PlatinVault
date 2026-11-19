import { useEffect, useId, useRef, useState } from 'react'
import { CopyErrorButton } from './ui/CopyErrorButton'
import { ErrorBlock } from './ui/ErrorBlock'
import { GhostIconButton } from './ui/GhostIconButton'
import { IconButton } from './ui/IconButton'
import { SettingsCard, SettingsGuide, SettingsRow, SettingsToggle, SettingsTopic } from './settings/SettingsParts'
import type { KeyboardEvent, ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Trans, useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import {
  IconAlertTriangle,
  IconArrowsMaximize,
  IconCheck,
  IconCopy,
  IconDownload,
  IconLoader2,
  IconLogin2,
  IconMusic,
  IconPlugConnected,
  IconPlus,
  IconRefresh,
  IconTrash,
  IconVolume,
  IconX,
} from '@tabler/icons-react'
import {
  createBridgeToken,
  downloadBridgePackage,
  deleteBridgeToken,
  fetchBridgeTokenStatus,
  fetchSettings,
  diagnoseGemini,
  probeAiModels,
  steamLoginUrl,
  testTwitchConnection,
  updateSettings,
} from '../api/client'
import type {
  AiModelOptionDto,
  BridgeOs,
  BridgeTokenCreatedDto,
  BridgeTokenStatusDto,
  AiProviderId,
  AiSelectionMode,
  NotificationPosition,
  PopupTarget,
  ResearchBlocksDto,
  SettingsResponseDto,
  SoundTheme,
  GeminiDiagnosisDto,
  TwitchTestResponseDto,
} from '../api/types'
import { useFormat } from '../i18n/format'
import { useUiLanguage } from '../i18n/useUiLanguage'
import { animationQueue } from '../utils/animationQueue'
import { DEFAULT_SOUND_THEME, normalizeSoundTheme, SOUND_THEMES, soundEngine } from '../utils/audio'
import { defaultBridgeOs } from '../utils/bridgeOs'
import { copyText } from '../utils/clipboard'
import { errorCodeOf } from '../utils/errorReport'
import { useFitSegments } from '../utils/useFitSegments'
import '../styles/dialogs.css'
import { DEFAULT_POPUP_SECONDS, MAX_POPUP_SECONDS, MIN_POPUP_SECONDS, normalizePopupSeconds } from '../utils/popupSeconds'
import { DEFAULT_POPUP_TARGET, normalizePopupTarget, POPUP_TARGETS } from '../utils/popupTarget'
import { steamClientUrl, useOpenInSteamClient } from '../utils/steamClient'
import { useSteamReach } from '../utils/useSteamReach'
import { AI_STATUS_KEY } from './ai/useAiStatus'
import { AiSelectionSection } from './settings/AiSelectionSection'
import { emptyResearchBlocks, ResearchBlocksSection, sameResearchBlocks } from './settings/ResearchBlocksSection'
import { SystemStatusSection } from './SystemStatusSection'
import { FoldersSection } from './settings/FoldersSection'
import { UpdatesSection } from './settings/UpdatesSection'
import { DesignSection } from './settings/DesignSection'
import { SteamFolderSection } from './settings/SteamFolderSection'
import { useReadsSteamLocally } from '../utils/useReadsSteamLocally'
import { useIsDesktopHost } from './FooterParts'
import { ReservedText } from './ui/ReservedText'
import { TierIcon } from './ui/TierIcon'
import { Tooltip } from './ui/Tooltip'

export type SettingsTab = 'live' | 'steam' | 'ki' | 'twitch' | 'bridge' | 'system'

// Names and hints of the tabs are texts: settings:tabs.<id>.label / .hint
const SETTINGS_TABS: { id: SettingsTab }[] = [{ id: 'steam' }, { id: 'ki' }, { id: 'live' }, { id: 'twitch' }, { id: 'bridge' }, { id: 'system' }]
const LAST_TAB_KEY = 'pv_settings_tab'

/** The tab the settings were last left on in this browser (null = none yet); the header's gear opens the page on it. */
export function rememberedSettingsTab(): SettingsTab | null {
  try {
    const value = localStorage.getItem(LAST_TAB_KEY)
    return SETTINGS_TABS.some((tab) => tab.id === value) ? (value as SettingsTab) : null
  } catch {
    return null
  }
}

interface SettingsModalProps {
  open: boolean
  // Tab to show when the modal opens (e.g. from the tour's setup checklist); otherwise the last used tab
  initialTab?: SettingsTab
  // Message to show when the modal opens, e.g. the result of "Sign in with Steam"
  notice?: { type: 'ok' | 'err'; text: string } | null
  onClose: () => void
  onSaved?: (settings: SettingsResponseDto) => void
  // Page mode (route /settings): no backdrop, no close button, the content area of the app shell is the frame. The tab then comes
  // from the URL (initialTab) and every tab change is reported through onTabChange, so a reload stays on the same tab.
  page?: boolean
  onTabChange?: (tab: SettingsTab) => void
}

interface ProviderConnectionState {
  checking: boolean
  connected: boolean
  error: string | null
  models: AiModelOptionDto[]
}

const EMPTY_CONN: ProviderConnectionState = {
  checking: false,
  connected: false,
  error: null,
  models: [],
}

// The names of the popup positions are texts: settings:live.positions.<id>. The nine spots form the 3x3 grid;
// "fullscreen" is the tenth way and has its own tile below the grid.
const POSITIONS: Exclude<NotificationPosition, 'fullscreen'>[] = [
  'top-left',
  'top-center',
  'top-right',
  'center-left',
  'center',
  'center-right',
  'bottom-left',
  'bottom-center',
  'bottom-right',
]

// Unified Settings Modal in Pastel 100% design with live-probed AI model dropdowns (only shown once an API key connects).
export function SettingsModal({ open, notice = null, initialTab, onClose, onSaved, page = false, onTabChange }: SettingsModalProps) {
  const { t } = useTranslation('settings')
  const { t: tc } = useTranslation()
  const { language } = useUiLanguage()
  const format = useFormat()
  const queryClient = useQueryClient()
  const formRef = useRef<HTMLFormElement>(null)
  // A choice bar that does not fit becomes a connected vertical list
  useFitSegments(formRef)
  const [activeTab, setActiveTab] = useState<SettingsTab>('steam')
  const openInSteamClient = useOpenInSteamClient()
  // Whether a game start can be noticed at all (bridge connected or Steam on the server itself)
  const reach = useSteamReach()
  // The Windows program reads Steam on this PC itself: its settings have no bridge tab
  const desktopHost = useIsDesktopHost()
  // A server that reads Steam on its own computer needs no bridge (the tab stays for playing on another PC)
  const readsSteamLocally = useReadsSteamLocally()
  const visibleTabs = desktopHost ? SETTINGS_TABS.filter((tab) => tab.id !== 'bridge') : SETTINGS_TABS
  useEffect(() => {
    if (!open) return
    let remembered: SettingsTab | null = null
    try {
      const value = localStorage.getItem(LAST_TAB_KEY)
      remembered = SETTINGS_TABS.some((t) => t.id === value) ? (value as SettingsTab) : null
    } catch {
      remembered = null
    }
    // A notice comes from the Steam account linking round trip, so it belongs to the Steam tab
    const wanted = initialTab ?? (notice ? 'steam' : (remembered ?? 'steam'))
    setActiveTab(desktopHost && wanted === 'bridge' ? 'steam' : wanted)
  }, [open, initialTab, notice, desktopHost])
  const selectTab = (tab: SettingsTab) => {
    setActiveTab(tab)
    onTabChange?.(tab)
    try {
      localStorage.setItem(LAST_TAB_KEY, tab)
    } catch {
      // Storage unavailable (private mode) - the tab just isn't remembered
    }
  }

  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [settings, setSettings] = useState<SettingsResponseDto | null>(null)

  // AI selection (mode, Standard-KI, automatic fallback) and the research building blocks: saved with the rest
  const [aiMode, setAiMode] = useState<AiSelectionMode>('default')
  const [aiDefault, setAiDefault] = useState<AiProviderId | null>(null)
  const [aiFallback, setAiFallback] = useState(true)
  const [blocks, setBlocks] = useState<ResearchBlocksDto>(emptyResearchBlocks())
  const [geminiModel, setGeminiModel] = useState('gemini-3.8-flash')
  const [claudeModel, setClaudeModel] = useState('claude-sonnet-4-5')
  const [openAiModel, setOpenAiModel] = useState('gpt-4o')
  const [steamUserId, setSteamUserId] = useState('')

  const [playSounds, setPlaySounds] = useState(true)
  const [soundVolume, setSoundVolume] = useState(0.15)
  const [soundTheme, setSoundTheme] = useState<SoundTheme>(DEFAULT_SOUND_THEME)
  const [popupPosition, setPopupPosition] =
    useState<NotificationPosition>('top-right')
  // "Trophäen-Popup anzeigen": in the app, in the game (overlay of the Windows program) or both
  const [popupTarget, setPopupTarget] = useState<PopupTarget>(DEFAULT_POPUP_TARGET)
  // "Anzeigedauer der Trophäen-Popups" (live.popup_seconds): whole seconds, 2..10
  const [popupSeconds, setPopupSeconds] = useState(DEFAULT_POPUP_SECONDS)
  const [autoTranslate, setAutoTranslate] = useState(true)
  // Personal notes on achievements (off hides them everywhere; stored notes are kept)
  const [achievementNotes, setAchievementNotes] = useState(true)
  // "Im Blick": true = the server removes an unlocked achievement from the pinned ones (pinsAfterUnlock 'remove'), false = it stays as done
  const [pinsRemoveOnUnlock, setPinsRemoveOnUnlock] = useState(false)
  // Switch every open window to the game page when a game of the collection is started
  const [autoSwitchGame, setAutoSwitchGame] = useState(true)

  const [geminiKey, setGeminiKey] = useState('')
  const [claudeKey, setClaudeKey] = useState('')
  const [openAiKey, setOpenAiKey] = useState('')
  const [steamKey, setSteamKey] = useState('')

  const [clearGemini, setClearGemini] = useState(false)
  const [clearClaude, setClearClaude] = useState(false)
  const [clearOpenAi, setClearOpenAi] = useState(false)
  const [clearSteam, setClearSteam] = useState(false)

  // Twitch button (no key needed) + optional own Twitch developer app for IGDB facts
  const [twitchButton, setTwitchButton] = useState(true)
  const [twitchClientId, setTwitchClientId] = useState('')
  const [twitchSecret, setTwitchSecret] = useState('')
  const [clearTwitch, setClearTwitch] = useState(false)
  const [twitchTest, setTwitchTest] = useState<{ checking: boolean; result: TwitchTestResponseDto | null; error: string | null }>({
    checking: false,
    result: null,
    error: null,
  })

  const [geminiConn, setGeminiConn] =
    useState<ProviderConnectionState>(EMPTY_CONN)
  const [claudeConn, setClaudeConn] =
    useState<ProviderConnectionState>(EMPTY_CONN)
  const [openAiConn, setOpenAiConn] =
    useState<ProviderConnectionState>(EMPTY_CONN)

  const [statusMsg, setStatusMsg] = useState<{
    type: 'ok' | 'err'
    text: string
    // Headline and error code of a problem (the text is the explanation that unfolds below it)
    title?: string
    code?: string | null
  } | null>(null)

  const checkProvider = async (
    prov: 'gemini' | 'claude' | 'openai',
    typedKey: string,
    hasSavedKey: boolean,
    isCleared: boolean
  ) => {
    const trimmed = typedKey.trim()
    if ((!trimmed && !hasSavedKey) || (isCleared && !trimmed)) {
      const setter =
        prov === 'gemini'
          ? setGeminiConn
          : prov === 'claude'
            ? setClaudeConn
            : setOpenAiConn
      setter(EMPTY_CONN)
      return
    }

    const setter =
      prov === 'gemini'
        ? setGeminiConn
        : prov === 'claude'
          ? setClaudeConn
          : setOpenAiConn

    setter((prev) => ({ ...prev, checking: true, error: null }))
    try {
      const res = await probeAiModels(prov, trimmed || undefined)
      setter({
        checking: false,
        connected: res.connected,
        error: res.errorMessage,
        models: res.models || [],
      })
      if (res.connected && res.models.length > 0) {
        if (prov === 'gemini') {
          setGeminiModel((curr) =>
            res.models.some((m) => m.id === curr) ? curr : res.models[0]!.id
          )
        } else if (prov === 'claude') {
          setClaudeModel((curr) =>
            res.models.some((m) => m.id === curr) ? curr : res.models[0]!.id
          )
        } else if (prov === 'openai') {
          setOpenAiModel((curr) =>
            res.models.some((m) => m.id === curr) ? curr : res.models[0]!.id
          )
        }
      }
    } catch (err: unknown) {
      setter({
        checking: false,
        connected: false,
        error:
          err instanceof Error
            ? err.message
            : t('ai.row.probeFailed'),
        models: [],
      })
    }
  }

  useEffect(() => {
    if (!open) return
    setLoading(true)
    setStatusMsg(notice ? { ...notice, title: notice.type === 'err' ? t('footer.steamLinkFailed') : undefined } : null)
    fetchSettings()
      .then((data) => {
        setSettings(data)
        setAiMode(data.aiSelectionMode)
        setAiDefault(data.aiDefaultProvider)
        setAiFallback(data.aiAutoFallback)
        setBlocks({ ...data.researchBlocks, focus: [...data.researchBlocks.focus] })
        setGeminiModel(data.geminiModel || 'gemini-3.8-flash')
        setClaudeModel(data.claudeModel || 'claude-sonnet-4-5')
        setOpenAiModel(data.openAiModel || 'gpt-4o')
        setSteamUserId(data.steamUserId || '')
        setPlaySounds(data.playSounds !== false)
        setSoundVolume(data.soundVolume ?? 0.15)
        setSoundTheme(normalizeSoundTheme(data.soundTheme))
        setPopupPosition(data.popupPosition || 'top-right')
        setPopupTarget(normalizePopupTarget(data.popupTarget))
        setPopupSeconds(normalizePopupSeconds(data.popupSeconds))
        setAutoTranslate(data.autoTranslate !== false)
        setAchievementNotes(data.achievementNotesEnabled !== false)
        setPinsRemoveOnUnlock(data.pinsAfterUnlock === 'remove')
        setAutoSwitchGame(data.autoSwitchGameEnabled !== false)

        setGeminiKey('')
        setClaudeKey('')
        setOpenAiKey('')
        setSteamKey('')
        setClearGemini(false)
        setClearClaude(false)
        setClearOpenAi(false)
        setClearSteam(false)
        setTwitchButton(data.twitchStreamsEnabled !== false)
        setTwitchClientId('')
        setTwitchSecret('')
        setClearTwitch(false)
        setTwitchTest({ checking: false, result: null, error: null })

        if (data.hasGeminiKey) {
          void checkProvider('gemini', '', true, false)
        } else {
          setGeminiConn(EMPTY_CONN)
        }
        if (data.hasClaudeKey) {
          void checkProvider('claude', '', true, false)
        } else {
          setClaudeConn(EMPTY_CONN)
        }
        if (data.hasOpenAiKey) {
          void checkProvider('openai', '', true, false)
        } else {
          setOpenAiConn(EMPTY_CONN)
        }
      })
      .catch((err: unknown) => {
        setStatusMsg({
          type: 'err',
          text:
            err instanceof Error
              ? err.message
              : t('loadFailed'),
        })
      })
      .finally(() => setLoading(false))
  }, [open, notice])

  // Automatically probe Gemini when user types a new Gemini key
  useEffect(() => {
    if (!open) return
    const trimmed = geminiKey.trim()
    if (!trimmed) {
      if (!settings?.hasGeminiKey || clearGemini) {
        setGeminiConn(EMPTY_CONN)
      }
      return
    }
    if (trimmed.length < 12) return
    const timer = setTimeout(() => {
      void checkProvider('gemini', trimmed, !!settings?.hasGeminiKey, clearGemini)
    }, 550)
    return () => clearTimeout(timer)
  }, [geminiKey, clearGemini, open])

  // Automatically probe Claude when user types a new Claude key
  useEffect(() => {
    if (!open) return
    const trimmed = claudeKey.trim()
    if (!trimmed) {
      if (!settings?.hasClaudeKey || clearClaude) {
        setClaudeConn(EMPTY_CONN)
      }
      return
    }
    if (trimmed.length < 12) return
    const timer = setTimeout(() => {
      void checkProvider('claude', trimmed, !!settings?.hasClaudeKey, clearClaude)
    }, 550)
    return () => clearTimeout(timer)
  }, [claudeKey, clearClaude, open])

  // Automatically probe OpenAI when user types a new OpenAI key
  useEffect(() => {
    if (!open) return
    const trimmed = openAiKey.trim()
    if (!trimmed) {
      if (!settings?.hasOpenAiKey || clearOpenAi) {
        setOpenAiConn(EMPTY_CONN)
      }
      return
    }
    if (trimmed.length < 12) return
    const timer = setTimeout(() => {
      void checkProvider('openai', trimmed, !!settings?.hasOpenAiKey, clearOpenAi)
    }, 550)
    return () => clearTimeout(timer)
  }, [openAiKey, clearOpenAi, open])

  // Esc closes a save problem (it lies over the page; the page behind stays usable)
  const hasError = statusMsg?.type === 'err'
  useEffect(() => {
    if (!hasError) return
    const onKey = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') setStatusMsg(null)
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [hasError])

  if (!open) return null

  const handleTestPosition = (pos: NotificationPosition) => {
    setPopupPosition(pos)
    soundEngine.setEnabled(playSounds)
    soundEngine.setVolume(soundVolume)
    soundEngine.setTheme(soundTheme)
    animationQueue.clearQueue()
    animationQueue.enqueueAchievement({
      apiName: 'TEST_POSITION_PREVIEW',
      displayName: t('live.preview.name'),
      description: t('live.preview.description', { position: t(`live.positions.${pos}`) }),
      originalDescription: null,
      isTranslated: false,
      iconUrl:
        'https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1462040/header.jpg',
      iconGrayUrl: '',
      achieved: true,
      unlockTime: new Date().toISOString(),
      globalPercent: 3.1,
      hasGlobalPercent: true,
      rarityTier: 'Gold',
      rarityLabel: t('live.preview.rarity'),
      rarityBand: 'ultraRare',
      isHidden: false,
      userNote: '',
    }, { position: pos, seconds: popupSeconds })
  }

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    // Never send what the form shows as red; a new Twitch app needs both values together
    const typedId = twitchClientId.trim()
    const typedSecret = twitchSecret.trim()
    const twitchError =
      twitchCredentialProblem(typedId, t) || twitchCredentialProblem(typedSecret, t)
        ? t('twitch.saveProblem')
        : (!settings?.hasTwitchKey || clearTwitch) && Boolean(typedId) !== Boolean(typedSecret)
          ? t('twitch.saveNeedBoth')
          : null
    if (twitchError) {
      selectTab('twitch')
      setStatusMsg({ type: 'err', text: twitchError })
      return
    }
    setSaving(true)
    setStatusMsg(null)
    try {
      const updated = await updateSettings({
        aiSelectionMode: aiMode,
        aiDefaultProvider: aiDefault ?? undefined,
        aiAutoFallback: aiFallback,
        // Only a changed selection is sent; unchanged = the server keeps what it has
        researchBlocks: settings && !sameResearchBlocks(blocks, settings.researchBlocks) ? blocks : undefined,
        geminiModel,
        claudeModel,
        openAiModel,
        steamUserId,
        autoDetectGame: true,
        playSounds,
        soundVolume,
        soundTheme,
        popupPosition,
        popupTarget,
        popupSeconds,
        autoTranslate,
        geminiApiKey: geminiKey.trim() || undefined,
        clearGeminiKey: clearGemini,
        claudeApiKey: claudeKey.trim() || undefined,
        clearClaudeKey: clearClaude,
        openAiApiKey: openAiKey.trim() || undefined,
        clearOpenAiKey: clearOpenAi,
        steamApiKey: steamKey.trim() || undefined,
        clearSteamKey: clearSteam,
        twitchClientId: twitchClientId.trim() || undefined,
        twitchClientSecret: twitchSecret.trim() || undefined,
        clearTwitchKey: clearTwitch,
        twitchStreamsEnabled: twitchButton,
        achievementNotesEnabled: achievementNotes,
        pinsAfterUnlock: pinsRemoveOnUnlock ? 'remove' : 'keep',
        autoSwitchGameEnabled: autoSwitchGame,
      })

      soundEngine.setEnabled(updated.playSounds)
      soundEngine.setVolume(updated.soundVolume)
      soundEngine.setTheme(updated.soundTheme)

      setSettings(updated)
      setAiMode(updated.aiSelectionMode)
      setAiDefault(updated.aiDefaultProvider)
      setAiFallback(updated.aiAutoFallback)
      setBlocks({ ...updated.researchBlocks, focus: [...updated.researchBlocks.focus] })
      // A replaced key clears what was remembered about the old one, and the selection may have changed: read the AI states again
      void queryClient.invalidateQueries({ queryKey: AI_STATUS_KEY })
      setGeminiKey('')
      setClaudeKey('')
      setOpenAiKey('')
      setSteamKey('')
      setClearGemini(false)
      setClearClaude(false)
      setClearOpenAi(false)
      setClearSteam(false)
      setTwitchClientId('')
      setTwitchSecret('')
      setClearTwitch(false)
      setStatusMsg({
        type: 'ok',
        text: t('footer.saved'),
      })
      onSaved?.(updated)
    } catch (err: unknown) {
      setStatusMsg({
        type: 'err',
        text: err instanceof Error ? err.message : t('footer.saveFailed'),
        title: t('footer.saveFailed'),
        code: errorCodeOf(err),
      })
    } finally {
      setSaving(false)
    }
  }

  const tabTitleId = 'settings-modal-title'
  const panelId = 'settings-tabpanel'
  const volumeText = format.percent(Math.round(soundVolume * 100))

  // One title, one save button, one connected tab bar, one light card: the same inside the page (route /settings) and the old dialog frame
  const content = (
    <form ref={formRef} onSubmit={handleSave} className={`pv-set ${page ? '' : 'pv-set--dialog'}`.trim()}>
      <div className="pv-set-head">
        <div className="min-w-0">
          <h1 id={tabTitleId} className="pv-page-title">
            {t('title')}
          </h1>
          <p className="pv-page-sub">{t('subtitle')}</p>
        </div>
        <div className="pv-set-save">
          {/* Fixed room for the confirmation: it appears in place, the button never moves */}
          <ReservedText
            className="pv-set-status"
            role="status"
            candidates={[t('footer.saved'), ...(notice?.type === 'ok' ? [notice.text] : [])]}
          >
            {statusMsg?.type === 'ok' ? statusMsg.text : ''}
          </ReservedText>
          {!page && (
            <button type="button" onClick={onClose} className="pv-btn">
              {tc('close')}
            </button>
          )}
          {/* Busy state: the label stays (invisible) and a spinner covers it, so the button keeps its size in both languages */}
          <button type="submit" disabled={saving || loading} aria-busy={saving || undefined} className="pv-btn pv-btn-primary relative">
            <BridgeBusyLabel busy={saving}>
              <IconCheck size={17} />
              <span>{t('footer.save')}</span>
            </BridgeBusyLabel>
          </button>
        </div>
      </div>

      {/* A problem lies over the content (never inside the flow): headline with code, an arrow for the details, copy */}
      {statusMsg?.type === 'err' && (
        <div className="pv-set-banner" data-testid="settings-error">
          <ErrorBlock title={statusMsg.title ?? t('footer.saveFailed')} code={statusMsg.code} message={statusMsg.text} defaultOpen />
          <div className="pv-set-banner__close">
            <GhostIconButton label={tc('close')} size="md" onClick={() => setStatusMsg(null)}>
              <IconX size={17} aria-hidden="true" />
            </GhostIconButton>
          </div>
        </div>
      )}

      <div className="pv-segmented pv-set-tabs" role="tablist" aria-label={t('tabsLabel')}>
        {visibleTabs.map((tab) => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            aria-selected={activeTab === tab.id}
            aria-controls={panelId}
            onClick={() => selectTab(tab.id)}
          >
            {t(`tabs.${tab.id}.label`)}
          </button>
        ))}
      </div>

      <div id={panelId} role="tabpanel" aria-labelledby={tabTitleId} className="min-w-0">
        {loading ? (
          <p className="pv-set-muted py-12 text-center">{t('loading')}</p>
        ) : (
          <>
            {/* Tab "Live & Sound": trophy popups, sound & position (applies to every game with achievements enabled) */}
            {activeTab === 'live' && (
              <SettingsCard>
                <SettingsRow title={t('live.previewTitle')} help={t('live.previewHint')}>
                  <button
                    type="button"
                    onClick={() => {
                      soundEngine.setTheme(soundTheme)
                      soundEngine.setVolume(soundVolume)
                      soundEngine.setEnabled(true)
                      soundEngine.playTrophyUnlock('Gold')
                    }}
                    className="pv-btn"
                  >
                    <IconVolume size={17} aria-hidden="true" />
                    <span>{t('live.previewSound')}</span>
                  </button>
                  {/* Same local queue the live 100% event uses, so this shows the real platinum moment */}
                  <Tooltip label={t('live.platinumTest')} description={t('live.platinumTestHint')}>
                    <button
                      type="button"
                      onClick={() => {
                        soundEngine.setTheme(soundTheme)
                        soundEngine.setVolume(soundVolume)
                        soundEngine.setEnabled(playSounds)
                        animationQueue.clearQueue()
                        animationQueue.enqueuePlatinum(t('live.exampleGame'), 40)
                      }}
                      className="pv-btn"
                    >
                      <TierIcon tier="Platinum" size={17} />
                      <span>{t('live.platinumTest')}</span>
                    </button>
                  </Tooltip>
                </SettingsRow>

                {/* 3x3 position grid (one connected frame, every cell a mini screen with the popup silhouette in its spot) and the tenth way
                    "fullscreen" as its own connected bar */}
                <SettingsRow wide title={t('live.positionLabel')} help={t('live.positionHint')} titleId="pv-popup-position-label">
                  <div className="pv-set-pos-wrap">
                    <div className="pv-set-pos" role="group" aria-labelledby="pv-popup-position-label">
                      {POSITIONS.map((pos) => (
                        <Tooltip key={pos} label={t(`live.positions.${pos}`)} className="pv-set-pos__cell">
                          <button
                            type="button"
                            aria-pressed={popupPosition === pos}
                            aria-label={t(`live.positions.${pos}`)}
                            onClick={() => handleTestPosition(pos)}
                          >
                            {/* Mini screen preview: a small popup silhouette drawn at this cell's own spot (CSS places it from data-pos) */}
                            <span className="pv-set-pos__shape" data-pos={pos} aria-hidden="true" />
                          </button>
                        </Tooltip>
                      ))}
                    </div>
                    {/* The tenth way: the whole window, for a few seconds */}
                    <div className="pv-segmented" role="group">
                      <button
                        type="button"
                        onClick={() => handleTestPosition('fullscreen')}
                        aria-pressed={popupPosition === 'fullscreen'}
                        data-position="fullscreen"
                      >
                        <IconArrowsMaximize size={17} aria-hidden="true" />
                        <span>{t('live.positions.fullscreen')}</span>
                      </button>
                    </div>
                    <p className="pv-set-row__help" style={{ margin: 0 }}>
                      {t('live.positionFullscreenHint')}
                    </p>
                  </div>
                </SettingsRow>

                {/* "Anzeigedauer der Trophäen-Popups" (live.popup_seconds): a native range input (keyboard: arrows, Home, End) with a
                    fixed-width value, so nothing moves while it is changed. The test popups use the chosen value. */}
                <SettingsRow
                  title={t('live.duration.label')}
                  labelFor="pv-popup-seconds"
                  help={<span id="pv-popup-seconds-hint">{t('live.duration.hint', { min: MIN_POPUP_SECONDS, max: MAX_POPUP_SECONDS })}</span>}
                >
                  <div className="pv-set-range" data-popup-seconds="">
                    <input
                      id="pv-popup-seconds"
                      type="range"
                      min={MIN_POPUP_SECONDS}
                      max={MAX_POPUP_SECONDS}
                      step="1"
                      value={popupSeconds}
                      onChange={(e) => setPopupSeconds(normalizePopupSeconds(Number(e.target.value)))}
                      aria-valuetext={t('live.duration.value', { seconds: popupSeconds })}
                      aria-describedby="pv-popup-seconds-hint"
                      className="pv-range"
                    />
                    <output htmlFor="pv-popup-seconds" className="inline-block min-w-[4rem] text-right tabular-nums" data-popup-seconds-value="">
                      {t('live.duration.value', { seconds: popupSeconds })}
                    </output>
                  </div>
                </SettingsRow>

                {/* "Trophäen-Popup anzeigen" (live.popup_target). Without the Windows program there is no overlay: those two choices stay
                    visible but disabled, the effect is "in the app". */}
                <SettingsRow
                  title={t('live.target.label')}
                  titleId="pv-popup-target-label"
                  help={<span id="pv-popup-target-hint">{desktopHost ? t('live.target.hintDesktop') : t('live.target.onlyDesktop')}</span>}
                >
                  <div className="pv-segmented" role="group" aria-labelledby="pv-popup-target-label">
                    {POPUP_TARGETS.map((target) => {
                      const unavailable = !desktopHost && target !== 'app'
                      const selected = desktopHost ? popupTarget === target : target === 'app'
                      return (
                        <button
                          key={target}
                          type="button"
                          data-popup-target={target}
                          aria-pressed={selected}
                          disabled={unavailable}
                          aria-describedby="pv-popup-target-hint"
                          onClick={() => setPopupTarget(target)}
                        >
                          {t(`live.target.${target}`)}
                        </button>
                      )
                    })}
                  </div>
                </SettingsRow>

                {/* Own sounds from real instrument recordings (CC0); a click selects and previews the theme */}
                <SettingsRow
                  title={t('live.soundTheme').replace(/:$/, '')}
                  titleId="pv-sound-theme-label"
                  help={
                    <ReservedText candidates={SOUND_THEMES.map((theme) => t(`live.soundThemes.${theme.id}.description`))} aria-live="polite">
                      {t(`live.soundThemes.${soundTheme}.description`)}
                    </ReservedText>
                  }
                >
                  <div className="pv-segmented" role="group" aria-labelledby="pv-sound-theme-label">
                    {SOUND_THEMES.map((theme) => (
                      <Tooltip key={theme.id} label={t(`live.soundThemes.${theme.id}.label`)} description={t(`live.soundThemes.${theme.id}.description`)} className="pv-seg-tip">
                        <button
                          type="button"
                          aria-pressed={soundTheme === theme.id}
                          onClick={() => {
                            setSoundTheme(theme.id)
                            soundEngine.setVolume(soundVolume)
                            soundEngine.preview(theme.id, 'Gold')
                          }}
                        >
                          <IconMusic size={17} aria-hidden="true" />
                          <span>{t(`live.soundThemes.${theme.id}.label`)}</span>
                        </button>
                      </Tooltip>
                    ))}
                  </div>
                </SettingsRow>

                <SettingsRow title={t('live.volumeLabel')} labelFor="pv-sound-volume" help={t('live.volumeRecommended')}>
                  <div className="pv-set-range">
                    <input
                      id="pv-sound-volume"
                      type="range"
                      min="0"
                      max="0.6"
                      step="0.01"
                      value={soundVolume}
                      onChange={(e) => setSoundVolume(parseFloat(e.target.value))}
                      aria-valuetext={volumeText}
                      className="pv-range"
                    />
                    <output htmlFor="pv-sound-volume" className="inline-block min-w-[4rem] text-right tabular-nums">
                      {volumeText}
                    </output>
                  </div>
                </SettingsRow>

                <SettingsToggle checked={playSounds} onChange={setPlaySounds} title={t('live.playSounds')} />

                {/* Only German needs this: in English Steam delivers its own English texts, there is nothing to translate */}
                {language === 'de' && <SettingsToggle checked={autoTranslate} onChange={setAutoTranslate} title={t('live.autoTranslate')} />}

                {/* Game start: every open window switches to the game page (switch "features.auto_switch_game") */}
                <SettingsToggle checked={autoSwitchGame} onChange={setAutoSwitchGame} title={t('live.gameStartSwitch')} help={t('live.gameStartText')}>
                  {/* Where the start is noticed from, or why nothing can be noticed now; the room is reserved for the longest of the three */}
                  <ReservedText
                    role="status"
                    className="pv-set-row__help"
                    style={{ margin: '0 0 0.625rem', paddingInlineEnd: '4rem', color: 'var(--text-secondary)' }}
                    candidates={[t('live.gameStartSourceLocal'), t('live.gameStartSourceBridge'), t('live.gameStartNoSource')]}
                  >
                    {reach.local ? t('live.gameStartSourceLocal') : reach.bridgeConnected ? t('live.gameStartSourceBridge') : t('live.gameStartNoSource')}
                  </ReservedText>
                </SettingsToggle>

                {/* Trophy list: personal notes on achievements (switch "features.achievement_notes"); off hides them everywhere */}
                <SettingsToggle checked={achievementNotes} onChange={setAchievementNotes} title={t('live.notesSwitch')} help={t('live.notesText')} />

                {/* "Im Blick" (pinned achievements): what happens to a pin once its achievement is unlocked (setting pins.after_unlock) */}
                <SettingsToggle checked={pinsRemoveOnUnlock} onChange={setPinsRemoveOnUnlock} title={t('live.pinsSwitch')} help={t('live.pinsText')} />
              </SettingsCard>
            )}

            {/* Tab "Steam": account (OpenID linking) + Steam Web API key */}
            {activeTab === 'steam' && (
              <SettingsCard>
                <SettingsRow
                  title={settings?.steamUserId ? t('steam.linked', { id: settings.steamUserId }) : t('steam.notLinked')}
                  help={t('steam.loginNote')}
                >
                  <a href={steamLoginUrl()} className="pv-btn pv-btn-primary shrink-0">
                    <IconLogin2 size={17} aria-hidden="true" />
                    <span>{settings?.steamUserId ? t('steam.otherAccount') : t('steam.login')}</span>
                  </a>
                </SettingsRow>

                <SettingsRow
                  title={t('steam.keyLabel')}
                  labelFor="pv-steam-key"
                  help={
                    <>
                      <a href="https://steamcommunity.com/dev/apikey" target="_blank" rel="noreferrer" className="pv-set-link">
                        {t('steam.getKey')}
                      </a>
                      {settings?.hasSteamKey && !clearSteam && (
                        <span className="pv-set-chip ms-3">
                          <IconCheck size={15} aria-hidden="true" /> {t('steam.active', { hint: settings.steamKeyHint })}
                        </span>
                      )}
                    </>
                  }
                >
                  <input
                    id="pv-steam-key"
                    type="password"
                    value={steamKey}
                    onChange={(e) => {
                      setSteamKey(e.target.value)
                      if (e.target.value) setClearSteam(false)
                    }}
                    placeholder={settings?.hasSteamKey && !clearSteam ? t('steam.keySaved', { hint: settings.steamKeyHint }) : t('steam.keyEmpty')}
                    className="pv-input"
                    autoComplete="off"
                  />
                  {settings?.hasSteamKey && (
                    <IconButton label={t('steam.deleteKey')} aria-pressed={clearSteam} onClick={() => setClearSteam(!clearSteam)}>
                      <IconTrash size={20} />
                    </IconButton>
                  )}
                </SettingsRow>

                <SettingsRow title={t('steam.manualIdLabel')} labelFor="pv-steam-id">
                  <input
                    id="pv-steam-id"
                    type="text"
                    value={steamUserId}
                    onChange={(e) => setSteamUserId(e.target.value)}
                    placeholder={t('steam.manualIdPlaceholder')}
                    className="pv-input"
                  />
                </SettingsRow>

                {/* Where the key comes from (Steam's own docs: partner.steamgames.com/doc/webapi_overview/auth) */}
                <SettingsRow wide title={t('steam.guide.title')}>
                  <SettingsGuide>
                    <ol>
                      <li>
                        <Trans t={t} i18nKey="steam.guide.step1" components={{ link: <a href="https://steamcommunity.com/dev/apikey" target="_blank" rel="noreferrer" /> }} />
                        {/* Only on the gaming PC itself: the Steam client is usually signed in already */}
                        {openInSteamClient && (
                          <>
                            {' '}
                            <Trans t={t} i18nKey="steam.guide.step1Client" components={{ clientLink: <a href={steamClientUrl('https://steamcommunity.com/dev/apikey')} /> }} />
                          </>
                        )}
                      </li>
                      <li>{t('steam.guide.step2')}</li>
                      <li>{t('steam.guide.step3')}</li>
                      <li>{t('steam.guide.step4')}</li>
                    </ol>
                    <p>{t('steam.guide.privacy')}</p>
                  </SettingsGuide>
                </SettingsRow>

                {/* The Steam folder on the computer PlatinVault runs on (only shown where PlatinVault reads Steam itself) */}
                <SteamFolderSection />
              </SettingsCard>
            )}

            {/* Tab "Twitch & IGDB": Twitch button (link only, no key) + optional own Twitch developer app for IGDB */}
            {activeTab === 'twitch' && (
              <SettingsCard>
                <SettingsToggle checked={twitchButton} onChange={setTwitchButton} title={t('twitch.buttonSwitch')} help={t('twitch.buttonText')} />

                <SettingsTopic>{t('twitch.igdbTitle')}</SettingsTopic>
                <p className="pv-set-muted" style={{ margin: '0.375rem 0 0.25rem' }}>
                  {t('twitch.igdbIntro')}
                  {settings?.hasTwitchKey && !clearTwitch && (
                    <span className="pv-set-chip ms-3">
                      <IconCheck size={15} aria-hidden="true" /> {t('steam.active', { hint: settings.twitchKeyHint })}
                    </span>
                  )}
                </p>

                <TwitchCredentialField
                  label={t('twitch.clientId')}
                  value={twitchClientId}
                  onChange={(v) => {
                    setTwitchClientId(v)
                    if (v) setClearTwitch(false)
                  }}
                  placeholder={settings?.hasTwitchKey && !clearTwitch ? t('twitch.idSaved', { hint: settings.twitchKeyHint }) : t('twitch.idEmpty')}
                />
                <TwitchCredentialField
                  label={t('twitch.clientSecret')}
                  secret
                  value={twitchSecret}
                  onChange={(v) => {
                    setTwitchSecret(v)
                    if (v) setClearTwitch(false)
                  }}
                  placeholder={settings?.hasTwitchKey && !clearTwitch ? t('twitch.secretSaved') : t('twitch.secretEmpty')}
                />

                <SettingsRow
                  title={t('twitch.test')}
                  help={
                    /* Reserved line, so the result does not push the guide below */
                    <ReservedText
                      role="status"
                      className="font-medium"
                      style={{
                        color: twitchTest.error || (twitchTest.result && !twitchTest.result.connected) ? 'var(--accent-crimson)' : 'var(--accent-emerald)',
                      }}
                      candidates={[twitchTest.error ?? twitchTest.result?.message ?? '']}
                    >
                      {twitchTest.error ?? twitchTest.result?.message ?? ''}
                      {(twitchTest.error || (twitchTest.result && !twitchTest.result.connected)) && (
                        <CopyErrorButton className="ms-2 align-middle" report={{ message: twitchTest.error ?? twitchTest.result?.message }} />
                      )}
                    </ReservedText>
                  }
                >
                  <button
                    type="button"
                    className="pv-btn relative"
                    disabled={
                      twitchTest.checking ||
                      (!settings?.hasTwitchKey && !(twitchClientId.trim() && twitchSecret.trim())) ||
                      (clearTwitch && !(twitchClientId.trim() && twitchSecret.trim()))
                    }
                    aria-busy={twitchTest.checking || undefined}
                    onClick={async () => {
                      setTwitchTest({ checking: true, result: null, error: null })
                      try {
                        const result = await testTwitchConnection(twitchClientId.trim() || undefined, twitchSecret.trim() || undefined)
                        setTwitchTest({ checking: false, result, error: null })
                      } catch (err: unknown) {
                        setTwitchTest({
                          checking: false,
                          result: null,
                          error: err instanceof Error ? err.message : t('twitch.testFailed'),
                        })
                      }
                    }}
                  >
                    <BridgeBusyLabel busy={twitchTest.checking}>
                      <IconPlugConnected size={17} aria-hidden="true" />
                      <span>{t('twitch.test')}</span>
                    </BridgeBusyLabel>
                  </button>
                  {settings?.hasTwitchKey && (
                    <button type="button" onClick={() => setClearTwitch(!clearTwitch)} className="pv-btn-text pv-btn-text--calm" aria-pressed={clearTwitch}>
                      <IconTrash size={17} aria-hidden="true" />
                      <span>{clearTwitch ? t('twitch.willDelete') : t('twitch.deleteSaved')}</span>
                    </button>
                  )}
                </SettingsRow>

                {/* Steps as described in IGDB's own docs (api-docs.igdb.com, "Account Creation") */}
                <SettingsRow wide title={t('twitch.guide.title')}>
                  <SettingsGuide>
                    <ol>
                      <li>{t('twitch.guide.step1')}</li>
                      <li>
                        <Trans t={t} i18nKey="twitch.guide.step2" components={{ securityLink: <a href="https://www.twitch.tv/settings/security" target="_blank" rel="noreferrer" /> }} />
                      </li>
                      <li>
                        <Trans
                          t={t}
                          i18nKey="twitch.guide.step3"
                          components={{
                            consoleLink: <a href="https://dev.twitch.tv/console/apps/create" target="_blank" rel="noreferrer" />,
                            code: <code />,
                          }}
                        />
                      </li>
                      <li>
                        <Trans t={t} i18nKey="twitch.guide.step4" components={{ strong: <strong /> }} />
                      </li>
                      <li>{t('twitch.guide.step5')}</li>
                      <li>{t('twitch.guide.step6')}</li>
                    </ol>
                    <p>{t('twitch.guide.privacy')}</p>
                  </SettingsGuide>
                </SettingsRow>
              </SettingsCard>
            )}

            {/* Tab "PlatinVault Bridge": token + download for the bridge program on the gaming PC */}
            {activeTab === 'bridge' && !desktopHost && (
              <SettingsCard>
                {/* PlatinVault reads Steam on this computer itself (native Linux package): say that the bridge is only for another PC */}
                {readsSteamLocally && (
                  <p className="pv-set-muted" style={{ margin: 0, paddingBlock: '0.875rem', borderBlockEnd: '1px solid var(--border-subtle)' }} data-testid="bridge-not-needed">
                    {t('steamLocal.bridgeNotNeeded')}
                  </p>
                )}
                <BridgeSection />
              </SettingsCard>
            )}

            {/* Tab "System": the design first, then updates (channel, check, install or image tag), the folders and the health check */}
            {activeTab === 'system' && (
              <SettingsCard>
                <DesignSection />
                <UpdatesSection />
                <FoldersSection />
                <SystemStatusSection />
              </SettingsCard>
            )}

            {/* Tab "KI-Recherche": AI providers with a model dropdown once a key connects */}
            {activeTab === 'ki' && (
              <SettingsCard>
                <AiSelectionSection
                  mode={aiMode}
                  onModeChange={setAiMode}
                  defaultProvider={aiDefault}
                  onDefaultProviderChange={setAiDefault}
                  autoFallback={aiFallback}
                  onAutoFallbackChange={setAiFallback}
                  hasKey={{
                    gemini: Boolean(geminiKey.trim()) || (Boolean(settings?.hasGeminiKey) && !clearGemini),
                    claude: Boolean(claudeKey.trim()) || (Boolean(settings?.hasClaudeKey) && !clearClaude),
                    openai: Boolean(openAiKey.trim()) || (Boolean(settings?.hasOpenAiKey) && !clearOpenAi),
                  }}
                />
                <SettingsTopic>{t('ai.title')}</SettingsTopic>

                {/* 1. Google Gemini */}
                <ProviderKeyAndModelRow
                  label={t('ai.geminiLabel')}
                  hint={<Trans t={t} i18nKey="ai.geminiHint" components={{ keyLink: <a href="https://aistudio.google.com/apikey" target="_blank" rel="noreferrer" className="pv-set-link" /> }} />}
                  placeholder="AIzaSy..."
                  keyValue={geminiKey}
                  onKeyChange={(val) => {
                    setGeminiKey(val)
                    if (val) setClearGemini(false)
                  }}
                  hasSavedKey={!!settings?.hasGeminiKey}
                  savedKeyHint={settings?.geminiKeyHint ?? ''}
                  isCleared={clearGemini}
                  onToggleClear={() => setClearGemini(!clearGemini)}
                  conn={geminiConn}
                  onRecheck={() => void checkProvider('gemini', geminiKey, !!settings?.hasGeminiKey, clearGemini)}
                  selectedModel={geminiModel}
                  onSelectModel={setGeminiModel}
                />
                {settings?.hasGeminiKey && !clearGemini && <GeminiDiagnoseBlock model={geminiModel} />}

                {/* 2. Anthropic Claude */}
                <ProviderKeyAndModelRow
                  label={t('ai.claudeLabel')}
                  hint={<Trans t={t} i18nKey="ai.claudeHint" components={{ keyLink: <a href="https://console.anthropic.com/settings/keys" target="_blank" rel="noreferrer" className="pv-set-link" /> }} />}
                  placeholder="sk-ant-api03-..."
                  keyValue={claudeKey}
                  onKeyChange={(val) => {
                    setClaudeKey(val)
                    if (val) setClearClaude(false)
                  }}
                  hasSavedKey={!!settings?.hasClaudeKey}
                  savedKeyHint={settings?.claudeKeyHint ?? ''}
                  isCleared={clearClaude}
                  onToggleClear={() => setClearClaude(!clearClaude)}
                  conn={claudeConn}
                  onRecheck={() => void checkProvider('claude', claudeKey, !!settings?.hasClaudeKey, clearClaude)}
                  selectedModel={claudeModel}
                  onSelectModel={setClaudeModel}
                />

                {/* 3. OpenAI ChatGPT */}
                <ProviderKeyAndModelRow
                  label={t('ai.openaiLabel')}
                  placeholder="sk-proj-..."
                  keyValue={openAiKey}
                  onKeyChange={(val) => {
                    setOpenAiKey(val)
                    if (val) setClearOpenAi(false)
                  }}
                  hasSavedKey={!!settings?.hasOpenAiKey}
                  savedKeyHint={settings?.openAiKeyHint ?? ''}
                  isCleared={clearOpenAi}
                  onToggleClear={() => setClearOpenAi(!clearOpenAi)}
                  conn={openAiConn}
                  onRecheck={() => void checkProvider('openai', openAiKey, !!settings?.hasOpenAiKey, clearOpenAi)}
                  selectedModel={openAiModel}
                  onSelectModel={setOpenAiModel}
                />

                {/* Predefined research building blocks (no free text): scope, focus, spoilers, presentation */}
                <ResearchBlocksSection value={blocks} onChange={setBlocks} policy={settings?.researchBlocksPolicy} />
              </SettingsCard>
            )}
          </>
        )}
      </div>
    </form>
  )

  // Page mode: the content area of the app shell is the frame. Otherwise the same content in the old dialog frame.
  if (page) return <section aria-labelledby={tabTitleId}>{content}</section>
  return (
    <div className="pv-modal-backdrop" role="dialog" aria-modal="true" aria-labelledby={tabTitleId}>
      <div className="pv-modal flex h-[min(90dvh,860px)] w-full max-w-5xl flex-col overflow-y-auto">{content}</div>
    </div>
  )
}

// Cheaper model families get a visible "Spar-Tipp" mark, so users without a big budget pick them deliberately.
// Two tiny real requests with the stored key (without and with Google Search): shows what the key may really use
function GeminiDiagnoseBlock({ model }: { model: string }) {
  const { t } = useTranslation('settings')
  const [state, setState] = useState<{ busy: boolean; result: GeminiDiagnosisDto | null; error: string | null }>({
    busy: false,
    result: null,
    error: null,
  })
  const run = async () => {
    setState({ busy: true, result: null, error: null })
    try {
      setState({ busy: false, result: await diagnoseGemini(model), error: null })
    } catch (err: unknown) {
      setState({ busy: false, result: null, error: err instanceof Error ? err.message : t('ai.diagnose.failed') })
    }
  }
  const line = (label: string, r: GeminiDiagnosisDto['plain']) => (
    <div className="flex items-start gap-2">
      {r.ok ? (
        <IconCheck size={17} className="mt-0.5 shrink-0" style={{ color: 'var(--accent-emerald)' }} aria-hidden="true" />
      ) : (
        <IconAlertTriangle size={17} className="mt-0.5 shrink-0" style={{ color: 'var(--accent-crimson)' }} aria-hidden="true" />
      )}
      <span>
        <strong style={{ fontWeight: 600 }}>{label}:</strong> {r.message}
      </span>
    </div>
  )
  const failedReport = state.result
    ? [
        { label: t('ai.diagnose.plain'), result: state.result.plain },
        { label: t('ai.diagnose.search'), result: state.result.search },
      ]
        .filter((entry) => !entry.result.ok)
        .map((entry) => `${entry.label}: ${entry.result.message}`)
        .join('\n')
    : ''
  return (
    <SettingsRow wide title={t('ai.diagnose.run')} help={t('ai.diagnose.intro', { model })}>
      <button type="button" className="pv-btn relative" onClick={() => void run()} disabled={state.busy} aria-busy={state.busy || undefined}>
        <BridgeBusyLabel busy={state.busy}>
          <IconPlugConnected size={17} aria-hidden="true" />
          {t('ai.diagnose.run')}
        </BridgeBusyLabel>
      </button>
      {(state.result || state.error) && (
        <div className="w-full space-y-2" role="status">
          {state.error && <ErrorBlock title={t('ai.diagnose.failed')} message={state.error} />}
          {state.result && line(t('ai.diagnose.plain'), state.result.plain)}
          {state.result && line(t('ai.diagnose.search'), state.result.search)}
          {state.result && failedReport && <CopyErrorButton variant="text" report={{ message: failedReport }} />}
        </div>
      )}
    </SettingsRow>
  )
}

// Same rule as the server (SettingsStore): 8-128 characters of letters, digits, '-' and '_'
function twitchCredentialProblem(value: string, t: TFunction<'settings'>): string | null {
  const v = value.trim()
  if (!v) return null
  if (/[^A-Za-z0-9_-]/.test(v)) return t('twitch.problemChars')
  if (v.length < 8) return t('twitch.problemTooShort', { length: v.length, min: 8 })
  if (v.length > 128) return t('twitch.problemTooLong', { length: v.length, max: 128 })
  return null
}

function TwitchCredentialField({
  label,
  value,
  onChange,
  placeholder,
  secret = false,
}: {
  label: string
  value: string
  onChange: (value: string) => void
  placeholder: string
  secret?: boolean
}) {
  const { t } = useTranslation('settings')
  const id = useId()
  const problem = twitchCredentialProblem(value, t)
  return (
    <SettingsRow
      title={label}
      labelFor={id}
      help={
        /* Reserved room for the longest message (every one of them, in this language and at this width), so nothing below moves while typing */
        <ReservedText
          id={`${id}-msg`}
          className="font-medium"
          style={{ color: 'var(--accent-crimson)' }}
          candidates={[t('twitch.problemChars'), t('twitch.problemTooShort', { length: 128, min: 8 }), t('twitch.problemTooLong', { length: 128, max: 128 })]}
        >
          {problem ?? ''}
        </ReservedText>
      }
    >
      <input
        id={id}
        type={secret ? 'password' : 'text'}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        autoComplete="off"
        spellCheck={false}
        aria-invalid={problem ? true : undefined}
        aria-describedby={`${id}-msg`}
        className="pv-input"
        style={problem ? { borderColor: 'var(--accent-crimson)' } : undefined}
      />
    </SettingsRow>
  )
}

function isBudgetModel(modelId: string): boolean {
  return /flash|haiku|mini|nano|lite/i.test(modelId)
}

interface ProviderKeyAndModelRowProps {
  label: string
  // Optional help line under the label (e.g. where to get a key)
  hint?: React.ReactNode
  placeholder: string
  keyValue: string
  onKeyChange: (val: string) => void
  hasSavedKey: boolean
  savedKeyHint: string
  isCleared: boolean
  onToggleClear: () => void
  conn: ProviderConnectionState
  onRecheck: () => void
  selectedModel: string
  onSelectModel: (modelId: string) => void
}

function ProviderKeyAndModelRow({
  label,
  hint,
  placeholder,
  keyValue,
  onKeyChange,
  hasSavedKey,
  savedKeyHint,
  isCleared,
  onToggleClear,
  conn,
  onRecheck,
  selectedModel,
  onSelectModel,
}: ProviderKeyAndModelRowProps) {
  const { t } = useTranslation('settings')
  const hasAnyKey = Boolean(keyValue.trim()) || (hasSavedKey && !isCleared)

  const inputId = useId()
  const modelsShown = conn.connected && conn.models.length > 0

  return (
    <>
      <SettingsRow
        title={label}
        labelFor={inputId}
        help={
          <>
            {hint && <span className="block">{hint}</span>}
            {conn.checking ? (
              <span className="inline-flex items-center gap-1.5" style={{ color: 'var(--link)' }}>
                <IconLoader2 size={15} className="animate-spin" aria-hidden="true" />
                {t('ai.row.checking')}
              </span>
            ) : conn.connected ? (
              <span className="pv-set-chip">
                <IconCheck size={15} aria-hidden="true" /> {t('ai.row.connected', { count: conn.models.length })}
              </span>
            ) : hasAnyKey && conn.error ? null : (
              <span>{t('ai.row.none')}</span>
            )}
          </>
        }
      >
        <input
          id={inputId}
          type="password"
          value={keyValue}
          onChange={(e) => onKeyChange(e.target.value)}
          placeholder={hasSavedKey && !isCleared ? t('ai.row.saved', { hint: savedKeyHint }) : placeholder}
          className="pv-input"
          autoComplete="off"
        />
        {hasAnyKey && (
          <IconButton label={t('ai.row.recheck')} description={t('ai.row.recheckHint')} aria-label={t('ai.row.recheckLabel')} onClick={onRecheck} disabled={conn.checking}>
            <IconRefresh size={20} className={conn.checking ? 'animate-spin' : ''} />
          </IconButton>
        )}
        {hasSavedKey && (
          <IconButton label={t('ai.row.removeKey')} aria-pressed={isCleared} onClick={onToggleClear}>
            <IconTrash size={20} />
          </IconButton>
        )}
        {/* Model dropdown is ONLY rendered when an API key is present AND the connection succeeded */}
        {modelsShown && (
          <select
            value={selectedModel}
            onChange={(e) => onSelectModel(e.target.value)}
            aria-label={t('ai.row.modelLabel', { provider: label })}
            className="pv-select"
          >
            {conn.models.map((m) => (
              <option key={m.id} value={m.id}>
                {isBudgetModel(m.id) ? t('ai.row.budgetTip', { model: m.label }) : m.label}
              </option>
            ))}
          </select>
        )}
      </SettingsRow>
      {hasAnyKey && !conn.connected && conn.error && !conn.checking && (
        <div className="pv-set-row pv-set-row--wide" style={{ paddingBlock: '0.25rem' }}>
          <ErrorBlock title={t('ai.row.failedTitle', { provider: label })} message={conn.error} />
        </div>
      )}
    </>
  )
}

const BRIDGE_KEY = ['bridge-token'] as const

// The bridge sends a heartbeat every 60 s; a little more than two missed beats counts as "not connected".
const BRIDGE_ONLINE_WINDOW_MS = 150_000

const DIALOG_FOCUSABLE =
  'a[href], button:not(:disabled), textarea:not(:disabled), input:not(:disabled), select:not(:disabled), [tabindex]:not([tabindex="-1"])'

interface BridgeDialogProps {
  labelledBy: string
  // While busy the dialog cannot be dismissed
  busy?: boolean
  onClose: () => void
  children: ReactNode
}

// Small modal on top of the settings modal (rendered into <body>, so the settings form never sees its buttons).
function BridgeDialog({ labelledBy, busy = false, onClose, children }: BridgeDialogProps) {
  const panelRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null
    const panel = panelRef.current
    const first =
      panel?.querySelector<HTMLElement>('[data-autofocus]') ?? panel?.querySelector<HTMLElement>(DIALOG_FOCUSABLE)
    first?.focus()
    return () => {
      previouslyFocused?.focus?.()
    }
  }, [])

  const handleKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Escape') {
      e.stopPropagation()
      if (!busy) onClose()
      return
    }
    if (e.key !== 'Tab' || !panelRef.current) return
    const items = Array.from(panelRef.current.querySelectorAll<HTMLElement>(DIALOG_FOCUSABLE))
    if (items.length === 0) return
    const firstItem = items[0]!
    const lastItem = items[items.length - 1]!
    if (e.shiftKey && document.activeElement === firstItem) {
      e.preventDefault()
      lastItem.focus()
    } else if (!e.shiftKey && document.activeElement === lastItem) {
      e.preventDefault()
      firstItem.focus()
    }
  }

  return createPortal(
    <div
      className="pv-modal-backdrop pv-modal-backdrop--top"
      style={{ zIndex: 60 }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget && !busy) onClose()
      }}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={labelledBy}
        onKeyDown={handleKeyDown}
        className="pv-modal w-full max-w-lg max-h-[92dvh] overflow-y-auto"
      >
        {children}
      </div>
    </div>,
    document.body
  )
}

// Button whose busy state never changes its size: the spinner is drawn over the invisible label.
function BridgeBusyLabel({ busy, children }: { busy: boolean; children: ReactNode }) {
  return (
    <>
      <span className={`inline-flex items-center gap-1.5 ${busy ? 'invisible' : ''}`}>{children}</span>
      {busy && (
        <span className="absolute inset-0 flex items-center justify-center" aria-hidden="true">
          <IconLoader2 size={16} className="animate-spin" />
        </span>
      )}
    </>
  )
}

// Copies text to the clipboard; the label swaps inside a fixed-size slot so the button never changes size.
function CopyButton({ value, kind, onFailed }: { value: string; kind: 'token' | 'command'; onFailed: () => void }) {
  const { t } = useTranslation('settings')
  const [copied, setCopied] = useState(false)
  const timer = useRef<number | undefined>(undefined)

  useEffect(() => () => window.clearTimeout(timer.current), [])

  const copy = async () => {
    // The shared helper tries the Clipboard API and falls back to the old copy command on plain http
    const ok = await copyText(value)
    if (!ok) {
      onFailed()
      return
    }
    setCopied(true)
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => setCopied(false), 2000)
  }

  return (
    <button
      type="button"
      onClick={() => void copy()}
      className="pv-btn shrink-0"
      aria-label={kind === 'token' ? t('bridge.copy.labelToken') : t('bridge.copy.labelCommand')}
    >
      <span className="inline-grid">
        <span className={`col-start-1 row-start-1 inline-flex items-center gap-1.5 ${copied ? 'invisible' : ''}`}>
          <IconCopy size={15} />
          <span>{t('bridge.copy.button')}</span>
        </span>
        <span
          className={`col-start-1 row-start-1 inline-flex items-center gap-1.5 ${copied ? '' : 'invisible'}`}
          style={{ color: 'var(--accent-emerald)' }}
          aria-hidden={!copied}
        >
          <IconCheck size={15} />
          <span>{t('bridge.copy.copied')}</span>
        </span>
      </span>
    </button>
  )
}

// "Echtzeit-Bridge (Unraid)": manages the token of the bridge program that runs on the gaming PC.
function BridgeSection() {
  const { t } = useTranslation('settings')
  const { t: tc } = useTranslation()
  const format = useFormat()
  const queryClient = useQueryClient()

  const { data: status, isLoading, error } = useQuery({
    queryKey: BRIDGE_KEY,
    queryFn: fetchBridgeTokenStatus,
    refetchInterval: 15_000,
  })

  // "Connected" depends on the clock, so the section re-evaluates it regularly even when the data is unchanged
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 15_000)
    return () => window.clearInterval(id)
  }, [])

  const [busyAction, setBusyAction] = useState<'create' | 'delete' | 'package' | null>(null)
  const [confirm, setConfirm] = useState<'replace' | 'delete' | 'package' | null>(null)
  const [dialogError, setDialogError] = useState<string | null>(null)
  const [cardError, setCardError] = useState<string | null>(null)
  const [created, setCreated] = useState<BridgeTokenCreatedDto | null>(null)
  const [copyHint, setCopyHint] = useState<string | null>(null)
  // Which bridge the download is for: the one that fits this browser's computer, switchable (Windows exe or Linux service)
  const [os, setOs] = useState<BridgeOs>(() => defaultBridgeOs())

  const titleId = useId()
  const confirmTitleId = useId()
  const createdTitleId = useId()

  const hasToken = status?.hasToken === true
  const lastSeenMs = status?.lastSeenAt ? new Date(status.lastSeenAt).getTime() : Number.NaN
  const online = hasToken && !Number.isNaN(lastSeenMs) && now - lastSeenMs <= BRIDGE_ONLINE_WINDOW_MS

  let statusText: string
  let statusColor: string
  if (isLoading) {
    statusText = t('bridge.status.loading')
    statusColor = 'var(--border-strong)'
  } else if (error || !status) {
    statusText = t('bridge.status.failed', { reason: error instanceof Error ? error.message : tc('unknownError') })
    statusColor = 'var(--accent-crimson)'
  } else if (!status.hasToken) {
    statusText = t('bridge.status.noToken')
    statusColor = 'var(--border-strong)'
  } else if (!status.lastSeenAt) {
    statusText = t('bridge.status.neverConnected')
    statusColor = 'var(--accent-gold)'
  } else if (online) {
    statusText = t('bridge.status.online', { date: format.date(status.lastSeenAt, 'dateTimeSeconds') })
    statusColor = 'var(--accent-emerald)'
  } else {
    statusText = t('bridge.status.offline', { date: format.date(status.lastSeenAt, 'dateTimeSeconds') })
    statusColor = 'var(--accent-gold)'
  }

  const createToken = async (fromDialog: boolean) => {
    setBusyAction('create')
    setCardError(null)
    setDialogError(null)
    try {
      const result = await createBridgeToken()
      queryClient.setQueryData<BridgeTokenStatusDto>(BRIDGE_KEY, {
        hasToken: true,
        hint: result.hint,
        lastSeenAt: null,
      })
      void queryClient.invalidateQueries({ queryKey: BRIDGE_KEY })
      setConfirm(null)
      setCopyHint(null)
      setCreated(result)
    } catch (err: unknown) {
      const message = t('bridge.errors.create', { reason: err instanceof Error ? err.message : tc('unknownError') })
      if (fromDialog) setDialogError(message)
      else setCardError(message)
    } finally {
      setBusyAction(null)
    }
  }

  const removeToken = async () => {
    setBusyAction('delete')
    setDialogError(null)
    try {
      await deleteBridgeToken()
      queryClient.setQueryData<BridgeTokenStatusDto>(BRIDGE_KEY, { hasToken: false, hint: null, lastSeenAt: null })
      void queryClient.invalidateQueries({ queryKey: BRIDGE_KEY })
      setConfirm(null)
    } catch (err: unknown) {
      setDialogError(t('bridge.errors.delete', { reason: err instanceof Error ? err.message : tc('unknownError') }))
    } finally {
      setBusyAction(null)
    }
  }

  const downloadPackage = async (fromDialog: boolean) => {
    setBusyAction('package')
    setCardError(null)
    setDialogError(null)
    try {
      await downloadBridgePackage(os)
      setConfirm(null)
      void queryClient.invalidateQueries({ queryKey: BRIDGE_KEY })
    } catch (err: unknown) {
      const message = t('bridge.errors.download', { reason: err instanceof Error ? err.message : tc('unknownError') })
      if (fromDialog) setDialogError(message)
      else setCardError(message)
    } finally {
      setBusyAction(null)
    }
  }

  const handlePackageClick = () => {
    if (hasToken) {
      setDialogError(null)
      setConfirm('package')
    } else {
      void downloadPackage(false)
    }
  }

  const handleCreateClick = () => {
    if (hasToken) {
      setDialogError(null)
      setConfirm('replace')
    } else {
      void createToken(false)
    }
  }

  const serverUrl = window.location.origin
  const command = created
    ? os === 'linux'
      ? `echo '${created.token}' | platinvault-bridge setup --server ${serverUrl} --token-stdin`
      : `PlatinVault.Bridge.exe --server ${serverUrl} --token ${created.token} --save`
    : ''
  const bundledVersion = os === 'linux' ? status?.linuxBundledVersion : status?.bundledVersion
  const onLocalhost = ['localhost', '127.0.0.1', '[::1]'].includes(window.location.hostname)

  return (
    <div className="pv-set-section">
      <div className="pv-set-section__title">
        <span id={titleId}>{t('bridge.title')}</span>
        {hasToken && (
          <span className="pv-set-chip">
            <IconCheck size={15} aria-hidden="true" /> {status?.hint ? t('bridge.tokenActiveHint', { hint: status.hint }) : t('bridge.tokenActive')}
          </span>
        )}
      </div>

      <p className="text-sm leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
        {t('bridge.intro')}
        {bundledVersion ? ` ${t('bridge.introVersion', { version: bundledVersion })}` : ''}
      </p>

      {/* Which gaming PC the download is for. The install steps below always take the room of the longer variant, so switching moves nothing */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
        <span className="text-sm font-bold" style={{ color: 'var(--text-secondary)' }}>
          {t('bridge.os.label')}
        </span>
        <div className="pv-segmented" role="group" aria-label={t('bridge.os.group')}>
          {(['windows', 'linux'] as const).map((id) => (
            <button key={id} type="button" aria-pressed={os === id} onClick={() => setOs(id)} disabled={busyAction !== null}>
              {t(`bridge.os.${id}`)}
            </button>
          ))}
        </div>
      </div>

      <div
        className="flex flex-wrap items-center justify-between gap-3 py-2"
        style={{ borderBlock: '1px solid var(--border-subtle)' }}
        role="group"
        aria-labelledby={titleId}
      >
        <div className="flex min-h-10 min-w-0 flex-1 items-center gap-2.5 text-sm" aria-live="polite">
          <span
            className="h-2.5 w-2.5 shrink-0 rounded-sm"
            style={{ background: statusColor }}
            aria-hidden="true"
          />
          <span className="font-bold break-words" style={{ color: 'var(--text-primary)' }}>
            {statusText}
          </span>
          {(error || !status) && !isLoading && <CopyErrorButton className="shrink-0" report={{ message: statusText }} />}
        </div>
        <div className="flex min-w-0 flex-wrap gap-2">
          <Tooltip
            label={t('bridge.download')}
            description={
              bundledVersion
                ? t(os === 'linux' ? 'bridge.linux.downloadHint' : 'bridge.downloadHint', { version: bundledVersion })
                : t(os === 'linux' ? 'bridge.linux.downloadMissing' : 'bridge.downloadMissing')
            }
          >
            <button
              type="button"
              onClick={handlePackageClick}
              disabled={busyAction !== null || isLoading || !bundledVersion}
              className="pv-btn pv-btn-primary relative"
            >
              <BridgeBusyLabel busy={busyAction === 'package' && confirm === null}>
                <IconDownload size={15} />
                <span>{t('bridge.download')}</span>
              </BridgeBusyLabel>
            </button>
          </Tooltip>
          <button
            type="button"
            onClick={handleCreateClick}
            disabled={busyAction !== null || isLoading}
            className="pv-btn pv-btn-primary relative"
          >
            <BridgeBusyLabel busy={busyAction === 'create' && confirm === null}>
              <IconPlus size={15} />
              <span>{t('bridge.createToken')}</span>
            </BridgeBusyLabel>
          </button>
          <button
            type="button"
            onClick={() => {
              setDialogError(null)
              setConfirm('delete')
            }}
            disabled={!hasToken || busyAction !== null}
            className="pv-btn-text pv-btn-text--calm"
          >
            <IconTrash size={15} />
            <span>{t('bridge.deleteToken')}</span>
          </button>
        </div>
      </div>

      {/* How to install the downloaded package. Both variants are laid over each other in one grid cell, so the block is as tall as the longer one */}
      <div className="grid text-sm leading-relaxed" style={{ color: 'var(--text-secondary)' }} data-testid="bridge-steps">
        <div className={`col-start-1 row-start-1 ${os === 'windows' ? '' : 'invisible'}`} aria-hidden={os !== 'windows'}>
          <Trans t={t} i18nKey="bridge.windows.steps" components={{ code: <code /> }} />
        </div>
        <div className={`col-start-1 row-start-1 space-y-1 ${os === 'linux' ? '' : 'invisible'}`} aria-hidden={os !== 'linux'}>
          <div className="font-bold" style={{ color: 'var(--text-primary)' }}>
            {t('bridge.linux.stepsTitle')}
          </div>
          <ol className="list-decimal space-y-0.5 ps-5">
            <li>{t('bridge.linux.step1')}</li>
            <li>
              <Trans t={t} i18nKey="bridge.linux.step2" components={{ code: <code /> }} />
            </li>
            <li>
              <Trans t={t} i18nKey="bridge.linux.step3" components={{ code: <code /> }} />
            </li>
          </ol>
          <p>{t('bridge.linux.note')}</p>
        </div>
      </div>

      {/* Reserved room for an error of the direct "Token erzeugen" click, so it moves nothing */}
      <div className="min-h-12">{cardError && <ErrorBlock title={t('bridge.errorTitle')} message={cardError} defaultOpen />}</div>

      {confirm && (
        <BridgeDialog
          labelledBy={confirmTitleId}
          busy={busyAction !== null}
          onClose={() => setConfirm(null)}
        >
          <div className="space-y-4 p-6">
            <div className="flex items-start gap-3.5">
              <div
                className="flex h-10 w-10 shrink-0 items-center justify-center"
                style={{ color: 'var(--accent-crimson)' }}
              >
                <IconAlertTriangle size={20} />
              </div>
              <div className="min-w-0 space-y-1.5">
                <h2 id={confirmTitleId} className="pv-dialog-title">
                  {confirm === 'replace'
                    ? t('bridge.confirm.replaceTitle')
                    : confirm === 'package'
                      ? t('bridge.confirm.packageTitle')
                      : t('bridge.confirm.deleteTitle')}
                </h2>
                <p className="text-sm leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
                  {confirm === 'replace' || confirm === 'package' ? t('bridge.confirm.replaceText') : t('bridge.confirm.deleteText')}
                </p>
              </div>
            </div>

            <div className="min-h-12">{dialogError && <ErrorBlock title={t('bridge.errorTitle')} message={dialogError} defaultOpen />}</div>

            <div className="flex justify-end gap-2.5 pt-3" style={{ borderTop: '1px solid var(--border-subtle)' }}>
              <button
                type="button"
                className="pv-btn"
                onClick={() => setConfirm(null)}
                disabled={busyAction !== null}
                data-autofocus
              >
                {tc('cancel')}
              </button>
              <button
                type="button"
                className={`pv-btn relative ${confirm === 'delete' ? 'pv-btn-danger' : 'pv-btn-primary'}`}
                disabled={busyAction !== null}
                onClick={() =>
                  confirm === 'replace'
                    ? void createToken(true)
                    : confirm === 'package'
                      ? void downloadPackage(true)
                      : void removeToken()
                }
              >
                <BridgeBusyLabel busy={busyAction !== null}>
                  {confirm === 'replace' ? <IconPlus size={15} /> : confirm === 'package' ? <IconDownload size={15} /> : <IconTrash size={15} />}
                  <span>
                    {confirm === 'replace'
                      ? t('bridge.confirm.replaceConfirm')
                      : confirm === 'package'
                        ? t('bridge.confirm.packageConfirm')
                        : t('bridge.confirm.deleteConfirm')}
                  </span>
                </BridgeBusyLabel>
              </button>
            </div>
          </div>
        </BridgeDialog>
      )}

      {created && (
        <BridgeDialog labelledBy={createdTitleId} onClose={() => setCreated(null)}>
          <div className="space-y-4 p-6">
            <div className="flex items-start gap-3.5">
              <div
                className="flex h-10 w-10 shrink-0 items-center justify-center"
                style={{ color: 'var(--link)' }}
              >
                <IconPlugConnected size={20} />
              </div>
              <div className="min-w-0 space-y-1.5">
                <h2 id={createdTitleId} className="pv-dialog-title">
                  {t('bridge.created.title')}
                </h2>
                <p className="text-sm leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
                  <Trans t={t} i18nKey={os === 'linux' ? 'bridge.linux.createdText' : 'bridge.created.text'} components={{ code: <code /> }} />
                </p>
              </div>
            </div>

            <div className="pv-note pv-note--warn flex items-start gap-2">
              <IconAlertTriangle size={17} className="mt-px shrink-0" style={{ color: 'var(--accent-gold)' }} />
              <span className="font-semibold">
                {t('bridge.created.warning')}
              </span>
            </div>

            <div className="space-y-1">
              <label htmlFor="bridge-token-value" className="block text-sm font-bold" style={{ color: 'var(--text-secondary)' }}>
                {t('bridge.created.tokenLabel')}
              </label>
              <div className="flex gap-2">
                <input
                  id="bridge-token-value"
                  type="text"
                  readOnly
                  value={created.token}
                  onFocus={(e) => e.currentTarget.select()}
                  className="pv-input w-full font-mono"
                />
                <CopyButton
                  value={created.token}
                  kind="token"
                  onFailed={() => setCopyHint(t('bridge.created.copyBlocked'))}
                />
              </div>
            </div>

            <div className="space-y-1">
              <label htmlFor="bridge-command-value" className="block text-sm font-bold" style={{ color: 'var(--text-secondary)' }}>
                {t('bridge.created.commandLabel')}
              </label>
              <div className="flex items-start gap-2">
                <textarea
                  id="bridge-command-value"
                  readOnly
                  rows={4}
                  value={command}
                  onFocus={(e) => e.currentTarget.select()}
                  className="pv-input w-full resize-none font-mono"
                />
                <CopyButton
                  value={command}
                  kind="command"
                  onFailed={() => setCopyHint(t('bridge.created.copyBlocked'))}
                />
              </div>
            </div>

            {/* Reserved room: a localhost hint and/or a copy failure message never move the buttons */}
            <div className="min-h-[3.75rem] space-y-1 text-sm leading-snug">
              {onLocalhost && (
                <p style={{ color: 'var(--text-secondary)' }}>
                  <Trans
                    t={t}
                    i18nKey="bridge.created.localhost"
                    values={{ host: window.location.hostname, url: serverUrl }}
                    components={{ code: <code /> }}
                  />
                </p>
              )}
              {copyHint && (
                <p className="font-bold" style={{ color: 'var(--accent-crimson)' }} role="alert">
                  {copyHint}
                </p>
              )}
            </div>

            <div className="flex justify-end pt-3" style={{ borderTop: '1px solid var(--border-subtle)' }}>
              <button type="button" className="pv-btn pv-btn-primary" onClick={() => setCreated(null)}>
                <IconCheck size={16} />
                <span>{t('bridge.created.done')}</span>
              </button>
            </div>
          </div>
        </BridgeDialog>
      )}
    </div>
  )
}
