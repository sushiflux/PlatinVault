using System.Text.RegularExpressions;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// How many chapters a guide gets: proportionate to the game and to the research scope the player chose (Settings -> AI research ->
/// "Scope"). The ranges are a CEILING, not a target - the blueprint prompt says so, and a small game lands at the low end. Without a chosen
/// scope the guide is "detailed" (the long-standing default).
///   compact     5-8    only what matters most, topics are merged aggressively
///   detailed    8-14   one chapter per distinct content area of the game (default)
///   exhaustive  12-24  every area of a big game gets its own chapter
/// The numbers are a judgement call: a 100 % guide that needs more than ~14 chapters is split into content areas that hardly stand
/// alone, and 24 is where the chapter bar of the page stops being scannable. The minimum is only a description of a typical big game (it is
/// named in the prompt as such); nothing ever fills a guide up to it - a topic the game does not have never becomes a chapter to reach a count.
/// </summary>
public static class ChapterBudget
{
    public static (int Min, int Max) For(string? scope) => scope switch
    {
        "compact" => (5, 8),
        "exhaustive" => (12, 24),
        _ => (8, 14)
    };

    /// <summary>The scope that counts: the chosen one, else "detailed".</summary>
    public static string Effective(string? scope) => scope is "compact" or "exhaustive" ? scope : "detailed";

    public static string ScopeName(string? scope, string language) => (Effective(scope), language == Lang.English) switch
    {
        ("compact", false) => "Kompakt",
        ("compact", true) => "Compact",
        ("exhaustive", false) => "Sehr ausführlich",
        ("exhaustive", true) => "Very detailed",
        (_, false) => "Ausführlich",
        _ => "Detailed"
    };

    /// <summary>How many more chapters the guide may get (the budget minus what it already has).</summary>
    public static int Room(string? scope, int existingChapters) => Math.Max(0, For(scope).Max - existingChapters);

    /// <summary>The paragraph of the blueprint prompt that sets the size of the guide and the rules for choosing topics.</summary>
    public static string PromptNote(string? scope, string language, int existingChapters)
    {
        var (min, max) = For(scope);
        var name = ScopeName(scope, language);
        var english = language == Lang.English;
        var existing = existingChapters switch
        {
            <= 0 => english ? "The guide is new." : "Der Guide ist neu.",
            _ => english
                ? $"The guide already has {existingChapters} chapters: there is room for at most {Room(scope, existingChapters)} more."
                : $"Der Guide hat schon {existingChapters} Kapitel: Es ist noch Platz für höchstens {Room(scope, existingChapters)} weitere."
        };
        return english
            ? $"""
              Size of the guide: scope "{name}" – at most {max} chapters in total (including the extraChapters); a big game usually ends up between {min} and {max}. {existing}
              This is a ceiling, not a target, and there is NO minimum: a small or simple game gets few chapters – if it only has 4 real topics, 4 chapters are right. Never pad.
              - A topic only becomes a chapter ("applicable": true) if your research CONFIRMS that the game really has it and that it helps the
                player toward 100 %. Do not add anything "just in case" – not even because it is typical for the kind of game.
              - Merge small related topics: give the smaller topic "applicable": false and "mergeInto": "<id of the bigger topic>", and let the
                "focus" of the bigger topic cover the content as well.
              - Give every topic you take a "priority": 1 = essential for 100 %, 2 = useful, 3 = nice to have if there is room.
              The topic "walkthrough" is special: it only becomes a chapter ("applicable": true) if your research found at least one reliable, COMPLETE
              walkthrough source (a full walkthrough on a large site, an official guide, a big wiki walkthrough) – name the sources in its "focus". Without such a
              source set "applicable": false (reason: no reliable source) – never plan a walkthrough "just in case". It does NOT count against the chapter total above and always comes first in the guide.
              """
            : $"""
              Umfang des Guides: „{name}“ – höchstens {max} Kapitel insgesamt (inklusive der extraChapters); ein großes Spiel landet meist zwischen {min} und {max}. {existing}
              Das ist eine Obergrenze, kein Ziel, und es gibt KEINE Mindestzahl: Ein kleines oder einfaches Spiel bekommt wenige Kapitel – hat es nur 4 echte Themen, sind 4 Kapitel richtig. Nie auffüllen.
              - Ein Thema wird NUR dann ein Kapitel ("applicable": true), wenn deine Recherche BESTÄTIGT, dass das Spiel es wirklich hat und
                es dem Spieler zu 100 % hilft. Nimm nichts „vorsorglich“ auf – auch nicht, weil es zur Spielart typischerweise gehört.
              - Fasse kleine, verwandte Themen zusammen: Setze beim kleineren Thema "applicable": false und "mergeInto": "<id des größeren Themas>",
                und lass den "focus" des größeren Themas den Inhalt mit abdecken.
              - Gib jedem übernommenen Thema eine "priority": 1 = unverzichtbar für 100 %, 2 = nützlich, 3 = nett, wenn Platz ist.
              Das Thema "walkthrough" ist besonders: Es wird nur dann ein Kapitel ("applicable": true), wenn deine Recherche mindestens eine belastbare, VOLLSTÄNDIGE
              Komplettlösungs-Quelle gefunden hat (eine vollständige Lösung auf einer großen Seite, einen offiziellen Guide, eine große Wiki-Komplettlösung) – nenne die Quellen in seinem
              "focus". Ohne solche Quelle setze "applicable": false (Grund: keine belastbare Quelle) – plane nie „vorsorglich“ eine Komplettlösung. Es zählt NICHT zur Kapitelzahl oben und steht im Guide immer an erster Stelle.
              """;
    }

