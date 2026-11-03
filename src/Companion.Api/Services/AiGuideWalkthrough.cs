using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Localization;
using Companion.Api.Endpoints;
using Companion.Api.Settings;

namespace Companion.Api.Services;

/// <summary>
/// The walkthrough ("Komplettlösung") chapter: one per game, found by its fixed slug, always listed FIRST in the guide (before the roadmap) whatever its
/// sort order is. It only exists when the research found a reliable, complete source (the blueprint decides, see the topic "walkthrough"), it does not
/// count against the chapter budget of the research scope, and it is researched in parts (outline, then the parts of the outline in parallel).
/// </summary>
public static class WalkthroughTab
{
    public const string Slug = "walkthrough";
    public const string TopicId = "walkthrough";

    public static bool Is(GuideTabEntity tab) => string.Equals(tab.Slug, Slug, StringComparison.Ordinal);

    /// <summary>The tabs in guide order: the walkthrough first, the rest by sort order.</summary>
    public static IEnumerable<GuideTabEntity> InGuideOrder(IEnumerable<GuideTabEntity> tabs) =>
        tabs.OrderBy(t => Is(t) ? 0 : 1).ThenBy(t => t.SortOrder);

    /// <summary>The chapters that count against the budget of the scope: everything except the lore tab and the walkthrough.</summary>
    public static int BudgetCount(IEnumerable<GuideTabEntity> tabs) => tabs.Count(t => t.Slug != LoreTab.Slug && !Is(t));

    /// <summary>The most parts the outline may have: the scope decides how finely the game is split (compact = by act, exhaustive = by area).</summary>
    public static int MaxParts(string? scope) => ChapterBudget.Effective(scope) switch
    {
        "compact" => 6,
        "exhaustive" => 20,
        _ => 12
    };
}

public sealed partial class AiGuideGeneratorService
{
    /// <summary>One part of the walkthrough outline: a story chapter, mission or area, in the order of the game.</summary>
    internal sealed record WalkthroughPart(string Title, string Focus);

    /// <summary>The outline of a walkthrough: the sources the research found and the parts in game order.</summary>
    internal sealed record WalkthroughOutline(List<string> Sources, List<WalkthroughPart> Parts);

    // ---- Prompts ----------------------------------------------------------------------------------------------------------------

    /// <summary>Step 1 of the walkthrough: only the outline (and the check that a reliable source exists at all).</summary>
    internal static string BuildWalkthroughOutlinePrompt(GameContext context, string focus, string sourceSituation, int maxParts)
    {
        var rules = ResearchRules(context.Language, context.Blocks);
        var wishes = ResearchBlocks.BuildWishesBlock(context.Blocks, context.ResearchBase, ResearchBlocks.Scope.Chapter, context.Language);
        if (context.Language == Lang.English)
        {
            return $$"""
                You are planning the WALKTHROUGH of a 100% guide for "{{context.Title}}". Write ALL texts in English. Your task is ONLY the outline of the walkthrough.

                Known facts from Steam:
                {{context.FactsBlock}}
                Source situation according to the preliminary analysis: {{sourceSituation}}
                Assignment: {{focus}}

                {{wishes}}

                Step 1 – Sources (research on the internet): Is there a reliable, COMPLETE walkthrough for this game (a full walkthrough on a large site, an
                official guide, a big wiki walkthrough)? Name the sources you will rely on ("sources": site/guide name, URL if you have it). If there is no
                reliable complete source (new game, thin coverage): "reliable": false, "parts": [] and the reason in "reason" – never invent a walkthrough.

                Step 2 – Outline: split the game into at most {{maxParts}} parts IN THE ORDER OF THE GAME, by story chapters, missions or areas (whatever the game uses).
                Every part gets a "title" (in English only, the official chapter/area name) and a "focus" (2–3 sentences: what happens there, which areas and bosses,
                which important items/missables/side content/puzzles of this part the part has to cover). Merge small parts rather than splitting finely.

                {{rules}}

                JSON structure:
                {
                  "reliable": true,
                  "reason": "only if reliable is false",
                  "sources": ["Site or guide name – URL"],
                  "parts": [ { "title": "Chapter 1 – ...", "focus": "..." } ]
                }
                """;
        }

        return $$"""
            Du planst die KOMPLETTLÖSUNG eines 100%-Guides für „{{context.Title}}“. Schreibe ALLE Texte auf Deutsch. Deine Aufgabe ist NUR die Gliederung der Komplettlösung.

            Bekannte Fakten aus Steam:
            {{context.FactsBlock}}
            Quellenlage laut Vorab-Analyse: {{sourceSituation}}
            Auftrag: {{focus}}

            {{wishes}}

            Schritt 1 – Quellen (recherchiere im Internet): Gibt es zu diesem Spiel eine belastbare, VOLLSTÄNDIGE Komplettlösung (eine vollständige Lösung auf einer
            großen Seite, einen offiziellen Guide, eine große Wiki-Komplettlösung)? Nenne die Quellen, auf die du dich stützt ("sources": Name der Seite bzw. des Guides, URL falls
            bekannt). Gibt es keine belastbare, vollständige Quelle (neues Spiel, dünne Quellenlage): "reliable": false, "parts": [] und der Grund in "reason" – erfinde
            nie eine Komplettlösung.

            Schritt 2 – Gliederung: Teile das Spiel in höchstens {{maxParts}} Teile IN DER REIHENFOLGE DES SPIELS, nach Story-Kapiteln, Missionen oder Gebieten (so, wie das Spiel es einteilt).
            Jeder Teil bekommt einen "title" (nur Deutsch, der Kapitel- bzw. Gebietsname) und einen "focus" (2–3 Sätze: was dort passiert, welche Gebiete und Bosse, welche wichtigen
            Items/Verpassbares/Nebeninhalte/Rätsel dieses Teils er abdecken muss). Fasse kleine Teile lieber zusammen, als fein zu zerlegen.

            {{rules}}

            JSON-Struktur:
            {
              "reliable": true,
              "reason": "nur wenn reliable false ist",
              "sources": ["Name der Seite oder des Guides – URL"],
              "parts": [ { "title": "Kapitel 1 – ...", "focus": "..." } ]
            }
            """;
    }

