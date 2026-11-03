using System.IO.Compression;
using System.IO.Pipes;
using PlatinVault.Desktop.Database;
using PlatinVault.Desktop.Ui;
using PlatinVault.Setup;
using PlatinVault.Windows.Install;
using PlatinVault.Windows.Localization;

namespace PlatinVault.Windows.Tests;

/// <summary>Regression tests for the findings of the code review before the first Windows RC.</summary>
public sealed class SteamLoginFlowTests
{
    private static readonly Uri App = new("http://127.0.0.1:42917/");

    [Fact]
    public void Steam_sign_in_stays_in_the_window_only_while_it_runs()
    {
        var flow = new SteamLoginFlow();
        var now = new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc);
        var signIn = new Uri("https://steamcommunity.com/openid/login?openid.mode=checkid_setup");

        // Before the start: Steam goes to the default browser like every other site
        Assert.Equal(NavigationDecision.External, flow.Decide(signIn, own: false, now));

        Assert.Equal(NavigationDecision.Allow, flow.Decide(new Uri(App, "/api/steam/openid/start?origin=x"), own: true, now));
        Assert.Equal(NavigationDecision.Allow, flow.Decide(signIn, own: false, now.AddSeconds(1)));
        Assert.Equal(NavigationDecision.Allow, flow.Decide(new Uri("https://login.steampowered.com/jwt/finalizelogin"), own: false, now.AddSeconds(2)));
        // Other sites and other Steam pages still leave the window, even during the sign-in
        Assert.Equal(NavigationDecision.External, flow.Decide(new Uri("https://store.steampowered.com/"), own: false, now.AddSeconds(3)));
        Assert.Equal(NavigationDecision.External, flow.Decide(new Uri("https://steamcommunity.com/id/someone"), own: false, now.AddSeconds(3)));
        Assert.Equal(NavigationDecision.External, flow.Decide(new Uri("http://steamcommunity.com/openid/login"), own: false, now.AddSeconds(3)));
        Assert.Equal(NavigationDecision.External, flow.Decide(new Uri("https://evil.example/openid/login"), own: false, now.AddSeconds(3)));

        // Back in the app (callback): the sign-in is over
        Assert.Equal(NavigationDecision.Allow, flow.Decide(new Uri(App, "/api/steam/openid/callback?state=x"), own: true, now.AddSeconds(30)));
        Assert.Equal(NavigationDecision.External, flow.Decide(signIn, own: false, now.AddSeconds(31)));
    }

    [Fact]
    public void A_forgotten_sign_in_ends_after_fifteen_minutes()
    {
        var flow = new SteamLoginFlow();
        var now = new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc);
        flow.Decide(new Uri(App, "/api/steam/openid/start"), own: true, now);
        Assert.Equal(NavigationDecision.External, flow.Decide(new Uri("https://steamcommunity.com/openid/login"), own: false, now + SteamLoginFlow.MaxDuration));
    }
}

public sealed class LoopbackPortTests
{
    private const string Netsh = """
        Portausschlussbereiche für das Protokoll "tcp"

        Startport      Endport
        ----------    --------
                80          80
              2869        2869
             42000       42099
             50000       50059     *
             64583       64682

        * - Verwaltete Portausschlüsse.
        """;

    [Fact]
    public void Excluded_ranges_are_read_from_netsh_in_any_language()
    {
        var ranges = LoopbackPorts.ParseExcluded(Netsh);
        Assert.Equal([(80, 80), (2869, 2869), (42000, 42099), (50000, 50059), (64583, 64682)], ranges);
    }

    [Fact]
    public void Ports_avoid_the_dynamic_and_reserved_ranges_and_are_stable()
    {
        var reserved = new LoopbackPorts.Reserved(49152, 65535, LoopbackPorts.ParseExcluded(Netsh));
        var first = LoopbackPorts.Pick("S-1-5-21-1|server", reserved, _ => true, remembered: null);
        Assert.Equal(first, LoopbackPorts.Pick("S-1-5-21-1|server", reserved, _ => true, remembered: null));
        Assert.InRange(first, LoopbackPorts.FallbackFirst, LoopbackPorts.PreferredLast);
        Assert.False(reserved.Contains(first));
        Assert.NotEqual(first, LoopbackPorts.Pick("S-1-5-21-1|database", reserved, _ => true, remembered: null));

        // Everything in the preferred range reserved or busy: the fallback range, never 49152 and above
        var busyPreferred = LoopbackPorts.Pick("seed", reserved, p => p < LoopbackPorts.PreferredFirst, remembered: null);
        Assert.InRange(busyPreferred, LoopbackPorts.FallbackFirst, LoopbackPorts.FallbackLast);
    }

