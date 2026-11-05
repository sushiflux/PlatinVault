using System.Text.Json;
using PlatinVault.Desktop;
using PlatinVault.Desktop.Database;
using PlatinVault.Desktop.Server;
using PlatinVault.Desktop.Updates;
using PlatinVault.Versioning;

namespace PlatinVault.Windows.Tests;

public sealed class UpdateCheckTests
{
    [Theory]
    [InlineData("v2026.11.9", 2026, 11, 9, false, false, null)]
    [InlineData("v2026.11.12.13.557-beta", 2026, 11, 12, true, false, null)]
    [InlineData("v2026.11.12.13.557-nightly", 2026, 11, 12, false, true, null)]
    [InlineData("v2026.11.9.31.2840-rc6", 2026, 11, 9, false, false, 6)]
    [InlineData("v2026.10.6-rc3", 2026, 10, 6, false, false, 3)]
    public void Parses_versions(string text, int year, int month, int commit, bool beta, bool nightly, int? rc)
    {
        var v = ReleaseVersion.Parse(text);
        Assert.NotNull(v);
        Assert.Equal((year, month, commit, beta, nightly, rc), (v.Value.Year, v.Value.Month, v.Value.Commit, v.Value.IsBeta, v.Value.IsNightly, v.Value.Rc));
        Assert.Equal(beta || nightly || rc is not null, v.Value.IsPreRelease);
    }

    [Theory]
    [InlineData("v2026.11.12 (13.557 β)")]
    [InlineData("v2026.11.12 (13.557 α)")]
    [InlineData("v2026.11.12.13.557-betas")]
    [InlineData("v2026.11")]
    [InlineData("")]
    public void Rejects_what_is_not_a_tag(string text) => Assert.Null(ReleaseVersion.Parse(text));

    [Theory]
    [InlineData("v2026.11.9", "v2026.11.10", true)]
    [InlineData("v2026.11.9-rc2", "v2026.11.9", true)]
    [InlineData("v2026.11.9-rc2", "v2026.11.9.3.100-rc3", true)]
    [InlineData("v2026.11.9.3.100-rc3", "v2026.11.9.4.200-beta", true)]
    [InlineData("v2026.11.12.13.557-nightly", "v2026.11.12.13.557-beta", true)]
    [InlineData("v2026.11.12.13.557-beta", "v2026.11.12", true)]
    [InlineData("v2026.11.12.13.557-beta", "v2026.11.13.1.5-nightly", true)]
    [InlineData("v2026.11.12.13.557-beta", "v2026.11.13.1.5-beta", true)]
    [InlineData("v2026.12.150", "v2027.1.1", true)]
    [InlineData("v2026.11.9", "v2026.11.9", false)]
    [InlineData("v2026.11.10", "v2026.11.9", false)]
    [InlineData("v2026.11.12", "v2026.11.12.13.557-beta", false)]
    public void Orders_versions(string older, string newer, bool expectNewer)
    {
        Assert.Equal(expectNewer, ReleaseVersion.Parse(newer)!.Value.CompareTo(ReleaseVersion.Parse(older)!.Value) > 0);
    }

    [Theory]
    [InlineData("v2026.11.12.13.557-beta", "v2026.11.12 (13.557 β)")]
    [InlineData("v2026.11.12.13.557-nightly", "v2026.11.12 (13.557 α)")]
    [InlineData("v2026.11.12", "v2026.11.12")]
    [InlineData("v2026.11.9.31.2840-rc6", "v2026.11.9.31.2840-rc6")]
    public void Shows_a_tag_in_the_readable_form(string tag, string expected) => Assert.Equal(expected, ReleaseVersion.Parse(tag)!.Value.Display);

    private static JsonElement Releases(params object[] releases) => JsonSerializer.SerializeToElement(releases);

    private static object Release(string tag, bool draft = false, bool checksum = true, string host = "github.com") => new
    {
        tag_name = tag,
        draft,
        assets = checksum
            ? new object[]
            {
                new { name = $"PlatinVault-Setup-{tag}.exe", browser_download_url = $"https://{host}/sushiflux/PlatinVault/releases/download/{tag}/PlatinVault-Setup-{tag}.exe" },
                new { name = $"PlatinVault-Setup-{tag}.exe.sha256", browser_download_url = $"https://{host}/sushiflux/PlatinVault/releases/download/{tag}/PlatinVault-Setup-{tag}.exe.sha256" },
            }
            : [new { name = $"PlatinVault-Setup-{tag}.exe", browser_download_url = $"https://{host}/x/{tag}.exe" }],
    };