    /// <summary>The line that sets the depth of a walkthrough part (the scope of the research settings decides).</summary>
    internal static string WalkthroughDetailLine(string? scope, string language) => (ChapterBudget.Effective(scope), language == Lang.English) switch
    {
        ("compact", true) => "Depth: compact – the main route in at most about 8 steps per part, only the hints that matter most.",
        ("compact", false) => "Tiefe: kompakt – der Hauptweg in höchstens etwa 8 Schritten je Teil, nur die wichtigsten Hinweise.",
        ("exhaustive", true) => "Depth: very detailed – every step of the route, every item and every side content of this part with exact locations and directions.",
        ("exhaustive", false) => "Tiefe: sehr ausführlich – jeder Schritt der Route, jedes Item und jeder Nebeninhalt dieses Teils mit genauen Fundorten und Wegbeschreibungen.",
        (_, true) => "Depth: detailed – the whole route of this part in clear steps, all important items, missables and side content with locations.",
        _ => "Tiefe: ausführlich – die ganze Route dieses Teils in klaren Schritten, alle wichtigen Items, Verpassbares und Nebeninhalte mit Fundorten."
    };

    /// <summary>The shared part of the walkthrough prompts: building blocks, the four kinds of hints, own-words rule.</summary>
    private static string WalkthroughBlocks(string language) => language == Lang.English
        ? $$"""
            Allowed building blocks (only these types), in the order of the game:
            - "heading": { "type": "heading", "title": "Sub-heading (area or mission)", "htmlContent": "optional one-line lead, e.g. recommended level" }
            - "story" with "variant": "steps": the route as numbered steps – { "type": "story", "variant": "steps", "paragraphs": ["Do this ...", "Then go ..."] }
              (one step per entry, no numbering of your own; concrete: where to go, what to do, what to talk to or pick up).
            - "alert" with a "kind": a hint EXACTLY at the spot where it becomes relevant – { "type": "alert", "kind": "must_have | missable | optional | tip",
              "title": "short heading", "htmlContent": "plain text" }:
              • "must_have" (Important – take it along): an item, equipment, ability or companion that matters for the run or for 100 % – with the exact location (where and how you get it) and why.
              • "missable" (Missable from here): from this point something is lost (point of no return, time-limited content) – what exactly, until when; recommend saving beforehand.
              • "optional" (Optional / side content available here): side quest, optional boss, collectible that becomes available at this spot – and how to start it.
              • "tip" (Tip): practical advice for this spot (short boss tactic, shortcut, save point, what to prepare).
            {{PuzzleBlock(language)}}
            An item that has its own row in another chapter of this guide (weapons, collectibles, ...): name it in the hint and refer to the chapter ("see chapter ...") instead of repeating the table.
            Copyright: write in YOUR OWN WORDS. Never copy longer passages of a source word for word; facts (places, names, orders, codes) are fine.
            """
        : $$"""
            Erlaubte Bausteine (nur diese Typen), in der Reihenfolge des Spiels:
            - "heading": { "type": "heading", "title": "Zwischenüberschrift (Gebiet oder Mission)", "htmlContent": "optional eine Zeile Einleitung, z. B. empfohlenes Level" }
            - "story" mit "variant": "steps": die Route als nummerierte Schritte – { "type": "story", "variant": "steps", "paragraphs": ["Tu dies ...", "Gehe dann ..."] }
              (ein Schritt pro Eintrag, keine eigene Nummerierung; konkret: wohin, was tun, mit wem sprechen, was aufheben).
            - "alert" mit einer "kind": ein Hinweis GENAU an der Stelle, an der er wichtig wird – { "type": "alert", "kind": "must_have | missable | optional | tip",
              "title": "kurze Überschrift", "htmlContent": "reiner Text" }:
              • "must_have" (Wichtig – unbedingt mitnehmen): ein Item, eine Ausrüstung, Fähigkeit oder ein Begleiter, der für den Durchlauf oder für 100 % wichtig ist – mit genauem Fundort (wo und wie man es bekommt) und warum.
              • "missable" (Verpassbar ab hier): ab diesem Punkt geht etwas verloren (Point of no Return, zeitlich begrenzte Inhalte) – was genau, bis wann; empfiehl vorher zu speichern.
              • "optional" (Optional / Nebeninhalt hier verfügbar): Nebenquest, optionaler Boss, Sammelobjekt, das an dieser Stelle verfügbar wird – und wie man es startet.
              • "tip" (Tipp): praktischer Rat für diese Stelle (kurze Boss-Taktik, Abkürzung, Speicherpunkt, was man vorbereiten sollte).
            {{PuzzleBlock(language)}}
            Ein Item, das in einem anderen Kapitel dieses Guides eine eigene Zeile hat (Waffen, Sammelobjekte, ...): nenne es im Hinweis und verweise auf das Kapitel („siehe Kapitel ...“), statt die Tabelle zu wiederholen.
            Urheberrecht: Schreibe in EIGENEN WORTEN. Übernimm nie längere Passagen einer Quelle wörtlich; Fakten (Orte, Namen, Reihenfolgen, Codes) sind in Ordnung.
            """;

