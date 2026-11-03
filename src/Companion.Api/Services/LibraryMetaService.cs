using System.Collections.Concurrent;
using System.Text.Json;
using Companion.Api.Configuration;
using Companion.Api.Endpoints;
using Microsoft.Extensions.Options;

namespace Companion.Api.Services;

/// <summary>One app's values for the dashboard sort orders, merged from the best source that has them (see <see cref="LibraryMetaService"/>).</summary>
/// <param name="SizeBytes">Real size on disk of the installed game.</param>
/// <param name="InstallSizeBytes">Estimated install size from Steam's depot data (known for installed and not installed games).</param>
public sealed record LibraryMeta(long? ReleaseUnix, int? Metacritic, int? ReviewPercent, int? ReviewScore, long? SizeBytes, long? InstallSizeBytes = null)
{
    /// <summary>The release date as a point in time (null when unknown).</summary>
    public DateTimeOffset? ReleaseAt => ReleaseUnix is > 0 ? DateTimeOffset.FromUnixTimeSeconds(ReleaseUnix.Value) : null;
}

/// <summary>What Steam's store answered for one app (the fallback source, used where no local Steam client can tell).</summary>
public sealed record StoreLibraryMeta(long? ReleaseUnix, int? ReviewPercent, int? ReviewScore, DateTimeOffset CheckedAt);

