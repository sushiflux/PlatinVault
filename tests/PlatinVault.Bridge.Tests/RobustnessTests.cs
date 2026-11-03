using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;

namespace PlatinVault.Bridge.Tests;

/// <summary>Restart safety, single instance, token handling and the install script (the script tests run on Linux only).</summary>
[Collection(BridgeGlobals.Name)]
public sealed class RobustnessTests
{
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static BridgeSettings Settings() =>
        BridgeSettings.TryResolve("http://server:5233", "token-for-the-tests-1234567890", _ => null, null, null, out var s, out _) ? s! : throw new InvalidOperationException();

    // ---- update marker: the loop guard survives a restart by systemd (which passes no argument) ------------------------------

    [Fact]
    public async Task A_restarted_bridge_that_is_still_old_does_not_download_the_same_file_again_and_again()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Path);
        var binary = temp.Combine("platinvault-bridge");
        var marker = temp.Combine("state", "updated-to.txt");
        File.WriteAllText(binary, "old binary");

        // The server announces 1.0.6 but serves a binary that still reports 1.0.5: after the update the new process is just as old
        var served = "mislabelled binary"u8.ToArray();
        var handler = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(served) });
        using var client = new BridgeClient(Settings(), "1.0.5", "linux", handler);

        var first = new LinuxSelfUpdater(client, "1.0.5", true, UpdateMarker.Read(marker, "1.0.5"), binary, marker);
        Assert.True(await first.TryUpdateAsync("1.0.6", Hash(served), CancellationToken.None));
        Assert.Equal("1.0.6", File.ReadAllText(marker));
        Assert.Single(handler.Requests);

        // "Restart": a new updater, version 1.0.5 again, the marker is read from the file instead of an argument
        var second = new LinuxSelfUpdater(client, "1.0.5", true, UpdateMarker.Read(marker, "1.0.5"), binary, marker);
        Assert.False(await second.TryUpdateAsync("1.0.6", Hash(served), CancellationToken.None));
        Assert.Single(handler.Requests); // no second download

        // A really newer version is still taken
        var third = new LinuxSelfUpdater(client, "1.0.5", true, UpdateMarker.Read(marker, "1.0.5"), binary, marker);
        Assert.True(await third.TryUpdateAsync("1.0.7", Hash(served), CancellationToken.None));
    }

    [Fact]
    public void The_marker_is_deleted_once_the_running_binary_has_caught_up_and_a_broken_one_is_ignored()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Path);
        var marker = temp.Combine("updated-to.txt");

        File.WriteAllText(marker, "1.0.6");
        Assert.Equal("1.0.6", UpdateMarker.Read(marker, "1.0.5"));
        Assert.True(File.Exists(marker));
        Assert.Null(UpdateMarker.Read(marker, "1.0.6"));
        Assert.False(File.Exists(marker));

        File.WriteAllText(marker, "garbage");
        Assert.Null(UpdateMarker.Read(marker, "1.0.5"));
        Assert.Null(UpdateMarker.Read(temp.Combine("missing.txt"), "1.0.5"));
        Assert.Null(UpdateMarker.Read(null, "1.0.5"));
    }

    // ---- single instance ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Only_one_bridge_holds_the_lock_and_it_is_free_again_after_it_ends()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("state", "bridge.lock");

        using (var first = InstanceLock.TryAcquire(path))
        {
            Assert.NotNull(first);
            Assert.Null(InstanceLock.TryAcquire(path));
        }

        using var again = InstanceLock.TryAcquire(path);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task A_second_run_refuses_to_start_while_the_first_one_is_running()
    {
        using var temp = new TempFolder();
        var env = LinuxEnvironment.Create(temp.Combine("root"), _ => null);
        using var held = InstanceLock.TryAcquire(env.LockPath);
        Assert.NotNull(held);

        BridgeLang.Reset();
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        int code;
        try
        {
            code = await LinuxApp.RunAsync(["run", "--test-root", temp.Combine("root"), "--language", "en", "--server", "http://s:1", "--token", "token-for-the-tests-1234567890"],
                _ => null, CancellationToken.None);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Equal(LinuxApp.ExitFailed, code);
        Assert.Contains("already running", writer.ToString());
    }

    // ---- token handling ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Setup_reads_the_token_from_standard_input_and_never_prints_it()
    {
        using var temp = new TempFolder();
        var originalIn = Console.In;
        var originalOut = Console.Out;
        var output = new StringWriter();
        Console.SetIn(new StringReader("token-for-the-tests-1234567890\n"));
        Console.SetOut(output);
        int code;
        try
        {
            BridgeLang.Reset();
            code = await LinuxApp.RunAsync(["setup", "--server", "http://s:1", "--token-stdin", "--test-root", temp.Combine("root")], _ => null, CancellationToken.None);
        }
        finally
        {
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
        }

        Assert.Equal(0, code);
        var config = BridgeConfigFile.Load(temp.Combine("root", "config", "platinvault-bridge", "bridge.json"), out _)!;
        Assert.Equal("token-for-the-tests-1234567890", config.Token);
        Assert.DoesNotContain("token-for-the-tests", output.ToString());
    }

    [Fact]
    public async Task Setup_with_an_empty_standard_input_is_refused()
    {
        using var temp = new TempFolder();
        var originalIn = Console.In;
        Console.SetIn(new StringReader(""));
        int code;
        try
        {
            BridgeLang.Reset();
            code = await LinuxApp.RunAsync(["setup", "--server", "http://s:1", "--token-stdin", "--test-root", temp.Combine("root")], _ => null, CancellationToken.None);
        }
        finally
        {
            Console.SetIn(originalIn);
        }

        Assert.Equal(LinuxApp.ExitUsage, code);
        Assert.False(File.Exists(temp.Combine("root", "config", "platinvault-bridge", "bridge.json")));
    }

    [Fact]
    public void A_stale_temp_file_with_wide_permissions_does_not_make_the_new_config_readable_by_others()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes");
            return;
        }

        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Path);
        var path = temp.Combine("bridge.json");
        File.WriteAllText(path + ".tmp", "stale");
        File.SetUnixFileMode(path + ".tmp", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        BridgeConfigFile.Write(path, new StoredBridgeConfig { Server = "http://s:1", Token = "secret-token-12345" });

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void On_linux_two_config_paths_that_differ_only_in_case_are_different_files()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("case-sensitive file system");
            return;
        }

        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Path);
        var target = temp.Combine("Config.json");
        Directory.CreateDirectory(target); // not writable as a file: forces the fallback
        var user = temp.Combine("config.json");
        BridgeConfigFile.Write(user, new StoredBridgeConfig { Server = "http://s:1", Token = "secret-token-12345" });

        BridgeConfigFile.RememberLanguage(target, user, "en");

        // Different path (case matters on Linux): the language went to the per-user file
        Assert.Equal("en", BridgeConfigFile.Load(user, out _)!.Language);
    }

    // ---- install.sh (needs a Linux shell) ----------------------------------------------------------------------------------------

    private static string RunScript(string script, string workDirectory, Dictionary<string, string?> environment, out int exitCode)
    {
        var info = new ProcessStartInfo("sh") { WorkingDirectory = workDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add(script);
        info.ArgumentList.Add("--no-start");
        foreach (var (key, value) in environment)
        {
            if (value is null) info.Environment.Remove(key);
            else info.Environment[key] = value;
        }
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        exitCode = process.ExitCode;
        return output;
    }

    [Fact]
    public void Install_writes_a_unit_that_survives_odd_paths_keeps_steam_alive_and_removes_the_token_copy()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Skip("needs sh and a normal user (install.sh refuses to run as root)");
            return;
        }

        using var temp = new TempFolder();
        var package = temp.Combine("PlatinVault-Bridge");
        Directory.CreateDirectory(package);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "install.sh"), Path.Combine(package, "install.sh"));
        // A stand-in for the binary: `setup --from <file>` copies the file to the config folder the way the real one saves it
        File.WriteAllText(Path.Combine(package, "platinvault-bridge"),
            "#!/bin/sh\nif [ \"$1\" = setup ] && [ \"$2\" = --from ]; then mkdir -p \"$XDG_CONFIG_HOME/platinvault-bridge\" && cp \"$3\" \"$XDG_CONFIG_HOME/platinvault-bridge/bridge.json\"; fi\n");
        File.WriteAllText(Path.Combine(package, "bridge.json"), "{\"server\":\"http://s:1\",\"token\":\"secret\"}");

        var home = temp.Combine("home dir");
        var config = temp.Combine("cfg 50%\"x$y");
        var state = temp.Combine("state");
        Directory.CreateDirectory(home);
        var output = RunScript("install.sh", package, new()
        {
            ["HOME"] = home, ["XDG_CONFIG_HOME"] = config, ["XDG_STATE_HOME"] = state, ["XDG_DATA_HOME"] = null,
        }, out var exit);

        Assert.True(exit == 0, output);

        var unit = File.ReadAllText(Path.Combine(config, "systemd", "user", "platinvault-bridge.service"));
        Assert.Contains("KillMode=process", unit);
        Assert.Contains("RestartPreventExitStatus=78", unit);
        // % doubled, $ doubled, quote escaped; the config folder reaches the service so it reads what the setup wrote
        Assert.Contains("%%", unit);
        Assert.Contains("$$", unit);
        Assert.Contains("\\\"", unit);
        var environmentLines = unit.Split('\n').Where(l => l.StartsWith("Environment=")).ToList();
        Assert.Contains(environmentLines, l => l.StartsWith("Environment=\"XDG_CONFIG_HOME=") && l.Contains("cfg 50%%"));
        Assert.Contains(environmentLines, l => l.StartsWith("Environment=\"XDG_STATE_HOME="));
        Assert.DoesNotContain(environmentLines, l => l.Contains("XDG_DATA_HOME"));
        Assert.Contains("ExecStart=\"", unit);
        Assert.EndsWith("\" run", unit.Split('\n').First(l => l.StartsWith("ExecStart=")).TrimEnd());

        // The token lives in the config folder only
        Assert.True(File.Exists(Path.Combine(config, "platinvault-bridge", "bridge.json")));
        Assert.False(File.Exists(Path.Combine(package, "bridge.json")));
        Assert.Contains("Deleted the copy of bridge.json", output);
    }

    [Fact]
    public void Install_keeps_the_token_copy_when_the_setup_failed()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            Assert.Skip("needs sh and a normal user (install.sh refuses to run as root)");
            return;
        }

        using var temp = new TempFolder();
        var package = temp.Combine("PlatinVault-Bridge");
        Directory.CreateDirectory(package);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "install.sh"), Path.Combine(package, "install.sh"));
        File.WriteAllText(Path.Combine(package, "platinvault-bridge"), "#!/bin/sh\nexit 2\n");
        File.WriteAllText(Path.Combine(package, "bridge.json"), "{}");
        Directory.CreateDirectory(temp.Combine("home"));

        RunScript("install.sh", package, new() { ["HOME"] = temp.Combine("home"), ["XDG_CONFIG_HOME"] = null, ["XDG_STATE_HOME"] = null, ["XDG_DATA_HOME"] = null }, out var exit);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(package, "bridge.json")));
    }
}
