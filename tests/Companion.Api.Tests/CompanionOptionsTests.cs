using Companion.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace Companion.Api.Tests;

/// <summary>Where the key ring and the encrypted API keys live: under the data folder, unless a host points them elsewhere (shared development mode).</summary>
public sealed class CompanionOptionsTests
{
    [Fact]
    public void Key_ring_and_secrets_default_to_folders_under_the_data_folder()
    {
        var options = new CompanionOptions { DataDir = Path.Combine("data", "dev") };
        Assert.Equal(Path.Combine("data", "dev", "dp-keys"), options.ResolvedKeyRingDir);
        Assert.Equal(Path.Combine("data", "dev", "secrets"), options.ResolvedSecretsDir);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_override_means_the_default(string? value)
    {
        var options = new CompanionOptions { DataDir = "d", KeyRingDir = value, SecretsDir = value };
        Assert.Equal(Path.Combine("d", "dp-keys"), options.ResolvedKeyRingDir);
        Assert.Equal(Path.Combine("d", "secrets"), options.ResolvedSecretsDir);
    }

    [Fact]
    public void Overrides_from_configuration_move_only_keys_and_secrets_not_the_caches()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Companion:DataDir"] = "program-own",
            ["Companion:KeyRingDir"] = Path.Combine("browser-stack", "dp-keys"),
            ["Companion:SecretsDir"] = Path.Combine("browser-stack", "secrets"),
        }).Build();

        var options = configuration.GetSection(CompanionOptions.Section).Get<CompanionOptions>()!;

        Assert.Equal("program-own", options.DataDir); // caches (library-stats, igdb, translations) stay here
        Assert.Equal(Path.Combine("browser-stack", "dp-keys"), options.ResolvedKeyRingDir);
        Assert.Equal(Path.Combine("browser-stack", "secrets"), options.ResolvedSecretsDir);
    }

    [Theory]
    [InlineData("linux-package", true)]
    [InlineData(" Linux-Package ", true)]
    [InlineData("", false)]
    [InlineData("container", false)]
    public void The_install_kind_tells_the_linux_package_apart(string kind, bool isLinuxPackage)
    {
        // The package's run.sh sets Companion__InstallKind=linux-package; every other value (and none) is not the package
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Companion:InstallKind"] = kind }).Build();
        var options = configuration.GetSection(CompanionOptions.Section).Get<CompanionOptions>() ?? new CompanionOptions();
        Assert.Equal(isLinuxPackage, options.IsLinuxPackage);
    }
}
