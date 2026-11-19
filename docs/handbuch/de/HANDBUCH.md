---
title: PlatinVault
subtitle: Handbuch
tagline: Live-Trophäen für deine Steam-Spiele und 100-%-Guides an einem Ort – so richtest du PlatinVault ein und nutzt es im Alltag.
version: v2026.11.22
date: 2026-11-19
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
- **Spiele von anderen Plattformen:** Auch Spiele von PlayStation, Xbox, Epic Games, GOG, Nintendo Switch, Ubisoft Connect, EA app
  und Battle.net legst du von Hand an. Die KI recherchiert die Trophäen- bzw. Erfolgsliste, du hakst sie selbst ab (Abschnitt 6.6).

Erfolge heißen bei Steam „Achievements“; PlatinVault nennt sie **Trophäen** und zeigt sie in den Stufen Bronze, Silber, Gold
– je nachdem, wie selten sie unter allen Spielern sind.

> [!HINWEIS] Dieses Handbuch beschreibt den aktuellen Stand von PlatinVault – es zeigt nur, was schon eingebaut ist.

# 2 So läuft PlatinVault

PlatinVault ist eine Web-App: Du öffnest sie im Browser – auf dem PC, Tablet oder Handy. Dahinter läuft ein kleiner Server,
der deine Spiele, Guides und Häkchen in einer Datenbank speichert.

| Wo läuft der Server? | Woher kommen die Live-Trophäen? |
|---|---|
| Auf deinem Gaming-PC | PlatinVault liest Steam direkt auf diesem PC – nichts weiter nötig. |
| Auf einem anderen Rechner (Server, NAS, Unraid) | Auf dem Gaming-PC läuft die kleine **PlatinVault Bridge** (Windows oder Linux) und meldet neue Trophäen sofort an den Server (Kapitel 8). |

Auf dem Gaming-PC gibt es PlatinVault auch als **Windows-Programm** mit eigenem Fenster, Overlay über dem Spiel und eigener Datenbank –
siehe Kapitel 11. Auf einem Linux-Gaming-PC (Desktop, Steam Deck im Desktop-Modus, Bazzite) gibt es ein **Linux-Paket**, das Steam
ebenfalls direkt liest – siehe Kapitel 13. **Auf Linux ist die Erkennung der Trophäen noch experimentell** (Kapitel 13).

# 3 Installation auf Unraid, Linux und NAS

PlatinVault gibt es als Container-Image. Auf Unraid legst du es aus der Vorlage an, auf einem normalen Linux-Rechner (und auf NAS-Systemen mit Compose-Unterstützung) startest du es mit einer fertigen Compose-Datei. Die Abschnitte 3.1 bis 3.3 beschreiben die drei Wege; Windows hat ein eigenes Programm (Kapitel 11), und für den Linux-PC, auf dem Steam läuft, gibt es ein eigenes Paket (Kapitel 13).

Der Rechner braucht einen 64-Bit-x86-Prozessor mit x86-64-v2-Unterstützung (praktisch jeder PC- oder Server-Prozessor ab etwa 2013).

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
   die Sprache deines Browsers) und dein **Farbschema** (Hell oder Dunkel). Beides kannst du später jederzeit oben in der Kopfzeile ändern.
   Danach führt dich eine kurze **Tour** durch die App. Du kannst sie jederzeit über das **?** oben rechts
   wieder starten.

**Eigene Domain / Reverse-Proxy:** Rufst du PlatinVault über einen Namen mit eigener Domain auf (zum Beispiel `pv.example.com` über einen Reverse-Proxy, also einen vorgeschalteten Webserver), trage diesen Namen in der Vorlage unter „Show more settings“ bei `PLATINVAULT_ALLOWED_HOSTS` ein. Mehrere Namen trennst du mit `;`, `*.example.com` gilt für alle Namen darunter. Nötig ist das nicht für IP-Adressen, `localhost`, Namen ohne Punkt (zum Beispiel `tower`) und Namen auf `.local`, `.lan`, `.home.arpa`, `.internal` oder `.fritz.box`. Hintergrund: PlatinVault lehnt andere Namen ab, damit fremde Webseiten es nicht über eine Täuschung im Namenssystem (DNS-Rebinding) ansprechen können. Rufst du PlatinVault schon jetzt so auf, trage den Namen nach dem Update einmal ein. Schreibt dein Proxy den Namen um, kommt für die Live-Popups zusätzlich `PLATINVAULT_ALLOWED_ORIGINS=https://pv.example.com` dazu.

Updates bekommst du über Unraid wie bei jedem anderen Container; deine Daten im Appdata-Ordner bleiben erhalten. Ob dein
Container nur Releases oder auch Vorabversionen bekommt, entscheidet sein Image-Tag – siehe Kapitel 12.

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
- **Eigene Domain / Reverse-Proxy:** Wie bei Unraid (Abschnitt 3.1): Rufst du PlatinVault unter einer eigenen Domain auf, trage sie in der Datei `.env` als `PLATINVAULT_ALLOWED_HOSTS=pv.example.com` ein (mehrere mit `;`, `*.example.com` für alle Namen darunter) und starte mit `docker compose up -d` neu. IP-Adressen, `localhost`, Namen ohne Punkt, `.local`, `.lan`, `.home.arpa`, `.internal` und `.fritz.box` brauchen nichts. Schreibt dein Proxy den Namen um, setze zusätzlich `PLATINVAULT_ALLOWED_ORIGINS=https://pv.example.com`.
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

Öffne die **Einstellungen** mit dem **Zahnrad** in der Kopfzeile (auf jeder Seite; 5.1). Die Einstellungen sind eine eigene Seite mit Reitern; der gewählte Reiter steht in der Adresse, ein Neuladen bringt dich also an dieselbe Stelle zurück. Einstellungen, die nur an oder aus sind, haben einen
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
- **Steam-Ordner (optional):** Wo PlatinVault direkt auf dem Gaming-PC läuft (Windows-Programm oder Linux-Paket), liest es die Steam-Dateien
  dieses Rechners. Findet es Steam nicht selbst, trägst du im Reiter **Steam** unter **Steam-Ordner (optional)** den Ordner ein, in dem
  der Unterordner `steamapps` liegt (zum Beispiel `/home/<name>/.local/share/Steam`), und klickst **Übernehmen**. PlatinVault prüft den
  Ordner sofort und sagt, was nicht stimmt. **Wieder selbst suchen** nimmt die Einstellung zurück. Unter Einstellungen → **System** zeigt
  die Zeile **Steam auf diesem Rechner**, welcher Ordner benutzt wird und wie er gefunden wurde; **Gesuchte Orte anzeigen** listet alle
  Stellen, an denen PlatinVault gesucht hat.

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

**Dein KI-Abo als Auswahl:** Bei Aktionen, die du auch ohne API-Key mit deinem eigenen KI-Abo erledigen kannst (**Guide vervollständigen**,
**Tab ergänzen**, **Story & Lore**), gibt es zusätzlich den Eintrag **Mein KI-Abo** („Prompt kopieren, Antwort einfügen – ohne API-Key“).
Er öffnet den Weg von Hand, bei dem du den Prompt in den Chat deines Abos kopierst und die Antwort wieder einfügst (6.2 und 6.5). Du
findest ihn im Auswahlfenster und im Pfeil-Menü des Knopfes. Hast du noch keinen Key gespeichert, öffnet sich dieser Weg gleich. Der Zauberstab
**Guide vervollständigen** fragt **immer** zuerst „Welche KI möchtest du benutzen?“, in jedem Modus; deine Standard-KI ist dort vorgewählt.
Hast du bei einer dieser drei Aktionen zuletzt **Mein KI-Abo** genommen, merkt sich PlatinVault das: Beim nächsten Mal ist es vorgewählt (mit „Zuletzt benutzt“) und der Knopf von **Tab ergänzen** und **Story & Lore** startet damit. Wählst du dort danach eine KI mit Key, ist die Erinnerung wieder weg; Abbrechen ändert nichts. Deine Standard-KI bleibt unverändert.

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

# 5 Navigation und Übersicht

## 5.1 Kopfzeile und Navigation

PlatinVault hat auf jeder Seite oben dieselbe **Kopfzeile**. Eine **Navigation** links gibt es nur auf der Seite eines Spiels. Einen Seitenfuß gibt es nirgends.

### Die Kopfzeile

Links das PlatinVault-Logo mit dem Namen; ein Klick führt zur Übersicht. Rechts stehen die Werkzeuge, auf jeder Seite dieselben:

| Symbol | Aktion |
|---|---|
| Profilbild mit Name | **Steam-Benutzer** – dein verbundenes Steam-Konto mit Profilbild, Name und – mit Steam-Key – der Zahl deiner Spiele. Ein Klick öffnet dein Steam-Profil. Ohne verbundenes Konto fehlt der Eintrag. |
| Pfeil nach oben mit grünem Punkt | **Update verfügbar** – nur sichtbar, wenn es eine neue Version gibt (Kapitel 12). Das Symbol steht immer als erstes in der Reihe der Werkzeuge, die anderen rutschen dadurch nicht. |
| Pokal | **Trophäenschrank** – öffnet den Schrank mit allen Spielen auf 100 % (5.5). |
| Aa | **Darstellungsgröße** – öffnet ein kleines Fenster: mit − und + oder direkt über die Stufen 80 / 90 / 100 / 110 / 125 % werden Schriften und Kästen kleiner oder größer; „Auf 100 % zurücksetzen“ stellt die Standardgröße wieder her. Das Fenster bleibt beim Umschalten an seinem Platz, du kannst also mehrfach hintereinander klicken. Weicht die Größe von 100 % ab, zeigt ein kleines Etikett am Symbol den Wert. Praktisch bei 4K-Monitoren mit hoher Windows-Skalierung. |
| Weltkugel mit Kürzel (DE / EN) | **Sprache** – öffnet eine kleine Liste mit allen Sprachen, jede mit Flagge und ihrem Namen („Deutsch“, „English“); die gerade benutzte ist hervorgehoben und mit einem Häkchen markiert. Ein Klick schaltet die ganze App **sofort** um, ohne Neuladen, und merkt sich die Wahl. Die Sprache gilt für die Oberfläche, Datums- und Zahlenformate, die Steam-Erfolgstexte (Steams eigene Texte in dieser Sprache) und das Handbuch, das sich in der gewählten Sprache öffnet. Mit der Tastatur: Pfeiltasten wählen, Enter übernimmt, Esc schließt. Klappt das Speichern nicht, nennt eine Meldung den Grund und die bisherige Sprache bleibt. |
| Sonne/Mond | **Farbschema:** wechselt zwischen hell und dunkel (die erste Wahl triffst du auf dem Willkommensbildschirm). Das Aussehen selbst wählst du unter Einstellungen → System → Design. |
| Zahnrad | **Einstellungen** – öffnet die Einstellungen (Kapitel 4) auf dem Reiter, den du zuletzt benutzt hast. |
| ? | **Hilfe:** zeigt die Hinweise für die aktuelle Seite noch einmal |
| Buch | **Handbuch** – dieses Handbuch direkt in der App |
| „i“ im Kreis | **Über PlatinVault** – öffnet die Seite mit Version, Neuerungen und Lizenzen (siehe unten). |

Fährst du mit der Maus über ein Symbol, erklärt ein kurzer Hinweis, was es tut. Die Werkzeuge der einzelnen Seiten (zum Beispiel **Guide importieren** auf der Übersicht oder die Aktionen eines Spiels) stehen im Inhalt der jeweiligen Seite, nicht in der Kopfzeile.

**Schmale Fenster.** Wird das Fenster schmaler, wandern seltener gebrauchte Werkzeuge in ein gemeinsames Menü mit drei Punkten (**Weitere Funktionen**), damit jedes Symbol bedienbar bleibt:

- **Unter 880 Pixel Breite:** Darstellungsgröße, Handbuch und Über PlatinVault. Der Steam-Benutzer zeigt dann nur noch das Profilbild; Name und Zahl der Spiele stehen im Hinweis.
- **Unter 640 Pixel Breite (Handy):** zusätzlich Farbschema, Hilfe und der Steam-Benutzer.

### Das Design wählen: Leicht oder Klassisch

Das **Design** bestimmt, wie PlatinVault aussieht. Es gibt zwei Möglichkeiten:

- **Leicht** (Standard): ruhige Flächen, dünne Linien und die Schrift Inter.
- **Klassisch**: der frühere Look von PlatinVault mit runderen Ecken, kräftigerer Schrift, Verläufen und Glanz.

Nur das Aussehen ändert sich. Seiten, Navigation und Funktionen bleiben in beiden Designs genau gleich.

So wechselst du das Design:

1. Öffne die **Einstellungen** (Zahnrad in der Kopfzeile) und wechsle zu **System**.
2. Wähle ganz oben im Abschnitt **Design** die Option **Leicht** oder **Klassisch**.

Die Wahl gilt **sofort**, du musst nichts neu laden und nichts speichern. Sie gilt für diese ganze PlatinVault-Installation, also auf jedem Gerät: im Browser, auf Handy und Tablet und im Windows-Programm, auch für die Trophäen-Popups über dem Spiel. Klappt das Speichern nicht, kehrt das bisherige Design zurück. Eine Meldung nennt dann den vollständigen Grund; mit **Kopieren** übernimmst du den Text.

Das **Farbschema** (hell oder dunkel) bleibt eine eigene Wahl: Du schaltest es mit dem Sonne/Mond-Symbol in der Kopfzeile um, und es funktioniert in beiden Designs.

### Die Navigation auf der Spielseite

Nur auf der Seite eines Spiels steht links eine Navigation – sie gehört zu diesem einen Spiel und enthält nur den Guide. Die Übersicht, **Alle Spiele**, die Einstellungen und **Über PlatinVault** haben keine Navigation; dort kommst du über die Kopfzeile weiter. Zur Übersicht kommst du jederzeit mit einem Klick auf das Logo in der Kopfzeile. Von oben nach unten:

