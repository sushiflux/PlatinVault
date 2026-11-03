using PlatinVault.SteamLocal;

namespace PlatinVault.Bridge;

/// <summary>
/// <c>platinvault-bridge setup</c>: validates server and token, writes <c>bridge.json</c> (mode 0600) and tests the connection with a real
/// state push - the same thing the Windows setup window does. Without arguments it takes the <c>bridge.json</c> that sits next to the binary
/// (the package from the server ships one). A connection problem is a warning, not a failure: the file is saved and the running bridge keeps trying.
/// </summary>
internal static class LinuxSetupCommand
{
    public static async Task<int> RunAsync(LinuxCliOptions cli, LinuxEnvironment env, StoredBridgeConfig? portable, StoredBridgeConfig? user,
        string version, Func<string, string?> getEnvironmentVariable, CancellationToken ct)
    {
        if (cli.TokenFromStdin)
        {
            var token = Console.In.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(token))
            {
                BridgeLog.Error(LinuxTexts.Setup.StdinEmpty);
                return LinuxApp.ExitUsage;
            }
            cli.Token = token;
        }

        StoredBridgeConfig? from = null;
        if (cli.FromFile is not null)
        {
            from = BridgeConfigFile.Load(cli.FromFile, out var warning);
            if (warning is not null) BridgeLog.Warn(warning);
            if (from is null || string.IsNullOrWhiteSpace(from.Server) || string.IsNullOrWhiteSpace(from.Token))
            {
                BridgeLog.Error(LinuxTexts.Setup.FromFileUnreadable(cli.FromFile));
                return LinuxApp.ExitUsage;
            }
            // The language the server wrote into the package: speak it from here on (a pinned --language still wins)
            BridgeLang.UseStored(from.Language);
        }

        // Arguments, then the package file given with --from, then the environment and a bridge.json next to the binary
        if (!BridgeSettings.TryResolve(cli.Server, cli.Token, getEnvironmentVariable, from ?? portable, user, out var settings, out var error, linux: true))
        {
            BridgeLog.Error(error!);
            return LinuxApp.ExitUsage;
        }

        var steamDir = cli.SteamDir ?? from?.SteamDir ?? portable?.SteamDir ?? user?.SteamDir;
        if (steamDir is not null)
        {
            var provider = new SteamLocationProvider(new SteamLocatorInput { ManualRoot = steamDir, Environment = getEnvironmentVariable });
            var check = provider.Check(steamDir);
            if (!check.IsValid)
            {
                var problem = check.Problem switch
                {
                    SteamRootProblem.NotAbsolute => LinuxTexts.Setup.SteamDirNotAbsolute,
                    SteamRootProblem.NotFound => LinuxTexts.Setup.SteamDirNotFound,
                    SteamRootProblem.LibraryOnly => LinuxTexts.Setup.SteamDirLibraryOnly,
                    _ => LinuxTexts.Setup.SteamDirNoSteamApps,
                };
                BridgeLog.Error(LinuxTexts.Setup.SteamDirInvalid(steamDir, problem));
                return LinuxApp.ExitUsage;
            }
            steamDir = check.Path;
        }

        var target = LinuxApp.SaveTargetPath(env);
        try
        {
            BridgeConfigFile.Write(target, new StoredBridgeConfig
            {
                Server = settings!.ServerText,
                Token = settings.Token,
                Language = BridgeLang.Remembered,
                SteamDir = steamDir,
            });
            BridgeLog.Print(LinuxTexts.Setup.Saved(target));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BridgeLog.Error(LinuxTexts.Setup.SaveFailed(target, ex.Message));
            return LinuxApp.ExitFailed;
        }

        var saved = new StoredBridgeConfig { SteamDir = steamDir };
        var steam = LinuxApp.CreateSteam(cli, env, saved, null, getEnvironmentVariable);
        if (steam is SteamLocalSource source)
        {
            BridgeLog.Print(source.Reader.Location.Root is { } root
                ? LinuxTexts.Status.SteamFolder(root, LinuxApp.DescribeMethod(source.Reader.Location.Method))
                : LinuxTexts.Status.SteamMissing);
        }

        // A test run never talks to a server on its own account; the tests that want the network call the run command
        if (cli.NoCheck || env.IsTest) return LinuxApp.ExitOk;

        BridgeLog.Print(LinuxTexts.Setup.Checking);
        using var client = new BridgeClient(settings, version, platform: "linux");
        var sampler = new StateSampler(steam, version, cli.TestAppId, platform: "linux");
        var (payload, _) = await sampler.BuildAsync(sampler.Observe(), 0, new HashSet<string>(), ct).ConfigureAwait(false);
        var result = await client.SendAsync(payload, ct).ConfigureAwait(false);

        switch (result.Outcome)
        {
            case SendOutcome.Ok:
                BridgeLog.Print(payload.SteamId64.Length > 0
                    ? LinuxTexts.Setup.CheckOk(settings.ServerText, payload.SteamId64)
                    : LinuxTexts.Setup.CheckOkNoAccount(settings.ServerText));
                break;
            case SendOutcome.Unauthorized:
                BridgeLog.Error(LinuxTexts.Setup.CheckTokenRejected);
                return LinuxApp.ExitFailed;
            default:
                BridgeLog.Warn(LinuxTexts.Setup.CheckFailed(result.Reason ?? "?"));
                break;
        }

        BridgeLog.Print(LinuxTexts.Setup.ServiceHint);
        return LinuxApp.ExitOk;
    }
}
