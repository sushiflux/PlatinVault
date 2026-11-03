namespace Companion.Api.Services;

/// <summary>One achievement as reported by the realtime bridge (the local Steam stats file of the gaming PC).</summary>
public sealed record BridgeAchievement(string ApiName, bool Achieved, DateTime? UnlockTime);

/// <summary>
/// The latest state the bridge pushed: which game runs and which achievements are unlocked. The list is empty when no
/// game runs or the PC had no local data. Names, icons and rarity are not part of it - they come from the cached
/// achievement list of that game.
/// </summary>
public sealed record BridgeSnapshot(
    uint RunningAppId,
    string GameName,
    string? SteamId64,
    IReadOnlyList<BridgeAchievement> Achievements,
    DateTimeOffset ReceivedAt);

/// <summary>Who detected an unlock: the stats file of the PC the server runs on, or the realtime bridge.</summary>
public enum UnlockSource
{
    Local,
    Bridge
}
