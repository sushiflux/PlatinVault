using System.Net;
using System.Text;
using System.Text.Json;
using Companion.Api.Configuration;
using Companion.Api.Data;
using Companion.Api.Endpoints;
using Companion.Api.Localization;
using Companion.Api.Services;
using Companion.Api.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlatinVault.Versioning;

namespace Companion.Api.Tests;

/// <summary>
/// The update check of every host (switch "Beta-Versionen erhalten", user decision 2026-11-02): which version is offered, the image-tag
/// advice for containers, the cache/throttle of the GitHub calls and the specific error messages. GitHub is never called: the service
/// talks to a fake handler.
/// </summary>
public sealed class UpdateCheckTests
{
    private static JsonElement Releases(params object[] releases) => JsonSerializer.SerializeToElement(releases);

    private static object Release(string tag, bool prerelease = false, bool draft = false, bool installer = true, string? body = null) => new
    {
        tag_name = tag,
        draft,
        prerelease,
        html_url = $"https://github.com/sushiflux/PlatinVault/releases/tag/{tag}",
        published_at = "2026-11-02T10:00:00Z",
        body,
        assets = installer
            ? new object[]
            {
                new { name = $"PlatinVault-Setup-{tag}.exe", browser_download_url = $"https://github.com/sushiflux/PlatinVault/releases/download/{tag}/PlatinVault-Setup-{tag}.exe" },
                new { name = $"PlatinVault-Setup-{tag}.exe.sha256", browser_download_url = $"https://github.com/sushiflux/PlatinVault/releases/download/{tag}/PlatinVault-Setup-{tag}.exe.sha256" },
            }
            : [],
    };

    // A release with the Linux package (tar.gz + sha256) attached, no Windows installer
    private static object LinuxRelease(string tag, bool prerelease = false, bool withChecksum = true, string host = "github.com") => new
    {
        tag_name = tag,
        draft = false,
        prerelease,
        html_url = $"https://github.com/sushiflux/PlatinVault/releases/tag/{tag}",
        published_at = "2026-11-02T10:00:00Z",
        assets = withChecksum
            ? new object[]
            {
                new { name = $"PlatinVault-linux-x64-{tag}.tar.gz", browser_download_url = $"https://{host}/sushiflux/PlatinVault/releases/download/{tag}/PlatinVault-linux-x64-{tag}.tar.gz" },
                new { name = $"PlatinVault-linux-x64-{tag}.tar.gz.sha256", browser_download_url = $"https://{host}/sushiflux/PlatinVault/releases/download/{tag}/PlatinVault-linux-x64-{tag}.tar.gz.sha256" },
                new { name = "PlatinVault-Bridge-linux-x64.tar.gz", browser_download_url = $"https://{host}/sushiflux/PlatinVault/releases/download/{tag}/PlatinVault-Bridge-linux-x64.tar.gz" },
            }
            : new object[]
            {
                new { name = $"PlatinVault-linux-x64-{tag}.tar.gz", browser_download_url = $"https://{host}/sushiflux/PlatinVault/releases/download/{tag}/PlatinVault-linux-x64-{tag}.tar.gz" },
            },
    };

    private static RunningVersion Running(string tag, VersionKind kind) => new(VersionFormat.DisplayFromTag(tag), tag, kind, ReleaseVersion.Parse(tag));

    private static ReleaseListResult List(params object[] releases) =>
        new(true, ReleaseFeed.Parse(Releases(releases)), DateTimeOffset.UtcNow, null, null, false, null);

    // ---- feed and choice ------------------------------------------------------------------------------------------------

    [Fact]
    public void Parse_skips_drafts_and_foreign_tags_and_keeps_only_github_links()
    {
        var feed = ReleaseFeed.Parse(Releases(
            Release("v2026.11.12"),
            Release("v2026.11.13", draft: true),
            Release("nightly"),
            new { tag_name = "v2026.11.11", draft = false, prerelease = false, html_url = "https://evil.example/x", assets = Array.Empty<object>() }));
        Assert.Equal(new[] { "v2026.11.12", "v2026.11.11" }, feed.Select(r => r.Version.Text));
        Assert.NotNull(feed[0].NotesUrl);
        Assert.Null(feed[1].NotesUrl);
        Assert.Null(feed[1].Installer);
    }

    // A tag the regular expression accepts but the number parser cannot read used to throw: one such release took down the whole check
    [Theory]
    [InlineData("v2026.11.99999999999")]
    [InlineData("v2026.11.12-rc99999999999")]
    [InlineData("v٢٠٢٦.11.12")]
    public void A_tag_with_an_unusable_number_is_skipped_and_the_rest_of_the_list_stays(string tag)
    {
        Assert.Null(ReleaseVersion.Parse(tag));
        var feed = ReleaseFeed.Parse(Releases(Release(tag), Release("v2026.11.30")));
        Assert.Equal("v2026.11.30", feed.Single().Version.Text);
    }

    [Fact]
    public void Asset_entries_that_are_not_objects_are_skipped()
    {
        var feed = ReleaseFeed.Parse(Releases(new { tag_name = "v2026.11.30", prerelease = false, assets = new object[] { 1, "x" } }));
        Assert.Null(feed.Single().Installer);
    }

