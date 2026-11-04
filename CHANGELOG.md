# Changelog

**[English](#english) · [Deutsch](#deutsch)**

---

<a name="english"></a>
## English

## v2026.11.18 (9.7263 β) · Unraid, Windows

### ✨ New

- **"In View" – pinned achievements:** pin up to 10 achievements per game with the pin button (in every trophy view and in
  the detail window). They stay in view on every tab of the game page – as a column on the right in a wide landscape window,
  as a bar at the bottom in portrait and on narrow screens (on phones it starts collapsed). Counters run live, a click opens the
  achievement with its guide tip and note. Unlocked ones show as done ("Remove completed") or leave on their own – your choice
  in Settings → Live & Sound. Browser and Windows program share the same pins.
- **Trophy pop-up duration:** choose in Settings → Live & Sound how long a new achievement stays on screen, from 2 to 10
  seconds (default 5). It applies everywhere – in the app, in full screen and in the overlay over your game.

### 🔄 Changed

- **Chapter achievements are always bronze:** achievements for completing a numbered story chapter ("Complete Chapter 5")
  count as bronze, however rare they are. The rarity label still shows the real worldwide share.
- **Worldwide shares from Steam are kept:** PlatinVault saves the last values it got, refreshes them at start and every hour,
  and keeps using them when Steam cannot be reached. The live trophies then show a copyable notice with the reason and the date
  of the saved values.

### 🔒 Security

- **The live channel only talks to PlatinVault itself:** other web pages you visit can no longer listen to the live events
  (running game, unlocks and your notes).
- **Docker / Unraid / NAS – protection against DNS rebinding:** PlatinVault now only answers to addresses a foreign web page
  cannot fake: IP addresses, `localhost`, names without a dot (e.g. `tower`) and names ending in `.local`, `.lan`, `.home.arpa`,
  `.internal` or `.fritz.box`. In a home network nothing needs to be set. **If you open PlatinVault under your own domain
  through a reverse proxy**, enter that name in the new optional setting `PLATINVAULT_ALLOWED_HOSTS` (Unraid template,
  advanced settings, or the compose file) – otherwise PlatinVault refuses with a message that names exactly what to enter.

### 🔧 Fix

- **Windows updates no longer fail with "being used by another process":** the installer leaves the program folder before
  replacing it, waits until the old program has really ended and tries again for a moment. If something still holds the files,
  the message names the program. This takes effect from this version's installer on.
- After a lost connection to Steam, the trophies no longer all show as silver for half an hour.

## v2026.11.17 (24.19071)

### ✨ New

- **Linux (experimental):** a native package for Linux gaming PCs – installs without admin rights in your home folder,
  runs as a background service, reads Steam itself and opens in the browser. For players whose PlatinVault runs on Unraid or
  a NAS there is now also a **PlatinVault Bridge for Linux** (Steam Deck included), downloadable ready to run from the app.
  Not yet tested on a real Linux Steam installation – feedback is welcome.
- **Steam folder (optional):** if Steam is installed in an unusual place, set the folder in Settings → Steam. The check shows
  every place PlatinVault looked at.
- **Own AI subscription – all at once:** instead of copying one prompt per chapter, you now get one prompt for all chapters
  and one field for all answers. PlatinVault checks every chapter, shows its status, saves them together and gives you a
  prompt for the chapters still missing. Text you pasted is kept when you close the window.
- **Research any chapter again:** with your AI or your own subscription. The old version stays until the new one is
  finished; check marks stay on every entry the new version still has.
- **Linked check marks:** an entry that appears in two chapters – for example an item in the walkthrough and its row in a
  collection list – shares one check mark. Tick it in one place and it is done in the other; links jump between them.
- **Guides like a printed strategy guide:** every walkthrough part starts with "At a glance" (recommended level, findings,
  achievements, missables), followed by the areas in order. A contents list jumps straight to a part or area. Every boss has
  its own card: location, recommended level, hit points (only with a source), weaknesses, resistances, immunities, dangerous
  attacks, phases, tactics and rewards. A "Point of no return" box lists what to do first, side quests appear compactly with a
  link to their own chapter, and every chapter ends with its sources.

### 🔄 Changed

- **Trophy tiers:** gold now means at most 10 % of players have the achievement (was 5 %), silver at most 50 % (was 25 %),
  bronze more than 50 %. More rare achievements now count as gold.
- **Research sources:** the AI first looks for a wiki made for that game, then for further sources (sources in your
  language first, English ones in addition); numbers are checked against a second source where possible.
- **Guide language:** a guide is written completely in its language and in its own words; original names appear at most
  once per chapter. With your own subscription PlatinVault warns about sentences in another language and offers a ready
  correction prompt.
- **Error messages can always be copied:** in the app, the Windows program, the installer and the Bridge every error message
  can be selected and has a "Copy" button that copies the message with its details, error code, version and time.
- **Code signing:** the Windows files are still unsigned, so the code signing policy has been removed from the README and the
  release pages for now.

### 🔧 Fix

- An AI answer with a single faulty field no longer discards the whole chapter – the readable parts are kept.
- When the database file cannot be opened (for example because an antivirus blocks it), PlatinVault says so in plain words
  instead of showing a technical database error.

## v2026.11.15 (27.4718)

### ✨ New

- **"What's new" window:** the release notes of a new version open in PlatinVault's own window – sorted into New, Changed,
  Fixed and more, in your language – instead of a link to GitHub.
- **Open folders (Windows program):** the tray menu and Settings → System → Folders open the program folder, the data
  folder, the backups and the logs with one click; every path can be copied.

### 🔄 Changed

- **Privacy at a glance:** the installer now says which connections PlatinVault makes by itself and how to switch them off.
  The README has a privacy policy and a code signing policy, and every release page links the code signing policy.
- **Release notes come from your PlatinVault:** the app window no longer contacts GitHub itself to show them.
- **Windows file properties** show the product name PlatinVault and the full version.
- **Handbook:** new section "Antivirus flags PlatinVault" – what to do when a virus scanner blocks or quarantines files.

### 🔒 Security

- **Prepared for signed Windows releases:** once the Windows files are signed, the program only installs an update whose
  installer carries a valid signature for PlatinVault – otherwise it refuses with a complete message. The signing
  application is still pending, so this release is not signed yet.

### 🔧 Fix

- **The installer repairs a damaged installation:** if an antivirus removed the program file, the installer no longer
  refuses with "the folder already contains other files". It repairs the installation, backs up your database first and
  keeps all your data.
- Installers downloaded for earlier updates are cleaned up automatically.

## v2026.11.14 (15.116022)

PlatinVault brings your Steam achievements and AI-researched 100 % guides together in one app. Every achievement you
unlock pops up live as a trophy – in the app and, with the Windows program, right over your game. For every game of your
Steam library an AI of your choice researches a complete guide; statistics and a trophy cabinet for your completed games
round it off. As a Windows program on your gaming PC or as a container on Unraid, Linux or a NAS, in German and English -
free and open source.

### ✨ New

- **Open source:** the complete source code is public under the GNU AGPL v3. The licence and a link to the source code
  are in the app under "Licences" and on the installer's licence page.
- **Windows program:** its own window in the PlatinVault look with its own title bar, a designed installer (per user, no
  admin rights), tray icon and start with Windows. The database is built in and backed up automatically – nothing to install
  or set up. Ctrl+Alt+G opens the guide over the game, Play starts games directly. Uninstalling asks whether to keep your
  data. The installer is not signed yet: Windows SmartScreen asks once ("More info" → "Run anyway").
- **Live trophies:** a new achievement appears within moments as an animated pop-up with sound – at one of nine positions
  or in full screen; reaching 100 % plays a platinum moment. The Windows program shows the same animation as an overlay over
  your game (borderless window mode); choose "only in the app", "only in the game" or "both" – the sound plays once. On a
  server the PlatinVault Bridge on your gaming PC reports new achievements (ready-to-run download in the app). After a short
  connection loss nothing gets lost, and each achievement appears exactly once.
- **German and English:** chosen at the first start, switchable at any time with the globe – the whole app, dates and
  numbers, Steam's achievement texts, the bridge and the handbook. Guides are translated by your AI; your ticks count in both
  languages.
- **Your Steam library:** sign in with Steam, bring games to the dashboard with the star, arrange them by drag and drop and
  sort by completion, name, playtime, last played, release, size, Metacritic or reviews. Every tile shows the same facts
  (including size on disk or the estimated install size) and three calm actions: Play, Favourite and Twitch – with the number
  of live streams once you have added Twitch access. A game that is running shows "Running".
- **Achievement list per game** in your language with rarity, counters, filters, search, your own notes and four views.
  When a game starts, PlatinVault switches to its page by itself (can be switched off).
- **AI guides:** researched chapter by chapter to fit the game – only topics the game really has, for Steam on PC. A
  complete walkthrough comes first when a reliable source exists, with "must have", "missable from here" and tips at the
  right spot; puzzles keep their solution hidden until you ask. Scope and building blocks set depth and focus, gaps are
  marked instead of invented. Research runs in the background with its real progress, and any chapter can be removed after a
  confirmation. Pick the AI per action or let PlatinVault switch when a quota runs out. Works with your own key for Google
  Gemini, Anthropic Claude or OpenAI – or with your own AI subscription via copy and paste.
- **Story & lore** per game, behind a spoiler lock.
- **Statistics:** trophies per day, play times by weekday and hour, rarity, a forecast to platinum, reviews, players online
  over time, your playtime calendar and the game's series.
- **Trophy cabinet:** every game at 100 % behind sliding glass doors with lighting and a golden plaque; new games move in
  with an animation, and the wall scrolls sideways.
- **Updates:** PlatinVault looks for new versions as often as you like (at every start up to monthly, or by hand with a click
  on the version number), and an icon in the header shows a new one. The Windows program installs it at a click, containers
  get it through their image tag (`latest`, or `beta` for betas). Betas only if you switch them on.
- **Unraid, Linux and NAS:** one container with the database built in (one file in the appdata folder), automatic backups
  and a restore script, a ready Docker Compose file for Linux and NAS; runs without root rights (PUID/PGID).
- **Comfort:** light and dark theme, adjustable display size, chapters in a side navigation grouped by topic (a drawer on the
  phone), complete error messages, hints on the first visit, handbook and licences inside the app, guide import and export.

---

<a name="deutsch"></a>
## Deutsch

## v2026.11.18 (9.7263 β) · Unraid, Windows

### ✨ Neu

- **„Im Blick“ – angeheftete Erfolge:** Hefte bis zu 10 Erfolge je Spiel mit der Pinnnadel an (in jeder Trophäen-Ansicht und
  im Detailfenster). Sie bleiben auf jedem Reiter der Spielseite im Blick – als Spalte rechts im breiten Querformat, als Leiste
  unten im Hochformat und auf schmalen Bildschirmen (auf dem Handy zunächst eingeklappt). Zähler laufen live mit, ein Klick
  öffnet den Erfolg mit Guide-Tipp und Notiz. Freigeschaltete stehen als geschafft da („Geschaffte entfernen“) oder gehen von
  selbst – das wählst du unter Einstellungen → Live & Sound. Browser und Windows-Programm teilen dieselben Favoriten.
- **Anzeigedauer der Trophäen-Popups:** Unter Einstellungen → Live & Sound stellst du ein, wie lange ein neuer Erfolg auf dem
  Bildschirm bleibt – 2 bis 10 Sekunden (Standard 5). Das gilt überall: in der App, im Vollbild und im Overlay über dem Spiel.

### 🔄 Geändert

- **Kapitel-Erfolge sind immer Bronze:** Erfolge für das Abschließen eines nummerierten Story-Kapitels („Schließe Kapitel 5
  ab.“) zählen als Bronze, egal wie selten sie sind. Die Seltenheitsangabe zeigt weiterhin den echten weltweiten Anteil.
- **Weltweite Anteile von Steam bleiben erhalten:** PlatinVault speichert die zuletzt geholten Werte, frischt sie beim Start
  und stündlich auf und nutzt sie weiter, wenn Steam nicht erreichbar ist. Die Live-Trophäen zeigen dann einen kopierbaren
  Hinweis mit Grund und Stand der gespeicherten Werte.

### 🔒 Sicherheit

- **Der Live-Kanal spricht nur noch mit PlatinVault selbst:** Andere Webseiten, die du besuchst, können die Live-Ereignisse
  (laufendes Spiel, Freischaltungen und deine Notizen) nicht mehr mithören.
- **Docker / Unraid / NAS – Schutz gegen DNS-Rebinding:** PlatinVault antwortet nur noch auf Adressen, die eine fremde
  Webseite nicht vortäuschen kann: IP-Adressen, `localhost`, Namen ohne Punkt (z. B. `tower`) und Namen auf `.local`, `.lan`,
  `.home.arpa`, `.internal` oder `.fritz.box`. Im Heimnetz musst du nichts einstellen. **Rufst du PlatinVault über einen
  Reverse-Proxy unter einer eigenen Domain auf**, trage diesen Namen in die neue, optionale Einstellung
  `PLATINVAULT_ALLOWED_HOSTS` ein (Unraid-Template, erweiterte Einstellungen, oder die Compose-Datei) – sonst lehnt PlatinVault
  mit einer Meldung ab, die genau sagt, was einzutragen ist.

### 🔧 Behoben

- **Windows-Updates scheitern nicht mehr mit „von einem anderen Prozess verwendet“:** Der Installer verlässt den
  Programmordner, bevor er ihn ersetzt, wartet, bis das alte Programm wirklich beendet ist, und versucht es einen Moment lang
  erneut. Hält trotzdem etwas die Dateien fest, nennt die Meldung das Programm. Das wirkt ab dem Installer dieser Version.
- Nach einer verlorenen Verbindung zu Steam stehen die Trophäen nicht mehr eine halbe Stunde lang alle auf Silber.

## v2026.11.17 (24.19071)

### ✨ Neu

- **Linux (experimentell):** ein eigenes Paket für Linux-Gaming-PCs – installiert sich ohne Administratorrechte in deinem
  Benutzerordner, läuft als Hintergrunddienst, liest Steam selbst und öffnet sich im Browser. Wer PlatinVault auf Unraid oder
  einem NAS betreibt, bekommt jetzt auch die **PlatinVault Bridge für Linux** (auch Steam Deck), fertig eingerichtet zum
  Herunterladen in der App. Noch nicht mit einem echten Linux-Steam geprüft – Rückmeldungen sind willkommen.
- **Steam-Ordner (optional):** Ist Steam an einem ungewöhnlichen Ort installiert, trägst du den Ordner unter Einstellungen →
  Steam ein. Die Prüfung zeigt jeden Ort, an dem PlatinVault gesucht hat.
- **Eigenes KI-Abo – alle auf einmal:** Statt für jedes Kapitel einen Prompt zu kopieren, bekommst du jetzt einen Prompt für
  alle Kapitel und ein Feld für alle Antworten. PlatinVault prüft jedes Kapitel, zeigt den Stand, speichert alle zusammen und
  gibt dir einen Prompt für die Kapitel, die noch fehlen. Eingefügter Text bleibt erhalten, wenn du das Fenster schließt.
- **Jedes Kapitel neu recherchieren:** mit deiner KI oder deinem eigenen Abo. Die alte Fassung bleibt stehen, bis die neue
  fertig ist; Häkchen bleiben bei allen Einträgen, die es in der neuen Fassung weiter gibt.
- **Verknüpfte Häkchen:** Ein Eintrag, der in zwei Kapiteln vorkommt – zum Beispiel ein Gegenstand in der Komplettlösung und
  seine Zeile in einer Sammelliste –, hat ein gemeinsames Häkchen. Hakst du ihn an einer Stelle ab, ist er an der anderen
  auch erledigt; Sprunglinks führen hin und her.
- **Guides wie ein gedrucktes Lösungsbuch:** Jeder Teil der Komplettlösung beginnt mit „Auf einen Blick“ (empfohlenes Level,
  Fundstücke, Erfolge, Verpassbares), danach kommen die Gebiete in Reihenfolge. Ein Inhaltsverzeichnis springt direkt zu
  Teil oder Gebiet. Jeder Boss hat eine eigene Karte: Ort, empfohlenes Level, Lebenspunkte (nur mit Quelle), Schwächen,
  Resistenzen, Immunitäten, gefährliche Angriffe, Phasen, Taktik und Belohnungen. Ein Kasten „Kein Zurück“ nennt, was du
  vorher erledigen solltest, Nebenaufträge stehen kompakt mit Link zu ihrem eigenen Kapitel, und jedes Kapitel endet mit
  seinen Quellen.

### 🔄 Geändert

- **Trophäen-Stufen:** Gold heißt jetzt, höchstens 10 % der Spieler haben den Erfolg (vorher 5 %), Silber höchstens 50 %
  (vorher 25 %), Bronze mehr als 50 %. Mehr seltene Erfolge zählen damit als Gold.
- **Quellen der Recherche:** Die KI sucht zuerst nach einem Wiki, das eigens für dieses Spiel gemacht ist, danach nach
  weiteren Quellen (zuerst in deiner Sprache, englische ergänzend); Zahlen werden möglichst mit einer zweiten Quelle
  abgeglichen.
- **Sprache der Guides:** Ein Guide ist ganz in seiner Sprache und in eigenen Worten geschrieben; Originalnamen stehen
  höchstens einmal je Kapitel. Mit dem eigenen Abo warnt PlatinVault vor Sätzen in einer anderen Sprache und bietet einen
  fertigen Korrektur-Prompt an.
- **Fehlermeldungen sind immer kopierbar:** In der App, im Windows-Programm, im Installer und in der Bridge lässt sich jede
  Fehlermeldung markieren und hat einen Knopf „Kopieren“, der Meldung, Details, Fehlercode, Version und Zeit kopiert.
- **Code-Signatur:** Die Windows-Dateien sind weiterhin nicht signiert; die Richtlinie zur Code-Signatur ist deshalb vorerst
  aus der README und den Release-Seiten entfernt.

### 🔧 Behoben

- Eine KI-Antwort mit einem einzelnen fehlerhaften Feld verwirft nicht mehr das ganze Kapitel – die lesbaren Teile bleiben.
- Lässt sich die Datenbankdatei nicht öffnen (zum Beispiel weil ein Virenschutz sie sperrt), sagt PlatinVault das in klaren
  Worten statt einer technischen Datenbank-Fehlermeldung.

## v2026.11.15 (27.4718)

### ✨ Neu

- **Fenster „Das ist neu“:** Die Release-Notizen einer neuen Version öffnen sich in einem eigenen PlatinVault-Fenster –
  sortiert nach Neu, Geändert, Behoben und mehr, in deiner Sprache – statt als Link zu GitHub.
- **Ordner öffnen (Windows-Programm):** Das Menü am Symbol neben der Uhr und Einstellungen → System → Ordner öffnen
  Programmordner, Datenordner, Sicherungen und Protokolle mit einem Klick; jeden Pfad kannst du kopieren.

### 🔄 Geändert

- **Datenschutz auf einen Blick:** Der Installer sagt jetzt, mit welchen Diensten PlatinVault von selbst Kontakt aufnimmt
  und wie du das abschaltest. Die README hat eine Datenschutzerklärung und eine Richtlinie zur Code-Signierung, jede
  Release-Seite verweist darauf.
- **Release-Notizen kommen von deinem PlatinVault:** Das App-Fenster fragt dafür nicht mehr selbst bei GitHub nach.
- **Dateieigenschaften unter Windows** zeigen den Produktnamen PlatinVault und die volle Version.
- **Handbuch:** neuer Abschnitt „Virenschutz meldet PlatinVault“ – was du tun kannst, wenn ein Virenscanner Dateien
  blockiert oder in Quarantäne verschiebt.

### 🔒 Sicherheit

- **Vorbereitet für signierte Windows-Versionen:** Sobald die Windows-Dateien signiert sind, installiert das Programm nur
  noch ein Update, dessen Installer eine gültige Signatur für PlatinVault trägt – sonst lehnt es mit einer vollständigen
  Meldung ab. Der Antrag für die Signatur läuft noch, diese Version ist deshalb noch nicht signiert.

### 🔧 Behoben

- **Der Installer repariert eine beschädigte Installation:** Hat ein Virenschutz die Programmdatei entfernt, lehnt der
  Installer nicht mehr mit „Im Ordner liegen schon andere Dateien“ ab. Er repariert die Installation, sichert vorher deine
  Datenbank und behält alle Daten.
- Installer, die für frühere Updates heruntergeladen wurden, werden automatisch aufgeräumt.

## v2026.11.14 (15.116022)

PlatinVault bringt deine Steam-Erfolge und KI-recherchierte 100%-Guides in eine App. Jeder Erfolg, den du freischaltest,
erscheint live als Trophäe – in der App und mit dem Windows-Programm direkt über deinem Spiel. Für jedes Spiel deiner
Steam-Bibliothek recherchiert eine KI deiner Wahl einen kompletten Guide; Statistiken und ein Trophäenschrank für deine
fertigen Spiele runden das ab. Als Windows-Programm auf deinem Gaming-PC oder als Container auf Unraid, Linux oder einem NAS,
auf Deutsch und Englisch – kostenlos und quelloffen.

### ✨ Neu

- **Open Source:** Der komplette Quellcode ist öffentlich, unter der GNU AGPL v3. Lizenz und Link zum Quellcode stehen in
  der App unter „Lizenzen“ und auf der Lizenzseite des Installers.
- **Windows-Programm:** eigenes Fenster im PlatinVault-Stil mit eigener Titelleiste, gestalteter Installer (pro Benutzer,
  ohne Adminrechte), Symbol neben der Uhr und Start mit Windows. Die Datenbank ist eingebaut und wird automatisch gesichert –
  nichts zu installieren oder einzurichten. Strg+Alt+G öffnet den Guide über dem Spiel, „Spielen“ startet Spiele direkt. Beim
  Deinstallieren fragt PlatinVault, ob deine Daten bleiben sollen. Der Installer ist noch nicht signiert: Windows SmartScreen
  fragt einmal nach („Weitere Informationen“ → „Trotzdem ausführen“).
- **Live-Trophäen:** Ein neuer Erfolg erscheint nach wenigen Augenblicken als animiertes Popup mit Klang – an einer von neun
  Positionen oder im Vollbild; bei 100 % gibt es den Platin-Moment. Das Windows-Programm zeigt dieselbe Animation als Overlay
  über deinem Spiel (randloser Fenstermodus); wähle „nur in der App“, „nur im Spiel“ oder „beides“ – der Ton erklingt einmal.
  Auf einem Server meldet die PlatinVault Bridge auf deinem Gaming-PC neue Erfolge (fertig eingerichtet als Download in der
  App). Nach einer kurzen Verbindungsunterbrechung geht nichts verloren, und jeder Erfolg erscheint genau einmal.
- **Deutsch und Englisch:** beim ersten Start gewählt, jederzeit über die Weltkugel umschaltbar – die ganze App, Datum und
  Zahlen, Steams Erfolgstexte, die Bridge und das Handbuch. Guides übersetzt deine KI; deine Häkchen gelten in beiden
  Sprachen.
- **Deine Steam-Bibliothek:** mit Steam anmelden, Spiele mit dem Stern aufs Dashboard holen, per Ziehen ordnen und nach
  Fortschritt, Name, Spielzeit, zuletzt gespielt, Erscheinung, Größe, Metacritic oder Rezensionen sortieren. Jede Kachel zeigt
  dieselben Angaben (auch die Größe auf der Platte bzw. die geschätzte Installationsgröße) und drei ruhige Aktionen: Spielen,
  Favorit und Twitch – mit der Zahl der Live-Streams, sobald du einen Twitch-Zugang eingetragen hast. Ein laufendes Spiel
  zeigt „Läuft“.
- **Erfolgsliste pro Spiel** in deiner Sprache mit Seltenheit, Zählern, Filtern, Suche, eigenen Notizen und vier Ansichten.
  Startet ein Spiel, wechselt PlatinVault von selbst auf seine Seite (abschaltbar).
- **KI-Guides:** Kapitel für Kapitel passend zum Spiel recherchiert – nur Themen, die es im Spiel wirklich gibt, für Steam
  am PC. Eine Komplettlösung steht ganz oben, wenn es eine belastbare Quelle gibt, mit „Unbedingt mitnehmen“, „Ab hier
  verpassbar“ und Tipps an der richtigen Stelle; Rätsel halten ihre Lösung verborgen, bis du sie willst. Umfang und Bausteine
  bestimmen Tiefe und Schwerpunkte, Lücken werden markiert statt erfunden. Die Recherche läuft im Hintergrund mit ihrem echten
  Fortschritt, und jedes Kapitel lässt sich nach einer Rückfrage entfernen. Wähle die KI pro Aktion oder lass PlatinVault bei
  aufgebrauchtem Kontingent wechseln. Mit deinem eigenen Key für Google Gemini, Anthropic Claude oder OpenAI – oder mit deinem
  eigenen KI-Abo per Kopieren und Einfügen.
- **Story & Lore** pro Spiel, hinter einer Spoiler-Sperre.
- **Statistiken:** Trophäen pro Tag, Spielzeiten nach Wochentag und Uhrzeit, Seltenheit, Hochrechnung bis Platin,
  Rezensionen, Spieler online im Verlauf, dein Spielzeit-Kalender und die Spielreihe.
- **Trophäenschrank:** jedes Spiel mit 100 % hinter gleitenden Glastüren mit Beleuchtung und goldener Plakette; neue Spiele
  ziehen mit einer Animation ein, und die Wand scrollt seitlich.
- **Updates:** PlatinVault sucht so oft nach neuen Versionen, wie du willst (bei jedem Start bis monatlich, oder von Hand per
  Klick auf die Versionsnummer), und ein Symbol in der Kopfzeile zeigt eine neue an. Das Windows-Programm installiert sie per
  Klick, Container bekommen sie über ihren Image-Tag (`latest`, oder `beta` für Betas). Betas nur, wenn du sie einschaltest.
- **Unraid, Linux und NAS:** ein Container mit eingebauter Datenbank (eine Datei im Appdata-Ordner), automatischen
  Sicherungen und einem Wiederherstellungs-Skript, eine fertige Docker-Compose-Datei für Linux und NAS; läuft ohne
  Root-Rechte (PUID/PGID).
- **Komfort:** helles und dunkles Farbschema, einstellbare Darstellungsgröße, Kapitel in einer Seitennavigation nach Themen
  gruppiert (auf dem Handy als Schublade), vollständige Fehlermeldungen, Hinweise beim ersten Besuch, Handbuch und Lizenzen
  direkt in der App, Guide-Import und -Export.
