using System.Runtime.InteropServices;
using Companion.Api.Data;
using PlatinVault.Desktop.Database;
using PlatinVault.Desktop.Server;
using PlatinVault.Windows.Install;
using PlatinVault.Windows.Localization;
using PlatinVault.Windows.Ui;

namespace PlatinVault.Desktop;

/// <summary>
/// Entry point of PlatinVault.exe. Without arguments (or with <c>--background</c> from autostart) it runs the program - once per user:
/// a second start only brings the running window to the front. The other modes serve the installer (<c>--prepare</c> sets up the
/// database during installation, <c>--uninstall</c> is the "Apps" entry), support (<c>--restore</c>, <c>--backup-now</c>) and tests
/// (<c>--smoke</c>, <c>--test-layout</c>, always with <c>--test-root</c>).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        DesktopOptions options;
        DesktopEnvironment env;
        try
        {
            options = DesktopOptions.Parse(args);
            env = DesktopEnvironment.Create(options);
        }
        catch (ArgumentException ex)
        {
            AttachConsole(-1);
            System.Console.Error.WriteLine(ex.Message);
            return 2;
        }

        ApplicationConfiguration.Initialize();
        DesktopLog.Init(env.Data.Logs);
        Dialogs.Headless = env.Quiet;
        Dialogs.Log = DesktopLog.Info;
        if (env.Layout.Registry.GetString(InstallLayout.AppKeyPath, "Language") is { } installedLanguage) UiLang.Current = installedLanguage;

        try
        {
            if (options.Prepare) return Console(() => Prepare(env));
            if (env.IsSharedDatabase && (options.Restore is not null || options.BackupNow))
            {
                // The shared database belongs to the browser stack (its backup service, scripts/restore-db.ps1)
                return Console(() =>
                {
                    Out(DesktopTexts.Dev.SharedNoBackupHere.De + " / " + DesktopTexts.Dev.SharedNoBackupHere.En);
                    return Task.FromResult(1);
                });
            }
            if (options.Restore is { } dump) return Console(() => Restore(env, dump));
            if (options.BackupNow) return Console(() => BackupNow(env));
            if (options.Quit) return Console(() => Quit(env));
            if (options.Uninstall) return Uninstall(env);
            if (options.TestLayout is { } folder) return TestLayout.Run(env, folder);
            if (options.Smoke is { } output) return Console(() => Smoke.RunAsync(env, output));
            return RunApp(env);
        }
        catch (Exception ex)
        {
            DesktopLog.Error($"Unerwarteter Fehler: {ex}");
            throw;
        }
    }

    private static int RunApp(DesktopEnvironment env)
    {
        var instance = AppInstance.TryAcquire(env.Layout);
        if (instance is null)
        {
            // Already running: bring that window up instead of a second program
            AppInstance.Send(env.Layout, AppInstance.Show, TimeSpan.FromSeconds(3));
            return 0;
        }

        DesktopLog.Info($"PlatinVault {env.Version} startet (Programmordner {env.InstallDir}, Daten {env.Data.Root}).");
        Application.Run(new DesktopApp(env, instance));
        return 0;
    }

    /// <summary>
    /// Installer step "set up the database": the server is built once without starting (that checks the SQLite file and applies every
    /// migration - also the guard against a database of a newer PlatinVault), then a first backup is made. Prints "PV-PROGRESS
    /// &lt;percent&gt; &lt;text&gt;" lines the installer shows and "PV-ERROR-DE/EN &lt;text&gt;" on failure.
    /// </summary>
    private static async Task<int> Prepare(DesktopEnvironment env)
    {
        DesktopLog.Echo = false;
        var settings = DesktopSettings.Load(env.Data.SettingsFile);
        var database = new DatabaseHost(env);
        try
        {
            Report(5, DesktopTexts.Prepare.Cluster);
            database.Prepare(null);
            Report(40, DesktopTexts.Prepare.Schema);
            await using (await DesktopServer.CreateAsync(env, settings, database, new LocalDesktopHost(true, null), start: false, CancellationToken.None))
            {
                // Building the server checked the file and applied every migration: the schema is ready
            }
            Report(80, DesktopTexts.Prepare.Backup);
            await database.CreateBackups().RunAsync("Installation", CancellationToken.None);
            Report(100, DesktopTexts.Prepare.Done);
            return 0;
        }
        catch (DatabaseException ex)
        {
            DesktopLog.Error($"Einrichtung der Datenbank fehlgeschlagen: {ex.Message}");
            Out("PV-ERROR-DE " + OneLine(ex.Text.De + (ex.Detail is null ? string.Empty : " " + ex.Detail)));
            Out("PV-ERROR-EN " + OneLine(ex.Text.En + (ex.Detail is null ? string.Empty : " " + ex.Detail)));
            return 1;
        }
        catch (DatabaseRefusedException ex)
        {
            // A damaged file or a database of a newer PlatinVault: nothing was changed, the installer puts the previous version back
            DesktopLog.Error($"Einrichtung der Datenbank abgebrochen: {ex.Text.De}");
            Out("PV-ERROR-DE " + OneLine(ex.Text.De));
            Out("PV-ERROR-EN " + OneLine(ex.Text.En));
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            DesktopLog.Error($"Einrichtung der Datenbank fehlgeschlagen: {ex}");
            Out("PV-ERROR-DE " + OneLine(ex.Message));
            Out("PV-ERROR-EN " + OneLine(ex.Message));
            return 1;
        }

        static void Report(int percent, Msg text) => Out($"PV-PROGRESS {percent} {text.De}|{text.En}");
    }

    /// <summary>
    /// <c>--restore &lt;backup&gt;</c>: the running program is asked to quit first (its server makes a last backup), then the server's restore
    /// replaces the file - with its rules: the chosen backup is copied to backups\restore and checked first, the current state gets a
    /// safety backup without pruning, only then the file is replaced. The server lifts an older backup to the current schema on its next start.
    /// </summary>
    private static async Task<int> Restore(DesktopEnvironment env, string dump)
    {
        if (!AppInstance.RequestQuitAndWait(env.Layout, TimeSpan.FromSeconds(90)))
        {
            Out(InstallTexts.StillRunning);
            return 1;
        }
        var database = new DatabaseHost(env);
        try
        {
            var rows = await database.CreateBackups().RestoreAsync(Path.GetFullPath(dump), env.Version, CancellationToken.None);
            Out($"Wiederhergestellt / restored: {dump}");
            foreach (var (table, count) in rows) Out($"  {table}: {count}");
            return 0;
        }
        catch (DatabaseRestoreException ex)
        {
            DesktopLog.Error($"Wiederherstellung abgebrochen: {ex.Message}");
            Out(new Msg(ex.Text.De, ex.Text.En) + (ex.Detail is null ? string.Empty : Environment.NewLine + ex.Detail));
            return 1;
        }
    }

    /// <summary>
    /// <c>--backup-now</c> (the installer before an update, support): one backup while the program does NOT run. Exit code 0 = created,
    /// 1 = failed (the installer reads exit code and text: a backup that could not be made must never fail silently before an update).
    /// </summary>
    private static async Task<int> BackupNow(DesktopEnvironment env)
    {
        if (AppInstance.IsRunning(env.Layout))
        {
            Out("PlatinVault läuft - es sichert selbst beim Start, alle 6 Stunden und beim Beenden. / PlatinVault is running and backs up on its own.");
            return 1;
        }
        var backups = new DatabaseHost(env).CreateBackups();
        try
        {
            var outcome = await backups.RunAsync("von Hand", CancellationToken.None, force: true);
            Out($"Sicherung / backup: {outcome} ({backups.RecentDir})");
            return outcome == BackupOutcome.Failed ? 1 : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            DesktopLog.Error($"Sicherung von Hand fehlgeschlagen: {ex.Message}");
            Out(OneLine(ex.Message));
            return 1;
        }
    }

    /// <summary>
    /// <c>--dev ... --quit</c> (start-windows.ps1 stop): asks the running development copy to back up, stop its database and exit,
    /// and waits for it. Exit code 0 when nothing runs (any more), 1 when it did not end in time.
    /// </summary>
    private static Task<int> Quit(DesktopEnvironment env)
    {
        var gone = AppInstance.RequestQuitAndWait(env.Layout, TimeSpan.FromSeconds(120));
        Out(gone ? "PlatinVault (Entwicklung) ist beendet. / PlatinVault (development) has quit." : "PlatinVault (Entwicklung) hat sich nicht rechtzeitig beendet. / PlatinVault (development) did not quit in time.");
        return Task.FromResult(gone ? 0 : 1);
    }

    private static int Uninstall(DesktopEnvironment env)
    {
        var installDir = env.Layout.InstalledDir ?? env.InstallDir;
        var uninstaller = new Uninstaller(env.Layout, installDir);
        if (env.Options.Headless)
        {
            AttachConsole(-1);
            try
            {
                foreach (var line in uninstaller.Run(keepData: !env.Options.DeleteData)) Out(line.De);
                return 0;
            }
            catch (InstallException ex)
            {
                Out("FEHLER: " + ex.Text.De);
                return 1;
            }
        }

        using var window = new UninstallWindow(env.Layout, env.Version, (keep, progress) => uninstaller.Run(keep, progress));
        Application.Run(window);
        return window.Finished ? 0 : 1;
    }

    // ---- console for the helper modes (a WinExe has none; the installer reads redirected output) -----------------------------------

    private static int Console(Func<Task<int>> run)
    {
        if (!System.Console.IsOutputRedirected) AttachConsole(-1);
        else
        {
            // The installer reads the lines as UTF-8 (umlauts in the progress texts)
            System.Console.SetOut(new StreamWriter(System.Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });
        }
        return run().GetAwaiter().GetResult();
    }

    private static void Out(string line)
    {
        try
        {
            System.Console.Out.WriteLine(line);
            System.Console.Out.Flush();
        }
        catch (IOException)
        {
            // The reader went away
        }
    }

    private static string OneLine(string text) => text.Replace("\r", " ").Replace("\n", " ");

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
