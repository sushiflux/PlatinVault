using Companion.Api.Localization;

namespace Companion.Api.Services;

public sealed partial class AiGuideGeneratorService
{
    /// <summary>One entry of the fixed topic list every guide is checked against (title and brief in ONE language).</summary>
    public sealed record GuideTopic(string Id, string Title, string Icon, string Brief);

    /// <summary>A kind of game: the topics typical for it (checked first, never forced in; they only break ties in the budget) and others that often fit.</summary>
    public sealed record GenreProfile(string Id, string Name, string Examples, string[] Required, string[] Recommended);

    private sealed record TopicDefinition(string Id, string Icon, string TitleDe, string BriefDe, string TitleEn, string BriefEn);

    private sealed record ProfileDefinition(string Id, string NameDe, string NameEn, string Examples, string[] Required, string[] Recommended);

    // Every topic a guide can have, in chapter order. Which ones a game gets is decided per game: the AI assigns
    // genre profiles (GenreProfiles) and checks every topic; only topics the research confirms become chapters - a profile's
    // "Required" topics are a checklist and a tie-breaker, not a guarantee (so a factory sim with a "survival"-looking tag does not get a bestiary).
    private static readonly TopicDefinition[] TopicDefinitions =
    [
        // The walkthrough always comes first: the course of the game in the order of the game, with the hints exactly where they matter.
        // Only with a reliable source (the blueprint decides), researched in parts (see AiGuideGeneratorService.ResearchWalkthroughAsync) and not counted against the chapter budget.
        new("walkthrough", "mdi:map-legend",
            "Komplettlösung",
            "Der Spielverlauf Schritt für Schritt in der Reihenfolge des Spiels, gegliedert nach Story-Kapiteln bzw. Gebieten, mit Hinweisen genau dort, wo sie wichtig werden (unbedingt mitnehmen mit Fundort, ab hier verpassbar, optional verfügbar, Tipps) und den vollständigen Lösungen von Rätseln. Nur mit einer belastbaren, vollständigen Quelle.",
            "Walkthrough",
            "The course of the game step by step in the order of the game, split into story chapters or areas, with hints exactly where they become relevant (must-have items with location, missable from here, optional content available, tips) and the complete solutions of puzzles. Only with a reliable, complete source."),

        // Core – checked for every game
        new("roadmap", "mdi:compass-outline",
            "Roadmap & Verpassbares",
            "Empfohlene Reihenfolge für 100 %, Kapitel für Kapitel (bzw. Gebiet für Gebiet): was man dort mitnehmen kann, was danach verpassbar ist, Points of no Return, wie viele Durchgänge nötig sind.",
            "Roadmap & missables",
            "Recommended order for 100 %, chapter by chapter (or area by area): what can be picked up there, what becomes missable afterwards, points of no return, how many playthroughs are needed."),
        new("tips", "mdi:lightbulb-on",
            "Tipps & Tricks",
            "Die wichtigsten Einsteiger- und Profi-Tipps, Mechaniken, die das Spiel schlecht erklärt, häufige Fehler und Zeitsparer.",
            "Tips & tricks",
            "The most important beginner and expert tips, mechanics the game explains poorly, common mistakes and time savers."),
        new("earlyop", "mdi:flash",
            "Früh stark werden & Power-Spikes",
            "Wie man früh stark wird bzw. schnell vorankommt: starke frühe Optionen, erlaubte Exploits, die wichtigsten Power-Spikes – jeweils wo und ab wann.",
            "Getting strong early & power spikes",
            "How to get strong early or progress quickly: strong early options, allowed exploits, the most important power spikes – each with where and from when."),

        // Role-playing / open world / action
        new("regions", "mdi:map",
            "Gebiete & Karte",
            "Alle Gebiete mit empfohlenem Level, was es dort gibt (Händler, Bosse, Sammelobjekte, Schnellreise) und sinnvolle Reihenfolge.",
            "Areas & map",
            "All areas with their recommended level, what is there (vendors, bosses, collectibles, fast travel) and a sensible order."),
        new("leveling", "mdi:chart-line",
            "Level-Empfehlungen, Leveln & Farming",
            "Empfohlenes Level pro Gebiet, Kapitel und Boss; die schnellsten Wege zu XP, Geld und wichtigen Ressourcen.",
            "Level recommendations, levelling & farming",
            "Recommended level per area, chapter and boss; the fastest ways to XP, money and important resources."),
        new("weapons", "mdi:sword",
            "Beste Waffen",
            "Die stärksten Waffen (bei Shootern: Waffen, Aufsätze und Loadouts) mit Fundort bzw. Freischaltbedingung, Werten und passendem Spielstil – auch starke frühe Waffen.",
            "Best weapons",
            "The strongest weapons (in shooters: weapons, attachments and loadouts) with location or unlock condition, stats and matching playstyle – including strong early weapons."),
        new("armor", "mdi:shield-half-full",
            "Beste Rüstung & Ausrüstung",
            "Die besten Rüstungen, Accessoires, Mods und Aufwertungen mit Fundort und Effekt.",
            "Best armour & equipment",
            "The best armour, accessories, mods and upgrades with location and effect."),
        new("builds", "mdi:account-cog",
            "Builds, Skills & Taktiken",
            "Stärkste Builds, Skill-/Perk-Reihenfolgen, Kampftaktiken und Synergien – für frühes Spiel, Mitte und Endgame.",
            "Builds, skills & tactics",
            "Strongest builds, skill/perk orders, combat tactics and synergies – for the early game, the middle and the endgame."),
        new("abilities", "mdi:arrow-up-bold-circle",
            "Fähigkeiten & Upgrade-Reihenfolge",
            "Alle Fähigkeiten/Upgrades, wo man sie bekommt, welche Bereiche sie öffnen und die beste Reihenfolge.",
            "Abilities & upgrade order",
            "All abilities/upgrades, where to get them, which areas they open and the best order."),
        new("bestiary", "mdi:skull",
            "Gegner, Bosse & Bestiarium",
            "Bosse und wichtige Gegner mit Fundort, Schwächen, Resistenzen, Phasen und einer konkreten Taktik.",
            "Enemies, bosses & bestiary",
            "Bosses and important enemies with location, weaknesses, resistances, phases and a concrete tactic."),
        new("collectibles", "mdi:map-marker-star",
            "Sammelobjekte & Fundorte",
            "Alle Sammelobjekte nach Gebiet bzw. Mission mit genauem Fundort und Hinweisen, welche verpassbar sind.",
            "Collectibles & locations",
            "All collectibles by area or mission with their exact location and notes on which ones are missable."),
        new("quests", "mdi:script-text",
            "Nebenquests & Aktivitäten",
            "Alle Nebenquests, Aufträge und Aktivitäten mit Startort, Bedingungen, Belohnungen und Fristen.",
            "Side quests & activities",
            "All side quests, contracts and activities with starting point, conditions, rewards and deadlines."),
        new("puzzles", "mdi:puzzle",
            "Rätsel & versteckte Mechaniken",
            "Alle Rätsel, Codes, Reihenfolgen, Kombinationen und versteckten Mechaniken – jeweils mit genauem Ort, Voraussetzungen, ob verpassbar, und der vollständigen Lösung (jeder Schritt in der richtigen Reihenfolge). Nur wenn das Spiel solche Rätsel hat.",
            "Puzzles & hidden mechanisms",
            "All puzzles, codes, sequences, combinations and hidden mechanisms – each with its exact location, prerequisites, whether it is missable, and the complete solution (every step in the right order). Only if the game has such puzzles."),
        new("companions", "mdi:account-heart",
            "Begleiter, Fraktionen & Beziehungen",
            "Begleiter, Fraktionen, Beziehungs- und Romanzen-Systeme mit Bedingungen und verpassbaren Momenten.",
            "Companions, factions & relationships",
            "Companions, factions, relationship and romance systems with conditions and missable moments."),
        new("crafting", "mdi:hammer-wrench",
            "Crafting, Ressourcen & Upgrades",
            "Crafting-System, wichtige Rezepte, wo man seltene Ressourcen findet und welche Upgrades sich zuerst lohnen.",
            "Crafting, resources & upgrades",
            "The crafting system, important recipes, where to find rare resources and which upgrades are worth it first."),
        new("basebuilding", "mdi:home-group",
            "Basisbau & Überleben",
            "Basisbau, Überlebens-Mechaniken (Hunger, Kälte, Gefahren), sinnvolle Reihenfolge der Ausbaustufen.",
            "Base building & survival",
            "Base building, survival mechanics (hunger, cold, dangers), a sensible order of the upgrade stages."),
        new("vehicles", "mdi:car-sports",
            "Fahrzeuge, Reittiere & Fortbewegung",
            "Fahrzeuge, Reittiere und Schnellreise: wo man sie bekommt, die besten Optionen und Freischaltungen.",
            "Vehicles, mounts & travel",
            "Vehicles, mounts and fast travel: where to get them, the best options and unlocks."),
        new("endings", "mdi:source-branch",
            "Enden & Entscheidungen",
            "Alle Enden und wichtigen Entscheidungen mit Bedingungen und wann man spätestens speichern sollte.",
            "Endings & decisions",
            "All endings and important decisions with their conditions and the latest point at which to save."),

        // Management / simulation / strategy
        new("economy", "mdi:cash-multiple",
            "Geld & Wirtschaft",
            "Wie man am schnellsten Geld verdient, Preise und Kosten im Griff behält, typische Pleite-Fallen und Profi-Strategien.",
            "Money & economy",
            "How to earn money fastest, keep prices and costs under control, typical bankruptcy traps and expert strategies."),
        new("production", "mdi:factory",
            "Produktion, Abläufe & Effizienz",
            "Produktionsketten, effiziente Layouts/Abläufe, Engpässe erkennen und beheben, Automatisierung.",
            "Production, workflows & efficiency",
            "Production chains, efficient layouts/workflows, spotting and fixing bottlenecks, automation."),
        new("research", "mdi:flask",
            "Forschung, Upgrades & Freischaltungen",
            "Forschungs- bzw. Upgrade-Baum: was es gibt, was sich zuerst lohnt und was man für Erfolge braucht.",
            "Research, upgrades & unlocks",
            "The research or upgrade tree: what exists, what is worth it first and what is needed for achievements."),
        new("scenarios", "mdi:flag-checkered",
            "Szenarien, Aufträge & Missionen",
            "Alle Szenarien, Aufträge oder Kampagnen-Missionen mit Zielen, Lösungsweg und Erfolgs-Bedingungen.",
            "Scenarios, contracts & missions",
            "All scenarios, contracts or campaign missions with goals, solution and achievement conditions."),
        new("factions", "mdi:chess-knight",
            "Fraktionen, Völker & Einheiten",
            "Spielbare Fraktionen bzw. Völker mit Stärken, Schwächen, besten Einheiten und Spielweise.",
            "Factions, peoples & units",
            "Playable factions or peoples with strengths, weaknesses, best units and playstyle."),
        new("openings", "mdi:format-list-numbered",
            "Eröffnungen & Bauordnungen",
            "Bewährte Eröffnungen und Bauordnungen, Timing, frühe Fehler vermeiden.",
            "Openings & build orders",
            "Proven openings and build orders, timing, avoiding early mistakes."),
        new("victory", "mdi:trophy",
            "Siegbedingungen & Kampagne",
            "Alle Siegbedingungen bzw. Kampagnen-Missionen mit Strategie und den dazugehörigen Erfolgen.",
            "Victory conditions & campaign",
            "All victory conditions or campaign missions with strategy and the achievements that belong to them."),

        // Sports / racing / fighting / roguelike / cards
        new("modes", "mdi:view-grid",
            "Spielmodi",
            "Alle Spielmodi (z. B. Karriere, Ultimate Team, Online-Saisons), was sie bieten und welche Erfolge dort stecken.",
            "Game modes",
            "All game modes (e.g. career, Ultimate Team, online seasons), what they offer and which achievements they hold."),
        new("skills", "mdi:gamepad-variant",
            "Steuerung, Tricks & Skill-Moves",
            "Steuerung, fortgeschrittene Techniken und Tricks, die man für Erfolge und Siege braucht.",
            "Controls, tricks & skill moves",
            "Controls, advanced techniques and tricks needed for achievements and wins."),
        new("tactics", "mdi:strategy",
            "Taktiken & Formationen",
            "Starke Taktiken, Formationen bzw. Aufstellungen und Einstellungen – für Offline- und Online-Spiele.",
            "Tactics & formations",
            "Strong tactics, formations or line-ups and settings – for offline and online matches."),
        new("roster", "mdi:account-star",
            "Beste Spieler, Teams & Talente",
            "Die besten Spieler, Teams und Talente (auch günstige Geheimtipps) für jeden Spielmodus.",
            "Best players, teams & talents",
            "The best players, teams and talents (including cheap hidden gems) for every game mode."),
        new("cars", "mdi:car-wrench",
            "Fahrzeuge, Upgrades & Tuning",
            "Die besten Fahrzeuge je Klasse, Upgrade-Reihenfolge und Tuning-Setups.",
            "Cars, upgrades & tuning",
            "The best cars per class, upgrade order and tuning setups."),
        new("tracks", "mdi:road-variant",
            "Strecken & Renn-Strategien",
            "Strecken, Ideallinien, Abkürzungen und Renn-Strategien für Meisterschaften und Lizenzen.",
            "Tracks & race strategies",
            "Tracks, racing lines, shortcuts and race strategies for championships and licences."),
        new("characters", "mdi:account-group-outline",
            "Charaktere & Klassen",
            "Alle spielbaren Charaktere bzw. Klassen mit Freischaltung, Stärken, Spielweise und Tier-Einschätzung.",
            "Characters & classes",
            "All playable characters or classes with unlock, strengths, playstyle and tier rating."),
        new("combos", "mdi:lightning-bolt",
            "Kombos & Techniken",
            "Wichtige Kombos und Techniken je Charakter, Grundlagen und Fortgeschrittenes.",
            "Combos & techniques",
            "Important combos and techniques per character, basics and advanced."),
        new("metaprogression", "mdi:lock-open-variant",
            "Freischaltungen & Meta-Fortschritt",
            "Alles, was man dauerhaft freischaltet (Charaktere, Items, Upgrades), in welcher Reihenfolge es sich lohnt und wie man es schnell bekommt.",
            "Unlocks & meta progression",
            "Everything that is unlocked permanently (characters, items, upgrades), in which order it is worth it and how to get it quickly."),
        new("decks", "mdi:cards",
            "Decks, Karten & Synergien",
            "Starke Decks und Karten, Synergien, Archetypen und wie man sie früh aufbaut.",
            "Decks, cards & synergies",
            "Strong decks and cards, synergies, archetypes and how to build them early."),

        // Core – again for every game, at the end of the guide
        new("difficulty", "mdi:trophy-variant",
            "Schwierigkeitsgrade, New Game+ & Challenges",
            "Schwierigkeitsgrad-Erfolge (Taktiken für den höchsten Grad, z. B. Mission für Mission), was in New Game+ übernommen wird, Challenge- und Speedrun-Erfolge.",
            "Difficulty levels, New Game+ & challenges",
            "Difficulty achievements (tactics for the highest level, e.g. mission by mission), what carries over into New Game+, challenge and speedrun achievements."),
        new("online", "mdi:account-group",
            "Multiplayer- & Online-Erfolge",
            "Mehrspieler-, Koop- und Online-Erfolge, Fortschritt im Mehrspieler, wie man sie am schnellsten bekommt und ob Server abgeschaltet werden könnten.",
            "Multiplayer & online achievements",
            "Multiplayer, co-op and online achievements, multiplayer progression, how to get them fastest and whether servers might be shut down."),
        new("dlc", "mdi:puzzle",
            "DLC & Erweiterungen",
            "Inhalte und Erfolge der DLCs/Erweiterungen, wann man sie am besten spielt und was sie freischalten.",
            "DLC & expansions",
            "Content and achievements of the DLCs/expansions, when to play them best and what they unlock."),
        new("bugs", "mdi:bug",
            "Bekannte Bugs & verbuggte Erfolge",
            "Bekannte Bugs, verbuggte oder schwer auslösbare Erfolge und erprobte Workarounds (mit Patch-Stand).",
            "Known bugs & bugged achievements",
            "Known bugs, bugged or hard-to-trigger achievements and proven workarounds (with patch state)."),
    ];

