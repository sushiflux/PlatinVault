# Nightly Changelog (α)

**[English](#english) · [Deutsch](#deutsch)**

---

<a name="english"></a>
## English

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

