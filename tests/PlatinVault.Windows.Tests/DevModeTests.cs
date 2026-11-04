using System.Net;
using System.Net.Sockets;
using PlatinVault.Desktop;
using PlatinVault.Desktop.Database;
using PlatinVault.Windows.Install;
using PlatinVault.Versioning;

namespace PlatinVault.Windows.Tests;

/// <summary>The development run of the Windows program (<c>PlatinVault.exe --dev</c>, started by start-windows.ps1).</summary>
public sealed class DevModeOptionsTests
{
    [Fact]
    public void Dev_options_are_parsed()
    {
        var o = DesktopOptions.Parse(["--dev", @"C:\repo", "--server-port", "5234", "--ui-url", "http://127.0.0.1:5236", "--offscreen"]);
        Assert.Equal(@"C:\repo", o.Dev);
        Assert.Equal(5234, o.ServerPort);
        Assert.Equal("http://127.0.0.1:5236/", o.UiUrl);
        Assert.True(o.Offscreen);
    }

    [Theory]
    [InlineData("--server-port", "80")]
    [InlineData("--server-port", "70000")]
    [InlineData("--server-port", "abc")]
    [InlineData("--server-port", "-5")]
    public void Ports_must_be_real_unprivileged_ports(string option, string value)
    {
        Assert.Throws<ArgumentException>(() => DesktopOptions.Parse(["--dev", @"C:\repo", option, value]));
    }

    [Theory]
    [InlineData("https://127.0.0.1:5236/")]
    [InlineData("http://example.com/")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("not a url")]
    public void The_window_only_shows_a_page_of_this_pc(string url)
    {
        Assert.Throws<ArgumentException>(() => DesktopOptions.Parse(["--dev", @"C:\repo", "--ui-url", url]));
    }

    [Fact]
    public void Localhost_is_accepted_as_ui_address()
    {
        Assert.Equal("http://localhost:5236/", DesktopOptions.Parse(["--dev", @"C:\repo", "--ui-url", "http://localhost:5236"]).UiUrl);
    }

    [Theory]
    [InlineData("--offscreen")]
    [InlineData("--quit")]
    [InlineData("--dev-hotkey")]
    public void Dev_switches_need_dev(string option)
    {
        Assert.Throws<ArgumentException>(() => DesktopOptions.Parse([option]));
    }

    [Fact]
    public void Dev_value_options_need_dev()
    {
        Assert.Throws<ArgumentException>(() => DesktopOptions.Parse(["--server-port", "5234"]));
        Assert.Throws<ArgumentException>(() => DesktopOptions.Parse(["--ui-url", "http://127.0.0.1:5236"]));
        Assert.Throws<ArgumentException>(() => DesktopOptions.Parse(["--backups-dir", @"C:\b"]));
    }

    [Theory]
    [InlineData("--uninstall")]
    [InlineData("--prepare")]
    public void Dev_never_runs_the_installers_modes(string mode)
    {
        Assert.Throws<ArgumentException>(() => DesktopOptions.Parse(["--dev", @"C:\repo", mode]));
    }

    [Fact]
    public void Dev_and_test_root_exclude_each_other()
    {
        Assert.Throws<ArgumentException>(() => DesktopOptions.Parse(["--dev", @"C:\repo", "--test-root", @"C:\t"]));
    }

    [Fact]
    public void Backup_now_and_restore_stay_available_in_dev()
    {
        Assert.True(DesktopOptions.Parse(["--dev", @"C:\repo", "--backup-now"]).BackupNow);
        Assert.Equal("x.db", DesktopOptions.Parse(["--dev", @"C:\repo", "--restore", "x.db"]).Restore);
    }
}

public sealed class DevLayoutTests
{
    private static T OnOtherThread<T>(Func<T> work)
    {
        T result = default!;
        var thread = new Thread(() => result = work());
        thread.Start();
        thread.Join();
        return result;
    }

    [Fact]
    public void A_dev_layout_never_touches_the_real_profile()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.CreateDev(temp.Sub("data"), temp.Sub("backups"));