    /// <summary>Step 2 of the walkthrough: ONE part of the outline.</summary>
    internal static string BuildWalkthroughPartPrompt(GameContext context, WalkthroughOutline outline, int index, IReadOnlyList<string> otherChapters)
    {
        var part = outline.Parts[index];
        var english = context.Language == Lang.English;
        var rules = ResearchRules(context.Language, context.Blocks);
        var wishes = ResearchBlocks.BuildWishesBlock(context.Blocks, context.ResearchBase, ResearchBlocks.Scope.Chapter, context.Language);
        var detail = WalkthroughDetailLine(context.Blocks.Scope, context.Language);
        var list = string.Join("\n", outline.Parts.Select((p, i) => $"{(i == index ? "-> " : "   ")}{i + 1}. {p.Title}"));
        var sources = outline.Sources.Count > 0 ? string.Join("; ", outline.Sources) : english ? "(none named)" : "(keine genannt)";
        var others = otherChapters.Count > 0 ? string.Join("; ", otherChapters) : english ? "(none)" : "(keine)";
        var blocks = WalkthroughBlocks(context.Language);
        var puzzleRules = PuzzleRules(context.Language);

        if (english)
        {
            return $$"""
                You are writing ONE PART of the walkthrough of a 100% guide for "{{context.Title}}". Write ALL texts in English.

                Known facts from Steam:
                {{context.FactsBlock}}

                Part {{index + 1}} of {{outline.Parts.Count}}: "{{part.Title}}"
                Assignment: {{part.Focus}}
                The whole walkthrough (for orientation – do NOT repeat what the other parts cover):
                {{list}}
                Sources to rely on: {{sources}}
                Other chapters of this guide: {{others}}

                {{wishes}}

                {{detail}}
                Content: the course of the game in THIS part, in the order of the game, as steps – with the hints exactly where they become relevant and
                the complete solutions of the puzzles of this part. Start with a "heading" for this part.
                {{blocks}}

                {{puzzleRules}}

                {{rules}}

                JSON structure:
                { "sections": [ { "type": "heading", "title": "{{part.Title}}" }, { "type": "story", "variant": "steps", "paragraphs": ["..."] },
                  { "type": "alert", "kind": "must_have", "title": "...", "htmlContent": "..." } ] }
                """;
        }

        return $$"""
            Du schreibst EINEN TEIL der Komplettlösung eines 100%-Guides für „{{context.Title}}“. Schreibe ALLE Texte auf Deutsch.

            Bekannte Fakten aus Steam:
            {{context.FactsBlock}}

            Teil {{index + 1}} von {{outline.Parts.Count}}: „{{part.Title}}“
            Auftrag: {{part.Focus}}
            Die ganze Komplettlösung (zur Orientierung – wiederhole NICHT, was die anderen Teile abdecken):
            {{list}}
            Quellen, auf die du dich stützt: {{sources}}
            Andere Kapitel dieses Guides: {{others}}

            {{wishes}}

            {{detail}}
            Inhalt: der Spielverlauf in DIESEM Teil, in der Reihenfolge des Spiels, als Schritte – mit den Hinweisen genau dort, wo sie wichtig werden, und den
            vollständigen Lösungen der Rätsel dieses Teils. Beginne mit einer "heading" für diesen Teil.
            {{blocks}}

            {{puzzleRules}}

            {{rules}}

            JSON-Struktur:
            { "sections": [ { "type": "heading", "title": "{{part.Title}}" }, { "type": "story", "variant": "steps", "paragraphs": ["..."] },
              { "type": "alert", "kind": "must_have", "title": "...", "htmlContent": "..." } ] }
            """;
    }

