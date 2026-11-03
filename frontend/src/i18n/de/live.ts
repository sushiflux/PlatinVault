// Namespace "live": live trophies - the trophy list (four views, filters, detail and note dialogs), the popup, the platinum
// celebration, the tier pills. German source of truth - en/live.ts is typed against it.
const live = {
  // The four trophy tiers (Steam style); a tier id from the data becomes its name through t(`tiers.${tier}`)
  tiers: {
    Platinum: 'Platin',
    Gold: 'Gold',
    Silver: 'Silber',
    Bronze: 'Bronze',
  },
  // "Gold-Trophäe": popup headline, trophy artwork label, tier pill tooltip
  tierTrophy: '{{tier}}-Trophäe',
  tierPills: {
    unlocked: '{{achieved}} von {{total}} freigeschaltet',
  },
  celebration: {
    earned: 'Platin-Trophäe erhalten',
    ariaLabel: 'Platin-Trophäe erhalten: {{game}}',
    // The gap between the number and the percent sign is a no-break space (German writes "100 %")
    headline: '100 % – PLATIN',
    summaryGuide_one: 'Der Punkt im Guide ist abgehakt',
    summaryGuide_other: 'Alle {{count, number}} Punkte im Guide abgehakt',
    summaryAchievements_one: 'Der Erfolg ist freigeschaltet',
    summaryAchievements_other: 'Alle {{count, number}} Erfolge freigeschaltet',
    dismiss: 'Klicke irgendwo oder drücke Esc, um fortzufahren',
  },
  // Popup position "Vollbild": the whole window shows the unlocked achievement for a few seconds
  fullscreen: {
    unlocked: '{{tier}}-Trophäe freigeschaltet',
    // After the rarity step's name: "Extrem selten · 3,1 % der Spieler"
    share: '{{percent}} der Spieler',
    dismiss: 'Klicke irgendwo oder drücke Esc, um das Popup zu schließen',
  },
  tracker: {
    loadingName: 'Lade Steam-Erfolge ...',
    refresh: 'Steam-Erfolge jetzt aktualisieren',
    running: {
      label: 'Live aktiv',
      description: 'Das Spiel läuft gerade, die Trophäen werden laufend abgeglichen',
    },
    platinum: '100 % Platin',
    syncedGuide: '{{n}} im Guide abgehakt',
    playtime: 'Spielzeit {{time}}',
    // Playtime written out: "35 Min." / "12,5 Std."
    minutes: '{{n}} Min.',
    hours: '{{n}} Std.',
    filters: {
      statusGroup: 'Erfolge anzeigen',
      status: {
        all: 'Alle',
        needed: 'Noch benötigt',
        unlocked: 'Erhalten',
        hidden: 'Geheime',
      },
      tierGroup: 'Stufe filtern',
      tierAll: 'Alle',
    },
    search: {
      label: 'Erfolge durchsuchen',
      placeholderWithNotes: 'Name, Text oder Notiz suchen ...',
      placeholder: 'Name oder Text suchen ...',
    },
    sort: {
      label: 'Sortierung',
      default: 'Sortierung: Standard',
      rarityAsc: 'Seltenste zuerst (Gold → Bronze)',
      rarityDesc: 'Häufigste zuerst',
      recent: 'Zuletzt freigeschaltet',
      name: 'Alphabetisch (A–Z)',
    },
    reveal: {
      label: 'Geheime Erfolge aufdecken',
      on: 'An: die Beschreibungen sind sichtbar (Spoiler).',
      off: 'Aus: die Beschreibungen bleiben verdeckt.',
    },
    loading: 'Lade Live-Erfolge...',
    empty: 'Keine passenden Erfolge für diesen Filter gefunden.',
  },
  // The four sizes of the list, chosen with the slider
  density: {
    label: 'Ansicht',
    list: 'Liste',
    tiles: 'Kacheln',
    compact: 'Kompakt',
    large: 'Groß',
  },
  trophy: {
    noDescription: 'Keine Beschreibung verfügbar.',
    hiddenAchievement: 'Verstecktes Story-/Geheim-Achievement',
    hiddenAchievementOpen: 'Verstecktes Story-/Geheim-Achievement – anklicken zum Öffnen',
    secret: 'Geheim',
    reveal: 'Aufdecken',
    shareTooltip: '{{share}} aller Steam-Spieler haben diesen Erfolg',
    shareDescription: 'Seltenheit, nicht dein Fortschritt',
    ofPlayers: 'der Spieler',
    translated: {
      tooltip: 'Automatisch ins Deutsche übersetzt',
      badge: 'DE',
      showTranslation: 'Deutsche Übersetzung zeigen',
      showOriginal: 'Englisches Original zeigen',
    },
    howTo: 'So bekommst du ihn:',
    unlocked: 'Freigeschaltet',
    unlockedOn: 'Freigeschaltet am {{date}}',
    stillOpen: 'Noch offen',
    itemLabelUnlocked: '{{name}}, {{tier}}, freigeschaltet. Anklicken für Details',
    itemLabelOpen: '{{name}}, {{tier}}, noch offen. Anklicken für Details',
    progress: {
      label: 'Fortschritt',
      ariaLabel: 'Fortschritt {{current}} von {{max}}',
    },
    note: {
      change: 'Notiz ändern',
      add: 'Notiz hinzufügen',
    },
    // A personal note shown on a card
    noteQuote: '„{{note}}“',
    tip: {
      hide: 'Tipp ausblenden',
      show: 'Tipp anzeigen',
    },
    // Lines of the hover text of tiles and list rows
    detail: {
      shareLine: '{{tier}} · {{share}} der Spieler',
      howTo: 'So bekommst du ihn: {{tip}}',
      progress: 'Fortschritt: {{counter}}',
      note: 'Notiz: {{note}}',
    },
  },
  noteDialog: {
    title: 'Persönliche Notiz',
    placeholder: 'Eigene Tipps, fehlende Fundorte oder Fortschritts-Notizen...',
    save: 'Notiz speichern',
    saveFailed: 'Notiz konnte nicht gespeichert werden: {{reason}}',
    serverUnreachable: 'PlatinVault-Server nicht erreichbar – läuft das Backend?',
  },
  // A game was started on the gaming PC: the window switches to its page (or says why it did not)
  gameStart: {
    started: {
      title: '{{name}} wurde gestartet',
      message: 'Du bist nicht automatisch gewechselt, weil du hier gerade etwas bearbeitest.',
      action: 'Zum Spiel wechseln',
    },
    noGuide: {
      title: 'Du spielst gerade {{name}} – noch kein Guide vorhanden.',
    },
    // Stands for the name when it cannot be found out
    unknownGame: 'ein Spiel',
  },
  // The browser refused to play an achievement sound because the page has not been clicked since it was (re)loaded
  soundBlocked: {
    title: 'Ton vom Browser gesperrt',
    message:
      'Der Browser spielt Töne erst ab, nachdem du einmal auf PlatinVault geklickt oder eine Taste gedrückt hast – zum Beispiel nach dem Neuladen der Seite. Das letzte Achievement kam deshalb ohne Ton. Ein Klick hier oder irgendwo auf der Seite gibt den Ton wieder frei.',
    action: 'Verpassten Ton abspielen',
  },
} as const

export default live
