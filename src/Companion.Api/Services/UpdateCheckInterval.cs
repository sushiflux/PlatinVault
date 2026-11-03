namespace Companion.Api.Services;

/// <summary>
/// How often PlatinVault looks for updates by itself (setting <c>updates.check_interval</c>, Settings > System > Updates). The server
/// decides when an automatic status request may ask GitHub again; clients (header icon, Windows tray) only re-read the cached status.
/// A check by hand is independent of it.
/// </summary>
public enum UpdateCheckInterval
{
    /// <summary>The first status request after a process start asks once, then no more automatic checks until the next start.</summary>
    Startup,
    FourHours,
    Daily,
    Weekly,
    Monthly,
    /// <summary>Only the button "Jetzt nach Updates suchen" (and the tray's entry) ask GitHub.</summary>
    Never,
}

public static class UpdateCheckIntervals
{
    public const UpdateCheckInterval Default = UpdateCheckInterval.Daily;

    private static readonly (string Id, UpdateCheckInterval Value)[] Ids =
    [
        ("startup", UpdateCheckInterval.Startup),
        ("4h", UpdateCheckInterval.FourHours),
        ("daily", UpdateCheckInterval.Daily),
        ("weekly", UpdateCheckInterval.Weekly),
        ("monthly", UpdateCheckInterval.Monthly),
        ("never", UpdateCheckInterval.Never),
    ];

    /// <summary>The ids the API accepts, in the order the settings show them.</summary>
    public static IReadOnlyList<string> AllIds => Ids.Select(x => x.Id).ToArray();

    public static string ToId(UpdateCheckInterval value) => Ids.First(x => x.Value == value).Id;

    /// <summary>The interval for a submitted or stored id (trimmed, case-insensitive), or null when it is not one of <see cref="AllIds"/>.</summary>
    public static UpdateCheckInterval? TryParse(string? id)
    {
        var v = id?.Trim();
        foreach (var (key, value) in Ids)
        {
            if (string.Equals(key, v, StringComparison.OrdinalIgnoreCase)) return value;
        }
        return null;
    }

    /// <summary>Time after which a good answer counts as stale; null for <see cref="UpdateCheckInterval.Startup"/> and <see cref="UpdateCheckInterval.Never"/>.</summary>
    public static TimeSpan? Period(UpdateCheckInterval value) => value switch
    {
        UpdateCheckInterval.FourHours => TimeSpan.FromHours(4),
        UpdateCheckInterval.Daily => TimeSpan.FromDays(1),
        UpdateCheckInterval.Weekly => TimeSpan.FromDays(7),
        UpdateCheckInterval.Monthly => TimeSpan.FromDays(30),
        _ => null,
    };
}
