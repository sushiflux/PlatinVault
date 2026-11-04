using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PlatinVault.Setup;

/// <summary>A process that may still hold the program files: id, name (without ".exe") and, when readable, the path of its image.</summary>
internal readonly record struct RunningProcess(int Pid, string Name, string? ImagePath)
{
    public string Display => Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? $"{Name} (PID {Pid})" : $"{Name}.exe (PID {Pid})";
}

/// <summary>
/// Finds out whether the previous PlatinVault is really gone before the setup renames its folder. The program's mutex is released
/// when the process begins to exit, but the process (and its WebView2 helper processes, which end a moment later) can still keep the
/// folder open for a while - and renaming a folder fails while ANY handle inside it is open.
/// Nothing here needs elevation or reads the memory of another process: whatever cannot be read counts as "not ours".
/// </summary>
internal static class ProcessWatch
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>True when <paramref name="path"/> lies inside <paramref name="folder"/> (case-insensitive, whole path segments only).</summary>
    internal static bool IsInside(string? path, string folder)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder)) return false;
        try
        {
            var root = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when <paramref name="currentDirectory"/> is <paramref name="folder"/> itself or lies inside it. A process keeps its current
    /// directory open, so Windows refuses to rename that folder ("being used by another process") - and the Restart Manager does not
    /// report such a holder. The program's update button used to start this setup with the program folder as working directory.
    /// </summary>
    internal static bool HoldsAsCurrentDirectory(string? currentDirectory, string folder)
    {
        if (string.IsNullOrWhiteSpace(currentDirectory) || string.IsNullOrWhiteSpace(folder)) return false;
        if (IsInside(currentDirectory, folder)) return true;
        try
        {
            return string.Equals(Path.GetFullPath(currentDirectory).TrimEnd('\\', '/'), Path.GetFullPath(folder).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// What still keeps the program folder busy: first the cheap check for a process whose image lies in <paramref name="programFolder"/>
    /// (no access to any process memory), then - only when that finds nothing - the Restart Manager, which also sees helper processes
    /// (WebView2 children, a scanner) that have files of the folder open. The process <paramref name="ownPid"/> (the setup) is never listed.
    /// </summary>
    internal static IReadOnlyList<RunningProcess> FindBlockers(string programFolder, int ownPid)
    {
        var running = FindRunningFrom(programFolder, ownPid);
        if (running.Count > 0) return running;
        return [.. FileLocks.FindHolders([programFolder]).Where(holder => holder.Pid != ownPid)];
    }

    /// <summary>Processes whose executable lies inside <paramref name="programFolder"/>; reads only the image path of each process.</summary>
    internal static IReadOnlyList<RunningProcess> FindRunningFrom(string programFolder, int ownPid)
    {
        var found = new List<RunningProcess>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == ownPid) continue;
                    var name = process.ProcessName;
                    var image = ImagePathOf(process.Id);
                    if (IsInside(image, programFolder))
                    {
                        found.Add(new RunningProcess(process.Id, name, image));
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // Ended while we looked, or not readable without more rights: not ours
                }
            }
        }
        return found;
    }

    /// <summary>
    /// Polls <paramref name="probe"/> until it reports nothing or <paramref name="timeout"/> is over. Returns what was still there at the end
    /// (empty = all gone). <paramref name="onWaiting"/> runs once, when the first poll finds something. Never a busy loop; ends with
    /// <see cref="OperationCanceledException"/> on cancellation.
    /// </summary>
    internal static async Task<IReadOnlyList<RunningProcess>> WaitUntilGoneAsync(
        Func<IReadOnlyList<RunningProcess>> probe, TimeSpan timeout, TimeSpan interval, Action<IReadOnlyList<RunningProcess>>? onWaiting, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        var announced = false;
        while (true)
        {
            var left = await Task.Run(probe, ct);
            if (left.Count == 0) return left;
            if (!announced)
            {
                announced = true;
                onWaiting?.Invoke(left);
            }
            var remaining = until - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return left;
            await Task.Delay(remaining < interval ? remaining : interval, ct);
        }
    }

    private static string? ImagePathOf(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var builder = new StringBuilder(1024);
            var size = builder.Capacity;
            return QueryFullProcessImageName(handle, 0, builder, ref size) ? builder.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
}

/// <summary>Retries a file operation that fails because another process still holds a file for a moment.</summary>
internal static class FileLockRetry
{
    private const int SharingViolation = 0x20; // ERROR_SHARING_VIOLATION
    private const int LockViolation = 0x21; // ERROR_LOCK_VIOLATION
    private const int AccessDenied = 0x5; // ERROR_ACCESS_DENIED

    /// <summary>Pauses between the attempts: 11 attempts over about 30 seconds, the pauses growing.</summary>
    internal static readonly TimeSpan[] DefaultDelays =
    [
        TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2.5),
        TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(6),
    ];

    /// <summary>
    /// True for the errors a lock produces: ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION and ERROR_ACCESS_DENIED (an
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> with that Win32 code in its HResult). A missing file or folder
    /// is not a lock.
    /// </summary>
    internal static bool IsLockError(Exception ex)
    {
        if (ex is not (IOException or UnauthorizedAccessException)) return false;
        var code = ex.HResult & 0xFFFF;
        return (ex.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000) && code is SharingViolation or LockViolation or AccessDenied;
    }

    /// <summary>
    /// Runs <paramref name="operation"/>; on a lock error waits the next pause (<paramref name="onWait"/> runs before each pause) and tries
    /// again. After the last pause the final error is thrown as it is. Other errors are thrown at once.
    /// </summary>
    internal static async Task RunAsync(Action operation, IReadOnlyList<TimeSpan> delays, Action? onWait, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                operation();
                return;
            }
            catch (Exception ex) when (IsLockError(ex) && attempt < delays.Count)
            {
                onWait?.Invoke();
            }
            await Task.Delay(delays[attempt], ct);
        }
    }
}

