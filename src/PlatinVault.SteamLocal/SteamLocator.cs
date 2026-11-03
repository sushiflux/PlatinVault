using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PlatinVault.SteamLocal;

/// <summary>The operating system whose Steam layout is read. Tests pass it explicitly to run the Linux rules on any machine.</summary>
public enum SteamPlatform
{
    Other,
    Windows,
    Linux,
}

/// <summary>How the Steam folder in use was found (also the kind of a <see cref="SteamRootCandidate"/>).</summary>
public enum SteamLocationMethod
{
    /// <summary>Nothing found.</summary>
    None,

    /// <summary>Set by hand: the setting "Steam-Ordner" of the server or a bridge's own option.</summary>
    Manual,

    /// <summary>Given by the host's configuration (server: <c>Companion:Steam:Root</c>, e.g. a Docker environment variable).</summary>
    Configuration,

    /// <summary>An environment variable: <c>PLATINVAULT_STEAM_DIR</c> (every platform) or <c>STEAM_DIR</c> (Linux, as protontricks).</summary>
    Environment,

    /// <summary>Windows: <c>HKCU\Software\Valve\Steam\SteamPath</c> (the behaviour PlatinVault always had).</summary>
    Registry,

    /// <summary>Linux: a known path that is a symbolic link (<c>~/.steam/steam</c>, <c>~/.steam/root</c>), resolved to its target.</summary>
    Symlink,

    /// <summary>Linux: a known installation path (native, Debian package, Flatpak, Snap).</summary>
    KnownPath,
}

/// <summary>One place where Steam was looked for, as shown in the system check.</summary>
/// <param name="Path">The path as looked at (explicit paths normalized, <c>~</c> expanded).</param>
/// <param name="Method">What kind of candidate it is; for the Linux known paths <see cref="SteamLocationMethod.Symlink"/> when the path resolved through a link.</param>
/// <param name="Detail">Where it came from: the variable name, <c>native</c> / <c>flatpak</c> / <c>snap</c>, the registry value, ...</param>
/// <param name="ResolvedPath">Linux: the path with every symbolic link resolved (null for a link loop); Windows: the path itself.</param>
/// <param name="Exists">The (resolved) folder exists.</param>
/// <param name="HasSteamApps">It contains a <c>steamapps</c> folder (as steamlocate checks).</param>
/// <param name="IsDuplicate">An earlier candidate resolved to the same folder.</param>
/// <param name="Chosen">This candidate is the Steam folder in use.</param>
/// <param name="IsSteamRoot">
/// It is the Steam client's own folder, not just a game library: <c>steamapps</c> plus <c>appcache</c> or <c>config</c> (see
/// <see cref="SteamLocator.IsSteamRoot"/>). Only a Steam root has the achievement stats (<c>appcache/stats</c>).
/// </param>
public sealed record SteamRootCandidate(
    string Path,
    SteamLocationMethod Method,
    string? Detail,
    string? ResolvedPath,
    bool Exists,
    bool HasSteamApps,
    bool IsDuplicate,
    bool Chosen,
    bool IsSteamRoot);

/// <summary>Where the local Steam client lives (or that none was found) and every place that was looked at.</summary>
/// <param name="Platform">The rules that were applied.</param>
/// <param name="Root">The Steam folder in use; null when none was found.</param>
/// <param name="Method">How <see cref="Root"/> was found (<see cref="SteamLocationMethod.None"/> without one).</param>
/// <param name="Candidates">Every candidate in the order of precedence, the chosen one marked.</param>
public sealed record SteamLocation(SteamPlatform Platform, string? Root, SteamLocationMethod Method, IReadOnlyList<SteamRootCandidate> Candidates)
{
    public bool Found => Root is not null;

    /// <summary>
    /// Reading Steam's files on Linux follows the Windows file formats and has not been checked on a real Linux Steam yet
    /// (location and live update of <c>UserGameStats_*.bin</c>, <c>appinfo.vdf</c>, the active account) - shown as "experimental".
    /// </summary>
    public bool IsExperimental => Platform == SteamPlatform.Linux;
}

/// <summary>Why a folder is not accepted as the Steam folder (empty = it is).</summary>
public enum SteamRootProblem
{
    None,

    /// <summary>Not an absolute path (a relative path would depend on the working directory of the process).</summary>
    NotAbsolute,

    /// <summary>The folder does not exist (or is a broken / looping link).</summary>
    NotFound,

