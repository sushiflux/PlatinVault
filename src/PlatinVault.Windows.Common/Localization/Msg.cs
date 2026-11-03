using System.Runtime.InteropServices;

namespace PlatinVault.Windows.Localization;

/// <summary>
/// A user-facing text in both languages of the Windows program and its installer. The language is picked when the text is READ
/// (<see cref="ToString"/>), so a window can switch language live. Same idea as <c>Msg</c> in the server and the bridge: two
/// strings side by side, a missing translation is visible in review and caught by <see cref="TextCatalog"/>.
/// </summary>
public readonly record struct Msg(string De, string En)
{
    public string Resolve(string language) => language == UiLang.English ? En : De;

    public override string ToString() => Resolve(UiLang.Current);

    public static implicit operator string(Msg message) => message.ToString();
}

/// <summary>The current language of the Windows UI ("de" or "en"). Changing it raises <see cref="Changed"/>.</summary>
public static class UiLang
{
    public const string German = "de";
    public const string English = "en";

    private static string _current = FromWindows();

    public static string Current
    {
        get => _current;
        set
        {
            var normalized = Normalize(value) ?? German;
            if (normalized == _current) return;
            _current = normalized;
            Changed?.Invoke();
        }
    }

    public static event Action? Changed;

    /// <summary>"de"/"en" for anything that starts with them, otherwise null.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim().ToLowerInvariant();
        if (v.StartsWith(German, StringComparison.Ordinal)) return German;
        if (v.StartsWith(English, StringComparison.Ordinal)) return English;
        return null;
    }

    /// <summary>German for a German Windows display language, English for every other one (the app's rule for the first start).</summary>
    public static string FromWindows()
    {
        try
        {
            // The user's display language, not CultureInfo (which may be invariant in a trimmed or test process)
            var lcid = GetUserDefaultUILanguage();
            return (lcid & 0x3FF) == 0x07 ? German : English;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return German;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();
}
