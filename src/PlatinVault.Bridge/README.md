# PlatinVault Bridge

Windows-Programm für den **Gaming-PC**. Es meldet freigeschaltete Steam-Erfolge in Echtzeit an deinen PlatinVault-Server,
auch wenn der auf einem anderen Gerät läuft (z. B. Unraid). Der Server kennt deinen Spielstand dadurch sofort, ohne auf die
Steam-Server zu warten, und zeigt Pop-ups und Sounds für neue Trophäen.

Die Bridge braucht **keine .NET-Installation und keine Administratorrechte**, richtet sich selbst für den Benutzer ein, zeigt
ein **Pokal-Symbol im Tray** (farbig = verbunden, grau = nicht verbunden) und **aktualisiert sich selbst** vom
PlatinVault-Server (kein GitHub nötig).

## Einrichten (der übliche Weg)

Das Paket (ZIP) kommt aus PlatinVault unter **Einstellungen > PlatinVault Bridge**. Es enthält `PlatinVault.Bridge.exe`, eine
fertige `bridge.json` (Server-Adresse, Token und Sprache), `LIESMICH.txt` / `README.txt` und das PlatinVault-Handbuch als PDF
(`Handbuch (Deutsch).pdf`, `Handbook (English).pdf`).

1. ZIP entpacken, `PlatinVault.Bridge.exe` doppelklicken.
2. Das **Einrichtungsfenster** zeigt Server und Token, prüft die Verbindung sofort mit einer echten Statusmeldung
   („✓ Verbunden mit … – Steam-Konto …“ oder ein konkreter Fehler) und fragt: *Mit Windows starten*, *Eintrag im Startmenü*.
   Fehlt die `bridge.json`, kann Server-Adresse und Token direkt eingefügt werden (live geprüft).
