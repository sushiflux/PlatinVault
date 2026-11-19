using Companion.Api.Data;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// The platform of a game added by hand, as the prompts need it (<see cref="AiGuideGeneratorService.GameContext.Platform"/>). Null on a context means a
/// Steam game: every prompt then renders exactly the text it always had (pinned by Golden/prompts.txt).
/// </summary>
/// <param name="Platform">One of <see cref="GamePlatforms.ManualIds"/>.</param>
/// <param name="Variant">"ps5" / "ps4" for PlayStation, else null.</param>
/// <param name="TrophyCount">Trophies of the researched list the app has (0 = none researched yet, or the platform has none).</param>
/// <param name="HasTrophyList">A list was researched (<c>TrophyListResearchedAt</c> is set).</param>
/// <param name="IgdbId">The IGDB game, when the game was added through IGDB.</param>
internal sealed record GamePlatformContext(string Platform, string? Variant, int TrophyCount, bool HasTrophyList, int? IgdbId = null)
{
    /// <summary>The readable platform name ("PlayStation 5", "Xbox", "Epic Games", ...).</summary>
    public string Name => GamePlatforms.DisplayName(Platform, Variant);

    public bool IsPlayStation => Platform == GamePlatforms.PlayStation;
    public bool IsXbox => Platform == GamePlatforms.Xbox;
    public bool IsNintendo => Platform == GamePlatforms.Nintendo;

    /// <summary>A PC store (Epic, GOG, Ubisoft Connect, EA app, Battle.net): PC rules like Steam, with that store's own achievement list.</summary>
    public bool IsPcStore => !IsPlayStation && !IsXbox && !IsNintendo;

    /// <summary>The context of a game: null for a Steam game (the prompts stay exactly as they are), else its platform.</summary>
    public static GamePlatformContext? Of(GameEntity game, int trophyCount) =>
        GamePlatforms.IsManual(game)
            ? new GamePlatformContext(
                GamePlatforms.Normalize(game.Platform) ?? game.Platform,
                game.PlatformVariant,
                trophyCount,
                game.TrophyListResearchedAt is not null,
                game.IgdbId)
            : null;
}

public sealed partial class AiGuideGeneratorService
{
    // The platform rule. A Steam game (no platform on the context) gets the rule it always had: PlatinVault's games came from the player's Steam library, so
    // every guide was about the Steam version on PC. Research sources are often console-centric (PlayStation trophy guides, TrueAchievements), and without
    // this rule the AI copied "PS4 trophy guide: 35 to 50+ hours to the platinum" into a Steam guide. A game added by hand (owner decision 2026-11-19) gets
    // the rule of ITS platform instead (PS5 vs PS4 lists, buttons, what "100 %" means). ONE place per language; it is part of ResearchRules, which every
    // prompt that researches or writes guide content includes (blueprint, chapter, walkthrough outline/parts/single, Story & Lore, achievement tips, the
    // manual subscription prompts, the trophy list) - tests check that.

