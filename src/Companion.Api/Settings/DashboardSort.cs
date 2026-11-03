namespace Companion.Api.Settings;

/// <summary>
/// The dashboard's sort order per view, stored as <c>"&lt;order&gt;:&lt;asc|desc&gt;"</c> in <c>app_settings</c> (single user, so the choice
/// follows him between the Windows window and the browser). The order ids are the same ones the frontend sorts by
/// (<c>frontend/src/utils/dashboardSort.ts</c>); the server only validates and stores them, the sorting itself runs in the browser
/// because every value is already part of the lists it loads.
/// </summary>
public static class DashboardSort
{
    public const string KeyFavorites = "dashboard.sort_favorites";
    public const string KeyAll = "dashboard.sort_all";

    /// <summary>"custom" = the hand-made order of the favourites (drag &amp; drop, <c>games.SortOrder</c>); only the favourites view has it.</summary>
    public const string Custom = "custom";

    public const string DefaultFavorites = "custom:asc";

    /// <summary>"All games" has no manual order; the games played last come first.</summary>
    public const string DefaultAll = "lastPlayed:desc";

    /// <summary>Orders both views know.</summary>
    public static readonly string[] SharedOrders =
        ["completion", "alphabetical", "playtime", "lastPlayed", "release", "size", "metacritic", "reviews"];

    /// <summary>The value to store for a submitted one (trimmed, order id case-sensitive, direction lower case), or null when it is invalid for the view.</summary>
    public static string? Normalize(string? value, bool favorites)
    {
        var parts = value?.Trim().Split(':');
        if (parts is not { Length: 2 }) return null;
        var order = parts[0];
        var direction = parts[1].ToLowerInvariant();
        if (direction is not ("asc" or "desc")) return null;
        var known = SharedOrders.Contains(order, StringComparer.Ordinal) || (favorites && order == Custom);
        return known ? $"{order}:{direction}" : null;
    }

    /// <summary>The stored value of a view, or its default when nothing (valid) is stored - a hand-edited bad value never reaches the UI.</summary>
    public static string Resolve(string? stored, bool favorites) =>
        Normalize(stored, favorites) ?? (favorites ? DefaultFavorites : DefaultAll);
}
