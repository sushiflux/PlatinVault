using System.Diagnostics;
using System.IO.Compression;
using PlatinVault.Setup;
using PlatinVault.Windows.Install;

namespace PlatinVault.Windows.Tests;

/// <summary>
/// The program folder swap of an update must survive a file that stays locked for a moment (the previous program's process and its WebView2
/// helpers still ending, a virus scanner looking at the files), wait for the old program to be really gone, and - when it still fails - name
/// the programs that hold the files. Everything runs in temp folders; nothing touches the real installation, registry or sound.
/// </summary>
public sealed class InstallLockTests
{
    private static IOException SharingViolation() => new("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020));

    // ---- predicates ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_program_folder_as_current_directory_counts_as_holding_it()
    {
        // The update button used to start the setup with the program folder as working directory - that alone blocks the rename
        var folder = @"C:\Users\someone\AppData\Local\Programs\PlatinVault";
        Assert.True(ProcessWatch.HoldsAsCurrentDirectory(folder, folder));
        Assert.True(ProcessWatch.HoldsAsCurrentDirectory(folder.ToLowerInvariant() + @"\", folder));
        Assert.True(ProcessWatch.HoldsAsCurrentDirectory(folder + @"\runtimes", folder));
        Assert.False(ProcessWatch.HoldsAsCurrentDirectory(folder + "-old", folder));
        Assert.False(ProcessWatch.HoldsAsCurrentDirectory(@"C:\Users\someone\AppData\Local\PlatinVault\updates", folder));
        Assert.False(ProcessWatch.HoldsAsCurrentDirectory(null, folder));
    }

    [Fact]
    public void A_folder_that_is_a_current_directory_cannot_be_renamed_but_can_after_leaving_it()
    {
        // Proves the cause with a child process (the test process's own current directory stays untouched): a process whose working
        // directory is the folder blocks Directory.Move with a sharing violation.
        var root = Path.Combine(Path.GetTempPath(), $"pv-cwd-{Guid.NewGuid():N}");
        var folder = Path.Combine(root, "PlatinVault");
        Directory.CreateDirectory(folder);
        using var child = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c ping -n 30 127.0.0.1 >nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = folder,
        })!;
        try
        {
            Thread.Sleep(300);
            var blocked = Assert.ThrowsAny<IOException>(() => Directory.Move(folder, folder + "-moved"));
            Assert.True(FileLockRetry.IsLockError(blocked));
        }
        finally
        {
            try { child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            child.WaitForExit(5000);
        }
        Directory.Move(folder, folder + "-moved");
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Image_paths_count_only_when_inside_the_program_folder()
    {
        var folder = @"C:\Users\someone\AppData\Local\Programs\PlatinVault";
        Assert.True(ProcessWatch.IsInside(folder + @"\PlatinVault.exe", folder));
        Assert.True(ProcessWatch.IsInside(folder.ToUpperInvariant() + @"\sub\x.exe", folder + @"\"));
        // A sibling that merely starts with the same letters is another folder
        Assert.False(ProcessWatch.IsInside(folder + @"-old\PlatinVault.exe", folder));
        Assert.False(ProcessWatch.IsInside(folder, folder));
        Assert.False(ProcessWatch.IsInside(null, folder));
        Assert.False(ProcessWatch.IsInside(@"C:\Windows\notepad.exe", folder));
        Assert.False(ProcessWatch.IsInside("not a path \0", folder));
    }

    [Fact]
    public void Only_sharing_lock_and_access_errors_count_as_a_lock()
    {
        Assert.True(FileLockRetry.IsLockError(SharingViolation()));
        Assert.True(FileLockRetry.IsLockError(new IOException("lock", unchecked((int)0x80070021))));
        Assert.True(FileLockRetry.IsLockError(new UnauthorizedAccessException("denied"))); // HResult 0x80070005
        Assert.False(FileLockRetry.IsLockError(new FileNotFoundException("gone")));
        Assert.False(FileLockRetry.IsLockError(new DirectoryNotFoundException("gone")));
        Assert.False(FileLockRetry.IsLockError(new IOException("disk full", unchecked((int)0x80070070))));
        Assert.False(FileLockRetry.IsLockError(new InvalidDataException("zip")));
    }

    // ---- waiting for the old program -----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_wait_ends_as_soon_as_nothing_of_the_old_program_is_left()
    {
        var polls = 0;
        var announced = 0;
        IReadOnlyList<RunningProcess> Probe() => ++polls <= 3 ? [new RunningProcess(1234, "msedgewebview2", null)] : [];

        var left = await ProcessWatch.WaitUntilGoneAsync(Probe, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(5), _ => announced++, CancellationToken.None);

        Assert.Empty(left);
        Assert.Equal(4, polls);
        Assert.Equal(1, announced); // the progress text is announced once, not on every poll
    }

    [Fact]
    public async Task The_wait_gives_up_after_its_timeout_and_returns_what_is_left()
    {
        var watch = Stopwatch.StartNew();
        var left = await ProcessWatch.WaitUntilGoneAsync(() => [new RunningProcess(7, "PlatinVault", null)], TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(20), null, CancellationToken.None);

        Assert.Equal(7, Assert.Single(left).Pid);
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task The_wait_can_be_cancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProcessWatch.WaitUntilGoneAsync(() => [new RunningProcess(7, "x", null)], TimeSpan.FromMinutes(5), TimeSpan.FromMilliseconds(10), null, cts.Token));
    }

    [Fact]
    public void The_process_scan_finds_a_process_by_its_image_folder_and_skips_the_setup_itself()
    {
        var folder = Path.GetDirectoryName(Environment.ProcessPath)!;
        var nowhere = Path.Combine(Path.GetTempPath(), "pv-wintest-no-webview-" + Guid.NewGuid().ToString("N")[..8]);

        Assert.Contains(ProcessWatch.FindBlockers(folder, ownPid: 0), p => p.Pid == Environment.ProcessId);
        Assert.DoesNotContain(ProcessWatch.FindBlockers(folder, Environment.ProcessId), p => p.Pid == Environment.ProcessId);
        Assert.Empty(ProcessWatch.FindBlockers(nowhere, ownPid: 0));
    }

    [Fact]
    public async Task The_wait_before_the_swap_uses_the_restart_manager_and_ends_when_the_file_is_released()
    {
        using var temp = new TempFolder();
        var folder = temp.Sub("program");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "helper.dll");
        File.WriteAllText(path, "x");
        var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var release = new System.Threading.Timer(_ => held.Dispose(), null, TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);

        // No process runs from the temp folder, so only the Restart Manager can see the holder (like a WebView2 child process)
        Assert.Contains(ProcessWatch.FindBlockers(folder, ownPid: 0), p => p.Pid == Environment.ProcessId);
        Assert.DoesNotContain(ProcessWatch.FindBlockers(folder, Environment.ProcessId), p => p.Pid == Environment.ProcessId);

        var left = await ProcessWatch.WaitUntilGoneAsync(() => ProcessWatch.FindBlockers(folder, ownPid: 0), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50), null, CancellationToken.None);
        Assert.Empty(left);
    }

    // ---- retry helper --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_lock_is_retried_with_the_given_pauses_until_it_goes_away()
    {
        var calls = 0;
        var waits = 0;
        await FileLockRetry.RunAsync(() => { if (++calls <= 3) throw SharingViolation(); }, Enumerable.Repeat(TimeSpan.FromMilliseconds(5), 10).ToArray(), () => waits++, CancellationToken.None);

        Assert.Equal(4, calls);
        Assert.Equal(3, waits);
    }

    [Fact]
    public async Task The_last_error_is_thrown_when_all_pauses_are_used_up()
    {
        var calls = 0;
        var ex = await Assert.ThrowsAsync<IOException>(() =>
            FileLockRetry.RunAsync(() => { calls++; throw SharingViolation(); }, [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)], null, CancellationToken.None));

        Assert.Equal(3, calls); // first attempt + one per pause
        Assert.True(FileLockRetry.IsLockError(ex));
    }

    [Fact]
    public async Task Other_errors_are_not_retried_and_cancellation_stops_the_waiting()
    {
        var calls = 0;
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            FileLockRetry.RunAsync(() => { calls++; throw new FileNotFoundException("gone"); }, [TimeSpan.FromMilliseconds(1)], null, CancellationToken.None));
        Assert.Equal(1, calls);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FileLockRetry.RunAsync(() => throw SharingViolation(), [TimeSpan.FromMinutes(5)], null, cts.Token));
    }

    [Fact]
    public void The_default_pauses_make_about_ten_attempts_over_about_half_a_minute()
    {
        Assert.InRange(FileLockRetry.DefaultDelays.Length, 9, 12);
        Assert.InRange(FileLockRetry.DefaultDelays.Aggregate(TimeSpan.Zero, (sum, d) => sum + d), TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(40));
        Assert.True(FileLockRetry.DefaultDelays.Zip(FileLockRetry.DefaultDelays.Skip(1)).All(pair => pair.Second >= pair.First));
    }

    // ---- Restart Manager -----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_restart_manager_names_this_process_as_the_holder_of_an_open_file()
    {
        using var temp = new TempFolder();
        var folder = temp.Sub("program");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "locked.dll");
        File.WriteAllText(path, "x");

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var holders = FileLocks.FindHolders([folder]);
            var own = Assert.Single(holders, h => h.Pid == Environment.ProcessId);
            Assert.Contains($"(PID {Environment.ProcessId})", own.Display);
            Assert.EndsWith(".exe (PID " + Environment.ProcessId + ")", own.Display);
        }

        Assert.DoesNotContain(FileLocks.FindHolders([folder]), h => h.Pid == Environment.ProcessId);
        Assert.Empty(FileLocks.FindHolders([temp.Sub("missing")]));
    }

    // ---- the engine ----------------------------------------------------------------------------------------------------------

    private static string MakePayload(TempFolder temp)
    {
        var zip = temp.Sub("payload.zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        void Add(string name, string text)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(text);
        }
        Add(InstallLayout.ExeName, "new program");
        Add("version.txt", "v-new");
        Add("release-channel.txt", "release");
        return zip;
    }

    private static string MakeOldInstall(TempFolder temp)
    {
        var target = temp.Sub("install");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, InstallLayout.ExeName), "old program");
        File.WriteAllText(Path.Combine(target, "version.txt"), "v-old");
        File.WriteAllText(Path.Combine(target, "release-channel.txt"), "release");
        File.WriteAllText(Path.Combine(target, "Companion.Api.dll"), "old server");
        return target;
    }

    private static Task<int> PrepareOk(string exe, IEnumerable<string> args, Action<string> onLine, TimeSpan timeout, CancellationToken ct)
    {
        onLine("PV-PROGRESS 100 Fertig|Done");
        return Task.FromResult(0);
    }

    [Fact]
    public async Task The_swap_succeeds_when_a_file_is_released_after_a_few_attempts()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        var target = MakeOldInstall(temp);
        var held = new FileStream(Path.Combine(target, "Companion.Api.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var release = new System.Threading.Timer(_ => held.Dispose(), null, TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
        var messages = new List<string>();
        using var payload = Payload.FromZip(MakePayload(temp));
        var engine = new InstallEngine(layout, payload, "v-new")
        {
            SkipWebViewInstall = true,
            Tool = PrepareOk,
            ProcessProbe = () => [],
            RetryDelays = Enumerable.Repeat(TimeSpan.FromMilliseconds(100), 60).ToArray(),
        };

        await engine.RunAsync(new InstallRequest(target, false, false, false, "de"), new Progress<InstallProgress>(p => { lock (messages) messages.Add(p.Text.En); }), CancellationToken.None);

        Assert.Equal("new program", File.ReadAllText(Path.Combine(target, InstallLayout.ExeName)));
        Assert.False(File.Exists(Path.Combine(target, "Companion.Api.dll")));
        Assert.Empty(Directory.GetDirectories(temp.Path, ".platinvault-*"));
        // Give the Progress<T> callbacks (posted to the thread pool) a moment, then check the user was told why it takes longer
        await Task.Delay(100);
        lock (messages) Assert.Contains("Waiting for Windows to release the program files …", messages);
    }

    [Fact]
    public async Task A_lock_that_never_goes_away_fails_with_a_message_that_names_the_holder_and_keeps_the_old_version()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        var target = MakeOldInstall(temp);
        using var payload = Payload.FromZip(MakePayload(temp));
        var engine = new InstallEngine(layout, payload, "v-new")
        {
            SkipWebViewInstall = true,
            Tool = PrepareOk,
            ProcessProbe = () => [],
            RetryDelays = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)],
        };

        InstallException ex;
        using (new FileStream(Path.Combine(target, "Companion.Api.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ex = await Assert.ThrowsAsync<InstallException>(() =>
                engine.RunAsync(new InstallRequest(target, false, false, false, "de"), new Progress<InstallProgress>(), CancellationToken.None));
        }

        Assert.Contains("Die Dateien werden gerade benutzt von:", ex.Text.De);
        Assert.Contains($"(PID {Environment.ProcessId})", ex.Text.De);
        Assert.Contains("Schließe diese Programme oder starte den PC neu", ex.Text.De);
        Assert.Contains("currently in use by:", ex.Text.En);
        Assert.Contains($"(PID {Environment.ProcessId})", ex.Text.En);
        // Complete message: the reason of Windows is part of it, and the previous version is untouched
        Assert.NotNull(ex.InnerException);
        Assert.Contains(ex.InnerException.Message, ex.Text.En); // the (OS-language) reason of Windows is part of the message
        Assert.Equal("old program", File.ReadAllText(Path.Combine(target, InstallLayout.ExeName)));
        Assert.Equal("old server", File.ReadAllText(Path.Combine(target, "Companion.Api.dll")));
        Assert.Empty(Directory.GetDirectories(temp.Path, ".platinvault-*"));
    }

    [Fact]
    public async Task Without_an_identifiable_holder_the_message_says_so_and_mentions_a_virus_scan()
    {
        var message = SetupTexts.CopyFailedLocked(@"C:\x\PlatinVault", "locked.", []);

        Assert.Contains("kein Programm ermitteln", message.De);
        Assert.Contains("Virenschutz", message.De);
        Assert.Contains("virus scanner", message.En);
        Assert.Contains("No program holding the files could be identified", message.En);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_engine_waits_for_the_old_program_before_it_swaps_and_goes_on_after_the_timeout()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        var target = MakeOldInstall(temp);
        using var payload = Payload.FromZip(MakePayload(temp));
        var polls = 0;
        var engine = new InstallEngine(layout, payload, "v-new")
        {
            SkipWebViewInstall = true,
            Tool = PrepareOk,
            // The old WebView2 process is "there" for the first polls, then gone
            ProcessProbe = () => ++polls <= 3 ? [new RunningProcess(4242, "msedgewebview2", null)] : [],
        };
        await engine.RunAsync(new InstallRequest(target, false, false, false, "de"), new Progress<InstallProgress>(), CancellationToken.None);
        Assert.True(polls >= 4);
        Assert.Equal("new program", File.ReadAllText(Path.Combine(target, InstallLayout.ExeName)));

        // A process that never ends does not hang the setup: after the (short) timeout the swap is tried anyway
        using var temp2 = new TempFolder();
        var layout2 = InstallLayout.Create(temp2.Path);
        var target2 = MakeOldInstall(temp2);
        using var payload2 = Payload.FromZip(MakePayload(temp2));
        var stuck = new InstallEngine(layout2, payload2, "v-new")
        {
            SkipWebViewInstall = true,
            Tool = PrepareOk,
            ProcessProbe = () => [new RunningProcess(4242, "msedgewebview2", null)],
            ReleaseWaitTimeout = TimeSpan.FromMilliseconds(300),
        };
        await stuck.RunAsync(new InstallRequest(target2, false, false, false, "de"), new Progress<InstallProgress>(), CancellationToken.None);
        Assert.Equal("new program", File.ReadAllText(Path.Combine(target2, InstallLayout.ExeName)));
    }
}