    /// <summary>The mandatory platform rule, in the language of the guide. Without <paramref name="platform"/>: "Steam version on PC only" (unchanged).</summary>
    internal static string PlatformRules(string language, GamePlatformContext? platform = null)
    {
        if (platform is not null)
        {
            return ManualPlatformRules(language, platform);
        }
        return language == Lang.English
        ? """
          Platform (mandatory rule): the player plays the STEAM version on PC.
          - Achievements are Steam achievements. Never write about PlayStation trophies, a platinum trophy, Xbox achievements or Gamerscore;
            "100%" means all Steam achievements (plus the in-game completion they need).
          - Controls are keyboard and mouse (or a controller on PC). Never use console button names (Cross, Square, Triangle, R1, A/B/X/Y …) unless the
            PC version itself shows them.
          - Leave out console-only content: console exclusives, console patch states, PSN/Xbox-only DLC, bonuses or events, cross-save, console
            performance modes. If the PC version differs (patch state, DLC, content), the PC version counts.
          - Console sites (PSNProfiles, PlayStationTrophies.org, PowerPyx trophy guides, TrueAchievements, Xbox guides …) may be used for everything that is
            identical on PC (story, route, missables, boss tactics, order) and are then not named as "PS4/PS5/PlayStation/Xbox guide" in the text – name the fact,
            not the console. The achievement list, the names and the conditions come ONLY from Steam: if a console trophy differs, Steam wins.
          - Time estimates ("hours to 100%") are for all Steam achievements on PC – never "hours to the platinum trophy".
          """
        : """
          Plattform (verbindliche Regel): Der Spieler spielt die STEAM-Version auf dem PC.
          - Erfolge sind Steam-Erfolge. Schreibe nie über PlayStation-Trophäen, eine Platin-Trophäe, Xbox-Erfolge oder Gamerscore;
            „100 %“ heißt alle Steam-Erfolge (plus die Spiel-Fortschritte, die sie brauchen).
          - Steuerung ist Tastatur und Maus (oder ein Controller am PC). Nutze nie Konsolen-Tastennamen (Kreuz, Quadrat, Dreieck, R1, A/B/X/Y …), außer die
            PC-Version zeigt sie selbst an.
          - Lass reine Konsolen-Inhalte weg: Konsolen-Exklusives, Konsolen-Patchstände, PSN-/Xbox-exklusive DLCs, Boni oder Events, Cross-Save,
            Konsolen-Leistungsmodi. Unterscheidet sich die PC-Version (Patchstand, DLC, Inhalt), gilt die PC-Version.
          - Konsolen-Seiten (PSNProfiles, PlayStationTrophies.org, PowerPyx-Trophäen-Guides, TrueAchievements, Xbox-Guides …) darfst du für alles nutzen, was am PC
            genauso ist (Story, Route, Verpassbares, Boss-Taktiken, Reihenfolge), nennst sie im Text aber nicht als „PS4-/PS5-/PlayStation-/Xbox-Guide“ – nenne den
            Fakt, nicht die Konsole. Die Erfolgsliste, die Namen und die Bedingungen kommen NUR von Steam: weicht eine Konsolen-Trophäe ab, gilt Steam.
          - Zeitangaben („Stunden bis 100 %“) gelten für alle Steam-Erfolge am PC – nie „Stunden bis zur Platin-Trophäe“.
          """;
    }

