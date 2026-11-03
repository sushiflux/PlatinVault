using System.Text;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// The fixed, small list of groups the chapters of a guide are listed under in the side navigation ("Story &amp; Welt", "Kampf &amp;
/// Taktik", "Fortschritt &amp; Freischaltungen", ...). The keys are the stable contract (database value, DTO, frontend); the labels exist in both app languages
/// (<see cref="Label"/>, also served by GET /api/guide/groups). Every standard topic belongs to exactly one group
/// (<see cref="ForTopic"/>); an older or custom chapter is placed by its title (<see cref="Classify"/>); a chapter may carry an explicit
/// <c>GroupKey</c> (set by the AI when it creates the chapter, or a user's choice), which always wins (<see cref="Resolve"/>).
/// </summary>
public static class GuideGroups
{
    public const string Start = "start";
    public const string Story = "story";
    public const string Characters = "characters";
    public const string Combat = "combat";
    public const string Progress = "progress";
    public const string Explore = "explore";
    public const string Economy = "economy";
    public const string Extras = "extras";

    /// <summary>The groups in the order they appear in the navigation.</summary>
    public static readonly IReadOnlyList<string> Keys = [Start, Story, Characters, Combat, Progress, Explore, Economy, Extras];

    public static bool IsValid(string? key) => key is not null && Keys.Contains(key);

    /// <summary>The label of a group in the given language ("de" / "en"; anything else = German).</summary>
    public static string Label(string key, string? language) => ServerTexts.Groups.Label(key).Resolve(Lang.NormalizeOr(language));

    // ---- Standard topics --------------------------------------------------------------------------------------------------