- **Spielkopf:** das Cover und der Titel des Spiels.
- **Kapitel:** die Kapitel des Guides unter ihren Gruppenüberschriften (hat ein Guide nur eine Gruppe, gibt es keine Überschriften). Neben einem Kapitel zeigt eine Zahl, wie viel du davon abgehakt hast; ein Warnsymbol heißt, dass die Recherche des Kapitels fehlgeschlagen ist (6.2).
- **Story & Lore** (6.5).

Die Seite, auf der du bist, ist hervorgehoben.

**Schmale Navigation.** Mit dem Knopf oben in der Navigation (Seitenleisten-Symbol) klappst du sie zu einer schmalen Leiste mit nur Symbolen ein, damit der Inhalt mehr Platz hat. In der Leiste steht für jede Kapitelgruppe ein Symbol. Ein Klick darauf (oder Enter bzw. Leertaste) öffnet daneben eine kleine Liste mit den Kapiteln dieser Gruppe; mit den Pfeiltasten wählst du ein Kapitel, Esc schließt die Liste. Ein Hinweis beim Darüberfahren nennt jedes Symbol. Ein zweiter Klick auf den Knopf klappt die Navigation wieder auf. PlatinVault merkt sich die Wahl in diesem Browser bzw. Programm.

**Auf dem Handy.** Unter etwa 768 Pixel Breite ist die Navigation versteckt. In der Kopfzeile links öffnet ein **Menü-Knopf** sie als Schublade, die sich von links über die Seite schiebt. Sie schließt sich, sobald du einen Eintrag wählst, mit dem Knopf **Navigation schließen**, mit Esc oder mit einem Tipp neben die Schublade.

**Über PlatinVault.** Diese Seite zeigt den Namen, die laufende **Version** (ein Klick öffnet **Version & Updates**: dort suchst du von Hand nach Updates, siehe Kapitel 12), **Neuerungen** (öffnet **Das ist neu in dieser Version**), „Mit ♥ gemacht“, die **Lizenz** mit dem Knopf **Lizenzen** und den Link zum **Quellcode**. Ein Copyright-Hinweis steht in der App nicht.

## 5.2 Favoriten und alle Spiele

Die Seite **Übersicht** und die Seite **Alle Spiele** sehen gleich aus und haben dieselben Werkzeuge. Oben steht der Seitentitel, rechts daneben die Knöpfe **Spiel hinzufügen** (ein Spiel von Hand anlegen, siehe 5.3) und **Guide importieren** (eine Guide-Datei aus einer anderen PlatinVault-Installation laden; das legt das Spiel samt Guide an). Darunter folgen die Kennzahlen als kleine Abzeichen, dann in einer Reihe die Suche, der Umschalter zwischen Favoriten und allen Spielen und der Regler für die Kachelgröße.

- **Übersicht** zeigt deine **Favoriten**: die Spiele, die du mit einem **Stern** markiert hast, als Kacheln.
- **Alle Spiele** zeigt deine komplette Steam-Bibliothek (nur mit Steam-Key) und dazu alle Spiele, die du von Hand hinzugefügt hast. Auch diese Spiele haben einen Stern: Mit ihm legst du sie auf die Übersicht oder nimmst sie wieder herunter. Hast du keine Steam-Bibliothek, aber Spiele von Hand angelegt, funktioniert die Seite trotzdem. Du erreichst sie über den Umschalter im Werkzeugfeld.
- Die Zahlen (Spiele, Trophäen, Spiele auf Platin) beziehen sich immer auf die gewählte Seite. Für
  „Alle Spiele“ wertet PlatinVault deine Bibliothek im Hintergrund aus; solange das läuft, steht dort „Bibliothek wird
  ausgewertet“.
- Mit dem Regler **Kachelgröße** wählst du zwischen Groß, Kompakt, Sehr kompakt, Liste und Nur Text.
- Die **Suche** filtert die aktuelle Ansicht; unter der Reihe zeigt „Zeige x von y“, wie viele Spiele gerade zu sehen sind, und **Filter zurücksetzen** räumt die Suche wieder auf.
- **Kompakte Leiste:** Scrollst du nach unten, blendet sich eine schmale Leiste unter der Kopfzeile ein, Schritt für Schritt, genau in dem Maß, wie die Suchzeile nach oben aus dem Bild wandert. Scrollst du wieder nach oben, verblasst sie genauso. Solange du in der Leiste tippst oder ein Menü aus ihr geöffnet hast, bleibt sie sichtbar. Sie enthält Suche, **Favoriten / Alle Spiele**, Sortierung und Kachelgröße. In schmalen Fenstern ist die Suche ein Lupen-Knopf, und Kachelgröße und Ansicht liegen hinter dem Knopf **Ansicht und Kachelgröße**.
- **Sortieren:** Rechts neben der Zählzeile wählst du die Reihenfolge der Kacheln – in beiden Ansichten. Zur Wahl stehen **Abschlussquote**
  (freigeschaltete Steam-Erfolge in Prozent), **Alphabetisch**, **Spielzeit**, **Zuletzt gespielt**, **Erscheinungsdatum**, **Größe**,
  **Metacritic-Wertung**, **Steam-Bewertungen**, **IGDB-Kritiker**, **IGDB-Spieler** und **Gesamtwertung**; bei den Favoriten kommt als erste **Eigene Reihenfolge** dazu. Der Knopf daneben dreht die Richtung um;
  jede Reihenfolge beginnt mit dem Sinnvollen zuerst (höchste Quote, A bis Z, neueste, größte, beste). Spiele, für die es keinen Wert gibt
  (zum Beispiel ohne Metacritic-Wertung oder nicht installiert), stehen immer am Ende – in beiden Richtungen. Eine Zeile unter der Zählzeile sagt in Worten, wie
  gerade sortiert ist und wie viele Spiele ohne Wert am Ende stehen; solange PlatinVault Werte noch nachlädt, steht dort, wie weit es ist, und die Liste ergänzt sich von selbst.
  Deine Wahl wird gespeichert (pro Ansicht) und gilt im Windows-Programm und im Browser gleich. Kacheln **ziehen** lässt sich nur in der **Eigenen Reihenfolge** (bei den
  Favoriten; ohne Suchfilter) – in jeder anderen Sortierung ist das Ziehen aus.
- **Die Bewertungs-Sortierungen:** **IGDB-Kritiker** sortiert nach der Wertung der Fachpresse bei IGDB, **IGDB-Spieler** nach der Wertung der IGDB-Nutzer (beide nur mit den Zugangsdaten einer eigenen Twitch-Anwendung, siehe 6.4). Die **Gesamtwertung** ist der Durchschnitt aller Bewertungen, die ein Spiel hat: Steam-Rezensionen (Anteil positiv in Prozent), Metacritic, IGDB-Kritiker und IGDB-Spieler. Hat ein Spiel nur eine Bewertung, zählt diese. Spiele ganz ohne Bewertung stehen am Ende.
- **Woher die Werte kommen:** Spielzeit, zuletzt gespielt und Abschlussquote liefert Steam selbst. Erscheinungsdatum, Metacritic-Wertung, Steam-Bewertungen und
  die Größe auf der Platte liest PlatinVault, wenn es kann, aus den eigenen Dateien des Steam-Programms auf dem Spiele-PC – im Windows-Programm direkt, auf einem
  Unraid-Server über die Bridge. Fehlt das, fragt PlatinVault Erscheinungsdatum und Bewertungen langsam im Hintergrund bei Steams Store nach. **Die Größe gibt es nur mit Steam
  auf diesem PC oder der Bridge:** installierte Spiele mit ihrer echten Größe, die übrigen mit der geschätzten Installationsgröße (5.4). Nach dem Datum, an dem ein Spiel in die Bibliothek kam, kann PlatinVault nicht sortieren: Steams öffentliche Schnittstellen geben es nicht heraus, und die Steam-Dateien auf dem PC enthalten es nur verschlüsselt.

## 5.3 Spiele aufs Dashboard holen

Es gibt zwei Wege: Steam-Spiele holst du mit dem **Stern** aus deiner Steam-Bibliothek. Spiele von allen anderen Plattformen legst du mit **Spiel hinzufügen** von Hand an. Eine Suche im Steam Store gibt es nicht.

### Steam-Spiele mit dem Stern

Solange noch kein Spiel auf der Startseite liegt, zeigt PlatinVault statt der Kacheln drei Schritte; sie zeigen den echten Stand
(Haken = erledigt) und ihre Schaltflächen öffnen direkt das Richtige:

1. **Steam-Konto verbinden:** „Mit Steam anmelden“ (oder unter Einstellungen, Reiter Steam). Danach landest du wieder im Reiter Steam der Einstellungen.
2. **Steam-Web-API-Key eintragen:** Den Key holst du kostenlos bei Steam (der Link steht dabei), eingetragen wird er in den
   Einstellungen, Reiter Steam. Ohne ihn kann PlatinVault deine Bibliothek nicht lesen.
3. **Mit dem Stern aufs Dashboard holen:** „Alle Spiele öffnen“, sortieren nach „Zuletzt gespielt“, „Meiste Spielzeit“ oder Name
   und bei einem Spiel auf den **Stern** tippen. Das Spiel erscheint sofort als Kachel – ohne KI-Kosten. Den Guide erstellst du
   danach auf der Spielseite (6.2), wann immer du willst.

Unter den drei Schritten steht der andere Weg: **Oder ein Spiel von Hand hinzufügen**. Die Tour erklärt beide Wege; ihre Checkliste zeigt Konto und Key mit dem echten Stand.

- **Stern entfernen** nimmt das Spiel von der Startseite; Guide und Häkchen bleiben gespeichert und sind wieder da, sobald
  du den Stern erneut setzt.
- Steam-Spiele haben keinen **Löschen-Knopf**: Der Stern reicht, um sie von der Startseite zu nehmen. Nur ein von Hand hinzugefügtes Spiel lässt sich ganz entfernen (siehe unten).

### Ein Spiel von Hand hinzufügen

Damit holst du Spiele auf die Übersicht, die nicht in deiner Steam-Bibliothek liegen. Sie bekommen einen Guide und eine Trophäenliste wie Steam-Spiele. Steam-Spiele kannst du hier **nicht** hinzufügen: Sie kommen nur aus deiner Bibliothek, mit dem Stern.

**So fügst du ein Spiel hinzu:**

1. Klicke auf der Übersicht oder bei **Alle Spiele** oben auf **Spiel hinzufügen**. Das Fenster **Spiel hinzufügen** öffnet sich.
2. Suche das Spiel oder lege es ohne Suche an (zwei Wege, siehe unten).
3. Wähle die **Plattform**. Bei PlayStation wählst du danach **PS5** oder **PS4**.
4. Klicke auf **Hinzufügen**. Das Spiel erscheint als Kachel auf der Übersicht. Mit **Zum Spiel** öffnest du seine Seite.

**Weg 1: Suche über IGDB.** IGDB ist eine große Online-Spieledatenbank. Du kannst sie nur nutzen, wenn du in den Einstellungen im Reiter **Twitch & IGDB** die Zugangsdaten einer eigenen Twitch-Anwendung eingetragen hast (siehe 6.4). Ohne diese Zugangsdaten sagt das Fenster, dass IGDB nicht antwortet, und zeigt dir den zweiten Weg.

1. Gib ins Feld **Spiel suchen** den **Namen**, die **IGDB-Nummer** oder den **IGDB-Link** ein, zum Beispiel `Astro Bot`, `90001` oder `https://www.igdb.com/games/astro-bot`.
2. Klicke in der Trefferliste das richtige Spiel an. Zu jedem Treffer stehen Erscheinungsjahr und Plattformen. Gibt es das Spiel schon in deiner Sammlung, steht dort **Schon hinzugefügt** mit der Plattform.
3. Das Fenster zeigt die Spielfakten aus IGDB: Erscheinungsdatum, Entwickler, Genres, die geschätzte Dauer für 100 % und die Wertungen. Titelbild und Fakten kommen von IGDB.
4. Mit **Anderes Spiel wählen** gehst du zurück zur Trefferliste.

IGDB nennt zu jedem Spiel seine Plattformen; sie sind im Fenster mit **IGDB** markiert. Nennt IGDB nur „PC“, markiert PlatinVault keinen PC-Shop – du wählst selbst den Shop, in dem du das Spiel besitzt.

**Weg 2: Von Hand, ohne IGDB.** Diesen Weg nimmst du, wenn du keine Twitch-Zugangsdaten hast, IGDB das Spiel nicht kennt oder du es lieber selbst eingibst (Knopf **Nicht dabei? Ohne IGDB hinzufügen**).

1. Gib den **Titel** ein.
2. Lade ein **Titelbild** (Cover) hoch: Ziehe eine Bilddatei in das Feld oder wähle **Datei wählen**. Erlaubt sind PNG, JPEG, WebP und GIF bis höchstens 5 MB, kein SVG. Hochkant (2:3) passt am besten. Das Bild kannst du mit **Ersetzen** tauschen oder mit **Entfernen** weglassen.
3. Erscheinungsdatum, Entwickler, Genres und Dauer fehlen zunächst. Die KI recherchiert sie später (siehe 6.6).

**Die Plattform.** Du wählst sie in einem Raster mit neun Feldern (auf dem Handy als Liste): **Steam**, **PlayStation**, **Xbox**, **Epic Games**, **GOG**, **Nintendo Switch**, **Ubisoft Connect**, **EA app** und **Battle.net**. Wählst du **Steam**, weist das Fenster dich auf den Stern unter „Alle Spiele“ hin; ein Steam-Spiel lässt sich hier nicht hinzufügen.

