using Companion.Api.Localization;

namespace Companion.Api.Services;

public sealed partial class AiGuideGeneratorService
{
    // Rule blocks and JSON patterns shared by the content prompts (real AI path and the manual/batch prompts): acquisition paths, the boss card, the
    // sources section and the structure of the walkthrough (print-guide layout with overview, areas, boss cards, typed hints). The JSON field names are
    // the contract with the app (GuideSectionNormalizer, GuideSectionDto) and never change between the languages.

    /// <summary>The mandatory rule for acquisition paths (anything that needs more than picking it up): the complete chain incl. every required fight.</summary>
    internal static string AcquisitionRules(string language) => language == Lang.English
        ? """
          Acquisition paths (mandatory rule): for everything that needs more than picking it up – weapons, legendary/ultimate gear, summons/espers,
          special items, rewards of quest chains – describe the COMPLETE chain in the right order: the prerequisites and from when it is available,
          what has to be collected, where to go (e.g. "the hall where you choose the weapon") and EVERY required fight – including a surprising, hard
          fight at the end (warn about it BEFORE the fight and give it a "boss" card). No step may be missing, otherwise the player walks into a fight he
          does not know about. One step per entry ("story" with "variant": "steps", or "puzzle.steps" for a riddle), the reward at the end.
          """
        : """
          Beschaffungswege (verbindliche Regel): Für alles, was man nicht einfach aufhebt – Waffen, legendäre/ultimative Ausrüstung, Beschwörungen/Esper,
          besondere Gegenstände, Belohnungen von Questreihen –, beschreibe die VOLLSTÄNDIGE Kette in der richtigen Reihenfolge: die Voraussetzungen und ab
          wann es verfügbar ist, was gesammelt werden muss, wohin man geht (z. B. „die Halle, in der man die Waffe auswählt“) und JEDEN nötigen Kampf – auch
          einen überraschenden, schweren Kampf am Ende (warne VOR dem Kampf und gib ihm eine "boss"-Karte). Kein Schritt darf fehlen, sonst steht der Spieler
          unvorbereitet vor einem Kampf, den er nicht kennt. Ein Schritt pro Eintrag ("story" mit "variant": "steps" oder bei einem Rätsel "puzzle.steps"), die Belohnung am Ende.
          """;

    /// <summary>The acquisition rule in one line, for the achievement tips.</summary>
    internal static string AcquisitionTipRule(string language) => language == Lang.English
        ? "If an achievement needs an acquisition chain (quest chain, collecting, a special place, fights), the tip names the chain briefly in the right order – including every required fight."
        : "Braucht ein Erfolg eine Beschaffungskette (Questreihe, Sammeln, ein besonderer Ort, Kämpfe), nennt der Tipp die Kette kurz in der richtigen Reihenfolge – inklusive jedes nötigen Kampfes.";

    /// <summary>The mandatory rule for boss cards.</summary>
    internal static string BossRules(string language) => language == Lang.English
        ? """
          Bosses (mandatory rule):
          - Every boss fight and every especially hard mandatory fight gets its own "boss" card – in the walkthrough directly after the step that leads to the
            fight; in other chapters (bestiary, bosses, acquisition paths) where the fight belongs.
          - Numbers only with evidence: every HP entry names the value, what it applies to ("appliesTo": difficulty, phase, version, NG+) and its source
            ("source"). Without a verified value write { "value": "Not documented" } – never invent numbers. Leave out every field you have nothing verified for.
          - "tactics": the tactic as steps in whole sentences; "achievements": only exact Steam API names from the achievement list (if there is one), otherwise leave it out.
          - "missable": true if the fight (or its reward) can be lost. Announce a surprise fight (no warning in the game) with an "alert" BEFORE it.
          """
        : """
          Bosse (verbindliche Regel):
          - Jeder Bosskampf und jeder besonders schwere Pflichtkampf bekommt eine eigene "boss"-Karte – in der Komplettlösung direkt nach dem Schritt, der zum
            Kampf führt; in anderen Kapiteln (Bestiarium, Bosse, Beschaffungswege) dort, wo der Kampf hingehört.
          - Zahlen nur mit Beleg: Jeder HP-Eintrag nennt den Wert, wofür er gilt ("appliesTo": Schwierigkeit, Phase, Version, NG+) und die Quelle ("source").
            Ohne belegten Wert schreibe { "value": "Nicht dokumentiert" } – erfinde nie Zahlen. Lass jedes Feld weg, zu dem du nichts Belegtes weißt.
          - "tactics": die Taktik als Schritte in ganzen Sätzen; "achievements": nur exakte Steam-API-Namen aus der Erfolgsliste (falls es eine gibt), sonst weglassen.
          - "missable": true, wenn der Kampf (oder seine Belohnung) verloren gehen kann. Kündige einen Überraschungskampf (ohne Vorwarnung im Spiel) VORHER mit einem "alert" an.
          """;

