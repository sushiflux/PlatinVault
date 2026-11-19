namespace Companion.Api.Localization;

/// <summary>
/// Texts of the researched trophy list of a game added by hand (part of <see cref="ServerTexts"/>, kept in its own file): the checks of an answer (AI
/// research and the answer pasted from the player's own AI subscription use the same reader, <c>Services/TrophyList.cs</c>) and the errors of the
/// research endpoints. Same rules as there: parameterised text = method, fixed text = field, English line directly behind the German one.
/// "Trophäen" covers PlayStation trophies and the achievements of every other platform; the sentences say "Trophäen/Erfolge" where both are meant.
/// </summary>
public static partial class ServerTexts
{
    public static class Trophies
    {
        public static readonly Msg NotManualTitle = M("Nur für selbst hinzugefügte Spiele", "Only for games added by hand");

        public static readonly Msg NotManual = M(
            "Dieses Spiel kommt aus deiner Steam-Bibliothek: seine Erfolge liest PlatinVault direkt von Steam, eine Liste muss nicht recherchiert werden.",
            "This game comes from your Steam library: PlatinVault reads its achievements straight from Steam, there is no list to research.");

        public static readonly Msg NothingToResearchTitle = M("Nichts zu recherchieren", "Nothing to research");

        /// <summary>Neither the list nor the facts can be researched (Nintendo Switch with complete facts, or facts that came from IGDB).</summary>
        public static Msg NothingToResearch(string platformName, bool hasTrophies, bool factsFromIgdb) => (hasTrophies, factsFromIgdb) switch
        {
            (false, true) => M(
                $"{platformName} hat keine Trophäen oder Erfolge, und die Spiel-Fakten kommen schon aus IGDB – es gibt nichts zu recherchieren.",
                $"{platformName} has no trophies or achievements, and the game facts come from IGDB already – there is nothing to research."),
            (false, false) => M(
                $"{platformName} hat keine Trophäen oder Erfolge, und die Spiel-Fakten sind schon vollständig. Wähle „Spiel-Fakten recherchieren“, um sie neu zu holen.",
                $"{platformName} has no trophies or achievements, and the game facts are complete already. Choose “Research game facts” to fetch them again."),
            _ => M(
                "Es wurde weder die Trophäenliste noch die Spiel-Fakten zum Recherchieren ausgewählt.",
                "Neither the trophy list nor the game facts were chosen for the research."),
        };

        public static readonly Msg NotUsableTitle = M("Trophäenliste nicht übernommen", "Trophy list not saved");

        /// <summary>The answer could not be used; <paramref name="reasons"/> = the complete reasons of the reader, joined.</summary>
        public static Msg NotUsable(string reasons) => M(
            $"Die Antwort der KI konnte nicht übernommen werden, es wurde nichts geändert: {reasons}",
            $"The AI answer could not be used, nothing was changed: {reasons}");

        /// <summary>The research found no reliable, complete list (documented:false or an empty list); nothing is saved.</summary>
        public static Msg NoReliableList(string? reason) => string.IsNullOrWhiteSpace(reason)
            ? M(
                "Die Recherche hat keine verlässliche, vollständige Trophäen-/Erfolgsliste für diese Version gefunden. Es wurde nichts gespeichert – lieber keine Liste als eine falsche. Versuche es später noch einmal oder mit einer anderen KI.",
                "The research found no reliable, complete trophy/achievement list for this version. Nothing was saved – better no list than a wrong one. Try again later or with another AI.")
            : M(
                $"Die Recherche hat keine verlässliche, vollständige Trophäen-/Erfolgsliste für diese Version gefunden: {reason.Trim()} Es wurde nichts gespeichert – lieber keine Liste als eine falsche.",
                $"The research found no reliable, complete trophy/achievement list for this version: {reason.Trim()} Nothing was saved – better no list than a wrong one.");

        public static readonly Msg NoTrophyArray = M(
            "In der Antwort fehlt das Feld „trophies“ (die Liste der Trophäen/Erfolge).",
            "The answer has no field “trophies” (the list of trophies/achievements).");

        public static readonly Msg TrophiesNotList = M(
            "„trophies“ muss eine Liste sein: [ { \"id\": …, \"name\": … }, … ].",
            "“trophies” must be a list: [ { \"id\": …, \"name\": … }, … ].");

        public static Msg TooMany(int count, int max) => M(
            $"Die Antwort enthält {count} Einträge – mehr als {max} sind keine echte Trophäenliste. Es wurde nichts übernommen.",
            $"The answer has {count} entries – more than {max} cannot be a real trophy list. Nothing was taken over.");

        public static Msg ManyTrophies(int count) => M(
            $"Die Liste hat {count} Einträge – ungewöhnlich viele. Prüfe, ob DLCs oder eine andere Version mit hineingeraten sind.",
            $"The list has {count} entries – unusually many. Check whether DLCs or another version slipped in.");

