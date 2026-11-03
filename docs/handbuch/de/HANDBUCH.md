---
title: PlatinVault
subtitle: Handbuch
tagline: Live-Trophäen für deine Steam-Spiele und 100-%-Guides an einem Ort – so richtest du PlatinVault ein und nutzt es im Alltag.
version: v2026.11.14
date: 2026-11-03
status: Release
copyright: © 2026 SushiFlux
lang: de
---

# 1 Was PlatinVault für dich tut

PlatinVault ist dein Begleiter auf dem Weg zu 100 % in Steam-Spielen:

- **Live-Trophäen:** Schaltest du in einem Spiel einen Steam-Erfolg frei, erscheint sofort ein eigenes Trophäen-Popup mit
  Animation und Sound. Hast du alle Erfolge eines Spiels, folgt der große **Platin-Moment**.
- **100-%-Guides:** Für jedes Spiel recherchiert eine KI einen Komplett-Guide mit Kapiteln wie Roadmap, Sammelobjekten,
  Waffen, Bossen oder Bestiarium – mit abhakbaren Checklisten.
- **Überblick über deine Bibliothek:** Trophäen, Spielzeit, Bewertungen und deine Fortschritte auf einen Blick.

Erfolge heißen bei Steam „Achievements“; PlatinVault nennt sie **Trophäen** und zeigt sie in den Stufen Bronze, Silber, Gold
– je nachdem, wie selten sie unter allen Spielern sind.

> [!HINWEIS] Dieses Handbuch beschreibt die **Vorabversion** – es zeigt nur, was schon eingebaut ist.

# 2 So läuft PlatinVault

PlatinVault ist eine Web-App: Du öffnest sie im Browser – auf dem PC, Tablet oder Handy. Dahinter läuft ein kleiner Server,
der deine Spiele, Guides und Häkchen in einer Datenbank speichert.

| Wo läuft der Server? | Woher kommen die Live-Trophäen? |
|---|---|
| Auf deinem Gaming-PC | PlatinVault liest Steam direkt auf diesem PC – nichts weiter nötig. |
| Auf einem anderen Rechner (Server, NAS, Unraid) | Auf dem Gaming-PC läuft die kleine **PlatinVault Bridge** und meldet neue Trophäen sofort an den Server (Kapitel 8). |

Auf dem Gaming-PC gibt es PlatinVault auch als **Windows-Programm** mit eigenem Fenster, Overlay über dem Spiel und eigener Datenbank –
siehe Kapitel 11.

# 3 Installation auf Unraid, Linux und NAS

PlatinVault gibt es als Container-Image. Auf Unraid legst du es aus der Vorlage an, auf einem normalen Linux-Rechner (und auf NAS-Systemen mit Compose-Unterstützung) startest du es mit einer fertigen Compose-Datei. Die Abschnitte 3.1 bis 3.3 beschreiben die drei Wege; Windows hat ein eigenes Programm (Kapitel 11).

## 3.1 Unraid

PlatinVault gibt es als Container für Unraid. Eine Datenbank musst du nicht einrichten: PlatinVault bringt sie mit – eine Datei im
Appdata-Ordner.

1. Container aus der Vorlage anlegen und ausfüllen:

   | Feld | Bedeutung |
   |---|---|
   | WebUI Port | Port, unter dem du PlatinVault im Browser öffnest (Standard 5233). |
   | Appdata (Datenbank, Sicherungen, Schlüssel) | Ordner für alles von PlatinVault: die Datenbank (`platinvault.db`), die automatischen Sicherungen (`backups`), die verschlüsselten API-Keys und Zwischenspeicher. Standard `/mnt/cache/appdata/platinvault` – direkt auf dem Cache-Pool, denn Nutzer berichten von beschädigten Datenbanken über den Weg `/mnt/user/…`. Heißt dein Pool anders, nimm `/mnt/<Poolname>/appdata/platinvault`. **Nie löschen.** |
   | PUID / PGID (erweitert) | Benutzer und Gruppe, denen der Appdata-Ordner gehört und unter denen PlatinVault läuft. Standard 99 / 100 – so legt Unraid neue Appdata-Ordner an; nur ändern, wenn dein Ordner einem anderen Benutzer gehört. |

2. Container starten und PlatinVault im Browser öffnen: `http://<IP deines Unraid>:5233`.
3. Beim ersten Öffnen wählst du zuerst auf einem Vollbild-Bildschirm deine **Sprache** (Deutsch oder English; vorausgewählt ist
   die Sprache deines Browsers, ändern kannst du sie später jederzeit oben in der Kopfzeile). Danach führt dich eine kurze **Tour** durch die App. Du kannst sie jederzeit über das **?** oben rechts
   wieder starten.

Updates bekommst du über Unraid wie bei jedem anderen Container; deine Daten im Appdata-Ordner bleiben erhalten. Ob dein
Container nur Releases oder auch Betas bekommt, entscheidet sein Image-Tag – siehe Kapitel 12.

## 3.2 Linux mit Docker

Auf einem Linux-Rechner (oder -Server) mit Docker brauchst du nichts außer dem Compose-Plugin (`docker compose version` zeigt eine
Version an). Die **Datenbank und die automatischen Sicherungen stecken im Container** – du richtest keine Datenbank ein und denkst
dir kein Passwort aus.

1. Lege einen Ordner an (zum Beispiel `~/platinvault`) und lade aus dem PlatinVault-Repository auf GitHub
   (`github.com/sushiflux/PlatinVault`, Ordner `deploy/`) die Dateien `docker-compose.yml` und `restore-db.sh` hinein. Wenn du den Port
   oder den Benutzer ändern willst, lade auch `.env.example` herunter und speichere es als `.env`.
2. Starte PlatinVault in diesem Ordner mit einem Befehl:

   ```
   docker compose up -d
   ```

   Beim ersten Start lädt Docker das Image, und PlatinVault legt seine Datenbank im Ordner `data` an. Du musst nichts eintragen.
3. Öffne `http://<Adresse deines Servers>:5233` im Browser und fahre mit Kapitel 4 fort.

Gut zu wissen:

- **Keine Anmeldung:** PlatinVault hat keinen Login. Betreibe es nur im eigenen Heimnetz und gib den Port nicht ins Internet frei.
- **Ordner:** Neben der Compose-Datei entsteht `data` mit allem: der Datenbank `platinvault.db`, den Sicherungen in `data/backups`,
  deinen verschlüsselten API-Keys und Einstellungen – nie löschen. Er gehört auf eine lokale Platte des Servers (keine Netzwerkfreigabe).
  Kopiere ihn regelmäßig auch an eine andere Stelle.
- **Benutzer-ID:** Die Dateien in `data` gehören dem Benutzer mit den Zahlen `PUID`/`PGID`; Standard ist 1000 / 1000, auf den meisten
  Linux-Systemen der erste normale Benutzer. Mit dem Befehl `id` siehst du deine Zahlen; andere Werte (und einen anderen Port,
  Standard 5233) trägst du in der Datei `.env` ein.
- **Aktualisieren:** `docker compose pull && docker compose up -d` im selben Ordner. Deine Daten bleiben erhalten. Die Betaversion
  bekommst du, indem du in der Compose-Datei die Zeile `image:` auf `ghcr.io/sushiflux/platinvault:beta` änderst (Kapitel 12).
- **Sicherungen:** PlatinVault sichert seine Datenbank selbst – beim Start, danach alle 6 Stunden und beim Stoppen des Containers – in
  den Ordner `data/backups` (die letzten 20 und je eine pro Tag der letzten 60 Tage). Eine Sicherung wird nur gespeichert, wenn sich
  etwas geändert hat, und vorher geprüft; jede ist eine einzelne Datei.
- **Wiederherstellen:** Im Ordner der Compose-Datei `./restore-db.sh` (neueste Sicherung) oder `./restore-db.sh data/backups/daily/<datei>.db`.
  Das Skript fragt nach und stoppt PlatinVault; PlatinVault sichert zuerst den aktuellen Stand, prüft die gewählte Sicherung, ersetzt dann
  die Datenbank, und das Skript startet PlatinVault wieder.
- **Deinstallieren:** `docker compose down` entfernt den Container, deine Daten bleiben. Danach kannst du den Ordner `data` löschen,
  wenn du ihn nicht mehr brauchst.

## 3.3 NAS

Eine NAS, deren Container-Verwaltung Compose-Dateien annimmt, kann dieselbe `docker-compose.yml` verwenden. Nach den Angaben der Hersteller:

- **Synology (Container Manager):** unter *Projekt* mit *Erstellen* ein Projekt anlegen, Namen und einen Ordner als Arbeitsverzeichnis wählen
  und die `docker-compose.yml` hochladen. Lege `restore-db.sh` in denselben Ordner.
- **TrueNAS (Apps):** *Install via YAML* im Menü neben *Custom App* nimmt Compose-YAML an. Die Datei verweist auf den Ordner `data` neben
  sich; verwende dort den absoluten Pfad eines Datasets (wie TrueNAS relative Pfade behandelt, ist nicht geprüft).
- **Andere NAS:** Schau in der Anleitung deines Herstellers, ob die Container-Verwaltung Compose-Dateien („Projekte“, „Stacks“) kennt.
  Setze `PUID` und `PGID` auf die Zahlen des Benutzers, dem dein Datenordner gehört.

PlatinVault wurde mit Docker auf Linux getestet; auf einzelnen NAS-Modellen haben wir es nicht ausprobiert.

# 4 Erste Schritte

Öffne die **Einstellungen** über das Zahnrad oben rechts (auf jeder Seite). Einstellungen, die nur an oder aus sind, haben einen
Schalter: **grün mit Häkchen** heißt an, **rot mit Kreuz** heißt aus; er lässt sich anklicken (auch auf den Text daneben) oder
mit der Tastatur umlegen (Leertaste). Gespeichert wird mit **Einstellungen speichern**.

## 4.1 Steam verbinden

- **Mit Steam verknüpfen:** Im Reiter **Steam** meldest du dich bei Steam an; Steam teilt PlatinVault nur deine SteamID mit
  – kein Passwort.
- **Steam Web API-Key:** Damit liest PlatinVault deine Bibliothek, Spielzeiten und Erfolge. Der Key ist kostenlos:
  1. `steamcommunity.com/dev/apikey` im Browser öffnen und bei Steam anmelden (eventuell fragt Steam einen
     Steam-Guard-Code ab). Läuft PlatinVault auf deinem Gaming-PC, kannst du die Seite auch direkt im Steam-Client öffnen.
  2. Bei „Domänenname“ einen beliebigen Namen eintragen, zum Beispiel die Adresse deines PlatinVault-Servers.
  3. Den Nutzungsbedingungen zustimmen und absenden. Steam zeigt dir den Key an.
  4. Key in PlatinVault einfügen und speichern.
- Dein **Steam-Profil** muss die Spieldetails öffentlich zeigen, sonst liefert Steam keine Bibliothek.

## 4.2 KI für die Guides

Im Reiter **KI-Recherche** hinterlegst du einen Key für **Google Gemini**, **Anthropic Claude** oder **OpenAI ChatGPT**.
Nach der Prüfung wählst du das Modell aus einer Liste.

