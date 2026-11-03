using System.Globalization;
using System.Text;

namespace PlatinVault.SteamLocal;

/// <summary>How the game that runs right now is detected.</summary>
public enum RunningGameMethod
{
    /// <summary>Not possible on this platform.</summary>
    None,

    /// <summary>Windows: <c>HKCU\Software\Valve\Steam\RunningAppID</c>, which Steam sets while a game runs.</summary>
    Registry,

    /// <summary>
    /// Linux: the process list. Steam starts every game (Proton included) as <c>reaper SteamLaunch AppId=&lt;id&gt; -- ...</c>, so the
    /// app id is read from <c>/proc/&lt;pid&gt;/cmdline</c>. <c>RunningAppID</c> in <c>~/.steam/registry.vdf</c> is not used: it
    /// stays 0 since Steam's new UI (ValveSoftware/steam-for-linux#9672).
    /// </summary>
    ProcessList,
}

/// <summary>The Steam app id of the game that runs right now on this computer (0 = none).</summary>
public interface IRunningGameSource
{
    RunningGameMethod Method { get; }

    uint GetRunningAppId();
}

/// <summary>Nothing can be detected (macOS, unknown platforms).</summary>
public sealed class NoRunningGameSource : IRunningGameSource
{
    public static readonly NoRunningGameSource Instance = new();

    public RunningGameMethod Method => RunningGameMethod.None;

    public uint GetRunningAppId() => 0;
}

/// <summary>Windows: Steam's <c>RunningAppID</c> in the registry of the signed-in user - exactly what PlatinVault always read.</summary>
public sealed class RegistryRunningGameSource : IRunningGameSource
{
    public static readonly RegistryRunningGameSource Instance = new();

    public RunningGameMethod Method => RunningGameMethod.Registry;

    public uint GetRunningAppId()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        var value = SteamLocator.ReadRegistryValue(@"Software\Valve\Steam", "RunningAppID");
        return value is not null && uint.TryParse(value, out var appId) ? appId : 0;
    }
}

/// <summary>One Steam game process found in the process list.</summary>
/// <param name="ProcessId">The pid of the <c>reaper SteamLaunch ...</c> process.</param>
/// <param name="AppId">The Steam app id from <c>AppId=&lt;id&gt;</c>.</param>
/// <param name="StartTime">Start time of the process in clock ticks since boot (<c>/proc/&lt;pid&gt;/stat</c> field 22); 0 when unknown.</param>
public sealed record SteamLaunchProcess(int ProcessId, uint AppId, ulong StartTime);

/// <summary>
/// Linux: finds the running Steam game in <c>/proc</c> (see <see cref="RunningGameMethod.ProcessList"/>). The full scan reads one
/// small <c>cmdline</c> file per process and runs at most once per <see cref="DefaultScanInterval"/>; calls in between get the last
/// answer. Several games at once: the newest process wins. The process of this program and its parents are ignored (PlatinVault
/// started from Steam as a non-Steam game would otherwise count as "the running game").
/// <para>
/// Experimental: the <c>reaper SteamLaunch AppId=</c> form is documented by other projects (Quasar issue #645, process listings),
/// not by Valve, and has not been checked here on a real Linux Steam. A Flatpak Steam runs in its own sandbox; whether a service
/// outside sees its processes is not verified. Inside a container <c>/proc</c> only shows the container's own processes unless the
/// container shares the host's process list (Docker <c>pid: host</c>).
/// </para>
/// </summary>
public sealed class ProcRunningGameSource : IRunningGameSource
{
    public const string DefaultProcRoot = "/proc";
    public static readonly TimeSpan DefaultScanInterval = TimeSpan.FromSeconds(1);

    private const string LaunchToken = "SteamLaunch";
    private const string AppIdPrefix = "AppId=";
    private static ReadOnlySpan<byte> LaunchTokenBytes => "SteamLaunch"u8;

    // The parent chain of this process is followed at most this far (a real chain is a handful of entries)
    private const int MaxAncestors = 64;

    private readonly string _procRoot;
    private readonly int _ownProcessId;
    private readonly long _scanMs;
    private readonly Func<long> _ticks;
    private readonly Lock _gate = new();
    private uint _lastAppId;
    private long _scannedAt;
    private bool _scanned;

    /// <param name="procRoot">The proc file system (a fake folder in tests).</param>
    /// <param name="ownProcessId">This process (default <see cref="Environment.ProcessId"/>); it and its parents are ignored.</param>
    /// <param name="scanInterval">Minimum time between two full scans (default <see cref="DefaultScanInterval"/>; zero = every call).</param>
    /// <param name="tickCount">Milliseconds clock for tests.</param>
    public ProcRunningGameSource(string procRoot = DefaultProcRoot, int? ownProcessId = null, TimeSpan? scanInterval = null, Func<long>? tickCount = null)
    {
        _procRoot = procRoot;
        _ownProcessId = ownProcessId ?? Environment.ProcessId;
        _scanMs = (long)(scanInterval ?? DefaultScanInterval).TotalMilliseconds;
        _ticks = tickCount ?? (() => Environment.TickCount64);
    }

