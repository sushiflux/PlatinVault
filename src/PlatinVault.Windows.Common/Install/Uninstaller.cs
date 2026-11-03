using System.Diagnostics;
using System.Text;
using PlatinVault.Windows.Localization;

namespace PlatinVault.Windows.Install;

/// <summary>Progress of a long step: 0..1 and what happens right now.</summary>
public readonly record struct StepProgress(float Value, Msg Text);

/// <summary>
/// Removes the Windows program: asks a running copy to quit (its server makes a last backup and closes the database first),
/// removes autostart, shortcuts and the "Apps" entry and - only when the user chose it - the data (database, backups, keys,
/// logs). The program folder is deleted last; the running uninstaller lives in it, so a small hidden script deletes it once this process
/// has exited (same technique as the bridge).
/// </summary>
public sealed class Uninstaller(InstallLayout layout, string installDir)
{
    /// <summary>Test aid: how long to wait for a running copy to quit.</summary>
    public TimeSpan QuitTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>Test aid: how long a locked data folder (WebView2 processes still ending) is retried.</summary>
    public TimeSpan LockedRetry { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Does everything it can. A running PlatinVault that does not quit stops it before anything is changed. After that a step that fails
    /// (a locked folder, a shortcut) does not stop the rest; at the very end an <see cref="UninstallIncompleteException"/> lists what was
    /// done and, completely, what was not. The "Apps" entry is removed last.
    /// </summary>
    public IReadOnlyList<Msg> Run(bool keepData, IProgress<StepProgress>? progress = null)
    {
        var done = new List<Msg>();
        var problems = new List<Msg>();

        progress?.Report(new StepProgress(0.1f, UninstallTexts.ProgressStopping));
        if (!AppInstance.RequestQuitAndWait(layout, QuitTimeout)) throw new InstallException(InstallTexts.StillRunning);
        done.Add(InstallTexts.DoneStopped);

        progress?.Report(new StepProgress(0.4f, keepData ? UninstallTexts.ProgressKeepData : UninstallTexts.ProgressDeleteData));
        if (keepData)
        {
            done.Add(InstallTexts.DoneDataKept(layout.DataDir));
        }
        else
        {
            var before = problems.Count;
            DeleteData(layout.Data, problems, LockedRetry);
            if (problems.Count == before) done.Add(InstallTexts.DoneDataDeleted);
        }

        progress?.Report(new StepProgress(0.7f, UninstallTexts.ProgressEntries));
        foreach (var shortcut in new[] { layout.StartMenuShortcut, layout.DesktopShortcut })
        {
            try { Registration.SetShortcut(shortcut, false, string.Empty, string.Empty); }
            catch (InstallException ex) { problems.Add(ex.Text); }
        }

        progress?.Report(new StepProgress(0.85f, UninstallTexts.ProgressProgram));
        try
        {
            done.Add(DeleteProgramFolder());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add(InstallTexts.ProgramNotDeleted(installDir, ex.Message));
        }

        // Last: the "Apps" entry and autostart (the program folder is gone or scheduled, an entry pointing at it would only mislead)
        try
        {
            Registration.RemoveEntries(layout);
            done.Add(InstallTexts.DoneEntries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            problems.Add(InstallTexts.AppsEntryFailed(ex.Message));
        }

        progress?.Report(new StepProgress(1f, UninstallTexts.ProgressDone));
        if (problems.Count > 0) throw new UninstallIncompleteException(done, problems);
        return done;
    }

    /// <summary>Deletes our parts of the data folder; throws with the complete list when something stays.</summary>
    public static void DeleteData(DataLayout data)
    {
        var problems = new List<Msg>();
        DeleteData(data, problems, TimeSpan.FromSeconds(5));
        if (problems.Count > 0) throw new UninstallIncompleteException([], problems);
    }

    /// <summary>
    /// Deletes our parts of the data folder, each on its own: a locked folder is retried for <paramref name="retry"/> (WebView2's
    /// processes end a moment after the program) and then reported, the others are deleted anyway. The WebView2 profile goes first,
    /// the database last, so a lock never leaves a deleted database next to a profile that stayed.
    /// </summary>
    public static void DeleteData(DataLayout data, List<Msg> problems, TimeSpan retry)
    {
        var order = new[] { data.WebView, data.Updates, data.Logs, data.Server, data.Backups, data.Database };
        foreach (var folder in order.Concat(data.Folders.Except(order)))
        {
            if (!Directory.Exists(folder)) continue;
            if (TryDeleteWithRetry(folder, retry) is { } error) problems.Add(InstallTexts.DataNotDeleted(folder, error));
        }
        foreach (var file in data.Files)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problems.Add(InstallTexts.DataNotDeleted(file, ex.Message)); }
        }
        // The parent folder only when nothing else lives there (the bridge keeps its log next to our data)
        if (Directory.Exists(data.Root) && !Directory.EnumerateFileSystemEntries(data.Root).Any()) Directory.Delete(data.Root);
    }

