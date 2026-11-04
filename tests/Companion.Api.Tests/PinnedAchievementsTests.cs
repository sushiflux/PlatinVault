using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Hubs;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Companion.Api.Tests;

/// <summary>
/// Pinned achievements ("Im Blick"): order, de-duplication, the limit of 10, idempotent removal, parallel writers on one SQLite file,
/// the endpoints with their bilingual 400 answers, the setting "live.pins_after_unlock" and the removal on unlock.
/// </summary>
public sealed class PinnedAchievementsTests
{
    private const uint App = 4711;

    // ---- storage ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pins_keep_their_order_and_pinning_twice_changes_nothing()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();

        Assert.Empty(await PinnedAchievements.GetAsync(db, App));
        foreach (var name in new[] { "ACH_C", "ACH_A", "ACH_B" })
        {
            Assert.True((await PinnedAchievements.AddAsync(db, App, name)).Changed);
        }

        var again = await PinnedAchievements.AddAsync(db, App, "ach_a");
        Assert.False(again.Changed);
        Assert.False(again.LimitReached);
        Assert.Equal(["ACH_C", "ACH_A", "ACH_B"], await PinnedAchievements.GetAsync(db, App));

        // Another game has its own list
        await PinnedAchievements.AddAsync(db, App + 1, "ACH_X");
        Assert.Equal(["ACH_C", "ACH_A", "ACH_B"], await PinnedAchievements.GetAsync(db, App));
        Assert.Equal(["ACH_X"], await PinnedAchievements.GetAsync(db, App + 1));
    }

    [Fact]
    public async Task The_eleventh_pin_is_refused_but_an_already_pinned_one_stays_idempotent_at_the_limit()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        Assert.Equal(10, SettingsStore.MaxPinnedAchievements);

        for (var i = 1; i <= 10; i++)
        {
            Assert.True((await PinnedAchievements.AddAsync(db, App, "ACH_" + i)).Changed);
        }

        var refused = await PinnedAchievements.AddAsync(db, App, "ACH_11");
        Assert.True(refused.LimitReached);
        Assert.False(refused.Changed);
        Assert.Equal(10, (await PinnedAchievements.GetAsync(db, App)).Count);

        var idempotent = await PinnedAchievements.AddAsync(db, App, "ACH_5");
        Assert.False(idempotent.LimitReached);
        Assert.False(idempotent.Changed);

        // Room again after one is removed
        Assert.True((await PinnedAchievements.RemoveAsync(db, App, ["ACH_1"])).Changed);
        Assert.True((await PinnedAchievements.AddAsync(db, App, "ACH_11")).Changed);
        Assert.Equal("ACH_11", (await PinnedAchievements.GetAsync(db, App)).Last());
    }

    [Fact]
    public async Task Removing_is_idempotent_works_for_several_names_and_the_last_removal_drops_the_row()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        foreach (var name in new[] { "A", "B", "C", "D" })
        {
            await PinnedAchievements.AddAsync(db, App, name);
        }

        Assert.False((await PinnedAchievements.RemoveAsync(db, App, ["NOPE"])).Changed);
        Assert.False((await PinnedAchievements.RemoveAsync(db, App + 99, ["A"])).Changed);

        var two = await PinnedAchievements.RemoveAsync(db, App, ["b", "D", "NOPE"]);
        Assert.True(two.Changed);
        Assert.Equal(["A", "C"], two.ApiNames);
        Assert.False((await PinnedAchievements.RemoveAsync(db, App, ["B"])).Changed);

        await PinnedAchievements.RemoveAsync(db, App, ["A", "C"]);
        Assert.Empty(await PinnedAchievements.GetAsync(db, App));
        Assert.False(await db.AppSettings.AnyAsync(x => x.Key == PinnedAchievements.KeyFor(App)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"a\":1}")]
    [InlineData("[1,2")]
    public void An_unreadable_value_is_an_empty_list(string? stored)
    {
        Assert.Empty(PinnedAchievements.Parse(stored));
    }

    [Fact]
    public void A_hand_edited_value_is_cleaned_up_when_read()
    {
        var parsed = PinnedAchievements.Parse("[\"A\",\"a\",\"\",null,\"B\",\"C\",\"D\",\"E\",\"F\",\"G\",\"H\",\"I\",\"J\",\"K\",\"L\"]");
        Assert.Equal(["A", "B", "C", "D", "E", "F", "G", "H", "I", "J"], parsed);
    }

    [Fact]
    public async Task An_unreadable_stored_value_is_replaced_by_the_next_pin()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        db.AppSettings.Add(new AppSettingEntity { Key = PinnedAchievements.KeyFor(App), Value = "garbage", UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        Assert.Empty(await PinnedAchievements.GetAsync(db, App));
        var change = await PinnedAchievements.AddAsync(db, App, "ACH_1");
        Assert.True(change.Changed);
        Assert.Equal(["ACH_1"], await PinnedAchievements.GetAsync(db, App));
    }

    [Fact]
    public async Task Parallel_writers_on_one_file_never_lose_a_change()
    {
        using var scratch = new SqliteScratch();
        await using (var init = await scratch.MigratedAsync())
        {
            // Pre-existing row (compare-and-swap update path)
            await PinnedAchievements.AddAsync(init, App, "BASE");
        }

        var names = Enumerable.Range(1, 9).Select(i => "ACH_" + i).ToList();
        await Task.WhenAll(names.Select(async name =>
        {
            // One context per writer: this is what two server processes on the same file look like
            await using var db = scratch.Context();
            await PinnedAchievements.AddAsync(db, App, name);
        }));

        await using var check = scratch.Context();
        var pinned = await PinnedAchievements.GetAsync(check, App);
        Assert.Equal(10, pinned.Count);
        Assert.Equal("BASE", pinned[0]);
        Assert.Equal(names.Order(), pinned.Skip(1).Order());

        // Parallel removals of different names
        await Task.WhenAll(names.Take(5).Select(async name =>
        {
            await using var db = scratch.Context();
            await PinnedAchievements.RemoveAsync(db, App, [name]);
        }));
        Assert.Equal(new[] { "BASE" }.Concat(names.Skip(5)).Order(), (await PinnedAchievements.GetAsync(check, App)).Order());
    }

    [Fact]
    public async Task Parallel_first_pins_on_a_game_without_a_row_all_land()
    {
        using var scratch = new SqliteScratch();
        await using (await scratch.MigratedAsync()) { }

        await Task.WhenAll(Enumerable.Range(1, 6).Select(async i =>
        {
            await using var db = scratch.Context();
            await PinnedAchievements.AddAsync(db, App, "ACH_" + i);
        }));

        await using var check = scratch.Context();
        Assert.Equal(6, (await PinnedAchievements.GetAsync(check, App)).Distinct().Count());
    }

    // ---- setting -----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("keep", "keep")]
    [InlineData(" Remove ", "remove")]
    [InlineData("REMOVE", "remove")]
    public void Known_modes_are_accepted_in_any_case(string submitted, string expected)
    {
        Assert.Equal(expected, SettingsStore.NormalizePinsAfterUnlock(submitted));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("delete")]
    [InlineData("keep;remove")]
    public void Unknown_modes_are_not_accepted(string? value)
    {
        Assert.Null(SettingsStore.NormalizePinsAfterUnlock(value));
        Assert.Equal(["keep", "remove"], SettingsStore.PinsAfterUnlockModes);
        Assert.Equal("keep", SettingsStore.DefaultPinsAfterUnlock);
    }

    [Fact]
    public async Task An_unknown_mode_is_rejected_with_a_message_in_both_languages_before_anything_is_written()
    {
        var store = new SettingsStore(null!, null!);
        var request = new UpdateSettingsRequest(null, null, null, null, null, null, null, null, null, null, null,
            null, false, null, false, null, false, null, false, PinsAfterUnlock: "delete");

        var error = await Assert.ThrowsAsync<SettingsValidationException>(() => store.UpdateAsync(request));

        Assert.Contains("\"delete\"", error.Text.De);
        Assert.Contains("\"remove\"", error.Text.De);
        Assert.Contains("is unknown", error.Text.En);
    }

    [Fact]
    public async Task The_stored_mode_reads_as_keep_when_missing_or_hand_edited()
    {
        using var scratch = new SqliteScratch();
        await using var db = await scratch.MigratedAsync();
        Assert.Equal("keep", await SettingsStore.ReadPinsAfterUnlockAsync(db));

        db.AppSettings.Add(new AppSettingEntity { Key = SettingsStore.KeyPinsAfterUnlock, Value = "remove", UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        Assert.Equal("remove", await SettingsStore.ReadPinsAfterUnlockAsync(db));

        var row = await db.AppSettings.FirstAsync(x => x.Key == SettingsStore.KeyPinsAfterUnlock);
        row.Value = "sideways";
        await db.SaveChangesAsync();
        Assert.Equal("keep", await SettingsStore.ReadPinsAfterUnlockAsync(db));
    }

    // ---- endpoints ---------------------------------------------------------------------------------------------------------------

    private static async Task<(TestServer Server, RecordingHub Hub)> StartAsync()
    {
        var hub = new RecordingHub();
        var server = await TestServer.StartAsync(null, b => b.Services.AddSingleton<IHubContext<AchievementHub>>(hub));
        server.Client.DefaultRequestHeaders.Add("X-Requested-With", "PlatinVault");
        return (server, hub);
    }

    private static HttpRequestMessage Json(HttpMethod method, string url, object? body, string? language = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (language is not null) request.Headers.AcceptLanguage.ParseAdd(language);
        return request;
    }

    private static async Task<(HttpStatusCode Status, string[] Names, int Max, string Body)> Answer(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return (response.StatusCode, [], 0, body);
        using var doc = JsonDocument.Parse(body);
        var names = doc.RootElement.GetProperty("apiNames").EnumerateArray().Select(e => e.GetString()!).ToArray();
        return (response.StatusCode, names, doc.RootElement.GetProperty("max").GetInt32(), body);
    }

    [Fact]
    public async Task The_endpoints_answer_with_the_agreed_contract_and_keep_order_and_idempotence()
    {
        var (server, hub) = await StartAsync();
        await using var _ = server;
        var client = server.Client;

        var empty = await Answer(await client.GetAsync($"/api/live/pins?appId={App}"));
        Assert.Equal(HttpStatusCode.OK, empty.Status);
        Assert.Empty(empty.Names);
        Assert.Equal(10, empty.Max);
        using (var doc = JsonDocument.Parse(empty.Body))
        {
            Assert.Equal(App, doc.RootElement.GetProperty("appId").GetUInt32());
            Assert.Equal(["appId", "apiNames", "max"], doc.RootElement.EnumerateObject().Select(p => p.Name));
        }

        await Answer(await client.SendAsync(Json(HttpMethod.Post, "/api/live/pins", new { appId = App, apiName = "ACH_B" })));
        var two = await Answer(await client.SendAsync(Json(HttpMethod.Post, "/api/live/pins", new { appId = App, apiName = "ACH_A" })));
        Assert.Equal(["ACH_B", "ACH_A"], two.Names);
        Assert.Equal(2, hub.Pins(App));

        var duplicate = await Answer(await client.SendAsync(Json(HttpMethod.Post, "/api/live/pins", new { appId = App, apiName = "ACH_B" })));
        Assert.Equal(HttpStatusCode.OK, duplicate.Status);
        Assert.Equal(["ACH_B", "ACH_A"], duplicate.Names);
        Assert.Equal(2, hub.Pins(App));

        var read = await Answer(await client.GetAsync($"/api/live/pins?appId={App}"));
        Assert.Equal(["ACH_B", "ACH_A"], read.Names);

        var removed = await Answer(await client.SendAsync(Json(HttpMethod.Delete, $"/api/live/pins?appId={App}&apiName=ACH_B", null)));
        Assert.Equal(["ACH_A"], removed.Names);
        Assert.Equal(3, hub.Pins(App));

        var again = await Answer(await client.SendAsync(Json(HttpMethod.Delete, $"/api/live/pins?appId={App}&apiName=ACH_B", null)));
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.Equal(["ACH_A"], again.Names);
        Assert.Equal(3, hub.Pins(App));
    }

    [Fact]
    public async Task The_eleventh_pin_is_a_400_with_a_complete_message_in_the_language_of_the_request()
    {
        var (server, hub) = await StartAsync();
        await using var _ = server;
        for (var i = 1; i <= 10; i++)
        {
            var ok = await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins", new { appId = App, apiName = "ACH_" + i }));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }
        var sent = hub.Pins(App);

        var de = await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins", new { appId = App, apiName = "ACH_11" }, "de"));
        var en = await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins", new { appId = App, apiName = "ACH_11" }, "en"));

        Assert.Equal(HttpStatusCode.BadRequest, de.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, en.StatusCode);
        var deDetail = JsonDocument.Parse(await de.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString()!;
        var enDetail = JsonDocument.Parse(await en.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString()!;
        Assert.Contains("10", deDetail);
        Assert.Contains("Löse zuerst einen Erfolg", deDetail);
        Assert.Contains("10", enDetail);
        Assert.Contains("Unpin one first", enDetail);
        Assert.Equal(sent, hub.Pins(App));

        var read = await Answer(await server.Client.GetAsync($"/api/live/pins?appId={App}"));
        Assert.Equal(10, read.Names.Length);
        Assert.DoesNotContain("ACH_11", read.Names);
    }

    [Theory]
    [InlineData(0, "ACH_1")]
    [InlineData(4711, "")]
    [InlineData(4711, "   ")]
    [InlineData(4711, null)]
    public async Task Invalid_pin_input_is_a_400(int appId, string? apiName)
    {
        var (server, hub) = await StartAsync();
        await using var _ = server;

        var response = await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins", new { appId, apiName }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var delete = await server.Client.SendAsync(Json(HttpMethod.Delete, $"/api/live/pins?appId={appId}&apiName={apiName}", null));
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Equal(0, hub.Pins(App));
    }

    [Fact]
    public async Task A_name_longer_than_200_characters_and_a_missing_app_id_on_get_are_400()
    {
        var (server, _) = await StartAsync();
        await using var _s = server;

        var longName = new string('x', 201);
        var tooLong = await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins", new { appId = App, apiName = longName }));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var exactly200 = await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins", new { appId = App, apiName = new string('y', 200) }));
        Assert.Equal(HttpStatusCode.OK, exactly200.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await server.Client.GetAsync("/api/live/pins")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await server.Client.GetAsync("/api/live/pins?appId=0")).StatusCode);
    }

    [Fact]
    public async Task Several_pins_can_be_removed_at_once_and_invalid_lists_are_a_400()
    {
        var (server, hub) = await StartAsync();
        await using var _ = server;
        foreach (var name in new[] { "A", "B", "C", "D" })
        {
            await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins", new { appId = App, apiName = name }));
        }
        var before = hub.Pins(App);

        var result = await Answer(await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins/remove", new { appId = App, apiNames = new[] { "B", "D", "NOT_PINNED" } })));
        Assert.Equal(["A", "C"], result.Names);
        Assert.Equal(before + 1, hub.Pins(App));

        var nothing = await Answer(await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins/remove", new { appId = App, apiNames = new[] { "NOT_PINNED" } })));
        Assert.Equal(["A", "C"], nothing.Names);
        Assert.Equal(before + 1, hub.Pins(App));

        Assert.Equal(HttpStatusCode.BadRequest, (await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins/remove", new { appId = 0, apiNames = new[] { "A" } }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins/remove", new { appId = App, apiNames = new[] { "" } }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins/remove", new { appId = App }))).StatusCode);
    }

    [Fact]
    public async Task State_changing_calls_without_the_security_header_are_refused()
    {
        var (server, hub) = await StartAsync();
        await using var _ = server;
        server.Client.DefaultRequestHeaders.Remove("X-Requested-With");

        Assert.Equal(HttpStatusCode.BadRequest, (await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins", new { appId = App, apiName = "A" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await server.Client.SendAsync(Json(HttpMethod.Delete, $"/api/live/pins?appId={App}&apiName=A", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins/remove", new { appId = App, apiNames = new[] { "A" } }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await server.Client.GetAsync($"/api/live/pins?appId={App}")).StatusCode);
        Assert.Equal(0, hub.Pins(App));
    }

    [Fact]
    public async Task The_setting_defaults_to_keep_is_validated_and_survives_a_round_trip()
    {
        var (server, _) = await StartAsync();
        await using var _s = server;

        string Read(string body) => JsonDocument.Parse(body).RootElement.GetProperty("pinsAfterUnlock").GetString()!;

        Assert.Equal("keep", Read(await server.Client.GetStringAsync("/api/settings")));

        var bad = await server.Client.SendAsync(Json(HttpMethod.Put, "/api/settings", new { pinsAfterUnlock = "delete" }, "en"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("is unknown", await bad.Content.ReadAsStringAsync());
        Assert.Equal("keep", Read(await server.Client.GetStringAsync("/api/settings")));

        var ok = await server.Client.SendAsync(Json(HttpMethod.Put, "/api/settings", new { pinsAfterUnlock = "Remove" }));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("remove", Read(await ok.Content.ReadAsStringAsync()));

        // A request without the field keeps the stored value
        var other = await server.Client.SendAsync(Json(HttpMethod.Put, "/api/settings", new { playSounds = false }));
        Assert.Equal("remove", Read(await other.Content.ReadAsStringAsync()));
        Assert.Equal("remove", Read(await server.Client.GetStringAsync("/api/settings")));
    }

    // ---- unlock handling ----------------------------------------------------------------------------------------------------------

    private static LiveAchievementItemDto Unlocked(string apiName) =>
        new(apiName, apiName, "", null, false, "", "", true, DateTime.Now, 5.0, "Gold", "Very rare", false, "") { HasGlobalPercent = true };

    [Theory]
    [InlineData("lokal")]
    [InlineData("Bridge")]
    public async Task An_announced_unlock_removes_its_pin_only_in_remove_mode_and_tells_the_windows(string source)
    {
        var (server, hub) = await StartAsync();
        await using var _ = server;
        var announcer = server.Services.GetRequiredService<LiveAnnouncer>();
        foreach (var name in new[] { "ACH_A", "ACH_B", "ACH_C" })
        {
            await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins", new { appId = App, apiName = name }));
        }
        var pinsSent = hub.Pins(App);

        // Default "keep": the popup goes out, the pin stays
        await announcer.AnnounceUnlockAsync(Unlocked("ACH_A"), "Game", (int)App, source, CancellationToken.None);
        Assert.Contains(hub.Sent, s => s.Method == "AchievementUnlocked");
        Assert.Equal(["ACH_A", "ACH_B", "ACH_C"], (await Answer(await server.Client.GetAsync($"/api/live/pins?appId={App}"))).Names);
        Assert.Equal(pinsSent, hub.Pins(App));

        await server.Client.SendAsync(Json(HttpMethod.Put, "/api/settings", new { pinsAfterUnlock = "remove" }));

        // "remove": the unlocked one is unpinned, the rest keeps its order, and PinsChanged { appId } is sent
        await announcer.AnnounceUnlockAsync(Unlocked("ach_a"), "Game", (int)App, source, CancellationToken.None);
        Assert.Equal(["ACH_B", "ACH_C"], (await Answer(await server.Client.GetAsync($"/api/live/pins?appId={App}"))).Names);
        Assert.Equal(pinsSent + 1, hub.Pins(App));

        // An unlock that was not pinned changes and sends nothing
        await announcer.AnnounceUnlockAsync(Unlocked("ACH_Z"), "Game", (int)App, source, CancellationToken.None);
        Assert.Equal(pinsSent + 1, hub.Pins(App));

        // Another game's pins are not touched
        await server.Client.SendAsync(Json(HttpMethod.Post, "/api/live/pins", new { appId = App + 1, apiName = "ACH_B" }));
        await announcer.AnnounceUnlockAsync(Unlocked("ACH_B"), "Game", (int)App, source, CancellationToken.None);
        Assert.Equal(["ACH_C"], (await Answer(await server.Client.GetAsync($"/api/live/pins?appId={App}"))).Names);
        Assert.Equal(["ACH_B"], (await Answer(await server.Client.GetAsync($"/api/live/pins?appId={App + 1}"))).Names);
    }

    [Fact]
    public async Task A_broken_pin_removal_never_blocks_the_popup()
    {
        var hub = new RecordingHub();
        var services = new ServiceCollection().AddScoped<CompanionDbContext>(_ => throw new InvalidOperationException("no database")).BuildServiceProvider();
        var announcer = new LiveAnnouncer(hub, new LiveEventFeed(Microsoft.Extensions.Logging.Abstractions.NullLogger<LiveEventFeed>.Instance), new LiveEventLog(),
            new LiveConnectionCounter(), Microsoft.Extensions.Logging.Abstractions.NullLogger<LiveAnnouncer>.Instance, services.GetRequiredService<IServiceScopeFactory>());

        await announcer.AnnounceUnlockAsync(Unlocked("ACH_A"), "Game", (int)App, "lokal", CancellationToken.None);

        Assert.Contains(hub.Sent, s => s.Method == "AchievementUnlocked");
        Assert.Equal(0, hub.Pins(App));
    }

    // ---- fake hub ------------------------------------------------------------------------------------------------------------------

    private sealed class RecordingHub : IHubContext<AchievementHub>, IClientProxy
    {
        private readonly List<(string Method, object?[] Args)> _sent = [];

        public List<(string Method, object?[] Args)> Sent
        {
            get { lock (_sent) return [.. _sent]; }
        }

        /// <summary>How many "PinsChanged" events with this appId were sent so far (the payload is { appId }).</summary>
        public int Pins(uint appId) => Sent.Count(s =>
            s.Method == "PinsChanged" && s.Args.Length == 1 && s.Args[0]!.GetType().GetProperty("appId")!.GetValue(s.Args[0]) is uint id && id == appId);

        public IHubClients Clients => new AllClients(this);

        public IGroupManager Groups => throw new NotSupportedException();

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            lock (_sent) _sent.Add((method, args));
            return Task.CompletedTask;
        }
    }

    private sealed class AllClients(IClientProxy proxy) : IHubClients
    {
        public IClientProxy All => proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => proxy;
        public IClientProxy Client(string connectionId) => proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => proxy;
        public IClientProxy Group(string groupName) => proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => proxy;
        public IClientProxy User(string userId) => proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => proxy;
    }
}