    // The asset name becomes a file name in the updates folder and part of the commands shown for the Linux package
    [Theory]
    [InlineData("PlatinVault-Setup-..\\..\\evil.exe")]
    [InlineData("PlatinVault-Setup-a/../../evil.exe")]
    [InlineData("PlatinVault-Setup-x:evil.exe")]
    public void An_installer_whose_asset_name_is_not_a_plain_file_name_is_ignored(string name)
    {
        var feed = ReleaseFeed.Parse(Releases(new
        {
            tag_name = "v2026.11.30",
            prerelease = false,
            assets = new object[]
            {
                new { name, browser_download_url = "https://github.com/sushiflux/PlatinVault/releases/download/v2026.11.30/a.exe" },
                new { name = name + ".sha256", browser_download_url = "https://github.com/sushiflux/PlatinVault/releases/download/v2026.11.30/a.sha256" },
            },
        }));
        Assert.Null(feed.Single().Installer);
    }

    [Theory]
    [InlineData("PlatinVault-linux-x64-$(touch x);id.tar.gz")]
    [InlineData("PlatinVault-linux-x64-a b.tar.gz")]
    public void A_linux_package_whose_asset_name_is_not_a_plain_file_name_is_ignored(string name)
    {
        var feed = ReleaseFeed.Parse(Releases(new
        {
            tag_name = "v2026.11.30",
            prerelease = false,
            assets = new object[]
            {
                new { name, browser_download_url = "https://github.com/sushiflux/PlatinVault/releases/download/v2026.11.30/a.tar.gz" },
                new { name = name + ".sha256", browser_download_url = "https://github.com/sushiflux/PlatinVault/releases/download/v2026.11.30/a.sha256" },
            },
        }));
        Assert.Null(feed.Single().LinuxPackage);
    }

    [Fact]
    public void A_release_tag_marked_as_pre_release_on_github_counts_as_beta()
    {
        var feed = ReleaseFeed.Parse(Releases(Release("v2026.11.12", prerelease: true)));
        Assert.True(feed.Single().IsPreRelease);
        Assert.Null(ReleaseFeed.PickUpdate(feed, ReleaseVersion.Parse("v2026.11.9")!.Value, UpdateChannel.None));
    }

    // A feed with all three kinds, newest first: nightly (.15) > beta (.14) > release (.13) > older beta (.12) > older release (.11)
    private static ReleaseListResult ThreeKindFeed() => List(
        Release("v2026.11.15.1.10-nightly", prerelease: true),
        Release("v2026.11.14.2.30-beta", prerelease: true),
        Release("v2026.11.13"),
        Release("v2026.11.12.13.557-beta", prerelease: true),
        Release("v2026.11.11"));

    [Fact]
    public void Each_channel_offers_only_what_it_receives_from_a_feed_with_all_three_kinds()
    {
        var running = Running("v2026.11.10", VersionKind.Release);

        // none: full releases only
        var none = UpdateStatusBuilder.Build(running, ThreeKindFeed(), UpdateChannel.None, desktopHost: false, desktopCanInstall: false);
        Assert.Equal(UpdateState.Available, none.State);
        Assert.Equal("v2026.11.13", none.Available!.Version.Text);

        // beta: betas and releases, never the nightly even though it is the newest of all
        var beta = UpdateStatusBuilder.Build(running, ThreeKindFeed(), UpdateChannel.Beta, desktopHost: false, desktopCanInstall: false);
        Assert.Equal("v2026.11.14.2.30-beta", beta.Available!.Version.Text);
        Assert.False(UpdateEndpoints.ToDto(beta).Available!.IsNightly);
        Assert.True(UpdateEndpoints.ToDto(beta).Available!.IsBeta);

        // nightly: nightlies and releases, never a beta
        var nightly = UpdateStatusBuilder.Build(running, ThreeKindFeed(), UpdateChannel.Nightly, desktopHost: false, desktopCanInstall: false);
        Assert.Equal("v2026.11.15.1.10-nightly", nightly.Available!.Version.Text);
        var nightlyDto = UpdateEndpoints.ToDto(nightly);
        Assert.True(nightlyDto.Available!.IsNightly);
        Assert.False(nightlyDto.Available.IsBeta);

        // the "latest" fields stay informational and do not depend on the channel
        Assert.Equal("v2026.11.13", beta.LatestRelease!.Version.Text);
        Assert.Equal("v2026.11.14.2.30-beta", beta.LatestBeta!.Version.Text);
        Assert.Equal("v2026.11.15.1.10-nightly", beta.LatestNightly!.Version.Text);
    }

    [Fact]
    public void A_nightly_user_is_not_offered_a_beta_even_when_the_beta_is_the_newest_version_there_is()
    {
        var feed = List(Release("v2026.11.14.2.30-beta", prerelease: true), Release("v2026.11.13"), Release("v2026.11.12.1.10-nightly", prerelease: true));
        var running = Running("v2026.11.12", VersionKind.Release);
        var nightly = UpdateStatusBuilder.Build(running, feed, UpdateChannel.Nightly, desktopHost: false, desktopCanInstall: false);
        // The release .13 is newer than the running version, the beta .14 is newer still but not in this channel
        Assert.Equal("v2026.11.13", nightly.Available!.Version.Text);
        var onlyBeta = UpdateStatusBuilder.Build(Running("v2026.11.13", VersionKind.Release), feed, UpdateChannel.Nightly, desktopHost: false, desktopCanInstall: false);
        Assert.Equal(UpdateState.UpToDate, onlyBeta.State);
        Assert.Null(onlyBeta.Available);
    }