    /// <summary>The "boss" building block as a line for the list of allowed blocks, with its JSON pattern.</summary>
    internal static string BossBlock(string language) => language == Lang.English
        ? """
          - "boss": a boss card exactly where the fight happens (text only) – { "type": "boss", "bossId": "unique-kebab-id", "title": "Name of the boss",
              "location": "Where the fight takes place", "recommendedLevel": "e.g. level 25", "hp": [ { "value": "e.g. 12,000", "appliesTo": "difficulty/phase/version",
              "source": "Name of the source" } ], "weaknesses": ["..."], "resistances": ["..."], "immunities": ["..."],
              "attacks": [ { "name": "...", "note": "what it does, how to avoid it" } ], "phases": ["..."], "party": "recommended party/gear",
              "tactics": ["Step 1 ...", "Step 2 ..."], "rewards": ["..."], "missable": false, "achievements": ["API_NAME"], "source": "Name of the source" }
          """
        : """
          - "boss": Boss-Karte genau an der Stelle des Kampfes (nur Text) – { "type": "boss", "bossId": "eindeutige-kebab-id", "title": "Name des Bosses",
              "location": "Wo der Kampf stattfindet", "recommendedLevel": "z. B. Stufe 25", "hp": [ { "value": "z. B. 12.000", "appliesTo": "Schwierigkeit/Phase/Version",
              "source": "Name der Quelle" } ], "weaknesses": ["..."], "resistances": ["..."], "immunities": ["..."],
              "attacks": [ { "name": "...", "note": "was sie tut, wie man ausweicht" } ], "phases": ["..."], "party": "empfohlene Gruppe/Ausrüstung",
              "tactics": ["Schritt 1 ...", "Schritt 2 ..."], "rewards": ["..."], "missable": false, "achievements": ["API_NAME"], "source": "Name der Quelle" }
          """;

    /// <summary>The "sources" building block: the pages the chapter really relied on, as its last section.</summary>
    internal static string SourcesBlock(string language) => language == Lang.English
        ? """
          - "sources": as the LAST section of the chapter the pages you really relied on – { "type": "sources", "sources": [ { "title": "Name of the page", "url": "https://..." } ] }.
            Only real, complete URLs you have actually seen; never invent a URL – without a URL leave the entry out.
          """
        : """
          - "sources": als LETZTER Abschnitt des Kapitels die Seiten, auf die du dich wirklich gestützt hast – { "type": "sources", "sources": [ { "title": "Name der Seite", "url": "https://..." } ] }.
            Nur echte, vollständige URLs, die du tatsächlich gesehen hast; erfinde nie eine URL – ohne URL lass den Eintrag weg.
          """;

    /// <summary>The "overview" building block ("at a glance" at the start of a walkthrough part).</summary>
    private static string OverviewBlock(string language) => language == Lang.English
        ? """
          - "overview": "At a glance" at the start of a part – { "type": "overview", "title": "At a glance", "recommendedLevel": "e.g. 10–15",
              "counts": [ { "label": "Treasure chests", "count": 12 } ], "achievements": ["API_NAME"], "keyItems": ["..."], "missables": ["..."],
              "endsAtPointOfNoReturn": false } ("count" only with a verified number, otherwise leave it out)
          """
        : """
          - "overview": „Auf einen Blick“ am Anfang eines Teils – { "type": "overview", "title": "Auf einen Blick", "recommendedLevel": "z. B. 10–15",
              "counts": [ { "label": "Schatztruhen", "count": 12 } ], "achievements": ["API_NAME"], "keyItems": ["..."], "missables": ["..."],
              "endsAtPointOfNoReturn": false } ("count" nur mit belegter Zahl, sonst weglassen)
          """;

