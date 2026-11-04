using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Companion.Api.Data;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// Batch mode of the manual flow: ONE prompt for many chapters (the shared rules once, then the chapter list) and ONE check/save of everything the user
/// collected from the chat. Nothing here talks to an AI provider; chapters are checked and built by the single-chapter code.
/// </summary>
public sealed partial class AiGuideGeneratorService
{
    // ---- The prompt --------------------------------------------------------------------------------------------------------------

    internal async Task<ManualBatchPromptDto> BuildManualBatchPromptAsync(GameEntity game, IReadOnlyList<BatchChapter> chapters, CancellationToken ct = default)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        // The context is cached for a few minutes; the list of existing items is made from the guide as it is now (chapters this batch replaces are left out)
        var context = WithLinkCandidates(await ManualContextAsync(game, language, ct), game.Tabs, chapters.Where(c => c.TabId is not null).Select(c => c.TabId!.Value).ToHashSet());
        chapters = ResolveStoredChapters(game, chapters);
        // Chapters that are written again keep the item ids of their current rows (one list for all of them, each group named by the chapter's key)
        var keep = new List<GuideLinker.CandidateGroup>();
        foreach (var chapter in chapters.Where(c => c.Replace && c.TabId is not null))
        {
            if (game.Tabs.FirstOrDefault(t => t.Id == chapter.TabId) is { } stored && WithKeepItems(context, stored).KeepGroups is { } groups)
            {
                keep.AddRange(groups.Select(g => g with { Title = $"{chapter.Key}: {g.Title}" }));
            }
        }
        context = context with { KeepGroups = keep.Count > 0 ? keep : null };
        var cleaned = chapters.Select(c => c with { Title = ChapterTitles.Clean(c.Title, language) }).ToList();
        var prompt = ComposeManualBatchPrompt(context, game, language, cleaned);
        return new ManualBatchPromptDto(
            "batch", prompt, ManualFileName(game, language == Lang.English ? "chapters-batch" : "kapitel-sammel"), language, game.SteamAppId, game.Title,
            cleaned.Select(c => new ManualBatchChapterInfo(c.Key, c.Title, c.TopicId)).ToList());
    }

    /// <summary>
    /// The chapters of a batch that replace a stored chapter (TabId + Replace) take topic and assignment from that chapter, exactly like a single manual
    /// replace (<see cref="ManualChapterAssignment"/>); a failed placeholder keeps the client's assignment but its stored topic. Used for prompt, check and save,
    /// so all three judge the chapter by the same topic (e.g. the walkthrough rules).
    /// </summary>
    internal static List<BatchChapter> ResolveStoredChapters(GameEntity game, IReadOnlyList<BatchChapter> chapters)
    {
        var language = GuideLanguage(game);
        return chapters.Select(chapter =>
        {
            if (chapter.TabId is not { } tabId || game.Tabs.FirstOrDefault(t => t.Id == tabId) is not { } stored || ChapterRemoval.Check(stored) != ChapterRemovalVerdict.Removable)
            {
                return chapter;
            }
            var (focus, topic) = ManualChapterAssignment(stored, new ManualChapterPromptRequest(chapter.Title, chapter.Icon, chapter.Focus, chapter.TopicId, chapter.SourceSituation, tabId, chapter.Replace), language);
            return chapter with { Focus = focus, TopicId = topic };
        }).ToList();
    }

    /// <summary>The name of the file a chat may hand back instead of many code blocks.</summary>
    internal static string ManualBatchAnswerFileName(GameEntity game, string language) =>
        $"platinvault-{Slugify(game.Title)}-{(language == Lang.English ? "chapters" : "kapitel")}.json";

    /// <summary>
    /// The batch prompt: facts, wishes, structure, building blocks and rules ONCE (the same text blocks as the single-chapter prompt), the pattern of one
    /// chapter object, the "does not exist" form, the special walkthrough block, the output contract for the chat, and last the list of chapters.
    /// </summary>
    internal static string ComposeManualBatchPrompt(GameContext context, GameEntity game, string language, IReadOnlyList<BatchChapter> chapters)
    {
        var english = language == Lang.English;
        string T(string de, string en) => english ? en : de;

        var title = game.Title.Replace("\"", "'");
        var appId = game.SteamAppId is > 0 ? game.SteamAppId.Value.ToString() : "null";
        var appIdLine = game.SteamAppId is > 0 ? T($"Steam-AppID {game.SteamAppId}", $"Steam AppID {game.SteamAppId}") : T("Steam-AppID unbekannt", "Steam AppID unknown");
        var first = chapters[0].Key;
        var last = chapters[^1].Key;
        var doneRange = chapters.Count == 1 ? first : $"{first}–{last}";
        var wishes = ResearchBlocks.BuildWishesBlock(context.Blocks, context.ResearchBase, ResearchBlocks.Scope.Chapter, language);
        var structure = ChapterStructureText(context);
        var sectionsJson = ChapterSectionsJson(language);
        var puzzleRules = PuzzleRules(language);
        var rules = WithLinkRules(ResearchRules(language, context.Blocks), context);
        var answerFile = ManualBatchAnswerFileName(game, language);

        // The source situation is one value in practice (from the blueprint); it is stated once, and per chapter only when the chapters differ
        var sources = chapters.Select(c => c.SourceSituation ?? UnknownSource(language)).Distinct(StringComparer.Ordinal).ToList();
        var sharedSource = sources.Count == 1 ? sources[0] : null;
        var anyDroppable = chapters.Any(c => ChapterApplicability.CanBeDropped(c.TopicId ?? "extra"));
        var walkthroughs = chapters.Where(c => c.TopicId == WalkthroughTab.TopicId).ToList();

        var text = new StringBuilder();
        text.AppendLine(T(
            $"Du schreibst {chapters.Count} Kapitel einer 100%-Komplettlösung für „{game.Title}“ – alle in diesem einen Chat. Schreibe ALLE Texte auf Deutsch.",
            $"You are writing {chapters.Count} chapters of a 100% walkthrough for \"{game.Title}\" – all of them in this one chat. Write ALL texts in English."));
        text.AppendLine();
        text.AppendLine(T("Bekannte Fakten aus Steam:", "Known facts from Steam:"));
        text.AppendLine(context.FactsBlock);
        if (sharedSource is not null)
        {
            text.AppendLine(T($"Quellenlage laut Vorab-Analyse: {sharedSource}", $"Source situation according to the preliminary analysis: {sharedSource}"));
        }
        text.AppendLine();
        if (!string.IsNullOrWhiteSpace(wishes))
        {
            text.AppendLine(wishes);
            text.AppendLine();
        }
        text.AppendLine(T(
            "So arbeitest du: Alle Kapitel stehen am Ende unter „Kapitel“. Schreibe jedes Kapitel für sich nach der folgenden Vorgabe – sie gilt für JEDES Kapitel. Jedes Kapitel bekommt sein eigenes JSON-Objekt (siehe „Ausgabe in diesem Chat“).",
            "How this works: all chapters are listed at the end under \"Chapters\". Write each chapter on its own, following the specification below – it applies to EVERY chapter. Every chapter gets its own JSON object (see \"Output in this chat\")."));
        text.AppendLine();
        text.AppendLine(structure);
        text.AppendLine();
        text.AppendLine(puzzleRules);
        text.AppendLine();
        text.AppendLine(rules);
        text.AppendLine();
        text.AppendLine(T("JSON-Struktur EINES Kapitels:", "JSON structure of ONE chapter:"));
        text.AppendLine("{");
        text.AppendLine($"  \"game\": \"{title}\",");
        text.AppendLine($"  \"steamAppId\": {appId},");
        text.AppendLine(T("  \"chapter\": \"<Schlüssel aus der Liste, z. B. K07>\",", "  \"chapter\": \"<key from the list, e.g. K07>\","));
        text.AppendLine(T("  \"title\": \"<Kapitel-Titel aus der Liste>\",", "  \"title\": \"<chapter title from the list>\","));
        text.AppendLine("  \"icon\": \"mdi:...\",");
        text.AppendLine(sectionsJson);
        text.AppendLine("}");
        text.AppendLine();

        if (anyDroppable)
        {
            text.AppendLine(T(
                $"Kapitel mit „notApplicable erlaubt“ dürfen entfallen: Zeigt deine Recherche, dass es das Thema in „{game.Title}“ NICHT gibt (z. B. Gegner/Bosse in einem Aufbau- oder Fabrikspiel ohne Kampf) oder nur so am Rande, dass ein eigenes Kapitel sinnlos ist, schreibe das Kapitel NICHT, erkläre nicht lang, dass es fehlt, und fülle es nicht mit Stoff aus anderen Themen auf. Antworte für dieses Kapitel stattdessen mit diesem Objekt (in einem eigenen Codeblock wie jedes andere Kapitel): {{ \"game\": \"{title}\", \"steamAppId\": {appId}, \"chapter\": \"K05\", \"notApplicable\": true, \"reason\": \"...\" }} – „reason“ ist ein kurzer Satz, warum (was das Spiel stattdessen hat). Kapitel mit „notApplicable NICHT erlaubt“ schreibst du immer vollständig.",
                $"Chapters marked \"notApplicable allowed\" may be left out: if your research shows that the topic does NOT exist in \"{game.Title}\" (e.g. enemies/bosses in a building or factory game without combat) or only so marginally that a chapter of its own is pointless, do NOT write the chapter, do not explain at length that it is missing and do not fill it with material from other topics. For that chapter answer with this object instead (in its own code block like every other chapter): {{ \"game\": \"{title}\", \"steamAppId\": {appId}, \"chapter\": \"K05\", \"notApplicable\": true, \"reason\": \"...\" }} – \"reason\" is one short sentence why (what the game has instead). Chapters marked \"notApplicable NOT allowed\" are always written in full."));
            text.AppendLine();
        }

        if (walkthroughs.Count > 0)
        {
            var keys = string.Join(", ", walkthroughs.Select(c => c.Key));
            text.AppendLine(T(
                $"Sonderfall Komplettlösung ({keys}): Beantworte dieses Kapitel ALLEIN in einer eigenen Antwort (es ist lang), nicht zusammen mit anderen Kapiteln. Der Pflicht-Aufbau oben gilt dafür NICHT; es gilt stattdessen:",
                $"Special case walkthrough ({keys}): answer this chapter ALONE in its own answer (it is long), not together with other chapters. The mandatory structure above does NOT apply to it; this applies instead:"));
            text.AppendLine(WalkthroughSingleInstructions(language));
            text.AppendLine(AchievementReference(context));
            text.AppendLine(T(
                "Sein JSON-Objekt hat dieselben Felder wie die anderen („game“, „steamAppId“, „chapter“, „title“, „icon“, „sections“).",
                "Its JSON object has the same fields as the others (\"game\", \"steamAppId\", \"chapter\", \"title\", \"icon\", \"sections\")."));
            text.AppendLine();
        }

        var skeleton = $"{{ \"game\": \"{title}\", \"steamAppId\": {appId}, \"chapter\": \"{first}\", \"title\": \"{chapters[0].Title.Replace("\"", "'")}\", \"icon\": \"mdi:...\", \"sections\": [...] }}";
        text.AppendLine(T(
            "Ausgabe in diesem Chat (das gilt zusätzlich und hat bei der Ausgabe Vorrang vor dem Satz „ohne Markdown-Codeblock“ oben):",
            "Output in this chat (this applies in addition and wins over the sentence \"without a Markdown code block\" above):"));
        text.AppendLine(T(
            $"- Gemeint ist genau dieses Spiel: „{game.Title}“, {appIdLine} – die Steam-Version auf dem PC. Prüfe vor dem Schreiben, dass du dieses Spiel meinst – nicht ein gleichnamiges Spiel, einen Vorgänger, Nachfolger, ein Remake oder eine Demo.",
            $"- The game is exactly this one: \"{game.Title}\", {appIdLine} – the Steam version on PC. Before writing, make sure you mean this game – not a game with the same name, a predecessor, a successor, a remake or a demo."));
        text.AppendLine(T(
            $"- Schreibe JEDES Kapitel als eigenes JSON-Objekt in einem eigenen Codeblock (```json … ```), im Block nichts außer dem Objekt. Jedes Objekt beginnt mit den Feldern „game“, „steamAppId“, „chapter“ (der Schlüssel aus der Liste, z. B. „{first}“) und „title“, danach „icon“ und „sections“, so (verkürzt, schreibe die echten Inhalte): {skeleton}",
            $"- Write EVERY chapter as its own JSON object in its own code block (```json … ```), nothing but the object inside the block. Every object starts with the fields \"game\", \"steamAppId\", \"chapter\" (the key from the list, e.g. \"{first}\") and \"title\", then \"icon\" and \"sections\", like this (shortened, write the real content): {skeleton}"));
        text.AppendLine(T(
            "- Beantworte die Kapitel in der Reihenfolge der Liste. Schreibe pro Antwort so viele VOLLSTÄNDIGE Kapitel, wie hineinpassen; schneide nie ein Kapitel mittendrin ab – höre nach einem vollständigen Kapitel auf. Schreibe ich dann „weiter“, mache mit dem nächsten Kapitel weiter, das noch fehlt.",
            "- Answer the chapters in the order of the list. Put as many COMPLETE chapters into one answer as fit; never cut a chapter off in the middle – stop after a complete one. When I then write \"continue\", go on with the next chapter that is still missing."));
        text.AppendLine(T(
            $"- Nach dem letzten Kapitel schreibe als allerletzte Zeile: „ALLE KAPITEL FERTIG: {doneRange}“.",
            $"- After the last chapter write as the very last line: \"ALL CHAPTERS DONE: {doneRange}\"."));
        text.AppendLine(T(
            $"- Kann dein Chat eine Datei zum Herunterladen erzeugen, darfst du stattdessen alle Kapitel-Objekte (gleiche Felder) als EIN JSON-Array in eine Datei „{answerFile}“ schreiben und danach dieselbe Abschlusszeile ausgeben.",
            $"- If your chat can create a downloadable file, you MAY instead put all chapter objects (same fields) as ONE JSON array into one file \"{answerFile}\" and then write the same final line."));
        text.AppendLine(T(
            "- Schreibe echte Inhalte, keine Platzhalter wie \"...\" – die Strukturbeschreibung oben ist nur das Muster. Was du nicht belegen kannst: „Noch nicht dokumentiert (Stand <heutiges Datum>)“ – erfinde nichts.",
            "- Write real content, no placeholders like \"...\" – the structure description above is only the pattern. What you cannot back up: \"Not documented yet (as of <today's date>)\" – invent nothing."));
        text.AppendLine(T(
            "- Nutze die Websuche deines Chats, falls es eine gibt – für jedes Kapitel zuerst nach einem Wiki speziell zu diesem Spiel (Schritt 1 der Recherche-Regeln; gibt es keins, überspring ihn ohne Erwähnung); ohne Websuche kennzeichne die Quellenlage als dünn.",
            "- Use the web search of your chat if it has one - for every chapter first for a wiki dedicated to this game (step 1 of the research rules; skip it silently when there is none); without web search mark the source situation as thin."));
        text.AppendLine();

        text.AppendLine(T($"Kapitel ({chapters.Count}), in dieser Reihenfolge:", $"Chapters ({chapters.Count}), in this order:"));
        foreach (var chapter in chapters)
        {
            var droppable = ChapterApplicability.CanBeDropped(chapter.TopicId ?? "extra");
            var flags = droppable ? T("notApplicable erlaubt", "notApplicable allowed") : T("notApplicable NICHT erlaubt", "notApplicable NOT allowed");
            if (chapter.TopicId == WalkthroughTab.TopicId)
            {
                flags += T(" | eigene Antwort (Sonderfall Komplettlösung)", " | own answer (special case walkthrough)");
            }
            text.AppendLine($"- {chapter.Key} | \"{chapter.Title.Replace("\"", "'")}\" | {flags}");
            text.AppendLine($"  {T("Rechercheauftrag", "Research assignment")}: {OneLine(chapter.Focus)}");
            if (sharedSource is null)
            {
                text.AppendLine($"  {T("Quellenlage", "Source situation")}: {chapter.SourceSituation ?? UnknownSource(language)}");
            }
        }
        text.AppendLine();
        text.Append(T($"Beginne jetzt mit {first}.", $"Begin with {first} now."));
        return text.ToString().Replace("\r\n", "\n");
    }

    private static string OneLine(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    // ---- Checking everything that was pasted -------------------------------------------------------------------------------------

    /// <summary>The check of a batch: what the UI shows, plus the checked chapters by key (the canonical JSON saving builds the tabs from).</summary>
    internal sealed record BatchChecked(ManualBatchCheckDto Dto, Dictionary<string, ManualChapterChecked> Checks, bool TooMany);

    private static string TitleForm(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// Finds every chapter object in <paramref name="text"/>, assigns each to a chapter (the "chapter" key first, case-insensitive and "K7" == "K07"; else the
    /// cleaned title when it is unambiguous) and judges it with the single-chapter rules. Nothing is saved. A key that occurs several times: the LAST
    /// complete object wins (a later paste replaces an earlier draft).
    /// </summary>
    internal BatchChecked CheckManualBatch(GameEntity game, string? text, IReadOnlyList<BatchChapter> chapters)
    {
        var language = GuideLanguage(game);
        var read = ManualBatchReader.Read(text ?? string.Empty);
        var byKey = chapters.ToDictionary(c => c.KeyForm, StringComparer.Ordinal);
        var byTitle = chapters.GroupBy(c => TitleForm(ChapterTitles.Clean(c.Title, language))).Where(g => g.Key.Length > 0).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        BatchChapter? Assign(BatchObject o)
        {
            if (o.KeyHint is { } key)
            {
                if (byKey.TryGetValue(ManualBatchChapters.KeyForm(key), out var exact))
                {
                    return exact;
                }
                // "chapter": 7 (a bare number) finds the one chapter whose key ends in that number
                if (int.TryParse(key, out var number))
                {
                    var numbered = chapters.Where(c => Regex.IsMatch(c.KeyForm, "^[A-Z_\\-]*" + number + "$")).ToList();
                    if (numbered.Count == 1)
                    {
                        return numbered[0];
                    }
                }
            }
            if (o.TitleHint is { } title && byTitle.TryGetValue(TitleForm(ChapterTitles.Clean(title, language)), out var matches) && matches.Count == 1)
            {
                return matches[0];
            }
            return null;
        }

        var assigned = new Dictionary<int, List<BatchObject>>();
        var unmatched = new List<ManualBatchUnmatchedDto>();
        foreach (var o in read.Objects)
        {
            var chapter = Assign(o);
            if (chapter is not null)
            {
                if (!assigned.TryGetValue(chapter.Index, out var list))
                {
                    assigned[chapter.Index] = list = [];
                }
                list.Add(o);
                continue;
            }

            var errors = new List<ManualIssueDto>(o.Errors);
            if (o.Node is { } node)
            {
                if (node["format"]?.ToString() == GuideImport.FormatName)
                {
                    errors.Add(ManualAnswers.Issue("wholeGuide", ServerTexts.Manual.WholeGuideFile));
                }
                else if (node["sections"] is null && (node["topics"] is not null || node["extraChapters"] is not null))
                {
                    errors.Add(ManualAnswers.Issue("blueprintForChapter", ServerTexts.Manual.BlueprintForChapter));
                }
                else
                {
                    errors.Add(ManualAnswers.Issue("unmatched", ServerTexts.Manual.BatchUnmatched(o.KeyHint, o.TitleHint)));
                }
            }
            else if (o.KeyHint is not null)
            {
                errors.Add(ManualAnswers.Issue("unmatched", ServerTexts.Manual.BatchUnmatched(o.KeyHint, o.TitleHint)));
            }
            unmatched.Add(new ManualBatchUnmatchedDto(o.Order, o.TitleHint, errors));
        }

        var items = new List<ManualBatchItemDto>();
        var checks = new Dictionary<string, ManualChapterChecked>(StringComparer.Ordinal);
        foreach (var chapter in chapters)
        {
            if (!assigned.TryGetValue(chapter.Index, out var candidates))
            {
                items.Add(new ManualBatchItemDto(chapter.Key, chapter.Title, "missing", [], [], null, null));
                continue;
            }

            var chosen = candidates.LastOrDefault(c => c.Node is not null) ?? candidates[^1];
            var warnings = new List<ManualIssueDto>();
            if (candidates.Count > 1)
            {
                warnings.Add(ManualAnswers.Issue("duplicateInText", ServerTexts.Manual.BatchDuplicateInText(candidates.Count)));
                if (!ReferenceEquals(chosen, candidates[^1]))
                {
                    warnings.Add(ManualAnswers.Issue("laterCopyBroken", ServerTexts.Manual.BatchLaterCopyBroken));
                }
            }

            if (chosen.Node is null)
            {
                items.Add(new ManualBatchItemDto(chapter.Key, chapter.Title, "error", chosen.Errors, warnings, null, null));
                continue;
            }

            var checkedAnswer = CheckManualChapter(game, chosen.Node.ToJsonString(), chapter.Title, chapter.TopicId, chapter.TabId is not null && chapter.Replace != true, chapter.Key);
            warnings.AddRange(checkedAnswer.Dto.Warnings);
            string state;
            if (!checkedAnswer.Dto.Ok || checkedAnswer.CanonicalJson is null)
            {
                state = "error";
            }
            else
            {
                state = checkedAnswer.Dto.NotApplicable is not null ? "notApplicable" : warnings.Count > 0 ? "warning" : "ok";
                checks[chapter.Key] = checkedAnswer;
            }
            items.Add(new ManualBatchItemDto(chapter.Key, chapter.Title, state, checkedAnswer.Dto.Errors, warnings, checkedAnswer.Dto.Preview, checkedAnswer.Dto.NotApplicable, checkedAnswer.Dto.FixPrompt));
        }

        return new BatchChecked(new ManualBatchCheckDto(read.Objects.Count, items, unmatched), checks, read.TooMany);
    }
}
