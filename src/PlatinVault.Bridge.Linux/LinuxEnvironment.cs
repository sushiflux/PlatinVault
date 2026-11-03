namespace PlatinVault.Bridge;

/// <summary>
/// Everything machine-specific the Linux bridge reads or writes: configuration, state/log folder, the install folder of the systemd user
/// service. Production uses the XDG locations in the home folder (nothing outside it, so it works on read-only-root systems such as SteamOS
/// and Bazzite). A test run (<c>--test-root</c>) moves all of them into one folder, so nothing touches the real installation.
/// </summary>
internal sealed class LinuxEnvironment
{
    public const string ProductName = "PlatinVault Bridge";

    /// <summary>File name of the binary; the self-update only replaces a file with this name.</summary>
    public const string BinaryName = "platinvault-bridge";

    /// <summary>Name of the systemd user unit.</summary>
    public const string UnitName = "platinvault-bridge.service";

    private const string AppFolder = "platinvault-bridge";

    private LinuxEnvironment()
    {
    }

    public required bool IsTest { get; init; }

    public string? TestRoot { get; init; }

    /// <summary><c>$XDG_CONFIG_HOME/platinvault-bridge/bridge.json</c> (mode 0600): server, token, language, Steam folder.</summary>
    public required string ConfigPath { get; init; }

    /// <summary><c>$XDG_STATE_HOME/platinvault-bridge/bridge.log</c>.</summary>
    public required string LogPath { get; init; }

    /// <summary>Small JSON file the running service keeps up to date for the <c>status</c> command.</summary>
    public required string StatusPath { get; init; }

    /// <summary>Remembers the last self-update across restarts (see <see cref="UpdateMarker"/>).</summary>
    public string UpdateMarkerPath => Path.Combine(Path.GetDirectoryName(StatusPath)!, "updated-to.txt");

    /// <summary>Lock file of the running bridge: only one bridge per user may push and launch.</summary>
    public string LockPath => Path.Combine(Path.GetDirectoryName(StatusPath)!, "bridge.lock");

    /// <summary><c>$XDG_DATA_HOME/platinvault-bridge</c>: where install.sh puts the binary and the documents.</summary>
    public required string InstallDirectory { get; init; }

    /// <summary><c>~/.config/systemd/user/platinvault-bridge.service</c>.</summary>
    public required string UnitPath { get; init; }

    /// <summary>Where a test run records the steam:// addresses it would have opened.</summary>
    public string LaunchRecordPath => Path.Combine(TestRoot ?? InstallDirectory, "launches.txt");

    /// <summary>Optional scripted Steam state of a test run (see <see cref="FakeLocalSteam"/>).</summary>
    public string FakeSteamPath => Path.Combine(TestRoot ?? InstallDirectory, "fake-steam.json");

    /// <summary>Full path of the running binary.</summary>
    public static string ExePath => Environment.ProcessPath ?? string.Empty;

    /// <summary>Portable config: a <c>bridge.json</c> next to the binary (the package of the server ships one next to install.sh).</summary>
    public static string PortableConfigPath => Path.Combine(Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory, "bridge.json");

    /// <param name="testRoot">The folder of a test run, null in production.</param>
    /// <param name="getEnvironmentVariable">Injected so the XDG rules can be tested without touching the real environment.</param>
    /// <param name="homeFallback">The home folder when $HOME is not set.</param>
    public static LinuxEnvironment Create(string? testRoot, Func<string, string?> getEnvironmentVariable, string? homeFallback = null)
    {
        if (!string.IsNullOrWhiteSpace(testRoot))
        {
            var root = Path.GetFullPath(testRoot);
            return new LinuxEnvironment
            {
                IsTest = true,
                TestRoot = root,
                ConfigPath = Path.Combine(root, "config", AppFolder, "bridge.json"),
                LogPath = Path.Combine(root, "state", AppFolder, "bridge.log"),
                StatusPath = Path.Combine(root, "state", AppFolder, "status.json"),
                InstallDirectory = Path.Combine(root, "data", AppFolder),
                UnitPath = Path.Combine(root, "config", "systemd", "user", UnitName),
            };
        }

        var home = Absolute(getEnvironmentVariable("HOME")) ?? homeFallback ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // The XDG specification: a relative value is invalid and must be ignored
        var config = Absolute(getEnvironmentVariable("XDG_CONFIG_HOME")) ?? Path.Combine(home, ".config");
        var state = Absolute(getEnvironmentVariable("XDG_STATE_HOME")) ?? Path.Combine(home, ".local", "state");
        var data = Absolute(getEnvironmentVariable("XDG_DATA_HOME")) ?? Path.Combine(home, ".local", "share");

        return new LinuxEnvironment
        {
            IsTest = false,
            ConfigPath = Path.Combine(config, AppFolder, "bridge.json"),
            LogPath = Path.Combine(state, AppFolder, "bridge.log"),
            StatusPath = Path.Combine(state, AppFolder, "status.json"),
            InstallDirectory = Path.Combine(data, AppFolder),
            UnitPath = Path.Combine(config, "systemd", "user", UnitName),
        };
    }

    private static string? Absolute(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.StartsWith('/') ? path.TrimEnd('/') : null;

    /// <summary>
    /// "de" for a German locale, "en" for every other one. The locale is the first of LC_ALL, LC_MESSAGES, LANG that is set (the order the
    /// C library uses); unset or "C"/"POSIX" means no language, English. Like glibc, the GNU <c>LANGUAGE</c> list (colon separated, most wanted
    /// first) beats the locale for message texts - the first entry of it that the bridge speaks wins; a list without German or English changes nothing.
    /// </summary>
    public static string SystemLanguage(Func<string, string?> getEnvironmentVariable)
    {
        string? locale = null;
        foreach (var name in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
        {
            var value = getEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value)) continue;
            locale = value;
            break;
        }

        // glibc ignores LANGUAGE in the C locale
        if (locale is null or "C" or "POSIX" or "C.UTF-8") return BridgeLang.English;

        foreach (var entry in (getEnvironmentVariable("LANGUAGE") ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (BridgeLang.Normalize(entry.Split('.')[0].Split('@')[0]) is { } supported) return supported;
        }

        return BridgeLang.Normalize(locale.Split('.')[0].Split('@')[0]) ?? BridgeLang.English;
    }
}