    private static readonly ProfileDefinition[] ProfileDefinitions =
    [
        new("rpg", "Rollenspiel / Action-RPG / JRPG", "Role-playing / action RPG / JRPG", "Final Fantasy, The Witcher, Baldur's Gate",
            ["leveling", "weapons", "armor", "builds", "bestiary", "quests"], ["earlyop", "regions", "companions", "crafting", "endings", "collectibles", "puzzles"]),
        new("openworld", "Open World", "Open world", "Elden Ring, Horizon, GTA",
            ["regions", "collectibles", "quests"], ["vehicles", "crafting", "earlyop", "puzzles"]),
        new("shooter", "Shooter mit Kampagne (Ego/Third Person)", "Shooter with a campaign (first/third person)", "Call of Duty, Doom, Halo",
            ["walkthrough", "weapons", "difficulty"], ["collectibles", "online", "tactics"]),
        new("soulslike", "Soulslike / Action-Boss-Spiel", "Soulslike / action boss game", "Dark Souls, Lies of P, Nioh",
            ["bestiary", "builds", "weapons"], ["armor", "regions", "difficulty", "earlyop"]),
        new("metroidvania", "Metroidvania / Plattformer", "Metroidvania / platformer", "Hollow Knight, Ori, Celeste",
            ["abilities", "regions", "collectibles"], ["bestiary", "difficulty"]),
        new("management", "Aufbau / Management / Simulation / Tycoon", "Building / management / simulation / tycoon", "Little Big Workshop, Planet Coaster, Two Point Hospital",
            ["economy", "production", "research"], ["scenarios", "earlyop"]),
        new("strategy", "Strategie / 4X / Echtzeitstrategie", "Strategy / 4X / real-time strategy", "Civilization, Age of Empires, Total War",
            ["factions", "openings", "victory"], ["economy", "research", "scenarios"]),
        new("survival", "Survival / Crafting", "Survival / crafting", "Valheim, Subnautica, The Forest",
            ["basebuilding", "crafting", "bestiary"], ["regions", "earlyop"]),
        new("roguelike", "Roguelike / Roguelite", "Roguelike / roguelite", "Hades, Dead Cells, Vampire Survivors",
            ["metaprogression", "builds", "characters"], ["bestiary", "earlyop"]),
        new("sports", "Sport", "Sports", "EA SPORTS FC, NBA 2K, Rocket League",
            ["modes", "skills", "tactics"], ["roster", "online"]),
        new("racing", "Rennspiel", "Racing game", "Forza, Gran Turismo, F1",
            ["cars", "tracks"], ["modes", "online"]),
        new("fighting", "Fighting-Game", "Fighting game", "Tekken, Street Fighter, Mortal Kombat",
            ["characters", "combos"], ["modes", "online"]),
        new("adventure", "Adventure / Puzzle / Story / Horror", "Adventure / puzzle / story / horror", "Life is Strange, The Talos Principle, Resident Evil",
            ["walkthrough", "endings"], ["collectibles", "bestiary", "puzzles"]),
        new("cards", "Karten- / Deckbuilding-Spiel", "Card / deck-building game", "Slay the Spire, Hearthstone, Inscryption",
            ["decks"], ["characters", "metaprogression"]),
        new("multiplayer", "Online- / Live-Service-Spiel", "Online / live-service game", "Destiny, Warframe, Sea of Thieves",
            ["online", "modes"], ["characters", "builds"]),
    ];