- **PlayStation:** Wähle darunter **PS5** oder **PS4**. Beide haben **getrennte Trophäenlisten**. Wähle die Fassung, die du spielst. Ein PS4-Spiel, das du auf der PS5 spielst, bekommt die PS4-Liste.
- **Nintendo Switch:** Auf der Switch gibt es keine Trophäen. Das Spiel bekommt nur einen Guide.
- **Die Plattform steht nach dem Hinzufügen fest.** Du kannst sie nicht ändern. Hast du dich vertan, entfernst du das Spiel (siehe unten) und fügst es neu hinzu.
- **Dasselbe Spiel auf zwei Plattformen** ist möglich: Du legst es zweimal an, jede Plattform ist ein eigener Eintrag mit eigener Liste und eigenen Häkchen. Dasselbe Spiel auf derselben Plattform (und derselben PlayStation-Fassung) gibt es nur einmal; PlatinVault bietet dir dann an, das vorhandene Spiel zu öffnen.

**Cover ändern und Spiel entfernen.** Auf der Seite eines von Hand hinzugefügten Spiels stehen in der Reihe der Werkzeuge zwei zusätzliche Knöpfe:

- **Cover ändern:** Lädt ein eigenes Titelbild hoch und ersetzt das jetzige. **Eigenes Cover entfernen** nimmt das Bild wieder weg. Das Titelbild liegt in der PlatinVault-Datenbank und ist deshalb in den automatischen Sicherungen enthalten.
- **Spiel entfernen:** Löscht das Spiel samt Guide, Trophäenliste mit allen Häkchen und Titelbild, nach einer Rückfrage. Das lässt sich nicht rückgängig machen; du kannst das Spiel danach neu hinzufügen.

## 5.4 Was eine Kachel zeigt

Oben links auf dem Cover steht ein kleines Feld mit der **Plattform** in Kurzform: **Steam**, **PS5**, **PS4**, **Xbox**, **Epic**, **GOG**, **Switch**, **Ubisoft**, **EA** oder **Battle.net**. Spiele aus deiner Steam-Bibliothek zeigen **Steam**. Ein Hinweis beim Darüberfahren nennt den vollen Namen; in der Listenansicht steht das Feld unter dem Titel. Bei Spielen, die du von Hand hinzugefügt hast, fehlen Werte, die nur Steam kennt (Spielzeit, zuletzt gespielt, Größe): Dort steht der graue Strich, und der Hinweis sagt, dass du das Spiel von Hand hinzugefügt hast.

Von oben nach unten besteht eine Kachel aus: **Cover**, **Bewertungsleiste**, **Leiste mit Spielen, Favorit und Twitch**, **Name** (bis zu zwei
Zeilen) mit den Genres von Steam und – in den Größen Groß und Kompakt – festen Zeilen: gespielte Zeit, zuletzt gespielt, Erscheinungsdatum, **Größe**
und bei den Favoriten die geschätzte Dauer für 100 %. Ganz unten steht deine **Trophäenzeile** mit Fortschrittsbalken. Alles, wonach du sortieren kannst (5.2), steht auch auf der Kachel: die
Abschlussquote im Trophäenbalken, die übrigen Werte in diesen Zeilen. Jede Kachel zeigt dieselben Zeilen mit demselben Symbol, damit
alle gleich aussehen: Fehlt ein Wert (noch nicht gespielt, keine Angabe von Steam, keine Schätzung), steht dort ein grauer Strich „–“,
und ein Hinweis beim Darüberfahren nennt den Grund. Spiele mit allen Trophäen erkennst du am **Platin**-Schild oben rechts auf dem Cover, um das ab und zu ein kurzes Funkeln aufblitzt, und an der Trophäenzeile unten an der Kachel: Statt des goldenen Pokals steht dort das Platin-Symbol (ein Pokal mit Funkeln), und die Linie darunter ist eine kräftige goldene Linie mit einem langsam wandernden Lichtschimmer und kleinen funkelnden Sternen. Am Ende der Linie sitzt ein kleiner goldener Orden (eine Rosette), auf dem ab und zu ein Glanzpunkt aufblitzt (im hellen Farbschema mit dunkelgoldenem Schein). Ist in Windows „Animationen reduzieren“ an, bleibt die Linie ruhig und steht still.

- **Größe:** Ist das Spiel installiert, steht dort die echte **Größe auf der Platte** (so schreibt sie auch Steam, zum Beispiel „48,3 GB“).
  Ist es nicht installiert, steht dort die **Installationsgröße** mit „ca.“: PlatinVault schätzt sie aus den Daten, die Steam selbst über das
  Spiel kennt (Windows, Hauptsprache, ohne DLC). Sie kann leicht abweichen – bei manchen Spielen liegt sie etwas zu hoch, nie darunter. Kennt PlatinVault
  keine Größe (kein Steam auf diesem PC, keine Bridge), steht der graue Strich da.
- In den kleineren Größen: **Sehr kompakt** zeigt Titel, Trophäen und die Größe auf dem Cover; **Liste** und **Nur Text** zeigen Trophäen und
  Größe in der Zeile (Liste dazu zuletzt gespielt und den Balken); die übrigen Werte stehen in den Größen Groß und Kompakt.

**Die Bewertungsleiste** unter dem Cover zeigt bis zu vier Bewertungen mit Symbol und Zahl: **Steam-Rezensionen** (Anteil positiver Rezensionen in Prozent),
**Metacritic**, **IGDB, Kritiker** und **IGDB, Spieler** (jeweils von 0 bis 100). Beim Darüberfahren steht im Hinweis, woher die Zahl kommt. Eine Bewertung, die ein Spiel nicht hat, wird
ausgeblendet; die übrigen teilen sich die Breite der Leiste. Hat ein Spiel gar keine, steht dort „Noch keine Bewertungen“. Die beiden IGDB-Werte erscheinen
nur, wenn du die Zugangsdaten einer eigenen Twitch-Anwendung eingetragen hast (siehe unten und 6.4). Die Leiste hat immer dieselbe Höhe, damit die
Kacheln einer Reihe gleich aussehen.

Direkt unter der Bewertungsleiste steht eine schmale Leiste mit gleich breiten Feldern ohne Trennlinien: **Spielen**, **Favorit** und **Twitch**
(auf schmalen Kacheln nur mit Symbol, ein Hinweis nennt das Feld). **Spielen** (grün) startet das Spiel (6.1) – auch bei den
Spielen unter „Alle Spiele“. Das Feld ist nur da, solange das auch klappen kann, also wenn die Bridge verbunden ist oder
PlatinVault direkt auf dem Gaming-PC läuft; sonst teilen sich die übrigen beiden die Leiste. **Twitch** zeigt, wenn du in den Einstellungen unter
**Twitch & IGDB** die Zugangsdaten einer eigenen Twitch-Anwendung eingetragen hast und „Live-Streams“ an ist, direkt im Feld eine Zahl neben dem Twitch-Symbol:
wie viele Streams gerade live zu diesem Spiel laufen („12“; ab 300 steht „300+“). Bei normal großen Kacheln steht dort nur das Symbol mit der Zahl; das Wort „Twitch“
erscheint nur in sehr breiten Feldern. Bei 0 steht keine Zahl, der Hinweis sagt dann, dass gerade niemand
streamt. Das Symbol steht mittig im Feld, genau wie die Symbole der anderen Felder; der kleine Live-Punkt neben der Zahl erscheint erst ab einer bestimmten Kachelbreite. Das Feld hält den Platz für die Zahl immer frei, damit sich nichts verschiebt. Die Zahlen kommen im Hintergrund für jede Kachel, zu der du scrollst – auch weit unten in „Alle Spiele“ – und aktualisieren sich alle paar Minuten. Kennt Twitch für ein Spiel keine Kategorie, steht keine Zahl da; PlatinVault behauptet dann nicht, dass niemand streamt. Ohne Zugangsdaten bleibt das Feld wie es war.

## 5.5 Trophäenschrank

Der Trophäenschrank zeigt alle Spiele, die du auf **100 %** gebracht hast, wie in einer Vitrine – auch Spiele, die du von Hand hinzugefügt hast, sobald sie ihr Platin erreicht haben (6.6): Hinter Glastüren stehen die Spiele mit ihrem Cover auf Regalbrettern. Unter jedem Cover stehen der Name und das Datum der Platin-Trophäe, davor ein kleiner goldener Pokal; ein kurzer goldener Strich markiert den Platz auf dem Regalbrett.

- **Öffnen:** Klicke in der Kopfzeile auf den **Pokal** (**Trophäenschrank**). Der Schrank legt sich als Vollbild über die aktuelle Seite; die Seite darunter bleibt, wie sie war. Er steht in der Adresse der Seite, ein Neuladen lässt ihn also offen, und der Zurück-Pfeil des Browsers schließt ihn. Erst lädt der Schrank (eine dünne blaue Linie), dann gleiten die Glastüren zur Seite und das Licht geht an.
- **Werkzeugleiste:** Oben im Schrank stehen die drei Zahlen (Spiele auf Platin, Trophäen, Spielzeit), der Größenregler, der Knopf **Ansicht**, der Tür-Knopf und der Knopf zum Schließen. PlatinVault merkt sich deine Wahl in diesem Browser bzw. Programm.
  - **Größe:** Der Regler macht die Spiele kleiner (mehr pro Regalbrett) oder größer. Er hat fünf Stufen von **Sehr klein** bis **Sehr groß**; vorgewählt ist **Klein**. In der kleinsten Stufe steht nur der Name unter dem Cover, ab **Klein** auch das Datum; in der größten Stufe kommt die Uhrzeit dazu. In jeder Stufe zeigt ein Hinweis beim Darüberfahren Name, Datum und Uhrzeit.
  - **Ansicht – Beleuchtung:** Der Knopf **Ansicht** öffnet ein kleines Fenster. Dort wählst du die Beleuchtung. **Spots** (vorgewählt) hängt eine Lampe über jedes Spiel, mit weichem Lichtkegel. **LED-Leiste** zieht eine durchgehende Leiste über jedes Regalbrett; die ganze Reihe geht gemeinsam an, die Regalbretter nacheinander von oben nach unten.
  - **Ansicht – Leere Plätze zeigen:** Im selben Fenster. Ist der Schalter an, stehen auf den noch freien Plätzen dünn umrandete Platzhalter, und (bei Spots) auch über ihnen brennt eine Lampe. So sieht der Schrank aus wie eine Vitrine, die darauf wartet, gefüllt zu werden. Platzhalter sind nur Dekoration (nicht anklickbar) und schieben deine Spiele nicht weg. Ausgeschaltet (Standard) bleiben die freien Plätze leer und dunkel.
  - **Türen:** Mit dem Tür-Knopf schließt und öffnest du die Glastüren wieder; beim Schließen geht zuerst das Licht aus. Ein Klick aufs geschlossene Glas öffnet sie.
  - **Schließen:** Der Knopf ganz rechts (X) oder Esc schließt den Schrank; ist gerade ein Detailfenster offen, schließt Esc zuerst dieses.
- **Neue Spiele:** Hast du seit deinem letzten Besuch ein Spiel auf 100 % gebracht, liegt es nicht einfach da: Sein Platz bleibt zuerst frei (eine glasige Karte), dann wird das Spiel mit einem kleinen Glanz auf das Regal gesetzt. Mehrere neue Spiele kommen nacheinander an die Reihe (bei vielen etwas schneller, die Gesamtdauer bleibt bei rund fünf Sekunden). Ein goldenes **Neu** markiert sie, bis du den Schrank wieder verlässt. Danach gelten sie als gezeigt; am selben Stand (Windows-Programm und Browser) wird nichts doppelt abgespielt. Beim allerersten Öffnen des Schranks werden die Spiele, die schon auf 100 % sind, ohne Animation einsortiert. Ist in Windows „Animationen reduzieren“ an, blenden die Spiele nur ruhig ein. Bildschirmleser erhalten einmal die Ansage „Neu im Schrank: …“.
- **Viele Spiele:** Haben nicht alle Spiele auf einmal Platz, scrollen die Regalbretter gemeinsam seitlich – in jeder Größe. Du kannst mit dem Mausrad scrollen, auf dem Touchpad oder Touchscreen wischen, mit den Links-/Rechts-Pfeiltasten von Spiel zu Spiel springen oder die **Pfeil-Tasten** am linken und rechten Rand anklicken (jeder Klick blättert etwa eine Seite weiter). Ein dunkler Verlauf am Rand zeigt, wo es weitergeht; ein abgeblendeter Pfeil heißt: in dieser Richtung ist nichts mehr. Passt alles auf einmal, sind Pfeile und Verläufe nicht zu sehen.
- **Details:** Ein Klick auf ein Spiel zeigt Trophäen, Tage bis Platin, Spielzeit und die seltenste Trophäe – im Querformat von rechts, im Hochformat von unten. Ein zweiter Klick auf das Spiel, der Schließen-Knopf oder Esc schließt die Details wieder.

# 6 Die Spielseite

## 6.1 Aufbau

Ganz oben im Spielkopf steht der Knopf **Zurück**. Er bringt dich auf die Seite, von der du gekommen bist – oder zur Übersicht, wenn du das Spiel direkt geöffnet hast. Darunter stehen Cover, Titel, der Knopf **Spielen** und eine ruhige Reihe mit den Werkzeugen des Spiels (Symbole mit Hinweis beim Darüberfahren: Guide vervollständigen, Kapitel ergänzen, Exportieren, Twitch, Drucken/PDF, Fortschritt zurücksetzen). Darunter zeigt eine Zeile deinen Trophäen-Fortschritt mit den Stufen. Danach folgt der Inhalt der gewählten Seite.

Unten im Spielkopf wechselst du mit der **Bereichsleiste** zwischen den Bereichen des Spiels. Sie erscheint nur bei Spielen, die mit Steam verbunden sind (bei Spielen, die du von Hand hinzugefügt hast, siehe 6.6):

- **Guide** – die Kapitel des Guides (6.2). Ein Klick bringt dich zu dem Kapitel zurück, das du in diesem Spiel zuletzt gelesen hast.
- **Live-Trophäen** – die Steam-Erfolge des Spiels (6.3).
- **Statistiken** (6.4).

Die **Navigation links** (sie gibt es nur auf der Spielseite, 5.1) enthält nur den Guide. Sie hat diese Einträge:

