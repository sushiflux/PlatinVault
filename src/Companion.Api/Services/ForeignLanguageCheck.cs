using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// Finds sentences of a chapter that are written in the OTHER app language (an English objective in a German guide such as "Go to the town and talk to the
/// elder", or the reverse). A heuristic per sentence on the readable texts only (GuideTextExtractor: no ids, URLs or numbers):
/// <list type="bullet">
/// <item>Proper names must not count: text in brackets and quotes is removed, and every capitalized word except the first one of the sentence is ignored
/// (German nouns are capitalized too - only the lowercase function words decide, and they are what gives a language away).</item>
/// <item>A sentence counts as foreign when it has at least two foreign function words and none of the guide language, or three and at least three times as
/// many; or when it starts with a typical imperative of the other language ("Rent a Chocobo", "Geh zum Tor") and has no function word of the guide language.</item>
/// </list>
/// Used by the check of a pasted chapter (manual flow); it only warns, it never changes or rejects the answer.
/// </summary>
internal static partial class ForeignLanguageCheck
{
    [GeneratedRegex(@"(?<=[.!?…])\s+|[:;]\s+|\s+/\s+|\s+[–—]\s+|\r?\n")]
    private static partial Regex SentenceBreak();

    // Text in brackets or quotes: names and original terms, never a judgement about the language of the sentence
    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]|„[^“”]*[“”]|“[^”]*”|""[^""]*""|«[^»]*»|»[^«]*«")]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"\p{L}+(?:['’]\p{L}+)?")]
    private static partial Regex Word();

    // Only words that are unambiguous between the two languages ("in", "an", "also", "will", "so" are both German and English and are left out)
    private static readonly HashSet<string> English =
        ["the", "and", "of", "to", "with", "you", "your", "is", "are", "was", "for", "on", "at", "from", "by", "this", "that", "it", "its", "be", "or", "if",
         "then", "can", "into", "there", "where", "after", "before", "back", "a", "when", "until", "next", "through", "which", "who", "have", "has", "not"];

    private static readonly HashSet<string> German =
        ["und", "der", "die", "das", "den", "dem", "des", "ein", "eine", "einen", "einem", "einer", "mit", "für", "nicht", "ist", "sind", "wird", "werden", "auf", "von",
         "zu", "zum", "zur", "im", "ab", "nach", "bei", "oder", "wenn", "dann", "auch", "kann", "du", "dich", "dir", "dein", "deine", "deinen", "sich", "noch", "aus",
         "über", "dort", "hier", "danach", "wieder", "bis", "vor", "hinter", "durch", "gegen", "ohne", "um", "es", "sie", "er", "wo", "wie", "was", "dass"];

    private static readonly HashSet<string> EnglishImperatives =
        ["go", "talk", "speak", "rent", "stay", "defeat", "head", "find", "collect", "return", "enter", "use", "open", "follow", "take", "reach", "climb", "examine",
         "leave", "meet", "buy", "sell", "equip", "pick", "grab", "travel", "visit", "save", "rest", "obtain", "get", "kill", "beat", "fight", "complete", "continue"];

    private static readonly HashSet<string> GermanImperatives =
        ["geh", "gehe", "sprich", "rede", "besiege", "öffne", "nimm", "kaufe", "verkaufe", "folge", "kehre", "betritt", "sammle", "rüste", "kämpfe", "hole", "suche",
         "laufe", "lauf", "untersuche", "verlasse", "benutze", "nutze", "wähle", "übernachte", "miete", "speichere", "reise", "besuche", "erreiche", "klettere", "töte"];

    /// <summary>The distinct sentences of the sections that look like the other language, in document order.</summary>
    public static List<string> FindSentences(JsonArray sections, string guideLanguage)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in GuideTextExtractor.Collect(sections).Select(s => s.Text).Where(GuideTextExtractor.NeedsTranslation))
        {
            foreach (var part in SentenceBreak().Split(text))
            {
                var sentence = part.Trim();
                if (sentence.Length > 0 && IsForeign(sentence, guideLanguage) && seen.Add(sentence))
                {
                    found.Add(sentence);
                }
            }
        }
        return found;
    }

    /// <summary>True when one sentence looks like the other app language (see the class summary for the rule).</summary>
    public static bool IsForeign(string sentence, string guideLanguage)
    {
        var words = Word().Matches(Bracketed().Replace(sentence, " ")).Select(m => m.Value).ToList();
        if (words.Count < 2)
        {
            return false;
        }
        var (foreignWords, ownWords, imperatives) = guideLanguage == Lang.English ? (German, English, GermanImperatives) : (English, German, EnglishImperatives);

        // The first word always counts (it is capitalized in every language); later capitalized words are names or German nouns
        var counted = words.Where((w, i) => i == 0 || !char.IsUpper(w[0])).Select(w => w.ToLowerInvariant()).ToList();
        var foreign = counted.Count(foreignWords.Contains);
        var own = counted.Count(ownWords.Contains);

        if (foreign >= 2 && own == 0)
        {
            return true;
        }
        if (foreign >= 3 && foreign >= 3 * own)
        {
            return true;
        }
        // "Rent a Chocobo": an imperative followed by a lowercase word - not "Save-Punkt nutzen" or "Go-Kart-Rennen" (a German compound with an English part)
        return own == 0 && words.Count >= 3 && !char.IsUpper(words[1][0]) && imperatives.Contains(words[0].ToLowerInvariant());
    }
}