    /// <summary>The fixed topic list in the language of the guide.</summary>
    public static IReadOnlyList<GuideTopic> Topics(string language) =>
        TopicDefinitions
            .Select(t => language == Lang.English
                ? new GuideTopic(t.Id, t.TitleEn, t.Icon, t.BriefEn)
                : new GuideTopic(t.Id, t.TitleDe, t.Icon, t.BriefDe))
            .ToList();

    /// <summary>The genre profiles in the language of the guide (ids, examples and topic ids are the same in both).</summary>
    public static IReadOnlyList<GenreProfile> Profiles(string language) =>
        ProfileDefinitions
            .Select(p => new GenreProfile(p.Id, language == Lang.English ? p.NameEn : p.NameDe, p.Examples, p.Required, p.Recommended))
            .ToList();

    internal sealed record LorePart(string Title, string Blurb, string Brief);

    // The four parts of the optional "Story & Lore" tab, in display order. Each is one research call.
    internal static LorePart[] LoreParts(string language) => language == Lang.English ? LorePartsEnglish : LorePartsGerman;

    private static readonly LorePart[] LorePartsGerman =
    [
        new(
            "Die Story einfach erklärt",
            "Die komplette Handlung in einfacher Sprache – mit allen Wendungen und dem Ende.",
            """
            Erkläre die komplette Handlung von Anfang bis Ende so, dass sie auch jemand versteht, der das Spiel nur halb verfolgt hat.
            - Beginne mit einer "story"-Section „Kurzfassung“ (1–2 Absätze).
            - Gliedere danach nach den Akten bzw. Kapiteln des Spiels: pro Akt/Kapitel genau eine "story"-Section
              (title z. B. „Akt 2 – Titel“ oder „Kapitel 7 – Titel“) mit 3–6 Absätzen.
            - Erkläre Fachbegriffe und Namen beim ersten Auftreten kurz in einfachen Worten, und erkläre WARUM Figuren handeln
              und was eine Wendung bedeutet – nicht nur WAS passiert.
            - Das Ende (bei mehreren Enden: jedes, plus wie man es erreicht) und alle großen Wendungen gehören ausdrücklich hinein.
            - Ist das Spiel Teil einer Reihe, ordne es in 1–2 Sätzen ein (was davor geschah), ohne andere Spiele auszubreiten.
            """),
        new(
            "Charaktere & Beziehungen",
            "Wer ist wer: Rolle, Hintergrund, Motive und die Beziehungen der Figuren untereinander.",
            """
            Erstelle ein "cards_grid" (columns 2) mit einer Karte pro wichtiger Figur (Hauptfiguren, wichtige Nebenfiguren,
            Gegenspieler; typisch 6–14 Karten):
            - title = Name der Figur (wie in der deutschen Fassung; Originalname höchstens hier einmal in Klammern), badgeText = Rolle (z. B. Protagonist, Gegenspieler), badgeVariant: s = Hauptfigur,
              a = wichtige Nebenfigur, b = Randfigur.
            - introText = Rolle, Hintergrund und Motive in 2–4 Sätzen.
            - bullets = Beziehungen zu anderen Figuren: pro Bullet icon (z. B. "mdi:account-heart"), title „Beziehung zu <Figur>“
              mit der Art der Beziehung, body 1–2 Sätze inklusive Entwicklung im Spielverlauf.
            Danach – empfohlen bei vielen Figuren – eine "checklist_table" mit "checkable": false als Beziehungsübersicht:
            Spalten figur_a (Figur A, isRowHeader), beziehung (Beziehung), figur_b (Figur B), hinweis (Hinweis / Entwicklung);
            jede Zeile mit eindeutiger "itemId" in kebab-case und "filterCategory" aus "categories"
            (z. B. Familie, Freundschaft, Liebe, Feindschaft, Allianz).
            """),
        new(
            "Welt, Fraktionen & Begriffe",
            "Schauplatz, Organisationen und ein Glossar der wichtigsten Spielbegriffe.",
            """
            Beschreibe Welt und Hintergründe:
            - Eine "story"-Section „Die Welt im Überblick“ (2–4 Absätze: Setting, Geschichte der Welt, Besonderheiten wie Technik
              oder Magie).
            - Ein "cards_grid" (columns 2 oder 3) für Fraktionen, Organisationen, Völker und wichtige Orte (introText plus bullets
              mit Zielen, Anführern und Verbindungen untereinander).
            - Eine "checklist_table" mit "checkable": false als Glossar: Spalten begriff (Begriff DE / EN, isRowHeader),
              bedeutung (Bedeutung), kategorie (z. B. Ort, Organisation, Technik, Magie, Ereignis); 15–40 Einträge mit allen
              wichtigen Spielbegriffen, jede Zeile mit eindeutiger "itemId" in kebab-case.
            """),
        new(
            "Insiderwissen & Entwicklung",
            "Easter Eggs, Anspielungen, geschnittene Inhalte und die Entstehungsgeschichte des Spiels.",
            """
            Sammle Insiderwissen und Hintergründe zur Entstehung:
            - Easter Eggs und versteckte Hinweise, Referenzen und Anspielungen (andere Spiele, Filme, Bücher, frühere Teile),
              geschnittene oder veränderte Inhalte.
            - Entwicklungsgeschichte (Studio, Team, Entwicklungszeit, Engine, Meilensteine, Verschiebungen), Behind-the-Scenes-
              Geschichten, Verkaufs- und Rezeptionsfakten (Verkaufszahlen, Wertungen, Auszeichnungen, Kontroversen).
            - Form: "story"-Sections für zusammenhängende Themen (z. B. Entwicklungsgeschichte), "cards_grid" für Listen
              (Easter Eggs, Referenzen, Trivia; pro Karte bullets mit: wo / was / warum interessant).
            STRENGE QUELLENREGEL für diesen Teil: Nimm nur belegte Fakten auf (Entwickler-Interviews, Making-ofs, Wikis,
            Fachpresse). Erfinde KEINE Anekdoten, Zitate, Zahlen oder Easter Eggs. Ist etwas nicht belegt oder nur ein Gerücht,
            schreibe „nicht belegt“ bzw. „Gerücht (nicht bestätigt)“. Nenne die Quelle kurz in Klammern
            (z. B. „laut Interview mit dem Director, IGN 2020“). Lieber weniger Einträge, dafür belegt.
            """)
    ];