    /// <summary>
    /// The structure of every walkthrough part, like an official print strategy guide (the structure only - never its text): part heading, "at a glance",
    /// then one block per area in the order you pass through, and the point of no return at the end.
    /// </summary>
    internal static string WalkthroughStructure(string language) => language == Lang.English
        ? """
          Structure of EVERY part (like an official printed strategy guide – only the structure, never its text):
          1. "heading" with "level": 1 = the part (story chapter/mission); in "htmlContent" ONE short orientation sentence: where you are and what this part is about.
          2. "overview" "At a glance": recommended level, how many collectibles/chests the part has ("counts"), the achievements of this part (API names),
             key items ("keyItems"), what is missable ("missables") and whether the part ends at a point of no return ("endsAtPointOfNoReturn").
          3. Then one block per AREA/LOCATION in the order you pass through:
             a. "heading" with "level": 2 = the name of the area; in "htmlContent" one orientation sentence for this area.
             b. The route: "story" with "variant": "steps".
             c. What can be found in this area – only if it has such things: a "checklist_table" with the chests/collectibles/items of this area
                (tickable, unique "itemId", a column "Missable").
             d. Side quests that become available here: ONE compact "alert" with "kind": "side_quests" (see the side quest rule) – other optional content
                (optional boss, extra area): "alert" with "kind": "optional".
             e. A boss fight: a "boss" card directly after the step that leads to the fight.
             f. An achievement that unlocks here: "alert" with "kind": "achievement" and "apiName".
          4. At the end of the part – before a point of no return – an "alert" with "kind": "point_of_no_return" and "items" (what you have to do before you continue).
          """
        : """
          Aufbau JEDES Teils (wie ein offizielles gedrucktes Lösungsbuch – nur der Aufbau, nie dessen Text):
          1. "heading" mit "level": 1 = der Teil (Story-Kapitel/Mission); in "htmlContent" EIN kurzer Orientierungssatz: wo du bist und worum es in diesem Teil geht.
          2. "overview" „Auf einen Blick“: empfohlenes Level, wie viele Sammelobjekte/Truhen der Teil hat ("counts"), die Erfolge dieses Teils (API-Namen),
             wichtige Gegenstände ("keyItems"), was verpassbar ist ("missables") und ob der Teil an einem Point of no Return endet ("endsAtPointOfNoReturn").
          3. Dann je GEBIET/ORT in der Reihenfolge, in der man durchkommt:
             a. "heading" mit "level": 2 = Name des Gebiets; in "htmlContent" ein Orientierungssatz für dieses Gebiet.
             b. Die Route: "story" mit "variant": "steps".
             c. Was es in diesem Gebiet zu finden gibt – nur wenn es dort so etwas gibt: eine "checklist_table" mit den Truhen/Sammelobjekten/Gegenständen dieses
                Gebiets (abhakbar, eindeutige "itemId", eine Spalte „Verpassbar“).
             d. Nebenquests, die hier verfügbar werden: EIN kompakter "alert" mit "kind": "side_quests" (siehe Nebenquest-Regel) – andere optionale Inhalte
                (optionaler Boss, Zusatzgebiet): "alert" mit "kind": "optional".
             e. Ein Bosskampf: eine "boss"-Karte direkt nach dem Schritt, der zum Kampf führt.
             f. Ein Erfolg, der hier freigeschaltet wird: "alert" mit "kind": "achievement" und "apiName".
          4. Am Ende des Teils – vor einem Point of no Return – ein "alert" mit "kind": "point_of_no_return" und "items" (was du vorher erledigen musst).
          """;

    /// <summary>
    /// The side quest rule: side quests never break the reading flow of the walkthrough - there they are only a compact callout where they become
    /// available; their complete solution belongs in the side quest chapter (topic "quests"). Part of ResearchRules, so every content prompt has it.
    /// </summary>
    internal static string SideQuestRule(string language) => language == Lang.English
        ? """
          - Side quests: their complete solution (steps, fights, rewards) belongs ONLY in the side quest chapter. In the walkthrough a side quest is only
            named in a compact "alert" with "kind": "side_quests" at the spot where it becomes available – { "type": "alert", "kind": "side_quests", "title": "Side quests here",
            "quests": [ { "title": "Name", "availableFrom": "from when", "missable": "time window / until when (only if missable)", "reward": "short" } ] } – nothing more.
            In other chapters name the quest briefly and refer to the side quest chapter.
          """
        : """
          - Nebenquests: Ihre vollständige Lösung (Schritte, Kämpfe, Belohnungen) steht NUR im Kapitel zu den Nebenquests. In der Komplettlösung wird eine Nebenquest
            nur in einem kompakten "alert" mit "kind": "side_quests" an der Stelle genannt, an der sie verfügbar wird – { "type": "alert", "kind": "side_quests", "title": "Nebenquests hier",
            "quests": [ { "title": "Name", "availableFrom": "ab wann", "missable": "Zeitfenster / bis wann (nur wenn verpassbar)", "reward": "kurz" } ] } – mehr nicht.
            In anderen Kapiteln nenne die Quest kurz und verweise auf das Nebenquest-Kapitel.
          """;

