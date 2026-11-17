using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Companion.Api.Configuration;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Companion.Api.Tests;

/// <summary>IGDB's three ratings (critics, users, both) and how they reach the card; IGDB is always a fake handler.</summary>
public sealed class IgdbRatingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pv-igdb-rating-" + Guid.NewGuid().ToString("N"));

    public IgdbRatingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class FakeIgdb(string gameJson) : HttpMessageHandler, IHttpClientFactory
    {
        public int GameQueries { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.StartsWith("https://id.twitch.tv/oauth2/token", StringComparison.Ordinal))
            {
                return Json("{\"access_token\":\"fake-token\",\"expires_in\":3600}");
            }
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (url.EndsWith("/external_game_sources", StringComparison.Ordinal)) return Json("[{\"id\":1,\"name\":\"Steam\"}]");
            if (url.EndsWith("/external_games", StringComparison.Ordinal)) return Json("[{\"game\":{\"id\":77}}]");
            if (url.EndsWith("/games", StringComparison.Ordinal))
            {
                GameQueries++;
                LastGamesBody = body;
                return Json(gameJson);
            }
            return Json("[]");
        }

        public string LastGamesBody { get; private set; } = "";
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private IgdbService Service(FakeIgdb fake)
    {
        var options = Options.Create(new CompanionOptions { DataDir = _dir });
        var secrets = new SecretStore(DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_dir, "keys"))), options);
        secrets.SetSecret(SettingsStore.SecretTwitchClientId, "client-id-12345");
        secrets.SetSecret(SettingsStore.SecretTwitchClientSecret, "client-secret-12345");
        var auth = new TwitchAuthService(fake, secrets, NullLogger<TwitchAuthService>.Instance);
        return new IgdbService(auth, options, NullLogger<IgdbService>.Instance);
    }

    private const string WithAllRatings =
        "[{\"id\":77,\"name\":\"Fake\",\"aggregated_rating\":82.4,\"aggregated_rating_count\":9,\"rating\":71.6,\"rating_count\":120,\"total_rating\":76.5,\"total_rating_count\":129}]";
    private const string CriticsOnly =
        "[{\"id\":77,\"name\":\"Fake\",\"aggregated_rating\":82.4,\"aggregated_rating_count\":9,\"total_rating\":82.4,\"total_rating_count\":9}]";

    [Fact]
    public async Task Parses_users_rating_next_to_critics_and_total()
    {
        var fake = new FakeIgdb(WithAllRatings);
        var facts = await Service(fake).GetBySteamAppIdAsync(123, CancellationToken.None);

        Assert.NotNull(facts);
        Assert.Equal(82.4, facts.AggregatedRating);
        Assert.Equal(71.6, facts.Rating);
        Assert.Equal(120, facts.RatingCount);
        Assert.Equal(76.5, facts.TotalRating);
        Assert.Contains("rating,rating_count", fake.LastGamesBody);
    }

    [Fact]
    public async Task Answer_without_rating_leaves_the_users_rating_null()
    {
        var facts = await Service(new FakeIgdb(CriticsOnly)).GetBySteamAppIdAsync(123, CancellationToken.None);

        Assert.NotNull(facts);
        Assert.Equal(82.4, facts.AggregatedRating);
        Assert.Null(facts.Rating);
        Assert.Null(facts.RatingCount);
    }

    [Fact]
    public async Task Old_cache_entry_without_the_users_rating_is_refetched()
    {
        var first = new FakeIgdb(CriticsOnly);
        await Service(first).GetBySteamAppIdAsync(123, CancellationToken.None);
        Assert.Equal(1, first.GameQueries);

        // Turn the persisted entry into the shape an older build wrote: no schema, no rating fields
        var file = Path.Combine(_dir, "cache", "igdb.json");
        var text = File.ReadAllText(file);
        text = Regex.Replace(text, "\"schema\":\\d+,?", "");
        text = Regex.Replace(text, ",?\"ratingCount\":[^,}]+", "");
        text = Regex.Replace(text, ",?\"rating\":[^,}]+", "");
        text = text.Replace(",}", "}");
        File.WriteAllText(file, text);

        var second = new FakeIgdb(WithAllRatings);
        var facts = await Service(second).GetBySteamAppIdAsync(123, CancellationToken.None);

        Assert.Equal(1, second.GameQueries);
        Assert.Equal(71.6, facts!.Rating);
    }

    [Theory]
    [InlineData("\"71.6\"")]          // wrong type
    [InlineData("{\"value\":71}")]    // wrong type
    [InlineData("1e400")]             // beyond double
    [InlineData("1e300")]             // a double, far beyond 0-100
    [InlineData("250")]
    [InlineData("-3")]
    [InlineData("null")]
    public async Task An_odd_users_rating_from_IGDB_is_left_out_and_never_reaches_the_card(string rating)
    {
        var json = "[{\"id\":77,\"name\":\"Fake\",\"aggregated_rating\":" + rating + ",\"rating\":" + rating + ",\"rating_count\":" + rating + ",\"total_rating\":" + rating + "}]";
        var facts = await Service(new FakeIgdb(json)).GetBySteamAppIdAsync(123, CancellationToken.None);

        Assert.NotNull(facts);
        Assert.Null(facts.Rating);
        Assert.Null(facts.AggregatedRating);
        Assert.Null(facts.TotalRating);
        var game = new GameEntity { IgdbRating = 80, IgdbUserRating = 70 };
        SteamLiveTrackerService.ApplyIgdbRatings(game, facts);
        Assert.Null(game.IgdbRating);
        Assert.Null(game.IgdbUserRating);
    }

    [Fact]
    public void A_rating_beyond_0_to_100_from_any_source_is_not_put_on_the_card()
    {
        var game = new GameEntity();
        SteamLiveTrackerService.ApplyIgdbRatings(game, Facts(critics: 1e300, users: double.NaN, total: null));

        Assert.Null(game.IgdbRating);
        Assert.Null(game.IgdbUserRating);
    }

    [Fact]
    public async Task Current_cache_entry_is_not_refetched()
    {
        await Service(new FakeIgdb(WithAllRatings)).GetBySteamAppIdAsync(123, CancellationToken.None);

        var second = new FakeIgdb(WithAllRatings);
        var facts = await Service(second).GetBySteamAppIdAsync(123, CancellationToken.None);

        Assert.Equal(0, second.GameQueries);
        Assert.Equal(120, facts!.RatingCount);
    }

    private static IgdbGameDto Facts(double? critics, double? users, double? total) => new(
        77, "Fake", null, null, null, critics, null, total, null, users, null,
        [], [], [], [], [], [], [], null, [], [], [], null, DateTimeOffset.UtcNow);

    [Fact]
    public void Card_gets_critics_and_users_separately_rounded()
    {
        var game = new GameEntity();
        SteamLiveTrackerService.ApplyIgdbRatings(game, Facts(critics: 82.4, users: 71.5, total: 76.5));

        Assert.Equal(82, game.IgdbRating);
        Assert.Equal(72, game.IgdbUserRating);
    }

    [Fact]
    public void Card_does_not_fall_back_to_the_total_and_overwrites_old_values()
    {
        // An old row held total_rating in IgdbRating; the next refresh must replace it, and a missing value clears it
        var game = new GameEntity { IgdbRating = 76, IgdbUserRating = 50 };
        SteamLiveTrackerService.ApplyIgdbRatings(game, Facts(critics: null, users: null, total: 76.5));

        Assert.Null(game.IgdbRating);
        Assert.Null(game.IgdbUserRating);
    }

    [Fact]
    public void Summary_dto_carries_both_ratings_with_the_json_names_the_frontend_reads()
    {
        var dto = ApiEndpoints.MapToSummaryDto(new GameEntity { Slug = "x", Title = "X", IgdbRating = 82, IgdbUserRating = 72 });
        Assert.Equal(82, dto.IgdbRating);
        Assert.Equal(72, dto.IgdbUserRating);

        var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Contains("\"igdbRating\":82", json);
        Assert.Contains("\"igdbUserRating\":72", json);
    }
}
