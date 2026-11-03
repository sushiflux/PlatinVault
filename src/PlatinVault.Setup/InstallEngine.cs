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

    public async Task RunAsync(InstallRequest request, IProgress<InstallProgress> progress, CancellationToken ct)
    {
        var target = Path.GetFullPath(request.InstallDir);

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
        if (File.Exists(oldExe) && Directory.Exists(layout.Data.Database))
        {
            var output = new List<string>();
            var backupExit = await RunToolAsync(oldExe, ["--backup-now", .. layout.InheritedArguments()], line => { lock (output) output.Add(line); }, TimeSpan.FromMinutes(5), ct);
            if (backupExit != 0)
            {
                string reason;
                lock (output) reason = output.Count > 0 ? string.Join(" ", output.TakeLast(6)) : $"PlatinVault.exe --backup-now (exit code {backupExit})";
                var question = SetupTexts.BackupFailed(reason);
                if (ConfirmWithoutBackup is null || !await ConfirmWithoutBackup(question)) throw new InstallException(SetupTexts.BackupFailedCancelled(reason));
            }
        }

        // 3 - files: staging folder on the same drive, then swap
        progress.Report(new InstallProgress(0.1f, InstallTask.Files, SetupTexts.TaskFiles));
        var parent = Path.GetDirectoryName(target)!;
        var staging = Path.Combine(parent, $".platinvault-new-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".platinvault-old-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            await Task.Run(() => payload.ExtractTo(staging, new Progress<float>(p =>
                progress.Report(new InstallProgress(0.1f + p * 0.45f, InstallTask.Files, SetupTexts.TaskFiles))), ct), ct);
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try
            {
                Directory.Move(staging, target);
            }
            catch (Exception) when (Directory.Exists(backup))
            {
                Directory.Move(backup, target);
                throw;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDelete(staging);
            throw new InstallException(SetupTexts.CopyFailed(target, ex.Message), ex);
        }
        // The previous version stays next to it until the new one has set up its database successfully (step 5)
        var hadOld = Directory.Exists(backup);
        try
        {
            await FinishAsync(request, target, progress, ct);
        }
        catch (Exception ex) when (ex is InstallException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var reason = ex is InstallException install ? install.Text : new Msg(ex.Message, ex.Message);
            throw new InstallException(RollBack(target, backup, hadOld, reason), ex);
        }
        TryDelete(backup);
    }

    /// <summary>
    /// Puts the previous program back after a failed update (the "Apps" entry was not touched yet, so it still names that version), or
    /// removes the unfinished program of a first installation. Returns the complete message for the window.
    /// </summary>
    private static Msg RollBack(string target, string backup, bool hadOld, Msg reason)
    {
        try
        {
            if (Directory.Exists(target)) Uninstaller.DeleteTree(target);
            if (hadOld) Directory.Move(backup, target);
            return hadOld ? SetupTexts.UpdateRolledBack(reason) : reason;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SetupTexts.RollBackFailed(reason, backup, ex.Message);
        }
    }

    private async Task FinishAsync(InstallRequest request, string target, IProgress<InstallProgress> progress, CancellationToken ct)
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
        progress.Report(new InstallProgress(0.62f, InstallTask.Database, SetupTexts.TaskDatabase));
        string? errorDe = null, errorEn = null;
        var exit = await RunToolAsync(Path.Combine(target, InstallLayout.ExeName), ["--prepare", .. layout.InheritedArguments()], line =>
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
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any() && !File.Exists(Path.Combine(folder, InstallLayout.ExeName)))
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
