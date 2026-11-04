using Companion.Api.Localization;

namespace Companion.Api.Services;

public sealed partial class AiGuideGeneratorService
{
    // The prompts exist once per guide language. The German ones are the original wording; the English ones say the same and
    // additionally state the output language and use English labels in the JSON the model has to fill, so a guide is never
    // half in one language. The JSON field names (type, variant, itemId ...) are the contract with the app and never change.

    // Rules shared by every research prompt: honesty over completeness, so brand-new games get gaps instead of inventions.
    private static string ResearchRules(string language, ResearchBlocks.Selection? blocks = null)
    {
        // The completeness bullet follows the chosen scope (without a choice: the long-standing "be exhaustive")
        var completeness = ResearchBlocks.CompletenessRule(blocks ?? ResearchBlocks.Selection.Empty, language);
        // Steam/PC only: part of every research prompt, so no prompt can forget it (see AiGuidePlatform.cs)
        var platform = PlatformRules(language);
        // Everything in the guide language, in own words (see AiGuideLanguage.cs)
        var languageRule = LanguageRules(language);
        var sideQuests = SideQuestRule(language);
        // Where to start researching, per guide language (hints only - the AI keeps searching on its own)
        var sourceHints = SourceHints(language);
        return language == Lang.English
        ? $"""
          {platform}

          {languageRule}

          Research rules:
          {sourceHints}
          - Invent NOTHING. If there is (as yet) no reliable source for a point – e.g. because the game has just been released –
            write explicitly "Not documented yet (as of <today's date>)" instead of guessing locations, values or names.
          - Take the current patch state into account; mark content that has changed through updates.
          {completeness}
          - For every entry state FROM WHEN it is available (chapter, area, level or story progress) and whether it is
            MISSABLE – the player wants to see: "I can already get this in this chapter, I have to watch out for this now."
          - Write like an experienced player: concrete practical tips, time savers, typical mistakes, orders.
          {sideQuests}
          - Answer EXCLUSIVELY with a valid JSON object, without a Markdown code block and without text before or after it.
          """
        : $"""
          {platform}

          {languageRule}

          Recherche-Regeln:
          {sourceHints}
          - Erfinde NICHTS. Wenn es zu einem Punkt (noch) keine verlässliche Quelle gibt – z. B. weil das Spiel gerade erst erschienen ist –,
            schreibe ausdrücklich „Noch nicht dokumentiert (Stand <heutiges Datum>)“ statt Orte, Werte oder Namen zu raten.
          - Berücksichtige den aktuellen Patch-Stand; markiere Inhalte, die sich durch Updates geändert haben.
          {completeness}
          - Gib bei jedem Eintrag an, AB WANN er erreichbar ist (Kapitel, Gebiet, Level oder Story-Fortschritt) und ob er
            VERPASSBAR ist – der Spieler will sehen: „Das kann ich in diesem Kapitel schon holen, darauf muss ich jetzt achten.“
          - Schreibe wie ein erfahrener Spieler: konkrete Praxis-Tipps, Zeitsparer, typische Fehler, Reihenfolgen.
          {sideQuests}
          - Antworte AUSSCHLIESSLICH mit einem validen JSON-Objekt, ohne Markdown-Codeblock und ohne Text davor oder danach.
          """;
    }

