using System.Text.Json;

namespace Companion.Api.Configuration;

/// <summary>
/// Product name, tagline, company and the public source repository, loaded from branding.json as the single source of truth. The source
/// URL is shown in the licence viewer: PlatinVault is AGPL-3.0-only, and section 13 asks to offer the source to everyone who uses it
/// over a network.
/// </summary>
public sealed record Branding(string ProductName, string Tagline, string Company, string? SourceUrl = null)
{
    public const string DefaultSourceUrl = "https://github.com/sushiflux/PlatinVault";

    /// <summary>The public source code repository (branding.json "sourceUrl", else the default).</summary>
    public string Source => string.IsNullOrWhiteSpace(SourceUrl) ? DefaultSourceUrl : SourceUrl;

    public static Branding Load(string baseDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "branding.json"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "branding.json"))
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                var loaded = JsonSerializer.Deserialize<Branding>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }

        return new Branding("PlatinVault", "Live Steam-Achievements, Platin-Overlay & 100% Master-Guides.", "SushiFlux", DefaultSourceUrl);
    }
}