    /// <summary>The rule for a game added by hand: one branch per kind of platform (PlayStation, Xbox, Nintendo Switch, a PC store).</summary>
    private static string ManualPlatformRules(string language, GamePlatformContext p)
    {
        var english = language == Lang.English;
        var name = p.Name;
        if (p.IsPlayStation)
        {
            var other = p.Variant == GamePlatforms.Ps4 ? "PS5" : "PS4";
            return english
                ? $"""
                  Platform (mandatory rule): the player plays the {name} version.
                  - Trophies are the trophies of the {name} version: bronze, silver, gold and the platinum trophy. The PS5 and the PS4 version have SEPARATE
                    trophy lists: use only the list of the {name} version and never mix in trophies of the {other} list. "100%" means the platinum trophy (all
                    base-game trophies) plus the DLC trophies where the player wants them.
                  - Controls are the PlayStation controller buttons: Cross, Circle, Square, Triangle, L1/R1, L2/R2, L3/R3, touchpad and Options. Never use keyboard
                    and mouse or Xbox button names.
                  - PlayStation content counts (PlayStation-only DLC, bonuses, the patch state of this console). Leave out what exists only on PC or Xbox.
                  - The trophy names, ids and conditions come ONLY from the trophy list the app has (see the facts): if a source names a trophy differently, the
                    app's list wins. Never invent a trophy.
                  - Sources about other versions may be used for what is the same on this console (story, route, missables, boss tactics) – name the fact, not
                    the other platform.
                  - Time estimates are "hours to the platinum trophy" of the {name} version.
                  """
                : $"""
                  Plattform (verbindliche Regel): Der Spieler spielt die {name}-Version.
                  - Trophäen sind die Trophäen der {name}-Version: Bronze, Silber, Gold und die Platin-Trophäe. PS5- und PS4-Version haben GETRENNTE
                    Trophäenlisten: nutze nur die Liste der {name}-Version und mische nie Trophäen der {other}-Liste hinein. „100 %“ heißt die Platin-Trophäe
                    (alle Trophäen des Hauptspiels) plus die DLC-Trophäen, wo der Spieler sie will.
                  - Steuerung sind die Tasten des PlayStation-Controllers: Kreuz, Kreis, Quadrat, Dreieck, L1/R1, L2/R2, L3/R3, Touchpad und Options. Nutze nie
                    Tastatur und Maus oder Xbox-Tastennamen.
                  - PlayStation-Inhalte zählen (PlayStation-exklusive DLCs, Boni, der Patchstand dieser Konsole). Lass weg, was es nur am PC oder auf der Xbox gibt.
                  - Die Namen, IDs und Bedingungen der Trophäen kommen NUR aus der Trophäenliste, die der App vorliegt (siehe Fakten): nennt eine Quelle eine
                    Trophäe anders, gilt die Liste der App. Erfinde nie eine Trophäe.
                  - Quellen zu anderen Versionen darfst du für alles nutzen, was auf dieser Konsole genauso ist (Story, Route, Verpassbares, Boss-Taktiken) – nenne
                    den Fakt, nicht die andere Plattform.
                  - Zeitangaben sind „Stunden bis zur Platin-Trophäe“ der {name}-Version.
                  """;
        }
        if (p.IsXbox)
        {
            return english
                ? """
                  Platform (mandatory rule): the player plays the Xbox version (Xbox Series X|S or Xbox One).
                  - Achievements are Xbox achievements with Gamerscore; "100%" means all achievements of the base game plus the DLC achievements where the player
                    wants them. Never write about PlayStation trophies, a platinum trophy or Steam achievements.
                  - Controls are the Xbox controller buttons: A, B, X, Y, LB/RB, LT/RT, the left and right stick, View and Menu. Never use PlayStation button names
                    or keyboard and mouse.
                  - Xbox content counts; leave out what exists only on PlayStation or PC.
                  - The achievement names, ids and conditions come ONLY from the achievement list the app has (see the facts): if a source differs, the app's list
                    wins. Never invent an achievement.
                  - Sources about other versions may be used for what is the same on Xbox (story, route, missables, boss tactics) – name the fact, not the other
                    platform.
                  - Time estimates are hours to all Xbox achievements.
                  """
                : """
                  Plattform (verbindliche Regel): Der Spieler spielt die Xbox-Version (Xbox Series X|S oder Xbox One).
                  - Erfolge sind Xbox-Erfolge mit Gamerscore; „100 %“ heißt alle Erfolge des Hauptspiels plus die DLC-Erfolge, wo der Spieler sie will. Schreibe nie
                    über PlayStation-Trophäen, eine Platin-Trophäe oder Steam-Erfolge.
                  - Steuerung sind die Tasten des Xbox-Controllers: A, B, X, Y, LB/RB, LT/RT, linker und rechter Stick, Ansicht und Menü. Nutze nie
                    PlayStation-Tastennamen oder Tastatur und Maus.
                  - Xbox-Inhalte zählen; lass weg, was es nur auf der PlayStation oder am PC gibt.
                  - Die Namen, IDs und Bedingungen der Erfolge kommen NUR aus der Erfolgsliste, die der App vorliegt (siehe Fakten): weicht eine Quelle ab, gilt die
                    Liste der App. Erfinde nie einen Erfolg.
                  - Quellen zu anderen Versionen darfst du für alles nutzen, was auf der Xbox genauso ist (Story, Route, Verpassbares, Boss-Taktiken) – nenne den
                    Fakt, nicht die andere Plattform.
                  - Zeitangaben sind Stunden bis zu allen Xbox-Erfolgen.
                  """;
        }
        if (p.IsNintendo)
        {
            return english
                ? """
                  Platform (mandatory rule): the player plays the Nintendo Switch version.
                  - The Nintendo Switch has NO trophies and NO achievements. Never write about trophies, achievements, a platinum trophy or Gamerscore, and never
                    invent any.
                  - "100%" means the in-game completion: everything the game itself counts (collectibles, completion percentage, in-game medals or records).
                  - Controls are the Joy-Con / Pro Controller buttons: A, B, X, Y, L/R, ZL/ZR, + and −. Never use PlayStation or Xbox button names or keyboard
                    and mouse.
                  - Switch content counts; leave out what exists only on other platforms, and say so when the Switch version lacks content.
                  - Sources about other versions may be used for what is the same on the Switch (story, route, missables, boss tactics) – name the fact, not the
                    other platform.
                  - Time estimates are hours to 100 % in-game completion.
                  """
                : """
                  Plattform (verbindliche Regel): Der Spieler spielt die Nintendo-Switch-Version.
                  - Die Nintendo Switch hat KEINE Trophäen und KEINE Erfolge. Schreibe nie über Trophäen, Erfolge, eine Platin-Trophäe oder Gamerscore und erfinde
                    keine.
                  - „100 %“ heißt die Vervollständigung im Spiel: alles, was das Spiel selbst zählt (Sammelobjekte, Prozentanzeige, Medaillen oder Rekorde im Spiel).
                  - Steuerung sind die Tasten der Joy-Con / des Pro Controllers: A, B, X, Y, L/R, ZL/ZR, + und −. Nutze nie PlayStation- oder Xbox-Tastennamen oder
                    Tastatur und Maus.
                  - Switch-Inhalte zählen; lass weg, was es nur auf anderen Plattformen gibt, und sag es, wenn der Switch-Version Inhalte fehlen.
                  - Quellen zu anderen Versionen darfst du für alles nutzen, was auf der Switch genauso ist (Story, Route, Verpassbares, Boss-Taktiken) – nenne den
                    Fakt, nicht die andere Plattform.
                  - Zeitangaben sind Stunden bis 100 % Vervollständigung im Spiel.
                  """;
        }

        // A PC store (Epic, GOG, Ubisoft Connect, EA app, Battle.net): PC rules, with the store's own achievement list
        return english
            ? $"""
              Platform (mandatory rule): the player plays the PC version from {name}.
              - Achievements are the {name} achievements of this game; "100%" means all of them (plus the in-game completion they need). Never write about
                PlayStation trophies, a platinum trophy, Xbox achievements, Gamerscore or Steam achievements.
              - Controls are keyboard and mouse (or a controller on PC). Never use console button names unless the PC version itself shows them.
              - Leave out console-only content. If the PC version differs (patch state, DLC, content), the PC version counts.
              - The achievement names, ids and conditions come ONLY from the achievement list the app has (see the facts): Steam, console or other store lists
                may differ – the app's list wins. Never invent an achievement.
              - Sources about other versions may be used for what is the same on PC (story, route, missables, boss tactics) – name the fact, not the other
                platform.
              - Time estimates are hours to all {name} achievements.
              """
            : $"""
              Plattform (verbindliche Regel): Der Spieler spielt die PC-Version von {name}.
              - Erfolge sind die {name}-Erfolge dieses Spiels; „100 %“ heißt alle davon (plus die Spiel-Fortschritte, die sie brauchen). Schreibe nie über
                PlayStation-Trophäen, eine Platin-Trophäe, Xbox-Erfolge, Gamerscore oder Steam-Erfolge.
              - Steuerung ist Tastatur und Maus (oder ein Controller am PC). Nutze nie Konsolen-Tastennamen, außer die PC-Version zeigt sie selbst an.
              - Lass reine Konsolen-Inhalte weg. Unterscheidet sich die PC-Version (Patchstand, DLC, Inhalt), gilt die PC-Version.
              - Die Namen, IDs und Bedingungen der Erfolge kommen NUR aus der Erfolgsliste, die der App vorliegt (siehe Fakten): Listen von Steam, Konsolen oder
                anderen Stores können abweichen – es gilt die Liste der App. Erfinde nie einen Erfolg.
              - Quellen zu anderen Versionen darfst du für alles nutzen, was am PC genauso ist (Story, Route, Verpassbares, Boss-Taktiken) – nenne den Fakt, nicht
                die andere Plattform.
              - Zeitangaben sind Stunden bis zu allen {name}-Erfolgen.
              """;
    }

