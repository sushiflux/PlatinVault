using System.Globalization;

namespace PlatinVault.Bridge;

/// <summary>What the user asked the bridge to do.</summary>
internal enum LinuxCommand
{
    /// <summary>Run in the foreground until stopped (what the systemd unit starts).</summary>
    Run,

    /// <summary>Save server and token once and check the connection.</summary>
    Setup,

    /// <summary>Print what the bridge and the service are doing.</summary>
    Status,

    ShowHelp,
    ShowVersion,

    /// <summary>Hidden, for tests: checks the text catalogue.</summary>
    TestTexts,
}

/// <summary>Raw command line values; server and token are validated by <see cref="BridgeSettings"/>.</summary>
internal sealed class LinuxCliOptions
{
    public LinuxCommand Command { get; set; } = LinuxCommand.ShowHelp;

    public string? Server { get; set; }
    public string? Token { get; set; }

    /// <summary><c>setup --token-stdin</c>: read the token from standard input, so it never shows in the process list.</summary>
    public bool TokenFromStdin { get; set; }

    /// <summary>The language forced with <c>--language</c> ('de' or 'en'), null when none was given.</summary>
    public string? Language { get; set; }

    /// <summary>Steam folder chosen by hand (<c>--steam-dir</c>), for installations the automatic search does not find.</summary>
    public string? SteamDir { get; set; }

    /// <summary><c>setup --from &lt;bridge.json&gt;</c>: take server, token and language from a package file instead of the arguments.</summary>
    public string? FromFile { get; set; }

    public bool Verbose { get; set; }
    public bool NoUpdate { get; set; }

    /// <summary><c>setup --no-check</c>: save without testing the connection.</summary>
    public bool NoCheck { get; set; }

    /// <summary>Marker the bridge adds itself when it restarts after a self-update (see <see cref="LinuxSelfUpdater"/>).</summary>
    public string? UpdatedTo { get; set; }

    /// <summary>Hidden, for tests: every file and folder the bridge uses moves below this folder; nothing is ever started for real.</summary>
    public string? TestRoot { get; set; }

    /// <summary>Hidden, for tests: how the recording launcher answers a "start this game" command.</summary>
    public LaunchOutcome? TestLaunch { get; set; }

    /// <summary>Hidden, for tests: behave as if this AppID were running (reads its local achievements, like the Windows bridge).</summary>
    public uint? TestAppId { get; set; }

    public int IntervalMs { get; set; } = DefaultIntervalMs;

    public const int DefaultIntervalMs = 500;
    public const int MinIntervalMs = 100;
    public const int MaxIntervalMs = 10_000;
}

internal static class LinuxCommandLine
{
    /// <summary>
    /// Parses the arguments: an optional command word first (<c>run</c> is the default when only options follow; no argument at all
    /// shows the help), then options as <c>--name value</c> or <c>--name=value</c>. Returns null and sets <paramref name="error"/> on failure.
    /// </summary>
    public static LinuxCliOptions? Parse(IReadOnlyList<string> args, out Msg? error)
    {
        var options = new LinuxCliOptions();
        error = null;

        var start = 0;
        if (args.Count > 0 && !args[0].StartsWith('-'))
        {
            switch (args[0].ToLowerInvariant())
            {
                case "run": options.Command = LinuxCommand.Run; break;
                case "setup": options.Command = LinuxCommand.Setup; break;
                case "status": options.Command = LinuxCommand.Status; break;
                case "help": options.Command = LinuxCommand.ShowHelp; break;
                case "version": options.Command = LinuxCommand.ShowVersion; break;
                default:
                    error = LinuxTexts.Cli.UnknownCommand(args[0]);
                    return null;
            }
            start = 1;
        }
        else if (args.Count > 0)
        {
            options.Command = LinuxCommand.Run;
        }

        for (var i = start; i < args.Count; i++)
        {
            var arg = args[i];

            // Accept both "--name value" and "--name=value"
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
                    options.Command = LinuxCommand.ShowHelp;
                    break;
                case "--version":
                    options.Command = LinuxCommand.ShowVersion;
                    break;
                case "--verbose":
                    options.Verbose = true;
                    break;
                case "--no-update":
                    options.NoUpdate = true;
                    break;
                case "--no-check":
                    options.NoCheck = true;
                    break;
                case "--token-stdin":
                    options.TokenFromStdin = true;
                    break;
                case "--test-texts":
                    options.Command = LinuxCommand.TestTexts;
                    break;
                case "--server":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var server, out error)) return null;
                    options.Server = server;
                    break;
                case "--token":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var token, out error)) return null;
                    options.Token = token;
                    break;
                case "--from":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var from, out error)) return null;
                    options.FromFile = from;
                    break;
                case "--steam-dir":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var steamDir, out error)) return null;
                    options.SteamDir = steamDir;
                    break;
                case "--updated-to":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var updatedTo, out error)) return null;
                    options.UpdatedTo = updatedTo;
                    break;
                case "--test-root":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var testRoot, out error)) return null;
                    options.TestRoot = testRoot;
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
                        error = LinuxTexts.Cli.UnknownOption($"{name} {testLaunch}");
                        return null;
                    }
                    break;
                case "--language":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var rawLanguage, out error)) return null;
                    if (BridgeLang.Normalize(rawLanguage) is not { } language)
                    {
                        error = LinuxTexts.Cli.InvalidLanguage(rawLanguage);
                        return null;
                    }
                    options.Language = language;
                    break;
                case "--interval":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var rawInterval, out error)) return null;
                    if (!int.TryParse(rawInterval, NumberStyles.None, CultureInfo.InvariantCulture, out var interval) ||
                        interval < LinuxCliOptions.MinIntervalMs || interval > LinuxCliOptions.MaxIntervalMs)
                    {
                        error = LinuxTexts.Cli.InvalidInterval(rawInterval, LinuxCliOptions.MinIntervalMs, LinuxCliOptions.MaxIntervalMs);
                        return null;
                    }
                    options.IntervalMs = interval;
                    break;
                case "--app":
                    if (!TryReadValue(args, ref i, inlineValue, name, out var rawApp, out error)) return null;
                    if (!uint.TryParse(rawApp, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) || appId == 0)
                    {
                        error = LinuxTexts.Cli.InvalidApp(rawApp);
                        return null;
                    }
                    options.TestAppId = appId;
                    break;
                default:
                    error = LinuxTexts.Cli.UnknownOption(arg);
                    return null;
            }
        }

        return options;
    }

    private static bool TryReadValue(IReadOnlyList<string> args, ref int index, string? inlineValue, string name,
        out string value, out Msg? error)
    {
        error = null;
        if (inlineValue is not null)
        {
            value = inlineValue;
            return true;
        }

        // The next argument is the value - unless it is missing or looks like another option
        if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[++index];
            return true;
        }

        value = string.Empty;
        error = LinuxTexts.Cli.MissingValue(name);
        return false;
    }
}
