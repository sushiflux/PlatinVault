using Companion.Api.Services;

namespace Companion.Api.Localization;

/// <summary>
/// Texts of the AI review of guide chapters ("Prüf-KI", part of <see cref="ServerTexts"/>, kept in its own file). Same rules as there: parameterised text =
/// method, fixed text = field, English directly behind the German line, a complete sentence for every refusal. Findings and the "unklar" box that are
/// written INTO a chapter are resolved in the guide language by the caller (<see cref="Msg.Resolve"/>).
/// </summary>
public static partial class ServerTexts
{
    public static class Review
    {
        private static string Name(string provider) => Ai.ProviderName(provider);

        // ---- what cannot be reviewed --------------------------------------------------------------------------------------------------

        public static readonly Msg NotReviewableTitle = M("Kapitel kann nicht geprüft werden", "Chapter cannot be reviewed");

        public static readonly Msg NotReviewableWalkthrough = M(
            "Die Komplettlösung lässt sich nicht prüfen: Sie besteht aus bis zu 20 Teilen und passt nicht in eine einzige Prüf-Antwort. Alle anderen Kapitel des Guides können geprüft werden.",
            "The walkthrough cannot be reviewed: it consists of up to 20 parts and does not fit into a single review answer. Every other chapter of the guide can be reviewed.");

        public static readonly Msg NotReviewableAchievementList = M(
            "Das ist die Steam-Erfolgsliste des Guides, kein Kapitel. Sie kommt von Steam und wird nicht geprüft.",
            "This is the guide's Steam achievement list, not a chapter. It comes from Steam and is not reviewed.");

        public static readonly Msg NotReviewableLore = M(
            "„Story & Lore“ ist ein eigener Bereich und kein Kapitel der Navigation. Er wird nicht geprüft.",
            "“Story & Lore” is an area of its own, not a chapter of the navigation. It is not reviewed.");

        public static readonly Msg NotReviewableFailed = M(
            "Dieses Kapitel hat keinen Inhalt, nur den Hinweis, dass die Recherche fehlgeschlagen ist. Recherchiere es zuerst erneut; danach lässt es sich prüfen.",
            "This chapter has no content, only the note that its research failed. Research it again first; then it can be reviewed.");

        // ---- the choice of the two AIs (400) -------------------------------------------------------------------------------------------

        public static readonly Msg ChoiceTitle = M("Prüfung kann nicht gestartet werden", "The review cannot be started");

        public static readonly Msg ReviewerMissing = M(
            "Bitte wähle, welche KI die Antworten prüft und entscheidet (Gemini oder Claude).",
            "Please choose which AI reviews the answers and decides (Gemini or Claude).");

        public static readonly Msg SecondMissing = M(
            "Bitte wähle, woher die zweite Antwort kommt: eine zweite KI (researcher) oder deine eingefügte Chat-Antwort (secondAnswer).",
            "Please choose where the second answer comes from: a second AI (researcher) or your pasted chat answer (secondAnswer).");

        public static readonly Msg SecondBoth = M(
            "Gib entweder eine zweite KI (researcher) oder eine eingefügte Chat-Antwort (secondAnswer) an, nicht beides.",
            "Give either a second AI (researcher) or a pasted chat answer (secondAnswer), not both.");

        public static Msg ReviewerNoWebSearch(string provider) => M(
            $"{Name(provider)} kann in dieser App nicht im Web suchen und deshalb nicht prüfen: Eine Prüfung muss jeden Unterschied mit einer Quelle belegen. Als Prüf-KI kommen nur Gemini und Claude infrage.",
            $"{Name(provider)} cannot search the web in this app and therefore cannot review: a review has to back every difference with a source. Only Gemini and Claude can act as the reviewer.");

        public static readonly Msg NeedsTwoAis = M(
            "Eine Prüfung braucht zwei verschiedene KIs: Die eine recherchiert das Kapitel ein zweites Mal, die andere vergleicht beide Antworten und entscheidet. Mit nur einem API-Key geht das nur, wenn die zweite Antwort aus deinem KI-Abo kommt (Mein KI-Abo) – oder hinterlege einen zweiten Key in den Einstellungen unter „KI-Recherche“.",
            "A review needs two different AIs: one researches the chapter a second time, the other compares both answers and decides. With only one API key that only works when the second answer comes from your AI subscription (My AI subscription) – or store a second key in Settings under “AI research”.");

        public static Msg SameProvider(string provider) => M(
            $"Die zweite Recherche und die Prüfung dürfen nicht von derselben KI kommen ({Name(provider)}): Dann würde sie ihre eigene Arbeit prüfen. Wähle für eine der beiden Aufgaben eine andere KI.",
            $"The second research and the review must not come from the same AI ({Name(provider)}): it would be reviewing its own work. Choose another AI for one of the two tasks.");

