using System.Text.Json.Nodes;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Services;

/// <summary>What one trophy-list research asks for (D10: the facts of a game without IGDB are researched in the same call).</summary>
public sealed record TrophyResearchInclude(bool Trophies, bool Facts)
{
    public bool Any => Trophies || Facts;
}

/// <summary>
/// Games added by hand (owner decision 2026-11-19): their research context (platform, stored facts, the researched trophy list) and the research of the
/// trophy / achievement list itself - for the AI job (<c>POST /api/games/{slug}/ai-trophies</c>) and for the player's own AI subscription
/// (<c>ManualTrophyEndpoints</c>). Both ways use the SAME prompt (the subscription adds the chat output contract) and the SAME reader (<see cref="TrophyList"/>).
/// </summary>
public sealed partial class AiGuideGeneratorService
{
    /// <summary>The most entries of the old list the keep list of a re-research names (a bigger list keeps the rest by name matching).</summary>
    internal const int MaxKeepTrophies = 400;

    /// <summary>The trophies of a game added by hand, in list order: from the database when the service has one, else what is loaded on the game.</summary>
    internal async Task<List<GameTrophyEntity>> TrophiesOfAsync(GameEntity game, CancellationToken ct)
    {
        if (db is not null)
        {
            return await db.GameTrophies.AsNoTracking()
                .Where(t => t.GameId == game.Id)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.TrophyKey)
                .ToListAsync(ct);
        }
        return game.Trophies.OrderBy(t => t.SortOrder).ThenBy(t => t.TrophyKey, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The research context of a game added by hand, from what the app stored about it (pure, so the golden file can pin it): platform and version, the
    /// IGDB id, release, developer, publisher, genres, hours to 100 %, short description and the researched trophy list. The trophies take the place of
    /// Steam's achievement list (id | name | condition), so chapters reference them exactly like Steam API names.
    /// </summary>
    internal static GameContext BuildManualContext(GameEntity game, IReadOnlyList<GameTrophyEntity> trophies, ResearchBlocks.Selection blocks, string language, DateTime today)
    {
        var english = language == Lang.English;
        var platform = GamePlatformContext.Of(game, trophies.Count)
            ?? throw new InvalidOperationException("BuildManualContext is only for games added by hand.");
        var lines = new List<string>
        {
            english ? $"Today's date: {today:yyyy-MM-dd}" : $"Heutiges Datum: {today:yyyy-MM-dd}",
            english ? $"Platform: {platform.Name}" : $"Plattform: {platform.Name}",
        };
        if (game.IgdbId is > 0)
        {
            lines.Add(english ? $"IGDB id: {game.IgdbId}" : $"IGDB-ID: {game.IgdbId}");
        }
        if (!string.IsNullOrWhiteSpace(game.ReleaseDate))
        {
            lines.Add($"Release: {game.ReleaseDate.Trim()}");
        }
        if (!string.IsNullOrWhiteSpace(game.Developer)) lines.Add(english ? $"Developers: {game.Developer.Trim()}" : $"Entwickler: {game.Developer.Trim()}");
        if (!string.IsNullOrWhiteSpace(game.Publisher)) lines.Add(english ? $"Publisher: {game.Publisher.Trim()}" : $"Publisher: {game.Publisher.Trim()}");
        var genres = game.SteamGenres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList();
        if (genres.Count > 0)
        {
            lines.Add(english ? $"Genres: {string.Join(", ", genres)}" : $"Genres: {string.Join(", ", genres)}");
        }
        if (game.CompletionHours is { } hours and > 0)
        {
            lines.Add(english
                ? $"Average playtime to 100 %: approx. {FormatHours(hours, language)} h"
                : $"Durchschnittliche Spielzeit bis 100 %: ca. {FormatHours(hours, language)} h");
        }
        if (!string.IsNullOrWhiteSpace(game.Summary))
        {
            var summary = game.Summary.Trim();
            summary = summary.Length > 600 ? summary[..597].TrimEnd() + "..." : summary;
            lines.Add(english ? $"Short description: {summary}" : $"Kurzbeschreibung: {summary}");
        }
        lines.Add(TrophyFactLine(platform, trophies, language));

        // The trophies stand where Steam's achievement list stands: id | name | condition (PlayStation with the grade in front)
        var achievements = trophies
            .Select(t => (t.TrophyKey, t.Name, GradePrefix(t.Grade, language) + t.Description))
            .ToList();

        var genre = genres.Count > 0 ? string.Join(", ", genres.Take(3)) : UnknownGenre(language);
        var researchBase = ResearchBlocks.FillBase(language, game.Title, genre, ResearchPlatformText(platform));
        return new GameContext(game.Title, string.Join("\n", lines), achievements, DetectProfilesFromGenres(genres), blocks, researchBase, language, Platform: platform);
    }

    /// <summary>The fact line about the trophy list: what the app has (with the count per grade), that it is not researched yet, or that there is none.</summary>
    private static string TrophyFactLine(GamePlatformContext platform, IReadOnlyList<GameTrophyEntity> trophies, string language)
    {
        var english = language == Lang.English;
        if (platform.IsNintendo)
        {
            return english
                ? "Trophies/achievements: Nintendo Switch has none."
                : "Trophäen/Erfolge: gibt es auf der Nintendo Switch nicht.";
        }
        if (trophies.Count == 0)
        {
            return platform.IsPlayStation
                ? english
                    ? $"Trophy list of the {platform.Name} version: not researched in the app yet – name no trophy you cannot back up."
                    : $"Trophäenliste der {platform.Name}-Version: in der App noch nicht recherchiert – nenne keine Trophäe, die du nicht belegen kannst."
                : english
                    ? $"{platform.Name} achievement list: not researched in the app yet – name no achievement you cannot back up."
                    : $"{platform.Name}-Erfolgsliste: in der App noch nicht recherchiert – nenne keinen Erfolg, den du nicht belegen kannst.";
        }
        if (platform.IsPlayStation)
        {
            var grades = TrophyTiers.Grades
                .Select(g => (Grade: g, Count: trophies.Count(t => t.Grade == g)))
                .Where(x => x.Count > 0)
                .Select(x => $"{x.Count} {GradeName(x.Grade, language)}");
            return english
                ? $"Official trophies of the {platform.Name} version: {trophies.Count} ({string.Join(", ", grades)}; the app has the list)"
                : $"Offizielle Trophäen der {platform.Name}-Version: {trophies.Count} ({string.Join(", ", grades)}; Liste liegt der App vor)";
        }
        var points = trophies.Where(t => t.Points is > 0).Sum(t => t.Points!.Value);
        var pointsText = points > 0 ? platform.IsXbox ? $", {points} Gamerscore" : $", {points} {(english ? "points" : "Punkte")}" : string.Empty;
        return english
            ? $"Official {platform.Name} achievements: {trophies.Count}{pointsText} (the app has the list)"
            : $"Offizielle {platform.Name}-Erfolge: {trophies.Count}{pointsText} (Liste liegt der App vor)";
    }

    private static string GradePrefix(string? grade, string language) =>
        TrophyTiers.NormalizeGrade(grade) is { } g ? $"[{GradeName(g, language)}] " : string.Empty;

    private static string GradeName(string grade, string language) => (grade, language == Lang.English) switch
    {
        (TrophyTiers.Platinum, true) => "Platinum",
        (TrophyTiers.Platinum, false) => "Platin",
        (TrophyTiers.Gold, _) => "Gold",
        (TrophyTiers.Silver, true) => "Silver",
        (TrophyTiers.Silver, false) => "Silber",
        (_, _) => "Bronze",
    };

    /// <summary>The default of what a research covers: the list when the platform has one; the facts when they do not come from IGDB and are missing.</summary>
    public static TrophyResearchInclude DefaultInclude(GameEntity game, TrophyResearchIncludeDto? asked)
    {
        var trophies = GamePlatforms.HasTrophies(game.Platform) && (asked?.Trophies ?? true);
        // Facts from IGDB are never overwritten by the AI (D10); otherwise: when asked for, or by default when they are missing
        var facts = game.FactsSource != "igdb" && (asked?.Facts ?? FactsMissing(game));
        return new TrophyResearchInclude(trophies, facts);
    }

    /// <summary>A game added by hand without facts: nothing researched or entered yet (no release, developer, publisher or description).</summary>
    public static bool FactsMissing(GameEntity game) =>
        game.FactsSource is null
        || (string.IsNullOrWhiteSpace(game.ReleaseDate) && string.IsNullOrWhiteSpace(game.Developer) && string.IsNullOrWhiteSpace(game.Publisher) && string.IsNullOrWhiteSpace(game.Summary));

    /// <summary>The expectations of the reader for an answer about this game.</summary>
    internal static TrophyListExpectations ExpectationsFor(GameEntity game, TrophyResearchInclude include, bool strictPlatinum) =>
        new(game.Platform, game.PlatformVariant, game.Title, include.Trophies, include.Facts, strictPlatinum);

    /// <summary>
    /// The prompt that researches the official trophy / achievement list of THIS platform version (and, D10, the facts of a game without IGDB). Pure.
    /// <paramref name="existing"/> = the list the app has today: its ids are named as the keep list, so a re-research keeps the ticks, pins and notes.
    /// </summary>
    internal static string BuildTrophyListPrompt(GameContext context, GameEntity game, TrophyResearchInclude include, IReadOnlyList<GameTrophyEntity> existing)
    {
        var language = context.Language;
        var english = language == Lang.English;
        var p = context.Platform ?? throw new InvalidOperationException("The trophy list is only researched for games added by hand.");
        var identity = ManualIdentity(game, language);
        var platformRule = PlatformRules(language, p);
        var languageRule = LanguageRules(language, p);
        var sourceHints = SourceHints(language, p);
        var echo = TrophyList.ExpectedPlatformValue(p.Platform, p.Variant);
        var title = game.Title.Replace("\"", "'");

        var task = new List<string>();
        if (include.Trophies)
        {
            task.Add(TrophyTask(p, language));
            task.Add(TrophyCrossCheck(p, language));
        }
        if (include.Facts)
        {
            task.Add(english
                ? """
                  - The game facts ("facts"): first release date (YYYY-MM-DD; only the year if no day is documented), developers, publishers, genres (at most 8,
                    in English), a short description in English (2–3 sentences, own words, no spoilers, no advertising) and the average hours to 100 % (only
                    with a source, else leave it out).
                  """
                : """
                  - Die Spiel-Fakten („facts“): erste Veröffentlichung (JJJJ-MM-TT; nur das Jahr, wenn kein Tag belegt ist), Entwickler, Publisher, Genres (höchstens 8,
                    auf Englisch wie in den großen Datenbanken), eine kurze Beschreibung auf Deutsch (2–3 Sätze, eigene Worte, keine Spoiler, keine Werbung) und die
                    durchschnittlichen Stunden bis 100 % (nur mit Quelle, sonst weglassen).
                  """);
        }

        if (include.Trophies)
        {
            task.Add(english
                ? """
                  - If you find NO reliable, complete list of exactly this version, answer only { "documented": false, "reason": "<why, one sentence>" } – an
                    incomplete or guessed list is worse than none. Never invent an entry, a name or a condition.
                  """
                : """
                  - Findest du KEINE verlässliche, vollständige Liste genau dieser Version, antworte nur mit { "documented": false, "reason": "<warum, ein Satz>" } –
                    eine unvollständige oder geratene Liste ist schlechter als keine. Erfinde nie einen Eintrag, einen Namen oder eine Bedingung.
                  """);
            if (existing.Count > 0)
            {
                task.Add(KeepList(existing, p, language));
            }
        }
        var taskText = string.Join("\n", task.Select(t => t.TrimEnd('\n', '\r')));
        var schema = TrophySchema(p, include, title, echo, language);
        // The platform rule says "names come ONLY from the list the app has" - true for guide chapters, but here that list is being researched
        var listOverride = include.Trophies ? ListResearchNote(p, language) : string.Empty;

        return english
            ? $$"""
              You research for the app PlatinVault: "{{game.Title}}" – {{identity}}.

              {{FactsHeader(p, language)}}
              {{context.FactsBlock}}

              Task:
              {{taskText}}

              {{platformRule}}{{listOverride}}

              {{languageRule}}

              Research rules:
              {{sourceHints}}
              - Invent NOTHING. What no reliable source states is left out (fields without a value are omitted, never guessed).
              - Name every page you really used under "sources" (only real http(s) addresses, never an invented one).
              - Answer EXCLUSIVELY with a valid JSON object, without a Markdown code block and without text before or after it.

              Answer format:
              {{schema}}
              """
            : $$"""
              Du recherchierst für die App PlatinVault: „{{game.Title}}“ – {{identity}}.

              {{FactsHeader(p, language)}}
              {{context.FactsBlock}}

              Aufgabe:
              {{taskText}}

              {{platformRule}}{{listOverride}}

              {{languageRule}}

              Recherche-Regeln:
              {{sourceHints}}
              - Erfinde NICHTS. Was keine verlässliche Quelle belegt, lässt du weg (Felder ohne Wert fehlen, nie geraten).
              - Nenne jede Seite, die du wirklich benutzt hast, unter „sources“ (nur echte http(s)-Adressen, nie eine erfundene).
              - Antworte AUSSCHLIESSLICH mit einem validen JSON-Objekt, ohne Markdown-Codeblock und ohne Text davor oder danach.

              Antwortformat:
              {{schema}}
              """;
    }

    /// <summary>The exception to the platform rule for the research of the list itself (appended right under the rule).</summary>
    private static string ListResearchNote(GamePlatformContext p, string language)
    {
        var english = language == Lang.English;
        return p.IsPlayStation
            ? english
                ? "\n- For THIS task: you research the trophy list itself – take it only from sources about exactly this version; the ids the app has (above) only keep the same trophies under the same id."
                : "\n- Für DIESE Aufgabe: Du recherchierst die Trophäenliste selbst – nimm sie nur aus Quellen zu genau dieser Version; die IDs der App (oben) halten nur dieselben Trophäen unter derselben ID."
            : english
                ? "\n- For THIS task: you research the achievement list itself – take it only from sources about exactly this version; the ids the app has (above) only keep the same achievements under the same id."
                : "\n- Für DIESE Aufgabe: Du recherchierst die Erfolgsliste selbst – nimm sie nur aus Quellen zu genau dieser Version; die IDs der App (oben) halten nur dieselben Erfolge unter derselben ID.";
    }

    private static string TrophyTask(GamePlatformContext p, string language)
    {
        var english = language == Lang.English;
        var platformLine = p switch
        {
            { IsPlayStation: true } when p.Variant == GamePlatforms.Ps4 => english
                ? "  The PS4 list – a PS5 version of the same game has its own, separate list; do not mix them."
                : "  Die PS4-Liste – eine PS5-Version desselben Spiels hat eine eigene, getrennte Liste; mische sie nicht.",
            { IsPlayStation: true } => english
                ? "  The PS5 list – the PS4 version has its own, separate list (a PS4 game played on a PS5 earns the PS4 list); do not mix them."
                : "  Die PS5-Liste – die PS4-Version hat eine eigene, getrennte Liste (ein PS4-Spiel auf der PS5 bekommt die PS4-Liste); mische sie nicht.",
            { IsXbox: true } => english
                ? "  The Xbox list with the gamerscore of every achievement (\"points\")."
                : "  Die Xbox-Liste mit dem Gamerscore jedes Erfolgs („points“).",
            _ => english
                ? $"  The list of the {p.Name} version (with the points / XP of each achievement as \"points\" when {p.Name} shows them) – it can differ from the Steam list."
                : $"  Die Liste der {p.Name}-Version (mit den Punkten / XP jedes Erfolgs als „points“, wenn {p.Name} sie zeigt) – sie kann von der Steam-Liste abweichen.",
        };
        var noun = p.IsPlayStation ? (english ? "trophy" : "jede Trophäe") : (english ? "achievement" : "jeden Erfolg");
        return english
            ? $"""
              - The COMPLETE official {(p.IsPlayStation ? "trophy" : "achievement")} list of exactly this version: every {noun} including hidden ones and those
                of DLCs (field "group" = the name of the DLC, base game = null).
              {platformLine}
              - "name" = the official name in the English version of the game; "originalName" = the official English name only if it differs (else leave it out).
              - "description" = the official condition, short; "tip" = one or two sentences on how to get it (own words, empty if nothing is documented);
                "missable": true only if it can be missed for good; "hidden": true for hidden / secret ones; "category" = one short word (Story, Collectibles,
                Combat, Difficulty, Online, Misc).
              - "rarityPercent" (share of players, 0–100) only if a source states it, else leave it out.
              - "id" = a short stable id in kebab-case made from the official English name (lower-case letters, digits, hyphens), unique in the list.
              """
            : $"""
              - Die VOLLSTÄNDIGE offizielle {(p.IsPlayStation ? "Trophäenliste" : "Erfolgsliste")} genau dieser Version: {noun} einschließlich der versteckten
                und der von DLCs (Feld „group“ = Name des DLC, Hauptspiel = null).
              {platformLine}
              - „name“ = der offizielle Name in der deutschen Fassung des Spiels (gibt es keine, der offizielle englische); „originalName“ = der offizielle englische
                Name, nur wenn er abweicht (sonst weglassen).
              - „description“ = die offizielle Bedingung, kurz, auf Deutsch; „tip“ = ein, zwei Sätze, wie man sie bekommt (eigene Worte, leer, wenn nichts belegt ist);
                „missable“: true nur, wenn sie endgültig verpasst werden kann; „hidden“: true bei versteckten / geheimen; „category“ = ein kurzes Wort (Story,
                Sammeln, Kampf, Schwierigkeit, Online, Sonstiges).
              - „rarityPercent“ (Anteil der Spieler, 0–100) nur, wenn eine Quelle ihn nennt, sonst weglassen.
              - „id“ = eine kurze stabile ID in kebab-case aus dem offiziellen englischen Namen (Kleinbuchstaben, Ziffern, Bindestriche), eindeutig in der Liste.
              """;
    }

    private static string TrophyCrossCheck(GamePlatformContext p, string language)
    {
        var english = language == Lang.English;
        if (p.IsPlayStation)
        {
            return english
                ? """
                  - "grade" for every trophy: "platinum", "gold", "silver" or "bronze". A game has at most ONE platinum trophy (in the base game; DLCs have none).
                  - Cross-check before answering: the number of trophies per grade and the total with a second source (e.g. PSNProfiles and
                    PlayStationTrophies.org or Exophase). If the sources disagree, take the list of the official platform page or say so in "reason".
                  """
                : """
                  - „grade“ für jede Trophäe: „platinum“, „gold“, „silver“ oder „bronze“. Ein Spiel hat höchstens EINE Platin-Trophäe (im Hauptspiel; DLCs haben keine).
                  - Prüfe vor der Antwort: die Zahl der Trophäen je Stufe und die Gesamtzahl mit einer zweiten Quelle (z. B. PSNProfiles und PlayStationTrophies.org
                    oder Exophase). Widersprechen sich die Quellen, nimm die Liste der offiziellen Plattform-Seite oder sag es in „reason“.
                  """;
        }
        return english
            ? $"""
              - Cross-check before answering: the number of achievements{(p.IsXbox ? " and the total gamerscore" : string.Empty)} with a second source (e.g.
                {(p.IsXbox ? "TrueAchievements and Exophase" : "Exophase and the store page")}). If the sources disagree, say so in "reason".
              """
            : $"""
              - Prüfe vor der Antwort: die Zahl der Erfolge{(p.IsXbox ? " und den Gesamt-Gamerscore" : string.Empty)} mit einer zweiten Quelle (z. B.
                {(p.IsXbox ? "TrueAchievements und Exophase" : "Exophase und die Store-Seite")}). Widersprechen sich die Quellen, sag es in „reason“.
              """;
    }

    /// <summary>The keep list of a re-research: the ids the app has today, so the same trophies keep the same ids (ticks, pins, notes stay).</summary>
    private static string KeepList(IReadOnlyList<GameTrophyEntity> existing, GamePlatformContext p, string language)
    {
        var english = language == Lang.English;
        var lines = existing.Take(MaxKeepTrophies).Select(t => $"{t.TrophyKey} | {t.Name}");
        var more = existing.Count > MaxKeepTrophies
            ? english ? $"\n(+{existing.Count - MaxKeepTrophies} more)" : $"\n(+{existing.Count - MaxKeepTrophies} weitere)"
            : string.Empty;
        var noun = p.IsPlayStation ? (english ? "trophies" : "Trophäen") : (english ? "achievements" : "Erfolge");
        return english
            ? $"""
              - The app has a list already (id | name). Use EXACTLY these ids for the same {noun}; new ones get a new id:
              {string.Join("\n", lines)}{more}
              """
            : $"""
              - Die App hat schon eine Liste (ID | Name). Nimm für dieselben {noun} GENAU diese IDs; neue bekommen eine neue ID:
              {string.Join("\n", lines)}{more}
              """;
    }

    /// <summary>The JSON shape of the answer (the reader is <see cref="TrophyList.Read(string?, TrophyListExpectations)"/>).</summary>
    private static string TrophySchema(GamePlatformContext p, TrophyResearchInclude include, string title, string echo, string language)
    {
        var english = language == Lang.English;
        var fields = new List<string> { $"\"game\": \"{title}\"", $"\"platform\": \"{echo}\"" };
        if (include.Trophies)
        {
            fields.Add("\"documented\": true");
            var gradeOrPoints = p.IsPlayStation ? "\"grade\": \"gold\", " : "\"points\": 30, ";
            var entry = english
                ? $"{{ \"id\": \"a-new-beginning\", \"name\": \"A New Beginning\", {gradeOrPoints}\"description\": \"Finish the prologue.\", \"tip\": \"\", \"hidden\": false, \"missable\": false, \"group\": null, \"category\": \"Story\" }}"
                : $"{{ \"id\": \"a-new-beginning\", \"name\": \"Ein neuer Anfang\", \"originalName\": \"A New Beginning\", {gradeOrPoints}\"description\": \"Schließe den Prolog ab.\", \"tip\": \"\", \"hidden\": false, \"missable\": false, \"group\": null, \"category\": \"Story\" }}";
            fields.Add($"\"trophies\": [ {entry}, … ]");
        }
        if (include.Facts)
        {
            fields.Add(english
                ? "\"facts\": { \"releaseDate\": \"2023-10-12\", \"developers\": [\"…\"], \"publishers\": [\"…\"], \"genres\": [\"Role-playing (RPG)\"], \"summary\": \"…\", \"hoursComplete\": 60 }"
                : "\"facts\": { \"releaseDate\": \"2023-10-12\", \"developers\": [\"…\"], \"publishers\": [\"…\"], \"genres\": [\"Role-playing (RPG)\"], \"summary\": \"…\", \"hoursComplete\": 60 }");
        }
        fields.Add("\"sources\": [ { \"title\": \"…\", \"url\": \"https://…\" } ]");
        var shape = "{ " + string.Join(", ", fields) + " }";
        return english
            ? $"{shape}\n(The values are examples of the shape only – write the real list; \"platform\" stays \"{echo}\".)"
            : $"{shape}\n(Die Werte zeigen nur die Form – schreibe die echte Liste; „platform“ bleibt „{echo}“.)";
    }

    /// <summary>The trophy-list prompt plus the output contract of a chat (own subscription): one JSON object, preferably as a file, never invented.</summary>
    internal static string ComposeManualTrophyPrompt(GameContext context, GameEntity game, TrophyResearchInclude include, IReadOnlyList<GameTrophyEntity> existing)
    {
        var language = context.Language;
        var file = TrophyAnswerFileName(game);
        var identity = ManualIdentity(game, language);
        var contract = language == Lang.English
            ? $"""
              Output in this chat (this applies in addition and wins over the sentence "without a Markdown code block" above):
              - The game is exactly this one: "{game.Title}", {identity}. Before writing, make sure you mean this version – not another platform, a remaster, a
                predecessor, a successor or a demo.
              - Output the result as ONE JSON object. If your chat can create files, create ONE file "{file}" with it; otherwise put it into exactly one
                code block (```json … ```), without any text before or after it.
              - Write the real list, no placeholders like "…" – the answer format above only shows the shape.
              - Use the web search of your chat. Without web search answer {"{"} "documented": false, "reason": "no web search" {"}"} – never a list from memory.
              """
            : $"""
              Ausgabe in diesem Chat (das gilt zusätzlich und hat bei der Ausgabe Vorrang vor dem Satz „ohne Markdown-Codeblock“ oben):
              - Gemeint ist genau dieses Spiel: „{game.Title}“, {identity}. Prüfe vor dem Schreiben, dass du diese Version meinst – nicht eine andere Plattform, ein
                Remaster, einen Vorgänger, Nachfolger oder eine Demo.
              - Gib das Ergebnis als EIN JSON-Objekt aus. Kann dein Chat Dateien erstellen, lege EINE Datei „{file}“ damit an; sonst in genau einem Codeblock
                (```json … ```), ohne Text davor oder danach.
              - Schreibe die echte Liste, keine Platzhalter wie „…“ – das Antwortformat oben zeigt nur die Form.
              - Nutze die Websuche deines Chats. Ohne Websuche antworte {"{"} "documented": false, "reason": "keine Websuche" {"}"} – nie eine Liste aus dem Gedächtnis.
              """;
        return BuildTrophyListPrompt(context, game, include, existing) + "\n\n" + contract;
    }

    /// <summary>The file the chat should write the answer into (the prompt download itself is <c>platinvault-&lt;slug&gt;-trophies.md</c>).</summary>
    internal static string TrophyAnswerFileName(GameEntity game) => $"platinvault-{Slugify(game.Title)}-trophies.json";

    /// <summary>
    /// Researches the trophy list (and/or the facts) with the AI: ONE call with web search through the run of the action (fallback as everywhere). A cut-off
    /// answer is asked for once more, more compactly (a trophy list cannot be shortened, its texts can). Returns what the reader made of the answer - the
    /// caller decides whether it is saved (<see cref="TrophyListStore"/>).
    /// </summary>
    public async Task<(TrophyListRead Read, TrophyResearchInclude Include, AiUsage Usage)> ResearchTrophyListAsync(
        GameEntity game, string? providerOverride, TrophyResearchInclude include, AiJobProgress? progress, CancellationToken ct = default)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var settings = await settingsStore.GetAsync(ct);
        var run = await router.BeginAsync(research: true, providerOverride, background: false, ct);
        progress?.Step(include.Trophies ? "trophies" : "facts");

        var existing = await TrophiesOfAsync(game, ct);
        var blocks = await settingsStore.GetResearchBlocksAsync(ct);
        var context = BuildManualContext(game, existing, blocks, language, DateTime.Now);
        var prompt = BuildTrophyListPrompt(context, game, include, existing);

        AiAnswer answer;
        try
        {
            answer = await CallAnswerAsync(run, run.Primary, prompt, settings, ct);
        }
        catch (AiProviderException first) when (first.IsTruncated)
        {
            var provider = first.Provider ?? run.Primary;
            logger.LogWarning("Trophäenliste von {Game}: Antwort von {Provider} abgeschnitten ({Detail}) – zweiter Versuch mit kürzeren Texten.", game.Title, provider, first.ProviderMessage);
            try
            {
                answer = await CallAnswerAsync(run, provider, prompt + "\n\n" + TrophyCompactNote(language), settings, ct);
            }
            catch (AiProviderException second) when (second.IsTruncated)
            {
                throw new AiProviderException(ServerTexts.Ai.AnswerTooLong(second.Provider ?? provider), second.ProviderMessage, 0, second, second.Provider ?? provider,
                    kind: AiFailureKind.Other, truncated: true);
            }
        }
        LogSwitches(run, "Trophäenliste");

        var read = WithProviderSources(TrophyList.Read(answer.Text, ExpectationsFor(game, include, strictPlatinum: false)), answer.Sources);
        return (read, include, run.Usage());
    }