    [Fact]
    public void A_beta_user_is_not_offered_a_nightly()
    {
        var feed = List(Release("v2026.11.14.1.10-nightly", prerelease: true), Release("v2026.11.13"));
        var status = UpdateStatusBuilder.Build(Running("v2026.11.13", VersionKind.Release), feed, UpdateChannel.Beta, desktopHost: false, desktopCanInstall: false);
        Assert.Equal(UpdateState.UpToDate, status.State);
    }

    [Fact]
    public void The_channel_never_downgrades_and_a_running_version_newer_than_the_channel_stays()
    {
        // Running the newest nightly, switching to none or beta: nothing is offered, the nightly stays
        var runningNightly = Running("v2026.11.15.1.10-nightly", VersionKind.Nightly);
        foreach (var channel in new[] { UpdateChannel.None, UpdateChannel.Beta })
        {
            var status = UpdateStatusBuilder.Build(runningNightly, ThreeKindFeed(), channel, desktopHost: false, desktopCanInstall: false);
            Assert.Equal(UpdateState.UpToDate, status.State);
            Assert.Null(status.Available);
        }

        // Running the newest beta, switching to none or nightly: older release/nightly are not offered
        var runningBeta = Running("v2026.11.14.2.30-beta", VersionKind.Beta);
        var older = List(Release("v2026.11.13"), Release("v2026.11.13.1.5-nightly", prerelease: true));
        foreach (var channel in new[] { UpdateChannel.None, UpdateChannel.Nightly })
        {
            Assert.Null(UpdateStatusBuilder.Build(runningBeta, older, channel, desktopHost: false, desktopCanInstall: false).Available);
        }

        // The same-commit order: release above beta above nightly
        var sameCommit = List(Release("v2026.11.14"), Release("v2026.11.14.2.30-beta", prerelease: true), Release("v2026.11.14.1.10-nightly", prerelease: true));
        Assert.Equal("v2026.11.14", UpdateStatusBuilder.Build(Running("v2026.11.14.1.10-nightly", VersionKind.Nightly), sameCommit, UpdateChannel.Nightly, false, false).Available!.Version.Text);
        Assert.Equal("v2026.11.14.2.30-beta", UpdateStatusBuilder.Build(Running("v2026.11.14.1.10-nightly", VersionKind.Nightly), List(Release("v2026.11.14.2.30-beta", prerelease: true)), UpdateChannel.Beta, false, false).Available!.Version.Text);
        Assert.Null(UpdateStatusBuilder.Build(Running("v2026.11.14.2.30-beta", VersionKind.Beta), List(Release("v2026.11.14.1.10-nightly", prerelease: true)), UpdateChannel.Nightly, false, false).Available);
    }

    [Fact]
    public void The_status_names_the_selected_channel_and_the_image_tag_it_needs()
    {
        var running = Running("v2026.11.12", VersionKind.Release);
        foreach (var (channel, id, tag) in new[]
                 {
                     (UpdateChannel.None, "none", "latest"),
                     (UpdateChannel.Beta, "beta", "beta"),
                     (UpdateChannel.Nightly, "nightly", "nightly"),
                 })
        {
            var dto = UpdateEndpoints.ToDto(UpdateStatusBuilder.Build(running, ThreeKindFeed(), channel, desktopHost: false, desktopCanInstall: false));
            Assert.Equal(id, dto.UpdateChannel);
            Assert.Equal(tag, dto.ImageTag);
            Assert.Equal("release", dto.CurrentChannel);
            Assert.Equal("ghcr.io/sushiflux/platinvault", dto.ImageRepository);
        }
    }

    [Fact]
    public void Turning_the_channel_off_on_a_beta_never_downgrades()
    {
        var running = Running("v2026.11.12.13.557-beta", VersionKind.Beta);
        var waiting = UpdateStatusBuilder.Build(running, List(Release("v2026.11.11")), UpdateChannel.None, desktopHost: false, desktopCanInstall: false);
        Assert.Equal(UpdateState.UpToDate, waiting.State);
        Assert.Equal(DockerAdvice.BetaStays, waiting.Docker);

        // The beta became a release (the same commit, released later): offered, and switching to :latest is safe now
        var released = UpdateStatusBuilder.Build(running, List(Release("v2026.11.12"), Release("v2026.11.11")), UpdateChannel.None, desktopHost: false, desktopCanInstall: false);
        Assert.Equal("v2026.11.12", released.Available!.Version.Text);
        Assert.Equal(DockerAdvice.SwitchToLatest, released.Docker);
    }

