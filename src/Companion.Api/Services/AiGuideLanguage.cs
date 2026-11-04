using Companion.Api.Localization;

namespace Companion.Api.Services;

public sealed partial class AiGuideGeneratorService
{
    // The language rule: a guide is written ENTIRELY in its own language. German guides used to contain English sentences (walkthrough objectives copied
    // from English sources such as "Ziel: Go to the town ..."), because the old rule asked for "German name with the English original in brackets" for every
    // game term. ONE place per language; it is part of ResearchRules and therefore of every prompt that writes guide content (blueprint, chapter,
    // walkthrough outline/parts/single, Story & Lore, achievement tips, the manual subscription prompts) - a test checks that.

    /// <summary>The mandatory rule "everything in the guide language, in own words; original names at most once per chapter", in the language of the guide.</summary>
    internal static string LanguageRules(string language) => language == Lang.English
        ? """
          Language (mandatory rule): the whole guide is in English – every sentence, heading, table cell, step, objective and instruction.
          - Write in your own words. Never copy sentences from sources in another language or from another language version of the game (also no
            objectives or quest texts) – rewrite them in English.
          - Names of places, items, enemies, characters, quests and abilities: as they are called in the English version of the game, if the game has one
            (see "English version according to Steam" in the facts; without that line check it yourself). If there is no English version, use the in-game name
            and explain it briefly in English at its first mention.
          - The original name in another language may be added in brackets at most ONCE per chapter, at its first mention – only for proper names, never for
            sentences, objectives or instructions, and never repeated. Chapter titles, headings and tab names get no bracketed addition.
          - Steam API names, itemIds, URLs, codes and numbers stay exactly as they are.
          """
        : """
          Sprache (verbindliche Regel): Der ganze Guide ist auf Deutsch – jeder Satz, jede Überschrift, jede Tabellenzelle, jeder Schritt, jedes Ziel und jede Anweisung.
          - Schreibe in eigenen Worten. Übernimm nie Sätze aus fremdsprachigen Quellen oder aus der fremdsprachigen Fassung des Spiels (auch keine Ziele oder
            Questtexte wie „Go to the town …“) – formuliere sie auf Deutsch neu.
          - Namen von Orten, Gegenständen, Gegnern, Figuren, Quests und Fähigkeiten: so, wie sie in der deutschen Fassung des Spiels heißen, wenn es eine gibt
            (siehe „Deutsche Fassung laut Steam“ in den Fakten; fehlt die Zeile, prüfe es selbst). Gibt es keine deutsche Fassung, nimm den Namen aus dem Spiel
            und erkläre ihn beim ersten Vorkommen kurz auf Deutsch.
          - Den Originalnamen (meist Englisch) schreibst du höchstens EINMAL pro Kapitel in Klammern dazu, beim ersten Vorkommen – nur bei Eigennamen, nie bei
            Sätzen, Zielen oder Anweisungen, und danach nicht wiederholen. Kapitel-Titel, Überschriften und Tab-Namen bekommen keinen Klammerzusatz.
          - Steam-API-Namen, itemIds, URLs, Codes und Zahlen bleiben genau, wie sie sind.
          """;

    /// <summary>
    /// Where to start researching, per guide language: German guides take German sources first and English ones as a complement (always rewritten in own
    /// German words), English guides the English ones. Hints only - the AI keeps searching on its own. The sites were checked as active on 2026-11-03.
    /// Console sites are allowed for what is the same on PC (see PlatformRules).
    /// </summary>
    internal static string SourceHints(string language) => GameWikiRule(language) + "\n" + (language == Lang.English
        ? """
          - Step 2 – research live on the internet. Good starting points (hints, keep searching on your own): Steam community guides (category Achievements),
            Game8 (walkthroughs), Fextralife (wikis), PowerPyx (roadmaps, collectibles), PSNProfiles and PlayStationTrophies.org (roadmaps, missables),
            official patch notes and Reddit.
          """
        : """
          - Schritt 2 – recherchiere live im Internet. Gute Startpunkte (Hinweise, suche selbst weiter): zuerst deutsche Quellen – pcgames.de (Komplettlösungen, PC-Fokus),
            spieletipps.de und GIGA (Komplettlösungen, Tipps), gameswelt.de (Tipps, Komplettlösungen, teils Steam-Erfolge), deutsche Steam-Community-Guides
            (Sprache Deutsch, Kategorie Erfolge), mein-mmo.de (nur MMO-/Online-Spiele); dann englische als Ergänzung – Game8 (Komplettlösungen), Fextralife (Wikis),
            PowerPyx (Roadmaps, Sammelobjekte), PSNProfiles und PlayStationTrophies.org (Roadmaps, Verpassbares). Englische Inhalte gibst du immer in eigenen
            deutschen Worten wieder (Sprachregel).
          """);

