using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// The whole guide from ONE prompt of the user's own AI subscription (owner decision 2026-11-19): the chat researches the game, picks the chapters from the
/// fixed topic list itself and writes every chapter. The answer is ONE JSON array: element 0 is the PLAN object ({ "game", "steamAppId", "sourceSituation",
/// "plan": [ { "chapter": "K01", "topicId", "title", "icon", "focus" } ... ] }), then one chapter object per plan entry in exactly the format of the batch
/// answer. The prompt joins the research task of the blueprint prompt (same text blocks) with the chapter rules and the output contract of the batch prompt;
/// the plan is read here (<see cref="CheckManualOneShotPlan"/>, saves nothing), the chapters are checked and saved by the batch code, which skips the plan element.
/// Nothing in this file talks to an AI provider.
/// </summary>
public sealed partial class AiGuideGeneratorService
{
    // ---- The guide's existing chapters, as the one-step flow sees them -------------------------------------------------------------------

    /// <summary>One chapter of the navigation: its tab, title, the standard topic it covers (null = unknown or an own chapter) and whether its research failed (a placeholder).</summary>
    internal sealed record OneShotChapter(Guid TabId, string Title, string? TopicId, bool Failed);

    /// <summary>The hidden Steam achievement list is no chapter of the navigation (the live trophies read their tips from it).</summary>
    private static bool IsAchievementTab(GuideTabEntity tab) =>
        string.Equals(tab.Slug, "achievements", StringComparison.OrdinalIgnoreCase)
        || (tab.SectionsJson.Contains("steam-achievements", StringComparison.Ordinal) && ApiEndpoints.IsAchievementList(tab, ApiEndpoints.DeserializeSections(tab.SectionsJson)));

    /// <summary>
    /// The chapters of the navigation in guide order (walkthrough first): not the Story &amp; Lore tab, not the hidden achievement list. A failed chapter is a
    /// placeholder (<see cref="FailedChapters"/>); its topic is the one it remembers, a real chapter's topic is the stored one (the walkthrough is known by its address).
    /// </summary>
    internal static List<OneShotChapter> OneShotExistingChapters(GameEntity game) =>
        WalkthroughTab.InGuideOrder(game.Tabs.Where(t => t.Slug != LoreTab.Slug && !IsAchievementTab(t)))
            .Select(tab =>
            {
                var failure = FailedChapters.Read(tab.SectionsJson);
                var topic = WalkthroughTab.Is(tab) ? WalkthroughTab.TopicId : tab.TopicId ?? failure?.TopicId;
                return new OneShotChapter(tab.Id, tab.Title, string.IsNullOrWhiteSpace(topic) || topic == "extra" ? null : topic, failure is not null);
            })
            .ToList();

    /// <summary>The chapters that count against the budget of the scope: everything of the navigation except the walkthrough (it has its own rule).</summary>
    private static int OneShotBudgetCount(IEnumerable<OneShotChapter> chapters) => chapters.Count(c => c.TopicId != WalkthroughTab.TopicId);

    // ---- The prompt --------------------------------------------------------------------------------------------------------------------

