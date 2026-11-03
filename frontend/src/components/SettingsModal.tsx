import { useEffect, useId, useRef, useState } from 'react'
import type { KeyboardEvent, ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Trans, useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import {
  IconAlertTriangle,
  IconArrowsMaximize,
  IconBooks,
  IconBrandTwitch,
  IconCheck,
  IconCopy,
  IconDeviceGamepad2,
  IconDownload,
  IconKey,
  IconLoader2,
  IconLogin2,
  IconMusic,
  IconNote,
  IconHeartbeat,
  IconPlugConnected,
  IconPlus,
  IconRefresh,
  IconSparkles,
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
import { copyText } from '../utils/clipboard'
import { DEFAULT_POPUP_TARGET, normalizePopupTarget, POPUP_TARGETS } from '../utils/popupTarget'
import { steamClientUrl, useOpenInSteamClient } from '../utils/steamClient'
import { useSteamReach } from '../utils/useSteamReach'
import { AI_STATUS_KEY } from './ai/useAiStatus'
import { AiSelectionSection } from './settings/AiSelectionSection'
import { emptyResearchBlocks, ResearchBlocksSection, sameResearchBlocks } from './settings/ResearchBlocksSection'
import { SystemStatusSection } from './SystemStatusSection'
import { UpdatesSection } from './settings/UpdatesSection'
import { useIsDesktopHost } from './FooterParts'
import { ReservedText } from './ui/ReservedText'
import { Switch } from './ui/Switch'
import { TierIcon } from './ui/TierIcon'
import { Tooltip } from './ui/Tooltip'

export type SettingsTab = 'live' | 'steam' | 'ki' | 'twitch' | 'bridge' | 'system'

// Names and hints of the tabs are texts: settings:tabs.<id>.label / .hint
const SETTINGS_TABS: { id: SettingsTab; icon: typeof IconKey }[] = [
  { id: 'live', icon: IconVolume },
  { id: 'steam', icon: IconBooks },
  { id: 'ki', icon: IconSparkles },
  { id: 'twitch', icon: IconBrandTwitch },
  { id: 'bridge', icon: IconPlugConnected },
  { id: 'system', icon: IconHeartbeat },
]
const LAST_TAB_KEY = 'pv_settings_tab'
// Row of a simple on/off setting: the shared Switch (track + label), the whole row is clickable
const SWITCH_ROW = 'flex w-full items-center gap-2.5 text-xs font-semibold text-[color:var(--text-secondary)]'

interface SettingsModalProps {
  open: boolean
  // Tab to show when the modal opens (e.g. from the tour's setup checklist); otherwise the last used tab
  initialTab?: SettingsTab
  // Message to show when the modal opens, e.g. the result of "Sign in with Steam"
  notice?: { type: 'ok' | 'err'; text: string } | null
  onClose: () => void
  onSaved?: (settings: SettingsResponseDto) => void
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
export function SettingsModal({ open, notice = null, initialTab, onClose, onSaved }: SettingsModalProps) {
  const { t } = useTranslation('settings')
  const { t: tc } = useTranslation()
  const { language } = useUiLanguage()
  const format = useFormat()
  const queryClient = useQueryClient()
  const [activeTab, setActiveTab] = useState<SettingsTab>('live')
  const openInSteamClient = useOpenInSteamClient()
  // Whether a game start can be noticed at all (bridge connected or Steam on the server itself)
  const reach = useSteamReach()
  // The Windows program reads Steam on this PC itself: its settings have no bridge tab
  const desktopHost = useIsDesktopHost()
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
    const wanted = initialTab ?? (notice ? 'steam' : (remembered ?? 'live'))
    setActiveTab(desktopHost && wanted === 'bridge' ? 'live' : wanted)
  }, [open, initialTab, notice, desktopHost])
  const selectTab = (tab: SettingsTab) => {
    setActiveTab(tab)
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
  const [autoTranslate, setAutoTranslate] = useState(true)
  // Personal notes on achievements (off hides them everywhere; stored notes are kept)
  const [achievementNotes, setAchievementNotes] = useState(true)
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
    setStatusMsg(notice)
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
        setAutoTranslate(data.autoTranslate !== false)
        setAchievementNotes(data.achievementNotesEnabled !== false)
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
    }, { position: pos })
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
      })
    } finally {
      setSaving(false)
    }
  }

  return (
    <div
      className="pv-modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-labelledby="settings-modal-title"
    >
      <div className="pv-modal flex h-[min(90dvh,860px)] w-full max-w-5xl flex-col overflow-hidden">
        {/* Header */}
        <div
          className="flex items-center justify-between px-6 py-4"
          style={{
            background: 'var(--bg-elevated)',
            borderBottom: '1px solid var(--border-subtle)',
          }}
        >
          <div className="flex items-center gap-3">
            <div
              className="flex h-10 w-10 items-center justify-center rounded-xl"
              style={{
                background: 'var(--accent-crystal-bg)',
                color: 'var(--accent-crystal)',
                border: '1px solid var(--accent-crystal-border)',
              }}
            >
              <IconKey size={20} />
            </div>
            <div>
              <h2
                id="settings-modal-title"
                className="text-base sm:text-lg font-extrabold"
                style={{ color: 'var(--text-primary)' }}
              >
                {t('title')}
              </h2>
              <p className="text-xs" style={{ color: 'var(--text-muted)' }}>
                {t('subtitle')}
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="pv-btn !p-1.5"
            aria-label={tc('close')}
          >
            <IconX size={20} />
          </button>
        </div>

        <form onSubmit={handleSave} className="flex min-h-0 flex-1 flex-col">
          <div className="flex min-h-0 flex-1 flex-col md:flex-row">
            <nav
              className="flex shrink-0 gap-1 overflow-x-auto p-2 md:w-60 md:flex-col md:overflow-x-visible md:p-3"
              style={{ borderRight: '1px solid var(--border-subtle)', background: 'var(--bg-elevated)' }}
              role="tablist"
              aria-label={t('tabsLabel')}
            >
              {visibleTabs.map((tab) => {
                const Icon = tab.icon
                const selected = activeTab === tab.id
                return (
                  <button
                    key={tab.id}
                    type="button"
                    role="tab"
                    aria-selected={selected}
                    onClick={() => selectTab(tab.id)}
                    className="flex shrink-0 items-center gap-2.5 rounded-lg px-3 py-2.5 text-left cursor-pointer md:w-full"
                    style={{
                      background: selected ? 'var(--accent-crystal-bg)' : 'transparent',
                      color: selected ? 'var(--accent-crystal)' : 'var(--text-secondary)',
                      border: `1px solid ${selected ? 'var(--accent-crystal-border)' : 'transparent'}`,
                    }}
                  >
                    <Icon size={18} className="shrink-0" />
                    <span className="min-w-0">
                      <span className="block whitespace-nowrap text-sm font-bold">{t(`tabs.${tab.id}.label`)}</span>
                      <span className="hidden truncate text-[11px] md:block" style={{ color: 'var(--text-muted)' }}>
                        {t(`tabs.${tab.id}.hint`)}
                      </span>
                    </span>
                  </button>
                )
              })}
            </nav>

          <div className="min-h-0 flex-1 overflow-y-auto p-6 space-y-5" role="tabpanel">
          {loading ? (
            <div
              className="py-12 text-center text-sm"
              style={{ color: 'var(--text-muted)' }}
            >
              {t('loading')}
            </div>
          ) : (
            <>
              {/* Tab "Live & Sound": trophy popups, sound & position (applies to every game with achievements enabled) */}
              {activeTab === 'live' && (
                <div className="space-y-4">
                  <div className="pv-card space-y-4">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div
                        className="flex items-center gap-2 text-sm font-extrabold"
                        style={{ color: 'var(--accent-gold)' }}
                      >
                        <IconVolume size={18} />
                        <span>{t('live.popupsTitle')}</span>
                      </div>
                      <div className="flex flex-wrap items-center gap-2">
                        <button
                          type="button"
                          onClick={() => {
                            soundEngine.setTheme(soundTheme)
                            soundEngine.setVolume(soundVolume)
                            soundEngine.setEnabled(true)
                            soundEngine.playTrophyUnlock('Gold')
                          }}
                          className="pv-btn pv-btn-gold !py-1 !px-3 !text-xs"
                        >
                          {t('live.previewSound')}
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
                            className="pv-btn !py-1 !px-3 !text-xs"
                          >
                            <TierIcon tier="Platinum" size={13} />
                            <span>{t('live.platinumTest')}</span>
                          </button>
                        </Tooltip>
                      </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div className="space-y-3">
                        <label
                          className="block text-xs font-bold"
                          style={{ color: 'var(--text-secondary)' }}
                        >
                          {t('live.soundTheme')}
                        </label>
                        {/* Own sounds from real instrument recordings (CC0); a click selects and previews the theme */}
                        <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                          {SOUND_THEMES.map((theme) => (
                            <Tooltip key={theme.id} label={t(`live.soundThemes.${theme.id}.label`)} description={t(`live.soundThemes.${theme.id}.description`)} className="flex">
                              <button
                                type="button"
                                aria-pressed={soundTheme === theme.id}
                                onClick={() => {
                                  setSoundTheme(theme.id)
                                  soundEngine.setVolume(soundVolume)
                                  soundEngine.preview(theme.id, 'Gold')
                                }}
                                className={`pv-btn w-full justify-center !px-2 ${soundTheme === theme.id ? 'pv-btn-primary' : ''}`}
                              >
                                <IconMusic size={15} />
                                <span>{t(`live.soundThemes.${theme.id}.label`)}</span>
                              </button>
                            </Tooltip>
                          ))}
                        </div>
                        <p className="min-h-[2.5em] text-[11px] leading-snug" style={{ color: 'var(--text-muted)' }}>
                          {t(`live.soundThemes.${soundTheme}.description`)}
                        </p>

                        <div>
                          <div
                            className="flex justify-between text-xs font-semibold mb-1"
                            style={{ color: 'var(--text-secondary)' }}
                          >
                            <span>{t('live.volume', { percent: format.percent(Math.round(soundVolume * 100)) })}</span>
                            <span style={{ color: 'var(--text-muted)' }}>{t('live.volumeRecommended')}</span>
                          </div>
                          <input
                            type="range"
                            min="0"
                            max="0.6"
                            step="0.01"
                            value={soundVolume}
                            onChange={(e) =>
                              setSoundVolume(parseFloat(e.target.value))
                            }
                            className="pv-range w-full"
                            style={{ accentColor: 'var(--accent-gold)' }}
                          />
                        </div>

                        <Switch checked={playSounds} onChange={setPlaySounds} className={SWITCH_ROW}>
                          {t('live.playSounds')}
                        </Switch>

                        {/* Only German needs this: in English Steam delivers its own English texts, there is nothing to translate */}
                        {language === 'de' && (
                          <Switch checked={autoTranslate} onChange={setAutoTranslate} className={SWITCH_ROW}>
                            {t('live.autoTranslate')}
                          </Switch>
                        )}
                      </div>

                      {/* 3x3 Position Grid */}
                      <div>
                        <label
                          className="block text-xs font-bold mb-1.5"
                          style={{ color: 'var(--text-secondary)' }}
                        >
                          {t('live.position')}
                        </label>
                        <div
                          className="grid grid-cols-3 gap-1.5 rounded-xl p-2"
                          style={{
                            background: 'var(--bg-elevated)',
                            border: '1px solid var(--border-subtle)',
                          }}
                        >
                          {POSITIONS.map((pos) => {
                            const isSelected = popupPosition === pos
                            return (
                              <button
                                key={pos}
                                type="button"
                                onClick={() => handleTestPosition(pos)}
                                className={`pv-btn justify-center !py-2 !px-1.5 !text-[11px] ${
                                  isSelected ? 'pv-btn-primary' : ''
                                }`}
                              >
                                {t(`live.positions.${pos}`)}
                              </button>
                            )
                          })}
                          {/* The tenth way: the whole window, for a few seconds */}
                          <button
                            type="button"
                            onClick={() => handleTestPosition('fullscreen')}
                            aria-pressed={popupPosition === 'fullscreen'}
                            className={`pv-btn col-span-3 justify-center !py-2 !px-1.5 !text-[11px] ${
                              popupPosition === 'fullscreen' ? 'pv-btn-primary' : ''
                            }`}
                            data-position="fullscreen"
                          >
                            <IconArrowsMaximize size={14} aria-hidden="true" />
                            <span>{t('live.positions.fullscreen')}</span>
                          </button>
                        </div>
                        <p className="mt-1.5 text-xs leading-relaxed" style={{ color: 'var(--text-muted)' }}>
                          {t('live.positionFullscreenHint')}
                        </p>

                        {/* "Trophäen-Popup anzeigen" (live.popup_target): same button look as the positions above. Without the Windows
                            program there is no overlay: those two choices stay visible but disabled, the effect is "in the app". */}
                        <div className="mt-3" role="group" aria-labelledby="pv-popup-target-label">
                          <span
                            id="pv-popup-target-label"
                            className="block text-xs font-bold mb-1.5"
                            style={{ color: 'var(--text-secondary)' }}
                          >
                            {t('live.target.label')}
                          </span>
                          <div
                            className="grid grid-cols-3 gap-1.5 rounded-xl p-2"
                            style={{ background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)' }}
                          >
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
                                  className={`pv-btn justify-center !py-2 !px-1.5 !text-[11px] ${selected ? 'pv-btn-primary' : ''}`}
                                  style={unavailable ? { opacity: 0.55, cursor: 'not-allowed' } : undefined}
                                >
                                  {t(`live.target.${target}`)}
                                </button>
                              )
                            })}
                          </div>
                          <p
                            id="pv-popup-target-hint"
                            className="mt-1.5 text-xs leading-relaxed"
                            style={{ color: 'var(--text-muted)' }}
                          >
                            {desktopHost ? t('live.target.hintDesktop') : t('live.target.onlyDesktop')}
                          </p>
                        </div>
                      </div>
                    </div>
                  </div>

                  {/* Game start: every open window switches to the game page (switch "features.auto_switch_game") */}
                  <div className="pv-card space-y-2">
                    <div className="flex items-center gap-2 text-sm font-extrabold" style={{ color: 'var(--accent-crystal)' }}>
                      <IconDeviceGamepad2 size={18} />
                      <span>{t('live.gameStartTitle')}</span>
                    </div>
                    <Switch checked={autoSwitchGame} onChange={setAutoSwitchGame} className={SWITCH_ROW}>
                      {t('live.gameStartSwitch')}
                    </Switch>
                    <p className="text-xs leading-relaxed" style={{ color: 'var(--text-muted)' }}>
                      {t('live.gameStartText')}
                    </p>
                    {/* Where the start is noticed from, or why nothing can be noticed now; the room is reserved for the longest of the three */}
                    <ReservedText
                      role="status"
                      className="text-xs font-semibold leading-relaxed"
                      style={{ color: 'var(--text-secondary)' }}
                      candidates={[t('live.gameStartSourceLocal'), t('live.gameStartSourceBridge'), t('live.gameStartNoSource')]}
                    >
                      {reach.local ? t('live.gameStartSourceLocal') : reach.bridgeConnected ? t('live.gameStartSourceBridge') : t('live.gameStartNoSource')}
                    </ReservedText>
                  </div>

                  {/* Trophy list: personal notes on achievements (switch "features.achievement_notes"); off hides them everywhere */}
                  <div className="pv-card space-y-2">
                    <div className="flex items-center gap-2 text-sm font-extrabold" style={{ color: 'var(--accent-crystal)' }}>
                      <IconNote size={18} />
                      <span>{t('live.notesTitle')}</span>
                    </div>
                    <Switch checked={achievementNotes} onChange={setAchievementNotes} className={SWITCH_ROW}>
                      {t('live.notesSwitch')}
                    </Switch>
                    <p className="text-xs leading-relaxed" style={{ color: 'var(--text-muted)' }}>
                      {t('live.notesText')}
                    </p>
                  </div>
                </div>
              )}

              {/* Tab "Steam": account (OpenID linking) + Steam Web API key */}
              {activeTab === 'steam' && (
              <div className="pv-card space-y-3">
                <div className="flex items-center justify-between">
                  <div
                    className="flex items-center gap-2 text-sm font-extrabold"
                    style={{ color: 'var(--accent-crystal)' }}
                  >
                    <IconBooks size={18} />
                    <span>{t('steam.title')}</span>
                  </div>
                  {settings?.hasSteamKey && !clearSteam && (
                    <span className="pv-badge pv-badge-ok">
                      <IconCheck size={13} /> {t('steam.active', { hint: settings.steamKeyHint })}
                    </span>
                  )}
                </div>

                <div
                  className="flex flex-wrap items-center justify-between gap-3 rounded-xl p-3"
                  style={{ background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)' }}
                >
                  <div className="text-xs" style={{ color: 'var(--text-secondary)' }}>
                    <div className="font-bold" style={{ color: 'var(--text-primary)' }}>
                      {settings?.steamUserId ? t('steam.linked', { id: settings.steamUserId }) : t('steam.notLinked')}
                    </div>
                    <div>{t('steam.loginNote')}</div>
                  </div>
                  <a href={steamLoginUrl()} className="pv-btn pv-btn-primary shrink-0">
                    <IconLogin2 size={15} />
                    <span>{settings?.steamUserId ? t('steam.otherAccount') : t('steam.login')}</span>
                  </a>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  <div>
                    <div className="flex items-center justify-between mb-1">
                      <label
                        className="text-xs font-bold"
                        style={{ color: 'var(--text-secondary)' }}
                      >
                        {t('steam.keyLabel')}
                      </label>
                      <a
                        href="https://steamcommunity.com/dev/apikey"
                        target="_blank"
                        rel="noreferrer"
                        className="text-[11px] font-bold hover:underline"
                        style={{ color: 'var(--accent-crystal)' }}
                      >
                        {t('steam.getKey')}
                      </a>
                    </div>
                    <div className="flex gap-2">
                      <input
                        type="password"
                        value={steamKey}
                        onChange={(e) => {
                          setSteamKey(e.target.value)
                          if (e.target.value) setClearSteam(false)
                        }}
                        placeholder={
                          settings?.hasSteamKey && !clearSteam
                            ? t('steam.keySaved', { hint: settings.steamKeyHint })
                            : t('steam.keyEmpty')
                        }
                        className="pv-input w-full"
                      />
                      {settings?.hasSteamKey && (
                        <Tooltip label={t('steam.deleteKey')}>
                          <button
                            type="button"
                            onClick={() => setClearSteam(!clearSteam)}
                            className="pv-btn !px-2.5"
                            aria-label={t('steam.deleteKey')}
                          >
                            <IconTrash size={15} />
                          </button>
                        </Tooltip>
                      )}
                    </div>
                  </div>

                  <div>
                    <label
                      className="block text-xs font-bold mb-1"
                      style={{ color: 'var(--text-secondary)' }}
                    >
                      {t('steam.manualIdLabel')}
                    </label>
                    <input
                      type="text"
                      value={steamUserId}
                      onChange={(e) => setSteamUserId(e.target.value)}
                      placeholder={t('steam.manualIdPlaceholder')}
                      className="pv-input w-full"
                    />
                  </div>
                </div>

                {/* Where the key comes from (Steam's own docs: partner.steamgames.com/doc/webapi_overview/auth) */}
                <div className="rounded-lg p-3.5 text-xs leading-relaxed" style={{ background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)' }}>
                  <div className="mb-1.5 font-extrabold" style={{ color: 'var(--text-primary)' }}>
                    {t('steam.guide.title')}
                  </div>
                  <ol className="list-decimal space-y-1 pl-5" style={{ color: 'var(--text-secondary)' }}>
                    <li>
                      <Trans
                        t={t}
                        i18nKey="steam.guide.step1"
                        components={{ link: <a href="https://steamcommunity.com/dev/apikey" target="_blank" rel="noreferrer" className="font-bold hover:underline" style={{ color: 'var(--accent-crystal)' }} /> }}
                      />
                      {/* Only on the gaming PC itself: the Steam client is usually signed in already */}
                      {openInSteamClient && (
                        <>
                          {' '}
                          <Trans
                            t={t}
                            i18nKey="steam.guide.step1Client"
                            components={{ clientLink: <a href={steamClientUrl('https://steamcommunity.com/dev/apikey')} className="font-bold hover:underline" style={{ color: 'var(--accent-crystal)' }} /> }}
                          />
                        </>
                      )}
                    </li>
                    <li>{t('steam.guide.step2')}</li>
                    <li>{t('steam.guide.step3')}</li>
                    <li>{t('steam.guide.step4')}</li>
                  </ol>
                  <p className="mt-1.5" style={{ color: 'var(--text-muted)' }}>
                    {t('steam.guide.privacy')}
                  </p>
                </div>
              </div>
              )}

              {/* Tab "Twitch & IGDB": Twitch button (link only, no key) + optional own Twitch developer app for IGDB */}
              {activeTab === 'twitch' && (
                <div className="space-y-4">
                  <div className="pv-card space-y-2">
                    <div className="flex items-center gap-2 text-sm font-extrabold" style={{ color: 'var(--accent-crystal)' }}>
                      <IconBrandTwitch size={18} />
                      <span>{t('twitch.buttonTitle')}</span>
                    </div>
                    <Switch checked={twitchButton} onChange={setTwitchButton} className={SWITCH_ROW}>
                      {t('twitch.buttonSwitch')}
                    </Switch>
                    <p className="text-xs leading-relaxed" style={{ color: 'var(--text-muted)' }}>
                      {t('twitch.buttonText')}
                    </p>
                  </div>

                  <div className="pv-card space-y-3">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div className="flex items-center gap-2 text-sm font-extrabold" style={{ color: 'var(--accent-gold)' }}>
                        <IconSparkles size={18} />
                        <span>{t('twitch.igdbTitle')}</span>
                      </div>
                      {settings?.hasTwitchKey && !clearTwitch && (
                        <span className="pv-badge pv-badge-ok">
                          <IconCheck size={13} /> {t('steam.active', { hint: settings.twitchKeyHint })}
                        </span>
                      )}
                    </div>
                    <p className="text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
                      {t('twitch.igdbIntro')}
                    </p>

                    <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                      <TwitchCredentialField
                        label={t('twitch.clientId')}
                        value={twitchClientId}
                        onChange={(v) => {
                          setTwitchClientId(v)
                          if (v) setClearTwitch(false)
                        }}
                        placeholder={
                          settings?.hasTwitchKey && !clearTwitch
                            ? t('twitch.idSaved', { hint: settings.twitchKeyHint })
                            : t('twitch.idEmpty')
                        }
                      />
                      <TwitchCredentialField
                        label={t('twitch.clientSecret')}
                        secret
                        value={twitchSecret}
                        onChange={(v) => {
                          setTwitchSecret(v)
                          if (v) setClearTwitch(false)
                        }}
                        placeholder={
                          settings?.hasTwitchKey && !clearTwitch ? t('twitch.secretSaved') : t('twitch.secretEmpty')
                        }
                      />
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                      <button
                        type="button"
                        className="pv-btn !py-1 !px-3 !text-xs"
                        disabled={
                          twitchTest.checking ||
                          (!settings?.hasTwitchKey && !(twitchClientId.trim() && twitchSecret.trim())) ||
                          (clearTwitch && !(twitchClientId.trim() && twitchSecret.trim()))
                        }
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
                        {twitchTest.checking ? <IconLoader2 size={14} className="animate-spin" /> : <IconPlugConnected size={14} />}
                        <span>{t('twitch.test')}</span>
                      </button>
                      {settings?.hasTwitchKey && (
                        <button
                          type="button"
                          onClick={() => setClearTwitch(!clearTwitch)}
                          className="pv-btn !py-1 !px-3 !text-xs"
                          aria-pressed={clearTwitch}
                        >
                          <IconTrash size={14} />
                          <span>{clearTwitch ? t('twitch.willDelete') : t('twitch.deleteSaved')}</span>
                        </button>
                      )}
                    </div>
                    {/* Reserved line, so the result does not push the guide below */}
                    <p
                      className="min-h-[2.5rem] text-xs font-semibold leading-relaxed"
                      role="status"
                      style={{
                        color: twitchTest.error || (twitchTest.result && !twitchTest.result.connected)
                          ? 'var(--accent-crimson)'
                          : 'var(--accent-emerald)',
                      }}
                    >
                      {twitchTest.error ?? twitchTest.result?.message ?? ''}
                    </p>

                    {/* Steps as described in IGDB's own docs (api-docs.igdb.com, "Account Creation") */}
                    <div className="rounded-lg p-3.5 text-xs leading-relaxed" style={{ background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)' }}>
                      <div className="mb-1.5 font-extrabold" style={{ color: 'var(--text-primary)' }}>
                        {t('twitch.guide.title')}
                      </div>
                      <ol className="list-decimal space-y-1 pl-5" style={{ color: 'var(--text-secondary)' }}>
                        <li>{t('twitch.guide.step1')}</li>
                        <li>
                          <Trans
                            t={t}
                            i18nKey="twitch.guide.step2"
                            components={{ securityLink: <a href="https://www.twitch.tv/settings/security" target="_blank" rel="noreferrer" className="font-bold hover:underline" style={{ color: 'var(--accent-crystal)' }} /> }}
                          />
                        </li>
                        <li>
                          <Trans
                            t={t}
                            i18nKey="twitch.guide.step3"
                            components={{
                              consoleLink: <a href="https://dev.twitch.tv/console/apps/create" target="_blank" rel="noreferrer" className="font-bold hover:underline" style={{ color: 'var(--accent-crystal)' }} />,
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
                      <p className="mt-1.5" style={{ color: 'var(--text-muted)' }}>
                        {t('twitch.guide.privacy')}
                      </p>
                    </div>
                  </div>
                </div>
              )}

              {/* Tab "PlatinVault Bridge": token + download for the bridge program on the gaming PC */}
              {activeTab === 'bridge' && !desktopHost && <BridgeSection />}

              {/* Tab "System": health check of backend, database, data folder and sources */}
              {/* Updates first (switch "Beta-Versionen erhalten", check, install or image tag), then the health check */}
              {activeTab === 'system' && (
                <div className="space-y-4">
                  <UpdatesSection />
                  <SystemStatusSection />
                </div>
              )}

              {/* Tab "KI-Recherche": AI providers with a model dropdown once a key connects */}
              {activeTab === 'ki' && (
              <div className="space-y-4">
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
              <div className="pv-card space-y-4">
                <div
                  className="flex items-center gap-2 text-sm font-extrabold"
                  style={{ color: 'var(--accent-emerald)' }}
                >
                  <IconSparkles size={18} />
                  <span>{t('ai.title')}</span>
                </div>

                {/* 1. Google Gemini */}
                <ProviderKeyAndModelRow
                  label={t('ai.geminiLabel')}
                  hint={
                    <Trans
                      t={t}
                      i18nKey="ai.geminiHint"
                      components={{ keyLink: <a href="https://aistudio.google.com/apikey" target="_blank" rel="noreferrer" className="font-bold hover:underline" style={{ color: 'var(--accent-crystal)' }} /> }}
                    />
                  }
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
                  onRecheck={() =>
                    void checkProvider(
                      'gemini',
                      geminiKey,
                      !!settings?.hasGeminiKey,
                      clearGemini
                    )
                  }
                  selectedModel={geminiModel}
                  onSelectModel={setGeminiModel}
                />
                {settings?.hasGeminiKey && !clearGemini && <GeminiDiagnoseBlock model={geminiModel} />}

                {/* 2. Anthropic Claude */}
                <ProviderKeyAndModelRow
                  label={t('ai.claudeLabel')}
                  hint={
                    <Trans
                      t={t}
                      i18nKey="ai.claudeHint"
                      components={{ keyLink: <a href="https://console.anthropic.com/settings/keys" target="_blank" rel="noreferrer" className="font-bold hover:underline" style={{ color: 'var(--accent-crystal)' }} /> }}
                    />
                  }
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
                  onRecheck={() =>
                    void checkProvider(
                      'claude',
                      claudeKey,
                      !!settings?.hasClaudeKey,
                      clearClaude
                    )
                  }
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
                  onRecheck={() =>
                    void checkProvider(
                      'openai',
                      openAiKey,
                      !!settings?.hasOpenAiKey,
                      clearOpenAi
                    )
                  }
                  selectedModel={openAiModel}
                  onSelectModel={setOpenAiModel}
                />

              </div>

              {/* Predefined research building blocks (no free text): scope, focus, spoilers, presentation */}
              <ResearchBlocksSection value={blocks} onChange={setBlocks} policy={settings?.researchBlocksPolicy} />
              </div>
              )}
            </>
          )}
          </div>
          </div>

          {/* Footer: status message + actions, always visible */}
          <div
            className="flex flex-wrap items-center justify-between gap-3 px-6 py-4"
            style={{ background: 'var(--bg-elevated)', borderTop: '1px solid var(--border-subtle)' }}
          >
          <ReservedText
            className="w-full min-w-0 text-xs font-bold sm:w-auto sm:flex-1"
            candidates={[
              t('footer.saved'),
              t('footer.saveFailed'),
              t('twitch.saveProblem'),
              t('twitch.saveNeedBoth'),
            ]}
            role="status"
            style={{ color: statusMsg?.type === 'err' ? 'var(--accent-crimson)' : 'var(--accent-emerald)' }}
          >
            {statusMsg?.text}
          </ReservedText>

          <div className="flex items-center justify-end gap-3">
            <button type="button" onClick={onClose} className="pv-btn">
              {tc('close')}
            </button>
            {/* Busy state: the label stays (invisible) and a spinner covers it, so the button keeps its size in both languages */}
            <button
              type="submit"
              disabled={saving || loading}
              aria-busy={saving || undefined}
              className="pv-btn pv-btn-emerald relative"
            >
              <BridgeBusyLabel busy={saving}>
                <IconCheck size={17} />
                <span>{t('footer.save')}</span>
              </BridgeBusyLabel>
            </button>
          </div>
          </div>
        </form>
      </div>
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
        <IconCheck size={15} className="mt-0.5 shrink-0" style={{ color: 'var(--accent-emerald)' }} />
      ) : (
        <IconAlertTriangle size={15} className="mt-0.5 shrink-0" style={{ color: 'var(--accent-crimson)' }} />
      )}
      <span>
        <strong>{label}:</strong> {r.message}
      </span>
    </div>
  )
  return (
    <div className="rounded-lg p-3 text-xs leading-relaxed" style={{ background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)', color: 'var(--text-secondary)' }}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span>{t('ai.diagnose.intro', { model })}</span>
        <button type="button" className="pv-btn !py-1 !px-3 !text-xs" onClick={() => void run()} disabled={state.busy} aria-busy={state.busy || undefined}>
          <span className="relative inline-flex items-center gap-1.5">
            <span className={`inline-flex items-center gap-1.5 ${state.busy ? 'invisible' : ''}`}>
              <IconPlugConnected size={14} />
              {t('ai.diagnose.run')}
            </span>
            {state.busy && (
              <span className="absolute inset-0 flex items-center justify-center" aria-hidden="true">
                <IconLoader2 size={14} className="animate-spin" />
              </span>
            )}
          </span>
        </button>
      </div>
      {(state.result || state.error) && (
        <div className="mt-2 space-y-1" role="status">
          {state.error && <div style={{ color: 'var(--accent-crimson)' }}>{state.error}</div>}
          {state.result && line(t('ai.diagnose.plain'), state.result.plain)}
          {state.result && line(t('ai.diagnose.search'), state.result.search)}
        </div>
      )}
    </div>
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
    <div>
      <label htmlFor={id} className="mb-1 block text-xs font-bold" style={{ color: 'var(--text-secondary)' }}>
        {label}
      </label>
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
        className="pv-input w-full"
        style={problem ? { borderColor: 'var(--accent-crimson)' } : undefined}
      />
      {/* Reserved room for the longest message (every one of them, in this language and at this width), so nothing below moves while typing */}
      <ReservedText
        id={`${id}-msg`}
        className="mt-1 text-[11px] font-semibold leading-snug"
        style={{ color: 'var(--accent-crimson)' }}
        candidates={[t('twitch.problemChars'), t('twitch.problemTooShort', { length: 128, min: 8 }), t('twitch.problemTooLong', { length: 128, max: 128 })]}
      >
        {problem ?? ''}
      </ReservedText>
    </div>
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

  return (
    <div
      className="pt-3 space-y-2.5"
      style={{ borderTop: '1px solid var(--border-subtle)' }}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <label
          className="text-xs font-bold"
          style={{ color: 'var(--text-secondary)' }}
        >
          {label}
        </label>

        {conn.checking ? (
          <span
            className="inline-flex items-center gap-1 text-xs font-bold"
            style={{ color: 'var(--accent-crystal)' }}
          >
            <IconLoader2 size={13} className="animate-spin" />
            {t('ai.row.checking')}
          </span>
        ) : conn.connected ? (
          <span className="pv-badge pv-badge-ok">
            <IconCheck size={12} /> {t('ai.row.connected', { count: conn.models.length })}
          </span>
        ) : hasAnyKey && conn.error ? (
          <span
            className="text-xs font-bold"
            style={{ color: 'var(--accent-crimson)' }}
          >
            {t('ai.row.failed', { reason: conn.error })}
          </span>
        ) : (
          <span className="text-[11px]" style={{ color: 'var(--text-muted)' }}>
            {t('ai.row.none')}
          </span>
        )}
      </div>

      {hint && (
        <p className="text-[11px] leading-snug" style={{ color: 'var(--text-muted)' }}>
          {hint}
        </p>
      )}

      <div className="grid grid-cols-1 md:grid-cols-12 gap-3 items-center">
        <div
          className={
            conn.connected && conn.models.length > 0
              ? 'md:col-span-7 flex gap-2'
              : 'md:col-span-12 flex gap-2'
          }
        >
          <input
            type="password"
            value={keyValue}
            onChange={(e) => onKeyChange(e.target.value)}
            placeholder={hasSavedKey && !isCleared ? t('ai.row.saved', { hint: savedKeyHint }) : placeholder}
            className="pv-input w-full"
          />
          {hasAnyKey && (
            <Tooltip label={t('ai.row.recheck')} description={t('ai.row.recheckHint')}>
              <button
                type="button"
                onClick={onRecheck}
                disabled={conn.checking}
                className="pv-btn !px-2.5"
                aria-label={t('ai.row.recheckLabel')}
              >
                <IconRefresh
                  size={15}
                  className={conn.checking ? 'animate-spin' : ''}
                />
              </button>
            </Tooltip>
          )}
          {hasSavedKey && (
            <Tooltip label={t('ai.row.removeKey')}>
              <button
                type="button"
                onClick={onToggleClear}
                className="pv-btn !px-2.5"
                aria-label={t('ai.row.removeKey')}
              >
                <IconTrash size={15} />
              </button>
            </Tooltip>
          )}
        </div>

        {/* Model Dropdown is ONLY rendered when an API key is present AND the connection succeeded */}
        {conn.connected && conn.models.length > 0 && (
          <div className="md:col-span-5">
            <select
              value={selectedModel}
              onChange={(e) => onSelectModel(e.target.value)}
              aria-label={t('ai.row.modelLabel', { provider: label })}
              className="pv-input w-full font-semibold"
            >
              {conn.models.map((m) => (
                <option key={m.id} value={m.id}>
                  {isBudgetModel(m.id) ? t('ai.row.budgetTip', { model: m.label }) : m.label}
                </option>
              ))}
            </select>
          </div>
        )}
      </div>
    </div>
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
      className="pv-modal-backdrop"
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
      await downloadBridgePackage()
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
  const command = created ? `PlatinVault.Bridge.exe --server ${serverUrl} --token ${created.token} --save` : ''
  const onLocalhost = ['localhost', '127.0.0.1', '[::1]'].includes(window.location.hostname)

  return (
    <div className="pv-card space-y-3">
      <div className="flex min-h-7 flex-wrap items-center justify-between gap-2">
        <div id={titleId} className="flex items-center gap-2 text-sm font-extrabold" style={{ color: 'var(--accent-purple)' }}>
          <IconPlugConnected size={18} />
          <span>{t('bridge.title')}</span>
        </div>
        {hasToken && (
          <span className="pv-badge pv-badge-ok">
            <IconCheck size={13} /> {status?.hint ? t('bridge.tokenActiveHint', { hint: status.hint }) : t('bridge.tokenActive')}
          </span>
        )}
      </div>

      <p className="text-xs leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
        {t('bridge.intro')}
        {status?.bundledVersion ? ` ${t('bridge.introVersion', { version: status.bundledVersion })}` : ''}
      </p>

      <div
        className="flex flex-wrap items-center justify-between gap-3 rounded-xl p-3"
        style={{ background: 'var(--bg-elevated)', border: '1px solid var(--border-subtle)' }}
        role="group"
        aria-labelledby={titleId}
      >
        <div className="flex min-h-10 min-w-0 flex-1 items-center gap-2.5 text-xs" aria-live="polite">
          <span
            className="h-2.5 w-2.5 shrink-0 rounded-full"
            style={{ background: statusColor }}
            aria-hidden="true"
          />
          <span className="font-bold break-words" style={{ color: 'var(--text-primary)' }}>
            {statusText}
          </span>
        </div>
        <div className="flex min-w-0 flex-wrap gap-2">
          <Tooltip
            label={t('bridge.download')}
            description={
              status?.bundledVersion
                ? t('bridge.downloadHint', { version: status.bundledVersion })
                : t('bridge.downloadMissing')
            }
          >
            <button
              type="button"
              onClick={handlePackageClick}
              disabled={busyAction !== null || isLoading || !status?.bundledVersion}
              className="pv-btn pv-btn-primary relative disabled:cursor-not-allowed disabled:opacity-60"
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
            className="pv-btn pv-btn-emerald relative disabled:cursor-not-allowed disabled:opacity-60"
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
            className="pv-btn disabled:cursor-not-allowed disabled:opacity-60"
            style={
              hasToken
                ? {
                    background: 'var(--accent-crimson-bg)',
                    color: 'var(--accent-crimson)',
                    borderColor: 'var(--accent-crimson-border)',
                  }
                : undefined
            }
          >
            <IconTrash size={15} />
            <span>{t('bridge.deleteToken')}</span>
          </button>
        </div>
      </div>

      {/* Reserved room for an error of the direct "Token erzeugen" click, so it moves nothing */}
      <div className="min-h-9 text-xs font-bold" role="alert" style={{ color: 'var(--accent-crimson)' }}>
        {cardError}
      </div>

      {confirm && (
        <BridgeDialog
          labelledBy={confirmTitleId}
          busy={busyAction !== null}
          onClose={() => setConfirm(null)}
        >
          <div className="space-y-4 p-6">
            <div className="flex items-start gap-3.5">
              <div
                className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl"
                style={{
                  background: 'var(--accent-crimson-bg)',
                  color: 'var(--accent-crimson)',
                  border: '1px solid var(--accent-crimson-border)',
                }}
              >
                <IconAlertTriangle size={20} />
              </div>
              <div className="min-w-0 space-y-1.5">
                <h2 id={confirmTitleId} className="text-base font-extrabold" style={{ color: 'var(--text-primary)' }}>
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

            <div className="min-h-[2.75rem] text-xs font-bold" role="alert" style={{ color: 'var(--accent-crimson)' }}>
              {dialogError}
            </div>

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
                className={`pv-btn relative ${confirm === 'replace' ? 'pv-btn-emerald' : confirm === 'package' ? 'pv-btn-primary' : ''}`}
                style={
                  confirm === 'delete'
                    ? {
                        background: 'var(--accent-crimson-bg)',
                        color: 'var(--accent-crimson)',
                        borderColor: 'var(--accent-crimson-border)',
                      }
                    : undefined
                }
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
                className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl"
                style={{
                  background: 'var(--accent-purple-bg)',
                  color: 'var(--accent-purple)',
                  border: '1px solid var(--accent-purple-border)',
                }}
              >
                <IconPlugConnected size={20} />
              </div>
              <div className="min-w-0 space-y-1.5">
                <h2 id={createdTitleId} className="text-base font-extrabold" style={{ color: 'var(--text-primary)' }}>
                  {t('bridge.created.title')}
                </h2>
                <p className="text-sm leading-relaxed" style={{ color: 'var(--text-secondary)' }}>
                  <Trans t={t} i18nKey="bridge.created.text" components={{ code: <code /> }} />
                </p>
              </div>
            </div>

            <div className="pv-alert pv-alert-gold !p-3 text-xs leading-snug">
              <IconAlertTriangle size={17} className="mt-px shrink-0" style={{ color: 'var(--accent-gold)' }} />
              <span className="font-semibold">
                {t('bridge.created.warning')}
              </span>
            </div>

            <div className="space-y-1">
              <label htmlFor="bridge-token-value" className="block text-xs font-bold" style={{ color: 'var(--text-secondary)' }}>
                {t('bridge.created.tokenLabel')}
              </label>
              <div className="flex gap-2">
                <input
                  id="bridge-token-value"
                  type="text"
                  readOnly
                  value={created.token}
                  onFocus={(e) => e.currentTarget.select()}
                  className="pv-input w-full font-mono !text-xs"
                />
                <CopyButton
                  value={created.token}
                  kind="token"
                  onFailed={() => setCopyHint(t('bridge.created.copyBlocked'))}
                />
              </div>
            </div>

            <div className="space-y-1">
              <label htmlFor="bridge-command-value" className="block text-xs font-bold" style={{ color: 'var(--text-secondary)' }}>
                {t('bridge.created.commandLabel')}
              </label>
              <div className="flex items-start gap-2">
                <textarea
                  id="bridge-command-value"
                  readOnly
                  rows={4}
                  value={command}
                  onFocus={(e) => e.currentTarget.select()}
                  className="pv-input w-full resize-none font-mono !text-xs"
                />
                <CopyButton
                  value={command}
                  kind="command"
                  onFailed={() => setCopyHint(t('bridge.created.copyBlocked'))}
                />
              </div>
            </div>

            {/* Reserved room: a localhost hint and/or a copy failure message never move the buttons */}
            <div className="min-h-[3.75rem] space-y-1 text-xs leading-snug">
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
              <button type="button" className="pv-btn pv-btn-emerald" onClick={() => setCreated(null)}>
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