    /// <summary>Null when the folder is gone, else the last error text after the retry time.</summary>
    public static string? TryDeleteWithRetry(string folder, TimeSpan retry)
    {
        var until = DateTime.UtcNow + retry;
        while (true)
        {
            try
            {
                if (Directory.Exists(folder)) DeleteTree(folder);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= until) return ex.Message;
                Thread.Sleep(500);
            }
        }
    }

    /// <summary>Deletes a tree, clearing read-only flags first (the former bundled PostgreSQL marked some files read-only).</summary>
    public static void DeleteTree(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
        }
        Directory.Delete(folder, recursive: true);
    }

    private Msg DeleteProgramFolder()
    {
        if (!Directory.Exists(installDir)) return InstallTexts.DoneProgramDeleted;
        if (!InstallLayout.IsSafeProgramFolder(installDir) || !File.Exists(Path.Combine(installDir, InstallLayout.ExeName)))
        {
            return InstallTexts.UnsafeFolder(installDir);
        }

        var running = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty) ?? string.Empty;
        if (running.Length > 0 && InstallLayout.SamePath(running, installDir))
        {
            ScheduleDeletion(installDir, layout.TestRoot);
            return InstallTexts.DoneProgramScheduled;
        }

        DeleteTree(installDir);
        return InstallTexts.DoneProgramDeleted;
    }

    /// <summary>A running exe cannot delete its own folder: a tiny hidden batch file retries until this process has exited, then removes itself.</summary>
    public static void ScheduleDeletion(string folder, string? testRoot)
    {
        var scriptDirectory = testRoot is null ? Path.GetTempPath() : Path.Combine(testRoot, "temp");
        Directory.CreateDirectory(scriptDirectory);
        var script = Path.Combine(scriptDirectory, $"platinvault-uninstall-{Guid.NewGuid():N}.cmd");
        var text = new StringBuilder()
            .AppendLine("@echo off")
            // The path may contain non-ASCII characters (user name): read the rest of the script as UTF-8
            .AppendLine("chcp 65001 >nul")
            .AppendLine($"set \"TARGET={folder}\"")
            .AppendLine("for /l %%i in (1,1,90) do (")
            .AppendLine("  rmdir /s /q \"%TARGET%\" >nul 2>&1")
            .AppendLine("  if not exist \"%TARGET%\" goto :done")
            .AppendLine("  ping -n 2 127.0.0.1 >nul")
            .AppendLine(")")
            .AppendLine(":done")
            .AppendLine("(goto) 2>nul & del \"%~f0\"")
            .ToString();
        File.WriteAllText(script, text, new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/d /c \"\"{script}\"\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = scriptDirectory,
        })?.Dispose();
    }
}

/// <summary>The uninstall did all it could but not everything: what was done and, completely, what was not.</summary>
public sealed class UninstallIncompleteException(IReadOnlyList<Msg> done, IReadOnlyList<Msg> problems)
    : InstallException(Combine(done, problems))
{
    public IReadOnlyList<Msg> Done { get; } = done;

    public IReadOnlyList<Msg> Problems { get; } = problems;

    private static Msg Combine(IReadOnlyList<Msg> done, IReadOnlyList<Msg> problems)
    {
        string Text(string language)
        {
            var lines = new List<string> { language == UiLang.English ? "Not done:" : "Nicht erledigt:" };
            lines.AddRange(problems.Select(p => "• " + p.Resolve(language)));
            if (done.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add(language == UiLang.English ? "Done:" : "Erledigt:");
                lines.AddRange(done.Select(d => "• " + d.Resolve(language)));
            }
            return string.Join(Environment.NewLine, lines);
        }
        return new Msg(Text(UiLang.German), Text(UiLang.English));
    }
}
