using System.Diagnostics;

namespace PlatinVault.Bridge;

/// <summary>
/// The PlatinVault end-user handbook as the bridge hands it to the user. The download ZIP carries the PDFs next to the exe
/// (the server writes them under exactly these names, see HandbookFiles in the server project); the installer copies them
/// into the install folder; the tray menu and the setup window open the one in the language the bridge speaks right now
/// (<see cref="BridgeLang.Current"/>: the language of the PlatinVault server) - or, when no PDF is there (an older package),
/// the server's own copy at <c>/api/docs/handbook</c> in the browser.
/// </summary>
internal static class Handbook
{
    public const string GermanFileName = "Handbuch (Deutsch).pdf";
    public const string EnglishFileName = "Handbook (English).pdf";

    /// <summary>
    /// The licence documents the ZIP carries next to the exe (the GNU AGPL v3 text and the third-party notices, see LicenseFiles in the
    /// server project). The exe is self-contained, so the open-source licences of the bundled runtime require these files in every copy.
    /// </summary>
    public static readonly string[] LicenseFileNames = ["LICENSE", "THIRD-PARTY-NOTICES.md"];

    private static readonly string[] FileNames = [GermanFileName, EnglishFileName, .. LicenseFileNames];

    /// <summary>File name of the PDF for a language ("de" -> German, anything else -> English).</summary>
    public static string FileNameFor(string language) => language == BridgeLang.German ? GermanFileName : EnglishFileName;

    /// <summary>The PDF in this language next to the running exe (unpacked ZIP, or the installed copy), else in the install folder; null when there is none.</summary>
    public static string? FindLocalPdf(BridgeEnvironment env, string language)
    {
        foreach (var directory in new[] { BridgeEnvironment.ExeDirectory, env.InstallRoot })
        {
            var path = Path.Combine(directory, FileNameFor(language));
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>Address of the handbook on the server (served inline as PDF) in the current language.</summary>
    public static string WebUrl(string serverUrl) => serverUrl.TrimEnd('/') + "/api/docs/handbook?lang=" + BridgeLang.Current;

    /// <summary>
    /// What "Open handbook" opens: the local PDF in the current language, else the server's copy in that language, else (a package
    /// that lost a file, no server known) the PDF in the other language - better than none; null when nothing is available.
    /// </summary>
    public static string? FindTarget(BridgeEnvironment env, string? serverUrl)
    {
        var current = BridgeLang.Current;
        if (FindLocalPdf(env, current) is { } local) return local;
        if (!string.IsNullOrWhiteSpace(serverUrl)) return WebUrl(serverUrl);
        return FindLocalPdf(env, current == BridgeLang.German ? BridgeLang.English : BridgeLang.German);
    }

    /// <summary>Shell-executes the target: the PDF opens in the user's PDF viewer, an address in the default browser.</summary>
    public static void Open(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>
    /// Copies the handbook PDFs and the licence documents from the folder of the running exe (the unpacked ZIP) next to the
    /// installed exe, so the tray menu finds the handbook there and every installed copy carries its licences. Best effort: a missing
    /// file must never fail an installation.
    /// </summary>
    public static void CopyToInstallFolder(string sourceDirectory, string installDirectory)
    {
        if (BridgeEnvironment.SamePath(sourceDirectory, installDirectory)) return;

        foreach (var name in FileNames)
        {
            var source = Path.Combine(sourceDirectory, name);
            if (!File.Exists(source)) continue;

            try
            {
                File.Copy(source, Path.Combine(installDirectory, name), overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                BridgeLog.Warn(BridgeTexts.Handbook.CopyFailed(name, ex.Message));
            }
        }
    }
}
