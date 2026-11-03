using System.Globalization;

namespace PlatinVault.Bridge;

/// <summary>Raw command line values; validation of server/token happens in <see cref="BridgeSettings"/>.</summary>
internal sealed class CliOptions
{
    public string? Server { get; set; }
    public string? Token { get; set; }
    public bool Save { get; set; }
    public bool Verbose { get; set; }

    /// <summary>Disables the self-update (diagnostics, or a deliberately pinned version).</summary>
    public bool NoUpdate { get; set; }

    /// <summary>
    /// Internal: set by the bridge itself when it restarts after a self-update. The new instance then never updates to
    /// this version (or lower) again, which stops a download loop if the server offers an exe that reports an older version.
    /// </summary>
    public string? UpdatedTo { get; set; }
    /// <summary>Internal: run from where the exe is (tray only), never offer the installer (also used by tests and scripts).</summary>
    public bool NoSetup { get; set; }

    /// <summary>Internal: set by the installer when it starts the installed copy, which then shows the one-time balloon.</summary>
    public bool FirstRun { get; set; }

    /// <summary>The Windows "Apps" uninstall entry runs the exe with this option.</summary>
    public bool Uninstall { get; set; }

    /// <summary>Hidden, for tests: install here instead of the PlatinVault Bridge folder below %LOCALAPPDATA%\Programs.</summary>
    public string? InstallRoot { get; set; }

    /// <summary>
    /// Hidden, for tests: keeps everything the bridge writes (install folder, log, Start-menu shortcut, registry keys, mutex and
    /// event names) inside this folder / a separate registry key, so a test never touches the real installation.
    /// </summary>
    public string? TestRoot { get; set; }


    /// <summary>Hidden, for tests: write a picture of the tray menu to this PNG file a few seconds after the start.</summary>
    public string? TestSnapshotMenu { get; set; }

    /// <summary>
    /// Hidden, for tests (needs --test-root): renders every window in both languages offscreen and measures that no text is clipped or
    /// overlaps at 100, 125, 150, 200 and 250 percent Windows scaling; pictures go to the given folder. See <see cref="Ui.LayoutCheck"/>.
    /// </summary>
    public string? TestLayout { get; set; }

    /// <summary>Hidden, for tests: checks that every entry of the text catalogue has a German and an English text, then exits. See <see cref="TextCatalogCheck"/>.</summary>
    public bool TestTexts { get; set; }

    /// <summary>
    /// Hidden, for tests (needs --test-root): runs a scripted, invisible UI scenario instead of the normal start - windows are
    /// created but never shown, and rendered into PNG files in the test root. See <see cref="Ui.HeadlessUi"/>.
    /// </summary>
    public string? TestUi { get; set; }

    /// <summary>
    /// Hidden, for tests (needs --test-root): how the fake launcher answers a "start this game" command - "started" (default),
    /// "steam-missing" or "failed". A test run never opens steam:// at all; it only writes the address it would have opened to
    /// <c>&lt;test root&gt;\ui\launches.txt</c>. See <see cref="RecordingLauncher"/>.
    /// </summary>
    public LaunchOutcome? TestLaunch { get; set; }

    /// <summary>The language forced with <c>--language</c> ('de' or 'en'), null when none was given.</summary>
    public string? Language { get; set; }

    public bool ShowHelp { get; set; }
    public bool ShowVersion { get; set; }
    public int IntervalMs { get; set; } = DefaultIntervalMs;

    /// <summary>Diagnostic: behave as if this AppID were running (reads its local stats without launching the game).</summary>
    public uint? TestAppId { get; set; }

    public const int DefaultIntervalMs = 350;
    public const int MinIntervalMs = 100;
    public const int MaxIntervalMs = 10_000;
}

