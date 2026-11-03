namespace PlatinVault.Bridge;

/// <summary>Where the current language came from; a later source in the order System &lt; Stored &lt; Server &lt; Pinned replaces an earlier one.</summary>
internal enum LanguageSource
{
    /// <summary>The display language of the operating system (Windows UI language, Linux locale) - the default until a better source is known.</summary>
    System,

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
/// live), the language stored in <c>bridge.json</c>, the language of the operating system (the host reads it and calls <see cref="UseSystem"/>). Anything that is not German is English, the
/// same fallback as the PlatinVault app. <see cref="Msg"/> resolves its text from <see cref="Current"/> at the moment it is read.
/// </summary>
internal static class BridgeLang
{
    public const string German = "de";
    public const string English = "en";

    /// <summary>Languages the bridge speaks. Same codes as <c>languages.json</c> in the repository (the app's language list).</summary>
    public static readonly string[] Supported = [German, English];

    private static readonly object Gate = new();
    private static string _current = English;
    private static LanguageSource _source = LanguageSource.System;

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

    /// <summary>The language to write into <c>bridge.json</c>: null while it is only the system default, so the file never freezes a guess.</summary>
    public static string? Remembered
    {
        get
        {
            lock (Gate) return _source == LanguageSource.System ? null : _current;
        }
    }

    /// <summary>"de", "de-DE", " DE " -> "de"; "en-GB" -> "en"; anything else (including null) -> null.</summary>
    public static string? Normalize(string? value)
    {
        var primary = value?.Trim().Split('-', '_')[0].ToLowerInvariant();
        return primary is German or English ? primary : null;
    }

    /// <summary>
    /// The language of the operating system as the host read it (Windows: the UI language, Linux: the locale variables); anything
    /// that is not German is English. Only applies while no better source (stored, server, pinned) has spoken.
    /// </summary>
    public static void UseSystem(string? language)
    {
        if (Source != LanguageSource.System) return;
        Set(Normalize(language) ?? English, LanguageSource.System);
    }

    /// <summary>Back to the initial state (English from the system). For tests, which run several bridges in one process.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            _current = English;
            _source = LanguageSource.System;
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
            BridgeLog.Info(CoreTexts.Language.AdoptedFromServer(language));

        if (_remembered == language) return;

        // Remember before writing: a read-only config must not make every heartbeat retry the write
        _remembered = language;
        try
        {
            remember(language);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BridgeLog.Debug(CoreTexts.Language.NotRemembered(ex.Message));
        }
    }
}
