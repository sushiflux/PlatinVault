using System.Globalization;

namespace Companion.Api.Localization;

/// <summary>
/// The language of the code that is running right now ("de" / "en"). One value, set once per API request by
/// <see cref="RequestLanguage"/> (Accept-Language header, else the stored <c>ui.language</c>, else German) and flowing along
/// every await of that request. Code without a request (background workers) sees the German default unless it opens its own
/// scope with <see cref="Use"/>; code that must not depend on a request passes the language explicitly.
/// Why an ambient value: texts are also produced deep inside singleton services (Twitch, IGDB, Steam) that cannot be handed a
/// request, and the language of an exception message is only needed at the moment somebody reads it (see <see cref="Msg"/>).
/// </summary>
public static class Lang
{
    public const string German = "de";
    public const string English = "en";

    /// <summary>Languages the app speaks. Same list as <c>SettingsStore.UiLanguages</c>.</summary>
    public static readonly string[] Supported = [German, English];

    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>The language for texts produced now; German when nothing set one.</summary>
    public static string Current => Ambient.Value ?? German;

    /// <summary>True when a request (or an explicit scope) has chosen the language.</summary>
    public static bool IsSet => Ambient.Value is not null;

    public static bool IsEnglish => Current == English;

    /// <summary>The German or the English text, depending on <see cref="Current"/>.</summary>
    public static string Pick(string german, string english) => IsEnglish ? english : german;

    /// <summary>
    /// "de", "de-DE", "DE" -> "de"; "en-GB" -> "en"; anything else (including null) -> null. Looser than the strict check of the
    /// settings API on purpose: this reads headers and file contents, where regional variants are normal.
    /// </summary>
    public static string? Normalize(string? value)
    {
        var primary = value?.Trim().Split('-', '_')[0].ToLowerInvariant();
        return primary is German or English ? primary : null;
    }

    /// <summary>The language, or <paramref name="fallback"/> (German when that is null too) for an unknown value.</summary>
    public static string NormalizeOr(string? value, string? fallback = null) => Normalize(value) ?? Normalize(fallback) ?? German;

    /// <summary>
    /// First supported language of an Accept-Language header, by quality ("en-US,en;q=0.9,de;q=0.8" -> "en"; "fr,de;q=0.5" -> "de").
    /// Null when the header is missing or names no supported language.
    /// </summary>
    public static string? FromAcceptLanguage(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        string? best = null;
        var bestQuality = -1.0;
        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(';', StringSplitOptions.TrimEntries);
            var language = Normalize(pieces[0]);
            if (language is null)
            {
                continue;
            }

            var quality = 1.0;
            foreach (var parameter in pieces.Skip(1))
            {
                if (parameter.StartsWith("q=", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(parameter[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var q))
                {
                    quality = q;
                }
            }

            // Strictly greater: with equal quality the earlier entry wins, as the header is ordered by preference
            if (quality > 0 && quality > bestQuality)
            {
                best = language;
                bestQuality = quality;
            }
        }
        return best;
    }

    /// <summary>Steam's name of the language for web API / store parameters ("l=").</summary>
    public static string SteamName(string language) => language == English ? "english" : "german";

    /// <summary>Culture for numbers and dates inside server-made texts (the UI formats its own numbers; this is for sentences).</summary>
    public static CultureInfo Culture(string language) => CultureInfo.GetCultureInfo(language == English ? "en-GB" : "de-DE");

    /// <summary>
    /// Makes <paramref name="language"/> the current language until the returned scope is disposed. A null or unknown language
    /// changes nothing. Dispose restores the previous value.
    /// </summary>
    public static IDisposable Use(string? language)
    {
        var previous = Ambient.Value;
        var normalized = Normalize(language);
        if (normalized is not null)
        {
            Ambient.Value = normalized;
        }
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            Ambient.Value = previous;
        }
    }
}