    /// <summary>The reading-flow rule of the walkthrough steps.</summary>
    internal static string WalkthroughReadingRules(string language) => language == Lang.English
        ? """
          Reading flow (mandatory rule):
          - Every step is 1–2 complete sentences addressing the player as "you", e.g. "Go to the village of Kol and spend the night at the inn." – no telegram
            style, no "Objective:", no objective lists copied from the game or from sources.
          - Merge trivial objectives (walking, a conversation, a cutscene) into one step. No numbering of your own.
          - Typed hints (missable, point of no return, achievement, optional, important, tip) are their own "alert" exactly where they matter – never inside the step text.
          """
        : """
          Lesefluss (verbindliche Regel):
          - Jeder Schritt sind 1–2 vollständige Sätze in der Du-Form, z. B. „Geh ins Dorf Kol und übernachte im Gasthaus.“ – kein Telegrammstil, kein „Ziel:“,
            keine aus dem Spiel oder aus Quellen kopierten Ziel-Listen.
          - Fasse triviale Ziele (Weg, Gespräch, Zwischensequenz) zu einem Schritt zusammen. Keine eigene Nummerierung.
          - Typisierte Hinweise (verpassbar, Point of no Return, Erfolg, optional, wichtig, Tipp) stehen als eigener "alert" genau dort, wo sie wichtig werden – nie im Schritt-Text.
          """;

    /// <summary>
    /// The official achievements for the walkthrough prompts (API name | title | condition, the condition shortened), so "apiName"/"achievements" can name
    /// exact API names; without a list the prompt says to leave those fields out.
    /// </summary>
    internal static string AchievementReference(GameContext context, int max = 150)
    {
        var english = context.Language == Lang.English;
        if (context.Achievements.Count == 0)
        {
            return english
                ? "There is no official achievement list for this guide: leave \"apiName\" and \"achievements\" out."
                : "Für diesen Guide liegt keine offizielle Erfolgsliste vor: lass \"apiName\" und \"achievements\" weg.";
        }
        static string Short(string text) => text.Length <= 80 ? text : text[..77].TrimEnd() + "...";
        var lines = context.Achievements.Take(max).Select(a => $"{a.ApiName} | {a.DisplayName} | {Short(a.Description)}");
        var more = context.Achievements.Count > max
            ? english ? $"\n(+{context.Achievements.Count - max} more)" : $"\n(+{context.Achievements.Count - max} weitere)"
            : string.Empty;
        return (english
            ? "The official Steam achievements (API name | title | condition) – use ONLY these names for \"apiName\" and \"achievements\":\n"
            : "Die offiziellen Steam-Erfolge (API-Name | Titel | Bedingung) – nutze für \"apiName\" und \"achievements\" NUR diese Namen:\n")
            + string.Join("\n", lines) + more;
    }

    /// <summary>
    /// The research assignment of "Kapitel neu recherchieren" for a chapter that has no stored assignment (a normal chapter): the title and what the
    /// current version covers, so the new research covers at least the same.
    /// </summary>
    internal static string RedoFocus(string title, IReadOnlyList<string> sectionTitles, string language)
    {
        var english = language == Lang.English;
        var covered = sectionTitles.Count > 0 ? string.Join("; ", sectionTitles.Take(20)) : english ? "(no headings)" : "(keine Überschriften)";
        return english
            ? $"Research the chapter \"{title}\" again, completely and thoroughly. The current version has these sections: {covered}. Cover at least the same topics, correct mistakes and add what is missing."
            : $"Recherchiere das Kapitel „{title}“ noch einmal komplett und gründlich. Die bisherige Fassung hat diese Abschnitte: {covered}. Decke mindestens dieselben Themen ab, korrigiere Fehler und ergänze, was fehlt.";
    }
}