        Assert.True(layout.IsDev);
        Assert.False(layout.IsTest);
        Assert.IsType<FileRegistry>(layout.Registry);
        Assert.Null(layout.InstalledDir); // no autostart entry in the tray menu
        foreach (var path in new[] { layout.DefaultInstallDir, layout.DataDir, layout.StartMenuShortcut, layout.DesktopShortcut, ((FileRegistry)layout.Registry).Path })
            Assert.StartsWith(temp.Path, path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Backups_live_outside_the_data_folder()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.CreateDev(temp.Sub("data"), temp.Sub("backups"));

        Assert.Equal(temp.Sub("backups"), layout.Data.Backups);
        Assert.False(layout.Data.Backups.StartsWith(layout.Data.Root, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith(temp.Sub("data"), layout.Data.Database, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_installed_programs_backups_stay_inside_its_data_folder()
    {
        var layout = InstallLayout.Create(null);
        Assert.Equal(Path.Combine(layout.DataDir, "backups"), layout.Data.Backups);
    }

    [Fact]
    public void Dev_runs_never_share_a_mutex_with_an_installation_or_with_each_other()
    {
        using var temp = new TempFolder();
        var a = InstallLayout.CreateDev(temp.Sub("a"), temp.Sub("ab"));
        var b = InstallLayout.CreateDev(temp.Sub("b"), temp.Sub("bb"));
        var installed = InstallLayout.Create(null);

        Assert.NotEqual(installed.MutexName, a.MutexName);
        Assert.NotEqual(installed.PipeName, a.PipeName);
        Assert.NotEqual(a.MutexName, b.MutexName);
        Assert.NotEqual(a.PipeName, b.PipeName);
        Assert.Equal(a.MutexName, InstallLayout.CreateDev(temp.Sub("a"), temp.Sub("ab")).MutexName);
    }

    [Fact]
    public void A_second_start_in_the_same_dev_layout_is_recognised_and_an_installation_is_not_affected()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.CreateDev(temp.Sub("data"), temp.Sub("backups"));
        using var first = AppInstance.TryAcquire(layout);
        Assert.NotNull(first);
        // A mutex is recursive per thread: ask from another thread, like a second process would
        Assert.True(OnOtherThread(() => AppInstance.IsRunning(layout)));
        Assert.Null(OnOtherThread(() => AppInstance.TryAcquire(layout)));
        // Another (test-root) installation layout is not affected. Never ask the real installation (InstallLayout.Create(null)): it is running whenever
        // PlatinVault is open on this PC, so the result would depend on the machine. Different names are what keeps the two apart.
        var installation = InstallLayout.Create(temp.Sub("installation"));
        Assert.NotEqual(installation.MutexName, layout.MutexName);
        Assert.False(OnOtherThread(() => AppInstance.IsRunning(installation)));
    }
}

public sealed class DevEnvironmentTests
{
    private static string FakeRepo(TempFolder temp, string version)
    {
        var repo = temp.Sub("repo");
        Directory.CreateDirectory(Path.Combine(repo, "src", "Companion.Api"));
        File.WriteAllText(Path.Combine(repo, "src", "Companion.Api", "Companion.Api.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(repo, "version.txt"), version);
        return repo;
    }

    [Fact]
    public void Defaults_put_data_and_backups_into_the_repository()
    {
        using var temp = new TempFolder();
        var repo = FakeRepo(temp, "v2026.11.5\nv2026.11.5.9.321");
        var env = DesktopEnvironment.Create(DesktopOptions.Parse(["--dev", repo]));

        Assert.True(env.IsDev);
        Assert.Equal(Path.Combine(repo, ".data", "desktop-dev"), env.Data.Root);
        Assert.Equal(Path.Combine(repo, ".backups", "desktop-dev"), env.Data.Backups);
        Assert.Equal("Development", env.EnvironmentName);
        Assert.Equal(Path.Combine(repo, "src", "PlatinVault.Desktop"), env.ContentRoot);
        // The server's Development convention: repository two levels above the content root
        Assert.Equal(repo, Path.GetFullPath(Path.Combine(env.ContentRoot, "..", "..")));
        Assert.Equal(Path.Combine(repo, "artifacts", "bridge"), env.BridgeDir);
    }

    [Fact]
    public void The_version_is_the_repositorys_last_commit_with_its_counters_and_dev_inside_the_parentheses()
    {
        using var temp = new TempFolder();
        var env = DesktopEnvironment.Create(DesktopOptions.Parse(["--dev", FakeRepo(temp, "v2026.11.5\nv2026.11.5.9.321")]));
        Assert.Equal("v2026.11.5 (9.321-dev)", env.Version);
        Assert.Equal("v2026.11.5.9.321-dev", env.VersionTag);
    }

    [Fact]
    public void Dev_has_no_update_check_and_no_hotkey_unless_asked()
    {
        using var temp = new TempFolder();
        var repo = FakeRepo(temp, "v2026.11.5");
        var plain = DesktopEnvironment.Create(DesktopOptions.Parse(["--dev", repo]));
        Assert.False(plain.UpdateCheckAllowed);
        Assert.False(plain.HotkeyAllowed);
        Assert.False(plain.Quiet);

        Assert.True(DesktopEnvironment.Create(DesktopOptions.Parse(["--dev", repo, "--dev-hotkey"])).HotkeyAllowed);

        var quiet = DesktopEnvironment.Create(DesktopOptions.Parse(["--dev", repo, "--offscreen", "--dev-hotkey"]));
        Assert.True(quiet.Quiet);
        Assert.False(quiet.HotkeyAllowed);
    }

    [Fact]
    public void Test_and_offscreen_runs_are_never_heard_a_normal_run_is()
    {
        using var temp = new TempFolder();
        var repo = FakeRepo(temp, "v2026.11.5");
        Assert.False(DesktopEnvironment.Create(DesktopOptions.Parse(["--dev", repo])).Silent);
        Assert.True(DesktopEnvironment.Create(DesktopOptions.Parse(["--dev", repo, "--offscreen"])).Silent);
        var testRoot = Path.Combine(temp.Path, "test-root");
        Assert.True(DesktopEnvironment.Create(DesktopOptions.Parse(["--test-root", testRoot, "--smoke", Path.Combine(temp.Path, "out")])).Silent);
    }

    [Theory]
    [InlineData("dev", true, "dev", "v2026.11.12 (13.557-dev)", "v2026.11.12.13.557-dev")]
    [InlineData("beta", false, "beta", "v2026.11.12 (13.557 β)", "v2026.11.12.13.557-beta")]
    [InlineData("release", false, "release", "v2026.11.12 (13.557)", "v2026.11.12")]
    public void An_installed_dev_build_never_checks_for_updates_a_beta_or_release_does(string channelFile, bool dev, string channel, string display, string tag)
    {
        using var temp = new TempFolder();
        File.WriteAllText(Path.Combine(temp.Path, "release-channel.txt"), channelFile);
        File.WriteAllText(Path.Combine(temp.Path, "version.txt"), "v2026.11.12\nv2026.11.12.13.557");
        // An installation (not --dev, not a test root) whose program folder is the scratch folder
        var env = new DesktopEnvironment { Options = DesktopOptions.Parse([]), Layout = InstallLayout.Create(null, null), InstallDir = temp.Path };

        Assert.False(env.IsDev);
        Assert.Equal(channel, env.ReleaseChannel);
        Assert.Equal(dev ? VersionKind.Dev : VersionFormat.KindFromChannel(channelFile), env.Kind);
        Assert.Equal(display, env.Version);
        Assert.Equal(tag, env.VersionTag);
        Assert.Equal(!dev, env.UpdateCheckAllowed);
        // The dev build is still a normal installation otherwise: the server runs as Production
        Assert.Equal("Production", env.EnvironmentName);
    }

    [Fact]
    public void A_folder_that_is_not_the_repository_is_refused()
    {
        using var temp = new TempFolder();
        Assert.Throws<ArgumentException>(() => DesktopEnvironment.Create(DesktopOptions.Parse(["--dev", temp.Path])));
    }

    [Fact]
    public void An_installation_keeps_its_behaviour()
    {
        var env = DesktopEnvironment.Create(DesktopOptions.Parse([]));
        Assert.False(env.IsDev);
        Assert.True(env.UpdateCheckAllowed);
        Assert.True(env.HotkeyAllowed);
        Assert.Equal("Production", env.EnvironmentName);
        Assert.Equal(env.InstallDir, env.ContentRoot);
    }
}

public sealed class FixedPortTests
{
    [Fact]
    public void A_fixed_free_port_is_used_as_is_and_ignores_the_remembered_one()
    {
        var port = LoopbackPorts.FreeLoopbackPort();
        Assert.Equal(port, LoopbackPorts.ChoosePort(port, remembered: 1234, busy: _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void A_fixed_busy_port_is_an_error_and_is_never_replaced()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var error = Assert.Throws<DatabaseException>(() => LoopbackPorts.ChoosePort(port, remembered: null, busy: p => new DatabaseException(DesktopTexts.Dev.PortBusy(DesktopTexts.Dev.ServerPort, p))));
            Assert.Contains(port.ToString(), error.Text.De);
            Assert.Contains(port.ToString(), error.Text.En);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Without_a_fixed_port_the_remembered_one_is_kept_while_free_else_a_free_one_is_picked()
    {
        // A remembered port outside Windows' dynamic/reserved ranges (review finding 4: ports from the dynamic range are replaced by a
        // stable one, see LoopbackPorts) is kept while it is free
        var free = LoopbackPorts.Pick(LoopbackPorts.Seed("test"), LoopbackPorts.ReadReserved(), LoopbackPorts.IsPortFree, remembered: null);
        Assert.Equal(free, LoopbackPorts.ChoosePort(null, free, _ => throw new InvalidOperationException()));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var busy = ((IPEndPoint)listener.LocalEndpoint).Port;
            var chosen = LoopbackPorts.ChoosePort(null, busy, _ => throw new InvalidOperationException());
            Assert.NotEqual(busy, chosen);
        }
        finally
        {
            listener.Stop();
        }
    }
}
