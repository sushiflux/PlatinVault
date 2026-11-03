using System.Globalization;

namespace PlatinVault.Desktop;

/// <summary>
/// Command line of PlatinVault.exe. Normal use has no arguments (or <c>--background</c> from autostart); everything else is for the
/// installer (<c>--prepare</c>, <c>--uninstall</c>), for support (<c>--restore</c>, <c>--backup-now</c>), for development (<c>--dev</c>)
/// and for tests.
/// Every test option needs <c>--test-root &lt;folder&gt;</c>: then all files, the "registry" and the database live in that folder,
/// nothing is shown on the real desktop and nothing of a real installation is touched.
/// <c>--dev &lt;repository&gt;</c> (start-windows.ps1) is the development run: own data folder <c>.data\desktop-dev</c> and backups
/// <c>.backups\desktop-dev</c> in the repository, no registry/autostart/update check, a fixed server port (<c>--server-port</c>),
/// optionally the Vite dev server as the window's UI (<c>--ui-url</c>) and <c>--offscreen</c> for runs without
/// anything on the real desktop. <c>--quit</c> asks the running dev copy to exit (its server makes a last backup).
/// <c>--shared-data-dir</c> (default of start-windows.ps1) makes a development run use the browser stack's database file
/// and its keys instead of its own, see <see cref="SharedStackOptions"/>.
/// </summary>
internal sealed record DesktopOptions
{
    public bool Background { get; init; }
    public string? TestRoot { get; init; }
    public string? DataDir { get; init; }
    public string? WebRoot { get; init; }
    public string? Environment { get; init; }
    public bool Prepare { get; init; }
    public bool Uninstall { get; init; }
    public bool Headless { get; init; }
    public bool DeleteData { get; init; }
    public string? Restore { get; init; }
    public bool BackupNow { get; init; }
    public string? Smoke { get; init; }
    public string? TestLayout { get; init; }
    public bool TestLaunch { get; init; }

    /// <summary>Repository root of a development run (<c>--dev</c>); null for the installed program and for tests.</summary>
    public string? Dev { get; init; }
    public string? BackupsDir { get; init; }
    public int? ServerPort { get; init; }
    public string? UiUrl { get; init; }
    public bool Offscreen { get; init; }
    public bool DevHotkey { get; init; }
    public bool Quit { get; init; }

    /// <summary>Shared development mode (<c>--shared-data-dir</c>): null = own database.</summary>
    public SharedStackOptions? Shared { get; init; }

    public static DesktopOptions Parse(IReadOnlyList<string> args)
    {
        var o = new DesktopOptions();
        for (var i = 0; i < args.Count; i++)
        {
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"Missing value after {args[i]}");
            o = args[i].ToLowerInvariant() switch
            {
                "--background" => o with { Background = true },
                "--test-root" => o with { TestRoot = Value() },
                "--data-dir" => o with { DataDir = Value() },
                "--web-root" => o with { WebRoot = Value() },
                "--environment" => o with { Environment = Value() },
                "--prepare" => o with { Prepare = true },
                "--uninstall" => o with { Uninstall = true },
                "--headless" => o with { Headless = true },
                "--delete-data" => o with { DeleteData = true },
                "--restore" => o with { Restore = Value() },
                "--backup-now" => o with { BackupNow = true },
                "--smoke" => o with { Smoke = Value() },
                "--test-layout" => o with { TestLayout = Value() },
                "--test-launch" => o with { TestLaunch = true },
                "--dev" => o with { Dev = Value() },
                "--backups-dir" => o with { BackupsDir = Value() },
                "--server-port" => o with { ServerPort = Port(args[i], Value()) },
                "--ui-url" => o with { UiUrl = LoopbackUrl(Value()) },
                "--offscreen" => o with { Offscreen = true },
                "--dev-hotkey" => o with { DevHotkey = true },
                "--quit" => o with { Quit = true },
                "--shared-data-dir" => o with { Shared = new SharedStackOptions(Path.GetFullPath(Value())) },
                _ => o,
            };
        }
        if (o.TestRoot is null && (o.Smoke is not null || o.TestLayout is not null || o.Headless || o.TestLaunch))
        {
            throw new ArgumentException("Test options need --test-root <folder>.");
        }
        if (o.Dev is null && (o.BackupsDir is not null || o.ServerPort is not null || o.UiUrl is not null ||
                              o.Offscreen || o.DevHotkey || o.Quit))
        {
            throw new ArgumentException("--backups-dir, --server-port, --ui-url, --offscreen, --dev-hotkey and --quit need --dev <repository>.");
        }
        if (o.Dev is not null)
        {
            // A development run never meets a test root, an installation or the installer's modes
            if (o.TestRoot is not null) throw new ArgumentException("--dev and --test-root exclude each other.");
            if (o.Uninstall || o.Prepare || o.Smoke is not null || o.TestLayout is not null)
            {
                throw new ArgumentException("--dev cannot be combined with --uninstall, --prepare, --smoke or --test-layout.");
            }
        }
        if (o.Shared is not null)
        {
            if (o.Dev is null) throw new ArgumentException("--shared-data-dir needs --dev <repository>.");
        }
        return o;
    }

    private static int Port(string option, string text)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1024 or > 65535)
        {
            throw new ArgumentException($"{option} needs a port between 1024 and 65535, got '{text}'.");
        }
        return port;
    }

    /// <summary>The window may only show a page of this PC (the Vite dev server): http on a loopback host, nothing else.</summary>
    private static string LoopbackUrl(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback)
        {
            throw new ArgumentException($"--ui-url needs an http address on this PC (127.0.0.1 or localhost), got '{text}'.");
        }
        return uri.ToString();
    }
}

/// <summary>
/// Development only: the Windows program works on the SAME data as the browser stack (start.ps1) instead of its own.
/// <paramref name="DataDir"/> = the browser stack's data folder (.data/dev): its database file <c>platinvault.db</c> (both servers open it at
/// the same time - SQLite in WAL mode on one local disk), its key ring (dp-keys) and its encrypted API keys (secrets). Caches stay in the
/// program's own folder.
/// </summary>
internal sealed record SharedStackOptions(string DataDir)
{
    public string KeyRingDir => Path.Combine(DataDir, "dp-keys");

    public string SecretsDir => Path.Combine(DataDir, "secrets");
}