/// <summary>
/// Release date, Metacritic score, review rating and size on disk for every app of the Steam library, for the dashboard's sort orders.
/// Three sources, strongest first, merged per app and field:
/// <list type="number">
/// <item>This PC's own Steam client files (<see cref="SteamLocalLibraryService"/>) - when the server runs on the gaming PC (Windows program).</item>
/// <item>The same files read by the bridge on the gaming PC and pushed to <c>POST /api/bridge/library</c> - for a server on Unraid.
/// Kept in memory and in <c>cache/bridge-library.json</c>, replaced wholesale by every push.</item>
/// <item>Steam's store, in the background (<see cref="StartScan"/>): <c>IStoreBrowseService/GetItems</c> answers release date and review
/// summary for up to 50 apps per call, so a library of 700 games costs 14 calls. Only apps no local source knows are asked; the
/// answer is cached in <c>cache/library-meta.json</c> for 14 days. Metacritic and size on disk are not part of that answer, so
/// without a local source they stay unknown (shown at the end of that order).</item>
/// </list>
/// Steam documents no rate limit for the store endpoints, so the scan is deliberately slow and gentle: one call at a time, a pause
/// of <see cref="DefaultPause"/> between calls, a failed call ends the run and the apps are retried only after an hour. All values are
/// numbers, so unlike Steam's texts the cache needs no language in its key.
/// </summary>
public sealed class LibraryMetaService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(14);
    private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromHours(1);

    /// <summary>Pause between two store calls of the background scan.</summary>
    public static readonly TimeSpan DefaultPause = TimeSpan.FromMilliseconds(1500);

    /// <summary>Apps per store call (the same chunk size the cover lookup uses).</summary>
    public const int ChunkSize = 50;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<Dictionary<int, LocalLibraryEntry>?> _readLocal;
    private readonly ILogger<LibraryMetaService> _logger;
    private readonly TimeProvider _time;
    private readonly TimeSpan _pause;
    private readonly string _storeFile;
    private readonly string _bridgeFile;
    private readonly ConcurrentDictionary<int, StoreLibraryMeta> _store = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> _failedAt = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private Dictionary<int, LocalLibraryEntry> _bridge = [];
    private DateTimeOffset? _bridgeAt;
    private int _scanRunning;
    private int _scanTotal;
    private int _scanDone;

    public LibraryMetaService(
        IHttpClientFactory httpClientFactory,
        SteamLocalLibraryService localLibrary,
        IOptions<CompanionOptions> options,
        ILogger<LibraryMetaService> logger)
        : this(httpClientFactory, localLibrary.Read, Path.Combine(options.Value.DataDir, "cache"), logger, TimeProvider.System, DefaultPause)
    {
    }

    /// <summary>For tests: the local source, the cache folder, the clock and the pause are replaceable.</summary>
    internal LibraryMetaService(
        IHttpClientFactory httpClientFactory,
        Func<Dictionary<int, LocalLibraryEntry>?> readLocal,
        string cacheDir,
        ILogger<LibraryMetaService> logger,
        TimeProvider time,
        TimeSpan pause)
    {
        _httpClientFactory = httpClientFactory;
        _readLocal = readLocal;
        _logger = logger;
        _time = time;
        _pause = pause;
        Directory.CreateDirectory(cacheDir);
        _storeFile = Path.Combine(cacheDir, "library-meta.json");
        _bridgeFile = Path.Combine(cacheDir, "bridge-library.json");
        Load();
    }

    /// <summary>Whether the background store scan is running, and how far it is.</summary>
    public (bool Scanning, int Done, int Total) ScanState =>
        (Volatile.Read(ref _scanRunning) == 1, Volatile.Read(ref _scanDone), Volatile.Read(ref _scanTotal));

    /// <summary>When the bridge last delivered its library (null = never since the cache file was written).</summary>
    public DateTimeOffset? BridgeDeliveredAt => _bridgeAt;

    /// <summary>
    /// A consistent view for one request: the local files are read once, so every game of a list is merged from the same state.
    /// </summary>
    public LibraryMetaView Snapshot() => new(_readLocal(), _bridge, _store);

    /// <summary>Replaces what the bridge delivered (the whole library of the gaming PC) and keeps it across a restart.</summary>
    public async Task IngestBridgeAsync(IReadOnlyDictionary<int, LocalLibraryEntry> entries, CancellationToken ct)
    {
        _bridge = new Dictionary<int, LocalLibraryEntry>(entries);
        _bridgeAt = _time.GetUtcNow();
        await _saveLock.WaitAsync(ct);
        try
        {
            await File.WriteAllTextAsync(_bridgeFile, JsonSerializer.Serialize(_bridge, Json), ct);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Bibliotheksdaten der Bridge nicht gespeichert.");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Starts the background store scan for the owned apps no local source knows; does nothing while one is running or when
    /// everything is known. The returned task finishes with the run (tests await it; the endpoints ignore it).
    /// </summary>
    public Task StartScan(IReadOnlyList<SteamOwnedGameDto> owned)
    {
        var now = _time.GetUtcNow();
        var view = Snapshot();
        var due = owned
            .Where(g => !view.KnowsLocally(g.AppId))
            .Where(g => !(_failedAt.TryGetValue(g.AppId, out var failed) && now - failed < RetryFailedAfter))
            .Where(g => !_store.TryGetValue(g.AppId, out var known) || now - known.CheckedAt > RecheckAfter)
            // Recently played first: those are the ones that get looked at
            .OrderByDescending(g => g.LastPlayedAt ?? DateTimeOffset.MinValue)
            .Select(g => g.AppId)
            .ToList();
        if (due.Count == 0 || Interlocked.CompareExchange(ref _scanRunning, 1, 0) != 0) return Task.CompletedTask;

        Volatile.Write(ref _scanTotal, due.Count);
        Volatile.Write(ref _scanDone, 0);
        return Task.Run(async () =>
        {
            try
            {
                var first = true;
                foreach (var chunk in due.Chunk(ChunkSize))
                {
                    if (!first) await Task.Delay(_pause, _time);
                    first = false;
                    var answer = await FetchChunkAsync(chunk, CancellationToken.None);
                    if (answer is null)
                    {
                        // Steam refused or was not reachable: no further calls now; this and the remaining apps wait for the retry time
                        foreach (var id in due.Skip(Volatile.Read(ref _scanDone))) _failedAt[id] = _time.GetUtcNow();
                        break;
                    }
                    foreach (var (id, meta) in answer) _store[id] = meta;
                    Interlocked.Add(ref _scanDone, chunk.Length);
                    await SaveStoreAsync();
                }
                _logger.LogInformation("Bibliotheksdaten aus dem Steam-Store: {Count} Spiele geprüft.", Volatile.Read(ref _scanDone));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Abruf der Bibliotheksdaten aus dem Steam-Store abgebrochen.");
            }
            finally
            {
                Interlocked.Exchange(ref _scanRunning, 0);
            }
        });
    }

    /// <summary>One store call for up to <see cref="ChunkSize"/> apps; null when the call failed (the whole chunk is then not cached).</summary>
    private async Task<Dictionary<int, StoreLibraryMeta>?> FetchChunkAsync(int[] appIds, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(SteamClient.HttpClientName);
            var ids = string.Join(",", appIds.Select(id => $"{{\"appid\":{id}}}"));
            var input = $"{{\"ids\":[{ids}],\"context\":{{\"language\":\"english\",\"country_code\":\"DE\"}},\"data_request\":{{\"include_release\":true,\"include_reviews\":true}}}}";
            using var response = await client.GetAsync($"https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json={Uri.EscapeDataString(input)}", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Store-Daten der Bibliothek: Steam antwortete {Status}.", (int)response.StatusCode);
                return null;
            }
            return ParseItems(await response.Content.ReadAsStringAsync(ct), appIds, _time.GetUtcNow());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogDebug(ex, "Store-Daten der Bibliothek nicht abrufbar.");
            return null;
        }
    }

    /// <summary>
    /// Reads a <c>GetItems</c> answer. Every asked app gets an entry (an app the store does not list gets empty values, so it is not asked
    /// again for 14 days). Reviews count only when Steam rates the game (review_score above 0 = at least 10 reviews): one lucky review
    /// must not put a game at the top of the review order.
    /// </summary>
    public static Dictionary<int, StoreLibraryMeta> ParseItems(string body, IEnumerable<int> asked, DateTimeOffset now)
    {
        var result = asked.ToDictionary(id => id, _ => new StoreLibraryMeta(null, null, null, now));
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("response", out var response) ||
            !response.TryGetProperty("store_items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("appid", out var idElement) || !idElement.TryGetInt32(out var appId) || !result.ContainsKey(appId)) continue;

            long? release = null;
            if (item.TryGetProperty("release", out var rel) && rel.ValueKind == JsonValueKind.Object)
            {
                release = PositiveLong(rel, "steam_release_date") ?? PositiveLong(rel, "original_release_date");
            }

            int? percent = null, score = null;
            if (item.TryGetProperty("reviews", out var reviews) && reviews.TryGetProperty("summary_filtered", out var summary) &&
                summary.ValueKind == JsonValueKind.Object &&
                summary.TryGetProperty("review_score", out var scoreElement) && scoreElement.TryGetInt32(out var s) && s > 0 &&
                summary.TryGetProperty("percent_positive", out var percentElement) && percentElement.TryGetInt32(out var p))
            {
                percent = p;
                score = s;
            }
            result[appId] = new StoreLibraryMeta(release, percent, score, now);
        }
        return result;
    }

    private static long? PositiveLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) && number > 0 ? number : null;

    private void Load()
    {
        try
        {
            if (File.Exists(_storeFile) && JsonSerializer.Deserialize<Dictionary<int, StoreLibraryMeta>>(File.ReadAllText(_storeFile), Json) is { } store)
            {
                foreach (var (id, meta) in store) _store[id] = meta;
            }
            if (File.Exists(_bridgeFile) && JsonSerializer.Deserialize<Dictionary<int, LocalLibraryEntry>>(File.ReadAllText(_bridgeFile), Json) is { } bridge)
            {
                _bridge = bridge;
                _bridgeAt = File.GetLastWriteTimeUtc(_bridgeFile);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Cache der Bibliotheksdaten nicht lesbar, wird neu aufgebaut.");
        }
    }

    private async Task SaveStoreAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(_storeFile, JsonSerializer.Serialize(_store.ToDictionary(p => p.Key, p => p.Value), Json));
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Cache der Bibliotheksdaten nicht gespeichert.");
        }
        finally
        {
            _saveLock.Release();
        }
    }
}

