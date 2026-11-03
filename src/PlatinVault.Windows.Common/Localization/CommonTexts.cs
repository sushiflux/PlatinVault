namespace PlatinVault.Windows.Localization;

/// <summary>Texts used by every window of the Windows program and its installer.</summary>
public static class CommonTexts
{
    public static string Copyright => $"© {DateTime.Now.Year} SushiFlux";

    /// <summary>"Mit ♥ gemacht" / "Made with ♥": the parts before and after the (green) heart.</summary>
    public static (string Before, string After) MadeWithParts =>
        UiLang.Current == UiLang.English ? ("Made with ", string.Empty) : ("Mit ", " gemacht");

    public static readonly Msg Ok = new("OK", "OK");
    public static readonly Msg Cancel = new("Abbrechen", "Cancel");
    public static readonly Msg Close = new("Schließen", "Close");
    public static readonly Msg Yes = new("Ja", "Yes");
    public static readonly Msg No = new("Nein", "No");
    public static readonly Msg Back = new("Zurück", "Back");
    public static readonly Msg Next = new("Weiter", "Next");
    public static readonly Msg Retry = new("Erneut versuchen", "Try again");
    public static readonly Msg CopyDetails = new("Text kopieren", "Copy text");

    public static readonly Msg DialogInfo = new("Hinweis", "Note");
    public static readonly Msg DialogQuestion = new("Frage", "Question");
    public static readonly Msg DialogWarning = new("Achtung", "Attention");
    public static readonly Msg DialogError = new("Fehler", "Error");

    // Window commands of the title bar (names for screen readers; the same words Windows uses for its own caption buttons)
    public static readonly Msg CaptionMinimize = new("Minimieren", "Minimize");
    public static readonly Msg CaptionMaximize = new("Maximieren", "Maximize");
    public static readonly Msg CaptionRestore = new("Verkleinern", "Restore Down");
    public static readonly Msg Press = new("Drücken", "Press");
}
