// Namespace "onboarding": the first-run language screen, the intro tour and the page hints (coach marks). German source of
// truth - en/onboarding.ts is typed against it.
const onboarding = {
  // First-run language screen (shown once, before the tour). Both languages are shown at once, so the texts below exist in
  // German AND English and are read with a fixed language (see LanguageScreen).
  language: {
    kicker: 'Willkommen',
    title: 'Wähle deine Sprache',
    groupLabel: 'Sprache der App',
    optionHint: 'Oberfläche, Steam-Erfolge und Formate auf Deutsch',
    note: 'Gilt für die ganze App. Du kannst die Sprache jederzeit oben in der Kopfzeile umschalten (Weltkugel-Symbol).',
    continue: 'Weiter',
    saveFailed: 'Die Sprache konnte nicht gespeichert werden: {{reason}}',
  },

  tour: {
    ariaLabel: 'PlatinVault kennenlernen',
    stepOf: 'Schritt {{current}} von {{total}}',
    stepAnnouncement: 'Schritt {{current}} von {{total}}: {{title}}',
    skip: 'Überspringen',
    skipHint: 'Tour beenden',
    stepsNav: 'Schritte der Tour',
    stepDot: 'Schritt {{n}}: {{title}}',
    back: 'Zurück',
    next: 'Weiter',
    finish: 'Los geht’s',
  },

  steps: {
    welcome: {
      title: 'Willkommen bei PlatinVault',
      intro:
        'PlatinVault begleitet dich beim 100%-Durchgang: Es verfolgt deine Steam-Erfolge live, recherchiert zu jedem Spiel automatisch einen kompletten Guide und zeigt dir, wie weit du bist.',
      points: {
        live: { lead: 'Live-Trophäen', text: 'ein Trophäen-Popup mit Sound, sobald du einen Erfolg freischaltest.' },
        guides: { lead: 'Guides ohne Tippen', text: 'Waffen, Bosse, Verpassbares – von der KI recherchiert, mit Checklisten.' },
        stats: { lead: 'Statistiken', text: 'Spielzeit, Fortschritt und Seltenheit auf einen Blick.' },
      },
      visual: {
        library: '4 Spiele in der Bibliothek',
        points: '218 / 480 Punkte (45%)',
        platinum: '1 Spiel auf 100%',
        progress: '100%-Fortschritt',
        progressValue: '72 / 120 (60%)',
        popupExample: 'So sieht ein Live-Popup aus',
      },
    },
    games: {
      title: 'Spiele aufs Dashboard holen',
      intro:
        'Deine Spiele kommen aus deiner Steam-Bibliothek. Jedes Spiel mit Stern liegt auf dem Dashboard und bekommt automatisch Live-Trophäen und einen Guide.',
      points: {
        account: { lead: 'Steam-Konto', text: 'mit „Mit Steam anmelden“ verbinden.' },
        key: { lead: 'Web-API-Key', text: 'kostenlos bei Steam holen und in den Einstellungen eintragen.' },
        star: { lead: 'Stern', text: 'in „Alle Spiele“ antippen – den Guide erstellst du danach auf der Spielseite.' },
      },
      visual: {
        favorites: 'Favoriten',
        allGames: 'Alle Spiele',
        starRow: 'Stern = aufs Dashboard',
        genre: 'Action-RPG',
        tags: 'Open World · Bosse · Verpassbares',
        playtime: 'Spielzeit',
        playtimeValue: 'ca. 100 Std.',
        progress: '100%-Fortschritt',
        progressValue: '12 / 40 (30%)',
        caption: 'Live-Popups bei neuen Erfolgen und ein 100%-Guide mit Checklisten – ohne dass du etwas einstellen musst.',
      },
    },
    guide: {
      title: 'Der Guide',
      intro:
        'Der Guide entsteht automatisch – du musst nichts eintippen. Es gibt Kapitel zu den Standard-Themen, jedes mit abhakbaren Checklisten.',
      points: {
        chapters: { lead: 'Kapitel', text: 'Waffen, Rüstung, Taktik, Bestiarium, Level …' },
        checks: { lead: 'Häkchen zählen', text: 'dein Fortschritt wird gespeichert und auf der Startseite angezeigt.' },
        complete: { lead: 'Guide vervollständigen', text: 'recherchiert fehlende Standard-Themen nach.' },
        addTab: { lead: 'Tab ergänzen', text: 'ein Thema anklicken oder selbst beschreiben.' },
      },
      visual: {
        complete: 'Guide vervollständigen',
        addTab: 'Tab ergänzen',
        sampleChapters: 'Beispiel-Kapitel',
        chapterPanel: 'Kapitel {{title}}',
        topicsHeading: '„Tab ergänzen“: Thema einfach anklicken',
        tabs: {
          weapons: {
            title: 'Waffen',
            rows: {
              r1: { label: 'Langschwert aus der Kerkertruhe', hint: 'Fundort' },
              r2: { label: 'Verzauberte Axt', hint: 'Nebenquest' },
              r3: { label: 'Bogen des Wächters', hint: 'Händler' },
              r4: { label: 'Legendäre Klinge', hint: 'Endgame' },
            },
          },
          armor: {
            title: 'Rüstung',
            rows: {
              r1: { label: 'Ritterset komplett', hint: 'Set' },
              r2: { label: 'Schattenmantel', hint: 'Verpassbar' },
              r3: { label: 'Drachenschuppen-Harnisch', hint: 'Boss' },
              r4: { label: 'Verstärkte Handschuhe', hint: 'Schmied' },
            },
          },
          tactics: {
            title: 'Taktik',
            rows: {
              r1: { label: 'Boss 1: Phase 2 ausweichen', hint: 'Boss' },
              r2: { label: 'Boss 2: Schwachstelle am Rücken', hint: 'Boss' },
              r3: { label: 'Heilung vor Phase 3 sparen', hint: 'Tipp' },
              r4: { label: 'Fernkampf gegen Wächter', hint: 'Tipp' },
            },
          },
          bestiary: {
            title: 'Bestiarium',
            rows: {
              r1: { label: 'Skelettkrieger', hint: 'schwach: Blitz' },
              r2: { label: 'Moorhexe', hint: 'Fernkampf' },
              r3: { label: 'Steingolem', hint: 'Gelenke' },
              r4: { label: 'Schattenwolf', hint: 'schwach: Feuer' },
            },
          },
        },
        topics: {
          weapons: 'Waffen',
          armor: 'Rüstung',
          tactics: 'Taktik',
          bestiary: 'Bestiarium',
          levels: 'Level',
          collectibles: 'Sammelobjekte',
        },
      },
    },
    live: {
      title: 'Live-Trophäen',
      intro: 'Während du spielst, erkennt PlatinVault neue Steam-Erfolge in Echtzeit und blendet ein Trophäen-Popup mit Sound ein.',
      points: {
        tiers: { lead: 'Bronze, Silber, Gold', text: 'je seltener der Erfolg, desto höher die Stufe.' },
        platinum: { lead: 'Platin', text: 'sind alle Erfolge eines Spiels freigeschaltet, gibt es eine große Platin-Feier.' },
        try: { lead: 'Ausprobieren', text: 'probier es rechts aus. Das ist nur ein Beispiel, nichts wird gespeichert.' },
      },
      visual: {
        screenLabel: 'Dein Bildschirm: das Popup erscheint {{position}}',
        tierBronze: 'Bronze',
        tierSilver: 'Silber',
        tierGold: 'Gold',
        tierPlatinum: 'Platin',
        showPopup: 'Beispiel-Popup zeigen',
        showPlatinum: 'Platin-Feier ansehen',
        caption: 'Das Popup erscheint {{position}}. Position, Sound und Lautstärke stellst du in den Einstellungen ein.',
        exampleGame: 'Beispielspiel',
        positions: {
          'top-left': 'links oben',
          'top-center': 'oben mittig',
          'top-right': 'rechts oben',
          'center-left': 'links mittig',
          center: 'in der Bildschirmmitte',
          'center-right': 'rechts mittig',
          'bottom-left': 'links unten',
          'bottom-center': 'unten mittig',
          'bottom-right': 'rechts unten',
          fullscreen: 'im Vollbild',
        },
        // Example achievements of the mock popup
        demo: {
          bronze: { label: 'Ungewöhnlich', name: 'Erster Auftrag', description: 'Schließe den ersten Auftrag ab.' },
          silver: { label: 'Selten', name: 'Waffenmeister', description: 'Meistere jede Waffenart des Spiels.' },
          gold: { label: 'Extrem selten', name: 'Meister des Schicksals', description: 'Beende das Spiel auf dem höchsten Schwierigkeitsgrad.' },
        },
      },
    },
    stats: {
      title: 'Statistiken & Story',
      intro:
        'Zu jedem Spiel mit Steam-Anbindung gibt es einen Statistik-Reiter. Wer mehr über die Geschichte wissen will, kann zusätzlich Story & Lore einschalten.',
      points: {
        stats: { lead: 'Statistiken', text: 'Spielzeit, Fortschritt, seltenster Erfolg, Reviews und Neuigkeiten von Steam.' },
        lore: { lead: 'Story & Lore', text: 'optional, du schaltest es in den Einstellungen ein. Recherchiert wird erst auf Klick.' },
        spoiler: { lead: 'Spoiler-Schutz', text: 'die Story bleibt verdeckt, bis du sie bewusst öffnest.' },
      },
      visual: {
        playtime: 'Spielzeit gesamt',
        playtimeValue: '87 Std.',
        playtimeHint: 'Letzte 2 Wochen: 6 Std.',
        progress: 'Fortschritt',
        progressValue: '63 %',
        progressHint: '45 von 72 Erfolgen',
        rarest: 'Seltenster Erfolg',
        rarestValue: '1,8 %',
        rarestHint: 'Meister des Schicksals',
        players: 'Spieler gerade',
        playersValue: '54.210',
        playersHint: 'Live von Steam',
        loreTitle: 'Story & Lore',
        optional: 'Optional',
        loreExample:
          'Beispiel: Die Handlung wird einfach erklärt – mit Charakteren, ihren Beziehungen, der Welt und Hintergründen zur Entwicklung des Spiels.',
        spoilerWarning: 'Achtung: Story-Spoiler – verdeckt, bis du es öffnest.',
        hideAgain: 'Wieder verdecken',
        showSpoiler: 'Spoiler anzeigen',
      },
    },
    setup: {
      title: 'Einrichtung',
      intro:
        'Damit alles läuft, brauchst du nur ein paar Schlüssel. Die Liste rechts zeigt live, was schon erledigt ist – ein Klick öffnet die Einstellungen.',
      points: {
        ai: { lead: 'KI-Key', text: 'ohne ihn entstehen keine automatischen Guides. Gemini ist gratis (inklusive Google-Suche).' },
        steam: { lead: 'Steam', text: 'Konto verbinden und Web-API-Key eintragen – daraus kommt deine Bibliothek, und aus ihr deine Spiele.' },
        bridge: { lead: 'Bridge', text: 'nur für Unraid-Nutzer. Alles andere läuft ohne.' },
      },
      visual: {
        state: { done: 'erledigt', open: 'offen', loading: 'wird geprüft', unknown: 'unbekannt' },
        optional: 'optional',
        change: 'Ändern',
        steamAccount: {
          title: 'Steam-Konto verknüpft',
          linked: 'Verknüpft mit SteamID {{id}}',
          open: 'Damit PlatinVault weiß, wessen Erfolge es verfolgen soll.',
          actionLink: 'Verknüpfen',
        },
        steamKey: {
          title: 'Steam-Web-API-Key',
          saved: 'Hinterlegt ({{hint}})',
          open: 'Nötig, damit PlatinVault deine Steam-Bibliothek lesen kann – daraus kommen deine Spiele.',
          action: 'Eintragen',
        },
        aiKey: {
          title: 'KI-Key (Gemini, Claude oder ChatGPT)',
          saved: 'Hinterlegt – Guides werden automatisch recherchiert.',
          open: 'Nötig für die automatischen Guides. Die Recherche nutzt die Google-Suche – bei neuen Google-Konten nur mit Abrechnung.',
          link: 'Gratis-Key bei Google AI Studio holen',
          action: 'Eintragen',
        },
        bridge: {
          title: 'Echtzeit-Bridge',
          saved: 'Token vorhanden – die Bridge kann Erfolge melden.',
          open: 'Nur nötig, wenn PlatinVault auf Unraid läuft: Ein Mini-Programm auf dem Gaming-PC meldet Erfolge.',
          action: 'Einrichten',
        },
        igdb: {
          title: 'Spielfakten von IGDB',
          saved: 'Hinterlegt ({{hint}}) – Spielzeit bis 100 %, Wertungen und Genres auf den Karten.',
          open: 'Spielzeit bis 100 %, Wertungen und Genres. Braucht eine eigene, kostenlose Twitch-Entwickler-App.',
          action: 'Einrichten',
        },
      },
    },
  },

  // Coach marks: one hint per real control (data-hint attribute), in the order they are shown
  hints: {
    meta: 'Hinweis {{current}} von {{total}}',
    hide: 'Hinweise ausblenden',
    done: 'Verstanden',
    next: 'Weiter',
    intro: 'Ganze Einführung ansehen',
    handbook: 'Handbuch öffnen (PDF)',
    dashboardAdd: {
      title: 'Spiele aufs Dashboard holen',
      text: 'Deine Spiele kommen aus deiner Steam-Bibliothek: unter „Alle Spiele“ siehst du sie alle, mit dem Stern holst du ein Spiel aufs Dashboard.',
    },
    dashboardStar: {
      title: 'Deine Favoriten',
      text: 'Mit dem Stern entscheidest du, welche Spiele auf der Startseite liegen. Die Karten kannst du anfassen und selbst sortieren.',
    },
    dashboardSize: {
      title: 'Kachelgröße',
      text: 'Mit dem Regler zeigst du mehr oder größere Karten – bis hin zur reinen Textliste.',
    },
    dashboardCabinet: {
      title: 'Trophäenschrank',
      text: 'Alle Spiele, die du komplett auf 100 % hast, stehen hier im Schaukasten – mit Datum und Eckdaten.',
    },
    headerLanguage: {
      title: 'Sprache',
      text: 'Wechselt die Sprache der ganzen App – Oberfläche, Datums- und Zahlenformate und Steam-Texte. Die Wahl gilt sofort und wird gespeichert.',
    },
    headerHandbook: {
      title: 'Handbuch',
      text: 'Das ausführliche Handbuch als PDF – direkt hier in der App, auf Deutsch und Englisch.',
    },
    gameNav: {
      title: 'Kapitel',
      text: 'Hier wechselst du zwischen den Kapiteln des Guides. Deine Häkchen merkt sich PlatinVault.',
    },
    // The same hint on a phone (under 768 px), where the chapters are a list opened by the "Kapitel" button of the trophy bar instead of a panel
    gameNavPhone: {
      title: 'Kapitel',
      text: 'Dieser Knopf öffnet die Liste der Kapitel des Guides. Deine Häkchen merkt sich PlatinVault.',
    },
    gameLive: {
      title: 'Live-Trophäen',
      text: 'Alle Steam-Trophäen dieses Spiels mit Seltenheit. Neue Erfolge hakt PlatinVault automatisch im Guide ab.',
    },
    gameTwitch: {
      title: 'Live auf Twitch',
      text: 'Öffnet Twitch mit allen Streams zu diesem Spiel – ohne Konto, ohne Schlüssel.',
    },
    settings: {
      title: 'Einstellungen',
      text: 'Hier trägst du Steam-ID und Schlüssel ein, stellst Popups ein und schaltest Funktionen an oder aus.',
    },
  },
} as const

export default onboarding