    // The switch "Beta-Versionen erhalten" decides (user decision 2026-11-02), not the channel of the installed build
    [Fact]
    public void Switch_off_only_takes_releases()
    {
        var json = Releases(Release("v2026.11.12.1.10-beta"), Release("v2026.11.12.1.9-rc1"), Release("v2026.11.11"), Release("v2026.11.9"));
        var offer = UpdateChecker.Pick(json, ReleaseVersion.Parse("v2026.11.9")!.Value, includeBetas: false);
        Assert.Equal("v2026.11.11", offer?.Version.Text);
    }

    [Fact]
    public void Switch_on_takes_the_newest_of_betas_and_releases()
    {
        var json = Releases(Release("v2026.11.12.1.10-beta"), Release("v2026.11.11"));
        Assert.Equal("v2026.11.12.1.10-beta", UpdateChecker.Pick(json, ReleaseVersion.Parse("v2026.11.9")!.Value, includeBetas: true)?.Version.Text);
        Assert.Equal("v2026.11.12.1.10-beta", UpdateChecker.Pick(json, ReleaseVersion.Parse("v2026.11.9.3.20-beta")!.Value, includeBetas: true)?.Version.Text);
    }

    [Fact]
    public void A_running_beta_takes_a_newer_release_when_no_newer_beta_exists()
    {
        var json = Releases(Release("v2026.11.12"), Release("v2026.11.10.4.4-beta"));
        var offer = UpdateChecker.Pick(json, ReleaseVersion.Parse("v2026.11.10.4.4-beta")!.Value, includeBetas: true);
        Assert.Equal("v2026.11.12", offer?.Version.Text);
    }

    [Fact]
    public void Switching_off_on_a_beta_never_downgrades_and_waits_for_the_next_release()
    {
        // Running a beta that is newer than every release: nothing is offered (no downgrade to v2026.11.11)
        var json = Releases(Release("v2026.11.12.13.557-beta"), Release("v2026.11.11"));
        Assert.Null(UpdateChecker.Pick(json, ReleaseVersion.Parse("v2026.11.12.13.557-beta")!.Value, includeBetas: false));
        // The beta became a release (same commit, later): offered automatically
        json = Releases(Release("v2026.11.12"), Release("v2026.11.12.13.557-beta"), Release("v2026.11.11"));
        Assert.Equal("v2026.11.12", UpdateChecker.Pick(json, ReleaseVersion.Parse("v2026.11.12.13.557-beta")!.Value, includeBetas: false)?.Version.Text);
    }

    [Fact]
    public void An_old_candidate_install_is_offered_betas_only_with_the_switch()
    {
        var json = Releases(Release("v2026.11.12.13.557-beta"), Release("v2026.11.9.31.2840-rc6"));
        Assert.Equal("v2026.11.12.13.557-beta", UpdateChecker.Pick(json, ReleaseVersion.Parse("v2026.11.9.31.2840-rc6")!.Value, includeBetas: true)?.Version.Text);
        Assert.Null(UpdateChecker.Pick(json, ReleaseVersion.Parse("v2026.11.9.31.2840-rc6")!.Value, includeBetas: false));
    }

    [Fact]
    public void Old_candidate_tags_stay_pre_releases_for_release_only_installs()
    {
        var json = Releases(Release("v2026.11.9.31.2840-rc6"));
        Assert.Null(UpdateChecker.Pick(json, ReleaseVersion.Parse("v2026.11.8")!.Value, includeBetas: false));
    }

    [Fact]
    public void Dev_build_tags_are_never_offered_to_any_installation()
    {
        var json = Releases(Release("v2026.11.20.1.2-dev"), Release("v2026.11.12.13.557-beta"));
        Assert.Null(ReleaseVersion.Parse("v2026.11.20.1.2-dev"));
        // Only the beta is a candidate; the (hypothetical) dev release is skipped even with the beta switch on
        Assert.Equal("v2026.11.12.13.557-beta", UpdateChecker.Pick(json, ReleaseVersion.Parse("v2026.11.9")!.Value, includeBetas: true)?.Version.Text);
        Assert.Null(UpdateChecker.Pick(Releases(Release("v2026.11.20.1.2-dev")), ReleaseVersion.Parse("v2026.11.9")!.Value, includeBetas: true));
    }