> [!WARNUNG] Gratis-Key und Websuche: Die Guide-Recherche nutzt die Google-Suche. Neue Google-Konten bekommen sie im
> Gratis-Tarif nicht mehr (Stand Oktober 2026) – dafür muss das Google-Projekt des Keys mit einer Abrechnung verbunden sein.
> Unter Einstellungen → KI-Recherche zeigt **„Verbindung echt testen“**, was dein Key darf.

Ein Abo (Gemini- oder Claude-Abo) gilt **nicht** für API-Keys – die werden getrennt abgerechnet. Hast du keinen Key, aber ein KI-Abo,
geht es trotzdem – von Hand, siehe **Guide mit deinem KI-Abo** in Abschnitt 6.2.

## 4.3 Welche KI arbeitet? Die KI-Auswahl

Oben im Reiter **KI-Recherche** steht die **KI-Auswahl**. Sie bestimmt, welche KI deine Aktionen übernimmt: neuen Guide recherchieren,
Guide vervollständigen, Kapitel ergänzen, Story & Lore und das Übersetzen eines Guides. Es gibt drei Wege:

| Modus | So arbeitet PlatinVault |
|---|---|
| **Standard-KI** | Eine KI schreibt den ganzen Guide, alle Kapitel im selben Stil. Welche, wählst du in der Liste darunter – wählbar sind nur KIs mit gespeichertem Key. |
| **Gemischt** | Die Kapitel eines Guides werden auf alle KIs mit Key verteilt. Das geht schneller, aber der Schreibstil kann zwischen den Kapiteln schwanken. Für die Recherche nimmt PlatinVault dabei nur KIs mit Websuche (Gemini, Claude); OpenAI recherchiert hier ohne Websuche und kommt nur dran, wenn es der einzige Key ist. |
| **Vor jeder Anfrage fragen** | Jede KI-Aktion öffnet zuerst ein kleines Fenster **„Welche KI möchtest du benutzen?“**. Es listet nur KIs mit Key, mit ihrem Zustand (zum Beispiel „Kontingent erschöpft seit 10:12“); deine zuletzt benutzte KI ist vorgewählt, und ist sie gerade erschöpft, bekommst du einen Hinweis und eine funktionierende KI vorgeschlagen. |

In der Liste **Deine KIs** siehst du von jeder KI, ob sie bereit ist oder ein Problem hat: **Kontingent erschöpft**, **Key
abgelehnt** oder **Nicht erreichbar**, jeweils mit der Uhrzeit, seit der das Problem besteht. PlatinVault merkt sich das für eine
Weile, damit nicht jedes Kapitel die erschöpfte KI erneut anfragt: bei erschöpftem Kontingent so lange, wie die KI es nennt (sonst
eine Stunde), bei einem abgelehnten Key, bis du den Key ersetzt, bei einer Störung zwei Minuten. Mit **Zurücksetzen** vergisst
PlatinVault den Fehler sofort und probiert die KI bei der nächsten Anfrage wieder.

**Bei Problemen automatisch zur nächsten KI wechseln** (Schalter, standardmäßig an; gilt für Standard-KI und Gemischt): Ist das
Kontingent der gewählten KI aufgebraucht, der Key ungültig oder die KI nicht erreichbar, macht PlatinVault mit der nächsten KI mit
Key weiter. Du erfährst es **immer** – mit einem Hinweis wie „Gemini-Kontingent aufgebraucht – weiter mit Claude.“. Andere Fehler,
zum Beispiel eine unlesbare Antwort, führen nicht zum Wechsel. Ist der Schalter aus, bricht die Aktion bei einem solchen Problem ab,
und du siehst den Grund. Im Modus „Vor jeder Anfrage fragen“ wechselt PlatinVault nie von selbst; nur Hintergrundaufgaben wie das
Übersetzen von Erfolgstexten oder eines Guides laufen im Notfall still auf der nächsten KI weiter (und tragen es ins Protokoll ein).

**Eine KI nur für diese eine Aktion:** Bei Standard-KI und Gemischt zeigt der Knopf einer KI-Aktion hinter seinem Namen, welche KI
arbeiten wird, zum Beispiel **Tab jetzt erstellen · Claude ▾**. Ein Klick auf den Teil mit dem Pfeil lässt dich für diese Aktion eine
andere KI wählen; deine Einstellung bleibt unverändert. Schlägt eine Aktion fehl, bietet die Meldung **„Mit Claude erneut
versuchen“** an, sobald eine andere KI einen Key hat.

## 4.4 Recherche-Bausteine

Weiter unten im Reiter findest du die **Recherche-Bausteine**. Freier Text ist dafür nicht nötig: Du klickst fertige Bausteine an,
die PlatinVault in jede KI-Recherche einbaut – beim Bauplan und bei jedem Kapitel, auch bei „Guide vervollständigen“ und „Tab
ergänzen“. Der Kapitel-Aufbau der App gilt immer zusätzlich.

- **Umfang** (eine Wahl): Kompakt · Ausführlich · Sehr ausführlich.
- **Schwerpunkte** (mehrere möglich): Verpassbares zuerst · Sammelobjekte mit Fundorten · Builds & Ausrüstung · Boss-Strategien ·
  Trophäen-Roadmap · Farming-Routen · Schwierigkeitsgrad-Tipps · Online-/Koop-Trophäen.
- **Spoiler** (eine Wahl): Spoilerfrei · Leichte Spoiler · Alles erlaubt.
- **Darstellung** (eine Wahl): Stichpunkte · Mehr Fließtext · Tabellen bevorzugen.

Ein zweiter Klick auf einen gewählten Baustein nimmt die Wahl zurück; **Alle abwählen** leert alles. **Nichts gewählt** heißt: die
Standard-Recherche – sie fragt nach allem, was es bei einem Spiel geben kann (Tipps, Builds, Verpassbares, Sammelobjekte, Bestiarium
und mehr, nur was zum Spiel passt). Der Guide wird in seiner eigenen Sprache geschrieben; die Bausteine wirken in dieser Sprache.
Bausteine gelten nur für künftige Recherchen, vorhandene Guides ändern sich nicht.

**Der Umfang bestimmt auch, wie viele Kapitel ein Guide bekommt.** Kompakt sind etwa 5 bis 8 Kapitel, **Ausführlich** (auch ohne Auswahl) 8 bis 14, Sehr
ausführlich 12 bis 24. Das ist eine Obergrenze, kein Ziel: Ein kleines, einfaches Spiel bekommt wenige Kapitel. Ein Thema wird nur dann ein
Kapitel, wenn die Recherche bestätigt, dass das Spiel es wirklich hat – auch wenn es zur Spielart eigentlich dazugehört (ein gemütliches
Aufbauspiel ohne Kämpfe bekommt kein Bestiarium). Kleine, verwandte Themen fasst die KI zu einem Kapitel zusammen. Im Guide-Baukasten
(6.2) steht oben, wie viele Kapitel vorgeschlagen sind; weitere passende Themen stehen unter **Weitere Themen**, nicht angehakt.

# 5 Die Startseite

## 5.1 Kopfzeile

Links das PlatinVault-Logo, daneben dein **Trophäen-Fortschritt**, dein **Steam-Konto** (Name, Profilbild und mit
Steam-Key die Zahl deiner Spiele; ein Klick öffnet dein Profil) und die Aktionen:

| Symbol | Aktion |
|---|---|
| Datei | **Guide importieren** – eine Guide-Datei aus einer anderen PlatinVault-Installation laden (legt das Spiel samt Guide an) |
| Pokal-Abzeichen | **Trophäenschrank** – alle Spiele, die du auf 100 % hast (5.5) |
| Aa | **Darstellungsgröße** – öffnet ein kleines Fenster: mit − und + oder direkt über die Stufen 80 / 90 / 100 / 110 / 125 % werden Schriften und Kästen kleiner oder größer, die Seitenbreite bleibt gleich; „Auf 100 % zurücksetzen“ stellt die Standardgröße wieder her. Das Fenster bleibt beim Umschalten an seinem Platz, du kannst also mehrfach hintereinander klicken. Weicht die Größe von 100 % ab, zeigt ein kleines Etikett am Symbol den Wert. Praktisch bei 4K-Monitoren mit hoher Windows-Skalierung. |
| Weltkugel mit Kürzel (DE / EN) | **Sprache** – öffnet eine kleine Liste mit allen Sprachen, jede mit Flagge und ihrem Namen („Deutsch“, „English“); die gerade benutzte ist hervorgehoben und mit einem Häkchen markiert. Ein Klick schaltet die ganze App **sofort** um, ohne Neuladen, und merkt sich die Wahl. Die Sprache gilt für die Oberfläche, Datums- und Zahlenformate, die Steam-Erfolgstexte (Steams eigene Texte in dieser Sprache) und das Handbuch, das sich in der gewählten Sprache öffnet. Mit der Tastatur: Pfeiltasten wählen, Enter übernimmt, Esc schließt. Klappt das Speichern nicht, nennt eine Meldung unter der Kopfzeile den Grund und die bisherige Sprache bleibt. |
| Sonne/Mond | Farbschema: wechselt zwischen hell und dunkel (beim ersten Start gilt die Einstellung deines Betriebssystems) |
| Zahnrad | Einstellungen |
| ? | Hilfe: zeigt die Hinweise für die aktuelle Seite |
| Buch | **Handbuch** – dieses Handbuch direkt in der App |

Fährst du mit der Maus über ein Symbol, erklärt ein kurzer Hinweis, was es tut.

## 5.2 Favoriten und alle Spiele

Oben auf der Startseite steht ein schlankes **Übersichtsfeld**: links der Titel, rechts die Kennzahlen als kleine Abzeichen, darunter in einer Reihe die Suche, die Ansicht und der Regler für die
Kachelgröße.

- **Favoriten** sind die Spiele, die du mit einem **Stern** markiert hast – sie stehen als Kacheln auf der Startseite.
- **Alle Spiele** zeigt deine komplette Steam-Bibliothek (nur mit Steam-Key).
- Die Zahlen oben rechts (Spiele, Trophäen, Spiele auf Platin) beziehen sich immer auf die gewählte Ansicht. Für
  „Alle Spiele“ wertet PlatinVault deine Bibliothek im Hintergrund aus; solange das läuft, steht dort „Bibliothek wird
  ausgewertet“.
- Mit dem Regler **Kachelgröße** wählst du zwischen Groß, Kompakt, Sehr kompakt, Liste und Nur Text.
- Die **Suche** filtert die aktuelle Ansicht; unter der Reihe zeigt „Zeige x von y“, wie viele Spiele gerade zu sehen sind, und **Filter zurücksetzen** räumt die Suche wieder auf.
  Scrollst du nach unten, übernimmt eine schmale Leiste unter der Kopfzeile Suche, Ansicht und Regler.
