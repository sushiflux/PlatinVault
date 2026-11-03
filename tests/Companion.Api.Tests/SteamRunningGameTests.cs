using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace Companion.Api.Tests;

/// <summary>
/// The running-game detection of Linux on a fake <c>/proc</c> (a temporary folder with <c>&lt;pid&gt;/cmdline</c> and
/// <c>&lt;pid&gt;/stat</c>), the Linux account guess, and how <see cref="SteamLocalStatsService"/> combines them per platform.
/// </summary>
public sealed class SteamRunningGameTests : IDisposable
{
    private const string Home = "/home/player";
    private readonly string _proc = Path.Combine(Path.GetTempPath(), "pv-proc-" + Guid.NewGuid().ToString("N")[..10]);

    public SteamRunningGameTests() => Directory.CreateDirectory(_proc);

    public void Dispose()
    {
        try { Directory.Delete(_proc, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>A process: its arguments (written NUL-separated with a trailing NUL, like the kernel does), parent and start time.</summary>
    private void Process(int pid, int ppid, ulong start, string comm, params string[] args)
    {
        var dir = Path.Combine(_proc, pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "cmdline"), args.Length == 0 ? [] : Encoding.UTF8.GetBytes(string.Join('\0', args) + "\0"));
        // Fields 3..22 after "(comm)": state ppid pgrp session tty_nr tpgid flags minflt cminflt majflt cmajflt utime stime cutime
        // cstime priority nice num_threads itrealvalue starttime
        File.WriteAllText(Path.Combine(dir, "stat"), $"{pid} ({comm}) S {ppid} {pid} {pid} 0 -1 4194560 100 0 0 0 5 3 0 0 20 0 1 0 {start} 1000000 200 18446744073709551615");
    }

    private ProcRunningGameSource Source(int ownPid = 1, TimeSpan? interval = null, Func<long>? ticks = null) =>
        new(_proc, ownPid, interval ?? TimeSpan.Zero, ticks);

    private static readonly string[] SteamClient = ["/home/player/.local/share/Steam/ubuntu12_32/steam", "-srt-logger-opened"];

    private static string[] Reaper(uint appId, params string[] game) =>
        ["/home/player/.local/share/Steam/ubuntu12_32/reaper", "SteamLaunch", $"AppId={appId}", "--", .. game];

    [Fact]
    public void A_native_game_started_by_Steam_is_found_by_its_reaper_process()
    {
        Process(100, 1, 500, "steam", SteamClient);
        Process(200, 100, 900, "reaper", Reaper(620, "/home/player/.local/share/Steam/steamapps/common/Portal 2/portal2.sh", "-game", "portal2"));
        Process(201, 200, 905, "portal2_linux", "/home/player/.local/share/Steam/steamapps/common/Portal 2/portal2_linux", "-game", "portal2");

        Assert.Equal(620u, Source().GetRunningAppId());
        Assert.Equal(RunningGameMethod.ProcessList, Source().Method);
    }

    [Fact]
    public void A_Proton_game_is_found_the_same_way()
    {
        // reaper -> Steam Linux Runtime entry point -> proton -> wine -> the Windows exe; only the reaper line carries SteamLaunch
        Process(300, 100, 1000, "reaper", Reaper(1245620,
            "/home/player/.local/share/Steam/steamapps/common/SteamLinuxRuntime_sniper/_v2-entry-point", "--verb=waitforexitandrun", "--",
            "/home/player/.local/share/Steam/steamapps/common/Proton - Experimental/proton", "waitforexitandrun",
            "/home/player/.local/share/Steam/steamapps/common/ELDEN RING/Game/start_protected_game.exe"));
        Process(301, 300, 1001, "pressure-vessel", "/usr/lib/pressure-vessel/from-host/bin/pressure-vessel-adverb", "--", "proton");
        Process(302, 301, 1005, "eldenring.exe", @"Z:\home\player\.local\share\Steam\steamapps\common\ELDEN RING\Game\eldenring.exe");

        Assert.Equal(1245620u, Source().GetRunningAppId());
    }

    [Fact]
    public void Without_a_running_game_the_answer_is_zero()
    {
        Process(100, 1, 500, "steam", SteamClient);
        Process(110, 100, 510, "steamwebhelper", "/home/player/.local/share/Steam/ubuntu12_64/steamwebhelper", "-lang=german");
        Process(120, 1, 520, "bash", "/bin/bash");
        Process(130, 1, 530, "kworker/0:1");
        Directory.CreateDirectory(Path.Combine(_proc, "self"));
        Directory.CreateDirectory(Path.Combine(_proc, "sys"));

        Assert.Equal(0u, Source().GetRunningAppId());
        Assert.Empty(Source().Scan());
    }

    [Fact]
    public void Only_the_exact_arguments_before_the_double_dash_count()
    {
        // One argument with spaces (a shell line), the words after "--" (the game's own arguments), missing or invalid ids
        Process(400, 1, 10, "bash", "/bin/bash", "-c", "reaper SteamLaunch AppId=10 -- game");
        Process(401, 1, 11, "game", "/opt/game", "--", "SteamLaunch", "AppId=20");
        Process(402, 1, 12, "reaper", "reaper", "AppId=30", "--", "game");
        Process(403, 1, 13, "reaper", "reaper", "SteamLaunch", "AppId=abc", "--", "game");
        Process(404, 1, 14, "reaper", "reaper", "SteamLaunch", "AppId=0", "--", "game");
        Process(405, 1, 15, "reaper", "reaper", "SteamLaunch", "appid=40", "--", "game");

        Assert.Empty(Source().Scan());
    }

    [Fact]
    public void With_several_games_the_newest_wins()
    {
        Process(500, 1, 2000, "reaper", Reaper(70, "hl.sh"));
        Process(510, 1, 3000, "reaper", Reaper(440, "tf.sh"));
        Process(505, 1, 2500, "reaper", Reaper(570, "dota.sh"));

        var scan = Source().Scan();

        Assert.Equal([440u, 570u, 70u], scan.Select(p => p.AppId));
        Assert.Equal(510, scan[0].ProcessId);
        Assert.Equal(3000UL, scan[0].StartTime);
        Assert.Equal(440u, Source().GetRunningAppId());
    }

    [Fact]
    public void Our_own_process_and_its_parents_are_ignored()
    {
        // PlatinVault added to Steam as a non-Steam game: its own reaper must not count as "the running game"
        Process(600, 1, 100, "reaper", Reaper(3000000001, "/opt/platinvault/PlatinVault"));
        Process(601, 600, 101, "PlatinVault", "/opt/platinvault/PlatinVault");
        Process(700, 1, 200, "reaper", Reaper(620, "portal2.sh"));

        Assert.Equal(620u, Source(ownPid: 601).GetRunningAppId());

        Directory.Delete(Path.Combine(_proc, "700"), recursive: true);
        Assert.Equal(0u, Source(ownPid: 601).GetRunningAppId());
    }

    [Fact]
    public void The_stat_line_is_read_even_when_the_name_has_spaces_and_parentheses()
    {
        var stat = ProcRunningGameSource.ParseStat("42 (Steam (x) y) S 7 42 42 0 -1 0 0 0 0 0 0 0 0 0 20 0 1 0 98765 0 0");

        Assert.Equal((7, 98765UL), stat);
        Assert.Null(ProcRunningGameSource.ParseStat("garbage"));
        Assert.Null(ProcRunningGameSource.ParseStat(null));
    }

    [Fact]
    public void A_missing_proc_folder_means_no_game_and_no_error()
    {
        var source = new ProcRunningGameSource(Path.Combine(_proc, "missing"), 1, TimeSpan.Zero);

        Assert.Equal(0u, source.GetRunningAppId());
    }

    [Fact]
    public void The_full_scan_runs_at_most_once_per_interval()
    {
        var now = 0L;
        var source = Source(interval: TimeSpan.FromSeconds(1), ticks: () => now);
        Assert.Equal(0u, source.GetRunningAppId());

        Process(800, 1, 50, "reaper", Reaper(620, "portal2.sh"));
        now = 500;
        Assert.Equal(0u, source.GetRunningAppId());
        now = 1000;
        Assert.Equal(620u, source.GetRunningAppId());
    }

    // ---- the active account on Linux (unverified there: loginusers.vdf, else the stats file names) ------------------------

    [Fact]
    public void The_most_recent_login_is_the_active_account()
    {
        const string vdf = """
            "users"
            {
            	"76561197960265729"
            	{
            		"AccountName"		"first"
            		"MostRecent"		"0"
            		"Timestamp"		"1800000000"
            	}
            	"76561197960265738"
            	{
            		"AccountName"		"second"
            		"mostrecent"		"1"
            		"Timestamp"		"1700000000"
            	}
            }
            """;

        Assert.Equal(10u, SteamAccounts.FromLoginUsers(vdf));
        // Without a MostRecent flag the newest Timestamp decides
        Assert.Equal(1u, SteamAccounts.FromLoginUsers(vdf.Replace("\"mostrecent\"\t\t\"1\"", "\"mostrecent\"\t\t\"0\"", StringComparison.Ordinal)));
        Assert.Equal(0u, SteamAccounts.FromLoginUsers("\"users\"\n{\n}\n"));
        Assert.Equal(0u, SteamAccounts.FromLoginUsers("not a vdf"));
    }

    [Fact]
    public void Without_loginusers_the_newest_stats_file_names_the_account()
    {
        var root = Path.Combine(_proc, "Steam");
        var stats = Path.Combine(root, "appcache", "stats");
        Directory.CreateDirectory(stats);
        File.WriteAllBytes(Path.Combine(stats, "UserGameStats_111_620.bin"), [0]);
        File.WriteAllBytes(Path.Combine(stats, "UserGameStats_222_70.bin"), [0]);
        File.WriteAllBytes(Path.Combine(stats, "UserGameStatsSchema_620.bin"), [0]);
        File.SetLastWriteTimeUtc(Path.Combine(stats, "UserGameStats_111_620.bin"), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Combine(stats, "UserGameStats_222_70.bin"), new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal((222u, SteamAccountMethod.StatsFiles), SteamAccounts.DetectLinux(root));

        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "loginusers.vdf"),
            "\"users\"\n{\n\t\"76561197960265839\"\n\t{\n\t\t\"MostRecent\"\t\t\"1\"\n\t}\n}\n");
        Assert.Equal((111u, SteamAccountMethod.LoginUsers), SteamAccounts.DetectLinux(root));
        Assert.Equal((0u, SteamAccountMethod.None), SteamAccounts.DetectLinux(Path.Combine(_proc, "nothing")));
    }

    // ---- SteamLocalStatsService: which platform reads what -------------------------------------------------------------

    private sealed class CountingSource(RunningGameMethod method, uint appId) : IRunningGameSource
    {
        public int Calls { get; private set; }

        public RunningGameMethod Method => method;

        public uint GetRunningAppId()
        {
            Calls++;
            return appId;
        }
    }

    private static SteamLocalStatsService Service(SteamLocatorInput input, IRunningGameSource source) =>
        new(NullLogger<SteamLocalStatsService>.Instance, new SteamLocationProvider(input), source);

    [Fact]
    public void Linux_without_a_Steam_folder_never_scans_the_process_list()
    {
        // A container on Unraid: no Steam, so no /proc scan every 350 ms and nothing found
        var source = new CountingSource(RunningGameMethod.ProcessList, 620);
        var service = Service(new SteamLocatorInput { Platform = SteamPlatform.Linux, FileSystem = new FakeSteamFileSystem(), HomeDirectory = Home, Environment = _ => null }, source);

        Assert.Equal(0u, service.GetLocalRunningAppId());
        Assert.False(service.IsLocalSteamAvailable());
        Assert.Equal(0, source.Calls);
        Assert.Equal((0u, SteamAccountMethod.None), service.GetActiveAccount());
    }

    [Fact]
    public void Linux_with_a_Steam_folder_reads_the_process_list()
    {
        var source = new CountingSource(RunningGameMethod.ProcessList, 620);
        var fs = new FakeSteamFileSystem().Steam(Home + "/.local/share/Steam");
        var service = Service(new SteamLocatorInput { Platform = SteamPlatform.Linux, FileSystem = fs, HomeDirectory = Home, Environment = _ => null }, source);

        Assert.Equal(620u, service.GetLocalRunningAppId());
        Assert.Equal(Home + "/.local/share/Steam", service.GetSteamPath());
        Assert.True(service.Location.IsExperimental);
        Assert.Equal(RunningGameMethod.ProcessList, service.RunningGameMethod);
    }

    [Fact]
    public void Windows_reads_the_registry_value_with_or_without_a_Steam_folder_as_before()
    {
        var source = new CountingSource(RunningGameMethod.Registry, 70);
        var service = Service(new SteamLocatorInput { Platform = SteamPlatform.Windows, RegistrySteamPath = () => null, Environment = _ => null }, source);

        Assert.Equal(70u, service.GetLocalRunningAppId());
        Assert.Equal(1, source.Calls);
        Assert.Null(service.GetSteamPath());
        Assert.False(service.Location.IsExperimental);
    }

    [Fact]
    public void Each_platform_gets_its_own_detection()
    {
        Assert.Equal(RunningGameMethod.Registry, RunningGameSources.For(SteamPlatform.Windows).Method);
        Assert.Equal(RunningGameMethod.ProcessList, RunningGameSources.For(SteamPlatform.Linux).Method);
        Assert.Equal(RunningGameMethod.None, RunningGameSources.For(SteamPlatform.Other).Method);
        // A plain service (the Windows bridge constructs it like this) keeps the registry on Windows
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(RunningGameMethod.Registry, new SteamLocalStatsService(NullLogger<SteamLocalStatsService>.Instance).RunningGameMethod);
        }
    }

