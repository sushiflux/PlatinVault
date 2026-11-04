using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Companion.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Companion.Api.Services;

/// <summary>
/// The worldwide unlock shares of a game's achievements (Steam: ISteamUserStats/GetGlobalAchievementPercentagesForApp, no key needed).
/// They decide Gold/Silver/Bronze and the rarity band, so a lost or empty answer must never silently turn every achievement into Silver.
/// <list type="bullet">
/// <item>The last good values per AppID are persisted in <c>DataDir/cache/achievement-percentages.json</c> (written atomically: temp file,
/// then replace), so they survive a restart and an outage - but only for games in the collection (the caller says so). Any other AppID
/// (a lookup through <c>/api/live/achievements?appId=</c>) is kept in memory only, and at most <see cref="MaxTransientEntries"/> of them
/// (the least recently used goes first), so arbitrary requests can grow neither the memory nor the file. The file is rewritten only when
/// the values of a persisted game changed or its stored copy is older than <see cref="RewriteUnchangedAfter"/>.</item>
/// <item>Fetched on the first use per process and then when the stored values are older than <see cref="RefreshAfter"/>.</item>
/// <item>A failure never touches stored values and is never cached as data. The next attempt for that AppID comes no earlier than
/// <see cref="FirstRetryDelay"/> later, doubling per failure up to <see cref="MaxRetryDelay"/>; each failed attempt is logged once as a
/// warning with the technical reason.</item>
/// </list>
/// </summary>
public sealed partial class AchievementPercentageService
{
    public const string StateOk = "ok";
    public const string StateStale = "stale";
    public const string StateUnavailable = "unavailable";

    public static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(1);
    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(30);

    // A blocked or hanging connection must not hold a request for the HttpClient's whole timeout
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private const string Host = "api.steampowered.com";
    private const int MaxDetailLength = 600;

    /// <summary>How many games outside the collection are kept in memory at most (least recently used are dropped first).</summary>
    public const int MaxTransientEntries = 500;

    /// <summary>Unchanged values of a persisted game are written again only when the stored copy is older than this (keeps its date useful).</summary>
    public static readonly TimeSpan RewriteUnchangedAfter = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AchievementPercentageService> _logger;
    private readonly TimeProvider _clock;
    private readonly string _cacheFile;
    private readonly ConcurrentDictionary<uint, Entry> _entries = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private long _useCounter;

    public AchievementPercentageService(
        IHttpClientFactory httpClientFactory,
        IOptions<CompanionOptions> options,
        ILogger<AchievementPercentageService> logger,
        TimeProvider? clock = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        var dir = Path.Combine(options.Value.DataDir, "cache");
        Directory.CreateDirectory(dir);
        _cacheFile = Path.Combine(dir, "achievement-percentages.json");
        Load();
    }

    /// <summary>
    /// The shares of a game, refreshed first when they are due (see the rules above). Waits for a refresh that is due; concurrent callers
    /// for one AppID share a single request. Never throws except when <paramref name="ct"/> is cancelled.
    /// <paramref name="inCollection"/> = the game is in the collection (games.SteamAppId): only then its values go to the file.
    /// </summary>
    public async Task<PercentageSnapshot> GetAsync(uint appId, bool inCollection, CancellationToken ct = default)
    {
        var entry = _entries.GetOrAdd(appId, _ => new Entry());
        lock (entry)
        {
            // Once in the collection it stays persisted until the worker says otherwise (RetainCollection)
            if (inCollection) entry.InCollection = true;
            entry.LastUsed = Interlocked.Increment(ref _useCounter);
        }
        if (!inCollection) TrimTransient(appId);
        var attempted = false;
        if (IsDue(entry))
        {
            await entry.Gate.WaitAsync(ct);
            try
            {
                // Another caller may have refreshed while this one waited
                if (IsDue(entry))
                {
                    attempted = true;
                    await RefreshAsync(appId, entry, ct);
                }
            }
            finally
            {
                entry.Gate.Release();
            }
        }
        return Snapshot(entry, attempted);
    }

    /// <summary>What is stored for a game right now, without any network access.</summary>
    public PercentageSnapshot Peek(uint appId) =>
        _entries.TryGetValue(appId, out var entry) ? Snapshot(entry, false) : new PercentageSnapshot(Empty(), StateUnavailable, null, null, false);

    /// <summary>How many games are held in memory right now (persisted and transient).</summary>
    public int EntryCount => _entries.Count;

    /// <summary>
    /// The current collection (from the worker): games that left it are no longer written to the file (they are dropped from it with the
    /// next save, and count as transient from now on); games of the collection are marked persisted.
    /// </summary>
    public async Task RetainCollectionAsync(IReadOnlyCollection<uint> appIds)
    {
        var keep = appIds as ISet<uint> ?? appIds.ToHashSet();
        var removed = false;
        foreach (var (appId, entry) in _entries)
        {
            lock (entry)
            {
                var now = keep.Contains(appId);
                if (entry.InCollection && !now && entry.SavedAt is not null) removed = true;
                entry.InCollection = now;
                if (!now) entry.SavedAt = null;
            }
        }
        if (removed) await SaveAsync();
        TrimTransient(0);
    }

