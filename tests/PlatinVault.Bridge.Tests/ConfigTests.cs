namespace PlatinVault.Bridge.Tests;

[Collection(BridgeGlobals.Name)]
public sealed class ConfigTests
{
    private static string? NoEnv(string _) => null;

    [Fact]
    public void Precedence_is_arguments_then_environment_then_portable_then_user()
    {
        var portable = new StoredBridgeConfig { Server = "http://portable:1", Token = "portable-token-0000" };
        var user = new StoredBridgeConfig { Server = "http://user:2", Token = "user-token-00000000" };
        string? Env(string name) => name == BridgeSettings.ServerEnvVar ? "http://env:3" : name == BridgeSettings.TokenEnvVar ? "env-token-000000000" : null;

        Assert.True(BridgeSettings.TryResolve("http://cli:4", "cli-token-000000000", Env, portable, user, out var cli, out _));
        Assert.Equal("http://cli:4", cli!.ServerText);

        Assert.True(BridgeSettings.TryResolve(null, null, Env, portable, user, out var env, out _));
        Assert.Equal("http://env:3", env!.ServerText);
        Assert.Equal("env-token-000000000", env.Token);

        Assert.True(BridgeSettings.TryResolve(null, null, NoEnv, portable, user, out var port, out _));
        Assert.Equal("http://portable:1", port!.ServerText);

        Assert.True(BridgeSettings.TryResolve(null, null, NoEnv, null, user, out var usr, out _));
        Assert.Equal("http://user:2", usr!.ServerText);
    }

    [Fact]
    public void Missing_server_or_token_gives_the_linux_wording_that_names_the_setup_command()
    {
        Assert.False(BridgeSettings.TryResolve(null, null, NoEnv, null, null, out _, out var noServer, linux: true));
        Assert.Contains("platinvault-bridge setup", noServer);

        Assert.False(BridgeSettings.TryResolve("http://server:5233", null, NoEnv, null, null, out _, out var noToken, linux: true));
        Assert.Contains("platinvault-bridge setup", noToken);

        // The Windows wording is unchanged
        Assert.False(BridgeSettings.TryResolve(null, null, NoEnv, null, null, out _, out var windows));
        Assert.Contains("--save", windows);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://server/")]
    [InlineData("http://user:pw@server/")]
    [InlineData("http://server/?x=1")]
    public void Bad_server_addresses_are_refused(string server)
    {
        Assert.False(BridgeSettings.TryResolve(server, "token-0000000000000", NoEnv, null, null, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void A_token_with_spaces_is_refused_and_the_server_gets_a_trailing_slash_so_sub_paths_survive()
    {
        Assert.False(BridgeSettings.TryResolve("http://s:1", "has a space", NoEnv, null, null, out _, out _));

        Assert.True(BridgeSettings.TryResolve("https://host/platinvault", "token-0000000000000", NoEnv, null, null, out var settings, out _));
        Assert.Equal("https://host/platinvault/api/bridge/state", settings!.StateEndpoint.ToString());
        Assert.Equal("https://host/platinvault/api/bridge/commands", settings.CommandsEndpoint.ToString());
        Assert.Equal("***0000", settings.TokenHint);
    }

    [Fact]
    public void The_config_file_round_trips_and_keeps_the_steam_folder_and_token_when_only_the_language_is_remembered()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("config", "bridge.json");

        BridgeConfigFile.Write(path, new StoredBridgeConfig { Server = "http://s:1", Token = "secret-token-12345", Language = "de", SteamDir = "/games/Steam" });
        BridgeConfigFile.RememberLanguage(path, temp.Combine("user.json"), "en");

        var loaded = BridgeConfigFile.Load(path, out var warning);
        Assert.Null(warning);
        Assert.Equal("en", loaded!.Language);
        Assert.Equal("secret-token-12345", loaded.Token);
        Assert.Equal("/games/Steam", loaded.SteamDir);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void An_unreadable_config_is_reported_and_never_overwritten_by_the_language_memory()
    {
        using var temp = new TempFolder();
        var path = temp.Combine("bridge.json");
        Directory.CreateDirectory(temp.Path);
        File.WriteAllText(path, "{ this is not json");

        Assert.Null(BridgeConfigFile.Load(path, out var warning));
        Assert.NotNull(warning);

        BridgeConfigFile.RememberLanguage(path, temp.Combine("user.json"), "en");
        Assert.Equal("{ this is not json", File.ReadAllText(path));
    }

    [Fact]
    public void On_unix_the_config_file_and_its_folder_are_private()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes");
            return;
        }

        using var temp = new TempFolder();
        var path = temp.Combine("cfg", "bridge.json");
        BridgeConfigFile.Write(path, new StoredBridgeConfig { Server = "http://s:1", Token = "secret-token-12345" });

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(temp.Combine("cfg")));
    }
}
