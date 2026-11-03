import type { Translation } from '../types'
import type de from '../de/live'

// Namespace "live": English texts, typed against the German source file.
const live = {
  tiers: {
    Platinum: 'Platinum',
    Gold: 'Gold',
    Silver: 'Silver',
    Bronze: 'Bronze',
  },
  tierTrophy: '{{tier}} trophy',
  tierPills: {
    unlocked: '{{achieved}} of {{total}} unlocked',
  },
  celebration: {
    earned: 'Platinum trophy earned',
    ariaLabel: 'Platinum trophy earned: {{game}}',
    headline: '100% – PLATINUM',
    summaryGuide_one: 'The guide item is ticked off',
    summaryGuide_other: 'All {{count, number}} guide items ticked off',
    summaryAchievements_one: 'The achievement is unlocked',
    summaryAchievements_other: 'All {{count, number}} achievements unlocked',
    dismiss: 'Click anywhere or press Esc to continue',
  },
  fullscreen: {
    unlocked: '{{tier}} trophy unlocked',
    share: '{{percent}} of players',
    dismiss: 'Click anywhere or press Esc to close the popup',
  },
  tracker: {
    loadingName: 'Loading Steam achievements ...',
    refresh: 'Refresh Steam achievements now',
    running: {
      label: 'Live',
      description: 'The game is running right now, trophies are checked continuously',
    },
    platinum: '100% platinum',
    syncedGuide: '{{n}} ticked off in the guide',
    playtime: 'Playtime {{time}}',
    minutes: '{{n}} min',
    hours: '{{n}} h',
    filters: {
      statusGroup: 'Show achievements',
      status: {
        all: 'All',
        needed: 'Still needed',
        unlocked: 'Unlocked',
        hidden: 'Secret',
      },
      tierGroup: 'Filter by tier',
      tierAll: 'All',
    },
    search: {
      label: 'Search achievements',
      placeholderWithNotes: 'Search name, text or note ...',
      placeholder: 'Search name or text ...',
    },
    sort: {
      label: 'Sort order',
      default: 'Sort: default',
      rarityAsc: 'Rarest first (Gold → Bronze)',
      rarityDesc: 'Most common first',
      recent: 'Recently unlocked',
      name: 'Alphabetical (A–Z)',
    },
    reveal: {
      label: 'Reveal secret achievements',
      on: 'On: the descriptions are visible (spoilers).',
      off: 'Off: the descriptions stay hidden.',
    },
    loading: 'Loading live achievements...',
    empty: 'No achievements match this filter.',
  },
  density: {
    label: 'View',
    list: 'List',
    tiles: 'Tiles',
    compact: 'Compact',
    large: 'Large',
  },
  trophy: {
    noDescription: 'No description available.',
    hiddenAchievement: 'Hidden story/secret achievement',
    hiddenAchievementOpen: 'Hidden story/secret achievement – click to open',
    secret: 'Secret',
    reveal: 'Reveal',
    shareTooltip: '{{share}} of all Steam players have this achievement',
    shareDescription: 'Rarity, not your progress',
    ofPlayers: 'of players',
    translated: {
      tooltip: 'Automatically translated into English',
      badge: 'EN',
      showTranslation: 'Show translation',
      showOriginal: 'Show original',
    },
    howTo: 'How to get it:',
    unlocked: 'Unlocked',
    unlockedOn: 'Unlocked on {{date}}',
    stillOpen: 'Still open',
    itemLabelUnlocked: '{{name}}, {{tier}}, unlocked. Click for details',
    itemLabelOpen: '{{name}}, {{tier}}, still open. Click for details',
    progress: {
      label: 'Progress',
      ariaLabel: 'Progress {{current}} of {{max}}',
    },
    note: {
      change: 'Edit note',
      add: 'Add note',
    },
    noteQuote: '“{{note}}”',
    tip: {
      hide: 'Hide tip',
      show: 'Show tip',
    },
    detail: {
      shareLine: '{{tier}} · {{share}} of players',
      howTo: 'How to get it: {{tip}}',
      progress: 'Progress: {{counter}}',
      note: 'Note: {{note}}',
    },
  },
  noteDialog: {
    title: 'Personal note',
    placeholder: 'Your own tips, missing locations or progress notes...',
    save: 'Save note',
    saveFailed: 'The note could not be saved: {{reason}}',
    serverUnreachable: 'PlatinVault server unreachable – is the backend running?',
  },
  gameStart: {
    started: {
      title: '{{name}} was started',
      message: 'You were not switched over automatically because you are in the middle of something here.',
      action: 'Switch to the game',
    },
    noGuide: {
      title: 'You are playing {{name}} – no guide yet.',
    },
    unknownGame: 'a game',
  },
  soundBlocked: {
    title: 'Sound blocked by the browser',
    message:
      'Browsers only play sounds after you have clicked on PlatinVault or pressed a key once – for example after the page was reloaded. That is why the last achievement came without sound. One click here or anywhere on the page allows sound again.',
    action: 'Play the missed sound',
  },
} satisfies Translation<typeof de>

export default live