    /// <summary>The second attempt after a cut-off trophy list: every entry stays, only its texts get shorter.</summary>
    internal static string TrophyCompactNote(string language) => language == Lang.English
        ? """
          IMPORTANT – second attempt: your previous answer was too long and was cut off, so it could not be used. Keep EVERY entry of the list, but write
          "description" in at most 12 words, leave "tip" empty and leave out "originalName" where it equals "name". This takes precedence over everything above.
          """
        : """
          WICHTIG – zweiter Versuch: Deine letzte Antwort war zu lang und wurde abgeschnitten, sie war deshalb unbrauchbar. Behalte JEDEN Eintrag der Liste, aber
          schreibe „description“ in höchstens 12 Wörtern, lass „tip“ leer und lass „originalName“ weg, wo er gleich „name“ ist. Das hat Vorrang vor allem oben.
          """;

    /// <summary>The sources the provider's search metadata names (Claude's cited and searched pages) come first; the model's own come after.</summary>
    internal static TrophyListRead WithProviderSources(TrophyListRead read, AiSourceMeta meta)
    {
        if (meta.IsEmpty || !read.Ok)
        {
            return read;
        }
        var provider = meta.Cited.Concat(meta.Searched).Select(s => new TrophySourceDto(s.Title, s.Url));
        var sources = provider.Concat(read.Sources)
            .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .Take(TrophyList.MaxSources).ToList();
        var warnings = sources.Count > 0 ? read.Warnings.Where(w => w.Code != "noSources").ToList() : read.Warnings;
        return read with { Sources = sources, Warnings = warnings };
    }

