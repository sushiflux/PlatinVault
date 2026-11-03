using System.Security.Cryptography;
using Companion.Api.Configuration;
using Companion.Api.Localization;
using Microsoft.Extensions.Options;

namespace Companion.Api.Services;

/// <summary>Which bridge a download or a self-update is about. The Windows bridge is the default everywhere (older bridges never say).</summary>
public enum BridgePlatform
{
    Windows,
    Linux,
}

/// <summary>
/// The realtime bridges that ship with the server: PlatinVault.Bridge.exe ("PlatinVault Bridge" for Windows) in <c>bridge/</c> and the
/// Linux bridge <c>platinvault-bridge</c> in <c>bridge-linux/</c>, each with its version (bridge-version.txt) and the templates in package/
/// (autostart scripts, install.sh, readme). The server offers them for download and tells a running bridge which version and SHA-256 it
/// should have, so the bridge can update itself. The two bridges have their own versions.
/// </summary>
public sealed class BridgeBundle
{
    public const string ExeName = "PlatinVault.Bridge.exe";
    public const string LinuxBinaryName = "platinvault-bridge";
    public const string VersionFileName = "bridge-version.txt";
    public const string PackageDirName = "package";

    private readonly Lock _hashLock = new();
    private readonly Dictionary<BridgePlatform, (DateTime LastWrite, long Length, string Sha256)> _cachedHashes = [];

    public BridgeBundle(IOptions<CompanionOptions> options, IWebHostEnvironment environment)
    {
        BundleDirectory = ResolveDirectory(options.Value.BridgeDir, environment, "bridge");
        LinuxBundleDirectory = ResolveDirectory(options.Value.BridgeLinuxDir, environment, "bridge-linux");
        IsDevelopment = environment.IsDevelopment();
    }

    /// <summary>Folder the Windows bridge is read from (may not exist).</summary>
    public string BundleDirectory { get; }

    /// <summary>Folder the Linux bridge is read from (may not exist).</summary>
    public string LinuxBundleDirectory { get; }

    public bool IsDevelopment { get; }

    public string ExePath => PathOf(BridgePlatform.Windows);

    public bool IsAvailable => IsAvailableFor(BridgePlatform.Windows);

    /// <summary>Version of the bundled Windows bridge ("1.0.3") or null when the bundle has no version file.</summary>
    public string? Version => VersionOf(BridgePlatform.Windows);

    /// <summary>Lowercase hex SHA-256 of the Windows exe, or null when there is none.</summary>
    public string? Sha256 => Sha256Of(BridgePlatform.Windows);

    /// <summary>Hint for the "no bundled bridge" error (language of the reader); in Development it names the build script.</summary>
    public Msg MissingHint => MissingHintFor(BridgePlatform.Windows);

    /// <summary>The platform asked for in a request ("linux"); anything else, including nothing, is the Windows bridge.</summary>
    public static BridgePlatform ParsePlatform(string? value) =>
        string.Equals(value?.Trim(), "linux", StringComparison.OrdinalIgnoreCase) ? BridgePlatform.Linux : BridgePlatform.Windows;

    public string DirectoryOf(BridgePlatform platform) => platform == BridgePlatform.Linux ? LinuxBundleDirectory : BundleDirectory;

    public string FileNameOf(BridgePlatform platform) => platform == BridgePlatform.Linux ? LinuxBinaryName : ExeName;

    public string PathOf(BridgePlatform platform) => Path.Combine(DirectoryOf(platform), FileNameOf(platform));

    public bool IsAvailableFor(BridgePlatform platform) => File.Exists(PathOf(platform));

    public Msg MissingHintFor(BridgePlatform platform) => (platform, IsDevelopment) switch
    {
        (BridgePlatform.Linux, true) => ServerTexts.Bridge.MissingHintLinuxDevelopment,
        (BridgePlatform.Linux, false) => ServerTexts.Bridge.MissingHintLinux,
        (_, true) => ServerTexts.Bridge.MissingHintDevelopment,
        _ => ServerTexts.Bridge.MissingHint,
    };

    /// <summary>Version of the bundled bridge ("1.0.3") or null when the bundle has no version file.</summary>
    public string? VersionOf(BridgePlatform platform)
    {
        var path = Path.Combine(DirectoryOf(platform), VersionFileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var line = File.ReadAllText(path).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(line) ? null : line;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Lowercase hex SHA-256 of the bridge file, or null when there is none. Computed once and cached; computed again when the
    /// file's last-write time or length changes (e.g. after scripts/build-bridge.ps1 ran).
    /// </summary>
    public string? Sha256Of(BridgePlatform platform)
    {
        var path = PathOf(platform);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return null;
        }

        lock (_hashLock)
        {
            if (_cachedHashes.TryGetValue(platform, out var cached) && cached.LastWrite == info.LastWriteTimeUtc && cached.Length == info.Length)
            {
                return cached.Sha256;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var hash = Convert.ToHexStringLower(SHA256.HashData(stream));

                // A file that changed while it was read (build in progress) must not be cached under the old stamp
                var after = new FileInfo(path);
                if (after.Exists && after.LastWriteTimeUtc == info.LastWriteTimeUtc && after.Length == info.Length)
                {
                    _cachedHashes[platform] = (info.LastWriteTimeUtc, info.Length, hash);
                }
                return hash;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    /// <summary>The files of package/ with their relative zip entry names (forward slashes); empty when there is no package folder.</summary>
    public List<(string EntryName, string Path)> PackageFiles(BridgePlatform platform = BridgePlatform.Windows)
    {
        var root = Path.Combine(DirectoryOf(platform), PackageDirName);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => (EntryName: Path.GetRelativePath(root, file).Replace('\\', '/'), Path: file))
            .OrderBy(x => x.EntryName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Opens the bridge file for streaming; shares read/write/delete so a rebuild does not fail while a download runs.</summary>
    public FileStream OpenExe(BridgePlatform platform = BridgePlatform.Windows) =>
        new(PathOf(platform), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 81920, useAsync: true);

    private static string ResolveDirectory(string? configured, IWebHostEnvironment environment, string folderName)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));
        }

        // Development: the repository root is two levels above src/Companion.Api (same convention as AppVersion)
        return environment.IsDevelopment()
            ? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "artifacts", folderName))
            : Path.Combine(AppContext.BaseDirectory, folderName);
    }
}
