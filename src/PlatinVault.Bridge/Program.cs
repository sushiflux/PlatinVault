using System.Diagnostics;
using System.Runtime.InteropServices;
using Companion.Api.Services;
using PlatinVault.Bridge.Ui;

namespace PlatinVault.Bridge;

internal static class Program
{
    private const string ProductCaption = BridgeEnvironment.ProductName;

    /// <summary>Exit code (0 ok / clean shutdown, 1 runtime problem, 2 wrong usage) and, after a self-update, the installed version.</summary>
    private readonly record struct RunOutcome(int ExitCode, string? InstalledVersion = null);

    // WinForms needs a single-threaded apartment on the thread that owns the windows and the tray icon.
    [STAThread]
    private static int Main(string[] args)
    {
        BridgeLog.InitConsole();

        var outcome = Run(args);

        // Everything that could hold the single-instance mutex is disposed by now, so the replacement can start.
        return outcome.InstalledVersion is null ? outcome.ExitCode : StartReplacement(args, outcome.InstalledVersion);
    }

    private static RunOutcome Run(string[] args)
    {
        var version = GetVersion();

        var cli = CommandLine.Parse(args, out var parseError);
        if (cli is null)
            return Fail(parseError!, exitCode: 2);

        // --language wins over everything; the stored language (bridge.json) and later the server's language come after it.
        if (cli.Language is not null) BridgeLang.Pin(cli.Language);

        var env = BridgeEnvironment.Create(cli);
        BridgeLog.LogPath = env.LogPath;
        BridgeLog.Verbose = cli.Verbose;
        UiEnvironment.Headless = env.Headless;

        // Read before --help/--version so that even they speak the language this bridge was configured with
        var portable = BridgeConfigFile.Load(BridgeConfigFile.PortablePath, out var portableWarning);
        if (portableWarning is not null) BridgeLog.Warn(portableWarning);
        var user = BridgeConfigFile.Load(env.UserConfigPath, out var userWarning);
        if (userWarning is not null) BridgeLog.Warn(userWarning);
        BridgeLang.UseStored(portable?.Language ?? user?.Language);

        if (cli.TestTexts)
            return new RunOutcome(TextCatalogCheck.Run(env));

        if (cli.ShowHelp)
        {
            BridgeLog.Print(BridgeTexts.Cli.Help(version, env));
            return new RunOutcome(0);
        }

        if (cli.ShowVersion)
        {
            BridgeLog.Print($"{ProductCaption} {version}");
            return new RunOutcome(0);
        }

        ApplicationConfiguration.Initialize();

        // Hidden test aid: all windows in both languages, rendered offscreen and measured (needs --test-root: nothing is shown)
        if (cli.TestLayout is { } layoutFolder)
        {
            if (!env.IsTest) return Fail("--test-layout needs --test-root.", exitCode: 2);
            return new RunOutcome(LayoutCheck.Run(env, cli, layoutFolder));
        }

        // The fake launcher is only for test runs; a normal run always opens the real steam:// address
        if (cli.TestLaunch is not null && !env.IsTest) return Fail("--test-launch needs --test-root.", exitCode: 2);

        using var guard = new InstanceGuard(env);

        if (cli.Uninstall)
        {
            UninstallFlow.Run(env, guard, runningInstance: false);
            return new RunOutcome(0);
        }

        var steam = new SteamLocalStatsService(new BridgeLogger<SteamLocalStatsService>());
        var sampler = new StateSampler(steam, version, cli.TestAppId);

        // Started from somewhere else (the unpacked ZIP): offer the installation instead of just running invisibly.
        var offerSetup = !env.IsRunningFromInstallRoot && !cli.NoSetup && !cli.FirstRun && cli.UpdatedTo is null;
        if (offerSetup)
        {
            var (prefillServer, prefillToken) = BridgeSettings.ResolveRaw(cli, Environment.GetEnvironmentVariable, portable, user);
            var installedVersion = Installer.ReadInstalledVersion(env);
            var mode = installedVersion is not null || Installer.IsInstalled(env) ? SetupMode.Update : SetupMode.Install;

            using var setup = new SetupForm(env, cli, guard, sampler, mode, prefillServer, prefillToken, version, installedVersion);
            ShowSetup(env, cli, setup);
            return new RunOutcome(0);
        }

        var instance = EnsureSingleInstance(env, guard);
        if (instance != InstanceResult.Acquired)
            return new RunOutcome(instance == InstanceResult.HandedOver ? 0 : 1);

        if (!BridgeSettings.TryResolve(cli, Environment.GetEnvironmentVariable, portable, user, out var settings, out var configError))
        {
            // Config missing or broken: let the user fix it in a window instead of an error nobody would see.
            BridgeLog.Warn(configError!);
            var (prefillServer, prefillToken) = BridgeSettings.ResolveRaw(cli, Environment.GetEnvironmentVariable, portable, user);

            using var setup = new SetupForm(env, cli, guard, sampler, SetupMode.SaveOnly, prefillServer, prefillToken, version, null);
            ShowSetup(env, cli, setup);
            if (setup.Outcome != SetupOutcome.Saved || setup.ChosenSettings is not { } chosen)
                return new RunOutcome(2);
            settings = chosen;
        }

        return RunBridge(env, cli, guard, settings!, portable, user, steam, sampler, version);
    }