/// <summary>Asks the Windows Restart Manager which processes have files of a folder open (the same service installers use to name blocking programs).</summary>
internal static class FileLocks
{
    private const int MoreData = 234; // ERROR_MORE_DATA

    /// <summary>
    /// The processes that hold files inside <paramref name="folders"/>, each process once. Executables and DLLs are registered first, at
    /// most <paramref name="maxFilesPerFolder"/> files per folder. Empty when nothing is found or the service is not available - the
    /// caller then says so in general terms.
    /// </summary>
    internal static IReadOnlyList<RunningProcess> FindHolders(IEnumerable<string> folders, int maxFilesPerFolder = 1500)
    {
        var files = new List<string>();
        foreach (var folder in folders)
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                files.AddRange(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .OrderBy(file => IsProgramFile(file) ? 0 : 1)
                    .Take(maxFilesPerFolder));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Listing is best effort: use what we have
            }
        }
        if (files.Count == 0) return [];

        uint session = 0;
        var key = new StringBuilder(33);
        if (RmStartSession(out session, 0, key) != 0) return [];
        try
        {
            if (RmRegisterResources(session, (uint)files.Count, files.ToArray(), 0, null, 0, null) != 0) return [];
            uint needed = 0, count = 0, reasons = 0;
            var result = RmGetList(session, out needed, ref count, null, ref reasons);
            if (result == 0 && needed == 0) return [];
            if (result != MoreData && result != 0) return [];
            var capacity = needed + 8;
            var infos = new RmProcessInfo[capacity];
            count = capacity;
            if (RmGetList(session, out needed, ref count, infos, ref reasons) != 0) return [];
            var holders = new Dictionary<int, RunningProcess>();
            for (var i = 0; i < count; i++)
            {
                var pid = infos[i].Process.ProcessId;
                if (holders.ContainsKey(pid)) continue;
                holders[pid] = new RunningProcess(pid, NameOf(pid, infos[i].AppName), null);
            }
            return [.. holders.Values];
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return [];
        }
        finally
        {
            RmEndSession(session);
        }
    }

    private static bool IsProgramFile(string file) =>
        file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    // The image name is what a user finds in the task manager; the Restart Manager's own text is a friendly name that can differ
    private static string NameOf(int pid, string friendly)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return string.IsNullOrWhiteSpace(friendly) ? "?" : friendly;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TsSessionId;
        [MarshalAs(UnmanagedType.Bool)] public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, int flags, StringBuilder key);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint fileCount, string[]? files, uint applicationCount, RmUniqueProcess[]? applications, uint serviceCount, string[]? services);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] RmProcessInfo[]? infos, ref uint reasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);
}