    internal static string BuildBlueprintPrompt(GameContext context, string? customFocus, List<string> existingChapters, int? budgetCount = null)
    {
        var language = context.Language;
        var english = language == Lang.English;
        var profiles = Profiles(language);
        var topics = Topics(language);

        var steamListNote = context.Achievements.Count > 0
            ? english
                ? "The app builds a chapter for the Steam achievements itself from the official Steam list – do not plan anything for it."
                : "Ein eigenes Kapitel für die Steam-Erfolge baut die App selbst aus der offiziellen Steam-Liste – plane dafür nichts ein."
            : english
                ? "The app has no official achievement list: plan a chapter \"All achievements / trophies\" under extraChapters."
                : "Die App hat keine offizielle Erfolgsliste: plane unter extraChapters ein Kapitel „Alle Erfolge / Trophäen“ ein.";

        var profileList = string.Join("\n", profiles.Select(p => english
            ? $"- {p.Id}: {p.Name} (e.g. {p.Examples}) – typical: {string.Join(", ", p.Required)}; often fitting: {string.Join(", ", p.Recommended)}"
            : $"- {p.Id}: {p.Name} (z. B. {p.Examples}) – typisch: {string.Join(", ", p.Required)}; oft passend: {string.Join(", ", p.Recommended)}"));
        var topicList = string.Join("\n", topics.Select(t => $"- {t.Id}: {t.Title} – {t.Brief}"));

        var existingNote = existingChapters.Count > 0
            ? english
                ? "The guide already has these chapters. For topics they already cover set \"covered\": true – " +
                  "they will then not be created again:\n" + string.Join("\n", existingChapters.Select(c => "- " + c))
                : "Der Guide hat bereits diese Kapitel. Setze bei Themen, die davon schon abgedeckt sind, \"covered\": true – " +
                  "sie werden dann nicht noch einmal angelegt:\n" + string.Join("\n", existingChapters.Select(c => "- " + c))
            : english ? "The guide is new." : "Der Guide ist neu.";

        var tagProfileNote = english
            ? context.TagProfiles.Count > 0
                ? "According to the Steam community tags these profiles are already set: " + string.Join(", ", context.TagProfiles) + " – only add more if they clearly fit."
                : "The Steam tags give no clear profile – decide based on your research."
            : context.TagProfiles.Count > 0
                ? "Laut Steam-Community-Tags sind diese Profile bereits gesetzt: " + string.Join(", ", context.TagProfiles) + " – ergänze nur, wenn weitere eindeutig passen."
                : "Aus den Steam-Tags ergibt sich kein eindeutiges Profil – entscheide anhand deiner Recherche.";

        var wish = string.IsNullOrWhiteSpace(customFocus)
            ? english ? "none in particular – a complete 100% guide" : "kein besonderer – vollständiger 100%-Guide"
            : customFocus;
        var wishesBlock = ResearchBlocks.BuildWishesBlock(context.Blocks, context.ResearchBase, ResearchBlocks.Scope.Blueprint, language);
        var rules = ResearchRules(language, context.Blocks);
        // The walkthrough chapter does not count against the budget (callers pass the count without it)
        var budgetNote = ChapterBudget.PromptNote(context.Blocks.Scope, language, budgetCount ?? existingChapters.Count);

        if (english)
        {
            return $$"""
                You are the editor-in-chief for 100% walkthroughs. Your task is ONLY the blueprint for the guide to "{{context.Title}}".
                Write ALL texts of your answer in English.

                Known facts from Steam (Steam genres are rough – rely on your research):
                {{context.FactsBlock}}

                Step 1 – Analysis (research on the internet): What kind of game is this (genre, structure: linear/open world/missions,
                playtime, multiplayer)? What belongs to "100%" here? What is missable? How good are the sources
                (established, thin, or newly released)?

                Step 2 – Kind of game: Assign the game to 1 to 3 profiles from this list ("profiles", ids only). The "typical" topics of the
                chosen profiles are only a hint what to check first – they become chapters only if the game really has them.
                {{tagProfileNote}}
                {{profileList}}

                Step 3 – Check EVERY topic of the topic list: Does a chapter of its own help the player with THIS game? Give every topic an entry in "topics"
                with "applicable" true or false – a topic without an entry gets no chapter.
                - Yes: "applicable": true, a game-specific title (e.g. "Best weapons & loadouts" for Call of Duty, "Cyberware & builds"
                  for Cyberpunk, "Production chains & layouts" for a factory game) and a precise research assignment ("focus", 2–4 sentences:
                  what exactly is collected, sensible table columns, what to look out for; name puzzles/riddles of the topic in the "focus" – they
                  are researched with location and complete solution).
                - No (the game does not have it, or only marginally – e.g. enemies or bosses in a building or factory game without combat): "applicable": false and a
                  short "reason". Never plan a chapter that would only explain that something is missing, and never take a topic only because it is typical for the genre.
                - Titles are in English only, without a translation in brackets.
                Side quests: if the game has side quests, the topic "quests" is applicable – their complete solutions belong there (the walkthrough only names them).
                Then at most 3 "extraChapters" for particularities of the game that are not part of any topic.
                {{budgetNote}}
                {{steamListNote}}
                {{existingNote}}
                Player's wish: {{wish}}

                {{wishesBlock}}

                Topic list (id: topic – content):
                {{topicList}}

                {{rules}}

                JSON structure:
                {
                  "subtitle": "concise English subtitle",
                  "estimatedHours": "e.g. approx. 80–120 hours for all Steam achievements",
                  "missablesInfo": "e.g. 3 missable achievements",
                  "difficultyInfo": "e.g. difficulty does not matter, 1 playthrough + chapter select",
                  "genreTags": "e.g. Action RPG, Open World",
                  "accentColor": "emerald | crystal | gold | crimson | purple",
                  "sourceSituation": "established | thin | new",
                  "overview": "2–4 sentences: what 100% means in this game and the most important strategy",
                  "profiles": ["rpg", "openworld"],
                  "topics": [
                    { "id": "weapons", "applicable": true, "priority": 1, "title": "Best weapons", "focus": "Research assignment ..." },
                    { "id": "economy", "applicable": false, "reason": "no economy in the game" },
                    { "id": "tactics", "applicable": false, "mergeInto": "weapons", "reason": "small, covered by the weapons chapter" }
                  ],
                  "extraChapters": [
                    { "title": "...", "icon": "mdi:...", "focus": "Research assignment ..." }
                  ]
                }
                """;
        }

        return $$"""
            Du bist Chef-Redakteur für 100%-Komplettlösungen. Deine Aufgabe ist NUR der Bauplan für den Guide zu „{{context.Title}}“.

            Bekannte Fakten aus Steam (Steam-Genres sind grob – verlass dich auf deine Recherche):
            {{context.FactsBlock}}

            Schritt 1 – Analyse (recherchiere im Internet): Was für ein Spiel ist das (Genre, Struktur: linear/Open World/Missionen,
            Spielzeit, Mehrspieler)? Was gehört hier zu „100%“? Was ist verpassbar? Wie gut ist die Quellenlage
            (etabliert, dünn, oder neu erschienen)?

            Schritt 2 – Spielart: Ordne das Spiel 1 bis 3 Profilen aus dieser Liste zu ("profiles", nur die ids). Die „typischen“ Themen der
            gewählten Profile sind nur ein Hinweis, was du zuerst prüfst – sie werden nur dann Kapitel, wenn das Spiel sie wirklich hat.
            {{tagProfileNote}}
            {{profileList}}

            Schritt 3 – Prüfe JEDES Thema der Themenliste: Hilft ein eigenes Kapitel dazu dem Spieler bei DIESEM Spiel? Gib jedem Thema einen Eintrag in "topics"
            mit "applicable" true oder false – ein Thema ohne Eintrag bekommt kein Kapitel.
            - Ja: "applicable": true, ein spielspezifischer Titel (z. B. „Beste Waffen & Loadouts“ bei Call of Duty, „Cyberware & Builds“
              bei Cyberpunk, „Produktionsketten & Layouts“ bei einem Fabrik-Spiel) und ein präziser Rechercheauftrag ("focus", 2–4 Sätze:
              was genau gesammelt wird, sinnvolle Tabellenspalten, worauf zu achten ist; nenne Rätsel des Themas im "focus" – sie werden mit Ort
              und vollständiger Lösung recherchiert).
            - Nein (gibt es im Spiel nicht oder nur am Rande – z. B. Gegner oder Bosse in einem Aufbau- oder Fabrikspiel ohne Kampf): "applicable": false und in
              "reason" kurz warum. Plane nie ein Kapitel, das nur erklären würde, dass es etwas nicht gibt, und nimm kein Thema nur, weil es zur Spielart typisch ist.
            - Titel stehen nur auf Deutsch, ohne Übersetzung in Klammern.
            Nebenquests: Hat das Spiel Nebenquests, ist das Thema "quests" zutreffend – ihre vollständigen Lösungen gehören dorthin (die Komplettlösung nennt sie nur).
            Danach höchstens 3 "extraChapters" für Besonderheiten des Spiels, die in keinem Thema stecken.
            {{budgetNote}}
            {{steamListNote}}
            {{existingNote}}
            Wunsch des Spielers: {{wish}}

            {{wishesBlock}}

            Themenliste (id: Thema – Inhalt):
            {{topicList}}

            {{rules}}

            JSON-Struktur:
            {
              "subtitle": "prägnanter deutscher Untertitel",
              "estimatedHours": "z. B. ca. 80–120 Stunden für alle Steam-Erfolge",
              "missablesInfo": "z. B. 3 verpassbare Erfolge",
              "difficultyInfo": "z. B. Schwierigkeit egal, 1 Durchgang + Kapitelauswahl",
              "genreTags": "z. B. Action-RPG, Open World",
              "accentColor": "emerald | crystal | gold | crimson | purple",
              "sourceSituation": "etabliert | duenn | neu",
              "overview": "2–4 Sätze: was 100% in diesem Spiel bedeutet und die wichtigste Strategie",
              "profiles": ["rpg", "openworld"],
              "topics": [
                { "id": "weapons", "applicable": true, "priority": 1, "title": "Beste Waffen", "focus": "Rechercheauftrag ..." },
                { "id": "economy", "applicable": false, "reason": "keine Wirtschaft im Spiel" },
                { "id": "tactics", "applicable": false, "mergeInto": "weapons", "reason": "klein, im Waffen-Kapitel enthalten" }
              ],
              "extraChapters": [
                { "title": "...", "icon": "mdi:...", "focus": "Rechercheauftrag ..." }
              ]
            }
            """;
    }

