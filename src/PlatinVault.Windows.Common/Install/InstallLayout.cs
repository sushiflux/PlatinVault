using System.Security.Cryptography;
using System.Text;

namespace PlatinVault.Windows.Install;

/// <summary>
/// Where the Windows program lives: program folder, data folder, shortcuts and the registry entries (all per user, no administrator
/// rights). In a test run (<c>--test-root &lt;folder&gt;</c>) EVERYTHING lives inside that folder - the registry entries in a JSON file
/// (<see cref="FileRegistry"/>), the shortcuts as files - so a test never touches the real profile (no Run key, no Start menu, no
/// "Apps" entry) and never meets a real installation (names of mutex and pipe carry a test suffix).
/// </summary>
public sealed class InstallLayout
{
    public const string ProductName = "PlatinVault";
    public const string ExeName = "PlatinVault.exe";
    public const string RunValueName = "PlatinVault";
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PlatinVault";
    public const string AppKeyPath = @"Software\SushiFlux\PlatinVault";

    public bool IsTest { get; private init; }

    /// <summary>
    /// Development run of the Windows program (<c>PlatinVault.exe --dev &lt;repository&gt;</c>, started by start-windows.ps1): its own data
    /// folder in the repository, a file instead of the registry and a name suffix for mutex and pipe. Unlike a test run the window, the
    /// tray icon and the overlay are real; unlike an installation nothing is registered with Windows.
    /// </summary>
    public bool IsDev { get; private init; }

    public string? TestRoot { get; private init; }

    /// <summary>Backup folder outside the data folder (development run), null = <c>backups</c> inside the data folder.</summary>
    public string? BackupsDir { get; private init; }

    /// <summary>Default program folder (an existing installation's folder wins, see <see cref="InstalledDir"/>).</summary>
    public required string DefaultInstallDir { get; init; }

    /// <summary>The data folder: database, backups, keys, logs. Never inside the program folder (updates replace that).</summary>
    public required string DataDir { get; init; }

    public required string StartMenuShortcut { get; init; }

    public required string DesktopShortcut { get; init; }

    public required IRegistryStore Registry { get; init; }

    /// <summary>"" for the real installation, "-test-xxxxxxxx" for a test root (mutex/pipe names).</summary>
    public required string NameSuffix { get; init; }

    public string MutexName => @"Local\PlatinVault.Desktop" + NameSuffix;

    /// <summary>
    /// Pipe names are machine-wide (unlike the "Local\" mutex): the name carries this user's SID (hashed) and the session, so two Windows
    /// accounts on one PC never meet each other's pipe; the pipe itself is also opened with <c>PipeOptions.CurrentUserOnly</c>.
    /// </summary>
    public string PipeName => $"PlatinVault.Desktop.{UserTag}.s{System.Diagnostics.Process.GetCurrentProcess().SessionId}{NameSuffix}";

    private static readonly string UserTag = ComputeUserTag();