    /// <summary>
    /// Step 1 of every research (owner rule 2026-11-04): a wiki dedicated to THIS game first (fan-made or official), in the guide language when it has that
    /// version - with the guardrails that such wikis are often fan sites (cross-check numbers, own words, list the page in "sources", never invent a wiki URL,
    /// skip silently when there is none). The example domain is a verified fan wiki (see the sources of the project); it is named as an example of the kind of
    /// site only, so the model does not assume every game has one.
    /// </summary>
    internal static string GameWikiRule(string language) => language == Lang.English
        ? """
          - Step 1 – before all other sources: look for a wiki dedicated to THIS game, made by fans or official (for example the game's Fandom wiki, an official
            game wiki, or a fan wiki of the kind of ffresonance.wiki for Final Fantasy Resonance – many games have none). If it has an English version (often
            the default without a language path, otherwise a language switcher or a path like /en/), use that one. Name a wiki only when your search really
            found it; if there is none, skip this step without mentioning it. Never invent a wiki URL.
            Such wikis are often fan sites: cross-check numbers (HP, levels, drop rates, prices) with a second source where one exists and say so when they
            disagree; write in your own words and never copy their text; list every wiki page you used in the chapter's "sources" section (where the answer has one).
          """
        : """
          - Schritt 1 – vor allen anderen Quellen: Suche ein Wiki speziell zu DIESEM Spiel, von Fans oder offiziell (zum Beispiel das Fandom-Wiki des Spiels, ein
            offizielles Spiel-Wiki oder ein Fan-Wiki von der Art wie ffresonance.wiki für Final Fantasy Resonance – viele Spiele haben keins). Hat es eine deutsche
            Fassung (Sprachpfad wie /de/ oder Sprachumschalter), nimm diese. Nenne ein Wiki nur, wenn deine Suche es wirklich gefunden hat; gibt es keins,
            überspring diesen Schritt ohne Erwähnung. Erfinde nie eine Wiki-Adresse.
            Solche Wikis sind oft Fan-Seiten: Prüfe Zahlen (HP, Level, Drop-Raten, Preise) mit einer zweiten Quelle, wo es eine gibt, und sag es, wenn sie sich
            widersprechen; schreibe in eigenen Worten und übernimm nie ihren Text; führe jede benutzte Wiki-Seite im Abschnitt "sources" des Kapitels auf (wo die
            Antwort einen hat).
          """;

    /// <summary>
    /// The fact lines about the game's languages from the Steam store (appdetails "supported_languages", read in the guide language): the list, the
    /// languages with full audio and whether a version in the guide language exists. No lines when Steam named no languages (then the rule says
    /// "check it yourself").
    /// </summary>
    internal static List<string> LanguageFactLines(IReadOnlyList<string> languages, IReadOnlyList<string> audio, string language)
    {
        var lines = new List<string>();
        if (languages.Count == 0)
        {
            return lines;
        }
        var english = language == Lang.English;
        var audioText = audio.Count > 0
            ? english ? $" (full audio: {string.Join(", ", audio)})" : $" (mit Sprachausgabe: {string.Join(", ", audio)})"
            : string.Empty;
        lines.Add(english
            ? $"Languages according to Steam: {string.Join(", ", languages)}{audioText}"
            : $"Sprachen laut Steam: {string.Join(", ", languages)}{audioText}");

        // The list comes in the language of the request (l=german -> "Deutsch"); the other spelling is accepted as well
        string[] names = english ? ["English", "Englisch"] : ["Deutsch", "German"];
        var exists = languages.Any(l => names.Any(n => l.StartsWith(n, StringComparison.OrdinalIgnoreCase)));
        lines.Add((english, exists) switch
        {
            (true, true) => "English version according to Steam: yes – use the names of the English version of the game.",
            (true, false) => "English version according to Steam: no – use the in-game names and explain them briefly in English at their first mention.",
            (false, true) => "Deutsche Fassung laut Steam: ja – verwende die Namen der deutschen Fassung des Spiels.",
            _ => "Deutsche Fassung laut Steam: nein – nimm die Namen aus dem Spiel und erkläre sie beim ersten Vorkommen kurz auf Deutsch."
        });
        return lines;
    }
}
