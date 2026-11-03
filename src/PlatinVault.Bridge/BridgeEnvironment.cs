using System.Security.Cryptography;
using System.Text;

namespace PlatinVault.Bridge;

/// <summary>
/// Everything machine-specific the bridge reads or writes: install folder, log, Start-menu shortcut, the registry keys for
/// autostart and the "Apps" entry, and the names of the single-instance mutex and events. In production these are the
/// real Windows locations. A test run (<c>--test-root</c>) moves all of them into a folder and a separate registry key,
/// so tests can install, update and uninstall without touching the real installation or a bridge that is running.
/// </summary>
internal sealed class BridgeEnvironment
{
    public const string ExeFileName = "PlatinVault.Bridge.exe";

    public const string ProductName = "PlatinVault Bridge";

    /// <summary>Name of the Run value and of the uninstall key.</summary>
    public const string RegistryName = "PlatinVaultBridge";

    private BridgeEnvironment()
    {
    }

    public required bool IsTest { get; init; }
    public required string InstallRoot { get; init; }
    public required string LogPath { get; init; }
    public required string UserConfigPath { get; init; }
    public required string StartMenuShortcutPath { get; init; }

    /// <summary>Registry sub key below HKCU that holds the autostart value.</summary>
    public required string RunKeyPath { get; init; }

    /// <summary>Registry sub key below HKCU of the "Apps" entry.</summary>
    public required string UninstallKeyPath { get; init; }

    /// <summary>Suffix of mutex and event names; empty in production.</summary>
    public required string NameSuffix { get; init; }

    public string? TestRoot { get; init; }

    /// <summary>A test run never shows anything on the real desktop: no window, message box, balloon or tray icon.</summary>
    public bool Headless => IsTest;

    /// <summary>Full path of the running exe.</summary>
    public static string ExePath => Environment.ProcessPath ?? string.Empty;

    public static string ExeDirectory => Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory;

    public string InstalledExePath => Path.Combine(InstallRoot, ExeFileName);

    public bool IsRunningFromInstallRoot => SamePath(ExeDirectory, InstallRoot);

    public string MutexName => @"Local\PlatinVault.Bridge" + NameSuffix;
    public string ShowStatusEventName => @"Local\PlatinVault.Bridge.ShowStatus" + NameSuffix;
    public string ShowAckEventName => @"Local\PlatinVault.Bridge.ShowAck" + NameSuffix;
    public string QuitEventName => @"Local\PlatinVault.Bridge.Quit" + NameSuffix;

    public static BridgeEnvironment Create(CliOptions cli)
    {
        if (cli.TestRoot is { Length: > 0 } testRoot)
        {
            var root = Path.GetFullPath(testRoot);
            var tag = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToLowerInvariant())))[..8];
            var registryBase = $@"Software\PlatinVaultBridge-Test\{tag}";

            return new BridgeEnvironment
            {
                IsTest = true,
                TestRoot = root,
                InstallRoot = Path.GetFullPath(cli.InstallRoot ?? Path.Combine(root, "install")),
                LogPath = Path.Combine(root, "data", "bridge.log"),
                UserConfigPath = Path.Combine(root, "data", "user-bridge.json"),
                StartMenuShortcutPath = Path.Combine(root, "startmenu", ProductName + ".lnk"),
                RunKeyPath = registryBase + @"\Run",
                UninstallKeyPath = registryBase + @"\Uninstall\" + RegistryName,
                NameSuffix = "-test-" + tag,
            };
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);

        return new BridgeEnvironment
        {
            IsTest = false,
            InstallRoot = Path.GetFullPath(cli.InstallRoot ?? Path.Combine(local, "Programs", ProductName)),
            LogPath = Path.Combine(local, "PlatinVault", "bridge.log"),
            UserConfigPath = Path.Combine(roaming, "PlatinVault", "bridge.json"),
            StartMenuShortcutPath = Path.Combine(programs, ProductName + ".lnk"),
            RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
            UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + RegistryName,
            NameSuffix = string.Empty,
        };
    }

    /// <summary>The hidden options a started copy must inherit so it keeps using the same (test) locations.</summary>
    public IEnumerable<string> InheritedArguments(CliOptions cli)
    {
        if (cli.TestRoot is not null) { yield return "--test-root"; yield return cli.TestRoot; }
        if (cli.InstallRoot is not null) { yield return "--install-root"; yield return cli.InstallRoot; }
        if (cli.Verbose) yield return "--verbose";
        if (cli.NoUpdate) yield return "--no-update";
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