- **Sortieren:** Rechts neben der Zählzeile wählst du die Reihenfolge der Kacheln – in beiden Ansichten. Zur Wahl stehen **Abschlussquote**
  (freigeschaltete Steam-Erfolge in Prozent), **Alphabetisch**, **Spielzeit**, **Zuletzt gespielt**, **Erscheinungsdatum**, **Größe**,
  **Metacritic-Wertung** und **Steam-Bewertungen**; bei den Favoriten kommt als erste **Eigene Reihenfolge** dazu. Der Knopf daneben dreht die Richtung um;
  jede Reihenfolge beginnt mit dem Sinnvollen zuerst (höchste Quote, A bis Z, neueste, größte, beste). Spiele, für die es keinen Wert gibt
  (zum Beispiel ohne Metacritic-Wertung oder nicht installiert), stehen immer am Ende – in beiden Richtungen. Eine Zeile unter der Zählzeile sagt in Worten, wie
  gerade sortiert ist und wie viele Spiele ohne Wert am Ende stehen; solange PlatinVault Werte noch nachlädt, steht dort, wie weit es ist, und die Liste ergänzt sich von selbst.
  Deine Wahl wird gespeichert (pro Ansicht) und gilt im Windows-Programm und im Browser gleich. Kacheln **ziehen** lässt sich nur in der **Eigenen Reihenfolge** (bei den
  Favoriten; ohne Suchfilter) – in jeder anderen Sortierung ist das Ziehen aus.
- **Woher die Werte kommen:** Spielzeit, zuletzt gespielt und Abschlussquote liefert Steam selbst. Erscheinungsdatum, Metacritic-Wertung, Steam-Bewertungen und
  die Größe auf der Platte liest PlatinVault, wenn es kann, aus den eigenen Dateien des Steam-Programms auf dem Spiele-PC – im Windows-Programm direkt, auf einem
  Unraid-Server über die Bridge. Fehlt das, fragt PlatinVault Erscheinungsdatum und Bewertungen langsam im Hintergrund bei Steams Store nach. **Die Größe gibt es nur mit Steam
  auf diesem PC oder der Bridge:** installierte Spiele mit ihrer echten Größe, die übrigen mit der geschätzten Installationsgröße (5.4). Nach dem Datum, an dem ein Spiel in die Bibliothek kam, kann PlatinVault nicht sortieren: Steams öffentliche Schnittstellen geben es nicht heraus, und die Steam-Dateien auf dem PC enthalten es nur verschlüsselt.

## 5.3 Spiele aufs Dashboard holen

Deine Spiele kommen **ausschließlich aus deiner Steam-Bibliothek** – es gibt keine Suche im Steam Store und kein Anlegen von Hand.
Solange noch kein Spiel auf der Startseite liegt, zeigt PlatinVault statt der Kacheln drei Schritte; sie zeigen den echten Stand
(Haken = erledigt) und ihre Schaltflächen öffnen direkt das Richtige:

1. **Steam-Konto verbinden:** „Mit Steam anmelden“ (oder in den Einstellungen, Reiter Steam).
2. **Steam-Web-API-Key eintragen:** Den Key holst du kostenlos bei Steam (der Link steht dabei), eingetragen wird er in den
   Einstellungen, Reiter Steam. Ohne ihn kann PlatinVault deine Bibliothek nicht lesen.
3. **Mit dem Stern aufs Dashboard holen:** „Alle Spiele öffnen“, sortieren nach „Zuletzt gespielt“, „Meiste Spielzeit“ oder Name
   und bei einem Spiel auf den **Stern** tippen. Das Spiel erscheint sofort als Kachel – ohne KI-Kosten. Den Guide erstellst du
   danach auf der Spielseite (6.2), wann immer du willst.

Dieselben drei Schritte erklärt die Tour; ihre Checkliste zeigt Konto und Key mit dem echten Stand.

- **Stern entfernen** nimmt das Spiel von der Startseite; Guide und Häkchen bleiben gespeichert und sind wieder da, sobald
  du den Stern erneut setzt.
- Einen **Löschen-Knopf** gibt es auf den Kacheln nicht (mehr): Der Stern reicht, um ein Spiel von der Startseite zu nehmen. Ein ganzes Spiel samt Guide und
  Häkchen zu löschen, kann die Oberfläche derzeit nicht.

## 5.4 Was eine Kachel zeigt

Cover, Name (bis zu zwei Zeilen), darunter die Genres von Steam, deine **Trophäen** mit Fortschrittsbalken und – in den Größen Groß und
Kompakt – feste Zeilen: gespielte Zeit, zuletzt gespielt, Bewertung (Steam-Rezensionen, Metacritic), Erscheinungsdatum, **Größe**
und bei den Favoriten die geschätzte Dauer für 100 %. Alles, wonach du sortieren kannst (5.2), steht auch auf der Kachel: die
Abschlussquote im Trophäenbalken, die übrigen Werte in diesen Zeilen. Jede Kachel zeigt dieselben Zeilen mit demselben Symbol, damit
alle gleich aussehen: Fehlt ein Wert (noch nicht gespielt, keine Angabe von Steam, keine Schätzung), steht dort ein grauer Strich „–“,
und ein Hinweis beim Darüberfahren nennt den Grund. Spiele mit allen Trophäen tragen das **Platin**-Kennzeichen.

- **Größe:** Ist das Spiel installiert, steht dort die echte **Größe auf der Platte** (so schreibt sie auch Steam, zum Beispiel „48,3 GB“).
  Ist es nicht installiert, steht dort die **Installationsgröße** mit „ca.“: PlatinVault schätzt sie aus den Daten, die Steam selbst über das
  Spiel kennt (Windows, Hauptsprache, ohne DLC). Sie kann leicht abweichen – bei manchen Spielen liegt sie etwas zu hoch, nie darunter. Kennt PlatinVault
  keine Größe (kein Steam auf diesem PC, keine Bridge), steht der graue Strich da.
- In den kleineren Größen: **Sehr kompakt** zeigt Titel, Trophäen und die Größe auf dem Cover; **Liste** und **Nur Text** zeigen Trophäen und
  Größe in der Zeile (Liste dazu zuletzt gespielt und den Balken); die übrigen Werte stehen in den Größen Groß und Kompakt.

Unter dem Cover steht eine schmale Leiste mit gleich breiten Feldern ohne Trennlinien: **Spielen**, **Favorit** und **Twitch**
(auf schmalen Kacheln nur mit Symbol, ein Hinweis nennt das Feld). **Spielen** (grün) startet das Spiel (6.1) – auch bei den
Spielen unter „Alle Spiele“. Das Feld ist nur da, solange das auch klappen kann, also wenn die Bridge verbunden ist oder
PlatinVault direkt auf dem Gaming-PC läuft; sonst teilen sich die übrigen beiden die Leiste. **Twitch** zeigt, wenn du in den Einstellungen unter
**Twitch & IGDB** die Zugangsdaten einer eigenen Twitch-Anwendung eingetragen hast und „Live-Streams“ an ist, in der Ecke eine kleine Zahl:
wie viele Streams gerade live zu diesem Spiel laufen („12“; ab 300 steht „300+“). Bei 0 steht keine Zahl, der Hinweis sagt dann, dass gerade niemand
streamt. Die Zahlen kommen im Hintergrund und aktualisieren sich alle paar Minuten; ohne Zugangsdaten bleibt das Feld wie es war.

## 5.5 Trophäenschrank

Der Trophäenschrank zeigt alle Spiele, die du auf **100 %** gebracht hast, wie in einer Vitrine: Hinter dunklem Rauchglas
stehen die Spiele auf Regalbrettern, jedes mit einer goldenen Plakette (Name, Datum und Uhrzeit der letzten Trophäe).

- **Öffnen:** Erst lädt der Schrank (eine dünne goldene Linie), dann gleiten die Glastüren zur Seite und das Licht geht an.
- **Neue Spiele:** Hast du seit deinem letzten Besuch ein Spiel auf 100 % gebracht, liegt es nicht einfach da: Sein Platz bleibt zuerst frei (eine glasige Karte), dann wird das Spiel mit einem kleinen Glanz auf das Regal gesetzt. Mehrere neue Spiele kommen nacheinander an die Reihe (bei vielen etwas schneller, die Gesamtdauer bleibt bei rund fünf Sekunden). Ein goldenes **Neu** markiert sie, bis du den Schrank wieder schließt. Danach gelten sie als gezeigt; am selben Stand (Windows-Programm und Browser) wird nichts doppelt abgespielt. Beim allerersten Öffnen des Schranks werden die Spiele, die schon auf 100 % sind, ohne Animation einsortiert. Ist in Windows „Animationen reduzieren“ an, blenden die Spiele nur ruhig ein. Bildschirmleser erhalten einmal die Ansage „Neu im Schrank: …“.
- **Türen:** Mit dem Tür-Symbol oben schließt und öffnest du die Glastüren wieder; ein Klick aufs geschlossene Glas öffnet sie.
- **Ansicht:** Das Regler-Symbol oben öffnet ein kleines Fenster mit zwei Dingen, die PlatinVault sich merkt:
  - **Beleuchtung:** **Spots** (eine Lampe über jedem Platz, mit weichem Lichtkegel) oder **LED-Leiste** (eine durchgehende Leiste
    pro Regalbrett; die ganze Reihe geht gemeinsam an, die Regalbretter nacheinander von oben nach unten).
  - **Leere Plätze zeigen:** Ist der Schalter an, stehen auf den noch freien Plätzen graue, glasige Platzhalter-Karten mit einem
    leeren Schild, damit der Schrank wie eine Vitrine aussieht, die darauf wartet, gefüllt zu werden. Platzhalter sind nur
    Dekoration (nicht anklickbar) und schieben deine Spiele nicht weg. Ausgeschaltet (Standard) bleiben die freien Plätze leer.
- **Größe:** Der Regler oben macht die Spiele kleiner (mehr pro Regalbrett) oder größer.
- **Viele Spiele:** Haben nicht alle Spiele auf einmal Platz, scrollen die Regalbretter gemeinsam seitlich – in jeder Größe. Du kannst mit dem Mausrad scrollen, auf dem Touchpad oder Touchscreen wischen, mit den Links-/Rechts-Pfeiltasten von Spiel zu Spiel springen oder die **Pfeil-Tasten** am linken und rechten Rand anklicken (jeder Klick blättert etwa eine Seite weiter). Ein dunkler Verlauf am Rand zeigt, wo es weitergeht; ein abgeblendeter Pfeil heißt: in dieser Richtung ist nichts mehr. Passt alles auf einmal, sind Pfeile und Verläufe nicht zu sehen.
- **Details:** Ein Klick auf ein Spiel zeigt Trophäen, Tage bis Platin, Spielzeit und die seltenste Trophäe – im Querformat von
  rechts, im Hochformat von unten. Esc schließt erst die Details, dann den Schrank.

# 6 Die Spielseite

## 6.1 Aufbau

Oben Cover, Titel und die Aktionen, darunter eine Leiste mit deinem Trophäen-Fortschritt und – rechts – den Bereichen
**Live-Trophäen**, **Statistiken** und **Story & Lore** (der aktive Bereich ist hervorgehoben).

