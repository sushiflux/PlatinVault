using System.Collections.Concurrent;
using System.Text.Json;
using System.Web;
using Companion.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Companion.Api.Services;

/// <summary>
/// Translates English Steam achievement descriptions into German (or the user's preferred language)
/// and caches translations on disk under DataDir/translations/cache.json.
/// </summary>
public sealed class TranslationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TranslationService> _logger;
    private readonly string _cacheFilePath;
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private static readonly string[] GermanIndicators =
    [
        " der ", " die ", " das ", " und ", " oder ", " mit ", " für ", " von ",
        " auf ", " im ", " in ", " den ", " dem ", " des ", " ein ", " eine ",
        " einen ", " einem ", " nicht ", " alle ", " durch ", " über ", " unter ",
        " schließe ", " erreiche ", " sammle ", " besiege ", " töte ", " kaufe ",
        " stelle ", " baue ", " gewinne ", " spiele ", " erhalte ", " schalte ",
        " finde ", " benutze ", " erledige ", " vervollständige ", " entdecke "
    ];

    public TranslationService(IHttpClientFactory httpClientFactory, IOptions<CompanionOptions> options, ILogger<TranslationService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("steam");
        _logger = logger;

        var dataDir = options.Value.DataDir;
        var translationsDir = Path.Combine(dataDir, "translations");
        Directory.CreateDirectory(translationsDir);

        _cacheFilePath = Path.Combine(translationsDir, "cache.json");
        LoadCache();
    }

    public static bool IsLikelyGerman(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        if (text.Contains('ä') || text.Contains('ö') || text.Contains('ü') ||
            text.Contains('Ä') || text.Contains('Ö') || text.Contains('Ü') ||
            text.Contains('ß'))
        {
            return true;
        }

        var padded = " " + text.ToLowerInvariant() + " ";
        return GermanIndicators.Any(ind => padded.Contains(ind));
    }

    // Game-aware AI translations ("Clear chapter 2" = finish it, not delete it) win over the word-by-word translator
    public bool TryGetAiTranslation(string text, string targetLang, out string translated)
    {
        translated = string.Empty;
        return !string.IsNullOrWhiteSpace(text) &&
               _cache.TryGetValue($"ai:{targetLang}:{text.Trim()}", out translated!) &&
               !string.IsNullOrWhiteSpace(translated);
    }

    public void StoreAiTranslations(IReadOnlyDictionary<string, string> translations, string targetLang)
    {
        foreach (var (source, translated) in translations)
        {
            if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(translated))
            {
                _cache[$"ai:{targetLang}:{source.Trim()}"] = translated.Trim();
            }
        }
        _ = SaveCacheAsync();
    }

    public async Task<(string TranslatedText, bool WasTranslated)> TranslateIfNeededAsync(string text, string targetLang = "de", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (text, false);

        if (targetLang == "de" && IsLikelyGerman(text))
            return (text, false);

        var cacheKey = $"{targetLang}:{text.Trim()}";
        if (_cache.TryGetValue(cacheKey, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return (cached, !string.Equals(cached, text, StringComparison.OrdinalIgnoreCase));
        }

        try
        {
            var encoded = HttpUtility.UrlEncode(text);
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetLang}&dt=t&q={encoded}";

            using var response = await _httpClient.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    var sentences = root[0];
                    if (sentences.ValueKind == JsonValueKind.Array)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var sentence in sentences.EnumerateArray())
                        {
                            if (sentence.ValueKind == JsonValueKind.Array && sentence.GetArrayLength() > 0)
                            {
                                var part = sentence[0].GetString();
                                if (!string.IsNullOrEmpty(part))
                                {
                                    sb.Append(part);
                                }
                            }
                        }

                        var result = sb.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(result))
                        {
                            _cache[cacheKey] = result;
                            _ = SaveCacheAsync();
                            var wasTranslated = !string.Equals(result, text.Trim(), StringComparison.OrdinalIgnoreCase);
                            return (result, wasTranslated);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Automatische Übersetzung fehlgeschlagen für: {Text}", text);
        }

        return (text, false);
    }

    private void LoadCache()
    {
        if (!File.Exists(_cacheFilePath)) return;

        try
        {
            var json = File.ReadAllText(_cacheFilePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict is not null)
            {
                foreach (var kvp in dict)
                {
                    _cache[kvp.Key] = kvp.Value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Laden des Übersetzungs-Caches.");
        }
    }

    private async Task SaveCacheAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fehler beim Speichern des Übersetzungs-Caches.");
        }
        finally
        {
            _saveLock.Release();
        }
    }
}

