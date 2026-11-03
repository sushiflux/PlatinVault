import deAi from './de/ai'
import deCabinet from './de/cabinet'
import deCommon from './de/common'
import deDashboard from './de/dashboard'
import deErrors from './de/errors'
import deGame from './de/game'
import deLive from './de/live'
import deOnboarding from './de/onboarding'
import deSettings from './de/settings'
import deShell from './de/shell'
import deStats from './de/stats'
import enAi from './en/ai'
import enCabinet from './en/cabinet'
import enCommon from './en/common'
import enDashboard from './en/dashboard'
import enErrors from './en/errors'
import enGame from './en/game'
import enLive from './en/live'
import enOnboarding from './en/onboarding'
import enSettings from './en/settings'
import enShell from './en/shell'
import enStats from './en/stats'

// One namespace per area of the app, so several people (or agents) can work on their texts without touching the same file:
//   common     words every screen uses (Cancel, Close, Try again, ...)
//   shell      app frame: header icons and tooltips, footer, "back to top", handbook and licence viewers, Steam account chip
//   settings   the settings window with all tabs, the system check
//   onboarding first-run language screen, intro tour, page hints
//   dashboard  start page: game tiles, library, header actions of the dashboard
//   game       game page: guide chapters, trophy table, game header actions
//   live       live trophies: popups, platinum celebration, trophy list/cards
//   stats      statistics tab and charts
//   cabinet    trophy cabinet
//   ai         AI selection (modes, chooser dialog, the "which AI" button, notices) and the research building blocks
//   errors     error screens, API error messages
export const NAMESPACES = ['common', 'shell', 'settings', 'onboarding', 'dashboard', 'game', 'live', 'stats', 'cabinet', 'ai', 'errors'] as const
export type Namespace = (typeof NAMESPACES)[number]
export const DEFAULT_NAMESPACE = 'common' satisfies Namespace

// German is the source of truth: the English files are typed against it (see types.ts)
export const de = {
  common: deCommon,
  shell: deShell,
  settings: deSettings,
  onboarding: deOnboarding,
  dashboard: deDashboard,
  game: deGame,
  live: deLive,
  stats: deStats,
  cabinet: deCabinet,
  ai: deAi,
  errors: deErrors,
} as const

export const en = {
  common: enCommon,
  shell: enShell,
  settings: enSettings,
  onboarding: enOnboarding,
  dashboard: enDashboard,
  game: enGame,
  live: enLive,
  stats: enStats,
  cabinet: enCabinet,
  ai: enAi,
  errors: enErrors,
} satisfies { [N in Namespace]: unknown }

export const resources = { de, en } as const
