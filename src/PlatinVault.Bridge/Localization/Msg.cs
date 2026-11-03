namespace PlatinVault.Bridge;

/// <summary>
/// A user-facing text in both app languages. The language is chosen when the text is READ (<see cref="ToString"/>, the implicit
/// string conversion), not when it is created - so a stored message (a window's status line) can be shown again in the new
/// language after the bridge switched to the language of the PlatinVault server. Same idea and shape as <c>Msg</c> in the server
/// project; plain C# (two strings, compiler-checked arguments) instead of resource files: the catalogue is small, both languages
/// sit next to each other so a missing translation is visible in review. All texts live in <see cref="BridgeTexts"/>.
/// </summary>
internal readonly record struct Msg(string De, string En, bool Shared = false)
{
    /// <summary>The same text in both languages on purpose (names, technical identifiers); <c>--test-texts</c> flags every other identical pair as a forgotten translation.</summary>
    public static Msg Same(string text) => new(text, text, Shared: true);

    public string Resolve(string language) => language == BridgeLang.English ? En : De;

    /// <summary>The text in the current language (see <see cref="BridgeLang.Current"/>).</summary>
    public override string ToString() => Resolve(BridgeLang.Current);

    public static implicit operator string(Msg message) => message.ToString();

    /// <summary>Appends text to both languages (e.g. the system's own error message, which is not translated).</summary>
    public Msg Append(string text) => this with { De = De + text, En = En + text };
}