Die **Kapitel** des Guides stehen immer in einer **Seitenleiste links**. Sie bleibt beim Scrollen stehen und ordnet die Kapitel
unter kleinen Überschriften wie **Einstieg**, **Story & Welt**, **Kampf & Taktik** oder **Fortschritt & Freischaltungen** (hat ein Guide nur eine Gruppe,
gibt es keine Überschriften). Neben jedem Kapitel zeigt eine Zahl, wie viel du abgehakt hast; das gewählte Kapitel ist farbig
markiert. Mit dem Pfeil oben in der Leiste klappst du sie zu schmalen Nummern ein, damit der Inhalt mehr Platz hat – PlatinVault
merkt sich das. Mit den Pfeiltasten (hoch/runter, Pos1/Ende) springst du durch die Kapitel. Auf dem Handy öffnest du die
Kapitel über den Knopf **Kapitel** in der Trophäenleiste: Sie schieben sich von links über die Seite und schließen sich wieder,
sobald du ein Kapitel gewählt hast (oder mit Esc oder einem Tipp neben die Leiste).

Ganz unten in der Kapitelleiste, unter einer feinen Trennlinie, stehen der Name der App, die **Version**, „Mit ♥ gemacht“, das Copyright und der Knopf
**Lizenzen** (auf der Startseite stehen dieselben Angaben im Fuß). In einer eingeklappten Leiste (und in einem sehr niedrigen Fenster) ist daraus ein kleines **Info-Symbol** (ein „i“ im Kreis)
geworden: Fährst du mit der Maus darüber, tippst du es an oder erreichst du es mit der Tab-Taste, öffnet sich eine Karte mit denselben Angaben. Sie bleibt offen,
solange der Mauszeiger auf ihr oder auf dem Symbol ist – du kommst also bequem zu **Lizenzen** – und schließt sich mit Esc, einem Klick daneben oder wenn du die Maus wegbewegst.

Die drei Bereiche der Leiste haben je eine eigene Farbe: **Live-Trophäen** gold, **Statistiken** blau, **Story & Lore** violett. Der Bereich, in dem du gerade bist, ist
in seiner Farbe ausgefüllt.

**Spiel starten.** Mit dem Spielen-Knopf startest du das Spiel in Steam, ohne zum Gaming-PC zu gehen. Auf der Seite des Spiels steht an derselben Stelle **Spiel läuft** (grüner pulsierender Punkt, nicht klickbar), solange genau dieses Spiel läuft; danach ist es wieder **Spielen**. Der Knopf (und der Hinweis
„Spiel läuft gerade“) erscheint nur, solange die Bridge verbunden ist oder PlatinVault direkt auf dem Gaming-PC läuft, und
verschwindet von selbst, wenn die Verbindung abbricht. Ist die PlatinVault
Bridge auf dem Gaming-PC verbunden (Kapitel 8), startet das Spiel dort – auch wenn du PlatinVault gerade auf dem Tablet
oder Handy bedienst; die Bridge zeigt dazu kurz „Starte …“ neben dem Symbol in der Taskleiste. Steam wird bei Bedarf mitgestartet,
und ist das Spiel noch nicht installiert, beginnt Steam die Installation. Ohne Bridge öffnet dein Browser die Steam-Adresse
selbst – das klappt nur, wenn du PlatinVault auf dem Rechner mit Steam im Browser geöffnet hast; dein Browser fragt dann
einmal nach, ob er Steam öffnen darf. Klappt der Start nicht, nennt PlatinVault den Grund, zum Beispiel „Steam wurde auf dem
Gaming-PC nicht gefunden“ oder „Der Gaming-PC hat den Startbefehl nicht abgeholt“ (dann läuft die Bridge nicht oder hat keine
Verbindung). Starten kannst du nur Spiele aus deiner Sammlung und deiner Steam-Bibliothek.

## 6.2 Der Guide

- **Kapitel mit Checklisten:** Hake erledigte Einträge ab; PlatinVault speichert sie sofort. Die Zahl neben jedem Kapitel
  zeigt, wie viel davon erledigt ist. Die Häkchen sind deine persönliche Liste – der Fortschritt eines Spiels richtet sich
  nach den Trophäen.
- **Guide vervollständigen:** Der **Guide-Baukasten** analysiert das Spiel und schlägt passende Kapitel vor (empfohlene
  sind vorausgewählt; ganz oben steht, wie viele es sind und zu welchem Umfang das passt). Du wählst per Klick, die KI recherchiert nur
  die fehlenden Kapitel. Die Spielart-Marke **Typisch** an einem Thema heißt: Das gehört bei dieser Art Spiel oft dazu – es wird nur
  vorgeschlagen, wenn die Analyse bestätigt, dass das Spiel es hat.
- **Guide mit deinem KI-Abo (ohne API-Key):** Du kopierst einen fertigen Prompt in den Chat deines Abos (zum Beispiel ChatGPT, Claude oder
  Gemini), fügst die Antwort hier wieder ein – PlatinVault greift nie auf dein Abo zu und schickt nichts an eine KI. Das ist erlaubt, weil du
  selbst im Chat sitzt. Du erreichst es im Guide-Baukasten (**Mit deinem KI-Abo (ohne API-Key)**), bei einer fehlgeschlagenen KI-Aktion und
  am Hinweis eines nicht recherchierten Kapitels; ohne gespeicherten Key öffnet **Guide vervollständigen** es direkt.
  - *Schritt 1, Kapitelliste:* **Prompt kopieren**, im Chat einfügen (am besten mit eingeschalteter Websuche) und die Antwort in das Feld
    darunter einfügen – auch mit Text davor oder in einem json-Codeblock, oder als Datei laden. Du siehst die vorgeschlagenen Kapitel wie im
    Baukasten und hakst an, was du willst. Wer keinen Bauplan-Prompt möchte, wählt die Themen selbst.
  - *Schritt 2, Kapitel für Kapitel:* Jedes Kapitel hat einen eigenen Prompt (**Prompt kopieren** oder **Als Datei exportieren**) und ein Feld für
    die Antwort. Die Antwort wird **beim Einfügen** geprüft: Gehört sie zum richtigen Spiel (Steam-AppID steht in jedem Prompt und in der
    Antwort)? Ist das JSON vollständig (nicht abgeschnitten)? Stimmen Abschnitte, Tabellenzeilen und Sprache? Fehler stehen vollständig
    darunter, dazu eine Vorschau (Titel, Abschnitte, Tabellenzeilen). **Kapitel speichern** legt das Kapitel genau wie ein KI-Ergebnis an; ein
    Haken zeigt, was schon gespeichert ist. Bei einem nicht recherchierten Kapitel ersetzt die Antwort den Hinweis.
  - Lässt dein Browser das Kopieren nicht zu (zum Beispiel bei einer Adresse mit http im Heimnetz), wird der Prompt angezeigt und markiert –
    dann Strg+C (am Mac Cmd+C).
- **Fortschritt einer KI-Aktion:** Solange die KI arbeitet (neuer Guide, Guide vervollständigen, Tab ergänzen, Story & Lore, Kapitel
  erneut recherchieren), zeigt eine Meldung, **was gerade passiert** („3 von 8 Kapiteln fertig – gerade: Beste Waffen …“) und seit
  wann es läuft. PlatinVault verspricht keine Dauer: Je nach Spiel und KI kann es einige Minuten dauern, die Seite darfst du offen
  lassen. Bleibt ein Schritt über fünf Minuten gleich, steht dort ein freundlicher Hinweis, dass er länger als gewöhnlich dauert – das ist
  kein Fehler. Fertige Kapitel werden sofort gespeichert. Wurde der Server währenddessen neu gestartet, steht dort: „Der Server wurde
  neu gestartet, die Recherche wurde unterbrochen – bereits fertige Kapitel sind gespeichert; erneut starten setzt fort.“ („Guide
  vervollständigen“ erkennt die fertigen Kapitel und recherchiert nur den Rest.)
- **Kapitel, das nicht recherchiert werden konnte:** Ein solches Kapitel zeigt einen ruhigen Hinweis mit dem Grund in einem Satz (die
  genaue Fehlermeldung der KI steht ausklappbar unter **Technische Details**) und dem Knopf **Kapitel erneut recherchieren**. Er
  recherchiert genau dieses Kapitel noch einmal – das Thema ist gespeichert, du musst nichts kopieren –, mit der KI aus deiner
  Einstellung (der Teil mit dem Pfeil lässt dich eine andere wählen; bei Quotenproblemen wechselt PlatinVault wie üblich selbst) und ersetzt
  den Hinweis, sobald es klappt. Klappt es wieder nicht, nennt der Hinweis den neuen Grund. Sind mehrere Kapitel betroffen, steht oben im
  Guide **Alle fehlgeschlagenen Kapitel erneut recherchieren**. Braucht das Spiel das Thema gar nicht, entfernt **Kapitel entfernen**
  (nach einer Rückfrage) das Kapitel, hier direkt im Hinweis (den Knopf gibt es auch bei jedem anderen Kapitel, s. u.).
- **Kapitel entfernen (jedes Kapitel):** Ein Kapitel, das du nicht brauchst – zum Beispiel ein Thema, das zu deinem Spiel gar nicht passt –,
  entfernst du mit **Kapitel entfernen** oben rechts über dem Inhalt des Kapitels. Eine Rückfrage nennt, was gelöscht wird: das Kapitel, seine
  Übersetzung und die Häkchen, die du in seinen Listen gesetzt hast. Deine Steam-Erfolge und alle anderen Kapitel bleiben unberührt; rückgängig
  machen lässt sich das Entfernen nicht, über **Guide vervollständigen** kann die KI fehlende Themen aber später erneut recherchieren. Danach
  öffnet sich das nächste Kapitel. Nicht entfernen lassen sich die Steam-Erfolgsliste des Guides (die Live-Trophäen lesen ihre Tipps daraus) und
  **Story & Lore** (ein eigener Bereich; er wird dort neu recherchiert).
- **Komplettlösung (immer ganz oben):** Gibt es für ein Spiel eine belastbare, vollständige Quelle, hat der Guide als erstes Kapitel die
  **Komplettlösung**: der Spielverlauf Schritt für Schritt in der Reihenfolge des Spiels, gegliedert nach Story-Kapiteln und Gebieten. An genau
  der Stelle, an der etwas wichtig wird, steht ein farbiger Hinweis mit eigenem Symbol: **Wichtig – unbedingt mitnehmen** (mit Fundort),
  **Verpassbar ab hier**, **Optional hier verfügbar** (Nebenquests und Co.) und **Tipp**. Rätsel erscheinen als eigener Block (siehe unten).
  Geschrieben ist sie in eigenen Worten, die Quellen stehen im Einleitungs-Kasten. Gibt es keine belastbare Quelle, legt PlatinVault das
  Kapitel nicht an – es wird nichts erfunden. Die Komplettlösung zählt nicht zur Kapitelzahl deines Umfangs; sie wird in mehreren Teilen
  recherchiert (die Meldung nennt den Teil, an dem gerade gearbeitet wird). Ein bestehender Guide bekommt sie über **Guide vervollständigen**
  und steht danach trotzdem ganz oben.
