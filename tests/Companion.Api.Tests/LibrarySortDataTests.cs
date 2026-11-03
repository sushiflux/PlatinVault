using System.Net;
using System.Text;
using Companion.Api.Endpoints;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Companion.Api.Tests;

/// <summary>
/// The data behind the dashboard sort orders (user wish 2026-11-02): the stored sort choice, Steam's local appinfo.vdf and appmanifest
/// files, and the library metadata service with its store fallback. Steam is never called: the store goes through a fake handler, the
/// local files are written into a temp folder.
/// </summary>
public sealed class LibrarySortDataTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pv-sort-" + Guid.NewGuid().ToString("N"));

    public LibrarySortDataTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ---- stored sort choice -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("custom:asc", true, "custom:asc")]
    [InlineData(" lastPlayed:DESC ", true, "lastPlayed:desc")]
    [InlineData("reviews:asc", false, "reviews:asc")]
    public void Sort_choice_is_normalised(string value, bool favorites, string expected) =>
        Assert.Equal(expected, DashboardSort.Normalize(value, favorites));

    [Theory]
    [InlineData("custom:asc", false)] // the manual order exists only in the favourites view
    [InlineData("dateAdded:desc", true)] // not an order of this app
    [InlineData("playtime", true)]
    [InlineData("playtime:up", true)]
    [InlineData("playtime:asc:x", true)]
    [InlineData("", true)]
    [InlineData(null, false)]
    public void Invalid_sort_choice_is_rejected(string? value, bool favorites) =>
        Assert.Null(DashboardSort.Normalize(value, favorites));

    [Fact]
    public void Missing_or_damaged_stored_sort_choice_falls_back_to_the_default()
    {
        Assert.Equal("custom:asc", DashboardSort.Resolve(null, favorites: true));
        Assert.Equal("lastPlayed:desc", DashboardSort.Resolve(null, favorites: false));
        Assert.Equal("lastPlayed:desc", DashboardSort.Resolve("custom:asc", favorites: false));
        Assert.Equal("size:desc", DashboardSort.Resolve("size:desc", favorites: true));
    }

    // ---- appinfo.vdf --------------------------------------------------------------------------------------------------------

    /// <summary>Writes binary KeyValues the way appinfo.vdf stores them (keys inline for v40, as table indexes for v41).</summary>
    private sealed class KvWriter(bool indexed, List<string> table)
    {
        private readonly MemoryStream _out = new();

        private void Key(string key)
        {
            if (indexed)
            {
                var index = table.IndexOf(key);
                if (index < 0) { table.Add(key); index = table.Count - 1; }
                _out.Write(BitConverter.GetBytes((uint)index));
            }
            else
            {
                _out.Write(Encoding.UTF8.GetBytes(key));
                _out.WriteByte(0);
            }
        }

        public KvWriter Obj(string key) { _out.WriteByte(0); Key(key); return this; }
        public KvWriter End() { _out.WriteByte(8); return this; }
        public KvWriter Str(string key, string value) { _out.WriteByte(1); Key(key); _out.Write(Encoding.UTF8.GetBytes(value)); _out.WriteByte(0); return this; }
        public KvWriter Int(string key, int value) { _out.WriteByte(2); Key(key); _out.Write(BitConverter.GetBytes(value)); return this; }
        public KvWriter Float(string key, float value) { _out.WriteByte(3); Key(key); _out.Write(BitConverter.GetBytes(value)); return this; }
        public KvWriter Long(string key, long value) { _out.WriteByte(10); Key(key); _out.Write(BitConverter.GetBytes(value)); return this; }
        public byte[] ToArray() => _out.ToArray();
    }

    private static byte[] AppInfoFile(bool v41, params (uint AppId, Func<KvWriter, KvWriter> Body)[] apps)
    {
        var table = new List<string>();
        var entries = new MemoryStream();
        foreach (var (appId, body) in apps)
        {
            var kv = body(new KvWriter(v41, table)).ToArray();
            entries.Write(BitConverter.GetBytes(appId));
            entries.Write(BitConverter.GetBytes((uint)(60 + kv.Length)));
            entries.Write(new byte[60]); // state, update time, token, hashes, change number: not read
            entries.Write(kv);
        }
        entries.Write(BitConverter.GetBytes(0u));

        var file = new MemoryStream();
        file.Write(BitConverter.GetBytes(v41 ? 0x07564429u : 0x07564428u));
        file.Write(BitConverter.GetBytes(1u));
        if (v41)
        {
            // The string table follows the entries: 16 bytes of header + the entries
            file.Write(BitConverter.GetBytes(16L + entries.Length));
        }
        file.Write(entries.ToArray());
        if (v41)
        {
            file.Write(BitConverter.GetBytes((uint)table.Count));
            foreach (var name in table)
            {
                file.Write(Encoding.UTF8.GetBytes(name));
                file.WriteByte(0);
            }
        }
        return file.ToArray();
    }

    private static KvWriter Game(KvWriter w, long steamRelease, long original, int metacritic, int review, int score) =>
        w.Obj("appinfo").Int("appid", 1)
            .Obj("common").Str("name", "Some Game").Str("type", "Game")
            .Int("steam_release_date", (int)steamRelease).Int("original_release_date", (int)original)
            .Int("metacritic_score", metacritic).Int("review_percentage", review).Int("review_score", score)
            .Float("some_float", 1.5f).Long("big", 123456789012L)
            .Obj("nested_inside_common").Str("metacritic_score", "must-not-count").End()
            .End() // common
            .Obj("depots").Int("metacritic_score", 5).End() // same key name elsewhere: ignored
            .End() // appinfo
            .End(); // root

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AppInfo_is_read_in_both_layouts(bool v41)
    {
        var file = AppInfoFile(v41,
            (70, w => Game(w, 911462400, 0, 96, 96, 9)),
            (620, w => w.Obj("appinfo").Obj("common").Str("type", "game").Int("steam_release_date", 1303186800).End().End().End()),
            (999, w => w.Obj("appinfo").Obj("config").Str("x", "y").End().End().End()));

        var apps = SteamAppInfoReader.Parse(file);

        Assert.NotNull(apps);
        Assert.Equal(new AppInfoCommon("Game", 911462400, 96, 96, 9), apps[70]);
        // Nothing but a release date: every other value is unknown (null), never 0
        Assert.Equal(new AppInfoCommon("game", 1303186800, null, null, null), apps[620]);
        // An app without a common section is not a library fact
        Assert.False(apps.ContainsKey(999));
    }

    [Fact]
    public void AppInfo_release_falls_back_to_the_original_date_and_ignores_zero()
    {
        var file = AppInfoFile(true,
            (1, w => Game(w, 0, 1052809200, 50, 60, 6)),
            (2, w => Game(w, 0, 0, 50, 60, 6)));
        var apps = SteamAppInfoReader.Parse(file)!;
        Assert.Equal(1052809200, apps[1].ReleaseUnix);
        Assert.Null(apps[2].ReleaseUnix);
    }

    [Fact]
    public void AppInfo_of_an_unknown_or_damaged_file_is_null_and_never_throws()
    {
        Assert.Null(SteamAppInfoReader.Parse([]));
        Assert.Null(SteamAppInfoReader.Parse(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
        var file = AppInfoFile(true, (70, w => Game(w, 911462400, 0, 96, 96, 9)));
        Assert.Null(SteamAppInfoReader.Parse(file.AsSpan(0, file.Length / 2)));
        // A future format version is reported as unsupported, not guessed at
        var future = (byte[])file.Clone();
        BitConverter.GetBytes(0x0756442Au).CopyTo(future, 0);
        Assert.Null(SteamAppInfoReader.Parse(future));
        Assert.Null(SteamAppInfoReader.ReadFile(Path.Combine(_dir, "missing.vdf")));
    }

    private static KvWriter Depot(KvWriter w, string id, string oslist, string osarch, string language, long size, bool dlc = false, bool deck = false)
    {
        w.Obj(id);
        if (dlc) w.Int("dlcappid", 99);
        w.Obj("config");
        if (oslist.Length > 0) w.Str("oslist", oslist);
        if (osarch == "64-number") w.Int("osarch", 64);
        else if (osarch.Length > 0) w.Str("osarch", osarch);
        if (language.Length > 0) w.Str("language", language);
        if (deck) w.Int("steamdeck", 1);
        w.End().Obj("manifests").Obj("public").Str("gid", "1");
        // Like Steam: a number up to 2 GB, text above (a KeyValues integer has 32 bits)
        if (size > int.MaxValue) w.Str("size", size.ToString(System.Globalization.CultureInfo.InvariantCulture));
        else w.Int("size", (int)size);
        return w.Int("download", 1).End().End().End();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AppInfo_install_size_is_the_sum_of_the_depots_that_apply(bool v41)
    {
        var file = AppInfoFile(v41,
            (70, w =>
            {
                w.Obj("appinfo").Obj("common").Str("type", "game").End().Obj("depots");
                Depot(w, "71", "windows", "64", "", 5_000_000_000); // counted, size stored as text
                Depot(w, "72", "windows", "32", "", 500); // 32 bit while a 64 bit depot exists
                Depot(w, "73", "macos", "", "", 700); // another system
                Depot(w, "74", "", "", "german", 300); // another language
                Depot(w, "75", "", "", "", 200, dlc: true); // DLC
                Depot(w, "76", "", "", "", 400, deck: true); // Steam Deck only
                Depot(w, "77", "windows,linux", "64-number", "english", 50); // osarch as a number, default language
                Depot(w, "78", "", "", "", 25); // no restriction at all
                w.Str("baselanguages", "english").Obj("branches").Obj("public").Int("buildid", 1).End().End();
                return w.End().End().End();
            }),
            (71, w => w.Obj("appinfo").Obj("common").Str("type", "game").End().End().End()), // no depot data
            (72, w =>
            {
                w.Obj("appinfo").Obj("common").Str("type", "game").End().Obj("depots");
                Depot(w, "73", "windows", "32", "", 300); // the only depot is 32 bit: it counts
                return w.End().End().End();
            }));

        var apps = SteamAppInfoReader.Parse(file)!;

        Assert.Equal(5_000_000_000 + 50 + 25, apps[70].InstallSizeBytes);
        Assert.Null(apps[71].InstallSizeBytes);
        Assert.Equal(300, apps[72].InstallSizeBytes);
    }

    [Fact]
    public void Install_size_is_unknown_when_no_depot_applies()
    {
        Assert.Null(SteamAppInfoReader.InstallSizeOf([]));
        Assert.Null(SteamAppInfoReader.InstallSizeOf([new(0, false, false, "", "", ""), new(100, true, false, "", "", "")]));
    }

    // ---- local library files ------------------------------------------------------------------------------------------------

    [Fact]
    public void Sizes_come_from_the_manifests_of_every_library_folder()
    {
        var steam = Path.Combine(_dir, "Steam");
        var second = Path.Combine(_dir, "Games2");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(second, "steamapps"));
        // VDF escapes backslashes in paths; a folder that does not exist is skipped
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam.Replace("\\", "\\\\") + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" +
            second.Replace("\\", "\\\\") + "\"\n\t}\n\t\"2\"\n\t{\n\t\t\"path\"\t\t\"Z:\\\\nowhere\"\n\t}\n}\n");
        File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_70.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"70\"\n\t\"SizeOnDisk\"\t\t\"12436299617\"\n}\n");
        File.WriteAllText(Path.Combine(second, "steamapps", "appmanifest_620.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"620\"\n\t\"SizeOnDisk\"\t\t\"500\"\n}\n");
        // Queued, not installed yet: no size
        File.WriteAllText(Path.Combine(second, "steamapps", "appmanifest_730.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"730\"\n\t\"SizeOnDisk\"\t\t\"0\"\n}\n");

        var folders = SteamLocalLibraryService.LibraryFolders(steam);
        var sizes = SteamLocalLibraryService.ReadSizes(SteamLocalLibraryService.ManifestFiles(steam));

        Assert.Equal(2, folders.Count);
        Assert.Equal(new Dictionary<int, long> { [70] = 12436299617, [620] = 500 }, sizes);
    }

    [Fact]
    public void Local_entries_join_appinfo_and_sizes()
    {
        var info = new Dictionary<int, AppInfoCommon> { [70] = new("Game", 911462400, 96, 96, 9, 777), [71] = new("Game", null, null, null, null) };
        var merged = SteamLocalLibraryService.Merge(info, new Dictionary<int, long> { [70] = 5, [99] = 7 });

        Assert.Equal(new LocalLibraryEntry(911462400, 96, 96, 9, 5, 777), merged[70]);
        Assert.Equal(new LocalLibraryEntry(null, null, null, null, null), merged[71]);
        // A game that has a manifest but is not in appinfo still gets its size
        Assert.Equal(new LocalLibraryEntry(null, null, null, null, 7), merged[99]);
    }

    // ---- library metadata service -------------------------------------------------------------------------------------------

    private static SteamOwnedGameDto Owned(int appId, int daysAgo = 0) => new(
        appId, $"Game {appId}", 1, "", "", "", false, null, false, DateTimeOffset.UtcNow.AddDays(-daysAgo));

    private static string StoreAnswer(params (int AppId, long Release, int Percent, int Score)[] items) =>
        "{\"response\":{\"store_items\":[" + string.Join(",", items.Select(i =>
            $"{{\"appid\":{i.AppId},\"success\":1,\"release\":{{\"steam_release_date\":{i.Release}}},\"reviews\":{{\"summary_filtered\":{{\"review_count\":500,\"percent_positive\":{i.Percent},\"review_score\":{i.Score}}}}}}}")) + "]}}";

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeStore(Func<string, HttpResponseMessage> respond) : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(respond(request.RequestUri.AbsoluteUri));
        }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private LibraryMetaService Service(FakeStore http, Dictionary<int, LocalLibraryEntry>? local = null, FakeClock? clock = null, string? name = null)
    {
        var cache = Path.Combine(_dir, name ?? "cache");
        return new LibraryMetaService(http, () => local, cache, NullLogger<LibraryMetaService>.Instance, clock ?? new FakeClock(DateTimeOffset.UtcNow), TimeSpan.Zero);
    }

    [Fact]
    public void Store_answer_gives_release_and_rating_and_ignores_unrated_games()
    {
        var body = StoreAnswer((70, 911499840, 96, 9)).Replace("\"review_score\":9", "\"review_score\":0");
        var parsed = LibraryMetaService.ParseItems(body, [70, 71], DateTimeOffset.UnixEpoch);

        // Release known, but one lucky review of a game Steam does not rate yet must not rank it
        Assert.Equal(911499840, parsed[70].ReleaseUnix);
        Assert.Null(parsed[70].ReviewPercent);
        // Asked but not listed: remembered as "nothing there", so it is not asked again for two weeks
        Assert.Equal(new StoreLibraryMeta(null, null, null, DateTimeOffset.UnixEpoch), parsed[71]);
    }

    [Fact]
    public async Task Scan_asks_in_chunks_of_fifty_and_only_for_apps_without_local_data()
    {
        var local = new Dictionary<int, LocalLibraryEntry> { [1] = new(100, 50, 80, 7, null) };
        var http = new FakeStore(url =>
        {
            var asked = System.Text.RegularExpressions.Regex.Matches(Uri.UnescapeDataString(url), "\"appid\":(\\d+)").Select(m => int.Parse(m.Groups[1].Value)).ToList();
            return Ok(StoreAnswer(asked.Select(id => (id, 1000L + id, 90, 8)).ToArray()));
        });
        var service = Service(http, local);

        // App 1 is known locally; 120 more are not -> 3 calls (50 + 50 + 20)
        await service.StartScan(Enumerable.Range(1, 121).Select(id => Owned(id)).ToList());

        Assert.Equal(3, http.Urls.Count);
        Assert.DoesNotContain(http.Urls, u => Uri.UnescapeDataString(u).Contains("\"appid\":1,") || Uri.UnescapeDataString(u).Contains("\"appid\":1}"));
        Assert.All(http.Urls, u => Assert.StartsWith("https://api.steampowered.com/IStoreBrowseService/GetItems/v1/", u));
        Assert.Equal((false, 120, 120), service.ScanState);

        var view = service.Snapshot();
        Assert.Equal(new LibraryMeta(100, 50, 80, 7, null), view.Get(1)); // local wins and is not overwritten
        Assert.Equal(1002, view.Get(2)!.ReleaseUnix);
        Assert.Equal(90, view.Get(2)!.ReviewPercent);
        Assert.Null(view.Get(2)!.Metacritic); // the store answer has no Metacritic: unknown, not 0
        Assert.Null(view.Get(2)!.SizeBytes);
    }

    [Fact]
    public async Task Scan_is_not_repeated_for_two_weeks_and_a_failure_is_not_cached()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var fail = true;
        var http = new FakeStore(_ => fail ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : Ok(StoreAnswer((5, 2000, 70, 6))));
        var service = Service(http, clock: clock);
        var games = new[] { Owned(5) };

        await service.StartScan(games);
        Assert.Single(http.Urls);
        Assert.Null(service.Snapshot().Get(5));

        // Within the hour the refused app is left alone; after it, it is asked again
        await service.StartScan(games);
        Assert.Single(http.Urls);
        clock.Advance(TimeSpan.FromMinutes(61));
        fail = false;
        await service.StartScan(games);
        Assert.Equal(2, http.Urls.Count);
        Assert.Equal(2000, service.Snapshot().Get(5)!.ReleaseUnix);

        // Known now: no new call for 14 days, then one again
        await service.StartScan(games);
        Assert.Equal(2, http.Urls.Count);
        clock.Advance(TimeSpan.FromDays(15));
        await service.StartScan(games);
        Assert.Equal(3, http.Urls.Count);
    }

    [Fact]
    public async Task A_failed_call_ends_the_run_instead_of_hammering_the_store()
    {
        var http = new FakeStore(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = Service(http);

        await service.StartScan(Enumerable.Range(1, 120).Select(id => Owned(id)).ToList());

        Assert.Single(http.Urls); // 120 apps = 3 chunks, but the first failure stops the run
    }

    [Fact]
    public async Task Store_answers_survive_a_restart()
    {
        var http = new FakeStore(_ => Ok(StoreAnswer((9, 4242, 88, 8))));
        await Service(http).StartScan([Owned(9)]);

        var again = Service(new FakeStore(_ => throw new InvalidOperationException("no call expected")));
        Assert.Equal(4242, again.Snapshot().Get(9)!.ReleaseUnix);
        await again.StartScan([Owned(9)]); // cached: would throw if it asked
    }

    [Fact]
    public async Task Bridge_data_is_merged_after_local_files_and_kept_across_a_restart()
    {
        var http = new FakeStore(_ => Ok(StoreAnswer((7, 111, 10, 2))));
        var service = Service(http, new Dictionary<int, LocalLibraryEntry> { [1] = new(1000, null, null, null, 10) });
        await service.IngestBridgeAsync(new Dictionary<int, LocalLibraryEntry>
        {
            [1] = new(2000, 70, 71, 7, 20),
            [7] = new(3000, 80, 81, 8, 30),
        }, CancellationToken.None);

        var view = service.Snapshot();
        // Per field the local file wins; what it lacks (Metacritic, review) is taken from the bridge
        Assert.Equal(new LibraryMeta(1000, 70, 71, 7, 10), view.Get(1));
        Assert.Equal(new LibraryMeta(3000, 80, 81, 8, 30), view.Get(7));
        Assert.Null(view.Get(8));

        // The store is not asked for an app the bridge knows
        await service.StartScan([Owned(7)]);
        Assert.Empty(http.Urls);

        var restarted = Service(http, local: null);
        Assert.Equal(new LibraryMeta(2000, 70, 71, 7, 20), restarted.Snapshot().Get(1));
    }

    [Fact]
    public void Summary_cards_get_library_values_without_changing_the_card_facts()
    {
        var view = new LibraryMetaView(
            new Dictionary<int, LocalLibraryEntry> { [10] = new(1700000000, 77, 91, 8, 123456, 130000) },
            new Dictionary<int, LocalLibraryEntry>(),
            new Dictionary<int, StoreLibraryMeta>());

        var card = Card(10, metacritic: 80, reviewPercent: null);
        var enriched = ApiEndpoints.WithLibraryMeta(card, view);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), enriched.ReleaseAt);
        Assert.Equal(123456, enriched.SizeOnDiskBytes);
        Assert.Equal(130000, enriched.InstallSizeBytes);
        Assert.Equal(80, enriched.SortMetacritic); // the card's own value comes first
        Assert.Equal(91, enriched.SortReviewPercent); // the library fills a gap
        Assert.Equal(80, enriched.MetacriticScore);
        Assert.Null(enriched.ReviewPercent);

        var unknown = ApiEndpoints.WithLibraryMeta(Card(null, null, null), view);
        Assert.Null(unknown.ReleaseAt);
        Assert.Null(unknown.SortMetacritic);
    }

    private static GameSummaryDto Card(int? appId, int? metacritic, int? reviewPercent) => new(
        Guid.NewGuid(), "slug", "Title", "", appId, "", "", "emerald", "", "", "", "", "de", [], 0, 0, 0, 0, 0, 0, 0, null, null, true,
        null, null, metacritic, null, reviewPercent, null, null, null, DateTimeOffset.UtcNow);
}
