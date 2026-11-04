# 100%-Guide mit Claude Code erstellen (Claude-Abo)

> **Einfacher geht es in der App:** Auf der Spielseite ist unter „Guide vervollständigen“ der Weg **„Mit deinem KI-Abo (ohne API-Key)“**.
> PlatinVault erzeugt dort die Prompts (mit Steam-AppID, deinen Recherche-Bausteinen und der Guide-Sprache), prüft die Antworten deines Chats
> beim Einfügen und speichert die Kapitel – ohne Datei und ohne Umweg. Bei vielen Kapiteln gibt es einen **Sammel-Prompt** (ein Prompt für alle
> Kapitel, ein Feld für alle gesammelten Antworten; die Antworten dürfen auch als eine Datei kommen). Dieses Dokument ist der Weg für **Claude Code** (ein ganzer Guide als
> Datei, die du über „Guide importieren“ lädst).

PlatinVault darf dein Claude-Abo nicht selbst benutzen (Anthropic erlaubt Drittanbieter-Apps nur API-Keys). Du kannst
den Guide aber selbst in **Claude Code** erstellen lassen – das läuft über dein Abo – und die fertige Datei in
PlatinVault über **„Guide importieren“** auf der Startseite laden.

## So geht's

1. Claude Code in einem beliebigen Ordner starten.
2. Den Prompt unten kopieren, `<SPIEL>` und `<STEAM-APPID>` ersetzen (AppID steht in der Steam-Store-URL,
   z. B. `store.steampowered.com/app/1091500/` → `1091500`) und abschicken.
3. Claude Code schreibt die Datei `<slug>.platinvault.json` in den Ordner.
4. In PlatinVault auf der Startseite „Guide importieren“ klicken und die Datei wählen.

Tipp: Ein Guide aus PlatinVault („Exportieren“ auf der Spielseite) hat dasselbe Format – so kannst du Guides auch
mit Freunden teilen.

---

## Prompt (zum Kopieren)

