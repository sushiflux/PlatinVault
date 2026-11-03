namespace PlatinVault.Bridge;

/// <summary>
/// Replaces the running binary by the one the PlatinVault server offers (<c>GET /api/bridge/exe?platform=linux</c>), so the bridge needs no
/// package manager and no GitHub. Same idea and rules as the Windows <c>SelfUpdater</c>: the server tells version and SHA-256 in the answer of
/// every state push, the download goes to <c>&lt;binary&gt;.new</c>, is verified, and replaces the binary. Linux allows replacing a running
/// file by renaming over it, so no <c>.old</c> dance is needed; restarting is up to the caller (systemd restarts the service).
/// </summary>
internal sealed class LinuxSelfUpdater : ISelfUpdater
{
    // A wrong hash points at a broken or tampered download - do not hammer the server with the same version
    private static readonly TimeSpan HashMismatchBlock = TimeSpan.FromHours(1);
    // Any other failure (network, disk) is retried, but not on every single state push
    private static readonly TimeSpan FailureBlock = TimeSpan.FromMinutes(10);

    private readonly BridgeClient _client;
    private readonly bool _enabled;
    private readonly Version? _current;
    private readonly Version? _updatedTo;
    private readonly string? _binaryPath;
    private readonly string? _markerPath;
    private readonly Dictionary<Version, DateTime> _blockedUntil = [];

    private bool _disabled;

    /// <summary>Set once the new binary is in place; the caller restarts into it.</summary>
    public Version? InstalledVersion { get; private set; }

    /// <param name="markerPath">File in the state folder that remembers "just updated to X" across restarts (see <see cref="UpdateMarker"/>). Under systemd the new process gets no
    /// <c>--updated-to</c> argument, so without the file a server that announces a version its binary does not carry would be downloaded again every few seconds.</param>
    /// <param name="binaryPath">The file to replace; null/other than <see cref="LinuxEnvironment.BinaryName"/> (e.g. `dotnet x.dll` in development) switches the update off.</param>
    public LinuxSelfUpdater(BridgeClient client, string currentVersion, bool enabled, string? updatedTo, string? binaryPath, string? markerPath = null)
    {
        _client = client;
        _enabled = enabled;
        _current = ParseVersion(currentVersion);
        _updatedTo = ParseVersion(updatedTo);
        _binaryPath = binaryPath;
        _markerPath = markerPath;
    }

    private static string NewPath(string path) => path + ".new";

    public bool CleanupLeftovers()
    {
        if (string.IsNullOrEmpty(_binaryPath)) return true;
        try
        {
            if (File.Exists(NewPath(_binaryPath))) File.Delete(NewPath(_binaryPath));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BridgeLog.Debug(LinuxTexts.Updater.LeftoverNotDeletedDebug(Path.GetFileName(NewPath(_binaryPath)), ex.Message));
            return false;
        }
    }

