using System.Text.Json;

namespace Companion.Api.Configuration;

/// <summary>One language of the app as the server needs it: the code of the setting ui.language and Steam's language name for achievement texts.</summary>
public sealed record UiLanguageInfo(string Code, string SteamLanguage);

/// <summary>
/// The languages of the app, read from languages.json in the repository root - the same file the frontend builds its language menu,
/// first-run screen, formats and flags from. Adding a language is one entry there (plus the text files of the frontend); this class
/// then validates and maps it without a code change. Without the file (should not happen: it is copied next to the program) the
/// built-in German and English entries are used.
/// </summary>
public static class LanguageRegistry
{
    private static readonly UiLanguageInfo[] BuiltIn = [new("de", "german"), new("en", "english")];

    private static readonly Lazy<IReadOnlyList<UiLanguageInfo>> Loaded = new(() => Load(AppContext.BaseDirectory));

    public static IReadOnlyList<UiLanguageInfo> All => Loaded.Value;

    public static IReadOnlyList<string> Codes => All.Select(language => language.Code).ToArray();

    /// <summary>The known language for a code ("de", " EN " -> the entry), or null.</summary>
    public static UiLanguageInfo? Find(string? code)
    {
        var normalized = code?.Trim().ToLowerInvariant();
        return normalized is null ? null : All.FirstOrDefault(language => language.Code == normalized);
    }

    /// <summary>Steam's language name for the achievement texts of an app language; German for an unknown code.</summary>
    public static string SteamLanguageFor(string code) => Find(code)?.SteamLanguage ?? BuiltIn[0].SteamLanguage;

    internal static IReadOnlyList<UiLanguageInfo> Load(string baseDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "languages.json"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "languages.json"))
        };

        foreach (var path in candidates.Where(File.Exists))
        {
            try
            {
                using var stream = File.OpenRead(path);
                var file = JsonSerializer.Deserialize<LanguageFile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var entries = file?.Languages?
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Code) && !string.IsNullOrWhiteSpace(entry.SteamLanguage))
                    .Select(entry => new UiLanguageInfo(entry.Code!.Trim().ToLowerInvariant(), entry.SteamLanguage!.Trim()))
                    .ToArray();
                if (entries is { Length: > 0 })
                {
                    return entries;
                }
            }
            catch (JsonException)
            {
                // A broken file is treated like a missing one: the built-in languages keep the app working
            }
        }

        return BuiltIn;
    }

    private sealed record LanguageFile(List<LanguageEntry>? Languages);

    private sealed record LanguageEntry(string? Code, string? SteamLanguage);
}
