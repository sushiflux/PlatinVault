namespace Companion.Api.Tests;

/// <summary>
/// An in-memory file system with Linux paths and symbolic links (creating real links needs privileges on Windows, and the Linux rules
/// must be testable on any machine). Folders and links follow the semantics of a real file system: a path counts when it resolves to a
/// known folder.
/// </summary>
internal sealed class FakeSteamFileSystem : ISteamFileSystem
{
    private readonly HashSet<string> _dirs = new(StringComparer.Ordinal) { "/" };
    private readonly HashSet<string> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _links = new(StringComparer.Ordinal);

    /// <summary>Folders (with all their parents).</summary>
    public FakeSteamFileSystem Dir(params string[] paths)
    {
        foreach (var path in paths)
        {
            var current = string.Empty;
            foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                current += "/" + part;
                _dirs.Add(current);
            }
        }
        return this;
    }

    /// <summary>A Steam installation: the folder with <c>steamapps</c>, <c>config</c> and <c>appcache</c> (the client's own folder).</summary>
    public FakeSteamFileSystem Steam(string path) => Dir(path + "/steamapps", path + "/config", path + "/appcache");

    /// <summary>A game library: only <c>steamapps</c> (e.g. a second drive's SteamLibrary).</summary>
    public FakeSteamFileSystem Library(string path) => Dir(path + "/steamapps");

    public FakeSteamFileSystem Link(string path, string target)
    {
        var slash = path.LastIndexOf('/');
        if (slash > 0) Dir(path[..slash]);
        _links[path] = target;
        return this;
    }

    public bool DirectoryExists(string path) => SteamLocator.RealPath(path, this) is { } real && _dirs.Contains(real);

    public bool FileExists(string path) => SteamLocator.RealPath(path, this) is { } real && _files.Contains(real);

    public string? ReadLink(string path) => _links.GetValueOrDefault(path);
}

public sealed class SteamLocatorTests : IDisposable
{
    private const string Home = "/home/player";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pv-steamlocator-" + Guid.NewGuid().ToString("N")[..10]);

    public SteamLocatorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private static SteamLocatorInput Linux(FakeSteamFileSystem fs, Dictionary<string, string>? env = null, string? manual = null, string? configured = null) => new()
    {
        Platform = SteamPlatform.Linux,
        FileSystem = fs,
        HomeDirectory = Home,
        ManualRoot = manual,
        ConfiguredRoot = configured,
        Environment = name => env?.GetValueOrDefault(name),
        RegistrySteamPath = () => throw new InvalidOperationException("Linux never reads the registry"),
    };