    private static readonly LorePart[] LorePartsEnglish =
    [
        new(
            "The story explained simply",
            "The complete plot in plain language – with all twists and the ending.",
            """
            Explain the complete plot from start to finish so that even someone who only half followed the game understands it.
            - Start with a "story" section "In short" (1–2 paragraphs).
            - Then follow the acts or chapters of the game: exactly one "story" section per act/chapter
              (title e.g. "Act 2 – Title" or "Chapter 7 – Title") with 3–6 paragraphs.
            - Explain technical terms and names briefly in simple words when they first appear, and explain WHY characters act
              and what a twist means – not only WHAT happens.
            - The ending (with several endings: each one, plus how to reach it) and all big twists explicitly belong in it.
            - If the game is part of a series, place it in 1–2 sentences (what happened before) without spreading out about other games.
            """),
        new(
            "Characters & relationships",
            "Who is who: role, background, motives and the relationships between the characters.",
            """
            Create a "cards_grid" (columns 2) with one card per important character (main characters, important supporting
            characters, antagonists; typically 6–14 cards):
            - title = name, badgeText = role (e.g. protagonist, antagonist), badgeVariant: s = main character,
              a = important supporting character, b = minor character.
            - introText = role, background and motives in 2–4 sentences.
            - bullets = relationships to other characters: per bullet an icon (e.g. "mdi:account-heart"), title "Relationship with <character>"
              naming the kind of relationship, body 1–2 sentences including how it develops during the game.
            Then – recommended with many characters – a "checklist_table" with "checkable": false as a relationship overview:
            columns figur_a (Character A, isRowHeader), beziehung (Relationship), figur_b (Character B), hinweis (Note / development);
            every row with a unique "itemId" in kebab-case and a "filterCategory" from "categories"
            (e.g. Family, Friendship, Love, Enmity, Alliance).
            """),
        new(
            "World, factions & terms",
            "Setting, organisations and a glossary of the most important game terms.",
            """
            Describe the world and its background:
            - A "story" section "The world at a glance" (2–4 paragraphs: setting, history of the world, particularities such as
              technology or magic).
            - A "cards_grid" (columns 2 or 3) for factions, organisations, peoples and important places (introText plus bullets
              with goals, leaders and connections to each other).
            - A "checklist_table" with "checkable": false as a glossary: columns begriff (Term, isRowHeader),
              bedeutung (Meaning), kategorie (e.g. Place, Organisation, Technology, Magic, Event); 15–40 entries covering all
              important game terms, every row with a unique "itemId" in kebab-case.
            """),
        new(
            "Insider knowledge & development",
            "Easter eggs, references, cut content and the making of the game.",
            """
            Collect insider knowledge and background on how the game was made:
            - Easter eggs and hidden hints, references and allusions (other games, films, books, earlier instalments),
              cut or changed content.
            - Development history (studio, team, development time, engine, milestones, delays), behind-the-scenes
              stories, sales and reception facts (sales figures, ratings, awards, controversies).
            - Form: "story" sections for connected topics (e.g. development history), "cards_grid" for lists
              (easter eggs, references, trivia; per card bullets with: where / what / why it is interesting).
            STRICT SOURCE RULE for this part: include only documented facts (developer interviews, making-ofs, wikis,
            specialist press). Do NOT invent anecdotes, quotes, numbers or easter eggs. If something is not documented or only a
            rumour, write "not documented" or "rumour (unconfirmed)". Name the source briefly in brackets
            (e.g. "according to an interview with the director, IGN 2020"). Fewer entries that are documented are better.
            """)
    ];