    [Fact]
    public void Container_advice_follows_channel_and_running_kind()
    {
        var list = List(Release("v2026.11.13.1.2-beta", prerelease: true), Release("v2026.11.12"));
        Assert.Equal(DockerAdvice.UseBeta, UpdateStatusBuilder.Build(Running("v2026.11.12", VersionKind.Release), list, UpdateChannel.Beta, false, false).Docker);
        Assert.Equal(DockerAdvice.None, UpdateStatusBuilder.Build(Running("v2026.11.12", VersionKind.Release), list, UpdateChannel.None, false, false).Docker);
        Assert.Equal(DockerAdvice.None, UpdateStatusBuilder.Build(Running("v2026.11.13.1.2-beta", VersionKind.Beta), list, UpdateChannel.Beta, false, false).Docker);
        Assert.Equal(DockerAdvice.UseNightly, UpdateStatusBuilder.Build(Running("v2026.11.12", VersionKind.Release), list, UpdateChannel.Nightly, false, false).Docker);
        Assert.Equal(DockerAdvice.None, UpdateStatusBuilder.Build(Running("v2026.11.14.1.1-nightly", VersionKind.Nightly), list, UpdateChannel.Nightly, false, false).Docker);
        // The Windows program installs itself: no image advice
        Assert.Equal(DockerAdvice.None, UpdateStatusBuilder.Build(Running("v2026.11.12", VersionKind.Release), list, UpdateChannel.Beta, true, true).Docker);
    }

    [Fact]
    public void Container_advice_never_recommends_a_tag_that_would_be_a_downgrade()
    {
        // A beta runs (.13); the newest nightly (.12) is older and no release is at least as new: :nightly would downgrade, so stay on :beta
        var older = List(Release("v2026.11.12.1.1-nightly", prerelease: true), Release("v2026.11.11"));
        var runningBeta = Running("v2026.11.13.1.2-beta", VersionKind.Beta);
        Assert.Equal(DockerAdvice.BetaStaysForNightly, UpdateStatusBuilder.Build(runningBeta, older, UpdateChannel.Nightly, false, false).Docker);
        // A nightly at least as new, or a release at least as new (it reaches :nightly too): the move is safe
        var caught = List(Release("v2026.11.14.5.5-nightly", prerelease: true));
        Assert.Equal(DockerAdvice.UseNightly, UpdateStatusBuilder.Build(runningBeta, caught, UpdateChannel.Nightly, false, false).Docker);
        // A nightly of the SAME commit ranks below the beta, so it is not "at least as new"
        var sameCommitNightly = List(Release("v2026.11.13.5.5-nightly", prerelease: true));
        Assert.Equal(DockerAdvice.BetaStaysForNightly, UpdateStatusBuilder.Build(runningBeta, sameCommitNightly, UpdateChannel.Nightly, false, false).Docker);
        Assert.Equal(DockerAdvice.UseNightly, UpdateStatusBuilder.Build(runningBeta, List(Release("v2026.11.13")), UpdateChannel.Nightly, false, false).Docker);

        // A nightly runs and the Beta channel is chosen: switch to :beta only once a beta or release is at least as new
        var runningNightly = Running("v2026.11.14.1.1-nightly", VersionKind.Nightly);
        Assert.Equal(DockerAdvice.NightlyStays, UpdateStatusBuilder.Build(runningNightly, List(Release("v2026.11.13")), UpdateChannel.Beta, false, false).Docker);
        Assert.Equal(DockerAdvice.SwitchToBeta, UpdateStatusBuilder.Build(runningNightly, List(Release("v2026.11.14.9.9-beta", prerelease: true)), UpdateChannel.Beta, false, false).Docker);
        Assert.Equal(DockerAdvice.NightlyStays, UpdateStatusBuilder.Build(runningNightly, List(Release("v2026.11.13")), UpdateChannel.None, false, false).Docker);
        Assert.Equal(DockerAdvice.SwitchToLatest, UpdateStatusBuilder.Build(runningNightly, List(Release("v2026.11.14")), UpdateChannel.None, false, false).Docker);
        Assert.Equal("betaStaysForNightly", UpdateEndpoints.ToDto(UpdateStatusBuilder.Build(runningBeta, older, UpdateChannel.Nightly, false, false)).DockerAdvice);
    }

    [Fact]
    public void The_windows_program_only_counts_versions_with_an_installer_and_may_install_them()
    {
        var list = List(Release("v2026.11.14", installer: false), Release("v2026.11.13"));
        var running = Running("v2026.11.12", VersionKind.Release);
        var desktop = UpdateStatusBuilder.Build(running, list, UpdateChannel.None, desktopHost: true, desktopCanInstall: true);
        Assert.Equal("v2026.11.13", desktop.Available!.Version.Text);
        Assert.True(desktop.CanInstall);
        Assert.False(UpdateStatusBuilder.Build(running, list, UpdateChannel.None, desktopHost: true, desktopCanInstall: false).CanInstall);
        // A container is told about every newer version, installer or not
        Assert.Equal("v2026.11.14", UpdateStatusBuilder.Build(running, list, UpdateChannel.None, desktopHost: false, desktopCanInstall: false).Available!.Version.Text);
    }

    [Fact]
    public void The_linux_package_is_found_only_with_its_checksum_and_only_on_github()
    {
        var feed = ReleaseFeed.Parse(Releases(
            LinuxRelease("v2026.11.13"),
            LinuxRelease("v2026.11.12", withChecksum: false),
            LinuxRelease("v2026.11.11", host: "evil.example"),
            Release("v2026.11.10")));
        Assert.Equal("PlatinVault-linux-x64-v2026.11.13.tar.gz", feed[0].LinuxPackage!.Name);
        Assert.EndsWith(".tar.gz.sha256", feed[0].LinuxPackage!.ChecksumUrl);
        Assert.Null(feed[1].LinuxPackage);
        Assert.Null(feed[2].LinuxPackage);
        Assert.Null(feed[3].LinuxPackage);
        // The bridge download next to the package is not mistaken for it
        Assert.StartsWith("https://github.com/", feed[0].LinuxPackage!.Url);
    }

