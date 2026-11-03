using System.Net;
using System.Text.Json;

namespace PlatinVault.Bridge.Tests;

[Collection(BridgeGlobals.Name)]
public sealed class ProtocolTests
{
    private static LocalAppData Game(params (string Name, bool Achieved)[] achievements) =>
        new("Portal", achievements.Select(a => new LocalAchievement(a.Name, a.Name, a.Achieved, a.Achieved ? new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc) : null)).ToList());

    private static BridgeSettings Settings(string server = "http://server:5233") =>
        BridgeSettings.TryResolve(server, "token-for-the-tests-1234567890", _ => null, null, null, out var settings, out _) ? settings! : throw new InvalidOperationException();

    // ---- the state payload ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_state_carries_the_platform_the_steam_id_and_utc_unlock_times()
    {
        var steam = new ScriptedSteam { RunningAppId = 620, AccountId = 12345, Data = Game(("ACH_ONE", true), ("ACH_TWO", false)) };
        var sampler = new StateSampler(steam, "1.0.7", null, platform: "linux");

        var observation = sampler.Observe();
        var (payload, data) = await sampler.BuildAsync(observation, 0, new HashSet<string>(), CancellationToken.None);

        Assert.Equal("linux", payload.Platform);
        Assert.Equal("1.0.7", payload.BridgeVersion);
        Assert.Equal(620u, payload.RunningAppId);
        Assert.Equal("76561197960278073", payload.SteamId64); // 76561197960265728 + 12345
        Assert.Equal("Portal", payload.GameName);
        Assert.Equal(2, payload.Achievements.Count);
        Assert.Equal("2026-11-03T10:00:00Z", payload.Achievements[0].UnlockTime);
        Assert.Null(payload.Achievements[1].UnlockTime);
        Assert.Equal(2, data!.Achievements.Count);
        Assert.EndsWith("Z", payload.SentAt);
    }

    [Fact]
    public async Task Without_an_active_account_nothing_is_sent_for_the_game_because_every_achievement_would_look_locked()
    {
        var steam = new ScriptedSteam { RunningAppId = 620, AccountId = 0, Data = Game(("ACH_ONE", true)) };
        var sampler = new StateSampler(steam, "1.0.7", null);

        var (payload, data) = await sampler.BuildAsync(sampler.Observe(), 0, new HashSet<string>(), CancellationToken.None);

        Assert.Null(data);
        Assert.Empty(payload.Achievements);
        Assert.Equal(string.Empty, payload.SteamId64);
        Assert.Null(payload.Platform); // the Windows bridge sends none
    }

    [Fact]
    public async Task A_half_written_stats_file_that_loses_an_unlock_is_read_again_before_it_is_believed()
    {
        var steam = new ScriptedSteam { RunningAppId = 620 };
        steam.ReadQueue.Enqueue(Game(("ACH_ONE", false), ("ACH_TWO", false))); // torn read: the unlock seems gone
        steam.Data = Game(("ACH_ONE", true), ("ACH_TWO", false));
        var sampler = new StateSampler(steam, "1.0.7", null);

        var (payload, _) = await sampler.BuildAsync(sampler.Observe(), 620, new HashSet<string> { "ACH_ONE" }, CancellationToken.None);

        Assert.True(payload.Achievements.Single(a => a.ApiName == "ACH_ONE").Achieved);
    }

    [Fact]
    public void The_test_app_id_replaces_the_running_game()
    {
        var sampler = new StateSampler(new ScriptedSteam { RunningAppId = 0 }, "1.0.7", testAppId: 730);
        Assert.Equal(730u, sampler.Observe().AppId);
    }

    // ---- the HTTP client -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_client_sends_bearer_token_csrf_header_json_and_the_language()
    {
        BridgeLang.Reset();
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.OK, "{\"ok\":true,\"latestBridgeVersion\":\"1.0.9\",\"bridgeSha256\":\"abc\",\"uiLanguage\":\"en\"}"));
        using var client = new BridgeClient(Settings(), "1.0.7", platform: "linux", handler: handler);
        var payload = new BridgeStateDto("", 0, "", [], "2026-11-03T10:00:00.000Z", "1.0.7", "linux");

        var result = await client.SendAsync(payload, CancellationToken.None);

        Assert.Equal(SendOutcome.Ok, result.Outcome);
        Assert.Equal("1.0.9", result.Reply!.LatestBridgeVersion);
        Assert.Equal("en", result.Reply.UiLanguage);

        var (request, body) = handler.Requests.Single();
        Assert.Equal("http://server:5233/api/bridge/state", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("token-for-the-tests-1234567890", request.Headers.Authorization.Parameter);
        Assert.Equal("PlatinVault", request.Headers.GetValues("X-Requested-With").Single());
        Assert.StartsWith("PlatinVault-Bridge/1.0.7", request.Headers.UserAgent.ToString());
        Assert.NotEmpty(request.Headers.AcceptLanguage);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("linux", json.RootElement.GetProperty("platform").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("runningAppId").GetInt32());
    }

    [Fact]
    public async Task The_windows_state_omits_the_platform_so_the_wire_format_is_unchanged()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.OK, "{\"ok\":true}"));
        using var client = new BridgeClient(Settings(), "1.0.3", handler: handler);

        await client.SendAsync(new BridgeStateDto("", 0, "", [], "2026-11-03T10:00:00.000Z", "1.0.3"), CancellationToken.None);

        Assert.DoesNotContain("platform", handler.Requests.Single().Body);
    }

    [Theory]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Rejected")]
    [InlineData(409, "Rejected")]
    [InlineData(429, "Transient")]
    [InlineData(503, "Transient")]
    [InlineData(302, "Rejected")]
    public async Task Answers_are_sorted_into_token_problem_refusal_and_temporary_failure(int status, string expectedName)
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json((HttpStatusCode)status, "{\"detail\":\"the reason\"}"));
        using var client = new BridgeClient(Settings(), "1.0.7", handler: handler);

        var result = await client.SendAsync(new BridgeStateDto("", 0, "", [], "t", "1.0.7"), CancellationToken.None);

        var expected = Enum.Parse<SendOutcome>(expectedName);
        Assert.Equal(expected, result.Outcome);
        if (expected != SendOutcome.Unauthorized && status != 302) Assert.Contains("the reason", result.Reason);
    }

    [Fact]
    public async Task A_network_failure_is_transient_with_a_readable_reason()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "boom"));
        using var client = new BridgeClient(Settings(), "1.0.7", handler: handler);

        var result = await client.SendAsync(new BridgeStateDto("", 0, "", [], "t", "1.0.7"), CancellationToken.None);

        Assert.Equal(SendOutcome.Transient, result.Outcome);
        Assert.False(string.IsNullOrEmpty(result.Reason));
    }

    [Fact]
    public async Task The_download_asks_for_the_linux_binary_and_hashes_what_it_received()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Path);
        var bytes = "linux-binary-bytes"u8.ToArray();
        var handler = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var client = new BridgeClient(Settings(), "1.0.7", platform: "linux", handler: handler);

        var result = await client.DownloadExeAsync(temp.Combine("x.new"), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("http://server:5233/api/bridge/exe?platform=linux", handler.Requests.Single().Request.RequestUri!.ToString());
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), result.Sha256);
        Assert.Equal(bytes, File.ReadAllBytes(temp.Combine("x.new")));
    }

    // ---- the command channel ---------------------------------------------------------------------------------------------

    private static (CommandPoller Poller, FakeHandler Handler, string RecordPath, TempFolder Temp) Poller(
        Func<string> commandsJson, LaunchOutcome outcome = LaunchOutcome.Started)
    {
        var temp = new TempFolder();
        var record = temp.Combine("launches.txt");
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.OK, commandsJson()));
        var client = new BridgeClient(Settings(), "1.0.7", handler: handler);
        return (new CommandPoller(client, new RecordingLauncher(record, outcome), (_, _) => { }), handler, record, temp);
    }

    [Fact]
    public async Task A_launch_command_opens_exactly_steam_run_with_the_number_and_reports_done_with_the_next_poll()
    {
        var (poller, handler, record, temp) = Poller(() => "{\"ok\":true,\"commands\":[{\"id\":\"c1\",\"type\":\"launch\",\"appId\":730,\"gameName\":\"Counter-Strike\"}]}");
        using var _ = temp;

        var delay = await poller.PollOnceAsync(CancellationToken.None);
        await poller.PollOnceAsync(CancellationToken.None);

        Assert.Equal(CommandPoller.PollMs, delay);
        Assert.Equal("steam://run/730" + Environment.NewLine, File.ReadAllText(record)); // run once although the server repeated it
        using var second = JsonDocument.Parse(handler.Requests[1].Body);
        var result = second.RootElement.GetProperty("results").EnumerateArray().Single();
        Assert.Equal("c1", result.GetProperty("id").GetString());
        Assert.Equal("done", result.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("launch", 0)]
    [InlineData("launch", -5)]
    [InlineData("launch", 4294967296)]
    [InlineData("shell", 730)]
    [InlineData("run-this", 730)]
    public async Task Unknown_command_types_and_invalid_app_ids_are_never_run(string type, long appId)
    {
        var (poller, handler, record, temp) = Poller(() => $"{{\"ok\":true,\"commands\":[{{\"id\":\"c1\",\"type\":\"{type}\",\"appId\":{appId}}}]}}");
        using var _ = temp;

        await poller.PollOnceAsync(CancellationToken.None);
        await poller.PollOnceAsync(CancellationToken.None);

        Assert.False(File.Exists(record));
        using var second = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.Equal("failed", second.RootElement.GetProperty("results").EnumerateArray().Single().GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("SteamNotFound", "steam-not-found")]
    [InlineData("Failed", "start-failed")]
    public async Task A_failed_start_is_reported_with_the_servers_failure_code(string outcomeName, string reason)
    {
        var (poller, handler, _, temp) = Poller(() => "{\"ok\":true,\"commands\":[{\"id\":\"c1\",\"type\":\"launch\",\"appId\":730}]}", Enum.Parse<LaunchOutcome>(outcomeName));
        using var _ = temp;

        await poller.PollOnceAsync(CancellationToken.None);
        await poller.PollOnceAsync(CancellationToken.None);

        using var second = JsonDocument.Parse(handler.Requests[1].Body);
        var result = second.RootElement.GetProperty("results").EnumerateArray().Single();
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Equal(reason, result.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_result_is_forgotten_once_the_server_stops_listing_the_command()
    {
        var listed = true;
        var (poller, handler, _, temp) = Poller(() => listed ? "{\"ok\":true,\"commands\":[{\"id\":\"c1\",\"type\":\"launch\",\"appId\":730}]}" : "{\"ok\":true,\"commands\":[]}");
        using var _ = temp;

        await poller.PollOnceAsync(CancellationToken.None);
        listed = false;
        await poller.PollOnceAsync(CancellationToken.None); // sends the result, server no longer lists the command
        await poller.PollOnceAsync(CancellationToken.None);

        using var third = JsonDocument.Parse(handler.Requests[2].Body);
        Assert.Empty(third.RootElement.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public async Task A_refused_poll_waits_longer_than_a_network_hiccup()
    {
        var token = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.Unauthorized, "{}"));
        using var client = new BridgeClient(Settings(), "1.0.7", handler: token);
        var poller = new CommandPoller(client, new RecordingLauncher(Path.GetTempFileName(), LaunchOutcome.Started), (_, _) => { });
        Assert.Equal(30_000, await poller.PollOnceAsync(CancellationToken.None));

        var network = new FakeHandler((_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "down"));
        using var client2 = new BridgeClient(Settings(), "1.0.7", handler: network);
        var poller2 = new CommandPoller(client2, new RecordingLauncher(Path.GetTempFileName(), LaunchOutcome.Started), (_, _) => { });
        Assert.Equal(5_000, await poller2.PollOnceAsync(CancellationToken.None));
    }
}
