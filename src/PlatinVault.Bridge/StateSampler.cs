using System.Globalization;
using Companion.Api.Services;

namespace PlatinVault.Bridge;

/// <summary>What the bridge sees on this PC right now; a change of any part triggers a push.</summary>
internal readonly record struct Observation(uint AppId, long StatsStamp, uint AccountId);

/// <summary>
/// Reads the local Steam state (running game, stats file, active account) and turns it into the payload of
/// <c>POST /api/bridge/state</c>. Shared by the polling loop and the setup window's connection test, so both send
/// exactly what the real bridge would send.
/// </summary>
internal sealed class StateSampler(SteamLocalStatsService steam, string bridgeVersion, uint? testAppId)
{
    // SteamID64 = this base + the 32 bit account id that Steam keeps in the registry (ActiveUser).
    private const ulong SteamId64Base = 76561197960265728UL;

    public Observation Observe()
    {
        var appId = testAppId ?? steam.GetLocalRunningAppId();
        var stamp = appId == 0 ? 0 : steam.GetLocalStatsStamp(appId);
        return new Observation(appId, stamp, steam.GetActiveAccountId());
    }

    public bool IsSteamAvailable => steam.IsLocalSteamAvailable();

    private readonly SteamLocalLibraryService _library = new(steam, new BridgeLogger<SteamLocalLibraryService>());

    /// <summary>
    /// The library facts of this PC's Steam client (release date, Metacritic, review rating, size on disk of installed games) for
    /// <c>POST /api/bridge/library</c>; null when Steam is not installed here or its files cannot be read. Read only.
    /// </summary>
    public BridgeLibraryDto? BuildLibrary()
    {
        var entries = _library.Read();
        if (entries is null || entries.Count == 0) return null;

        return new BridgeLibraryDto(entries
            .OrderBy(pair => pair.Key)
            .Select(pair => new BridgeLibraryGameDto(pair.Key, pair.Value.ReleaseUnix, pair.Value.Metacritic, pair.Value.ReviewPercent, pair.Value.ReviewScore, pair.Value.SizeBytes, pair.Value.InstallSizeBytes))
            .ToList());
    }

    /// <summary>The SteamID64 for an account id from the registry, or an empty string when no account is active.</summary>
    public static string SteamIdOf(uint accountId) =>
        accountId == 0 ? string.Empty : (SteamId64Base + accountId).ToString(CultureInfo.InvariantCulture);

    /// <param name="previousAppId">App of the last delivered state (0 for none).</param>
    /// <param name="previousAchieved">Achievements the server already knows for that app.</param>
    public async Task<(BridgeStateDto Payload, SteamLocalStatsService.LocalSchemaResult? Data)> BuildAsync(
        Observation observation, uint previousAppId, IReadOnlySet<string> previousAchieved, CancellationToken ct)
    {
        SteamLocalStatsService.LocalSchemaResult? data = null;

        // Without an active account the local reader would report every achievement as locked - better to send nothing.
        if (observation.AppId != 0 && observation.AccountId != 0)
        {
            data = steam.ReadLocalAppData(observation.AppId);

            // Steam rewrites the stats file in place, so a read can catch it half written and see fewer unlocks than
            // before. Sending that would make the next good read look like a burst of new unlocks (false pop-ups),
            // so re-read briefly before believing that an achievement vanished.
            for (var attempt = 0;
                 attempt < 2 && data is not null && observation.AppId == previousAppId && LostAchievements(data, previousAchieved);
                 attempt++)
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
                data = steam.ReadLocalAppData(observation.AppId) ?? data;
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
            bridgeVersion);

        return (payload, data);
    }

    private static bool LostAchievements(SteamLocalStatsService.LocalSchemaResult data, IReadOnlySet<string> previousAchieved)
    {
        if (previousAchieved.Count == 0) return false;
        var nowAchieved = data.Achievements.Where(a => a.Achieved).Select(a => a.ApiName).ToHashSet(StringComparer.Ordinal);
        return previousAchieved.Any(name => !nowAchieved.Contains(name));
    }

    // Always "Z"-suffixed ISO-8601 UTC, independent of the PC's culture and time zone.
    private static string FormatUtc(DateTime utc, bool withMilliseconds) =>
        utc.ToString(withMilliseconds ? "yyyy-MM-dd'T'HH:mm:ss.fff'Z'" : "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
