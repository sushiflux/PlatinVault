// Namespace "cabinet": German texts (source of truth) of the trophy cabinet - the header button, the display case with its
// controls, the plaques and the detail panel. Add keys here first; src/i18n/en/cabinet.ts must get the same keys (TypeScript and
// vitest check it).
const cabinet = {
  // The button in the header tool row
  tool: {
    group: 'Sammlung',
    label: 'Trophäenschrank',
    description: 'Alle Spiele auf 100 % im Schaukasten, mit Eckdaten zu jedem Spiel',
  },
  title: 'Trophäenschrank',
  subtitle: 'Alle deine Spiele auf 100 %',
  close: 'Trophäenschrank schließen',
  // The three figures in the cabinet header
  stats: {
    games_one: '{{count, number}} Spiel auf Platin',
    games_other: '{{count, number}} Spiele auf Platin',
    gamesHint: 'So viele Spiele hast du komplett auf 100 % gebracht.',
    trophies_one: '{{count, number}} Trophäe',
    trophies_other: '{{count, number}} Trophäen',
    trophiesHint: 'Alle Trophäen dieser Spiele zusammen.',
    trophiesTotal_one: '{{count, number}} Trophäe insgesamt',
    trophiesTotal_other: '{{count, number}} Trophäen insgesamt',
    playtime: '{{time}} Spielzeit',
    playtimeHint: 'Gesamte Spielzeit in diesen Spielen.',
    playtimeTotal: '{{time}} Spielzeit insgesamt',
  },
  hours: '{{value}} Std.',
  // The size slider; steps 0..4 from left (smaller) to right (larger)
  size: {
    label: 'Größe im Schrank',
    description: 'Links kleiner, mehr Spiele pro Regalbrett – rechts größer.',
    step: {
      veryS: 'Sehr klein',
      small: 'Klein',
      medium: 'Mittel',
      large: 'Groß',
      veryL: 'Sehr groß',
    },
  },
  // Popover "View": lighting and empty places
  view: {
    label: 'Ansicht',
    description: 'Beleuchtung und leere Plätze',
    lighting: 'Beleuchtung',
    spots: { name: 'Spots', hint: 'Eine Lampe über jedem Spiel' },
    led: { name: 'LED-Leiste', hint: 'Eine durchgehende Leiste je Regalbrett' },
    showEmpty: { name: 'Leere Plätze zeigen', hint: 'Graue Platzhalter mit leerem Schild' },
  },
  doors: {
    close: { label: 'Türen schließen', description: 'Das Licht geht aus, die Glastüren schließen sich.' },
    open: { label: 'Türen öffnen', description: 'Die Glastüren öffnen sich, dann geht das Licht an.' },
    closedState: 'Türen geschlossen',
  },
  // Nameplate under each game
  plaque: {
    time: '{{time}} Uhr',
    stamp: 'Platin am {{date}} um {{time}}',
  },
  // Detail panel of the selected game
  info: {
    panel: 'Details zu {{name}}',
    close: 'Details schließen',
    platinumOn: 'Platin am {{date}}',
    trophies: 'Trophäen',
    unlocked: '{{n}} freigeschaltet',
    duration: 'Dauer',
    sameDay: 'am selben Tag auf 100 %',
    days_one: 'in {{count}} Tag auf 100 %',
    days_other: 'in {{count, number}} Tagen auf 100 %',
    playtime: 'Spielzeit',
    averageRarity: 'Ø Seltenheit pro Trophäe',
    ofPlayers: '{{percent}} der Spieler',
    rarest: 'Seltenste Trophäe',
    toGame: 'Zur Spielseite',
  },
  loading: 'Der Schrank wird eingeräumt …',
  error: {
    title: 'Der Trophäenschrank konnte nicht geladen werden',
    unknown: 'Unbekannter Fehler',
  },
  // The arrows at the edges of the shelves when there are more games than places
  scroll: {
    prev: 'Nach links blättern',
    next: 'Nach rechts blättern',
  },
  // New games that move into their place when the cabinet is opened
  presented: {
    badge: 'Neu',
    badgeLabel: 'Neu im Schrank',
    announce_one: 'Neu im Schrank: {{names}}',
    announce_other: 'Neu im Schrank: {{count}} Spiele, darunter {{names}}',
  },
  empty: {
    title: 'Noch ist der Schrank leer',
    text: 'Sobald du in einem Spiel alle Trophäen hast, bekommt es hier seinen Platz – mit Cover, Namensschild und allen Eckdaten.',
  },
} as const

export default cabinet