    internal static string BuildChapterPrompt(GameContext context, string chapterTitle, string focus, string sourceSituation, string? topicId = null)
    {
        var wishesBlock = ResearchBlocks.BuildWishesBlock(context.Blocks, context.ResearchBase, ResearchBlocks.Scope.Chapter, context.Language);
        var rules = WithLinkRules(ResearchRules(context.Language, context.Blocks), context, chapterTitle + " " + focus, GuideLinker.ChapterPromptCap);
        var structure = ChapterStructureText(context);
        var sectionsJson = ChapterSectionsJson(context.Language);
        var puzzleRules = PuzzleRules(context.Language);
        // Second line of defence: a chapter whose topic the game does not have is answered with "notApplicable" and dropped (not for roadmap/tips/walkthrough)
        var notApplicableRule = ChapterApplicability.CanBeDropped(topicId) ? ChapterApplicability.PromptRule(context.Title, context.Language) : string.Empty;

        if (context.Language == Lang.English)
        {
            return $$"""
                You are writing a chapter of a 100% walkthrough for "{{context.Title}}". Write ALL texts in English.

                Known facts from Steam:
                {{context.FactsBlock}}
                Source situation according to the preliminary analysis: {{sourceSituation}}

                Chapter: "{{chapterTitle}}"
                Research assignment: {{focus}}

                {{wishesBlock}}

                {{notApplicableRule}}

                {{structure}}

                {{puzzleRules}}

                {{rules}}

                JSON structure:
                {
                  "title": "{{chapterTitle}}",
                  "icon": "mdi:...",
                {{sectionsJson}}
                }
                """;
        }

        return $$"""
            Du schreibst ein Kapitel einer 100%-Komplettlösung für „{{context.Title}}“.

            Bekannte Fakten aus Steam:
            {{context.FactsBlock}}
            Quellenlage laut Vorab-Analyse: {{sourceSituation}}

            Kapitel: „{{chapterTitle}}“
            Rechercheauftrag: {{focus}}

            {{wishesBlock}}

            {{notApplicableRule}}

            {{structure}}

            {{puzzleRules}}

            {{rules}}

            JSON-Struktur:
            {
              "title": "{{chapterTitle}}",
              "icon": "mdi:...",
            {{sectionsJson}}
            }
            """;
    }