    // Every id of AiGuideGeneratorService.Topics (the standard topics); a test fails when a topic has no entry here
    private static readonly Dictionary<string, string> TopicGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        // Core
        ["roadmap"] = Start,
        ["tips"] = Start,
        ["earlyop"] = Start,
        ["walkthrough"] = Start,
        // Role-playing / open world / action
        ["regions"] = Story,
        ["leveling"] = Progress,
        ["weapons"] = Combat,
        ["armor"] = Combat,
        ["builds"] = Combat,
        ["abilities"] = Progress,
        ["bestiary"] = Combat,
        ["collectibles"] = Explore,
        ["quests"] = Explore,
        ["puzzles"] = Explore,
        ["companions"] = Characters,
        ["crafting"] = Economy,
        ["basebuilding"] = Economy,
        ["vehicles"] = Explore,
        ["endings"] = Story,
        // Management / simulation / strategy
        ["economy"] = Economy,
        ["production"] = Economy,
        ["research"] = Economy,
        ["scenarios"] = Story,
        ["factions"] = Characters,
        ["openings"] = Combat,
        ["victory"] = Story,
        // Sports / racing / fighting / roguelike / cards
        ["modes"] = Start,
        ["skills"] = Start,
        ["tactics"] = Combat,
        ["roster"] = Characters,
        ["cars"] = Progress,
        ["tracks"] = Explore,
        ["characters"] = Characters,
        ["combos"] = Combat,
        ["metaprogression"] = Progress,
        ["decks"] = Combat,
        // Core, at the end of the guide
        ["difficulty"] = Extras,
        ["online"] = Extras,
        ["dlc"] = Extras,
        ["bugs"] = Extras,
    };

    /// <summary>Every standard topic id with its group (for tests and the topics endpoint).</summary>
    public static IReadOnlyDictionary<string, string> StandardTopicGroups => TopicGroups;

    /// <summary>The group of a standard topic ("roadmap", "weapons", ...); null for an unknown id and for "extra" (a game-specific chapter).</summary>
    public static string? ForTopic(string? topicId) =>
        topicId is not null && TopicGroups.TryGetValue(topicId, out var group) ? group : null;

    // ---- Chapters by title ------------------------------------------------------------------------------------------------

    // First matching rule wins, so the order is part of the logic: the more specific words come first. Keywords are written
    // normalized (lower case, ae/oe/ue/ss for umlauts) and match inside the normalized title; "^word" = at the start of a word,
    // "word$" = at the end of a word, "^word$" = the whole word. German and English words sit side by side because titles are
    // bilingual ("Gebiete & Karte (Regions & Map)"). Why substrings by default: German compounds ("Superbosse", "Nebenquests").
    private static readonly (string Group, string[] Keywords)[] Rules =
    [
        // Online/DLC/bugs/difficulty/achievements: the "rest" of a guide, recognised first because their words are unmistakable
        (Extras, ["bug", "workaround", "online", "multiplayer", "koop", "co op", "^coop", "dlc", "erweiterung", "expansion", "schwierigkeit", "difficulty", "new game", "challenge", "speedrun", "achievement", "erfolg", "trophae", "trophy", "trophies"]),
        // Getting started: plan, tips, walkthrough, controls, game modes
        (Start, ["roadmap", "fahrplan", "verpassbar", "missable", "^tipps", "^tips", "komplettloesung", "walkthrough", "loesung", "^frueh", "^early", "power spike", "powerspike", "steuerung", "controls", "einsteiger", "beginner", "spielmodi", "game modes", "^modi$", "^modes$", "uebersicht", "overview", "grundlagen", "basics", "^start"]),
        // People: characters, companions, factions, teams
        (Characters, ["charakter", "character", "begleiter", "companion", "beziehung", "relationship", "^figuren", "klassen", "^classes", "fraktion", "faction", "voelker", "^teams", "talente", "^party"]),
        // Story and world: plot, areas, endings, campaign
        (Story, ["story", "handlung", "geschichte", "^lore", "^welt", "world", "^enden", "ending", "entscheidung", "decision", "gebiet", "^region", "^area", "^karte$", "^map$", "^maps$", "kampagne", "campaign", "szenario", "scenario", "mission", "siegbedingung", "victory", "^akt "]),
        // Economy and crafting: before combat, so "Crafting, Ressourcen & Upgrades" and "Forschung, Upgrades & Freischaltungen" land here
        (Economy, ["forschung", "research", "crafting", "handwerk", "basebuilding", "base building", "basisbau", "ueberleben", "survival", "aufbau", "^dorf", "village", "produktion", "production", "automat", "ablauf", "effizienz", "efficiency", "wirtschaft", "economy", "^geld", "money", "ressource", "resource", "^kochen", "cooking"]),
        // Combat and tactics: enemies, equipment, builds, abilities, tactics, combos, decks, openings
        (Combat, ["boss", "gegner", "enem", "bestiar", "monster", "waffe", "weapon", "ruestung", "armor", "armour", "ausruestung", "equipment", "werkzeug", "^tool", "build", "skill", "faehigkeit", "abilit", "taktik", "tactic", "formation", "kombo", "combo", "technik", "^deck", "synerg", "eroeffnung", "opening", "kampf", "combat", "esper", "summon"]),
        // Progression and unlocks: levels, experience, ranks, tiers, milestones, upgrades - after combat, so "Waffen-Upgrades" stays a combat chapter; a
        // progression chapter of a game without combat ("Fabrikstufe & Erfahrung" in Little Big Workshop) lands here instead of under "Kampf"
        (Progress, ["level", "^xp", "erfahrung", "experience", "farm", "freischalt", "unlock", "meta", "fortschritt", "progress", "tuning", "upgrade", "aufstieg", "^rang", "^rank", "prestige", "stufe", "meilenstein", "milestone"]),
        // Collecting and exploring: collectibles, quests, secrets, vehicles, tracks
        (Explore, ["sammel", "collect", "fundort", "location", "quest", "aktivitaet", "activit", "raetsel", "riddle", "puzzle", "geheimnis", "secret", "erkund", "explor", "strecke", "^track", "fahrzeug", "vehicle", "reittier", "^mount", "schnellreise", "fast travel", "angeln", "fishing", "auftrag", "auftraege", "contract", "jagd", "hunting", "minispiel", "mini game"]),
    ];

    /// <summary>
    /// The group of a chapter, judged by its title alone: a keyword list in both languages (see the rules above), deterministic, no AI.
    /// Titles are bilingual ("Gebiete &amp; Karte (Regions &amp; Map)"): the part outside the brackets is tried first, then the part in the
    /// brackets, then the whole text; a title no rule recognises belongs to <see cref="Extras"/>. A leading chapter number ("3. ") is ignored.
    /// <paramref name="language"/> is the language of the guide; the keywords of both languages are always matched, so it only exists to
    /// keep the call the same for callers that know it.
    /// </summary>
    public static string Classify(string? title, string? language = null)
    {
        _ = language;
        if (string.IsNullOrWhiteSpace(title))
        {
            return Extras;
        }

        var main = new StringBuilder();
        var inner = new StringBuilder();
        var depth = 0;
        foreach (var ch in title)
        {
            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')' && depth > 0)
            {
                depth--;
            }
            else
            {
                (depth > 0 ? inner : main).Append(ch);
            }
        }

        foreach (var text in new[] { main.ToString(), inner.ToString(), title })
        {
            var normalized = Normalize(text);
            if (normalized.Length == 0)
            {
                continue;
            }
            foreach (var (group, keywords) in Rules)
            {
                if (keywords.Any(keyword => Matches(normalized, keyword)))
                {
                    return group;
                }
            }
        }
        return Extras;
    }

    /// <summary>
    /// What the API reports for a chapter: its explicit key when it has a valid one, else the group of its title.
    /// <paramref name="title"/> is the ORIGINAL title (not a translation), so a chapter keeps its group in every language.
    /// </summary>
    public static string Resolve(string? storedKey, string? title, string? language = null)
    {
        if (!IsValid(storedKey))
        {
            return Classify(title, language);
        }
        // Until 2026-11-02 "combat" was "Kampf & Fortschritt" and also held the progression chapters. Since "progress" is its own group, a stored
        // "combat" is split by the title: a chapter whose title reads as progression ("Fabrikstufe & Erfahrung", "Level-Empfehlungen") moves to
        // "progress", everything else stays. Nothing in the database is rewritten for that.
        if (storedKey == Combat && Classify(title, language) == Progress)
        {
            return Progress;
        }
        return storedKey!;
    }

    /// <summary>
    /// The key to store on a NEW chapter: the group of its standard topic (<c>ChapterPlan.TopicId</c>; "extra" or a topic of a custom
    /// chapter has none), else the group of its title. This is the one call chapter creation should use:
    /// <c>tab.GroupKey = GuideGroups.KeyFor(plan.TopicId, plan.Title, language)</c>.
    /// </summary>
    public static string KeyFor(string? topicId, string? title, string? language = null) =>
        ForTopic(topicId) ?? Classify(title, language);

    // ---- helpers ----------------------------------------------------------------------------------------------------------

    /// <summary>Lower case, umlauts as ae/oe/ue/ss, every other non-letter and non-digit as a space (padded with one space each side), a leading "3." removed.</summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(" ");
        var number = true;
        foreach (var raw in text.ToLowerInvariant().Trim())
        {
            if (number && (char.IsDigit(raw) || raw is '.' or ')' or ' '))
            {
                // A leading chapter number such as "3. " or "12) "
                continue;
            }
            number = false;
            switch (raw)
            {
                case 'ä': builder.Append("ae"); break;
                case 'ö': builder.Append("oe"); break;
                case 'ü': builder.Append("ue"); break;
                case 'ß': builder.Append("ss"); break;
                default: builder.Append(char.IsLetterOrDigit(raw) ? raw : ' '); break;
            }
        }
        // Collapse runs of spaces so "co  op" and "co-op" are the same
        var collapsed = System.Text.RegularExpressions.Regex.Replace(builder.ToString(), " +", " ");
        return collapsed.EndsWith(' ') ? collapsed : collapsed + " ";
    }

    private static bool Matches(string normalized, string keyword)
    {
        var atStart = keyword.StartsWith('^');
        var atEnd = keyword.EndsWith('$');
        var core = keyword.TrimStart('^').TrimEnd('$');
        var needle = (atStart ? " " : string.Empty) + core + (atEnd ? " " : string.Empty);
        return normalized.Contains(needle, StringComparison.Ordinal);
    }
}
