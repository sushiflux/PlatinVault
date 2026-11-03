// Namespace "dashboard": German texts (source of truth) of the start page - header tools, hero, search and view switch, the game
// tiles, the empty-dashboard help and the sorting announcements. Add keys here first; src/i18n/en/dashboard.ts must get the same keys
// (TypeScript and vitest check it).
const dashboard = {
  // Game tile / card parts (also used by the intro tour)
  card: {
    guideChapters_one: '{{count}} Guide-Kapitel',
    guideChapters_other: '{{count}} Guide-Kapitel',
    platinum: 'Platin',
    trophies: 'Trophäen',
    trophyProgress: 'Trophäen-Fortschritt',
    noSteam: 'ohne Steam',
    noAchievements: 'keine Erfolge',
    addFavorite: '„{{title}}“ zu den Favoriten',
    removeFavorite: '„{{title}}“ aus den Favoriten nehmen',
    // The short words of the action bar's segments (the full sentences above are their tooltips)
    favoriteShort: 'Favorit',
    twitchShort: 'Twitch',
    movable: '{{title}}, verschiebbar',
    putOnDashboard: '„{{name}}“ aufs Dashboard',
    removeFromDashboard: '„{{name}}“ vom Dashboard nehmen',
    fallbackTitle: 'Karte',
  },
  // Playtime and "last played" on the cards
  time: {
    minutes: '{{value}} Min.',
    hours: '{{value}} Std.',
    // The bare unit and the "approximately" prefix, for the guide's free-text estimate (see normalizeEstimate)
    hoursUnit: 'Std.',
    today: 'heute',
    yesterday: 'gestern',
    daysAgo_one: 'vor {{count}} Tag',
    daysAgo_other: 'vor {{count}} Tagen',
  },
  // The fact rows of a large card (label = for screen readers, value = the visible line)
  facts: {
    // Shown (as the tooltip and for screen readers) where a fact row has no value: the row then holds its icon and a dash
    missing: {
      notPlayed: 'Noch nicht gespielt',
      steam: 'Keine Angabe von Steam',
      estimate: 'Keine Schätzung vorhanden',
      size: 'Größe unbekannt – PlatinVault liest sie aus den Steam-Dateien auf diesem PC (oder über die Bridge)',
    },
    played: { label: 'Gespielt', value: '{{time}} gespielt' },
    lastPlayed: { label: 'Zuletzt gespielt', value: 'Zuletzt gespielt: {{when}}' },
    rating: { label: 'Bewertung', review: '{{description}} ({{percent}})', percent: '{{percent}} positiv', metacritic: 'Metacritic {{score}}', igdb: 'IGDB {{score}}' },
    released: { label: 'Erschienen', value: 'Erschienen: {{date}}' },
    // Size: the real size on disk of an installed game; for a game that is not installed the install size estimated from Steam's data
    size: {
      label: 'Größe auf der Platte',
      onDisk: '{{size}} auf der Platte',
      installLabel: 'Installationsgröße',
      install: 'ca. {{size}} Installationsgröße',
      approx: 'ca. {{size}}',
      approxShort: '~{{size}}',
      installNote: 'Noch nicht installiert: geschätzt aus den Daten von Steam (Windows, Hauptsprache, ohne DLC). Auf der Platte kann es abweichen.',
    },
    duration: { label: 'Dauer für 100 %', average: '100 %: ca. {{time}}', estimate: '100 %: {{text}}', short: 'ca. {{time}}', approx: 'ca.' },
  },
  // Tile size slider
  tiles: {
    sizeLabel: 'Kachelgröße',
    size: {
      textOnly: 'Nur Text',
      list: 'Liste',
      veryCompact: 'Sehr kompakt',
      compact: 'Kompakt',
      large: 'Groß',
    },
  },
  // The three figure badges, the view switch and the compact bar under the header
  figures: {
    favorites_one: '{{count, number}} Favorit',
    favorites_other: '{{count, number}} Favoriten',
    library_one: '{{count, number}} Spiel in der Bibliothek',
    library_other: '{{count, number}} Spiele in der Bibliothek',
    libraryShort_one: '{{count, number}} Spiel',
    libraryShort_other: '{{count, number}} Spiele',
    trophies: '{{unlocked}} / {{total}} Trophäen ({{percent}})',
    trophiesShort: '{{unlocked}} / {{total}} ({{percent}})',
    platinum_one: '{{count, number}} Spiel auf Platin',
    platinum_other: '{{count, number}} Spiele auf Platin',
    platinumShort: '{{n}} Platin',
    scanning: 'Bibliothek wird ausgewertet: {{done}} / {{total}}',
  },
  controls: {
    viewGroup: 'Welche Spiele angezeigt werden',
    favorites: 'Favoriten ({{n}})',
    allGames: 'Alle Spiele ({{n}})',
    subBar: 'Schnellzugriff: Suche, Ansicht und Kennzahlen',
    searchPlaceholder: 'Spiel finden …',
    searchLabel: 'Spiel schnell finden',
    changeTileSize: 'Kachelgröße ändern',
  },
  // Header of the start page
  header: {
    logoAlt: 'PlatinVault Trophäe',
    // Shown instead of the server's branding tagline in English (the branding file has one text only)
    tagline: 'Live Steam-Achievements, Platin-Overlay & 100% Master-Guides.',
    overall: {
      label: 'Trophäen über alle Spiele',
      description: 'Freigeschaltete Steam-Trophäen aller Spiele deiner Sammlung',
      text: '{{unlocked}} / {{total}} Trophäen ({{percent}})',
      progress: 'Trophäen-Fortschritt aller Spiele',
    },
    importGroup: 'Guide importieren',
    importGuide: {
      label: 'Guide importieren',
      description: 'Guide-Datei einlesen (Export aus PlatinVault oder in Claude Code erstellt)',
    },
  },
  // Notice about the game that is running right now
  live: {
    title: 'Steam Live-Sync aktiv: „{{title}}“ läuft gerade!',
    text: 'Neue Erfolge werden in unter 400 ms als Trophäen-Popup eingeblendet und automatisch im Guide abgehakt.',
    open: 'Jetzt Spiel-Seite öffnen →',
  },
  // Hero banner with the search card
  hero: {
    title: 'Wähle dein Spiel für den 100%-Durchgang',
    searchLabel: 'Spiel schnell finden',
    searchPlaceholder: 'Titel oder Genre …',
    showingLibrary_one: 'Zeige {{shown}} von {{count, number}} Spiel',
    showingLibrary_other: 'Zeige {{shown}} von {{count, number}} Spielen',
    showingFavorites_one: 'Zeige {{shown}} von {{count, number}} Favorit',
    showingFavorites_other: 'Zeige {{shown}} von {{count, number}} Favoriten',
    resetFilter: 'Filter zurücksetzen',
    sortOff: {
      label: 'Sortieren ist gerade aus',
      description: 'Solange ein Filter aktiv ist, lässt sich die Reihenfolge nicht ändern – erst „Filter zurücksetzen“.',
      aria: 'Sortieren ist gerade aus, Filter aktiv',
    },
    sortOn: {
      label: 'Karten selbst sortieren',
      description: 'Karte anfassen und an die gewünschte Stelle ziehen (Touch: gedrückt halten, Tastatur: Leertaste, Pfeiltasten, Leertaste).',
      aria: 'Karten lassen sich per Ziehen sortieren',
    },
    sortOffOrder: {
      label: 'Ziehen ist aus',
      description: 'Karten lassen sich nur in der „Eigenen Reihenfolge“ ziehen – wähle sie im Sortier-Menü neben der Zählzeile.',
      aria: 'Ziehen ist aus, eine andere Sortierung ist gewählt',
    },
  },
  // The sort control of the overview panel (components/DashboardSortControl.tsx) and the sentence under the count line
  sort: {
    group: 'Sortierung',
    menu: 'Sortierung wählen',
    // Label of the menu button for screen readers
    buttonAria: 'Sortieren nach: {{order}}. Menü öffnen',
    orders: {
      custom: 'Eigene Reihenfolge',
      completion: 'Abschlussquote',
      alphabetical: 'Alphabetisch',
      playtime: 'Spielzeit',
      lastPlayed: 'Zuletzt gespielt',
      release: 'Erscheinungsdatum',
      size: 'Größe',
      metacritic: 'Metacritic-Wertung',
      reviews: 'Steam-Bewertungen',
    },
    // What each order measures, as the second line of its menu entry
    hints: {
      custom: 'So, wie du die Karten gezogen hast',
      completion: 'Freigeschaltete Steam-Erfolge in Prozent',
      alphabetical: 'Nach dem Titel',
      playtime: 'Gesamte Spielzeit',
      lastPlayed: 'Wann du zuletzt gespielt hast',
      release: 'Wann das Spiel erschienen ist',
      size: 'Größe auf der Platte – ohne Installation die geschätzte Installationsgröße',
      metacritic: 'Wertung der Fachpresse',
      reviews: 'Anteil positiver Steam-Rezensionen',
    },
    sentence: 'Sortiert: {{order}} – {{direction}}.',
    // The direction in words, per order (what comes first)
    directions: {
      custom: { asc: 'wie von dir gezogen', desc: 'wie von dir gezogen' },
      completion: { asc: 'niedrigste zuerst', desc: 'höchste zuerst' },
      alphabetical: { asc: 'A bis Z', desc: 'Z bis A' },
      playtime: { asc: 'wenigste Stunden zuerst', desc: 'meiste Stunden zuerst' },
      lastPlayed: { asc: 'am längsten her zuerst', desc: 'zuletzt gespielt zuerst' },
      release: { asc: 'älteste zuerst', desc: 'neueste zuerst' },
      size: { asc: 'kleinste zuerst', desc: 'größte zuerst' },
      metacritic: { asc: 'niedrigste zuerst', desc: 'höchste zuerst' },
      reviews: { asc: 'schlechteste zuerst', desc: 'beste zuerst' },
    },
    direction: {
      label: 'Richtung umkehren',
      description: 'Jetzt: {{direction}}. Ein Klick dreht die Reihenfolge um.',
      aria: 'Richtung umkehren, jetzt: {{direction}}',
      fixedLabel: 'Keine Richtung',
      fixedDescription: 'Die eigene Reihenfolge hat keine Richtung – sie ist so, wie du die Karten gezogen hast.',
      fixedAria: 'Die eigene Reihenfolge hat keine Richtung',
    },
    status: {
      pending: 'Die Werte werden noch geladen ({{done}} von {{total}}) – die Liste ergänzt sich von selbst.',
      pendingShort: 'Die Werte werden noch geladen – die Liste ergänzt sich von selbst.',
      noData: 'Dafür liegen noch keine Werte vor – alle Spiele stehen nach Titel geordnet.',
      noSize:
        'Größen liest PlatinVault nur aus den Steam-Dateien auf diesem PC oder über die Bridge – alle Spiele stehen nach Titel geordnet.',
      missing_one: '{{count}} Spiel ohne Wert steht am Ende.',
      missing_other: '{{count}} Spiele ohne Wert stehen am Ende.',
      missingSize_one: '{{count}} Spiel ohne Größenangabe steht am Ende.',
      missingSize_other: '{{count}} Spiele ohne Größenangabe stehen am Ende.',
    },
  },
  loading: {
    library: 'Lade deine Steam-Bibliothek …',
    games: 'Lade Spiele aus der Datenbank …',
  },
  // Spoken by screen readers while sorting the favourites
  drag: {
    instructions: 'Zum Sortieren die Leertaste drücken, dann mit den Pfeiltasten die Stelle wählen und mit der Leertaste ablegen. Escape bricht ab.',
    start: '„{{title}}“ aufgenommen, Position {{position}} von {{total}}. Mit den Pfeiltasten verschieben, mit der Leertaste ablegen.',
    over: '„{{title}}“ liegt über Position {{position}} von {{total}}.',
    drop: '„{{title}}“ an Position {{position}} von {{total}} abgelegt.',
    dropped: '„{{title}}“ abgelegt.',
    cancel: 'Verschieben abgebrochen, „{{title}}“ bleibt an Position {{position}}.',
  },
  // Results shown in the bottom notice or in the settings after the Steam sign-in
  notice: {
    unknown: 'Unbekannter Fehler',
    steamLinked: 'Steam-Konto verknüpft – deine SteamID wurde übernommen.',
    steamLinkFailed: 'Steam-Anmeldung fehlgeschlagen: {{reason}}',
    unknownReason: 'unbekannter Grund',
    // Titles of the floating notice (what failed); the reason follows as the notice's message
    reorderFailed: 'Die neue Reihenfolge konnte nicht gespeichert werden',
    favoriteFailed: 'Favorit für „{{name}}“ konnte nicht gespeichert werden',
    importInvalid: '„{{name}}“ ist keine gültige JSON-Datei.',
    importFailed: 'Import fehlgeschlagen',
    sortFailed: 'Die Sortierung konnte nicht gespeichert werden',
  },
  // "Spielen": starts a game on the gaming PC (bridge) or in the Steam client of this PC. Shared with the game page.
  play: {
    label: 'Spielen',
    viaBridge: 'Auf dem Gaming-PC starten',
    viaSteam: 'In Steam starten',
    aria: '„{{name}}“ spielen',
    running: {
      label: 'Spiel läuft',
      // On a segment of a card's action bar
      short: 'Läuft',
      viaBridge: 'Läuft gerade auf dem Gaming-PC',
      viaSteam: 'Läuft gerade auf deinem PC',
      aria: '„{{name}}“ läuft',
    },
    progress: {
      title: 'Starte „{{name}}“ auf dem Gaming-PC …',
      pending: 'Warte darauf, dass die Bridge den Befehl abholt.',
      delivered: 'Die Bridge hat den Befehl und startet das Spiel.',
    },
    done: {
      title: '„{{name}}“ wird gestartet',
      message: 'Der Start wurde an Windows auf dem Gaming-PC übergeben.',
      local: 'Steam startet das Spiel auf diesem PC.',
    },
    failed: { title: 'Start von „{{name}}“ fehlgeschlagen' },
    fallback: {
      failed: 'Das Spiel konnte nicht gestartet werden.',
      expired: 'Der Gaming-PC hat nicht rechtzeitig geantwortet.',
    },
  },
  // The calm help shown instead of the tiles while no game is on the dashboard: where the games come from and what to do first
  empty: {
    title: 'Dein Dashboard ist noch leer',
    intro: 'Deine Spiele kommen aus deiner Steam-Bibliothek. So holst du das erste aufs Dashboard:',
    stepsLabel: 'Die drei Schritte',
    state: { done: 'erledigt', open: 'offen', loading: 'wird geprüft' },
    account: {
      title: 'Steam-Konto verbinden',
      text: 'Damit PlatinVault weiß, welche Spiele und Erfolge zu dir gehören.',
      login: 'Mit Steam anmelden',
      change: 'Ändern',
    },
    key: {
      title: 'Steam-Web-API-Key eintragen',
      text: 'Mit dem kostenlosen Key liest PlatinVault deine Bibliothek, Spielzeiten und Erfolge.',
      link: 'Key bei Steam holen',
      action: 'Key eintragen',
      change: 'Ändern',
    },
    star: {
      title: 'Mit dem Stern aufs Dashboard holen',
      text: 'Öffne „Alle Spiele“ und tippe bei einem Spiel auf den Stern. Seinen Guide erstellst du danach auf der Spielseite.',
      action: 'Alle Spiele öffnen',
      locked: 'Sobald Konto und Key stehen, öffnet sich hier deine Bibliothek.',
    },
  },
} as const

export default dashboard
