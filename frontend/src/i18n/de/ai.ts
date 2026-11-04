// Namespace "ai": which AI does the work (selection modes, the chooser dialog, the "which AI" button, notices about a change of
// provider) and the research building blocks. German is the source of truth, the English file is typed against it.
const ai = {
  // Brand names: the same in every language
  providers: {
    gemini: 'Gemini',
    claude: 'Claude',
    openai: 'OpenAI',
  },
  state: {
    ok: 'Bereit',
    quota: 'Kontingent erschöpft',
    invalidKey: 'Key abgelehnt',
    unavailable: 'Nicht erreichbar',
  },
  stateSince: {
    quota: 'Kontingent erschöpft seit {{time}}',
    invalidKey: 'Key abgelehnt seit {{time}}',
    unavailable: 'Nicht erreichbar seit {{time}}',
  },
  noWebSearch: 'ohne Websuche',
  noWebSearchHint: 'PlatinVault ruft OpenAI ohne Websuche auf. Für die Guide-Recherche sind Gemini oder Claude besser geeignet.',
  noKey: 'Kein Key gespeichert',

  // What the AI buttons and their tooltips say about the AI that will be used
  uses: {
    provider: 'Nutzt {{provider}}.',
    mixed: 'Nutzt alle KIs mit Key, gemischt.',
    ask: 'Fragt vor dem Start, welche KI.',
    oneOff: 'Nutzt {{provider}} – nur dieses Mal.',
  },

  // Settings > AI research > "AI selection"
  selection: {
    title: 'KI-Auswahl',
    intro:
      'Legt fest, welche KI deine Aktionen übernimmt: neuen Guide recherchieren, Guide vervollständigen, Kapitel ergänzen, Story & Lore und das Übersetzen von Guides.',
    modeGroup: 'So wählt PlatinVault die KI',
    modes: {
      default: {
        label: 'Standard-KI',
        hint: 'Eine KI schreibt den ganzen Guide, alle Kapitel im selben Stil. Du wählst unten, welche.',
      },
      mixed: {
        label: 'Gemischt',
        hint: 'Die Kapitel eines Guides werden auf alle KIs mit Key verteilt. Das geht schneller, aber der Schreibstil kann zwischen den Kapiteln schwanken.',
      },
      ask: {
        label: 'Vor jeder Anfrage fragen',
        hint: 'Jede KI-Aktion fragt zuerst, welche KI sie benutzen soll. Du siehst dabei, welche KI gerade Kontingent hat.',
      },
    },
    listTitle: 'Deine KIs',
    listHint: 'Nur KIs mit gespeichertem Key sind wählbar. Den Key trägst du darunter ein.',
    defaultRadio: '{{provider}} als Standard-KI verwenden',
    defaultBadge: 'Standard-KI',
    reset: 'Zurücksetzen',
    resetHint: 'Vergisst den gemerkten Fehler dieser KI. Die nächste Anfrage probiert sie wieder.',
    resetFailed: 'Zurücksetzen fehlgeschlagen: {{reason}}',
    statusLoadFailed: 'Der Zustand der KIs konnte nicht geladen werden: {{reason}}',
    fallback: {
      label: 'Bei Problemen automatisch zur nächsten KI wechseln',
      hintOn:
        'Ist das Kontingent der gewählten KI aufgebraucht, der Key ungültig oder die KI nicht erreichbar, macht PlatinVault mit der nächsten KI mit Key weiter. Du siehst dazu immer einen Hinweis.',
      hintOff: 'Ohne diese Option bricht die Aktion bei einem solchen Problem ab. Du siehst den Grund und kannst selbst eine andere KI wählen.',
      hintAsk:
        'Im Modus „Vor jeder Anfrage fragen“ wechselt PlatinVault nie von selbst: Nach einem Problem wählst du die nächste KI selbst. Nur Hintergrundaufgaben wie das Übersetzen von Erfolgstexten wechseln still weiter und tragen es ins Protokoll ein.',
    },
  },

  // The dialog that asks which AI to use ("ask before every request")
  chooser: {
    title: 'Welche KI möchtest du benutzen?',
    intro: 'Die Aktion läuft nur mit der KI, die du hier wählst.',
    groupLabel: 'KI für diese Aktion',
    unhealthy: '{{provider}}: {{state}}. Am besten wählst du eine andere KI.',
    lastUsed: 'Zuletzt benutzt',
    start: 'Mit {{provider}} starten',
  },

  // The "which AI" part of AI buttons (split button)
  button: {
    menuLabel: 'KI für diese Aktion wählen',
    menuDescription: 'Gilt nur für diese eine Aktion. Deine Einstellung bleibt unverändert.',
    standardEntry: 'Einstellung: {{what}}',
    mixedName: 'Gemischt',
    noKeyName: 'Kein Key',
    standardTag: 'Standard',
  },

  // Live progress of the AI actions that run as background jobs (no fixed time is ever promised)
  progress: {
    title: {
      generate: 'Guide wird recherchiert …',
      plan: 'Das Spiel wird analysiert …',
      complete: 'Guide wird vervollständigt …',
      expand: 'Kapitel wird recherchiert …',
      lore: 'Story & Lore wird recherchiert …',
      retry: 'Kapitel werden erneut recherchiert …',
      redo: 'Kapitel wird neu recherchiert …',
    },
    step: {
      starting: 'Starte …',
      facts: 'Sammle Steam-Fakten und Spielinfos …',
      blueprint: 'Analysiere das Spiel und prüfe alle Themen …',
      chapters: '{{done}} von {{total}} Kapiteln fertig – gerade: {{title}} …',
      chaptersNoTitle: '{{done}} von {{total}} Kapiteln fertig …',
      chapter: 'Recherchiere das Kapitel „{{title}}“ …',
      loreParts: '{{done}} von {{total}} Teilen fertig – gerade: {{title}} …',
      loreNoTitle: '{{done}} von {{total}} Teilen fertig …',
      saving: 'Speichere …',
    },
    elapsed: 'Läuft seit {{time}}',
    general: 'Die KI recherchiert im Hintergrund – das kann je nach Spiel und KI einige Minuten dauern. Du kannst die Seite offen lassen.',
    slow: 'Dieser Schritt dauert länger als gewöhnlich – die KI ist gerade wohl langsam. Das ist kein Fehler, du kannst die Seite offen lassen.',
  },
  job: {
    unreachable:
      'Die Verbindung zum PlatinVault-Server ist abgebrochen. Läuft er noch? Solange er läuft, arbeitet die Recherche weiter und fertige Kapitel werden gespeichert – lade die Seite später neu.',
  },

  // Notices
  notice: {
    switchedTitle: 'Andere KI verwendet',
    retryWith: 'Mit {{provider}} erneut versuchen',
  },

  // The guide builder: how many chapters the plan suggests for the chosen research scope
  planBudget: {
    note: 'Vorschlag: {{suggested}} Kapitel – passend zum Umfang „{{scope}}“ ({{min}} bis {{max}} Kapitel). Weitere passende Themen sind nicht angehakt; du kannst sie dazunehmen oder Angehaktes abwählen.',
    scope: { compact: 'Kompakt', detailed: 'Ausführlich', exhaustive: 'Sehr ausführlich' },
  },

  // The guide via the player's own AI subscription: copy the prompt into the chat, paste the answer back (nothing is sent to an AI)
  manual: {
    title: 'Guide mit deinem KI-Abo (ohne API-Key)',
    chapterTitle: 'Kapitel mit deinem KI-Abo recherchieren',
    intro: 'Das nutzt dein eigenes KI-Abo von Hand – erlaubt, weil du selbst im Chat sitzt. PlatinVault greift nie darauf zu und schickt nichts an eine KI.',
    close: 'Schließen',
    entry: { builder: 'Mit deinem KI-Abo (ohne API-Key)', chapter: 'Mit deinem KI-Abo', all: 'Alle mit meinem KI-Abo' },
    step: { plan: 'Schritt 1 von 2 · Kapitelliste', chapters: 'Schritt 2 von 2 · Kapitel recherchieren', single: 'Ein Kapitel', list: 'Mehrere Kapitel' },
    mode: { label: 'Vorgehen', batch: 'Alle auf einmal', single: 'Einzeln' },
    plan: {
      promptHeading: '1. Prompt kopieren und im Chat deines Abos einfügen',
      answerHeading: '2. Antwort des Chats hier einfügen',
      skip: 'Ich wähle die Themen selbst – ohne Bauplan-Prompt',
      skipHint: 'Du bekommst die Standard-Themenliste; für jedes angehakte Thema gibt es danach einen eigenen Kapitel-Prompt.',
      next_one: 'Weiter: {{count}} Kapitel',
      next_other: 'Weiter: {{count}} Kapitel',
      noneChosen: 'Hake mindestens ein Kapitel an.',
    },
    chapters: {
      intro: 'Für jedes Kapitel gibt es einen eigenen Prompt: kopieren, im Chat einfügen, die Antwort hier einfügen und speichern. Die Reihenfolge ist egal; Kapitel, die schon gespeichert sind, bleiben es.',
      progress: 'Gespeichert: {{done}} von {{total}}',
      back: 'Zurück zur Kapitelliste',
      finished: 'Fertig',
    },
    row: {
      copy: 'Prompt kopieren',
      file: 'Als Datei exportieren',
      paste: 'Antwort einfügen',
      hidePaste: 'Eingabe schließen',
      done: 'Gespeichert',
      dropped: 'Entfällt – gibt es in diesem Spiel nicht',
      open: 'Offen',
      save: 'Kapitel speichern',
      saveFailed: 'Das Kapitel konnte nicht gespeichert werden: {{reason}}',
      replaces: 'Ersetzt den Hinweis „nicht recherchiert“',
    },
    // "All at once": one prompt for every chapter, one text that collects all answers, one save
    batch: {
      how: {
        heading: 'So geht es',
        step1: 'Kopiere den Prompt unten und füge ihn im Chat deines KI-Abos ein – am besten mit eingeschalteter Websuche.',
        step2: 'Füge hier jede Antwort des Chats ein, oder ziehe die Datei hinein, die dein Chat erstellt hat. Schreibe im Chat „weiter“, bis er meldet, dass alle Kapitel fertig sind.',
        step3: 'Speichere alle erkannten Kapitel auf einmal. Fehlt noch etwas, kopierst du den Prompt für die restlichen Kapitel und machst damit weiter.',
      },
      promptHeading: 'Prompt für alle Kapitel',
      promptInfo_one: '{{count}} Kapitel · Prompt ca. {{size}} KB',
      promptInfo_other: '{{count}} Kapitel · Prompt ca. {{size}} KB',
      promptInfoLoading: 'Erstelle den Prompt …',
      checklist: {
        heading_one: 'Kapitel ({{count}})',
        heading_other: 'Kapitel ({{count}})',
        recognized_one: '{{found}} von {{count}} Kapitel erkannt',
        recognized_other: '{{found}} von {{count}} Kapiteln erkannt',
      },
      status: { pending: 'Ausstehend', ok: 'Erkannt', warning: 'Mit Hinweisen', error: 'Fehler', notApplicable: 'Gibt es im Spiel nicht', saved: 'Gespeichert' },
      row: { details: 'Details zu „{{title}}“ ein- oder ausklappen' },
      unmatched: {
        heading_one: 'Nicht zuordenbare Antworten ({{count}})',
        heading_other: 'Nicht zuordenbare Antworten ({{count}})',
        idle: 'Antworten, die keinem Kapitel zugeordnet werden können, erscheinen hier.',
        none: 'Alle gefundenen Antworten sind einem Kapitel zugeordnet.',
        hint: 'Sie werden nicht gespeichert. Prüfe, ob der Chat die vorgegebene Kapitel-ID (K01, K02 …) verwendet hat.',
        item: 'Antwort {{n}}',
        itemTitled: 'Antwort {{n}}: „{{title}}“',
      },
      answer: {
        heading: 'Antworten des Chats',
        label: 'Alle eingefügten Antworten des Chats',
        placeholder: 'Hier jede Antwort des Chats einfügen – sie wird an das bisher Eingefügte angehängt. Text davor oder ein json-Codeblock stört nicht.',
        chooseFile: 'Datei wählen',
        clear: 'Eingefügtes leeren',
        collected: '{{size}} Zeichen gesammelt – jede weitere Einfügung wird angehängt.',
        empty: 'Noch nichts eingefügt. Du kannst auch .json-, .md- oder .txt-Dateien hineinziehen.',
        dropHint: 'Datei hier ablegen',
        clearTitle: 'Eingefügten Text leeren?',
        clearMessage: 'Der gesamte gesammelte Text der Chat-Antworten wird verworfen. Bereits gespeicherte Kapitel bleiben gespeichert; für alle anderen musst du die Antworten erneut einfügen.',
        clearConfirm: 'Leeren',
      },
      panel: { found: 'Im Text gefunden: {{found}} Antworten · davon nicht zuordenbar: {{unmatched}}' },
      skipped: { heading_one: 'Ein Kapitel wurde nicht gespeichert', heading_other: '{{count}} Kapitel wurden nicht gespeichert' },
      footer: {
        save_one: '{{count}} Kapitel speichern',
        save_other: '{{count}} Kapitel speichern',
        reasonChecking: 'Die Antworten werden geprüft …',
        reasonEmpty: 'Noch keine Antwort eingefügt – füge zuerst die Antworten des Chats ein.',
        reasonNone: 'Keine der Antworten ist speicherbar – die Gründe stehen bei den Kapiteln.',
        saveFailed: 'Die Kapitel konnten nicht gespeichert werden: {{reason}}',
        restPrompt_one: 'Prompt für das restliche Kapitel kopieren',
        restPrompt_other: 'Prompt für die restlichen {{count}} Kapitel kopieren',
        restLoading: 'Erstelle den Prompt …',
        restDone: 'Kopiert',
        restCopied: 'Prompt für die restlichen Kapitel kopiert – füge ihn im Chat ein.',
        restFailed: 'Der Prompt für die restlichen Kapitel konnte nicht erstellt werden: {{reason}}',
      },
      allDone: 'Alle Kapitel sind gespeichert. Du kannst das Fenster schließen.',
    },
    copy: {
      copied: 'Prompt kopiert – füge ihn im Chat deines Abos ein, am besten mit eingeschalteter Websuche.',
      selected: 'Dein Browser erlaubt das Kopieren hier nicht. Der Prompt ist unten markiert – drücke Strg+C (am Mac Cmd+C).',
      exported: 'Datei gespeichert: {{name}}',
      loading: 'Erstelle den Prompt …',
      failed: 'Der Prompt konnte nicht erstellt werden: {{reason}}',
      preview: 'Prompt anzeigen',
      hidePreview: 'Prompt ausblenden',
      promptLabel: 'Der Prompt',
      hint: 'Kopiert den fertigen Prompt. Füge ihn im Chat deines Abos ein, am besten mit eingeschalteter Websuche.',
    },
    // The answer contains sentences in another language (or other findings): a prompt asks the chat to correct exactly those
    fix: {
      button: 'Korrektur-Prompt kopieren',
      hint: 'Kopiert einen Prompt, mit dem der Chat die Antwort korrigiert (zum Beispiel fremdsprachige Sätze übersetzen). Füge ihn im selben Chat ein und ersetze die Antwort hier durch die neue.',
      copied: 'Korrektur-Prompt kopiert – füge ihn im selben Chat ein und setze die neue Antwort hier ein.',
      selected: 'Dein Browser erlaubt das Kopieren hier nicht. Der Prompt ist unten markiert – drücke Strg+C (am Mac Cmd+C).',
      promptLabel: 'Der Korrektur-Prompt',
    },
    answer: {
      label: 'Antwort des Chats',
      placeholder: 'Hier die komplette Antwort des Chats einfügen – auch mit Text davor oder in einem json-Codeblock.',
      loadFile: 'Datei laden',
      clear: 'Leeren',
      checking: 'Prüfe …',
      empty: 'Noch nichts eingefügt.',
      ok: 'Antwort gelesen.',
      errors: 'Das muss noch geklärt werden',
      warnings: 'Hinweise',
      examples: 'Beispiele',
      fileFailed: 'Die Datei konnte nicht gelesen werden: {{reason}}',
      checkFailed: 'Die Prüfung ist fehlgeschlagen: {{reason}}',
      preview: 'Vorschau: „{{title}}“ – Abschnitte: {{sections}} · Tabellenzeilen: {{rows}} · Karten: {{cards}}',
      planPreview: 'Kapitelliste gelesen.',
    },
  },

  // A chapter whose research failed: calm block with a button that researches exactly this chapter again
  failedChapter: {
    title: 'Dieses Kapitel konnte nicht recherchiert werden',
    hint: 'Das Thema dieses Kapitels ist gespeichert – du musst nichts kopieren. Der Knopf recherchiert genau dieses Kapitel noch einmal und ersetzt diesen Hinweis.',
    retry: 'Kapitel erneut recherchieren',
    allTitle_one: '{{count}} Kapitel konnte nicht recherchiert werden',
    allTitle_other: '{{count}} Kapitel konnten nicht recherchiert werden',
    allHint: 'Ihre Themen sind gespeichert. Alle noch einmal zu recherchieren ändert nur diese Kapitel – fertige Kapitel bleiben unberührt.',
    retryAll: 'Alle fehlgeschlagenen Kapitel erneut recherchieren',
    remove: 'Kapitel entfernen',
    removeTitle: 'Kapitel „{{title}}“ entfernen?',
    removeMessage: 'Das Kapitel enthält nur diesen Hinweis, es geht kein Inhalt verloren. Wenn das Spiel dieses Thema gar nicht hat, ist das die richtige Wahl. Über „Guide vervollständigen“ kannst du es später wieder anlegen.',
    removeFailed: 'Das Kapitel konnte nicht entfernt werden',
  },
  // "Kapitel entfernen" for every chapter of a guide (the failed-research note above has its own, shorter question)
  removeChapter: {
    button: 'Kapitel entfernen',
    title: 'Kapitel „{{title}}“ wirklich entfernen?',
    messageWhat: 'Entfernt werden dieses Kapitel, seine Übersetzung und die Häkchen, die du in seinen Listen gesetzt hast.',
    messageKept: 'Deine Steam-Erfolge und alle anderen Kapitel bleiben unberührt. Das Entfernen lässt sich nicht rückgängig machen.',
    messageAgain: 'Über „Guide vervollständigen“ kann die KI fehlende Themen später erneut recherchieren.',
    doneTitle: 'Kapitel entfernt',
    done: '„{{title}}“ wurde aus dem Guide entfernt.',
    failed: 'Das Kapitel konnte nicht entfernt werden',
  },
  // "Kapitel neu recherchieren": a normal chapter is researched again and replaced
  redoChapter: {
    button: 'Kapitel neu recherchieren',
    manualButton: 'Mit meinem KI-Abo neu recherchieren',
    title: 'Kapitel „{{title}}“ neu recherchieren?',
    messageWhat: 'Das Kapitel wird ersetzt, sobald die neue Fassung fertig ist. Bis dahin bleibt die alte Fassung stehen.',
    messageKept: 'Häkchen bleiben bei allen Einträgen, die es in der neuen Fassung weiter gibt. Einträge, die wegfallen, verlieren ihr Häkchen. Deine Steam-Erfolge und die Inhalte der anderen Kapitel bleiben unberührt.',
    messageCost: 'Die KI recherchiert das Kapitel noch einmal; das kann einen Moment dauern und verbraucht dein Kontingent.',
    messageManual: 'Du bekommst einen Prompt für den Chat deines Abos. Das Kapitel wird erst ersetzt, wenn du die Antwort einfügst und speicherst.',
    confirm: 'Neu recherchieren',
    confirmManual: 'Prompt vorbereiten',
    doneTitle: 'Kapitel neu recherchiert',
    done: '„{{title}}“ wurde neu recherchiert und ersetzt.',
    failed: 'Das Kapitel konnte nicht neu recherchiert werden',
  },
  retryChapters: {
    failTitle: 'Kapitel konnten nicht erneut recherchiert werden',
    doneTitle: 'Kapitel recherchiert',
    done_one: 'Das Kapitel wurde erneut recherchiert.',
    done_other: '{{count}} Kapitel wurden erneut recherchiert.',
    partialTitle: 'Nicht alle Kapitel konnten recherchiert werden',
    partial: '{{done}} von {{total}} Kapiteln wurden recherchiert. Weiter fehlgeschlagen: {{titles}}. Der Grund steht im jeweiligen Kapitel.',
    // Appended when the research now says the game does not have a chapter's topic (its placeholder was removed)
    dropped: 'Entfernt, weil es das in diesem Spiel nicht gibt: {{titles}}.',
  },

  // Settings > AI research > "Research building blocks"
  blocks: {
    title: 'Recherche-Bausteine',
    intro:
      'Ergänze die KI-Recherche mit fertigen Bausteinen. Nichts gewählt heißt: die Standard-Recherche, also alles, was zum Spiel passt, vollständig. Die Bausteine gelten für den Bauplan und jedes Kapitel, auch bei „Guide vervollständigen“ und „Kapitel ergänzen“. Der Kapitel-Aufbau der App gilt immer zusätzlich.',
    single: 'Eine Auswahl',
    multiple: 'Mehrere möglich',
    optionalHint: 'Erneut anklicken, um die Wahl zu entfernen.',
    summaryNone: 'Nichts gewählt: Standard-Recherche.',
    summary_one: '{{count, number}} Baustein gewählt.',
    summary_other: '{{count, number}} Bausteine gewählt.',
    clear: 'Alle abwählen',
    scope: {
      group: 'Umfang',
      compact: { label: 'Kompakt', description: 'Nur das Wichtigste je Thema: kurze Kapitel, keine vollständigen Listen.' },
      detailed: { label: 'Ausführlich', description: 'Vollständige Listen und konkrete Tipps zu jedem Thema, wie bei einem 100-%-Guide üblich.' },
      exhaustive: {
        label: 'Sehr ausführlich',
        description: 'Maximale Tiefe: genaue Zahlenwerte, Alternativen, Sonderfälle und Schritt-für-Schritt-Wege. Dauert länger und kostet mehr.',
      },
    },
    focus: {
      group: 'Schwerpunkte',
      missables: { label: 'Verpassbares zuerst', description: 'Alles Verpassbare und jeder Punkt ohne Rückweg steht ganz oben, mit Zeitpunkt und Speicher-Hinweis.' },
      collectibles: { label: 'Sammelobjekte mit Fundorten', description: 'Alle Sammelobjekte und Geheimnisse vollständig, mit genauem Fundort und Bedingung.' },
      builds: { label: 'Builds & Ausrüstung', description: 'Starke Builds, Waffen und Rüstungen samt Fundorten, Voraussetzungen und Einsatzzweck.' },
      bosses: { label: 'Boss-Strategien', description: 'Zu jedem Boss und Gegnertyp Schwächen, Phasen und eine konkrete Taktik.' },
      roadmap: { label: 'Trophäen-Roadmap', description: 'Eine sinnvolle Reihenfolge zu 100 %, mit Durchgängen, Zeitaufwand und Etappen.' },
      farming: { label: 'Farming-Routen', description: 'Die schnellsten Routen für Geld, Erfahrung und Materialien, mit Ort, Dauer und Ertrag.' },
      difficulty: { label: 'Schwierigkeitsgrad-Tipps', description: 'Welcher Schwierigkeitsgrad wann sinnvoll ist, und Tipps für besonders schwere Trophäen.' },
      online: { label: 'Online-/Koop-Trophäen', description: 'Alles, was Mehrspieler oder Koop braucht: Voraussetzungen, Partnersuche, Absprachen, Serverstand.' },
    },
    spoilers: {
      group: 'Spoiler',
      none: { label: 'Spoilerfrei', description: 'Keine Story-Spoiler: keine Wendungen, Enden oder Schicksale von Figuren.' },
      light: { label: 'Leichte Spoiler', description: 'Nur das, was nötig ist, um etwas zu finden oder zu erreichen. Wendungen und Enden bleiben geheim.' },
      all: { label: 'Alles erlaubt', description: 'Story-Hinweise dürfen vollständig genannt werden.' },
    },
    presentation: {
      group: 'Darstellung',
      bullets: { label: 'Stichpunkte', description: 'Knappe Stichpunkte und kurze Sätze, bevorzugt Listen statt Absätzen.' },
      prose: { label: 'Mehr Fließtext', description: 'Zusammenhängende, erklärende Sätze und kurze Absätze statt reiner Stichwörter.' },
      tables: { label: 'Tabellen bevorzugen', description: 'So viel wie möglich als abhakbare Tabelle mit passenden Spalten, Fließtext nur für das Nötigste.' },
    },
  },
} as const

export default ai
