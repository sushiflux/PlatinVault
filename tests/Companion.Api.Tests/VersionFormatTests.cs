using Companion.Api.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using PlatinVault.Versioning;

namespace Companion.Api.Tests;

/// <summary>How a version is shown and tagged (user decision 2026-11-02): readable form for people, technical form for tags and file names.</summary>
public sealed class VersionFormatTests
{
    private const string Short = "v2026.11.12";
    private const string Build = "v2026.11.12.13.557";

    [Theory]
    [InlineData(VersionKind.Release, "v2026.11.12 (13.557)")]
    [InlineData(VersionKind.Beta, "v2026.11.12 (13.557 \u03B2)")]
    [InlineData(VersionKind.Dev, "v2026.11.12 (13.557-dev)")]
    public void Display_has_the_counters_in_parentheses(VersionKind kind, string expected) =>
        Assert.Equal(expected, VersionFormat.Display(Short, Build, kind));

    [Theory]
    [InlineData(VersionKind.Release, "v2026.11.12")]
    [InlineData(VersionKind.Beta, "v2026.11.12")]
    [InlineData(VersionKind.Dev, "v2026.11.12-dev")]
    public void Display_falls_back_to_line_one_without_counters(VersionKind kind, string expected)
    {
        Assert.Equal(expected, VersionFormat.Display(Short, null, kind));
        Assert.Equal(expected, VersionFormat.Display(Short, "  ", kind));
        // A build id of another commit is not trusted either
        Assert.Equal(expected, VersionFormat.Display(Short, "v2026.11.11.13.557", kind));
    }

    [Theory]
    [InlineData(VersionKind.Release, "v2026.11.12")]
    [InlineData(VersionKind.Beta, "v2026.11.12.13.557-beta")]
    [InlineData(VersionKind.Dev, "v2026.11.12.13.557-dev")]
    public void Tag_is_the_technical_form_without_spaces_or_beta_sign(VersionKind kind, string expected)
    {
        var tag = VersionFormat.Tag(Short, Build, kind);
        Assert.Equal(expected, tag);
        Assert.DoesNotContain(' ', tag);
        Assert.DoesNotContain('(', tag);
        Assert.DoesNotContain(VersionFormat.BetaMark, tag);
    }

    [Theory]
    [InlineData("v2026.11.12.13.557-beta", "v2026.11.12 (13.557 \u03B2)")]
    [InlineData("v2026.11.12.13.557-dev", "v2026.11.12 (13.557-dev)")]
    [InlineData("v2026.11.12", "v2026.11.12")]
    [InlineData("v2026.11.9.31.2840-rc6", "v2026.11.9.31.2840-rc6")]
    [InlineData("something else", "something else")]
    public void A_tag_can_be_shown_in_the_readable_form(string tag, string expected) =>
        Assert.Equal(expected, VersionFormat.DisplayFromTag(tag));

    [Theory]
    [InlineData(VersionKind.Release, "release")]
    [InlineData(VersionKind.Beta, "beta")]
    [InlineData(VersionKind.Dev, "dev")]
    public void The_channel_name_round_trips_through_the_kind(VersionKind kind, string channel)
    {
        Assert.Equal(channel, VersionFormat.ChannelName(kind));
        Assert.Equal(kind, VersionFormat.KindFromChannel(channel));
    }

    [Theory]
    [InlineData("beta", VersionKind.Beta)]
    [InlineData("BETA", VersionKind.Beta)]
    [InlineData("rc", VersionKind.Beta)]
    [InlineData("dev", VersionKind.Dev)]
    [InlineData(" DEV ", VersionKind.Dev)]
    [InlineData("release", VersionKind.Release)]
    [InlineData("", VersionKind.Release)]
    [InlineData(null, VersionKind.Release)]
    public void The_channel_decides_the_kind(string? channel, VersionKind expected) =>
        Assert.Equal(expected, VersionFormat.KindFromChannel(channel));

    [Theory]
    [InlineData("release", VersionKind.Release, "v2026.11.12 (13.557)")]
    [InlineData("beta", VersionKind.Release, "v2026.11.12 (13.557 \u03B2)")]
    [InlineData("rc", VersionKind.Release, "v2026.11.12 (13.557 \u03B2)")]
    [InlineData("release", VersionKind.Dev, "v2026.11.12 (13.557-dev)")]
    // A dev-build image: channel "dev" in a Production environment (the image is not the Development environment)
    [InlineData("dev", VersionKind.Release, "v2026.11.12 (13.557-dev)")]
    public void The_server_shows_the_version_of_its_channel(string channel, VersionKind running, string expected)
    {
        var folder = Path.Combine(Path.GetTempPath(), "pv-version-" + Guid.NewGuid().ToString("N"));
        try
        {
            var content = Path.Combine(folder, "a", "b");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(folder, "version.txt"), $"{Short}\n{Build}\n");
            var environment = new FakeEnvironment(content, running == VersionKind.Dev ? "Development" : "Production");
            var version = new AppVersion(environment, Options.Create(new CompanionOptions { ReleaseChannel = channel }));
            Assert.Equal(expected, version.Current);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private sealed class FakeEnvironment(string contentRoot, string name) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "test";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = name;
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