3. **Installieren & starten** kopiert Exe und Konfiguration nach `%LOCALAPPDATA%\Programs\PlatinVault Bridge\`, legt den
   Autostart (`HKCU\...\Run`), den Startmenü-Eintrag und einen Eintrag unter Windows „Apps“ an und startet die installierte
   Kopie. Ist schon eine ältere Version installiert, wird sie an Ort und Stelle aktualisiert (die laufende wird zuerst
   sauber beendet). Die Handbuch-PDFs aus dem ZIP werden mit in den Programmordner kopiert.

## Sprache

Die Bridge spricht **Deutsch und Englisch** und folgt der Sprache des PlatinVault-Servers. Reihenfolge, Wichtigeres gewinnt:

1. `--language de|en` (nur für diesen Lauf),
2. die Sprache, die der Server in der Antwort auf jeden Statuspush nennt (`uiLanguage` = die gespeicherte Einstellung `ui.language`;
   fehlt sie, weil noch keine gewählt ist oder der Server älter ist, ändert sich nichts) – ein Wechsel in PlatinVault gilt in
   Tray-Menü, Tooltip, Statusfenster und Handbuch sofort, in den Einrichtungs-/Meldungsfenstern beim nächsten Öffnen,
3. der Wert `language` in `bridge.json` (das ZIP aus PlatinVault bringt ihn mit; die laufende Bridge schreibt ihn bei jedem Wechsel nach),
4. die Windows-Anzeigesprache: Deutsch für Deutsch, alles andere Englisch. Gelesen mit `GetUserDefaultUILanguage` – die Exe läuft
   mit `InvariantGlobalization`, deshalb sagt `CultureInfo.CurrentUICulture` hier nichts.

Das Handbuch öffnet die Bridge in ihrer Sprache (`Handbuch (Deutsch).pdf` bzw. `Handbook (English).pdf`; fehlt die Datei, die Seite des
Servers `/api/docs/handbook?lang=…`). Alle Texte stehen in `Localization/BridgeTexts*.cs` (`Msg(De, En)`, Typ-geprüft; nur die Zeilen
eines Testlaufs bleiben deutsch).

## Tray-Symbol

- Tooltip: `PlatinVault Bridge – verbunden`, `– nicht verbunden` oder `– Spiel: <Name> (4/40)` (englisch: `connected`, `not connected`, `Game: …`).
- Rechtsklick: Statuszeile, **PlatinVault öffnen**, **Handbuch öffnen**, **Protokoll öffnen**, **Mit Windows starten** (Haken), **Beenden**,
  **Deinstallieren …**.
- Doppelklick: kleines Statusfenster (Verbindung, aktuelles Spiel mit Fortschritt, letzter Erfolg, Version, Server).
  Dasselbe passiert, wenn die Exe ein zweites Mal gestartet wird, während sie läuft.
- Windows 11 versteckt neue Symbole oft hinter „^“: *Einstellungen > Personalisierung > Taskleiste > Weitere Symbole der
  Taskleiste*.

## Deinstallieren

Windows-Einstellungen > Apps > **PlatinVault Bridge**, oder Tray-Menü > Deinstallieren …, oder
`PlatinVault.Bridge.exe --uninstall`. Nach einer Bestätigung (mit Wahl, ob das Protokoll mitgelöscht wird) werden die
laufende Bridge beendet, Autostart, Startmenü- und „Apps“-Eintrag sowie die gespeicherten Einstellungen entfernt und der
Programmordner gelöscht.

## Protokoll

`%LOCALAPPDATA%\PlatinVault\bridge.log` (rollierend, höchstens ca. 1 MB: die aktuelle Datei plus `bridge.log.1`). Wird die
Exe in einem Terminal gestartet, erscheinen dieselben Zeilen zusätzlich dort; `--help` und `--version` drucken ebenfalls im
Terminal.

## Prüfen (Entwickler)

Zwei versteckte Optionen, beide brauchen `--test-root <Ordner>` (alles Schreibbare und die Registry-Schlüssel gehen dorthin, es erscheint
nichts auf dem Desktop und eine laufende Bridge bleibt unberührt):

- `--test-texts` prüft den Textkatalog (`BridgeTexts*.cs`): jeder Eintrag hat einen deutschen und einen englischen Text, eine Übersetzung
  ist nicht versehentlich gleich, und jedes Argument eines Textes kommt in beiden Sprachen vor.
- `--test-layout <Bilderordner>` baut alle Fenster in beiden Sprachen, setzt jedes durch alle Zustände mit anderen Texten
  (`SetupForm.TestStates()`, Statusfenster, Meldungsfenster), speichert je ein PNG und misst, dass jeder Text bei 100, 125, 150, 200 und
  250 % Windows-Skalierung in sein Steuerelement passt (Bericht: `layout-report.txt`).

Beide enden mit Exit-Code 0 (alles in Ordnung) oder 1 (Liste der Fehler); die Ausgabe erscheint nur, wenn die Exe aus einem Terminal mit
Umleitung läuft (`… --test-texts | cat`).

## Was die Bridge macht

- Sie liest auf diesem PC, welches Steam-Spiel gerade läuft (Registry `RunningAppID`) und die lokalen Steam-Statistikdateien
  (`Steam\appcache\stats`). Es wird **keine Steam-Web-API und kein API-Key** benötigt.
- Sobald sich etwas ändert (Spiel gestartet/beendet, Erfolg freigeschaltet), schickt sie den Stand per
  `POST /api/bridge/state` an den Server, inklusive ihrer Version (`bridgeVersion`). Zusätzlich sendet sie alle 60 Sekunden
  einen Heartbeat.
- **Spiele starten:** In einem zweiten, eigenen Takt fragt sie alle 2 Sekunden `POST /api/bridge/commands` (nur ein paar hundert Bytes)
  nach Aufträgen. Tippt jemand in PlatinVault auf „Spielen“, bekommt sie den Auftrag „starte AppID n“ und öffnet
  `steam://run/<n>` (`CommandPoller`, `GameLauncher.cs`); ein Ballon neben dem Symbol meldet „Starte …“, das Ergebnis geht mit der nächsten
  Anfrage an den Server. Aufträge werden höchstens einmal ausgeführt (Merken der Auftrags-Id), und es wird immer nur die feste Adresse
  `steam://run/<Zahl>` aus einer geprüften Zahl gebaut – nie ein Text vom Server. Ein Testlauf (`--test-root`) startet nichts: der
  `RecordingLauncher` schreibt die Adresse nur in `ui\launches.txt`; sein Ergebnis steuert das versteckte `--test-launch started|steam-missing|failed`.
- Authentifizierung: ein persönliches **Bridge-Token** (`Authorization: Bearer ...`), das zu deinem Benutzer in PlatinVault
  gehört.
- Ist der Server nicht erreichbar, läuft die Bridge leise weiter (Tray-Symbol grau) und meldet sich nur bei Statuswechseln.
  Ist das Token ungültig, steht das **einmal** im Protokoll; sie versucht es alle 30 Sekunden erneut.

## Von Hand starten (ohne Einrichtungsfenster)

```powershell
PlatinVault.Bridge.exe --no-setup --server http://192.168.1.20:5233 --token <DEIN-TOKEN> --save
```