    // ---- Texts that end up inside a guide or on its card, in the language of the guide -------------------------------------

    /// <summary>Card fields of a game that has no researched values yet (and the defaults when the AI left a field out).</summary>
    public sealed record GameDefaults(string Subtitle, string EstimatedHours, string MissablesInfo, string DifficultyInfo, string GenreTags);

    public static GameDefaults DefaultsFor(string language) => language == Lang.English
        ? new GameDefaults("100% Master Companion Guide", "Open", "Check the guide notes", "100% checklist", "100% Guide")
        : new GameDefaults("100% Master Companion Guide", "Offen", "Prüfe Guide-Hinweise", "100% Checkliste", "100% Guide");

    /// <summary>Defaults of the blueprint when the AI did not write the field (the AI-researched guide card).</summary>
    public static GameDefaults BlueprintDefaultsFor(string language) => language == Lang.English
        ? new GameDefaults("100% Master Companion · Roadmap, collectibles, builds & achievements", "Open", "Check the roadmap", "100% complete guide", "100% Guide")
        : new GameDefaults("100% Master Companion · Roadmap, Sammelobjekte, Builds & Achievements", "Offen", "Prüfe Roadmap", "100% Komplett-Guide", "100% Guide");

    /// <summary>The same set in the other language is recognised as "still the default" (see ApiEndpoints: adopting the language of an empty tile).</summary>
    public static bool IsDefaultCardText(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        new[] { "de", "en" }.Any(l =>
        {
            var d = DefaultsFor(l);
            var b = BlueprintDefaultsFor(l);
            return new[] { d.Subtitle, d.EstimatedHours, d.MissablesInfo, d.DifficultyInfo, d.GenreTags, b.Subtitle, b.MissablesInfo, b.DifficultyInfo }.Contains(value.Trim());
        });

    public static string StarterTabTitle(string language) => language == Lang.English ? "1. Overview & notes" : "1. Übersicht & Notizen";

    public static string StarterAlertTitle(string gameTitle, string language) =>
        language == Lang.English ? $"{gameTitle} — ready for your 100% guide!" : $"{gameTitle} — Bereit für deinen 100%-Guide!";

    public static string StarterAlertText(string language) => language == Lang.English
        ? "This game was created successfully. Use “Complete the guide” at the top right to have the AI research farming spots, weapons, bestiary or achievements from the internet automatically!"
        : "Dieses Spiel wurde erfolgreich angelegt. Klicke oben rechts auf „Guide vervollständigen“, um automatisch Farming-Spots, Waffen, Bestiarium oder Achievements aus dem Internet recherchieren zu lassen!";

    public static string FailedChapterTitle(string language) =>
        language == Lang.English ? "This chapter could not be researched" : "Dieses Kapitel konnte nicht recherchiert werden";

    /// <summary>
    /// Placeholders written before a failed chapter remembered its topic ended with an instruction to copy the topic into "Add chapter":
    /// "{our explanation}{separator}{topic}". Only used to recognise those old placeholders (see <see cref="FailedChapters"/>).
    /// </summary>
    internal static readonly string[] LegacyFailedChapterSeparators =
    [
        " – Lösche den Tab nicht, sondern ergänze ihn über „Tab ergänzen“ mit dem Thema: ",
        " – Do not delete the tab, add it again with “Add chapter” using this topic: "
    ];

    public static string FailedLoreTitle(string partTitle, string language) =>
        language == Lang.English ? $"“{partTitle}” could not be researched" : $"„{partTitle}“ konnte nicht recherchiert werden";

    public static string FailedLoreText(string error, string language) => language == Lang.English
        ? $"{error} – Use “Research Story & Lore again” to create the tab again."
        : $"{error} – Starte „Story & Lore neu recherchieren“, um den Reiter erneut zu erstellen.";

    public static string IntroAlertTitle(string language) =>
        language == Lang.English ? "What 100% means in this game" : "Was 100% in diesem Spiel bedeutet";

    public static string SourceThinTitle(bool isNew, string language) => language == Lang.English
        ? (isNew ? "Just released – few sources so far" : "Thin sources")
        : (isNew ? "Frisch erschienen – noch wenige Quellen" : "Dünne Quellenlage");

    public static string SourceThinText(string language) => language == Lang.English
        ? "There are no reliable guides for some points yet. Such places are marked “not documented yet” instead of guessed. Add them later with “Add chapter” once the community has found out more."
        : "Zu manchen Punkten gibt es noch keine verlässlichen Guides. Solche Stellen sind als „noch nicht dokumentiert“ markiert statt geraten. Ergänze sie später über „Tab ergänzen“, wenn die Community mehr herausgefunden hat.";

    // The Steam achievement chapter (built from Steam's official list)
    public static string AchievementTabTitle(int sortOrder, string language) =>
        language == Lang.English ? $"{sortOrder}. All Steam achievements" : $"{sortOrder}. Alle Steam-Erfolge";

    public static string AchievementTabSlugSuffix(string language) => language == Lang.English ? "steam-achievements" : "steam-erfolge";

    public static string AchievementAlertTitle(int count, string language) =>
        language == Lang.English ? $"All {count} official Steam achievements" : $"Alle {count} offiziellen Steam-Erfolge";

    public static string AchievementAlertText(string language) => language == Lang.English
        ? "This list comes straight from Steam. Achievements you unlock are ticked off here automatically."
        : "Diese Liste kommt direkt von Steam. Erfolge, die du freischaltest, werden hier automatisch abgehakt.";

    public static string AchievementCaption(string language) => language == Lang.English ? "Steam achievements" : "Steam-Erfolge";
    public static string AchievementSearch(string language) => language == Lang.English ? "Search achievement..." : "Erfolg suchen...";
    public static string AllCategories(string language) => language == Lang.English ? "All categories" : "Alle Kategorien";
    public static string MissableAndAnytime(string language) => language == Lang.English ? "Missable & anytime" : "Verpassbar & jederzeit";
    public static string Missable(string language) => language == Lang.English ? "Missable" : "Verpassbar";
    public static string Anytime(string language) => language == Lang.English ? "Anytime" : "Jederzeit";
    public static string OtherCategory(string language) => language == Lang.English ? "Miscellaneous" : "Sonstiges";
    public static string ColumnAchievement(string language) => language == Lang.English ? "Achievement" : "Erfolg";
    public static string ColumnCondition(string language) => language == Lang.English ? "Condition" : "Bedingung";
    public static string ColumnCategory(string language) => language == Lang.English ? "Category" : "Kategorie";
    public static string ColumnTip(string language) => language == Lang.English ? "Tip" : "Tipp";
    public static string SecretPrefix(string language) => language == Lang.English ? "(Secret)" : "(Geheim)";
    public static string SecretUnknown(string language) => language == Lang.English ? "(Secret) condition not documented yet" : "(Geheim) Bedingung noch nicht dokumentiert";
    public static string UnknownSource(string language) => language == Lang.English ? "unknown" : "unbekannt";
    public static string EstablishedSource(string language) => language == Lang.English ? "established" : "etabliert";
    public static string UnknownGenre(string language) => language == Lang.English ? "genre not known yet" : "Genre noch unbekannt";
}