- die **Kapitel** des Guides (6.2), geordnet unter kleinen Überschriften wie **Einstieg**, **Story & Welt**, **Kampf & Taktik** oder **Fortschritt & Freischaltungen** (hat ein Guide nur eine Gruppe, gibt es keine Überschriften). Neben jedem Kapitel zeigt eine Zahl, wie viel du abgehakt hast,
- **Story & Lore** (6.5).

Die gewählte Seite ist hervorgehoben und steht in der Adresse – ein Neuladen oder ein Lesezeichen bringt dich also genau dorthin zurück. Die Navigation selbst, ihre schmale Form und die Schublade auf dem Handy erklärt 5.1. Die Angaben zu Version, Lizenzen und „Mit ♥ gemacht“ findest du nicht auf der Spielseite, sondern auf der Seite **Über PlatinVault** (Info-Symbol in der Kopfzeile).

**Spiel starten.** Mit dem Spielen-Knopf startest du das Spiel in Steam, ohne zum Gaming-PC zu gehen. Auf der Seite des Spiels steht an derselben Stelle **Spiel läuft** (grüner pulsierender Punkt, nicht klickbar), solange genau dieses Spiel läuft; danach ist es wieder **Spielen**. Der Knopf (und der Hinweis
„Spiel läuft gerade“) erscheint nur, solange die Bridge verbunden ist oder PlatinVault direkt auf dem Gaming-PC läuft, und
verschwindet von selbst, wenn die Verbindung abbricht. Ist die PlatinVault
Bridge auf dem Gaming-PC verbunden (Kapitel 8), startet das Spiel dort – auch wenn du PlatinVault gerade auf dem Tablet
oder Handy bedienst; die Bridge zeigt dazu kurz „Starte …“ neben dem Symbol in der Taskleiste. Steam wird bei Bedarf mitgestartet,
und ist das Spiel noch nicht installiert, beginnt Steam die Installation. Ohne Bridge öffnet dein Browser die Steam-Adresse
selbst – das klappt nur, wenn du PlatinVault auf dem Rechner mit Steam im Browser geöffnet hast; dein Browser fragt dann
einmal nach, ob er Steam öffnen darf. Klappt der Start nicht, nennt PlatinVault den Grund, zum Beispiel „Steam wurde auf dem
Gaming-PC nicht gefunden“ oder „Der Gaming-PC hat den Startbefehl nicht abgeholt“ (dann läuft die Bridge nicht oder hat keine
Verbindung). Starten kannst du nur Spiele aus deiner Sammlung und deiner Steam-Bibliothek. Spiele, die du von Hand hinzugefügt hast, haben keinen Spielen-Knopf.

## 6.2 Der Guide

- **Kapitel mit Checklisten:** Hake erledigte Einträge ab; PlatinVault speichert sie sofort. Die Zahl neben jedem Kapitel
  zeigt, wie viel davon erledigt ist. Die Häkchen sind deine persönliche Liste – der Fortschritt eines Spiels richtet sich
  nach den Trophäen.
- **Guide vervollständigen:** Der Zauberstab fragt zuerst „Welche KI möchtest du benutzen?“ (4.3); dort wählst du eine KI mit Key oder **Mein KI-Abo**. Mit einer KI
  öffnet sich der **Guide-Baukasten**. Er analysiert das Spiel und schlägt passende Kapitel vor (empfohlene
  sind vorausgewählt; ganz oben steht, wie viele es sind und zu welchem Umfang das passt). Du wählst per Klick, die KI recherchiert nur
  die fehlenden Kapitel. Die Spielart-Marke **Typisch** an einem Thema heißt: Das gehört bei dieser Art Spiel oft dazu – es wird nur
  vorgeschlagen, wenn die Analyse bestätigt, dass das Spiel es hat.
- **Guide mit deinem KI-Abo (ohne API-Key):** Du kopierst einen fertigen Prompt in den Chat deines Abos (zum Beispiel ChatGPT, Claude oder
  Gemini), fügst die Antwort hier wieder ein – PlatinVault greift nie auf dein Abo zu und schickt nichts an eine KI. Das ist erlaubt, weil du
  selbst im Chat sitzt. Du erreichst es im Guide-Baukasten (**Mit deinem KI-Abo (ohne API-Key)**), bei einer fehlgeschlagenen KI-Aktion und
  am Hinweis eines nicht recherchierten Kapitels, im Auswahlfenster von **Guide vervollständigen** (**Mein KI-Abo**) und im Pfeil-Menü von **Tab ergänzen**; ohne gespeicherten Key öffnet **Guide vervollständigen** es direkt.
  - *Schritt 1, Kapitelliste:* **Prompt kopieren**, im Chat einfügen (am besten mit eingeschalteter Websuche) und die Antwort in das Feld
    darunter einfügen – auch mit Text davor oder in einem json-Codeblock, oder als Datei laden. Du siehst die vorgeschlagenen Kapitel wie im
    Baukasten und hakst an, was du willst. Wer keinen Bauplan-Prompt möchte, wählt die Themen selbst.
  - *Schritt 2, Vorgehen:* Oben wählst du unter **Vorgehen** zwischen **Alle auf einmal** und **Einzeln**. Bei vielen Kapiteln ist
    **Alle auf einmal** der bequemere Weg (siehe unten).
  - *Schritt 2, Einzeln, Kapitel für Kapitel:* Jedes Kapitel hat einen eigenen Prompt (**Prompt kopieren** oder **Als Datei exportieren**) und ein Feld für
    die Antwort. Die Antwort wird **beim Einfügen** geprüft: Gehört sie zum richtigen Spiel (Steam-AppID steht in jedem Prompt und in der
    Antwort)? Ist das JSON vollständig (nicht abgeschnitten)? Stimmen Abschnitte, Tabellenzeilen und Sprache? Fehler stehen vollständig
    darunter, dazu eine Vorschau (Titel, Abschnitte, Tabellenzeilen). **Kapitel speichern** legt das Kapitel genau wie ein KI-Ergebnis an; ein
    Haken zeigt, was schon gespeichert ist. Bei einem nicht recherchierten Kapitel ersetzt die Antwort den Hinweis.
  - *Alle auf einmal:* PlatinVault macht **einen** Prompt für alle gewählten Kapitel (darüber steht, wie viele Kapitel es sind und wie groß der
    Prompt ist). So gehst du vor:
    1. Prompt kopieren und im Chat deines Abos einfügen, am besten mit eingeschalteter Websuche.
    2. Der Chat erstellt **eine** Datei mit allen Kapiteln (Name `platinvault-<spiel>-kapitel.json`) und gibt sie dir zum Herunterladen. Ziehe
       die Datei in das Feld **Antworten des Chats** (`.json`, `.md` oder `.txt`, oder **Datei wählen**). Kann dein Chat keine Datei erzeugen,
       füge stattdessen seine Antwort ein: ein Codeblock mit allen Kapiteln. Auch die Komplettlösung gehört dazu – sie wird nicht mehr
       einzeln beantwortet. Meldet der Chat am Ende **ALLE KAPITEL FERTIG**, ist alles da.
    3. Unter **Kapitel** steht für jedes Kapitel sein Zustand: **Ausstehend**, **Erkannt**, **Mit Hinweisen**, **Fehler**, **Gibt es im Spiel
       nicht** oder **Gespeichert**. Ein Klick auf ein Kapitel zeigt die Einzelheiten. Antworten, die keinem Kapitel zugeordnet werden können
       (der Chat hat die vorgegebene Kapitel-ID K01, K02 … nicht benutzt), stehen gesondert und werden nicht gespeichert.
    4. **N Kapitel speichern** legt alle erkannten Kapitel auf einmal an, genau wie KI-Ergebnisse. Passt nicht alles in eine Antwort, hört der Chat nach einem
       vollständigen Kapitel auf und schreibt **NOCH OFFEN: K05–K13**. Dann – oder wenn ein Kapitel fehlerhaft ist – kopierst du **Prompt für die
       restlichen X Kapitel kopieren**, fügst ihn im Chat ein und machst damit weiter (oder du schreibst „weiter“; der Chat antwortet dann mit den offenen Kapiteln).
    5. Sind alle Kapitel gespeichert, sagt PlatinVault, dass du das Fenster schließen kannst.

    Was du eingefügt hast, bleibt erhalten, wenn du das Fenster zwischendurch schließt (solange du den Browser-Tab offen lässt). **Eingefügtes
    leeren** verwirft es nach einer Rückfrage; bereits gespeicherte Kapitel bleiben gespeichert. Sind mehrere Kapitel fehlgeschlagen, startet
    **Alle mit meinem KI-Abo** am Hinweis den Weg für genau diese Kapitel.
  - **Warnung bei Sätzen in einer anderen Sprache:** Steht in einer eingefügten Antwort etwas in einer anderen Sprache als der des Guides,
    zeigt PlatinVault eine Warnung mit bis zu drei Beispielsätzen. **Korrektur-Prompt kopieren** kopiert einen Prompt, mit dem der Chat genau
    diese Sätze übersetzt und die Antwort noch einmal ausgibt. Füge ihn im selben Chat ein und ersetze die Antwort hier durch die neue. Du kannst
    die Antwort auch so speichern – die Warnung hindert dich nicht.
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
- **Kapitel neu recherchieren (jedes Kapitel):** Ist dir ein Kapitel zu dünn oder veraltet, ersetzt **Kapitel neu recherchieren** (oben rechts
  über dem Inhalt, neben **Kapitel entfernen**) es durch eine frisch recherchierte Fassung. Die alte Fassung bleibt stehen, bis die neue fertig
  ist. Deine Häkchen bleiben, wo die Einträge erhalten bleiben; Steam-Erfolge und alle anderen Kapitel bleiben unberührt. Die KI verbraucht dafür
  dein Kontingent. Ohne Key oder mit einem Abo nimmst du **Mit meinem KI-Abo neu recherchieren**: Du bekommst einen Prompt für den Chat, das
  Kapitel wird erst ersetzt, wenn du die Antwort einfügst und speicherst. Gibt es das Thema im Spiel gar nicht, bricht PlatinVault mit einer
  Meldung ab und das Kapitel bleibt so, wie es war. Bei Einträgen, die es weiterhin gibt, bleiben deine Häkchen auch dann, wenn die KI sie umbenennt.
- **Kapitel entfernen (jedes Kapitel):** Ein Kapitel, das du nicht brauchst – zum Beispiel ein Thema, das zu deinem Spiel gar nicht passt –,
  entfernst du mit **Kapitel entfernen** oben rechts über dem Inhalt des Kapitels. Eine Rückfrage nennt, was gelöscht wird: das Kapitel, seine
  Übersetzung und die Häkchen, die du in seinen Listen gesetzt hast. Deine Steam-Erfolge und alle anderen Kapitel bleiben unberührt; rückgängig
  machen lässt sich das Entfernen nicht, über **Guide vervollständigen** kann die KI fehlende Themen aber später erneut recherchieren. Danach
  öffnet sich das nächste Kapitel. Nicht entfernen lassen sich die Steam-Erfolgsliste des Guides (die Live-Trophäen lesen ihre Tipps daraus) und
  **Story & Lore** (ein eigener Bereich; er wird dort neu recherchiert).
- **Komplettlösung (immer ganz oben):** Gibt es für ein Spiel eine belastbare, vollständige Quelle, hat der Guide als erstes Kapitel die
  **Komplettlösung**: der Spielverlauf in der Reihenfolge des Spiels, aufgebaut wie ein gedrucktes Lösungsbuch. Neben der Komplettlösung steht ein
  **Inhalt**, über den du direkt zu einem Teil oder Gebiet springst (ein- und ausblendbar). Jeder **Teil** (Story-Kapitel oder Mission) beginnt
  mit einem kurzen Orientierungssatz und dem Kasten **Auf einen Blick**: empfohlene Stufe, was es hier zu finden gibt, welche Steam-Erfolge in
  diesem Teil möglich sind, Schlüsselgegenstände, Verpassbares und ob der Teil an einem Point of no Return endet. Darunter folgen die
  **Gebiete** in der Reihenfolge, in der man hindurchkommt: die Route in nummerierten Schritten (jeder Schritt ein bis zwei ganze Sätze),
  die **Funde** des Gebiets als abhakbare Tabelle (mit einer Spalte **Verpassbar**), Nebenaufgaben und Bosse. Farbige Hinweise mit eigenem
  Symbol stehen genau dort, wo etwas wichtig wird: **Wichtig – unbedingt mitnehmen** (mit Fundort), **Verpassbar ab hier**, **Optional hier
  verfügbar**, **Erfolg hier möglich**, **Tipp** und – kurz vor dem Punkt, ab dem es kein Zurück gibt – **Point of no Return** mit der Liste
  **Erledige das vorher**. Rätsel erscheinen als eigener Block (siehe unten).
  Geschrieben ist sie in eigenen Worten; die Quellen stehen am Ende des Kapitels unter **Quellen** (Links öffnen im Browser) und im
  Einleitungs-Kasten. Gibt es keine belastbare Quelle, legt PlatinVault das
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
- **Nebenaufgaben:** In der Komplettlösung stehen Nebenaufgaben nur kompakt in einem Hinweis **Nebenaufgaben hier verfügbar** (Name, ab wann,
  verpassbar bis wann, kurze Belohnung). Die vollständige Lösung steht im eigenen Nebenaufgaben-Kapitel; **Alle im Kapitel „…“** und der Name
  der Aufgabe führen dorthin.
- **Bosse:** Jeder Bosskampf hat eine eigene Karte **Gegner** mit Ort, empfohlener Stufe, **Lebenspunkten** (nur mit Quelle – sonst steht
  „nicht dokumentiert“, PlatinVault erfindet keine Zahlen), **Schwächen**, **Resistenzen**, **Immunitäten**, **Gefährlichen Angriffen**,
  **Phasen**, empfohlener Gruppe und Ausrüstung, **Taktik** und **Belohnungen**. Ist der Kampf verpassbar oder schaltet er einen Erfolg frei,
  steht das auch dort.
