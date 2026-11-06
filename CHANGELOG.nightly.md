# Nightly Changelog (α)

**[English](#english) · [Deutsch](#deutsch)**

---

<a name="english"></a>
## English

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