        // ---- failures while it runs ---------------------------------------------------------------------------------------------------

        /// <summary>The beginning of the message of a failed step; our sentence and the provider's own text follow.</summary>
        public static Msg StepFailed(bool secondResearch, string provider) => secondResearch
            ? M($"Die zweite Recherche mit {Name(provider)} ist fehlgeschlagen, das Kapitel bleibt unverändert.",
                $"The second research with {Name(provider)} failed, the chapter stays as it was.")
            : M($"Die Prüfung durch {Name(provider)} ist fehlgeschlagen, das Kapitel bleibt unverändert.",
                $"The review by {Name(provider)} failed, the chapter stays as it was.");

        public static Msg AnswerNotUsable(string provider, string reasons) => M(
            $"Die Antwort von {Name(provider)} auf die Prüfung war nicht verwendbar, das Kapitel bleibt unverändert. {reasons}".Trim(),
            $"The answer of {Name(provider)} to the review was not usable, the chapter stays as it was. {reasons}".Trim());

        public static readonly Msg AnswerNoChapter = M(
            "Die Antwort enthält kein Feld „chapter“ mit dem zusammengeführten Kapitel. Die Prüfung muss das ganze Kapitel zurückgeben (title, icon, sections) und die Befunde in „findings“.",
            "The answer has no “chapter” field with the merged chapter. The review has to return the whole chapter (title, icon, sections) and the findings in “findings”.");

        public static readonly Msg AnswerNoSections = M(
            "Das Kapitel in der Antwort hat keine Abschnitte („sections“ fehlt oder ist leer, oder keiner der Abschnitte ist lesbar).",
            "The chapter in the answer has no sections (“sections” is missing or empty, or none of the sections is readable).");

        public static Msg SecondSaysNotApplicable(string title, string reason) => M(
            $"Die zweite Antwort sagt, dass es das Thema „{title}“ in diesem Spiel nicht gibt{(string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason.Trim())} Das ist keine zweite Fassung des Kapitels, die sich prüfen ließe – das Kapitel bleibt unverändert. Entfernst du es nicht, kannst du die Prüfung mit einer anderen Antwort wiederholen.",
            $"The second answer says the topic “{title}” does not exist in this game{(string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason.Trim())} That is no second version of the chapter that could be reviewed – the chapter stays as it was. Unless you remove it, you can repeat the review with another answer.");

        public static Msg ChapterChangedMeanwhile(string title) => M(
            $"Die Prüfung von „{title}“ wird nicht gespeichert: Das Kapitel wurde seit dem Start der Prüfung geändert (z. B. neu recherchiert oder in einem zweiten Fenster ersetzt) oder entfernt. Lade die Seite neu und starte die Prüfung bei Bedarf erneut.",
            $"The review of “{title}” is not saved: the chapter was changed since the review started (e.g. researched again or replaced in a second window) or removed. Reload the page and start the review again if you want.");

        // ---- warnings of a checked answer ------------------------------------------------------------------------------------------

        public static Msg WarnFindingsDropped(int count) => M(
            $"{count} Befund(e) der Antwort wurden ignoriert, weil sie keine bekannte Art (added, corrected, removed, unclear) oder keine Begründung haben.",
            $"{count} finding(s) of the answer were ignored because they have no known kind (added, corrected, removed, unclear) or no reason.");

        public static Msg WarnFindingsWithoutSource(int count) => M(
            $"{count} Befund(e) nennen keine Quelle (eine gültige http(s)-Adresse). Ohne Quelle lässt sich die Entscheidung nicht nachprüfen – prüfe sie selbst, bevor du dich darauf verlässt. Bei einem Chat ohne Websuche ist das zu erwarten.",
            $"{count} finding(s) name no source (a valid http(s) address). Without a source the decision cannot be verified – check it yourself before you rely on it. With a chat that has no web search this is to be expected.");

        public static Msg WarnEntriesPutBack(int count) => M(
            $"{count} Eintrag/Einträge beider Antworten fehlten im zusammengeführten Kapitel, ohne dass ein Befund „removed“ sie begründet. Sie wurden wieder eingefügt und als „unklar“ markiert.",
            $"{count} entry/entries of the two answers were missing in the merged chapter without a “removed” finding that explains them. They were put back and marked “unclear”.");

        // ---- what is written INTO the chapter (guide language, resolved by the caller) ----------------------------------------------------