    // ---- The other platform-dependent pieces of the prompts. Each returns, for a Steam game, exactly the literal the prompt always had. ----

    /// <summary>Heading of the fact block in the chapter, walkthrough, lore and batch prompts.</summary>
    internal static string FactsHeader(GamePlatformContext? platform, string language) => (platform is null, language == Lang.English) switch
    {
        (true, true) => "Known facts from Steam:",
        (true, false) => "Bekannte Fakten aus Steam:",
        (false, true) => "Known facts about the game:",
        _ => "Bekannte Fakten zum Spiel:",
    };

    /// <summary>Heading of the fact block in the blueprint prompt.</summary>
    internal static string BlueprintFactsHeader(GamePlatformContext? platform, string language) => (platform is null, language == Lang.English) switch
    {
        (true, true) => "Known facts from Steam (Steam genres are rough – rely on your research):",
        (true, false) => "Bekannte Fakten aus Steam (Steam-Genres sind grob – verlass dich auf deine Recherche):",
        (false, true) => "Known facts about the game (genres are rough – rely on your research):",
        _ => "Bekannte Fakten zum Spiel (Genres sind grob – verlass dich auf deine Recherche):",
    };

    /// <summary>What the blueprint is told about the achievement chapter: the app builds or shows the list itself, or there is none.</summary>
    internal static string ListNote(GameContext context)
    {
        var english = context.Language == Lang.English;
        if (context.Platform is not { } p)
        {
            return context.Achievements.Count > 0
                ? english
                    ? "The app builds a chapter for the Steam achievements itself from the official Steam list – do not plan anything for it."
                    : "Ein eigenes Kapitel für die Steam-Erfolge baut die App selbst aus der offiziellen Steam-Liste – plane dafür nichts ein."
                : english
                    ? "The app has no official achievement list: plan a chapter \"All achievements / trophies\" under extraChapters."
                    : "Die App hat keine offizielle Erfolgsliste: plane unter extraChapters ein Kapitel „Alle Erfolge / Trophäen“ ein.";
        }
        if (p.IsNintendo)
        {
            return english
                ? "The game has no trophies or achievements (Nintendo Switch) – plan no chapter for them and never invent any."
                : "Das Spiel hat keine Trophäen oder Erfolge (Nintendo Switch) – plane dafür kein Kapitel ein und erfinde keine.";
        }
        var trophies = p.IsPlayStation;
        if (p.HasTrophyList && p.TrophyCount > 0)
        {
            return english
                ? $"The app has the official {(trophies ? "trophy" : "achievement")} list of the {p.Name} version and shows it in its own {(trophies ? "trophy" : "achievement")} area – plan no chapter for it."
                : $"Die App hat die offizielle {(trophies ? "Trophäenliste" : "Erfolgsliste")} der {p.Name}-Version und zeigt sie in ihrem eigenen {(trophies ? "Trophäenbereich" : "Erfolgsbereich")} – plane dafür kein Kapitel ein.";
        }
        return english
            ? $"The {(trophies ? "trophy" : "achievement")} list has not been researched in the app yet – plan no {(trophies ? "trophy" : "achievement")} chapter (the app researches the list as its own step)."
            : $"Die {(trophies ? "Trophäenliste" : "Erfolgsliste")} ist in der App noch nicht recherchiert – plane kein {(trophies ? "Trophäenkapitel" : "Erfolgskapitel")} ein (die App recherchiert die Liste als eigenen Schritt).";
    }

