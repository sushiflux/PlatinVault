using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;

namespace Companion.Api.Services;

/// <summary>
/// Writes the facts IGDB knows about a game onto a game added by hand (card facts, sort values, developer, publisher, summary). One place for
/// "add a game with IGDB" and for the weekly refresh (<see cref="ManualFactsRefresh"/>), so a refreshed game looks exactly like a new one.
/// Title, cover and banner are NOT touched here (the player may have changed the cover; the title was chosen at adding).
/// </summary>
public static class ManualGameFacts
{
    private const int MaxGenres = 8;

    // German and English month names, fixed here: the server may run without culture data (invariant globalization)
    private static readonly string[] MonthsDe = ["Jan.", "Feb.", "März", "Apr.", "Mai", "Juni", "Juli", "Aug.", "Sept.", "Okt.", "Nov.", "Dez."];
    private static readonly string[] MonthsEn = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>The readable release date for the card: "12. Okt. 2023" (de), "12 Oct 2023" (en); the language is the one of the UI the game was added in.</summary>
    public static string FormatReleaseDate(DateTimeOffset date, string language)
    {
        var utc = date.UtcDateTime;
        return language == Lang.English
            ? $"{utc.Day} {MonthsEn[utc.Month - 1]} {utc.Year}"
            : $"{utc.Day}. {MonthsDe[utc.Month - 1]} {utc.Year}";
    }

    /// <summary>
    /// Copies IGDB's facts onto the game: first release (UTC + readable text in <paramref name="language"/>), average hours to 100 %, critics and
    /// users rating, genres (English names, as IGDB has them), developer, publisher and summary; marks the facts as coming from IGDB.
    /// </summary>
    public static void Apply(GameEntity game, IgdbGameDto facts, string language, DateTimeOffset now)
    {
        game.IgdbId = facts.IgdbId;
        game.FactsSource = "igdb";
        game.FirstReleaseAt = facts.FirstReleaseAt;
        if (facts.FirstReleaseAt is { } release)
        {
            game.ReleaseDate = FormatReleaseDate(release, language);
        }
        game.CompletionHours = facts.TimeToBeat?.CompletelyHours ?? game.CompletionHours;
        SteamLiveTrackerService.ApplyIgdbRatings(game, facts);

        var genres = facts.Genres.Select(g => g.Trim()).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxGenres).ToList();
        if (genres.Count > 0)
        {
            game.SteamGenres = genres;
            game.SteamGenresLanguage = Lang.English;
        }

        game.Developer = Join(facts.Developers, 240);
        game.Publisher = Join(facts.Publishers, 240);
        game.Summary = string.IsNullOrWhiteSpace(facts.Summary) ? null : Clip(facts.Summary.Trim(), 3000);
        game.FactsUpdatedAt = now;
    }

    /// <summary>The banner IGDB offers: the first artwork (1080p), else the first screenshot; empty when it has neither.</summary>
    public static string HeroUrl(IgdbGameDto facts) =>
        facts.Artworks.FirstOrDefault()?.FullUrl ?? facts.Screenshots.FirstOrDefault()?.FullUrl ?? string.Empty;

    private static string? Join(IEnumerable<string>? names, int max)
    {
        var text = string.Join(", ", (names ?? []).Select(n => n.Trim()).Where(n => n.Length > 0));
        return text.Length == 0 ? null : Clip(text, max);
    }

    private static string Clip(string text, int max) => text.Length > max ? text[..max].TrimEnd() : text;
}
