using System.Globalization;

namespace PlatinVault.Bridge;

/// <summary>One achievement as read from the local Steam files; <c>UnlockTime</c> is null while locked or without a timestamp.</summary>
internal sealed record LocalAchievement(string ApiName, string? DisplayName, bool Achieved, DateTime? UnlockTime);

/// <summary>The local achievement state of one app: the game name and every achievement the schema defines.</summary>
internal sealed record LocalAppData(string? GameName, IReadOnlyList<LocalAchievement> Achievements);

/// <summary>
/// The seam between the bridge protocol and the machine's Steam installation. The Windows bridge implements it with the Windows
/// registry and the Steam files, the Linux bridge with the Linux locations and the process scan; tests use a scripted fake. Nothing
/// here knows an operating system.
/// </summary>
internal interface ILocalSteam
{
    /// <summary>True when a Steam installation was found on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>AppID of the game that runs right now, 0 when none does.</summary>
    uint GetRunningAppId();

    /// <summary>A number that changes whenever the local stats file of the app was written (0 when there is none).</summary>
    long GetStatsStamp(uint appId);

    /// <summary>The 32 bit account id of the active Steam user, 0 when nobody is signed in.</summary>
    uint GetActiveAccountId();

    /// <summary>Game name and achievement state of the app, null when the local files know nothing about it.</summary>
    LocalAppData? ReadAppData(uint appId);

    /// <summary>The library facts (release date, ratings, size) of this machine's Steam client, null when unavailable.</summary>
    BridgeLibraryDto? ReadLibrary();
}

/// <summary>What the bridge sees on this PC right now; a change of any part triggers a push.</summary>
internal readonly record struct Observation(uint AppId, long StatsStamp, uint AccountId);

/// <summary>
/// Reads the local Steam state (running game, stats file, active account) through <see cref="ILocalSteam"/> and turns it into the
/// payload of <c>POST /api/bridge/state</c>. Shared by the polling loop and the Windows setup window's connection test, so both send
/// exactly what the real bridge would send.
/// </summary>
/// <param name="platform">"linux" for the Linux bridge, null for the Windows one (see <see cref="BridgeStateDto.Platform"/>).</param>
internal sealed class StateSampler(ILocalSteam steam, string bridgeVersion, uint? testAppId, string? platform = null)
{
    // SteamID64 = this base + the 32 bit account id that Steam keeps (ActiveUser)
    private const ulong SteamId64Base = 76561197960265728UL;

    public Observation Observe()
    {
        var appId = testAppId ?? steam.GetRunningAppId();
        var stamp = appId == 0 ? 0 : steam.GetStatsStamp(appId);
        return new Observation(appId, stamp, steam.GetActiveAccountId());
    }

    public bool IsSteamAvailable => steam.IsAvailable;

    /// <summary>
    /// The library facts of this machine's Steam client (release date, Metacritic, review rating, size on disk of installed games) for
    /// <c>POST /api/bridge/library</c>; null when Steam is not installed here or its files cannot be read. Read only.
    /// </summary>
    public BridgeLibraryDto? BuildLibrary()
    {
        var library = steam.ReadLibrary();
        return library is null || library.Games.Count == 0 ? null : library;
    }

    /// <summary>The SteamID64 for an account id, or an empty string when no account is active.</summary>
    public static string SteamIdOf(uint accountId) =>
        accountId == 0 ? string.Empty : (SteamId64Base + accountId).ToString(CultureInfo.InvariantCulture);

    /// <param name="previousAppId">App of the last delivered state (0 for none).</param>
    /// <param name="previousAchieved">Achievements the server already knows for that app.</param>
    public async Task<(BridgeStateDto Payload, LocalAppData? Data)> BuildAsync(
        Observation observation, uint previousAppId, IReadOnlySet<string> previousAchieved, CancellationToken ct)
    {
        LocalAppData? data = null;

        // Without an active account the local reader would report every achievement as locked - better to send nothing.
        if (observation.AppId != 0 && observation.AccountId != 0)
        {
            data = steam.ReadAppData(observation.AppId);

            // Steam rewrites the stats file in place, so a read can catch it half written and see fewer unlocks than
            // before. Sending that would make the next good read look like a burst of new unlocks (false pop-ups),
            // so re-read briefly before believing that an achievement vanished.
            for (var attempt = 0;
                 attempt < 2 && data is not null && observation.AppId == previousAppId && LostAchievements(data, previousAchieved);
                 attempt++)
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
                data = steam.ReadAppData(observation.AppId) ?? data;
            }
        }

        var achievements = data?.Achievements
            .Select(a => new BridgeAchievementDto(a.ApiName, a.Achieved,
                a.Achieved && a.UnlockTime is { } time ? FormatUtc(time.ToUniversalTime(), withMilliseconds: false) : null))
            .ToList() ?? [];

        var payload = new BridgeStateDto(
            SteamIdOf(observation.AccountId),
            observation.AppId,
            data?.GameName ?? string.Empty,
            achievements,
            FormatUtc(DateTime.UtcNow, withMilliseconds: true),
            bridgeVersion,
            platform);

        return (payload, data);
    }

    private static bool LostAchievements(LocalAppData data, IReadOnlySet<string> previousAchieved)
    {
        if (previousAchieved.Count == 0) return false;
        var nowAchieved = data.Achievements.Where(a => a.Achieved).Select(a => a.ApiName).ToHashSet(StringComparer.Ordinal);
        return previousAchieved.Any(name => !nowAchieved.Contains(name));
    }

    // Always "Z"-suffixed ISO-8601 UTC, independent of the PC's culture and time zone.
    private static string FormatUtc(DateTime utc, bool withMilliseconds) =>
        utc.ToString(withMilliseconds ? "yyyy-MM-dd'T'HH:mm:ss.fff'Z'" : "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