    [Fact]
    public void The_linux_package_only_counts_versions_with_a_package_gets_no_image_advice_and_cannot_install()
    {
        // The newest release is an image-only build (for example platform: unraid): the package is not offered it
        var list = List(LinuxRelease("v2026.11.13"), Release("v2026.11.14", installer: false));
        var running = Running("v2026.11.12", VersionKind.Release);
        var linux = UpdateStatusBuilder.Build(running, list, UpdateChannel.None, desktopHost: false, desktopCanInstall: false, linuxPackage: true);
        Assert.True(linux.LinuxPackageHost);
        Assert.Equal("v2026.11.13", linux.Available!.Version.Text);
        Assert.False(linux.CanInstall);
        Assert.Equal(DockerAdvice.None, linux.Docker);
        // With the beta switch on and a running release the container advice would say "use :beta" - not for the package
        var withBetas = UpdateStatusBuilder.Build(running, List(LinuxRelease("v2026.11.13.1.2-beta", prerelease: true)), UpdateChannel.Beta, false, false, linuxPackage: true);
        Assert.Equal(DockerAdvice.None, withBetas.Docker);
        Assert.Equal("v2026.11.13.1.2-beta", withBetas.Available!.Version.Text);
        // Nothing newer with a package: up to date, although a newer image-only release exists
        var upToDate = UpdateStatusBuilder.Build(Running("v2026.11.13", VersionKind.Release), list, UpdateChannel.None, false, false, linuxPackage: true);
        Assert.Equal(UpdateState.UpToDate, upToDate.State);
        Assert.False(UpdateStatusBuilder.Build(running, list, UpdateChannel.None, false, false).LinuxPackageHost);
    }

    [Fact]
    public void The_update_status_names_the_host_and_the_package_of_the_offer()
    {
        var list = List(LinuxRelease("v2026.11.13"));
        var running = Running("v2026.11.12", VersionKind.Release);
        var dto = UpdateEndpoints.ToDto(UpdateStatusBuilder.Build(running, list, UpdateChannel.None, false, false, linuxPackage: true));
        Assert.Equal("linuxPackage", dto.Host);
        Assert.Equal("PlatinVault-linux-x64-v2026.11.13.tar.gz", dto.Available!.LinuxPackageName);
        Assert.StartsWith("https://github.com/", dto.Available.LinuxPackageUrl);
        Assert.Equal("server", UpdateEndpoints.ToDto(UpdateStatusBuilder.Build(running, list, UpdateChannel.None, false, false)).Host);
        Assert.Equal("desktop", UpdateEndpoints.ToDto(UpdateStatusBuilder.Build(running, list, UpdateChannel.None, true, false)).Host);
    }

    [Fact]
    public void A_dev_build_is_never_offered_a_beta_or_release_and_gets_no_image_advice()
    {
        var dev = new RunningVersion("v2026.11.12.13.557 (</>)", "v2026.11.12.13.557-dev", VersionKind.Dev, ReleaseVersion.Parse("v2026.11.12"));
        // Even a clearly newer beta and release, with the beta switch on, and with a Windows installer: nothing is offered
        var list = List(Release("v2026.11.20"), Release("v2026.11.21.1.2-beta", prerelease: true), Release("v2026.11.12"));
        foreach (var desktop in new[] { false, true })
        {
            var status = UpdateStatusBuilder.Build(dev, list, UpdateChannel.Beta, desktop, desktop);
            Assert.Equal(UpdateState.DevBuild, status.State);
            Assert.Null(status.Available);
            Assert.False(status.CanInstall);
            Assert.Equal(DockerAdvice.None, status.Docker);
            var dto = UpdateEndpoints.ToDto(status);
            Assert.Equal("dev", dto.CurrentChannel);
            Assert.Equal("devBuild", dto.State);
            Assert.Null(dto.Available);
        }
    }

    [Fact]
    public void A_dev_build_tag_in_the_feed_is_skipped_so_no_installation_is_ever_offered_one()
    {
        // The public repository never gets dev builds, but even a stray one must not count as a version
        var feed = ReleaseFeed.Parse(Releases(Release("v2026.11.20.1.2-dev", prerelease: true), Release("v2026.11.12")));
        Assert.Equal(new[] { "v2026.11.12" }, feed.Select(r => r.Version.Text));
        Assert.Null(ReleaseVersion.Parse("v2026.11.20.1.2-dev"));
        Assert.Null(ReleaseFeed.PickUpdate(feed, ReleaseVersion.Parse("v2026.11.12")!.Value, UpdateChannel.Beta));
    }


    [Fact]
    public void Disabled_and_unchecked_states()
    {
        var running = Running("v2026.11.12", VersionKind.Release);
        Assert.Equal(UpdateState.Disabled, UpdateStatusBuilder.Build(running, new ReleaseListResult(false, null, null, null, null, false, null), UpdateChannel.None, true, false).State);
        Assert.Equal(UpdateState.Unchecked, UpdateStatusBuilder.Build(running, new ReleaseListResult(true, null, null, null, null, false, null), UpdateChannel.None, false, false).State);
        var dto = UpdateEndpoints.ToDto(UpdateStatusBuilder.Build(running, List(Release("v2026.11.13")), UpdateChannel.None, false, false));
        Assert.Equal("available", dto.State);
        Assert.Equal("server", dto.Host);
        Assert.Equal("ghcr.io/sushiflux/platinvault", dto.ImageRepository);
    }

