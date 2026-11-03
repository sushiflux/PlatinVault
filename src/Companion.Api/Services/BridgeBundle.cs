using System.Security.Cryptography;
using Companion.Api.Configuration;
using Companion.Api.Localization;
using Microsoft.Extensions.Options;

namespace Companion.Api.Services;

/// <summary>
/// The realtime bridge that ships with the server: PlatinVault.Bridge.exe ("PlatinVault Bridge"), its version (bridge-version.txt) and the
/// templates in package/ (autostart scripts, readme). The server offers it for download and tells a running bridge
/// which version and SHA-256 it should have, so the bridge can update itself.
/// </summary>
public sealed class BridgeBundle
{
    public const string ExeName = "PlatinVault.Bridge.exe";
    public const string VersionFileName = "bridge-version.txt";
    public const string PackageDirName = "package";

    private readonly Lock _hashLock = new();
    private (DateTime LastWrite, long Length, string Sha256)? _cachedHash;

    public BridgeBundle(IOptions<CompanionOptions> options, IWebHostEnvironment environment)
    {
        BundleDirectory = ResolveDirectory(options.Value.BridgeDir, environment);
        IsDevelopment = environment.IsDevelopment();
    }

    /// <summary>Folder the bundle is read from (may not exist).</summary>
    public string BundleDirectory { get; }

    public bool IsDevelopment { get; }

    public string ExePath => Path.Combine(BundleDirectory, ExeName);

    public bool IsAvailable => File.Exists(ExePath);

    /// <summary>Hint for the "no bundled bridge" error (language of the reader); in Development it names the build script.</summary>
    public Msg MissingHint => IsDevelopment ? ServerTexts.Bridge.MissingHintDevelopment : ServerTexts.Bridge.MissingHint;

    /// <summary>Version of the bundled bridge ("1.0.3") or null when the bundle has no version file.</summary>
    public string? Version
    {
        get
        {
            var path = Path.Combine(BundleDirectory, VersionFileName);
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
    }

    /// <summary>
    /// Lowercase hex SHA-256 of the exe, or null when there is none. Computed once and cached; computed again when the
    /// file's last-write time or length changes (e.g. after scripts/build-bridge.ps1 ran).
    /// </summary>
    public string? Sha256
    {
        get
        {
            var info = new FileInfo(ExePath);
            if (!info.Exists)
            {
                return null;
            }

            lock (_hashLock)
            {
                if (_cachedHash is { } cached && cached.LastWrite == info.LastWriteTimeUtc && cached.Length == info.Length)
                {
                    return cached.Sha256;
                }

                try
                {
                    using var stream = new FileStream(ExePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var hash = Convert.ToHexStringLower(SHA256.HashData(stream));

                    // A file that changed while it was read (build in progress) must not be cached under the old stamp
                    var after = new FileInfo(ExePath);
                    if (after.Exists && after.LastWriteTimeUtc == info.LastWriteTimeUtc && after.Length == info.Length)
                    {
                        _cachedHash = (info.LastWriteTimeUtc, info.Length, hash);
                    }
                    return hash;
                }
                catch (IOException)
                {
                    return null;
                }
            }
        }
    }

    /// <summary>The files of package/ with their relative zip entry names (forward slashes); empty when there is no package folder.</summary>
    public List<(string EntryName, string Path)> PackageFiles()
    {
        var root = Path.Combine(BundleDirectory, PackageDirName);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => (EntryName: Path.GetRelativePath(root, file).Replace('\\', '/'), Path: file))
            .OrderBy(x => x.EntryName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Opens the exe for streaming; shares read/write/delete so a rebuild does not fail while a download runs.</summary>
    public FileStream OpenExe() =>
        new(ExePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 81920, useAsync: true);

    private static string ResolveDirectory(string? configured, IWebHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configured));
        }

        // Development: the repository root is two levels above src/Companion.Api (same convention as AppVersion)
        return environment.IsDevelopment()
            ? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "artifacts", "bridge"))
            : Path.Combine(AppContext.BaseDirectory, "bridge");
    }
}
