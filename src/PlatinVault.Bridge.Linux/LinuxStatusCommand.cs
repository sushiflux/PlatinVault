using System.Diagnostics;
using PlatinVault.SteamLocal;

namespace PlatinVault.Bridge;

/// <summary>
/// <c>platinvault-bridge status</c>: what the bridge and its service are doing. The service writes <c>status.json</c> (connection, current
/// game, last achievement); this command adds what it can see itself - whether a Steam folder is found and how, and the state of the
/// systemd user unit. Read only, never talks to the server.
/// </summary>
internal static class LinuxStatusCommand
{
    // The service rewrites the file every 30 seconds; two minutes without a write means it is not running (or hangs)
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    public static int Run(LinuxCliOptions cli, LinuxEnvironment env, StoredBridgeConfig? portable, StoredBridgeConfig? user, string version,
        Func<string, string?> getEnvironmentVariable)
    {
        BridgeLog.Print(LinuxTexts.Status.Title(version));

        var hasConfig = portable is not null || user is not null;
        BridgeLog.Print(LinuxTexts.Status.Config(LinuxApp.SaveTargetPath(env), hasConfig));
        if (BridgeSettings.TryResolve(cli.Server, cli.Token, getEnvironmentVariable, portable, user, out var settings, out _, linux: true))
            BridgeLog.Print(LinuxTexts.Status.Server(settings!.ServerText, settings.TokenHint));

        if (!env.IsTest) BridgeLog.Print(LinuxTexts.Status.Service(ServiceState()));

        var file = LinuxStatusFile.Load(env.StatusPath);
        var updated = file?.UpdatedAtUtc;
        if (file is not null && updated is { } at && DateTime.UtcNow - at < MaxAge && IsAlive(file.Pid))
        {
            BridgeLog.Print(LinuxTexts.Status.Running(file.Pid, (int)Math.Max(0, (DateTime.UtcNow - at).TotalSeconds)));
            BridgeLog.Print(LinuxTexts.Status.Connection(ConnectionText(file.Connection), file.Connection == "online" ? null : file.Detail));
            if (!string.IsNullOrEmpty(file.SteamId64)) BridgeLog.Print(LinuxTexts.Status.SteamAccount(file.SteamId64));
            BridgeLog.Print(file.AppId == 0
                ? LinuxTexts.Status.NoGame
                : LinuxTexts.Status.Game(string.IsNullOrWhiteSpace(file.GameName) ? $"AppID {file.AppId}" : file.GameName, file.Achieved, file.Total));
            if (file.LastUnlockName is { Length: > 0 } name && DateTime.TryParse(file.LastUnlockAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var unlockedAt))
                BridgeLog.Print(LinuxTexts.Status.LastUnlock(name, unlockedAt.ToLocalTime().ToString(BridgeLang.DateTimeFormat, System.Globalization.CultureInfo.InvariantCulture)));
        }
        else
        {
            BridgeLog.Print(LinuxTexts.Status.NotRunning);
        }

        PrintSteam(CreateLocation(cli, portable, user, getEnvironmentVariable));
        BridgeLog.Print(LinuxTexts.Status.Log(env.LogPath));
        return LinuxApp.ExitOk;
    }

    private static SteamLocationProvider CreateLocation(LinuxCliOptions cli, StoredBridgeConfig? portable, StoredBridgeConfig? user,
        Func<string, string?> getEnvironmentVariable) =>
        new(new SteamLocatorInput { ManualRoot = cli.SteamDir ?? portable?.SteamDir ?? user?.SteamDir, Environment = getEnvironmentVariable });

    private static void PrintSteam(SteamLocationProvider provider)
    {
        var location = provider.Current;
        if (location.Root is { } root)
        {
            BridgeLog.Print(LinuxTexts.Status.SteamFolder(root, LinuxApp.DescribeMethod(location.Method)));
            return;
        }

        BridgeLog.Print(LinuxTexts.Status.SteamMissing);
        foreach (var candidate in location.Candidates)
        {
            var detail = !candidate.Exists ? LinuxTexts.Status.CandidateMissing
                : !candidate.HasSteamApps ? LinuxTexts.Status.CandidateNoSteamApps
                : !candidate.IsSteamRoot ? LinuxTexts.Status.CandidateLibraryOnly
                : candidate.IsDuplicate ? LinuxTexts.Status.CandidateDuplicate
                : LinuxTexts.Status.CandidateUsable;
            BridgeLog.Print(LinuxTexts.Status.Candidate(candidate.Path, detail));
        }
    }

    private static Msg ConnectionText(string connection) => connection switch
    {
        "online" => LinuxTexts.Status.ConnOnline,
        "offline" => LinuxTexts.Status.ConnOffline,
        "unauthorized" => LinuxTexts.Status.ConnUnauthorized,
        "rejected" => LinuxTexts.Status.ConnRejected,
        _ => LinuxTexts.Status.ConnStarting,
    };

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>"active", "inactive", "failed", ... from <c>systemctl --user is-active</c>; "unknown" when systemctl is not there or does not answer.</summary>
    private static string ServiceState()
    {
        try
        {
            var info = new ProcessStartInfo("systemctl") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("--user");
            info.ArgumentList.Add("is-active");
            info.ArgumentList.Add(LinuxEnvironment.UnitName);
            using var process = Process.Start(info);
            if (process is null) return LinuxTexts.Status.ServiceUnknown;
            if (!process.WaitForExit(TimeSpan.FromSeconds(3))) { process.Kill(); return LinuxTexts.Status.ServiceUnknown; }
            var text = process.StandardOutput.ReadToEnd().Trim();
            return text.Length == 0 ? LinuxTexts.Status.ServiceUnknown : text;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return LinuxTexts.Status.ServiceUnknown;
        }
    }
}