- **Verknüpfte Häkchen:** Taucht ein Eintrag in zwei Kapiteln auf – zum Beispiel ein Gegenstand, den die Komplettlösung erwähnt und der in
  einer Sammelliste eine eigene Zeile hat –, teilen sich beide **dasselbe Häkchen**: Hakst du den Eintrag an einer Stelle ab, ist er an der
  anderen auch erledigt. Der Eintrag in der Komplettlösung trägt den Sprunglink **→ Name des Kapitels** zur ursprünglichen Zeile, und die
  ursprüngliche Zeile zeigt, wo er sonst noch vorkommt (**Komplettlösung · Teil · Gebiet**). Verknüpft wird nur, wenn der Name zu genau einem
  Eintrag eines anderen Kapitels passt; im Zweifel bleibt es getrennt, es wird nichts geraten. Entfernst du das Kapitel mit der ursprünglichen
  Zeile, behält der andere Eintrag sein Häkchen und wird wieder ein normaler Eintrag.
- **Quellen der Recherche:** Die KI sucht zuerst nach einem Wiki, das eigens für dieses Spiel gemacht ist, und nimmt danach weitere Quellen dazu; Zahlen werden möglichst mit einer zweiten Quelle abgeglichen.
- **Sprache und Titel:** Ein Guide ist ganz in seiner Sprache geschrieben – jeder Satz in eigenen Worten, nicht aus fremdsprachigen Quellen
  übernommen. Namen stehen so, wie sie in der Fassung des Spiels in dieser Sprache heißen; der englische Originalname steht höchstens einmal je
  Kapitel beim ersten Vorkommen in Klammern. Kapitel-Titel stehen nur in der Sprache des Guides, ohne Übersetzung in Klammern.
- **Tab ergänzen:** Ein einzelnes Kapitel zu einem Thema recherchieren lassen – Thema anklicken oder selbst beschreiben. Welche KI das
  übernimmt, steht am Knopf (siehe 4.3); im Pfeil-Menü des Knopfes wählst du auch **Mein KI-Abo**.
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

### Im Blick: Erfolge, an denen du gerade arbeitest

Mit dem **Stecknadel-Knopf** an einem Erfolg – er steht in allen vier Ansichten und im Detailfenster – behältst du bis zu **10 Erfolge pro Spiel** im Blick. Sind schon 10 angeheftet, nimm zuerst einen heraus. Die angehefteten Erfolge erscheinen auf **jeder Seite** des Spiels, also auch beim Lesen des Guides:

- **Breites Fenster im Querformat:** als einklappbare Spalte rechts neben dem Inhalt.
- **Hochformat und schmale Fenster:** als einklappbare Leiste unten. Auf dem Handy startet sie eingeklappt als eine schlanke Zeile.

Ob Spalte oder Leiste ein- oder ausgeklappt ist, merkt sich der Browser bzw. das Programm für jede der beiden Darstellungen getrennt.

Zähler-Erfolge zählen live mit, während du spielst. Ein Klick auf eine Karte öffnet das Detailfenster mit Guide-Tipp und deiner Notiz. Schaltest du einen angehefteten Erfolg frei, steht er als **Geschafft** da; mit **Geschaffte entfernen** nimmst du alle auf einmal heraus. Wer das nicht von Hand machen will, lässt PlatinVault sie selbst entfernen (Einstellungen → **Live & Sound** → „Im Blick“, siehe Kapitel 7). Browser und Windows-Programm teilen sich dieselbe Auswahl, weil sie auf dem Server gespeichert ist – ein Erfolg, den du am Handy anheftest, steht auch am PC da.

**Wenn Steam nicht erreichbar ist:** Die weltweiten Anteile holt PlatinVault beim Start und danach stündlich für die Spiele deiner Sammlung und behält den letzten guten Stand auch über einen Neustart hinweg. Scheitert ein Abruf, ändert sich an den gespeicherten Werten nichts; PlatinVault versucht es nach 2 Minuten wieder, danach mit wachsenden Pausen bis zu 30 Minuten. Über der Liste erscheint dann ein Hinweis (mit **Kopieren**-Knopf): Entweder zeigt er das Datum der gespeicherten Werte, oder – wenn es noch gar keine gibt – dass die Stufen vorläufig sind und die Anteile fehlen. Meist blockiert dann ein Virenschutz oder eine Firewall die Verbindung zu Steam.

**So ordnet PlatinVault die Seltenheit ein:** Überall in der App – in den Statistiken und in der Bezeichnung, die ein
Trophäen-Popup trägt – gilt dieselbe Skala mit fünf Stufen:

- **Extrem selten:** unter 5 % der Spieler
- **Sehr selten:** 5 % bis unter 10 %
- **Selten:** 10 % bis unter 20 %
- **Ungewöhnlich:** 20 % bis unter 50 %
- **Häufig:** ab 50 %

Die **Farbe** einer Trophäe (Gold, Silber, Bronze) richtet sich nach demselben Anteil, aber mit eigenen Grenzen: **Gold**
haben höchstens 10 % der Spieler, **Silber** höchstens 50 %, **Bronze** mehr als 50 %. **Platin** bekommst du, wenn du alle
Trophäen eines Spiels hast. Eine Ausnahme gibt es bei Story-Kapiteln: Erfolge, die man durch das Abschließen eines nummerierten Story-Kapitels erhält (z. B. „Schließe Kapitel 5 ab.“), zählen immer als Bronze – unabhängig davon, wie selten sie weltweit sind. Die Seltenheitsangabe (z. B. „Extrem selten“) zeigt weiterhin den echten weltweiten Anteil. Sammel-Erfolge wie „Schließe alle Kapitel auf Schwer ab“ sind davon nicht betroffen. Liefert Steam für eine Trophäe keinen Anteil, nennt PlatinVault dafür keine Seltenheitsstufe und
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
- **Bewertungen im Vergleich** *(nur mit IGDB)*: IGDB-Kritiker (Wertungen der Fachpresse), IGDB-Spieler (Durchschnitt der IGDB-Nutzer), Steam-Rezensionen und Metacritic auf einer
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

**Story & Lore mit deinem KI-Abo (ohne API-Key):** Auf dem Knopf zum Recherchieren (und zum erneuten Recherchieren) wählst du im Pfeil-Menü **Mein KI-Abo**.
PlatinVault schickt dabei nichts an eine KI.

1. Kopiere den Prompt und füge ihn im Chat deines Abos ein. Er enthält alle vier Teile der Story.
2. Füge die Antwort des Chats in das Feld darunter ein – auch mit Text davor oder in einem json-Codeblock, oder lade sie als Datei.
3. Prüfe die **Vorschau**: Sie nennt jeden erkannten Teil mit der Zahl seiner Abschnitte.
4. Klicke **Story & Lore speichern**.

Fehlen Teile, weil die Antwort zu lang für eine Nachricht war, steht dort eine Warnung. Der Chat schreibt dann am Ende „NOCH OFFEN: …“. Schreibe „weiter“ in
denselben Chat und füge die neue Antwort ebenfalls ein. Du kannst auch mit den vorhandenen Teilen speichern. Hat das Spiel schon Story & Lore, ersetzt das
Speichern den Reiter vollständig; ein Hinweis und eine Rückfrage zeigen dir das vorher. Das lässt sich nicht rückgängig machen.

## 6.6 Spiele von Hand: Trophäen- und Erfolgsliste

Diese Seite gilt für Spiele, die du mit **Spiel hinzufügen** angelegt hast (5.3). Sie sieht anders aus als die Seite eines Steam-Spiels, weil PlatinVault bei anderen Plattformen nichts automatisch erfährt.

**Warum hakst du selbst ab?** PlatinVault liest bei Steam deine freigeschalteten Erfolge selbst. Für PlayStation und die anderen Plattformen gibt es das nicht. Sony bietet keine öffentliche Schnittstelle für Trophäen an. Inoffizielle Wege widersprechen nach unserer Lesart den Nutzungsbedingungen des PlayStation Network (Abschnitt 10.7); PlatinVault ruft deshalb nirgends das PlayStation Network auf. Das ist keine Rechtsberatung – nutze den Weg, mit dem du dich wohlfühlst. Die Liste kommt stattdessen aus der KI-Recherche, und du hakst erledigte Trophäen selbst ab.

**Der Aufbau der Seite.** Die Bereichsleiste im Spielkopf zeigt **Guide** und daneben **Trophäen** (bei PlayStation) bzw. **Erfolge** (bei Xbox, Epic Games, GOG, Ubisoft Connect, EA app und Battle.net). **Statistiken** und der Knopf **Spielen** gibt es bei diesen Spielen nicht. Unter dem Titel steht die Plattform, zum Beispiel „PS5: Trophäen“, und ob du Platin erreicht hast. Bei der Nintendo Switch gibt es nur den Guide, denn dort gibt es keine Trophäen oder Erfolge.

### Die Liste recherchieren

Bevor du etwas abhaken kannst, braucht das Spiel eine Liste. Das sind zwei getrennte Schritte; wir empfehlen diese Reihenfolge:

1. **Trophäenliste recherchieren** (bei den anderen Plattformen **Erfolgsliste recherchieren**): Öffne den Bereich **Trophäen** bzw. **Erfolge**. Klicke auf den Knopf. Die KI sucht die Liste der richtigen Fassung heraus (bei PlayStation die der PS5 oder PS4, nie gemischt): Namen, Beschreibungen, Stufen, verpassbare und versteckte Trophäen, DLC-Trophäen und kurze Tipps.
2. **Guide erstellen:** Das ist ein eigener, späterer Schritt im Bereich **Guide**. Er nutzt die Liste, wenn es sie schon gibt. Er funktioniert aber auch ohne Liste.

Für die Recherche nutzt du eine **KI mit API-Key** (siehe 4.3) oder **Mein KI-Abo**. Beim zweiten Weg kopierst du einen Prompt in den Chat deines Abos und fügst die Antwort wieder ein – genau wie beim Guide (6.2). Der Dialog heißt dort **Trophäenliste mit deinem KI-Abo (ohne API-Key)**; er prüft die Antwort, zeigt eine Vorschau mit der Zahl der Trophäen, den versteckten und den DLC-Trophäen und speichert erst, wenn du **Trophäenliste speichern** klickst. Beim API-Weg läuft die Recherche im Hintergrund, du kannst die Seite währenddessen verlassen.

> [!WARNUNG] Die Liste stammt aus Webquellen und kann Fehler enthalten. PlatinVault nennt unter der Liste, wann sie recherchiert wurde und welche **Quellen** die KI angegeben hat. Prüfe Stufen, verpassbare und versteckte Trophäen am besten gegen den Guide oder die Quellen. Kann die KI keine verlässliche Liste finden, speichert PlatinVault nichts und erfindet nichts.

Kennt IGDB das Spiel nicht und hast du es ohne IGDB angelegt, recherchiert die KI im selben Schritt auch die **Spielfakten** (Erscheinungsdatum, Entwickler, Publisher, Beschreibung, Dauer). Bei der Nintendo Switch gibt es dafür den eigenen Knopf **Spiel-Fakten recherchieren**.

**Neu recherchieren.** Mit **Trophäenliste neu recherchieren** holst du eine frische Liste, zum Beispiel nach einem neuen DLC. **Deine Häkchen bleiben erhalten.** Gleiche Einträge behalten ihr Häkchen; ein Eintrag, den du schon abgehakt hast und der in der neuen Liste fehlt, bleibt trotzdem stehen. Nicht abgehakte Einträge, die die neue Liste nicht mehr nennt, entfallen. Vor dem Ersetzen fragt PlatinVault im KI-Abo-Weg zurück.

**Andere Sprache.** Die Liste ist in der Sprache des Spiels geschrieben. Wechselst du die Oberfläche in die andere Sprache, übersetzt die KI die Liste zusammen mit dem Guide (Namen, Beschreibungen, Tipps). Bis die Übersetzung fertig ist, siehst du das Original. Stufen, Punkte und Häkchen ändern sich dadurch nicht.

### Abhaken

Die Liste sieht aus wie die Live-Trophäen eines Steam-Spiels (6.3): dieselben vier Ansichten, Filter, Suche, Notizen und **Im Blick**. Neu ist ein **Haken-Knopf** an jeder Trophäe.

1. Öffne den Bereich **Trophäen** bzw. **Erfolge** des Spiels.
2. Klicke an der erledigten Trophäe auf den Haken-Knopf („… als erledigt abhaken“).
3. PlatinVault zeigt das **Trophäen-Popup** mit demselben Aussehen und Ton wie bei einer Freischaltung in Steam.
4. Ein zweiter Klick nimmt den Haken wieder weg. Das geschieht **ohne Popup und ohne Ton**.

- **Nur in der App:** Das Popup erscheint in den PlatinVault-Fenstern (Browser oder Programmfenster), **nie** im Overlay über dem Spiel – auch nicht bei der Einstellung „Nur im Spiel“ oder „Beides“ (siehe Kapitel 7).
- **Stufen:** Bei PlayStation gelten die Stufen von Sony (Bronze, Silber, Gold, Platin). Bei den anderen Plattformen richten sie sich nach der Seltenheit, wenn die Liste sie nennt (Skala wie in 6.3). Sonst gilt bei Xbox: ab 50 Gamerscore-Punkten Gold, 20 bis 49 Silber, darunter Bronze; ohne Angaben Bronze. Das ist eine Einteilung von PlatinVault, keine der Plattform.
- **Platin bei PlayStation:** Hast du alle anderen Trophäen des Hauptspiels abgehakt (DLC-Trophäen zählen nicht), hakt sich die **Platin-Trophäe selbst ab**; es folgt der Platin-Moment. Entfernst du danach einen Haken, verschwindet das Platin still wieder. Der Haken an der Platin-Trophäe selbst lässt sich nicht von Hand setzen. Bei Plattformen ohne Platin-Stufe gilt das Spiel bei 100 % aller Einträge als abgeschlossen.
- **Fortschritt:** Die Zahl der Trophäen, der Fortschrittsbalken, der Platin-Moment, die Kachel auf der Übersicht und der Trophäenschrank (5.5) zählen deine Haken mit.
- **Fortschritt zurücksetzen** im Spielkopf betrifft nur die Guide-Häkchen, **nicht** deine Trophäen-Haken.

