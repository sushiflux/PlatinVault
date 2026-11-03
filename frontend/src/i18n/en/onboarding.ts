import type { Translation } from '../types'
import type de from '../de/onboarding'

// Namespace "onboarding": English texts, typed against the German source file.
const onboarding = {
  language: {
    kicker: 'Welcome',
    title: 'Choose your language',
    groupLabel: 'App language',
    optionHint: 'Interface, Steam achievements and formats in English',
    note: 'Applies to the whole app. You can switch the language at any time in the top bar (globe icon).',
    continue: 'Continue',
    saveFailed: 'The language could not be saved: {{reason}}',
  },

  tour: {
    ariaLabel: 'Get to know PlatinVault',
    stepOf: 'Step {{current}} of {{total}}',
    stepAnnouncement: 'Step {{current}} of {{total}}: {{title}}',
    skip: 'Skip',
    skipHint: 'End the tour',
    stepsNav: 'Steps of the tour',
    stepDot: 'Step {{n}}: {{title}}',
    back: 'Back',
    next: 'Next',
    finish: 'Let’s go',
  },

  steps: {
    welcome: {
      title: 'Welcome to PlatinVault',
      intro:
        'PlatinVault accompanies you on your 100% run: it tracks your Steam achievements live, automatically researches a complete guide for every game and shows you how far you are.',
      points: {
        live: { lead: 'Live trophies', text: 'a trophy popup with sound as soon as you unlock an achievement.' },
        guides: { lead: 'Guides without typing', text: 'weapons, bosses, missables – researched by the AI, with checklists.' },
        stats: { lead: 'Statistics', text: 'playtime, progress and rarity at a glance.' },
      },
      visual: {
        library: '4 games in the library',
        points: '218 / 480 points (45%)',
        platinum: '1 game at 100%',
        progress: '100% progress',
        progressValue: '72 / 120 (60%)',
        popupExample: 'This is what a live popup looks like',
      },
    },
    games: {
      title: 'Bring games onto the dashboard',
      intro:
        'Your games come from your Steam library. Every game with a star is on the dashboard and automatically gets live trophies and a guide.',
      points: {
        account: { lead: 'Steam account', text: 'connect it with “Sign in with Steam”.' },
        key: { lead: 'Web API key', text: 'get it from Steam for free and enter it in the settings.' },
        star: { lead: 'Star', text: 'tap it in “All games” – you create the guide afterwards on the game page.' },
      },
      visual: {
        favorites: 'Favourites',
        allGames: 'All games',
        starRow: 'Star = onto the dashboard',
        genre: 'Action RPG',
        tags: 'Open world · Bosses · Missables',
        playtime: 'Playtime',
        playtimeValue: 'approx. 100 h',
        progress: '100% progress',
        progressValue: '12 / 40 (30%)',
        caption: 'Live popups for new achievements and a 100% guide with checklists – without you having to set anything up.',
      },
    },
    guide: {
      title: 'The guide',
      intro:
        'The guide is created automatically – you do not have to type anything. There are chapters for the standard topics, each with checklists you can tick off.',
      points: {
        chapters: { lead: 'Chapters', text: 'weapons, armour, tactics, bestiary, levels …' },
        checks: { lead: 'Ticks count', text: 'your progress is saved and shown on the start page.' },
        complete: { lead: 'Complete guide', text: 'researches missing standard topics.' },
        addTab: { lead: 'Add tab', text: 'click a topic or describe your own.' },
      },
      visual: {
        complete: 'Complete guide',
        addTab: 'Add tab',
        sampleChapters: 'Example chapters',
        chapterPanel: 'Chapter {{title}}',
        topicsHeading: '“Add tab”: just click a topic',
        tabs: {
          weapons: {
            title: 'Weapons',
            rows: {
              r1: { label: 'Longsword from the dungeon chest', hint: 'Location' },
              r2: { label: 'Enchanted axe', hint: 'Side quest' },
              r3: { label: 'Bow of the Guardian', hint: 'Merchant' },
              r4: { label: 'Legendary blade', hint: 'Endgame' },
            },
          },
          armor: {
            title: 'Armour',
            rows: {
              r1: { label: 'Complete knight set', hint: 'Set' },
              r2: { label: 'Shadow cloak', hint: 'Missable' },
              r3: { label: 'Dragon-scale cuirass', hint: 'Boss' },
              r4: { label: 'Reinforced gauntlets', hint: 'Blacksmith' },
            },
          },
          tactics: {
            title: 'Tactics',
            rows: {
              r1: { label: 'Boss 1: dodge phase 2', hint: 'Boss' },
              r2: { label: 'Boss 2: weak spot on the back', hint: 'Boss' },
              r3: { label: 'Save healing before phase 3', hint: 'Tip' },
              r4: { label: 'Ranged attacks against guardians', hint: 'Tip' },
            },
          },
          bestiary: {
            title: 'Bestiary',
            rows: {
              r1: { label: 'Skeleton warrior', hint: 'weak: lightning' },
              r2: { label: 'Bog witch', hint: 'ranged' },
              r3: { label: 'Stone golem', hint: 'joints' },
              r4: { label: 'Shadow wolf', hint: 'weak: fire' },
            },
          },
        },
        topics: {
          weapons: 'Weapons',
          armor: 'Armour',
          tactics: 'Tactics',
          bestiary: 'Bestiary',
          levels: 'Levels',
          collectibles: 'Collectibles',
        },
      },
    },
    live: {
      title: 'Live trophies',
      intro: 'While you play, PlatinVault recognises new Steam achievements in real time and shows a trophy popup with sound.',
      points: {
        tiers: { lead: 'Bronze, silver, gold', text: 'the rarer the achievement, the higher the tier.' },
        platinum: { lead: 'Platinum', text: 'once all achievements of a game are unlocked, there is a big platinum celebration.' },
        try: { lead: 'Try it out', text: 'try it on the right. This is only an example, nothing is saved.' },
      },
      visual: {
        screenLabel: 'Your screen: the popup appears {{position}}',
        tierBronze: 'Bronze',
        tierSilver: 'Silver',
        tierGold: 'Gold',
        tierPlatinum: 'Platinum',
        showPopup: 'Show example popup',
        showPlatinum: 'Watch the platinum celebration',
        caption: 'The popup appears {{position}}. You set position, sound and volume in the settings.',
        exampleGame: 'Example game',
        positions: {
          'top-left': 'at the top left',
          'top-center': 'at the top centre',
          'top-right': 'at the top right',
          'center-left': 'at the middle left',
          center: 'in the middle of the screen',
          'center-right': 'at the middle right',
          'bottom-left': 'at the bottom left',
          'bottom-center': 'at the bottom centre',
          'bottom-right': 'at the bottom right',
          fullscreen: 'in full screen',
        },
        demo: {
          bronze: { label: 'Uncommon', name: 'First Assignment', description: 'Complete the first assignment.' },
          silver: { label: 'Rare', name: 'Weapon Master', description: 'Master every weapon type in the game.' },
          gold: { label: 'Ultra rare', name: 'Master of Fate', description: 'Beat the game on the highest difficulty.' },
        },
      },
    },
    stats: {
      title: 'Statistics & story',
      intro:
        'Every game with a Steam link has a statistics tab. If you want to know more about the story, you can additionally switch on Story & Lore.',
      points: {
        stats: { lead: 'Statistics', text: 'playtime, progress, rarest achievement, reviews and news from Steam.' },
        lore: { lead: 'Story & Lore', text: 'optional, you switch it on in the settings. It is only researched on click.' },
        spoiler: { lead: 'Spoiler protection', text: 'the story stays hidden until you deliberately open it.' },
      },
      visual: {
        playtime: 'Total playtime',
        playtimeValue: '87 h',
        playtimeHint: 'Last 2 weeks: 6 h',
        progress: 'Progress',
        progressValue: '63%',
        progressHint: '45 of 72 achievements',
        rarest: 'Rarest achievement',
        rarestValue: '1.8%',
        rarestHint: 'Master of Fate',
        players: 'Players right now',
        playersValue: '54,210',
        playersHint: 'Live from Steam',
        loreTitle: 'Story & Lore',
        optional: 'Optional',
        loreExample:
          'Example: the plot is explained simply – with characters, their relationships, the world and background on how the game came about.',
        spoilerWarning: 'Warning: story spoilers – hidden until you open them.',
        hideAgain: 'Hide again',
        showSpoiler: 'Show spoiler',
      },
    },
    setup: {
      title: 'Setup',
      intro:
        'To get everything running you only need a few keys. The list on the right shows live what is already done – a click opens the settings.',
      points: {
        ai: { lead: 'AI key', text: 'without it no automatic guides are created. Gemini is free (including Google Search).' },
        steam: { lead: 'Steam', text: 'connect your account and enter the Web API key – your library comes from them, and your games from it.' },
        bridge: { lead: 'Bridge', text: 'only for Unraid users. Everything else works without it.' },
      },
      visual: {
        state: { done: 'done', open: 'open', loading: 'being checked', unknown: 'unknown' },
        optional: 'optional',
        change: 'Change',
        steamAccount: {
          title: 'Steam account linked',
          linked: 'Linked to SteamID {{id}}',
          open: 'So that PlatinVault knows whose achievements to track.',
          actionLink: 'Link',
        },
        steamKey: {
          title: 'Steam Web API key',
          saved: 'Saved ({{hint}})',
          open: 'Needed so PlatinVault can read your Steam library – your games come from it.',
          action: 'Enter',
        },
        aiKey: {
          title: 'AI key (Gemini, Claude or ChatGPT)',
          saved: 'Saved – guides are researched automatically.',
          open: 'Needed for the automatic guides. The research uses Google Search – for new Google accounts only with billing.',
          link: 'Get a free key at Google AI Studio',
          action: 'Enter',
        },
        bridge: {
          title: 'Real-time bridge',
          saved: 'Token exists – the bridge can report achievements.',
          open: 'Only needed if PlatinVault runs on Unraid: a mini program on the gaming PC reports achievements.',
          action: 'Set up',
        },
        igdb: {
          title: 'Game facts from IGDB',
          saved: 'Saved ({{hint}}) – time to 100 %, ratings and genres on the cards.',
          open: 'Time to 100 %, ratings and genres. Needs your own free Twitch developer app.',
          action: 'Set up',
        },
      },
    },
  },

  hints: {
    meta: 'Hint {{current}} of {{total}}',
    hide: 'Hide hints',
    done: 'Got it',
    next: 'Next',
    intro: 'View the full introduction',
    handbook: 'Open handbook (PDF)',
    dashboardAdd: {
      title: 'Bring games onto the dashboard',
      text: 'Your games come from your Steam library: under “All games” you see all of them, the star puts a game on the dashboard.',
    },
    dashboardStar: {
      title: 'Your favourites',
      text: 'The star decides which games sit on the start page. You can pick up the cards and sort them yourself.',
    },
    dashboardSize: {
      title: 'Tile size',
      text: 'The slider shows more or larger cards – right down to a plain text list.',
    },
    dashboardCabinet: {
      title: 'Trophy cabinet',
      text: 'All games you have completed to 100% stand here in the display case – with date and key facts.',
    },
    headerLanguage: {
      title: 'Language',
      text: 'Switches the language of the whole app – interface, date and number formats and Steam texts. The choice applies immediately and is saved.',
    },
    headerHandbook: {
      title: 'Handbook',
      text: 'The detailed handbook as a PDF – right here in the app, in German and English.',
    },
    gameNav: {
      title: 'Chapters',
      text: 'Here you switch between the chapters of the guide. PlatinVault remembers your ticks.',
    },
    gameNavPhone: {
      title: 'Chapters',
      text: 'This button opens the chapter list of the guide. PlatinVault remembers your ticks.',
    },
    gameLive: {
      title: 'Live trophies',
      text: 'All Steam trophies of this game with their rarity. PlatinVault ticks new achievements in the guide automatically.',
    },
    gameTwitch: {
      title: 'Live on Twitch',
      text: 'Opens Twitch with all streams of this game – without an account, without a key.',
    },
    settings: {
      title: 'Settings',
      text: 'Here you enter your Steam ID and keys, set up popups and switch features on or off.',
    },
  },
} satisfies Translation<typeof de>

export default onboarding
