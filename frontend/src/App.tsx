import { useEffect, useState } from 'react'
import { BrowserRouter, Route, Routes } from 'react-router'
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { fetchLiveEvents, fetchLiveStatus, fetchSettings } from './api/client'
import type { LiveAchievementItemDto, NotificationPosition } from './api/types'
import { ErrorBoundary } from './components/errors/ErrorBoundary'
import { FullscreenAchievement } from './components/FullscreenAchievement'
import { PlatinumCelebration } from './components/PlatinumCelebration'
import { TrophyNotification } from './components/TrophyNotification'
import { LanguageGate } from './components/onboarding/LanguageGate'
import { AiChoiceProvider } from './components/ai/AiChoice'
import { LaunchProvider } from './components/launch/LaunchProvider'
import { ConfirmProvider } from './components/ui/ConfirmDialog'
import { DashboardPage } from './pages/DashboardPage'
import { GameGuidePage } from './pages/GameGuidePage'
import { animationQueue } from './utils/animationQueue'
import type { PlatinumData } from './utils/animationQueue'
import { DEFAULT_SOUND_THEME, soundEngine } from './utils/audio'
import { connectLiveHub } from './utils/liveConnection'
import { showsPopupInWindow } from './utils/popupTarget'
import { connectProgramWindow, useProgramWindow } from './utils/programWindow'
import { useGameStartSwitch } from './utils/useGameStartSwitch'
import { useSoundBlockedNotice } from './utils/useSoundBlockedNotice'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 15_000,
      refetchOnWindowFocus: false,
    },
  },
})

function GlobalLiveOverlayBridge() {
  const [activeNotification, setActiveNotification] =
    useState<LiveAchievementItemDto | null>(null)
  const [activePlatinum, setActivePlatinum] = useState<PlatinumData | null>(null)
  // Set only while a settings preview runs: shows the popup at the clicked spot before it is saved
  const [previewPosition, setPreviewPosition] = useState<NotificationPosition | null>(null)

  const { data: settings } = useQuery({
    queryKey: ['settings'],
    queryFn: fetchSettings,
  })
  const { t } = useTranslation('settings')

  const position: NotificationPosition = previewPosition ?? (settings?.popupPosition || 'top-right')
  const fullscreen = position === 'fullscreen'
  // "Trophäen-Popup anzeigen": with "only in the game" the overlay of the Windows program shows the popup instead of this window.
  // The queue keeps running either way, because this window plays the sound (exactly once per unlock, the overlay is silent).
  // A settings preview always shows here.
  const program = useProgramWindow()
  const showHere = previewPosition !== null || showsPopupInWindow(settings?.popupTarget, program.overlay)

  // Full-screen popup names the game: the running game from the live status (only asked for while that popup is up), or the
  // example game during a preview
  const { data: liveStatus } = useQuery({
    queryKey: ['live-status'],
    queryFn: fetchLiveStatus,
    enabled: fullscreen && activeNotification !== null && previewPosition === null,
  })
  const fullscreenGameName = previewPosition !== null ? t('live.exampleGame') : liveStatus?.runningGameName

  useEffect(() => {
    if (settings) {
      soundEngine.setEnabled(settings.playSounds)
      soundEngine.setVolume(settings.soundVolume ?? 0.15)
      soundEngine.setTheme(settings.soundTheme || DEFAULT_SOUND_THEME)
    }
  }, [settings])

  useEffect(() => {
    const unsub = animationQueue.subscribe(
      ({ currentAchievement, currentPosition, currentPlatinum }) => {
        setActiveNotification(currentAchievement)
        setPreviewPosition(currentPosition)
        setActivePlatinum(currentPlatinum)
      }
    )

    const refreshTrophies = () => {
      void queryClient.invalidateQueries({ queryKey: ['live-achievements'] })
      void queryClient.invalidateQueries({ queryKey: ['games'] })
      void queryClient.invalidateQueries({ queryKey: ['game'] })
    }

    // Stays connected across backend restarts and catches up on unlocks sent while this window was not connected
    const disconnect = connectLiveHub(
      {
        onAchievement: (ach) => {
          animationQueue.enqueueAchievement(ach)
          refreshTrophies()
        },
        onPlatinum: (gameName, total) => {
          animationQueue.enqueuePlatinum(gameName, total)
          refreshTrophies()
        },
        onGameChanged: () => {
          void queryClient.invalidateQueries({ queryKey: ['live-status'] })
          void queryClient.invalidateQueries({ queryKey: ['live-achievements'] })
        },
        onConnected: () => {
          void queryClient.invalidateQueries({ queryKey: ['live-status'] })
          refreshTrophies()
        },
      },
      { fetchEvents: fetchLiveEvents }
    )

    // Inside the Windows program: its state (overlay available?) and settings it changed itself (tray menu)
    const disconnectProgram = connectProgramWindow(() => {
      void queryClient.invalidateQueries({ queryKey: ['settings'] })
    })

    return () => {
      unsub()
      disconnect()
      disconnectProgram()
    }
  }, [])

  // The backend only broadcasts unlocks for vault games with achievements enabled, so the overlay is always mounted.
  return (
    <>
      <TrophyNotification
        achievement={fullscreen || !showHere ? null : activeNotification}
        position={position === 'fullscreen' ? 'top-right' : position}
      />
      {/* Always mounted: its screen reader announcement only works when the live region exists before the text appears */}
      <FullscreenAchievement
        achievement={fullscreen && showHere ? activeNotification : null}
        gameName={fullscreenGameName}
        onDismiss={() => animationQueue.dismissAchievement()}
      />
      <PlatinumCelebration
        data={showHere ? activePlatinum : null}
        onDismiss={() => animationQueue.dismissPlatinum()}
      />
    </>
  )
}