# 7 Trophäen-Popups und Sounds

Im Reiter **Live & Sound** der Einstellungen:

- Popups an- oder ausschalten, **Position** auf dem Bildschirm wählen: neun Plätze (links oben bis rechts unten) oder **Vollbild**.
  Ein Klick auf eine Position zeigt sofort eine Vorschau. Bei **Vollbild** gehört das ganze Fenster für die eingestellte Anzeigedauer der
  neuen Trophäe: großer Pokal in der Farbe der Stufe, Name, Beschreibung, Seltenheit und Spiel – im Quer- wie im Hochformat. Ein Klick
  irgendwo oder die Taste Esc beendet das Popup früher; danach geht es mit der nächsten Trophäe weiter.
- **Anzeigedauer der Trophäen-Popups:** Ein Schieberegler unter den Positionen. Er bestimmt, wie lange ein neuer Erfolg auf dem Bildschirm bleibt: ganze Sekunden von 2 bis 10, Standard 5. Das gilt für die Karte, das Vollbild-Popup, die Proben und das Overlay über dem Spiel. Das Vollbild-Popup schließt früher, wenn du klickst oder Esc drückst. Der Platin-Moment behält seine eigene Länge.
- **Zeitbalken und Freischaltzeit:** Am Rand der Karte und des Vollbild-Popups läuft ein dünner Balken in der Farbe der Trophäenstufe von voll
  auf leer. Er zeigt, wie lange das Popup noch stehen bleibt – auch im Overlay über dem Spiel. Ist in Windows „reduzierte Bewegung“ aktiv,
  gleitet er nicht, sondern springt einmal pro Sekunde. Außerdem steht im Popup, **wann** du die Trophäe freigeschaltet hast (Datum und
  Uhrzeit in der Sprache der App). Nennt Steam keine brauchbare Zeit, fehlt diese Zeile.
- **Trophäen-Popup anzeigen:** **Nur in der App**, **Nur im Spiel** oder **Beides** (Standard). „Im Spiel“ heißt: als Overlay über dem
  Spiel – das gibt es nur im Windows-Programm (siehe 11.3). Im Browser und auf Unraid sind die beiden Spiel-Wahlen ausgegraut, dort
  erscheint das Popup immer in der App. Der Ton erklingt bei jeder Trophäe **genau einmal**, auch bei „Beides“.
- **Sound-Thema:** Kristall, Orchester oder Spieluhr – ein Klick spielt die Probe. Alle Klänge sind eigene Aufnahmen
  echter Instrumente.
- Lautstärke einstellen, **Sound probehören** und den **Platin-Moment testen**.
- **Notizen an Erfolgen** ein- oder ausschalten (Standard: an; siehe 6.3).
- **Im Blick:** Schalter **„Freigeschaltete automatisch aus ‚Im Blick‘ entfernen“** (Standard: aus). Aus: Ein freigeschalteter Erfolg bleibt als „Geschafft“ stehen, bis du ihn selbst entfernst. An: PlatinVault nimmt ihn nach dem Trophäen-Popup selbst heraus (siehe 6.3) – eine fertige Karte bleibt dafür so lange stehen, wie die **Anzeigedauer** es vorgibt.
- **Beim Spielstart automatisch zum Spiel wechseln** ein- oder ausschalten (Standard: an; siehe unten).

Startest du ein Spiel aus deiner Sammlung, springt jedes geöffnete PlatinVault-Fenster zu den **Live-Trophäen** dieses Spiels,
du musst nicht erst dorthin navigieren. Das geht, sobald die Bridge auf dem Gaming-PC verbunden ist oder PlatinVault selbst auf
dem Gaming-PC läuft. Gewechselt wird nur bei einem echten Start – nicht beim Neuladen der Seite, während das Spiel schon läuft,
und nicht beim Beenden. Ist gerade ein Fenster offen, steht der Cursor in einem Eingabefeld oder arbeitet eine KI-Aufgabe,
bleibst du, wo du bist: Ein Hinweis meldet den Start und bietet **Zum Spiel wechseln** an. Läuft ein Spiel, das du noch nicht
angelegt hast, nennt ein Hinweis nur den Namen – „noch kein Guide vorhanden“.

**Trophäen von Hand abgehakt** (Spiele, die du hinzugefügt hast, 6.6) zeigen dasselbe Popup und spielen denselben Ton wie eine Steam-Freischaltung – aber immer nur in den PlatinVault-Fenstern, **nie** im Overlay über dem Spiel, welche Wahl bei **Trophäen-Popup anzeigen** auch steht. Ein Haken wieder wegnehmen bleibt still.

Popups erscheinen für alle Spiele, die in PlatinVault angelegt sind. Ein Popup nennt den Anteil der Spieler, die die Trophäe
haben; seine Farbe (Bronze, Silber, Gold) und seine Bezeichnung folgen der Seltenheit (siehe 6.3).

# 8 PlatinVault Bridge (Server auf einem anderen Rechner)

Läuft PlatinVault auf einem Server, NAS oder Unraid, meldet die Bridge deine Trophäen vom Gaming-PC in Echtzeit. Die Bridge gibt es für
**Windows** und für **Linux** (Desktop, Steam Deck, Bazzite); im Reiter **PlatinVault Bridge** wählst du bei **Gaming-PC mit** dein
Betriebssystem. Die Schritte 1 bis 6 gelten für Windows, die Linux-Bridge steht in Abschnitt 8.1.

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

## 8.1 Die Bridge unter Linux (Desktop, Steam Deck, Bazzite)

Die Linux-Bridge ist ein kleiner Hintergrunddienst ohne Fenster, der unter deinem Benutzer läuft. Sie braucht weder Root noch .NET und
legt alles in deinem Home-Ordner ab (deshalb geht sie auch auf SteamOS und Bazzite mit ihrer schreibgeschützten Systempartition).
Sie ist **experimentell** – siehe die Hinweise am Ende dieses Abschnitts.

1. Wähle in den Einstellungen, Reiter **PlatinVault Bridge**, bei **Gaming-PC mit** den Eintrag **Linux / Steam Deck** und klicke auf
   **PlatinVault Bridge herunterladen**. Du bekommst ein `.tar.gz`, das schon mit deinem Server verbunden ist; jeder neue Download
   erzeugt einen neuen Schlüssel, der alte gilt dann nicht mehr. (Dieselbe Datei, `PlatinVault-Bridge-linux-x64.tar.gz`, liegt auch bei
   den Releases auf GitHub – dort ohne deine Zugangsdaten.)
2. Öffne auf dem Linux-Rechner ein Terminal im Download-Ordner und entpacke das Archiv:

   ```
   tar -xzf PlatinVault-Bridge-linux-x64.tar.gz
   cd PlatinVault-Bridge
   ```

3. Installiere und starte die Bridge: `./install.sh`. Das kopiert das Programm nach `~/.local/share/platinvault-bridge/`, speichert
   Server und Schlüssel (nur für dich lesbar) und richtet den systemd-Benutzerdienst `platinvault-bridge` ein.
4. Prüfen, ob sie arbeitet:

   ```
   ~/.local/share/platinvault-bridge/platinvault-bridge status
   journalctl --user -u platinvault-bridge -f
   ```

   Im Reiter **PlatinVault Bridge** steht dann **Verbunden**. Das Protokoll liegt auch in `~/.local/state/platinvault-bridge/bridge.log`.
5. **Ohne fertiges Paket** (zum Beispiel wenn du den Schlüssel von Hand erzeugt hast) richtest du sie so ein:
   `platinvault-bridge setup --server http://<Server>:<Port> --token <Schlüssel>`; zum Ausprobieren im Vordergrund startet
   `platinvault-bridge run`. Die Version zeigt `platinvault-bridge --version`.
6. **Steam an einem ungewöhnlichen Ort:** `platinvault-bridge setup --steam-dir /pfad/zu/Steam` (der Ordner mit `steamapps`) oder die
   Umgebungsvariable `PLATINVAULT_STEAM_DIR` bzw. `STEAM_DIR`. Die Konfiguration steht in `~/.config/platinvault-bridge/bridge.json`.
7. Neue Versionen holt sich die Bridge selbst vom PlatinVault-Server. Die Bridge spricht die Sprache von PlatinVault (Deutsch oder
   Englisch).
8. **Deinstallieren:** `uninstall.sh` im Programmordner `~/.local/share/platinvault-bridge/`.

Wie bei Windows startet die Bridge auf Wunsch auch Spiele (Knopf **Spielen**).

**Noch nicht an echten Geräten geprüft:** Das Lesen der Erfolge unter Linux folgt dem Windows-Dateiformat – ob Steam die Statistikdatei im
Moment der Freischaltung schreibt und wie das angemeldete Steam-Konto gefunden wird, ist offen. Das laufende Spiel erkennt die Bridge
an der Prozessliste von Steam. Ebenso ist offen, ob der Dienst im Spielmodus des Steam Deck weiterläuft (im Desktop-Modus läuft er).
**Docker auf demselben Linux-PC** sieht das laufende Spiel nicht – dort nimmst du das Linux-Paket (Kapitel 13) oder Docker zusammen mit
der Linux-Bridge. Hilft `status` nicht weiter, melde uns bitte, was es und das Protokoll zeigen.

# 9 Daten und Datenschutz

- **Alles bleibt bei dir:** Spiele, Guides, Häkchen und Einstellungen liegen in deiner eigenen Datenbank.
- **API-Keys** (Steam, KI) werden verschlüsselt im Appdata-Ordner gespeichert – nicht in der Datenbank – und danach nur
  gekürzt angezeigt. Unter Windows sind sie zusätzlich an dein Windows-Konto gebunden. Gib den Appdata-Ordner nicht
  offen im Netzwerk frei: Wer den ganzen Ordner hat, hat auch die Keys.
- **Keine Konten, keine Telemetrie:** PlatinVault sendet keine Nutzungsdaten an die Entwickler. Von selbst baut es nur diese Verbindungen auf:
  - **Update-Suche bei GitHub** (`api.github.com`): standardmäßig einmal am Tag; GitHub sieht dabei deine IP-Adresse. Abschalten:
    Einstellungen → **System** → **Updates** → **Nach Updates suchen** auf **Nie (nur von Hand)**. Ein Update wird erst nach deiner Bestätigung geladen.
  - **Microsoft WebView2:** Fehlt diese Laufzeit auf dem Windows-PC, lädt der Installer sie von Microsoft nach. WebView2 ist Microsoft-Software mit eigenen Regeln.
  - **Google Übersetzer:** Bei deutscher Oberfläche, ohne KI-Schlüssel und wenn Steam keinen deutschen Text hat, werden die englischen
    Erfolgstexte zum Übersetzen an Google gesendet. Abschalten: Einstellungen → **Live & Sound** → **Englische Steam-Erfolge automatisch ins Deutsche übersetzen**.
- **Steam-Daten** ruft PlatinVault nur für dein eigenes Konto und auf deinen Wunsch ab – mit dem Web-API-Key und dem Konto, die du selbst einträgst
  (`api.steampowered.com`, `store.steampowered.com`, `steamcommunity.com`, Steam-Bilderserver).
- **KI-Anbieter** bekommen für die Recherche den Spielnamen und öffentliche Spielinformationen – keine persönlichen Daten. Es wird nur der
  Anbieter angesprochen, dessen Schlüssel du eingetragen hast, und nur bei einer KI-Aktion (Google, Anthropic, OpenAI).
- **Twitch und IGDB** werden nur mit deinen eigenen Zugangsdaten befragt. Beim Suchen im Fenster **Spiel hinzufügen** geht dein Suchtext (Name, Nummer oder Link) an IGDB.
- **Eigene Titelbilder und Trophäenlisten** liegen in deiner Datenbank und damit in deinen Sicherungen. Für die Recherche einer Trophäenliste bekommt der KI-Anbieter den Spielnamen, die Plattform und öffentliche Spielinformationen; deine Haken bekommt er nicht. PlatinVault ruft nie das PlayStation Network auf und liest keine Konten anderer Plattformen.
- Die **Bridge** (Windows und Linux) spricht nur mit dem PlatinVault-Server, den du eingetragen hast.
- Das **Linux-Paket** baut keine anderen Verbindungen auf als jeder PlatinVault-Server.
- PlatinVault hat bewusst **keine Anmeldung** und ist für einen Nutzer gedacht. Betreibe es im Heimnetz, nicht offen im
  Internet.
- **Lizenz und Quellcode:** PlatinVault ist freie Software unter der **GNU Affero General Public License, Version 3**
  (AGPL-3.0-only). Du darfst es kostenlos nutzen, untersuchen, ändern und weitergeben; wer es weitergibt oder eine geänderte
  Fassung anderen über ein Netzwerk anbietet, muss den Quellcode unter derselben Lizenz anbieten. Zum Nutzen musst du nichts
  akzeptieren. Der Quellcode steht unter `github.com/sushiflux/PlatinVault`.
- **Lizenzen:** Die Lizenz von PlatinVault (mit dem Knopf **Quellcode**) und die Lizenztexte der enthaltenen Open-Source-Komponenten liest du
  in der App: das **Info-Symbol** in der Kopfzeile öffnen (Seite **Über PlatinVault**) und dort **Lizenzen** wählen, oder in den Einstellungen, Reiter **System**, auf **Lizenzen öffnen**. Dasselbe liegt als `LICENSE` und `THIRD-PARTY-NOTICES.md` im Bridge-ZIP, im Programmordner der Bridge und im Programmordner des Windows-Programms (`docs`);
  die Dateien lassen sich mit jedem Texteditor öffnen.

# 10 Probleme lösen

