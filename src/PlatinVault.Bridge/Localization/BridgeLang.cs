using System.Runtime.InteropServices;

namespace PlatinVault.Bridge;

/// <summary>Where the current language came from; a later source in the order Windows &lt; Stored &lt; Server &lt; Pinned replaces an earlier one.</summary>
internal enum LanguageSource
{
    /// <summary>The Windows display language - the default until a better source is known.</summary>
    Windows,

    /// <summary>The <c>language</c> value of <c>bridge.json</c> (written by the server's ZIP, the setup window or an earlier run).</summary>
    Stored,

    /// <summary>The <c>ui.language</c> of the PlatinVault server, reported with every state push.</summary>
    Server,

    /// <summary>Forced with <c>--language</c>; nothing else changes it for this run.</summary>
    Pinned,
}

/// <summary>
/// The language the bridge speaks right now ("de" / "en"), one process-wide value. Order of precedence: <c>--language</c>, the
/// language of the PlatinVault server (learned from the answer of every state push, so a change in PlatinVault is followed
/// live), the language stored in <c>bridge.json</c>, the Windows display language. Anything that is not German is English, the
/// same fallback as the PlatinVault app. <see cref="Msg"/> resolves its text from <see cref="Current"/> at the moment it is read.
/// </summary>
internal static class BridgeLang
{
    public const string German = "de";
    public const string English = "en";

    /// <summary>Languages the bridge speaks. Same codes as <c>languages.json</c> in the repository (the app's language list).</summary>
    public static readonly string[] Supported = [German, English];

    // LANG_GERMAN, the primary language id inside a Windows LANGID (the low 10 bits)
    private const int PrimaryLanguageGerman = 0x07;

    private static readonly object Gate = new();
    private static string _current = FromWindows();
    private static LanguageSource _source = LanguageSource.Windows;

    /// <summary>Raised (on whichever thread caused the change) after <see cref="Current"/> changed. UI code marshals to its own thread.</summary>
    public static event Action<string>? Changed;

    public static string Current
    {
        get { lock (Gate) return _current; }
    }

    public static LanguageSource Source
    {
        get { lock (Gate) return _source; }
    }

    public static bool IsEnglish => Current == English;

    /// <summary>The language to write into <c>bridge.json</c>: null while it is only the Windows default, so the file never freezes a guess.</summary>
    public static string? Remembered
    {
        get
        {
            lock (Gate) return _source == LanguageSource.Windows ? null : _current;
        }
    }

    /// <summary>"de", "de-DE", " DE " -> "de"; "en-GB" -> "en"; anything else (including null) -> null.</summary>
    public static string? Normalize(string? value)
    {
        var primary = value?.Trim().Split('-', '_')[0].ToLowerInvariant();
        return primary is German or English ? primary : null;
    }

    /// <summary>
    /// "de" for a German Windows display language, "en" for every other one. Asks Windows directly: the exe is built with
    /// InvariantGlobalization (smaller, no ICU), under which <c>CultureInfo.CurrentUICulture</c> is always the invariant
    /// culture and says nothing about the user's language.
    /// </summary>
    public static string FromWindows()
    {
        try
        {
            var languageId = GetUserDefaultUILanguage();
            return (languageId & 0x3FF) == PrimaryLanguageGerman ? German : English;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return Normalize(System.Globalization.CultureInfo.CurrentUICulture.Name) ?? English;
        }
    }

    /// <summary><c>--language</c>: this run speaks this language, whatever the server or the stored value says.</summary>
    public static void Pin(string language) => Set(language, LanguageSource.Pinned);

    /// <summary>The language stored in <c>bridge.json</c>; ignored when none is stored or the run is pinned.</summary>
    public static void UseStored(string? language)
    {
        if (Normalize(language) is { } normalized && Source != LanguageSource.Pinned && Source != LanguageSource.Server)
            Set(normalized, LanguageSource.Stored);
    }

    /// <summary>
    /// The language the PlatinVault server reported. Returns true when the visible language changed. Ignored (false) for an
    /// unknown value - an older server reports none - and while the run is pinned.
    /// </summary>
    public static bool UseServer(string? language)
    {
        var normalized = Normalize(language);
        if (normalized is null || Source == LanguageSource.Pinned) return false;
        return Set(normalized, LanguageSource.Server);
    }

    private static bool Set(string language, LanguageSource source)
    {
        bool changed;
        lock (Gate)
        {
            changed = _current != language;
            _current = language;
            _source = source;
        }

        if (changed) Changed?.Invoke(language);
        return changed;
    }

    /// <summary>"dd.MM.yyyy HH:mm" for German, "dd/MM/yyyy HH:mm" for English (day first, 24 hours - the same as the app's en-GB).</summary>
    public static string DateTimeFormat => IsEnglish ? "dd/MM/yyyy HH:mm" : "dd.MM.yyyy HH:mm";

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();
}

/// <summary>
/// Follows the server's language while the bridge runs: switches <see cref="BridgeLang"/> when the answer of a state push names
/// another language and remembers it in <c>bridge.json</c> (once per change, so the file is not rewritten on every heartbeat),
/// so the next start - before the first connection - already speaks the right language.
/// </summary>
internal sealed class LanguageFollower(string? storedLanguage, Action<string> remember)
{
    private string? _remembered = BridgeLang.Normalize(storedLanguage);

    public void Observe(string? serverLanguage)
    {
        var language = BridgeLang.Normalize(serverLanguage);
        if (language is null || BridgeLang.Source == LanguageSource.Pinned) return;

        if (BridgeLang.UseServer(language))
            BridgeLog.Info(BridgeTexts.Language.AdoptedFromServer(language));

        if (_remembered == language) return;

        // Remember before writing: a read-only config must not make every heartbeat retry the write
        _remembered = language;
        try
        {
            remember(language);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BridgeLog.Debug(BridgeTexts.Language.NotRemembered(ex.Message));
        }
    }
}