| Option | Bedeutung |
|---|---|
| `--server <url>` | Adresse des PlatinVault-Servers (`http://` oder `https://`) |
| `--token <token>` | Bridge-Token aus den PlatinVault-Einstellungen |
| `--save` | Server, Token und Sprache speichern (`bridge.json` neben der Exe, falls dort schon eine liegt, sonst `%APPDATA%\PlatinVault\bridge.json`) |
| `--no-setup` | von hier aus laufen, ohne Einrichtungsfenster (kein Installieren) |
| `--interval <ms>` | Abfrageintervall, 100 bis 10000 ms, Standard 350 |
| `--language <de\|en>` | Sprache dieses Laufs erzwingen (sonst die des Servers, siehe „Sprache“); mit `--save` wird sie mitgespeichert |
| `--no-update` | Selbstupdate abschalten |
| `--uninstall` | deinstallieren (so ruft Windows „Apps“ die Deinstallation auf) |
| `--verbose` | ausführliche Ausgabe (Heartbeats, Fehlerdetails) |
| `--app <appid>` | nur zum Testen: so tun, als liefe dieses Spiel, und dessen lokale Erfolge senden |
| `--version`, `--help` | Version bzw. Hilfe |

Konfiguration, in dieser Reihenfolge (Wichtigeres gewinnt):
**Argumente > Umgebungsvariablen (`PLATINVAULT_SERVER`, `PLATINVAULT_TOKEN`) > `bridge.json` neben der Exe >
`%APPDATA%\PlatinVault\bridge.json`.** Beide Dateien haben dieselbe Form:

```json
{ "server": "http://192.168.1.20:5233", "token": "...", "language": "de" }
```

Beim Start aus einem Terminal gibt cmd den Prompt sofort zurück, weil die Exe eine Windows-Anwendung ohne Konsole ist; die
Ausgabe folgt kurz danach (`start /wait PlatinVault.Bridge.exe --help` wartet).

## Selbstupdate

Die Bridge hat eine eigene Versionsnummer `1.0.<n>`, unabhängig von der PlatinVault-Version; `n` zählt die Commits, die die
Bridge selbst (oder den gemeinsamen Steam-Reader) ändern. Jede Antwort des Servers auf den Statuspush nennt die neueste
Version und deren SHA-256 (`latestBridgeVersion`, `bridgeSha256`). Ist sie neuer:

1. Die Bridge lädt `GET /api/bridge/exe` (mit Token) als `PlatinVault.Bridge.exe.new` neben die laufende Exe.
2. Stimmt die Prüfsumme nicht, wird die Datei verworfen und diese Version eine Stunde lang nicht erneut versucht.
3. Sonst wird die laufende Exe in `.old` umbenannt, die neue an ihre Stelle verschoben und neu gestartet (mit denselben
   Argumenten, ohne Einrichtungsfenster, das Tray-Symbol meldet kurz die neue Version). Bei einer Installation wird so die
   installierte Exe ersetzt. `.old` wird beim nächsten Start gelöscht.
4. Ist der Ordner nicht beschreibbar, steht das einmal im Protokoll und das Update wird übersprungen.

## Hinweise

- **Sicherheit:** Das Token liegt im Klartext in `bridge.json`. Über `http://` im Heimnetz wird es unverschlüsselt übertragen;
  nutze `https://`, sobald der Server von außerhalb des eigenen Netzes erreichbar ist. Ein Token kann nur Erfolgsdaten an
  dein Konto melden und die Bridge-Datei abrufen. Im Zweifel in PlatinVault löschen oder neu erzeugen.
- **Mehrere Steam-Konten auf dem PC:** gemeldet wird das Konto, das in Steam gerade aktiv ist.
- **Eine ältere Bridge ohne Fenster** (vor der Einrichtung) läuft noch? Das Einrichtungsfenster erkennt sie und bietet an, sie zu
  beenden.
- **Unsignierte Exe:** Windows SmartScreen warnt beim ersten Start („Weitere Informationen“ > „Trotzdem ausführen“).

## Selbst bauen

```powershell
.\scripts\build-bridge.ps1                  # Version 1.0.<n> aus git
.\scripts\build-bridge.ps1 -Version 1.0.7   # oder env BRIDGE_VERSION, z. B. für CI/Docker ohne .git
```

Ergebnis in `artifacts\bridge\`: `PlatinVault.Bridge.exe` (eigenständig, WinForms, eine Datei, win-x64, ca. 103 MB wegen der
mitgelieferten .NET-Laufzeit samt Windows-Forms), `bridge-version.txt`, `package\LIESMICH.txt` und `package\README.txt` (Vorlagen, die der Server mit
Exe und `bridge.json` zippt). Die Exe ist bewusst **nicht getrimmt** (WinForms ist nicht trim-sicher) und **nicht
komprimiert**: Komprimieren halbiert die Datei (ca. 47 MB), kostet aber dauerhaft rund 75 MB mehr Arbeitsspeicher im
laufenden Tray-Programm und verdreifacht die Startzeit. Der Code zum Lesen der Steam-Dateien ist dieselbe Datei wie im Server
(`src/Companion.Api/Services/SteamLocalStatsService.cs`), die hier per Link mitkompiliert wird.
