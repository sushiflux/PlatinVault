using Companion.Api.Localization;

namespace Companion.Api.Services;

public sealed partial class AiGuideGeneratorService
{
    // The "puzzle" section: one rule block and one JSON pattern shared by every prompt that produces guide content (chapter prompts, the walkthrough
    // parts, the manual subscription flow, docs/guide-prompt.md). The wording of the rule is a contract with the player: a puzzle is never a vague
    // sentence, it is WHERE + the COMPLETE solution, or an honest "not documented". Lore is deliberately excluded (it contains no tasks).

    /// <summary>The mandatory rule for puzzles, riddles, sequences, combinations and hidden mechanisms, in the language of the guide.</summary>
    internal static string PuzzleRules(string language) => language == Lang.English
        ? """
          Puzzles and riddles (mandatory rule):
          - EVERY puzzle, riddle, code, sequence, combination, hidden mechanism or task of the kind "do X in the right order" gets its own "puzzle"
            section – never just a vague sentence in a table cell, an alert or a card. Leave such a table cell short and name the puzzle there.
          - WHERE exactly: "puzzle.location" (area, landmark, how to get there), "puzzle.requirements" (what has to be done or owned first, when it
            becomes available) and "puzzle.missable" (true if it can be lost). The visible task goes into "htmlContent" (one or two sentences, NO solution in it).
          - The COMPLETE SOLUTION in "puzzle.steps": every step in the exact order, with the concrete answers, codes, symbols, positions, items and
            places – a player must be able to solve it with the list alone. Forbidden are sentences such as "solve the riddle" or "collect the 3
            fragments in the right order" without saying where each one is and which order is the right one.
          - Optional "puzzle.hints": 1 to 3 hints from vague to concrete (no complete solution), so a player can ask for help bit by bit.
          - "puzzle.source": where the solution comes from (site or guide name).
          - If your research cannot verify the solution: leave "puzzle.steps" EMPTY (the app then shows "Solution not documented") and still give
            location and requirements as far as they are verified. NEVER invent codes, positions or orders.
          - "puzzle.itemId" (kebab-case, unique) when the player can tick the puzzle off as done.
          """
        : """
          Rätsel (verbindliche Regel):
          - JEDES Rätsel, jede Code-Eingabe, Reihenfolge, Kombination, jeder versteckte Mechanismus und jede Aufgabe der Art „mach X in der richtigen
            Reihenfolge“ bekommt einen eigenen "puzzle"-Abschnitt – nie nur einen vagen Satz in einer Tabellenzelle, einem Hinweis oder einer Karte.
            Die Tabellenzelle dazu bleibt kurz und nennt das Rätsel nur.
          - WO genau: "puzzle.location" (Gebiet, Landmarke, Weg dorthin), "puzzle.requirements" (was vorher erledigt sein oder vorhanden sein muss,
            ab wann es verfügbar ist) und "puzzle.missable" (true, wenn es verloren gehen kann). Die sichtbare Aufgabe steht in "htmlContent"
            (ein bis zwei Sätze, KEINE Lösung darin).
          - Die VOLLSTÄNDIGE LÖSUNG in "puzzle.steps": jeder Schritt in der genauen Reihenfolge, mit den konkreten Antworten, Codes, Symbolen,
            Positionen, Gegenständen und Orten – ein Spieler muss das Rätsel allein mit dieser Liste lösen können. Verboten sind Sätze wie
            „löse das Rätsel“ oder „sammle die 3 Fragmente in der richtigen Reihenfolge ein“, ohne zu sagen, wo jedes liegt und welche Reihenfolge die richtige ist.
          - Optional "puzzle.hints": 1 bis 3 Hinweise von vage bis konkret (keine vollständige Lösung), damit ein Spieler sich Stück für Stück helfen lassen kann.
          - "puzzle.source": woher die Lösung stammt (Name der Seite oder des Guides).
          - Kann deine Recherche die Lösung nicht belegen: lass "puzzle.steps" LEER (die App zeigt dann „Lösung nicht dokumentiert“) und nenne Ort
            und Voraussetzungen, soweit belegt. Erfinde NIEMALS Codes, Positionen oder Reihenfolgen.
          - "puzzle.itemId" (kebab-case, eindeutig), wenn der Spieler das Rätsel als erledigt abhaken können soll.
          """;

    /// <summary>The "puzzle" building block as a line for the list of allowed blocks, with its JSON pattern.</summary>
    internal static string PuzzleBlock(string language) => language == Lang.English
        ? """
          - "puzzle": a riddle/sequence with a hidden solution – { "type": "puzzle", "title": "Name of the puzzle", "htmlContent": "The task in 1-2 sentences, no solution",
              "puzzle": { "itemId": "unique-id", "location": "Where exactly", "requirements": "What first / from when", "missable": false,
                "hints": ["vague hint", "more concrete hint"], "steps": ["Step 1 ...", "Step 2 ..."], "source": "Name of the source" } }
          """
        : """
          - "puzzle": Rätsel/Reihenfolge mit versteckter Lösung – { "type": "puzzle", "title": "Name des Rätsels", "htmlContent": "Die Aufgabe in 1-2 Sätzen, ohne Lösung",
              "puzzle": { "itemId": "eindeutige-id", "location": "Wo genau", "requirements": "Was vorher / ab wann", "missable": false,
                "hints": ["vager Hinweis", "konkreterer Hinweis"], "steps": ["Schritt 1 ...", "Schritt 2 ..."], "source": "Name der Quelle" } }
          """;

    /// <summary>One line for the achievement-tip prompt: the same rule in short form (an achievement that is a puzzle needs location and solution in its tip).</summary>
    internal static string PuzzleTipRule(string language) => language == Lang.English
        ? "If an achievement needs a puzzle, riddle, code or an order to be solved, the tip must state WHERE it is and the complete solution (every step, exact order, codes) – or say \"Solution not documented\"; never a vague sentence and never invented."
        : "Braucht ein Erfolg ein Rätsel, einen Code oder eine Reihenfolge, muss der Tipp sagen, WO es ist, und die vollständige Lösung nennen (jeder Schritt, genaue Reihenfolge, Codes) – oder „Lösung nicht dokumentiert“ sagen; nie ein vager Satz und nie erfunden.";
}