    [Fact]
    public void Drafts_releases_without_checksum_and_foreign_hosts_are_ignored()
    {
        var json = Releases(Release("v2026.11.20", draft: true), Release("v2026.11.19", checksum: false), Release("v2026.11.18", host: "evil.example"));
        Assert.Null(UpdateChecker.Pick(json, ReleaseVersion.Parse("v2026.11.9")!.Value, includeBetas: true));
    }

    [Fact]
    public async Task Test_host_without_install_flow_installs_nothing()
    {
        var host = new LocalDesktopHost(recordOnly: true, null);
        Assert.False(host.CanInstallUpdates);
        var release = ReleaseFeed.Parse(Releases(Release("v2026.11.20"))).Single();
        Assert.Equal(Companion.Api.Desktop.DesktopUpdateOutcome.NotSupported, (await host.InstallUpdateAsync(release, CancellationToken.None)).Outcome);
    }
}

public sealed class DesktopHostTests
{
    [Fact]
    public void Test_launches_are_only_recorded()
    {
        using var temp = new TempFolder();
        var record = temp.Sub("launches.txt");
        var host = new LocalDesktopHost(recordOnly: true, record);
        Assert.True(host.LaunchGame(1091500).Started);
        Assert.False(host.LaunchGame(0).Started);
        Assert.Equal("steam://run/1091500", File.ReadAllText(record).Trim());
    }

    [Fact]
    public void Server_binding_stays_on_loopback_in_this_version()
    {
        Assert.Equal(System.Net.IPAddress.Loopback, ServerBinding.From(new DesktopSettings()).Address);
        Assert.Equal(System.Net.IPAddress.Loopback, ServerBinding.From(new DesktopSettings { ServerBindAddress = "0.0.0.0" }).Address);
        Assert.Equal("127.0.0.1;localhost", ServerBinding.From(new DesktopSettings { ServerBindAddress = "192.168.1.20" }).AllowedHosts);
    }

    [Fact]
    public void Options_need_a_test_root_for_test_modes()
    {
        Assert.Throws<ArgumentException>(() => DesktopOptions.Parse(["--smoke", "x"]));
        var o = DesktopOptions.Parse(["--test-root", "t", "--smoke", "x", "--background"]);
        Assert.True(o.Background);
        Assert.Equal("x", o.Smoke);
    }
}

/// <summary>"Trophäen-Popup anzeigen" in the Windows program: who shows what, the tray switch, the migration of the former switch.</summary>
public sealed class OverlayRoutingTests
{
    [Theory]
    [InlineData("overlay", false, true)]
    [InlineData("overlay", true, true)]   // "only in the game" also over the program's own window
    [InlineData("both", false, true)]
    [InlineData("both", true, false)]     // the in-app popup is already on screen - never twice
    [InlineData("app", false, false)]
    [InlineData("app", true, false)]
    [InlineData(null, false, true)]       // nothing stored = "both"
    [InlineData("sideways", true, false)] // unknown = "both"
    public void The_overlay_shows_by_the_app_setting(string? target, bool mainWindowForeground, bool shows)
    {
        Assert.Equal(shows, PlatinVault.Desktop.Ui.OverlayRouting.ShowsOverlay(target, mainWindowForeground));
    }

    [Theory]
    [InlineData("both", true, "app")]
    [InlineData("overlay", true, "app")]
    [InlineData("app", false, "both")]
    [InlineData(null, true, "app")]
    public void The_tray_switch_is_the_same_setting(string? target, bool checkedNow, string afterClick)
    {
        Assert.Equal(checkedNow, PlatinVault.Desktop.Ui.OverlayRouting.TrayChecked(target));
        Assert.Equal(afterClick, PlatinVault.Desktop.Ui.OverlayRouting.TrayToggled(target));
    }

    [Theory]
    [InlineData(false, false, "app")]   // switched off before, nothing chosen in the app yet: kept
    [InlineData(false, true, null)]     // the app setting exists: it wins
    [InlineData(true, false, null)]     // switched on = the default anyway
    [InlineData(null, false, null)]     // nothing to migrate
    public void The_former_switch_is_moved_over_once(bool? legacy, bool targetStored, string? expected)
    {
        Assert.Equal(expected, PlatinVault.Desktop.Ui.OverlayRouting.MigrateLegacySwitch(legacy, targetStored));
    }

