using System.Security.Cryptography;
using System.Text.Json;
using PlatinVault.Versioning;

namespace PlatinVault.Desktop.Updates;

/// <summary>A newer installer found on GitHub (the newest beta or release the switch "Beta-Versionen erhalten" allows).</summary>
internal sealed record UpdateOffer(ReleaseVersion Version, string InstallerUrl, string ChecksumUrl, string InstallerName)
{
    /// <summary>The offer for a published release; null when it has no Windows installer.</summary>
    public static UpdateOffer? From(PublishedRelease? release) =>
        release?.Installer is { } installer ? new UpdateOffer(release.Version, installer.Url, installer.ChecksumUrl, installer.Name) : null;
}

/// <summary>
/// Downloads and verifies the installer of an update. WHICH update is offered is decided by the server's update check
/// (<c>Companion.Api.Services.UpdateService</c>, the same for every host) with the shared rules in <see cref="ReleaseFeed"/>: the
/// releases of the public repository sushiflux/PlatinVault, betas only while "Beta-Versionen erhalten" is switched on, never older than
/// what runs. The installer is only run after its SHA-256 matches the published ".sha256" file.
/// </summary>
internal sealed class UpdateChecker(HttpClient http)
{
    /// <summary>The newest acceptable release with an installer and its checksum (pure, unit-tested; same rules as the server).</summary>
    public static UpdateOffer? Pick(JsonElement releases, ReleaseVersion current, bool includeBetas) =>
        UpdateOffer.From(ReleaseFeed.PickUpdate(ReleaseFeed.Parse(releases), current, includeBetas, requireInstaller: true));

    /// <summary>Downloads the installer into <paramref name="folder"/>; returns its path only when the SHA-256 matches.</summary>
    public async Task<string?> DownloadAsync(UpdateOffer offer, string folder, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var expected = (await http.GetStringAsync(offer.ChecksumUrl, ct)).Trim().Split([' ', '\t', '*'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        var target = Path.Combine(folder, Path.GetFileName(offer.InstallerName));
        var partial = target + ".part";
        await using (var source = await http.GetStreamAsync(offer.InstallerUrl, ct))
        await using (var file = File.Create(partial))
        {
            await source.CopyToAsync(file, ct);
        }
        string actual;
        await using (var check = File.OpenRead(partial))
        {
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, ct));
        }
        if (expected is null || expected.Length != 64 || !expected.All(Uri.IsHexDigit) || !string.Equals(expected, actual, StringComparison.Ordinal))
        {
            File.Delete(partial);
            return null;
        }
        File.Move(partial, target, overwrite: true);
        return target;
    }
}
