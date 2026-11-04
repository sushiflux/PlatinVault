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
                Every part gets a "title" (in English only, the chapter/area name of the English version of the game) and a "focus" (2–3 sentences: what happens
                there, the areas/locations IN THE ORDER YOU PASS THROUGH them, the bosses, the achievements of this part, the important items/missables/side
                content/puzzles and whether the part ends at a point of no return). Merge small parts rather than splitting finely.

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
            Jeder Teil bekommt einen "title" (nur Deutsch, der Kapitel- bzw. Gebietsname der deutschen Fassung des Spiels) und einen "focus" (2–3 Sätze: was dort passiert,
            die Gebiete/Orte IN DER REIHENFOLGE, in der man durchkommt, die Bosse, die Erfolge dieses Teils, die wichtigen Items/Verpassbares/Nebeninhalte/Rätsel und ob der Teil an
            einem Point of no Return endet). Fasse kleine Teile lieber zusammen, als fein zu zerlegen.

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

    /// <summary>
    /// The shared part of the walkthrough prompts: the print-guide structure of a part, the reading-flow rule, the building blocks (incl. overview, boss card,
    /// the typed hints), the boss and acquisition rules and the own-words rule.
    /// </summary>
    private static string WalkthroughBlocks(string language) => language == Lang.English
        ? $$"""
            {{WalkthroughStructure(language)}}
            {{WalkthroughReadingRules(language)}}
            Allowed building blocks (only these types):
            - "heading": { "type": "heading", "level": 1, "title": "Part or area name", "htmlContent": "one orientation sentence" } ("level" 1 = part, 2 = area)
            {{OverviewBlock(language)}}
            - "story" with "variant": "steps": the route as steps – { "type": "story", "variant": "steps", "paragraphs": ["Go to ... and ...", "Then ..."] }
              (one step per entry, no numbering of your own).
            - "checklist_table": the finds of an area (see "Structure" 3c), same shape as in the other chapters, with a unique "itemId" per row.
            - "alert" with a "kind": a hint EXACTLY at the spot where it becomes relevant – { "type": "alert", "kind": "must_have | missable | optional | tip | achievement | point_of_no_return | side_quests",
              "title": "short heading", "htmlContent": "plain text" }:
              • "must_have" (Important – take it along): an item, equipment, ability or companion that matters for the run or for 100 % – with the exact location (where and how you get it) and why.
              • "missable" (Missable from here): from this point something is lost (time-limited content) – what exactly, until when; recommend saving beforehand.
              • "optional" (Optional content available here): optional boss, extra area, collectible that becomes available at this spot – and how to start it.
              • "side_quests" (Side quests here) with "quests": the side quests that become available here, compact (name, from when, missable window, short reward).
              • "tip" (Tip): practical advice for this spot (shortcut, save point, what to prepare).
              • "achievement" (Achievement here) with "apiName": the exact Steam API name – what you have to do here to unlock it.
              • "point_of_no_return" (Point of no return) with "items": ["..."] – everything you have to do before you continue.
            {{BossBlock(language)}}
            {{PuzzleBlock(language)}}
            {{SourcesBlock(language)}}
            An item that has its own row in another chapter of this guide (weapons, collectibles, ...): name it in the hint and refer to the chapter ("see chapter ...") instead of repeating the table.
            Copyright: write in YOUR OWN WORDS. Never copy longer passages of a source word for word; facts (places, names, orders, codes) are fine.

            {{BossRules(language)}}

            {{AcquisitionRules(language)}}
            """
        : $$"""
            {{WalkthroughStructure(language)}}
            {{WalkthroughReadingRules(language)}}
            Erlaubte Bausteine (nur diese Typen):
            - "heading": { "type": "heading", "level": 1, "title": "Name des Teils oder Gebiets", "htmlContent": "ein Orientierungssatz" } ("level" 1 = Teil, 2 = Gebiet)
            {{OverviewBlock(language)}}
            - "story" mit "variant": "steps": die Route als Schritte – { "type": "story", "variant": "steps", "paragraphs": ["Geh zu ... und ...", "Danach ..."] }
              (ein Schritt pro Eintrag, keine eigene Nummerierung).
            - "checklist_table": die Funde eines Gebiets (siehe „Aufbau“ 3c), gleiche Form wie in den anderen Kapiteln, mit eindeutiger "itemId" je Zeile.
            - "alert" mit einer "kind": ein Hinweis GENAU an der Stelle, an der er wichtig wird – { "type": "alert", "kind": "must_have | missable | optional | tip | achievement | point_of_no_return | side_quests",
              "title": "kurze Überschrift", "htmlContent": "reiner Text" }:
              • "must_have" (Wichtig – unbedingt mitnehmen): ein Item, eine Ausrüstung, Fähigkeit oder ein Begleiter, der für den Durchlauf oder für 100 % wichtig ist – mit genauem Fundort (wo und wie man es bekommt) und warum.
              • "missable" (Verpassbar ab hier): ab diesem Punkt geht etwas verloren (zeitlich begrenzte Inhalte) – was genau, bis wann; empfiehl vorher zu speichern.
              • "optional" (Optional hier verfügbar): optionaler Boss, Zusatzgebiet, Sammelobjekt, das an dieser Stelle verfügbar wird – und wie man es startet.
              • "side_quests" (Nebenquests hier) mit "quests": die Nebenquests, die hier verfügbar werden, kompakt (Name, ab wann, Zeitfenster, kurze Belohnung).
              • "tip" (Tipp): praktischer Rat für diese Stelle (Abkürzung, Speicherpunkt, was man vorbereiten sollte).
              • "achievement" (Erfolg hier) mit "apiName": der exakte Steam-API-Name – was du hier tun musst, um ihn freizuschalten.
              • "point_of_no_return" (Point of no Return) mit "items": ["..."] – alles, was du vorher erledigen musst.
            {{BossBlock(language)}}
            {{PuzzleBlock(language)}}
            {{SourcesBlock(language)}}
            Ein Item, das in einem anderen Kapitel dieses Guides eine eigene Zeile hat (Waffen, Sammelobjekte, ...): nenne es im Hinweis und verweise auf das Kapitel („siehe Kapitel ...“), statt die Tabelle zu wiederholen.
            Urheberrecht: Schreibe in EIGENEN WORTEN. Übernimm nie längere Passagen einer Quelle wörtlich; Fakten (Orte, Namen, Reihenfolgen, Codes) sind in Ordnung.

            {{BossRules(language)}}

            {{AcquisitionRules(language)}}
            """;

    /// <summary>Step 2 of the walkthrough: ONE part of the outline.</summary>
    internal static string BuildWalkthroughPartPrompt(GameContext context, WalkthroughOutline outline, int index, IReadOnlyList<string> otherChapters)
    {
        var part = outline.Parts[index];
        var english = context.Language == Lang.English;
        var rules = WithLinkRules(ResearchRules(context.Language, context.Blocks), context, part.Title + " " + part.Focus, GuideLinker.PartPromptCap);
        var wishes = ResearchBlocks.BuildWishesBlock(context.Blocks, context.ResearchBase, ResearchBlocks.Scope.Chapter, context.Language);
        var detail = WalkthroughDetailLine(context.Blocks.Scope, context.Language);
        var list = string.Join("\n", outline.Parts.Select((p, i) => $"{(i == index ? "-> " : "   ")}{i + 1}. {p.Title}"));
        var sources = outline.Sources.Count > 0 ? string.Join("; ", outline.Sources) : english ? "(none named)" : "(keine genannt)";
        var others = otherChapters.Count > 0 ? string.Join("; ", otherChapters) : english ? "(none)" : "(keine)";
        var blocks = WalkthroughBlocks(context.Language);
        var puzzleRules = PuzzleRules(context.Language);
        var achievements = AchievementReference(context);

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
                Content: the course of the game in THIS part, in the order of the game – with the hints exactly where they become relevant, a boss card at every
                fight and the complete solutions of the puzzles of this part. Start with the "heading" (level 1) of this part, followed by its "overview".
                {{blocks}}

                {{achievements}}

                {{puzzleRules}}

                {{rules}}

                JSON structure (shortened):
                { "sections": [ { "type": "heading", "level": 1, "title": "{{part.Title}}", "htmlContent": "..." }, { "type": "overview", "title": "At a glance", ... },
                  { "type": "heading", "level": 2, "title": "...", "htmlContent": "..." }, { "type": "story", "variant": "steps", "paragraphs": ["..."] },
                  { "type": "boss", "title": "...", ... }, { "type": "alert", "kind": "point_of_no_return", "title": "...", "htmlContent": "...", "items": ["..."] },
                  { "type": "sources", "sources": [ { "title": "...", "url": "https://..." } ] } ] }
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
            Inhalt: der Spielverlauf in DIESEM Teil, in der Reihenfolge des Spiels – mit den Hinweisen genau dort, wo sie wichtig werden, einer Boss-Karte bei jedem
            Kampf und den vollständigen Lösungen der Rätsel dieses Teils. Beginne mit der "heading" (level 1) dieses Teils, danach sein "overview".
            {{blocks}}

            {{achievements}}

            {{puzzleRules}}

            {{rules}}

            JSON-Struktur (verkürzt):
            { "sections": [ { "type": "heading", "level": 1, "title": "{{part.Title}}", "htmlContent": "..." }, { "type": "overview", "title": "Auf einen Blick", ... },
              { "type": "heading", "level": 2, "title": "...", "htmlContent": "..." }, { "type": "story", "variant": "steps", "paragraphs": ["..."] },
              { "type": "boss", "title": "...", ... }, { "type": "alert", "kind": "point_of_no_return", "title": "...", "htmlContent": "...", "items": ["..."] },
              { "type": "sources", "sources": [ { "title": "...", "url": "https://..." } ] } ] }
            """;
    }

    /// <summary>
    /// The walkthrough in ONE answer, for a chat (the subscription flow): the same blocks and rules as a part, for the whole game, kept compact so it
    /// fits one answer. Used instead of the generic chapter prompt when the chapter is the walkthrough topic.
    /// </summary>
    internal static string BuildWalkthroughSinglePrompt(GameContext context, string chapterTitle, string focus, string sourceSituation)
    {
        var english = context.Language == Lang.English;
        var rules = WithLinkRules(ResearchRules(context.Language, context.Blocks), context, chapterTitle + " " + focus, GuideLinker.ChapterPromptCap);
        var wishes = ResearchBlocks.BuildWishesBlock(context.Blocks, context.ResearchBase, ResearchBlocks.Scope.Chapter, context.Language);
        var single = WalkthroughSingleInstructions(context.Language);
        var puzzleRules = PuzzleRules(context.Language);
        var achievements = AchievementReference(context);

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

                {{single}}

                {{achievements}}

                {{puzzleRules}}

                {{rules}}

                JSON structure (shortened):
                { "title": "{{chapterTitle}}", "icon": "mdi:map-legend", "sections": [ { "type": "heading", "level": 1, "title": "...", "htmlContent": "..." },
                  { "type": "overview", "title": "At a glance", ... }, { "type": "heading", "level": 2, "title": "...", "htmlContent": "..." },
                  { "type": "story", "variant": "steps", "paragraphs": ["..."] }, { "type": "boss", "title": "...", ... },
                  { "type": "alert", "kind": "missable", "title": "...", "htmlContent": "..." }, { "type": "sources", "sources": [ { "title": "...", "url": "https://..." } ] } ] }
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

            {{single}}

            {{achievements}}

            {{puzzleRules}}

            {{rules}}

            JSON-Struktur (verkürzt):
            { "title": "{{chapterTitle}}", "icon": "mdi:map-legend", "sections": [ { "type": "heading", "level": 1, "title": "...", "htmlContent": "..." },
              { "type": "overview", "title": "Auf einen Blick", ... }, { "type": "heading", "level": 2, "title": "...", "htmlContent": "..." },
              { "type": "story", "variant": "steps", "paragraphs": ["..."] }, { "type": "boss", "title": "...", ... },
              { "type": "alert", "kind": "missable", "title": "...", "htmlContent": "..." }, { "type": "sources", "sources": [ { "title": "...", "url": "https://..." } ] } ] }
            """;
    }

    /// <summary>
    /// The instructions of the single-answer walkthrough (reliable-source check, whole game compact, then the building blocks); shared by the one-chapter
    /// prompt and the walkthrough block of the batch prompt.
    /// </summary>
    internal static string WalkthroughSingleInstructions(string language)
    {
        var blocks = WalkthroughBlocks(language);
        return language == Lang.English
            ? $$"""
                First check whether there is a reliable, COMPLETE walkthrough source (large site, official guide, big wiki). If not: return "sections" with ONE
                "alert" (variant "danger") saying that no reliable walkthrough source exists (as of today's date) – never invent a walkthrough.
                Otherwise: the whole game in the order of the game, every story chapter/mission as one part with the structure below (part heading level 1,
                overview, areas with level 2, boss cards, typed hints exactly where they become relevant, the complete puzzle solutions). Keep it COMPACT enough
                to fit into ONE answer (main route, the important hints only, no filler).
                {{blocks}}
                """
            : $$"""
                Prüfe zuerst, ob es eine belastbare, VOLLSTÄNDIGE Quelle für eine Komplettlösung gibt (große Seite, offizieller Guide, großes Wiki). Wenn nicht: gib "sections" mit EINEM
                "alert" (variant "danger") zurück, der sagt, dass es (Stand heute) keine belastbare Quelle für eine Komplettlösung gibt – erfinde nie eine Komplettlösung.
                Sonst: das ganze Spiel in der Reihenfolge des Spiels, jedes Story-Kapitel bzw. jede Mission als ein Teil mit dem Aufbau unten (Teil-Überschrift level 1,
                Überblick, Gebiete mit level 2, Boss-Karten, typisierte Hinweise genau dort, wo sie wichtig werden, die vollständigen Rätsel-Lösungen). Halte es KOMPAKT
                genug für EINE Antwort (Hauptroute, nur die wichtigen Hinweise, kein Füllstoff).
                {{blocks}}
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

    internal static JsonArray ParseWalkthroughSections(string raw, NormalizeOptions? options = null)
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

            var sections = GuideSectionNormalizer.Normalize(sectionsEl, options);
            return sections.Count > 0 ? sections : throw new LocalizedException(ServerTexts.Ai.WalkthroughPartNoSections);
        }
    }

    /// <summary>The normalizer options of an AI answer for this game: its official API names (achievement references to others are dropped).</summary>
    internal static NormalizeOptions OptionsFor(GameContext context) =>
        new(context.Achievements.Count > 0 ? context.Achievements.Select(a => a.ApiName).ToHashSet(StringComparer.Ordinal) : null);

    // ---- Research ---------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Researches the walkthrough in parts: one outline call, then the parts in the order of the game (the calls share the research gate, so at most
    /// three AI calls run at the same time for the whole job), merged into ONE chapter in the order of the outline. A part that fails becomes a danger alert
    /// at its place so the other parts are kept (the progress line names the part being worked on, e.g. "Walkthrough – Chapter 3 (4/9)"); only when every part fails (or there is no reliable source) an exception is thrown.
    /// <paramref name="requireAllParts"/> (a repeated research of an existing walkthrough): any failed part throws, so the old walkthrough is kept whole.
    /// The sources of all calls (Claude's citations and search results, the pages the outline and the parts name) end up in ONE "sources" section at the end.
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
        CancellationToken ct,
        bool requireAllParts = false)
    {
        var language = context.Language;
        var maxParts = WalkthroughTab.MaxParts(context.Blocks.Scope);
        var options = OptionsFor(context);

        report?.Invoke(plan.Title);
        AiAnswer outlineAnswer;
        await gate.WaitAsync(ct);
        try
        {
            outlineAnswer = await CallContentAnswerAsync(run, run.Primary, BuildWalkthroughOutlinePrompt(context, plan.Focus, sourceSituation, maxParts), settings, language, plan.Title, ct);
        }
        finally
        {
            gate.Release();
        }
        var outline = ParseWalkthroughOutline(outlineAnswer.Text, maxParts);

        var finished = 0;
        var results = await Task.WhenAll(outline.Parts.Select(async (part, index) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                report?.Invoke($"{plan.Title} – {part.Title} ({Volatile.Read(ref finished) + 1}/{outline.Parts.Count})");
                var answer = await CallContentAnswerAsync(run, run.SpreadFor(index), BuildWalkthroughPartPrompt(context, outline, index, otherChapters), settings, language, part.Title, ct);
                return (Part: part, Sections: (JsonArray?)ParseWalkthroughSections(answer.Text, options), Error: (Exception?)null, Sources: answer.Sources);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Komplettlösung-Teil '{Part}' konnte nicht recherchiert werden.", part.Title);
                return (Part: part, Sections: (JsonArray?)null, Error: (Exception?)ex, Sources: AiSourceMeta.Empty);
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
        var failed = results.Count(r => r.Sections is null);
        if (requireAllParts && failed > 0)
        {
            var first = results.First(r => r.Error is not null).Error!;
            throw new LocalizedException(ServerTexts.Ai.RedoWalkthroughPartsFailed(failed, results.Length, AiProviderException.TextOf(first)), first);
        }

        var provider = new AiSourceMeta(
            outlineAnswer.Sources.Cited.Concat(results.SelectMany(r => r.Sources.Cited)).ToList(),
            outlineAnswer.Sources.Searched.Concat(results.SelectMany(r => r.Sources.Searched)).ToList());
        var merged = MergeWalkthroughParts(outline, results.Select(r => (r.Sections, r.Error)).ToList(), language, provider);

        return new GuideTabEntity
        {
            Id = Guid.NewGuid(),
            GameId = gameId,
            Slug = WalkthroughTab.Slug,
            Title = plan.Title,
            Icon = plan.Icon,
            SortOrder = sortOrder,
            GroupKey = GuideGroups.Start,
            TopicId = WalkthroughTab.TopicId,
            SectionsJson = merged.ToJsonString()
        };
    }

    /// <summary>
    /// The sections of the walkthrough chapter: the opening box, then every part in the order of the outline, then ONE "sources" section. A part that
    /// failed becomes a danger alert at its place (its own error text), so the parts that worked are kept. The "sources" sections of the parts are
    /// collected into the one at the end, together with the outline's sources and the provider's (cited first).
    /// </summary>
    internal static JsonArray MergeWalkthroughParts(WalkthroughOutline outline, IReadOnlyList<(JsonArray? Sections, Exception? Error)> results, string language, AiSourceMeta? provider = null)
    {
        var merged = new JsonArray { WalkthroughIntro(outline.Sources, language) };
        var named = outline.Sources.Select(GuideSources.FromText).OfType<GuideSourceDto>().ToList();
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
                if (section["type"]?.ToString() == "sources")
                {
                    named.AddRange(GuideSources.FromStored(section["sources"], trustOrigin: false));
                    continue;
                }
                merged.Add(section.DeepClone());
            }
        }

        var sources = GuideSources.Combine(provider, GuideSources.Distinct(named), GuideSources.MaxPerWalkthrough);
        if (sources.Count > 0)
        {
            merged.Add(SourcesSection(sources));
        }
        return merged;
    }

    /// <summary>A stored "sources" section (no cap: the caller decides how many).</summary>
    private static JsonObject SourcesSection(IReadOnlyList<GuideSourceDto> sources)
    {
        var list = new JsonArray();
        foreach (var source in sources)
        {
            list.Add(new JsonObject { ["title"] = source.Title, ["url"] = source.Url, ["origin"] = source.Origin });
        }
        return new JsonObject { ["type"] = "sources", ["sources"] = list };
    }

    /// <summary>
    /// The opening box of the walkthrough: how to read it (the kinds of hints, the boss cards, the puzzle buttons). Sources with a link are listed in the
    /// "sources" section at the end; a source the research named without a link is still mentioned here by name.
    /// </summary>
    internal static JsonObject WalkthroughIntro(IReadOnlyList<string> sources, string language)
    {
        var english = language == Lang.English;
        var how = english
            ? "The game in the order of the game: every part starts with \"At a glance\", then the areas in the order you pass through them. Hints stand exactly where they become relevant: Important (take it along), Missable from here, Optional content available, Tip, Achievement here and Point of no return. Boss cards stand at the fight. Puzzles show where they are and keep their solution hidden until you ask for it."
            : "Das Spiel in der Reihenfolge des Spiels: Jeder Teil beginnt mit „Auf einen Blick“, danach folgen die Gebiete in der Reihenfolge, in der du durchkommst. Hinweise stehen genau dort, wo sie wichtig werden: Wichtig (unbedingt mitnehmen), Verpassbar ab hier, Optional verfügbar, Tipp, Erfolg hier und Point of no Return. Boss-Karten stehen beim Kampf. Rätsel zeigen, wo sie sind, und halten ihre Lösung verborgen, bis du sie anforderst.";
        var withoutLink = sources.Where(s => GuideSources.FromText(s) is null).ToList();
        var text = withoutLink.Count > 0
            ? how + (english ? " Also used (written in our own words): " : " Außerdem genutzt (in eigenen Worten wiedergegeben): ") + string.Join("; ", withoutLink)
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