    /// <summary>Drops the least recently used games outside the collection while there are more than <see cref="MaxTransientEntries"/>.</summary>
    private void TrimTransient(uint justUsed)
    {
        var transient = _entries.Where(e => !e.Value.InCollection).ToList();
        var excess = transient.Count - MaxTransientEntries;
        if (excess <= 0) return;
        foreach (var (appId, _) in transient.Where(e => e.Key != justUsed).OrderBy(e => e.Value.LastUsed).Take(excess))
        {
            // A refresh in flight for a dropped game only fills an orphaned entry: harmless
            _entries.TryRemove(appId, out _);
        }
    }

    private bool IsDue(Entry entry)
    {
        var now = _clock.GetUtcNow();
        lock (entry)
        {
            if (now < entry.NextAttemptAt) return false;
            return entry.Failures > 0 || !entry.FetchedThisProcess || entry.Values is null || entry.FetchedAt is not { } at || now - at >= RefreshAfter;
        }
    }

    private PercentageSnapshot Snapshot(Entry entry, bool attempted)
    {
        lock (entry)
        {
            var state = entry.Values is null ? StateUnavailable : entry.Failures > 0 ? StateStale : StateOk;
            return new PercentageSnapshot(entry.Values ?? Empty(), state, entry.Values is null ? null : entry.FetchedAt, state == StateOk ? null : entry.LastDetail, attempted);
        }
    }