- **Rätsel und „Lösungsweg zeigen“:** Jedes Rätsel, jeder Code und jede Reihenfolge („in der richtigen Reihenfolge“) ist ein eigener Block. Er
  sagt offen, **was** zu tun ist, **wo** es ist (Gebiet, Landmarke, Weg), **was vorher nötig ist** und ob es **verpassbar** ist. Die Lösung bleibt
  verborgen, bis du sie willst: **Tipp zeigen** zeigt einen Hinweis nach dem anderen (vom vagen zum konkreten; derselbe Knopf zeigt den
  nächsten und zuletzt **Tipps verbergen**), **Lösungsweg zeigen** die vollständige Anleitung – jeder Schritt in der richtigen Reihenfolge
  mit Quelle –, und **Lösungsweg verbergen** blendet sie wieder aus. Du kannst es also erst selbst versuchen. PlatinVault merkt sich in
  diesem Browser, was du aufgedeckt hast. Mit Tastatur bedienbar (Tab, Enter, Leertaste). Bei manchen Rätseln hast du ein Kästchen **Erledigt**.
  Konnte die Recherche die Lösung nicht belegen, steht dort **Lösung nicht dokumentiert** – PlatinVault erfindet keine Codes oder Reihenfolgen.
  Neue Kapitel enthalten Rätsel so; ältere Kapitel behalten ihren Text. Wer die strukturierten Lösungen für ein bestehendes Spiel möchte, wählt
  in **Guide vervollständigen** das Thema **Rätsel & versteckte Mechaniken** (es wird nur vorgeschlagen, wenn das Spiel solche Rätsel hat)
  oder ergänzt mit **Tab ergänzen** ein Kapitel zu einem bestimmten Rätsel.
- **Titel:** Kapitel-Titel stehen nur in der Sprache des Guides, ohne Übersetzung in Klammern.
- **Tab ergänzen:** Ein einzelnes Kapitel zu einem Thema recherchieren lassen – Thema anklicken oder selbst beschreiben. Welche KI das
  übernimmt, steht am Knopf (siehe 4.3).
- **Exportieren / Als PDF:** Guide als Datei teilen (ohne deinen Fortschritt) oder ausdrucken.
- **Fortschritt zurücksetzen:** entfernt alle Häkchen dieses Spiels.
- **Guide in deiner Sprache:** Ein Guide ist in der Sprache geschrieben, in der er recherchiert wurde. Öffnest du ihn in der anderen
  Sprache der App, übersetzt ihn die KI aus deinen KI-Einstellungen automatisch – du siehst bis dahin das Original und unten eine
  Fortschrittsmeldung, danach tauscht sich der Inhalt von selbst. Das Original bleibt erhalten, deine Häkchen gelten in beiden
  Sprachen. Klappt es nicht (kein KI-Key, Kontingent erschöpft), nennt die Meldung den Grund und bietet „KI-Einstellungen“ und „Erneut
  versuchen“ an; neue Kapitel werden immer in der Sprache des Guides geschrieben und beim nächsten Öffnen mit übersetzt.
- Neue Spiele, über die es noch wenig gibt, bekommen den Hinweis „noch nicht dokumentiert“ statt erfundener Inhalte.

## 6.3 Steam-Trophäen (Live)

Alle Erfolge des Spiels – in der gewählten Sprache, so wie Steam sie liefert.

**Filter und Ansicht** stehen in einem Kasten über der Liste, in zwei Zeilen:

- **Was angezeigt wird:** links der Status (**Alle, Noch benötigt, Erhalten, Geheime**, jeweils mit der Anzahl), rechts die
  Stufe (**Alle, Gold, Silber, Bronze**).
- **Wie es angezeigt wird:** das Suchfeld (Name, Text oder – bei eingeschalteten Notizen – Notiz), die **Sortierung**, die **Ansicht** als Regler und rechts
  ein Auge-Knopf für die **geheimen Erfolge** (aufgedeckt oder verdeckt). Auf schmalen Fenstern, etwa am Handy, rutschen die
  Gruppen sauber untereinander; nichts im Kasten ändert seine Größe oder springt, auch wenn die Liste leer wird.

**Vier Ansichten** (Regler „Ansicht“): **Liste** (eine Zeile pro Erfolg), **Kacheln** (nur Symbol, die Einzelheiten stehen im
Hinweis und per Klick im Detailfenster), **Kompakt** (eine ruhige Karte mit Symbol-Streifen links, Name, Seltenheit, Beschreibung
und Freischaltdatum) und **Groß** (große Karte mit großem Symbol). In allen vier Ansichten sieht ein freigeschalteter Erfolg
gleich aus: ein **grünes Häkchen** in der Ecke des Symbols; offene Erfolge haben ein graues Symbol.

- **Geheime Trophäen** sind aufgedeckt und zeigen, was zu tun ist.
- **„So bekommst du ihn“:** Tipps aus dem Guide direkt bei der Trophäe.
- **Zähler-Trophäen** (zum Beispiel „Nutze 400-mal …“) zeigen einen Balken wie „244 / 10.000“, wenn das Spiel einen Zähler
  bei Steam hinterlegt hat.
- **„x % der Spieler“** ist die Seltenheit: So viele Steam-Spieler haben diese Trophäe – nicht dein Fortschritt.
- Eigene **Notizen** pro Trophäe: ein kleiner Notiz-Knopf an der Karte (Kompakt, Groß) und im Detailfenster öffnet das
  Eingabefeld; die Notiz steht danach auf der Karte (in Liste und Kacheln im Hinweis) und wird bei der Suche mitgefunden.
  Wer keine Notizen braucht, schaltet sie unter **Einstellungen → Live & Sound → „Notizen an Erfolgen“** aus: Dann
  verschwinden Notiz-Knöpfe, Notiztexte und die Notiz-Suche überall. Bereits geschriebene Notizen werden dabei **nicht
  gelöscht** – sie sind wieder da, sobald du die Option wieder einschaltest.

**So ordnet PlatinVault die Seltenheit ein:** Überall in der App – in den Statistiken und in der Bezeichnung, die ein
Trophäen-Popup trägt – gilt dieselbe Skala mit fünf Stufen:

- **Extrem selten:** unter 5 % der Spieler
- **Sehr selten:** 5 % bis unter 10 %
- **Selten:** 10 % bis unter 20 %
- **Ungewöhnlich:** 20 % bis unter 50 %
- **Häufig:** ab 50 %

Die **Farbe** einer Trophäe (Gold, Silber, Bronze) richtet sich nach demselben Anteil, aber mit eigenen Grenzen: **Gold**
haben höchstens 5 % der Spieler, **Silber** höchstens 25 %, **Bronze** mehr als 25 %. **Platin** bekommst du, wenn du alle
Trophäen eines Spiels hast. Liefert Steam für eine Trophäe keinen Anteil, nennt PlatinVault dafür keine Seltenheitsstufe und
rechnet sie in den Statistiken nicht mit.

## 6.4 Statistiken

Der Reiter **Statistiken** zeigt dir dein Spiel in Zahlen und Diagrammen. Es erscheint nur, was PlatinVault wirklich weiß:
Fehlt etwas (zum Beispiel weil das Spiel keine Erfolge hat oder noch nichts aufgezeichnet wurde), fehlt die Karte, und die
übrigen rücken nach. Bei jedem Diagramm schaltet das **Tabellen-Symbol** („Als Tabelle anzeigen“) auf dieselben Zahlen als
Tabelle um – praktisch zum Nachlesen und für Vorlese-Programme. Fährst du mit der Maus über ein Diagramm (oder wählst es mit
der Tastatur an), erscheinen die genauen Werte. **Aktualisieren** unten auf der Seite lädt alles neu.

### Die Kennzahlen ganz oben

- **Trophäen:** wie viele du von wie vielen hast, mit Fortschrittsbalken.
- **Wie exklusiv ist Platin?** Zeigt den höchsten Anteil aller Spieler, der überhaupt Platin haben kann: Wer alles hat, hat
  auch die seltenste Trophäe, und die haben weltweit nur so viele Spieler. Der echte Anteil kann niedriger sein – die Zahl ist
  eine **Obergrenze**.
- **Noch bis Platin:** eine **grobe Schätzung**, wie viele Stunden dir noch fehlen. Sie rechnet mit deinem bisherigen Tempo
  (Trophäen pro Spielstunde) auf die offenen Trophäen hoch und erscheint erst, wenn du mindestens drei Trophäen und eine
  Stunde Spielzeit hast. Die seltenen Trophäen am Ende dauern oft länger – der echte Wert liegt eher höher. Hast du
  Platin, steht hier stattdessen, nach wie viel Spielzeit und in wie vielen Tagen du es geschafft hast.
- **Spielzeit:** deine Gesamtspielzeit, die Zeit der letzten zwei Wochen und wann du zuletzt gespielt hast.

### Fortschritt

- **Trophäen pro Tag:** Ein Balken je Tag zeigt, wie viele Trophäen du an diesem Tag freigeschaltet hast; die Zahl steht über
  jedem Balken. Eingezeichnet sind nur Tage mit mindestens einer Trophäe – Tage ohne Freischaltung tauchen nicht auf, die
  Datumsleiste springt also von Freischalttag zu Freischalttag. Bei wenigen Tagen füllen die Balken die Karte (ein einzelner Tag
  ist ein einzelner, nicht zu breiter Balken). Kommen mehr Tage zusammen, als bequem nebeneinander passen, zeigt das Diagramm
  einen Ausschnitt der neuesten Tage; mit den Pfeilen „Frühere Tage“ und „Spätere Tage“ (oder den Pfeiltasten, am Handy durch
  Wischen) blätterst du durch die übrigen. Fährst du mit der Maus über einen Balken (oder tippst du darauf), siehst du das Datum
  und die Trophäen dieses Tages mit Uhrzeit. Das Diagramm erscheint ab zwei Trophäen mit bekanntem Zeitpunkt; Trophäen, für die
  Steam keinen Zeitpunkt kennt, fehlen darin.
- **Trophäen nach Stufe:** Platin, Gold, Silber und Bronze als vier Balken – wie viele du jeweils schon hast.

### Seltenheit

- **Seltenheit deiner Trophäen:** Wie viele Trophäen in welche der fünf Stufen fallen (siehe 6.3) und wie viele davon du schon
  hast. Darüber steht, wie viele der sehr seltenen (unter 10 %) du geschafft hast. Trophäen ohne Steam-Anteil zählen nicht
  mit; gibt es für ein Spiel gar keine Anteile, fehlt der ganze Abschnitt.
- **Die leichtesten offenen Trophäen:** Deine noch offenen Trophäen, die häufigste zuerst – das sind die schnellsten Erfolge.
  Zuerst zehn, die übrigen per Klick.

### Zeit

- **Wann du am erfolgreichsten bist:** Ein Raster aus Wochentag und Uhrzeit zeigt, wann du die meisten Trophäen freigeschaltet
  hast (in deiner Ortszeit) – von Grün (wenige) bis Gold (die meisten). Es erscheint ab drei Trophäen mit Zeitpunkt.
- **Spielzeit-Kalender:** Ein Feld pro Tag. Ein **goldener Punkt** markiert jeden Tag, an dem du eine Trophäe freigeschaltet
  hast – auch weit zurück, vor der Aufzeichnung. Die **Farbe** des Feldes zeigt, wie lange du gespielt hast, sobald PlatinVault
  das aufgezeichnet hat (je kräftiger, desto länger). Darüber stehen Steams Summen (gesamt und letzte zwei Wochen), darunter die
  Spielzeit dieser Woche, dieses Monats und dein längster Tag – diese Werte erst, wenn Minuten aufgezeichnet sind.
