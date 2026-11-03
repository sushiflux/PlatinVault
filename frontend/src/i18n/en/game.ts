import type { Translation } from '../types'
import type de from '../de/game'

// Namespace "game": English texts, typed against the German source file.
const game = {
  loading: 'Loading 100% guide …',
  loadFailed: 'The game guide could not be loaded.',
  backHome: 'Back to the start page',

  header: {
    home: {
      label: 'To the start page',
      description: 'Back to the game overview',
    },
    coverAlt: 'Trophy',
    titleSuffix: '100% Master Companion',
    extendGroup: 'Extend the guide',
    complete: {
      label: 'Complete the guide',
      description: 'Guide builder: the AI checks all standard topics (weapons, armour, tactics, bestiary, levels …) and suggests what is still missing',
    },
    addTab: {
      label: 'Add chapter',
      description: 'Add a single chapter – click a topic or describe your own',
    },
    viewGroup: 'View, export and progress',
    export: {
      label: 'Export guide',
      description: 'As a file to share or back up – without your progress',
    },
    print: {
      label: 'Print as PDF',
      description: 'Opens the print dialog – choose “Save as PDF” there',
    },
    reset: {
      label: 'Reset progress',
      description: 'Removes all ticks of this game',
    },
  },

  nav: {
    region: 'Chapter navigation',
    title: 'Guide chapters',
    expandSidebar: 'Expand sidebar',
    collapseSidebar: 'Collapse sidebar',
    openChapters: 'Chapters',
    openChaptersDescription: 'Open the guide chapters',
    closeChapters: 'Close chapters',
  },

  chapters: {
    listLabel: 'Guide chapters',
    progress: '{{checked}} of {{total}} ticked off',
    noItems: 'No checklist items',
    groups: {
      start: 'Getting started',
      story: 'Story & world',
      characters: 'Characters & companions',
      combat: 'Combat & tactics',
      progress: 'Progression & unlocks',
      explore: 'Collecting & exploring',
      economy: 'Economy & crafting',
      extras: 'Extras',
    },
  },

  sections: {
    groupLabel: 'Areas of the game page',
    live: {
      label: 'Live trophies',
      fullLabel: 'Steam trophies (live)',
      description: 'All trophies of this game live from Steam, with rarity',
      descriptionWithNotes: 'All trophies of this game live from Steam, with rarity and your own notes',
    },
    stats: {
      label: 'Statistics',
      fullLabel: 'Statistics',
      description: 'Progress, rarity, playtime and history for this game',
    },
    lore: {
      label: 'Story & Lore',
      fullLabel: 'Story & Lore',
      description: 'Plot, characters and world – contains spoilers and is only shown after you confirm',
    },
  },

  trophyBar: {
    region: 'Trophy overview and areas',
    summary: '{{achieved}} / {{total}} trophies ({{percent}})',
    progress: 'Trophy progress',
  },
  progressBar: {
    label: 'Progress',
  },

  notLinked: {
    title: 'Not linked to Steam.',
    text: 'Without a Steam AppID there is no cover, no live achievements and no statistics.',
    button: 'Link to Steam',
  },
  noGuide: {
    text: 'There is no guide for this game yet.',
    button: 'Research the first chapter with AI',
  },
  hero: {
    title: '{{title}} — 100% Master Guide',
    openLive: 'Open live Steam trophies',
    backToOverview: 'Back to the game overview',
  },
  contentLanguage: {
    hint: 'This guide was written in {{language}}.',
    de: 'German',
    en: 'English',
  },

  // A guide is written in one language; in the other UI language it is translated by the user's AI (the original is kept)
  translate: {
    // "{{language}}" after "Translating the guide into"
    into: { de: 'German', en: 'English' },
    // "from {{language}}" in the hint above the guide
    from: { de: 'German', en: 'English' },
    translatedFrom: 'Translated automatically from {{language}} – the original is kept.',
    running: {
      title: 'Translating the guide into {{language}} …',
      message: 'The AI translates every chapter. Until then you see the original.',
      progress: 'The AI translates every chapter ({{translated}} of {{total}} done). Until then you see the original.',
    },
    failed: {
      title: 'The guide could not be translated into {{language}}',
    },
    retry: 'Try again',
  },

  lore: {
    none: 'No Story & Lore for this game yet.',
    noneHint:
      'The AI researches the story explained simply, characters and their relationships, the world, insider knowledge and the development history. The result contains spoilers and stays hidden until you open it.',
    research: 'Research Story & Lore',
    gateTitle: 'Warning: story spoilers',
    gateText: 'This tab explains the complete plot including twists and the ending, plus characters, relationships and background.',
    remember: 'Remember for this game',
    reveal: 'Show spoilers',
    redo: {
      label: 'Research Story & Lore again',
      description: 'Replaces the tab with a fresh research',
      button: 'Research again',
    },
  },
  spoilerSection: {
    fallbackTitle: 'Section',
    show: 'Show spoilers',
  },

  // Puzzle block in the guide: location and requirements are always visible, hints and the solution only appear on request
  puzzle: {
    badge: 'Puzzle',
    missable: 'Missable',
    where: 'Where',
    requirements: 'Requirement',
    done: 'Done',
    doneFor: 'Mark puzzle “{{name}}” as done',
    selfHint: 'The solution stays hidden until you ask for it – you can also try it yourself first.',
    hint: {
      show: 'Show hint',
      next: 'Show next hint',
      hide: 'Hide hints',
      title: 'Hint {{shown}} of {{total}}',
    },
    solution: {
      show: 'Show solution',
      hide: 'Hide solution',
      title: 'Solution',
      source: 'Source: {{source}}',
    },
    undocumented: {
      title: 'Solution not documented',
      text: 'No verified solution was found for this puzzle. The research invents nothing – a vague sentence would not help.',
    },
  },

  // Typed hints in the walkthrough (the label of the hint; symbol and colour come from the kind)
  hintKind: {
    must_have: 'Important – take it along',
    missable: 'Missable from here',
    optional: 'Optional content available here',
    tip: 'Tip',
  },
  stepsList: 'Steps',

  table: {
    searchPlaceholder: 'Search the table...',
    categoryFilter: 'Filter by category',
    allCategories: 'All categories',
    secondaryFilter: 'Secondary filter',
    allFilters: 'All filters',
    caption_one: '{{caption}} ({{count, number}} entry)',
    caption_other: '{{caption}} ({{count, number}} entries)',
    done: 'Done',
    doneFor: 'Done: {{name}}',
  },

  fail: {
    lore: 'Couldn’t research Story & Lore',
    check: 'Couldn’t save the tick',
    reset: 'Couldn’t reset the progress',
    complete: 'Couldn’t complete the guide',
  },
  resetConfirm: {
    title: 'Reset ticks?',
    message: 'All ticked entries in the guide for “{{title}}” will be removed. Your Steam trophies are not affected.',
    confirm: 'Reset',
  },
  notice: {
    details: 'Technical details',
    aiSettings: 'AI settings',
    completed: {
      title: 'Guide completed',
      added_one: '{{count, number}} chapter added: {{titles}}',
      added_other: '{{count, number}} chapters added: {{titles}}',
      dropped: 'Left out because this game does not have it: {{titles}}.',
    },
    nothingToAdd: {
      title: 'Nothing to add',
      message: 'All suitable topics are already in the guide.',
    },
    steamLinked: {
      title: 'Linked to Steam',
      message: 'Cover, live achievements and statistics are now active.',
    },
  },

  expand: {
    title: 'Research a new chapter for “{{title}}” with AI',
    pickTopic: 'Click a topic',
    titleLabel: 'Title of the new chapter *',
    titlePlaceholder: 'e.g. 8. All secret bosses & locations',
    promptLabel: 'What should the AI look up on the web for this chapter? *',
    promptPlaceholder: 'e.g. List all secret armour, accessories and the best materia combinations with locations and tactics.',
    missing: 'Please enter a chapter title and your question / the topic.',
    failedFallback: 'The AI research failed.',
    submit: 'Create chapter now',
  },

  builder: {
    title: 'Guide builder · {{game}}',
    intro: 'The AI has analysed the game – suitable topics are ticked. Adjust them or start the research right away.',
    analysisFailed: 'Analysis failed: {{reason}}',
    researchFailed: 'Research failed: {{reason}}',
    genre: 'Genre:',
    genreFromSteam: 'Detected from the Steam community tags',
    genreFromAi: 'Detected by the AI',
    noGenre: 'no clear match',
    groups: {
      recommended: 'Recommended',
      other: 'More topics',
      otherHint: 'The analysis says these fit less well – tick them anyway if you like.',
      covered: 'Already in the guide',
    },
    required: 'Typical',
    all: 'All',
    onlyRecommended: 'Recommended only',
    research_one: 'Research {{count, number}} chapter',
    research_other: 'Research {{count, number}} chapters',
  },

  steamLink: {
    title: 'Link to Steam',
    text: 'With the Steam AppID the game gets the official cover, live achievements and statistics.',
    searchLabel: 'Game name in the Steam Store',
    placeholder: 'Search the Steam Store for the game name …',
    appId: 'AppID {{id}}',
    none: 'No results in the Steam Store.',
    searchFailed: 'Steam search failed: {{reason}}',
    linkFailed: '“{{name}}” could not be linked: {{reason}}',
  },

  twitch: {
    label: 'Live on Twitch',
    searchDescription: 'Opens a Twitch search for “{{name}}” – see who is playing it live right now.',
    gameDescription: 'Opens Twitch with all live streams for “{{name}}”.',
    // The count of live streams on the button of a card (only with Twitch credentials)
    live_one: '{{count}} live stream',
    live_other: '{{count}} live streams',
    liveMany: 'More than {{count}} live streams',
    liveNone: 'Nobody is streaming this game right now.',
  },
} satisfies Translation<typeof de>

export default game
