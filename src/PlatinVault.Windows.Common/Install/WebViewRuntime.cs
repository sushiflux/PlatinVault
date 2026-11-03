using System.Diagnostics;
using Microsoft.Win32;

namespace PlatinVault.Windows.Install;

/// <summary>
/// The Microsoft Edge WebView2 Runtime that shows PlatinVault's window. Detection exactly as Microsoft documents it ("Detect if a
/// WebView2 Runtime is already installed": the pv value under EdgeUpdate\Clients\{F3017226-...} in HKLM (per machine, WOW6432Node on
/// 64-bit Windows) or HKCU (per user), a version greater than 0.0.0.0). Windows 11 ships it; a few Windows 10 PCs lack it - then the
/// installer downloads Microsoft's Evergreen Bootstrapper and runs it with "/silent /install" (per user without administrator rights).
/// Source: https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution
/// </summary>
public static class WebViewRuntime
{
    private const string ClientKey = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    /// <summary>Microsoft's permanent link to the Evergreen Bootstrapper (MicrosoftEdgeWebview2Setup.exe), from the download page's "Get the Link".</summary>
    public const string BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    public static string? InstalledVersion()
    {
        foreach (var (hive, path) in new[]
                 {
                     (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\" + ClientKey),
                     (RegistryHive.LocalMachine, @"SOFTWARE\" + ClientKey),
                     (RegistryHive.CurrentUser, @"Software\" + ClientKey),
                 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = root.OpenSubKey(path);
                if (key?.GetValue("pv") is string version && IsRealVersion(version)) return version;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Not readable: try the next location
            }
        }
        return null;
    }

    public static bool IsRealVersion(string? version) =>
        !string.IsNullOrWhiteSpace(version) && Version.TryParse(version, out var parsed) && parsed > new Version(0, 0, 0, 0);

    /// <summary>Downloads the bootstrapper into <paramref name="folder"/> and runs it silently. Returns the installed version or null.</summary>
    public static async Task<string?> InstallAsync(HttpClient http, string folder, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var setup = Path.Combine(folder, "MicrosoftEdgeWebview2Setup.exe");
        await using (var response = await http.GetStreamAsync(BootstrapperUrl, ct))
        await using (var file = File.Create(setup))
        {
            await response.CopyToAsync(file, ct);
        }

        using var process = Process.Start(new ProcessStartInfo(setup, "/silent /install") { UseShellExecute = false, CreateNoWindow = true })
                            ?? throw new InvalidOperationException("The WebView2 bootstrapper did not start.");
        await process.WaitForExitAsync(ct);
        try { File.Delete(setup); } catch (IOException) { /* still locked: the temp folder is cleaned up later */ }
        return InstalledVersion();
    }
}
