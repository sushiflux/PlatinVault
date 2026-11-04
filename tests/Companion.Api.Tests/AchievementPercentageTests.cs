using System.Net;
using System.Net.Sockets;
using System.Text;
using Companion.Api.Configuration;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Companion.Api.Tests;

/// <summary>
/// The worldwide unlock shares (they decide Gold/Silver/Bronze): last good values are kept on disk and survive outages, a failure never
/// empties them and is never cached as data, retries back off, the cached remote list shows a refresh at once. Steam is never called:
/// every request goes to a fake handler.
/// </summary>
public sealed class AchievementPercentageTests : IDisposable
{
    private const uint App = 1234;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pv-percent-" + Guid.NewGuid().ToString("N"));

    public AchievementPercentageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string CacheFile(string dir) => Path.Combine(dir, "cache", "achievement-percentages.json");

    // ---- fake Steam -----------------------------------------------------------------------------------------------------------

    private sealed class FakeSteam : HttpMessageHandler, IHttpClientFactory
    {
        public Func<string, HttpResponseMessage> Answer { get; set; } = _ => Shares(("ACH_RARE", 3.5), ("ACH_COMMON", 71.2));
        public TimeSpan Delay { get; set; }
        public List<string> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Calls) Calls.Add(request.RequestUri!.AbsoluteUri);
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            return Answer(request.RequestUri!.AbsoluteUri);
        }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        public int Count
        {
            get { lock (Calls) return Calls.Count; }
        }
    }

    private static HttpResponseMessage Shares(params (string Name, double Percent)[] shares)
    {
        var list = string.Join(",", shares.Select(s => $"{{\"name\":\"{s.Name}\",\"percent\":{s.Percent.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"achievementpercentages\":{{\"achievements\":[{list}]}}}}", Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>What a firewall that blocks the connection looks like (Windows socket error 10013).</summary>
    private static HttpResponseMessage Blocked(string url) => throw new HttpRequestException(
        "An attempt was made to access a socket in a way forbidden by its access permissions. (api.steampowered.com:443)",
        new SocketException((int)SocketError.AccessDenied));

    private AchievementPercentageService Service(FakeSteam steam, ManualClock clock, string? dir = null) =>
        new(steam, Options.Create(new CompanionOptions { DataDir = dir ?? _dir }), NullLogger<AchievementPercentageService>.Instance, clock);

    // ---- failure, then success ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_failure_is_no_data_and_the_next_success_replaces_it()
    {
        var steam = new FakeSteam { Answer = Blocked };
        var clock = new ManualClock(Fakes.Start);
        var service = Service(steam, clock);

        var first = await service.GetAsync(App, inCollection: true);
        Assert.Equal(AchievementPercentageService.StateUnavailable, first.State);
        Assert.Empty(first.Values);
        Assert.Null(first.FetchedAt);
        Assert.True(first.Attempted);
        Assert.Contains("SocketException AccessDenied 10013", first.Detail);
        Assert.Contains("api.steampowered.com:443", first.Detail);

        // The empty answer of the failure is not cached: once the pause is over, Steam is asked again and the values arrive
        steam.Answer = _ => Shares(("ACH_RARE", 3.5), ("ACH_COMMON", 71.2));
        clock.Advance(AchievementPercentageService.FirstRetryDelay);
        var second = await service.GetAsync(App, inCollection: true);

        Assert.Equal(AchievementPercentageService.StateOk, second.State);
        Assert.Equal(3.5, second.Values["ACH_RARE"]);
        Assert.Equal(71.2, second.Values["ach_common"]);
        Assert.Equal(Fakes.Start + AchievementPercentageService.FirstRetryDelay, second.FetchedAt);
        Assert.Null(second.Detail);
    }

    [Fact]
    public async Task A_failure_after_a_success_keeps_the_values_and_reports_stale()
    {
        var steam = new FakeSteam();
        var clock = new ManualClock(Fakes.Start);
        var service = Service(steam, clock);
        var good = await service.GetAsync(App, inCollection: true);
        Assert.Equal(AchievementPercentageService.StateOk, good.State);

        steam.Answer = Blocked;
        clock.Advance(AchievementPercentageService.RefreshAfter);
        var stale = await service.GetAsync(App, inCollection: true);

        Assert.Equal(AchievementPercentageService.StateStale, stale.State);
        Assert.Equal(3.5, stale.Values["ACH_RARE"]);
        Assert.Equal(Fakes.Start, stale.FetchedAt);
        Assert.NotNull(stale.Detail);

        // The stored file still holds the good values, not the failure
        var onDisk = await File.ReadAllTextAsync(CacheFile(_dir));
        Assert.Contains("ACH_RARE", onDisk);
    }

    [Fact]
    public async Task Without_any_stored_value_a_failure_is_unavailable_and_the_status_explains_it_in_both_languages()
    {
        var steam = new FakeSteam { Answer = Blocked };
        var service = Service(steam, new ManualClock(Fakes.Start));
        var snapshot = await service.GetAsync(App, inCollection: true);

        var de = SteamLiveTrackerService.BuildRarityStatus(snapshot, "de");
        var en = SteamLiveTrackerService.BuildRarityStatus(snapshot, "en");

        Assert.Equal("unavailable", de.State);
        Assert.Null(de.FetchedAt);
        Assert.Contains("vorläufig", de.Message);
        Assert.Contains("Firewall", de.Message);
        Assert.Contains("provisional", en.Message);
        Assert.Contains("firewall", en.Message);
        Assert.Contains("SocketException", de.Detail);

        var ok = SteamLiveTrackerService.BuildRarityStatus(new PercentageSnapshot([], "ok", Fakes.Start, null, false), "de");
        Assert.Equal("ok", ok.State);
        Assert.Null(ok.Message);
        Assert.Null(ok.Detail);
    }

    [Fact]
    public void The_stale_status_names_the_age_of_the_values_and_says_what_it_means()
    {
        var snapshot = new PercentageSnapshot(new Dictionary<string, double> { ["A"] = 5 }, "stale", Fakes.Start, "HttpRequestException: boom", false);
        var status = SteamLiveTrackerService.BuildRarityStatus(snapshot, "en");

        Assert.Equal("stale", status.State);
        Assert.Equal(Fakes.Start, status.FetchedAt);
        Assert.Contains("last saved", status.Message);
        Assert.Equal("HttpRequestException: boom", status.Detail);
    }

    [Fact]
    public async Task The_technical_detail_has_no_query_string()
    {
        var steam = new FakeSteam
        {
            Answer = _ => throw new HttpRequestException("Failed for https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v0002/?gameid=1234&key=SECRET&format=json now"),
        };
        var snapshot = await Service(steam, new ManualClock(Fakes.Start)).GetAsync(App, inCollection: true);

        Assert.Contains("https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v0002/", snapshot.Detail);
        Assert.DoesNotContain("SECRET", snapshot.Detail);
        Assert.DoesNotContain("gameid", snapshot.Detail);
    }

    [Fact]
    public async Task An_error_status_and_an_unexpected_answer_count_as_failures()
    {
        var steam = new FakeSteam { Answer = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) };
        var clock = new ManualClock(Fakes.Start);
        var service = Service(steam, clock);

        var unavailable = await service.GetAsync(App, inCollection: true);
        Assert.Equal("unavailable", unavailable.State);
        Assert.Contains("HTTP 503", unavailable.Detail);

        steam.Answer = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"something\":1}") };
        clock.Advance(AchievementPercentageService.FirstRetryDelay);
        var wrong = await service.GetAsync(App, inCollection: true);
        Assert.Equal("unavailable", wrong.State);
        Assert.Contains("Unexpected answer", wrong.Detail);
    }

    // ---- the file survives ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_cache_file_survives_a_new_service_instance_and_an_outage_right_after_the_start()
    {
        var first = Service(new FakeSteam(), new ManualClock(Fakes.Start));
        await first.GetAsync(App, inCollection: true);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(CacheFile(_dir))!, "*.tmp"));

        // A new process: Steam cannot be reached, the stored values are used (and say so)
        var offline = new FakeSteam { Answer = Blocked };
        var restarted = Service(offline, new ManualClock(Fakes.Start + TimeSpan.FromMinutes(5)));
        Assert.Equal(3.5, restarted.Peek(App).Values["ACH_RARE"]);

        var snapshot = await restarted.GetAsync(App, inCollection: true);
        Assert.Equal(1, offline.Count);
        Assert.Equal("stale", snapshot.State);
        Assert.Equal(3.5, snapshot.Values["ACH_RARE"]);
        Assert.Equal(Fakes.Start, snapshot.FetchedAt);

        // The first use per process fetches even though the stored values are young
        var online = new FakeSteam { Answer = _ => Shares(("ACH_RARE", 4.0)) };
        var next = Service(online, new ManualClock(Fakes.Start + TimeSpan.FromMinutes(5)));
        var fresh = await next.GetAsync(App, inCollection: true);
        Assert.Equal(1, online.Count);
        Assert.Equal("ok", fresh.State);
        Assert.Equal(4.0, fresh.Values["ACH_RARE"]);
    }

    [Fact]
    public async Task An_unreadable_file_is_ignored_and_replaced_by_the_next_good_fetch()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CacheFile(_dir))!);
        await File.WriteAllTextAsync(CacheFile(_dir), "{ this is not json");

        var service = Service(new FakeSteam(), new ManualClock(Fakes.Start));
        Assert.Equal("unavailable", service.Peek(App).State);
        Assert.Equal("ok", (await service.GetAsync(App, inCollection: true)).State);
        Assert.Contains("ACH_RARE", await File.ReadAllTextAsync(CacheFile(_dir)));
    }

    // ---- only the collection is persisted, memory is bounded ------------------------------------------------------------------

    [Fact]
    public async Task A_lookup_outside_the_collection_works_but_is_never_written_to_the_file()
    {
        var steam = new FakeSteam();
        var service = Service(steam, new ManualClock(Fakes.Start));

        var lookup = await service.GetAsync(999_001, inCollection: false);
        Assert.Equal("ok", lookup.State);
        Assert.Equal(3.5, lookup.Values["ACH_RARE"]);
        Assert.Equal(3.5, service.Peek(999_001).Values["ACH_RARE"]);
        Assert.False(File.Exists(CacheFile(_dir)));

        await service.GetAsync(App, inCollection: true);
        var file = await File.ReadAllTextAsync(CacheFile(_dir));
        Assert.Contains($"\"{App}\"", file);
        Assert.DoesNotContain("999001", file);

        // A later lookup of the same game outside the collection does not take it out of the file
        await service.GetAsync(App, inCollection: false);
        Assert.Equal("ok", Service(new FakeSteam(), new ManualClock(Fakes.Start)).Peek(App).State);
    }

    [Fact]
    public async Task Arbitrary_app_ids_cannot_grow_the_memory_beyond_the_cap_and_the_collection_is_never_dropped()
    {
        var steam = new FakeSteam();
        var service = Service(steam, new ManualClock(Fakes.Start));
        await service.GetAsync(App, inCollection: true);

        var extra = AchievementPercentageService.MaxTransientEntries + 150;
        for (uint id = 1; id <= extra; id++)
        {
            await service.GetAsync(500_000 + id, inCollection: false);
        }

        Assert.Equal(AchievementPercentageService.MaxTransientEntries + 1, service.EntryCount);
        Assert.Equal("ok", service.Peek(App).State);
        // The least recently used ones went first, the newest are still there
        Assert.Equal("unavailable", service.Peek(500_001).State);
        Assert.Equal("ok", service.Peek(500_000 + (uint)extra).State);
        Assert.DoesNotContain("500001", await File.ReadAllTextAsync(CacheFile(_dir)));
    }

    [Fact]
    public async Task Unchanged_values_do_not_rewrite_the_file_until_the_stored_copy_is_a_day_old()
    {
        var steam = new FakeSteam();
        var clock = new ManualClock(Fakes.Start);
        var service = Service(steam, clock);
        await service.GetAsync(App, inCollection: true);
        var marker = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(CacheFile(_dir), marker);

        // Due again after an hour, Steam answers the same values: no write
        clock.Advance(AchievementPercentageService.RefreshAfter);
        await service.GetAsync(App, inCollection: true);
        Assert.Equal(2, steam.Count);
        Assert.Equal(marker, File.GetLastWriteTimeUtc(CacheFile(_dir)));

        // Changed values are written at once
        steam.Answer = _ => Shares(("ACH_RARE", 3.6), ("ACH_COMMON", 71.2));
        clock.Advance(AchievementPercentageService.RefreshAfter);
        await service.GetAsync(App, inCollection: true);
        Assert.NotEqual(marker, File.GetLastWriteTimeUtc(CacheFile(_dir)));
        Assert.Contains("3.6", await File.ReadAllTextAsync(CacheFile(_dir)));

        // The same values again a day after the last write: written, so the stored date stays useful
        File.SetLastWriteTimeUtc(CacheFile(_dir), marker);
        clock.Advance(AchievementPercentageService.RewriteUnchangedAfter);
        await service.GetAsync(App, inCollection: true);
        Assert.NotEqual(marker, File.GetLastWriteTimeUtc(CacheFile(_dir)));
    }

    [Fact]
    public async Task A_game_that_left_the_collection_is_removed_from_the_file()
    {
        var service = Service(new FakeSteam(), new ManualClock(Fakes.Start));
        await service.GetAsync(App, inCollection: true);
        await service.GetAsync(4321, inCollection: true);

        await service.RetainCollectionAsync([4321]);

        var file = await File.ReadAllTextAsync(CacheFile(_dir));
        Assert.Contains("\"4321\"", file);
        Assert.DoesNotContain($"\"{App}\"", file);
        // Still usable in memory as a transient lookup
        Assert.Equal("ok", service.Peek(App).State);
    }

    // ---- refresh rules and back-off -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Values_are_fetched_once_and_then_only_when_older_than_an_hour()
    {
        var steam = new FakeSteam();
        var clock = new ManualClock(Fakes.Start);
        var service = Service(steam, clock);

        await service.GetAsync(App, inCollection: true);
        await service.GetAsync(App, inCollection: true);
        clock.Advance(AchievementPercentageService.RefreshAfter - TimeSpan.FromSeconds(1));
        await service.GetAsync(App, inCollection: true);
        Assert.Equal(1, steam.Count);

        clock.Advance(TimeSpan.FromSeconds(1));
        var refreshed = await service.GetAsync(App, inCollection: true);
        Assert.Equal(2, steam.Count);
        Assert.True(refreshed.Attempted);
    }

    [Fact]
    public async Task Retries_back_off_from_two_to_thirty_minutes_and_never_hammer()
    {
        var steam = new FakeSteam { Answer = Blocked };
        var clock = new ManualClock(Fakes.Start);
        var service = Service(steam, clock);

        // Many callers in the same moment: one request
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.GetAsync(App, inCollection: true)));
        Assert.Equal(1, steam.Count);

        var expectedDelays = new[] { 2, 4, 8, 16, 30, 30 };
        for (var i = 0; i < expectedDelays.Length; i++)
        {
            var delay = TimeSpan.FromMinutes(expectedDelays[i]);
            Assert.Equal(delay, AchievementPercentageService.RetryDelay(i + 1));

            clock.Advance(delay - TimeSpan.FromSeconds(1));
            var tooEarly = await service.GetAsync(App, inCollection: true);
            Assert.False(tooEarly.Attempted);
            Assert.Equal(i + 1, steam.Count);

            clock.Advance(TimeSpan.FromSeconds(1));
            var retry = await service.GetAsync(App, inCollection: true);
            Assert.True(retry.Attempted);
            Assert.Equal(i + 2, steam.Count);
        }
    }

    [Fact]
    public async Task Concurrent_callers_for_one_game_share_a_single_request()
    {
        var steam = new FakeSteam { Delay = TimeSpan.FromMilliseconds(150) };
        var service = Service(steam, new ManualClock(Fakes.Start));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.GetAsync(App, inCollection: true)));

        Assert.Equal(1, steam.Count);
        Assert.All(results, r => Assert.Equal("ok", r.State));
    }

    // ---- the cached remote list shows a refresh at once ------------------------------------------------------------------------

    [Fact]
    public void Items_built_without_shares_get_tier_band_and_label_from_the_current_shares()
    {
        LiveAchievementItemDto Item(string name) => new(name, name, "", null, false, "", "", false, null, 25.0, "Silver", "", false, "") { HasGlobalPercent = false };
        var cached = new[] { Item("ACH_RARE"), Item("ACH_COMMON"), Item("ACH_UNKNOWN") };

        var shares = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["ach_rare"] = 3.5, ["ACH_COMMON"] = 71.2 };
        var applied = SteamLiveTrackerService.ApplyPercentages(cached, shares, "en");

        Assert.Equal("Gold", applied[0].RarityTier);
        Assert.Equal("ultraRare", applied[0].RarityBand);
        Assert.Equal("Ultra rare", applied[0].RarityLabel);
        Assert.True(applied[0].HasGlobalPercent);
        Assert.Equal(3.5, applied[0].GlobalPercent);
        Assert.Equal("Bronze", applied[1].RarityTier);
        Assert.Equal("common", applied[1].RarityBand);

        // No share known: exactly the fallback of before (Silver, no band, no label)
        Assert.Equal("Silver", applied[2].RarityTier);
        Assert.False(applied[2].HasGlobalPercent);
        Assert.Equal(string.Empty, applied[2].RarityBand);
        Assert.Equal(string.Empty, applied[2].RarityLabel);

        // The cached originals are not touched
        Assert.Equal("Silver", cached[0].RarityTier);
    }

    // ---- JSON contract --------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_status_is_serialised_as_rarity_with_camel_case_fields()
    {
        var empty = new LiveTrophyTierSummaryDto(0, 0);
        var dto = new LiveGameAchievementsDto(
            new LiveGameInfoDto(0, "x", "", "", false, 0, 0, "", "", "", null), [], 0, 0, 0, 0, false, empty, empty, empty, empty, null, 0,
            new LiveRarityStatusDto("stale", Fakes.Start, "Message", "Detail"));

        var json = System.Text.Json.JsonSerializer.Serialize(dto, ApiEndpoints.JsonOptions);

        Assert.Contains("\"rarity\":{\"state\":\"stale\",\"fetchedAt\":\"2026-11-01T10:00:00+00:00\",\"message\":\"Message\",\"detail\":\"Detail\"}", json);
    }

    // ---- background worker -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_worker_refreshes_the_collection_one_game_after_another_and_stops_when_steam_stays_unreachable()
    {
        using var scratch = new SqliteScratch();
        await using (var seed = await scratch.MigratedAsync())
        {
            for (var i = 1; i <= 6; i++)
            {
                seed.Games.Add(new GameEntity { Id = Guid.NewGuid(), Slug = "game-" + i, Title = "Game " + i, SteamAppId = 1000 + i });
            }
            seed.Games.Add(new GameEntity { Id = Guid.NewGuid(), Slug = "no-steam", Title = "No Steam" });
            await seed.SaveChangesAsync();
        }
        var services = new ServiceCollection().AddScoped(_ => scratch.Context()).BuildServiceProvider();

        var steam = new FakeSteam();
        var worker = new AchievementPercentageWorker(
            Service(steam, new ManualClock(Fakes.Start)), services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AchievementPercentageWorker>.Instance);

        Assert.Equal(6, await worker.RunOnceAsync(CancellationToken.None, TimeSpan.Zero));
        Assert.Equal(6, steam.Count);
        Assert.Equal(6, steam.Calls.Select(c => c[(c.IndexOf("gameid=", StringComparison.Ordinal) + 7)..].Split('&')[0]).Distinct().Count());

        // Nothing is due right after: no request at all
        Assert.Equal(0, await worker.RunOnceAsync(CancellationToken.None, TimeSpan.Zero));
        Assert.Equal(6, steam.Count);

        // A blocked connection: the run gives up after three failures in a row instead of trying every game
        var blocked = new FakeSteam { Answer = Blocked };
        var other = new AchievementPercentageWorker(
            Service(blocked, new ManualClock(Fakes.Start), Path.Combine(_dir, "other")), services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AchievementPercentageWorker>.Instance);
        Assert.Equal(AchievementPercentageWorker.MaxFailuresInARow, await other.RunOnceAsync(CancellationToken.None, TimeSpan.Zero));
        Assert.Equal(AchievementPercentageWorker.MaxFailuresInARow, blocked.Count);
    }

    [Fact]
    public async Task The_worker_never_throws_when_the_database_is_unusable()
    {
        var services = new ServiceCollection().AddScoped<CompanionDbContext>(_ => throw new InvalidOperationException("no database")).BuildServiceProvider();
        var worker = new AchievementPercentageWorker(
            Service(new FakeSteam(), new ManualClock(Fakes.Start)), services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AchievementPercentageWorker>.Instance);

        Assert.Equal(0, await worker.RunOnceAsync(CancellationToken.None, TimeSpan.Zero));
    }
}