    private static string ComputeUserTag()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? Environment.UserName;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..12];
    }

    public DataLayout Data => new(DataDir, BackupsDir);

    /// <summary>The program folder of the current installation, if there is one.</summary>
    public string? InstalledDir => Registry.GetString(AppKeyPath, "InstallDir") is { Length: > 0 } dir ? dir : null;

    public string? InstalledVersion => Registry.GetString(UninstallKeyPath, "DisplayVersion");

    public static InstallLayout Create(string? testRoot, string? dataDirOverride = null)
    {
        if (testRoot is { Length: > 0 })
        {
            var root = Path.GetFullPath(testRoot);
            var tag = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToLowerInvariant())))[..8];
            return new InstallLayout
            {
                IsTest = true,
                TestRoot = root,
                DefaultInstallDir = Path.Combine(root, "install"),
                DataDir = Path.GetFullPath(dataDirOverride ?? Path.Combine(root, "data")),
                StartMenuShortcut = Path.Combine(root, "startmenu", ProductName + ".lnk"),
                DesktopShortcut = Path.Combine(root, "desktop", ProductName + ".lnk"),
                Registry = new FileRegistry(Path.Combine(root, "registry.json")),
                NameSuffix = "-test-" + tag,
            };
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new InstallLayout
        {
            DefaultInstallDir = Path.Combine(local, "Programs", ProductName),
            DataDir = Path.GetFullPath(dataDirOverride ?? Path.Combine(local, ProductName)),
            StartMenuShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName + ".lnk"),
            DesktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ProductName + ".lnk"),
            Registry = new WindowsRegistry(),
            NameSuffix = string.Empty,
        };
    }

    /// <summary>
    /// Layout of a development run: everything below <paramref name="dataDir"/> (database, logs, WebView profile), the backups
    /// in <paramref name="backupsDir"/> (a folder of its own, so deleting the data folder never takes the backups along). The "registry" is
    /// a JSON file in the data folder and the shortcuts are never created, so nothing of the real profile (Run key, Start menu, "Apps"
    /// entry) is read or written and a real installation is never met (mutex/pipe names carry a suffix derived from the data folder).
    /// </summary>
    public static InstallLayout CreateDev(string dataDir, string backupsDir)
    {
        var data = Path.GetFullPath(dataDir);
        var tag = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(data.ToLowerInvariant())))[..8];
        return new InstallLayout
        {
            IsDev = true,
            DefaultInstallDir = Path.Combine(data, "install"),
            DataDir = data,
            BackupsDir = Path.GetFullPath(backupsDir),
            StartMenuShortcut = Path.Combine(data, "shortcuts", ProductName + " (Start menu).lnk"),
            DesktopShortcut = Path.Combine(data, "shortcuts", ProductName + " (Desktop).lnk"),
            Registry = new FileRegistry(Path.Combine(data, "registry.json")),
            NameSuffix = "-dev-" + tag,
        };
    }

    /// <summary>The arguments a started copy must get to keep using the same (test) locations.</summary>
    public IEnumerable<string> InheritedArguments()
    {
        if (TestRoot is not null) { yield return "--test-root"; yield return TestRoot; }
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A folder the installer may fill or delete: a full path, not a drive root, not the profile or a system folder itself.
    /// Protects against a mistyped "C:\" or "C:\Users\Name" ending up as the program folder.
    /// </summary>
    public static bool IsSafeProgramFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) return false;
        string full;
        try { full = Path.GetFullPath(folder).TrimEnd('\\', '/'); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        if (!Path.IsPathFullyQualified(full)) return false;
        var root = Path.GetPathRoot(full)?.TrimEnd('\\', '/') ?? string.Empty;
        if (full.Length <= root.Length + 1) return false;

        var forbidden = new[]
        {
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.System,
            Environment.SpecialFolder.Programs,
        };
        foreach (var special in forbidden)
        {
            var path = Environment.GetFolderPath(special);
            if (path.Length > 0 && SamePath(path, full)) return false;
        }
        var localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        return !SamePath(localPrograms, full);
    }
}

/// <summary>The fixed parts of the data folder. Uninstall "delete data" removes exactly these and nothing else (the bridge writes its log into the same parent folder).</summary>
public sealed record DataLayout(string Root, string? BackupsDirectory = null)
{
    public string Database => Path.Combine(Root, "database");
    public string Backups => BackupsDirectory ?? Path.Combine(Root, "backups");
    public string Server => Path.Combine(Root, "server");
    public string Logs => Path.Combine(Root, "logs");
    public string WebView => Path.Combine(Root, "webview2");
    public string Updates => Path.Combine(Root, "updates");
    public string SettingsFile => Path.Combine(Root, "desktop.json");
    /// <summary>Credentials of the former bundled PostgreSQL (betas before 2026-11-02). Not used any more; only "delete data" removes it.</summary>
    public string CredentialsFile => Path.Combine(Root, "database.credentials");

    public IEnumerable<string> Folders => [Database, Backups, Server, Logs, WebView, Updates];

    public IEnumerable<string> Files => [SettingsFile, CredentialsFile];
}
