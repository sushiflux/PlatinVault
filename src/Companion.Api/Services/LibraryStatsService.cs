using System.Collections.Concurrent;
using System.Text.Json;
using Companion.Api.Configuration;
using Companion.Api.Endpoints;
using Companion.Api.Settings;
using Microsoft.Extensions.Options;

namespace Companion.Api.Services;

/// <summary>
/// Trophy figures of the whole Steam library (dashboard: unlocked trophies, games at 100 %): one
/// ISteamUserStats/GetPlayerAchievements call per owned game, in the background, cached in DataDir/cache/library-stats.json.
/// After the first full run only games played since their last check are asked again. Steam allows 100,000 Web API
/// calls a day and data only on the user's own request (steamcommunity.com/dev/apiterms) - both hold for the own library.
/// </summary>
public sealed class LibraryStatsService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    // A full re-check of games that were not played meanwhile is not needed often (new DLC achievements, rare)
    private static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(14);
    // A failed call (rate limit, server error) is retried after a pause, not on every dashboard refresh
    private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<int, DateTimeOffset> _failedAt = new();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SecretStore _secretStore;
    private readonly ILogger<LibraryStatsService> _logger;
    private readonly string _cacheFile;
    private readonly ConcurrentDictionary<int, LibraryGameStat> _stats = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private int _scanRunning;
    private int _scanTotal;
    private int _scanDone;

    public LibraryStatsService(
        IHttpClientFactory httpClientFactory,
        SecretStore secretStore,
        IOptions<CompanionOptions> options,
        ILogger<LibraryStatsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _secretStore = secretStore;
        _logger = logger;
        var dir = Path.Combine(options.Value.DataDir, "cache");
        Directory.CreateDirectory(dir);
        _cacheFile = Path.Combine(dir, "library-stats.json");
        Load();
    }

    /// <summary>Trophies of one game as far as known (null = not checked yet or no achievements).</summary>
    public LibraryGameStat? Get(int appId) => _stats.TryGetValue(appId, out var stat) ? stat : null;

    /// <summary>Owned games with every achievement unlocked (the trophy cabinet).</summary>
    public IReadOnlyList<int> PerfectAppIds() =>
        _stats.Where(p => p.Value.Total > 0 && p.Value.Unlocked >= p.Value.Total).Select(p => p.Key).ToList();

    public LibrarySummaryDto Summarize(int libraryGameCount)
    {
        var withAchievements = _stats.Values.Where(s => s.Total > 0).ToList();
        return new LibrarySummaryDto(
            LibraryGameCount: libraryGameCount,
            GamesWithAchievements: withAchievements.Count,
            TrophiesUnlocked: withAchievements.Sum(s => s.Unlocked),
            TrophiesTotal: withAchievements.Sum(s => s.Total),
            PerfectGames: withAchievements.Count(s => s.Unlocked >= s.Total),
            Scanning: Volatile.Read(ref _scanRunning) == 1,
            ScanDone: Volatile.Read(ref _scanDone),
            ScanTotal: Volatile.Read(ref _scanTotal));
    }

    /// <summary>Starts a background run for the games that need a (re)check; does nothing while one is running.</summary>
    public void StartScan(string steamId64, IReadOnlyList<SteamOwnedGameDto> owned)
    {
        var apiKey = _secretStore.GetSecret(SettingsStore.SecretSteam);
        if (string.IsNullOrWhiteSpace(apiKey) || owned.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        var due = owned
            .Where(g => !(_failedAt.TryGetValue(g.AppId, out var failed) && now - failed < RetryFailedAfter))
            .Where(g => !_stats.TryGetValue(g.AppId, out var known) ||
                        (g.LastPlayedAt is { } played && played > known.CheckedAt) ||
                        now - known.CheckedAt > RecheckAfter)
            .ToList();
        if (due.Count == 0 || Interlocked.CompareExchange(ref _scanRunning, 1, 0) != 0) return;

        Volatile.Write(ref _scanTotal, due.Count);
        Volatile.Write(ref _scanDone, 0);
        _ = Task.Run(async () =>
        {
            try
            {
                // Recently played first: their numbers matter most and show up quickly
                var ordered = due.OrderByDescending(g => g.LastPlayedAt ?? DateTimeOffset.MinValue).ToList();
                await Parallel.ForEachAsync(ordered, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (game, ct) =>
                {
                    var stat = await FetchAsync(apiKey, steamId64, game, ct);
                    if (stat is not null)
                    {
                        _stats[game.AppId] = stat;
                        _failedAt.TryRemove(game.AppId, out _);
                    }
                    else
                    {
                        _failedAt[game.AppId] = DateTimeOffset.UtcNow;
                    }
                    if (Interlocked.Increment(ref _scanDone) % 25 == 0) await SaveAsync();
                });
                await SaveAsync();
                _logger.LogInformation("Steam-Bibliothek ausgewertet: {Count} Spiele geprüft.", due.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auswertung der Steam-Bibliothek abgebrochen.");
            }
            finally
            {
                Interlocked.Exchange(ref _scanRunning, 0);
            }
        });
    }

    private async Task<LibraryGameStat?> FetchAsync(string apiKey, string steamId64, SteamOwnedGameDto game, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(SteamClient.HttpClientName);
            var url = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId64)}&appid={game.AppId}";
            using var response = await client.GetAsync(url, ct);
            var now = DateTimeOffset.UtcNow;
            var body = await response.Content.ReadAsStringAsync(ct);

            // Only Steam's explicit "Requested app has no stats" means "no achievements"; any other failure (rate limit,
            // server error, private profile) leaves the game unchecked, so the next run asks again
            if (body.Contains("has no stats", StringComparison.OrdinalIgnoreCase)) return new LibraryGameStat(0, 0, now);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Erfolge von App {AppId}: Steam antwortete {Status}.", game.AppId, (int)response.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("playerstats", out var stats) ||
                !stats.TryGetProperty("success", out var success) || !success.GetBoolean())
            {
                return null;
            }
            // A game with stats but without achievements has no "achievements" list
            if (!stats.TryGetProperty("achievements", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return new LibraryGameStat(0, 0, now);
            }

            var total = 0;
            var unlocked = 0;
            foreach (var achievement in list.EnumerateArray())
            {
                total++;
                if (achievement.TryGetProperty("achieved", out var achieved) && achieved.TryGetInt32(out var flag) && flag == 1) unlocked++;
            }
            return new LibraryGameStat(unlocked, total, now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Network hiccup: leave the game unchecked so the next run tries again
            _logger.LogDebug(ex, "Erfolge von App {AppId} nicht abrufbar.", game.AppId);
            return null;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_cacheFile)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<int, LibraryGameStat>>(File.ReadAllText(_cacheFile), Json);
            if (saved is null) return;
            foreach (var (appId, stat) in saved) _stats[appId] = stat;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Cache der Steam-Bibliothek nicht lesbar, wird neu aufgebaut.");
        }
    }

    private async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(_cacheFile, JsonSerializer.Serialize(_stats.ToDictionary(p => p.Key, p => p.Value), Json));
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Cache der Steam-Bibliothek nicht gespeichert.");
        }
        finally
        {
            _saveLock.Release();
        }
    }
}

/// <summary>Unlocked / total achievements of one owned game and when Steam was asked.</summary>
public sealed record LibraryGameStat(int Unlocked, int Total, DateTimeOffset CheckedAt);