    /// <summary>What the blueprint is told about the genre profiles that are set already (Steam: from the community tags; manual: from the stored genres).</summary>
    internal static string TagProfileNote(GameContext context)
    {
        var english = context.Language == Lang.English;
        if (context.Platform is null)
        {
            return english
                ? context.TagProfiles.Count > 0
                    ? "According to the Steam community tags these profiles are already set: " + string.Join(", ", context.TagProfiles) + " – only add more if they clearly fit."
                    : "The Steam tags give no clear profile – decide based on your research."
                : context.TagProfiles.Count > 0
                    ? "Laut Steam-Community-Tags sind diese Profile bereits gesetzt: " + string.Join(", ", context.TagProfiles) + " – ergänze nur, wenn weitere eindeutig passen."
                    : "Aus den Steam-Tags ergibt sich kein eindeutiges Profil – entscheide anhand deiner Recherche.";
        }
        return english
            ? context.TagProfiles.Count > 0
                ? "According to the genres of the game these profiles are already set: " + string.Join(", ", context.TagProfiles) + " – only add more if they clearly fit."
                : "The genres of the game give no clear profile – decide based on your research."
            : context.TagProfiles.Count > 0
                ? "Laut den Genres des Spiels sind diese Profile bereits gesetzt: " + string.Join(", ", context.TagProfiles) + " – ergänze nur, wenn weitere eindeutig passen."
                : "Aus den Genres des Spiels ergibt sich kein eindeutiges Profil – entscheide anhand deiner Recherche.";
    }