    // ---- Own subscription (ManualTrophyEndpoints): nothing here talks to an AI provider ----

    /// <summary>The prompt for the player's chat; FileName is the download of the prompt itself (.md), the answer file is named inside the prompt (.json).</summary>
    public async Task<ManualPromptDto> BuildManualTrophyPromptAsync(GameEntity game, TrophyResearchInclude include, CancellationToken ct = default)
    {
        var language = GuideLanguage(game);
        using var languageScope = Lang.Use(language);
        var existing = await TrophiesOfAsync(game, ct);
        var blocks = await settingsStore.GetResearchBlocksAsync(ct);
        var context = BuildManualContext(game, existing, blocks, language, DateTime.Now);
        var prompt = ComposeManualTrophyPrompt(context, game, include, existing);
        return new ManualPromptDto("trophies", prompt, ManualFileName(game, "trophies"), language, null, game.Title);
    }

    /// <summary>Checks a pasted answer without saving: the same reader as the AI way (strict about several platinum trophies), plus what saving would do.</summary>
    public async Task<ManualTrophyCheckDto> CheckManualTrophiesAsync(GameEntity game, string? text, CancellationToken ct = default)
    {
        var (read, _) = ReadManualTrophyAnswer(game, text);
        var existing = await TrophiesOfAsync(game, ct);
        return CheckResult(read, existing, game.Platform);
    }

