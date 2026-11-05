# Beta Changelog (β)

**[English](#english) · [Deutsch](#deutsch)**

---

<a name="english"></a>
## English

## v2026.11.19 (3.1601 β)

### ✨ New

- **"What's new" after an update:** on the first start after an update, PlatinVault shows what is new in the version you
  now run – in your language, sorted into New, Changed, Fixed and more. One click closes it, and it does not come back for
  that version. You can open it again any time with "What's new in this version" in the version box at the bottom of the
  start page or in the chapter bar. The notes come with PlatinVault itself, so this works offline too.
- **Countdown on trophy pop-ups:** a thin bar in the trophy's colour runs down while a pop-up is on screen, so you can see
  how long it stays – in the app, in full screen and in the overlay over your game.
- **Unlock time in the pop-up:** the trophy pop-up now shows the date and time the achievement was unlocked.

### 🔧 Fix

- **Steam account box on the start page:** its bottom border no longer disappears when the mouse is over it.

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

---

<a name="deutsch"></a>
## Deutsch

## v2026.11.19 (3.1601 β)

### ✨ Neu

- **„Das ist neu“ nach einem Update:** Beim ersten Start nach einem Update zeigt PlatinVault, was in der Version neu ist, die
  jetzt läuft – in deiner Sprache, sortiert nach Neu, Geändert, Behoben und mehr. Ein Klick schließt das Fenster, für diese
  Version kommt es nicht wieder. Jederzeit erneut öffnen kannst du es mit „Das ist neu in dieser Version“ im Versions-Kasten
  unten auf der Startseite oder in der Kapitelleiste. Die Notizen werden mit PlatinVault mitgeliefert, das klappt also auch
  ohne Internet.
- **Restzeit bei Trophäen-Popups:** Ein dünner Balken in der Farbe der Trophäe läuft ab, solange ein Popup zu sehen ist – so
  siehst du, wie lange es noch bleibt. In der App, im Vollbild und im Overlay über dem Spiel.
- **Freischaltzeit im Popup:** Das Trophäen-Popup zeigt jetzt Datum und Uhrzeit, zu der der Erfolg freigeschaltet wurde.

### 🔧 Behoben

- **Steam-Konto-Kasten auf der Startseite:** Sein unterer Rand verschwindet nicht mehr, wenn die Maus darüber ist.

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