- **Spielzeit im Vergleich** *(nur mit IGDB, siehe unten)*: Die übliche Spielzeit für „nur die Story“, „mit Extras“ und „100 %“
  als Balken – und deine eigene Zeit als Linie darüber. Dazu steht, bei wie viel Prozent der üblichen Zeit du gerade bist.
  Die Werte sind Meldungen anderer Spieler bei IGDB, also Richtwerte.

### Community

- **Spielerzahl im Verlauf:** Wie viele Leute gleichzeitig spielen. Du wählst **7 Tage, 30 Tage, 90 Tage oder 1 Jahr**. Bis
  30 Tage siehst du die einzelnen Messungen (alle 30 Minuten), darüber den Tagesdurchschnitt mit dem Bereich vom Tiefst- bis
  zum Höchstwert des Tages. Lücken in der Linie bedeuten: PlatinVault lief in dieser Zeit nicht.
- **Rezensionen auf Steam:** Ein geteilter Balken zeigt, wie viele Rezensionen positiv und wie viele negativ sind (Blau und
  Orange statt Grün und Rot, damit man sie auch bei Farbschwäche unterscheidet).
- **Bewertungen im Vergleich** *(nur mit IGDB)*: IGDB-Kritiker, IGDB-Spieler, Steam-Rezensionen und Metacritic auf einer
  gemeinsamen Skala von 0 bis 100, mit der Zahl der Stimmen. Erscheint, sobald mindestens zwei Quellen vorliegen.

### Reihe *(nur mit IGDB)*

Gehört das Spiel zu einer Spielereihe, siehst du alle Teile in der Reihenfolge ihres Erscheinens, mit Cover, Jahr, dem
Hinweis, ob du sie in deiner Steam-Bibliothek oder in PlatinVault hast, und deinem Trophäen-Fortschritt. Das Spiel, in dem du
gerade bist, ist markiert; ein Klick auf einen Teil, den du in PlatinVault hast, öffnet dessen Spielseite. Die Karte
erscheint ab zwei Teilen.

### Über das Spiel

Erscheinungsdatum, Entwickler, Publisher, Genres, Zahl der Erfolge, Metacritic-Wertung, wie viele gerade spielen, das letzte
Update und die neuesten Neuigkeiten und Patches von Steam.

### Wichtig zu wissen

- **Der Verlauf füllt sich mit der Zeit.** Steam verrät nur, was *jetzt* ist. Darum merkt sich PlatinVault die Spielerzahl alle
  30 Minuten und deine Spielzeit jeden Tag selbst, **solange es läuft** – ab dem Tag, an dem es das zum ersten Mal tut. Für die
  Zeit davor gibt es keine Werte. Die Spielerzahl erscheint ab der ersten Messung (erst als Punkt, ab der zweiten als Linie).
  Die Spielzeit pro Tag ergibt sich aus dem Unterschied der Gesamtspielzeit zwischen zwei aufgezeichneten Tagen, sie erscheint
  also ab dem zweiten Tag. Ein kleiner Hinweis „Wird seit … aufgezeichnet“ zeigt, ab wann gemessen wird.
- **IGDB ist freiwillig.** Die drei Karten mit „nur mit IGDB“ erscheinen nur, wenn du in den Einstellungen unter **Twitch &
  IGDB** die Zugangsdaten einer eigenen Twitch-Anwendung (Client-ID und Client-Secret) eingetragen hast. IGDB ist für private,
  nicht-kommerzielle Nutzung kostenlos. Ohne diese Zugangsdaten fehlen genau diese Karten – alles andere bleibt unverändert.
- **Geschätzt ist gekennzeichnet:** „Noch bis Platin“ und „Wie exklusiv ist Platin?“ sind Schätzungen bzw. Obergrenzen und
  erklären sich über das Info-Symbol an der Karte.

## 6.5 Story & Lore

Auf Wunsch recherchiert die KI einen eigenen Reiter mit Story, Charakteren, Welt und Hintergründen. Er **enthält Spoiler**
und bleibt verdeckt, bis du ihn bewusst öffnest; PlatinVault fragt dafür pro Spiel nach.

# 7 Trophäen-Popups und Sounds

Im Reiter **Live & Sound** der Einstellungen:

- Popups an- oder ausschalten, **Position** auf dem Bildschirm wählen: neun Plätze (links oben bis rechts unten) oder **Vollbild**.
  Ein Klick auf eine Position zeigt sofort eine Vorschau. Bei **Vollbild** gehört das ganze Fenster für etwa fünf Sekunden der
  neuen Trophäe: großer Pokal in der Farbe der Stufe, Name, Beschreibung, Seltenheit und Spiel – im Quer- wie im Hochformat. Ein Klick
  irgendwo oder die Taste Esc beendet das Popup früher; danach geht es mit der nächsten Trophäe weiter.
- **Trophäen-Popup anzeigen:** **Nur in der App**, **Nur im Spiel** oder **Beides** (Standard). „Im Spiel“ heißt: als Overlay über dem
  Spiel – das gibt es nur im Windows-Programm (siehe 11.3). Im Browser und auf Unraid sind die beiden Spiel-Wahlen ausgegraut, dort
  erscheint das Popup immer in der App. Der Ton erklingt bei jeder Trophäe **genau einmal**, auch bei „Beides“.
- **Sound-Thema:** Kristall, Orchester oder Spieluhr – ein Klick spielt die Probe. Alle Klänge sind eigene Aufnahmen
  echter Instrumente.
- Lautstärke einstellen, **Sound probehören** und den **Platin-Moment testen**.
- **Notizen an Erfolgen** ein- oder ausschalten (Standard: an; siehe 6.3).
- **Beim Spielstart automatisch zum Spiel wechseln** ein- oder ausschalten (Standard: an; siehe unten).

Startest du ein Spiel aus deiner Sammlung, springt jedes geöffnete PlatinVault-Fenster zu den **Live-Trophäen** dieses Spiels,
du musst nicht erst dorthin navigieren. Das geht, sobald die Bridge auf dem Gaming-PC verbunden ist oder PlatinVault selbst auf
dem Gaming-PC läuft. Gewechselt wird nur bei einem echten Start – nicht beim Neuladen der Seite, während das Spiel schon läuft,
und nicht beim Beenden. Ist gerade ein Fenster offen, steht der Cursor in einem Eingabefeld oder arbeitet eine KI-Aufgabe,
bleibst du, wo du bist: Ein Hinweis meldet den Start und bietet **Zum Spiel wechseln** an. Läuft ein Spiel, das du noch nicht
angelegt hast, nennt ein Hinweis nur den Namen – „noch kein Guide vorhanden“.

Popups erscheinen für alle Spiele, die in PlatinVault angelegt sind. Ein Popup nennt den Anteil der Spieler, die die Trophäe
haben; seine Farbe (Bronze, Silber, Gold) und seine Bezeichnung folgen der Seltenheit (siehe 6.3).

# 8 PlatinVault Bridge (Server auf einem anderen Rechner)

Läuft PlatinVault auf einem Server, NAS oder Unraid, meldet die Bridge deine Trophäen vom Gaming-PC in Echtzeit.

1. In den Einstellungen, Reiter **PlatinVault Bridge**, auf **herunterladen** klicken. Das ZIP enthält das Programm und ist
   bereits mit deinem Server verbunden. Jeder neue Download erzeugt einen neuen Schlüssel; der alte gilt dann nicht mehr.
   Im ZIP liegt auch dieses Handbuch als PDF (Deutsch und Englisch); später öffnest du es jederzeit mit einem Rechtsklick
   auf das Symbol der Bridge und **Handbuch öffnen**.
2. ZIP auf dem Gaming-PC entpacken und `PlatinVault.Bridge.exe` starten. Das Einrichtungsfenster prüft die Verbindung und
   installiert die Bridge (Autostart mit Windows, Eintrag im Startmenü).
3. Danach sitzt die Bridge als Symbol unten rechts in der Taskleiste. Ein Rechtsklick zeigt den Status und das Menü.
4. Neue Versionen holt sich die Bridge selbst vom PlatinVault-Server.
5. Die Bridge spricht die Sprache von PlatinVault (Deutsch oder Englisch): Das ZIP bringt sie schon mit, und stellst du in
   PlatinVault die Sprache um, zieht die Bridge nach – Menü, Statusfenster und Handbuch wechseln mit, die Einrichtungsfenster
   beim nächsten Öffnen. Vor der ersten Verbindung nutzt sie die Anzeigesprache von Windows.
6. **Deinstallieren:** über Windows „Apps“ oder das Menü der Bridge.

Die Bridge startet auf Wunsch auch Spiele: Tippst du in PlatinVault auf „Spielen“, richtet die Bridge auf dem Gaming-PC den
Start in Steam aus (siehe 6.1). Dafür fragt sie den Server alle paar Sekunden nach Aufträgen; ein Auftrag, den sie nicht
abholt, verfällt nach 30 Sekunden.

Läuft PlatinVault direkt auf dem Gaming-PC, brauchst du keine Bridge.

# 9 Daten und Datenschutz

- **Alles bleibt bei dir:** Spiele, Guides, Häkchen und Einstellungen liegen in deiner eigenen Datenbank.
- **API-Keys** (Steam, KI) werden verschlüsselt im Appdata-Ordner gespeichert – nicht in der Datenbank – und danach nur
  gekürzt angezeigt. Unter Windows sind sie zusätzlich an dein Windows-Konto gebunden. Gib den Appdata-Ordner nicht
  offen im Netzwerk frei: Wer den ganzen Ordner hat, hat auch die Keys.
- **Steam-Daten** ruft PlatinVault nur für dein eigenes Konto und auf deinen Wunsch ab.
- **KI-Anbieter** bekommen für die Recherche den Spielnamen und öffentliche Spielinformationen – keine persönlichen Daten.
- PlatinVault hat bewusst **keine Anmeldung** und ist für einen Nutzer gedacht. Betreibe es im Heimnetz, nicht offen im
  Internet.
- **Lizenz und Quellcode:** PlatinVault ist freie Software unter der **GNU Affero General Public License, Version 3**
  (AGPL-3.0-only). Du darfst es kostenlos nutzen, untersuchen, ändern und weitergeben; wer es weitergibt oder eine geänderte
  Fassung anderen über ein Netzwerk anbietet, muss den Quellcode unter derselben Lizenz anbieten. Zum Nutzen musst du nichts
  akzeptieren. Der Quellcode steht unter `github.com/sushiflux/PlatinVault`.
- **Lizenzen:** Die Lizenz von PlatinVault (mit dem Knopf **Quellcode**) und die Lizenztexte der enthaltenen Open-Source-Komponenten liest du
  in der App: auf der Startseite unten auf **Lizenzen** klicken, auf einer Spielseite unten in der Kapitelleiste (ist sie eingeklappt, erst das **Info-Symbol** öffnen), oder in den Einstellungen, Reiter **System**, auf **Lizenzen öffnen**. Dasselbe liegt als `LICENSE` und `THIRD-PARTY-NOTICES.md` im Bridge-ZIP, im Programmordner der Bridge und im Programmordner des Windows-Programms (`docs`);
  die Dateien lassen sich mit jedem Texteditor öffnen.

# 10 Probleme lösen