    /// <summary>Shows the setup window - or, in a test run, runs the invisible scenario instead (nothing appears on the desktop).</summary>
    private static void ShowSetup(BridgeEnvironment env, CliOptions cli, SetupForm setup)
    {
        if (env.Headless) HeadlessUi.RunSetupScenario(env, setup, cli.TestUi);
        else setup.ShowDialog();
    }

    /// <summary>The normal run: polling loop in the background, tray icon on this thread, until quit/update/uninstall.</summary>
    private static RunOutcome RunBridge(BridgeEnvironment env, CliOptions cli, InstanceGuard guard, BridgeSettings settings,
        StoredBridgeConfig? portable, StoredBridgeConfig? user, SteamLocalStatsService steam, StateSampler sampler, string version)
    {
        BridgeLog.Info(BridgeTexts.Program.Started(ProductCaption, version, settings.ServerText, settings.TokenHint, cli.IntervalMs, BridgeLog.LogPath));

        if (cli.Save)
        {
            var target = BridgeConfigFile.SaveTargetPath(env);
            try
            {
                BridgeConfigFile.Save(target, settings, BridgeLang.Remembered);
                BridgeLog.Info(BridgeTexts.Program.ConfigSaved(target));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                BridgeLog.Error(BridgeTexts.Program.ConfigSaveFailed(target, ex.Message));
                return new RunOutcome(1);
            }
        }

        if (cli.TestAppId is { } testApp)
            BridgeLog.Warn(BridgeTexts.Program.TestMode(testApp));

        if (!steam.IsLocalSteamAvailable())
            BridgeLog.Warn(BridgeTexts.Program.SteamNotFound);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // let the loop finish its current step instead of killing the process mid-request
            cts.Cancel();
        };
        // Closing a terminal window, logoff and shutdown arrive as SIGTERM on Windows.
        using var termRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cts.Cancel();
        });

        SelfUpdater.CleanupLeftovers();
        if (env.IsRunningFromInstallRoot) Installer.RefreshUninstallVersion(env, version);

        using var client = new BridgeClient(settings, version);
        var updater = new SelfUpdater(client, version, enabled: !cli.NoUpdate, cli.UpdatedTo);
        var status = new BridgeStatus(new StatusSnapshot { ServerText = settings.ServerText, Version = version });
        // Follows the language of the PlatinVault server and remembers it in bridge.json for the next start
        var language = new LanguageFollower(portable?.Language ?? user?.Language, code => BridgeConfigFile.RememberLanguage(env, code));
        var runner = new BridgeRunner(settings, client, sampler, cli.IntervalMs, updater, status, language);

        var tray = new TrayApp(env, cli, status, guard, settings.ServerText, cts.Cancel);
        guard.StartListening(onShowStatus: tray.PostShowStatus, onQuit: cts.Cancel);

        // "Start this game": a test run gets the recording launcher, which writes the steam:// address down instead of opening it
        ISteamLauncher launcher = env.IsTest
            ? new RecordingLauncher(Path.Combine(HeadlessUi.Directory(env), "launches.txt"), cli.TestLaunch ?? LaunchOutcome.Started)
            : new ShellSteamLauncher();
        var poller = new CommandPoller(client, launcher, tray.PostBalloon);

        var loop = Task.Run(() => runner.RunAsync(cts.Token));
        var commandLoop = Task.Run(() => poller.RunAsync(cts.Token));
        // Whatever ends the loop (tray "Beenden", quit request, update, Ctrl+C) also ends the message loop below.
        _ = loop.ContinueWith(_ => tray.PostExit(), TaskScheduler.Default);

        Application.Run(tray);

        cts.Cancel();
        var result = BridgeRunResult.Stopped;
        try
        {
            result = loop.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
        try
        {
            // Before the client is disposed: an in-flight poll ends at once through the cancelled token
            commandLoop.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
        tray.Dispose();

        if (result == BridgeRunResult.RestartAfterUpdate && updater.InstalledVersion is { } installed)
            return new RunOutcome(0, installed.ToString());

        BridgeLog.Info(BridgeTexts.Program.Stopped);
        return new RunOutcome(0);
    }

    /// <summary>
    /// One bridge per Windows user session. A second start brings up the running bridge's status window instead of
    /// silently exiting, which would look like "nothing happened".
    /// </summary>
    private enum InstanceResult
    {
        /// <summary>This process is the one running bridge.</summary>
        Acquired,

        /// <summary>Another bridge runs and was asked to show its status window - nothing more to do here.</summary>
        HandedOver,

        /// <summary>Another bridge runs and could not be reached or replaced.</summary>
        Blocked,
    }

    private static InstanceResult EnsureSingleInstance(BridgeEnvironment env, InstanceGuard guard)
    {
        if (guard.TryAcquire()) return InstanceResult.Acquired;
        if (guard.RequestShowStatus(TimeSpan.FromSeconds(2))) return InstanceResult.HandedOver;

        // Nobody answered: an older bridge without windows still holds the mutex.
        BridgeLog.Print(BridgeTexts.Program.AlreadyRunningOld);
        if (env.IsTest) return InstanceResult.Blocked;

        var answer = Dialogs.Show(null, BridgeTexts.Program.AskReplaceOld,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, DialogResult.No);
        if (answer != DialogResult.Yes) return InstanceResult.Blocked;

        InstanceGuard.KillOtherInstances();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (guard.TryAcquire()) return InstanceResult.Acquired;
            Thread.Sleep(250);
        }

        Dialogs.Warning(BridgeTexts.Program.OldWontQuit);
        return InstanceResult.Blocked;
    }

    /// <summary>
    /// Starts the freshly installed exe with the same arguments. Without a console window nobody would notice a failed
    /// start, so it is logged.
    /// </summary>
    private static int StartReplacement(string[] originalArgs, string installedVersion)
    {
        try
        {
            var exePath = Environment.ProcessPath!;
            var startInfo = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
            };

            // The marker stops an update loop should the server ever offer an exe that reports an older version.
            foreach (var arg in WithoutRestartMarkers(originalArgs)) startInfo.ArgumentList.Add(arg);
            startInfo.ArgumentList.Add("--updated-to");
            startInfo.ArgumentList.Add(installedVersion);

            using var process = Process.Start(startInfo);
            BridgeLog.Info(BridgeTexts.Program.NewBridgeStarted(installedVersion, process?.Id));
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            BridgeLog.Error(BridgeTexts.Program.RestartFailed(ex.Message, Path.GetFileName(Environment.ProcessPath) ?? BridgeEnvironment.ExeFileName));
            return 1;
        }
    }

    // --updated-to is replaced by the new marker; --first-run belongs to the installer's start only (the balloon is shown once).
    private static IEnumerable<string> WithoutRestartMarkers(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--updated-to=", StringComparison.OrdinalIgnoreCase)) continue;
            if (args[i].Equals("--updated-to", StringComparison.OrdinalIgnoreCase)) { i++; continue; }
            if (args[i].Equals("--first-run", StringComparison.OrdinalIgnoreCase)) continue;
            yield return args[i];
        }
    }

    /// <summary>Logs a usage error and, when there is no terminal to read it, shows it in a message box.</summary>
    private static RunOutcome Fail(string message, int exitCode)
    {
        BridgeLog.Error(message);
        if (exitCode == 2) BridgeLog.Print(BridgeTexts.Cli.UsageHint);
        if (!BridgeLog.ConsoleAvailable && !UiEnvironment.Headless) NativeConsole.ShowError(ProductCaption, message);
        return new RunOutcome(exitCode);
    }

    // The bridge's own version 1.0.<n> is its assembly version (PlatinVault.Bridge.csproj). The informational version ("product version")
    // is the readable version of the PlatinVault build (e.g. "v2026.11.14 (15.116022)") and is not comparable, so it is not used here.
    private static string GetVersion() => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

}