    /// <summary>
    /// Reads a pasted answer. What it covers follows the answer (the check has no prompt state): the list when the platform has one and the answer is
    /// about a list (or carries nothing else), the facts when the answer has them and they do not come from IGDB - IGDB facts are kept with a warning.
    /// </summary>
    public static (TrophyListRead Read, TrophyResearchInclude Include) ReadManualTrophyAnswer(GameEntity game, string? text)
    {
        var errors = new List<ManualIssueDto>();
        if (!ManualAnswers.TryRead(text, errors, out var root) || root is null)
        {
            return (new TrophyListRead(errors, [], [], [], null), new TrophyResearchInclude(GamePlatforms.HasTrophies(game.Platform), false));
        }
        var hasFacts = root["facts"] is JsonObject;
        var aboutList = root["trophies"] is not null || root["documented"] is not null;
        var include = new TrophyResearchInclude(
            Trophies: GamePlatforms.HasTrophies(game.Platform) && (aboutList || !hasFacts),
            Facts: hasFacts && game.FactsSource != "igdb");
        if (!include.Any)
        {
            // Nintendo without facts in the answer: the facts are what was asked for
            include = include with { Facts = game.FactsSource != "igdb" };
        }
        var read = TrophyList.Read(root, ExpectationsFor(game, include, strictPlatinum: true));
        if (hasFacts && game.FactsSource == "igdb")
        {
            read.Warnings.Add(ManualAnswers.Issue("factsFromIgdbKept", ServerTexts.Trophies.FactsFromIgdbKept));
        }
        if (!include.Any)
        {
            read.Errors.Add(ManualAnswers.Issue("nothingToSave", ServerTexts.Trophies.NothingToResearch(GamePlatforms.DisplayName(game.Platform, game.PlatformVariant), false, true)));
        }
        return (read, include);
    }

