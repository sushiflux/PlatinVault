using System.Diagnostics;
using System.Runtime.InteropServices;
using PlatinVault.SteamLocal;

namespace PlatinVault.Bridge;

/// <summary>
/// The Linux bridge as a function: parse the command line, set the language and the log up, run the command. <c>Program.Main</c> only
/// calls <see cref="RunAsync"/>; tests call it with their own environment, stop token and (for network tests) fake servers.
/// Exit codes: 0 fine / clean stop, 1 runtime problem, 2 wrong usage, 78 (EX_CONFIG) configuration missing or invalid - the systemd unit
/// does not restart on 78, because restarting cannot fix it.
/// </summary>
internal static class LinuxApp
{
    public const int ExitOk = 0;
    public const int ExitFailed = 1;
    public const int ExitUsage = 2;
    public const int ExitConfig = 78;

    public static async Task<int> RunAsync(string[] args, Func<string, string?> getEnvironmentVariable, CancellationToken stop)
    {
        BridgeLang.UseSystem(LinuxEnvironment.SystemLanguage(getEnvironmentVariable));
        BridgeLog.InitConsole(available: true);

        var cli = LinuxCommandLine.Parse(args, out var parseError);
        if (cli is null)
        {
            BridgeLog.Error(parseError!);
            BridgeLog.Print(LinuxTexts.Cli.UsageHint);
            return ExitUsage;
        }

        // --language wins over everything; the stored language (bridge.json) and later the server's language come after it
        if (cli.Language is not null) BridgeLang.Pin(cli.Language);

        var env = LinuxEnvironment.Create(cli.TestRoot, getEnvironmentVariable);
        BridgeLog.LogPath = env.LogPath;
        BridgeLog.Verbose = cli.Verbose;

        // Read before --help/--version so that even they speak the language this bridge was configured with
        var portable = BridgeConfigFile.Load(LinuxEnvironment.PortableConfigPath, out var portableWarning);
        if (portableWarning is not null) BridgeLog.Warn(portableWarning);
        var user = BridgeConfigFile.Load(env.ConfigPath, out var userWarning);
        if (userWarning is not null) BridgeLog.Warn(userWarning);
        BridgeLang.UseStored(portable?.Language ?? user?.Language);

        var version = GetVersion();

        switch (cli.Command)
        {
            case LinuxCommand.ShowVersion:
                BridgeLog.Print($"{LinuxEnvironment.BinaryName} {version}");
                return ExitOk;
            case LinuxCommand.ShowHelp:
                BridgeLog.Print(LinuxTexts.Cli.Help(version, env.ConfigPath, env.LogPath, env.InstallDirectory));
                return ExitOk;
            case LinuxCommand.TestTexts:
                return TextCatalog.Run([typeof(LinuxTexts), typeof(CoreTexts)]);
            case LinuxCommand.Status:
                return LinuxStatusCommand.Run(cli, env, portable, user, version, getEnvironmentVariable);
            case LinuxCommand.Setup:
                return await LinuxSetupCommand.RunAsync(cli, env, portable, user, version, getEnvironmentVariable, stop).ConfigureAwait(false);
            default:
                return await RunServiceAsync(cli, env, portable, user, version, getEnvironmentVariable, args, stop).ConfigureAwait(false);
        }
    }

    /// <summary>Stops the run on SIGINT, SIGTERM and SIGHUP. A signal the platform does not know (a test on Windows) is simply not registered.</summary>
    private sealed class SignalRegistrations : IDisposable
    {
        private readonly List<PosixSignalRegistration> _registrations = [];

        public SignalRegistrations(CancellationTokenSource cts)
        {
            foreach (var signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP })
            {
                try
                {
                    _registrations.Add(PosixSignalRegistration.Create(signal, context => { context.Cancel = true; cts.Cancel(); }));
                }
                catch (PlatformNotSupportedException)
                {
                    // not available on this platform
                }
            }
        }