    /// <summary>
    /// The part of a chapter prompt that is the same for every chapter: content line, the mandatory structure, the table requirement and the allowed
    /// building blocks (incl. the puzzle block). Shared by the single-chapter prompt and the batch prompt.
    /// </summary>
    internal static string ChapterStructureText(GameContext context)
    {
        var contentLine = ResearchBlocks.ChapterContentLine(context.Blocks, context.Language);
        var tableLine = ResearchBlocks.ChapterTableLine(context.Blocks, context.Language);
        var puzzleBlock = PuzzleBlock(context.Language);
        var bossBlock = BossBlock(context.Language);
        var sourcesBlock = SourcesBlock(context.Language);
        var acquisition = AcquisitionRules(context.Language);
        var bossRules = BossRules(context.Language);

        if (context.Language == Lang.English)
        {
            return $$"""
                {{contentLine}}
                Structure (mandatory, in this order):
                1. An "alert" (variant "gold") "Key points first": the 3–5 things you absolutely have to know about this topic.
                2. An "alert" (variant "danger") with everything that is MISSABLE – or explicitly "Nothing missable".
                3. "cards_grid" with strategies/recommendations from practice (e.g. early / middle / endgame, best build, best order).
                {{tableLine}}
                Use the fitting building blocks:
                - "alert": important warnings (variant "danger" for missables, "gold" for expert tips, "crystal" for info, "emerald" for achievements)
                - "cards_grid": strategies, builds, tactics (columns 2 or 3; every card with bullets)
                - "checklist_table": everything tickable (collectibles, quests, weapons, bosses ...), choose columns to fit the topic.
                  Every row needs a unique, descriptive "itemId" in kebab-case (e.g. "weapon-mantis-blades"), "filterCategory"
                  must appear in "categories"; "checkable" is true if the player can do/collect something.
                {{puzzleBlock}}
                {{bossBlock}}
                {{sourcesBlock}}

                {{acquisition}}

                {{bossRules}}
                """;
        }

        return $$"""
            {{contentLine}}
            Aufbau (Pflicht, in dieser Reihenfolge):
            1. Ein "alert" (variant "gold") „Das Wichtigste vorab“: die 3–5 Dinge, die man zu diesem Thema unbedingt wissen muss.
            2. Ein "alert" (variant "danger") mit allem, was VERPASSBAR ist – oder ausdrücklich „Nichts verpassbar“.
            3. "cards_grid" mit Strategien/Empfehlungen aus der Praxis (z. B. früh / mittel / Endgame, bester Build, beste Reihenfolge).
            {{tableLine}}
            Nutze die passenden Bausteine:
            - "alert": wichtige Warnungen (variant "danger" für Verpassbares, "gold" für Profi-Tipps, "crystal" für Infos, "emerald" für Erfolge)
            - "cards_grid": Strategien, Builds, Taktiken (columns 2 oder 3; jede Karte mit bullets)
            - "checklist_table": alles Abhakbare (Sammelobjekte, Quests, Waffen, Bosse ...), Spalten passend zum Thema wählen.
              Jede Zeile braucht eine eindeutige, sprechende "itemId" in kebab-case (z. B. "waffe-mantis-blades"), "filterCategory"
              muss in "categories" vorkommen; "checkable" ist true, wenn der Spieler etwas erledigen/sammeln kann.
            {{puzzleBlock}}
            {{bossBlock}}
            {{sourcesBlock}}

            {{acquisition}}

            {{bossRules}}
            """;
    }

