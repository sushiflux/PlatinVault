using System.Diagnostics;
using System.Globalization;
using Companion.Api.Desktop;
using Microsoft.Win32;
using PlatinVault.Versioning;

namespace PlatinVault.Desktop.Server;

/// <summary>
/// The server's view of the Windows program: starting a game is done here, on the PC that runs Steam, by opening
/// <c>steam://run/&lt;AppID&gt;</c> through Windows - no browser in between, so no "open Steam?" question. The address is always
/// built from a validated number. In a test run (<c>--test-launch</c>) the address is only recorded: a test never starts a game.
/// Updates started from the settings (Settings > System > Updates) are handed to <paramref name="installUpdate"/> - the program's own
/// download/verify/run flow; without it (development and test runs) this host installs nothing.
/// </summary>
internal sealed class LocalDesktopHost(bool recordOnly, string? recordFile,
    Func<PublishedRelease, CancellationToken, Task<DesktopUpdateStart>>? installUpdate = null) : IDesktopHost
{
    public bool CanInstallUpdates => installUpdate is not null;

    public Task<DesktopUpdateStart> InstallUpdateAsync(PublishedRelease release, CancellationToken ct) =>
        installUpdate?.Invoke(release, ct) ?? Task.FromResult(new DesktopUpdateStart(DesktopUpdateOutcome.NotSupported, null));

    public LocalLaunchResult LaunchGame(int appId)
    {
        if (appId <= 0) return new LocalLaunchResult(false, false, "invalid AppID");
        var url = string.Create(CultureInfo.InvariantCulture, $"steam://run/{appId}");

        if (recordOnly)
        {
            DesktopLog.Info($"Testlauf: Spielstart nur aufgezeichnet ({url}).");
            if (recordFile is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(recordFile)!);
                File.AppendAllText(recordFile, url + Environment.NewLine);
            }
            return LocalLaunchResult.Ok();
        }

        if (!IsSteamRegistered()) return new LocalLaunchResult(false, true, null);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            DesktopLog.Info($"Spiel gestartet: {url}");
            return LocalLaunchResult.Ok();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new LocalLaunchResult(false, false, ex.Message);
        }
    }

    /// <summary>Steam registers the steam:// protocol for the user (HKCU\Software\Classes\steam) or the machine (HKCR\steam).</summary>
    private static bool IsSteamRegistered()
    {
        try
        {
            using var user = Registry.CurrentUser.OpenSubKey(@"Software\Classes\steam\Shell\Open\Command");
            if (user?.GetValue(null) is string { Length: > 0 }) return true;
            using var machine = Registry.ClassesRoot.OpenSubKey(@"steam\Shell\Open\Command");
            return machine?.GetValue(null) is string { Length: > 0 };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