        public void Dispose()
        {
            foreach (var registration in _registrations) registration.Dispose();
        }
    }

    /// <summary>The Steam reader of this run: the scripted file in a test root, otherwise the real one with the chosen Steam folder.</summary>
    internal static ILocalSteam CreateSteam(LinuxCliOptions cli, LinuxEnvironment env, StoredBridgeConfig? portable, StoredBridgeConfig? user,
        Func<string, string?> getEnvironmentVariable)
    {
        if (env.IsTest && File.Exists(env.FakeSteamPath)) return new FakeLocalSteam(env.FakeSteamPath);
        return new SteamLocalSource(CreateLocation(cli, portable, user, getEnvironmentVariable));
    }

    internal static SteamLocationProvider CreateLocation(LinuxCliOptions cli, StoredBridgeConfig? portable, StoredBridgeConfig? user,
        Func<string, string?> getEnvironmentVariable)
    {
        var input = new SteamLocatorInput
        {
            ManualRoot = cli.SteamDir ?? portable?.SteamDir ?? user?.SteamDir,
            Environment = getEnvironmentVariable,
        };
        // "run" logs the folder it starts with itself (LogSteamDiagnosis): the provider only logs later changes
        return new SteamLocationProvider(input, new BridgeLogger<SteamLocationProvider>(), logInitialResult: false);
    }

    private static async Task<int> RunServiceAsync(LinuxCliOptions cli, LinuxEnvironment env, StoredBridgeConfig? portable, StoredBridgeConfig? user,
        string version, Func<string, string?> getEnvironmentVariable, string[] originalArgs, CancellationToken stop)
    {
        if (!BridgeSettings.TryResolve(cli.Server, cli.Token, getEnvironmentVariable, portable, user, out var settings, out var configError, linux: true))
        {
            BridgeLog.Error(configError!);
            return ExitConfig;
        }

        // One bridge per user: the service and a manual `run` must never both push and launch
        using var instance = InstanceLock.TryAcquire(env.LockPath);
        if (instance is null)
        {
            BridgeLog.Error(LinuxTexts.Run.AlreadyRunning);
            return ExitFailed;
        }

        var steam = CreateSteam(cli, env, portable, user, getEnvironmentVariable);
        var sampler = new StateSampler(steam, version, cli.TestAppId, platform: "linux");

        BridgeLog.Info(LinuxTexts.Run.Started(version, settings!.ServerText, settings.TokenHint, cli.IntervalMs, BridgeLog.LogPath));
        if (cli.TestAppId is { } testApp) BridgeLog.Warn(LinuxTexts.Run.TestMode(testApp));
        LogSteamDiagnosis(steam);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        // systemd stops a service with SIGTERM, a terminal with SIGINT, a closed terminal with SIGHUP: all of them end the loops cleanly
        using var signals = new SignalRegistrations(cts);

        var saveTarget = SaveTargetPath(env);
        using var client = new BridgeClient(settings, version, platform: "linux");
        // After an update the bridge learns what it was updated to from the argument (started by hand) or the marker file (restarted by systemd)
        var updatedTo = cli.UpdatedTo ?? UpdateMarker.Read(env.UpdateMarkerPath, version);
        var updater = new LinuxSelfUpdater(client, version, enabled: !cli.NoUpdate && !env.IsTest, updatedTo, LinuxEnvironment.ExePath, env.UpdateMarkerPath);
        updater.CleanupLeftovers();
        var status = new BridgeStatus(new StatusSnapshot { ServerText = settings.ServerText, Version = version });
        // Follows the language of the PlatinVault server and remembers it in bridge.json for the next start
        var language = new LanguageFollower(portable?.Language ?? user?.Language, code => BridgeConfigFile.RememberLanguage(saveTarget, env.ConfigPath, code));
        var runner = new BridgeRunner(settings, client, sampler, cli.IntervalMs, updater, status, language);

        // "Start this game": a test run gets the recording launcher, which writes the steam:// address down instead of opening it
        ISteamLauncher launcher = env.IsTest
            ? new RecordingLauncher(env.LaunchRecordPath, cli.TestLaunch ?? LaunchOutcome.Started)
            : new LinuxSteamLauncher(
                new SystemProcessRunner(),
                name => LinuxSteamLauncher.FindOnPath(name, getEnvironmentVariable),
                Directory.Exists(Path.Combine(HomeOf(getEnvironmentVariable), ".var", "app", "com.valvesoftware.Steam")),
                getEnvironmentVariable);
        var poller = new CommandPoller(client, launcher, (_, _) => { /* no balloon on a headless service; CommandPoller logs everything */ });
        var writer = new LinuxStatusWriter(status, env.StatusPath, settings.TokenHint, Environment.ProcessId);

        var loop = Task.Run(() => runner.RunAsync(cts.Token), CancellationToken.None);
        var commandLoop = Task.Run(() => poller.RunAsync(cts.Token), CancellationToken.None);
        var statusLoop = Task.Run(() => writer.RunAsync(cts.Token), CancellationToken.None);

        // Whatever ends the main loop (stop signal, update) ends the others
        var result = BridgeRunResult.Stopped;
        try
        {
            result = await loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path
        }
        await cts.CancelAsync().ConfigureAwait(false);
        try { await commandLoop.ConfigureAwait(false); } catch (OperationCanceledException) { /* normal */ }
        try { await statusLoop.ConfigureAwait(false); } catch (OperationCanceledException) { /* normal */ }
        writer.DeleteFile();

        if (result == BridgeRunResult.RestartAfterUpdate && updater.InstalledVersion is { } installed)
            return Restart(originalArgs, installed, settings.Token, getEnvironmentVariable);

        BridgeLog.Info(LinuxTexts.Run.Stopped);
        return ExitOk;
    }

    /// <summary>
    /// After a self-update: under systemd (INVOCATION_ID is set) just exit - the unit has Restart=always. Started by hand, start the new
    /// binary with the same arguments (plus the marker that stops an update loop) and exit.
    /// </summary>
    private static int Restart(string[] originalArgs, Version installed, string token, Func<string, string?> getEnvironmentVariable)
    {
        if (!string.IsNullOrEmpty(getEnvironmentVariable("INVOCATION_ID")))
        {
            BridgeLog.Info(LinuxTexts.Run.RestartedBySystemd);
            return ExitOk;
        }

        try
        {
            var info = new ProcessStartInfo(LinuxEnvironment.ExePath) { UseShellExecute = false };
            // The token travels in the environment of the new process (readable by its owner only), never on the command line (visible to every user in the process list)
            foreach (var arg in WithoutToken(WithoutRestartMarker(originalArgs))) info.ArgumentList.Add(arg);
            info.Environment[BridgeSettings.TokenEnvVar] = token;
            info.ArgumentList.Add("--updated-to");
            info.ArgumentList.Add(installed.ToString());
            using var process = Process.Start(info);
            BridgeLog.Info(LinuxTexts.Run.NewBridgeStarted(installed.ToString(), process?.Id));
            return ExitOk;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            BridgeLog.Error(LinuxTexts.Run.RestartFailed(ex.Message));
            return ExitFailed;
        }
    }

    internal static IEnumerable<string> WithoutToken(IEnumerable<string> args)
    {
        var skipNext = false;
        foreach (var arg in args)
        {
            if (skipNext) { skipNext = false; continue; }
            if (arg.StartsWith("--token=", StringComparison.OrdinalIgnoreCase)) continue;
            if (arg.Equals("--token", StringComparison.OrdinalIgnoreCase)) { skipNext = true; continue; }
            yield return arg;
        }
    }

    internal static IEnumerable<string> WithoutRestartMarker(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--updated-to=", StringComparison.OrdinalIgnoreCase)) continue;
            if (args[i].Equals("--updated-to", StringComparison.OrdinalIgnoreCase)) { i++; continue; }
            yield return args[i];
        }
    }

    private static void LogSteamDiagnosis(ILocalSteam steam)
    {
        if (steam is not SteamLocalSource source) return;

        var location = source.Reader.Location;
        if (location.Root is { } root)
            BridgeLog.Info(LinuxTexts.Run.SteamFolder(root, DescribeMethod(location.Method)));
        else
            BridgeLog.Warn(LinuxTexts.Run.SteamNotFound);
        BridgeLog.Info(LinuxTexts.Run.RunningGameMethod(source.Reader.RunningGameMethod.ToString()));
        if (location.IsExperimental) BridgeLog.Info(LinuxTexts.Run.ExperimentalHint);
    }

    internal static string DescribeMethod(SteamLocationMethod method) => method switch
    {
        SteamLocationMethod.Manual => "--steam-dir / bridge.json",
        SteamLocationMethod.Configuration => "configuration",
        SteamLocationMethod.Environment => "PLATINVAULT_STEAM_DIR / STEAM_DIR",
        SteamLocationMethod.Registry => "registry",
        SteamLocationMethod.Symlink => "~/.steam link",
        SteamLocationMethod.KnownPath => "known path",
        _ => "-",
    };

    /// <summary>Where setup and the language memory write: an existing portable config next to the binary beats the per-user file (so saving elsewhere would silently have no effect).</summary>
    internal static string SaveTargetPath(LinuxEnvironment env) =>
        !env.IsTest && File.Exists(LinuxEnvironment.PortableConfigPath) ? LinuxEnvironment.PortableConfigPath : env.ConfigPath;

    internal static string HomeOf(Func<string, string?> getEnvironmentVariable) =>
        getEnvironmentVariable("HOME") is { Length: > 0 } home ? home : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // The bridge's own version 1.0.<n> is its assembly version (PlatinVault.Bridge.Linux.csproj)
    internal static string GetVersion() => typeof(LinuxApp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