    private async Task RefreshAsync(uint appId, Entry entry, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            var values = await DownloadAsync(appId, timeout.Token);
            var now = _clock.GetUtcNow();
            bool save;
            lock (entry)
            {
                // Only games of the collection go to the file, and only when something changed or the stored copy got old
                save = entry.InCollection &&
                       (entry.SavedAt is not { } savedAt || now - savedAt >= RewriteUnchangedAfter || !SameValues(entry.Values, values));
                entry.Values = values;
                entry.FetchedAt = now;
                entry.FetchedThisProcess = true;
                entry.Failures = 0;
                entry.NextAttemptAt = DateTimeOffset.MinValue;
                entry.LastDetail = null;
            }
            if (save) await SaveAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var timedOut = timeout.IsCancellationRequested && !ct.IsCancellationRequested;
            var detail = timedOut ? $"Timeout: no answer from {Host} within {(int)RequestTimeout.TotalSeconds} s" : Describe(ex);
            var now = _clock.GetUtcNow();
            TimeSpan delay;
            DateTimeOffset? storedAt;
            lock (entry)
            {
                entry.Failures++;
                delay = RetryDelay(entry.Failures);
                entry.NextAttemptAt = now + delay;
                entry.LastDetail = detail;
                storedAt = entry.Values is null ? null : entry.FetchedAt;
            }
            _logger.LogWarning(
                "Weltweite Seltenheitswerte für App {AppId} konnten nicht von Steam geladen werden ({Detail}). {Stored} Nächster Versuch frühestens in {Minutes} min.",
                appId, detail,
                storedAt is { } at ? $"Die gespeicherten Werte vom {at.UtcDateTime:yyyy-MM-dd HH:mm} UTC bleiben in Gebrauch." : "Es gibt keine gespeicherten Werte, die Stufen sind vorläufig.",
                (int)delay.TotalMinutes);
        }
    }

    /// <summary>2, 4, 8, 16, then 30 minutes for the 1st, 2nd, ... failure in a row.</summary>
    internal static TimeSpan RetryDelay(int failures)
    {
        var exponent = Math.Clamp(failures - 1, 0, 8);
        var delay = TimeSpan.FromTicks(FirstRetryDelay.Ticks << exponent);
        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }

    private async Task<Dictionary<string, double>> DownloadAsync(uint appId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(SteamClient.HttpClientName);
        var url = $"https://{Host}/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v0002/?gameid={appId}&format=json";
        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Steam answered HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) from {Host}", null, response.StatusCode);
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("achievementpercentages", out var container) ||
            !container.TryGetProperty("achievements", out var list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Unexpected answer from {Host}: no list \"achievementpercentages.achievements\"");
        }

        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            if (string.IsNullOrEmpty(name)) continue;

            double percent = 0;
            if (item.TryGetProperty("percent", out var value))
            {
                if (value.ValueKind == JsonValueKind.Number)
                    percent = value.GetDouble();
                else if (value.ValueKind == JsonValueKind.String)
                    _ = double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out percent);
            }
            if (double.IsFinite(percent))
            {
                map[name] = Math.Clamp(percent, 0.0, 100.0);
            }
        }
        return map;
    }

    /// <summary>
    /// The technical reason of a failure for the log and the status shown to the user: exception types and messages down the inner chain
    /// (for a blocked connection that includes the socket error and the host), URLs without their query string. No secrets are involved:
    /// this request carries no key.
    /// </summary>
    internal static string Describe(Exception ex)
    {
        var text = new StringBuilder();
        text.Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is SocketException socket)
            {
                text.Append(" [SocketException ").Append(socket.SocketErrorCode).Append(' ').Append(socket.NativeErrorCode).Append(']');
            }
            else if (!text.ToString().Contains(inner.Message, StringComparison.Ordinal))
            {
                text.Append(" -> ").Append(inner.GetType().Name).Append(": ").Append(inner.Message);
            }
        }
        var clean = QueryString().Replace(text.ToString(), "$1");
        return clean.Length > MaxDetailLength ? clean[..MaxDetailLength] + "…" : clean;
    }

    [GeneratedRegex(@"(https?://[^\s?""'<>]+)\?[^\s""'<>)]*")]
    private static partial Regex QueryString();

    private static Dictionary<string, double> Empty() => new(StringComparer.OrdinalIgnoreCase);

    private static bool SameValues(Dictionary<string, double>? a, Dictionary<string, double> b) =>
        a is not null && a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v.Equals(kv.Value));

    // ---- Persistence --------------------------------------------------------------------------------------------------------

    private sealed class PersistedFile
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, PersistedApp> Apps { get; set; } = [];
    }

    private sealed class PersistedApp
    {
        public DateTimeOffset FetchedAt { get; set; }
        public Dictionary<string, double> Values { get; set; } = [];
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_cacheFile)) return;
            var file = JsonSerializer.Deserialize<PersistedFile>(File.ReadAllText(_cacheFile), Json);
            if (file?.Apps is null) return;
            foreach (var (key, app) in file.Apps)
            {
                if (!uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) || app?.Values is null) continue;
                var values = Empty();
                foreach (var (name, percent) in app.Values)
                {
                    if (!string.IsNullOrEmpty(name) && double.IsFinite(percent)) values[name] = Math.Clamp(percent, 0.0, 100.0);
                }
                // Only games of the collection were written, so they count as such until the worker's first run says otherwise
                _entries[appId] = new Entry { Values = values, FetchedAt = app.FetchedAt, SavedAt = app.FetchedAt, InCollection = true };
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // The file stays as it is; the next successful fetch replaces it
            _logger.LogWarning(ex, "Gespeicherte Seltenheitswerte nicht lesbar, sie werden neu von Steam geladen.");
        }
    }

    private async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        string? temp = null;
        try
        {
            var file = new PersistedFile();
            var written = new List<(Entry Entry, DateTimeOffset At)>();
            foreach (var (appId, entry) in _entries)
            {
                lock (entry)
                {
                    if (entry.InCollection && entry.Values is not null && entry.FetchedAt is { } at)
                    {
                        file.Apps[appId.ToString(CultureInfo.InvariantCulture)] = new PersistedApp { FetchedAt = at, Values = new Dictionary<string, double>(entry.Values) };
                        written.Add((entry, at));
                    }
                }
            }

            // Temp file in the same folder, then replace: a crash in the middle never leaves a half-written cache behind
            temp = Path.Combine(Path.GetDirectoryName(_cacheFile)!, $".achievement-percentages-{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, file, Json);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(_cacheFile))
                File.Replace(temp, _cacheFile, null);
            else
                File.Move(temp, _cacheFile);
            temp = null;
            foreach (var (entry, at) in written)
            {
                lock (entry) entry.SavedAt = at;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Seltenheitswerte konnten nicht gespeichert werden; sie bleiben bis zum Neustart im Speicher.");
        }
        finally
        {
            if (temp is not null)
            {
                try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            _saveLock.Release();
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);

        // Guarded by lock (entry). Values null = nothing ever fetched or loaded for this game.
        public Dictionary<string, double>? Values;
        public DateTimeOffset? FetchedAt;
        public bool FetchedThisProcess;
        public int Failures;
        public DateTimeOffset NextAttemptAt = DateTimeOffset.MinValue;
        public string? LastDetail;
        // True = a game of the collection: its values go to the file. SavedAt = FetchedAt of the copy in the file (null = not in it).
        public bool InCollection;
        public DateTimeOffset? SavedAt;
        // Order of the last use, for dropping the least recently used transient games
        public long LastUsed;
    }
}

/// <summary>
/// State "ok" (values from a successful fetch, last attempt not failed), "stale" (stored values in use because the last refresh failed;
/// FetchedAt = when they were fetched) or "unavailable" (no values at all). Detail = the technical reason of the last failure.
/// Attempted = this call asked Steam.
/// </summary>
public sealed record PercentageSnapshot(Dictionary<string, double> Values, string State, DateTimeOffset? FetchedAt, string? Detail, bool Attempted);