    /// <summary>The "sections" part of the chapter JSON pattern (two spaces indented, to sit inside the object); shared by the single-chapter and batch prompts.</summary>
    internal static string ChapterSectionsJson(string language) => language == Lang.English
        ? """
            "sections": [
              { "type": "alert", "variant": "danger", "title": "...", "htmlContent": "plain text" },
              { "type": "cards_grid", "columns": 2, "cards": [
                { "title": "...", "badgeText": "...", "badgeVariant": "s | a | b | ok", "introText": "...",
                  "bullets": [ { "icon": "mdi:check", "title": "...", "body": "..." } ] } ] },
              { "type": "checklist_table", "table": {
                "tableId": "short-id", "caption": "...", "checkable": true,
                "searchPlaceholder": "Search...", "categoryFilterLabel": "All categories", "categories": ["..."],
                "columns": [ { "key": "name", "label": "Name", "isRowHeader": true }, { "key": "location", "label": "Location" } ],
                "rows": [ { "itemId": "unique-id", "filterCategory": "...", "cells": { "name": "...", "location": "..." } } ] } },
              { "type": "puzzle", "title": "...", "htmlContent": "...", "puzzle": { "itemId": "unique-id", "location": "...", "requirements": "...",
                "missable": false, "hints": ["..."], "steps": ["..."], "source": "..." } },
              { "type": "sources", "sources": [ { "title": "...", "url": "https://..." } ] }
            ]
          """
        : """
            "sections": [
              { "type": "alert", "variant": "danger", "title": "...", "htmlContent": "reiner Text" },
              { "type": "cards_grid", "columns": 2, "cards": [
                { "title": "...", "badgeText": "...", "badgeVariant": "s | a | b | ok", "introText": "...",
                  "bullets": [ { "icon": "mdi:check", "title": "...", "body": "..." } ] } ] },
              { "type": "checklist_table", "table": {
                "tableId": "kurze-id", "caption": "...", "checkable": true,
                "searchPlaceholder": "Suchen...", "categoryFilterLabel": "Alle Kategorien", "categories": ["..."],
                "columns": [ { "key": "name", "label": "Name", "isRowHeader": true }, { "key": "ort", "label": "Fundort" } ],
                "rows": [ { "itemId": "eindeutige-id", "filterCategory": "...", "cells": { "name": "...", "ort": "..." } } ] } },
              { "type": "puzzle", "title": "...", "htmlContent": "...", "puzzle": { "itemId": "eindeutige-id", "location": "...", "requirements": "...",
                "missable": false, "hints": ["..."], "steps": ["..."], "source": "..." } },
              { "type": "sources", "sources": [ { "title": "...", "url": "https://..." } ] }
            ]
          """;