    /// <summary>The example of "estimatedHours" in the blueprint JSON.</summary>
    internal static string EstimatedHoursExample(GamePlatformContext? platform, string language)
    {
        var english = language == Lang.English;
        return platform switch
        {
            null => english ? "e.g. approx. 80–120 hours for all Steam achievements" : "z. B. ca. 80–120 Stunden für alle Steam-Erfolge",
            { IsPlayStation: true } => english ? "e.g. approx. 80–120 hours to the platinum trophy" : "z. B. ca. 80–120 Stunden bis zur Platin-Trophäe",
            { IsNintendo: true } => english ? "e.g. approx. 80–120 hours to 100 % in-game completion" : "z. B. ca. 80–120 Stunden bis 100 % Vervollständigung im Spiel",
            _ => english ? $"e.g. approx. 80–120 hours for all {platform.Name} achievements" : $"z. B. ca. 80–120 Stunden für alle {platform.Name}-Erfolge",
        };
    }

    /// <summary>"exact … from the list" in the boss rule: Steam API names, or the ids of the app's trophy / achievement list.</summary>
    internal static string ApiNamesFromList(GamePlatformContext? platform, string language) => (platform, language == Lang.English) switch
    {
        (null, true) => "Steam API names from the achievement list",
        (null, false) => "Steam-API-Namen aus der Erfolgsliste",
        ({ IsPlayStation: true }, true) => "trophy ids from the trophy list",
        ({ IsPlayStation: true }, false) => "Trophäen-IDs aus der Trophäenliste",
        (_, true) => "achievement ids from the achievement list",
        _ => "Erfolgs-IDs aus der Erfolgsliste",
    };

    /// <summary>What "apiName" of the walkthrough's achievement hint holds.</summary>
    internal static string ApiNameSingular(GamePlatformContext? platform, string language) => (platform, language == Lang.English) switch
    {
        (null, true) => "the exact Steam API name",
        (null, false) => "der exakte Steam-API-Name",
        ({ IsPlayStation: true }, true) => "the exact id from the trophy list",
        ({ IsPlayStation: true }, false) => "die exakte ID aus der Trophäenliste",
        (_, true) => "the exact id from the achievement list",
        _ => "die exakte ID aus der Erfolgsliste",
    };

    /// <summary>The platform in the base wish text ("{Plattform}" of <see cref="ResearchBlocks.FillBase"/>).</summary>
    internal static string ResearchPlatformText(GamePlatformContext? platform) => platform switch
    {
        null => ResearchBlocks.PlatformText,
        { IsPcStore: true } => $"PC ({platform.Name})",
        _ => platform.Name,
    };