    /// <summary>What the check of a pasted answer reports (pure).</summary>
    internal static ManualTrophyCheckDto CheckResult(TrophyListRead read, IReadOnlyList<GameTrophyEntity> existing, string platform)
    {
        if (!read.Ok)
        {
            return new ManualTrophyCheckDto(false, read.Errors, read.Warnings, 0, [], 0, 0, [], 0, 0, false);
        }
        var byGrade = GamePlatforms.HasPlatinumGrade(platform)
            ? TrophyTiers.Grades.ToDictionary(g => g, g => read.Trophies.Count(t => t.Grade == g))
            : [];
        var plan = read.Trophies.Count > 0 ? TrophyMerge.Plan(existing, read.Trophies) : null;
        var preview = read.Trophies.Take(12).Select(t => new ManualTrophyPreviewDto(t.Key, t.Name, t.Grade, t.Points, t.Hidden, t.Group)).ToList();
        return new ManualTrophyCheckDto(
            Ok: true,
            Errors: read.Errors,
            Warnings: read.Warnings,
            Count: read.Trophies.Count,
            ByGrade: byGrade,
            Hidden: read.Trophies.Count(t => t.Hidden),
            Dlc: read.Trophies.Count(t => t.Group is not null),
            Preview: preview,
            KeepsTicks: plan?.KeptTicks ?? 0,
            DropsTicks: plan?.Removed.Count ?? 0,
            HasFacts: read.Facts is not null);
    }
}
