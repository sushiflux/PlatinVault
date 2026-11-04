using System.Text;
using System.Text.RegularExpressions;

namespace Companion.Api.Services;

/// <summary>
/// Recognises achievements that are earned by completing a NUMBERED story chapter ("Schließe Kapitel 5 ab.", "Kapitel 1 abgeschlossen",
/// "Complete Chapter 3"). The owner's rule (2026-11-04): these are always Bronze, whatever their worldwide share
/// (see <see cref="SteamLiveTrackerService.CalculateRarityTier"/>). The match is deliberately narrow: a completion verb right next to
/// "Kapitel/Chapter" plus a number (digits or Roman numerals I-XX). "Complete all chapters on Hard", "Chapter select", "Lies 5 Kapitel"
/// or "Complete 5 quests in Chapter 3" do not match. Every given text is checked on its own (name, description, English name ...), so a
/// match in any one language is enough.
/// </summary>
public static class ChapterCompletion
{
    // Digits or Roman numerals I-XX as a whole word (the text is lower case with single spaces when matched)
    private const string Number = @"(?:\d{1,3}|(?=[ivx])(?:xx|x?(?:ix|iv|v?i{0,3})))\b";
    private const string DeArticle = @"(?:(?:das|den|dieses|dein) )?";
    private const string DeAdverb = @"(?:(?:erfolgreich|komplett|vollständig|endlich) )?";

    private static readonly string[] Patterns =
    [
        // German: "Kapitel 1 abgeschlossen", "Kapitel 3 erfolgreich abschließen"
        $@"\bkapitel {Number} {DeAdverb}(?:abgeschlossen|abschlie(?:ß|ss)en|beendet|beenden|beende)\b",
        // German: "Beende Kapitel 3", "Das Kapitel ..." is not enough without the verb
        $@"\b(?:beende|beendet|beenden|abschlie(?:ß|ss)en) {DeArticle}kapitel {Number}",
        // German: "Schließe Kapitel 5 ab", "Schließe das Kapitel 5 erfolgreich ab"
        $@"\bschlie(?:ß|ss)(?:e|t|en) {DeArticle}kapitel {Number} {DeAdverb}ab\b",
        // German: "Erreiche das Ende von Kapitel 3"
        $@"\bende (?:von|vom|des) (?:dem )?kapitel {Number}",
        // English: "Complete Chapter 3", "Finished the chapter IV", "Beat Chapter 4"
        $@"\b(?:complete|completed|finish|finished|clear|cleared|beat|beaten) (?:(?:the|this|story) )*chapter {Number}",
        // English: "Chapter 3 completed", "Chapter 2 is cleared"
        $@"\bchapter {Number} (?:(?:is|was|has been|successfully|fully) )*(?:complete|completed|finished|cleared|beaten|done)\b",
    ];

    private static readonly Regex[] Matchers = Patterns
        .Select(p => new Regex(p, RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(250)))
        .ToArray();

    /// <summary>True when any of the given texts (null/blank ones are skipped) reads as the completion of a numbered chapter.</summary>
    public static bool IsChapterCompletion(params string?[] texts)
    {
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 2000) continue;
            var normalized = Normalize(text);
            foreach (var matcher in Matchers)
            {
                try
                {
                    if (matcher.IsMatch(normalized)) return true;
                }
                catch (RegexMatchTimeoutException)
                {
                    // An absurd text: treat it as no match
                }
            }
        }
        return false;
    }

    /// <summary>Lower case, every run of punctuation/quotes/other symbols becomes one space ("„Schließe Kapitel 5 ab.“" -> "schließe kapitel 5 ab").</summary>
    internal static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var space = true;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (!space)
            {
                sb.Append(' ');
                space = true;
            }
        }
        return sb.ToString().TrimEnd();
    }
}
