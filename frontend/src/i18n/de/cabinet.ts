// Namespace "cabinet": German texts (source of truth) of the trophy cabinet page - the header figures, the display wall with its
// controls, the captions and the detail panel. Add keys here first; src/i18n/en/cabinet.ts must get the same keys (TypeScript and
// vitest check it).
const cabinet = {
  title: 'Trophäenschrank',
  subtitle: 'Alle deine Spiele auf 100 %',
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
  // The label under each figure in the header (the number stands above it)
  figure: {
    games_one: 'Spiel auf Platin',
    games_other: 'Spiele auf Platin',
    trophies_one: 'Trophäe',
    trophies_other: 'Trophäen',
    playtime: 'Spielzeit',
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
  // The view choices in the header: the lighting (a connected two-part bar) and the switch for the empty places
  view: {
    lighting: 'Beleuchtung',
    spots: { name: 'Spots', hint: 'Eine Lampe über jedem Spiel' },
    led: { name: 'LED', hint: 'Eine durchgehende Leiste je Regalbrett' },
    showEmpty: { name: 'Leere Plätze zeigen', hint: 'Dünn umrandete Platzhalter für die noch freien Plätze' },
  },
  // The glass doors of the cabinet (the button in the header)
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
