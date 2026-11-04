using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Text.RegularExpressions;
using PlatinVault.Windows.Install;
using PlatinVault.Windows.Localization;

namespace PlatinVault.Setup;

/// <summary>The choices of the setup window (or the defaults of a headless test install).</summary>
internal sealed record InstallRequest(string InstallDir, bool Autostart, bool StartMenu, bool DesktopShortcut, string Language);

/// <summary>The steps the installing page lists.</summary>
internal enum InstallTask
{
    Prepare,
    Stop,
    Files,
    WebView,
    Database,
    Shortcuts,
}

/// <summary>Progress of a run: overall 0..1, the step that is active and a line that says what happens right now.</summary>
internal readonly record struct InstallProgress(float Value, InstallTask Task, Msg Text);

/// <summary>
/// The installation itself, without UI: checks, quits a running PlatinVault (it backs up and stops its database), copies the program into
/// a staging folder next to the target and swaps it in (the old version comes back if anything fails), makes sure the WebView2 runtime is
/// there, lets the new PlatinVault.exe set up its database (<c>--prepare</c>, progress lines on stdout) and registers shortcuts, autostart
/// and the "Apps" entry. Every failure is an <see cref="InstallException"/> with a complete message in both languages.
/// </summary>
internal sealed partial class InstallEngine(InstallLayout layout, Payload payload, string version)
{
    /// <summary>Test switch: skip the WebView2 download (a test PC either has it or the test says so).</summary>
    public bool SkipWebViewInstall { get; init; }

    /// <summary>
    /// Asked when the backup before an update failed: true = update anyway. Null (headless) = never, the update is cancelled. The
    /// window answers with our own dialog.
    /// </summary>
    public Func<Msg, Task<bool>>? ConfirmWithoutBackup { get; init; }

    /// <summary>Starts PlatinVault.exe for <c>--backup-now</c> / <c>--prepare</c> (exe, arguments, line callback, timeout) and returns its exit code.</summary>
    internal delegate Task<int> ToolRunner(string exe, IEnumerable<string> args, Action<string> onLine, TimeSpan timeout, CancellationToken ct);

    /// <summary>Test aid: replaces the real process start (a test has no real PlatinVault.exe to run). Null = start the program.</summary>
    internal ToolRunner? Tool { get; init; }

    /// <summary>Pauses between the attempts to swap the program folder while a file is locked. Null = <see cref="FileLockRetry.DefaultDelays"/> (about 30 s); tests pass short ones.</summary>
    internal IReadOnlyList<TimeSpan>? RetryDelays { get; init; }

    /// <summary>How long the setup waits for the previous program (and its WebView2 helpers) to disappear before it swaps the folder.</summary>
    internal TimeSpan ReleaseWaitTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Test aid: replaces the scan of <see cref="ProcessWatch.FindBlockers"/> (image check + Restart Manager).</summary>
    internal Func<IReadOnlyList<RunningProcess>>? ProcessProbe { get; init; }

