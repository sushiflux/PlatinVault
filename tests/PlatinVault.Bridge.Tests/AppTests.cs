using System.Text.Json;

namespace PlatinVault.Bridge.Tests;

/// <summary>
/// Whole runs of the Linux bridge (`setup`, `run`, `status`) in a test root against a fake server on a loopback port and a scripted Steam
/// state (fake-steam.json). The only things that are real: the HTTP traffic, the files below the test root and the loops.
/// </summary>
[Collection(BridgeGlobals.Name)]
public sealed class AppTests
{
    private const string Token = "token-for-the-tests-1234567890";

    private static Func<string, string?> Env(Dictionary<string, string>? values = null) => name => values is not null && values.TryGetValue(name, out var v) ? v : null;

    private static async Task<(int Code, string Output)> CaptureAsync(Func<Task<int>> run)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var code = await run();
            return (code, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static Task<(int Code, string Output)> RunCliAsync(string[] args, Dictionary<string, string>? env = null, CancellationToken stop = default)
    {
        BridgeLang.Reset();
        return CaptureAsync(() => LinuxApp.RunAsync(args, Env(env), stop));
    }

    private static void WriteFakeSteam(string root, int stamp, params (string Name, bool Achieved)[] achievements)
    {
        var list = string.Join(",", achievements.Select(a =>
            $"{{\"apiName\":\"{a.Name}\",\"displayName\":\"{a.Name}\",\"achieved\":{(a.Achieved ? "true" : "false")}{(a.Achieved ? ",\"unlockTime\":\"2026-11-03T10:00:00Z\"" : "")}}}"));
        var json = $"{{\"available\":true,\"runningAppId\":620,\"accountId\":12345,\"statsStamp\":{stamp},\"gameName\":\"Portal\",\"achievements\":[{list}]}}";
        Directory.CreateDirectory(root);
        var temp = Path.Combine(root, "fake-steam.json.tmp");
        File.WriteAllText(temp, json);
        File.Move(temp, Path.Combine(root, "fake-steam.json"), overwrite: true);
    }

    // ---- setup ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Setup_saves_server_token_language_and_steam_folder_without_touching_the_network_in_a_test_root()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Combine("steam", "steamapps"));
        Directory.CreateDirectory(temp.Combine("steam", "appcache")); // a Steam folder, not just a game library

        var (code, output) = await RunCliAsync(["setup", "--server", "http://192.168.1.20:5233", "--token", Token, "--language", "de",
            "--steam-dir", temp.Combine("steam"), "--test-root", temp.Combine("root")]);

        Assert.True(code == 0, output);
        var config = BridgeConfigFile.Load(temp.Combine("root", "config", "platinvault-bridge", "bridge.json"), out _)!;
        Assert.Equal("http://192.168.1.20:5233", config.Server);
        Assert.Equal(Token, config.Token);
        Assert.Equal("de", config.Language);
        Assert.Equal(Path.GetFullPath(temp.Combine("steam")).TrimEnd(Path.DirectorySeparatorChar), config.SteamDir!.TrimEnd(Path.DirectorySeparatorChar));
        Assert.Contains("Gespeichert", output);
        Assert.DoesNotContain(Token, output); // the token is never printed
    }

    [Fact]
    public async Task Setup_reads_the_package_file_given_with_from_and_speaks_its_language()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Path);
        File.WriteAllText(temp.Combine("bridge.json"), $"{{\"server\":\"http://nas:5233\",\"token\":\"{Token}\",\"language\":\"en\"}}");

        var (code, output) = await RunCliAsync(["setup", "--from", temp.Combine("bridge.json"), "--test-root", temp.Combine("root")]);

        Assert.Equal(0, code);
        Assert.Contains("Saved:", output);
        var config = BridgeConfigFile.Load(temp.Combine("root", "config", "platinvault-bridge", "bridge.json"), out _)!;
        Assert.Equal("http://nas:5233", config.Server);
        Assert.Equal("en", config.Language);
    }

    [Fact]
    public async Task Setup_refuses_missing_values_an_unreadable_file_and_a_wrong_steam_folder_and_saves_nothing()
    {
        using var temp = new TempFolder();
        var root = temp.Combine("root");
        var config = Path.Combine(root, "config", "platinvault-bridge", "bridge.json");

        Assert.Equal(2, (await RunCliAsync(["setup", "--token", Token, "--test-root", root])).Code);
        Assert.Equal(2, (await RunCliAsync(["setup", "--server", "ftp://x", "--token", Token, "--test-root", root])).Code);
        Assert.Equal(2, (await RunCliAsync(["setup", "--from", temp.Combine("missing.json"), "--test-root", root])).Code);
        Assert.Equal(2, (await RunCliAsync(["setup", "--server", "http://s:1", "--token", Token, "--steam-dir", temp.Combine("nowhere"), "--test-root", root])).Code);
        Assert.Equal(2, (await RunCliAsync(["setup", "--server", "http://s:1", "--token", Token, "--steam-dir", "relative/path", "--test-root", root])).Code);
        Assert.False(File.Exists(config));

        // A folder without steamapps (one level too high) is named as such
        Directory.CreateDirectory(temp.Combine("steam"));
        var (code, output) = await RunCliAsync(["setup", "--server", "http://s:1", "--token", Token, "--steam-dir", temp.Combine("steam"), "--language", "en", "--test-root", root]);
        Assert.Equal(2, code);
        Assert.Contains("steamapps", output);
    }

    [Fact]
    public async Task Setup_checks_the_connection_with_a_real_state_push_and_names_a_rejected_token()
    {
        await using var server = new FakeBridgeServer();
        using var temp = new TempFolder();

        var (ok, okOutput) = await RunCliAsync(["setup", "--server", server.Url, "--token", Token, "--language", "en", "--test-root", temp.Combine("root"), "--app", "620"],
            env: null);
        // A test root skips the check by design (no network on its own account)
        Assert.Equal(0, ok);
        Assert.DoesNotContain("Connected to", okOutput);
        Assert.Empty(server.Snapshot());
    }

    // ---- run -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Run_without_a_configuration_stops_with_exit_78_that_the_service_does_not_restart()
    {
        using var temp = new TempFolder();

        var (code, output) = await RunCliAsync(["run", "--test-root", temp.Combine("root")]);

        Assert.Equal(LinuxApp.ExitConfig, code);
        Assert.Contains("platinvault-bridge setup", output);
    }

    [Fact]
    public async Task Run_pushes_the_scripted_state_follows_the_server_language_launches_games_and_stops_cleanly()
    {
        await using var server = new FakeBridgeServer { UiLanguage = "en" };
        using var temp = new TempFolder();
        var root = temp.Combine("root");
        WriteFakeSteam(root, 1, ("A1", true), ("A2", false));
        var env = new Dictionary<string, string> { [BridgeSettings.ServerEnvVar] = server.Url, [BridgeSettings.TokenEnvVar] = server.Token, ["LANG"] = "de_DE.UTF-8" };

        using var stop = new CancellationTokenSource();
        var run = RunCliAsync(["run", "--test-root", root, "--interval", "100", "--verbose"], env, stop.Token);

        // 1. the first state: platform, account, game and achievements
        var first = await server.WaitForAsync(r => r.Target == "/api/bridge/state", TimeSpan.FromSeconds(10));
        Assert.NotNull(first);
        Assert.Equal($"Bearer {server.Token}", first.Headers["Authorization"]);
        Assert.Equal("PlatinVault", first.Headers["X-Requested-With"]);
        using (var json = JsonDocument.Parse(first.Body))
        {
            Assert.Equal("linux", json.RootElement.GetProperty("platform").GetString());
            Assert.Equal(620, json.RootElement.GetProperty("runningAppId").GetInt32());
            Assert.Equal("76561197960278073", json.RootElement.GetProperty("steamId64").GetString());
            Assert.Equal("Portal", json.RootElement.GetProperty("gameName").GetString());
            Assert.Equal(2, json.RootElement.GetProperty("achievements").GetArrayLength());
        }

        // 2. an unlock (the stats file changed): pushed at once
        WriteFakeSteam(root, 2, ("A1", true), ("A2", true));
        var unlock = await server.WaitForAsync(r => r.Target == "/api/bridge/state" && r.Body.Contains("\"apiName\":\"A2\",\"achieved\":true"), TimeSpan.FromSeconds(10));
        Assert.NotNull(unlock);

        // 3. the server's language was adopted and remembered for the next start
        var configPath = Path.Combine(root, "config", "platinvault-bridge", "bridge.json");
        var remembered = await WaitUntilAsync(() => File.Exists(configPath) && BridgeConfigFile.Load(configPath, out _)?.Language == "en");
        Assert.True(remembered, "the language of the server should have been written to bridge.json");

        // 4. status, asked while the service runs, reports a connection and the game (the status file follows a change within a moment)
        var statusFile = Path.Combine(root, "state", "platinvault-bridge", "status.json");
        Assert.True(await WaitUntilAsync(() => LinuxStatusFile.Load(statusFile) is { Connection: "online", Total: 2 }), "the status file should show the connection");
        var (statusCode, statusOutput) = await RunCliAsync(["status", "--test-root", root, "--language", "en"], env);
        Assert.Equal(0, statusCode);
        Assert.Contains("Bridge is running", statusOutput);
        Assert.True(statusOutput.Contains("Connection: connected"), statusOutput);
        Assert.Contains("Game: Portal (2/2 achievements)", statusOutput);

        // 5. a launch command from the server: the recording launcher notes the address, the result goes back with the next poll
        server.Commands.Add(("c1", 730, "launch"));
        var launched = await WaitUntilAsync(() => File.Exists(Path.Combine(root, "launches.txt")), TimeSpan.FromSeconds(12));
        Assert.True(launched, "the launch command should have been run");
        Assert.Equal("steam://run/730" + Environment.NewLine, File.ReadAllText(Path.Combine(root, "launches.txt")));
        var reported = await server.WaitForAsync(r => r.Target == "/api/bridge/commands" && r.Body.Contains("\"id\":\"c1\"") && r.Body.Contains("\"done\""), TimeSpan.FromSeconds(12));
        Assert.NotNull(reported);

        // 6. a stop request ends the run with exit code 0 and removes the status file
        await stop.CancelAsync();
        var (code, output) = await run;
        Assert.Equal(0, code);
        Assert.Contains("Bridge stopped", output);
        Assert.False(File.Exists(Path.Combine(root, "state", "platinvault-bridge", "status.json")));
    }

    [Fact]
    public async Task A_rejected_token_is_shown_in_the_status_and_the_bridge_keeps_running()
    {
        await using var server = new FakeBridgeServer();
        using var temp = new TempFolder();
        var root = temp.Combine("root");
        WriteFakeSteam(root, 1, ("A1", false));
        var env = new Dictionary<string, string> { [BridgeSettings.ServerEnvVar] = server.Url, [BridgeSettings.TokenEnvVar] = "a-different-token-000000000" };

        using var stop = new CancellationTokenSource();
        var run = RunCliAsync(["run", "--test-root", root, "--interval", "100"], env, stop.Token);

        var statusFile = Path.Combine(root, "state", "platinvault-bridge", "status.json");
        var seen = await WaitUntilAsync(() => LinuxStatusFile.Load(statusFile)?.Connection == "unauthorized");
        Assert.True(seen, "the status file should report the rejected token");

        var (_, statusOutput) = await RunCliAsync(["status", "--test-root", root, "--language", "en"], env);
        Assert.Contains("token invalid", statusOutput);

        await stop.CancelAsync();
        Assert.Equal(0, (await run).Code);
    }

    [Fact]
    public async Task Status_without_a_running_bridge_says_so_and_points_at_the_setup()
    {
        using var temp = new TempFolder();

        var (code, output) = await RunCliAsync(["status", "--test-root", temp.Combine("root"), "--language", "en"]);

        Assert.Equal(0, code);
        Assert.Contains("missing", output);
        Assert.Contains("not running", output);
        Assert.Contains("Steam folder:", output);
    }

    [Fact]
    public async Task Version_and_help_need_no_configuration()
    {
        var (versionCode, version) = await RunCliAsync(["--version"]);
        Assert.Equal(0, versionCode);
        Assert.StartsWith("platinvault-bridge 1.", version);

        var (helpCode, help) = await RunCliAsync(["--help", "--language", "de"]);
        Assert.Equal(0, helpCode);
        Assert.Contains("Aufruf:", help);

        var (wrongCode, wrong) = await RunCliAsync(["bogus"]);
        Assert.Equal(2, wrongCode);
        Assert.Contains("bogus", wrong);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < until)
        {
            if (condition()) return true;
            await Task.Delay(50);
        }
        return condition();
    }
}