```text
Erstelle einen vollständigen 100%-Komplett-Guide für das Spiel „<SPIEL>“ (Steam-AppID <STEAM-APPID>) und speichere
ihn als JSON-Datei <slug>.platinvault.json im aktuellen Ordner (slug = Spieltitel klein, mit Bindestrichen).

Vorgehen:
1. Analyse: Recherchiere im Web. Schritt 1, vor allen anderen Quellen: Suche ein Wiki speziell zu diesem Spiel (von Fans oder
   offiziell – z. B. das Fandom-Wiki des Spiels, ein offizielles Spiel-Wiki oder ein Fan-Wiki von der Art wie ffresonance.wiki für
   Final Fantasy Resonance; viele Spiele haben keins). Hat es eine deutsche Fassung (Sprachpfad wie /de/ oder Sprachumschalter),
   nimm diese. Nenne ein Wiki nur, wenn du es wirklich gefunden hast, sonst überspring den Schritt ohne Erwähnung; erfinde nie
   eine Wiki-Adresse. Solche Wikis sind oft Fan-Seiten: Zahlen (HP, Level, Drop-Raten) mit einer zweiten Quelle prüfen, wo es
   eine gibt, und Widersprüche nennen; in eigenen Worten schreiben, nie Text übernehmen; jede benutzte Wiki-Seite in "sources"
   aufführen. Schritt 2: zuerst deutsche Quellen (pcgames.de, spieletipps.de, GIGA, gameswelt.de, deutsche
   Steam-Community-Guides, bei MMO-/Online-Spielen mein-mmo.de), dann englische als Ergänzung (Game8, Fextralife, PowerPyx,
   PSNProfiles, PlayStationTrophies.org); das sind nur Startpunkte, such selbst weiter. Kläre,
   was für ein Spiel das ist (Genre, Struktur, Spielzeit, Mehrspieler), was zu 100% gehört und was verpassbar ist.
   Hol dir die offizielle Erfolgsliste von https://steamcommunity.com/stats/<STEAM-APPID>/achievements.
2. Kapitel wählen: 5–8 Kapitel, die für GENAU dieses Spiel den größten Nutzen bringen – spielspezifisch benannt.
   Die Komplettlösung („Walkthrough“) steht, wenn es eine belastbare, vollständige Quelle dafür gibt, IMMER als allererstes
   Kapitel (sie zählt nicht zu den 5–8); gibt es keine, lass sie weg – erfinde keine.
   Baukasten: Komplettlösung (siehe oben, Kapitel 1) · Roadmap & Verpassbares · Nebenquests & Aktivitäten · Rätsel & versteckte Mechaniken ·
   Sammelobjekte mit Fundorten · Builds, Skills, Perks · Waffen & Ausrüstung · Crafting & Upgrades · Leveln, Geld &
   Ressourcen farmen · Bosse, Gegner & Schwächen · Fahrzeuge, Begleiter · Fraktionen, Beziehungen, Romanzen ·
   Enden & Entscheidungen · Schwierigkeitsgrade, New Game+, Challenges · Multiplayer-/Online-Erfolge · DLC ·
   Bekannte Bugs & verbuggte Erfolge. Nimm NUR Themen, die es in diesem Spiel laut Recherche wirklich gibt – nie ein Kapitel, das nur
   erklärt, dass es etwas nicht gibt, oder es mit anderem Stoff auffüllt (z. B. kein Bestiarium in einer Aufbau- oder Fabriksimulation ohne Kampf),
   und nie ein Thema nur, weil es zur Spielart typisch ist. Weniger Kapitel sind bei einem kleinen Spiel richtig. Ergänze eigene Kapitel für besondere Mechaniken.
3. Letztes Kapitel immer „Alle Steam-Erfolge“: eine Checkliste mit JEDEM offiziellen Erfolg. Als itemId den
   offiziellen Steam-API-Namen des Erfolgs verwenden, wenn du ihn findest (z. B. aus der Steam-Seite oder SteamDB-
   Einträgen, die dir vorliegen) – dann hakt PlatinVault freigeschaltete Erfolge automatisch ab. Spalten: Erfolg,
   Bedingung, Kategorie, Tipp.
4. Jedes Kapitel gründlich recherchieren: echte Namen, Fundorte, Bedingungen, Zahlenwerte, Reihenfolgen.
   Die Komplettlösung ist der Spielverlauf in der Reihenfolge des Spiels, aufgebaut wie ein gedrucktes Lösungsbuch (nur der
   Aufbau, nie dessen Text). Jeder Teil (Story-Kapitel/Mission): "heading" mit "level": 1 und einem Orientierungssatz in
   "htmlContent" → "overview" „Auf einen Blick“ (empfohlenes Level, Anzahl Sammelobjekte, Erfolge des Teils als API-Namen,
   wichtige Gegenstände, Verpassbares, endet am Point of no Return?) → je GEBIET in der Reihenfolge, in der man durchkommt:
   "heading" mit "level": 2 + Orientierungssatz, die Route als "story" mit "variant": "steps", die Funde des Gebiets als
   "checklist_table" (nur wenn es welche gibt, mit Spalte „Verpassbar“), Nebenquests als EIN kompakter "alert" mit "kind":
   "side_quests", eine "boss"-Karte direkt nach dem Schritt, der zum Kampf führt, ein "alert" mit "kind": "achievement" und
   "apiName", wo ein Erfolg freigeschaltet wird → am Ende vor einem Point of no Return ein "alert" mit "kind":
   "point_of_no_return" und "items" (was du vorher erledigen musst). Weitere Hinweisarten an ihrer Stelle: "must_have"
   (Wichtig – unbedingt mitnehmen, mit Fundort), "missable" (Verpassbar ab hier), "optional" (optionaler Boss, Zusatzgebiet),
   "tip" (Tipp).
   Lesefluss: Jeder Schritt sind 1–2 ganze Sätze in der Du-Form („Geh ins Dorf Kol und übernachte im Gasthaus.“) – kein
   „Ziel:“-Telegrammstil, keine kopierten Ziel-Listen; triviale Ziele zu einem Schritt zusammenfassen; Hinweise als eigener
   "alert", nie im Schritt-Text. Schreibe in eigenen Worten, übernimm keine längeren Passagen wörtlich. Ein Item, das in einem
   anderen Kapitel eine eigene Zeile hat: „siehe Kapitel …“ statt Wiederholung.
   Nebenquests: die vollständige Lösung (Schritte, Kämpfe, Belohnungen) steht NUR im Kapitel „Nebenquests“; die Komplettlösung
   nennt sie nur kompakt (Name, ab wann, Zeitfenster, kurze Belohnung). Hat das Spiel Nebenquests, gehört dieses Kapitel dazu.
   Jedes Kapitel endet mit einem "sources"-Abschnitt: die Seiten, auf die du dich wirklich gestützt hast (nur echte
   http(s)-Adressen, keine erfundenen).
5. Optional (nur wenn ich es will, Spoiler!): ein zusätzlicher Tab mit "slug": "story-lore", "title": "Story & Lore",
   "sortOrder": 900 – Story einfach erklärt (Akt für Akt, mit Ende), Charaktere mit Hintergrund und Beziehungen,
   Welt & Begriffe, Insiderwissen & Entwicklungsgeschichte. Alle Abschnitte dort mit "spoiler": true.

Regeln:
- Plattform (verbindlich): Ich spiele die STEAM-Version auf dem PC. Erfolge sind Steam-Erfolge – nie PlayStation-Trophäen, Platin-Trophäe,
  Xbox-Erfolge oder Gamerscore; „100 %“ heißt alle Steam-Erfolge. Steuerung ist Tastatur und Maus (oder ein Controller am PC), keine
  Konsolen-Tastennamen, außer die PC-Version zeigt sie selbst. Reine Konsolen-Inhalte (Konsolen-Exklusives, Konsolen-Patchstände,
  PSN-/Xbox-exklusive DLCs, Cross-Save) weglassen; unterscheidet sich die PC-Version, gilt sie. Konsolen-Seiten (PSNProfiles,
  PlayStationTrophies.org, PowerPyx-Trophäen-Guides, TrueAchievements …) darfst du für alles nutzen, was am PC genauso ist (Story,
  Route, Verpassbares, Boss-Taktiken, Reihenfolge), nennst sie aber nicht als „PS4-/PlayStation-Guide“. Die Erfolgsliste, die Namen
  und die Bedingungen kommen NUR von Steam – weicht eine Konsolen-Trophäe ab, gilt Steam. Zeitangaben gelten für alle Steam-Erfolge am PC.
- Sprache (verbindlich): Der ganze Guide ist auf Deutsch – jeder Satz, jedes Ziel, jede Anweisung, in eigenen Worten; nie Sätze
  aus englischen Quellen oder der englischen Fassung übernehmen. Namen (Orte, Gegenstände, Gegner, Figuren, Quests) so, wie
  sie in der deutschen Fassung des Spiels heißen; gibt es keine, den Namen aus dem Spiel nehmen und beim ersten Vorkommen kurz
  erklären. Den Originalnamen höchstens EINMAL pro Kapitel beim ersten Vorkommen in Klammern – nur bei Eigennamen, nie bei Sätzen.
- Beschaffungswege: Für alles, was man nicht einfach aufhebt (Waffen, legendäre Ausrüstung, Beschwörungen, besondere
  Gegenstände, Belohnungen von Questreihen), die VOLLSTÄNDIGE Kette in der richtigen Reihenfolge – Voraussetzungen und ab wann,
  was gesammelt wird, wohin man geht, und JEDER nötige Kampf, auch ein überraschender schwerer am Ende (vorher warnen, "boss"-Karte).
- Bosse: jeder Bosskampf bekommt eine "boss"-Karte (nur Text). HP nur mit Beleg: jeder Eintrag mit Wert, wofür er gilt
  ("appliesTo") und Quelle ("source"); ohne Beleg { "value": "Nicht dokumentiert" } – nie Zahlen erfinden. "achievements" und
  "apiName" nur mit exakten Steam-API-Namen aus der Erfolgsliste.
- Erfinde NICHTS. Gibt es zu einem Punkt (noch) keine verlässliche Quelle, schreibe „Noch nicht dokumentiert
  (Stand <heutiges Datum>)“.
- Rätsel (verbindliche Regel): JEDES Rätsel, jeder Code, jede Reihenfolge, Kombination und jeder versteckte Mechanismus
  („mach X in der richtigen Reihenfolge“) bekommt einen eigenen "puzzle"-Abschnitt – nie nur einen vagen Satz in einer
  Tabellenzelle. Er nennt WO genau ("puzzle.location": Gebiet, Landmarke, Weg dorthin; "puzzle.requirements": was vorher /
  ab wann; "puzzle.missable") und die VOLLSTÄNDIGE LÖSUNG in "puzzle.steps" (jeder Schritt in der genauen Reihenfolge, mit den
  konkreten Antworten, Codes, Symbolen, Positionen, Orten), optional "puzzle.hints" (1–3 Hinweise von vage bis konkret) und
  "puzzle.source". Kannst du die Lösung nicht belegen: "puzzle.steps" leer lassen (PlatinVault zeigt dann „Lösung nicht
  dokumentiert“) – NIE Codes, Positionen oder Reihenfolgen erfinden. PlatinVault zeigt Ort und Voraussetzungen offen; Tipps
  („Tipp zeigen“, einer nach dem anderen) und Lösungsweg („Lösungsweg zeigen“) bleiben verborgen, bis der Spieler sie anfordert.
- Jede Checklisten-Zeile braucht eine im ganzen Guide eindeutige, sprechende itemId in kebab-case
  (z. B. "waffe-mantis-blades"); filterCategory muss in categories vorkommen.
- Verknüpfte Häkchen: Kommt ein Eintrag in einem anderen Kapitel schon als eigene Checklisten-Zeile vor (z. B. ein Gegenstand der
  Komplettlösung, der im Kapitel „Waffen“ eine Zeile hat), setze an der Zeile des zweiten Kapitels (und bei Nebenquest-Einträgen
  in "quests") das Feld "ref" mit der itemId der ursprünglichen Zeile. Eine Zeile mit "ref" hat kein eigenes Häkchen, sondern teilt das
  der ursprünglichen Zeile. "ref" nur setzen, wenn die itemId wirklich existiert (bei einem Prompt von PlatinVault steht eine Liste
  „itemId | Name“ dabei) – nie raten; ein "ref" auf ein anderes "ref" ist nicht erlaubt. PlatinVault verknüpft eindeutige Namen
  nach dem Speichern auch selbst.
- htmlContent ist reiner Text (kein HTML).

Dateiformat (genau so, gültiges JSON):
{
  "format": "platinvault-guide",
  "formatVersion": 1,
  "exportedAt": "<ISO-Datum>",
  "exportedBy": "Claude Code",
  "game": {
    "title": "<SPIEL>",
    "subtitle": "prägnanter deutscher Untertitel",
    "steamAppId": <STEAM-APPID>,
    "coverUrl": "",
    "heroBannerUrl": "",
    "accentColor": "emerald | crystal | gold | crimson | purple",
    "estimatedHours": "z. B. ca. 80–120 Stunden für alle Steam-Erfolge",
    "missablesInfo": "z. B. 3 verpassbare Erfolge",
    "difficultyInfo": "z. B. Schwierigkeit egal, 1 Durchgang",
    "genreTags": "z. B. Action-RPG, Open World",
    "modules": "both"
  },
  "tabs": [
    {
      "title": "1. Roadmap & Verpassbares",
      "icon": "mdi:compass-outline",
      "sortOrder": 1,
      "sections": [
        { "type": "alert", "variant": "danger", "title": "...", "htmlContent": "..." },
        { "type": "story", "title": "...", "paragraphs": ["Absatz 1", "Absatz 2"], "spoiler": true },
        { "type": "heading", "level": 1, "title": "Kapitel 1 – Name", "htmlContent": "ein Orientierungssatz" },
        { "type": "overview", "title": "Auf einen Blick", "recommendedLevel": "z. B. 10–15", "counts": [ { "label": "Schatztruhen", "count": 12 } ],
          "achievements": ["API_NAME"], "keyItems": ["..."], "missables": ["..."], "endsAtPointOfNoReturn": false },
        { "type": "heading", "level": 2, "title": "Gebiet", "htmlContent": "ein Orientierungssatz für das Gebiet" },
        { "type": "story", "variant": "steps", "paragraphs": ["Geh ins Dorf Kol und übernachte im Gasthaus.", "Sprich danach mit ..."] },
        { "type": "alert", "kind": "must_have", "title": "...", "htmlContent": "Item mit genauem Fundort" },
        { "type": "alert", "kind": "side_quests", "title": "Nebenquests hier", "htmlContent": "",
          "quests": [ { "title": "Name", "availableFrom": "ab wann", "missable": "bis wann (nur wenn verpassbar)", "reward": "kurz", "ref": "itemId im Nebenquest-Kapitel (optional)" } ] },
        { "type": "boss", "bossId": "eindeutige-id", "title": "Name des Bosses", "location": "Wo", "recommendedLevel": "z. B. 25",
          "hp": [ { "value": "12.000", "appliesTo": "Normal", "source": "Name der Quelle" } ], "weaknesses": ["..."], "resistances": ["..."],
          "immunities": ["..."], "attacks": [ { "name": "...", "note": "..." } ], "phases": ["..."], "party": "...",
          "tactics": ["Schritt 1", "Schritt 2"], "rewards": ["..."], "missable": false, "achievements": ["API_NAME"], "source": "Name der Quelle" },
        { "type": "alert", "kind": "achievement", "apiName": "API_NAME", "title": "...", "htmlContent": "was du hier tun musst" },
        { "type": "alert", "kind": "point_of_no_return", "title": "...", "htmlContent": "...", "items": ["vorher erledigen"] },
        { "type": "puzzle", "title": "Name des Rätsels", "htmlContent": "Die Aufgabe in 1–2 Sätzen, ohne Lösung",
          "puzzle": { "itemId": "eindeutige-id", "location": "Wo genau", "requirements": "Was vorher / ab wann", "missable": false,
                      "hints": ["vager Hinweis", "konkreterer Hinweis"], "steps": ["Schritt 1", "Schritt 2"], "source": "Name der Quelle" } },
        { "type": "cards_grid", "columns": 2, "cards": [
          { "title": "...", "badgeText": "...", "badgeVariant": "s", "introText": "...",
            "bullets": [ { "icon": "mdi:check", "title": "...", "body": "..." } ] } ] },
        { "type": "checklist_table", "table": {
          "tableId": "kurze-id", "caption": "...", "checkable": true,
          "searchPlaceholder": "Suchen...", "categoryFilterLabel": "Alle Kategorien", "categories": ["..."],
          "columns": [ { "key": "name", "label": "Name", "isRowHeader": true },
                       { "key": "ort", "label": "Fundort" } ],
          "rows": [ { "itemId": "eindeutige-id", "ref": "itemId der ursprünglichen Zeile (optional)", "filterCategory": "...", "cells": { "name": "...", "ort": "..." } } ] } },
        { "type": "sources", "sources": [ { "title": "Name der Seite", "url": "https://..." } ] }
      ]
    }
  ]
}
Jeder Abschnitt kann "spoiler": true bekommen (wird dann erst auf Klick gezeigt); "story" ist Fließtext in Absätzen.
Varianten für alert: danger (Verpassbares), gold (Profi-Tipps), crystal (Infos), emerald (Erfolge). Ein alert mit "kind"
(must_have, missable, optional, tip, achievement, point_of_no_return, side_quests) bekommt Symbol und Beschriftung der Hinweisart;
ohne "variant" gilt gold, danger, emerald, crystal, emerald, danger, emerald in dieser Reihenfolge.
Ein Tab darf "topicId" tragen (z. B. "walkthrough", "quests"); unbekannte Werte werden beim Import verworfen.
Ältere Dateien ohne "puzzle", "heading", "level", "kind", "boss", "overview", "sources" und "variant": "steps" bleiben gültig und werden unverändert gelesen.
badgeVariant: s, a, b oder ok. Cover und Banner leer lassen – PlatinVault holt sie über die Steam-AppID.
Prüfe am Ende, dass die Datei gültiges JSON ist.
```
