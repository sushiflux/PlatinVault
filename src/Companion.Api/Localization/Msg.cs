namespace Companion.Api.Localization;

/// <summary>
/// A user-facing text in both app languages. The language is chosen when the text is READ (<see cref="ToString"/>, the implicit
/// string conversion), not when it is created - so a message that was cached or thrown in one request is still shown in the
/// language of the request that finally reads it. All server texts live in <see cref="ServerTexts"/>; this is the type they have.
/// Plain C# (two strings, type-checked arguments) instead of resource files: the catalogue is small, both languages sit next to
/// each other so a missing translation is visible in review, and parameters are checked by the compiler.
/// </summary>
public readonly record struct Msg(string De, string En)
{
    /// <summary>The same text in both languages (names, technical identifiers).</summary>
    public static Msg Same(string text) => new(text, text);

    public string Resolve(string language) => language == Lang.English ? En : De;

    /// <summary>The text in the current language (see <see cref="Lang.Current"/>).</summary>
    public override string ToString() => Resolve(Lang.Current);

    public static implicit operator string(Msg message) => message.ToString();

    /// <summary>Appends text to both languages (e.g. a provider's own message, which is not translated).</summary>
    public Msg Append(string text) => new(De + text, En + text);

    /// <summary>Joins two messages with a separator, language by language.</summary>
    public static Msg Join(string separator, Msg first, Msg second) => new(first.De + separator + second.De, first.En + separator + second.En);
}

/// <summary>
/// An <see cref="InvalidOperationException"/> whose message is a <see cref="Msg"/>: <see cref="Message"/> is resolved when it is
/// read, in the language of the reader. Services throw these; endpoints put <c>ex.Message</c> into the problem detail.
/// </summary>
public sealed class LocalizedException(Msg text, Exception? inner = null) : InvalidOperationException(text.De, inner)
{
    public Msg Text { get; } = text;

    public override string Message => Text.ToString();
}
