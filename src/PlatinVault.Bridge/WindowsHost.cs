using System.Runtime.InteropServices;

namespace PlatinVault.Bridge;

/// <summary>Where the Windows bridge keeps its configuration, and the Windows display language (the shared code in Core knows neither).</summary>
internal static class WindowsHost
{
    // LANG_GERMAN, the primary language id inside a Windows LANGID (the low 10 bits)
    private const int PrimaryLanguageGerman = 0x07;

    /// <summary>Directory of the running exe (the portable config, the update files and the package live there).</summary>
    public static string ExeDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    /// <summary>Portable config: the server-generated <c>bridge.json</c> shipped in the zip next to the exe.</summary>
    public static string PortablePath => Path.Combine(ExeDirectory, "bridge.json");

    /// <summary>
    /// Where <c>--save</c> writes: an existing portable config next to the exe is updated in place (it beats the per-user
    /// file, so saving elsewhere would silently have no effect), otherwise the per-user file.
    /// </summary>
    public static string SaveTargetPath(BridgeEnvironment env) => File.Exists(PortablePath) ? PortablePath : env.UserConfigPath;

    /// <summary>Stores the language in the config the bridge reads (see <see cref="BridgeConfigFile.RememberLanguage"/>).</summary>
    public static void RememberLanguage(BridgeEnvironment env, string language) =>
        BridgeConfigFile.RememberLanguage(SaveTargetPath(env), env.UserConfigPath, language);

    /// <summary>
    /// "de" for a German Windows display language, "en" for every other one. Asks Windows directly: the exe is built with
    /// InvariantGlobalization (smaller, no ICU), under which <c>CultureInfo.CurrentUICulture</c> is always the invariant
    /// culture and says nothing about the user's language.
    /// </summary>
    public static string DisplayLanguage()
    {
        try
        {
            var languageId = GetUserDefaultUILanguage();
            return (languageId & 0x3FF) == PrimaryLanguageGerman ? BridgeLang.German : BridgeLang.English;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return BridgeLang.Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name) ?? BridgeLang.English;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();
}
