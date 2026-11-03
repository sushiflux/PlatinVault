namespace PlatinVault.Bridge;

/// <summary>
/// Replaces the running exe by the one the PlatinVault server offers, so the bridge needs no store, no installer and
/// no GitHub. The server tells the version and SHA-256 in the answer of every state push; this class downloads
/// <c>GET /api/bridge/exe</c> next to the exe, verifies the hash and swaps the files. Restarting is up to the caller
/// (it has to release the single-instance mutex first).
/// </summary>
internal sealed class SelfUpdater : ISelfUpdater
{
    private const string ExeFileName = BridgeEnvironment.ExeFileName;

    // A wrong hash points at a broken or tampered download - do not hammer the server with the same version.
    private static readonly TimeSpan HashMismatchBlock = TimeSpan.FromHours(1);
    // Any other failure (network, disk, locked file) is retried, but not on every single state push.
    private static readonly TimeSpan FailureBlock = TimeSpan.FromMinutes(10);

    private readonly BridgeClient _client;
    private readonly bool _enabled;
    private readonly Version? _current;
    private readonly Version? _updatedTo;
    private readonly string? _exePath;
    private readonly Dictionary<Version, DateTime> _blockedUntil = [];

    private bool _disabled;

    /// <summary>Set once the new exe is in place; the caller restarts into it.</summary>
    public Version? InstalledVersion { get; private set; }

    public SelfUpdater(BridgeClient client, string currentVersion, bool enabled, string? updatedTo)
    {
        _client = client;
        _enabled = enabled;
        _current = ParseVersion(currentVersion);
        _updatedTo = ParseVersion(updatedTo);
        _exePath = Environment.ProcessPath;
    }

    private static string OldPath(string exePath) => exePath + ".old";

    private static string NewPath(string exePath) => exePath + ".new";

    /// <summary>
    /// Removes what an earlier update left behind: <c>.old</c> (the replaced exe, still locked while the previous process
    /// is shutting down - hence callable repeatedly) and <c>.new</c> (an interrupted download). Returns true when clean.
    /// </summary>
    public static bool CleanupLeftovers()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return true;

        var clean = true;
        foreach (var path in new[] { OldPath(exePath), NewPath(exePath) })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                BridgeLog.Debug(BridgeTexts.Updater.LeftoverNotDeletedDebug(Path.GetFileName(path), ex.Message));
                clean = false;
            }
        }
        return clean;
    }

    bool ISelfUpdater.CleanupLeftovers() => CleanupLeftovers();

    /// <summary>
    /// Updates when the server offers a newer version than this one. Returns true when the new exe is in place and the
    /// caller must restart; false in every other case (nothing to do, blocked, failed - all logged).
    /// </summary>
    public async Task<bool> TryUpdateAsync(string? latestText, string? expectedSha256, CancellationToken ct)
    {
        if (!_enabled || _disabled || string.IsNullOrWhiteSpace(latestText)) return false;

        var latest = ParseVersion(latestText);
        if (latest is null)
        {
            BridgeLog.Debug(BridgeTexts.Updater.VersionNotParsableDebug(latestText));
            return false;
        }

        if (_current is null || latest <= _current) return false;

        if (_updatedTo is not null && latest <= _updatedTo)
        {
            // We were started by an update to this very version but still report an older one: the server serves an exe
            // whose version does not match what it announces. Updating again would loop forever.
            BridgeLog.Warn(BridgeTexts.Updater.ServerOffersOlder(latest, _current));
            _disabled = true;
            return false;
        }

        if (_blockedUntil.TryGetValue(latest, out var until) && DateTime.UtcNow < until) return false;

        // Never replace the host process when the bridge runs as "dotnet PlatinVault.Bridge.dll" during development.
        if (_exePath is null || !string.Equals(Path.GetFileName(_exePath), ExeFileName, StringComparison.OrdinalIgnoreCase))
        {
            BridgeLog.Info(BridgeTexts.Updater.OnlyForPublishedExe(latest, ExeFileName));
            _disabled = true;
            return false;
        }

        BridgeLog.Info(BridgeTexts.Updater.Updating(latest));

        var newPath = NewPath(_exePath);
        var oldPath = OldPath(_exePath);

        try
        {
            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                BridgeLog.Error(BridgeTexts.Updater.NoChecksum);
                Block(latest, HashMismatchBlock);
                return false;
            }

            // Fail early and clearly when the folder is read-only (e.g. Program Files) instead of after the download.
            Directory.CreateDirectory(Path.GetDirectoryName(_exePath)!);
            File.WriteAllBytes(newPath, []);

            BridgeLog.Info(BridgeTexts.Updater.Downloading);
            var download = await _client.DownloadExeAsync(newPath, ct).ConfigureAwait(false);
            if (!download.Ok)
            {
                BridgeLog.Error(BridgeTexts.Updater.DownloadFailed(download.Error, FailureBlock.TotalMinutes));
                DeleteQuietly(newPath);
                Block(latest, FailureBlock);
                return false;
            }

            if (!string.Equals(download.Sha256, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                BridgeLog.Error(BridgeTexts.Updater.ChecksumMismatch(
                    Shorten(expectedSha256), Shorten(download.Sha256), latest, HashMismatchBlock.TotalHours));
                DeleteQuietly(newPath);
                Block(latest, HashMismatchBlock);
                return false;
            }

            BridgeLog.Info(BridgeTexts.Updater.DownloadVerified(download.Bytes / 1024.0 / 1024.0));
            Swap(_exePath, newPath, oldPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            BridgeLog.Warn(BridgeTexts.Updater.FolderNotWritable(Path.GetDirectoryName(_exePath), ex.Message, latest));
            DeleteQuietly(newPath);
            _disabled = true;
            return false;
        }
        catch (IOException ex)
        {
            BridgeLog.Error(BridgeTexts.Updater.UpdateFailed(latest, ex.Message, FailureBlock.TotalMinutes));
            DeleteQuietly(newPath);
            Block(latest, FailureBlock);
            return false;
        }

        InstalledVersion = latest;
        BridgeLog.Success(BridgeTexts.Updater.Installed(latest));
        return true;
    }

    /// <summary>
    /// Renames the running exe to <c>.old</c> (Windows allows renaming a running image, not deleting or overwriting it)
    /// and moves the verified download into its place. Rolls back when the second step fails.
    /// </summary>
    private static void Swap(string exePath, string newPath, string oldPath)
    {
        try
        {
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            // A still running image cannot be deleted; that is a locked file, not a read-only folder.
            throw new IOException(BridgeTexts.Updater.FileStillInUse(Path.GetFileName(oldPath)), ex);
        }

        File.Move(exePath, oldPath);
        try
        {
            File.Move(newPath, exePath);
        }
        catch
        {
            // Put the working exe back before reporting the failure, so the bridge never ends up without an exe.
            File.Move(oldPath, exePath, overwrite: true);
            throw;
        }
    }

    private void Block(Version version, TimeSpan duration) => _blockedUntil[version] = DateTime.UtcNow + duration;

    private static void DeleteQuietly(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* leftovers are cleaned at the next start */ }
    }

    private static string Shorten(string? hash) => string.IsNullOrEmpty(hash) ? "-" : hash.Length > 12 ? hash[..12] + "…" : hash;

    // The assembly version may carry a "+commit" suffix and servers may prefix a "v"; Version needs plain numbers.
    private static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var plain = text.Trim().TrimStart('v', 'V').Split('+', '-')[0];
        return Version.TryParse(plain, out var version) ? version : null;
    }
}
