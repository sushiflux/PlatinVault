# 100%-Guide mit Claude Code erstellen (Claude-Abo)

> **Einfacher geht es in der App:** Auf der Spielseite ist unter „Guide vervollständigen“ der Weg **„Mit deinem KI-Abo (ohne API-Key)“**.
> PlatinVault erzeugt dort die Prompts (mit Steam-AppID, deinen Recherche-Bausteinen und der Guide-Sprache), prüft die Antworten deines Chats
> beim Einfügen und speichert die Kapitel – ohne Datei und ohne Umweg. Dieses Dokument ist der Weg für **Claude Code** (ein ganzer Guide als
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
1. Analyse: Recherchiere im Web (Wikis, Fandom, PowerPyx, IGN, Reddit, Steam-Community-Guides, offizielle Patch-Notes),
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
   Die Komplettlösung ist der Spielverlauf Schritt für Schritt in der Reihenfolge des Spiels, gegliedert nach Story-Kapiteln bzw.
   Gebieten ("heading" + "story" mit "variant": "steps"), und enthält genau an der Stelle, an der sie wichtig werden, typisierte
   Hinweise ("alert" mit "kind"): "must_have" (Wichtig – unbedingt mitnehmen, mit Fundort), "missable" (Verpassbar ab hier),
   "optional" (Nebenquest/Optionales hier verfügbar), "tip" (Tipp). Schreibe in eigenen Worten, übernimm keine längeren Passagen
   wörtlich, und nenne die Quellen. Ein Item, das in einem anderen Kapitel eine eigene Zeile hat: „siehe Kapitel …“ statt Wiederholung.
5. Optional (nur wenn ich es will, Spoiler!): ein zusätzlicher Tab mit "slug": "story-lore", "title": "Story & Lore",
   "sortOrder": 900 – Story einfach erklärt (Akt für Akt, mit Ende), Charaktere mit Hintergrund und Beziehungen,
   Welt & Begriffe, Insiderwissen & Entwicklungsgeschichte. Alle Abschnitte dort mit "spoiler": true.

Regeln:
- Plattform (verbindlich): Ich spiele die STEAM-Version auf dem PC. Erfolge sind Steam-Erfolge – nie PlayStation-Trophäen, Platin-Trophäe,
  Xbox-Erfolge oder Gamerscore; „100 %“ heißt alle Steam-Erfolge. Steuerung ist Tastatur und Maus (oder ein Controller am PC), keine
  Konsolen-Tastennamen, außer die PC-Version zeigt sie selbst. Reine Konsolen-Inhalte (Konsolen-Exklusives, Konsolen-Patchstände,
  PSN-/Xbox-exklusive DLCs, Cross-Save) weglassen; unterscheidet sich die PC-Version, gilt sie. Konsolen-Quellen (PSNProfiles,
  PlayStation-Trophäen-Guides, TrueAchievements …) nur für Fakten, die am PC genauso gelten, und dann nicht als „PS4-/PlayStation-Guide“
  nennen. Zeitangaben gelten für alle Steam-Erfolge am PC.
- Begriffe zweisprachig: deutscher Name mit englischem Originalbegriff in Klammern.
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
        { "type": "heading", "title": "Kapitel 1 – Gebiet", "htmlContent": "optional: eine Zeile, z. B. empfohlenes Level" },
        { "type": "story", "variant": "steps", "paragraphs": ["Schritt 1", "Schritt 2"] },
        { "type": "alert", "kind": "must_have", "title": "...", "htmlContent": "Item mit genauem Fundort" },
        { "type": "puzzle", "title": "Name des Rätsels", "htmlContent": "Die Aufgabe in 1–2 Sätzen, ohne Lösung",
          "puzzle": { "itemId": "eindeutige-id", "location": "Wo genau", "requirements": "Was vorher / ab wann", "missable": false,
                      "hints": ["vager Hinweis", "konkreterer Hinweis"], "steps": ["Schritt 1", "Schritt 2"], "source": "Name der Quelle" } },
        { "type": "cards_grid", "columns": 2, "cards": [
          { "title": "...", "badgeText": "...", "badgeVariant": "s", "introText": "...",
            "bullets": [ { "icon": "mdi:check", "title": "...", "body": "..." } ] } ] },
        { "type": "checklist_table", "table": {
          "tableId": "kurze-id", "caption": "...", "checkable": true,
          "searchPlaceholder": "Suchen...", "categoryFilterLabel": "Alle Kategorien", "categories": ["..."],
          "columns": [ { "key": "name", "label": "Name (DE / EN)", "isRowHeader": true },
                       { "key": "ort", "label": "Fundort" } ],
          "rows": [ { "itemId": "eindeutige-id", "filterCategory": "...", "cells": { "name": "...", "ort": "..." } } ] } }
      ]
    }
  ]
}
Jeder Abschnitt kann "spoiler": true bekommen (wird dann erst auf Klick gezeigt); "story" ist Fließtext in Absätzen.
Varianten für alert: danger (Verpassbares), gold (Profi-Tipps), crystal (Infos), emerald (Erfolge). Ein alert mit "kind"
(must_have, missable, optional, tip) bekommt Symbol und Beschriftung der Hinweisart; ohne "variant" gilt gold, danger, emerald, crystal in dieser Reihenfolge.
Ältere Dateien ohne "puzzle", "heading", "kind" und "variant": "steps" bleiben gültig und werden unverändert gelesen.
badgeVariant: s, a, b oder ok. Cover und Banner leer lassen – PlatinVault holt sie über die Steam-AppID.
Prüfe am Ende, dass die Datei gültiges JSON ist.
```