    [Fact]
    public void A_remembered_port_is_kept_unless_it_became_reserved()
    {
        var reserved = new LoopbackPorts.Reserved(49152, 65535, [(43000, 43010)]);
        Assert.Equal(44444, LoopbackPorts.Pick("seed", reserved, _ => true, remembered: 44444));
        Assert.NotEqual(43005, LoopbackPorts.Pick("seed", reserved, _ => true, remembered: 43005));
        // An old port from the dynamic range (earlier builds) is replaced by a stable one
        Assert.True(LoopbackPorts.Pick("seed", reserved, _ => true, remembered: 58796) < 49152);
    }

    [Fact]
    public void A_fixed_development_port_wins()
    {
        var port = LoopbackPorts.FreeLoopbackPort();
        Assert.Equal(port, LoopbackPorts.ChoosePort(port, remembered: 44444, busy: p => new DatabaseException(new Msg("busy", "busy"))));
    }
}

public sealed class CommandPipeTests
{
    [Fact]
    public async Task Pipe_name_is_per_user_and_session_and_commands_arrive()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        Assert.Matches(@"^PlatinVault\.Desktop\.[0-9a-f]{12}\.s\d+-test-[0-9a-f]{8}$", layout.PipeName);

        using var instance = AppInstance.TryAcquire(layout)!;
        var received = new TaskCompletionSource<string>();
        instance.Listen(layout, command => received.TrySetResult(command));
        Assert.True(AppInstance.Send(layout, AppInstance.Show, TimeSpan.FromSeconds(5)));
        Assert.Equal(AppInstance.Show, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void A_name_held_by_someone_else_neither_crashes_nor_spins()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        // Someone already owns the only instance of this pipe name
        using var squatter = new NamedPipeServerStream(layout.PipeName, PipeDirection.InOut, 1);
        using var instance = AppInstance.TryAcquire(layout)!;
        instance.Listen(layout, _ => { });
        Thread.Sleep(1600);
        // Errors are caught and retried with a pause: a few attempts, not thousands
        Assert.InRange(instance.Failures, 1, 6);
    }
}

public sealed class UninstallRobustnessTests
{
    [Fact]
    public void A_locked_folder_does_not_stop_the_rest_and_everything_is_reported()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        var installDir = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(installDir);
        File.WriteAllText(Path.Combine(installDir, InstallLayout.ExeName), "x");
        Registration.WriteEntries(layout, installDir, "v1", "de");
        foreach (var folder in layout.Data.Folders) Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(layout.Data.Database, "PG_VERSION"), "18");

        // WebView2 still holds a file of its profile
        using (new FileStream(Path.Combine(layout.Data.WebView, "Cookies"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var uninstaller = new Uninstaller(layout, installDir) { QuitTimeout = TimeSpan.FromSeconds(1), LockedRetry = TimeSpan.FromSeconds(1) };
            var ex = Assert.Throws<UninstallIncompleteException>(() => uninstaller.Run(keepData: false));
            Assert.Single(ex.Problems);
            Assert.Contains("webview2", ex.Problems[0].De);
            Assert.Contains("Nicht erledigt", ex.Text.De);
            Assert.Contains(InstallTexts.DoneEntries, ex.Done);
        }

        // The rest was done anyway: database, program folder, "Apps" entry
        Assert.False(Directory.Exists(layout.Data.Database));
        Assert.False(Directory.Exists(installDir));
        Assert.Null(layout.InstalledDir);
    }
}

public sealed class UpdateRollbackTests
{
    [Fact]
    public async Task A_failed_database_setup_puts_the_previous_version_back()
    {
        using var temp = new TempFolder();
        var layout = InstallLayout.Create(temp.Path);
        var target = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, InstallLayout.ExeName), "old program");
        File.WriteAllText(Path.Combine(target, "version.txt"), "v-old");
        Registration.WriteEntries(layout, target, "v-old", "de");

        // The new "program" is Windows' where.exe: it rejects --prepare with an error exit code, like a failing database setup
        var zip = temp.Sub("payload.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(Path.Combine(Environment.SystemDirectory, "where.exe"), InstallLayout.ExeName);
            using var writer = new StreamWriter(archive.CreateEntry("version.txt").Open());
            writer.Write("v-new");
        }
        using var payload = Payload.FromZip(zip);
        var engine = new InstallEngine(layout, payload, "v-new") { SkipWebViewInstall = true };

        var ex = await Assert.ThrowsAsync<InstallException>(() =>
            engine.RunAsync(new InstallRequest(target, false, false, false, "de"), new Progress<InstallProgress>(), CancellationToken.None));

        Assert.Contains("bisherige Version wurde wiederhergestellt", ex.Text.De);
        Assert.Equal("old program", File.ReadAllText(Path.Combine(target, InstallLayout.ExeName)));
        Assert.Equal("v-old", File.ReadAllText(Path.Combine(target, "version.txt")));
        Assert.Equal("v-old", layout.InstalledVersion);
        // No staging or backup folder is left behind
        Assert.Empty(Directory.GetDirectories(temp.Path, ".platinvault-*"));
    }
}