    // The Windows rules on real folders need Windows paths (the Linux run in Docker skips them)
    private static void SkipUnlessWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows paths on the real file system: runs on Windows only.");
    }

    private SteamLocatorInput Windows(string? registry, Dictionary<string, string>? env = null, string? manual = null) => new()
    {
        Platform = SteamPlatform.Windows,
        ManualRoot = manual,
        Environment = name => env?.GetValueOrDefault(name),
        RegistrySteamPath = () => registry,
    };

    // ---- Linux: the known places ----------------------------------------------------------------------------------------

    [Fact]
    public void Linux_without_Steam_finds_nothing_and_lists_every_known_place()
    {
        var location = SteamLocator.Locate(Linux(new FakeSteamFileSystem().Dir(Home)));

        Assert.False(location.Found);
        Assert.Null(location.Root);
        Assert.Equal(SteamLocationMethod.None, location.Method);
        Assert.True(location.IsExperimental);
        Assert.DoesNotContain(location.Candidates, c => c.Chosen || c.Exists);
        Assert.Equal(
            [
                Home + "/.steam/steam", Home + "/.steam/root", Home + "/.local/share/Steam", Home + "/.steam/debian-installation",
                Home + "/.var/app/com.valvesoftware.Steam/.local/share/Steam", Home + "/.var/app/com.valvesoftware.Steam/.steam/steam",
                Home + "/.var/app/com.valvesoftware.Steam/.steam/root", Home + "/snap/steam/common/.local/share/Steam",
                Home + "/.snap/data/steam/common/.local/share/Steam",
            ],
            location.Candidates.Select(c => c.Path));
    }

    [Fact]
    public void Valve_default_folder_is_found_as_a_known_path()
    {
        var location = SteamLocator.Locate(Linux(new FakeSteamFileSystem().Steam(Home + "/.local/share/Steam")));

        Assert.Equal(Home + "/.local/share/Steam", location.Root);
        Assert.Equal(SteamLocationMethod.KnownPath, location.Method);
        Assert.Equal("native", location.Candidates.Single(c => c.Chosen).Detail);
    }

    [Fact]
    public void The_steam_symlink_wins_and_resolves_to_its_target_which_then_counts_once()
    {
        // Debian/Ubuntu package: ~/.steam/debian-installation is the folder, ~/.steam/steam and ~/.steam/root point at it
        var fs = new FakeSteamFileSystem()
            .Steam(Home + "/.steam/debian-installation")
            .Link(Home + "/.steam/steam", Home + "/.steam/debian-installation")
            .Link(Home + "/.steam/root", Home + "/.steam/debian-installation");

        var location = SteamLocator.Locate(Linux(fs));

        Assert.Equal(Home + "/.steam/debian-installation", location.Root);
        Assert.Equal(SteamLocationMethod.Symlink, location.Method);
        var steam = location.Candidates[0];
        Assert.True(steam.Chosen);
        Assert.Equal(Home + "/.steam/steam", steam.Path);
        Assert.Equal(Home + "/.steam/debian-installation", steam.ResolvedPath);
        // The other two ways to the same folder are listed, but as duplicates
        Assert.True(location.Candidates.Single(c => c.Path == Home + "/.steam/root").IsDuplicate);
        Assert.True(location.Candidates.Single(c => c.Path == Home + "/.steam/debian-installation").IsDuplicate);
        Assert.Single(location.Candidates, c => c.Chosen);
    }

    [Fact]
    public void A_relative_link_target_is_resolved_against_the_folder_of_the_link()
    {
        var fs = new FakeSteamFileSystem()
            .Steam(Home + "/.local/share/Steam")
            .Link(Home + "/.steam/steam", "../.local/share/Steam");

        var location = SteamLocator.Locate(Linux(fs));

        Assert.Equal(Home + "/.local/share/Steam", location.Root);
        Assert.Equal(SteamLocationMethod.Symlink, location.Method);
    }

    [Fact]
    public void A_link_loop_or_a_broken_link_is_skipped_without_an_error()
    {
        // Reported on the Arch forum: the link can point at itself
        var fs = new FakeSteamFileSystem()
            .Link(Home + "/.steam/steam", Home + "/.steam/steam")
            .Link(Home + "/.steam/root", "/nowhere/Steam")
            .Steam(Home + "/.var/app/com.valvesoftware.Steam/.local/share/Steam");

        var location = SteamLocator.Locate(Linux(fs));

        Assert.Null(location.Candidates[0].ResolvedPath);
        Assert.False(location.Candidates[0].Exists);
        Assert.False(location.Candidates[1].Exists);
        Assert.Equal(Home + "/.var/app/com.valvesoftware.Steam/.local/share/Steam", location.Root);
        Assert.Equal("flatpak", location.Candidates.Single(c => c.Chosen).Detail);
    }

    [Fact]
    public void A_folder_without_steamapps_does_not_count()
    {
        // A leftover ~/.local/share/Steam (e.g. only the tray-icon symlink target of the Flatpak wiki missing) next to a Snap Steam
        var fs = new FakeSteamFileSystem()
            .Dir(Home + "/.local/share/Steam")
            .Steam(Home + "/snap/steam/common/.local/share/Steam");

        var location = SteamLocator.Locate(Linux(fs));

        var leftover = location.Candidates.Single(c => c.Path == Home + "/.local/share/Steam");
        Assert.True(leftover.Exists);
        Assert.False(leftover.HasSteamApps);
        Assert.False(leftover.Chosen);
        Assert.Equal(Home + "/snap/steam/common/.local/share/Steam", location.Root);
        Assert.Equal("snap", location.Candidates.Single(c => c.Chosen).Detail);
    }

    [Fact]
    public void Xdg_data_home_is_used_when_it_is_absolute_and_the_default_stays_a_candidate()
    {
        var fs = new FakeSteamFileSystem().Steam("/data/xdg/Steam");

        var location = SteamLocator.Locate(Linux(fs, new() { ["XDG_DATA_HOME"] = "/data/xdg" }));

        Assert.Equal("/data/xdg/Steam", location.Root);
        Assert.Contains(location.Candidates, c => c.Path == Home + "/.local/share/Steam");

        // A relative XDG_DATA_HOME is ignored (XDG Base Directory specification)
        var relative = SteamLocator.Locate(Linux(fs, new() { ["XDG_DATA_HOME"] = "xdg" }));
        Assert.DoesNotContain(relative.Candidates, c => c.Path.Contains("xdg", StringComparison.Ordinal));
    }

    // ---- explicit folders: setting, configuration, environment ------------------------------------------------------------

    [Fact]
    public void Precedence_is_setting_then_configuration_then_our_variable_then_STEAM_DIR_then_the_search()
    {
        var fs = new FakeSteamFileSystem()
            .Steam("/games/manual").Steam("/games/config").Steam("/games/own").Steam("/games/steamdir").Steam(Home + "/.local/share/Steam");
        var env = new Dictionary<string, string> { [SteamLocator.PlatinVaultSteamDirVariable] = "/games/own", [SteamLocator.SteamDirVariable] = "/games/steamdir" };

        Assert.Equal(("/games/manual", SteamLocationMethod.Manual), Pick(SteamLocator.Locate(Linux(fs, env, "/games/manual", "/games/config"))));
        Assert.Equal(("/games/config", SteamLocationMethod.Configuration), Pick(SteamLocator.Locate(Linux(fs, env, null, "/games/config"))));
        Assert.Equal(("/games/own", SteamLocationMethod.Environment), Pick(SteamLocator.Locate(Linux(fs, env))));
        var steamDir = SteamLocator.Locate(Linux(fs, new() { [SteamLocator.SteamDirVariable] = "/games/steamdir" }));
        Assert.Equal(("/games/steamdir", SteamLocationMethod.Environment), Pick(steamDir));
        Assert.Equal(SteamLocator.SteamDirVariable, steamDir.Candidates.Single(c => c.Chosen).Detail);
        Assert.Equal((Home + "/.local/share/Steam", SteamLocationMethod.KnownPath), Pick(SteamLocator.Locate(Linux(fs))));

        static (string?, SteamLocationMethod) Pick(SteamLocation l) => (l.Root, l.Method);
    }

    [Fact]
    public void An_unusable_explicit_folder_is_skipped_and_the_search_goes_on()
    {
        var fs = new FakeSteamFileSystem().Dir("/games/empty").Steam(Home + "/.local/share/Steam");

        var location = SteamLocator.Locate(Linux(fs, manual: "/games/empty"));

        var manual = location.Candidates[0];
        Assert.Equal(SteamLocationMethod.Manual, manual.Method);
        Assert.True(manual.Exists);
        Assert.False(manual.HasSteamApps);
        Assert.False(manual.Chosen);
        Assert.Equal(Home + "/.local/share/Steam", location.Root);
    }

    [Fact]
    public void A_tilde_in_an_explicit_folder_means_the_home_folder()
    {
        var fs = new FakeSteamFileSystem().Steam(Home + "/Games/SteamLibrary");

        var location = SteamLocator.Locate(Linux(fs, manual: "  \"~/Games/SteamLibrary/\"  "));

        Assert.Equal(Home + "/Games/SteamLibrary", location.Root);
        Assert.Equal(Home + "/Games/SteamLibrary", location.Candidates[0].Path);
    }

    // ---- Windows: the registry, exactly as before -------------------------------------------------------------------------

    [Fact]
    public void Windows_takes_the_registry_path_as_before_even_without_steamapps()
    {
        var location = SteamLocator.Locate(Windows("c:/program files (x86)/steam"));

        Assert.Equal(@"c:\program files (x86)\steam", location.Root);
        Assert.Equal(SteamLocationMethod.Registry, location.Method);
        Assert.False(location.IsExperimental);
        Assert.Equal(SteamLocator.RegistryValueName, location.Candidates.Single().Detail);
    }

    [Fact]
    public void Windows_without_registry_value_finds_nothing_and_ignores_STEAM_DIR()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "steamapps"));

        var location = SteamLocator.Locate(Windows(null, new() { [SteamLocator.SteamDirVariable] = _dir }));

        Assert.False(location.Found);
        Assert.Empty(location.Candidates);
    }

    [Fact]
    public void Windows_takes_a_usable_setting_or_our_variable_before_the_registry()
    {
        SkipUnlessWindows();
        var steam = Path.Combine(_dir, "Steam");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(steam, "config"));

        var manual = SteamLocator.Locate(Windows(@"C:\Steam", manual: steam.Replace('\\', '/') + "/"));
        Assert.Equal(steam, manual.Root);
        Assert.Equal(SteamLocationMethod.Manual, manual.Method);

        var variable = SteamLocator.Locate(Windows(@"C:\Steam", new() { [SteamLocator.PlatinVaultSteamDirVariable] = steam }));
        Assert.Equal(steam, variable.Root);
        Assert.Equal(SteamLocationMethod.Environment, variable.Method);

        // An unusable setting falls back to the registry
        var stale = SteamLocator.Locate(Windows(@"C:\Steam", manual: Path.Combine(_dir, "gone")));
        Assert.Equal(@"C:\Steam", stale.Root);
        Assert.Equal(SteamLocationMethod.Registry, stale.Method);
    }

    // ---- the check of the setting ---------------------------------------------------------------------------------------

    [Fact]
    public void Check_names_what_is_wrong_with_a_folder()
    {
        var fs = new FakeSteamFileSystem().Dir("/games/empty").Steam("/games/steam").Link("/games/link", "/games/steam");
        var input = Linux(fs);

        Assert.Equal(SteamRootProblem.NotAbsolute, SteamLocator.CheckRoot("games/steam", input).Problem);
        Assert.Equal(SteamRootProblem.NotAbsolute, SteamLocator.CheckRoot("   ", input).Problem);
        Assert.Equal(SteamRootProblem.NotFound, SteamLocator.CheckRoot("/games/missing", input).Problem);
        Assert.Equal(SteamRootProblem.NoSteamApps, SteamLocator.CheckRoot("/games/empty", input).Problem);
        var ok = SteamLocator.CheckRoot("/games/steam/", input);
        Assert.True(ok.IsValid);
        Assert.Equal("/games/steam", ok.Path);
        var link = SteamLocator.CheckRoot("/games/link", input);
        Assert.True(link.IsValid);
        Assert.Equal("/games/link", link.Path);
        Assert.Equal("/games/steam", link.ResolvedPath);
    }

    [Fact]
    public void Check_on_Windows_uses_the_real_folders()
    {
        SkipUnlessWindows();
        var steam = Path.Combine(_dir, "Steam");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        Directory.CreateDirectory(Path.Combine(steam, "config"));
        var input = Windows(null);

        Assert.True(SteamLocator.CheckRoot(steam, input).IsValid);
        Assert.Equal(SteamRootProblem.NoSteamApps, SteamLocator.CheckRoot(_dir, input).Problem);
        Assert.Equal(SteamRootProblem.NotFound, SteamLocator.CheckRoot(Path.Combine(_dir, "missing"), input).Problem);
        Assert.Equal(SteamRootProblem.NotAbsolute, SteamLocator.CheckRoot(@"Steam\steamapps", input).Problem);
    }

    // ---- real links on the real file system --------------------------------------------------------------------------------

    [Fact]
    public void The_physical_file_system_reads_a_real_directory_link()
    {
        var target = Path.Combine(_dir, "target");
        Directory.CreateDirectory(target);
        var link = Path.Combine(_dir, "link");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Windows only allows symbolic links with developer mode or as administrator; the fake file system covers the logic
            Assert.Skip($"Symbolic links cannot be created here: {ex.Message}");
        }

        Assert.Equal(target, PhysicalSteamFileSystem.Instance.ReadLink(link));
        Assert.Null(PhysicalSteamFileSystem.Instance.ReadLink(target));
    }

    [Fact]
    public void On_a_real_Linux_the_locator_resolves_real_links()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Runs on Linux only (the Docker check of the Linux build).");
        }
        var home = Path.Combine(_dir, "home");
        Directory.CreateDirectory(Path.Combine(home, ".local", "share", "Steam", "steamapps"));
        Directory.CreateDirectory(Path.Combine(home, ".local", "share", "Steam", "appcache"));
        Directory.CreateDirectory(Path.Combine(home, ".steam"));
        Directory.CreateSymbolicLink(Path.Combine(home, ".steam", "steam"), Path.Combine(home, ".local", "share", "Steam"));

        var location = SteamLocator.Locate(new SteamLocatorInput { Platform = SteamPlatform.Linux, HomeDirectory = home, Environment = _ => null });

        Assert.Equal(Path.Combine(home, ".local", "share", "Steam"), location.Root);
        Assert.Equal(SteamLocationMethod.Symlink, location.Method);
    }

    // ---- the provider -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Provider_reuses_its_answer_for_the_interval_and_a_new_setting_takes_effect_at_once()
    {
        var fs = new FakeSteamFileSystem().Steam("/games/a");
        var now = 0L;
        var provider = new SteamLocationProvider(Linux(fs), refreshInterval: TimeSpan.FromSeconds(2), tickCount: () => now);

        Assert.False(provider.Current.Found);
        fs.Steam(Home + "/.local/share/Steam");
        now = 1000;
        Assert.False(provider.Current.Found);
        now = 2500;
        Assert.Equal(Home + "/.local/share/Steam", provider.Current.Root);

        provider.ManualRoot = "/games/a";
        Assert.Equal("/games/a", provider.Current.Root);
        Assert.Equal(SteamLocationMethod.Manual, provider.Current.Method);
        Assert.True(provider.Check("/games/a").IsValid);

        provider.ManualRoot = "  ";
        Assert.Null(provider.ManualRoot);
        Assert.Equal(Home + "/.local/share/Steam", provider.Current.Root);
    }

    // ---- game library vs. Steam folder ------------------------------------------------------------------------------------

    [Fact]
    public void A_game_library_is_refused_as_the_Steam_folder_with_its_own_reason()
    {
        var fs = new FakeSteamFileSystem().Library("/mnt/games/SteamLibrary").Dir("/mnt/games/OnlyConfig/steamapps", "/mnt/games/OnlyConfig/config");
        var input = Linux(fs);

        Assert.Equal(SteamRootProblem.LibraryOnly, SteamLocator.CheckRoot("/mnt/games/SteamLibrary", input).Problem);
        // Either marker is enough: config without appcache still is the Steam client's folder
        Assert.True(SteamLocator.CheckRoot("/mnt/games/OnlyConfig", input).IsValid);
    }

    [Fact]
    public void A_library_set_by_hand_does_not_win_over_the_search()
    {
        var fs = new FakeSteamFileSystem().Library("/mnt/games/SteamLibrary").Steam(Home + "/.local/share/Steam");

        var location = SteamLocator.Locate(Linux(fs, manual: "/mnt/games/SteamLibrary"));

        var manual = location.Candidates[0];
        Assert.True(manual.HasSteamApps);
        Assert.False(manual.IsSteamRoot);
        Assert.False(manual.Chosen);
        Assert.Equal(Home + "/.local/share/Steam", location.Root);
        Assert.True(location.Candidates.Single(c => c.Chosen).IsSteamRoot);
    }

    [Fact]
    public void A_known_path_that_is_only_a_library_is_not_chosen()
    {
        var fs = new FakeSteamFileSystem().Library(Home + "/.local/share/Steam").Steam(Home + "/.var/app/com.valvesoftware.Steam/.local/share/Steam");

        var location = SteamLocator.Locate(Linux(fs));

        Assert.Equal(Home + "/.var/app/com.valvesoftware.Steam/.local/share/Steam", location.Root);
    }

    [Fact]
    public void On_Windows_a_library_set_by_hand_leaves_the_registry_in_charge()
    {
        SkipUnlessWindows();
        var library = Path.Combine(_dir, "SteamLibrary");
        Directory.CreateDirectory(Path.Combine(library, "steamapps"));

        Assert.Equal(SteamRootProblem.LibraryOnly, SteamLocator.CheckRoot(library, Windows(null)).Problem);
        var location = SteamLocator.Locate(Windows(@"C:\Program Files (x86)\Steam", manual: library));
        Assert.Equal(@"C:\Program Files (x86)\Steam", location.Root);
        Assert.Equal(SteamLocationMethod.Registry, location.Method);
        // The registry value itself is still taken without any check, as before
        Assert.False(location.Candidates.Single(c => c.Chosen).IsSteamRoot);
    }

    // ---- Fedora Silverblue / Bazzite: /home is a link to /var/home ----------------------------------------------------------

    [Fact]
    public void A_linked_home_folder_keeps_the_standard_paths_standard()
    {
        var fs = new FakeSteamFileSystem()
            .Link("/home", "var/home")
            .Steam("/var/home/player/.local/share/Steam")
            .Link("/var/home/player/.steam/steam", "/var/home/player/.local/share/Steam");

        var location = SteamLocator.Locate(Linux(fs));

        Assert.Equal("/var/home/player/.local/share/Steam", location.Root);
        Assert.Equal(SteamLocationMethod.Symlink, location.Method);
        var standard = location.Candidates.Single(c => c.Path == "/var/home/player/.local/share/Steam");
        Assert.Equal(SteamLocationMethod.KnownPath, standard.Method);
        Assert.Equal(SteamLocationMethod.KnownPath, location.Candidates.Single(c => c.Path.EndsWith("/snap/steam/common/.local/share/Steam", StringComparison.Ordinal)).Method);
    }

    // ---- logging --------------------------------------------------------------------------------------------------------

    private sealed class ListLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{logLevel}: {formatter(state, exception)}");
    }

    [Fact]
    public void A_host_that_reports_the_folder_itself_only_gets_later_changes_logged()
    {
        var fs = new FakeSteamFileSystem().Steam(Home + "/.local/share/Steam");
        var quiet = new ListLogger();
        var provider = new SteamLocationProvider(Linux(fs), quiet, refreshInterval: TimeSpan.Zero, logInitialResult: false);

        Assert.True(provider.Current.Found);
        Assert.True(provider.Current.Found);
        Assert.Empty(quiet.Lines);

        // A change while it runs is still logged (Steam installed after the start)
        var later = new FakeSteamFileSystem();
        var laterLog = new ListLogger();
        var laterProvider = new SteamLocationProvider(Linux(later), laterLog, refreshInterval: TimeSpan.Zero, logInitialResult: false);
        Assert.False(laterProvider.Current.Found);
        Assert.Empty(laterLog.Lines);
        later.Steam(Home + "/.local/share/Steam");
        Assert.True(laterProvider.Current.Found);
        Assert.Contains(laterLog.Lines, line => line.Contains("Lokales Steam gefunden", StringComparison.Ordinal));

        var loud = new ListLogger();
        _ = new SteamLocationProvider(Linux(fs), loud).Current;
        Assert.Equal(2, loud.Lines.Count);
        Assert.Contains("EXPERIMENTELL", loud.Lines[1], StringComparison.Ordinal);
    }
}