    /// <summary>
    /// The walkthrough in ONE answer, for a chat (the subscription flow): the same blocks and rules as a part, for the whole game, kept compact so it
    /// fits one answer. Used instead of the generic chapter prompt when the chapter is the walkthrough topic.
    /// </summary>
    internal static string BuildWalkthroughSinglePrompt(GameContext context, string chapterTitle, string focus, string sourceSituation)
    {
        var english = context.Language == Lang.English;
        var rules = ResearchRules(context.Language, context.Blocks);
        var wishes = ResearchBlocks.BuildWishesBlock(context.Blocks, context.ResearchBase, ResearchBlocks.Scope.Chapter, context.Language);
        var blocks = WalkthroughBlocks(context.Language);
        var puzzleRules = PuzzleRules(context.Language);

        if (english)
        {
            return $$"""
                You are writing the WALKTHROUGH chapter of a 100% guide for "{{context.Title}}". Write ALL texts in English.

                Known facts from Steam:
                {{context.FactsBlock}}
                Source situation according to the preliminary analysis: {{sourceSituation}}

                Chapter: "{{chapterTitle}}"
                Assignment: {{focus}}

                {{wishes}}

                First check whether there is a reliable, COMPLETE walkthrough source (large site, official guide, big wiki). If not: return "sections" with ONE
                "alert" (variant "danger") saying that no reliable walkthrough source exists (as of today's date) – never invent a walkthrough.
                Otherwise: the whole game in the order of the game, split by story chapters/missions/areas (one "heading" each), with the steps, the typed hints exactly where they
                become relevant and the complete puzzle solutions. Keep it COMPACT enough to fit into ONE answer (main route, the important hints only, no filler).
                {{blocks}}

                {{puzzleRules}}

                {{rules}}

                JSON structure:
                { "title": "{{chapterTitle}}", "icon": "mdi:map-legend", "sections": [ { "type": "heading", "title": "..." }, { "type": "story", "variant": "steps", "paragraphs": ["..."] },
                  { "type": "alert", "kind": "missable", "title": "...", "htmlContent": "..." } ] }
                """;
        }

        return $$"""
            Du schreibst das Kapitel KOMPLETTLÖSUNG eines 100%-Guides für „{{context.Title}}“. Schreibe ALLE Texte auf Deutsch.

            Bekannte Fakten aus Steam:
            {{context.FactsBlock}}
            Quellenlage laut Vorab-Analyse: {{sourceSituation}}

            Kapitel: „{{chapterTitle}}“
            Auftrag: {{focus}}

            {{wishes}}

            Prüfe zuerst, ob es eine belastbare, VOLLSTÄNDIGE Quelle für eine Komplettlösung gibt (große Seite, offizieller Guide, großes Wiki). Wenn nicht: gib "sections" mit EINEM
            "alert" (variant "danger") zurück, der sagt, dass es (Stand heute) keine belastbare Quelle für eine Komplettlösung gibt – erfinde nie eine Komplettlösung.
            Sonst: das ganze Spiel in der Reihenfolge des Spiels, gegliedert nach Story-Kapiteln/Missionen/Gebieten (je eine "heading"), mit den Schritten, den typisierten Hinweisen genau dort,
            wo sie wichtig werden, und den vollständigen Rätsel-Lösungen. Halte es KOMPAKT genug für EINE Antwort (Hauptroute, nur die wichtigen Hinweise, kein Füllstoff).
            {{blocks}}

            {{puzzleRules}}

            {{rules}}

            JSON-Struktur:
            { "title": "{{chapterTitle}}", "icon": "mdi:map-legend", "sections": [ { "type": "heading", "title": "..." }, { "type": "story", "variant": "steps", "paragraphs": ["..."] },
              { "type": "alert", "kind": "missable", "title": "...", "htmlContent": "..." } ] }
            """;
    }

