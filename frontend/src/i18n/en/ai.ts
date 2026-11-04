import type { Translation } from '../types'
import type de from '../de/ai'

// Namespace "ai": English texts, typed against the German source file.
const ai = {
  providers: {
    gemini: 'Gemini',
    claude: 'Claude',
    openai: 'OpenAI',
  },
  state: {
    ok: 'Ready',
    quota: 'Quota used up',
    invalidKey: 'Key rejected',
    unavailable: 'Not reachable',
  },
  stateSince: {
    quota: 'Quota used up since {{time}}',
    invalidKey: 'Key rejected since {{time}}',
    unavailable: 'Not reachable since {{time}}',
  },
  noWebSearch: 'no web search',
  noWebSearchHint: 'PlatinVault calls OpenAI without web search. Gemini or Claude are better suited for guide research.',
  noKey: 'No key saved',

  uses: {
    provider: 'Uses {{provider}}.',
    mixed: 'Uses every AI that has a key, mixed.',
    ask: 'Asks which AI before it starts.',
    oneOff: 'Uses {{provider}} – this time only.',
  },

  selection: {
    title: 'AI selection',
    intro:
      'Decides which AI takes over your actions: researching a new guide, completing a guide, adding a chapter, Story & Lore and translating guides.',
    modeGroup: 'How PlatinVault picks the AI',
    modes: {
      default: {
        label: 'Default AI',
        hint: 'One AI writes the whole guide, every chapter in the same style. You choose which one below.',
      },
      mixed: {
        label: 'Mixed',
        hint: 'The chapters of a guide are spread over every AI that has a key. That is faster, but the writing style may vary between chapters.',
      },
      ask: {
        label: 'Ask before every request',
        hint: 'Every AI action first asks which AI to use. You can see which AI still has quota.',
      },
    },
    listTitle: 'Your AIs',
    listHint: 'Only AIs with a saved key can be chosen. You enter the key below.',
    defaultRadio: 'Use {{provider}} as the default AI',
    defaultBadge: 'Default AI',
    reset: 'Reset',
    resetHint: 'Forgets the remembered problem of this AI. The next request tries it again.',
    resetFailed: 'Reset failed: {{reason}}',
    statusLoadFailed: 'The state of the AIs could not be loaded: {{reason}}',
    fallback: {
      label: 'Switch to the next AI automatically when there is a problem',
      hintOn:
        'If the chosen AI has used up its quota, its key is invalid or it cannot be reached, PlatinVault carries on with the next AI that has a key. You always get a notice about it.',
      hintOff: 'Without this option the action stops at such a problem. You see the reason and can pick another AI yourself.',
      hintAsk:
        'In the mode “Ask before every request” PlatinVault never switches on its own: after a problem you pick the next AI yourself. Only background tasks such as translating achievement texts carry on quietly and note it in the log.',
    },
  },

  chooser: {
    title: 'Which AI do you want to use?',
    intro: 'The action runs only with the AI you choose here.',
    groupLabel: 'AI for this action',
    unhealthy: '{{provider}}: {{state}}. You had better choose another AI.',
    lastUsed: 'Used last',
    start: 'Start with {{provider}}',
  },

  button: {
    menuLabel: 'Choose the AI for this action',
    menuDescription: 'Applies to this one action only. Your setting stays as it is.',
    standardEntry: 'Setting: {{what}}',
    mixedName: 'Mixed',
    noKeyName: 'No key',
    standardTag: 'Default',
  },

  progress: {
    title: {
      generate: 'Researching the guide …',
      plan: 'Analysing the game …',
      complete: 'Completing the guide …',
      expand: 'Researching the chapter …',
      lore: 'Researching Story & Lore …',
      retry: 'Researching the chapters again …',
      redo: 'Researching the chapter again …',
    },
    step: {
      starting: 'Starting …',
      facts: 'Collecting Steam facts and game info …',
      blueprint: 'Analysing the game and checking every topic …',
      chapters: '{{done}} of {{total}} chapters done – working on: {{title}} …',
      chaptersNoTitle: '{{done}} of {{total}} chapters done …',
      chapter: 'Researching the chapter “{{title}}” …',
      loreParts: '{{done}} of {{total}} parts done – working on: {{title}} …',
      loreNoTitle: '{{done}} of {{total}} parts done …',
      saving: 'Saving …',
    },
    elapsed: 'Running for {{time}}',
    general: 'The AI is researching in the background – depending on the game and the AI this can take a few minutes. You can leave the page open.',
    slow: 'This step is taking longer than usual – the AI seems to be slow right now. That is not an error, you can leave the page open.',
  },
  job: {
    unreachable:
      'The connection to the PlatinVault server was lost. Is it still running? As long as it runs, the research carries on and finished chapters are saved – reload the page later.',
  },

  notice: {
    switchedTitle: 'Different AI used',
    retryWith: 'Try again with {{provider}}',
  },

  // The guide builder: how many chapters the plan suggests for the chosen research scope
  planBudget: {
    note: 'Suggestion: {{suggested}} chapters – matching the scope “{{scope}}” ({{min}} to {{max}} chapters). Other fitting topics are not ticked; you can add them or untick what is ticked.',
    scope: { compact: 'Compact', detailed: 'Detailed', exhaustive: 'Very detailed' },
  },

  // The guide via the player's own AI subscription: copy the prompt into the chat, paste the answer back (nothing is sent to an AI)
  manual: {
    title: 'Guide with your AI subscription (no API key)',
    chapterTitle: 'Research a chapter with your AI subscription',
    intro: 'This uses your own AI subscription by hand – allowed, because you are the one sitting in the chat. PlatinVault never accesses it and sends nothing to an AI.',
    close: 'Close',
    entry: { builder: 'With your AI subscription (no API key)', chapter: 'With your AI subscription', all: 'All with my AI subscription' },
    step: { plan: 'Step 1 of 2 · Chapter list', chapters: 'Step 2 of 2 · Research the chapters', single: 'One chapter', list: 'Several chapters' },
    mode: { label: 'Approach', batch: 'All at once', single: 'One by one' },
    plan: {
      promptHeading: '1. Copy the prompt and paste it into the chat of your subscription',
      answerHeading: '2. Paste the answer of the chat here',
      skip: 'I choose the topics myself – no blueprint prompt',
      skipHint: 'You get the standard topic list; every ticked topic then gets a chapter prompt of its own.',
      next_one: 'Next: {{count}} chapter',
      next_other: 'Next: {{count}} chapters',
      noneChosen: 'Tick at least one chapter.',
    },
    chapters: {
      intro: 'Every chapter has a prompt of its own: copy it, paste it into the chat, paste the answer here and save. The order does not matter; chapters that are already saved stay saved.',
      progress: 'Saved: {{done}} of {{total}}',
      back: 'Back to the chapter list',
      finished: 'Done',
    },
    row: {
      copy: 'Copy prompt',
      file: 'Export as file',
      paste: 'Paste answer',
      hidePaste: 'Close input',
      done: 'Saved',
      dropped: 'Left out – not in this game',
      open: 'Open',
      save: 'Save chapter',
      saveFailed: 'The chapter could not be saved: {{reason}}',
      replaces: 'Replaces the “not researched” note',
    },
    // "All at once": one prompt for every chapter, one text that collects all answers, one save
    batch: {
      how: {
        heading: 'How it works',
        step1: 'Copy the prompt below and paste it into the chat of your AI subscription – ideally with web search switched on.',
        step2: 'Paste every answer of the chat here, or drop the file your chat created. Write “continue” in the chat until it says all chapters are done.',
        step3: 'Save all recognised chapters at once. If something is still missing, copy the prompt for the remaining chapters and carry on with it.',
      },
      promptHeading: 'Prompt for all chapters',
      promptInfo_one: '{{count}} chapter · prompt approx. {{size}} KB',
      promptInfo_other: '{{count}} chapters · prompt approx. {{size}} KB',
      promptInfoLoading: 'Creating the prompt …',
      checklist: {
        heading_one: 'Chapter ({{count}})',
        heading_other: 'Chapters ({{count}})',
        recognized_one: '{{found}} of {{count}} chapter recognised',
        recognized_other: '{{found}} of {{count}} chapters recognised',
      },
      status: { pending: 'Pending', ok: 'Recognised', warning: 'With notes', error: 'Error', notApplicable: 'Not in this game', saved: 'Saved' },
      row: { details: 'Show or hide details of “{{title}}”' },
      unmatched: {
        heading_one: 'Answers without a chapter ({{count}})',
        heading_other: 'Answers without a chapter ({{count}})',
        idle: 'Answers that cannot be matched to a chapter appear here.',
        none: 'Every answer found is matched to a chapter.',
        hint: 'They are not saved. Check whether the chat used the given chapter ids (K01, K02 …).',
        item: 'Answer {{n}}',
        itemTitled: 'Answer {{n}}: “{{title}}”',
      },
      answer: {
        heading: 'Answers of the chat',
        label: 'All pasted answers of the chat',
        placeholder: 'Paste every answer of the chat here – it is appended to what is already there. Text before it or a json code block is fine.',
        chooseFile: 'Choose file',
        clear: 'Clear pasted text',
        collected: '{{size}} characters collected – every further paste is appended.',
        empty: 'Nothing pasted yet. You can also drop .json, .md or .txt files here.',
        dropHint: 'Drop the file here',
        clearTitle: 'Clear the pasted text?',
        clearMessage: 'All collected text of the chat answers is discarded. Chapters that are already saved stay saved; for all others you have to paste the answers again.',
        clearConfirm: 'Clear',
      },
      panel: { found: 'Found in the text: {{found}} answers · not matched: {{unmatched}}' },
      skipped: { heading_one: 'One chapter was not saved', heading_other: '{{count}} chapters were not saved' },
      footer: {
        save_one: 'Save {{count}} chapter',
        save_other: 'Save {{count}} chapters',
        reasonChecking: 'Checking the answers …',
        reasonEmpty: 'No answer pasted yet – paste the answers of the chat first.',
        reasonNone: 'None of the answers can be saved – the reasons are shown at the chapters.',
        saveFailed: 'The chapters could not be saved: {{reason}}',
        restPrompt_one: 'Copy prompt for the remaining chapter',
        restPrompt_other: 'Copy prompt for the remaining {{count}} chapters',
        restLoading: 'Creating the prompt …',
        restDone: 'Copied',
        restCopied: 'Prompt for the remaining chapters copied – paste it into the chat.',
        restFailed: 'The prompt for the remaining chapters could not be created: {{reason}}',
      },
      allDone: 'All chapters are saved. You can close this window.',
    },
    copy: {
      copied: 'Prompt copied – paste it into the chat of your subscription, ideally with web search switched on.',
      selected: 'Your browser does not allow copying here. The prompt is selected below – press Ctrl+C (Cmd+C on a Mac).',
      exported: 'File saved: {{name}}',
      loading: 'Creating the prompt …',
      failed: 'The prompt could not be created: {{reason}}',
      preview: 'Show prompt',
      hidePreview: 'Hide prompt',
      promptLabel: 'The prompt',
      hint: 'Copies the finished prompt. Paste it into the chat of your subscription, ideally with web search switched on.',
    },
    // The answer contains sentences in another language (or other findings): a prompt asks the chat to correct exactly those
    fix: {
      button: 'Copy correction prompt',
      hint: 'Copies a prompt that makes the chat correct the answer (for example translate sentences in another language). Paste it into the same chat and replace the answer here with the new one.',
      copied: 'Correction prompt copied – paste it into the same chat and put the new answer in here.',
      selected: 'Your browser does not allow copying here. The prompt is selected below – press Ctrl+C (Cmd+C on a Mac).',
      promptLabel: 'The correction prompt',
    },
    answer: {
      label: 'Answer of the chat',
      placeholder: 'Paste the complete answer of the chat here – text before it or a json code block is fine.',
      loadFile: 'Load file',
      clear: 'Clear',
      checking: 'Checking …',
      empty: 'Nothing pasted yet.',
      ok: 'Answer read.',
      errors: 'This still needs attention',
      warnings: 'Notes',
      examples: 'Examples',
      fileFailed: 'The file could not be read: {{reason}}',
      checkFailed: 'The check failed: {{reason}}',
      preview: 'Preview: “{{title}}” – sections: {{sections}} · table rows: {{rows}} · cards: {{cards}}',
      planPreview: 'Chapter list read.',
    },
  },

  // A chapter whose research failed: calm block with a button that researches exactly this chapter again
  failedChapter: {
    title: 'This chapter could not be researched',
    hint: 'The topic of this chapter is saved – you do not need to copy anything. The button researches exactly this chapter again and replaces this note.',
    retry: 'Research this chapter again',
    allTitle_one: '{{count}} chapter could not be researched',
    allTitle_other: '{{count}} chapters could not be researched',
    allHint: 'Their topics are saved. Researching them all again only changes these chapters – finished chapters stay untouched.',
    retryAll: 'Research all failed chapters again',
    remove: 'Remove chapter',
    removeTitle: 'Remove the chapter “{{title}}”?',
    removeMessage: 'The chapter only holds this note, so no content is lost. If the game does not have this topic at all, this is the right choice. You can add it again later with “Complete the guide”.',
    removeFailed: 'The chapter could not be removed',
  },
  // "Remove chapter" for every chapter of a guide (the failed-research note above has its own, shorter question)
  removeChapter: {
    button: 'Remove chapter',
    title: 'Really remove the chapter “{{title}}”?',
    messageWhat: 'This removes the chapter, its translation and the check marks you set in its lists.',
    messageKept: 'Your Steam achievements and all other chapters are not affected. Removing a chapter cannot be undone.',
    messageAgain: 'With “Complete the guide” the AI can research missing topics again later.',
    doneTitle: 'Chapter removed',
    done: '“{{title}}” was removed from the guide.',
    failed: 'The chapter could not be removed',
  },
  // "Research chapter again": a normal chapter is researched again and replaced
  redoChapter: {
    button: 'Research chapter again',
    manualButton: 'Research again with my AI subscription',
    title: 'Research the chapter “{{title}}” again?',
    messageWhat: 'The chapter is replaced as soon as the new version is finished. Until then the old version stays.',
    messageKept: 'Check marks stay on every entry the new version still has. Entries that are dropped lose their check mark. Your Steam achievements and the content of the other chapters are not affected.',
    messageCost: 'The AI researches the chapter once more; this can take a moment and uses your quota.',
    messageManual: 'You get a prompt for the chat of your subscription. The chapter is only replaced once you paste the answer and save it.',
    confirm: 'Research again',
    confirmManual: 'Prepare prompt',
    doneTitle: 'Chapter researched again',
    done: '“{{title}}” was researched again and replaced.',
    failed: 'The chapter could not be researched again',
  },
  retryChapters: {
    failTitle: 'The chapters could not be researched again',
    doneTitle: 'Chapters researched',
    done_one: 'The chapter was researched again.',
    done_other: '{{count}} chapters were researched again.',
    partialTitle: 'Not every chapter could be researched',
    partial: '{{done}} of {{total}} chapters were researched. Still failed: {{titles}}. The reason is shown in each chapter.',
    dropped: 'Removed because this game does not have it: {{titles}}.',
  },

  blocks: {
    title: 'Research building blocks',
    intro:
      'Add ready-made building blocks to the AI research. Nothing chosen means the standard research: everything that fits the game, complete. The blocks apply to the blueprint and to every chapter, also for “Complete guide” and “Add chapter”. The chapter layout of the app always applies on top.',
    single: 'One choice',
    multiple: 'Several possible',
    optionalHint: 'Click again to remove the choice.',
    summaryNone: 'Nothing chosen: standard research.',
    summary_one: '{{count, number}} block chosen.',
    summary_other: '{{count, number}} blocks chosen.',
    clear: 'Clear all',
    scope: {
      group: 'Scope',
      compact: { label: 'Compact', description: 'Only the essentials per topic: short chapters, no complete lists.' },
      detailed: { label: 'Detailed', description: 'Complete lists and concrete tips for every topic, as usual for a 100 % guide.' },
      exhaustive: {
        label: 'Very detailed',
        description: 'Maximum depth: exact numbers, alternatives, special cases and step-by-step routes. Takes longer and costs more.',
      },
    },
    focus: {
      group: 'Focus',
      missables: { label: 'Missables first', description: 'Everything missable and every point of no return comes first, with the point in time and a save hint.' },
      collectibles: { label: 'Collectibles with locations', description: 'All collectibles and secrets, complete, with exact location and condition.' },
      builds: { label: 'Builds & equipment', description: 'Strong builds, weapons and armour with locations, prerequisites and what they are good for.' },
      bosses: { label: 'Boss strategies', description: 'For every boss and enemy type its weaknesses, phases and a concrete tactic.' },
      roadmap: { label: 'Trophy roadmap', description: 'A sensible order to 100 %, with playthroughs, time needed and stages.' },
      farming: { label: 'Farming routes', description: 'The fastest routes for money, experience and materials, with location, duration and yield.' },
      difficulty: { label: 'Difficulty tips', description: 'Which difficulty makes sense when, and tips for especially hard trophies.' },
      online: { label: 'Online/co-op trophies', description: 'Everything that needs multiplayer or co-op: prerequisites, finding partners, arrangements, server state.' },
    },
    spoilers: {
      group: 'Spoilers',
      none: { label: 'Spoiler-free', description: 'No story spoilers: no twists, endings or fates of characters.' },
      light: { label: 'Light spoilers', description: 'Only what is needed to find or reach something. Twists and endings stay secret.' },
      all: { label: 'Anything goes', description: 'Story details may be named in full.' },
    },
    presentation: {
      group: 'Presentation',
      bullets: { label: 'Bullet points', description: 'Short bullet points and short sentences, lists preferred over paragraphs.' },
      prose: { label: 'More running text', description: 'Connected, explanatory sentences and short paragraphs instead of bare keywords.' },
      tables: { label: 'Prefer tables', description: 'As much as possible as a tickable table with fitting columns, running text only for what is necessary.' },
    },
  },
} satisfies Translation<typeof de>

export default ai
