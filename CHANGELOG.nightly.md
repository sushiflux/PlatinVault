# Nightly Changelog (α)

**[English](#english) · [Deutsch](#deutsch)**

---

<a name="english"></a>
## English

## v2026.11.26.7.3366 (α)

### ✨ New

- **Compact bar while scrolling:** when you scroll down in your favourites or in "All games", a slim bar with search,
  view, sorting and tile size fades in under the header, exactly in step with your scrolling. When you scroll back, it
  fades out the same way.
- **Three new sort orders:** IGDB critics, IGDB players and Overall rating. The overall rating is the average of all
  ratings a game shows on its tile; if it has only one, that one counts. Games without a rating stay at the end.

### 🔄 Changed

- **A "Back" button at the top of the game page** takes you to where you were before. You now reach live trophies and
  statistics through the bar "Guide | Live trophies | Statistics" in the game header; the navigation on the left only shows
  the guide chapters and Story & Lore.
- Switching chapters in the navigation no longer piles up in the history: "Back" (also the browser's back button) leaves
  the game page instead of going back chapter by chapter.
- Platinum games now carry a small golden medal at the end of their golden trophy line.
- The Twitch icon on the tiles now sits in the middle like the other icons, with the number of streams next to it.

### 🔧 Fix

- The Twitch number showed 0 for some games even though someone was streaming, and further down in "All games" there
  was often no number at all. Now every game gets its number as soon as you see it. If Twitch cannot find a game, there is
  no number instead of a wrong 0.
- At the bottom edge of the window the sort menu now opens upwards instead of opening invisibly below the screen.

## v2026.11.25.15.12074 (α)

### ✨ New

- **Before "Complete the guide", PlatinVault now always asks how you want to work:** with one of your AIs or with
  **My AI plan**. **Add chapter** and **Story & Lore** also offer **My AI plan** in the menu of their button.
- **Story & Lore with your own AI plan:** copy the prompt, paste it into your chat and paste the answer back into
  PlatinVault. A preview shows the parts it found, and PlatinVault tells you which ones are missing.
- If you chose **My AI plan** last time, it is preselected next time. If you pick an AI with a key again, PlatinVault
  forgets it.

### 🔄 Changed

- **The navigation on the left now only appears on a game page:** game header, live trophies, statistics, the guide
  chapters by group and Story & Lore. Overview, All games, Settings and "About PlatinVault" have no side bar and use the
  full width.
- **Everything else is in the top bar:** your Steam profile with picture and number of games, trophy cabinet, display
  size, language, colour scheme, settings, help, handbook and info. In narrow windows some icons move into the **More**
  menu.
- The trophy cabinet opens again as a full-screen window over the page you are on. Its header now matches the new design
  and shows platinum games, trophies and playtime.
- The gear opens the settings on the tab you used last.
- The active entry of the game navigation is marked more clearly with a wider line.
- Cards now stand out only by a lighter surface, without a frame. The whole interface uses the Inter typeface that comes
  with PlatinVault, also in the trophy pop-up over your game.
- **New rating strip on the game tiles, directly below the cover:** Steam reviews, Metacritic, IGDB critics and IGDB
  players. A missing rating is left out and the others share the space. The bar "Play | Favourite | Twitch" sits right
  below it; the former "Rating" row is gone.
- How many people are streaming on Twitch right now is shown as a number directly in the tile's Twitch field instead of a
  badge in the corner.
- Games at 100 % carry a platinum plaque with an occasional sparkle on the cover. Their trophy line turns into a golden
  line with the platinum icon and small stars.
- In Settings under **Live & sound**, every position tile shows a small preview of where the trophy pop-up appears.

### 🔧 Fix

- On the statistics page, "IGDB players" now really shows the rating of the IGDB players. Before, it showed a mixed value
  of players and critics.

## v2026.11.24.13.28980 (α)

### 🔄 Changed

- **New, light design:** fine lines instead of heavy frames and a calm typeface. Crystal blue shows what you can click and
  where you are; gold now belongs only to trophies and platinum.
- **A navigation on the left for everything:** Overview, All games, your favourite games and the trophy cabinet. The open
  game unfolds there and shows live trophies, statistics, all guide chapters by group and Story & Lore. The separate chapter
  bar of the game page is gone.
- The navigation can be collapsed to a slim icon bar, and PlatinVault remembers your choice. On a phone you open it with the
  menu icon at the top left.
- **Settings are now a page of their own**, which you open at the bottom of the navigation. The selected tab stays when you
  reload the page.
- Version, "What's new" and the licences are on the new page **About PlatinVault** behind the info icon at the bottom of the
  navigation. The page footer is gone.
- When you reload the page, the handbook, the licences and "What's new" stay open.
- The scrollbar now lies over the content and only appears when there is something to scroll, so the page always uses the
  full width.
- Buttons and icons are larger and easier to hit, and all texts are easy to read in the light and the dark theme.
- The first start welcomes you with a new page: on the left you choose language and colour scheme, on the right you see what
  PlatinVault can do.
- **PlatinVault now needs a processor with x86-64-v2 support**, which is practically every PC or server processor from about
  2013 onwards. This applies to Windows, Linux and the container on Unraid or a NAS.

## v2026.11.23.2.530 (α)

### 🔄 Changed

- **"All at once" with your own AI subscription now delivers every chapter in one answer:** the prompt asks the AI for one
  file with all chapters, which you simply drop into PlatinVault. If your chat cannot create files, all chapters come in a
  single code block. No more writing "continue" and pasting again and again.
- If not everything fits into one answer, the chat stops after a complete chapter and reports **STILL OPEN** with the
  missing chapters. You get the rest with **Copy prompt for the remaining chapters**.

### 🔧 Fix

- When you pasted the answers, a chapter could disappear without any message if a table contained "…" or the AI had kept
  the example icon from the prompt. Such chapters are now recognised.

## v2026.11.22.1.2206 (α)

### 🔄 Changed

- **One update channel instead of two switches:** under Settings → System → Updates you now pick your channel with a single
  choice – **None**, **Beta** or **Nightly**. None gives you finished releases only, Beta gives you betas (β) and releases,
  Nightly gives you nightlies (α) and releases. If you had switched on one of the old switches, your choice is carried over
  automatically (Nightly if "nightly" was on, otherwise Beta if "beta" was on, otherwise None). Please note: Nightly no longer
  includes betas; if you want those too, choose Beta.
- **Container tags follow the same rule:** a beta no longer moves the `nightly` image tag. `nightly` now carries nightlies and
  releases, `beta` carries betas and releases, `latest` carries releases only. The Unraid template descriptions and the example
  Compose file explain which tag goes with which channel.
- **Version numbers now show all five parts:** a version is written as `v2026.11.22.8.105` for a release, with **(β)** for a beta
  and **(α)** for a nightly after it, for example `v2026.11.22.8.105 (α)`, in the app, in the info card, in the installer and in
  the release titles. Earlier "What’s new" entries written in the old style are still found after an update.

### 🔒 Security

- **A new release is no longer hidden behind many nightlies:** the update check now reads up to 100 releases from the feed
  instead of 30, so a new final release is still found even when many nightlies were published after it.
- **A strange version tag no longer breaks the update check:** an unusual or malformed version tag or release entry in the
  update feed is now ignored instead of making the check fail.
- **Download file names from the feed are checked:** only plain file names (letters, digits, dot, hyphen, underscore) are
  accepted for installers and Linux packages.

## v2026.11.21 (8.105 α)

### 🔄 Changed

- **Calmer, smoother full-screen trophy pop-ups:** the full-screen trophy overlay now glides in more swiftly and harmoniously (~1.2 s) so that the finished trophy remains visible in all its detail for ~3.5 s. Before the chosen display duration runs out, a gentle 450 ms fade-out buffer softly darkens the screen and countdown bar together without clipping or abrupt cuts.
- **Hardware-accelerated exit animations:** outer backdrop blur and inner trophy stage now share synchronized easing curves and compositing hints, eliminating GPU stutter during the fade-out on high-refresh and 4K displays.

## v2026.11.20 (10.1884 α)

### ✨ New

- **Nightly channel (α):** in Settings → System → Updates you can now switch on **Receive nightly versions (α)** in addition to
  **Receive beta versions**. Nightly versions carry an **α** after the version number and deliver the newest changes straight
  from ongoing development, before they move to the beta channel (β) or the final release. Containers can follow this channel
  with the image tag `ghcr.io/sushiflux/platinvault:nightly`.

### 🔄 Changed

- **More room for chapters in the sidebar:** the bottom of the chapter sidebar on a game page now always uses the compact info
  icon ("i" in a circle), even in a tall window. Clicking or hovering it opens the info card with the version (click for
  **Version & updates** and **What’s new in this version**) and **Licences**.
- **Separate changelogs per channel:** release notes are now kept in dedicated files for final releases, betas (β) and
  nightlies (α), all included with PlatinVault for offline reading.

### 🔧 Fix

- **Smooth countdown bar on full-screen trophy pop-ups:** the countdown bar at the bottom of a full-screen trophy pop-up now
  glides smoothly and evenly from full to empty over the chosen display duration, with a subtle light point at its leading edge.

---

<a name="deutsch"></a>
## Deutsch

## v2026.11.26.7.3366 (α)

### ✨ Neu

- **Kompakte Leiste beim Scrollen:** Scrollst du in den Favoriten oder in „Alle Spiele“ nach unten, blendet sich unter der
  Kopfzeile eine schmale Leiste mit Suche, Ansicht, Sortierung und Kachelgröße ein, genau im Takt deines Scrollens.
  Scrollst du zurück, verblasst sie genauso.
- **Drei neue Sortierungen:** IGDB-Kritiker, IGDB-Spieler und Gesamtwertung. Die Gesamtwertung ist der Durchschnitt aller
  Bewertungen, die ein Spiel auf seiner Kachel zeigt; hat es nur eine, zählt diese. Spiele ohne Bewertung stehen am Ende.

### 🔄 Geändert

- **Auf der Spielseite gibt es oben einen Knopf „Zurück“**, der dich dorthin bringt, wo du vorher warst. Live-Trophäen und
  Statistiken erreichst du jetzt über die Leiste „Guide | Live-Trophäen | Statistiken“ im Kopf der Spielseite; die
  Navigation links zeigt nur noch die Guide-Kapitel und Story & Lore.
- Kapitelwechsel in der Navigation landen nicht mehr einzeln im Verlauf: „Zurück“ (auch der Zurück-Knopf des Browsers)
  verlässt die Spielseite, statt Kapitel für Kapitel zurückzugehen.
- Platin-Spiele tragen am Ende ihrer goldenen Trophäenlinie jetzt einen kleinen goldenen Orden.
- Das Twitch-Symbol sitzt auf den Kacheln jetzt in der Mitte wie die anderen Symbole, die Zahl der Streams steht daneben.

### 🔧 Behoben

- Die Twitch-Zahl zeigte bei manchen Spielen 0, obwohl gerade gestreamt wurde, und weiter unten in „Alle Spiele“ kam oft
  gar keine Zahl. Jetzt bekommt jedes Spiel seine Zahl, sobald du es siehst. Findet Twitch ein Spiel nicht, steht dort
  keine Zahl statt einer falschen 0.
- Das Sortiermenü öffnet sich am unteren Fensterrand jetzt nach oben, statt unsichtbar unter dem Bildschirm aufzugehen.

## v2026.11.25.15.12074 (α)

### ✨ Neu

- **Vor „Guide vervollständigen“ fragt PlatinVault jetzt immer, womit du arbeiten willst:** mit einer deiner KIs oder mit
  **Mein KI-Abo**. Auch **Tab ergänzen** und **Story & Lore** bieten **Mein KI-Abo** im Menü des Knopfs an.
- **Story & Lore mit deinem eigenen KI-Abo:** Prompt kopieren, im Chat einfügen, Antwort zurück in PlatinVault. Eine
  Vorschau zeigt die erkannten Teile, fehlende meldet PlatinVault.
- Hast du zuletzt **Mein KI-Abo** gewählt, ist es beim nächsten Mal vorgewählt. Nimmst du wieder eine KI mit Key,
  vergisst PlatinVault das.

### 🔄 Geändert

- **Die Navigation links gibt es nur noch auf einer Spielseite:** Spielkopf, Live-Trophäen, Statistiken, die
  Guide-Kapitel nach Gruppen und Story & Lore. Übersicht, Alle Spiele, Einstellungen und „Über PlatinVault“ kommen ohne
  Seitenleiste aus und nutzen die ganze Breite.
- **Alles andere findest du oben in der Kopfzeile:** dein Steam-Profil mit Bild und Zahl deiner Spiele, Trophäenschrank,
  Darstellungsgröße, Sprache, Farbschema, Einstellungen, Hilfe, Handbuch und Info. In schmalen Fenstern wandern einige
  Symbole in das Menü **Weitere Funktionen**.
- Der Trophäenschrank öffnet sich wieder als Vollbild über der Seite, auf der du gerade bist. Seine Kopfzeile passt jetzt
  zum neuen Design und zeigt Platin-Spiele, Trophäen und Spielzeit.
- Das Zahnrad öffnet die Einstellungen auf dem Reiter, den du zuletzt benutzt hast.
- Der aktive Eintrag der Spielnavigation ist mit einem breiteren Strich deutlicher markiert.
- Karten heben sich nur noch durch eine hellere Fläche ab, ohne Rahmen. Die Oberfläche nutzt überall die Schrift Inter,
  die mit PlatinVault mitkommt, auch im Trophäen-Popup über dem Spiel.
- **Neue Bewertungsleiste auf den Spielkacheln direkt unter dem Cover:** Steam-Rezensionen, Metacritic, IGDB-Kritiker und
  IGDB-Spieler. Fehlt eine Bewertung, fällt sie weg und die anderen teilen sich den Platz. Die Leiste „Spielen | Favorit |
  Twitch“ sitzt direkt darunter, die frühere Zeile „Bewertung“ entfällt.
- Wie viele gerade auf Twitch streamen, steht als Zahl direkt im Twitch-Feld der Kachel statt als Abzeichen in der Ecke.
- Spiele mit 100 % tragen auf dem Cover ein Platin-Schild mit gelegentlichem Funkeln. Ihre Trophäenlinie wird zur
  goldenen Linie mit Platin-Symbol und kleinen Sternen.
- In den Einstellungen unter **Live & Sound** zeigt jede Positions-Kachel eine kleine Vorschau, wo das Trophäen-Popup
  erscheint.

### 🔧 Behoben

- Auf der Statistikseite zeigt „IGDB-Spieler“ jetzt wirklich die Wertung der IGDB-Spieler. Bisher stand dort ein
  Mischwert aus Spielern und Kritikern.

## v2026.11.24.13.28980 (α)

### 🔄 Geändert

- **Neues, leichtes Design:** feine Linien statt schwerer Rahmen und eine ruhige Schrift. Kristallblau zeigt, was du
  anklicken kannst und wo du gerade bist; Gold gehört nur noch den Trophäen und Platin.
- **Links gibt es jetzt eine Navigation für alles:** Übersicht, Alle Spiele, deine Lieblingsspiele und den Trophäenschrank.
  Das geöffnete Spiel klappt dort auf und zeigt Live-Trophäen, Statistiken, alle Guide-Kapitel nach Gruppen und Story & Lore.
  Die eigene Kapitelleiste der Spielseite gibt es nicht mehr.
- Die Navigation lässt sich zu einer schmalen Symbolleiste einklappen, PlatinVault merkt sich das. Auf dem Handy öffnest du
  sie über das Menü-Symbol oben links.
- **Die Einstellungen sind jetzt eine eigene Seite**, du öffnest sie unten in der Navigation. Der gewählte Reiter bleibt beim
  Neuladen erhalten.
- Version, „Das ist neu“ und die Lizenzen findest du auf der neuen Seite **Über PlatinVault** hinter dem Info-Symbol unten in
  der Navigation. Den Seitenfuß gibt es nicht mehr.
- Lädst du die Seite neu, bleiben Handbuch, Lizenzen und „Das ist neu“ geöffnet.
- Die Scrollleiste liegt jetzt über dem Inhalt und erscheint nur, wenn es etwas zu scrollen gibt. So nutzt die Seite immer
  die ganze Breite.
- Knöpfe und Symbole sind größer und leichter zu treffen, und alle Texte sind im hellen und im dunklen Modus gut lesbar.
- Der erste Start begrüßt dich mit einer neuen Seite: Links wählst du Sprache und Farbschema, rechts siehst du, was
  PlatinVault kann.
- **PlatinVault braucht jetzt einen Prozessor mit x86-64-v2-Unterstützung**, das ist praktisch jeder PC- oder
  Server-Prozessor ab etwa 2013. Das gilt für Windows, Linux und den Container auf Unraid oder einem NAS.

## v2026.11.23.2.530 (α)

### 🔄 Geändert

- **„Alle auf einmal“ mit deinem KI-Abo liefert jetzt alle Kapitel in einer Antwort:** Der Prompt bittet die KI um eine
  Datei mit allen Kapiteln, die du einfach in PlatinVault ziehst. Kann dein Chat keine Datei erstellen, kommen alle Kapitel
  in einem einzigen Codeblock. Das ständige „weiter“ schreiben und Einfügen entfällt.
- Passt nicht alles in eine Antwort, hört der Chat nach einem vollständigen Kapitel auf und meldet **NOCH OFFEN** mit den
  fehlenden Kapiteln. Den Rest holst du über **Prompt für die restlichen Kapitel kopieren**.

### 🔧 Behoben

- Beim Einfügen der Antworten ging ein Kapitel ohne Meldung verloren, wenn in einer Tabelle „…“ stand oder die KI das
  Beispiel-Symbol aus dem Prompt übernommen hatte. Solche Kapitel werden jetzt erkannt.

## v2026.11.22.1.2206 (α)

### 🔄 Geändert

- **Ein Update-Kanal statt zweier Schalter:** Unter Einstellungen → System → Updates wählst du deinen Kanal jetzt mit einer
  einzigen Auswahl – **Keiner**, **Beta** oder **Nightly**. Keiner liefert nur fertige Releases, Beta liefert Betas (β) und
  Releases, Nightly liefert Nightlies (α) und Releases. Hattest du einen der alten Schalter eingeschaltet, wird deine Wahl
  automatisch übernommen (Nightly, wenn „Nightly“ an war, sonst Beta, wenn „Beta“ an war, sonst Keiner). Bitte beachten:
  Nightly enthält keine Betas mehr; willst du auch diese, wähle Beta.
- **Container-Tags folgen derselben Regel:** Eine Beta schiebt den Image-Tag `nightly` nicht mehr mit. `nightly` führt jetzt
  Nightlies und Releases, `beta` führt Betas und Releases, `latest` nur Releases. Die Beschreibungen in der Unraid-Vorlage und
  die Beispiel-Compose-Datei erklären, welcher Tag zu welchem Kanal gehört.
- **Versionsnummern zeigen jetzt alle fünf Teile:** Eine Version steht als `v2026.11.22.8.105` für ein Release, mit **(β)**
  für eine Beta und **(α)** für eine Nightly dahinter, zum Beispiel `v2026.11.22.8.105 (α)` – in der App, in der Info-Karte, im
  Installer und in den Release-Titeln. Frühere „Das ist neu“-Einträge im alten Stil werden nach einem Update weiterhin gefunden.

### 🔒 Sicherheit

- **Ein neues Release verschwindet nicht mehr hinter vielen Nightlies:** Die Update-Prüfung liest jetzt bis zu 100 statt 30
  Releases aus dem Feed, ein neues fertiges Release wird also auch dann gefunden, wenn danach viele Nightlies erschienen sind.
- **Ein seltsames Versions-Tag bricht die Update-Prüfung nicht mehr:** Ein ungewöhnliches oder fehlerhaftes Versions-Tag oder
  Release im Update-Feed wird jetzt übergangen, statt die Prüfung scheitern zu lassen.
- **Dateinamen von Downloads aus dem Feed werden geprüft:** Für Installer und Linux-Pakete werden nur schlichte Dateinamen
  (Buchstaben, Ziffern, Punkt, Bindestrich, Unterstrich) akzeptiert.

## v2026.11.21 (8.105 α)

### 🔄 Geändert

- **Ruhigere, geschmeidigere Vollbild-Trophäen-Popups:** Das Vollbild-Overlay fließt jetzt zügiger und harmonischer ein (~1,2 s), sodass die fertige Trophäe rund 3,5 Sekunden in voller Ruhe betrachtet werden kann. 450 ms vor Ablauf der eingestellten Anzeigedauer blendet ein sanfter Ausblend-Puffer das Overlay samt Zeitbalken weich aus – ohne harte Kanten oder plötzliche Abbrüche.
- **Hardwarebeschleunigte Ausblend-Animation:** Außenrahmen mit Backdrop-Blur und Innenbühne nutzen synchrone Easing-Kurven und GPU-Hinweise (`willChange: 'opacity'`), wodurch das Ausblenden auch auf Bildschirmen mit hoher Bildwiederholrate butterweich und ruckelfrei abläuft.

## v2026.11.20 (10.1884 α)

### ✨ Neu

- **Nightly-Kanal (α):** Unter Einstellungen → System → Updates kannst du neben **Beta-Versionen erhalten** jetzt auch
  **Nightly-Versionen (α) erhalten** einschalten. Nightly-Versionen tragen ein **α** hinter der Versionsnummer und liefern die
  allerneuesten Änderungen direkt aus der laufenden Entwicklung, bevor sie als Beta (β) oder fertiges Release freigegeben
  werden. Container folgen diesem Kanal über den Image-Tag `ghcr.io/sushiflux/platinvault:nightly`.

### 🔄 Geändert

- **Mehr Platz für Kapitel in der Seitenleiste:** Am Fuß der Kapitelleiste auf einer Spielseite steht jetzt in jeder
  Fenstergröße das kompakte Info-Symbol („i“ im Kreis), damit die Kapitelliste den vollen Platz behält. Ein Klick oder Zeigen
  öffnet die Info-Karte mit der klickbaren Versionsnummer (**Version & Updates** sowie **Das ist neu in dieser Version**) und
  **Lizenzen**.
- **Eigene Änderungsprotokolle je Kanal:** Die Release-Notizen sind jetzt sauber nach fertigen Releases, Betas (β) und
  Nightlies (α) getrennt und liegen alle für die Offline-Anzeige im Programm bei.

### 🔧 Behoben

- **Fließender Zeitbalken beim Vollbild-Trophäen-Popup:** Der Zeitbalken am unteren Rand des Vollbild-Popups gleitet jetzt
  über die gesamte eingestellte Anzeigedauer gleichmäßig und butterweich mit einem feinen Lichtpunkt von voll auf leer.

