# Changelog

**[English](#english) · [Deutsch](#deutsch)**

---

<a name="english"></a>
## English

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