        public static Msg WithoutName(int count) => M(
            count == 1 ? "Ein Eintrag hat keinen Namen und wurde ausgelassen." : $"{count} Einträge haben keinen Namen und wurden ausgelassen.",
            count == 1 ? "One entry has no name and was left out." : $"{count} entries have no name and were left out.");

        public static readonly Msg NoUsableEntries = M(
            "Kein Eintrag der Liste hat einen Namen – die Liste ist unbrauchbar.",
            "No entry of the list has a name – the list cannot be used.");

        /// <summary>PlayStation: entries without one of the four grades (error: the tier of a trophy is its grade).</summary>
        public static Msg MissingGrade(int count, string examples) => M(
            $"{count} Trophäe(n) ohne gültige Stufe („grade“ muss „platinum“, „gold“, „silver“ oder „bronze“ sein): {examples}",
            $"{count} trophy/trophies without a valid grade (“grade” must be “platinum”, “gold”, “silver” or “bronze”): {examples}");

        /// <summary>PlayStation: more than one platinum trophy (own subscription: error; AI research: only the first is kept).</summary>
        public static Msg SeveralPlatinum(string names) => M(
            $"Ein PlayStation-Spiel hat höchstens EINE Platin-Trophäe, die Antwort nennt mehrere: {names}. Prüfe die Liste (DLC oder eine andere Version?) und korrigiere die Stufen.",
            $"A PlayStation game has at most ONE platinum trophy, the answer names several: {names}. Check the list (DLC or another version?) and correct the grades.");

        public static Msg ExtraPlatinumDropped(string names) => M(
            $"Die Antwort nannte mehrere Platin-Trophäen; übernommen wurde nur die erste, ausgelassen: {names}.",
            $"The answer named several platinum trophies; only the first was taken over, left out: {names}.");

        public static readonly Msg NoPlatinum = M(
            "Die Liste hat keine Platin-Trophäe. Manche Spiele haben keine – sonst fehlt sie in der Antwort.",
            "The list has no platinum trophy. Some games have none – otherwise it is missing from the answer.");

        public static Msg PointsDropped(int count) => M(
            $"Bei {count} Eintrag/Einträgen lag „points“ nicht zwischen 0 und 1000 und wurde weggelassen.",
            $"For {count} entry/entries “points” was not between 0 and 1000 and was left out.");

        public static Msg RarityDropped(int count) => M(
            $"Bei {count} Eintrag/Einträgen lag „rarityPercent“ nicht zwischen 0 und 100 und wurde weggelassen.",
            $"For {count} entry/entries “rarityPercent” was not between 0 and 100 and was left out.");

        public static Msg DuplicateIds(int count) => M(
            $"{count} ID(s) kamen doppelt vor oder fehlten und wurden eindeutig gemacht.",
            $"{count} id(s) were missing or appeared twice and were made unique.");

        public static Msg TextsShortened(int count) => M(
            $"{count} zu lange Texte (Name, Bedingung, Tipp, Gruppe oder Kategorie) wurden gekürzt.",
            $"{count} texts that were too long (name, condition, tip, group or category) were shortened.");

        public static readonly Msg NoSources = M(
            "Die Antwort nennt keine Quellen. Prüfe die Liste besonders sorgfältig.",
            "The answer names no sources. Check the list especially carefully.");

        public static Msg OtherPlatform(string found, string expected) => M(
            $"Die Antwort nennt die Plattform „{found}“, erwartet ist „{expected}“ – prüfe, ob es die Liste der richtigen Version ist.",
            $"The answer names the platform “{found}”, expected is “{expected}” – check that it is the list of the right version.");

        public static readonly Msg FactsMissing = M(
            "Die Spiel-Fakten („facts“) fehlen in der Antwort; es wurden keine Fakten übernommen.",
            "The game facts (“facts”) are missing from the answer; no facts were taken over.");

        public static Msg FactsValueDropped(string field) => M(
            $"Der Wert von „facts.{field}“ war nicht plausibel und wurde weggelassen.",
            $"The value of “facts.{field}” was not plausible and was left out.");

        public static readonly Msg FactsFromIgdbKept = M(
            "Die Spiel-Fakten kommen aus IGDB und werden nicht überschrieben; nur die Trophäenliste wird übernommen.",
            "The game facts come from IGDB and are not overwritten; only the trophy list is taken over.");

        public static readonly Msg NotSavedTitle = M("Nicht gespeichert", "Not saved");

        public static Msg NotSaved(string reasons) => M(
            $"Die Trophäenliste wurde nicht gespeichert: {reasons}",
            $"The trophy list was not saved: {reasons}");

        /// <summary>The label of the trophy list for the translation prompt (it is no guide chapter).</summary>
        public static string TranslationLabel(string language) => language == Lang.English ? "Trophy and achievement list" : "Trophäen- und Erfolgsliste";
    }
}