// Renders nothing: lets this window follow a game that was just started (needs the router and the launch provider above it),
// and says so when the browser refused an achievement sound (needs the launch provider's notice stack)
function GameStartSwitchBridge() {
  useGameStartSwitch()
  useSoundBlockedNotice()
  return null
}

// Root application component managing the global pastel theme state, SignalR trophy overlay, and client-side routing.
export function App() {
  // Light or dark, nothing else. public/theme-init.js has already settled the stored value before the first paint (a first visit
  // starts from the OS preference once, an older stored "system" became the theme it resolved to); this only reads it back.
  const [theme, setTheme] = useState<'light' | 'dark'>(() => {
    const stored = document.documentElement.dataset.theme ?? localStorage.getItem('companion_vault_theme_v2')
    return stored === 'dark' ? 'dark' : 'light'
  })

  useEffect(() => {
    localStorage.setItem('companion_vault_theme_v2', theme)
    document.documentElement.dataset.theme = theme
    const meta = document.getElementById('color-scheme-meta')
    if (meta) meta.setAttribute('content', theme)
  }, [theme])

  const handleToggleTheme = () => {
    setTheme((prev) => (prev === 'light' ? 'dark' : 'light'))
  }

  return (
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <GlobalLiveOverlayBridge />
        <ConfirmProvider>
          <AiChoiceProvider>
          {/* The first-run language screen comes before everything else: no tour, hint or page shows up behind it */}
          <LanguageGate>
            {/* Starts games (Play) and shows what happens as floating notices, on every page */}
            <LaunchProvider>
            <BrowserRouter>
              <GameStartSwitchBridge />
              <Routes>
                <Route
                  path="/"
                  element={
                    <DashboardPage
                      theme={theme}
                      onToggleTheme={handleToggleTheme}
                    />
                  }
                />
                <Route
                  path="/games/:slug"
                  element={
                    <GameGuidePage
                      theme={theme}
                      onToggleTheme={handleToggleTheme}
                    />
                  }
                />
              </Routes>
            </BrowserRouter>
            </LaunchProvider>
          </LanguageGate>
          </AiChoiceProvider>
        </ConfirmProvider>
      </QueryClientProvider>
    </ErrorBoundary>
  )
}