    // ---- release notes in the status (the UI never calls GitHub itself) -------------------------------------------------------

    [Fact]
    public void The_release_body_becomes_plain_text_notes_and_a_missing_or_empty_body_gives_null()
    {
        var feed = ReleaseFeed.Parse(Releases(
            Release("v2026.11.14", body: "## Neu\r\n- Eins\r\n- Zwei\t(Tab)\u0007\r\n\r\n  "),
            Release("v2026.11.13", body: "   "),
            Release("v2026.11.12")));
        Assert.Equal("## Neu\n- Eins\n- Zwei\t(Tab)", feed[0].Notes);
        Assert.Null(feed[1].Notes);
        Assert.Null(feed[2].Notes);
    }

    [Fact]
    public void Notes_are_capped_and_never_cut_inside_a_surrogate_pair()
    {
        var long1 = new string('a', ReleaseFeed.MaxNotesLength + 500);
        Assert.Equal(ReleaseFeed.MaxNotesLength, ReleaseFeed.Parse(Releases(Release("v2026.11.14", body: long1)))[0].Notes!.Length);

        // an emoji (two UTF-16 units) straddling the limit is dropped completely, not halved
        var straddling = new string('a', ReleaseFeed.MaxNotesLength - 1) + "\U0001F3C6" + "tail";
        var cut = ReleaseFeed.Parse(Releases(Release("v2026.11.14", body: straddling)))[0].Notes!;
        Assert.Equal(ReleaseFeed.MaxNotesLength - 1, cut.Length);
        Assert.All(cut, ch => Assert.False(char.IsSurrogate(ch)));
    }

