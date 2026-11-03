using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlatinVault.Bridge;

/// <summary>Scripted Steam state of a test run: <c>fake-steam.json</c> in the test root. Edited while the bridge runs, it simulates unlocks.</summary>
internal sealed class FakeSteamState
{
    public bool Available { get; set; } = true;
    public uint RunningAppId { get; set; }
    public uint AccountId { get; set; }
    public long StatsStamp { get; set; }
    public string? GameName { get; set; }
    public List<FakeAchievement> Achievements { get; set; } = [];
    public List<BridgeLibraryGameDto> Library { get; set; } = [];
}

internal sealed class FakeAchievement
{
    public string ApiName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public bool Achieved { get; set; }
    public DateTime? UnlockTime { get; set; }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(FakeSteamState))]
internal sealed partial class FakeSteamJsonContext : JsonSerializerContext
{
}

/// <summary>
/// <see cref="ILocalSteam"/> for <c>--test-root</c> runs: reads a scripted state from a JSON file on every call, so tests (and the container
/// proof) need no Steam installation and can change the state while the bridge runs. Never used outside a test root.
/// </summary>
internal sealed class FakeLocalSteam(string path) : ILocalSteam
{
    private FakeSteamState Read()
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), FakeSteamJsonContext.Default.FakeSteamState) ?? new FakeSteamState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // The test script may be in the middle of rewriting the file: report "nothing" for this moment
            return new FakeSteamState { Available = false };
        }
    }

    public bool IsAvailable => Read().Available;

    public uint GetRunningAppId() => Read().RunningAppId;

    public long GetStatsStamp(uint appId) => Read().StatsStamp;

    public uint GetActiveAccountId() => Read().AccountId;

    public LocalAppData? ReadAppData(uint appId)
    {
        var state = Read();
        if (state.RunningAppId != appId && state.Achievements.Count == 0) return null;
        return new LocalAppData(
            state.GameName,
            state.Achievements.Select(a => new LocalAchievement(a.ApiName, a.DisplayName, a.Achieved, a.UnlockTime)).ToList());
    }

    public BridgeLibraryDto? ReadLibrary()
    {
        var state = Read();
        return state.Library.Count == 0 ? null : new BridgeLibraryDto(state.Library);
    }
}