| Problem | Was hilft |
|---|---|
| Etwas lädt nicht oder zeigt Fehler | Einstellungen → **System**: zeigt, ob Backend, Datenbank, Datenordner, Steam, Bridge und Keys in Ordnung sind. |
| „PlatinVault-Server nicht erreichbar – der Browser hat keine Antwort bekommen“ | Der Server antwortet gar nicht. Läuft das Programm bzw. der Container (auf Unraid in der Docker-Liste), und ist die Netzwerkverbindung zum Server in Ordnung? Danach die Aktion noch einmal ausführen. |
| Die Bibliothek bleibt leer | Steam Web API-Key prüfen; im Steam-Profil die Spieldetails auf öffentlich stellen. |
| Keine Live-Popups | Ist das Spiel in PlatinVault angelegt? Läuft PlatinVault auf einem Server: Ist die Bridge verbunden (Symbol in der Taskleiste, System-Check)? |
| Popup kommt, aber ohne Ton | Steht der Hinweis **„Ton vom Browser gesperrt“** da, hat der Browser den Ton zurückgehalten: Browser spielen Töne erst nach einem Klick oder Tastendruck auf der Seite, zum Beispiel nach dem Neuladen. Ein Klick gibt den Ton frei, **Verpassten Ton abspielen** spielt ihn nach. Ohne diesen Hinweis: Lautstärke unter Einstellungen → **Live & Sound** erhöhen (neben lautem Spielsound gehen leise Töne unter) und prüfen, ob der Browser-Tab stumm geschaltet ist. |
| Die KI meldet ein Limit | Unter Einstellungen → KI-Recherche zeigt die Liste **Deine KIs**, welche KI wann ein Problem bekam. Mit eingeschaltetem automatischem Wechsel macht PlatinVault mit der nächsten KI weiter und sagt es dir; sonst wählst du selbst eine andere KI („Mit … erneut versuchen“). „Verbindung echt testen“ zeigt bei Gemini zusätzlich den Grund: Tageslimit (später erneut versuchen), Modell überlastet (kurz warten) oder keine Websuche im Gratis-Tarif (Google-Projekt mit Abrechnung nötig). |
| Ein Kapitel zeigt „konnte nicht recherchiert werden“ | Auf **Kapitel erneut recherchieren** klicken (Details siehe 6.2). Der Grund steht im Hinweis; bei einem Quoten- oder Key-Problem zuerst unter Einstellungen → **KI-Recherche** nachsehen oder über den Pfeil am Knopf eine andere KI wählen. |
| „Der Server wurde neu gestartet, die Recherche wurde unterbrochen“ | Der Server war während der Recherche kurz weg (Update, Neustart). Fertige Kapitel sind gespeichert; starte dieselbe Aktion noch einmal, sie macht dort weiter. |
| Erfolge auf Englisch | Das Spiel hat bei Steam keine deutschen Texte; mit KI-Key übersetzt PlatinVault sie automatisch. |
| Kein Zähler bei einer Trophäe | Das Spiel hat bei Steam keinen Zähler hinterlegt. |
| PlatinVault startet nicht: „Diese Datenbank wurde bereits von PlatinVault … benutzt“ | Die Datenbank kommt von einer neueren Version (zum Beispiel einer Beta). Damit nichts kaputtgeht, startet die ältere Version nicht und ändert nichts. Installiere die genannte Version (oder neuer) wieder oder warte auf das nächste Release (Kapitel 12). |

# 11 PlatinVault für Windows

Auf deinem Gaming-PC kannst du PlatinVault auch als **Windows-Programm** installieren. Es ist dieselbe App wie im Browser – mit
denselben Seiten, Guides und Einstellungen –, nur in einem eigenen Programmfenster. Alles läuft auf deinem PC: der PlatinVault-Server,
seine Datenbank und die Live-Trophäen. Eine Bridge brauchst du dafür nicht.

Was das Programm zusätzlich kann:

- **Trophäen-Popup über dem Spiel** (Overlay), an der Stelle, die du unter Einstellungen → **Live & Sound** für die Popups gewählt
  hast – auch als Vollbild.
- **Ton ohne Klick:** Der Trophäen-Sound spielt sofort, auch wenn du vorher nichts angeklickt hast.
- **Windows-Benachrichtigungen** für jede freigeschaltete Trophäe (sie bleiben im Info-Center von Windows).
- **Symbol neben der Uhr** und **Start mit Windows**, damit Trophäen auch ohne geöffnetes Fenster erkannt werden.
- **Strg+Alt+G** öffnet den Guide zum laufenden Spiel über dem Spiel.
- **Spiele starten ohne Rückfrage:** „Spielen“ startet das Spiel direkt in Steam.

## 11.1 Installieren

1. Lade `PlatinVault-Setup-<Version>.exe` aus den Releases von PlatinVault auf GitHub herunter und starte die Datei mit einem
   Doppelklick – **nicht** mit „Als Administrator ausführen“. PlatinVault wird nur für dein Windows-Konto installiert und braucht keine
   Administratorrechte.
2. **Windows-SmartScreen:** Die Installationsdatei ist noch nicht digital signiert. Windows zeigt deshalb beim ersten Start
   „Der Computer wurde durch Windows geschützt“. Klicke auf **Weitere Informationen** und dann auf **Trotzdem ausführen**.
3. Der Installer führt dich durch sechs Schritte:

   | Schritt | Was passiert |
   |---|---|
   | Sprache | Deutsch oder English – gilt sofort für den Installer und ist die Vorauswahl der App. |
   | Lizenz | Der Lizenztext (GNU AGPL v3) zum Lesen und der Link zum Quellcode – nichts anzukreuzen. |
   | Datenbank | Nur zur Info: Es gibt nichts einzurichten. PlatinVault speichert alles in einer Datei auf deinem PC. Du installierst und tippst nichts. |
   | Ort & Autostart | Programmordner (Vorschlag: `…\AppData\Local\Programs\PlatinVault`), **Mit Windows starten** (empfohlen), Startmenü und Desktop-Verknüpfung. Deine Daten liegen getrennt davon. |
   | Installieren | Kopiert das Programm, prüft die Fenster-Laufzeit (Microsoft Edge WebView2) und legt die Datenbank an. Fehlt WebView2 (selten, nur ältere Windows-10-PCs), lädt der Installer es von Microsoft nach – dafür braucht er eine Internetverbindung. |
   | Fertig | **PlatinVault jetzt starten** ist vorausgewählt. |

4. Beim ersten Öffnen wählst du die Sprache der App und verbindest dein Steam-Konto (Kapitel 4).

Die Installation läuft nur auf Windows 10 oder 11 (64 Bit).

**Das Programmfenster.** Beim ersten Start öffnet PlatinVault **maximiert**; danach merkt es sich, wie du es verlassen hast
(maximiert oder in deiner Größe und Position). Ist der Bildschirm von damals nicht mehr angeschlossen, öffnet es auf dem
Hauptbildschirm. Die Leiste oben ist PlatinVaults eigene Titelleiste im Stil der App (hell oder dunkel wie die App): links Pokal und
Name, rechts **Minimieren**, **Maximieren** bzw. **Verkleinern** und **Schließen** (rot beim Darüberfahren). Sie verhält sich wie jedes
Windows-Fenster: an der Leiste ziehen verschiebt, ein Doppelklick maximiert bzw. verkleinert, an den Rand oder in eine Ecke ziehen
teilt den Bildschirm (Andocken), unter Windows 11 zeigt das Verweilen auf dem Maximieren-Knopf die Andock-Layouts, an allen Rändern und
Ecken lässt sich die Größe ändern, Rechtsklick auf die Leiste oder **Alt+Leertaste** öffnet das Fenstermenü, **Windows-Taste+Pfeiltasten**
funktionieren wie gewohnt. Auf Bildschirmen mit unterschiedlicher Skalierung (z. B. 4K mit 250 % neben einem 100-%-Bildschirm) bleibt
alles scharf und gleich groß.


## 11.2 Das Symbol neben der Uhr

Das Fenster zu schließen beendet PlatinVault **nicht** – es läuft neben der Uhr weiter, damit Live-Trophäen und Overlay
funktionieren. Ein Klick auf das Symbol öffnet das Fenster, ein Rechtsklick zeigt das Menü:

| Eintrag | Bedeutung |
|---|---|
| PlatinVault öffnen | Holt das Fenster nach vorn. |
| Guide zum laufenden Spiel | Öffnet die Seite des Spiels, das gerade läuft, über dem Spiel (wie Strg+Alt+G). |
| Trophäen-Overlay über dem Spiel | Schaltet das Overlay ein oder aus (Haken = an). Dasselbe wie „Trophäen-Popup anzeigen“ in der App: an = „Beides“, aus = „Nur in der App“. |
| Overlay testen | Zeigt eine Test-Trophäe im Overlay (ohne Ton), damit du siehst, wo das Popup erscheint. |
| Windows-Benachrichtigungen | Schaltet die Benachrichtigungen ein oder aus. |
| Mit Windows starten | Autostart bei der Anmeldung ein oder aus. |
| Handbuch öffnen | Dieses Handbuch als PDF. |
| Sicherungsordner öffnen / Protokolle öffnen | Für den Notfall und für Rückfragen beim Support. |
| Update auf … installieren | Erscheint, wenn eine neue Version bereitsteht. |
| Beenden | Sichert die Datenbank und schließt PlatinVault. |

Startest du PlatinVault ein zweites Mal, kommt einfach das vorhandene Fenster nach vorn.

## 11.3 Overlay über dem Spiel

Wird eine Trophäe freigeschaltet, während du spielst, erscheint über dem Spiel **genau dasselbe Popup wie in der App** – dieselbe Karte
mit derselben Animation, auf Wunsch das Vollbild-Popup und der Platin-Moment. Es erscheint auf dem Monitor, auf dem das Spiel läuft, an
der Stelle, die du unter Einstellungen → **Live & Sound** gewählt hast. Das Overlay nimmt dem Spiel nie den Fokus, lässt jeden Klick
durch (ein Klick schließt es deshalb nicht – es verschwindet nach einigen Sekunden von selbst) und taucht nicht in Alt+Tab oder der
Taskleiste auf.

Wo das Popup erscheint, bestimmt die Einstellung **Trophäen-Popup anzeigen** (Einstellungen → Live & Sound):

| Wahl | Was passiert |
|---|---|
| Beides (Standard) | Overlay über dem Spiel; schaust du gerade auf das PlatinVault-Fenster, zeigt nur die App ihr Popup – nie zweimal auf einem Bildschirm. |
| Nur im Spiel | Immer das Overlay, auch über dem PlatinVault-Fenster; die App selbst zeigt kein Popup. |
| Nur in der App | Kein Overlay; das Popup erscheint nur im PlatinVault-Fenster. |

Den Ton spielt immer das PlatinVault-Fenster, einmal pro Trophäe – auch wenn es im Hintergrund oder neben der Uhr läuft. Der Haken
**Trophäen-Overlay über dem Spiel** im Menü des Symbols ist dieselbe Einstellung (an = „Beides“, aus = „Nur in der App“).