    [Fact]
    public void Linux_reads_libraryfolders_from_steamapps_and_config()
    {
        var steam = Path.Combine(_proc, "Steam");
        var second = Path.Combine(_proc, "Games2");
        var third = Path.Combine(_proc, "Games3");
        foreach (var dir in new[] { Path.Combine(steam, "steamapps"), Path.Combine(steam, "config"), Path.Combine(second, "steamapps"), Path.Combine(third, "steamapps") })
        {
            Directory.CreateDirectory(dir);
        }
        static string Vdf(string path) => "\"libraryfolders\"\n{\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + path.Replace("\\", "\\\\") + "\"\n\t}\n}\n";
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), Vdf(second));
        File.WriteAllText(Path.Combine(steam, "config", "libraryfolders.vdf"), Vdf(third));

        Assert.Equal([steam, second, third], SteamLocalLibraryService.LibraryFolders(steam, SteamPlatform.Linux));
        // Windows keeps reading only steamapps/libraryfolders.vdf, as before
        Assert.Equal([steam, second], SteamLocalLibraryService.LibraryFolders(steam, SteamPlatform.Windows));
    }

    // ---- end to end on a real Linux file system (runs in the Docker check of the Linux build) ---------------------------

    /// <summary>Binary KeyValues as Steam writes them (0x00 node, 0x01 string, 0x02 int32, 0x08 end), for synthetic stats files.</summary>
    private sealed class BinaryVdf
    {
        private readonly MemoryStream _out = new();

        public BinaryVdf Node(string key, Action<BinaryVdf> children)
        {
            _out.WriteByte(0x00);
            Key(key);
            children(this);
            _out.WriteByte(0x08);
            return this;
        }

        public BinaryVdf Text(string key, string value)
        {
            _out.WriteByte(0x01);
            Key(key);
            Key(value);
            return this;
        }

        public BinaryVdf Int(string key, uint value)
        {
            _out.WriteByte(0x02);
            Key(key);
            _out.Write(BitConverter.GetBytes(value));
            return this;
        }

        public byte[] Bytes()
        {
            _out.WriteByte(0x08);
            return _out.ToArray();
        }

        private void Key(string text)
        {
            _out.Write(Encoding.UTF8.GetBytes(text));
            _out.WriteByte(0);
        }
    }

    [Fact]
    public void On_a_real_Linux_a_synthetic_Steam_and_proc_tree_give_the_running_game_account_and_unlocks()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Runs on Linux only (the Docker check of the Linux build).");
        }

        // ~/.steam/steam -> ~/.local/share/Steam, like Valve's installer leaves it
        var home = Path.Combine(_proc, "home");
        var steam = Path.Combine(home, ".local", "share", "Steam");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(steam, "config"));
        Directory.CreateDirectory(Path.Combine(steam, "appcache", "stats"));
        Directory.CreateDirectory(Path.Combine(home, ".steam"));
        Directory.CreateSymbolicLink(Path.Combine(home, ".steam", "steam"), steam);
        File.WriteAllText(Path.Combine(steam, "config", "loginusers.vdf"),
            "\"users\"\n{\n\t\"76561197960265738\"\n\t{\n\t\t\"AccountName\"\t\t\"player\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t}\n}\n");

        File.WriteAllBytes(Path.Combine(steam, "appcache", "stats", "UserGameStatsSchema_620.bin"), new BinaryVdf()
            .Node("620", app => app
                .Text("gamename", "Portal 2")
                .Node("stats", stats => stats
                    .Node("1", stat => stat
                        .Node("bits", bits => bits
                            .Node("0", bit => bit.Text("name", "ACH_WAKE_UP").Node("display", d => d.Node("name", n => n.Text("english", "Wake Up Call"))))
                            .Node("1", bit => bit.Text("name", "ACH_LASER").Node("display", d => d.Node("name", n => n.Text("english", "You Monster"))))))))
            .Bytes());
        File.WriteAllBytes(Path.Combine(steam, "appcache", "stats", "UserGameStats_10_620.bin"), new BinaryVdf()
            .Node("cache", cache => cache
                .Node("1", stat => stat.Int("data", 0b01).Node("AchievementTimes", times => times.Int("0", 1_700_000_000))))
            .Bytes());

        var proc = Path.Combine(_proc, "proc");
        Directory.CreateDirectory(proc);
        var game = Path.Combine(proc, "4242");
        Directory.CreateDirectory(game);
        File.WriteAllBytes(Path.Combine(game, "cmdline"), Encoding.UTF8.GetBytes(string.Join('\0', Reaper(620, "portal2.sh")) + "\0"));
        File.WriteAllText(Path.Combine(game, "stat"), "4242 (reaper) S 1 4242 4242 0 -1 0 0 0 0 0 0 0 0 0 20 0 1 0 12345 0 0");

        var service = new SteamLocalStatsService(
            NullLogger<SteamLocalStatsService>.Instance,
            new SteamLocationProvider(new SteamLocatorInput { Platform = SteamPlatform.Linux, HomeDirectory = home, Environment = _ => null }),
            new ProcRunningGameSource(proc, ownProcessId: 1, scanInterval: TimeSpan.Zero));

        Assert.Equal(steam, service.GetSteamPath());
        Assert.Equal(SteamLocationMethod.Symlink, service.Location.Method);
        Assert.Equal(620u, service.GetLocalRunningAppId());
        Assert.Equal((10u, SteamAccountMethod.LoginUsers), service.GetActiveAccount());
        Assert.NotEqual(0, service.GetLocalStatsStamp(620));

        var data = service.ReadLocalAppData(620, "english");
        Assert.NotNull(data);
        Assert.Equal("Portal 2", data.GameName);
        Assert.Equal(["ACH_WAKE_UP", "ACH_LASER"], data.Achievements.Select(a => a.ApiName));
        Assert.True(data.Achievements[0].Achieved);
        Assert.False(data.Achievements[1].Achieved);
        Assert.Equal("Wake Up Call", data.Achievements[0].DisplayName);
    }
}
