using Companion.Api.Configuration;
using Companion.Api.Endpoints;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Companion.Api.Tests;

/// <summary>The IGDB search of the add dialog, the lookup by IGDB id and the platform mapping. IGDB is always a fake handler.</summary>
public sealed class IgdbSearchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pv-igdb-search-" + Guid.NewGuid().ToString("N"));

    public IgdbSearchTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private IgdbService Service(FakeIgdb fake, bool credentials = true)
    {
        var options = Options.Create(new CompanionOptions { DataDir = _dir });
        var secrets = new SecretStore(DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_dir, "keys"))), options);
        if (credentials)
        {
            secrets.SetSecret(SettingsStore.SecretTwitchClientId, "client-id-12345");
            secrets.SetSecret(SettingsStore.SecretTwitchClientSecret, "client-secret-12345");
        }
        var auth = new TwitchAuthService(fake, secrets, NullLogger<TwitchAuthService>.Instance);
        return new IgdbService(auth, options, NullLogger<IgdbService>.Instance);
    }

    // ---- reading the search box -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("1001", IgdbSearchKind.Id, "1001")]
    [InlineData("  0042 ", IgdbSearchKind.Id, "42")]
    [InlineData("https://www.igdb.com/games/the-witcher-3-wild-hunt", IgdbSearchKind.Slug, "the-witcher-3-wild-hunt")]
    [InlineData("igdb.com/games/Hades/", IgdbSearchKind.Slug, "hades")]
    [InlineData("http://igdb.com/games/hades?utm=1", IgdbSearchKind.Slug, "hades")]
    [InlineData("Elden Ring", IgdbSearchKind.Text, "Elden Ring")]
    [InlineData("  Elden   Ring  ", IgdbSearchKind.Text, "Elden Ring")]
    [InlineData("12345678901234", IgdbSearchKind.Text, "12345678901234")]
    // Digits of other scripts (Arabic-Indic, full-width) are no IGDB id: \d would let them into "where id = ..." and IGDB refuses that query
    [InlineData("٤٢", IgdbSearchKind.Text, "٤٢")]
    [InlineData("４２", IgdbSearchKind.Text, "４２")]
    public void The_search_box_is_an_id_an_address_or_a_name(string input, IgdbSearchKind kind, string value)
    {
        var parsed = IgdbService.ParseSearch(input);
        Assert.Equal(new IgdbSearchQuery(kind, value), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("\"\\;")]
    public void Nothing_usable_is_refused(string? input) => Assert.Null(IgdbService.ParseSearch(input));

    [Fact]
    public void More_than_120_characters_are_refused_and_120_are_fine()
    {
        Assert.Null(IgdbService.ParseSearch(new string('a', 121)));
        Assert.NotNull(IgdbService.ParseSearch(new string('a', 120)));
    }

    [Fact]
    public void Characters_that_could_break_out_of_the_query_are_removed()
    {
        var parsed = IgdbService.ParseSearch("Evil\"; fields *; delete\\ \"game\"\r\n;")!;
        Assert.Equal(IgdbSearchKind.Text, parsed.Kind);
        Assert.DoesNotContain('"', parsed.Value);
        Assert.DoesNotContain('\\', parsed.Value);
        Assert.DoesNotContain(';', parsed.Value);
        Assert.DoesNotContain('\n', parsed.Value);
        Assert.Equal("Evil fields * delete game", parsed.Value);
    }

    // ---- searching ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_name_search_returns_main_games_and_remakes_but_no_dlc()
    {
        var fake = new FakeIgdb();
        var result = await Service(fake).SearchAsync(IgdbService.ParseSearch("Fake Quest")!, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal([1001, 1002], result.Results.Select(h => h.IgdbId));
        var first = result.Results[0];
        Assert.Equal("Fake Quest", first.Name);
        Assert.Equal(2023, first.ReleaseYear);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), first.FirstReleaseAt);
        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_cover_big/co1abc.jpg", first.CoverUrl);
        Assert.Empty(first.Existing);
        // A game without a cover has none (not a made-up address)
        Assert.Null(result.Results[1].CoverUrl);

        var query = Assert.Single(fake.Requests, r => r.Endpoint == "games").Body;
        Assert.StartsWith("search \"Fake Quest\";", query);
        // Editions of a game are left out by IGDB already
        Assert.Contains("where version_parent = null;", query);
        Assert.Contains("limit 20;", query);
    }

    [Fact]
    public async Task An_injection_attempt_reaches_IGDB_as_plain_text()
    {
        var fake = new FakeIgdb();
        await Service(fake).SearchAsync(IgdbService.ParseSearch("x\"; where id = 1; \"")!, CancellationToken.None);

        var query = Assert.Single(fake.Requests, r => r.Endpoint == "games").Body;
        // One search string and nothing else injected: exactly the two quotes of the search term, no second "where id"
        Assert.Equal(2, query.Count(c => c == '"'));
        Assert.StartsWith("search \"x where id = 1\";", query);
        Assert.Equal(1, query.Split("where id = 1").Length - 1);
    }

    [Fact]
    public async Task An_id_or_an_address_finds_exactly_that_game_without_a_type_filter()
    {
        var fake = new FakeIgdb();
        var service = Service(fake);

        var byId = await service.SearchAsync(IgdbService.ParseSearch("1002")!, CancellationToken.None);
        var bySlug = await service.SearchAsync(IgdbService.ParseSearch("https://www.igdb.com/games/fake-quest")!, CancellationToken.None);
        var unknown = await service.SearchAsync(IgdbService.ParseSearch("424242")!, CancellationToken.None);

        // Fake Quest 2 is a remake; an id names it, whatever its type
        Assert.Equal([1002], byId!.Results.Select(h => h.IgdbId));
        Assert.Equal([1001], bySlug!.Results.Select(h => h.IgdbId));
        Assert.Empty(unknown!.Results);
        Assert.Contains(fake.Requests, r => r.Body.Contains("where id = 1002; limit 1;"));
        Assert.Contains(fake.Requests, r => r.Body.Contains("where slug = \"fake-quest\"; limit 1;"));
        // The game types table is only needed for a name search
        Assert.Equal(0, fake.Count("game_types"));
    }

    [Fact]
    public async Task The_platforms_of_a_hit_carry_our_mapping()
    {
        var result = await Service(new FakeIgdb()).SearchAsync(IgdbService.ParseSearch("Fake Quest")!, CancellationToken.None);

        var platforms = result!.Results[0].Platforms;
        Assert.Equal(["PlayStation 5", "PC (Microsoft Windows)"], platforms.Select(p => p.Name));
        Assert.Equal(new PlatformChoiceDto("playstation", "ps5"), platforms[0].Maps);
        // IGDB only says "PC": no PC shop is marked
        Assert.Null(platforms[1].Maps);
        Assert.Equal(new PlatformChoiceDto("xbox", null), result.Results[1].Platforms.Single().Maps);
    }

    [Theory]
    [InlineData("PlayStation 5", "PS5", "ps5", "playstation", "ps5")]
    [InlineData("PlayStation 4", "PS4", "ps4--1", "playstation", "ps4")]
    [InlineData("PlayStation 4", null, null, "playstation", "ps4")]
    [InlineData("Xbox One", "XONE", "xboxone", "xbox", null)]
    [InlineData("Xbox Series X|S", "Series X|S", "series-x-s", "xbox", null)]
    [InlineData("Xbox 360", "X360", "xbox360", "xbox", null)]
    [InlineData("Xbox", "XBOX", "xbox", "xbox", null)]
    [InlineData("Nintendo Switch", "Switch", "nintendo-switch", "nintendo", null)]
    [InlineData("Nintendo Switch 2", "Switch 2", "nintendo-switch-2", "nintendo", null)]
    [InlineData("PC (Microsoft Windows)", "PC", "win", null, null)]
    [InlineData("Linux", "Linux", "linux", null, null)]
    [InlineData("PlayStation 3", "PS3", "ps3", null, null)]
    [InlineData("PlayStation VR2", "PSVR2", "psvr2", null, null)]
    [InlineData("PlayStation Vita", "Vita", "psvita", null, null)]
    [InlineData("Wii U", "WiiU", "wiiu", null, null)]
    [InlineData("Android", "Android", "android", null, null)]
    public void IGDB_platforms_map_by_name_abbreviation_or_slug_never_by_number(string name, string? abbreviation, string? slug, string? platform, string? variant)
    {
        var mapped = IgdbPlatformMap.Map(name, abbreviation, slug);
        Assert.Equal(platform is null ? null : new PlatformChoiceDto(platform, variant), mapped);
    }

    [Fact]
    public async Task A_failed_search_is_never_cached_and_a_good_one_is_cached_for_ten_minutes()
    {
        var fake = new FakeIgdb { Fail = true };
        var service = Service(fake);
        var query = IgdbService.ParseSearch("Fake Quest")!;

        var error = await Assert.ThrowsAsync<TwitchApiException>(() => service.SearchAsync(query, CancellationToken.None));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        fake.Fail = false;

        var ok = await service.SearchAsync(query, CancellationToken.None);
        Assert.Equal(2, ok!.Results.Count);
        var gamesCalls = fake.Count("games");

        await service.SearchAsync(query, CancellationToken.None);
        await service.SearchAsync(IgdbService.ParseSearch("fake quest")!, CancellationToken.None);
        // Same search, same case-folded text: answered from memory
        Assert.Equal(gamesCalls, fake.Count("games"));
    }

    [Fact]
    public async Task Without_credentials_nothing_is_asked()
    {
        var fake = new FakeIgdb();
        var service = Service(fake, credentials: false);

        Assert.Null(await service.SearchAsync(IgdbService.ParseSearch("Fake Quest")!, CancellationToken.None));
        Assert.Null(await service.GetByIgdbIdAsync(1001, CancellationToken.None));
        Assert.Empty(fake.Requests);
    }

    // ---- facts by IGDB id -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_facts_of_a_game_by_its_IGDB_id_carry_companies_release_platforms_cover_and_time_to_beat()
    {
        var fake = new FakeIgdb();
        var facts = await Service(fake).GetByIgdbIdAsync(1001, CancellationToken.None);

        Assert.NotNull(facts);
        Assert.Equal("Fake Quest", facts.Name);
        Assert.Equal(1001, facts.IgdbId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), facts.FirstReleaseAt);
        // Distinct names, in order: a company that is developer and publisher is listed under both
        Assert.Equal(["Fake Studio"], facts.Developers);
        Assert.Equal(["Fake Publishing", "Fake Studio"], facts.Publishers);
        Assert.Equal(["Role-playing (RPG)", "Adventure"], facts.Genres);
        Assert.Equal(82.4, facts.AggregatedRating);
        Assert.Equal(71.6, facts.Rating);
        Assert.Equal(40.0, facts.TimeToBeat!.CompletelyHours);
        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_cover_big_2x/co1abc.jpg", facts.CoverUrl);
        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_1080p/ar1xyz.jpg", facts.Artworks.Single().FullUrl);
        Assert.Equal(new PlatformChoiceDto("playstation", "ps5"), facts.Platforms![0].Maps);
        Assert.Contains(fake.Requests, r => r.Endpoint == "involved_companies" && r.Body.Contains("where game = 1001"));
    }

    [Fact]
    public async Task The_banner_of_a_game_is_its_first_artwork_else_its_first_screenshot()
    {
        var service = Service(new FakeIgdb());
        var withArtwork = (await service.GetByIgdbIdAsync(1001, CancellationToken.None))!;
        var screenshotOnly = (await service.GetByIgdbIdAsync(1002, CancellationToken.None))!;

        Assert.EndsWith("/t_1080p/ar1xyz.jpg", ManualGameFacts.HeroUrl(withArtwork));
        Assert.EndsWith("/t_1080p/sc3ghi.jpg", ManualGameFacts.HeroUrl(screenshotOnly));
        Assert.Equal(string.Empty, ManualGameFacts.HeroUrl(withArtwork with { Artworks = [], Screenshots = [] }));
    }

    [Fact]
    public async Task Facts_by_id_are_cached_persisted_and_an_unknown_game_is_remembered_for_a_day()
    {
        var first = new FakeIgdb();
        await Service(first).GetByIgdbIdAsync(1001, CancellationToken.None);
        var calls = first.Requests.Count;
        Assert.True(calls > 0);

        // A new service on the same data folder (a restart) reads the persisted entry
        var second = new FakeIgdb();
        var again = await Service(second).GetByIgdbIdAsync(1001, CancellationToken.None);
        Assert.Equal("Fake Quest", again!.Name);
        Assert.Empty(second.Requests);

        var third = new FakeIgdb();
        var service = Service(third);
        Assert.Null(await service.GetByIgdbIdAsync(777777, CancellationToken.None));
        var asked = third.Requests.Count;
        Assert.Null(await service.GetByIgdbIdAsync(777777, CancellationToken.None));
        Assert.Equal(asked, third.Requests.Count);
    }

    [Fact]
    public async Task A_failed_lookup_by_id_is_never_cached()
    {
        var fake = new FakeIgdb { Fail = true };
        var service = Service(fake);

        await Assert.ThrowsAsync<TwitchApiException>(() => service.GetByIgdbIdAsync(1001, CancellationToken.None));
        fake.Fail = false;
        Assert.Equal("Fake Quest", (await service.GetByIgdbIdAsync(1001, CancellationToken.None))!.Name);
    }

    [Fact]
    public async Task The_series_of_a_game_is_found_by_its_IGDB_id_without_a_Steam_app()
    {
        var series = await Service(new FakeIgdb()).GetSeriesByIgdbIdAsync(1001, CancellationToken.None);

        Assert.NotNull(series);
        Assert.Equal("Fake Series", series.Name);
        Assert.Equal([1001, 1002], series.Games.Select(g => g.IgdbId));
    }

    [Fact]
    public async Task The_Steam_path_asks_for_no_companies()
    {
        var fake = new FakeIgdb();
        var facts = await Service(fake).GetBySteamAppIdAsync(123, CancellationToken.None);

        // The fake has no Steam source or mapping (external_games answers empty), so IGDB does not know the app: nothing else is asked
        Assert.Null(facts);
        Assert.Equal(0, fake.Count("involved_companies"));
    }
}