    [Fact]
    public void The_status_carries_the_notes_as_the_json_field_notes()
    {
        var running = Running("v2026.11.12", VersionKind.Release);
        var dto = UpdateEndpoints.ToDto(UpdateStatusBuilder.Build(running, List(Release("v2026.11.13", body: "Neu: Notizen im Status")), UpdateChannel.None, false, false));
        Assert.Equal("Neu: Notizen im Status", dto.Available!.Notes);
        Assert.Equal("Neu: Notizen im Status", dto.LatestRelease!.Notes);

        var json = JsonSerializer.SerializeToElement(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("Neu: Notizen im Status", json.GetProperty("available").GetProperty("notes").GetString());

        var without = UpdateEndpoints.ToDto(UpdateStatusBuilder.Build(running, List(Release("v2026.11.13")), UpdateChannel.None, false, false));
        var withoutJson = JsonSerializer.SerializeToElement(without, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(JsonValueKind.Null, withoutJson.GetProperty("available").GetProperty("notes").ValueKind);
    }

    [Fact]
    public async Task Notes_come_from_the_one_releases_request_and_stay_in_the_cache_without_asking_GitHub_again()
    {
        var (service, github, _) = Service();
        github.Answer = () => FakeGitHub.Json(Releases(Release("v2026.11.13", body: "Release notes of 13")));
        var first = await service.GetAsync(UpdateCheckMode.Automatic, UpdateCheckInterval.Daily, CancellationToken.None);
        var cached = await service.GetAsync(UpdateCheckMode.Automatic, UpdateCheckInterval.Daily, CancellationToken.None);

        Assert.Equal("Release notes of 13", first.Releases!.Single().Notes);
        Assert.Equal("Release notes of 13", cached.Releases!.Single().Notes);
        Assert.Equal(1, github.Calls);
        // the only request is the list of releases - never one per release for its notes
        Assert.Single(github.Requests);
        Assert.Contains("/releases", github.Requests[0].AbsolutePath);
    }

    // ---- GitHub calls: cache, throttle, failures ------------------------------------------------------------------------

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeGitHub : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public List<Uri> Requests { get; } = [];

        public Func<HttpResponseMessage> Answer { get; set; } = () => Json(Releases(Release("v2026.11.13")));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Requests.Add(request.RequestUri!);
            Assert.Contains(request.Headers.UserAgent, p => p.Product?.Name == "PlatinVault");
            return Task.FromResult(Answer());
        }

        public static HttpResponseMessage Json(JsonElement body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json") };
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (UpdateService Service, FakeGitHub GitHub, FakeClock Clock) Service(bool enabled = true)
    {
        var github = new FakeGitHub();
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new CompanionOptions { Updates = new UpdateCheckOptions { Enabled = enabled, ApiBaseUrl = "https://fake-github.test" } });
        return (new UpdateService(new Factory(github), options, clock, NullLogger<UpdateService>.Instance), github, clock);
    }

    [Theory]
    [InlineData(UpdateCheckInterval.FourHours, 3.9, 4.1)]
    [InlineData(UpdateCheckInterval.Daily, 23, 25)]
    [InlineData(UpdateCheckInterval.Weekly, 24 * 6.9, 24 * 7.1)]
    [InlineData(UpdateCheckInterval.Monthly, 24 * 29.9, 24 * 30.1)]
    public async Task The_interval_decides_when_an_automatic_check_may_ask_GitHub_again(UpdateCheckInterval interval, double hoursBefore, double hoursAfter)
    {
        var (service, github, clock) = Service();
        await service.GetAsync(UpdateCheckMode.Automatic, interval, CancellationToken.None);
        Assert.Equal(1, github.Calls);

        clock.Now += TimeSpan.FromHours(hoursBefore);
        await service.GetAsync(UpdateCheckMode.Automatic, interval, CancellationToken.None);
        Assert.Equal(1, github.Calls);

        clock.Now += TimeSpan.FromHours(hoursAfter - hoursBefore);
        await service.GetAsync(UpdateCheckMode.Automatic, interval, CancellationToken.None);
        Assert.Equal(2, github.Calls);
    }

    [Fact]
    public async Task At_every_start_asks_once_per_process_and_then_never_automatically()
    {
        var (service, github, clock) = Service();
        await service.GetAsync(UpdateCheckMode.Automatic, UpdateCheckInterval.Startup, CancellationToken.None);
        clock.Now += TimeSpan.FromDays(90);
        await service.GetAsync(UpdateCheckMode.Automatic, UpdateCheckInterval.Startup, CancellationToken.None);
        Assert.Equal(1, github.Calls);
        // A check by hand still works
        await service.GetAsync(UpdateCheckMode.Manual, UpdateCheckInterval.Startup, CancellationToken.None);
        Assert.Equal(2, github.Calls);
    }

    [Fact]
    public async Task Never_asks_only_for_a_manual_check_and_keeps_the_one_minute_rule()
    {
        var (service, github, clock) = Service();
        var idle = await service.GetAsync(UpdateCheckMode.Automatic, UpdateCheckInterval.Never, CancellationToken.None);
        clock.Now += TimeSpan.FromDays(90);
        await service.GetAsync(UpdateCheckMode.Automatic, UpdateCheckInterval.Never, CancellationToken.None);
        Assert.Equal(0, github.Calls);
        Assert.Null(idle.Releases);
        Assert.Null(idle.CheckedAt);

        await service.GetAsync(UpdateCheckMode.Manual, UpdateCheckInterval.Never, CancellationToken.None);
        var throttled = await service.GetAsync(UpdateCheckMode.Manual, UpdateCheckInterval.Never, CancellationToken.None);
        Assert.Equal(1, github.Calls);
        Assert.True(throttled.Throttled);
    }

    [Fact]
    public async Task A_failed_check_is_retried_after_the_pause_whatever_the_interval_even_at_every_start()
    {
        var (service, github, clock) = Service();
        github.Answer = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var failed = await service.GetAsync(UpdateCheckMode.Automatic, UpdateCheckInterval.Startup, CancellationToken.None);
        Assert.NotNull(failed.Error);
        await service.GetAsync(UpdateCheckMode.Automatic, UpdateCheckInterval.Startup, CancellationToken.None);
        Assert.Equal(1, github.Calls);

        clock.Now += UpdateService.AutomaticRetryAfterFailure;
        github.Answer = () => FakeGitHub.Json(Releases(Release("v2026.11.13")));
        var ok = await service.GetAsync(UpdateCheckMode.Automatic, UpdateCheckInterval.Startup, CancellationToken.None);
        Assert.Equal(2, github.Calls);
        Assert.Null(ok.Error);
    }

    [Fact]
    public void The_interval_ids_round_trip_and_unknown_ids_are_not_accepted()
    {
        foreach (var id in UpdateCheckIntervals.AllIds)
        {
            Assert.Equal(id, UpdateCheckIntervals.ToId(UpdateCheckIntervals.TryParse(id)!.Value));
        }
        Assert.Equal(UpdateCheckInterval.Weekly, UpdateCheckIntervals.TryParse(" WEEKLY "));
        Assert.Null(UpdateCheckIntervals.TryParse("hourly"));
        Assert.Null(UpdateCheckIntervals.TryParse(null));
        Assert.Equal(UpdateCheckInterval.Daily, UpdateCheckIntervals.Default);
    }

    [Fact]
    public async Task An_unknown_interval_is_rejected_before_anything_is_written()
    {
        var store = new SettingsStore(null!, null!);
        var request = new UpdateSettingsRequest(
            null, null, null, null, null, null, null, null, null, null, null,
            null, false, null, false, null, false, null, false, UpdateCheckInterval: "hourly");
        var error = await Assert.ThrowsAsync<SettingsValidationException>(() => store.UpdateAsync(request));
        Assert.Contains("\"hourly\"", error.Text.De);
        Assert.Contains("is unknown", error.Text.En);
    }

    [Fact]
    public async Task Automatic_checks_are_cached_for_hours_manual_checks_at_most_once_a_minute()
    {
        var (service, github, clock) = Service();
        var first = await service.GetAsync(UpdateCheckMode.Automatic, CancellationToken.None);
        Assert.Equal(1, github.Calls);
        Assert.Equal("https://fake-github.test/repos/sushiflux/PlatinVault/releases?per_page=100", github.Requests[0].ToString());
        Assert.Equal("v2026.11.13", first.Releases!.Single().Version.Text);

        clock.Now += TimeSpan.FromHours(5);
        await service.GetAsync(UpdateCheckMode.Automatic, CancellationToken.None);
        Assert.Equal(1, github.Calls);

        await service.GetAsync(UpdateCheckMode.Manual, CancellationToken.None);
        Assert.Equal(2, github.Calls);
        var throttled = await service.GetAsync(UpdateCheckMode.Manual, CancellationToken.None);
        Assert.Equal(2, github.Calls);
        Assert.True(throttled.Throttled);
        Assert.Equal(clock.Now + UpdateService.ManualInterval, throttled.NextManualCheckAt);

        clock.Now += TimeSpan.FromSeconds(61);
        await service.GetAsync(UpdateCheckMode.Manual, CancellationToken.None);
        Assert.Equal(3, github.Calls);

        await service.GetAsync(UpdateCheckMode.CacheOnly, CancellationToken.None);
        clock.Now += TimeSpan.FromHours(7);
        await service.GetAsync(UpdateCheckMode.CacheOnly, CancellationToken.None);
        Assert.Equal(3, github.Calls);
        await service.GetAsync(UpdateCheckMode.Automatic, CancellationToken.None);
        Assert.Equal(4, github.Calls);
    }

    [Fact]
    public async Task Switched_off_checks_never_call_github()
    {
        var (service, github, _) = Service(enabled: false);
        var result = await service.GetAsync(UpdateCheckMode.Manual, CancellationToken.None);
        Assert.False(result.Enabled);
        Assert.Equal(0, github.Calls);
    }

    [Fact]
    public async Task A_failure_is_reported_specifically_and_never_cached()
    {
        var (service, github, clock) = Service();
        var reset = clock.Now.AddMinutes(17).ToUnixTimeSeconds();
        github.Answer = () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"message\":\"API rate limit exceeded\"}") };
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("x-ratelimit-reset", reset.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return response;
        };
        var limited = await service.GetAsync(UpdateCheckMode.Automatic, CancellationToken.None);
        Assert.NotNull(limited.Error);
        Assert.Contains("60", limited.Error!.Value.De);
        Assert.Contains("17", limited.Error.Value.De);
        Assert.Contains("12:17", limited.Error.Value.En);

