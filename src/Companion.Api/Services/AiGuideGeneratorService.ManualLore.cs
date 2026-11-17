using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Companion.Api.Data;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// Story &amp; Lore via the user's own AI subscription: ONE prompt for all four parts (ids L1-L4, the same assignments and building blocks as the API
/// research of a part), ONE pasted answer { "game", "steamAppId", "parts": [ { "part": "L1", "sections": [...] }, ... ] }. The answer is merged into the
/// tab by the same code as the API research (<see cref="MergeLoreTab"/>). Nothing in this file talks to an AI provider.
/// </summary>
public sealed partial class AiGuideGeneratorService
{
    /// <summary>The id of a lore part in the prompt and in the answer: "L1" for the first part.</summary>
    internal static string LorePartId(int index) => $"L{index + 1}";

    /// <summary>The name of the file a chat may hand back instead of one big code block.</summary>
    internal static string ManualLoreAnswerFileName(GameEntity game) => $"platinvault-{Slugify(game.Title)}-lore.json";

    /// <summary>The prompt for the chat that asks for the whole Story &amp; Lore tab (answer = one JSON object with all parts).</summary>
    public async Task<ManualPromptDto> BuildManualLorePromptAsync(GameEntity game, CancellationToken ct = default)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var context = await ManualContextAsync(game, language, ct);
        var prompt = ComposeManualLorePrompt(context, game, language);
        return new ManualPromptDto("lore", prompt, ManualFileName(game, "lore"), language, game.SteamAppId, game.Title);
    }

    /// <summary>
    /// The lore prompt: facts, writing style and building blocks of the API prompt of a part (<see cref="LoreStyleAndBlocks"/>), the research rules every
    /// content prompt has (platform, language, game wiki first), the output contract for a chat, and last the four parts with their assignments.
    /// </summary>
    internal static string ComposeManualLorePrompt(GameContext context, GameEntity game, string language)
    {
        var english = language == Lang.English;
        string T(string de, string en) => english ? en : de;

        var parts = LoreParts(language);
        var first = LorePartId(0);
        var last = LorePartId(parts.Length - 1);
        var doneRange = $"{first}–{last}";
        var openExample = parts.Length > 2 ? $"{LorePartId(2)}–{last}" : last;
        var title = game.Title.Replace("\"", "'");
        var appId = game.SteamAppId is > 0 ? game.SteamAppId.Value.ToString() : "null";
        var appIdLine = game.SteamAppId is > 0 ? T($"Steam-AppID {game.SteamAppId}", $"Steam AppID {game.SteamAppId}") : T("Steam-AppID unbekannt", "Steam AppID unknown");
        var answerFile = ManualLoreAnswerFileName(game);
        var partSkeletons = string.Join(", ", Enumerable.Range(0, parts.Length).Select(i => $"{{ \"part\": \"{LorePartId(i)}\", \"sections\": [...] }}"));
        var skeleton = $"{{ \"game\": \"{title}\", \"steamAppId\": {appId}, \"parts\": [ {partSkeletons} ] }}";

        var text = new StringBuilder();
        text.AppendLine(T(
            $"Du schreibst den kompletten Reiter „Story & Lore“ für „{game.Title}“ – für Spieler, die die Handlung und die Hintergründe wirklich verstehen wollen. Er besteht aus {parts.Length} Teilen ({first}–{last}), alle in diesem einen Chat. SPOILER sind ausdrücklich gewünscht (Wendungen, Enden, Schicksale der Figuren). Schreibe ALLE Texte auf Deutsch.",
            $"You are writing the complete \"Story & Lore\" tab for \"{game.Title}\" – for players who really want to understand the plot and the background. It consists of {parts.Length} parts ({first}–{last}), all of them in this one chat. SPOILERS are explicitly wanted (twists, endings, fates of the characters). Write ALL texts in English."));
        text.AppendLine();
        text.AppendLine(T("Bekannte Fakten aus Steam:", "Known facts from Steam:"));
        text.AppendLine(context.FactsBlock);
        text.AppendLine();
        text.AppendLine(T(
            "So arbeitest du: Alle Teile stehen am Ende unter „Teile“, jeder mit seinem Auftrag. Schreibe jeden Teil für sich nach seinem Auftrag und nach der folgenden Vorgabe – sie gilt für JEDEN Teil. Das Ergebnis eines Teils ist eine Liste von Abschnitten („sections“); alle Teile zusammen bilden EIN JSON-Objekt (siehe „Ausgabe in diesem Chat“). Die App setzt vor jeden Teil selbst eine Überschrift mit dem Titel des Teils – schreibe sie nicht noch einmal.",
            "How this works: all parts are listed at the end under \"Parts\", each with its assignment. Write each part on its own, following its assignment and the specification below – the specification applies to EVERY part. The result of a part is a list of sections (\"sections\"); all parts together form ONE JSON object (see \"Output in this chat\"). The app puts a heading with the title of the part in front of every part itself – do not write it again."));
        text.AppendLine();
        text.AppendLine(LoreStyleAndBlocks(language));
        text.AppendLine();
        text.AppendLine(ResearchRules(language));
        text.AppendLine();

        text.AppendLine(T(
            "Ausgabe in diesem Chat (das gilt zusätzlich und hat bei der Ausgabe Vorrang vor dem Satz „ohne Markdown-Codeblock“ oben):",
            "Output in this chat (this applies in addition and wins over the sentence \"without a Markdown code block\" above):"));
        text.AppendLine(T(
            $"- Gemeint ist genau dieses Spiel: „{game.Title}“, {appIdLine} – die Steam-Version auf dem PC. Prüfe vor dem Schreiben, dass du dieses Spiel meinst – nicht ein gleichnamiges Spiel, einen Vorgänger, Nachfolger, ein Remake oder eine Demo.",
            $"- The game is exactly this one: \"{game.Title}\", {appIdLine} – the Steam version on PC. Before writing, make sure you mean this game – not a game with the same name, a predecessor, a successor, a remake or a demo."));
        text.AppendLine(T(
            $"- Die Ausgabe ist EIN JSON-Objekt mit den Feldern „game“, „steamAppId“ und „parts“, in dieser Reihenfolge. „parts“ enthält GENAU einen Eintrag pro Teil in der Reihenfolge der Liste; jeder Eintrag hat das Feld „part“ (die Kennung aus der Liste, exakt, z. B. „{first}“) und das Feld „sections“ (die Abschnitte dieses Teils). Beginne jeden Teil in einer neuen Zeile. Im Objekt steht sonst nichts – kein Text, keine Kommentare, keine Platzhalter. So (verkürzt, schreibe die echten Inhalte): {skeleton}",
            $"- The output is ONE JSON object with the fields \"game\", \"steamAppId\" and \"parts\", in this order. \"parts\" contains EXACTLY one entry per part, in the order of the list; every entry has the field \"part\" (the id from the list, exactly, e.g. \"{first}\") and the field \"sections\" (the sections of this part). Start every part on a new line. Nothing else is in the object – no text, no comments, no placeholders. Like this (shortened, write the real content): {skeleton}"));
        text.AppendLine(T(
            $"- Kann dein Chat Code ausführen oder Dateien erzeugen: Liefere das Objekt als EINE Datei zum Herunterladen mit dem Namen „{answerFile}“. Baue sie Teil für Teil auf – hänge jeden fertigen Teil an die Datei an, damit kein einzelner Schritt alles auf einmal tragen muss – und prüfe am Ende, dass die ganze Datei gültiges JSON ist (z. B. indem du sie einliest). Schreibe den Inhalt dann nicht zusätzlich in den Chat; schreibe nur die Abschlusszeile.",
            $"- If your chat can run code or create files: deliver the object as ONE downloadable file named \"{answerFile}\". Build it part by part – append each finished part to the file, so that no single step has to hold everything at once – and at the end check that the whole file is valid JSON (e.g. by reading it back in). Do not write the content into the chat as well; write only the final line."));
        text.AppendLine(T(
            $"- NUR wenn dein Chat keine Datei erzeugen kann: Schreibe dasselbe Objekt in EINEN ```json-Codeblock in EINER Antwort, im Block nichts außer dem Objekt. Passt nicht alles hinein, höre nach einem vollständigen Teil auf (schneide nie mitten in einem Teil ab), schließe „parts“ und das Objekt und schreibe als allerletzte Zeile „NOCH OFFEN: {openExample}“ (mit den echten Kennungen der fehlenden Teile). Schreibe ich dann „weiter“, antworte mit einem NEUEN Objekt derselben Form („game“, „steamAppId“, „parts“) nur mit den offenen Teilen, nach denselben Regeln.",
            $"- ONLY if your chat cannot create a file: write the same object into ONE ```json code block in ONE answer, nothing but the object inside the block. If everything does not fit, stop after a complete part (never cut off in the middle of a part), close \"parts\" and the object and write as the very last line \"STILL OPEN: {openExample}\" (with the real ids of the missing parts). When I then write \"continue\", answer with a NEW object of the same shape (\"game\", \"steamAppId\", \"parts\") containing only the open parts, following the same rules."));
        text.AppendLine(T(
            $"- Wenn alle Teile fertig sind, schreibe als allerletzte Zeile: „ALLE TEILE FERTIG: {doneRange}“.",
            $"- When all parts are done, write as the very last line: \"ALL PARTS DONE: {doneRange}\"."));
        text.AppendLine(T(
            "- Schreibe echte Inhalte, keine Platzhalter wie \"...\" – die Strukturbeschreibung oben ist nur das Muster. Was du nicht belegen kannst: „Noch nicht dokumentiert (Stand <heutiges Datum>)“ – erfinde nichts.",
            "- Write real content, no placeholders like \"...\" – the structure description above is only the pattern. What you cannot back up: \"Not documented yet (as of <today's date>)\" – invent nothing."));
        text.AppendLine(T(
            "- Nutze die Websuche deines Chats, falls es eine gibt – für jeden Teil zuerst nach einem Wiki speziell zu diesem Spiel (Schritt 1 der Recherche-Regeln; gibt es keins, überspring ihn ohne Erwähnung); ohne Websuche kennzeichne die Quellenlage als dünn.",
            "- Use the web search of your chat if it has one - for every part first for a wiki dedicated to this game (step 1 of the research rules; skip it silently when there is none); without web search mark the source situation as thin."));
        text.AppendLine();

        text.AppendLine(T($"Teile ({parts.Length}), in dieser Reihenfolge:", $"Parts ({parts.Length}), in this order:"));
        for (var i = 0; i < parts.Length; i++)
        {
            text.AppendLine($"- {LorePartId(i)} | \"{parts[i].Title.Replace("\"", "'")}\"");
            text.AppendLine($"  {T("Auftrag", "Assignment")}:");
            foreach (var line in parts[i].Brief.Replace("\r\n", "\n").Split('\n'))
            {
                text.AppendLine(line.Length == 0 ? string.Empty : "  " + line);
            }
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>The result of reading a Story &amp; Lore answer: what the UI shows, plus the parts the tab is built from (null when there are errors).</summary>
    internal sealed record ManualLoreChecked(ManualLoreCheckDto Dto, IReadOnlyList<LorePartOutcome>? Parts);

    // A chat that answers in several pieces ("STILL OPEN") produces several objects; more than this is not a lore answer
    private const int MaxLoreObjects = 8;

    // How many top-level objects of the pasted text are looked at in total (lore or not); a real answer has a handful
    private const int MaxLoreScannedObjects = 64;

    // How many entries of "parts" (all objects together) are read; a real answer has four, a repeated part or two at most
    private const int MaxLoreEntries = 32;

    [GeneratedRegex(@"^\s*(?:L|Teil|Part)?[\s\-_]*0*(?<n>\d{1,3})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LorePartIdPattern();

    /// <summary>
    /// Checks a pasted Story &amp; Lore answer (structure, game, parts, sections, language) without touching the database. Unknown part ids and parts that
    /// are missing or unreadable are warnings (the readable parts are saved without them); no readable part at all, a different game, cut-off or invalid
    /// JSON and the pasted pattern of the prompt are errors. Several JSON objects in the text (a chat that continued after "STILL OPEN") are merged.
    /// </summary>
    internal ManualLoreChecked CheckManualLore(GameEntity game, string? text)
    {
        var errors = new List<ManualIssueDto>();
        var warnings = new List<ManualIssueDto>();
        ManualLoreChecked Failed() => new(new ManualLoreCheckDto(false, errors, warnings, null), null);

        var language = GuideLanguage(game);
        var loreParts = LoreParts(language);
        var roots = ReadLoreObjects(text, errors, warnings);
        if (errors.Count > 0 || roots.Count == 0)
        {
            return Failed();
        }

        // The right game? (every object: a continuation may come from another chat)
        foreach (var root in roots)
        {
            var gameErrors = new List<ManualIssueDto>();
            var gameWarnings = new List<ManualIssueDto>();
            ManualAnswers.CheckGame(root, game.SteamAppId, game.Title, gameErrors, gameWarnings);
            AddDistinct(errors, gameErrors);
            AddDistinct(warnings, gameWarnings);
        }
        if (roots[0]["format"]?.ToString() == GuideImport.FormatName)
        {
            errors.Add(ManualAnswers.Issue("wholeGuide", ServerTexts.Manual.WholeGuideFile));
        }
        if (roots[0]["parts"] is null)
        {
            errors.Add(roots[0]["sections"] is not null
                ? ManualAnswers.Issue("sectionsWithoutParts", ServerTexts.Manual.LoreSectionsWithoutParts)
                : ManualAnswers.Issue("noParts", ServerTexts.Manual.LoreNoParts));
        }
        if (errors.Count > 0)
        {
            return Failed();
        }

        var allowed = $"{LorePartId(0)}–{LorePartId(loreParts.Length - 1)}";
        var readable = new Dictionary<int, JsonArray>();
        var twice = new HashSet<int>();
        var sawPlaceholder = false;
        var sawEntries = false;
        // Each entry can add a warning: without a limit ~2 MB of "{}" entries were answered with ~100 MB of warnings
        var entries = roots.SelectMany(root => LoreEntries(root["parts"])).Take(MaxLoreEntries + 1).ToList();
        if (entries.Count > MaxLoreEntries)
        {
            entries.RemoveAt(MaxLoreEntries);
            warnings.Add(ManualAnswers.Issue("tooManyParts", ServerTexts.Manual.LoreTooManyEntries(MaxLoreEntries)));
        }
        foreach (var (idText, sectionsNode) in entries)
        {
            sawEntries = true;
            if (idText is null)
            {
                warnings.Add(ManualAnswers.Issue("partNoId", ServerTexts.Manual.LorePartNoId(allowed)));
                continue;
            }
            var match = LorePartIdPattern().Match(idText);
            var index = match.Success && int.TryParse(match.Groups["n"].Value, out var number) ? number - 1 : -1;
            if (index < 0 || index >= loreParts.Length)
            {
                sawPlaceholder |= ManualBatchReader.IsPlaceholder(idText);
                warnings.Add(ManualAnswers.Issue("unknownPart", ServerTexts.Manual.LoreUnknownPart(idText.Trim(), allowed)));
                continue;
            }

            var part = loreParts[index];
            var id = LorePartId(index);
            sawPlaceholder |= ManualBatchReader.CountPlaceholders(sectionsNode, 0) > 0;
            var partWarnings = new List<ManualIssueDto>();
            JsonArray? normalized = null;
            if (sectionsNode is JsonArray sections && sections.Count > 0)
            {
                ManualAnswers.ScanSections(sections, partWarnings);
                using (Lang.Use(language))
                {
                    using var doc = JsonDocument.Parse(sections.ToJsonString());
                    normalized = GuideSectionNormalizer.Normalize(doc.RootElement);
                }
            }
            // What the normalizer drops or repairs, said per part (the same note in four parts would otherwise be ambiguous)
            warnings.AddRange(partWarnings.Select(w => w with { Message = ServerTexts.Manual.LorePartPrefix(id, part.Title).ToString() + w.Message }));
            if (normalized is null || normalized.Count == 0)
            {
                warnings.Add(ManualAnswers.Issue("partUnreadable", ServerTexts.Manual.LorePartUnreadable(id, part.Title)));
                continue;
            }
            if (!readable.TryAdd(index, normalized))
            {
                readable[index] = normalized;
                if (twice.Add(index))
                {
                    warnings.Add(ManualAnswers.Issue("partTwice", ServerTexts.Manual.LorePartTwice(id, part.Title)));
                }
            }
        }

        if (readable.Count == 0)
        {
            // The pattern of the prompt pasted back (placeholders instead of content) is told apart from an answer that is just unusable
            errors.Add(sawEntries && sawPlaceholder
                ? ManualAnswers.Issue("promptPattern", ServerTexts.Manual.LorePatternPasted)
                : sawEntries
                    ? ManualAnswers.Issue("noReadableParts", ServerTexts.Manual.LoreNoReadableParts)
                    : ManualAnswers.Issue("noParts", ServerTexts.Manual.LoreNoParts));
            return Failed();
        }

        var present = readable.Keys.Order().ToList();
        var missing = Enumerable.Range(0, loreParts.Length).Where(i => !readable.ContainsKey(i)).ToList();
        if (missing.Count > 0)
        {
            warnings.Add(ManualAnswers.Issue("partsMissing", ServerTexts.Manual.LorePartsMissing(string.Join(", ", missing.Select(i => $"{LorePartId(i)} „{loreParts[i].Title}“")))));
        }

        // Language: the whole answer as one text; single foreign sentences get a ready fix prompt
        var combined = new JsonArray();
        foreach (var index in present)
        {
            foreach (var section in readable[index])
            {
                combined.Add(section?.DeepClone());
            }
        }
        var wrongLanguage = ManualAnswers.GuessLanguage(combined);
        if (wrongLanguage is not null && wrongLanguage != language)
        {
            warnings.Add(ManualAnswers.Issue("otherLanguage", ServerTexts.Manual.LoreOtherLanguage(language)));
        }
        string? fixPrompt = null;
        var foreign = ForeignLanguageCheck.FindSentences(combined, language);
        if (foreign.Count > 0)
        {
            var examples = string.Join(" · ", foreign.Take(3).Select(f => "„" + (f.Length > 120 ? f[..117].TrimEnd() + "..." : f) + "“"));
            warnings.Add(ManualAnswers.Issue("foreignSentences", ServerTexts.Manual.ForeignSentences(language, foreign.Count, examples), string.Join("\n", foreign), foreign.Take(3).ToList()));
            fixPrompt = ManualLoreFixPrompt(game, language, foreign);
        }

        var preview = new ManualLorePreviewDto(
            present.Select(i => new ManualLorePartPreviewDto(loreParts[i].Title, readable[i].Count)).ToList(),
            missing.Select(i => loreParts[i].Title).ToList());
        var outcomes = present.Select(i => new LorePartOutcome(loreParts[i], readable[i], null)).ToList();
        return new ManualLoreChecked(new ManualLoreCheckDto(true, [], warnings, preview, fixPrompt), outcomes);
    }

    /// <summary>Builds the Story &amp; Lore tab of a checked answer exactly like the API research does (parts in the fixed order, a heading before each).</summary>
    internal GuideTabEntity BuildManualLoreTab(GameEntity game, IReadOnlyList<LorePartOutcome> parts) =>
        MergeLoreTab(game.Id, parts, GuideLanguage(game));

    /// <summary>
    /// The JSON objects of the pasted text that carry lore parts, in text order. The first object decides whether the text can be read at all (the
    /// usual messages of <see cref="ManualAnswers.TryRead"/>, or "that is the pattern of the prompt"); later ones (a continuation after "STILL OPEN")
    /// are added when they can be read and mentioned when they cannot.
    /// </summary>
    private static List<JsonObject> ReadLoreObjects(string? text, List<ManualIssueDto> errors, List<ManualIssueDto> warnings)
    {
        var roots = new List<JsonObject>();
        if (!ManualAnswers.TryRead(text, errors, out var first) || first is null)
        {
            // The prompt's own pattern ("sections": [...]) is not even JSON: say what it is instead of "invalid JSON"
            if (errors.Any(e => e.Code is "invalidJson" or "cutOff") && SpanOf(text!) is { } span && ManualBatchReader.IsPatternSpan(span.Span))
            {
                errors.Clear();
                errors.Add(ManualAnswers.Issue("promptPattern", ServerTexts.Manual.LorePatternPasted));
            }
            return roots;
        }
        roots.Add(AsPartsRoot(first));

        var rest = text!.Substring(SpanOf(text)!.Value.End);
        var broken = 0;
        // Every object looked at counts toward a hard limit, also the ones that are neither lore nor broken lore ("{}" or a sentence in braces):
        // otherwise a text of a million tiny objects is taken apart one by one, each step copying the rest (quadratic time per request)
        var scanned = 1;
        while (roots.Count + broken < MaxLoreObjects && scanned < MaxLoreScannedObjects && SpanOf(rest) is { } next)
        {
            scanned++;
            rest = rest[next.End..];
            JsonObject? obj;
            try
            {
                obj = JsonNode.Parse(next.Span, documentOptions: ManualAnswers.Lenient) as JsonObject;
            }
            catch (JsonException)
            {
                obj = null;
            }
            if (obj is not null && (obj = AsPartsRoot(obj))["parts"] is not null)
            {
                roots.Add(obj);
            }
            else if (obj is null && next.Span.Contains("\"part\"", StringComparison.Ordinal))
            {
                broken++;
                warnings.Add(ManualAnswers.Issue("extraBlockBroken", ServerTexts.Manual.LoreExtraBlockBroken(roots.Count + broken)));
            }
        }
        return roots;
    }

    /// <summary>
    /// A chat that writes every part as an object of its own ({ "part": "L1", "sections": [...] }, one after the other, or as a bare list) gives no "parts"
    /// wrapper: such an object is taken as a one-part answer, so the parts of the text still add up.
    /// </summary>
    private static JsonObject AsPartsRoot(JsonObject obj)
    {
        if (obj["parts"] is not null || obj["part"] is null || obj["sections"] is null)
        {
            return obj;
        }
        return new JsonObject { ["game"] = obj["game"]?.DeepClone(), ["steamAppId"] = obj["steamAppId"]?.DeepClone(), ["parts"] = new JsonArray(obj.DeepClone()) };
    }

    /// <summary>The first JSON object of a text (its text and the index after its last character), or null when there is none.</summary>
    private static (string Span, int End)? SpanOf(string text)
    {
        var start = FindObjectStart(text);
        if (start < 0)
        {
            return null;
        }
        try
        {
            var span = ExtractJsonObject(text);
            return (span, start + span.Length);
        }
        catch (LocalizedException)
        {
            return null;
        }
    }

    /// <summary>The entries of "parts" as (id text, sections node): a list of { "part", "sections" } objects - or, tolerated, an object keyed by part id.</summary>
    private static IEnumerable<(string? Id, JsonNode? Sections)> LoreEntries(JsonNode? parts)
    {
        switch (parts)
        {
            case JsonArray array:
                foreach (var entry in array.OfType<JsonObject>())
                {
                    var id = (entry["part"] ?? entry["id"]) is JsonValue value ? (value.TryGetValue<string>(out var s) ? s : value.TryGetValue<int>(out var n) ? n.ToString() : null) : null;
                    yield return (string.IsNullOrWhiteSpace(id) ? null : id, entry["sections"]);
                }
                break;
            case JsonObject keyed:
                foreach (var (key, value) in keyed)
                {
                    yield return (key, value is JsonObject inner ? inner["sections"] : value);
                }
                break;
        }
    }

    private static void AddDistinct(List<ManualIssueDto> target, List<ManualIssueDto> add)
    {
        foreach (var issue in add)
        {
            if (!target.Any(t => t.Code == issue.Code && t.Message == issue.Message))
            {
                target.Add(issue);
            }
        }
    }

    /// <summary>
    /// The prompt that asks the chat to rewrite the sentences that are not in the guide language - exactly those (and any further one), by the language
    /// rule, without changing anything else - and to output the same JSON object again.
    /// </summary>
    internal static string ManualLoreFixPrompt(GameEntity game, string language, IReadOnlyList<string> sentences)
    {
        var english = language == Lang.English;
        var appId = game.SteamAppId is > 0 ? game.SteamAppId.Value.ToString() : "null";
        var list = string.Join("\n", sentences.Take(15).Select(x => "- " + x));
        var more = sentences.Count > 15 ? (english ? $"\n(+{sentences.Count - 15} more)" : $"\n(+{sentences.Count - 15} weitere)") : string.Empty;
        var loreParts = LoreParts(language);
        var ids = $"{LorePartId(0)}–{LorePartId(loreParts.Length - 1)}";
        return english
            ? $"""
              Your answer for "Story & Lore" of "{game.Title}" contains sentences that are not in English, for example:
              {list}{more}

              Rewrite EXACTLY these passages – and every other sentence that is not in English – in your own words in English. This rule applies:
              {LanguageRules(language)}
              Change nothing else: the same parts ({ids}) with the same ids, the same sections in the same order, the same itemIds, numbers and URLs.
              Then output the same JSON object again, complete (every part you wrote), in exactly one code block (```json … ```), starting with the fields "game", "steamAppId" ({appId}) and "parts".
              """
            : $"""
              Deine Antwort für „Story & Lore“ von „{game.Title}“ enthält Sätze, die nicht auf Deutsch sind, zum Beispiel:
              {list}{more}

              Schreibe GENAU diese Stellen – und jeden weiteren Satz, der nicht auf Deutsch ist – in eigenen Worten auf Deutsch neu. Dabei gilt:
              {LanguageRules(language)}
              Ändere sonst nichts: dieselben Teile ({ids}) mit denselben Kennungen, dieselben Abschnitte in derselben Reihenfolge, dieselben itemIds, Zahlen und URLs.
              Gib danach dasselbe JSON-Objekt vollständig noch einmal aus (jeden Teil, den du geschrieben hast), in genau einem Codeblock (```json … ```), beginnend mit den Feldern „game“, „steamAppId“ ({appId}) und „parts“.
              """;
    }
}