internal static class CommandLine
{
    /// <summary>Parses the arguments. Returns null and sets <paramref name="error"/> (user-facing, in the current language) on failure.</summary>
    public static CliOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        var options = new CliOptions();
        error = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            // Accept both "--name value" and "--name=value".
            string? inlineValue = null;
            var name = arg;
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var eq = arg.IndexOf('=', StringComparison.Ordinal);
                if (eq > 0)
                {
                    name = arg[..eq];
                    inlineValue = arg[(eq + 1)..];
                }
            }

            switch (name.ToLowerInvariant())
            {
                case "--help":
                case "-h":
                case "-?":
                case "/?":
                    options.ShowHelp = true;
                    break;
                case "--version":
                    options.ShowVersion = true;
                    break;
                case "--save":
                    options.Save = true;
                    break;
                case "--no-update":
                    options.NoUpdate = true;
                    break;
                case "--updated-to":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var updatedTo, out error)) return null;
                    options.UpdatedTo = updatedTo;
                    break;
                case "--no-setup":
                    options.NoSetup = true;
                    break;
                case "--first-run":
                    options.FirstRun = true;
                    break;
                case "--uninstall":
                    options.Uninstall = true;
                    break;
                case "--install-root":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var installRoot, out error)) return null;
                    options.InstallRoot = installRoot;
                    break;
                case "--test-root":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var testRoot, out error)) return null;
                    options.TestRoot = testRoot;
                    break;
                case "--test-ui":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var testUi, out error)) return null;
                    options.TestUi = testUi;
                    break;
                case "--test-layout":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var testLayout, out error)) return null;
                    options.TestLayout = testLayout;
                    break;
                case "--test-texts":
                    options.TestTexts = true;
                    break;
                case "--test-launch":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var testLaunch, out error)) return null;
                    options.TestLaunch = testLaunch.ToLowerInvariant() switch
                    {
                        "started" => LaunchOutcome.Started,
                        "steam-missing" => LaunchOutcome.SteamNotFound,
                        "failed" => LaunchOutcome.Failed,
                        _ => null,
                    };
                    if (options.TestLaunch is null)
                    {
                        error = BridgeTexts.Cli.UnknownOption($"{name} {testLaunch}");
                        return null;
                    }
                    break;
                case "--language":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var rawLanguage, out error)) return null;
                    if (BridgeLang.Normalize(rawLanguage) is not { } language)
                    {
                        error = BridgeTexts.Cli.InvalidLanguage(rawLanguage);
                        return null;
                    }
                    options.Language = language;
                    break;
                case "--test-snapshot-menu":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var snapshotMenu, out error)) return null;
                    options.TestSnapshotMenu = snapshotMenu;
                    break;
                case "--verbose":
                    options.Verbose = true;
                    break;
                case "--server":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var server, out error)) return null;
                    options.Server = server;
                    break;
                case "--token":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var token, out error)) return null;
                    options.Token = token;
                    break;
                case "--interval":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var rawInterval, out error)) return null;
                    if (!int.TryParse(rawInterval, NumberStyles.None, CultureInfo.InvariantCulture, out var interval) ||
                        interval < CliOptions.MinIntervalMs || interval > CliOptions.MaxIntervalMs)
                    {
                        error = BridgeTexts.Cli.InvalidInterval(rawInterval, CliOptions.MinIntervalMs, CliOptions.MaxIntervalMs);
                        return null;
                    }
                    options.IntervalMs = interval;
                    break;
                case "--app":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var rawApp, out error)) return null;
                    if (!uint.TryParse(rawApp, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) || appId == 0)
                    {
                        error = BridgeTexts.Cli.InvalidApp(rawApp);
                        return null;
                    }
                    options.TestAppId = appId;
                    break;
                default:
                    error = BridgeTexts.Cli.UnknownOption(arg);
                    return null;
            }
        }

        return options;
    }

    private static bool TryReadValue(IReadOnlyList<string> args, ref int index, string? inlineValue, string name,
        out string value, out string? error)
    {
        error = null;
        if (inlineValue is not null)
        {
            value = inlineValue;
            return true;
        }

        // The next argument is the value - unless it is missing or looks like another option.
        if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[++index];
            return true;
        }

        value = string.Empty;
        error = BridgeTexts.Cli.MissingValue(name);
        return false;
    }
}