| Problem | Was hilft |
|---|---|
| Etwas lädt nicht oder zeigt Fehler | Einstellungen → **System**: zeigt, ob Backend, Datenbank, Datenordner, Steam, Bridge und Keys in Ordnung sind. |
| „PlatinVault-Server nicht erreichbar – der Browser hat keine Antwort bekommen“ | Der Server antwortet gar nicht. Läuft das Programm bzw. der Container (auf Unraid in der Docker-Liste), und ist die Netzwerkverbindung zum Server in Ordnung? Danach die Aktion noch einmal ausführen. |
| Beim Öffnen steht „PlatinVault hat diese Anfrage abgelehnt: Der Name … ist nicht freigegeben“ | Du rufst PlatinVault unter einem Namen auf, den es nicht kennt (zum Beispiel eine eigene Domain über einen Reverse-Proxy). Trage genau diesen Namen in der Variable `PLATINVAULT_ALLOWED_HOSTS` des Containers ein und starte den Container neu (Abschnitt 3.1 und 3.2). Mit der IP-Adresse des Servers geht es sofort ohne Eintrag. |
| Die Bibliothek bleibt leer | Steam Web API-Key prüfen; im Steam-Profil die Spieldetails auf öffentlich stellen. |
| Keine Live-Popups | Ist das Spiel in PlatinVault angelegt? Läuft PlatinVault auf einem Server: Ist die Bridge verbunden (Symbol in der Taskleiste bzw. `platinvault-bridge status` unter Linux, System-Check)? |
| Unter Linux: „PlatinVault hat auf diesem Rechner kein Steam gefunden“ | Im Reiter **Steam** unter **Steam-Ordner (optional)** den Ordner mit `steamapps` eintragen (Abschnitt 4.1). Bei Flatpak-Steam liegt er meist unter `~/.var/app/com.valvesoftware.Steam/.local/share/Steam`. Unter Einstellungen → **System** zeigt **Gesuchte Orte anzeigen**, wo PlatinVault gesucht hat. Läuft PlatinVault in Docker, sieht es den Steam-Ordner des Rechners nicht – nimm das Linux-Paket oder die Linux-Bridge. |
| Unter Linux kommen keine Live-Popups | Die Erkennung unter Linux ist noch **experimentell** und nicht auf allen Steam-Installationen geprüft (nativ, Flatpak, Snap, Steam Deck). Prüfe im System-Check, ob Steam gefunden wird, und bei der Linux-Bridge `platinvault-bridge status`. Läuft der Dienst im Spielmodus des Steam Deck nicht weiter, bleibt er im Desktop-Modus aktiv. Bitte melde uns, was bei dir zu sehen ist – das hilft, die Erkennung zu verbessern. |
| Unter Linux: „Port 5233 ist belegt“ bei der Installation | Ein anderes Programm benutzt den Port. Installiere mit `./install.sh --port 5300` (oder einer anderen freien Nummer). |
| Popup kommt, aber ohne Ton | Steht der Hinweis **„Ton vom Browser gesperrt“** da, hat der Browser den Ton zurückgehalten: Browser spielen Töne erst nach einem Klick oder Tastendruck auf der Seite, zum Beispiel nach dem Neuladen. Ein Klick gibt den Ton frei, **Verpassten Ton abspielen** spielt ihn nach. Ohne diesen Hinweis: Lautstärke unter Einstellungen → **Live & Sound** erhöhen (neben lautem Spielsound gehen leise Töne unter) und prüfen, ob der Browser-Tab stumm geschaltet ist. |
| Die KI meldet ein Limit | Unter Einstellungen → KI-Recherche zeigt die Liste **Deine KIs**, welche KI wann ein Problem bekam. Mit eingeschaltetem automatischem Wechsel macht PlatinVault mit der nächsten KI weiter und sagt es dir; sonst wählst du selbst eine andere KI („Mit … erneut versuchen“). „Verbindung echt testen“ zeigt bei Gemini zusätzlich den Grund: Tageslimit (später erneut versuchen), Modell überlastet (kurz warten) oder keine Websuche im Gratis-Tarif (Google-Projekt mit Abrechnung nötig). |
| Im KI-Abo-Fenster erscheint eine Warnung mit fremdsprachigen Sätzen | Die Antwort des Chats enthält Sätze in einer anderen Sprache als der Guide. **Korrektur-Prompt kopieren**, im selben Chat einfügen und die neue Antwort einsetzen (Abschnitt 6.2). |
| Bei „Alle auf einmal“ bleibt ein Kapitel **Ausstehend** oder steht bei **Nicht zuordenbare Antworten** | Der Chat hat die Kapitel-ID (K01, K02 …) nicht benutzt oder ist nicht fertig geworden. Meldet der Chat **NOCH OFFEN**, kopiere **Prompt für die restlichen X Kapitel kopieren** (oder schreibe „weiter“) und mach damit weiter, bis er **ALLE KAPITEL FERTIG** meldet. |
| Ein Kapitel ist zu dünn oder veraltet | **Kapitel neu recherchieren** bzw. **Mit meinem KI-Abo neu recherchieren** über dem Kapitelinhalt (Abschnitt 6.2). |
| Ein Kapitel zeigt „konnte nicht recherchiert werden“ | Auf **Kapitel erneut recherchieren** klicken (Details siehe 6.2). Der Grund steht im Hinweis; bei einem Quoten- oder Key-Problem zuerst unter Einstellungen → **KI-Recherche** nachsehen oder über den Pfeil am Knopf eine andere KI wählen. |
| „Der Server wurde neu gestartet, die Recherche wurde unterbrochen“ | Der Server war während der Recherche kurz weg (Update, Neustart). Fertige Kapitel sind gespeichert; starte dieselbe Aktion noch einmal, sie macht dort weiter. |
| Beim Hinzufügen steht „IGDB antwortet nicht ohne Zugangsdaten“ | Die IGDB-Suche braucht die Zugangsdaten einer eigenen Twitch-Anwendung (Einstellungen → **Twitch & IGDB**, 6.4). Ohne sie klickst du auf **Nicht dabei? Ohne IGDB hinzufügen** und gibst Titel, Plattform und Titelbild selbst an (5.3). |
| Das Titelbild wird nicht angenommen | Erlaubt sind PNG, JPEG, WebP und GIF bis 5 MB, kein SVG. Verkleinere das Bild oder wähle ein anderes (5.3). |
| Bei einem Spiel von Hand fehlen Spielzeit, Größe und Statistiken | Das sind Werte, die nur Steam kennt. Erscheinungsdatum, Entwickler und Genres kommen aus IGDB oder aus der KI-Recherche (6.6). |
| Die recherchierte Trophäenliste ist falsch oder unvollständig | Die KI liest Webseiten und kann irren. Prüfe die unter der Liste genannten Quellen und klicke auf **Trophäenliste neu recherchieren**; deine Haken bleiben (6.6). Eine andere KI kannst du über den Pfeil am Knopf wählen. |
| Ein PS4-Spiel zeigt die falschen Trophäen | Die Fassung (PS5 oder PS4) steht nach dem Hinzufügen fest. Ein PS4-Spiel auf der PS5 hat die PS4-Liste. Entferne das Spiel (**Spiel entfernen**) und füge es mit der richtigen Fassung neu hinzu (5.3). |
| Erfolge auf Englisch | Das Spiel hat bei Steam keine deutschen Texte; mit KI-Key übersetzt PlatinVault sie automatisch. |
| Über der Trophäenliste steht „Seltenheitswerte sind nicht aktuell“ bzw. „nicht verfügbar“ | PlatinVault erreicht Steam gerade nicht, um die weltweiten Anteile zu holen. Es zeigt den letzten gespeicherten Stand (mit Datum) und versucht es von selbst wieder. Prüfe die Internetverbindung und ob ein Virenschutz oder eine Firewall PlatinVault den Zugriff ins Netz verbietet; **Kopieren** im Hinweis gibt dir den vollständigen Text für den Support. |
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
   „Der Computer wurde durch Windows geschützt“. Klicke auf **Weitere Informationen** und dann auf **Trotzdem ausführen**. Meldet
   dein Virenschutz die Datei oder das Programm, hilft Abschnitt 11.9.
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

Die Installation läuft nur auf Windows 10 oder 11 (64 Bit) mit einem Prozessor, der x86-64-v2 unterstützt (praktisch jeder ab etwa 2013).

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
- **Warum liegt das Programm im Benutzerordner?** PlatinVault wird pro Benutzer installiert: Programmordner
  `%LOCALAPPDATA%\Programs\PlatinVault`, Daten `%LOCALAPPDATA%\PlatinVault`. Dadurch braucht die Installation keine Administratorrechte, und
  Updates laufen ohne Admin-Bestätigung. Der Ordner `AppData` ist in Windows standardmäßig ausgeblendet – deshalb gibt es Abkürzungen:
  im Menü des Symbols neben der Uhr **Programmordner öffnen**, **Datenordner öffnen** und **Sicherungsordner öffnen**, und in den
  Einstellungen → **System** → **Ordner** je ein Knopf **Öffnen** (dazu **Pfad kopieren**). Der Explorer zeigt dann den Ordner.
- Die Datenbank ist eine einzige Datei (`database\platinvault.db`). Es läuft kein eigener Datenbank-Dienst, und von außen ist nichts erreichbar.
- **Sicherungen** entstehen automatisch: beim Start, alle 6 Stunden und beim Beenden (also auch vor jedem Update) – aber nur, wenn sich
  etwas geändert hat. Die 20 neuesten liegen in `backups\recent`, dazu eine pro Tag für 60 Tage in `backups\daily`.
- **Sicherung zurückspielen:** PlatinVault über das Symbol beenden, dann in der Eingabeaufforderung
  `"<Programmordner>\PlatinVault.exe" --restore "<Pfad zur Sicherung>.db"` ausführen. PlatinVault prüft die gewählte Sicherung, sichert vorher
  den aktuellen Stand (er liegt danach als neueste Sicherung in `backups\recent`) und ersetzt dann die Datenbank. Danach PlatinVault normal starten.

## 11.6 Updates

PlatinVault sieht von selbst nach, ob es eine neue Version gibt – so oft, wie unter **Nach Updates suchen** eingestellt (Standard: täglich) –, nur Releases, oder auch Betas, wenn du unter Einstellungen →
**System** → **Updates** beim **Update-Kanal** **Beta** (oder **Nightly**) gewählt hast (Kapitel 12). Gibt es eine, erscheint im Menü des
Symbols **Update auf … installieren** und eine Benachrichtigung. Sofort nachsehen kannst du mit **Nach Updates suchen** im Menü des
Symbols oder mit **Jetzt nach Updates suchen** in den Einstellungen; dort installierst du ein gefundenes Update auch direkt mit
**Update installieren**. Nach deiner Bestätigung lädt PlatinVault den Installer, prüft seine Prüfsumme, beendet sich (mit Sicherung)
und der Installer aktualisiert das Programm. Deine Daten bleiben unverändert. Sobald die Windows-Dateien digital signiert sind,
prüft PlatinVault zusätzlich die Signatur des geladenen Installers und startet ihn nur, wenn sie gültig ist. Hast du ein
älteres, unsigniertes PlatinVault installiert, gilt diese Prüfung erst nach dem Update auf eine signierte Version.

**Release-Notizen lesen:** In den Einstellungen unter **System** → **Updates** (und im Update-Symbol der Kopfzeile) öffnet
**Release-Notizen ansehen** ein eigenes Fenster **Das ist neu** mit den Änderungen der Version – ohne dass du die GitHub-Seite öffnen musst.
**Auf GitHub ansehen** öffnet die Release-Seite im Browser; **Esc** schließt das Fenster. Das Fenster öffnet sich nach einem Update
auch von selbst (siehe 12.1, „Das ist neu nach einem Update“).

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
| Das Update meldet „… wird von einem anderen Prozess verwendet“ oder „Die Programmdateien konnten nicht kopiert werden“ | Der Installer wartet bis zu 30 Sekunden („Warte, bis Windows die Programmdateien freigibt …“), bis sich das alte Programm und seine Hilfsprogramme beendet haben, und versucht es mehrfach. Klappt es trotzdem nicht, nennt die Meldung die Programme, die die Dateien festhalten: schließe sie (oft ein Virenschutz, der die Dateien gerade prüft) oder starte den PC neu und führe das Update erneut aus. Deine Daten bleiben dabei unverändert. |
| Der Virenschutz meldet PlatinVault | Siehe Abschnitt 11.9. |

## 11.9 Virenschutz meldet PlatinVault

**Warum kann das passieren?** Die Windows-Dateien von PlatinVault sind noch nicht digital signiert. Gleichzeitig lädt das Programm bei einem
Update einen Installer aus dem Internet, startet ihn und beendet sich selbst – ein Ablauf, den die Verhaltensüberwachung mancher
Virenschutzprogramme bei einem unbekannten, unsignierten Programm als verdächtig einstuft. Es handelt sich
um einen Fehlalarm; der Quellcode ist öffentlich einsehbar. Manche Programme verschieben dabei Dateien in die Quarantäne.

**Was du tun kannst:**

1. Trage in deinem Virenschutz **Ausnahmen** ein für den Programmordner `%LOCALAPPDATA%\Programs\PlatinVault` und den Datenordner
   `%LOCALAPPDATA%\PlatinVault`.
2. Wurden Dateien in die Quarantäne verschoben: Stelle die **Schlüsseldateien** (deine verschlüsselten API-Keys im Datenordner,
   Unterordner `server\secrets` und `server\dp-keys`) wieder her. Stelle **nicht** die Hilfsdateien der Datenbank wieder her
   (`platinvault.db-wal` und `platinvault.db-shm`) – sie gehören zu einem früheren Zustand und können die Datenbank durcheinanderbringen.
   Die Datenbank selbst (`platinvault.db`) und die Sicherungen sind bei Bedarf in Ordnung; wenn nötig, spiele eine Sicherung zurück (Abschnitt 11.5).
