using System.Net;
using System.Text;
using System.Text.Json;
using Companion.Api.Data;
using Companion.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace Companion.Api.Tests;

/// <summary>Guide export / import of games added by hand (trophy list yes, ticks never) and the trophy cabinet with games that have no Steam AppID.</summary>
public sealed class ManualGameTransferTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ImportAsync(ManualServer s, object document) =>
        await ImportRawAsync(s, JsonSerializer.Serialize(document));

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ImportRawAsync(ManualServer s, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await s.Client.PostAsync("/api/games/import", content);
        return (response.StatusCode, await ManualServer.ReadAsync(response));
    }

    private static object Doc(object game, object? trophies = null, object? trophySources = null) => new Dictionary<string, object?>
    {
        ["format"] = "platinvault-guide",
        ["formatVersion"] = 1,
        ["game"] = game,
        ["tabs"] = Array.Empty<object>(),
        ["trophies"] = trophies,
        ["trophySources"] = trophySources,
    };

    private static object Trophy(string key, string name, string? grade = null, string? group = null, bool hidden = false) => new Dictionary<string, object?>
    {
        ["key"] = key,
        ["name"] = name,
        ["description"] = "Do " + name,
        ["grade"] = grade,
        ["group"] = group,
        ["hidden"] = hidden,
    };

    // ---- export -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_export_of_a_manual_game_carries_platform_facts_and_the_list_but_no_ticks_and_no_local_cover()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        var id = await s.SeedAsync("ps-game", "playstation", "ps5", ("a", "bronze", null), ("b", "platinum", null), ("dlc", "gold", "Extra Levels"));
        await s.WithDbAsync(db => db.GameTrophies.Where(t => t.TrophyKey == "dlc").ExecuteUpdateAsync(t => t
            .SetProperty(x => x.IsHidden, true).SetProperty(x => x.Tip, "Look behind the door").SetProperty(x => x.RarityPercent, 4.5)));
        await s.WithDbAsync(db => db.Games.Where(g => g.Id == id).ExecuteUpdateAsync(g => g
            .SetProperty(x => x.IgdbId, 1001).SetProperty(x => x.FactsSource, "igdb").SetProperty(x => x.Developer, "Fake Studio")
            .SetProperty(x => x.TrophyListSourcesJson, "[{\"title\":\"PSNProfiles\",\"url\":\"https://psnprofiles.com/x\"}]")));
        await s.SendAsync(HttpMethod.Put, "/api/games/ps-game/cover", Png, "image/png");
        await s.PostAsync("/api/games/ps-game/trophies/a/unlock", new { unlocked = true });

        using var response = await s.Client.GetAsync("/api/games/ps-game/export");
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        var game = doc.RootElement.GetProperty("game");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("playstation", "ps5", 1001, "igdb", "Fake Studio"), (
            game.GetProperty("platform").GetString(), game.GetProperty("platformVariant").GetString(), game.GetProperty("igdbId").GetInt32(),
            game.GetProperty("factsSource").GetString(), game.GetProperty("developer").GetString()));
        // The address of an upload means nothing on another installation
        Assert.Equal(string.Empty, game.GetProperty("coverUrl").GetString());
        var trophies = doc.RootElement.GetProperty("trophies").EnumerateArray().ToList();
        Assert.Equal(["a", "b", "dlc"], trophies.Select(t => t.GetProperty("key").GetString()));
        Assert.Equal("platinum", trophies[1].GetProperty("grade").GetString());
        Assert.Equal("Extra Levels", trophies[2].GetProperty("group").GetString());
        Assert.True(trophies[2].GetProperty("hidden").GetBoolean());
        Assert.Equal(4.5, trophies[2].GetProperty("rarityPercent").GetDouble());
        Assert.Equal("Look behind the door", trophies[2].GetProperty("tip").GetString());
        // Ticks are progress: never in a file
        Assert.DoesNotContain("unlock", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("achieved", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://psnprofiles.com/x", doc.RootElement.GetProperty("trophySources")[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task The_export_of_a_Steam_game_is_exactly_what_it_was_before()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await s.WithDbAsync(async db =>
        {
            db.Games.Add(new GameEntity { Id = Guid.NewGuid(), Slug = "steam-game", Title = "S", SteamAppId = 620, ContentLanguage = "de" });
            await db.SaveChangesAsync();
        });

        using var response = await s.Client.GetAsync("/api/games/steam-game/export");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(["format", "formatVersion", "exportedAt", "exportedBy", "game", "tabs"], doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            ["title", "subtitle", "steamAppId", "coverUrl", "heroBannerUrl", "accentColor", "estimatedHours", "missablesInfo", "difficultyInfo", "genreTags", "contentLanguage"],
            doc.RootElement.GetProperty("game").EnumerateObject().Select(p => p.Name));
    }

    // ---- import -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_manual_game_survives_export_and_import_without_its_ticks()
    {
        await using var from = await ManualServer.StartAsync(credentials: false);
        await from.SeedAsync("ps-game", "playstation", "ps5", ("a", "bronze", null), ("b", "platinum", null), ("dlc", "gold", "Extra Levels"));
        await from.WithDbAsync(db => db.GameTrophies.Where(t => t.TrophyKey == "dlc").ExecuteUpdateAsync(t => t
            .SetProperty(x => x.IsHidden, true).SetProperty(x => x.IsMissable, true).SetProperty(x => x.Category, "DLC").SetProperty(x => x.Points, 30)));
        await from.WithDbAsync(db => db.Games.ExecuteUpdateAsync(g => g
            .SetProperty(x => x.TrophyListSourcesJson, "[{\"title\":\"PSNProfiles\",\"url\":\"https://psnprofiles.com/x\"}]")
            .SetProperty(x => x.Summary, "A summary")));
        await from.PostAsync("/api/games/ps-game/trophies/a/unlock", new { unlocked = true });
        var file = await from.Client.GetStringAsync("/api/games/ps-game/export");

        await using var to = await ManualServer.StartAsync(credentials: false);
        var (status, summary) = await ImportRawAsync(to, file);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(("playstation", "ps5", true, 3, 0), (
            summary.GetProperty("platform").GetString(), summary.GetProperty("platformVariant").GetString(), summary.GetProperty("hasTrophyList").GetBoolean(),
            summary.GetProperty("trophiesTotal").GetInt32(), summary.GetProperty("trophiesUnlocked").GetInt32()));
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("steamAppId").ValueKind);
        Assert.Equal(0, summary.GetProperty("tabCount").GetInt32());

        await to.WithDbAsync(async db =>
        {
            var game = await db.Games.Include(g => g.Tabs).SingleAsync();
            Assert.Empty(game.Tabs);
            Assert.Equal("A summary", game.Summary);
            Assert.NotNull(game.TrophyListResearchedAt);
            Assert.Null(game.PlatinumAt);
            Assert.Contains("psnprofiles.com", game.TrophyListSourcesJson);
            var trophies = await db.GameTrophies.AsNoTracking().OrderBy(t => t.SortOrder).ToListAsync();
            Assert.Equal(["a", "b", "dlc"], trophies.Select(t => t.TrophyKey));
            Assert.All(trophies, t => Assert.Null(t.UnlockedAt));
            var dlc = trophies[2];
            Assert.Equal(("gold", "Extra Levels", true, true, "DLC", 30), (dlc.Grade, dlc.GroupName, dlc.IsHidden, dlc.IsMissable, dlc.Category, dlc.Points));
        });

        // The imported list works like a researched one: it can be ticked
        var (tickStatus, tick) = await ManualServer.SendJsonAsync(to.PostAsync($"/api/games/{summary.GetProperty("slug").GetString()}/trophies/a/unlock", new { unlocked = true }));
        Assert.Equal(HttpStatusCode.OK, tickStatus);
        // "a" is the only base-game trophy besides the platinum one: ticking it takes the platinum trophy along (D3)
        Assert.Equal(2, tick.GetProperty("achievedCount").GetInt32());
        Assert.True(tick.GetProperty("announced").GetProperty("platinum").GetBoolean());
    }

    [Fact]
    public async Task Importing_the_same_manual_game_twice_is_a_409_with_the_slug_but_another_platform_is_fine()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        var game = new { title = "Hades", platform = "gog" };

        Assert.Equal(HttpStatusCode.OK, (await ImportAsync(s, Doc(game))).Status);
        var (status, body) = await ImportAsync(s, Doc(game));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("hades", body.GetProperty("existingSlug").GetString());
        Assert.Equal(HttpStatusCode.OK, (await ImportAsync(s, Doc(new { title = "Hades", platform = "epic" }))).Status);
        Assert.Equal(HttpStatusCode.OK, (await ImportAsync(s, Doc(new { title = "Hades", platform = "playstation", platformVariant = "ps4" }))).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await ImportAsync(s, Doc(new { title = "Hades", platform = "playstation", platformVariant = "ps4" }))).Status);
    }

    [Fact]
    public async Task An_ordinary_file_without_a_platform_stays_a_Steam_style_guide_with_its_starter_tab()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);

        var (status, summary) = await ImportAsync(s, Doc(new { title = "Old Guide" }));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("steam", summary.GetProperty("platform").GetString());
        Assert.Equal(1, summary.GetProperty("tabCount").GetInt32());
    }

    [Fact]
    public async Task A_grade_on_a_platform_without_grades_is_dropped_and_the_order_of_the_list_is_kept()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);

        await ImportAsync(s, Doc(new { title = "X Game", platform = "xbox" }, new[] { Trophy("z", "Zed", "gold"), Trophy("y", "Why"), Trophy("x", "Ex") }));

        await s.WithDbAsync(async db =>
        {
            var list = await db.GameTrophies.AsNoTracking().OrderBy(t => t.SortOrder).ToListAsync();
            Assert.Equal(["z", "y", "x"], list.Select(t => t.TrophyKey));
            Assert.All(list, t => Assert.Null(t.Grade));
        });
    }

    [Theory]
    [InlineData("""{"title":"G","platform":"playstation"}""", null)]
    [InlineData("""{"title":"G","platform":"playstation","platformVariant":"ps3"}""", null)]
    [InlineData("""{"title":"G","platform":"dreamcast"}""", null)]
    [InlineData("""{"title":"G","platform":"gog","steamAppId":620}""", null)]
    [InlineData("""{"title":"G","platform":"gog","platformVariant":"ps5"}""", null)]
    [InlineData("""{"title":"G","platform":"gog","igdbId":-4}""", null)]
    [InlineData("""{"title":"G","platform":"gog","igdbId":"x"}""", null)]
    [InlineData("""{"title":"G","platform":"gog","firstReleaseAt":"not a date"}""", null)]
    [InlineData("""{"title":"G"}""", """[{"key":"a","name":"A"}]""")]
    [InlineData("""{"title":"G","platform":"steam"}""", """[{"key":"a","name":"A"}]""")]
    [InlineData("""{"title":"G","platform":"nintendo"}""", """[{"key":"a","name":"A"}]""")]
    [InlineData("""{"title":"G","platform":"gog"}""", """{"key":"a"}""")]
    [InlineData("""{"title":"G","platform":"gog"}""", """["text"]""")]
    [InlineData("""{"title":"G","platform":"gog"}""", """[{"name":"A"}]""")]
    [InlineData("""{"title":"G","platform":"gog"}""", """[{"key":"a"}]""")]
    [InlineData("""{"title":"G","platform":"gog"}""", """[{"key":"a","name":"A"},{"key":"A","name":"B"}]""")]
    [InlineData("""{"title":"G","platform":"playstation","platformVariant":"ps5"}""", """[{"key":"a","name":"A","grade":"diamond"}]""")]
    [InlineData("""{"title":"G","platform":"gog"}""", """[{"key":"a","name":"A","rarityPercent":101}]""")]
    [InlineData("""{"title":"G","platform":"gog"}""", """[{"key":"a","name":"A","rarityPercent":"high"}]""")]
    [InlineData("""{"title":"G","platform":"gog"}""", """[{"key":"a","name":"A","points":-1}]""")]
    [InlineData("""{"title":"G","platform":"gog"}""", """[{"key":"a","name":"A","hidden":"yes","description":5}]""")]
    public async Task A_file_with_a_wrong_platform_or_trophy_is_refused_with_a_reason_and_nothing_is_saved(string game, string? trophies)
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        var json = $$"""{"format":"platinvault-guide","formatVersion":1,"game":{{game}},"tabs":[]{{(trophies is null ? "" : ",\"trophies\":" + trophies)}}}""";

        var (status, body) = await ImportRawAsync(s, json);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
        Assert.Equal(0, await s.WithDbAsync(db => db.Games.CountAsync()));
        Assert.Equal(0, await s.WithDbAsync(db => db.GameTrophies.CountAsync()));
    }

    [Fact]
    public async Task An_imported_trophy_key_that_breaks_the_key_rule_is_rebuilt_from_the_name_and_stays_unique()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);

        var (status, _) = await ImportAsync(s, Doc(new { title = "G", platform = "gog" }, new object[]
        {
            Trophy("Good-Key", "Fine one"),                         // upper case: lower-cased
            Trophy("Bad Key!", "First Lap"),                        // not a kebab id: rebuilt from the name
            Trophy("another key", "First Lap"),                     // rebuilt to the same key: made unique
            Trophy("---", "***"),                                   // nothing usable in the name either
            Trophy(new string('k', 70), "Long"),                    // longer than the 64 of the key rule
        }));

        Assert.Equal(HttpStatusCode.OK, status);
        var keys = await s.WithDbAsync(db => db.GameTrophies.OrderBy(t => t.SortOrder).Select(t => t.TrophyKey).ToListAsync());
        Assert.Equal(["good-key", "first-lap", "first-lap-2", "trophy-4", "long"], keys);
        Assert.All(keys, k => Assert.Matches("^[a-z0-9][a-z0-9-]{0,63}$", k));
    }

    [Fact]
    public async Task The_duplicate_check_of_an_import_folds_the_case_of_non_ASCII_letters_too()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        Assert.Equal(HttpStatusCode.OK, (await ImportAsync(s, Doc(new { title = "Ökoland", platform = "gog" }))).Status);

        var (status, body) = await ImportAsync(s, Doc(new { title = "ÖKOLAND", platform = "gog" }));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("existingSlug").GetString()));
        Assert.Equal(1, await s.WithDbAsync(db => db.Games.CountAsync()));
    }

    [Fact]
    public async Task Too_long_texts_too_many_trophies_and_odd_sources_are_handled()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);

        var tooMany = Enumerable.Range(0, 1001).Select(i => Trophy("t" + i, "T" + i)).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(s, Doc(new { title = "G", platform = "gog" }, tooMany))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(s, Doc(new { title = "G", platform = "gog" }, new[] { Trophy(new string('k', 81), "A") }))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await ImportAsync(s, Doc(new { title = "G", platform = "gog" }, new[] { Trophy("a", new string('n', 241)) }))).Status);

        // Sources: only web addresses survive, the rest is dropped without an error
        var (status, _) = await ImportAsync(s, Doc(
            new { title = "G", platform = "gog" },
            new[] { Trophy("a", "A") },
            new object[] { new { title = "ok", url = "https://example.org/a" }, new { title = "evil", url = "javascript:alert(1)" }, new { title = "rel", url = "/x" } }));
        Assert.Equal(HttpStatusCode.OK, status);
        var sources = await s.WithDbAsync(db => db.Games.Select(g => g.TrophyListSourcesJson).SingleAsync());
        Assert.Contains("example.org", sources);
        Assert.DoesNotContain("javascript", sources);
        Assert.DoesNotContain("\"/x\"", sources);
    }

    [Fact]
    public async Task A_list_that_comes_with_the_file_is_exactly_the_one_that_was_exported_when_it_has_no_trophies_at_all()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);

        var (status, summary) = await ImportAsync(s, Doc(new { title = "Zelda", platform = "nintendo", developer = "Nintendo" }));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(summary.GetProperty("hasTrophyList").GetBoolean());
        Assert.Equal("nintendo", summary.GetProperty("platform").GetString());
        Assert.Equal("Nintendo", await s.WithDbAsync(db => db.Games.Select(g => g.Developer).SingleAsync()));
    }

    // ---- trophy cabinet -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_manual_platinum_appears_in_the_cabinet_at_once_with_the_figures_of_its_list_and_leaves_when_it_is_lost()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        var id = await s.SeedAsync("xb", "xbox", null, ("x", null, null), ("y", null, null));
        await s.WithDbAsync(async db =>
        {
            await db.GameTrophies.Where(t => t.TrophyKey == "x").ExecuteUpdateAsync(t => t.SetProperty(v => v.RarityPercent, 2.0));
            await db.GameTrophies.Where(t => t.TrophyKey == "y").ExecuteUpdateAsync(t => t.SetProperty(v => v.RarityPercent, 10.0));
            await db.Games.Where(g => g.Id == id).ExecuteUpdateAsync(g => g.SetProperty(v => v.CoverUrl, "https://example.org/c.jpg"));
        });

        // Nothing at 100 % yet (and this answer is cached for ten minutes)
        var (_, before) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/showcase"));
        Assert.Equal(0, before.GetProperty("games").GetArrayLength());

        await s.PostAsync("/api/games/xb/trophies/x/unlock", new { unlocked = true });
        await s.PostAsync("/api/games/xb/trophies/y/unlock", new { unlocked = true });
        var (_, after) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/showcase"));

        var entry = Assert.Single(after.GetProperty("games").EnumerateArray());
        Assert.Equal($"game:{id:D}", entry.GetProperty("key").GetString());
        Assert.Equal(0, entry.GetProperty("appId").GetInt32());
        Assert.Equal(id, entry.GetProperty("gameId").GetGuid());
        Assert.Equal(("xbox", "Game xb", "xb"), (entry.GetProperty("platform").GetString(), entry.GetProperty("name").GetString(), entry.GetProperty("existingSlug").GetString()));
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("platformVariant").ValueKind);
        Assert.Equal(2, entry.GetProperty("trophiesTotal").GetInt32());
        Assert.Equal(JsonValueKind.String, entry.GetProperty("platinumAt").ValueKind);
        Assert.Equal(JsonValueKind.String, entry.GetProperty("firstUnlockAt").ValueKind);
        Assert.Equal(1, entry.GetProperty("daysToPlatinum").GetInt32());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("playtimeHours").ValueKind);
        Assert.Equal(("Trophy x", 2.0, 6.0), (entry.GetProperty("rarestName").GetString(), entry.GetProperty("rarestPercent").GetDouble(), entry.GetProperty("averageRarityPercent").GetDouble()));
        Assert.Equal("https://example.org/c.jpg", entry.GetProperty("coverUrl").GetString());
        Assert.Equal(2, after.GetProperty("totalTrophies").GetInt32());

        // A platinum that is taken back leaves the cabinet at once as well
        await s.PostAsync("/api/games/xb/trophies/y/unlock", new { unlocked = false });
        var (_, gone) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/showcase"));
        Assert.Equal(0, gone.GetProperty("games").GetArrayLength());
    }

    [Fact]
    public async Task The_cabinet_remembers_presented_games_by_id_in_a_row_of_its_own_and_only_ever_adds()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var (_, none) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/showcase/presented"));
        Assert.False(none.GetProperty("initialized").GetBoolean());
        Assert.Equal(0, none.GetProperty("gameIds").GetArrayLength());

        var (_, one) = await ManualServer.SendJsonAsync(s.PutAsync("/api/showcase/presented", new { appIds = new[] { 620 }, gameIds = new[] { first } }));
        Assert.True(one.GetProperty("initialized").GetBoolean());
        Assert.Equal([620], one.GetProperty("appIds").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal([first], one.GetProperty("gameIds").EnumerateArray().Select(x => x.GetGuid()));

        // A union, an empty id is ignored, an old-style request (no gameIds) keeps them
        await s.PutAsync("/api/showcase/presented", new { gameIds = new[] { second, Guid.Empty, first } });
        var (_, both) = await ManualServer.SendJsonAsync(s.PutAsync("/api/showcase/presented", new { appIds = new[] { 70 } }));
        Assert.Equal([70, 620], both.GetProperty("appIds").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(new[] { first, second }.Order(), both.GetProperty("gameIds").EnumerateArray().Select(x => x.GetGuid()).Order());

        Assert.True(await s.WithDbAsync(db => db.AppSettings.AnyAsync(a => a.Key == CabinetPresented.GamesKey)));
        Assert.True(await s.WithDbAsync(db => db.AppSettings.AnyAsync(a => a.Key == CabinetPresented.Key)));
    }

    [Fact]
    public async Task A_Steam_cabinet_without_a_games_row_reads_as_nothing_presented_yet_so_new_manual_platinums_animate()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await s.PutAsync("/api/showcase/presented", new { appIds = new[] { 620 } });

        var (_, state) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/showcase/presented"));

        Assert.True(state.GetProperty("initialized").GetBoolean());
        Assert.Equal(0, state.GetProperty("gameIds").GetArrayLength());
    }

    [Fact]
    public void The_presented_games_are_read_defensively_and_merged_as_a_capped_union()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.Empty(CabinetPresented.ParseGames(null));
        Assert.Empty(CabinetPresented.ParseGames("not json"));
        Assert.Empty(CabinetPresented.ParseGames("{\"a\":1}"));
        Assert.Equal([a], CabinetPresented.ParseGames($"[\"{a}\",\"{a}\",\"{Guid.Empty}\"]"));
        Assert.Equal(new[] { a, b }.Order(), CabinetPresented.MergeGames([a], [b, Guid.Empty, a]).Order());
        Assert.Equal(CabinetPresented.MaxIds, CabinetPresented.MergeGames(null, Enumerable.Range(0, CabinetPresented.MaxIds + 100).Select(_ => Guid.NewGuid())).Count);
        Assert.Equal(new[] { a, b }.Order(), CabinetPresented.ParseGames(CabinetPresented.SerializeGames([b, a])).Order());
    }
}