    /// <summary>The folder exists, but has no <c>steamapps</c> folder - usually one level too high or too low.</summary>
    NoSteamApps,

    /// <summary>
    /// A game library (e.g. <c>D:\SteamLibrary</c>): it has <c>steamapps</c>, but neither <c>appcache</c> nor <c>config</c> - the Steam
    /// client lives elsewhere, and without its <c>appcache/stats</c> there is no live detection.
    /// </summary>
    LibraryOnly,
}

/// <summary>Result of <see cref="SteamLocator.CheckRoot"/>.</summary>
/// <param name="Path">The normalized path that was checked (what is stored when it is accepted).</param>
/// <param name="ResolvedPath">The resolved folder (Linux: links resolved), null when it could not be resolved.</param>
public sealed record SteamRootCheck(string Path, string? ResolvedPath, SteamRootProblem Problem)
{
    public bool IsValid => Problem == SteamRootProblem.None;
}

/// <summary>The few file system calls the locator needs - a fake in tests (symbolic links need privileges on Windows).</summary>
public interface ISteamFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    /// <summary>The immediate target of a symbolic link (as stored, may be relative), or null when <paramref name="path"/> is no link.</summary>
    string? ReadLink(string path);
}

/// <summary>The real file system.</summary>
public sealed class PhysicalSteamFileSystem : ISteamFileSystem
{
    public static readonly PhysicalSteamFileSystem Instance = new();

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public string? ReadLink(string path)
    {
        try
        {
            // FileInfo reads the link itself (readlink / the reparse point) for file and directory links alike
            return new FileInfo(path).LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>Everything the locator reads from its surroundings; every part can be replaced in tests.</summary>
public sealed record SteamLocatorInput
{
    public SteamPlatform Platform { get; init; } = SteamLocator.CurrentPlatform;

    /// <summary>Set by hand (highest precedence). Null or blank = not set.</summary>
    public string? ManualRoot { get; init; }

    /// <summary>From the host's configuration (server: <c>Companion:Steam:Root</c>). Null or blank = not set.</summary>
    public string? ConfiguredRoot { get; init; }

    /// <summary>Reads an environment variable (default: the process environment).</summary>
    public Func<string, string?> Environment { get; init; } = System.Environment.GetEnvironmentVariable;

    /// <summary>The user's home folder; null = <c>HOME</c>, else the user profile folder.</summary>
    public string? HomeDirectory { get; init; }

    /// <summary>Windows: reads <c>HKCU\Software\Valve\Steam\SteamPath</c>; null = the real registry.</summary>
    public Func<string?>? RegistrySteamPath { get; init; }

    public ISteamFileSystem FileSystem { get; init; } = PhysicalSteamFileSystem.Instance;
}

/// <summary>
/// Finds the Steam installation of this computer. Order of precedence: set by hand, host configuration, the environment variable
/// <c>PLATINVAULT_STEAM_DIR</c> (on Linux also <c>STEAM_DIR</c>), then per platform:
/// <list type="bullet">
/// <item>Windows: the registry value <c>SteamPath</c> - accepted as it is, exactly like PlatinVault always did (no check for
/// <c>steamapps</c>, so the Windows behaviour is unchanged).</item>
/// <item>Linux: the candidates of the established tools (protontricks, steamlocate, Lutris; sources in the project's research notes):
/// the symbolic links <c>~/.steam/steam</c> and <c>~/.steam/root</c> first (Steam's starter points them at the installation it
/// uses), then <c>$XDG_DATA_HOME/Steam</c>, <c>~/.local/share/Steam</c>, <c>~/.steam/debian-installation</c>, Flatpak and Snap.
/// Every link is resolved, the same folder reached twice counts once, and a folder only counts with a <c>steamapps</c> folder.</item>
/// </list>
/// An explicit path (hand, configuration, variable) is skipped when it is not usable, so a stale setting falls back to the search.
/// </summary>
public static class SteamLocator
{
    /// <summary>Our own variable; wins over <see cref="SteamDirVariable"/>.</summary>
    public const string PlatinVaultSteamDirVariable = "PLATINVAULT_STEAM_DIR";

    /// <summary>The variable protontricks reads ("export $STEAM_DIR to the directory where Steam is"); Linux only.</summary>
    public const string SteamDirVariable = "STEAM_DIR";

    /// <summary>Shown as the detail of the registry candidate.</summary>
    public const string RegistryValueName = @"HKCU\Software\Valve\Steam\SteamPath";

    private const string FlatpakApp = ".var/app/com.valvesoftware.Steam";

    // Link chains longer than this are treated as a loop (Linux allows 40 links in one lookup, see path_resolution(7))
    private const int MaxLinkHops = 40;

    public static SteamPlatform CurrentPlatform =>
        OperatingSystem.IsWindows() ? SteamPlatform.Windows
        : OperatingSystem.IsLinux() ? SteamPlatform.Linux
        : SteamPlatform.Other;

    /// <summary>Looks for Steam; never throws (an unreadable place just does not count).</summary>
    public static SteamLocation Locate(SteamLocatorInput input)
    {
        var platform = input.Platform;
        var fs = input.FileSystem;
        var home = HomeOf(input);
        if (platform == SteamPlatform.Linux && home is not null && RealPath(home, fs) is { } realHome)
        {
            // Fedora Silverblue / Bazzite: /home is a link to /var/home. Resolving the home folder first keeps the standard paths
            // "standard paths" - only a link below the home folder (~/.steam/steam) makes a candidate a "Symlink".
            home = realHome;
        }
        var comparer = PathComparer(platform);

        var raw = new List<(string Path, SteamLocationMethod Method, string? Detail)>();
        AddExplicit(raw, input.ManualRoot, SteamLocationMethod.Manual, "setting", platform, home);
        AddExplicit(raw, input.ConfiguredRoot, SteamLocationMethod.Configuration, "configuration", platform, home);
        AddExplicit(raw, Read(input, PlatinVaultSteamDirVariable), SteamLocationMethod.Environment, PlatinVaultSteamDirVariable, platform, home);
        if (platform == SteamPlatform.Linux)
        {
            AddExplicit(raw, Read(input, SteamDirVariable), SteamLocationMethod.Environment, SteamDirVariable, platform, home);
        }

        string? registryPath = null;
        if (platform == SteamPlatform.Windows)
        {
            registryPath = (input.RegistrySteamPath ?? ReadRegistrySteamPath)();
            if (!string.IsNullOrEmpty(registryPath))
            {
                // Identical to the earlier GetSteamPath(): Steam stores forward slashes, Windows paths use backslashes
                raw.Add((registryPath.Replace('/', '\\'), SteamLocationMethod.Registry, RegistryValueName));
            }
        }
        else if (platform == SteamPlatform.Linux && home is not null)
        {
            foreach (var (path, detail) in LinuxKnownPaths(home, Read(input, "XDG_DATA_HOME")))
            {
                raw.Add((path, SteamLocationMethod.KnownPath, detail));
            }
        }

        var candidates = new List<SteamRootCandidate>(raw.Count);
        var seen = new HashSet<string>(comparer);
        string? root = null;
        var method = SteamLocationMethod.None;
        foreach (var (path, kind, detail) in raw)
        {
            var resolved = platform == SteamPlatform.Linux ? RealPath(path, fs) : path;
            var exists = resolved is not null && SafeDirectoryExists(fs, resolved);
            var hasSteamApps = exists && SafeDirectoryExists(fs, Join(platform, resolved!, "steamapps"));
            var steamRoot = hasSteamApps && IsSteamRoot(fs, platform, resolved!);
            var effectiveKind = kind == SteamLocationMethod.KnownPath && resolved is not null && !string.Equals(resolved, path, StringComparison.Ordinal)
                ? SteamLocationMethod.Symlink
                : kind;
            var duplicate = resolved is not null && exists && !seen.Add(resolved);
            // The registry value is taken as it is (unchanged Windows behaviour); everything else needs the Steam client's own folder -
            // a game library (only steamapps) would win over the registry and silently switch the live detection off
            var usable = kind == SteamLocationMethod.Registry || steamRoot;
            var chosen = root is null && usable && !duplicate;
            if (chosen)
            {
                root = kind == SteamLocationMethod.Registry ? path : resolved;
                method = effectiveKind;
            }
            candidates.Add(new SteamRootCandidate(path, effectiveKind, detail, resolved, exists, hasSteamApps, duplicate, chosen, steamRoot));
        }

        return new SteamLocation(platform, root, method, candidates);
    }

    /// <summary>
    /// Checks a folder given by hand (the setting "Steam-Ordner"): absolute, exists, is the Steam client's own folder (see
    /// <see cref="IsSteamRoot"/>; a game library with only <c>steamapps</c> is refused). The returned <see cref="SteamRootCheck.Path"/>
    /// is the normalized form to store.
    /// </summary>
    public static SteamRootCheck CheckRoot(string path, SteamLocatorInput input)
    {
        var normalized = NormalizeExplicitPath(path, input.Platform, HomeOf(input)) ?? string.Empty;
        if (normalized.Length == 0 || !IsAbsolute(normalized, input.Platform))
        {
            return new SteamRootCheck(normalized.Length == 0 ? path.Trim() : normalized, null, SteamRootProblem.NotAbsolute);
        }
        var resolved = input.Platform == SteamPlatform.Linux ? RealPath(normalized, input.FileSystem) : normalized;
        if (resolved is null || !SafeDirectoryExists(input.FileSystem, resolved))
        {
            return new SteamRootCheck(normalized, resolved, SteamRootProblem.NotFound);
        }
        if (!SafeDirectoryExists(input.FileSystem, Join(input.Platform, resolved, "steamapps")))
        {
            return new SteamRootCheck(normalized, resolved, SteamRootProblem.NoSteamApps);
        }
        return IsSteamRoot(input.FileSystem, input.Platform, resolved)
            ? new SteamRootCheck(normalized, resolved, SteamRootProblem.None)
            : new SteamRootCheck(normalized, resolved, SteamRootProblem.LibraryOnly);
    }

    /// <summary>
    /// The Steam client's own folder, not a game library: besides <c>steamapps</c> (checked by the caller) it has <c>appcache</c>
    /// (appinfo.vdf, stats) or <c>config</c> (loginusers.vdf, libraryfolders.vdf). The Windows code reads <c>appcache/appinfo.vdf</c>
    /// and <c>appcache/stats</c>, Lutris and game-detector read <c>config/libraryfolders.vdf</c>, accshift reads <c>config/loginusers.vdf</c>
    /// (sources in the project's Linux research notes). An extra game library (<c>D:\SteamLibrary</c>) is not expected to have either
    /// (assumption, not checked against a real library folder here). Either marker is enough, so an install that has not written one of
    /// them yet still counts.
    /// </summary>
    public static bool IsSteamRoot(ISteamFileSystem fs, SteamPlatform platform, string folder) =>
        SafeDirectoryExists(fs, Join(platform, folder, "appcache")) || SafeDirectoryExists(fs, Join(platform, folder, "config"));

    /// <summary>
    /// The form an explicit path is used in: trimmed, surrounding quotes removed, a leading <c>~</c> expanded to the home folder
    /// (Linux), backslashes on Windows, no trailing separator. Null for a blank value.
    /// </summary>
    public static string? NormalizeExplicitPath(string? value, SteamPlatform platform, string? home)
    {
        var path = value?.Trim();
        if (string.IsNullOrEmpty(path)) return null;
        if (path.Length >= 2 && ((path[0] == '"' && path[^1] == '"') || (path[0] == '\'' && path[^1] == '\'')))
        {
            path = path[1..^1].Trim();
            if (path.Length == 0) return null;
        }

        if (platform == SteamPlatform.Linux)
        {
            if (home is not null && (path == "~" || path.StartsWith("~/", StringComparison.Ordinal)))
            {
                path = Join(platform, home, path.Length > 2 ? path[2..] : string.Empty);
            }
            while (path.Length > 1 && path.EndsWith('/')) path = path[..^1];
            return path;
        }

        if (platform == SteamPlatform.Windows)
        {
            path = path.Replace('/', '\\');
            // Keep "C:\" (a drive root needs its separator), strip it everywhere else
            while (path.Length > 3 && path.EndsWith('\\')) path = path[..^1];
        }
        return path;
    }

    /// <summary>
    /// Linux <c>realpath</c> without touching the disk beyond reading links: every component that is a symbolic link is replaced by
    /// its target (relative targets against the link's folder), <c>.</c> and <c>..</c> are folded. Null for a link loop or a
    /// relative input. Components that do not exist are kept as they are (the caller checks existence).
    /// </summary>
    public static string? RealPath(string path, ISteamFileSystem fs)
    {
        if (!path.StartsWith('/')) return null;

        var pending = new LinkedList<string>(path.Split('/', StringSplitOptions.RemoveEmptyEntries));
        var resolved = new List<string>();
        var hops = 0;
        while (pending.First is { } node)
        {
            var part = node.Value;
            pending.RemoveFirst();
            if (part == ".") continue;
            if (part == "..")
            {
                if (resolved.Count > 0) resolved.RemoveAt(resolved.Count - 1);
                continue;
            }

            var current = "/" + string.Join('/', resolved.Append(part));
            string? target;
            try
            {
                target = fs.ReadLink(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                target = null;
            }
            if (target is null)
            {
                resolved.Add(part);
                continue;
            }

            if (++hops > MaxLinkHops) return null;
            if (target.StartsWith('/')) resolved.Clear();
            var targetParts = target.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = targetParts.Length - 1; i >= 0; i--)
            {
                pending.AddFirst(targetParts[i]);
            }
        }
        return "/" + string.Join('/', resolved);
    }

    /// <summary>The Linux known paths in the order they are tried (see the class summary), with their kind.</summary>
    public static IReadOnlyList<(string Path, string Detail)> LinuxKnownPaths(string home, string? xdgDataHome)
    {
        var p = SteamPlatform.Linux;
        var defaultData = Join(p, home, ".local/share");
        // XDG_DATA_HOME only counts when it is absolute (XDG Base Directory spec: relative values are to be ignored)
        var data = !string.IsNullOrWhiteSpace(xdgDataHome) && xdgDataHome.StartsWith('/') ? xdgDataHome.TrimEnd('/') : defaultData;
        var list = new List<(string, string)>
        {
            (Join(p, home, ".steam/steam"), "native"),
            (Join(p, home, ".steam/root"), "native"),
            (Join(p, data, "Steam"), "native"),
        };
        if (!string.Equals(data, defaultData, StringComparison.Ordinal))
        {
            list.Add((Join(p, defaultData, "Steam"), "native"));
        }
        list.Add((Join(p, home, ".steam/debian-installation"), "native"));
        list.Add((Join(p, home, FlatpakApp + "/.local/share/Steam"), "flatpak"));
        list.Add((Join(p, home, FlatpakApp + "/.steam/steam"), "flatpak"));
        list.Add((Join(p, home, FlatpakApp + "/.steam/root"), "flatpak"));
        list.Add((Join(p, home, "snap/steam/common/.local/share/Steam"), "snap"));
        list.Add((Join(p, home, ".snap/data/steam/common/.local/share/Steam"), "snap"));
        return list;
    }

    /// <summary>Joins path parts with the separator of <paramref name="platform"/> (never the one of the machine the code runs on).</summary>
    public static string Join(SteamPlatform platform, string first, string second)
    {
        if (second.Length == 0) return first;
        if (platform == SteamPlatform.Linux)
        {
            return first.EndsWith('/') ? first + second : first + "/" + second;
        }
        return Path.Combine(first, second);
    }

    /// <summary>Paths are case-sensitive on Linux, not on Windows.</summary>
    public static StringComparer PathComparer(SteamPlatform platform) =>
        platform == SteamPlatform.Linux ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    internal static string? HomeOf(SteamLocatorInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.HomeDirectory)) return input.HomeDirectory.TrimEnd('/', '\\') is { Length: > 0 } h ? h : input.HomeDirectory;
        var home = Read(input, "HOME");
        if (!string.IsNullOrWhiteSpace(home)) return home.Length > 1 ? home.TrimEnd('/') : home;
        var profile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile) ? null : profile;
    }

    private static bool IsAbsolute(string path, SteamPlatform platform) =>
        platform == SteamPlatform.Linux ? path.StartsWith('/') : Path.IsPathFullyQualified(path);

    private static void AddExplicit(List<(string, SteamLocationMethod, string?)> list, string? value, SteamLocationMethod method, string detail, SteamPlatform platform, string? home)
    {
        if (NormalizeExplicitPath(value, platform, home) is { } path)
        {
            list.Add((path, method, detail));
        }
    }

    private static string? Read(SteamLocatorInput input, string variable)
    {
        try
        {
            return input.Environment(variable);
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }

    private static bool SafeDirectoryExists(ISteamFileSystem fs, string path)
    {
        try
        {
            return fs.DirectoryExists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string? ReadRegistrySteamPath() => OperatingSystem.IsWindows() ? ReadRegistryValue(@"Software\Valve\Steam", "SteamPath") : null;

    [SupportedOSPlatform("windows")]
    internal static string? ReadRegistryValue(string subKey, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            return key?.GetValue(name)?.ToString();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return null;
        }
    }
}