3. Führe den **Installer erneut aus** (gleiche oder neuere Version). Er erkennt einen beschädigten Programmordner, in dem zum Beispiel
   `PlatinVault.exe` fehlt, repariert die Installation und lässt deine Daten unberührt.
4. Blockiert der Virenschutz danach weiter Dateinamen, obwohl du sie freigegeben hast: Entferne die PlatinVault-Einträge aus der
   Quarantäne bzw. dem Blockierverlauf oder starte Windows neu. Meist ist der Eintrag dann vergessen. Du kannst die Datei auch dem Hersteller deines Virenschutzes als Fehlalarm melden.

# 12 Updates, Beta- und Nightly-Versionen

Von PlatinVault gibt es drei veröffentlichte Kanäle:

- **Releases** – die normalen, fertigen Versionen. Das ist der Standard und für die meisten die richtige Wahl.
- **Betas (`β`)** – geprüfte Vorabversionen mit neuen Funktionen vor der endgültigen Freigabe. Eine Beta zeigt hinter der Versionsnummer ein **β**, zum Beispiel `v2026.11.12.13.557 (β)`.
- **Nightlies (`α`)** – frühe Vorabversionen mit den allerneuesten Änderungen direkt aus der laufenden Entwicklung. Eine Nightly zeigt hinter der Versionsnummer ein **α** (Alpha), zum Beispiel `v2026.11.20.8.1234 (α)`.

## 12.1 Den Update-Kanal wählen

Der **Update-Kanal** legt fest, welche Art von Versionen dir PlatinVault anbietet. Du wählst ihn auf jeder Plattform an derselben Stelle:

1. Öffne die **Einstellungen** und wechsle zu **System**.
2. Suche den Abschnitt **Updates**. Der **Update-Kanal** steht direkt unter **Installierte Version**.
3. Klicke auf einen der drei Knöpfe. Die Wahl wird sofort gespeichert; darunter erklärt ein kurzer Text, was der Kanal bedeutet.

Die drei Knöpfe:

- **Keiner:** Du bekommst nur fertige Releases. Das ist der Standard.
- **Beta:** Du bekommst Betas (`β`) und Releases – immer die neuere von beiden, aber keine Nightlies.
- **Nightly:** Du bekommst Nightly-Versionen (`α`) und Releases – immer die neuere von beiden, aber keine Betas.

Hast du früher einen der beiden alten Schalter für Vorabversionen benutzt, behältst du deine Wahl: War „Nightly“ eingeschaltet, ist jetzt **Nightly** gewählt, sonst bei eingeschaltetem „Beta“ der Kanal **Beta**, sonst **Keiner**.

Es gibt **nie einen Schritt zurück**: Wechselst du auf einen ruhigeren Kanal, während eine Nightly oder Beta läuft, bleibt deine installierte Version, bis im gewählten Kanal eine Version erscheint, die mindestens so neu ist. Diese Version wird dir dann automatisch angeboten.

Weiter unten siehst du mit **Jetzt nach Updates suchen** das Ergebnis: „Du bist auf dem neuesten Stand“,
„Neue Nightly … verfügbar“, „Neue Beta … verfügbar“ oder „Neues Release … verfügbar“ – mit einem Knopf zu den Release-Notizen. PlatinVault fragt die
veröffentlichten Versionen bei GitHub ab (ohne Anmeldung; nur ein paar Mal am Tag, eine Suche von Hand höchstens einmal pro Minute).
Klappt das nicht, steht der vollständige Grund dort, zum Beispiel keine Internetverbindung oder ein ausgeschöpftes Abfrage-Kontingent
bei GitHub.

Gibt es für deine Installation eine neuere Version, erscheint in der Kopfzeile ein kleines **Update-Symbol** (Pfeil nach oben mit grünem Punkt). Ein Klick zeigt die neue Version, die Release-Notizen und – im Windows-Programm – **Update installieren**; bei Unraid, Docker und NAS steht dort kurz, wie du den Container aktualisierst. Mit **Alle Update-Einstellungen** springst du zu diesem Abschnitt. Ist alles aktuell, die Prüfung nicht möglich oder dies ein lokaler Entwicklungsstand, siehst du kein Symbol; Fehler der Prüfung stehen nur hier in den Einstellungen.

**Das ist neu nach einem Update:** Startest du PlatinVault zum ersten Mal nach einem Update, öffnet sich von selbst das Fenster
**Das ist neu in …** mit den Änderungen der Version, die jetzt läuft – in der Sprache der App. Ein Klick auf **Schließen**, die Taste
**Esc** oder ein Klick neben das Fenster schließt es; danach kommt es für diese Version nicht wieder. Es erscheint nicht bei einer
Neuinstallation, nicht bei einem lokalen Entwicklungsstand und erst, wenn die Einführungstour beim ersten Start beendet ist. Später liest du den Text
jederzeit nach: Ein Klick auf die Versionsnummer auf der Seite **Über PlatinVault** (Info-Symbol in der Kopfzeile)
öffnet **Version & Updates**, dort steht **Das ist neu in dieser Version**. Die Texte gehören zum Programm; dafür brauchst du keine
Internetverbindung.

Wie oft PlatinVault von selbst nach Updates sucht, stellst du in diesem Abschnitt unter **Nach Updates suchen** ein: **Bei jedem Start**, **Alle 4 Stunden**, **Täglich** (Standard), **Wöchentlich**, **Monatlich** oder **Nie (nur von Hand)**. Das gilt für jede Plattform, auch für das Windows-Programm. Von Hand suchst du mit **Jetzt nach Updates suchen** hier – oder über die Versionsnummer auf der Seite **Über PlatinVault** (Info-Symbol in der Kopfzeile): ein Klick öffnet **Version & Updates** mit dem Knopf **Nach Updates suchen**, dem Zeitpunkt der letzten Prüfung und dem Ergebnis. Wird dabei eine neue Version gefunden, erscheint sofort das Update-Symbol in der Kopfzeile.

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
| `ghcr.io/sushiflux/platinvault:beta` | Betas (`β`) und Releases (immer die neueste von beiden) |
| `ghcr.io/sushiflux/platinvault:nightly` | Nightlies (`α`) und Releases (immer die neueste von beiden) |

Wähle in der App den passenden **Update-Kanal**: **Keiner** für `:latest`, **Beta** für `:beta`, **Nightly** für `:nightly`. Stimmen Kanal und Tag nicht überein, nennt dir PlatinVault in den Einstellungen und im Update-Symbol den Tag, den deine Wahl braucht. So stellst du den Tag um – deine Daten in Datenbank und Appdata bleiben dabei
erhalten:

- **Unraid:** Docker-Reiter → PlatinVault-Container anklicken → **Edit** → bei **Repository** den Tag ändern → **Apply**. Neue Versionen
  holst du danach wie gewohnt mit **Check for Updates** und **Update**. In den Community Applications kannst du den Tag schon bei der
  Installation wählen.
- **Docker Compose:** in der `docker-compose.yml` die Zeile `image:` ändern und im selben Ordner `docker compose pull && docker compose up -d`
  ausführen.
- **Andere NAS:** in der Container-Verwaltung deines NAS das Image des PlatinVault-Containers auf den gewünschten Tag umstellen und den
  Container mit dem neuen Image neu erstellen.

Zurück von `:nightly` oder `:beta` auf einen ruhigeren Kanal stellst du erst um, wenn dort eine Version erschienen ist, die mindestens so neu ist wie deine laufende Version – vorher
wäre es ein Schritt zurück auf eine ältere Version. Die Einstellungen sagen dir, wann es so weit ist.

## 12.4 Linux-Paket

Das Linux-Paket aktualisiert sich nicht selbst. PlatinVault meldet neue Versionen wie überall (Update-Symbol in der Kopfzeile, Einstellungen →
**System** → **Updates**) und zeigt dir dort die drei Befehle zum Aktualisieren; Schritt für Schritt steht es in Abschnitt 13.5.

## 12.5 Schutz deiner Daten

Startest du eine ältere PlatinVault-Version mit einer Datenbank, die schon eine neuere Version (zum Beispiel eine Beta) benutzt hat,
startet PlatinVault nicht und ändert **nichts** an der Datenbank. Die Meldung nennt die Version, die du brauchst (im Container-Protokoll
auf Deutsch und Englisch, im Windows-Programm in einem eigenen Fenster). Installiere diese Version (oder eine neuere) wieder oder warte auf
das nächste Release.

# 13 PlatinVault für Linux

Auf dem Linux-PC, auf dem Steam läuft (Desktop-Linux, Steam Deck im Desktop-Modus, Bazzite), installierst du PlatinVault als **Linux-Paket**.
Es ist dieselbe App wie im Browser, mit eigener Datenbank, und liest Steam auf diesem Rechner selbst – eine Bridge brauchst du nicht.
Bedient wird es im Browser. Voraussetzungen: 64-Bit-Linux (x86_64) mit einem Prozessor, der x86-64-v2 unterstützt (praktisch jeder ab etwa 2013), ein Webbrowser, möglichst systemd (ohne startet das Menü-Symbol
PlatinVault selbst) und `libicu` (meist schon da; ohne sie nutzen Datum und Zahlen ein neutrales Format).

> [!HINWEIS]
> **Experimentell:** Das Lesen der Steam-Dateien unter Linux (Statistikdatei, Erkennung des Steam-Kontos, Sicht auf Flatpak-Prozesse) ist noch nicht
> auf einem echten Linux-Steam geprüft, und ob der Dienst im Spielmodus des Steam Deck weiterläuft, ist offen (im Desktop-Modus läuft er).
> Funktioniert etwas nicht, siehe 13.6, und melde uns gern, was bei dir passiert.

## 13.1 Installieren

1. Lade unter **Releases** auf GitHub (`github.com/sushiflux/PlatinVault`) die Datei `PlatinVault-linux-x64-<Version>.tar.gz` herunter.
2. Öffne ein Terminal im Download-Ordner und führe aus – ohne Root, nichts außerhalb deines Home-Ordners:

   ```
   tar xzf PlatinVault-linux-x64-<Version>.tar.gz
   cd PlatinVault-linux-x64-<Version>
   ./install.sh
   ```

3. Der Installer startet PlatinVault und nennt die Adresse `http://127.0.0.1:5233/`. Im Anwendungsmenü gibt es außerdem den Eintrag
   **PlatinVault**, der die Seite im Browser öffnet. Mach weiter mit Kapitel 4.
4. Ist der Port 5233 belegt, hält der Installer an und sagt es dir. Mit `./install.sh --port 5300` wählst du einen anderen.
   `./install.sh --no-systemd` richtet keinen Dienst ein; dann startet das Menü-Symbol PlatinVault selbst.

## 13.2 Wo liegt was

| Was | Wo |
|---|---|
| Programm | `~/.local/share/platinvault/app/` |
| Daten (Datenbank, automatische Sicherungen, verschlüsselte Schlüssel) | `~/.local/share/platinvault/data/` |
| Einstellungen (Port, Adresse, Datenordner) | `~/.config/platinvault/platinvault.env` (`PLATINVAULT_PORT`, `PLATINVAULT_BIND`, `PLATINVAULT_DATA_DIR`) |
| Dienst | systemd-Benutzerdienst `platinvault` |
| Protokoll | `journalctl --user -u platinvault` oder `~/.local/share/platinvault/platinvault.log` |

Den Ordner `data` gibst du nicht frei und sicherst ihn als Ganzes: Wer ihn hat, hat auch deine API-Schlüssel. PlatinVault sichert die
Datenbank selbst (Start, alle 6 Stunden, Stoppen), wie in Kapitel 3.2 beschrieben.

## 13.3 Starten, stoppen, öffnen

`~/.local/share/platinvault/app/ctl.sh start|stop|restart|status|url|open` steuert PlatinVault: `status` zeigt, ob es läuft, `url` nennt die
Adresse, `open` öffnet sie im Browser.

## 13.4 Nur auf diesem Rechner erreichbar

PlatinVault lauscht standardmäßig nur auf `127.0.0.1`, also nur auf diesem Rechner. Trägst du in `platinvault.env` bei `PLATINVAULT_BIND` eine
Netzwerkadresse ein, ist PlatinVault für das ganze Netz erreichbar – **es gibt keine Anmeldung**, jeder dort kann alles sehen und ändern.
Tu das nur im vertrauenswürdigen Heimnetz und gib den Port nie ins Internet frei.

## 13.5 Aktualisieren und Deinstallieren

- **Aktualisieren:** Lade das neue Paket, entpacke es und führe seine `install.sh` erneut aus. Die alte Version wird beendet (mit einer letzten
  Sicherung) und ersetzt, deine Daten bleiben erhalten. Die App zeigt dir bei einem verfügbaren Update diese Schritte und den Dateinamen.
- **Deinstallieren:** `~/.local/share/platinvault/app/uninstall.sh`. Es fragt, ob deine Daten behalten werden – Standard ist behalten.

## 13.6 Probleme unter Linux

| Problem | Was hilft |
|---|---|
| Steam wird nicht gefunden | Im Reiter **Steam** unter **Steam-Ordner (optional)** den Ordner mit `steamapps` eintragen (4.1). Der System-Check listet die durchsuchten Orte. |
| Keine Live-Popups | Die Erkennung ist experimentell. Prüfe, ob Steam gefunden wird (System-Check), dass das Spiel in PlatinVault angelegt ist, und bei Flatpak-Steam, ob der Steam-Ordner stimmt. Melde uns, was du siehst. |
| Port 5233 belegt | `./install.sh --port 5300` oder einen anderen freien Port. |
| Die Seite öffnet sich nicht | `ctl.sh status` zeigt, ob PlatinVault läuft; das Protokoll mit `journalctl --user -u platinvault`. |
| Nach dem Wechsel in den Spielmodus des Steam Deck ist PlatinVault weg | Ob der Benutzerdienst dort weiterläuft, ist nicht geprüft. Im Desktop-Modus mit `ctl.sh start` neu starten. |
