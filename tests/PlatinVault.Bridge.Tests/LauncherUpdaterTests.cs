using System.Net;
using System.Security.Cryptography;

namespace PlatinVault.Bridge.Tests;

[Collection(BridgeGlobals.Name)]
public sealed class LauncherUpdaterTests
{
    private sealed class FakeRunner(Func<string, ProcessStartResult> answer) : IProcessRunner
    {
        public List<(string File, string[] Arguments, Dictionary<string, string> Environment, TimeSpan Wait)> Calls { get; } = [];

        public ProcessStartResult Start(string fileName, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> extraEnvironment, TimeSpan wait)
        {
            Calls.Add((fileName, [.. arguments], new Dictionary<string, string>(extraEnvironment), wait));
            return answer(fileName);
        }
    }

    private static Func<string, string?> Path(params string[] available) => name => available.Contains(name) ? "/usr/bin/" + name : null;

    private static Func<string, string?> Env(params (string, string)[] values) => name => values.FirstOrDefault(v => v.Item1 == name).Item2;

    // ---- launcher -------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_steam_command_comes_first_and_gets_only_the_url_built_from_the_number()
    {
        var runner = new FakeRunner(_ => new ProcessStartResult(true));
        var launcher = new LinuxSteamLauncher(runner, Path("steam", "xdg-open"), false, Env(("DISPLAY", ":1")));

        var result = launcher.Launch(730);

        Assert.Equal(LaunchOutcome.Started, result.Outcome);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("/usr/bin/steam", call.File);
        Assert.Equal(["steam://run/730"], call.Arguments);
        Assert.Empty(call.Environment); // a display exists: nothing to add
        Assert.True(call.Wait > TimeSpan.Zero && call.Wait < TimeSpan.FromSeconds(3)); // long enough to see an immediate failure, never wait for a whole Steam client
    }

    [Fact]
    public void Steam_is_started_in_its_own_systemd_scope_so_a_restart_of_the_service_does_not_kill_it()
    {
        var runner = new FakeRunner(_ => new ProcessStartResult(true));
        var launcher = new LinuxSteamLauncher(runner, Path("steam", "systemd-run", "setsid"), false, Env(("DISPLAY", ":0")));

        Assert.Equal(LaunchOutcome.Started, launcher.Launch(730).Outcome);

        var call = Assert.Single(runner.Calls);
        Assert.Equal("/usr/bin/systemd-run", call.File);
        Assert.Equal(["--user", "--scope", "--collect", "--quiet", "/usr/bin/steam", "steam://run/730"], call.Arguments);
    }

    [Fact]
    public void Without_a_user_bus_the_launch_falls_back_to_setsid_and_then_to_the_plain_start()
    {
        // systemd-run fails at once when there is no user manager to talk to; setsid fails too (cannot happen in practice, but the chain must hold)
        var runner = new FakeRunner(file => file.EndsWith("systemd-run") ? new ProcessStartResult(true, 1)
            : file.EndsWith("setsid") ? new ProcessStartResult(false, null, "boom") : new ProcessStartResult(true));
        var launcher = new LinuxSteamLauncher(runner, Path("steam", "systemd-run", "setsid"), false, Env(("DISPLAY", ":0")));

        var result = launcher.Launch(730);

        Assert.Equal(LaunchOutcome.Started, result.Outcome);
        Assert.Equal(["/usr/bin/systemd-run", "/usr/bin/setsid", "/usr/bin/steam"], runner.Calls.Select(c => c.File));
        Assert.Equal(["/usr/bin/steam", "steam://run/730"], runner.Calls[1].Arguments); // setsid <command> <url>, no scope arguments
        Assert.Equal(["steam://run/730"], runner.Calls[2].Arguments);
    }

    [Fact]
    public void Every_tool_gets_the_detached_start_and_the_url_is_still_built_from_the_number_only()
    {
        var runner = new FakeRunner(_ => new ProcessStartResult(true, 0));
        new LinuxSteamLauncher(runner, Path("xdg-open", "systemd-run"), false, Env(("DISPLAY", ":0"))).Launch(620);

        var call = Assert.Single(runner.Calls);
        Assert.Equal("/usr/bin/systemd-run", call.File);
        Assert.Equal(["--user", "--scope", "--collect", "--quiet", "/usr/bin/xdg-open", "steam://run/620"], call.Arguments);
    }

