using System.Net;
using System.Text;
using Companion.Api.Configuration;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Companion.Api.Tests;

/// <summary>
/// The live-stream count on the Twitch buttons of the cards: category lookup, paging of "Get Streams" (no total in the answer, so up to
/// three pages of 100), the five-minute cache, failures that are never cached, the batch with its time budget. Twitch is never called:
/// every request goes to a fake handler.
/// </summary>
public sealed class TwitchLiveCountTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pv-twitch-" + Guid.NewGuid().ToString("N"));

    public TwitchLiveCountTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ---- fake Twitch ----------------------------------------------------------------------------------------------------

    private sealed class FakeTwitch(Func<string, HttpResponseMessage?> streams) : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Calls { get; } = [];
        public int TokenRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            lock (Calls) Calls.Add(url);
            if (url.StartsWith("https://id.twitch.tv/oauth2/token", StringComparison.Ordinal))
            {
                TokenRequests++;
                return Task.FromResult(Json("{\"access_token\":\"fake-token\",\"expires_in\":3600}"));
            }
            if (url.Contains("/helix/games?", StringComparison.Ordinal))
            {
                // Every title is a category; its id is the number in the title ("Game 7" -> 7), "Nothing" is no category
                var name = Uri.UnescapeDataString(url[(url.IndexOf("name=", StringComparison.Ordinal) + 5)..]).Split('&')[0];
                var digits = new string(name.Where(char.IsAsciiDigit).ToArray());
                return Task.FromResult(Json(digits.Length == 0 ? "{\"data\":[]}" : $"{{\"data\":[{{\"id\":\"{digits}\",\"name\":\"{name}\"}}]}}"));
            }
            if (url.Contains("/helix/streams?", StringComparison.Ordinal))
            {
                return Task.FromResult(streams(url) ?? Json("{\"data\":[]}"));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        public IEnumerable<string> StreamCalls => Calls.Where(c => c.Contains("/helix/streams?", StringComparison.Ordinal));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>A "Get Streams" page with <paramref name="count"/> streams and, when there is more, the cursor of the next one.</summary>
    private static HttpResponseMessage Page(int count, string? cursor)
    {
        var data = string.Join(",", Enumerable.Range(0, count).Select(i => $"{{\"user_login\":\"user{i}\",\"user_name\":\"User{i}\",\"title\":\"t\",\"viewer_count\":{i},\"language\":\"de\"}}"));
        return Json($"{{\"data\":[{data}],\"pagination\":{(cursor is null ? "{}" : $"{{\"cursor\":\"{cursor}\"}}")}}}");
    }

    private (TwitchLiveCountService Batch, TwitchStreamsService Streams, FakeTwitch Twitch, SecretStore Secrets) Service(FakeTwitch twitch, bool credentials = true)
    {
        var options = Options.Create(new CompanionOptions { DataDir = _dir });
        var secrets = new SecretStore(DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_dir, "keys"))), options);
        if (credentials)
        {
            secrets.SetSecret(SettingsStore.SecretTwitchClientId, "client-id-12345");
            secrets.SetSecret(SettingsStore.SecretTwitchClientSecret, "client-secret-12345");
        }
        var auth = new TwitchAuthService(twitch, secrets, NullLogger<TwitchAuthService>.Instance);
        var igdb = new IgdbService(auth, options, NullLogger<IgdbService>.Instance);
        var streams = new TwitchStreamsService(auth, igdb, new MemoryCache(new MemoryCacheOptions()), NullLogger<TwitchStreamsService>.Instance);
        return (new TwitchLiveCountService(streams, NullLogger<TwitchLiveCountService>.Instance), streams, twitch, secrets);
    }

    // ---- the count of one game --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Counts_the_streams_of_the_category_exactly_below_the_limit()
    {
        var (_, streams, twitch, _) = Service(new FakeTwitch(url => Page(42, null)));

        var count = await streams.CountLiveAsync(null, "Game 7", null, CancellationToken.None);

        Assert.Equal(new TwitchLiveCount(42, false), count);
        // One category lookup and one page; the call asks for 100 per page, live streams of that category only
        var call = Assert.Single(twitch.StreamCalls);
        Assert.Contains("game_id=7", call);
        Assert.Contains("first=100", call);
        Assert.Contains("type=live", call);
        Assert.DoesNotContain("language=", call);
    }

    [Fact]
    public async Task Pages_through_the_answer_and_stops_at_300()
    {
        var (_, streams, twitch, _) = Service(new FakeTwitch(url => url.Contains("after=p1") ? Page(100, "p2") : url.Contains("after=p2") ? Page(100, "p3") : Page(100, "p1")));

        var count = await streams.CountLiveAsync(null, "Game 7", null, CancellationToken.None);

        // Three full pages with more behind them: "300+", and no fourth call
        Assert.Equal(new TwitchLiveCount(300, true), count);
        Assert.Equal(3, twitch.StreamCalls.Count());
        Assert.Contains("after=p1", twitch.StreamCalls.ElementAt(1));
        Assert.Contains("after=p2", twitch.StreamCalls.ElementAt(2));
    }

    [Fact]
    public async Task A_last_partial_page_makes_the_count_exact()
    {
        var (_, streams, twitch, _) = Service(new FakeTwitch(url => url.Contains("after=p1") ? Page(37, null) : Page(100, "p1")));

        Assert.Equal(new TwitchLiveCount(137, false), await streams.CountLiveAsync(null, "Game 7", null, CancellationToken.None));
        Assert.Equal(2, twitch.StreamCalls.Count());
    }

    [Fact]
    public async Task Uses_the_language_filter_of_the_stream_list_and_ignores_garbage()
    {
        var (_, streams, twitch, _) = Service(new FakeTwitch(_ => Page(3, null)));

        await streams.CountLiveAsync(null, "Game 7", "DE", CancellationToken.None);
        await streams.CountLiveAsync(null, "Game 7", "german; drop", CancellationToken.None);

        Assert.Contains("language=de", twitch.StreamCalls.First());
        Assert.DoesNotContain("language=", twitch.StreamCalls.Last());
    }

    [Fact]
    public async Task A_game_without_a_twitch_category_has_zero_live_streams_and_costs_no_streams_call()
    {
        var (_, streams, twitch, _) = Service(new FakeTwitch(_ => Page(5, null)));

        Assert.Equal(new TwitchLiveCount(0, false), await streams.CountLiveAsync(null, "Nothing", null, CancellationToken.None));
        Assert.Empty(twitch.StreamCalls);
    }

    [Fact]
    public async Task Without_credentials_there_is_nothing_to_count_and_nothing_is_sent()
    {
        var (_, streams, twitch, _) = Service(new FakeTwitch(_ => Page(5, null)), credentials: false);

        Assert.Null(await streams.CountLiveAsync(null, "Game 7", null, CancellationToken.None));
        Assert.Empty(twitch.Calls);
    }

    [Fact]
    public async Task The_count_is_cached_for_five_minutes_per_category_and_language()
    {
        var (_, streams, twitch, _) = Service(new FakeTwitch(_ => Page(8, null)));

        await streams.CountLiveAsync(null, "Game 7", null, CancellationToken.None);
        await streams.CountLiveAsync(null, "Game 7", null, CancellationToken.None);
        Assert.Single(twitch.StreamCalls);

        // Another language is another question
        await streams.CountLiveAsync(null, "Game 7", "de", CancellationToken.None);
        Assert.Equal(2, twitch.StreamCalls.Count());
    }

    [Fact]
    public async Task A_failed_call_throws_and_is_never_cached()
    {
        var fail = true;
        var (_, streams, twitch, _) = Service(new FakeTwitch(_ => fail ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Page(9, null)));

        await Assert.ThrowsAsync<TwitchApiException>(() => streams.CountLiveAsync(null, "Game 7", null, CancellationToken.None));
        fail = false;
        Assert.Equal(new TwitchLiveCount(9, false), await streams.CountLiveAsync(null, "Game 7", null, CancellationToken.None));
        Assert.Equal(2, twitch.StreamCalls.Count());
    }

    // ---- the batch ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_batch_answers_every_game_and_leaves_failed_ones_out_without_an_error()
    {
        var (batch, _, _, _) = Service(new FakeTwitch(url => url.Contains("game_id=2&") ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Page(5, null)));
        var games = new[] { new LiveCountGame(101, "Game 1"), new LiveCountGame(102, "Game 2"), new LiveCountGame(103, "Nothing"), new LiveCountGame(101, "Game 1") };

        var result = await batch.CountAsync(games, null, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal([new LiveCountEntry(101, 5, false), new LiveCountEntry(103, 0, false)], result.Counts);
        // Nothing is pending (a failure is not "still loading"): the card just has no badge
        Assert.Empty(result.Pending);
    }

    [Fact]
    public async Task A_rate_limit_ends_the_batch_instead_of_hammering_twitch()
    {
        var (batch, _, twitch, _) = Service(new FakeTwitch(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var games = Enumerable.Range(1, 40).Select(i => new LiveCountGame(1000 + i, $"Game {i}")).ToList();

        var result = await batch.CountAsync(games, null, TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Empty(result.Counts);
        Assert.Empty(result.Pending);
        // At most one call per parallel worker went out before the first 429 was seen
        Assert.True(twitch.StreamCalls.Count() <= 4, $"{twitch.StreamCalls.Count()} stream calls");
    }

    [Fact]
    public async Task Games_that_are_not_done_within_the_budget_are_reported_as_pending()
    {
        var (batch, _, _, _) = Service(new FakeTwitch(_ =>
        {
            Thread.Sleep(400);
            return Page(1, null);
        }));
        var games = Enumerable.Range(1, 12).Select(i => new LiveCountGame(2000 + i, $"Game {i}")).ToList();

        var result = await batch.CountAsync(games, null, TimeSpan.FromMilliseconds(700), CancellationToken.None);

        Assert.NotEmpty(result.Pending);
        Assert.Equal(12, result.Counts.Count + result.Pending.Count);
        Assert.DoesNotContain(result.Pending, id => result.Counts.Any(c => c.AppId == id));
    }

    [Fact]
    public async Task A_batch_is_capped()
    {
        var (batch, _, twitch, _) = Service(new FakeTwitch(_ => Page(1, null)));
        var games = Enumerable.Range(1, TwitchLiveCountService.MaxGames + 30).Select(i => new LiveCountGame(3000 + i, $"Game {i}")).ToList();

        var result = await batch.CountAsync(games, null, TimeSpan.FromSeconds(20), CancellationToken.None);

        Assert.Equal(TwitchLiveCountService.MaxGames, result.Counts.Count);
        Assert.Equal(TwitchLiveCountService.MaxGames, twitch.StreamCalls.Count());
    }
}