    /// <summary>
    /// Which of the candidate topics fit into <paramref name="slots"/> chapters: the most important first (priority 1 before 2 before 3, a topic
    /// typical for the genre before others, then list order). Everything fits when there is room for all.
    /// </summary>
    public static HashSet<string> Keep(IEnumerable<(string Id, bool Required, int Priority, int Order)> candidates, int slots) =>
        candidates
            .OrderBy(c => c.Priority)
            .ThenBy(c => c.Required ? 0 : 1)
            .ThenBy(c => c.Order)
            .Take(Math.Max(0, slots))
            .Select(c => c.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Chapter titles are in the language of the guide ONLY. The AI sometimes adds the other language in brackets ("Tipps &amp; Tricks (Tips &amp;
/// Tricks)"); <see cref="Clean"/> removes such a translation from new results. It is deliberately conservative: a bracket is only removed
/// when its words clearly belong to the OTHER language (judged by the vocabulary of the topic catalogue and common guide words), so
/// "Geister (Ember)" or "Speedrun (Any%)" stay as they are.
/// </summary>
public static partial class ChapterTitles
{
    [GeneratedRegex(@"^(?<main>.*\S)\s*[\(\[](?<alt>[^()\[\]]+)[\)\]]\s*$")]
    private static partial Regex TrailingBracket();

    [GeneratedRegex(@"^(?<main>.*\S)\s+[/|]\s+(?<alt>\S.*)$")]
    private static partial Regex TrailingSlash();

    [GeneratedRegex(@"\p{L}{2,}")]
    private static partial Regex Word();

    private static readonly Lazy<HashSet<string>> English = new(() => Vocabulary(Lang.English, EnglishExtra));
    private static readonly Lazy<HashSet<string>> German = new(() => Vocabulary(Lang.German, GermanExtra));

    private const string EnglishExtra =
        "the and of for to in on with all your you how what where when from best top new tips tricks guide walkthrough roadmap missable missables " +
        "enemies enemy bosses boss bestiary weapons weapon armor armour gear equipment builds build skills skill tactics strategy strategies " +
        "collectibles collectible locations location quests quest side activities crafting resources resource upgrades upgrade early game " +
        "strong power spikes farming level levelling leveling economy money endings ending decisions companions companion factions relationships " +
        "base building survival vehicles mounts travel areas map secrets secret achievements trophies village spirits cooking fishing mining shop " +
        "trading characters story world items recipes overview basics beginner expert guide known bugs bug workarounds glitches controls riddles puzzles " +
        "ghosts automation rebuilding regions order production unlocks research accessories tools";

    private const string GermanExtra =
        "und der die das den dem des für mit alle allen aller beste besten bester nach von zum zur im ein eine wie wo wann was " +
        "tipps tricks waffen rüstung rüstungen verpassbares verpassbar fahrplan gegner bosse sammelobjekte fundorte gebiete karte erfolge " +
        "trophäen geheimnisse dorf geister kochen angeln basisbau überleben fahrzeuge forschung wirtschaft geld ressourcen figuren " +
        "geschichte welt gegenstände rezepte übersicht grundlagen einsteiger profis anfänger";

    private static HashSet<string> Vocabulary(string language, string extra)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var topic in AiGuideGeneratorService.Topics(language))
        {
            foreach (Match match in Word().Matches((topic.Title + " " + topic.Brief).ToLowerInvariant()))
            {
                words.Add(match.Value);
            }
        }
        foreach (Match match in Word().Matches(extra))
        {
            words.Add(match.Value);
        }
        return words;
    }

    /// <summary>The title without a trailing translation into the other language; anything else is returned as it is (trimmed).</summary>
    public static string Clean(string title, string language)
    {
        var text = title.Trim();
        foreach (var pattern in new[] { TrailingBracket(), TrailingSlash() })
        {
            var match = pattern.Match(text);
            if (match.Success && IsOtherLanguage(match.Groups["alt"].Value, language) && !IsOtherLanguage(match.Groups["main"].Value, language))
            {
                return match.Groups["main"].Value.Trim();
            }
        }
        return text;
    }

    /// <summary>True when the words clearly belong to the language other than <paramref name="guideLanguage"/> (and none of them only to its own).</summary>
    internal static bool IsOtherLanguage(string text, string guideLanguage)
    {
        var own = guideLanguage == Lang.English ? English.Value : German.Value;
        var foreign = guideLanguage == Lang.English ? German.Value : English.Value;
        var words = Word().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToList();
        if (words.Count == 0)
        {
            return false;
        }
        var foreignOnly = words.Count(w => foreign.Contains(w) && !own.Contains(w));
        var ownOnly = words.Count(w => own.Contains(w) && !foreign.Contains(w));
        // Words both languages share ("tricks", "skills", "roadmap") say nothing; words in neither list (names like "Ember") must not outnumber the foreign ones
        var unknown = words.Count(w => !foreign.Contains(w) && !own.Contains(w));
        return foreignOnly >= 1 && ownOnly == 0 && unknown <= foreignOnly;
    }
}