        // Automatic checks wait a few minutes after a failure, then try again; the next good answer clears the error
        clock.Now += TimeSpan.FromMinutes(1);
        await service.GetAsync(UpdateCheckMode.Automatic, CancellationToken.None);
        Assert.Equal(1, github.Calls);
        github.Answer = () => FakeGitHub.Json(Releases(Release("v2026.11.13")));
        clock.Now += UpdateService.AutomaticRetryAfterFailure;
        var ok = await service.GetAsync(UpdateCheckMode.Automatic, CancellationToken.None);
        Assert.Equal(2, github.Calls);
        Assert.Null(ok.Error);
        Assert.NotNull(ok.Releases);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "404")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    public async Task Http_failures_name_the_status(HttpStatusCode status, string expected)
    {
        var (service, github, _) = Service();
        github.Answer = () => new HttpResponseMessage(status);
        var result = await service.GetAsync(UpdateCheckMode.Manual, CancellationToken.None);
        Assert.Contains(expected, result.Error!.Value.De);
        Assert.Contains(expected, result.Error.Value.En);
    }

    [Fact]
    public async Task Unreachable_and_garbage_answers_are_explained()
    {
        var (service, github, clock) = Service();
        github.Answer = () => throw new HttpRequestException("No such host is known. (fake-github.test:443)");
        var down = await service.GetAsync(UpdateCheckMode.Manual, CancellationToken.None);
        Assert.Contains("No such host is known", down.Error!.Value.De);
        Assert.Contains("internet connection", down.Error.Value.En);

        clock.Now += TimeSpan.FromMinutes(2);
        github.Answer = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"not\":\"a list\"}") };
        var garbage = await service.GetAsync(UpdateCheckMode.Manual, CancellationToken.None);
        Assert.Contains("Release-Liste", garbage.Error!.Value.De);
    }

    [Fact]
    public void Error_text_reaches_the_dto_complete_in_the_request_language()
    {
        var running = Running("v2026.11.12", VersionKind.Release);
        var failed = new ReleaseListResult(true, null, null, ServerTexts.Updates.Timeout(20), DateTimeOffset.UtcNow, false, null);
        var dto = UpdateEndpoints.ToDto(UpdateStatusBuilder.Build(running, failed, UpdateChannel.None, false, false));
        Assert.Equal("error", dto.State);
        Assert.Equal(ServerTexts.Updates.Timeout(20).ToString(), dto.Error);
    }

    // ---- start guard: which applied migrations are unknown -------------------------------------------------------------

    [Fact]
    public void Unknown_migrations_ignore_retired_ids()
    {
        var known = new[] { "20261030184109_InitialCreate", "20261101152801_AddGuideTabGroupKey" };
        Assert.Empty(DatabaseInitializer.UnknownMigrations(known, known, new HashSet<string>()));
        Assert.Equal(new[] { "20991231000000_FromTheFuture" },
            DatabaseInitializer.UnknownMigrations([.. known, "20991231000000_FromTheFuture"], known, new HashSet<string>()));
        Assert.Empty(DatabaseInitializer.UnknownMigrations([.. known, "20261031000000_Renamed"], known, new HashSet<string> { "20261031000000_Renamed" }));
        // Every migration in code today counts as known: the retired list starts empty (checked against the dev database 2026-11-02)
        Assert.Empty(DatabaseInitializer.RetiredMigrations);
    }
}
