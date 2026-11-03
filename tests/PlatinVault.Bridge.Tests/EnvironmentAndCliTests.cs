namespace PlatinVault.Bridge.Tests;

[Collection(BridgeGlobals.Name)]
public sealed class EnvironmentAndCliTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] values) =>
        name => values.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void Without_xdg_variables_everything_lives_below_the_home_folder()
    {
        var env = LinuxEnvironment.Create(null, Env(("HOME", "/home/deck")));

        Assert.Equal(Path.Combine("/home/deck", ".config", "platinvault-bridge", "bridge.json"), env.ConfigPath);
        Assert.Equal(Path.Combine("/home/deck", ".local", "state", "platinvault-bridge", "bridge.log"), env.LogPath);
        Assert.Equal(Path.Combine("/home/deck", ".local", "share", "platinvault-bridge"), env.InstallDirectory);
        Assert.Equal(Path.Combine("/home/deck", ".config", "systemd", "user", "platinvault-bridge.service"), env.UnitPath);
        Assert.False(env.IsTest);
    }

    [Fact]
    public void XDG_variables_win_but_a_relative_value_is_ignored()
    {
        var env = LinuxEnvironment.Create(null, Env(("HOME", "/home/deck"), ("XDG_CONFIG_HOME", "/cfg"), ("XDG_STATE_HOME", "relative/state"), ("XDG_DATA_HOME", "/data/")));

        Assert.Equal(Path.Combine("/cfg", "platinvault-bridge", "bridge.json"), env.ConfigPath);
        Assert.Equal(Path.Combine("/home/deck", ".local", "state", "platinvault-bridge", "bridge.log"), env.LogPath);
        Assert.Equal(Path.Combine("/data", "platinvault-bridge"), env.InstallDirectory);
    }

    [Fact]
    public void A_test_root_moves_every_location_into_one_folder_and_ignores_the_environment()
    {
        using var temp = new TempFolder();
        var env = LinuxEnvironment.Create(temp.Path, Env(("HOME", "/home/deck"), ("XDG_CONFIG_HOME", "/cfg")));

        Assert.True(env.IsTest);
        foreach (var path in new[] { env.ConfigPath, env.LogPath, env.StatusPath, env.InstallDirectory, env.UnitPath, env.LaunchRecordPath, env.FakeSteamPath })
            Assert.StartsWith(Path.GetFullPath(temp.Path), path);
    }

    [Theory]
    [InlineData("de_DE.UTF-8", "de")]
    [InlineData("en_US.UTF-8", "en")]
    [InlineData("fr_FR.UTF-8", "en")]
    [InlineData("de_AT@euro", "de")]
    [InlineData(null, "en")]
    public void The_system_language_is_german_only_for_a_german_locale(string? lang, string expected)
    {
        Assert.Equal(expected, LinuxEnvironment.SystemLanguage(lang is null ? Env() : Env(("LANG", lang))));
    }

    [Fact]
    public void LC_ALL_beats_LANG_and_a_missing_or_C_locale_means_english_whatever_LANGUAGE_says()
    {
        Assert.Equal("en", LinuxEnvironment.SystemLanguage(Env(("LC_ALL", "en_US.UTF-8"), ("LANG", "de_DE.UTF-8"))));
        Assert.Equal("en", LinuxEnvironment.SystemLanguage(Env(("LANG", "C"), ("LANGUAGE", "de:en")))); // glibc ignores LANGUAGE in the C locale
        Assert.Equal("en", LinuxEnvironment.SystemLanguage(Env(("LANG", "C"))));
        Assert.Equal("en", LinuxEnvironment.SystemLanguage(Env(("LANGUAGE", "de"))));
    }

    [Fact]
    public void Like_glibc_the_LANGUAGE_list_beats_LANG_for_message_texts()
    {
        Assert.Equal("de", LinuxEnvironment.SystemLanguage(Env(("LANG", "en_US.UTF-8"), ("LANGUAGE", "de:en"))));
        Assert.Equal("en", LinuxEnvironment.SystemLanguage(Env(("LANG", "de_DE.UTF-8"), ("LANGUAGE", "en_GB:de"))));
        // The first entry the bridge speaks counts; a list without German or English changes nothing
        Assert.Equal("de", LinuxEnvironment.SystemLanguage(Env(("LANG", "en_US.UTF-8"), ("LANGUAGE", "fr:de"))));
        Assert.Equal("de", LinuxEnvironment.SystemLanguage(Env(("LANG", "de_DE.UTF-8"), ("LANGUAGE", "fr:es"))));
    }

    [Fact]
    public void The_command_word_is_optional_and_options_take_both_spellings()
    {
        var cli = LinuxCommandLine.Parse(["setup", "--server", "http://s:1", "--token=abc", "--steam-dir", "/games/Steam", "--no-check"], out var error)!;
        Assert.Null(error);
        Assert.Equal(LinuxCommand.Setup, cli.Command);
        Assert.Equal("http://s:1", cli.Server);
        Assert.Equal("abc", cli.Token);
        Assert.Equal("/games/Steam", cli.SteamDir);
        Assert.True(cli.NoCheck);

        Assert.Equal(LinuxCommand.Run, LinuxCommandLine.Parse(["--verbose"], out _)!.Command);
        Assert.Equal(LinuxCommand.Run, LinuxCommandLine.Parse(["run"], out _)!.Command);
        Assert.Equal(LinuxCommand.ShowHelp, LinuxCommandLine.Parse([], out _)!.Command);
        Assert.Equal(LinuxCommand.ShowVersion, LinuxCommandLine.Parse(["--version"], out _)!.Command);
        Assert.Equal(LinuxCommand.Status, LinuxCommandLine.Parse(["status"], out _)!.Command);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("--nonsense")]
    [InlineData("--server")]
    [InlineData("--language fr")]
    [InlineData("--interval 5")]
    [InlineData("--interval abc")]
    [InlineData("--app 0")]
    public void Wrong_usage_gives_a_message_and_no_options(string commandLine)
    {
        var cli = LinuxCommandLine.Parse(commandLine.Split(' '), out var error);
        Assert.Null(cli);
        Assert.False(string.IsNullOrEmpty(error?.ToString()));
    }

    [Fact]
    public void Test_options_are_parsed_into_their_types()
    {
        var cli = LinuxCommandLine.Parse(["run", "--test-root", "/tmp/x", "--test-launch", "steam-missing", "--app", "730", "--interval", "250", "--language", "DE"], out _)!;
        Assert.Equal("/tmp/x", cli.TestRoot);
        Assert.Equal(LaunchOutcome.SteamNotFound, cli.TestLaunch);
        Assert.Equal(730u, cli.TestAppId);
        Assert.Equal(250, cli.IntervalMs);
        Assert.Equal("de", cli.Language);
    }

    [Fact]
    public void The_token_option_and_its_value_are_taken_out_of_the_arguments_of_a_restart()
    {
        Assert.Equal(["run", "--server", "http://s:1", "--verbose"], LinuxApp.WithoutToken(["run", "--server", "http://s:1", "--token", "secret", "--verbose"]));
        Assert.Equal(["run"], LinuxApp.WithoutToken(["run", "--token=secret"]));
    }

    [Fact]
    public void Token_stdin_is_a_flag_without_a_value()
    {
        var cli = LinuxCommandLine.Parse(["setup", "--server", "http://s:1", "--token-stdin"], out _)!;
        Assert.True(cli.TokenFromStdin);
        Assert.Null(cli.Token);
    }

    [Fact]
    public void The_update_marker_is_removed_before_the_restart_adds_a_new_one()
    {
        Assert.Equal(["run", "--verbose"], LinuxApp.WithoutRestartMarker(["run", "--updated-to", "1.0.4", "--verbose"]));
        Assert.Equal(["run"], LinuxApp.WithoutRestartMarker(["run", "--updated-to=1.0.4"]));
    }

    [Fact]
    public void Both_text_catalogues_have_every_text_in_both_languages()
    {
        Assert.Equal(0, TextCatalog.Run([typeof(LinuxTexts), typeof(CoreTexts)]));
    }
}