    /// <summary>
    /// The identity of the game in the output contract of a chat prompt: "Steam AppID 123 – the Steam version on PC" for a Steam game (unchanged), the
    /// platform version and the IGDB id for a game added by hand.
    /// </summary>
    internal static string ManualIdentity(GameEntity game, string language)
    {
        var english = language == Lang.English;
        if (!GamePlatforms.IsManual(game))
        {
            var appIdLine = game.SteamAppId is > 0
                ? english ? $"Steam AppID {game.SteamAppId}" : $"Steam-AppID {game.SteamAppId}"
                : english ? "Steam AppID unknown" : "Steam-AppID unbekannt";
            return english ? $"{appIdLine} – the Steam version on PC" : $"{appIdLine} – die Steam-Version auf dem PC";
        }
        var name = GamePlatforms.DisplayName(game.Platform, game.PlatformVariant);
        var igdb = game.IgdbId is > 0 ? (english ? $"IGDB id {game.IgdbId}" : $"IGDB-ID {game.IgdbId}") : null;
        var version = GamePlatforms.Normalize(game.Platform) is GamePlatforms.PlayStation or GamePlatforms.Xbox or GamePlatforms.Nintendo
            ? english ? $"the {name} version" : $"die {name}-Version"
            : english ? $"the PC version from {name}" : $"die PC-Version von {name}";
        return igdb is null ? version : $"{igdb} – {version}";
    }

    /// <summary>Where the language rule points for the names of the game's language version (Steam: a fact line; manual: no such line exists).</summary>
    internal static string LanguageVersionHint(GamePlatformContext? platform, string language) => (platform is null, language == Lang.English) switch
    {
        (true, true) => "(see \"English version according to Steam\" in the facts; without that line check it yourself)",
        (true, false) => "(siehe „Deutsche Fassung laut Steam“ in den Fakten; fehlt die Zeile, prüfe es selbst)",
        (false, true) => "(check it yourself)",
        _ => "(prüfe es selbst)",
    };

    /// <summary>The first words of the last bullet of the language rule: which ids stay exactly as they are.</summary>
    internal static string KeptIdsWord(GamePlatformContext? platform, string language) => (platform, language == Lang.English) switch
    {
        (null, true) => "Steam API names",
        (null, false) => "Steam-API-Namen",
        ({ IsPlayStation: true }, true) => "Trophy ids",
        ({ IsPlayStation: true }, false) => "Trophäen-IDs",
        (_, true) => "Achievement ids",
        _ => "Erfolgs-IDs",
    };

    // IGDB genre names (English, as IGDB names them) that decide a genre profile on their own for a game added by hand. Like the Steam tags above, broad
    // genres ("Adventure", "Simulator", "Indie", "Arcade") are left to the AI. The names follow IGDB's genre list; they were not checked against a live IGDB
    // answer (no credentials in development) - a name that does not match only means no profile is preset.
    private static readonly Dictionary<string, string[]> ProfileGenres = new()
    {
        ["rpg"] = ["Role-playing (RPG)"],
        ["shooter"] = ["Shooter"],
        ["metroidvania"] = ["Platform"],
        ["strategy"] = ["Strategy", "Real Time Strategy (RTS)", "Turn-based strategy (TBS)"],
        ["sports"] = ["Sport"],
        ["racing"] = ["Racing"],
        ["fighting"] = ["Fighting"],
        ["adventure"] = ["Point-and-click", "Puzzle", "Visual Novel"],
        ["cards"] = ["Card & Board Game"],
        ["multiplayer"] = ["MOBA"],
    };

    /// <summary>The genre profiles the stored IGDB genres of a game added by hand set already (empty = the AI decides alone).</summary>
    internal static HashSet<string> DetectProfilesFromGenres(IReadOnlyCollection<string> genres)
    {
        var set = genres.Select(g => g.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ProfileGenres
            .Where(p => p.Value.Any(set.Contains))
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