    [Fact]
    public void A_desktop_json_with_the_former_switch_still_loads()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pv-desktop-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "desktop.json");
            File.WriteAllText(path, """{ "OverlayEnabled": false, "NotificationsEnabled": false }""");
            var settings = PlatinVault.Desktop.DesktopSettings.Load(path);
            Assert.False(settings.LegacyOverlayEnabled);
            Assert.False(settings.NotificationsEnabled);

            settings.LegacyOverlayEnabled = null;
            settings.Save(path);
            Assert.DoesNotContain("OverlayEnabled", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

/// <summary>The JSON between the program and its pages: the same shape the app gets over SignalR (camelCase), and the page's answers.</summary>
public sealed class OverlayMessagesTests
{
    private static Companion.Api.Endpoints.LiveAchievementItemDto Sample() => new(
        "ACH_1", "First Blood", "Win a fight.", null, false, "https://cdn.example/a.jpg", "", true, null, 2.4, "Gold", "Extrem selten", false, "")
    {
        HasGlobalPercent = true,
        RarityBand = "ultraRare",
    };

    [Fact]
    public void An_unlock_is_sent_with_the_app_s_field_names_position_duration_and_language()
    {
        using var json = System.Text.Json.JsonDocument.Parse(PlatinVault.Desktop.Ui.OverlayMessages.Achievement(Sample(), "Elden Ring", "fullscreen", 8, "en"));
        var payload = json.RootElement.GetProperty("pvOverlay");
        Assert.Equal("achievement", payload.GetProperty("kind").GetString());
        Assert.Equal("Elden Ring", payload.GetProperty("gameName").GetString());
        Assert.Equal("fullscreen", payload.GetProperty("position").GetString());
        Assert.Equal(8, payload.GetProperty("seconds").GetInt32());
        Assert.Equal("en", payload.GetProperty("language").GetString());
        var achievement = payload.GetProperty("achievement");
        Assert.Equal("ACH_1", achievement.GetProperty("apiName").GetString());
        Assert.Equal("Gold", achievement.GetProperty("rarityTier").GetString());
        Assert.True(achievement.GetProperty("hasGlobalPercent").GetBoolean());
        Assert.Equal("ultraRare", achievement.GetProperty("rarityBand").GetString());
    }

    [Fact]
    public void A_platinum_moment_carries_game_and_total()
    {
        using var json = System.Text.Json.JsonDocument.Parse(PlatinVault.Desktop.Ui.OverlayMessages.Platinum("Elden Ring", 42, "de"));
        var payload = json.RootElement.GetProperty("pvOverlay");
        Assert.Equal("platinum", payload.GetProperty("kind").GetString());
        Assert.Equal(42, payload.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("""{"pvOverlayState":"ready","received":0}""", "ready", 0L)]
    [InlineData("""{"pvOverlayState":"idle","received":3}""", "idle", 3L)]
    [InlineData("""{"pvOverlayState":"busy"}""", "busy", 0L)]
    public void The_page_s_state_is_read(string json, string state, long received)
    {
        Assert.Equal((state, received), PlatinVault.Desktop.Ui.OverlayMessages.ReadOverlayState(json));
    }

    [Theory]
    [InlineData("""{"pvOverlayState":"gone"}""")]
    [InlineData("""{"pvTheme":"dark"}""")]
    [InlineData("\"idle\"")]
    [InlineData("not json")]
    public void Anything_else_is_no_state(string json)
    {
        Assert.Null(PlatinVault.Desktop.Ui.OverlayMessages.ReadOverlayState(json));
    }

    [Fact]
    public void An_idle_sent_before_the_newest_event_arrived_does_not_hide_the_window()
    {
        Assert.True(PlatinVault.Desktop.Ui.OverlayMessages.IdleCoversAll(received: 2, presented: 2));
        Assert.False(PlatinVault.Desktop.Ui.OverlayMessages.IdleCoversAll(received: 1, presented: 2));
    }

    [Fact]
    public void The_main_window_s_page_asks_and_gets_the_overlay_state()
    {
        Assert.True(PlatinVault.Desktop.Ui.OverlayMessages.IsHostHello("""{"pvHostHello":true}"""));
        Assert.False(PlatinVault.Desktop.Ui.OverlayMessages.IsHostHello("""{"pvHostHello":"yes"}"""));
        Assert.False(PlatinVault.Desktop.Ui.OverlayMessages.IsHostHello("""{"pvTheme":"dark"}"""));
        Assert.Equal("""{"pvHost":{"overlay":true}}""", PlatinVault.Desktop.Ui.OverlayMessages.HostState(true));
        Assert.Equal("""{"pvSettingsChanged":true}""", PlatinVault.Desktop.Ui.OverlayMessages.SettingsChanged);
    }
}
