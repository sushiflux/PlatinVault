namespace Companion.Api.Localization;

/// <summary>
/// Texts of the one-step prompt of the subscription flow (the whole guide from ONE prompt: plan + chapters in one answer): the findings about the plan
/// element of a pasted answer. Part of <see cref="ServerTexts"/>, kept in its own file; same rules as there (parameterised text = method, fixed text = field,
/// English line directly behind the German one).
/// </summary>
public static partial class ServerTexts
{
    public static class ManualOneShot
    {
        public static readonly Msg NoPlan = M(
            "Die Antwort enthält keinen Plan. Das erste Element des Arrays muss ein Objekt mit der Liste „plan“ sein, danach folgen die Kapitel (so steht es im Prompt). Füge die erste Antwort der KI ein, nicht nur spätere Teile („weiter“), oder bitte sie, den Plan als erstes Element vor die Kapitel zu setzen.",
            "The answer contains no plan. The first element of the array must be an object with the list “plan”, followed by the chapters (as the prompt says). Paste the first answer of the AI, not only later parts (“continue”), or ask it to put the plan as the first element before the chapters.");

        public static readonly Msg PlanEmpty = M(
            "Der Plan ist leer: „plan“ enthält kein Kapitel. Bitte die KI, die Antwort noch einmal mit dem vollständigen Plan als erstem Element auszugeben.",
            "The plan is empty: “plan” contains no chapter. Ask the AI to output the answer again with the complete plan as the first element.");

        public static readonly Msg PlanOnlyPattern = M(
            "Der Plan ist nur das Muster aus dem Prompt (mit Platzhaltern wie „...“), nicht die Antwort der KI. Kopiere die Antwort der KI, nicht den Prompt.",
            "The plan is only the pattern from the prompt (with placeholders like “...”), not the answer of the AI. Copy the answer of the AI, not the prompt.");

        public static Msg PlanCutOff(int missing) => M(
            $"Der Plan ist abgeschnitten: Das Objekt wird nicht geschlossen (es fehlen {missing} schließende Klammer(n)). Der Plan muss vollständig in der ersten Antwort stehen. Bitte die KI, die Antwort noch einmal auszugeben – mit dem vollständigen Plan als erstem Element.",
            $"The plan is cut off: the object is never closed ({missing} closing bracket(s) are missing). The plan must be complete in the first answer. Ask the AI to output the answer again – with the complete plan as the first element.");

        public static Msg PlanInvalid(string detail) => M(
            $"Der Plan ist kein gültiges JSON. {detail}".Trim(),
            $"The plan is not valid JSON. {detail}".Trim());

        public static Msg DuplicateKeys(string keys) => M(
            $"Im Plan kommt derselbe Kapitelschlüssel mehrfach vor: {keys}. Jedes Kapitel braucht einen eigenen Schlüssel (K01, K02, …), sonst lassen sich die Kapitel der Antwort nicht zuordnen. Bitte die KI, den Plan mit eindeutigen Schlüsseln noch einmal auszugeben.",
            $"The same chapter key occurs several times in the plan: {keys}. Every chapter needs its own key (K01, K02, …), otherwise the chapters of the answer cannot be assigned. Ask the AI to output the plan again with unique keys.");

        public static Msg TooManyChapters(int count, int max) => M(
            $"Der Plan hat {count} Kapitel – mehr als {max} Kapitel kann PlatinVault auf einmal nicht verarbeiten. Bitte die KI, den Guide kleiner zu planen (Themen zusammenfassen), oder nimm den Weg „Kapitel selbst auswählen“.",
            $"The plan has {count} chapters – PlatinVault cannot process more than {max} chapters at once. Ask the AI to plan a smaller guide (merge topics), or use the way “Choose chapters myself”.");

        public static Msg UnknownTopic(string key, string title, string topicId) => M(
            $"Kapitel {key} „{title}“: Das Thema „{topicId}“ steht nicht in der Themenliste und wird als eigenes Kapitel („extra“) behandelt.",
            $"Chapter {key} “{title}”: the topic “{topicId}” is not in the topic list and is treated as a chapter of its own (“extra”).");

        public static Msg OverBudget(int count, string scope, int max) => M(
            $"Der Plan führt zu {count} Kapiteln, der Umfang „{scope}“ sieht höchstens {max} vor. Weil die KI die Kapitel schon geschrieben hat, bleiben alle im Plan. Was dir zu viel ist, kannst du nach dem Speichern über „Kapitel entfernen“ löschen.",
            $"The plan leads to {count} chapters, the scope “{scope}” allows at most {max}. Because the AI has already written the chapters, all of them stay in the plan. What is too much for you can be deleted after saving with “Remove chapter”.");

        public static Msg AlreadyCovered(string key, string title, string existing) => M(
            $"Kapitel {key} „{title}“ gibt es schon im Guide („{existing}“). Es wird nicht noch einmal gespeichert; falls die Antwort es trotzdem enthält, erscheint es unten als „nicht zugeordnet“.",
            $"Chapter {key} “{title}” already exists in the guide (“{existing}”). It is not saved again; if the answer contains it anyway, it shows up below as “not assigned”.");

        public static Msg EntrySkipped(int number) => M(
            $"Eintrag {number} des Plans hat keinen Titel und kein bekanntes Thema und wird übersprungen.",
            $"Entry {number} of the plan has no title and no known topic and is skipped.");

        public static Msg SeveralPlans(int count) => M(
            $"Im eingefügten Text stehen {count} Pläne; verwendet wird der erste.",
            $"The pasted text contains {count} plans; the first one is used.");

        public static readonly Msg NothingNew = M(
            "Der Plan enthält kein Kapitel, das dem Guide noch fehlt: Alle genannten Themen sind schon als Kapitel vorhanden. Es gibt nichts zu speichern.",
            "The plan contains no chapter that the guide is still missing: all the topics it names already exist as chapters. There is nothing to save.");
    }
}
