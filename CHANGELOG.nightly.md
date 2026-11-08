# Nightly Changelog (α)

**[English](#english) · [Deutsch](#deutsch)**

---

<a name="english"></a>
## English

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