    [Fact]
    public void A_service_without_a_display_gets_the_fallback_display()
    {
        var runner = new FakeRunner(_ => new ProcessStartResult(true));
        new LinuxSteamLauncher(runner, Path("steam"), false, Env()).Launch(730);
        Assert.Equal(":0", runner.Calls.Single().Environment["DISPLAY"]);

        var wayland = new FakeRunner(_ => new ProcessStartResult(true));
        new LinuxSteamLauncher(wayland, Path("steam"), false, Env(("WAYLAND_DISPLAY", "wayland-0"))).Launch(730);
        Assert.Empty(wayland.Calls.Single().Environment);
    }

    [Fact]
    public void When_steam_cannot_be_started_flatpak_and_then_xdg_open_are_tried()
    {
        var runner = new FakeRunner(file => file.EndsWith("xdg-open") ? new ProcessStartResult(true, 0) : new ProcessStartResult(false, null, "no such file"));
        var launcher = new LinuxSteamLauncher(runner, Path("steam", "flatpak", "xdg-open"), flatpakSteamInstalled: true, Env(("DISPLAY", ":0")));

        var result = launcher.Launch(620);

        Assert.Equal(LaunchOutcome.Started, result.Outcome);
        Assert.Equal(["/usr/bin/steam", "/usr/bin/flatpak", "/usr/bin/xdg-open"], runner.Calls.Select(c => c.File));
        Assert.Equal(["run", "com.valvesoftware.Steam", "steam://run/620"], runner.Calls[1].Arguments);
    }

    [Fact]
    public void Flatpak_is_only_used_when_flatpak_steam_is_installed()
    {
        var runner = new FakeRunner(_ => new ProcessStartResult(true, 0));
        new LinuxSteamLauncher(runner, Path("flatpak", "xdg-open"), flatpakSteamInstalled: false, Env(("DISPLAY", ":0"))).Launch(620);
        Assert.Equal(["/usr/bin/xdg-open"], runner.Calls.Select(c => c.File));
    }

    [Fact]
    public void A_nonzero_exit_of_xdg_open_is_a_failure_and_no_tool_at_all_is_steam_not_found()
    {
        var failing = new FakeRunner(_ => new ProcessStartResult(true, 3));
        var failed = new LinuxSteamLauncher(failing, Path("xdg-open"), false, Env(("DISPLAY", ":0"))).Launch(620);
        Assert.Equal(LaunchOutcome.Failed, failed.Outcome);
        Assert.Contains("exit code 3", failed.Detail);

        var none = new FakeRunner(_ => new ProcessStartResult(true));
        var missing = new LinuxSteamLauncher(none, Path(), false, Env()).Launch(620);
        Assert.Equal(LaunchOutcome.SteamNotFound, missing.Outcome);
        Assert.Empty(none.Calls);
    }

    [Fact]
    public void The_path_lookup_finds_a_file_in_the_first_matching_directory()
    {
        // The lookup splits PATH on ':' (Linux only code); on Windows a drive letter would be cut in two
        Assert.SkipWhen(OperatingSystem.IsWindows(), "PATH syntax of Linux");

        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Combine("a"));
        Directory.CreateDirectory(temp.Combine("b"));
        File.WriteAllText(temp.Combine("b", "steam"), "");
        var path = temp.Combine("a") + ":" + temp.Combine("b");