    internal static string BuildLorePrompt(GameContext context, LorePart part)
    {
        var rules = ResearchRules(context.Language);

        if (context.Language == Lang.English)
        {
            return $$"""
                You are writing one part of the "Story & Lore" tab for "{{context.Title}}" – for players who really want to understand the plot and
                the background. SPOILERS are explicitly wanted (twists, endings, fates of the characters).

                Known facts from Steam:
                {{context.FactsBlock}}

                Part: "{{part.Title}}"
                Assignment:
                {{part.Brief}}

                Writing style: English, simple and clear language, paragraphs of 2–5 sentences, no keyword lists inside running text.
                Names and terms follow the language rule below.

                Allowed building blocks (only these types):
                - "story": running text – { "type": "story", "title": "...", "paragraphs": ["Paragraph 1", "Paragraph 2"] }
                - "cards_grid": cards – { "type": "cards_grid", "columns": 2, "cards": [
                    { "title": "...", "badgeText": "...", "badgeVariant": "s | a | b | ok", "introText": "...",
                      "bullets": [ { "icon": "mdi:...", "title": "...", "body": "..." } ] } ] }
                - "checklist_table": pure reference table with "checkable": false – { "type": "checklist_table", "table": {
                    "tableId": "short-id", "caption": "...", "checkable": false, "searchPlaceholder": "Search...",
                    "categoryFilterLabel": "All categories", "categories": ["..."],
                    "columns": [ { "key": "name", "label": "...", "isRowHeader": true }, { "key": "info", "label": "..." } ],
                    "rows": [ { "itemId": "unique-id", "filterCategory": "...", "cells": { "name": "...", "info": "..." } } ] } }

                {{rules}}

                JSON structure:
                { "sections": [ ... ] }
                """;
        }

        return $$"""
            Du schreibst einen Teil des Reiters „Story & Lore“ für „{{context.Title}}“ – für Spieler, die die Handlung und die
            Hintergründe wirklich verstehen wollen. SPOILER sind ausdrücklich gewünscht (Wendungen, Enden, Schicksale der Figuren).

            Bekannte Fakten aus Steam:
            {{context.FactsBlock}}

            Teil: „{{part.Title}}“
            Auftrag:
            {{part.Brief}}

            Schreibstil: Deutsch, einfache und klare Sprache, Absätze mit 2–5 Sätzen, keine Stichwortlisten im Fließtext.
            Namen und Begriffe nach der Sprachregel unten.

            Erlaubte Bausteine (nur diese Typen):
            - "story": Fließtext – { "type": "story", "title": "...", "paragraphs": ["Absatz 1", "Absatz 2"] }
            - "cards_grid": Karten – { "type": "cards_grid", "columns": 2, "cards": [
                { "title": "...", "badgeText": "...", "badgeVariant": "s | a | b | ok", "introText": "...",
                  "bullets": [ { "icon": "mdi:...", "title": "...", "body": "..." } ] } ] }
            - "checklist_table": reine Nachschlagetabelle mit "checkable": false – { "type": "checklist_table", "table": {
                "tableId": "kurze-id", "caption": "...", "checkable": false, "searchPlaceholder": "Suchen...",
                "categoryFilterLabel": "Alle Kategorien", "categories": ["..."],
                "columns": [ { "key": "name", "label": "...", "isRowHeader": true }, { "key": "info", "label": "..." } ],
                "rows": [ { "itemId": "eindeutige-id", "filterCategory": "...", "cells": { "name": "...", "info": "..." } } ] } }

            {{rules}}

            JSON-Struktur:
            { "sections": [ ... ] }
            """;
    }