/// <summary>The merged values of one request (see <see cref="LibraryMetaService.Snapshot"/>).</summary>
public sealed class LibraryMetaView(
    IReadOnlyDictionary<int, LocalLibraryEntry>? local,
    IReadOnlyDictionary<int, LocalLibraryEntry> bridge,
    IReadOnlyDictionary<int, StoreLibraryMeta> store)
{
    /// <summary>True when a local source (this PC's Steam or the bridge) has release/rating data for the app, so the store need not be asked.</summary>
    public bool KnowsLocally(int appId) =>
        (local is not null && local.TryGetValue(appId, out var l) && HasFacts(l)) ||
        (bridge.TryGetValue(appId, out var b) && HasFacts(b));

    private static bool HasFacts(LocalLibraryEntry entry) => entry.ReleaseUnix is not null || entry.ReviewPercent is not null || entry.Metacritic is not null;

    /// <summary>The merged values of an app; null when no source knows anything about it.</summary>
    public LibraryMeta? Get(int appId)
    {
        LocalLibraryEntry? own = null;
        if (local is not null) local.TryGetValue(appId, out own);
        bridge.TryGetValue(appId, out var remote);
        store.TryGetValue(appId, out var shop);

        // Per field the first source that has a value wins; size on disk exists only locally
        var release = own?.ReleaseUnix ?? remote?.ReleaseUnix ?? shop?.ReleaseUnix;
        var metacritic = own?.Metacritic ?? remote?.Metacritic;
        var percent = own?.ReviewPercent ?? remote?.ReviewPercent ?? shop?.ReviewPercent;
        var score = own?.ReviewScore ?? remote?.ReviewScore ?? shop?.ReviewScore;
        var size = own?.SizeBytes ?? remote?.SizeBytes;
        var installSize = own?.InstallSizeBytes ?? remote?.InstallSizeBytes;
        return release is null && metacritic is null && percent is null && size is null && installSize is null
            ? null
            : new LibraryMeta(release, metacritic, percent, score, size, installSize);
    }
}