    public RunningGameMethod Method => RunningGameMethod.ProcessList;

    public uint GetRunningAppId()
    {
        lock (_gate)
        {
            var now = _ticks();
            if (_scanned && now - _scannedAt < _scanMs) return _lastAppId;
            var found = Scan();
            _lastAppId = found.Count > 0 ? found[0].AppId : 0;
            _scannedAt = now;
            _scanned = true;
            return _lastAppId;
        }
    }

    /// <summary>Every Steam game process, newest first (by start time, then pid). Empty when <c>/proc</c> is not readable.</summary>
    public IReadOnlyList<SteamLaunchProcess> Scan()
    {
        IEnumerable<string> entries;
        try
        {
            if (!Directory.Exists(_procRoot)) return [];
            entries = Directory.EnumerateDirectories(_procRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        HashSet<int>? ignored = null;
        var found = new List<SteamLaunchProcess>();
        try
        {
            foreach (var entry in entries)
            {
                if (!int.TryParse(Path.GetFileName(entry), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
                var appId = ParseSteamLaunch(ReadBytes(Path.Combine(entry, "cmdline")));
                if (appId == 0) continue;

                ignored ??= OwnProcessChain();
                if (ignored.Contains(pid)) continue;

                var stat = ParseStat(ReadText(Path.Combine(entry, "stat")));
                found.Add(new SteamLaunchProcess(pid, appId, stat?.StartTime ?? 0));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // /proc changed while it was listed: what was found so far stands, the next scan sees the rest
        }

        found.Sort((a, b) => b.StartTime != a.StartTime ? b.StartTime.CompareTo(a.StartTime) : b.ProcessId.CompareTo(a.ProcessId));
        return found;
    }

    /// <summary>
    /// The app id of a <c>SteamLaunch</c> command line (the NUL-separated arguments of <c>/proc/&lt;pid&gt;/cmdline</c>), else 0.
    /// Only the arguments before the first <c>--</c> count (after it comes the game's own command line), and both the exact
    /// argument <c>SteamLaunch</c> and an argument <c>AppId=&lt;digits&gt;</c> must be there.
    /// </summary>
    public static uint ParseSteamLaunch(ReadOnlySpan<byte> cmdline)
    {
        // Runs for every process once per scan: almost every command line is rejected on its raw bytes, without decoding or splitting
        if (cmdline.IsEmpty || cmdline.IndexOf(LaunchTokenBytes) < 0) return 0;
        var text = Encoding.UTF8.GetString(cmdline);
        var hasLaunch = false;
        uint appId = 0;
        foreach (var arg in text.Split('\0'))
        {
            if (arg == "--") break;
            if (arg == LaunchToken)
            {
                hasLaunch = true;
            }
            else if (arg.StartsWith(AppIdPrefix, StringComparison.Ordinal) &&
                     uint.TryParse(arg.AsSpan(AppIdPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                appId = id;
            }
        }
        return hasLaunch ? appId : 0;
    }

    /// <summary>Parent pid and start time from <c>/proc/&lt;pid&gt;/stat</c>; the name in parentheses may contain spaces and parentheses.</summary>
    internal static (int ParentId, ulong StartTime)? ParseStat(string? stat)
    {
        if (string.IsNullOrEmpty(stat)) return null;
        var close = stat.LastIndexOf(')');
        if (close < 0) return null;
        // After the name: field 3 (state), 4 (ppid), ... 22 (starttime) -> index 0, 1, ... 19
        var fields = stat[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ppid)) return null;
        var start = fields.Length > 19 && ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : 0;
        return (ppid, start);
    }

    // This process and every parent up to init
    private HashSet<int> OwnProcessChain()
    {
        var chain = new HashSet<int>();
        var pid = _ownProcessId;
        for (var i = 0; i < MaxAncestors && pid > 0 && chain.Add(pid); i++)
        {
            var stat = ParseStat(ReadText(Path.Combine(_procRoot, pid.ToString(CultureInfo.InvariantCulture), "stat")));
            if (stat is null) break;
            pid = stat.Value.ParentId;
        }
        return chain;
    }

    private static byte[] ReadBytes(string path)
    {
        try
        {
            // procfs reports a length of 0; File.ReadAllBytes reads such files to their end
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>The running-game detection of a platform.</summary>
public static class RunningGameSources
{
    public static IRunningGameSource For(SteamPlatform platform) => platform switch
    {
        SteamPlatform.Windows => RegistryRunningGameSource.Instance,
        SteamPlatform.Linux => new ProcRunningGameSource(),
        _ => NoRunningGameSource.Instance,
    };
}