> [!HINWEIS]
> Das Overlay funktioniert, wenn das Spiel im **Fenstermodus** oder im **randlosen Vollbild** („Borderless“, „Fenster-Vollbild“) läuft.
> Im **exklusiven Vollbild** gehört der Bildschirm allein dem Spiel – dann sieht man kein Fenster darüber, auch das Overlay nicht.
> Die Windows-Benachrichtigung und der Ton kommen trotzdem.

## 11.4 Strg+Alt+G: der Guide über dem Spiel

**Strg+Alt+G** öffnet PlatinVault über dem Spiel mit der Seite des Spiels, das gerade läuft (war sie schon offen, bleibt dein Kapitel
offen). Noch einmal Strg+Alt+G bringt dich zurück zum Spiel. Hat ein anderes Programm die Tastenkombination schon belegt, nimmt
PlatinVault **Strg+Alt+Umschalt+G**; ist auch die belegt, gibt es keine Tastenkombination – den Guide öffnest du dann über das Symbol
neben der Uhr.

## 11.5 Deine Daten und Sicherungen

- Programm und Daten liegen getrennt: das Programm im Programmordner, deine Daten unter `…\AppData\Local\PlatinVault` (Datenbank,
  Sicherungen, verschlüsselte API-Keys, Protokolle). Ein Update ersetzt nur das Programm.
- Die Datenbank ist eine einzige Datei (`database\platinvault.db`). Es läuft kein eigener Datenbank-Dienst, und von außen ist nichts erreichbar.
- **Sicherungen** entstehen automatisch: beim Start, alle 6 Stunden und beim Beenden (also auch vor jedem Update) – aber nur, wenn sich
  etwas geändert hat. Die 20 neuesten liegen in `backups\recent`, dazu eine pro Tag für 60 Tage in `backups\daily`.
- **Sicherung zurückspielen:** PlatinVault über das Symbol beenden, dann in der Eingabeaufforderung
  `"<Programmordner>\PlatinVault.exe" --restore "<Pfad zur Sicherung>.db"` ausführen. PlatinVault prüft die gewählte Sicherung, sichert vorher
  den aktuellen Stand (er liegt danach als neueste Sicherung in `backups\recent`) und ersetzt dann die Datenbank. Danach PlatinVault normal starten.

## 11.6 Updates

PlatinVault sieht von selbst nach, ob es eine neue Version gibt – so oft, wie unter **Nach Updates suchen** eingestellt (Standard: täglich) –, nur Releases, oder auch Betas, wenn du unter Einstellungen →
**System** → **Updates** den Schalter **Beta-Versionen erhalten** eingeschaltet hast (Kapitel 12). Gibt es eine, erscheint im Menü des
Symbols **Update auf … installieren** und eine Benachrichtigung. Sofort nachsehen kannst du mit **Nach Updates suchen** im Menü des
Symbols oder mit **Jetzt nach Updates suchen** in den Einstellungen; dort installierst du ein gefundenes Update auch direkt mit
**Update installieren**. Nach deiner Bestätigung lädt PlatinVault den Installer, prüft seine Prüfsumme, beendet sich (mit Sicherung)
und der Installer aktualisiert das Programm. Deine Daten bleiben unverändert.

## 11.7 Deinstallieren

Windows-Einstellungen → **Apps** → **PlatinVault** → **Deinstallieren**. Ein Fenster fragt **„Daten behalten?“**:

- **Daten behalten (empfohlen):** Datenbank, Sicherungen und API-Keys bleiben. Installierst du PlatinVault später wieder, geht es genau
  dort weiter.
- **Alles löschen:** löscht Datenbank, alle Sicherungen, gespeicherte API-Keys und Protokolle endgültig.

Danach beendet sich PlatinVault, entfernt Autostart, Verknüpfungen und den Eintrag unter „Apps“ und löscht den Programmordner.

## 11.8 Probleme unter Windows

| Problem | Was hilft |
|---|---|
| „Der Computer wurde durch Windows geschützt“ | **Weitere Informationen** → **Trotzdem ausführen** (die Datei ist noch nicht signiert). |
| Der Installer meldet „läuft mit Administratorrechten“ | Die Datei per Doppelklick starten, nicht mit „Als Administrator ausführen“. |
| Das Fenster bleibt leer bzw. meldet, dass WebView2 fehlt | Die „Microsoft Edge WebView2 Runtime“ von Microsofts Website installieren (Evergreen) oder den Installer erneut ausführen. |
| PlatinVault startet nicht und zeigt einen Datenbank-Fehler | Der vollständige Grund steht im Fenster. **Protokolle öffnen** zeigt die Protokolle (für den Support). |
| „Die Datenbankdatei … ist beschädigt“ | PlatinVault ändert nichts an der Datei. Stelle die letzte Sicherung wieder her (Abschnitt 11.5); die beschädigte Datei bleibt dabei als Sicherheitskopie erhalten. |
| Kein Overlay im Spiel | Läuft das Spiel im exklusiven Vollbild? Auf „randloses Vollbild“ umstellen. Steht **Trophäen-Popup anzeigen** auf „Nur in der App“ (Haken **Trophäen-Overlay** im Menü des Symbols fehlt)? Bei „Beides“ bleibt das Overlay weg, solange das PlatinVault-Fenster vorne ist. **Overlay testen** zeigt eine Test-Karte; meldet es „nicht verfügbar“, steht der Grund im Protokoll. |
| Strg+Alt+G tut nichts | Ein anderes Programm hat die Tasten belegt – PlatinVault nimmt dann Strg+Alt+Umschalt+G oder keine; der Guide geht immer über das Symbol neben der Uhr. |
| Keine Live-Trophäen nach dem Neustart des PCs | Ist **Mit Windows starten** im Menü des Symbols angehakt? |

# 12 Updates und Beta-Versionen

Von PlatinVault gibt es zwei Arten von Versionen:

- **Releases** – die normalen, fertigen Versionen. Das ist der Standard und für die meisten die richtige Wahl.
- **Betas** – Vorabversionen mit den neuesten Funktionen. Sie sind fertig gebaut und getestet, können aber noch Fehler haben. Eine
  Beta zeigt hinter der Versionsnummer ein **β**, zum Beispiel `v2026.11.12 (13.557 β)`.

## 12.1 Der Schalter „Beta-Versionen erhalten“

Unter Einstellungen → **System** → **Updates** steht – auf jeder Plattform – der Schalter **Beta-Versionen erhalten**. Er ist
anfangs **aus**.

- **Aus:** Du bekommst nur Releases.
- **An:** Du bekommst Betas und Releases – immer die neueste von beiden.

Es gibt **nie einen Schritt zurück**: Schaltest du ihn aus, während eine Beta läuft, bleibt diese Beta, bis ein Release erscheint, das
mindestens so neu ist. Dieses Release wird dir dann automatisch angeboten. Schaltest du ihn ein, bekommst du die neueste Beta, sobald
sie neuer ist als deine Version.

Darunter siehst du deine installierte Version und mit **Jetzt nach Updates suchen** das Ergebnis: „Du bist auf dem neuesten Stand“,
„Neue Beta … verfügbar“ oder „Neues Release … verfügbar“ – mit einem Link zu den Release-Notizen. PlatinVault fragt die
veröffentlichten Versionen bei GitHub ab (ohne Anmeldung; nur ein paar Mal am Tag, eine Suche von Hand höchstens einmal pro Minute).
Klappt das nicht, steht der vollständige Grund dort, zum Beispiel keine Internetverbindung oder ein ausgeschöpftes Abfrage-Kontingent
bei GitHub.

Gibt es für deine Installation eine neuere Version, erscheint in der Kopfzeile jeder Seite ein kleines **Update-Symbol** (Pfeil nach oben mit grünem Punkt). Ein Klick zeigt die neue Version, einen Link zu den Release-Notizen und – im Windows-Programm – **Update installieren**; bei Unraid, Docker und NAS steht dort kurz, wie du den Container aktualisierst. Mit **Alle Update-Einstellungen** springst du zu diesem Abschnitt. Ist alles aktuell, die Prüfung nicht möglich oder dies ein Dev-Build, siehst du kein Symbol; Fehler der Prüfung stehen nur hier in den Einstellungen.

Wie oft PlatinVault von selbst nach Updates sucht, stellst du in diesem Abschnitt unter **Nach Updates suchen** ein: **Bei jedem Start**, **Alle 4 Stunden**, **Täglich** (Standard), **Wöchentlich**, **Monatlich** oder **Nie (nur von Hand)**. Das gilt für jede Plattform, auch für das Windows-Programm. Von Hand suchst du mit **Jetzt nach Updates suchen** hier – oder ganz unten auf jeder Seite: ein Klick auf die Versionsnummer öffnet **Version & Updates** mit dem Knopf **Nach Updates suchen**, dem Zeitpunkt der letzten Prüfung und dem Ergebnis. Wird dabei eine neue Version gefunden, erscheint sofort das Update-Symbol in der Kopfzeile.

## 12.2 Windows-Programm

Das Programm aktualisiert sich selbst (Kapitel 11.6): Mit **Update installieren** in den Einstellungen oder im Menü des Symbols neben
der Uhr wird die gefundene Version heruntergeladen, geprüft und installiert.

## 12.3 Unraid, NAS und Docker

Ein Container kann sein eigenes Programm nicht austauschen – das macht dein Server. PlatinVault merkt sich deine Wahl, meldet neue
Versionen und zeigt dir in den Einstellungen genau, was zu tun ist. Welche Versionen dein Container bekommt, entscheidet sein
**Image-Tag**:

| Image | Du bekommst |
|---|---|
| `ghcr.io/sushiflux/platinvault:latest` | nur Releases |
| `ghcr.io/sushiflux/platinvault:beta` | Betas und Releases (immer die neueste von beiden) |

Stelle den Schalter in der App passend zum Tag ein. So stellst du den Tag um – deine Daten in Datenbank und Appdata bleiben dabei
erhalten:

- **Unraid:** Docker-Reiter → PlatinVault-Container anklicken → **Edit** → bei **Repository** den Tag ändern → **Apply**. Neue Versionen
  holst du danach wie gewohnt mit **Check for Updates** und **Update**. In den Community Applications kannst du den Tag schon bei der
  Installation wählen.
- **Docker Compose:** in der `docker-compose.yml` die Zeile `image:` ändern und im selben Ordner `docker compose pull && docker compose up -d`
  ausführen.
- **Andere NAS:** in der Container-Verwaltung deines NAS das Image des PlatinVault-Containers auf den gewünschten Tag umstellen und den
  Container mit dem neuen Image neu erstellen.

Zurück von `:beta` auf `:latest` stellst du erst um, wenn ein Release erschienen ist, das mindestens so neu ist wie deine Beta – vorher
wäre es ein Schritt zurück auf eine ältere Version. Die Einstellungen sagen dir, wann es so weit ist.

## 12.4 Schutz deiner Daten

Startest du eine ältere PlatinVault-Version mit einer Datenbank, die schon eine neuere Version (zum Beispiel eine Beta) benutzt hat,
startet PlatinVault nicht und ändert **nichts** an der Datenbank. Die Meldung nennt die Version, die du brauchst (im Container-Protokoll
auf Deutsch und Englisch, im Windows-Programm in einem eigenen Fenster). Installiere diese Version (oder eine neuere) wieder oder warte auf
das nächste Release.
