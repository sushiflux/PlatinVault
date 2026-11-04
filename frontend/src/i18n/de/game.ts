// Namespace "game": the game page - header tools, chapter bar, trophy bar, section tabs, guide content frame (lore gate,
// checklist tables), the dialogs of the page (guide builder, add chapter, Steam link), floating notices. German source of
// truth - en/game.ts is typed against it. Guide CONTENT (chapter texts, table cells) is data and stays as written.
const game = {
  loading: 'Lade 100%-Guide …',
  loadFailed: 'Spiel-Guide konnte nicht geladen werden.',
  backHome: 'Zurück zur Startseite',

  header: {
    home: {
      label: 'Zur Startseite',
      description: 'Zurück zur Spiele-Auswahl',
    },
    coverAlt: 'Trophäe',
    // After the game title, separated by a dash
    titleSuffix: '100% Master Companion',
    extendGroup: 'Guide erweitern',
    complete: {
      label: 'Guide vervollständigen',
      description: 'Guide-Baukasten: die KI prüft alle Standard-Themen (Waffen, Rüstung, Taktik, Bestiarium, Level …) und schlägt vor, was noch fehlt',
    },
    addTab: {
      label: 'Tab ergänzen',
      description: 'Ein einzelnes Kapitel ergänzen – Thema anklicken oder selbst beschreiben',
    },
    viewGroup: 'Ansicht, Export und Fortschritt',
    export: {
      label: 'Guide exportieren',
      description: 'Als Datei zum Teilen oder Sichern – ohne deinen Fortschritt',
    },
    print: {
      label: 'Als PDF drucken',
      description: 'Öffnet den Druckdialog, dort „Als PDF speichern“ wählen',
    },
    reset: {
      label: 'Fortschritt zurücksetzen',
      description: 'Entfernt alle Häkchen dieses Spiels',
    },
  },

  // Where the chapter bar sits: Auto follows the orientation of the window
  nav: {
    region: 'Kapitel-Navigation',
    title: 'Guide-Kapitel',
    expandSidebar: 'Seitenleiste ausklappen',
    collapseSidebar: 'Seitenleiste einklappen',
    // The button in the trophy bar that opens the chapters on a phone
    openChapters: 'Kapitel',
    openChaptersDescription: 'Die Guide-Kapitel öffnen',
    closeChapters: 'Kapitel schließen',
  },

  chapters: {
    listLabel: 'Guide-Kapitel',
    progress: '{{checked}} von {{total}} abgehakt',
    noItems: 'Keine Checklisten-Einträge',
    // Sub-headings of the chapter navigation; the groups stand in this fixed order
    groups: {
      start: 'Einstieg',
      story: 'Story & Welt',
      characters: 'Charaktere & Begleiter',
      combat: 'Kampf & Taktik',
      progress: 'Fortschritt & Freischaltungen',
      explore: 'Sammeln & Erkunden',
      economy: 'Wirtschaft & Handwerk',
      extras: 'Extras',
    },
  },

  // The areas next to the guide (right side of the trophy bar)
  sections: {
    groupLabel: 'Bereiche der Spielseite',
    live: {
      label: 'Live-Trophäen',
      fullLabel: 'Steam-Trophäen (Live)',
      description: 'Alle Trophäen dieses Spiels live von Steam, mit Seltenheit',
      descriptionWithNotes: 'Alle Trophäen dieses Spiels live von Steam, mit Seltenheit und eigenen Notizen',
    },
    stats: {
      label: 'Statistiken',
      fullLabel: 'Statistiken',
      description: 'Fortschritt, Seltenheit, Spielzeit und Verlauf zu diesem Spiel',
    },
    lore: {
      label: 'Story & Lore',
      fullLabel: 'Story & Lore',
      description: 'Handlung, Charaktere und Welt – enthält Spoiler und wird erst nach Nachfrage gezeigt',
    },
  },

  trophyBar: {
    region: 'Trophäen-Überblick und Bereiche',
    summary: '{{achieved}} / {{total}} Trophäen ({{percent}})',
    progress: 'Trophäen-Fortschritt',
  },
  progressBar: {
    label: 'Fortschritt',
  },

  notLinked: {
    title: 'Nicht mit Steam verknüpft.',
    text: 'Ohne Steam-AppID gibt es kein Cover, keine Live-Achievements und keine Statistiken.',
    button: 'Mit Steam verknüpfen',
  },
  noGuide: {
    text: 'Für dieses Spiel gibt es noch keinen Guide.',
    button: 'Erstes Kapitel per KI recherchieren',
  },
  hero: {
    title: '{{title}} — 100% Master Guide',
    openLive: 'Live Steam-Trophäen öffnen',
    backToOverview: 'Zurück zur Spiele-Übersicht',
  },
  // Shown above guide and lore content that is written in the other language than the interface
  contentLanguage: {
    hint: 'Dieser Guide ist auf {{language}} verfasst.',
    de: 'Deutsch',
    en: 'Englisch',
  },

  // A guide is written in one language; in the other UI language it is translated by the user's AI (the original is kept)
  translate: {
    // "{{language}}" after "Übersetze den Guide"
    into: { de: 'ins Deutsche', en: 'ins Englische' },
    // "aus {{language}}" in the hint above the guide
    from: { de: 'dem Deutschen', en: 'dem Englischen' },
    translatedFrom: 'Automatisch aus {{language}} übersetzt – das Original bleibt erhalten.',
    running: {
      title: 'Übersetze den Guide {{language}} …',
      message: 'Die KI übersetzt jedes Kapitel. Bis dahin siehst du das Original.',
      progress: 'Die KI übersetzt jedes Kapitel ({{translated}} von {{total}} fertig). Bis dahin siehst du das Original.',
    },
    failed: {
      title: 'Der Guide konnte nicht {{language}} übersetzt werden',
    },
    retry: 'Erneut versuchen',
  },

  lore: {
    none: 'Noch keine Story & Lore für dieses Spiel.',
    noneHint:
      'Die KI recherchiert die Story einfach erklärt, Charaktere und ihre Beziehungen, die Welt, Insiderwissen und die Entwicklungsgeschichte. Das Ergebnis enthält Spoiler und bleibt verdeckt, bis du es öffnest.',
    research: 'Story & Lore recherchieren',
    gateTitle: 'Achtung: Story-Spoiler',
    gateText: 'Dieser Reiter erklärt die komplette Handlung inklusive Wendungen und Ende, dazu Charaktere, Beziehungen und Hintergründe.',
    remember: 'Für dieses Spiel merken',
    reveal: 'Spoiler anzeigen',
    redo: {
      label: 'Story & Lore neu recherchieren',
      description: 'Ersetzt den Reiter durch eine frische Recherche',
      button: 'Neu recherchieren',
    },
  },
  spoilerSection: {
    fallbackTitle: 'Abschnitt',
    show: 'Spoiler anzeigen',
  },

  // Rätsel-Block im Guide: Ort und Voraussetzungen stehen offen, Tipps und Lösungsweg erscheinen erst auf Wunsch
  puzzle: {
    badge: 'Rätsel',
    missable: 'Verpassbar',
    where: 'Wo',
    requirements: 'Voraussetzung',
    done: 'Erledigt',
    doneFor: 'Rätsel „{{name}}“ als erledigt markieren',
    selfHint: 'Die Lösung bleibt verborgen, bis du sie anforderst – du kannst es auch erst selbst versuchen.',
    hint: {
      show: 'Tipp zeigen',
      next: 'Nächsten Tipp zeigen',
      hide: 'Tipps verbergen',
      title: 'Tipp {{shown}} von {{total}}',
    },
    solution: {
      show: 'Lösungsweg zeigen',
      hide: 'Lösungsweg verbergen',
      title: 'Lösungsweg',
      source: 'Quelle: {{source}}',
    },
    undocumented: {
      title: 'Lösung nicht dokumentiert',
      text: 'Für dieses Rätsel wurde keine belegte Lösung gefunden. Die Recherche erfindet nichts – ein vager Satz wäre keine Hilfe.',
    },
  },

  // Typisierte Hinweise in der Komplettlösung (Überschrift des Hinweises, Symbol und Farbe kommen aus dem Typ)
  hintKind: {
    must_have: 'Wichtig – unbedingt mitnehmen',
    missable: 'Verpassbar ab hier',
    optional: 'Optional hier verfügbar',
    tip: 'Tipp',
    achievement: 'Erfolg hier möglich',
    point_of_no_return: 'Point of no Return',
    side_quests: 'Nebenaufgaben hier verfügbar',
  },
  stepsList: 'Schritte',

  // Karten und Blöcke der Komplettlösung im Stil eines gedruckten Strategieführers (nur Text, keine Bilder)
  boss: {
    badge: 'Gegner',
    level: 'Empfohlene Stufe',
    hp: 'Lebenspunkte',
    hpUnknown: 'nicht dokumentiert',
    hpSource: 'Quelle: {{source}}',
    weaknesses: 'Schwächen',
    resistances: 'Resistenzen',
    immunities: 'Immunitäten',
    attacks: 'Gefährliche Angriffe',
    phases: 'Phasen',
    party: 'Empfohlene Gruppe und Ausrüstung',
    tactics: 'Taktik',
    rewards: 'Belohnungen',
    achievements: 'Erfolge',
    missable: 'Verpassbar',
    source: 'Quelle: {{source}}',
    location: 'Ort',
    nameless: 'Gegner',
  },
  overview: {
    title: 'Auf einen Blick',
    level: 'Empfohlene Stufe',
    counts: 'Das gibt es hier',
    achievements: 'Erfolge in diesem Teil',
    keyItems: 'Schlüsselgegenstände',
    missables: 'Verpassbar in diesem Teil',
    pointOfNoReturn: 'Dieser Teil endet an einem Point of no Return',
  },
  achievementRef: {
    unknown: 'Steam-Erfolg {{name}}',
    done: 'freigeschaltet',
  },
  pointOfNoReturn: { doFirst: 'Erledige das vorher' },
  // Kurzer Hinweis auf Nebenaufgaben mitten in der Route; die ganze Lösung steht im Nebenaufgaben-Kapitel
  itemLinks: {
    goToRow: 'Zu diesem Eintrag im Kapitel „{{chapter}}“ springen (derselbe Haken)',
    goToPlace: 'Fundort im Spiel: {{path}} – dorthin springen',
  },
  sideQuests: {
    doneFor: '{{quest}} erledigt',
    goTo: 'Alle im Kapitel „{{title}}“',
    open: '{{quest}} im Kapitel „{{chapter}}“ öffnen',
    from: 'ab {{from}}',
    missable: 'verpassbar',
    missableUntil: 'verpassbar bis {{until}}',
    reward: 'Belohnung: {{reward}}',
  },
  walkthrough: {
    part: 'Teil {{n}}',
    area: 'Gebiet {{label}}',
    toc: {
      label: 'Inhalt',
      nav: 'Inhalt der Komplettlösung',
      show: 'Inhalt anzeigen',
      hide: 'Inhalt ausblenden',
      count_one: '{{count}} Abschnitt',
      count_other: '{{count}} Abschnitte',
    },
  },
  sources: {
    title: 'Quellen',
    cited: 'zitiert',
    ai: 'von der KI genannt',
    search: 'aus der Suche',
    unknown: 'Herkunft unbekannt',
    external: 'öffnet im Browser',
  },


  // The frame the app puts around a guide table (the table's own texts are data and win when present)
  table: {
    searchPlaceholder: 'In Tabelle suchen...',
    categoryFilter: 'Kategorie filtern',
    allCategories: 'Alle Kategorien',
    secondaryFilter: 'Sekundärfilter',
    allFilters: 'Alle Filter',
    caption_one: '{{caption}} ({{count, number}} Eintrag)',
    caption_other: '{{caption}} ({{count, number}} Einträge)',
    done: 'Erledigt',
    doneFor: 'Erledigt: {{name}}',
  },

  // Titles of the notices when an action fails; the reason follows below them
  fail: {
    lore: 'Story & Lore konnte nicht recherchiert werden',
    check: 'Häkchen konnte nicht gespeichert werden',
    reset: 'Fortschritt konnte nicht zurückgesetzt werden',
    complete: 'Guide konnte nicht vervollständigt werden',
  },
  resetConfirm: {
    title: 'Häkchen zurücksetzen?',
    message: 'Alle abgehakten Einträge im Guide von „{{title}}“ werden entfernt. Deine Steam-Trophäen sind davon nicht betroffen.',
    confirm: 'Zurücksetzen',
  },
  notice: {
    details: 'Technische Details',
    aiSettings: 'Zu den KI-Einstellungen',
    completed: {
      title: 'Guide vervollständigt',
      added_one: '{{count, number}} Kapitel ergänzt: {{titles}}',
      added_other: '{{count, number}} Kapitel ergänzt: {{titles}}',
      // Appended when planned chapters were left out because the game does not have their topic
      dropped: 'Weggelassen, weil es das in diesem Spiel nicht gibt: {{titles}}.',
    },
    nothingToAdd: {
      title: 'Nichts zu ergänzen',
      message: 'Alle passenden Themen sind schon im Guide.',
    },
    steamLinked: {
      title: 'Mit Steam verknüpft',
      message: 'Cover, Live-Achievements und Statistiken sind jetzt aktiv.',
    },
  },

  expand: {
    title: 'Neuen Tab für „{{title}}“ per KI recherchieren',
    pickTopic: 'Thema anklicken',
    titleLabel: 'Titel des neuen Tabs *',
    titlePlaceholder: 'z.B. 8. Alle Geheimen Bosse & Fundorte',
    promptLabel: 'Was soll die KI im Internet für diesen Tab heraussuchen? *',
    promptPlaceholder: 'z.B. Liste alle geheimen Rüstungen, Accessoires und besten Materia-Kombinationen mit Fundorten und Taktiken auf.',
    missing: 'Bitte gib einen Tab-Titel und deine Frage / das Thema ein.',
    failedFallback: 'Fehler bei der KI-Recherche.',
    submit: 'Tab jetzt erstellen',
  },

  builder: {
    title: 'Guide-Baukasten · {{game}}',
    intro: 'Die KI hat das Spiel analysiert – passende Themen sind angehakt. Anpassen oder direkt recherchieren.',
    analysisFailed: 'Analyse fehlgeschlagen: {{reason}}',
    researchFailed: 'Recherche fehlgeschlagen: {{reason}}',
    genre: 'Spielart:',
    genreFromSteam: 'Aus den Steam-Community-Tags erkannt',
    genreFromAi: 'Von der KI erkannt',
    noGenre: 'keine eindeutige Zuordnung',
    groups: {
      recommended: 'Empfohlen',
      other: 'Weitere Themen',
      otherHint: 'Passen laut Analyse eher nicht – bei Bedarf trotzdem anhaken.',
      covered: 'Schon im Guide',
    },
    required: 'Typisch',
    all: 'Alles',
    onlyRecommended: 'Nur Empfohlene',
    research_one: '{{count, number}} Kapitel recherchieren',
    research_other: '{{count, number}} Kapitel recherchieren',
  },

  steamLink: {
    title: 'Mit Steam verknüpfen',
    text: 'Mit der Steam-AppID bekommt das Spiel das offizielle Cover, Live-Achievements und Statistiken.',
    searchLabel: 'Spielname im Steam Store',
    placeholder: 'Spielname im Steam Store suchen …',
    appId: 'AppID {{id}}',
    none: 'Keine Treffer im Steam Store.',
    searchFailed: 'Steam-Suche fehlgeschlagen: {{reason}}',
    linkFailed: '„{{name}}“ konnte nicht verknüpft werden: {{reason}}',
  },

  twitch: {
    label: 'Live auf Twitch',
    searchDescription: 'Öffnet die Twitch-Suche nach „{{name}}“ – dort siehst du, wer es gerade live spielt.',
    gameDescription: 'Öffnet Twitch mit allen Live-Streams zu „{{name}}“.',
    // The count of live streams on the button of a card (only with Twitch credentials)
    live_one: '{{count}} Stream live',
    live_other: '{{count}} Streams live',
    liveMany: 'Mehr als {{count}} Streams live',
    liveNone: 'Gerade streamt niemand dieses Spiel.',
  },
} as const

export default game
