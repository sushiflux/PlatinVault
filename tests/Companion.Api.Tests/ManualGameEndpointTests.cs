using System.Net;
using System.Text.Json;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Companion.Api.Tests;

/// <summary>Adding games by hand (with and without IGDB), the IGDB endpoints, the uploaded cover, linking, removing and the "All games" listing.</summary>
public sealed class ManualGameEndpointTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 73, 72, 68, 82];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 74, 70, 73, 70];
    private static readonly byte[] Gif = "GIF89a"u8.ToArray();
    private static readonly byte[] Webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();

    private static Task<(HttpStatusCode Status, JsonElement Body)> Create(ManualServer s, object body) =>
        ManualServer.SendJsonAsync(s.PostAsync("/api/games/manual", body));

    // ---- IGDB endpoints -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_search_answers_hits_with_existing_games_and_the_agreed_shape()
    {
        await using var s = await ManualServer.StartAsync();
        await Create(s, new { platform = "playstation", platformVariant = "ps5", igdbId = 1001 });

        var (status, body) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/igdb/search?q=Fake%20Quest"));

        Assert.Equal(HttpStatusCode.OK, status);
        var hits = body.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal([1001, 1002], hits.Select(h => h.GetProperty("igdbId").GetInt32()));
        Assert.Equal(
            ["igdbId", "name", "releaseYear", "firstReleaseAt", "coverUrl", "platforms", "existing"],
            hits[0].EnumerateObject().Select(p => p.Name));
        var existing = hits[0].GetProperty("existing").EnumerateArray().Single();
        Assert.Equal(("playstation", "ps5"), (existing.GetProperty("platform").GetString(), existing.GetProperty("variant").GetString()));
        Assert.Equal("fake-quest", existing.GetProperty("slug").GetString());
        Assert.Empty(hits[1].GetProperty("existing").EnumerateArray());
        var platform = hits[0].GetProperty("platforms").EnumerateArray().First();
        Assert.Equal("ps5", platform.GetProperty("maps").GetProperty("variant").GetString());
    }

    [Fact]
    public async Task The_search_answers_204_without_credentials_400_for_nothing_and_502_when_IGDB_fails()
    {
        await using (var none = await ManualServer.StartAsync(credentials: false))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await none.Client.GetAsync("/api/igdb/search?q=quest")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await none.Client.GetAsync("/api/igdb/games/1001")).StatusCode);
            Assert.Empty(none.Igdb.Requests);
        }

        await using var s = await ManualServer.StartAsync();
        var empty = await s.Client.GetAsync("/api/igdb/search?q=%20%20");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.GetAsync("/api/igdb/search?q=" + new string('a', 121))).StatusCode);

        s.Igdb.Fail = true;
        var (status, body) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/igdb/search?q=quest"));
        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task The_facts_of_one_game_come_with_companies_and_an_unknown_id_is_a_404()
    {
        await using var s = await ManualServer.StartAsync();

        var (status, body) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/igdb/games/1001"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Fake Studio", body.GetProperty("developers")[0].GetString());
        Assert.Equal(2, body.GetProperty("platforms").GetArrayLength());
        Assert.Equal(JsonValueKind.String, body.GetProperty("firstReleaseAt").ValueKind);

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync("/api/igdb/games/424242")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.GetAsync("/api/igdb/games/0")).StatusCode);
    }

    // ---- adding a game ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_game_without_IGDB_needs_a_title_and_a_platform_and_starts_as_a_favourite_without_tabs()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);

        var (status, body) = await Create(s, new { title = "  My Game  ", platform = "XBOX" });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("My Game", body.GetProperty("title").GetString());
        Assert.Equal("my-game", body.GetProperty("slug").GetString());
        Assert.Equal("xbox", body.GetProperty("platform").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("platformVariant").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("steamAppId").ValueKind);
        Assert.True(body.GetProperty("isFavorite").GetBoolean());
        Assert.False(body.GetProperty("hasTrophyList").GetBoolean());
        Assert.Equal(0, body.GetProperty("tabCount").GetInt32());

        await s.WithDbAsync(async db =>
        {
            var game = await db.Games.Include(g => g.Tabs).SingleAsync();
            Assert.Empty(game.Tabs);
            Assert.Null(game.SteamAppId);
            Assert.Null(game.IgdbId);
            Assert.Null(game.FactsSource);
            Assert.Equal(string.Empty, game.CoverUrl);
            // Sorted behind every other game; the guide language is the language of the request
            Assert.NotNull(game.ContentLanguage);
        });
    }

    [Theory]
    [InlineData("{\"title\":\"X\"}")]
    [InlineData("{\"title\":\"X\",\"platform\":\"steam\"}")]
    [InlineData("{\"title\":\"X\",\"platform\":\"dreamcast\"}")]
    [InlineData("{\"title\":\"X\",\"platform\":\"playstation\"}")]
    [InlineData("{\"title\":\"X\",\"platform\":\"playstation\",\"platformVariant\":\"ps3\"}")]
    [InlineData("{\"title\":\"X\",\"platform\":\"xbox\",\"platformVariant\":\"ps5\"}")]
    [InlineData("{\"platform\":\"xbox\"}")]
    [InlineData("{\"title\":\"   \",\"platform\":\"gog\"}")]
    public async Task A_wrong_platform_or_variant_or_a_missing_title_is_a_400_with_a_reason(string json)
    {
        await using var s = await ManualServer.StartAsync(credentials: false);

        using var response = await s.Client.PostAsync("/api/games/manual", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        var body = await ManualServer.ReadAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
        Assert.Equal(0, await s.WithDbAsync(db => db.Games.CountAsync()));
    }

    [Fact]
    public async Task A_title_over_240_characters_is_refused()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s, new { title = new string('x', 241), platform = "gog" })).Status);
        Assert.Equal(HttpStatusCode.Created, (await Create(s, new { title = new string('x', 240), platform = "gog" })).Status);
    }

    [Fact]
    public async Task The_same_title_twice_on_one_platform_is_a_409_with_the_slug_but_another_platform_or_variant_is_fine()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);

        var first = await Create(s, new { title = "Hades", platform = "gog" });
        var (status, body) = await Create(s, new { title = " hades ", platform = "gog" });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(first.Body.GetProperty("slug").GetString(), body.GetProperty("existingSlug").GetString());
        Assert.Contains("Hades", body.GetProperty("detail").GetString());

        Assert.Equal(HttpStatusCode.Created, (await Create(s, new { title = "Hades", platform = "epic" })).Status);
        Assert.Equal(HttpStatusCode.Created, (await Create(s, new { title = "Hades", platform = "playstation", platformVariant = "ps5" })).Status);
        Assert.Equal(HttpStatusCode.Created, (await Create(s, new { title = "Hades", platform = "playstation", platformVariant = "ps4" })).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Create(s, new { title = "Hades", platform = "playstation", platformVariant = "ps4" })).Status);
        // The second game of that title got its own address
        Assert.Equal(4, await s.WithDbAsync(db => db.Games.Select(g => g.Slug).Distinct().CountAsync()));
    }

    [Fact]
    public async Task The_duplicate_check_folds_the_case_of_non_ASCII_letters_too()
    {
        // SQLite's lower() folds ASCII only: "Ökoland" and "ÖKOLAND" (and "ökoland") must still be the same game
        await using var s = await ManualServer.StartAsync(credentials: false);

        var first = await Create(s, new { title = "Ökoland", platform = "gog" });
        Assert.Equal(HttpStatusCode.Created, first.Status);
        var (status, body) = await Create(s, new { title = "ÖKOLAND", platform = "gog" });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(first.Body.GetProperty("slug").GetString(), body.GetProperty("existingSlug").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Create(s, new { title = "ökoland", platform = "gog" })).Status);
        Assert.Equal(1, await s.WithDbAsync(db => db.Games.CountAsync()));
        // Another platform is a game of its own
        Assert.Equal(HttpStatusCode.Created, (await Create(s, new { title = "ÖKOLAND", platform = "epic" })).Status);
    }

    [Fact]
    public async Task With_an_IGDB_id_the_server_fills_title_cover_banner_and_facts()
    {
        await using var s = await ManualServer.StartAsync();

        var (status, body) = await Create(s, new { platform = "playstation", platformVariant = "ps5", igdbId = 1001 });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("Fake Quest", body.GetProperty("title").GetString());
        Assert.Equal(1001, body.GetProperty("igdbId").GetInt32());
        Assert.EndsWith("/t_cover_big_2x/co1abc.jpg", body.GetProperty("coverUrl").GetString());
        Assert.Equal(82, body.GetProperty("igdbRating").GetInt32());
        Assert.Equal(72, body.GetProperty("igdbUserRating").GetInt32());
        Assert.Equal(40.0, body.GetProperty("completionHours").GetDouble());
        // The release date sorts by its UTC value and shows as text in the language of the request (German by default)
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), body.GetProperty("releaseAt").GetDateTimeOffset());
        Assert.Equal("14. Nov. 2023", body.GetProperty("releaseDate").GetString());

        await s.WithDbAsync(async db =>
        {
            var game = await db.Games.SingleAsync();
            Assert.EndsWith("/t_1080p/ar1xyz.jpg", game.HeroBannerUrl);
            Assert.Equal("igdb", game.FactsSource);
            Assert.Equal("Fake Studio", game.Developer);
            Assert.Equal("Fake Publishing, Fake Studio", game.Publisher);
            Assert.Equal("A made-up game.", game.Summary);
            Assert.Equal(["Role-playing (RPG)", "Adventure"], game.SteamGenres);
            Assert.Equal("en", game.SteamGenresLanguage);
            Assert.NotNull(game.FactsUpdatedAt);
            Assert.Null(game.SteamAppId);
        });
    }

    [Fact]
    public async Task A_title_typed_by_the_player_wins_over_IGDBs_name()
    {
        await using var s = await ManualServer.StartAsync();
        var (_, body) = await Create(s, new { title = "Mein Schatz", platform = "xbox", igdbId = 1001 });
        Assert.Equal("Mein Schatz", body.GetProperty("title").GetString());
        Assert.Equal(1001, body.GetProperty("igdbId").GetInt32());
    }

    [Fact]
    public async Task The_same_IGDB_game_on_the_same_platform_twice_is_a_409_whatever_the_title()
    {
        await using var s = await ManualServer.StartAsync();
        var first = await Create(s, new { platform = "xbox", igdbId = 1001 });

        var (status, body) = await Create(s, new { title = "Anderer Titel", platform = "xbox", igdbId = 1001 });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(first.Body.GetProperty("slug").GetString(), body.GetProperty("existingSlug").GetString());
        // The same game for another platform is a game of its own
        Assert.Equal(HttpStatusCode.Created, (await Create(s, new { platform = "gog", igdbId = 1001 })).Status);
    }

    [Fact]
    public async Task An_IGDB_id_without_credentials_is_a_400_and_IGDB_failing_is_a_502_and_an_unknown_id_a_404()
    {
        await using (var none = await ManualServer.StartAsync(credentials: false))
        {
            var (status, body) = await Create(none, new { platform = "gog", igdbId = 1001 });
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("Twitch", body.GetProperty("detail").GetString());
            Assert.Equal(0, await none.WithDbAsync(db => db.Games.CountAsync()));
        }

        await using var s = await ManualServer.StartAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Create(s, new { platform = "gog", igdbId = 424242 })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(s, new { platform = "gog", igdbId = -3 })).Status);

        s.Igdb.Fail = true;
        Assert.Equal(HttpStatusCode.BadGateway, (await Create(s, new { platform = "gog", igdbId = 1002 })).Status);
        Assert.Equal(0, await s.WithDbAsync(db => db.Games.CountAsync()));
    }

    [Fact]
    public async Task The_new_game_goes_to_the_end_of_the_dashboard()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await s.WithDbAsync(async db =>
        {
            db.Games.Add(new GameEntity { Id = Guid.NewGuid(), Slug = "old", Title = "Old", SortOrder = 7 });
            await db.SaveChangesAsync();
        });

        await Create(s, new { title = "New", platform = "gog" });

        Assert.Equal(8, await s.WithDbAsync(db => db.Games.Where(g => g.Slug == "new").Select(g => g.SortOrder).SingleAsync()));
    }

    // ---- linking, facts, series -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_game_added_by_hand_can_never_be_linked_to_a_steam_app()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await Create(s, new { title = "Hades", platform = "gog" });

        var (status, body) = await ManualServer.SendJsonAsync(s.PutAsync("/api/games/hades/steam-app", new { steamAppId = 1145360 }));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("GOG", body.GetProperty("detail").GetString());
        Assert.Null(await s.WithDbAsync(db => db.Games.Select(g => g.SteamAppId).SingleAsync()));
    }

    [Fact]
    public async Task The_IGDB_facts_and_the_series_of_a_manual_game_are_found_through_its_IGDB_id()
    {
        await using var s = await ManualServer.StartAsync();
        await Create(s, new { platform = "xbox", igdbId = 1001 });

        var (status, facts) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/games/fake-quest/igdb"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1001, facts.GetProperty("igdbId").GetInt32());

        var (seriesStatus, series) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/games/fake-quest/series"));
        Assert.Equal(HttpStatusCode.OK, seriesStatus);
        var games = series.GetProperty("games").EnumerateArray().ToList();
        Assert.Equal([1001, 1002], games.Select(g => g.GetProperty("igdbId").GetInt32()));
        // The game of the page is marked, and it is in the vault (by its IGDB id, there is no AppID)
        Assert.True(games[0].GetProperty("isCurrent").GetBoolean());
        Assert.True(games[0].GetProperty("inVault").GetBoolean());
        Assert.Equal("fake-quest", games[0].GetProperty("slug").GetString());
        Assert.False(games[1].GetProperty("inVault").GetBoolean());
    }

    [Fact]
    public async Task A_manual_game_without_an_IGDB_id_has_no_facts_and_no_series()
    {
        await using var s = await ManualServer.StartAsync();
        await Create(s, new { title = "Hades", platform = "gog" });

        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.GetAsync("/api/games/hades/igdb")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.GetAsync("/api/games/hades/series")).StatusCode);
    }

    [Fact]
    public async Task Facts_older_than_a_week_are_refreshed_from_IGDB_and_a_Steam_game_or_one_without_an_id_never_is()
    {
        await using var s = await ManualServer.StartAsync();
        await Create(s, new { platform = "xbox", igdbId = 1001 });
        var stale = DateTimeOffset.UtcNow - TimeSpan.FromDays(8);
        await s.WithDbAsync(async db =>
        {
            await db.Games.ExecuteUpdateAsync(g => g.SetProperty(x => x.FactsUpdatedAt, stale).SetProperty(x => x.IgdbRating, 10).SetProperty(x => x.Developer, "Old"));
            db.Games.Add(new GameEntity { Id = Guid.NewGuid(), Slug = "steam-one", Title = "S", SteamAppId = 1, FactsUpdatedAt = stale });
            db.Games.Add(new GameEntity { Id = Guid.NewGuid(), Slug = "no-igdb", Title = "N", Platform = "gog", FactsUpdatedAt = stale });
            await db.SaveChangesAsync();
        });

        var due = await s.WithDbAsync(async db =>
            (await db.Games.AsNoTracking().ToListAsync()).Where(g => ManualFactsRefresh.IsDue(g, DateTimeOffset.UtcNow)).Select(g => g.Slug).ToList());
        Assert.Equal(["fake-quest"], due);

        var refresh = s.Services.GetRequiredService<ManualFactsRefresh>();
        var id = await s.WithDbAsync(db => db.Games.Where(g => g.Slug == "fake-quest").Select(g => g.Id).SingleAsync());
        Assert.True(await refresh.RefreshAsync(id, CancellationToken.None));

        var game = await s.WithDbAsync(db => db.Games.AsNoTracking().SingleAsync(g => g.Id == id));
        Assert.Equal(82, game.IgdbRating);
        Assert.Equal("Fake Studio", game.Developer);
        Assert.True(DateTimeOffset.UtcNow - game.FactsUpdatedAt < TimeSpan.FromMinutes(1));

        Assert.False(await refresh.RefreshAsync(await s.WithDbAsync(db => db.Games.Where(g => g.Slug == "no-igdb").Select(g => g.Id).SingleAsync()), CancellationToken.None));
        Assert.False(await refresh.RefreshAsync(await s.WithDbAsync(db => db.Games.Where(g => g.Slug == "steam-one").Select(g => g.Id).SingleAsync()), CancellationToken.None));
    }

    [Fact]
    public async Task The_game_list_starts_the_weekly_refresh_in_the_background()
    {
        await using var s = await ManualServer.StartAsync();
        var id = Guid.NewGuid();
        var stale = DateTimeOffset.UtcNow - TimeSpan.FromDays(10);
        await s.WithDbAsync(async db =>
        {
            db.Games.Add(new GameEntity { Id = id, Slug = "quest", Title = "Quest", Platform = "xbox", IgdbId = 1001, Developer = "Old", FactsUpdatedAt = stale, FactsSource = "igdb" });
            await db.SaveChangesAsync();
        });

        // The answer itself does not wait for IGDB
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/games")).StatusCode);

        var refreshed = false;
        for (var i = 0; i < 100 && !refreshed; i++)
        {
            await Task.Delay(50);
            refreshed = (await s.WithDbAsync(db => db.Games.AsNoTracking().SingleAsync(g => g.Id == id))).Developer == "Fake Studio";
        }
        Assert.True(refreshed, "the facts were not refreshed within five seconds");
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_old_facts_and_the_list_still_answers()
    {
        await using var s = await ManualServer.StartAsync();
        var stale = DateTimeOffset.UtcNow - TimeSpan.FromDays(9);
        // IGDB 1002 was never fetched, so a refresh really has to ask - and IGDB is down
        var id = Guid.NewGuid();
        await s.WithDbAsync(async db =>
        {
            db.Games.Add(new GameEntity { Id = id, Slug = "quest-2", Title = "Quest 2", Platform = "xbox", IgdbId = 1002, Developer = "Old", FactsUpdatedAt = stale, FactsSource = "igdb" });
            await db.SaveChangesAsync();
        });
        s.Igdb.Fail = true;

        var refresh = s.Services.GetRequiredService<ManualFactsRefresh>();
        Assert.False(await refresh.RefreshAsync(id, CancellationToken.None));

        var game = await s.WithDbAsync(db => db.Games.AsNoTracking().SingleAsync(g => g.Id == id));
        Assert.Equal("Old", game.Developer);
        Assert.Equal(stale, game.FactsUpdatedAt);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync("/api/games")).StatusCode);

        // IGDB is back: the next refresh writes the facts
        s.Igdb.Fail = false;
        Assert.True(await refresh.RefreshAsync(id, CancellationToken.None));
        Assert.True((await s.WithDbAsync(db => db.Games.AsNoTracking().SingleAsync(g => g.Id == id))).FactsUpdatedAt > stale);
    }

    // ---- cover --------------------------------------------------------------------------------------------------------------

    private static async Task<(HttpStatusCode Status, JsonElement Body)> UploadAsync(ManualServer s, string slug, byte[] data, string? contentType) =>
        await ManualServer.SendJsonAsync(s.SendAsync(HttpMethod.Put, $"/api/games/{slug}/cover", data, contentType));

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    public async Task An_uploaded_cover_is_stored_in_the_database_and_served_with_safe_headers(string type)
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await Create(s, new { title = "Hades", platform = "gog" });
        var data = type switch { "image/png" => Png, "image/jpeg" => Jpeg, "image/gif" => Gif, _ => Webp };

        var (status, body) = await UploadAsync(s, "hades", data, type);

        Assert.Equal(HttpStatusCode.OK, status);
        var url = body.GetProperty("coverUrl").GetString()!;
        Assert.Matches(@"^/api/covers/[0-9a-f]{32}\?v=\d+$", url);

        using var served = await s.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(type, served.Content.Headers.ContentType!.MediaType);
        Assert.Equal(data, await served.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", served.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("public, max-age=31536000, immutable", served.Headers.CacheControl!.ToString());
        Assert.NotNull(served.Headers.ETag);
        // Not an attachment: an image tag shows it
        Assert.Null(served.Content.Headers.ContentDisposition);

        // The bytes live in the database (a backup carries them), not in a file of the data folder
        var row = await s.WithDbAsync(db => db.GameCovers.AsNoTracking().SingleAsync());
        Assert.Equal(data, row.Data);
        Assert.Equal(type, row.ContentType);
        Assert.Equal(64, row.Sha256.Length);
    }

    [Fact]
    public async Task A_new_upload_replaces_the_old_one_and_changes_the_address()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await Create(s, new { title = "Hades", platform = "gog" });
        var first = (await UploadAsync(s, "hades", Png, "image/png")).Body.GetProperty("coverUrl").GetString();
        await Task.Delay(5);

        var second = (await UploadAsync(s, "hades", Jpeg, "image/jpeg")).Body.GetProperty("coverUrl").GetString();

        Assert.NotEqual(first, second);
        Assert.Equal(1, await s.WithDbAsync(db => db.GameCovers.CountAsync()));
        using var served = await s.Client.GetAsync(second);
        Assert.Equal("image/jpeg", served.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Svg_html_and_files_that_are_not_what_they_claim_are_refused()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await Create(s, new { title = "Hades", platform = "gog" });
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();

        // A declared type that is not on the list (SVG included, and no type at all)
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await UploadAsync(s, "hades", svg, "image/svg+xml")).Status);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await UploadAsync(s, "hades", Png, "text/html")).Status);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await UploadAsync(s, "hades", Png, null)).Status);
        // The first bytes decide: an SVG, a script or a PNG under the wrong type never gets through
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(s, "hades", svg, "image/png")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(s, "hades", Png, "image/jpeg")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(s, "hades", "<html>"u8.ToArray(), "image/gif")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(s, "hades", [], "image/png")).Status);

        Assert.Equal(0, await s.WithDbAsync(db => db.GameCovers.CountAsync()));
        Assert.Equal(string.Empty, await s.WithDbAsync(db => db.Games.Select(g => g.CoverUrl).SingleAsync()));
    }

    [Fact]
    public async Task A_cover_over_five_megabytes_is_refused_and_five_megabytes_are_fine()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await Create(s, new { title = "Hades", platform = "gog" });

        var tooBig = new byte[CoverStore.MaxBytes + 1];
        Png.CopyTo(tooBig, 0);
        var limit = new byte[CoverStore.MaxBytes];
        Png.CopyTo(limit, 0);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await UploadAsync(s, "hades", tooBig, "image/png")).Status);
        Assert.Equal(0, await s.WithDbAsync(db => db.GameCovers.CountAsync()));
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(s, "hades", limit, "image/png")).Status);
    }

    [Fact]
    public async Task A_cover_can_only_be_set_for_a_game_added_by_hand_and_an_unknown_game_is_a_404()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await s.WithDbAsync(async db =>
        {
            db.Games.Add(new GameEntity { Id = Guid.NewGuid(), Slug = "steam-game", Title = "S", SteamAppId = 10 });
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(s, "steam-game", Png, "image/png")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(s, "nope", Png, "image/png")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/covers/{Guid.NewGuid():N}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync("/api/covers/not-a-guid")).StatusCode);
    }

    [Fact]
    public async Task Removing_the_cover_gives_the_IGDB_cover_back_and_a_game_without_IGDB_none()
    {
        await using var s = await ManualServer.StartAsync();
        await Create(s, new { platform = "xbox", igdbId = 1001 });
        await Create(s, new { title = "Hades", platform = "gog" });
        await UploadAsync(s, "fake-quest", Png, "image/png");
        var hadesUrl = (await UploadAsync(s, "hades", Png, "image/png")).Body.GetProperty("coverUrl").GetString()!;

        var (status, igdb) = await ManualServer.SendJsonAsync(s.SendAsync(HttpMethod.Delete, "/api/games/fake-quest/cover"));
        var (_, plain) = await ManualServer.SendJsonAsync(s.SendAsync(HttpMethod.Delete, "/api/games/hades/cover"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.EndsWith("/t_cover_big_2x/co1abc.jpg", igdb.GetProperty("coverUrl").GetString());
        Assert.Equal(string.Empty, plain.GetProperty("coverUrl").GetString());
        Assert.Equal(0, await s.WithDbAsync(db => db.GameCovers.CountAsync()));
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync(hadesUrl)).StatusCode);
        // Deleting again is quiet
        Assert.Equal(HttpStatusCode.OK, (await s.SendAsync(HttpMethod.Delete, "/api/games/hades/cover")).StatusCode);
    }

    [Fact]
    public void The_cover_check_knows_every_type_by_its_first_bytes()
    {
        Assert.Equal("image/png", CoverStore.Sniff(Png));
        Assert.Equal("image/jpeg", CoverStore.Sniff(Jpeg));
        Assert.Equal("image/gif", CoverStore.Sniff("GIF87a"u8));
        Assert.Equal("image/gif", CoverStore.Sniff(Gif));
        Assert.Equal("image/webp", CoverStore.Sniff(Webp));
        Assert.Null(CoverStore.Sniff("RIFF\0\0\0\0WAVE"u8));
        Assert.Null(CoverStore.Sniff("<svg"u8));
        Assert.Null(CoverStore.Sniff([0x89, 0x50]));
        Assert.Equal("image/jpeg", CoverStore.NormalizeContentType("IMAGE/JPG; charset=binary"));
        Assert.Null(CoverStore.NormalizeContentType(null));
    }

    // ---- removing and the listing ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Removing_a_manual_game_removes_everything_that_belongs_to_it()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        var id = await s.SeedAsync("ps-game", "playstation", "ps5", ("a", "bronze", null), ("b", "platinum", null));
        var other = await s.SeedAsync("other", "xbox", null, ("x", null, null));
        await s.WithDbAsync(async db =>
        {
            db.GameCovers.Add(new GameCoverEntity { GameId = id, ContentType = "image/png", Data = Png, Sha256 = new string('0', 64) });
            db.GameTrophyTranslations.Add(new GameTrophyTranslationEntity { GameId = id, Language = "de", ItemsJson = "[]", SourceHash = "x" });
            db.ProgressChecks.Add(new ProgressCheckEntity { Id = Guid.NewGuid(), GameId = id, ItemKey = "k", CheckedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        await s.PostAsync("/api/live/pins", new { gameId = id, apiName = "a" });
        await s.PostAsync("/api/live/note", new { appId = 0, gameId = id, apiName = "a", note = "hint" });
        await s.PostAsync("/api/live/pins", new { gameId = other, apiName = "x" });
        await s.PostAsync("/api/live/note", new { appId = 0, gameId = other, apiName = "x", note = "keep" });

        var response = await s.SendAsync(HttpMethod.Delete, $"/api/games/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await s.WithDbAsync(async db =>
        {
            Assert.Equal(["other"], await db.Games.Select(g => g.Slug).ToListAsync());
            Assert.Equal(1, await db.GameTrophies.CountAsync());
            Assert.Equal(0, await db.GameCovers.CountAsync());
            Assert.Equal(0, await db.GameTrophyTranslations.CountAsync());
            Assert.Equal(0, await db.ProgressChecks.CountAsync());
            // Pins and notes of the removed game go with it, those of the other game stay
            var keys = await db.AppSettings.Select(a => a.Key).ToListAsync();
            Assert.DoesNotContain(keys, k => k.Contains(id.ToString("N")));
            Assert.Contains(keys, k => k.Contains(other.ToString("N")) && k.StartsWith("pins.g."));
            Assert.Contains(keys, k => k.Contains(other.ToString("N")) && k.StartsWith("note.g."));
        });
    }

    [Fact]
    public async Task All_games_lists_games_added_by_hand_and_works_without_a_Steam_key()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);

        // Nothing at all: as before (no library, 204; the library list is an error about the missing key)
        Assert.Equal(HttpStatusCode.NoContent, (await s.Client.GetAsync("/api/steam/library/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.GetAsync("/api/steam/library")).StatusCode);

        await s.SeedAsync("ps-game", "playstation", "ps5", ("a", "bronze", null), ("c", "bronze", null), ("b", "platinum", null));
        await s.SeedAsync("xb", "xbox", null, ("x", null, null));
        await s.WithDbAsync(db => db.Games.Where(g => g.Slug == "xb").ExecuteUpdateAsync(g => g.SetProperty(x => x.IsFavorite, false)));
        await s.PostAsync("/api/games/ps-game/trophies/a/unlock", new { unlocked = true });

        var (status, summary) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/steam/library/summary"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, summary.GetProperty("manualGameCount").GetInt32());
        Assert.Equal(4, summary.GetProperty("manualTrophiesTotal").GetInt32());
        Assert.Equal(1, summary.GetProperty("manualTrophiesUnlocked").GetInt32());
        Assert.Equal(0, summary.GetProperty("manualPerfectGames").GetInt32());
        Assert.Equal(0, summary.GetProperty("libraryGameCount").GetInt32());

        var (libStatus, library) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/steam/library"));
        Assert.Equal(HttpStatusCode.OK, libStatus);
        Assert.Equal(0, library.GetProperty("games").GetArrayLength());

        // The games themselves (also the one that is not a favourite: the star only hides it from the overview) come from GET /api/games
        var (_, games) = await ManualServer.SendJsonAsync(s.Client.GetAsync("/api/games"));
        var list = games.EnumerateArray().ToList();
        Assert.Equal(["ps-game", "xb"], list.Select(g => g.GetProperty("slug").GetString()).Order());
        Assert.Equal("playstation", list.Single(g => g.GetProperty("slug").GetString() == "ps-game").GetProperty("platform").GetString());
        Assert.False(list.Single(g => g.GetProperty("slug").GetString() == "xb").GetProperty("isFavorite").GetBoolean());
    }

    [Fact]
    public async Task The_star_of_a_manual_game_works_through_the_game_favourite_endpoint()
    {
        await using var s = await ManualServer.StartAsync(credentials: false);
        await Create(s, new { title = "Hades", platform = "gog" });

        var (_, off) = await ManualServer.SendJsonAsync(s.PutAsync("/api/games/hades/favorite", new { isFavorite = false }));
        var (_, on) = await ManualServer.SendJsonAsync(s.PutAsync("/api/games/hades/favorite", new { isFavorite = true }));

        Assert.False(off.GetProperty("isFavorite").GetBoolean());
        Assert.True(on.GetProperty("isFavorite").GetBoolean());
        Assert.Equal("gog", on.GetProperty("platform").GetString());
    }

    [Fact]
    public void A_game_with_a_researched_trophy_list_keeps_its_guide_language()
    {
        var withList = new GameEntity { Slug = "a", Title = "A", Platform = "xbox", ContentLanguage = "de", TrophyListResearchedAt = DateTimeOffset.UtcNow };
        var without = new GameEntity { Slug = "b", Title = "B", Platform = "xbox", ContentLanguage = "de" };
        using var _ = Companion.Api.Localization.Lang.Use("en");

        Assert.False(ApiEndpoints.AdoptRequestLanguageIfEmpty(withList));
        Assert.Equal("de", withList.ContentLanguage);
        Assert.True(ApiEndpoints.AdoptRequestLanguageIfEmpty(without));
        Assert.Equal("en", without.ContentLanguage);
    }
}
