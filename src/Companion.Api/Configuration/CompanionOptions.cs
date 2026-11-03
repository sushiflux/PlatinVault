namespace Companion.Api.Configuration;

/// <summary>Application configuration bound from the "Companion" section.</summary>
public sealed class CompanionOptions
{
    public const string Section = "Companion";

    public string DataDir { get; set; } = ".data/dev";

    /// <summary>
    /// Folder of the Data Protection key ring. Empty = <c>dp-keys</c> under <see cref="DataDir"/>. Only the Windows program's shared
    /// development mode sets it (together with <see cref="SecretsDir"/>) so it can read the API keys the browser stack stored.
    /// </summary>
    public string? KeyRingDir { get; set; }

    /// <summary>Folder of the encrypted API keys. Empty = <c>secrets</c> under <see cref="DataDir"/> (see <see cref="KeyRingDir"/>).</summary>
    public string? SecretsDir { get; set; }

    public string ResolvedKeyRingDir => string.IsNullOrWhiteSpace(KeyRingDir) ? Path.Combine(DataDir, "dp-keys") : KeyRingDir;

    public string ResolvedSecretsDir => string.IsNullOrWhiteSpace(SecretsDir) ? Path.Combine(DataDir, "secrets") : SecretsDir;

    /// <summary>
    /// Folder with the bundled realtime bridge (PlatinVault.Bridge.exe, bridge-version.txt, package/). Empty = default:
    /// "bridge" next to the app in a release build (/app/bridge in the container), artifacts/bridge of the repository in Development.
    /// </summary>
    public string? BridgeDir { get; set; }

    /// <summary>
    /// Folder with the bundled Linux bridge (platinvault-bridge, bridge-version.txt, package/). Empty = default: "bridge-linux" next to the app
    /// in a release build (/app/bridge-linux in the container), artifacts/bridge-linux of the repository in Development.
    /// </summary>
    public string? BridgeLinuxDir { get; set; }

    public DatabaseOptions Database { get; set; } = new();

    public BackupOptions Backups { get; set; } = new();

    /// <summary>The database file (absolute).</summary>
    public string DatabaseFile => Database.ResolvePath(DataDir);

    /// <summary>The backup folder (absolute).</summary>
    public string BackupDir => Backups.ResolveDir(DataDir);

    /// <summary>Where the AI providers are reached. The official addresses unless configured otherwise (a proxy, or a test double).</summary>
    public AiProviderUrls Ai { get; set; } = new();

    /// <summary>"beta" for a beta image (version shown as "v2026.11.12 (13.557 β)"; the former "rc" is accepted as beta too),
    /// "dev" for a dev-build image ("v2026.11.12 (13.557-dev)", no update offers; the server environment stays Production),
    /// "release" otherwise ("v2026.11.12 (13.557)"); set by the Docker build.</summary>
    public string ReleaseChannel { get; set; } = "release";

    /// <summary>
    /// How this server was installed. <see cref="LinuxPackageKind"/> ("linux-package") = the Linux package (a tar.gz with install.sh, started
    /// by its run.sh as a systemd user service); the update check then shows "download the new package and run install.sh" instead of the
    /// container tag advice. Empty = anything else (container, development).
    /// </summary>
    public string InstallKind { get; set; } = "";

    /// <summary>The <see cref="InstallKind"/> of the Linux package (set by its run.sh).</summary>
    public const string LinuxPackageKind = "linux-package";

    /// <summary>True when this server is the Linux package.</summary>
    public bool IsLinuxPackage => string.Equals(InstallKind?.Trim(), LinuxPackageKind, StringComparison.OrdinalIgnoreCase);

    /// <summary>Where the update check reads the published betas and releases (see <c>UpdateService</c>).</summary>
    public UpdateCheckOptions Updates { get; set; } = new();

    /// <summary>The local Steam client ("Companion:Steam:*").</summary>
    public SteamLocalOptions Steam { get; set; } = new();
}

/// <summary>
/// Where the server finds the Steam client of this computer ("Companion:Steam:*"). Normally nothing is set: the Windows program reads
/// the registry, the native Linux package searches the usual Steam folders. For a packaging or a container with a mounted Steam folder.
/// </summary>
public sealed class SteamLocalOptions
{
    /// <summary>
    /// The Steam folder (the one that contains <c>steamapps</c>), e.g. via the environment variable <c>Companion__Steam__Root</c>. The
    /// setting "Steam-Ordner" in the app wins over it; an unusable folder is skipped and the search continues.
    /// </summary>
    public string? Root { get; set; }
}

/// <summary>
/// The update check ("Companion:Updates:*"). The defaults read the public customer repository on GitHub; tests point
/// <see cref="ApiBaseUrl"/> at a fake handler, and the Windows program switches the check off in development and test runs.
/// </summary>
public sealed class UpdateCheckOptions
{
    /// <summary>False = no update check at all (the settings show that instead of a result).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Base address of the GitHub REST API (no trailing slash).</summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com";

    /// <summary>The public repository with the betas and releases ("owner/name").</summary>
    public string Repository { get; set; } = "sushiflux/PlatinVault";

    /// <summary>
    /// How long a successful answer is reused by the automatic checks. GitHub allows 60 unauthenticated requests per hour and IP, and
    /// several programs behind one internet connection share them - 6 hours keeps PlatinVault far below that.
    /// </summary>
    public double CacheHours { get; set; } = 6;
}

/// <summary>Base URLs of the AI provider APIs (no trailing slash). Changing them is for proxies and tests only.</summary>
public sealed class AiProviderUrls
{
    public string GeminiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public string ClaudeBaseUrl { get; set; } = "https://api.anthropic.com/v1";
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";
}

/// <summary>
/// The database: ONE SQLite file per installation (user decision 2026-11-02: one user, a few MB, one writing server - no database server to install). Empty
/// <see cref="Path"/> = <c>platinvault.db</c> in the data folder (Docker <c>/data/platinvault.db</c>, development
/// <c>.data/dev/platinvault.db</c>); the Windows program sets <c>%LOCALAPPDATA%\PlatinVault\database\platinvault.db</c>.
/// The file must lie on a local disk (never a network share; on Unraid a cache/pool path, not <c>/mnt/user</c>).
/// </summary>
public sealed class DatabaseOptions
{
    public const string FileName = "platinvault.db";

    public string? Path { get; set; }

    public string ResolvePath(string dataDir) =>
        System.IO.Path.GetFullPath(string.IsNullOrWhiteSpace(Path) ? System.IO.Path.Combine(dataDir, FileName) : Path);
}

/// <summary>
/// Automatic backups of the database file (<c>Companion:Backups:*</c>, see <c>DatabaseBackupService</c>): at start, every
/// <see cref="IntervalHours"/> hours and at a clean stop. Empty <see cref="Dir"/> = <c>backups</c> in the data folder (Docker
/// <c>/data/backups</c>); development sets <c>.backups/auto</c>, the Windows program <c>%LOCALAPPDATA%\PlatinVault\backups</c>.
/// </summary>
public sealed class BackupOptions
{
    /// <summary>False = this server makes no automatic backups (tests). Restore and "back up now" still work.</summary>
    public bool Enabled { get; set; } = true;

    public string? Dir { get; set; }

    public double IntervalHours { get; set; } = 6;

    public string ResolveDir(string dataDir) =>
        System.IO.Path.GetFullPath(string.IsNullOrWhiteSpace(Dir) ? System.IO.Path.Combine(dataDir, "backups") : Dir);
}