    // ---- Reading the answers -------------------------------------------------------------------------------------------------------

    /// <summary>Reads the outline answer. Throws a localized error when the research found no reliable source (or no parts at all).</summary>
    internal static WalkthroughOutline ParseWalkthroughOutline(string raw, int maxParts)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(ExtractJsonObject(raw));
        }
        catch (JsonException)
        {
            throw new LocalizedException(ServerTexts.Ai.WalkthroughOutlineNotJson);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var reliable = !(root.TryGetProperty("reliable", out var r) && r.ValueKind == JsonValueKind.False);
            var parts = new List<WalkthroughPart>();
            if (root.TryGetProperty("parts", out var partsEl) && partsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in partsEl.EnumerateArray())
                {
                    var title = Text(part, "title", string.Empty);
                    if (title.Length > 0)
                    {
                        parts.Add(new WalkthroughPart(
                            ChapterTitles.Clean(System.Text.RegularExpressions.Regex.Replace(title, @"^\s*\d+\.\s*", string.Empty), Lang.Current),
                            Text(part, "focus", title)));
                    }
                }
            }

            if (!reliable || parts.Count == 0)
            {
                throw new LocalizedException(ServerTexts.Ai.WalkthroughNoSource(Text(root, "reason", string.Empty)));
            }

            var sources = new List<string>();
            if (root.TryGetProperty("sources", out var sourcesEl) && sourcesEl.ValueKind == JsonValueKind.Array)
            {
                sources.AddRange(sourcesEl.EnumerateArray()
                    .Select(s => s.ValueKind == JsonValueKind.String ? s.GetString()?.Trim() : null)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .Take(8));
            }
            return new WalkthroughOutline(sources, parts.Take(Math.Max(1, maxParts)).ToList());
        }
    }

    private static JsonArray ParseWalkthroughSections(string raw)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(ExtractJsonObject(raw));
        }
        catch (JsonException)
        {
            throw new LocalizedException(ServerTexts.Ai.WalkthroughPartNotJson);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("sections", out var sectionsEl) || sectionsEl.ValueKind != JsonValueKind.Array)
            {
                throw new LocalizedException(ServerTexts.Ai.WalkthroughPartNoSections);
            }

            var sections = GuideSectionNormalizer.Normalize(sectionsEl);
            return sections.Count > 0 ? sections : throw new LocalizedException(ServerTexts.Ai.WalkthroughPartNoSections);
        }
    }

    // ---- Research ---------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Researches the walkthrough in parts: one outline call, then the parts in the order of the game (the calls share the research gate, so at most
    /// three AI calls run at the same time for the whole job), merged into ONE chapter in the order of the outline. A part that fails becomes a danger alert
    /// at its place so the other parts are kept (the progress line names the part being worked on, e.g. "Walkthrough – Chapter 3 (4/9)"); only when every part fails (or there is no reliable source) an exception is thrown.
    /// </summary>
    private async Task<GuideTabEntity> ResearchWalkthroughAsync(
        Guid gameId,
        int sortOrder,
        ChapterPlan plan,
        GameContext context,
        string sourceSituation,
        IReadOnlyList<string> otherChapters,
        AiRun run,
        SettingsResponseDto settings,
        SemaphoreSlim gate,
        Action<string>? report,
        CancellationToken ct)
    {
        var language = context.Language;
        var maxParts = WalkthroughTab.MaxParts(context.Blocks.Scope);

        report?.Invoke(plan.Title);
        string outlineRaw;
        await gate.WaitAsync(ct);
        try
        {
            outlineRaw = await CallContentAsync(run, run.Primary, BuildWalkthroughOutlinePrompt(context, plan.Focus, sourceSituation, maxParts), settings, language, plan.Title, ct);
        }
        finally
        {
            gate.Release();
        }
        var outline = ParseWalkthroughOutline(outlineRaw, maxParts);

        var finished = 0;
        var results = await Task.WhenAll(outline.Parts.Select(async (part, index) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                report?.Invoke($"{plan.Title} – {part.Title} ({Volatile.Read(ref finished) + 1}/{outline.Parts.Count})");
                var raw = await CallContentAsync(run, run.SpreadFor(index), BuildWalkthroughPartPrompt(context, outline, index, otherChapters), settings, language, part.Title, ct);
                return (Part: part, Sections: (JsonArray?)ParseWalkthroughSections(raw), Error: (Exception?)null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Komplettlösung-Teil '{Part}' konnte nicht recherchiert werden.", part.Title);
                return (Part: part, Sections: (JsonArray?)null, Error: (Exception?)ex);
            }
            finally
            {
                Interlocked.Increment(ref finished);
                gate.Release();
            }
        }));

        if (results.All(r => r.Sections is null))
        {
            ExceptionDispatchInfo.Capture(results[0].Error!).Throw();
        }

        var merged = MergeWalkthroughParts(outline, results.Select(r => (r.Sections, r.Error)).ToList(), language);

        return new GuideTabEntity
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            Slug = WalkthroughTab.Slug,
            Title = plan.Title,
            Icon = plan.Icon,
            SortOrder = sortOrder,
            GroupKey = GuideGroups.Start,
            SectionsJson = merged.ToJsonString()
        };
    }

    /// <summary>
    /// The sections of the walkthrough chapter: the opening box, then every part in the order of the outline. A part that failed becomes a danger
    /// alert at its place (its own error text), so the parts that worked are kept.
    /// </summary>
    internal static JsonArray MergeWalkthroughParts(WalkthroughOutline outline, IReadOnlyList<(JsonArray? Sections, Exception? Error)> results, string language)
    {
        var merged = new JsonArray { WalkthroughIntro(outline.Sources, language) };
        for (var i = 0; i < outline.Parts.Count && i < results.Count; i++)
        {
            var (sections, error) = results[i];
            if (sections is null)
            {
                merged.Add(new JsonObject
                {
                    ["type"] = "alert",
                    ["variant"] = "danger",
                    ["icon"] = "mdi:alert-circle-outline",
                    ["title"] = language == Lang.English ? $"Part could not be researched: {outline.Parts[i].Title}" : $"Teil konnte nicht recherchiert werden: {outline.Parts[i].Title}",
                    ["htmlContent"] = error is null ? string.Empty : AiProviderException.TextOf(error).Resolve(language)
                });
                continue;
            }
            foreach (var section in sections.OfType<JsonObject>())
            {
                merged.Add(section.DeepClone());
            }
        }
        return merged;
    }

    /// <summary>The opening box of the walkthrough: how to read it (the four kinds of hints, the puzzle buttons) and the sources it relies on.</summary>
    internal static JsonObject WalkthroughIntro(IReadOnlyList<string> sources, string language)
    {
        var english = language == Lang.English;
        var how = english
            ? "The game in the order of the game, split by chapters and areas. Hints stand exactly where they become relevant: Important (take it along), Missable from here, Optional content available and Tip. Puzzles show where they are and keep their solution hidden until you ask for it."
            : "Das Spiel in der Reihenfolge des Spiels, gegliedert nach Kapiteln und Gebieten. Hinweise stehen genau dort, wo sie wichtig werden: Wichtig (unbedingt mitnehmen), Verpassbar ab hier, Optional verfügbar und Tipp. Rätsel zeigen, wo sie sind, und halten ihre Lösung verborgen, bis du sie anforderst.";
        var text = sources.Count > 0
            ? how + (english ? " Sources (written in our own words): " : " Quellen (in eigenen Worten wiedergegeben): ") + string.Join("; ", sources)
            : how;
        return new JsonObject
        {
            ["type"] = "alert",
            ["variant"] = "crystal",
            ["icon"] = "mdi:map-legend",
            ["title"] = english ? "How to use this walkthrough" : "So nutzt du die Komplettlösung",
            ["htmlContent"] = text
        };
    }
}