    public async Task RunAsync(InstallRequest request, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        var target = Path.GetFullPath(request.InstallDir);
        // This process (and the tools it starts, which inherit its current directory) must not hold the folder itself: started from the
        // program's update button it inherited the program folder as its current directory, and a folder that is any process's current
        // directory cannot be renamed.
        LeaveFolder(target);

        // 1 - requirements
        progress.Report(new InstallProgress(0.01f, InstallTask.Prepare, SetupTexts.TaskPrepare));
        if (IsElevated()) throw new InstallException(SetupTexts.Elevated);
        var problem = CheckFolder(target);
        if (problem is { } folderProblem) throw new InstallException(folderProblem);
        if (!payload.Verify()) throw new InstallException(SetupTexts.PayloadBroken);
        var needed = payload.UncompressedSize * 2 + 300L * 1024 * 1024;
        var free = FreeSpace(target);
        if (free is { } available && available < needed) throw new InstallException(SetupTexts.NoSpace(Size(needed), Size(available)));

        // 2 - a running copy quits (with its own last backup); an existing installation that is not running is backed up here
        progress.Report(new InstallProgress(0.04f, InstallTask.Stop, SetupTexts.TaskStop));
        if (!await Task.Run(() => AppInstance.RequestQuitAndWait(layout, TimeSpan.FromSeconds(120)), ct)) throw new InstallException(InstallTexts.StillRunning);
        var oldExe = Path.Combine(target, InstallLayout.ExeName);
        var hasDatabase = File.Exists(DatabaseFile(layout));
        // The previous program makes the backup. When it is gone (an antivirus removed PlatinVault.exe, a half-deleted folder) the NEW program
        // does it right after the files are in place and before it touches the database in step 5 - see FinishAsync.
        var backupWithNewProgram = hasDatabase && !File.Exists(oldExe);
        if (hasDatabase && !backupWithNewProgram) await BackupAsync(oldExe, ct);

        // The program has said "I am quitting", but the process and its WebView2 helpers can still keep the folder open for a moment (and
        // renaming a folder fails while anything inside is open). Wait - bounded - until they are really gone; whatever is left after the
        // timeout is not an error by itself, the swap below retries and names the holders if it still fails.
        var waitingText = new InstallProgress(0.07f, InstallTask.Stop, SetupTexts.WaitingForRelease);
        await ProcessWatch.WaitUntilGoneAsync(
            ProcessProbe ?? (() => ProcessWatch.FindBlockers(target, Environment.ProcessId)),
            ReleaseWaitTimeout, TimeSpan.FromMilliseconds(250), _ => progress.Report(waitingText), ct);

        // 3 - files: staging folder on the same drive, then swap
        progress.Report(new InstallProgress(0.1f, InstallTask.Files, SetupTexts.TaskFiles));
        var parent = Path.GetDirectoryName(target)!;
        var staging = Path.Combine(parent, $".platinvault-new-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".platinvault-old-{Guid.NewGuid():N}");
        var delays = RetryDelays ?? FileLockRetry.DefaultDelays;
        var waiting = new InstallProgress(0.12f, InstallTask.Files, SetupTexts.WaitingForRelease);
        void ReportWaiting() => progress.Report(waiting);
        try
        {
            Directory.CreateDirectory(staging);
            await Task.Run(() => payload.ExtractTo(staging, new Progress<float>(p =>
                progress.Report(new InstallProgress(0.1f + p * 0.45f, InstallTask.Files, SetupTexts.TaskFiles))), ct), ct);
            if (Directory.Exists(target)) await FileLockRetry.RunAsync(() => Directory.Move(target, backup), delays, ReportWaiting, ct);
            try
            {
                await FileLockRetry.RunAsync(() => Directory.Move(staging, target), delays, ReportWaiting, ct);
            }
            catch (Exception) when (Directory.Exists(backup))
            {
                // Putting the previous version back must not be cancelled half-way
                await FileLockRetry.RunAsync(() => Directory.Move(backup, target), delays, ReportWaiting, CancellationToken.None);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(staging);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Ask for the holders BEFORE the staging folder is removed: a scanner may be sitting on the freshly extracted files
            var message = FileLockRetry.IsLockError(ex)
                ? SetupTexts.CopyFailedLocked(target, ex.Message, DescribeHolders([target, staging]))
                : SetupTexts.CopyFailed(target, ex.Message);
            TryDelete(staging);
            throw new InstallException(message, ex);
        }
        // The previous version stays next to it until the new one has set up its database successfully (step 5)
        var hadOld = Directory.Exists(backup);
        try
        {
            await FinishAsync(request, target, backupWithNewProgram, progress, ct);
        }
        catch (Exception ex) when (ex is InstallException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var reason = ex is InstallException install ? install.Text : new Msg(ex.Message, ex.Message);
            throw new InstallException(await RollBackAsync(target, backup, hadOld, reason, layout.DataDir, RetryDelays ?? FileLockRetry.DefaultDelays), ex);
        }
        TryDelete(backup);
    }

    /// <summary>
    /// Puts the previous program back after a failed update (the "Apps" entry was not touched yet, so it still names that version), or
    /// removes the unfinished program of a first installation. Returns the complete message for the window. A file that is still locked
    /// (the new program has just run, a scanner is looking at it) is retried like the swap; every step can be repeated safely.
    /// </summary>
    private static async Task<Msg> RollBackAsync(string target, string backup, bool hadOld, Msg reason, string dataDir, IReadOnlyList<TimeSpan> delays)
    {
        try
        {
            await FileLockRetry.RunAsync(() =>
            {
                if (Directory.Exists(target)) Uninstaller.DeleteTree(target);
                if (hadOld) Directory.Move(backup, target);
            }, delays, null, CancellationToken.None);
            return hadOld ? SetupTexts.UpdateRolledBack(reason) : reason;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SetupTexts.RollBackFailed(reason, backup, ex.Message, dataDir);
        }
    }

    /// <summary>"name.exe (PID n)" for every process that holds files of the folders; empty when none could be identified.</summary>
    private static IReadOnlyList<string> DescribeHolders(IEnumerable<string> folders)
    {
        try
        {
            return [.. FileLocks.FindHolders(folders).Select(holder => holder.Display)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    /// <summary>Moves this process's current directory out of <paramref name="target"/> (to its parent folder) when it is inside it.</summary>
    internal static void LeaveFolder(string target)
    {
        if (!ProcessWatch.HoldsAsCurrentDirectory(Environment.CurrentDirectory, target)) return;
        var parent = Path.GetDirectoryName(Path.GetFullPath(target).TrimEnd('\\', '/'));
        Environment.CurrentDirectory = !string.IsNullOrEmpty(parent) && Directory.Exists(parent) ? parent : Environment.SystemDirectory;
    }

    /// <summary>The SQLite file of the Windows program; the same relative place the program itself uses (DatabaseHost.DatabaseFile).</summary>
    internal static string DatabaseFile(InstallLayout layout) => Path.Combine(layout.Data.Database, "platinvault.db");

    /// <summary>
    /// <c>--backup-now</c> of <paramref name="exe"/> (the program that is about to be replaced, or the new one when the old one is gone).
    /// Exit code 0 = a backup was made or nothing had changed; anything else asks the user (our own dialog) whether to go on without one -
    /// a headless run (no dialog) cancels.
    /// </summary>
    private async Task BackupAsync(string exe, CancellationToken ct)
    {
        var output = new List<string>();
        var backupExit = await (Tool ?? RunToolAsync)(exe, ["--backup-now", .. layout.InheritedArguments()], line => { lock (output) output.Add(line); }, TimeSpan.FromMinutes(5), ct);
        if (backupExit == 0) return;
        string reason;
        lock (output) reason = output.Count > 0 ? string.Join(" ", output.TakeLast(6)) : $"PlatinVault.exe --backup-now (exit code {backupExit})";
        var question = SetupTexts.BackupFailed(reason);
        if (ConfirmWithoutBackup is null || !await ConfirmWithoutBackup(question)) throw new InstallException(SetupTexts.BackupFailedCancelled(reason));
    }

    private async Task FinishAsync(InstallRequest request, string target, bool backupWithNewProgram, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        // 4 - WebView2 runtime
        progress.Report(new InstallProgress(0.58f, InstallTask.WebView, SetupTexts.TaskWebView));
        if (WebViewRuntime.InstalledVersion() is null && !SkipWebViewInstall)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                if (await WebViewRuntime.InstallAsync(http, Path.Combine(Path.GetTempPath(), "PlatinVault-Setup"), ct) is null)
                    throw new InstallException(SetupTexts.WebViewFailed(string.Empty));
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                throw new InstallException(SetupTexts.WebViewFailed(ex.Message), ex);
            }
        }

        // 5 - database: the installed program sets it up itself and reports progress lines
        if (backupWithNewProgram)
        {
            // Repair of a damaged installation: the old PlatinVault.exe was missing, so its backup could not be made earlier. The new program's
            // --backup-now only copies the file (VACUUM INTO), it migrates nothing - so this still happens before --prepare changes the schema.
            progress.Report(new InstallProgress(0.6f, InstallTask.Database, SetupTexts.TaskBackupExisting));
            await BackupAsync(Path.Combine(target, InstallLayout.ExeName), ct);
        }
        progress.Report(new InstallProgress(0.62f, InstallTask.Database, SetupTexts.TaskDatabase));
        string? errorDe = null, errorEn = null;
        var exit = await (Tool ?? RunToolAsync)(Path.Combine(target, InstallLayout.ExeName), ["--prepare", .. layout.InheritedArguments()], line =>
        {
            var match = ProgressLine().Match(line);
            if (match.Success)
            {
                var percent = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                progress.Report(new InstallProgress(0.62f + percent / 100f * 0.3f, InstallTask.Database, new Msg(match.Groups[2].Value, match.Groups[3].Value)));
            }
            else if (line.StartsWith("PV-ERROR-DE ", StringComparison.Ordinal)) errorDe = line[12..];
            else if (line.StartsWith("PV-ERROR-EN ", StringComparison.Ordinal)) errorEn = line[12..];
        }, TimeSpan.FromMinutes(10), ct);
        if (exit != 0)
        {
            var fallback = $"PlatinVault.exe --prepare (exit code {exit})";
            throw new InstallException(new Msg(SetupTexts.DatabaseFailed(errorDe ?? fallback).De, SetupTexts.DatabaseFailed(errorEn ?? errorDe ?? fallback).En));
        }

        // 6 - shortcuts, autostart, "Apps" entry
        progress.Report(new InstallProgress(0.94f, InstallTask.Shortcuts, SetupTexts.TaskShortcuts));
        var exe = Path.Combine(target, InstallLayout.ExeName);
        Registration.SetShortcut(layout.StartMenuShortcut, request.StartMenu, exe, InstallTexts.ShortcutDescription);
        Registration.SetShortcut(layout.DesktopShortcut, request.DesktopShortcut, exe, InstallTexts.ShortcutDescription);
        Registration.SetAutostart(layout, request.Autostart, exe);
        Registration.WriteEntries(layout, target, version, request.Language);
        progress.Report(new InstallProgress(1f, InstallTask.Shortcuts, SetupTexts.TaskShortcuts));
    }

    [GeneratedRegex(@"^PV-PROGRESS (\d+) (.*?)\|(.*)$")]
    private static partial Regex ProgressLine();

    /// <summary>Null when the folder may become the program folder, else what is wrong with it.</summary>
    public static Msg? CheckFolder(string folder)
    {
        if (!InstallLayout.IsSafeProgramFolder(folder)) return SetupTexts.FolderUnsafe(folder);
        // A folder with files is fine when it is a PlatinVault program folder - also one whose PlatinVault.exe is gone (see IsProgramFolder)
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any() && !InstallLayout.IsProgramFolder(folder))
            return SetupTexts.FolderNotEmpty(folder);
        return null;
    }

    public static long? FreeSpace(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            return root is null ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The setup runs with invariant globalization: the decimal comma for German is set by hand
    public static string Size(long bytes)
    {
        if (bytes < 1L << 30) return (bytes / (1L << 20)).ToString(CultureInfo.InvariantCulture) + " MB";
        var text = (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture);
        return (UiLang.Current == UiLang.English ? text : text.Replace('.', ',')) + " GB";
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Uninstaller.DeleteTree(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover of an old version that is still locked: harmless, the next setup removes it
        }
    }

    private static async Task<int> RunToolAsync(string exe, IEnumerable<string> args, Action<string> onLine, TimeSpan timeout, CancellationToken ct)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InstallException(SetupTexts.DatabaseFailed(exe));
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return -1;
        }
        return process.ExitCode;
    }
}
