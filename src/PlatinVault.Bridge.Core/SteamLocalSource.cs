using PlatinVault.SteamLocal;

namespace PlatinVault.Bridge;

/// <summary>
/// <see cref="ILocalSteam"/> on top of the shared Steam readers (<c>PlatinVault.SteamLocal</c>), for both bridges: where Steam lives, which
/// game runs (Windows: registry, Linux: process list), the stats files, the active account and the library facts all come from the very
/// same code the server uses, so a bridge reads Steam exactly like the server does when it runs on the same machine.
/// </summary>
internal sealed class SteamLocalSource : ILocalSteam
{
    private readonly SteamLocalStatsService _steam;
    private readonly SteamLocalLibraryService _library;

    /// <param name="location">Where Steam lives (manual folder, environment); null = the defaults of this platform.</param>
    /// <param name="runningGame">The running-game detection; null = the one of the location's platform.</param>
    public SteamLocalSource(SteamLocationProvider? location = null, IRunningGameSource? runningGame = null)
    {
        _steam = new SteamLocalStatsService(new BridgeLogger<SteamLocalStatsService>(), location, runningGame);
        _library = new SteamLocalLibraryService(_steam, new BridgeLogger<SteamLocalLibraryService>());
    }

    /// <summary>The Steam reader itself, for the diagnosis of the Linux <c>status</c> command (folder found, how, which detection).</summary>
    public SteamLocalStatsService Reader => _steam;

    public bool IsAvailable => _steam.IsLocalSteamAvailable();

    public uint GetRunningAppId() => _steam.GetLocalRunningAppId();

    public long GetStatsStamp(uint appId) => _steam.GetLocalStatsStamp(appId);

    public uint GetActiveAccountId() => _steam.GetActiveAccountId();

    public LocalAppData? ReadAppData(uint appId)
    {
        var data = _steam.ReadLocalAppData(appId);
        return data is null
            ? null
            : new LocalAppData(
                data.GameName,
                data.Achievements.Select(a => new LocalAchievement(a.ApiName, a.DisplayName, a.Achieved, a.UnlockTime)).ToList());
    }

    public BridgeLibraryDto? ReadLibrary()
    {
        var entries = _library.Read();
        if (entries is null || entries.Count == 0) return null;

        return new BridgeLibraryDto(entries
            .OrderBy(pair => pair.Key)
            .Select(pair => new BridgeLibraryGameDto(pair.Key, pair.Value.ReleaseUnix, pair.Value.Metacritic, pair.Value.ReviewPercent, pair.Value.ReviewScore, pair.Value.SizeBytes, pair.Value.InstallSizeBytes))
            .ToList());
    }
}
