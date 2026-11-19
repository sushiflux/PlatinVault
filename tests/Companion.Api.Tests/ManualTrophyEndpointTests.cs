using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Companion.Api.Data;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Companion.Api.Tests;

/// <summary>The trophy list of a game added by hand: reading it, ticking by hand (popup exactly once), platinum, pins and notes by game id, the overlay rule.</summary>
public sealed class ManualTrophyEndpointTests
{
    private static async Task<(HttpStatusCode Status, JsonElement Body)> Tick(ManualServer s, string slug, string key, bool unlocked = true) =>
        await ManualServer.SendJsonAsync(s.PostAsync($"/api/games/{slug}/trophies/{key}/unlock", new { unlocked }));

    private static async Task<(ManualServer Server, Guid Id)> PsGameAsync()
    {
        var server = await ManualServer.StartAsync(credentials: false);
        var id = await server.SeedAsync("ps-game", "playstation", "ps5", ("a", "bronze", null), ("b", "silver", null), ("dlc", "bronze", "Extra Levels"), ("plat", "platinum", null));
        return (server, id);
    }

    private static LiveEventMeta Meta((string Method, object?[] Args) sent) => Assert.IsType<LiveEventMeta>(sent.Args[1]);

    // ---- reading the list -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_has_the_agreed_shape_order_counts_and_tiers()
    {
        var (s, id) = await PsGameAsync();
        await using var guard = s;
        await s.PostAsync("/api/live/note", new { appId = 0, gameId = id, apiName = "a", note = "behind the waterfall" });

        var (status, list) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/games/ps-game/trophies"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(id, list.GetProperty("gameId").GetGuid());
        Assert.Equal(("playstation", "ps5", "trophies", true), (
            list.GetProperty("platform").GetString(), list.GetProperty("platformVariant").GetString(),
            list.GetProperty("kind").GetString(), list.GetProperty("supported").GetBoolean()));
        Assert.Equal(["a", "b", "dlc", "plat"], list.GetProperty("achievements").EnumerateArray().Select(a => a.GetProperty("apiName").GetString()));
        Assert.Equal(4, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, list.GetProperty("achievedCount").GetInt32());
        Assert.Equal(4, list.GetProperty("remainingCount").GetInt32());
        Assert.False(list.GetProperty("is100Percent").GetBoolean());
        // Tiers: the PlayStation grades decide
        Assert.Equal((0, 1), (list.GetProperty("platinum").GetProperty("achieved").GetInt32(), list.GetProperty("platinum").GetProperty("total").GetInt32()));
        Assert.Equal(1, list.GetProperty("silver").GetProperty("total").GetInt32());
        Assert.Equal(2, list.GetProperty("bronze").GetProperty("total").GetInt32());
        Assert.Equal(0, list.GetProperty("gold").GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, list.GetProperty("platinumAt").ValueKind);

        var first = list.GetProperty("achievements")[0];
        Assert.Equal("Bronze", first.GetProperty("rarityTier").GetString());
        Assert.Equal("bronze", first.GetProperty("grade").GetString());
        Assert.Equal("behind the waterfall", first.GetProperty("userNote").GetString());
        // No icon (the page draws its own symbol), no share: the band and label stay empty
        Assert.Equal(string.Empty, first.GetProperty("iconUrl").GetString());
        Assert.False(first.GetProperty("hasGlobalPercent").GetBoolean());
        Assert.Equal(string.Empty, first.GetProperty("rarityBand").GetString());
        Assert.False(first.GetProperty("achieved").GetBoolean());
        Assert.Equal("Extra Levels", list.GetProperty("achievements")[2].GetProperty("group").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("group").ValueKind);
        // Same language as the list: nothing to translate
        Assert.Equal("original", list.GetProperty("translation").GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_stated_share_gives_the_Steam_scale_band_and_label_on_a_platform_without_grades()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await s.SeedAsync("xb", "xbox", null, ("rare", null, null), ("plain", null, null), ("big", null, null));
        await s.WithDbAsync(async db =>
        {
            await db.GameTrophies.Where(t => t.TrophyKey == "rare").ExecuteUpdateAsync(t => t.SetProperty(x => x.RarityPercent, 3.5));
            await db.GameTrophies.Where(t => t.TrophyKey == "big").ExecuteUpdateAsync(t => t.SetProperty(x => x.Points, 100));
        });

        var (_, list) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/games/xb/trophies"));
        var byKey = list.GetProperty("achievements").EnumerateArray().ToDictionary(a => a.GetProperty("apiName").GetString()!);

        Assert.Equal("achievements", list.GetProperty("kind").GetString());
        Assert.Equal("Gold", byKey["rare"].GetProperty("rarityTier").GetString());
        Assert.Equal("ultraRare", byKey["rare"].GetProperty("rarityBand").GetString());
        Assert.True(byKey["rare"].GetProperty("hasGlobalPercent").GetBoolean());
        // Xbox without a share: gamerscore bands; nothing at all: bronze
        Assert.Equal("Gold", byKey["big"].GetProperty("rarityTier").GetString());
        Assert.Equal("Bronze", byKey["plain"].GetProperty("rarityTier").GetString());
        // No platinum trophy: the one platinum is the whole game
        Assert.Equal((0, 1), (list.GetProperty("platinum").GetProperty("achieved").GetInt32(), list.GetProperty("platinum").GetProperty("total").GetInt32()));
    }

    [Fact]
    public async Task A_Steam_game_has_no_list_an_unknown_game_is_a_404_and_Nintendo_has_nothing_to_tick()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await s.WithDbAsync(async db =>
        {
            db.Games.Add(new GameEntity { Id = Guid.NewGuid(), Slug = "steam-game", Title = "S", SteamAppId = 10 });
            await db.SaveChangesAsync();
        });
        await s.SeedAsync("zelda", "nintendo", null);

        var (steamStatus, steam) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/games/steam-game/trophies"));
        Assert.Equal(HttpStatusCode.BadRequest, steamStatus);
        Assert.False(string.IsNullOrWhiteSpace(steam.GetProperty("detail").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await Tick(s, "steam-game", "x")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync("/api/games/nope/trophies")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Tick(s, "nope", "x")).Status);

        var (status, list) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/games/zelda/trophies"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(list.GetProperty("supported").GetBoolean());
        Assert.Equal(0, list.GetProperty("achievements").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await Tick(s, "zelda", "x")).Status);
    }

    [Fact]
    public async Task Another_language_than_the_list_says_the_translation_is_missing_and_shows_the_original()
    {
        var (s, _) = await PsGameAsync();
        await using var guard = s;

        var (_, list) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/games/ps-game/trophies?lang=en"));

        Assert.Equal("de", list.GetProperty("language").GetString());
        var translation = list.GetProperty("translation");
        Assert.Equal(("missing", "en", "de", false), (
            translation.GetProperty("state").GetString(), translation.GetProperty("language").GetString(),
            translation.GetProperty("sourceLanguage").GetString(), translation.GetProperty("running").GetBoolean()));
    }

    [Fact]
    public async Task The_sources_of_the_list_are_shown_and_only_web_addresses_survive()
    {
        var (s, id) = await PsGameAsync();
        await using var guard = s;
        await s.WithDbAsync(db => db.Games.Where(g => g.Id == id).ExecuteUpdateAsync(g => g.SetProperty(x => x.TrophyListSourcesJson,
            "[{\"title\":\"PSNProfiles\",\"url\":\"https://psnprofiles.com/x\"},{\"title\":\"bad\",\"url\":\"javascript:alert(1)\"}]")));

        var (_, list) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/games/ps-game/trophies"));

        var sources = list.GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal("https://psnprofiles.com/x", Assert.Single(sources).GetProperty("url").GetString());
        Assert.True(list.GetProperty("researchedAt").ValueKind == JsonValueKind.String);
    }

    // ---- ticking ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_tick_shows_the_popup_once_and_the_same_tick_again_is_silent()
    {
        var (s, _) = await PsGameAsync();
        await using var guard = s;

        var (status, first) = await Tick(s, "ps-game", "a");
        var (_, second) = await Tick(s, "ps-game", "a");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(first.GetProperty("announced").GetProperty("trophy").GetBoolean());
        Assert.False(first.GetProperty("announced").GetProperty("platinum").GetBoolean());
        Assert.Equal(1, first.GetProperty("achievedCount").GetInt32());
        Assert.True(first.GetProperty("achievements")[0].GetProperty("achieved").GetBoolean());
        Assert.Equal("a", first.GetProperty("latestAchievement").GetProperty("apiName").GetString());
        // The second request changed nothing: no second popup
        Assert.False(second.GetProperty("announced").GetProperty("trophy").GetBoolean());
        Assert.Equal(1, second.GetProperty("achievedCount").GetInt32());

        var popup = Assert.Single(s.Hub.Sent, e => e.Method == "AchievementUnlocked");
        var item = Assert.IsType<Companion.Api.Endpoints.LiveAchievementItemDto>(popup.Args[0]);
        Assert.Equal(("a", true, "Bronze"), (item.ApiName, item.Achieved, item.RarityTier));
        var meta = Meta(popup);
        Assert.Equal("manual", meta.Source);
        Assert.Equal("Game ps-game", meta.GameName);
    }

    [Fact]
    public async Task Un_ticking_is_silent_and_a_new_tick_announces_again()
    {
        var (s, _) = await PsGameAsync();
        await using var guard = s;

        await Tick(s, "ps-game", "a");
        var (_, off) = await Tick(s, "ps-game", "a", unlocked: false);
        Assert.Equal(1, s.Hub.Count("AchievementUnlocked"));
        Assert.Equal(0, off.GetProperty("achievedCount").GetInt32());
        Assert.False(off.GetProperty("announced").GetProperty("trophy").GetBoolean());
        Assert.Equal(JsonValueKind.Null, off.GetProperty("achievements")[0].GetProperty("unlockTime").ValueKind);

        await Tick(s, "ps-game", "a");
        Assert.Equal(2, s.Hub.Count("AchievementUnlocked"));
    }

    [Fact]
    public async Task Parallel_ticks_of_one_trophy_announce_exactly_once()
    {
        var (s, _) = await PsGameAsync();
        await using var guard = s;

        var answers = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Tick(s, "ps-game", "b")));

        Assert.All(answers, a => Assert.Equal(HttpStatusCode.OK, a.Status));
        Assert.Equal(1, answers.Count(a => a.Body.GetProperty("announced").GetProperty("trophy").GetBoolean()));
        Assert.Equal(1, s.Hub.Count("AchievementUnlocked"));
        Assert.Equal(1, (await s.WithDbAsync(db => db.GameTrophies.CountAsync(t => t.UnlockedAt != null))));
    }

    [Fact]
    public async Task The_platinum_trophy_ticks_itself_when_the_base_game_is_complete_with_one_platinum_moment()
    {
        var (s, id) = await PsGameAsync();
        await using var guard = s;

        await Tick(s, "ps-game", "a");
        // A DLC trophy does not count towards the platinum trophy
        await Tick(s, "ps-game", "dlc");
        Assert.Equal(0, s.Hub.Count("PlatinumUnlocked"));
        var (_, last) = await Tick(s, "ps-game", "b");

        Assert.True(last.GetProperty("announced").GetProperty("trophy").GetBoolean());
        Assert.True(last.GetProperty("announced").GetProperty("platinum").GetBoolean());
        Assert.Equal(JsonValueKind.String, last.GetProperty("platinumAt").ValueKind);
        var plat = last.GetProperty("achievements").EnumerateArray().Single(a => a.GetProperty("apiName").GetString() == "plat");
        Assert.True(plat.GetProperty("achieved").GetBoolean());
        Assert.Equal(1, last.GetProperty("platinum").GetProperty("achieved").GetInt32());
        // Popups: a, dlc, b - never one for the platinum trophy itself; the platinum moment once
        Assert.Equal(3, s.Hub.Count("AchievementUnlocked"));
        Assert.DoesNotContain(s.Hub.Sent.Where(e => e.Method == "AchievementUnlocked"), e => ((Companion.Api.Endpoints.LiveAchievementItemDto)e.Args[0]!).ApiName == "plat");
        var moment = Assert.Single(s.Hub.Sent, e => e.Method == "PlatinumUnlocked");
        Assert.Equal("manual", Meta(moment).Source);

        Assert.NotNull((await s.WithDbAsync(db => db.Games.AsNoTracking().SingleAsync(g => g.Id == id))).PlatinumAt);
    }

    [Fact]
    public async Task Un_ticking_a_base_trophy_takes_the_platinum_trophy_back_silently_and_ticking_again_gives_a_new_moment()
    {
        var (s, _) = await PsGameAsync();
        await using var guard = s;
        await Tick(s, "ps-game", "a");
        await Tick(s, "ps-game", "b");
        var before = (s.Hub.Count("AchievementUnlocked"), s.Hub.Count("PlatinumUnlocked"));
        Assert.Equal((2, 1), before);

        var (_, off) = await Tick(s, "ps-game", "a", unlocked: false);

        Assert.Equal(JsonValueKind.Null, off.GetProperty("platinumAt").ValueKind);
        Assert.False(off.GetProperty("achievements").EnumerateArray().Single(a => a.GetProperty("apiName").GetString() == "plat").GetProperty("achieved").GetBoolean());
        Assert.Equal(before, (s.Hub.Count("AchievementUnlocked"), s.Hub.Count("PlatinumUnlocked")));

        var (_, again) = await Tick(s, "ps-game", "a");
        Assert.True(again.GetProperty("announced").GetProperty("platinum").GetBoolean());
        Assert.Equal((3, 2), (s.Hub.Count("AchievementUnlocked"), s.Hub.Count("PlatinumUnlocked")));
    }

    [Fact]
    public async Task Ticking_the_platinum_trophy_by_hand_is_the_platinum_moment_and_no_normal_popup()
    {
        var (s, _) = await PsGameAsync();
        await using var guard = s;

        var (_, answer) = await Tick(s, "ps-game", "plat");

        Assert.False(answer.GetProperty("announced").GetProperty("trophy").GetBoolean());
        Assert.True(answer.GetProperty("announced").GetProperty("platinum").GetBoolean());
        Assert.Equal(0, s.Hub.Count("AchievementUnlocked"));
        Assert.Equal(1, s.Hub.Count("PlatinumUnlocked"));
    }

    [Fact]
    public async Task Without_a_platinum_grade_the_last_tick_is_the_platinum_moment()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await s.SeedAsync("xb", "xbox", null, ("x", null, null), ("y", null, null));

        await Tick(s, "xb", "x");
        Assert.Equal(0, s.Hub.Count("PlatinumUnlocked"));
        var (_, done) = await Tick(s, "xb", "y");

        Assert.True(done.GetProperty("is100Percent").GetBoolean());
        Assert.True(done.GetProperty("announced").GetProperty("platinum").GetBoolean());
        Assert.Equal(1, s.Hub.Count("PlatinumUnlocked"));
        var moment = Assert.Single(s.Hub.Sent, e => e.Method == "PlatinumUnlocked");
        Assert.Equal(2, moment.Args[0]!.GetType().GetProperty("total")!.GetValue(moment.Args[0]));
    }

    [Fact]
    public async Task An_unknown_trophy_a_missing_key_and_a_missing_body_are_refused()
    {
        var (s, _) = await PsGameAsync();
        await using var guard = s;

        var (status, body) = await Tick(s, "ps-game", "nope");
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Contains("nope", body.GetProperty("detail").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await Tick(s, "ps-game", new string('k', 81))).Status);
        var noBody = await s.SendAsync(HttpMethod.Post, "/api/games/ps-game/trophies/a/unlock");
        Assert.Equal(HttpStatusCode.BadRequest, noBody.StatusCode);
        Assert.Equal(0, s.Hub.Count("AchievementUnlocked"));
    }

    [Fact]
    public async Task A_state_changing_call_needs_the_security_header()
    {
        var (s, _) = await PsGameAsync();
        await using var guard = s;
        using var plain = new HttpClient { BaseAddress = s.Client.BaseAddress };

        using var response = await plain.PostAsJsonAsync("/api/games/ps-game/trophies/a/unlock", new { unlocked = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, s.Hub.Count("AchievementUnlocked"));
    }

    // ---- the overlay and the catch-up -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_manual_tick_never_reaches_the_overlay_feed_but_a_steam_unlock_does()
    {
        var (s, _) = await PsGameAsync();
        await using var guard = s;
        var feed = s.Services.GetRequiredService<LiveEventFeed>();
        var unlocks = new List<LiveUnlockEvent>();
        var platinums = new List<LivePlatinumEvent>();
        feed.AchievementUnlocked += unlocks.Add;
        feed.PlatinumUnlocked += platinums.Add;

        await Tick(s, "ps-game", "a");
        await Tick(s, "ps-game", "b");
        Assert.Equal(1, s.Hub.Count("PlatinumUnlocked"));
        Assert.Empty(unlocks);
        Assert.Empty(platinums);

        // The same announcer, a Steam source: reaches the overlay as before
        var announcer = s.Services.GetRequiredService<LiveAnnouncer>();
        var item = new Companion.Api.Endpoints.LiveAchievementItemDto("S1", "Steam one", "d", null, false, "", "", true, DateTime.Now, 5, "Silver", "Rare", false, "");
        await announcer.AnnounceUnlockAsync(item, "Steam game", 620, "lokal", CancellationToken.None);
        await announcer.AnnouncePlatinumAsync("Steam game", 12, 620, "lokal", CancellationToken.None);
        Assert.Single(unlocks);
        Assert.Single(platinums);
        Assert.Null(unlocks[0].Source);
    }

    [Fact]
    public async Task A_window_that_connects_later_can_catch_up_on_a_manual_tick()
    {
        var (s, _) = await PsGameAsync();
        await using var guard = s;
        await Tick(s, "ps-game", "a");

        var (_, events) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/live/events"));

        var entry = events.GetProperty("events").EnumerateArray().Single();
        Assert.Equal("achievement", entry.GetProperty("kind").GetString());
        Assert.Equal("manual", entry.GetProperty("source").GetString());
        Assert.Equal(0, entry.GetProperty("appId").GetInt32());
        Assert.Equal("Game ps-game", entry.GetProperty("gameName").GetString());
        Assert.Equal("a", entry.GetProperty("achievement").GetProperty("apiName").GetString());
    }

    // ---- pins by game id --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pins_of_a_game_added_by_hand_keep_their_order_answer_with_the_game_id_and_tell_the_windows()
    {
        var (s, id) = await PsGameAsync();
        await using var guard = s;

        var (_, empty) = await ManualServer.SendJsonAsync(s.Client.GetAsync($"/api/live/pins?gameId={id}"));
        Assert.Equal(["appId", "apiNames", "max", "gameId"], empty.EnumerateObject().Select(p => p.Name));
        Assert.Equal(0, empty.GetProperty("appId").GetInt32());
        Assert.Equal(id, empty.GetProperty("gameId").GetGuid());

        await s.PostAsync("/api/live/pins", new { gameId = id, apiName = "b" });
        var (_, two) = await ManualServer.SendJsonAsync(s.PostAsync("/api/live/pins", new { gameId = id, apiName = "a" }));
        Assert.Equal(["b", "a"], two.GetProperty("apiNames").EnumerateArray().Select(x => x.GetString()));
        // Pinning again changes nothing and says nothing
        var changes = s.Hub.Count("PinsChanged");
        await s.PostAsync("/api/live/pins", new { gameId = id, apiName = "b" });
        Assert.Equal(changes, s.Hub.Count("PinsChanged"));

        var payload = s.Hub.Sent.Last(e => e.Method == "PinsChanged").Args[0]!;
        Assert.Equal(0u, payload.GetType().GetProperty("appId")!.GetValue(payload));
        Assert.Equal(id, payload.GetType().GetProperty("gameId")!.GetValue(payload));

        var (_, removed) = await ManualServer.SendJsonAsync(s.SendAsync(HttpMethod.Delete, $"/api/live/pins?gameId={id}&apiName=b"));
        Assert.Equal(["a"], removed.GetProperty("apiNames").EnumerateArray().Select(x => x.GetString()));
        var (_, many) = await ManualServer.SendJsonAsync(s.PostAsync("/api/live/pins/remove", new { gameId = id, apiNames = new[] { "a", "zzz" } }));
        Assert.Equal(0, many.GetProperty("apiNames").GetArrayLength());

        // Stored under the game's own key, apart from every Steam game's list
        Assert.False(await s.WithDbAsync(db => db.AppSettings.AnyAsync(a => a.Key == PinnedAchievements.KeyForGame(id))));
    }

    [Fact]
    public async Task A_pin_call_names_a_game_by_appId_or_gameId_never_both_and_an_unknown_game_is_a_404()
    {
        var (s, id) = await PsGameAsync();
        await using var guard = s;

        Assert.Equal(HttpStatusCode.BadRequest, (await ManualServer.SendJsonAsync(s.PostAsync("/api/live/pins", new { appId = 620, gameId = id, apiName = "a" }))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await ManualServer.SendJsonAsync(s.PostAsync("/api/live/pins", new { apiName = "a" }))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.GetAsync("/api/live/pins")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.GetAsync($"/api/live/pins?appId=620&gameId={id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/live/pins?gameId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ManualServer.SendJsonAsync(s.PostAsync("/api/live/pins", new { gameId = Guid.NewGuid(), apiName = "a" }))).Status);
        Assert.False(await s.WithDbAsync(db => db.AppSettings.AnyAsync(a => a.Key.StartsWith("pins."))));
    }

    [Fact]
    public async Task The_eleventh_pin_of_a_manual_game_is_refused()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        var id = await s.SeedAsync("many", "xbox", null, Enumerable.Range(1, 11).Select(i => ($"t{i}", (string?)null, (string?)null)).ToArray());

        for (var i = 1; i <= 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await ManualServer.SendJsonAsync(s.PostAsync("/api/live/pins", new { gameId = id, apiName = $"t{i}" }))).Status);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await ManualServer.SendJsonAsync(s.PostAsync("/api/live/pins", new { gameId = id, apiName = "t11" }))).Status);
    }

    [Fact]
    public async Task Parallel_pins_of_a_manual_game_on_one_database_file_lose_nothing()
    {
        using var scratch = new SqliteScratch();
        await using var seed = await scratch.MigratedAsync();
        var id = Guid.NewGuid();

        await Task.WhenAll(Enumerable.Range(1, 8).Select(async i =>
        {
            await using var db = scratch.Context();
            await PinnedAchievements.AddForGameAsync(db, id, "t" + i);
        }));

        var pinned = await PinnedAchievements.GetForGameAsync(seed, id);
        Assert.Equal(8, pinned.Count);
        Assert.Equal(8, pinned.Distinct().Count());
    }

    [Fact]
    public async Task With_the_setting_pins_after_unlock_remove_a_ticked_trophy_leaves_the_pins_of_its_game()
    {
        var (s, id) = await PsGameAsync();
        await using var guard = s;
        await s.WithDbAsync(async db =>
        {
            db.AppSettings.Add(new AppSettingEntity { Key = SettingsStore.KeyPinsAfterUnlock, Value = "remove", UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        await s.PostAsync("/api/live/pins", new { gameId = id, apiName = "a" });
        await s.PostAsync("/api/live/pins", new { gameId = id, apiName = "b" });
        var before = s.Hub.Count("PinsChanged");

        await Tick(s, "ps-game", "a");

        var (_, pins) = await ManualServer.SendJsonAsync(s.Client.GetAsync($"/api/live/pins?gameId={id}"));
        Assert.Equal(["b"], pins.GetProperty("apiNames").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(before + 1, s.Hub.Count("PinsChanged"));
    }

    [Fact]
    public async Task With_the_default_setting_a_ticked_trophy_stays_pinned()
    {
        var (s, id) = await PsGameAsync();
        await using var guard = s;
        await s.PostAsync("/api/live/pins", new { gameId = id, apiName = "a" });

        await Tick(s, "ps-game", "a");

        var (_, pins) = await ManualServer.SendJsonAsync(s.Client.GetAsync($"/api/live/pins?gameId={id}"));
        Assert.Equal(["a"], pins.GetProperty("apiNames").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task A_pin_by_game_id_of_a_Steam_game_is_a_400_not_a_hidden_second_list()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        var steamId = await s.WithDbAsync(async db =>
        {
            var game = new GameEntity { Id = Guid.NewGuid(), Slug = "steam-game", Title = "S", SteamAppId = 10, Platform = GamePlatforms.Steam };
            db.Games.Add(game);
            await db.SaveChangesAsync();
            return game.Id;
        });

        Assert.Equal(HttpStatusCode.BadRequest, (await ManualServer.SendJsonAsync(s.PostAsync("/api/live/pins", new { gameId = steamId, apiName = "ACH" }))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.GetAsync($"/api/live/pins?gameId={steamId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.SendAsync(HttpMethod.Delete, $"/api/live/pins?gameId={steamId}&apiName=ACH")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ManualServer.SendJsonAsync(s.PostAsync("/api/live/pins/remove", new { gameId = steamId, apiNames = new[] { "ACH" } }))).Status);
        Assert.False(await s.WithDbAsync(db => db.AppSettings.AnyAsync(a => a.Key.StartsWith("pins."))));
    }

    // ---- notes by game id --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_note_by_game_id_of_a_Steam_game_is_a_400_and_nothing_is_stored()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        var steamId = await s.WithDbAsync(async db =>
        {
            var game = new GameEntity { Id = Guid.NewGuid(), Slug = "steam-game", Title = "S", SteamAppId = 10, Platform = GamePlatforms.Steam };
            db.Games.Add(game);
            await db.SaveChangesAsync();
            return game.Id;
        });

        var (status, body) = await ManualServer.SendJsonAsync(s.PostAsync("/api/live/note", new { appId = 0, gameId = steamId, apiName = "ACH", note = "x" }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
        Assert.False(await s.WithDbAsync(db => db.AppSettings.AnyAsync(a => a.Key.StartsWith("note."))));
    }

    [Fact]
    public async Task A_note_is_stored_under_the_spelling_of_the_trophy_key_and_a_name_that_is_no_trophy_is_a_400()
    {
        var (s, id) = await PsGameAsync();
        await using var guard = s;

        Assert.Equal(HttpStatusCode.OK, (await ManualServer.SendJsonAsync(s.PostAsync("/api/live/note", new { appId = 0, gameId = id, apiName = " B ", note = "x" }))).Status);
        var (status, body) = await ManualServer.SendJsonAsync(s.PostAsync("/api/live/note", new { appId = 0, gameId = id, apiName = "no-such-trophy", note = "x" }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("no-such-trophy", body.GetProperty("detail").GetString());
        Assert.Equal([$"note.g.{id:N}.b"], await s.WithDbAsync(db => db.AppSettings.Where(a => a.Key.StartsWith("note.")).Select(a => a.Key).ToListAsync()));
    }

    [Fact]
    public async Task Two_notes_that_differ_only_in_the_case_of_the_key_do_not_break_the_list()
    {
        // Written by an older build: the list used to build a case-insensitive dictionary from them and threw (HTTP 500)
        var (s, id) = await PsGameAsync();
        await using var guard = s;
        await s.WithDbAsync(async db =>
        {
            db.AppSettings.Add(new AppSettingEntity { Key = $"note.g.{id:N}.a", Value = "older", UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2) });
            db.AppSettings.Add(new AppSettingEntity { Key = $"note.g.{id:N}.A", Value = "newer", UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        });

        var (status, list) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/games/ps-game/trophies"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("newer", list.GetProperty("achievements")[0].GetProperty("userNote").GetString());
        // The tick answer builds the same list
        Assert.Equal(HttpStatusCode.OK, (await Tick(s, "ps-game", "b")).Status);
    }

    // ---- guide chapters of the own subscription check apiNames against the trophy list ---------------------------------------------------

    private const string TrophyChapter = """
        { "game": "Game ps-game", "steamAppId": null, "title": "Erfolge im Blick", "icon": "mdi:trophy", "sections": [
          { "type": "alert", "kind": "achievement", "title": "Erfolg", "htmlContent": "Gleich am Anfang.", "apiName": "A" },
          { "type": "alert", "kind": "achievement", "title": "Erfolg", "htmlContent": "Erfunden.", "apiName": "invented-trophy" } ] }
        """;

    [Fact]
    public async Task A_subscription_chapter_of_a_manual_game_is_checked_against_its_trophy_keys_like_a_Steam_game_against_its_list()
    {
        var (s, id) = await PsGameAsync();
        await using var guard = s;

        var (checkStatus, check) = await ManualServer.SendJsonAsync(s.PostAsync("/api/games/ps-game/manual/chapter-check", new { text = TrophyChapter, title = "Erfolge im Blick", topicId = "extra" }));
        var (answerStatus, _) = await ManualServer.SendJsonAsync(s.PostAsync("/api/games/ps-game/manual/chapter-answer", new { text = TrophyChapter, title = "Erfolge im Blick", topicId = "extra" }));

        Assert.Equal(HttpStatusCode.OK, checkStatus);
        Assert.True(check.GetProperty("ok").GetBoolean(), check.ToString());
        var warning = Assert.Single(check.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "unknownApiName");
        Assert.Contains("invented-trophy", warning.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, answerStatus);
        // Saved: the known name in the spelling of the list, the invented one dropped
        var json = await s.WithDbAsync(db => db.GuideTabs.Where(t => t.GameId == id).Select(t => t.SectionsJson).SingleAsync());
        Assert.Contains("\"apiName\":\"a\"", json.Replace(" ", ""));
        Assert.DoesNotContain("invented-trophy", json);
    }

    [Fact]
    public async Task A_subscription_chapter_of_a_manual_game_without_a_list_is_not_judged()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await s.SeedAsync("no-list", "gog", null);

        var (status, check) = await ManualServer.SendJsonAsync(s.PostAsync("/api/games/no-list/manual/chapter-check",
            new { text = TrophyChapter.Replace("Game ps-game", "Game no-list"), title = "Erfolge im Blick", topicId = "extra" }));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain(check.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "unknownApiName");
    }

    [Fact]
    public async Task The_notes_of_a_trophy_of_another_game_stay_apart()
    {
        // The unknown-key rule is per game: a key of game A is no trophy of game B
        var (s, _) = await PsGameAsync();
        await using var guard = s;
        var other = await s.SeedAsync("other-game", "xbox", null, ("only-here", null, null));

        var (status, _) = await ManualServer.SendJsonAsync(s.PostAsync("/api/live/note", new { appId = 0, gameId = other, apiName = "a", note = "x" }));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    // ---- notes by game id (continued) ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_note_on_a_trophy_of_a_manual_game_is_stored_by_game_id_shown_in_the_list_and_removed_when_empty()
    {
        var (s, id) = await PsGameAsync();
        await using var guard = s;

        var (status, saved) = await ManualServer.SendJsonAsync(s.PostAsync("/api/live/note", new { appId = 0, gameId = id, apiName = "b", note = "  second boss  " }));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(saved.GetProperty("success").GetBoolean());
        Assert.Equal("second boss", await s.WithDbAsync(db => db.AppSettings.Where(a => a.Key == $"note.g.{id:N}.b").Select(a => a.Value).SingleAsync()));

        var (_, list) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/games/ps-game/trophies"));
        Assert.Equal("second boss", list.GetProperty("achievements").EnumerateArray().Single(a => a.GetProperty("apiName").GetString() == "b").GetProperty("userNote").GetString());

        await s.PostAsync("/api/live/note", new { appId = 0, gameId = id, apiName = "b", note = "  " });
        Assert.False(await s.WithDbAsync(db => db.AppSettings.AnyAsync(a => a.Key.StartsWith("note."))));
    }

    [Fact]
    public async Task A_note_names_a_game_by_appId_or_gameId_and_the_game_must_exist()
    {
        var (s, id) = await PsGameAsync();
        await using var guard = s;

        Assert.Equal(HttpStatusCode.BadRequest, (await ManualServer.SendJsonAsync(s.PostAsync("/api/live/note", new { appId = 620, gameId = id, apiName = "a", note = "x" }))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await ManualServer.SendJsonAsync(s.PostAsync("/api/live/note", new { appId = 0, gameId = Guid.NewGuid(), apiName = "a", note = "x" }))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await ManualServer.SendJsonAsync(s.PostAsync("/api/live/note", new { appId = 0, gameId = id, apiName = "", note = "x" }))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await ManualServer.SendJsonAsync(s.PostAsync("/api/live/note", new { appId = 0, apiName = "a", note = "x" }))).Status);
        // A Steam game's note still works as before
        Assert.Equal(HttpStatusCode.OK, (await ManualServer.SendJsonAsync(s.PostAsync("/api/live/note", new { appId = 620, apiName = "ACH", note = "x" }))).Status);
        Assert.True(await s.WithDbAsync(db => db.AppSettings.AnyAsync(a => a.Key == "note.620.ACH")));
    }
}