        Assert.Equal(temp.Combine("b", "steam"), LinuxSteamLauncher.FindOnPath("steam", Env(("PATH", path))));
        Assert.Null(LinuxSteamLauncher.FindOnPath("missing", Env(("PATH", path))));
    }

    // ---- self-update ---------------------------------------------------------------------------------------------------------

    private static (LinuxSelfUpdater Updater, BridgeClient Client, string Binary, TempFolder Temp) Updater(
        byte[] served, string currentVersion = "1.0.5", string? updatedTo = null, bool enabled = true, string binaryName = "platinvault-bridge")
    {
        var temp = new TempFolder();
        Directory.CreateDirectory(temp.Path);
        var binary = temp.Combine(binaryName);
        File.WriteAllText(binary, "old binary");
        var settings = BridgeSettings.TryResolve("http://server:5233", "token-for-the-tests-1234567890", _ => null, null, null, out var s, out _) ? s! : throw new InvalidOperationException();
        var client = new BridgeClient(settings, currentVersion, "linux", new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(served) }));
        return (new LinuxSelfUpdater(client, currentVersion, enabled, updatedTo, binary), client, binary, temp);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    [Fact]
    public async Task A_newer_verified_binary_replaces_the_file_and_asks_for_a_restart()
    {
        var served = "new binary"u8.ToArray();
        var (updater, client, binary, temp) = Updater(served);
        using var _ = temp;
        using var __ = client;

        var restart = await updater.TryUpdateAsync("1.0.6", Hash(served), CancellationToken.None);

        Assert.True(restart);
        Assert.Equal(new Version(1, 0, 6), updater.InstalledVersion);
        Assert.Equal(served, File.ReadAllBytes(binary));
        Assert.False(File.Exists(binary + ".new"));
        if (!OperatingSystem.IsWindows()) Assert.True(File.GetUnixFileMode(binary).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public async Task A_wrong_checksum_discards_the_download_and_is_not_retried_at_once()
    {
        var (updater, client, binary, temp) = Updater("tampered"u8.ToArray());
        using var _ = temp;
        using var __ = client;

        Assert.False(await updater.TryUpdateAsync("1.0.6", Hash("something else"u8.ToArray()), CancellationToken.None));
        Assert.Equal("old binary", File.ReadAllText(binary));
        Assert.False(File.Exists(binary + ".new"));
        Assert.False(await updater.TryUpdateAsync("1.0.6", Hash("something else"u8.ToArray()), CancellationToken.None)); // blocked, no second download
    }

    [Theory]
    [InlineData("1.0.5")] // same
    [InlineData("1.0.4")] // older
    [InlineData("not-a-version")]
    [InlineData("")]
    public async Task Nothing_happens_without_a_newer_valid_version(string offered)
    {
        var (updater, client, binary, temp) = Updater("new"u8.ToArray());
        using var _ = temp;
        using var __ = client;

        Assert.False(await updater.TryUpdateAsync(offered, Hash("new"u8.ToArray()), CancellationToken.None));
        Assert.Equal("old binary", File.ReadAllText(binary));
    }

    [Fact]
    public async Task No_checksum_means_no_update_and_a_development_host_is_never_replaced()
    {
        var (updater, client, binary, temp) = Updater("new"u8.ToArray());
        using var _ = temp;
        using var __ = client;
        Assert.False(await updater.TryUpdateAsync("1.0.6", null, CancellationToken.None));
        Assert.Equal("old binary", File.ReadAllText(binary));

        var (dev, devClient, devBinary, devTemp) = Updater("new"u8.ToArray(), binaryName: "dotnet");
        using var ___ = devTemp;
        using var ____ = devClient;
        Assert.False(await dev.TryUpdateAsync("1.0.6", Hash("new"u8.ToArray()), CancellationToken.None));
        Assert.Equal("old binary", File.ReadAllText(devBinary));
    }

    [Fact]
    public async Task A_disabled_updater_and_a_server_that_offers_less_than_what_was_just_installed_do_not_update()
    {
        var served = "new"u8.ToArray();
        var (off, offClient, _, offTemp) = Updater(served, enabled: false);
        using var _ = offTemp;
        using var __ = offClient;
        Assert.False(await off.TryUpdateAsync("1.0.6", Hash(served), CancellationToken.None));

        // Started by an update to 1.0.6 but still reports 1.0.5: the server serves a mislabelled file - stop instead of looping
        var (loop, loopClient, _, loopTemp) = Updater(served, updatedTo: "1.0.6");
        using var ___ = loopTemp;
        using var ____ = loopClient;
        Assert.False(await loop.TryUpdateAsync("1.0.6", Hash(served), CancellationToken.None));
    }

    [Fact]
    public void Leftovers_of_an_interrupted_download_are_cleaned()
    {
        var (updater, client, binary, temp) = Updater([]);
        using var _ = temp;
        using var __ = client;
        File.WriteAllText(binary + ".new", "half");

        Assert.True(updater.CleanupLeftovers());
        Assert.False(File.Exists(binary + ".new"));
    }
}