    internal static string BuildAchievementTipsPrompt(GameContext context)
    {
        var list = string.Join("\n", context.Achievements.Select(a => $"{a.ApiName} | {a.DisplayName} | {a.Description}"));
        var rules = ResearchRules(context.Language);

        if (context.Language == Lang.English)
        {
            return $$"""
                The official Steam achievement list for "{{context.Title}}" is available (API name | title | condition):
                {{list}}

                Research for EVERY achievement: category (Story, Collecting, Combat, Side content, Difficulty, Online, Miscellaneous),
                whether it is missable, and a concrete tip (where/when/how, from which chapter on, 1–3 sentences). Write the tips in English.
                Achievements without a condition in the list are SECRET: for them give the exact condition in "condition" (what has to be done).
                {{PuzzleTipRule(context.Language)}}
                {{AcquisitionTipRule(context.Language)}}

                {{rules}}

                JSON structure – the key is exactly the API name from the list:
                { "tips": { "API_NAME": { "category": "Story", "missable": false, "tip": "...", "condition": "only for secret achievements" } } }
                """;
        }

        return $$"""
            Für „{{context.Title}}“ liegt die offizielle Steam-Erfolgsliste vor (API-Name | Titel | Bedingung):
            {{list}}

            Recherchiere zu JEDEM Erfolg: Kategorie (Story, Sammeln, Kampf, Nebeninhalte, Schwierigkeit, Online, Sonstiges),
            ob er verpassbar ist, und einen konkreten Tipp (wo/wann/wie, ab welchem Kapitel, 1–3 Sätze).
            Erfolge ohne Bedingung in der Liste sind GEHEIM: gib für sie in "condition" die genaue Bedingung an (was man tun muss).
            {{PuzzleTipRule(context.Language)}}
            {{AcquisitionTipRule(context.Language)}}

            {{rules}}

            JSON-Struktur – Schlüssel ist exakt der API-Name aus der Liste:
            { "tips": { "API_NAME": { "category": "Story", "missable": false, "tip": "...", "condition": "nur bei geheimen Erfolgen" } } }
            """;
    }
}