    /// <summary>The prompt for the whole guide (a new guide, or - when the guide has chapters - only the missing ones). Answer = plan + chapters in one array.</summary>
    public async Task<ManualPromptDto> BuildManualOneShotPromptAsync(GameEntity game, CancellationToken ct = default)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        // The items of the chapters that exist are the link targets of the new ones (the same list the batch prompt gives)
        var context = WithLinkCandidates(await ManualContextAsync(game, language, ct), game.Tabs);
        var prompt = ComposeManualOneShotPrompt(context, game, language);
        return new ManualPromptDto("oneshot", prompt, ManualFileName(game, language == Lang.English ? "guide-complete" : "guide-komplett"), language, game.SteamAppId, game.Title);
    }

    /// <summary>
    /// The one-step prompt: facts, wishes, the research task of the blueprint (kind of game, check of EVERY topic, budget, walkthrough rule, the existing chapters),
    /// then the chapter rules of the batch prompt (structure, puzzles, research rules, chapter object, walkthrough special case) and the output contract for the chat.
    /// </summary>
    internal static string ComposeManualOneShotPrompt(GameContext context, GameEntity game, string language)
    {
        var english = language == Lang.English;
        string T(string de, string en) => english ? en : de;

        var title = game.Title.Replace("\"", "'");
        var appId = game.SteamAppId is > 0 ? game.SteamAppId.Value.ToString() : "null";
        var existing = OneShotExistingChapters(game);
        // A walkthrough that exists as a real chapter is never written again; otherwise the plan may contain it
        var walkthroughPossible = !existing.Any(c => c.TopicId == WalkthroughTab.TopicId && !c.Failed);
        var scope = context.Blocks.Scope;
        var wishes = ResearchBlocks.BuildWishesBlock(context.Blocks, context.ResearchBase, ResearchBlocks.Scope.Chapter, language);
        var structure = ChapterStructureText(context);
        var sectionsJson = ChapterSectionsJson(language);
        var puzzleRules = PuzzleRules(language);
        var rules = WithLinkRules(ResearchRules(language, context.Blocks, context.Platform), context);
        var answerFile = ManualBatchAnswerFileName(game, language);

        // Steps 1 and 2 and the topic check are the text of the blueprint prompt; only the words that name its answer format are the plan's
        var analysisSteps = BlueprintAnalysisSteps(context);
        var topicStep = BlueprintTopicStep(language,
            header: T(
                "Schritt 3 – Plane die Kapitel: Prüfe JEDES Thema der Themenliste: Hilft ein eigenes Kapitel dazu dem Spieler bei DIESEM Spiel? Die Themen, die du übernimmst, ergeben deinen Plan – in der Reihenfolge des Guides, die Komplettlösung zuerst, wenn es eine gibt; ein Thema, das du weglässt, bekommt kein Kapitel. Die Wünsche des Nutzers oben gelten auch für die Auswahl und die Rechercheaufträge („focus“).",
                "Step 3 – Plan the chapters: check EVERY topic of the topic list: Does a chapter of its own help the player with THIS game? The topics you take make your plan – in the order of the guide, the walkthrough first if there is one; a topic you leave out gets no chapter. The wishes of the user above also apply to the choice and to the research assignments (\"focus\")."),
            yes: T("in den Plan: die Themen-Id,", "into the plan: the topic id,"),
            no: T("lass es im Plan weg.", "leave it out of the plan."),
            quests: T("eines deiner Kapitel", "is one of your chapters"),
            extras: T("Kapitel mit \"topicId\": \"extra\"", "chapters with \"topicId\": \"extra\""));
        var budgetNote = ChapterBudget.PlanNote(scope, language, OneShotBudgetCount(existing));
        var listNote = ListNote(context, T("unter den \"extra\"-Kapiteln", "with the topicId \"extra\""));

        var text = new StringBuilder();
        text.AppendLine(T(
            $"Du schreibst eine komplette 100%-Komplettlösung für „{game.Title}“ – alles in diesem einen Chat: Du recherchierst das Spiel, entscheidest selbst, welche Kapitel der Guide bekommt, und schreibst dann jedes Kapitel. Schreibe ALLE Texte auf Deutsch.",
            $"You are writing a complete 100% walkthrough for \"{game.Title}\" – all of it in this one chat: you research the game, decide yourself which chapters the guide gets, and then write every chapter. Write ALL texts in English."));
        text.AppendLine();
        text.AppendLine(FactsHeader(context.Platform, language));
        text.AppendLine(context.FactsBlock);
        text.AppendLine();
        if (!string.IsNullOrWhiteSpace(wishes))
        {
            text.AppendLine(wishes);
            text.AppendLine();
        }
        text.AppendLine(T(
            "So arbeitest du: Schritt 1 bis 3 sind die Recherche und der Plan, Schritt 4 ist das Schreiben der Kapitel. Die Antwort ist EIN JSON-Array: zuerst der Plan, dann ein JSON-Objekt je Kapitel (siehe „Ausgabe in diesem Chat“).",
            "How this works: steps 1 to 3 are the research and the plan, step 4 is writing the chapters. The answer is ONE JSON array: the plan first, then one JSON object per chapter (see \"Output in this chat\")."));
        text.AppendLine();
        text.AppendLine(analysisSteps);
        text.AppendLine();
        text.AppendLine(topicStep);
        text.AppendLine(budgetNote);
        text.AppendLine(listNote);
        text.AppendLine(ExistingChaptersNote(existing, english));
        text.AppendLine();
        text.AppendLine(T("Themenliste (id: Thema – Inhalt):", "Topic list (id: topic – content):"));
        text.AppendLine(TopicListText(language));
        text.AppendLine();
        text.AppendLine(T(
            "Schritt 4 – Schreibe jedes Kapitel deines Plans, jedes für sich nach der folgenden Vorgabe – sie gilt für JEDES Kapitel. Jedes Kapitel ist ein eigenes JSON-Objekt; der Plan und alle Kapitel zusammen bilden EIN JSON-Array (siehe „Ausgabe in diesem Chat“).",
            "Step 4 – Write every chapter of your plan, each on its own, following the specification below – it applies to EVERY chapter. Every chapter is its own JSON object; the plan and all chapters together form ONE JSON array (see \"Output in this chat\")."));
        text.AppendLine();
        text.AppendLine(structure);
        text.AppendLine();
        text.AppendLine(puzzleRules);
        text.AppendLine();
        text.AppendLine(rules);
        text.AppendLine();
        text.AppendLine(ManualChapterJsonStructure(language, title, appId, sectionsJson, fromPlan: true));
        text.AppendLine();
        text.AppendLine(T(
            $"Zeigt deine Recherche beim Schreiben, dass es das Thema eines Kapitels in „{game.Title}“ NICHT gibt oder nur so am Rande, dass ein eigenes Kapitel sinnlos ist (nicht bei den Themen walkthrough, roadmap und tips – sie schreibst du immer vollständig), schreibe das Kapitel NICHT, erkläre nicht lang, dass es fehlt, und fülle es nicht mit Stoff aus anderen Themen auf. Setze stattdessen dieses Objekt an seine Stelle im Array: {{ \"game\": \"{title}\", \"steamAppId\": {appId}, \"chapter\": \"K05\", \"notApplicable\": true, \"reason\": \"...\" }} – „reason“ ist ein kurzer Satz, warum (was das Spiel stattdessen hat).",
            $"If, while writing, your research shows that the topic of a chapter does NOT exist in \"{game.Title}\" or only so marginally that a chapter of its own is pointless (not for the topics walkthrough, roadmap and tips – you always write those in full), do NOT write the chapter, do not explain at length that it is missing and do not fill it with material from other topics. Put this object in its place in the array instead: {{ \"game\": \"{title}\", \"steamAppId\": {appId}, \"chapter\": \"K05\", \"notApplicable\": true, \"reason\": \"...\" }} – \"reason\" is one short sentence why (what the game has instead)."));
        text.AppendLine();
        if (walkthroughPossible)
        {
            text.AppendLine(ManualWalkthroughSpecialCase(context, language, "\"topicId\": \"walkthrough\"",
                T("im selben Array, an der Stelle, die dein Plan ihr gibt", "into the same array like every other chapter, at the place your plan gives it")));
            text.AppendLine();
        }

        var planSkeleton = $"{{ \"game\": \"{title}\", \"steamAppId\": {appId}, \"sourceSituation\": \"{T("etabliert | duenn | neu", "established | thin | new")}\", \"plan\": [ {{ \"chapter\": \"K01\", \"topicId\": \"walkthrough\", \"title\": \"...\", \"icon\": \"mdi:...\", \"focus\": \"...\" }}, ... ] }}";
        var chapterSkeleton = $"{{ \"game\": \"{title}\", \"steamAppId\": {appId}, \"chapter\": \"K01\", \"title\": \"...\", \"icon\": \"mdi:...\", \"sections\": [...] }}";
        text.AppendLine(ManualOutputHeading(language));
        text.AppendLine(ManualGameIdentityLine(game, language));
        text.AppendLine(T(
            "- Die Ausgabe ist EIN JSON-Array: Element 0 ist der PLAN, danach folgt genau ein Objekt je Eintrag des Plans, in der Reihenfolge des Plans: [ <Plan>, <Objekt K01>, <Objekt K02>, … ]. Im Array sonst nichts – kein Text, keine Kommentare, keine Platzhalter. Beginne jedes Objekt in einer neuen Zeile.",
            "- The output is ONE JSON array: element 0 is the PLAN, followed by exactly one object per entry of the plan, in the order of the plan: [ <plan>, <object K01>, <object K02>, … ]. Nothing else in the array – no text, no comments, no placeholders. Start every object on a new line."));
        text.AppendLine(T(
            $"- Der Plan ist ein Objekt mit den Feldern „game“, „steamAppId“, „sourceSituation“ (etabliert, duenn oder neu – wie gut die Quellenlage ist) und „plan“, der Liste der Kapitel, so (verkürzt, schreibe die echten Inhalte): {planSkeleton}\n  „chapter“ ist der Schlüssel des Kapitels: K01, K02, … in der Reihenfolge des Guides (die Komplettlösung zuerst, wenn es eine gibt); „topicId“ ist die Id aus der Themenliste oder „extra“; „title“, „icon“ und „focus“ schreibst du wie in Schritt 3 beschrieben. Schreibe den Plan ZUERST und vollständig, noch vor dem ersten Kapitel – nur hier wird entschieden, welche Kapitel der Guide bekommt.",
            $"- The plan is an object with the fields \"game\", \"steamAppId\", \"sourceSituation\" (established, thin or new – how good the sources are) and \"plan\", the list of the chapters, like this (shortened, write the real content): {planSkeleton}\n  \"chapter\" is the key of the chapter: K01, K02, … in the order of the guide (the walkthrough first if there is one); \"topicId\" is the id from the topic list or \"extra\"; you write \"title\", \"icon\" and \"focus\" as described in step 3. Write the plan FIRST and in full, before the first chapter – this is the only place where it is decided which chapters the guide gets."));
        text.AppendLine(T(
            $"- Jedes Kapitel-Objekt folgt dem Plan: derselbe Schlüssel in „chapter“, derselbe „title“. Die Felder stehen in genau dieser Reihenfolge: „game“, „steamAppId“, „chapter“, „title“, „icon“, „sections“, so (verkürzt, schreibe die echten Inhalte): {chapterSkeleton}",
            $"- Every chapter object follows the plan: the same key in \"chapter\", the same \"title\". The fields are in exactly this order: \"game\", \"steamAppId\", \"chapter\", \"title\", \"icon\", \"sections\", like this (shortened, write the real content): {chapterSkeleton}"));
        text.AppendLine(ManualAnswerFileLine(language, answerFile));
        text.AppendLine(T(
            "- NUR wenn dein Chat keine Datei erzeugen kann: Schreibe dasselbe Array in EINEN ```json-Codeblock in EINER Antwort, im Block nichts außer dem Array. Passt nicht alles hinein, höre nach einem vollständigen Kapitel auf (schneide nie mitten in einem Kapitel ab), schließe das Array mit ] und schreibe als allerletzte Zeile „NOCH OFFEN: K05–K13“ (Beispiel; schreibe die echten Schlüssel der fehlenden Kapitel). Der Plan steht IMMER schon in dieser ersten Antwort. Schreibe ich dann „weiter“, antworte mit einem NEUEN Array nur der offenen Kapitel – OHNE den Plan –, nach denselben Regeln.",
            "- ONLY if your chat cannot create a file: write the same array into ONE ```json code block in ONE answer, nothing but the array inside the block. If everything does not fit, stop after a complete chapter (never cut off in the middle of a chapter), close the array with ] and write as the very last line \"STILL OPEN: K05–K13\" (an example; write the real keys of the missing chapters). The plan is ALWAYS in this first answer already. When I then write \"continue\", answer with a NEW array of only the open chapters – WITHOUT the plan –, following the same rules."));
        text.AppendLine(T(
            "- Wenn alle Kapitel deines Plans fertig sind, schreibe als allerletzte Zeile: „ALLE KAPITEL FERTIG: K01–K12“ (Beispiel; nenne den echten letzten Schlüssel deines Plans).",
            "- When all chapters of your plan are done, write as the very last line: \"ALL CHAPTERS DONE: K01–K12\" (an example; name the real last key of your plan)."));
        text.AppendLine(ManualRealContentLine(language));
        text.AppendLine(ManualWebSearchLine(language));
        text.AppendLine();
        text.Append(T(
            "Recherchiere jetzt das Spiel, plane die Kapitel, schreibe sie alle und liefere das JSON-Array.",
            "Now research the game, plan the chapters, write them all and deliver the JSON array."));
        return text.ToString().Replace("\r\n", "\n");
    }

    /// <summary>What the prompt says about the chapters the guide already has: nothing to write again, failed ones to write anew, or "the guide is new".</summary>
    private static string ExistingChaptersNote(IReadOnlyList<OneShotChapter> existing, bool english)
    {
        string T(string de, string en) => english ? en : de;
        if (existing.Count == 0)
        {
            return T("Der Guide ist neu.", "The guide is new.");
        }

        static string Line(OneShotChapter c) => $"- \"{c.Title.Replace("\"", "'")}\"" + (c.TopicId is null ? string.Empty : $" (topicId: {c.TopicId})");
        var note = new List<string>();
        var done = existing.Where(c => !c.Failed).ToList();
        var failed = existing.Where(c => c.Failed).ToList();
        if (done.Count > 0)
        {
            note.Add(T(
                "Der Guide hat schon diese Kapitel – plane und schreibe sie NICHT noch einmal (auch nicht unter einem anderen Titel):",
                "The guide already has these chapters – do NOT plan or write them again (not under another title either):"));
            note.AddRange(done.Select(Line));
        }
        if (failed.Count > 0)
        {
            note.Add(T(
                "Diese Kapitel sind schon angelegt, ihre Recherche ist aber fehlgeschlagen – plane sie wieder (mit derselben topicId und demselben Titel) und schreibe sie neu:",
                "These chapters exist, but their research failed – plan them again (with the same topicId and the same title) and write them anew:"));
            note.AddRange(failed.Select(Line));
        }
        note.Add(T("Dein Plan enthält nur, was dem Guide noch fehlt.", "Your plan contains only what the guide is still missing."));
        return string.Join("\n", note);
    }

    // ---- Reading the plan -------------------------------------------------------------------------------------------------------------

    /// <summary>The check of a pasted one-step answer: the dto, and whether the text had too many JSON blocks (the endpoint refuses that like the batch check does).</summary>
    internal sealed record OneShotPlanChecked(ManualOneShotPlanDto Dto, bool TooMany);

    /// <summary>One entry of the plan as it was read (before it is judged against the guide's existing chapters).</summary>
    private sealed record PlanEntry(int Number, string? Key, string Title, string Focus, string TopicId, string Icon);

    private static string? PlanText(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }
        var text = value.TryGetValue<string>(out var s) ? s : value.TryGetValue<int>(out var n) ? n.ToString() : null;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max].TrimEnd();

    /// <summary>
    /// Reads the plan of a pasted answer (the plan element of the array, or the first object with "plan") and turns it into the chapter list the batch endpoints
    /// take. Nothing is saved. A topic the guide already has is dropped from the list with a warning (its chapter object then shows up as unassigned in the
    /// batch check and is never saved twice); an entry for a failed chapter carries that placeholder's tab id, so saving replaces it. <paramref name="scope"/> is the
    /// research scope (<see cref="ChapterBudget"/>); more chapters than its ceiling is only a warning - the chapters are written already.
    /// </summary>
    internal static OneShotPlanChecked CheckManualOneShotPlan(GameEntity game, string? text, string? scope)
    {
        var language = GuideLanguage(game);
        var errors = new List<ManualIssueDto>();
        var warnings = new List<ManualIssueDto>();
        OneShotPlanChecked Result(List<ManualBatchChapter>? chapters = null) =>
            new(new ManualOneShotPlanDto(errors.Count == 0, errors, warnings, errors.Count == 0 ? chapters ?? [] : []), false);

        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add(ManualAnswers.Issue("empty", ServerTexts.Manual.AnswerEmpty));
            return Result();
        }
        var read = ManualBatchReader.Read(text);
        if (read.TooMany)
        {
            return new OneShotPlanChecked(new ManualOneShotPlanDto(false, [], [], []), TooMany: true);
        }

        if (read.Plans.Found.Count == 0)
        {
            if (read.Plans.Errors.Count > 0)
            {
                errors.AddRange(read.Plans.Errors);
            }
            else if (read.Plans.Patterns > 0)
            {
                errors.Add(ManualAnswers.Issue("planPattern", ServerTexts.ManualOneShot.PlanOnlyPattern));
            }
            else if (!text.Contains('{'))
            {
                errors.Add(ManualAnswers.Issue("noJson", ServerTexts.Manual.NoJson));
            }
            else
            {
                errors.Add(ManualAnswers.Issue("noPlan", ServerTexts.ManualOneShot.NoPlan));
            }
            return Result();
        }

        var plan = read.Plans.Found[0];
        if (read.Plans.Found.Count > 1)
        {
            warnings.Add(ManualAnswers.Issue("severalPlans", ServerTexts.ManualOneShot.SeveralPlans(read.Plans.Found.Count)));
        }
        ManualAnswers.CheckGame(plan, game.SteamAppId, game.Title, errors, warnings);
        if (errors.Count > 0)
        {
            return Result();
        }

        var rawEntries = (JsonArray)plan["plan"]!;
        if (rawEntries.Count == 0)
        {
            errors.Add(ManualAnswers.Issue("emptyPlan", ServerTexts.ManualOneShot.PlanEmpty));
            return Result();
        }

        // The entries: key, title, topic (unknown -> "extra" with a warning), research assignment and icon (from the standard topic when the entry has none)
        var topics = Topics(language).ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var entries = new List<PlanEntry>();
        for (var i = 0; i < rawEntries.Count; i++)
        {
            var number = i + 1;
            if (rawEntries[i] is not JsonObject raw)
            {
                warnings.Add(ManualAnswers.Issue("planEntrySkipped", ServerTexts.ManualOneShot.EntrySkipped(number)));
                continue;
            }
            var key = PlanText(raw["chapter"]);
            var given = PlanText(raw["topicId"]);
            string topicId;
            GuideTopic? topic = null;
            if (given is null || string.Equals(given, "extra", StringComparison.OrdinalIgnoreCase))
            {
                topicId = "extra";
            }
            else if (topics.TryGetValue(given, out topic))
            {
                topicId = topic.Id;
            }
            else
            {
                topicId = "extra";
                warnings.Add(ManualAnswers.Issue("unknownTopic", ServerTexts.ManualOneShot.UnknownTopic(key ?? $"#{number}", PlanText(raw["title"]) ?? given, given)));
            }

            var title = PlanText(raw["title"]) ?? topic?.Title;
            if (title is null)
            {
                warnings.Add(ManualAnswers.Issue("planEntrySkipped", ServerTexts.ManualOneShot.EntrySkipped(number)));
                continue;
            }
            // Chapter numbers are assigned by sort order: strip any numbering the AI added; titles are in the guide's language only
            title = Clip(ChapterTitles.Clean(Regex.Replace(title, @"^\s*\d+\.\s*", string.Empty), language), ManualBatchChapters.MaxTitle);
            var icon = PlanText(raw["icon"]);
            if (icon is null || icon.Length > 100 || ManualBatchReader.IsPlaceholder(icon))
            {
                icon = topic?.Icon ?? "mdi:star-four-points";
            }
            var focus = Clip(PlanText(raw["focus"]) ?? topic?.Brief ?? title, ManualBatchChapters.MaxFocus);
            entries.Add(new PlanEntry(number, key, title, focus, topicId, icon));
        }

        // One key per chapter: the answer's chapter objects are assigned by it
        var duplicateKeys = entries.Where(e => e.Key is not null).GroupBy(e => ManualBatchChapters.KeyForm(e.Key!)).Where(g => g.Count() > 1).Select(g => g.First().Key!).ToList();
        if (duplicateKeys.Count > 0)
        {
            errors.Add(ManualAnswers.Issue("duplicateKeys", ServerTexts.ManualOneShot.DuplicateKeys(string.Join(", ", duplicateKeys))));
            return Result();
        }

        // Against the guide: a failed chapter is replaced (its tab id goes with the entry), a topic the guide already has is dropped
        var existing = OneShotExistingChapters(game);
        var real = existing.Where(c => !c.Failed).ToList();
        var placeholders = existing.Where(c => c.Failed).ToList();
        var used = new HashSet<Guid>();
        string FormOf(string value) => TitleForm(ChapterTitles.Clean(value, language));
        var kept = new List<(PlanEntry Entry, Guid? TabId)>();
        foreach (var entry in entries)
        {
            var key = entry.Key ?? $"#{entry.Number}";
            var placeholder = placeholders.FirstOrDefault(p => !used.Contains(p.TabId) && entry.TopicId != "extra" && p.TopicId == entry.TopicId)
                ?? placeholders.FirstOrDefault(p => !used.Contains(p.TabId) && FormOf(p.Title) is { Length: > 0 } form && form == FormOf(entry.Title));
            if (placeholder is not null)
            {
                used.Add(placeholder.TabId);
                kept.Add((entry, placeholder.TabId));
                continue;
            }
            var covered = real.FirstOrDefault(c => entry.TopicId != "extra" && string.Equals(c.TopicId, entry.TopicId, StringComparison.OrdinalIgnoreCase))
                ?? real.FirstOrDefault(c => FormOf(c.Title) is { Length: > 0 } form && form == FormOf(entry.Title));
            if (covered is not null)
            {
                warnings.Add(ManualAnswers.Issue("alreadyCovered", ServerTexts.ManualOneShot.AlreadyCovered(key, entry.Title, covered.Title)));
                continue;
            }
            kept.Add((entry, null));
        }

        if (kept.Count == 0)
        {
            errors.Add(ManualAnswers.Issue("nothingNew", ServerTexts.ManualOneShot.NothingNew));
            return Result();
        }
        if (kept.Count > ManualBatchChapters.MaxChapters)
        {
            errors.Add(ManualAnswers.Issue("tooManyChapters", ServerTexts.ManualOneShot.TooManyChapters(kept.Count, ManualBatchChapters.MaxChapters)));
            return Result();
        }

        // More chapters than the ceiling of the scope: only a warning (the chapters are written already); the walkthrough has its own rule and never counts
        var (_, max) = ChapterBudget.For(scope);
        var total = OneShotBudgetCount(existing) + kept.Count(k => k.TabId is null && k.Entry.TopicId != WalkthroughTab.TopicId);
        if (total > max)
        {
            warnings.Add(ManualAnswers.Issue("overBudget", ServerTexts.ManualOneShot.OverBudget(total, ChapterBudget.ScopeName(scope, language), max)));
        }

        var source = PlanText(plan["sourceSituation"]) is { } situation ? Clip(situation, ManualBatchChapters.MaxFocus) : null;
        var items = kept.Select(k => new ManualBatchChapter(k.Entry.Key, k.Entry.Title, k.Entry.Focus, k.Entry.TopicId, source, k.Entry.Icon, k.TabId)).ToList();
        // The same validation and key assignment the batch endpoints apply (a missing or invalid key becomes K01, K02, ... by position), so the list goes back to them as it is
        if (ManualBatchChapters.Normalize(items, forPrompt: false, out var chapters) is not null)
        {
            errors.Add(ManualAnswers.Issue("planInvalid", ServerTexts.ManualOneShot.PlanInvalid(string.Empty)));
            return Result();
        }
        return Result(chapters.Select(c => new ManualBatchChapter(c.Key, c.Title, c.Focus, c.TopicId, c.SourceSituation, c.Icon, c.TabId)).ToList());
    }

    /// <summary>The check with the research scope from the settings (nothing else is read: no Steam facts, no web call).</summary>
    internal async Task<OneShotPlanChecked> CheckManualOneShotPlanAsync(GameEntity game, string? text, CancellationToken ct = default)
    {
        var blocks = await settingsStore.GetResearchBlocksAsync(ct);
        return CheckManualOneShotPlan(game, text, blocks.Scope);
    }
}
