using System.ComponentModel;
using System.Diagnostics;

namespace PlatinVault.Bridge;

/// <summary>Result of handing a program to the system; <c>ExitCode</c> is null while the program still runs (or when nobody waited).</summary>
internal readonly record struct ProcessStartResult(bool Started, int? ExitCode = null, string? Error = null);

/// <summary>The seam to <see cref="Process"/>, so the launch order and the exact arguments can be tested without starting anything.</summary>
internal interface IProcessRunner
{
    /// <param name="wait">How long to wait for the program to end; zero = do not wait (the program may be a whole Steam client).</param>
    ProcessStartResult Start(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> extraEnvironment, TimeSpan wait);
}

internal sealed class SystemProcessRunner : IProcessRunner
{
    public ProcessStartResult Start(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> extraEnvironment, TimeSpan wait)
    {
        var info = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var (key, value) in extraEnvironment) info.Environment[key] = value;

        try
        {
            using var process = Process.Start(info);
            if (process is null) return new ProcessStartResult(false, null, "the program did not start");
            if (wait <= TimeSpan.Zero) return new ProcessStartResult(true);
            return process.WaitForExit(wait) ? new ProcessStartResult(true, process.ExitCode) : new ProcessStartResult(true);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return new ProcessStartResult(false, null, ex.Message);
        }
    }
}

/// <summary>
/// Starts a game on Linux by handing <c>steam://run/&lt;number&gt;</c> (built from a validated integer by <see cref="SteamUrl"/>, never from
/// server text) to the first of: the <c>steam</c> command, Flatpak Steam, <c>xdg-open</c>. Not verified on real devices (the service has no
/// DISPLAY of its own: a missing display gets the fallback <c>:0</c>).
/// <para>
/// If Steam is not running yet, this command IS the Steam client (and the game below it). Started plainly it would stay in the cgroup of the
/// bridge service, and every stop or restart of the service (self-update, install.sh, uninstall.sh) would kill Steam and the game. So the
/// command is started detached: in its own transient scope (<c>systemd-run --user --scope</c>) when available, otherwise in its own session
/// (<c>setsid</c>), and only as a last resort plainly. The wrapper counts as failed when it ends within a short moment with an error (for
/// example no user bus for systemd-run), then the next one is tried; a command that is still running after that moment has started.
/// </para>
/// </summary>
/// <param name="findExecutable">Full path of a program found on PATH, null when there is none.</param>
/// <param name="flatpakSteamInstalled">True when Flatpak Steam's data folder exists.</param>
internal sealed class LinuxSteamLauncher(
    IProcessRunner runner, Func<string, string?> findExecutable, bool flatpakSteamInstalled, Func<string, string?> getEnvironmentVariable) : ISteamLauncher
{
    private static readonly TimeSpan OpenerWait = TimeSpan.FromSeconds(5);

    // How long a start command may take to fail. Steam itself (a whole client) is still running after it, a failing wrapper is not.
    private static readonly TimeSpan SettleWait = TimeSpan.FromMilliseconds(1500);

    public LaunchResult Launch(uint appId)
    {
        var url = SteamUrl.Run(appId);
        var environment = new Dictionary<string, string>();
        // A systemd user service starts without the graphical session variables; Steam needs a display to open a window
        if (string.IsNullOrEmpty(getEnvironmentVariable("DISPLAY")) && string.IsNullOrEmpty(getEnvironmentVariable("WAYLAND_DISPLAY")))
            environment["DISPLAY"] = ":0";

        var attempts = new List<(string Tool, string File, string[] Arguments, TimeSpan Wait)>();
        if (findExecutable("steam") is { } steam) attempts.Add(("steam", steam, [url], SettleWait));
        if (flatpakSteamInstalled && findExecutable("flatpak") is { } flatpak)
            attempts.Add(("flatpak run com.valvesoftware.Steam", flatpak, ["run", "com.valvesoftware.Steam", url], SettleWait));
        if (findExecutable("xdg-open") is { } opener) attempts.Add(("xdg-open", opener, [url], OpenerWait));

        if (attempts.Count == 0)
            return new LaunchResult(LaunchOutcome.SteamNotFound, LinuxTexts.Launcher.NoSteamCommand.En);

        var failures = new List<string>();
        foreach (var (tool, file, arguments, wait) in attempts)
        {
            foreach (var (wrapper, wrapperFile, wrapperArguments) in Wrappers(file, arguments))
            {
                var result = runner.Start(wrapperFile, wrapperArguments, environment, wait);
                if (result.Started && (result.ExitCode is null || result.ExitCode == 0))
                {
                    BridgeLog.Debug(LinuxTexts.Launcher.StartedWith(wrapper is null ? tool : $"{tool} ({wrapper})"));
                    return new LaunchResult(LaunchOutcome.Started);
                }

                var label = wrapper is null ? tool : $"{tool} via {wrapper}";
                failures.Add(result.Started ? $"{label}: exit code {result.ExitCode}" : $"{label}: {result.Error}");
            }
        }

        return new LaunchResult(LaunchOutcome.Failed, string.Join("; ", failures));
    }

    /// <summary>The ways to start a command detached from the service, best first; the last entry runs it plainly.</summary>
    private IEnumerable<(string? Wrapper, string File, string[] Arguments)> Wrappers(string file, string[] arguments)
    {
        if (findExecutable("systemd-run") is { } systemdRun)
        {
            // --scope: the command is exec'd by systemd-run itself inside a new transient scope below the user manager, not below the
            // bridge service; --collect removes the scope when it ends (also when it failed)
            yield return ("systemd-run", systemdRun, ["--user", "--scope", "--collect", "--quiet", file, .. arguments]);
        }
        if (findExecutable("setsid") is { } setsid)
            yield return ("setsid", setsid, [file, .. arguments]);
        yield return (null, file, arguments);
    }

    /// <summary>Looks a program up on PATH (the first directory that holds a file of that name).</summary>
    public static string? FindOnPath(string name, Func<string, string?> getEnvironmentVariable)
    {
        foreach (var directory in (getEnvironmentVariable("PATH") ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