        public static readonly Msg DroppedWithoutReason = M(
            "Die Prüf-KI hat diesen Eintrag ohne Begründung weggelassen; er wurde wieder eingefügt.",
            "The reviewer left this entry out without a reason; it was put back.");

        public static readonly Msg UnclearTitle = M("Noch unklar", "Still unclear");

        public static Msg UnclearIntro(int count) => count == 1
            ? M("Bei einem Punkt konnten die beiden Antworten nicht mit einer Quelle geklärt werden. Der Eintrag ist markiert und zeigt beide Fassungen.",
                "The two answers could not be settled with a source for one point. The entry is marked and shows both versions.")
            : M($"Bei {count} Punkten konnten die beiden Antworten nicht mit einer Quelle geklärt werden. Die Einträge sind markiert und zeigen beide Fassungen.",
                $"The two answers could not be settled with a source for {count} points. The entries are marked and show both versions.");

        // ---- the stored review and "take the other version" -------------------------------------------------------------------------------

        public static readonly Msg NoReviewTitle = M("Keine Prüfung", "No review");

        public static readonly Msg NoReview = M(
            "Dieses Kapitel wurde noch nicht geprüft.",
            "This chapter has not been reviewed yet.");

        public static readonly Msg FindingNotFoundTitle = M("Befund nicht gefunden", "Finding not found");

        public static Msg FindingNotFound(int index, int count) => M(
            $"Die Prüfung hat keinen Befund Nr. {index} (es gibt {count}). Lade die Seite neu.",
            $"The review has no finding no. {index} (there are {count}). Reload the page.");

        public static readonly Msg ChooseTitle = M("Fassung kann nicht übernommen werden", "The version cannot be taken");

        public static readonly Msg VersionUnknown = M(
            "Mögliche Fassungen sind „a“ (Antwort A, das Kapitel wie vorher), „b“ (die zweite Antwort) und „result“ (die Entscheidung der Prüf-KI).",
            "Possible versions are “a” (answer A, the chapter as it was), “b” (the second answer) and “result” (the reviewer's decision).");

        public static readonly Msg StaleChoose = M(
            "Das Kapitel wurde nach der Prüfung geändert (neu recherchiert, ersetzt oder erneut geprüft). Die Prüfung ist veraltet: Du kannst sie noch lesen, aber keine andere Fassung mehr übernehmen.",
            "The chapter was changed after the review (researched again, replaced or reviewed again). The review is outdated: you can still read it, but no longer take another version.");

        public static readonly Msg NotStructured = M(
            "Dieser Befund ist ein Freitext ohne Tabellenzeile, Karte oder Abschnitt, auf den er sich bezieht. Er lässt sich nur lesen, keine Fassung davon übernehmen.",
            "This finding is free text without a table row, card or section it refers to. It can only be read; no version of it can be taken.");

        public static readonly Msg VersionNotFound = M(
            "Die Stelle dieses Befunds gibt es weder im Kapitel noch in der gespeicherten Fassung (z. B. weil ihre Kennung beim Speichern geändert wurde). Es wurde nichts geändert.",
            "The place of this finding exists neither in the chapter nor in the stored version (e.g. because its id was changed when saving). Nothing was changed.");

        public static readonly Msg ResultNotAvailable = M(
            "Die Entscheidung der Prüf-KI lässt sich nicht wiederherstellen, nachdem eine andere Fassung übernommen wurde. Wähle „a“ oder „b“.",
            "The reviewer's decision cannot be restored after another version was taken. Choose “a” or “b”.");

        // ---- pasted answers (own AI subscription) -------------------------------------------------------------------------------------

        public static readonly Msg PasteTitle = M("Prüfung nicht gespeichert", "The review was not saved");

        public static readonly Msg SecondSectionsInvalid = M(
            "Die zweite Antwort (secondSections) fehlt oder enthält keinen lesbaren Abschnitt. Starte die Prüfung noch einmal mit der zweiten Antwort.",
            "The second answer (secondSections) is missing or has no readable section. Start the review again with the second answer.");

        public static readonly Msg BaseHashMissing = M(
            "Es fehlt der Stand des Kapitels, auf dem die Prüfung begonnen hat (baseHash). Starte die Prüfung noch einmal.",
            "The state of the chapter the review started from is missing (baseHash). Start the review again.");

        public static Msg NotSaved(string reasons) => M(
            $"Die Prüfung wurde nicht gespeichert, das Kapitel ist unverändert. {reasons}".Trim(),
            $"The review was not saved, the chapter is unchanged. {reasons}".Trim());

        public static Msg SecondNotUsable(string reasons) => M(
            $"Die zweite Antwort ist nicht verwendbar. {reasons}".Trim(),
            $"The second answer is not usable. {reasons}".Trim());
    }
}
