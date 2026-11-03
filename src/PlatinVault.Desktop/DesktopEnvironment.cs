using PlatinVault.Versioning;
using PlatinVault.Windows.Install;

namespace PlatinVault.Desktop;

/// <summary>Folders of this run: the program folder (where the exe lies), the built UI and the data folder.</summary>
internal sealed class DesktopEnvironment
{
    public required DesktopOptions Options { get; init; }

    public required InstallLayout Layout { get; init; }

    public string InstallDir { get; init; } = AppContext.BaseDirectory.TrimEnd('\\', '/');

    /// <summary>Repository root of a development run (<c>--dev</c>), null otherwise.</summary>
    public string? RepoRoot => Options.Dev is { Length: > 0 } dir ? Path.GetFullPath(dir) : null;


    public string WebRoot => Options.WebRoot is { Length: > 0 } dir ? Path.GetFullPath(dir) : Path.Combine(InstallDir, "wwwroot");

    public DataLayout Data => Layout.Data;

    public bool IsTest => Layout.IsTest;

    public bool IsDev => Layout.IsDev;

    /// <summary>Development only: the browser stack's database file and keys are used instead of the program's own.</summary>
    public SharedStackOptions? Shared => Options.Shared;

    public bool IsSharedDatabase => Options.Shared is not null;

    /// <summary>
    /// Nothing may appear on the real desktop: no tray icon, no hotkey, the window far off-screen, no dialogs. Test runs always,
    /// development runs with <c>--offscreen</c> (automated checks of start-windows.ps1).
    /// </summary>
    public bool Quiet => IsTest || Options.Offscreen;

    /// <summary>
    /// Nothing may be HEARD either (user rule 2026-11-03, after smoke runs played trophy sounds on the real PC): every WebView2 of the
    /// run is muted (CoreWebView2.IsMuted - the page still "plays", so the autoplay check keeps working, but nothing reaches the
    /// speakers) and no Windows notification or dialog is raised. Same runs as <see cref="Quiet"/>.
    /// </summary>
    public bool Silent => Quiet;

    /// <summary>The global hotkey is off in a test run and in a development run (it would take the key from an installed PlatinVault) unless <c>--dev-hotkey</c>.</summary>
    public bool HotkeyAllowed => !Quiet && (!IsDev || Options.DevHotkey);

    /// <summary>The daily look at GitHub releases only makes sense for an installed beta or release; never for a test run, a development run or a dev build.</summary>
    public bool UpdateCheckAllowed => !IsTest && Kind != VersionKind.Dev;

    /// <summary>"Production" for the program, "Development" for a development run; <c>--environment</c> overrides both.</summary>
    public string EnvironmentName => Options.Environment ?? (IsDev ? "Development" : "Production");

    /// <summary>
    /// Content root of the server. The server finds the repository's handbook, licence files and bridge two levels above it in the
    /// Development environment (same convention as the plain dev server in src\Companion.Api), so a development run uses a folder two
    /// levels below the repository root instead of the build output; the installed program uses its own folder.
    /// </summary>
    public string ContentRoot => RepoRoot is { } repo ? Path.Combine(repo, "src", "PlatinVault.Desktop") : InstallDir;

    /// <summary>Folder of the bridge downloads: the program's own <c>bridge</c> folder, in a development run the bridge build output.</summary>
    public string BridgeDir => RepoRoot is { } repo ? Path.Combine(repo, "artifacts", "bridge") : Path.Combine(InstallDir, "bridge");

    /// <summary>"beta", "dev" or "release" as written by scripts/build-windows.ps1 (release-channel.txt; the former "rc" counts as beta); without the file it is a release.</summary>
    public string ReleaseChannel
    {
        get
        {
            var file = Path.Combine(InstallDir, "release-channel.txt");
            var kind = File.Exists(file) ? VersionFormat.KindFromChannel(File.ReadAllText(file)) : VersionKind.Release;
            return VersionFormat.ChannelName(kind);
        }
    }

    /// <summary>The kind of this build: development run or installed dev build (both <see cref="VersionKind.Dev"/>), beta or release.</summary>
    public VersionKind Kind => IsDev ? VersionKind.Dev : VersionFormat.KindFromChannel(ReleaseChannel);

    /// <summary>The version of this build as shown to people, e.g. <c>v2026.11.12 (13.557 β)</c> (see <see cref="VersionFormat"/>).</summary>
    public string Version
    {
        get
        {
            var (shortVersion, buildId) = ReadVersionLines();
            return shortVersion is null ? "v0.0.0-dev" : VersionFormat.Display(shortVersion, buildId, Kind);
        }
    }

    /// <summary>The technical form of this build's version (git tag form, e.g. <c>v2026.11.12.13.557-beta</c>); what the update check compares.</summary>
    public string VersionTag
    {
        get
        {
            var (shortVersion, buildId) = ReadVersionLines();
            return shortVersion is null ? "v0.0.0" : VersionFormat.Tag(shortVersion, buildId, Kind);
        }
    }

    /// <summary>version.txt: line 1 = vYear.Month.Commit, line 2 = the build id with the counters. A development run reads the repository's file.</summary>
    private (string? Short, string? BuildId) ReadVersionLines()
    {
        var file = RepoRoot is { } repo ? Path.Combine(repo, "version.txt") : Path.Combine(InstallDir, "version.txt");
        if (!File.Exists(file)) return (null, null);
        var lines = File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        return lines.Length == 0 ? (null, null) : (lines[0], lines.Length > 1 ? lines[1] : null);
    }

    public static DesktopEnvironment Create(DesktopOptions options)
    {
        InstallLayout layout;
        if (options.Dev is { Length: > 0 } dev)
        {
            var repo = Path.GetFullPath(dev);
            if (!File.Exists(Path.Combine(repo, "src", "Companion.Api", "Companion.Api.csproj")))
            {
                throw new ArgumentException($"--dev needs the PlatinVault repository root (src\\Companion.Api not found in '{repo}').");
            }
            layout = InstallLayout.CreateDev(
                options.DataDir ?? Path.Combine(repo, ".data", "desktop-dev"),
                options.BackupsDir ?? Path.Combine(repo, ".backups", "desktop-dev"));
        }
        else
        {
            layout = InstallLayout.Create(options.TestRoot, options.DataDir);
        }
        return new DesktopEnvironment { Options = options, Layout = layout };
    }
}