    public async Task<bool> TryUpdateAsync(string? latestText, string? expectedSha256, CancellationToken ct)
    {
        if (!_enabled || _disabled || string.IsNullOrWhiteSpace(latestText)) return false;

        var latest = ParseVersion(latestText);
        if (latest is null)
        {
            BridgeLog.Debug(LinuxTexts.Updater.VersionNotParsableDebug(latestText));
            return false;
        }

        if (_current is null || latest <= _current) return false;

        if (_updatedTo is not null && latest <= _updatedTo)
        {
            // We were started by an update to this very version (argument or marker file) but still report an older one: the server serves
            // a binary whose version does not match what it announces. Updating again would loop forever.
            BridgeLog.Warn(LinuxTexts.Updater.ServerOffersOlder(latest, _current));
            _disabled = true;
            return false;
        }

        if (_blockedUntil.TryGetValue(latest, out var until) && DateTime.UtcNow < until) return false;

        // Never replace the host process when the bridge runs as "dotnet platinvault-bridge.dll" during development
        if (_binaryPath is null || !string.Equals(Path.GetFileName(_binaryPath), LinuxEnvironment.BinaryName, StringComparison.Ordinal))
        {
            BridgeLog.Info(LinuxTexts.Updater.OnlyForPublishedBinary(latest, LinuxEnvironment.BinaryName));
            _disabled = true;
            return false;
        }

        BridgeLog.Info(LinuxTexts.Updater.Updating(latest));
        var newPath = NewPath(_binaryPath);

        try
        {
            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                BridgeLog.Error(LinuxTexts.Updater.NoChecksum);
                Block(latest, HashMismatchBlock);
                return false;
            }

            // Fail early and clearly when the folder is read-only instead of after the download
            File.WriteAllBytes(newPath, []);

            BridgeLog.Info(LinuxTexts.Updater.Downloading);
            var download = await _client.DownloadExeAsync(newPath, ct).ConfigureAwait(false);
            if (!download.Ok)
            {
                BridgeLog.Error(LinuxTexts.Updater.DownloadFailed(download.Error, FailureBlock.TotalMinutes));
                DeleteQuietly(newPath);
                Block(latest, FailureBlock);
                return false;
            }

            if (!string.Equals(download.Sha256, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                BridgeLog.Error(LinuxTexts.Updater.ChecksumMismatch(
                    Shorten(expectedSha256), Shorten(download.Sha256), latest, HashMismatchBlock.TotalHours));
                DeleteQuietly(newPath);
                Block(latest, HashMismatchBlock);
                return false;
            }

            BridgeLog.Info(LinuxTexts.Updater.DownloadVerified(
                (download.Bytes / 1024.0 / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));

            // Executable like the file it replaces; the rename then swaps both atomically (a running process keeps its old inode)
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(newPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            File.Move(newPath, _binaryPath, overwrite: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            BridgeLog.Warn(LinuxTexts.Updater.FolderNotWritable(Path.GetDirectoryName(_binaryPath), ex.Message, latest));
            DeleteQuietly(newPath);
            _disabled = true;
            return false;
        }
        catch (IOException ex)
        {
            BridgeLog.Error(LinuxTexts.Updater.UpdateFailed(latest, ex.Message, FailureBlock.TotalMinutes));
            DeleteQuietly(newPath);
            Block(latest, FailureBlock);
            return false;
        }

        InstalledVersion = latest;
        UpdateMarker.Write(_markerPath, latest);
        BridgeLog.Success(LinuxTexts.Updater.Installed(latest));
        return true;
    }

    private void Block(Version version, TimeSpan duration) => _blockedUntil[version] = DateTime.UtcNow + duration;

    private static void DeleteQuietly(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* leftovers are cleaned at the next start */ }
    }

    private static string Shorten(string? hash) => string.IsNullOrEmpty(hash) ? "-" : hash.Length > 12 ? hash[..12] + "…" : hash;

    // The assembly version may carry a "+commit" suffix and servers may prefix a "v"; Version needs plain numbers
    internal static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var plain = text.Trim().TrimStart('v', 'V').Split('+', '-')[0];
        return Version.TryParse(plain, out var version) ? version : null;
    }
}

/// <summary>
/// The memory of the last self-update across restarts: a one-line file with the version that was installed. A restarted bridge that is
/// still older than that version knows the server's binary does not match its announcement, and stops updating instead of downloading
/// the same file again and again. Once the running binary is at least as new as the marker, the marker is deleted.
/// </summary>
internal static class UpdateMarker
{
    public static void Write(string? path, Version version)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, version.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BridgeLog.Debug(LinuxTexts.Updater.LeftoverNotDeletedDebug(Path.GetFileName(path), ex.Message));
        }
    }

    /// <summary>The marker's version text while it is still newer than <paramref name="currentVersion"/>; otherwise null (and the file is removed).</summary>
    public static string? Read(string? path, string currentVersion)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path).Trim();
            var marker = LinuxSelfUpdater.ParseVersion(text);
            var current = LinuxSelfUpdater.ParseVersion(currentVersion);
            if (marker is not null && (current is null || current < marker)) return text;
            File.Delete(path);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
